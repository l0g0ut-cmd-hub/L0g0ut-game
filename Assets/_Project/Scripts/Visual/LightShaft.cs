using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Лунный луч сквозь крону в ночной дымке: аддитивная лента вдоль направления света, всегда повёрнута к камере вокруг
    /// своей оси; гаснет, когда смотрим вдоль неё (иначе виден край). Яркость медленно «дышит» (пыль и листья в луче).
    /// Текстура процедурная: мягкие края, затухание к концу, продольные прожилки.
    /// </summary>
    public class LightShaft : MonoBehaviour
    {
        public Vector3 dir = Vector3.down;
        public float length = 6f, width = 0.8f;
        public Color color = new Color(0.45f, 0.55f, 0.85f);
        public float strength = 0.06f;
        static Material mat;
        static Mesh quad;
        MeshRenderer rend;
        MaterialPropertyBlock mpb;
        float seed;
        Camera cachedCamera;
        float nextCameraLookup;

        void Awake()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = Quad();
            rend = GetComponent<MeshRenderer>();
            if (rend == null) rend = gameObject.AddComponent<MeshRenderer>();
            rend.sharedMaterial = Mat();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
            mpb = new MaterialPropertyBlock();
            seed = Random.value * 10f;
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "shaft_quad" };
            // ось ленты — локальная +Y от 0 до 1, ширина по X
            quad.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateNormals(); quad.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 2f, 2f));
            return quad;
        }

        static Material Mat()
        {
            if (mat != null) return mat;
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) return null;
            mat = new Material(sh) { name = "LightShaft" };
            const int w = 64, h = 128;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "light_shaft" };
            Color[] px = new Color[w * h];
            System.Random r = new System.Random(5);
            float[] streak = new float[w];
            float acc = 0f;
            for (int x = 0; x < w; x++) { acc = acc * 0.7f + (float)r.NextDouble() * 0.3f; streak[x] = acc; }
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                float along = Mathf.SmoothStep(0f, 1f, v / 0.18f) * Mathf.Pow(1f - v, 1.3f);   // от кроны к земле: вспыхивает и тает
                for (int x = 0; x < w; x++)
                {
                    float u = x / (w - 1f) * 2f - 1f;
                    float across = Mathf.Exp(-u * u * 3.2f);
                    float s = 0.6f + 0.8f * streak[x];
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(along * across * s));
                }
            }
            t.SetPixels(px); t.Apply(true);
            mat.mainTexture = t;
            mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return mat;
        }

        void LateUpdate()
        {
            if (cachedCamera == null && Time.unscaledTime >= nextCameraLookup)
            {
                cachedCamera = Camera.main;
                nextCameraLookup = Time.unscaledTime + 0.5f;
            }
            Camera c = cachedCamera;
            if (rend == null) return;
            if (c == null || strength <= 0.0001f || width <= 0f || length <= 0f)
            {
                if (rend.enabled) rend.enabled = false;
                return;
            }
            Vector3 ax = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.down;
            Vector3 toCam = c.transform.position - transform.position;
            // Distance fade keeps nearby shafts cinematic without leaving
            // distant billboards bright in the background of low-end phones.
            float distance = toCam.magnitude;
            if (distance > 55f)
            {
                rend.enabled = false;
                return;
            }
            Vector3 side = Vector3.Cross(ax, toCam);
            if (side.sqrMagnitude < 1e-6f) { rend.enabled = false; return; }
            side.Normalize();
            Vector3 nrm = Vector3.Cross(side, ax);
            transform.rotation = Quaternion.LookRotation(-nrm, ax);   // локальная +Y — вдоль луча, лицом к камере
            transform.localScale = new Vector3(width, length, 1f);
            float facing = Mathf.Pow(1f - Mathf.Abs(Vector3.Dot(toCam.normalized, ax)), 1.45f);
            float distanceFade = 1f - Mathf.SmoothStep(28f, 55f, distance);
            float breathe = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 0.15f + seed, seed);
            float wet = Weather.Instance != null && Weather.Instance.Raining ? 0.4f : (Weather.Instance != null && Weather.Instance.Snowing ? 0.7f : 1f);   // в дождь луна за тучами
            float budget = DuelManager.Gfx == 1
                ? (MobileVfxBudget.Level == 2 ? 0.72f : 1f) : 0.55f;
            float brightness = strength * breathe * facing * wet * distanceFade * budget;
            Color col = color * brightness;
            col.a = Mathf.Clamp01(0.18f + brightness * 0.85f);
            rend.enabled = brightness > 0.0015f;
            if (!rend.enabled) return;
            mpb.SetColor("_TintColor", col);
            rend.SetPropertyBlock(mpb);
        }
    }
}
