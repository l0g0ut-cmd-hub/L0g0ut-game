using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Облака бегут по луне: ясной ночью раз в полминуты-минуту через лунный диск медленно проплывает рваное тёмное облако
    /// с подсвеченным краем; пока оно на луне — лунный свет во дворе гаснет (Weather.MoonDim), тени бледнеют, потом снова светло.
    /// </summary>
    public class MoonClouds : MonoBehaviour
    {
        public Vector3 moon = new Vector3(4.8f, 6.4f, 16f);
        Transform cloud; Renderer rend; Material mat;
        float x = -99f, speed, nextAt;
        static Texture2D tex;

        void Start()
        {
            if (tex == null) tex = MakeTex();
            Shader sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            mat = new Material(sh) { name = "MoonCloud", mainTexture = tex };
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(0.32f, 0.34f, 0.42f, 0.5f));
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Collider c = q.GetComponent<Collider>(); if (c != null) Destroy(c);
            q.name = "MoonCloud"; q.transform.SetParent(transform, false);
            q.transform.localScale = new Vector3(7.5f, 2.6f, 1f);
            rend = q.GetComponent<Renderer>(); rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
            cloud = q.transform; cloud.gameObject.SetActive(false);
            nextAt = Time.time + Random.Range(10f, 25f);
        }

        void OnDisable() { Weather.MoonDim = 1f; }

        void Update()
        {
            if (cloud == null) return;
            Weather w = Weather.Instance;
            bool clear = (DayNight.Instance == null || !DayNight.Instance.Day) && (w == null || (!w.Raining && !w.Snowing));
            if (x < -50f)
            {
                Weather.MoonDim = Mathf.MoveTowards(Weather.MoonDim, 1f, Time.deltaTime * 0.5f);
                if (clear && Time.time >= nextAt) { x = moon.x - 9f; speed = Random.Range(0.35f, 0.6f); cloud.gameObject.SetActive(true); }
                return;
            }
            x += speed * Time.deltaTime;
            cloud.position = new Vector3(x, moon.y + Mathf.Sin(x * 0.3f) * 0.3f, moon.z - 1.2f);
            float over = Mathf.Clamp01(1f - Mathf.Abs(x - moon.x) / 4.2f);
            Weather.MoonDim = Mathf.Lerp(1f, 0.35f, Mathf.SmoothStep(0f, 1f, over));
            if (x > moon.x + 9f || !clear) { x = -99f; cloud.gameObject.SetActive(false); nextAt = Time.time + Random.Range(30f, 70f); }
        }

        /// <summary>Рваное облако: шумовая клякса с мягким краем, по верхней кромке — серебряная подсветка луной (светлее).</summary>
        static Texture2D MakeTex()
        {
            const int W = 128, H = 48;
            Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Color[] px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int i = 0; i < W; i++)
                {
                    float u = i / (W - 1f) * 2f - 1f, v = y / (H - 1f) * 2f - 1f;
                    float n = Mathf.PerlinNoise(i * 0.06f, y * 0.12f) * 0.6f + Mathf.PerlinNoise(i * 0.17f + 7f, y * 0.3f) * 0.4f;
                    float shape = 1f - (u * u + v * v * 1.6f);
                    float a = Mathf.Clamp01((shape * 0.9f + (n - 0.5f) * 0.9f) * 2.2f);
                    float rim = Mathf.Clamp01(v * 1.4f) * (1f - a) * 2f;   // верхний край светлее
                    float b = 0.55f + 0.45f * Mathf.Clamp01(rim + n * 0.3f);
                    px[y * W + i] = new Color(b, b, b * 1.05f, a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }
    }
}
