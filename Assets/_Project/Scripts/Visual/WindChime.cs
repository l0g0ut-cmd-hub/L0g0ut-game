using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Фурин под карнизом храма: бумажная полоска качается на ветру, порыв — и колокольчик звенит (чугунный «нанбу»:
    /// высокие негармоничные моды с долгим хвостом и биениями). Звон редкий, тихий — фон ночи.
    /// </summary>
    public class WindChime : MonoBehaviour
    {
        public Transform strip;    // бумажная полоска (танзаку) — качается
        public float minGap = 7f, maxGap = 16f;
        float nextAt, gust, lastWind, lastRing = -10f;
        AudioSource src;
        static AudioClip ring;

        void Start()
        {
            src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0.14f;
            if (ring == null) ring = Ring();
            nextAt = Time.time + Random.Range(2f, 8f);
        }

        void Update()
        {
            // порыв ветра (WindGusts) — колокольчик звенит на подъёме порыва
            float ws = WindGusts.Strength;
            if (ws > 0.55f && lastWind <= 0.55f && Time.time > lastRing + 2.5f) nextAt = Time.time;
            lastWind = ws;
            gust = Mathf.Max(gust, ws);
            if (Time.time >= nextAt)
            {
                lastRing = Time.time;
                nextAt = Time.time + Random.Range(minGap, maxGap);
                gust = 1f;
                bool rain = Weather.Instance != null && Weather.Instance.Raining;
                if (src != null && ring != null) { src.pitch = Random.Range(0.97f, 1.03f); src.PlayOneShot(ring, rain ? 0.6f : 1f); }
            }
            gust = Mathf.MoveTowards(gust, 0f, Time.deltaTime * 0.35f);
            if (strip != null)
            {
                float t = Time.time;
                float a = Mathf.Sin(t * 1.7f) * 6f + Mathf.Sin(t * 3.1f + 1f) * 2f + gust * 18f * Mathf.Sin(t * 5.5f);
                strip.localRotation = Quaternion.Euler(a * 0.4f, 0f, a);
            }
        }

        static AudioClip Ring()
        {
            const int rate = 22050;
            int n = (int)(rate * 2.4f);
            float[] d = new float[n];
            float[] f = { 2630f, 3910f, 5420f, 7180f };
            float[] dec = { 1.6f, 2.4f, 3.6f, 5.5f };
            float[] amp = { 1f, 0.6f, 0.35f, 0.2f };
            float peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, v = 0f;
                for (int k = 0; k < f.Length; k++)
                    v += amp[k] * Mathf.Exp(-t * dec[k]) * 0.5f * (Mathf.Sin(2f * Mathf.PI * (f[k] - 2f) * t) + Mathf.Sin(2f * Mathf.PI * (f[k] + 2f) * t + 0.4f));
                // второй лёгкий удар язычка
                float t2 = t - 0.23f;
                if (t2 > 0f) for (int k = 0; k < 2; k++) v += 0.4f * amp[k] * Mathf.Exp(-t2 * dec[k] * 1.3f) * Mathf.Sin(2f * Mathf.PI * f[k] * 1.003f * t2);
                v *= Mathf.Min(1f, t * 3000f);
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
            AudioClip c = AudioClip.Create("furin", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
