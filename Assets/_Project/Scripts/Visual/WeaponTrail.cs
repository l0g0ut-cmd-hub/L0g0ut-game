using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// След клинка «во всю ширину»: лента между основанием и остриём за последние ~0,14 с удара.
    /// Сэмплы сглаживаются сплайном Катмулла–Рома, поэтому дуга плавная даже при 30 кадрах/с.
    /// Цвет: белое ядро у острия → цвет владельца → прозрачный хвост.
    /// </summary>
    [DefaultExecutionOrder(200)]   // после BladeTrajectorySampler (100) и позы рук
    public class WeaponTrail : MonoBehaviour
    {
        public WeaponController weapon;
        public Color color = new Color(0.6f, 0.85f, 1f);
        public float life = 0.14f;
        public float from = 0.25f;     // доля клинка, с которой начинается лента
        bool textured;                 // цвет — в текстуре: вершины почти белые, только прозрачность

        struct S { public Vector3 a, b; public float t; }
        readonly List<S> samples = new List<S>();
        Mesh mesh;
        MeshRenderer trailRenderer;
        const int MaxSamples = 18; // bounded geometry and memory even at very high frame rates
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();
        static Material mat;

        static readonly Dictionary<string, Material> texMats = new Dictionary<string, Material>();
        /// <summary>След с текстурой из атласа (Resources/FX/trail_*: острая яркая кромка у острия, к хвосту рвётся на нити).</summary>
        static Material TexMat(string tex)
        {
            Material m;
            if (string.IsNullOrEmpty(tex)) return null;
            if (texMats.TryGetValue(tex, out m) && m != null) return m;
            Texture2D t = Resources.Load<Texture2D>("FX/" + tex);
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (t == null || sh == null) return null;
            t.wrapMode = TextureWrapMode.Clamp;
            m = new Material(sh) { name = "Trail_" + tex, mainTexture = t };
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            texMats[tex] = m;
            return m;
        }

        public static WeaponTrail Attach(WeaponController w, Color c, string tex = null)
        {
            GameObject g = new GameObject("WeaponTrail");
            WeaponTrail t = g.AddComponent<WeaponTrail>();
            t.weapon = w; t.color = c;
            g.AddComponent<MeshFilter>().sharedMesh = t.mesh = new Mesh { name = "trail" };
            t.mesh.MarkDynamic();
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            t.trailRenderer = r;
            r.enabled = false;
            Material tm = TexMat(tex);
            r.sharedMaterial = tm != null ? tm : Mat();
            t.textured = tm != null;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return t;
        }

        static float SStep(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3f - 2f * x); }

        static Material Mat()
        {
            if (mat != null) return mat;
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            mat = new Material(sh);
            // поперечный градиент (мягкие края, яркая кромка у острия) + «волоски кисти»: тонкие продольные пряди разной яркости,
            // к хвосту (u = возраст) пряди обрываются вразнобой — след рвётся на нити, как мазок сухой кистью, а не ровная лента
            const int TW = 64, TH = 128;
            Texture2D t = new Texture2D(TW, TH, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            System.Random rnd = new System.Random(7);
            float[] hair = new float[TH], end = new float[TH];
            float h = 0.8f;
            for (int y = 0; y < TH; y++)
            {
                h = Mathf.Lerp(h, 0.45f + 0.55f * (float)rnd.NextDouble(), 0.55f);   // соседние пряди связаны — не «шум», а волокна
                hair[y] = h;
                end[y] = 0.35f + 0.65f * (float)rnd.NextDouble();
            }
            for (int y = 0; y < TH; y++)
            {
                float v = y / (TH - 1f);
                float a0 = Mathf.SmoothStep(0f, 1f, v) * (v > 0.9f ? Mathf.Lerp(1f, 0.2f, (v - 0.9f) / 0.1f) : 1f);
                float hv = Mathf.Lerp(hair[y], 1f, SStep(0.75f, 1f, v));   // у острия кромка цельная
                for (int x = 0; x < TW; x++)
                {
                    float u = x / (TW - 1f);
                    float cut = 1f - SStep(end[y] - 0.18f, end[y], u) * (1f - SStep(0.8f, 1f, v));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a0 * hv * cut));
                }
            }
            t.Apply();
            mat.mainTexture = t;
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return mat;
        }

        void LateUpdate()
        {
            if (weapon == null || weapon.bladeRoot == null)
            {
                samples.Clear();
                if (trailRenderer != null) trailRenderer.enabled = false;
                return;
            }
            float now = Time.time;
            float duration = Mathf.Max(0.035f, life);
            bool live = weapon.Phase == SwingPhase.Strike || weapon.Phase == SwingPhase.FollowThrough;
            if (live)
            {
                Transform br = weapon.bladeRoot;
                float length = Mathf.Max(0.02f, weapon.bladeLength);
                float start = Mathf.Clamp01(from);
                S s; s.a = br.position + br.forward * (length * start); s.b = br.position + br.forward * length; s.t = now;
                if (samples.Count > 0 && (samples[samples.Count - 1].b - s.b).sqrMagnitude > 1.5f)
                    samples.Clear(); // new weapon pose/teleport: no full-screen streak
                if (samples.Count == 0 || (samples[samples.Count - 1].b - s.b).sqrMagnitude > 1e-6f)
                {
                    int maxSamples = MobileVfxBudget.Level >= 2 ? 12 : MaxSamples;
                    if (samples.Count >= maxSamples) samples.RemoveAt(0);
                    samples.Add(s);
                }
            }
            while (samples.Count > 0 && now - samples[0].t > duration) samples.RemoveAt(0);
            transform.position = Vector3.zero; transform.rotation = Quaternion.identity;
            Build(now);
        }

        void OnDisable()
        {
            samples.Clear();
            if (trailRenderer != null) trailRenderer.enabled = false;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        void Build(float now)
        {
            if (samples.Count < 2)
            {
                if (trailRenderer != null && trailRenderer.enabled) trailRenderer.enabled = false;
                return;
            }
            if (trailRenderer != null && !trailRenderer.enabled) trailRenderer.enabled = true;
            mesh.Clear();
            verts.Clear(); cols.Clear(); uvs.Clear(); tris.Clear();
            // At the lowest mobile tier the silhouette remains intact with
            // half as many Catmull-Rom subdivisions and triangles.
            int sub = MobileVfxBudget.Level >= 2 ? 2 : 4;
            int n = samples.Count;
            for (int i = 0; i < n - 1; i++)
            {
                S p0 = samples[Mathf.Max(0, i - 1)], p1 = samples[i], p2 = samples[i + 1], p3 = samples[Mathf.Min(n - 1, i + 2)];
                int steps = (i == n - 2) ? sub + 1 : sub;
                for (int k = 0; k < steps; k++)
                {
                    float u = k / (float)sub;
                    Vector3 a = CR(p0.a, p1.a, p2.a, p3.a, u), b = CR(p0.b, p1.b, p2.b, p3.b, u);
                    float t = Mathf.Lerp(p1.t, p2.t, u);
                    float age = Mathf.Clamp01((now - t) / Mathf.Max(0.035f, life));
                    // Bright leading edge with a soft, fast-dissolving tail; keep the blade itself visible.
                    // Old sections collapse towards the blade's back edge. A
                    // narrowing dry-brush tail reads like a katana arc rather than
                    // a thick translucent plastic strip at its endpoint.
                    float taper = Mathf.Lerp(0.24f, 1f, Mathf.Pow(1f - age, 0.75f));
                    b = Vector3.Lerp(a, b, taper);
                    float alpha = Mathf.Pow(1f - age, 2.4f);
                    float headGlow = 1f - Mathf.SmoothStep(0f, 0.45f, age);
                    Color core = Color.Lerp(color, Color.white, headGlow * 0.8f);
                    core.a = alpha * (0.75f + 0.25f * headGlow);
                    Color edge = Color.Lerp(color, Color.white, headGlow * 0.15f);
                    edge.a = alpha * 0.12f;
                    if (textured)
                    {
                        // Atlas controls shape; vertex alpha adds the same temporal fade.
                        core = new Color(1f, 1f, 1f, alpha);
                        edge = new Color(1f, 1f, 1f, alpha * 0.8f);
                    }
                    verts.Add(a); cols.Add(edge); uvs.Add(new Vector2(age, 0f));
                    verts.Add(b); cols.Add(core); uvs.Add(new Vector2(age, 1f));
                }
            }
            for (int i = 0; i + 3 < verts.Count; i += 2)
            {
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i + 2); tris.Add(i + 1); tris.Add(i + 3);
                // обратная сторона (лента видна с обеих сторон)
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i + 2); tris.Add(i + 3); tris.Add(i + 1);
            }
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
