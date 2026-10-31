using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Жизнь за сёдзи храма ночью: на светящейся бумаге — тень монаха в сэйдза (чуть дышит), а раз в 25–55 с вдоль
    /// всех створок неспешно проходит другой монах (8 кадров шага, тень проявляется и тает у краёв). Днём бумага не светится
    /// изнутри — теней не видно. Тени — наклейки умножения (BladePath/AO) прямо на бумаге; кадр и сила — через MaterialPropertyBlock.
    /// </summary>
    public class ShojiShadows : MonoBehaviour
    {
        public Renderer monk, walker;
        public float xMin = -4.0f, xMax = 2.4f, speed = 0.42f, stride = 0.0975f;   // stride — путь за кадр (8 кадров ≈ 2 шага, 0,78 м)
        public int frames = 8;
        public float dir = 1f;
        MaterialPropertyBlock mpb;
        float nextAt, t = -1f, monkSy = 1f, walkStr = 0.66f;
        // «соловьиный пол» (угуисубари): доски галереи храма поскрипывают-чирикают под шагами идущего
        AudioSource floor;
        static AudioClip[] chirps;
        int lastStep = -1;
        static readonly int StId = Shader.PropertyToID("_MainTex_ST"), StrId = Shader.PropertyToID("_Strength");

        void Start()
        {
            mpb = new MaterialPropertyBlock();
            nextAt = Time.time + Random.Range(6f, 14f);
            if (monk != null) monkSy = monk.transform.localScale.z;
            if (walker != null) { if (walker.sharedMaterial != null) walkStr = walker.sharedMaterial.GetFloat(StrId); walker.enabled = false; }
            if (walker != null)
            {
                floor = walker.gameObject.AddComponent<AudioSource>();
                floor.playOnAwake = false; floor.loop = false;
                floor.spatialBlend = 0.55f; floor.minDistance = 3f; floor.maxDistance = 30f; floor.rolloffMode = AudioRolloffMode.Linear;
                if (chirps == null) chirps = new[] { Chirp(11, 2100f, 0.07f), Chirp(12, 2500f, 0.06f), Chirp(13, 1800f, 0.09f) };
            }
        }

        /// <summary>
        /// Скрип-чириканье соловьиного пола: скоба трётся о гвоздь — частота «кю-и» взлетает и опадает, амплитуда дрожит
        /// (прилипание-проскальзывание ~170 Гц), подмешан шорох трения. Короткий, 60–90 мс, со звонким хвостом доски.
        /// </summary>
        static AudioClip Chirp(int seed, float f0, float len)
        {
            const int rate = 22050;
            int n = (int)(rate * (len + 0.08f));
            float[] d = new float[n];
            System.Random r = new System.Random(seed);
            float ph = 0f, body = 0f, bv = 0f, peak = 1e-4f;
            float fb = 2f * Mathf.PI * 330f / rate;   // гулкий отзвук доски
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, u = Mathf.Clamp01(t / len);
                float f = f0 * (1f + 0.35f * Mathf.Sin(Mathf.PI * u) - 0.12f * u);
                ph += 2f * Mathf.PI * f / rate;
                float env = t < len ? Mathf.Sin(Mathf.PI * u) : 0f;
                float stick = 0.55f + 0.45f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 170f * t));
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                float v = (Mathf.Sin(ph) + 0.3f * Mathf.Sin(2f * ph)) * env * stick + x * env * 0.15f;
                bv += -fb * fb * body - 0.004f * bv + v * 0.02f; body += bv;   // доска откликается низким звоном
                d[i] = v + body;
                peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
            AudioClip c = AudioClip.Create("nightingale_floor_" + seed, n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        void Update()
        {
            // внутри зажгли свет — ночью, на рассвете и на закате (днём бумага изнутри не светится)
            bool night = DayNight.Instance == null || !DayNight.Instance.Day || DayNight.Instance.Dawn || DayNight.Instance.Dusk;
            if (monk != null)
            {
                monk.enabled = night;
                Vector3 s = monk.transform.localScale; s.z = monkSy * (1f + 0.008f * Mathf.Sin(Time.time * 1.25f)); monk.transform.localScale = s;   // дыхание
            }
            if (walker == null) return;
            if (t < 0f)
            {
                walker.enabled = false;
                if (!night || Time.time < nextAt) return;
                t = 0f; dir = Random.value < 0.5f ? 1f : -1f;
            }
            t += Time.deltaTime;
            float dur = (xMax - xMin) / speed;
            if (t > dur || !night) { t = -1f; lastStep = -1; walker.enabled = false; nextAt = Time.time + Random.Range(25f, 55f); return; }
            Pose(t / dur);
        }

        /// <summary>Поставить идущего в долю пути u (0…1): позиция, разворот по направлению, кадр шага, проявление у краёв.</summary>
        public void Pose(float u)
        {
            if (walker == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float len = xMax - xMin;
            Transform w = walker.transform;
            Vector3 p = w.localPosition; p.x = dir > 0f ? Mathf.Lerp(xMin, xMax, u) : Mathf.Lerp(xMax, xMin, u); w.localPosition = p;
            Vector3 sc = w.localScale; sc.x = Mathf.Abs(sc.x) * (dir > 0f ? 1f : -1f); w.localScale = sc;
            float fade = Mathf.Clamp01(Mathf.Min(u, 1f - u) * len / 0.6f);
            int frame = (int)(u * len / stride) % frames;
            // шаг — каждые полцикла (кадры 0 и 4 — стопа встаёт): скрип доски
            int step = (int)(u * len / stride) / (frames / 2);
            if (step != lastStep && Application.isPlaying)
            {
                lastStep = step;
                if (floor != null && chirps != null && fade > 0.3f && Random.value < 0.75f)
                {
                    floor.pitch = Random.Range(0.92f, 1.1f);
                    floor.PlayOneShot(chirps[Random.Range(0, chirps.Length)], Random.Range(0.1f, 0.18f));
                }
            }
            walker.enabled = true;
            walker.GetPropertyBlock(mpb);
            float fw = 1f / frames;
            mpb.SetVector(StId, new Vector4(fw, 1f, frame * fw, 0f));
            mpb.SetFloat(StrId, walkStr * fade);
            walker.SetPropertyBlock(mpb);
        }
    }
}
