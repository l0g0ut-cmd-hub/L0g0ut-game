using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Отражение огня/фонаря в мокром камне без второго рендера сцены: аддитивная полоса на полу там, где луч из камеры
    /// в зеркальное (под полом) изображение источника пересекает пол, вытянута к камере (чем ниже камера, тем длиннее),
    /// разбита поперечной «рябью» — блики на неровных мокрых плитах. В дождь ярче. Яркость идёт за Light (мерцание огня).
    /// </summary>
    public class FloorReflection : MonoBehaviour
    {
        public Transform source;
        public Light lightRef;
        public float baseIntensity = 1.7f;
        public Color color = new Color(1f, 0.55f, 0.2f);
        public float width = 0.45f, length = 2.4f, strength = 0.55f;
        public float floorY = 0.008f;
        [Tooltip("Мокрая площадка (minX, maxX, minZ, maxZ): за её краем — гравий, отражения нет.")]
        public Vector4 bounds = new Vector4(-3.6f, 3.6f, -2.7f, 2.7f);

        static Material mat;
        static Mesh quad;
        MeshRenderer rend;
        MaterialPropertyBlock mpb;

        static Material Mat()
        {
            if (mat != null) return mat;
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) return null;
            mat = new Material(sh) { name = "FloorReflection" };
            mat.mainTexture = Streak();
            mat.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return mat;
        }

        // полоса: узкая по ширине (гаусс), длинная по длине, с поперечными просветами — неровные мокрые плиты
        static Texture2D Streak()
        {
            const int w = 64, h = 256;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "reflection_streak" };
            Color[] px = new Color[w * h];
            System.Random r = new System.Random(3);
            float[] band = new float[h];
            float acc = 0f;
            for (int y = 0; y < h; y++) { acc = acc * 0.82f + (float)r.NextDouble() * 0.18f; band[y] = acc; }
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                float along = Mathf.Exp(-Mathf.Pow((v - 0.35f) / (v < 0.35f ? 0.18f : 0.38f), 2f));   // ярче у основания (ближе к источнику)
                float rip = 0.45f + 1.1f * Mathf.Clamp01((band[y] - 0.35f) * 3f);
                for (int x = 0; x < w; x++)
                {
                    float u = (x / (w - 1f)) * 2f - 1f;
                    float wob = 0.12f * Mathf.Sin(y * 0.37f) * (float)r.NextDouble();
                    float across = Mathf.Exp(-Mathf.Pow((u + wob) / 0.42f, 2f));
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(along * across * rip));
                }
            }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "reflection_quad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateNormals(); quad.RecalculateBounds();
            return quad;
        }

        void Awake()
        {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = Quad();
            rend = GetComponent<MeshRenderer>();
            if (rend == null) rend = gameObject.AddComponent<MeshRenderer>();
            rend.sharedMaterial = Mat();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            mpb = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            Camera c = Camera.main;
            if (c == null || source == null || rend == null) return;
            Vector3 L = source.position;
            float hgt = L.y - floorY;
            Vector3 cp = c.transform.position;
            if (hgt <= 0.01f || cp.y <= floorY + 0.05f) { rend.enabled = false; return; }
            Vector3 mirror = new Vector3(L.x, floorY - hgt, L.z);
            float k = (cp.y - floorY) / (cp.y - mirror.y);
            Vector3 hit = cp + (mirror - cp) * k; hit.y = floorY;
            if (hit.x < bounds.x || hit.x > bounds.y || hit.z < bounds.z || hit.z > bounds.w) { rend.enabled = false; return; }
            Vector3 toCam = cp - hit; toCam.y = 0f;
            float dist = toCam.magnitude;
            if (dist < 0.05f) { rend.enabled = false; return; }
            toCam /= dist;
            float graze = Mathf.Clamp(dist / (cp.y - floorY), 0.6f, 7f);   // пологий взгляд — длинная полоса
            float len = length * Mathf.Lerp(0.35f, 1.15f, Mathf.InverseLerp(0.6f, 7f, graze));
            rend.enabled = true;
            // полоса начинается чуть «за» точкой отражения и тянется к камере
            transform.position = hit + toCam * (len * 0.3f);
            transform.rotation = Quaternion.LookRotation(Vector3.down, toCam);   // +Y квада (v↑) — к камере
            transform.localScale = new Vector3(width * Mathf.Lerp(1f, 0.7f, Mathf.InverseLerp(0.6f, 7f, graze)), len, 1f);
            float li = lightRef != null && baseIntensity > 0f ? lightRef.intensity / baseIntensity : 1f;
            float wet = Weather.Instance != null && Weather.Instance.Raining ? 1.5f : 1f;
            Color col = color * (0.5f * strength * li * wet); col.a = 0.5f;
            mpb.SetColor("_TintColor", col);
            rend.SetPropertyBlock(mpb);
        }
    }
}
