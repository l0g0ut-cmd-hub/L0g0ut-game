using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Сиси-одоси в саду храма: бамбуковая трубка медленно наполняется из жёлоба, перевешивает, выливает воду
    /// и падает обратно — сухой стук бамбука о камень раз в несколько секунд (звук синтезирован).
    /// Вращается вокруг оси pivot (локальная Z); угол покоя — закрытый конец лежит на камне.
    /// </summary>
    public class ShishiOdoshi : MonoBehaviour
    {
        public float restAngle = -16f, tipAngle = 6f, spillAngle = 30f;
        public float fillTime = 7f;
        public ParticleSystem spill, trickle;
        float t; int phase;   // 0 наполнение, 1 опрокидывание, 2 выливает, 3 падает назад
        AudioSource src;
        static AudioClip knock;

        void Start()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0.32f;
            if (knock == null) knock = Knock();
            // капли воды: мягкий диск (текстура процедурная — ставится при запуске)
            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                Material m = new Material(sh) { name = "WaterDrops" }; m.mainTexture = SoftSprites.Disc;
                foreach (ParticleSystem ps in new[] { spill, trickle })
                    if (ps != null) ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
            }
            t = Random.Range(0f, fillTime);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            t += dt;
            float a;
            switch (phase)
            {
                case 0:   // наполнение: трубка медленно приподнимается, к концу — быстрее (перевес)
                    { float u = Mathf.Clamp01(t / fillTime); a = Mathf.Lerp(restAngle, tipAngle, u * u * u); if (u >= 1f) { phase = 1; t = 0f; } break; }
                case 1:   // перевесила
                    { float u = Mathf.Clamp01(t / 0.35f); a = Mathf.Lerp(tipAngle, spillAngle, 1f - (1f - u) * (1f - u)); if (u >= 1f) { phase = 2; t = 0f; if (spill != null) spill.Play(); } break; }
                case 2:   // вода льётся
                    a = spillAngle; if (t > 0.45f) { phase = 3; t = 0f; }
                    break;
                default:  // пустая — падает обратно и бьёт о камень, лёгкий отскок
                    {
                        float u = Mathf.Clamp01(t / 0.22f);
                        a = Mathf.Lerp(spillAngle, restAngle, u * u);
                        if (u >= 1f)
                        {
                            if (src != null && knock != null) { src.pitch = Random.Range(0.96f, 1.04f); src.PlayOneShot(knock); }
                            phase = 0; t = 0f;
                        }
                        break;
                    }
            }
            if (phase == 0 && t < 0.25f) a += 3.5f * Mathf.Sin(t / 0.25f * Mathf.PI) * (1f - t / 0.25f);   // отскок после удара
            transform.localRotation = Quaternion.Euler(0f, 0f, a);
        }

        /// <summary>Стук бамбука о камень: полый «ток» — две затухающие моды трубки, щелчок и короткий низ.</summary>
        static AudioClip Knock()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.6f);
            float[] d = new float[n];
            System.Random r = new System.Random(3);
            float lp = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float v = Mathf.Sin(2f * Mathf.PI * 540f * t) * Mathf.Exp(-t * 32f) + 0.55f * Mathf.Sin(2f * Mathf.PI * 1190f * t) * Mathf.Exp(-t * 48f)
                        + 0.35f * Mathf.Sin(2f * Mathf.PI * 230f * t) * Mathf.Exp(-t * 20f);
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                lp += 0.5f * (x - lp);
                v += (x - lp) * Mathf.Exp(-t * 900f) * 1.2f;
                v *= Mathf.Min(1f, t * 4000f);
                // эхо от стен сада
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            int echo = (int)(rate * 0.11f);
            for (int i = n - 1; i >= echo; i--) d[i] += d[i - echo] * 0.22f;
            for (int i = 0; i < n; i++) d[i] *= 0.9f / (peak * 1.25f);
            AudioClip c = AudioClip.Create("shishi_odoshi", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
