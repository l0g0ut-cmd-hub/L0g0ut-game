using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Покадровый спрайт эффекта (аддитивный, из листа cols×rows в Resources/FX): всегда к камере; при loop — крутится
    /// по кругу (шар духа), иначе проигрывается один раз и исчезает (взрыв). dir — куда «смотрит» кадр (вправо в листе):
    /// плоскость поворачивается так, чтобы хвост пламени тянулся за полётом. follow — привязка к летящему объекту.
    /// </summary>
    public class FxSprite : MonoBehaviour
    {
        public int cols = 3, rows = 2, frames = 6;
        public float fps = 14f, life = 0.4f, size = 0.7f, grow = 0f;
        public bool loop;
        public Transform follow;
        public System.Func<Vector3> dir;
        public Color tint = Color.white;
        public bool unscaled;      // время без замедления (эффекты в замедленной концовке раунда не тянутся)
        public Vector3 vel;        // дрейф, если не привязан к объекту
        public bool flipX;         // зеркально (удар влево по экрану)
        float Now { get { return unscaled ? Time.unscaledTime : Time.time; } }
        Mesh[] meshes; MeshFilter mf; MeshRenderer mr; float born; MaterialPropertyBlock mpb;
        Camera cachedCamera;
        float nextCameraLookup;
        // One immutable quad per atlas frame, shared by every instance of that layout.
        static readonly System.Collections.Generic.Dictionary<string, Mesh[]> meshCache = new System.Collections.Generic.Dictionary<string, Mesh[]>();
        static readonly System.Collections.Generic.Dictionary<string, Material> mats = new System.Collections.Generic.Dictionary<string, Material>();
        static readonly int TintId = Shader.PropertyToID("_TintColor");

        /// <summary>То же, но с обычным смешением по альфе (тёмный дым, пыль, камни — то, что не светится).</summary>
        public static FxSprite SpawnAlpha(string tex, Vector3 pos, float size, int cols, int rows, int frames, float fps, bool loop, float life = 0f)
        { return Spawn(tex, pos, size, cols, rows, frames, fps, loop, life, true); }

        public static FxSprite Spawn(string tex, Vector3 pos, float size, int cols, int rows, int frames, float fps, bool loop, float life = 0f, bool alpha = false)
        {
            Material m = Mat(tex, alpha);
            if (m == null) return null;
            GameObject g = new GameObject("Fx_" + tex);
            g.transform.position = pos;
            FxSprite f = g.AddComponent<FxSprite>();
            f.cols = Mathf.Max(1, cols); f.rows = Mathf.Max(1, rows);
            f.frames = Mathf.Clamp(frames, 1, f.cols * f.rows);
            f.fps = Mathf.Max(1f, fps); f.loop = loop; f.size = size;
            f.life = life > 0f ? life : frames / Mathf.Max(1f, fps);
            f.mf = g.AddComponent<MeshFilter>(); f.mr = g.AddComponent<MeshRenderer>(); f.mr.sharedMaterial = m;
            f.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; f.mr.receiveShadows = false;
            f.Build();
            return f;
        }

        static Material Mat(string tex, bool alpha)
        {
            Material m;
            string id = alpha ? "a:" + tex : tex;
            if (mats.TryGetValue(id, out m) && m != null) return m;
            Texture2D t = Resources.Load<Texture2D>("FX/" + tex);
            if (t == null) t = EmbeddedFxArt.Get(tex);
            Shader sh = Shader.Find(alpha ? "Legacy Shaders/Particles/Alpha Blended" : "Legacy Shaders/Particles/Additive");
            if (t == null || sh == null) return null;
            m = new Material(sh) { name = "Fx_" + tex, mainTexture = t };
            m.SetColor(TintId, new Color(0.5f, 0.5f, 0.5f, 0.5f));
            mats[id] = m;
            return m;
        }

        void Build()
        {
            string key = cols + "x" + rows + ":" + frames;
            if (!meshCache.TryGetValue(key, out meshes))
            {
                meshes = new Mesh[frames];
                float du = 1f / cols, dv = 1f / rows;
                for (int i = 0; i < frames; i++)
                {
                    float u0 = (i % cols) * du, v0 = 1f - (i / cols + 1) * dv;
                    Mesh m = new Mesh { name = "FxFrame_" + key + "_" + i };
                    m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                    m.uv = new[] { new Vector2(u0, v0), new Vector2(u0 + du, v0), new Vector2(u0 + du, v0 + dv), new Vector2(u0, v0 + dv) };
                    m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                    m.RecalculateBounds();
                    meshes[i] = m;
                }
                meshCache[key] = meshes;
            }
            mpb = new MaterialPropertyBlock();
            born = Now;
            Pose();
        }

        void LateUpdate()
        {
            if (meshes == null) return;
            if (loop && follow == null) { Destroy(gameObject); return; }   // шар погас — и пламя с ним
            if (!loop && Now - born >= life) { Destroy(gameObject); return; }
            if (follow == null && vel != Vector3.zero) transform.position += vel * (unscaled ? Time.unscaledDeltaTime : Time.deltaTime);
            Pose();
        }

        void Pose()
        {
            float age = Now - born;
            int fr = loop ? (int)(age * fps) % frames : Mathf.Clamp((int)(age / Mathf.Max(0.01f, life) * frames), 0, frames - 1);
            mf.sharedMesh = meshes[fr];
            if (follow != null) transform.position = follow.position;
            if (cachedCamera == null && Time.unscaledTime >= nextCameraLookup)
            {
                cachedCamera = Camera.main;
                nextCameraLookup = Time.unscaledTime + 0.5f;
            }
            Camera cam = cachedCamera;
            if (cam != null)
            {
                Vector3 f = cam.transform.forward, up = cam.transform.up, right = cam.transform.right;
                float ang = 0f;
                if (dir != null)
                {
                    Vector3 d = dir();
                    Vector2 s = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up));
                    if (s.sqrMagnitude > 1e-6f) ang = Mathf.Atan2(s.y, s.x) * Mathf.Rad2Deg;
                }
                transform.rotation = Quaternion.LookRotation(f, up) * Quaternion.Euler(0f, 0f, ang);
            }
            float k = loop ? 1f : 1f + grow * (age / Mathf.Max(0.01f, life));
            transform.localScale = new Vector3(flipX ? -1f : 1f, 1f, 1f) * (size * k);
            float fade = loop ? 1f : Mathf.Clamp01((life - age) / (life * 0.35f));
            mr.GetPropertyBlock(mpb);
            mpb.SetColor(TintId, new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.5f, 0.5f * tint.a * fade));
            mr.SetPropertyBlock(mpb);
        }
    }
}
