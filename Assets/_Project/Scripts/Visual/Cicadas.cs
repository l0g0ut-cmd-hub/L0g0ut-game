using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Цикады летнего дня (семи): только днём и в ясную погоду (в дождь, снег и ночью молчат — ночью сверчки, Juice).
    /// Не постоянный фон, а редкие «выходы»: раз в 22–50 с то сухой шипящий треск абурадзэми («дзи-и-и-и», как масло на сковороде),
    /// то певучая минминдзэми («ми-и-ин, мин-мин-мин… ми-и-и»); иногда вторая подхватывает с другой стороны.
    /// Голоса синтезированы (шум в полосе 4–6 кГц с быстрой пульсацией; тон 3–4 кГц с жужжащей модуляцией), стерео-панорама.
    /// </summary>
    public class Cicadas : MonoBehaviour
    {
        public float minGap = 22f, maxGap = 50f;
        AudioSource[] voices;
        static AudioClip abura, minmin, higurashi;
        float nextAt = -1f;
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
        }

        bool Allowed { get { Weather w = Weather.Instance; return w != null && w.Daytime && !w.Raining && !w.Snowing; } }

        void Update()
        {
            bool ok = Allowed;
            if (ok && !wasDay) nextAt = Time.time + Random.Range(3f, 8f);   // наступил день (новый матч) — скоро первая
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
            v.volume = Random.Range(0.05f, 0.1f) * vol;
            v.PlayDelayed(delay);
        }

        const int Rate = 22050;

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
            for (int i = 0; i < n; i++) d[i] *= 0.85f / peak;
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
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
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
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
            AudioClip c = AudioClip.Create("cicada_minmin", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
