using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Следы стоп: в снегу — вдавленный отпечаток таби (тень внутри, светлый валик снега по краю), после шага по луже крови —
    /// несколько кровавых отпечатков, всё бледнее. Пул из 56 плоских квадов, без мусора на шаг; след тает со временем,
    /// снежный — ещё и вместе со снегом на камне (_BP_Snow). Левая стопа — зеркальный отпечаток.
    /// </summary>
    public class Footprints : MonoBehaviour
    {
        public static Footprints Instance { get; private set; }
        const int Max = 56;
        struct Print { public Transform t; public MeshRenderer r; public float born, life, alpha; public bool blood; }
        readonly List<Print> prints = new List<Print>();
        int next;
        Material snowMat, bloodMat;
        MaterialPropertyBlock mpb;
        static Mesh quad;
        static readonly int SnowId = Shader.PropertyToID("_BP_Snow");

        public static Footprints Ensure()
        {
            if (Instance != null) return Instance;
            GameObject g = new GameObject("Footprints");
            Instance = g.AddComponent<Footprints>();
            return Instance;
        }

        void Awake() { Instance = this; Init(); }

        void Init()
        {
            if (snowMat != null) return;
            mpb = new MaterialPropertyBlock();
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return;
            snowMat = new Material(sh) { name = "FootprintSnow", mainTexture = Sole(true), renderQueue = 2458 };
            bloodMat = new Material(sh) { name = "FootprintBlood", mainTexture = Sole(false), renderQueue = 2458 };
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Отпечаток: pos — точка стопы на земле, toe — куда смотрит носок, left — левая стопа.</summary>
        public void Add(Vector3 pos, Vector3 toe, bool left, bool blood, float strength)
        {
            Init();   // в редакторе (кадры CI) Awake не вызывается
            if (snowMat == null || strength <= 0.01f) return;
            toe.y = 0f;
            if (toe.sqrMagnitude < 1e-6f) return;
            Print p;
            if (prints.Count < Max)
            {
                GameObject g = new GameObject("Footprint");
                g.transform.SetParent(transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = Quad();
                p = new Print { t = g.transform, r = g.AddComponent<MeshRenderer>() };
                p.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; p.r.receiveShadows = false;
                prints.Add(p); next = prints.Count - 1;
            }
            else { next = (next + 1) % prints.Count; p = prints[next]; }
            p.blood = blood;
            p.r.sharedMaterial = blood ? bloodMat : snowMat;
            p.born = Time.time; p.life = blood ? 26f : 40f; p.alpha = Mathf.Clamp01(strength);
            // носок вперёд, пятка — у точки опоры чуть назад; порядок по высоте — чтобы соседние следы не мерцали
            Vector3 f = toe.normalized;
            p.t.position = new Vector3(pos.x, 0.0118f + (next % 9) * 0.00025f, pos.z) + f * 0.06f;
            p.t.rotation = Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f);
            p.t.localScale = new Vector3(left ? -0.125f : 0.125f, 1f, 0.29f) * Random.Range(0.96f, 1.04f);
            p.t.gameObject.SetActive(true);
            prints[next] = p;
        }

        void LateUpdate()
        {
            if (mpb == null) return;
            float snow = Shader.GetGlobalFloat(SnowId);
            for (int i = 0; i < prints.Count; i++)
            {
                Print p = prints[i];
                if (!p.t.gameObject.activeSelf) continue;
                float age = Time.time - p.born;
                if (age > p.life) { p.t.gameObject.SetActive(false); continue; }
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - p.life * 0.55f) / (p.life * 0.45f)));
                float a = p.alpha * fade * (p.blood ? 1f : Mathf.Clamp01(snow / 0.5f));
                if (p.blood) a *= Mathf.Lerp(1f, 0.7f, Mathf.Clamp01(age / 8f));   // подсыхает — темнеет и тускнеет
                Color c = p.blood ? new Color(0.36f, 0.025f, 0.02f, a * 0.85f) : new Color(0.7f, 0.76f, 0.88f, a * 0.55f);   // на телефоне в полную силу следы читались чёрными кляксами
                p.r.GetPropertyBlock(mpb);
                mpb.SetColor("_Color", c);
                p.r.SetPropertyBlock(mpb);
            }
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "footprint_quad" };
            quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateNormals(); quad.RecalculateBounds();
            return quad;
        }

        /// <summary>
        /// Отпечаток таби (правая стопа, носок к +V): подушечка с отдельным большим пальцем, узкий свод, пятка.
        /// Снег: внутри тень (тёмнее), по краю валик (светлее и плотнее); кровь — сплошной отпечаток с неровной кромкой.
        /// </summary>
        static Texture2D Sole(bool snow)
        {
            const int w = 48, h = 112;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = snow ? "print_snow" : "print_blood" };
            Color[] px = new Color[w * h];
            System.Random r = new System.Random(snow ? 41 : 43);
            float[] jit = new float[h];
            for (int y = 0; y < h; y++) jit[y] = (float)r.NextDouble();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;   // u: -1 внутр. край (большой палец) … 1 наружный
                    float d = SoleDist(u, v);
                    float edge = 0.06f + 0.02f * jit[(y * 7 + x) % h];
                    float inside = Mathf.Clamp01(-d / edge + 0.5f);
                    Color c;
                    if (snow)
                    {
                        // снег на камне тонкий: под подошвой он примят и подтаял — тёмный мокрый камень; по краю — узкий светлый валик
                        float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.04f) / 0.04f);
                        float shade = inside * (0.7f + 0.3f * Mathf.Clamp01(-d / 0.2f));
                        float a = Mathf.Max(shade * 0.85f, rim * 0.3f);
                        float lum = Mathf.Lerp(0.16f, 1f, rim * (1f - inside));
                        c = new Color(lum, lum, lum, a);
                    }
                    else
                    {
                        float n = (float)r.NextDouble();
                        float a = inside * (0.75f + 0.25f * n) * (d < -0.05f ? 0.85f + 0.15f * Mathf.Clamp01(-d / 0.2f) : 1f);
                        c = new Color(1f, 1f, 1f, a);
                    }
                    px[y * w + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>Знаковое расстояние до контура подошвы (в долях ширины, отрицательное внутри).</summary>
        static float SoleDist(float u, float v)
        {
            // пятка, свод, подушечка — эллипсы; таби: большой палец отдельно
            float heel = Ell(u - 0.04f, v - 0.17f, 0.62f, 0.14f);
            float arch = Ell(u - 0.3f, v - 0.42f, 0.4f, 0.2f);
            float ball = Ell(u - 0.02f, v - 0.66f, 0.82f, 0.17f);
            float toes = Ell(u - 0.22f, v - 0.85f, 0.56f, 0.09f);
            float big = Ell(u + 0.58f, v - 0.86f, 0.3f, 0.1f);
            float d = Mathf.Min(Mathf.Min(heel, arch), Mathf.Min(ball, Mathf.Min(toes, big)));
            // щель таби между большим пальцем и остальными
            float gap = Mathf.Abs(u + 0.3f) < 0.05f && v > 0.8f ? 0.08f : 0f;
            return d + gap;
        }

        static float Ell(float x, float y, float rx, float ry)
        {
            float k = Mathf.Sqrt((x / rx) * (x / rx) + (y / ry) * (y / ry));
            return (k - 1f) * Mathf.Min(rx, ry * 2.2f);
        }
    }
}
