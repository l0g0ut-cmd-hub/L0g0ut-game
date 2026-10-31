using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Цикады летнего дня (семи): только днём и в ясную погоду (в дождь, снег и ночью молчат — ночью сверчки, Juice).
    /// Не постоянный фон, а редкие «выходы»: раз в 22–50 с то сухой шипящий треск абурадзэми («дзи-и-и-и», как масло на сковороде),
    /// то певучая минминдзэми («ми-и-ин, мин-мин-мин… ми-и-и»); иногда вторая подхватывает с другой стороны.
    /// Голоса синтезированы (шум в полосе 4–6 кГц с быстрой пульсацией; тон 3–4 кГц с жужжащей модуляцией), стерео-панорама.
    /// Дальний план (раз в 40–75 с, в ясную погоду): днём над рощей кричит коршун тоби («пи-и-и… хйороро»),
    /// ночью — неясыть («у-у… у-у-у»); тихо, с эхом от леса.
    /// </summary>
    public class Cicadas : MonoBehaviour
    {
        public float minGap = 16f, maxGap = 34f;
        AudioSource[] voices;
        static AudioClip abura, minmin, higurashi, tobi, owl;
        AudioSource far;
        float nextAt = -1f, farAt;
        bool wasDay;

        void Start()
        {
            voices = new AudioSource[2];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false; voices[i].spatialBlend = 0f; voices[i].loop = false;
            }
            if (abura == null) abura = Abura();
            if (minmin == null) minmin = Minmin();
            if (higurashi == null) higurashi = Higurashi();
            far = gameObject.AddComponent<AudioSource>();
            far.playOnAwake = false; far.spatialBlend = 0f; far.loop = false;
            if (tobi == null) tobi = Tobi();
            if (owl == null) owl = Owl();
            farAt = Time.time + Random.Range(10f, 22f);
        }

        /// <summary>Дальний голос: коршун днём (не на рассвете), неясыть ночью; в дождь и снег — тишина.</summary>
        void Far()
        {
            Weather w = Weather.Instance;
            if (far == null || w == null || Time.time < farAt) return;
            farAt = Time.time + Random.Range(40f, 75f);
            if (w.Raining || w.Snowing) return;
            AudioClip c = w.Daytime ? (w.Dawn ? null : tobi) : owl;
            if (c == null) return;
            far.clip = c; far.panStereo = Random.Range(-0.65f, 0.65f); far.pitch = Random.Range(0.96f, 1.04f);
            far.volume = w.Daytime ? 0.24f : 0.32f;
            far.Play();
        }

        bool Allowed { get { Weather w = Weather.Instance; return w != null && w.Daytime && !w.Raining && !w.Snowing; } }

        void Update()
        {
            Far();
            bool ok = Allowed;
            if (ok && !wasDay) nextAt = Time.time + Random.Range(1.5f, 4f);   // наступил день (новый матч) — первая почти сразу, ещё в заставке
            if (!ok && wasDay) foreach (AudioSource v in voices) if (v != null) v.Stop();
            wasDay = ok;
            if (!ok || voices == null || Time.time < nextAt) return;
            nextAt = Time.time + Random.Range(minGap, maxGap);
            if (Weather.Instance.Dawn)
            {
                // рассвет: хигураси — «кана-кана-кана», печально, по одной, иногда отвечает вторая издалека
                Sing(voices[0], higurashi, Random.Range(-0.8f, 0.8f), 0f);
                if (Random.value < 0.4f) Sing(voices[1], higurashi, Random.Range(-0.8f, 0.8f), Random.Range(2.5f, 5f), 0.6f);
                return;
            }
            bool first = Random.value < 0.6f;
            Sing(voices[0], first ? abura : minmin, Random.Range(-0.7f, 0.7f), 0f);
            if (Random.value < 0.3f) Sing(voices[1], first ? minmin : abura, Random.Range(-0.7f, 0.7f), Random.Range(1.5f, 4f));   // подхватила вторая
        }

        void Sing(AudioSource v, AudioClip c, float pan, float delay, float vol = 1f)
        {
            if (v == null || c == null) return;
            v.clip = c; v.panStereo = pan; v.pitch = Random.Range(0.95f, 1.05f);
            v.volume = Random.Range(0.26f, 0.38f) * vol;   // на динамике телефона тише 0,2 — тонет под музыкой и ветром
            v.PlayDelayed(delay);
        }

        const int Rate = 22050;

        /// <summary>
        /// Нормировка с мягким насыщением (tanh): шумовой треск с высоким пик-фактором по пику выходит тихим (RMS ~0,18) —
        /// насыщение поднимает среднюю громкость в 1,7–2 раза и добавляет цикаде характерной «жёсткости» звука.
        /// </summary>
        static void Loud(float[] d, float peak, float drive)
        {
            float k = 1f / (float)System.Math.Tanh(drive);
            for (int i = 0; i < d.Length; i++) d[i] = 0.85f * k * (float)System.Math.Tanh(d[i] / peak * drive);
        }

        /// <summary>Эхо от леса и даль: две задержанные тихие копии и мягкий срез верхов (далёкий голос тусклее).</summary>
        static void Distance(float[] d, float lp)
        {
            int d1 = (int)(Rate * 0.21f), d2 = (int)(Rate * 0.47f);
            for (int i = d.Length - 1; i >= 0; i--)
                d[i] += (i >= d1 ? d[i - d1] * 0.26f : 0f) + (i >= d2 ? d[i - d2] * 0.12f : 0f);
            float y = 0f, a = 1f - Mathf.Exp(-2f * Mathf.PI * lp / Rate);
            for (int i = 0; i < d.Length; i++) { y += a * (d[i] - y); d[i] = y; }
        }

        /// <summary>
        /// Коршун тоби: протяжный свист «пи-и-и» ~3 кГц (чуть вверх и назад) и, после короткой паузы, трель «хйороро» —
        /// пять нисходящих переливов, каждый с быстрым спадом высоты; общий спад громкости. Эхо и срез верхов — птица далеко над рощей.
        /// </summary>
        static AudioClip Tobi()
        {
            const float T = 3.0f;
            int n = (int)(Rate * T);
            float[] d = new float[n];
            float ph = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, f = 0f, a = 0f;
                if (t < 0.8f)
                {
                    f = t < 0.18f ? Mathf.Lerp(2700f, 3150f, t / 0.18f) : Mathf.Lerp(3150f, 2950f, (t - 0.18f) / 0.62f);
                    a = Mathf.Min(1f, t / 0.06f) * Mathf.Min(1f, (0.8f - t) / 0.12f);
                }
                else if (t > 0.9f && t < 2.2f)
                {
                    float u = (t - 0.9f) / 1.3f;
                    float w = (t - 0.9f) / 0.26f % 1f;                   // переливы по 0,26 с
                    float top = Mathf.Lerp(2950f, 2150f, u);
                    f = top - 520f * Mathf.Pow(w, 0.7f);
                    a = Mathf.Sin(Mathf.PI * w) * (1f - 0.55f * u);
                }
                f *= 1f + 0.006f * Mathf.Sin(t * 2f * Mathf.PI * 9f);
                ph += 2f * Mathf.PI * f / Rate;
                float v = (Mathf.Sin(ph) + 0.08f * Mathf.Sin(2f * ph)) * a;
                d[i] = v;
            }
            Distance(d, 4200f);
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
            AudioClip c = AudioClip.Create("bird_tobi", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>
        /// Неясыть (фукуро): низкие глухие «у-у» ~400 Гц — долгое, два коротких, долгое с понижением; к тону подмешан
        /// дыхательный шум в той же полосе. Эхо от леса.
        /// </summary>
        static AudioClip Owl()
        {
            const float T = 3.2f;
            int n = (int)(Rate * T);
            float[] d = new float[n];
            System.Random r = new System.Random(77);
            float[] st = { 0.0f, 0.95f, 1.32f, 1.78f }, len = { 0.48f, 0.24f, 0.26f, 0.62f }, f0 = { 430f, 455f, 445f, 425f }, f1 = { 405f, 440f, 430f, 375f };
            float ph = 0f, lo = 0f, band = 0f, peak = 1e-4f;
            float fn = 2f * Mathf.Sin(Mathf.PI * 420f / Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, f = 420f, a = 0f;
                for (int k = 0; k < st.Length; k++)
                {
                    float u = (t - st[k]) / len[k];
                    if (u < 0f || u > 1f) continue;
                    f = Mathf.Lerp(f0[k], f1[k], u);
                    a = Mathf.Min(1f, u * len[k] / 0.07f) * Mathf.Pow(Mathf.Min(1f, (1f - u) * len[k] / 0.16f), 1.5f);
                }
                ph += 2f * Mathf.PI * f / Rate;
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                lo += fn * band; float hi = x - lo - 0.18f * band; band += fn * hi;
                d[i] = (Mathf.Sin(ph) + 0.12f * Mathf.Sin(2f * ph) + band * 0.06f) * a;
            }
            Distance(d, 2500f);
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            for (int i = 0; i < n; i++) d[i] *= 0.85f / peak;
            AudioClip c = AudioClip.Create("bird_owl", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>Абурадзэми: шум, отфильтрованный резонансом ~5 кГц, с грубой пульсацией ~90 Гц и дрожью; нарастание 1,5 с, хвост 2 с.</summary>
        static AudioClip Abura()
        {
            int n = (int)(Rate * 9f);
            float[] d = new float[n];
            System.Random r = new System.Random(51);
            float lo = 0f, band = 0f, peak = 1e-4f, ph = 0f;
            float fc = 5000f, q = 0.32f;
            float f = 2f * Mathf.Sin(Mathf.PI * fc / Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                lo += f * band; float hi = x - lo - q * band; band += f * hi;
                ph += 2f * Mathf.PI * (88f + 6f * Mathf.Sin(t * 1.7f)) / Rate;
                float pulse = Mathf.Pow(Mathf.Abs(Mathf.Sin(ph)), 0.6f);
                float trem = 0.8f + 0.2f * Mathf.Sin(t * 2f * Mathf.PI * 5.5f);
                float env = Mathf.SmoothStep(0f, 1f, t / 1.5f) * Mathf.SmoothStep(0f, 1f, (9f - t) / 2.2f);
                float v = band * (0.4f + 0.6f * pulse) * trem * env;
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            Loud(d, peak, 2.6f);
            AudioClip c = AudioClip.Create("cicada_abura", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>
        /// Хигураси (вечерняя/рассветная цикада): серия звонких «ка-на» ~4,5 кГц, трель ~12 в секунду с мягкой атакой;
        /// за 4 с тон медленно сползает вниз, громкость тает — характерная печальная «кана-кана-кана».
        /// </summary>
        static AudioClip Higurashi()
        {
            const float T = 4.6f;
            int n = (int)(Rate * T);
            float[] d = new float[n];
            float ph = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float u = t / T;
                float f = Mathf.Lerp(4700f, 3900f, Mathf.Pow(u, 0.8f)) * (1f + 0.01f * Mathf.Sin(t * 2f * Mathf.PI * 6f));
                ph += 2f * Mathf.PI * f / Rate;
                float rate = Mathf.Lerp(13f, 10.5f, u);   // трель чуть замедляется
                float cyc = (t * rate) % 1f;
                float syl = cyc < 0.62f ? Mathf.Sin(Mathf.PI * cyc / 0.62f) : 0f;
                syl = Mathf.Pow(syl, 0.7f) * ((int)(t * rate) % 2 == 0 ? 1f : 0.82f);   // «ка» громче «на»
                float env = Mathf.SmoothStep(0f, 1f, t / 0.25f) * Mathf.Pow(1f - u, 1.4f);
                float tone = Mathf.Sin(ph) + 0.2f * Mathf.Sin(2f * ph);
                float v = tone * syl * env;
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            Loud(d, peak, 1.6f);
            AudioClip c = AudioClip.Create("cicada_higurashi", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>Минминдзэми: тон 3–4 кГц с жужжащей модуляцией ~180 Гц — «ми-и-ин», 9–11 коротких «мин», протяжное «ми-и-и» на спаде.</summary>
        static AudioClip Minmin()
        {
            System.Random r = new System.Random(57);
            int pulses = 10;
            float tIntro = 0.9f, tPulse = 0.28f, tOut = 1.4f;
            float total = tIntro + pulses * tPulse + tOut;
            int n = (int)(Rate * total);
            float[] d = new float[n];
            float ph = 0f, bz = 0f, lo = 0f, band = 0f, peak = 1e-4f;
            float fn = 2f * Mathf.Sin(Mathf.PI * 3600f / Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, f, a;
                if (t < tIntro)
                {
                    float u = t / tIntro;
                    f = Mathf.Lerp(3000f, 3750f, u); a = Mathf.SmoothStep(0f, 1f, u) * (0.75f + 0.25f * u);
                }
                else if (t < tIntro + pulses * tPulse)
                {
                    float u = (t - tIntro) % tPulse / tPulse;
                    f = Mathf.Lerp(3350f, 3900f, Mathf.Min(1f, u * 2.5f)) - 250f * Mathf.Max(0f, u - 0.5f);
                    a = Mathf.Sin(Mathf.PI * Mathf.Min(1f, u / 0.75f)) * 0.95f;
                }
                else
                {
                    float u = (t - tIntro - pulses * tPulse) / tOut;
                    f = Mathf.Lerp(3800f, 3100f, u); a = (1f - u) * (1f - u) * 0.9f;
                }
                ph += 2f * Mathf.PI * f / Rate;
                bz += 2f * Mathf.PI * 180f / Rate;
                float buzz = 0.45f + 0.55f * Mathf.Clamp01(Mathf.Sin(bz) * 3f);
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                lo += fn * band; float hi = x - lo - 0.25f * band; band += fn * hi;
                float tone = Mathf.Sin(ph) + 0.35f * Mathf.Sin(2f * ph) + 0.15f * Mathf.Sin(3f * ph);
                float v = (tone * buzz + band * 0.25f) * a;
                v *= Mathf.Min(1f, t * 200f) * Mathf.Min(1f, (total - t) * 20f);
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            Loud(d, peak, 2f);
            AudioClip c = AudioClip.Create("cicada_minmin", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
