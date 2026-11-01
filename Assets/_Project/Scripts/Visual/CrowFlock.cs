using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Вороны над храмом: изредка (раз в 20–40 с) небольшая стая пролетает высоко на фоне неба и луны, машет крыльями
    /// (4 кадра силуэта, рисуются процедурно), изредка каркает. Всё создаётся при запуске — сцене нужен только компонент.
    /// </summary>
    public class CrowFlock : MonoBehaviour
    {
        public float minGap = 20f, maxGap = 40f;
        ParticleSystem ps;
        ParticleSystemRenderer rend;
        bool photo;
        float nextAt;
        AudioSource src;
        AudioClip caw;

        void Start()
        {
            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.startLifetime = 9f; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.startColor = new Color(0.03f, 0.03f, 0.05f, 0.95f);
            main.maxParticles = 12; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.enabled = false;
            var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 1;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
            tsa.cycleCount = 40;   // взмахи крыльев за полёт
            var r = GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Sprites/Default");
            // фото-кадры взмаха из атласа (Resources/FX/crow_flap: вверх → горизонт → вниз → подъём); без них — процедурный силуэт
            Texture2D atlas = Resources.Load<Texture2D>("FX/crow_flap");
            photo = atlas != null;
            if (sh != null) { Material m = new Material(sh) { name = "Crows" }; m.mainTexture = photo ? atlas : Sheet(); r.sharedMaterial = m; }
            if (photo) main.startColor = new Color(0.62f, 0.62f, 0.68f, 1f);   // перо видно, но не светится ночью
            rend = r;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0.18f;
            caw = Caw();
            nextAt = Time.time + Random.Range(6f, 14f);
        }

        void Update()
        {
            if (ps == null || Time.time < nextAt) return;
            nextAt = Time.time + Random.Range(minGap, maxGap);
            // стая летит поперёк неба (слева направо или наоборот), чуть снижаясь
            int n = Random.Range(3, 7);
            float dir = Random.value < 0.5f ? 1f : -1f;
            if (photo && rend != null) rend.flip = new Vector3(dir < 0f ? 1f : 0f, 0f, 0f);   // кадры смотрят вправо — стая влево отражается
            float y = Random.Range(6.5f, 9f), z = Random.Range(12f, 18f);
            for (int i = 0; i < n; i++)
            {
                ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
                e.position = new Vector3(-18f * dir - i * Random.Range(0.6f, 1.4f) * dir, y + Random.Range(-0.6f, 0.6f), z + Random.Range(-1.5f, 1.5f));
                e.velocity = new Vector3(Random.Range(3.6f, 4.6f) * dir, Random.Range(-0.15f, 0.05f), 0f);
                e.startSize = Random.Range(0.45f, 0.7f);
                e.rotation = 0f;
                if (photo)
                {
                    DayNight dn = DayNight.Instance;
                    float k = dn == null || !dn.Day ? 0.26f : dn.Dusk || dn.Dawn ? 0.45f : 0.7f;   // спрайт не освещается — тон по времени суток
                    e.startColor = new Color(k, k, k * 1.06f, 1f);
                }
                ps.Emit(e, 1);
            }
            if (src != null && caw != null && Random.value < 0.7f && Ambience.Claim(0.7f, 3f, 7f)) { src.pitch = Random.Range(0.9f, 1.1f); src.PlayOneShot(caw); }
        }

        /// <summary>4 кадра силуэта вороны в ряд (128×32): крылья вверх, середина, вниз, середина.</summary>
        static Texture2D Sheet()
        {
            const int fw = 32, fh = 32;
            Texture2D t = new Texture2D(fw * 4, fh, TextureFormat.RGBA32, true) { name = "crow_sheet", wrapMode = TextureWrapMode.Clamp };
            Color[] px = new Color[fw * 4 * fh];
            float[] wing = { 0.75f, 0.1f, -0.55f, 0.1f };   // подъём концов крыльев по кадрам
            for (int f = 0; f < 4; f++)
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                    {
                        float u = (x + 0.5f) / fw * 2f - 1f, v = (y + 0.5f) / fh * 2f - 1f;
                        float a = 0f;
                        // тело и голова
                        float body = (u * u) / 0.09f + (v * v) / 0.02f;
                        if (body < 1f) a = 1f;
                        float hx = u - 0.33f, hv = v - 0.04f;
                        if (hx * hx + hv * hv < 0.012f) a = 1f;
                        // крылья: от плеча к концу дуга, конец поднят/опущен по кадру
                        float au = Mathf.Abs(u);
                        if (au < 0.95f)
                        {
                            float k = au / 0.95f;
                            float cy = wing[f] * k * k * 0.75f + 0.02f;
                            float th = 0.11f * (1f - k) + 0.03f;
                            if (Mathf.Abs(v - cy) < th && u < 0.4f + 0.0f && u > -0.95f) a = 1f;
                            if (Mathf.Abs(v - cy) < th && u >= 0.4f && u < 0.95f && Mathf.Abs(u) > 0.42f) a = 1f;
                        }
                        // хвост
                        if (u < -0.2f && u > -0.48f && Mathf.Abs(v + 0.02f) < 0.05f + (-0.2f - u) * 0.15f) a = Mathf.Max(a, 1f);
                        px[y * fw * 4 + f * fw + x] = new Color(1f, 1f, 1f, a);
                    }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>Карканье: хриплый шум с основным тоном ~600 Гц, два коротких «кар».</summary>
        static AudioClip Caw()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.75f);
            float[] d = new float[n];
            System.Random r = new System.Random(9);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = 0f;
                for (int c = 0; c < 2; c++) { float s = t - c * 0.32f; if (s > 0f && s < 0.24f) env = Mathf.Max(env, Mathf.Sin(Mathf.PI * s / 0.24f) * (c == 0 ? 1f : 0.8f)); }
                float f = 560f + 120f * Mathf.Sin(t * 30f);
                ph += 2f * Mathf.PI * f / rate;
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                lp += 0.35f * (x - lp);
                float saw = (ph / (2f * Mathf.PI)) % 1f * 2f - 1f;
                d[i] = (saw * 0.45f + lp * 0.8f) * env * 0.5f;
            }
            AudioClip c2 = AudioClip.Create("caw", n, 1, rate, false);
            c2.SetData(d, 0);
            return c2;
        }
    }
}
