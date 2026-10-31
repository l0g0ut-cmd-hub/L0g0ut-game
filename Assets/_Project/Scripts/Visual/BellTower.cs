using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Колокольня храма (сёро) за главным залом: раз в 70–130 с (ночью и на рассвете чаще, днём реже) бревно-било
    /// (сюмоку) на верёвках отводят назад и отпускают — оно бьёт в бронзовый колокол, тот глухо качается, и над двором
    /// плывёт долгий удар бонсё: низкий гул с негармоничными обертонами, биениями и хвостом секунд на двенадцать.
    /// </summary>
    public class BellTower : MonoBehaviour
    {
        public Transform bell, log;          // колокол (качается) и бревно (поворот вокруг точки подвеса)
        public float minGap = 70f, maxGap = 130f;
        AudioSource src;
        static AudioClip toll;
        static float[] tollData; static volatile bool tollReady; static bool tollStarted;   // синтез в фоне — без рывка при загрузке
        float nextAt, t = -1f, sway, swayV;
        Quaternion bell0, log0;
        const float Pull = 1.6f, Release = 0.22f;

        void Start()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0.35f; src.minDistance = 8f; src.maxDistance = 60f;
            if (toll == null && !tollStarted) { tollStarted = true; System.Threading.ThreadPool.QueueUserWorkItem(_ => { tollData = TollData(); tollReady = true; }); }
            if (bell != null) bell0 = bell.localRotation;
            if (log != null) log0 = log.localRotation;
            nextAt = Time.time + Random.Range(8f, 20f);   // первый удар — вскоре после начала
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (toll == null && tollReady && tollData != null)
            {
                toll = AudioClip.Create("temple_bell", tollData.Length, 1, 22050, false);
                toll.SetData(tollData, 0); tollData = null;
            }
            if (t < 0f && Time.time >= nextAt)
            {
                t = 0f;
                bool day = DayNight.Instance != null && DayNight.Instance.Day && !DayNight.Instance.Dawn && !DayNight.Instance.Dusk;   // вечерний колокол — на закате
                nextAt = Time.time + Random.Range(minGap, maxGap) * (day ? 1.6f : 1f);
            }
            float logAng = 0f;
            if (t >= 0f)
            {
                t += dt;
                if (t < Pull) logAng = -28f * Mathf.SmoothStep(0f, 1f, t / Pull);                       // отводят назад
                else if (t < Pull + Release) logAng = Mathf.Lerp(-28f, 2f, (t - Pull) / Release);       // отпустили — летит
                else
                {
                    if (t - dt < Pull + Release)
                    {
                        // удар: звук и толчок колоколу
                        if (src != null && toll != null) { src.pitch = Random.Range(0.97f, 1.02f); src.PlayOneShot(toll, 0.55f); }
                        swayV += 9f;
                    }
                    float k = t - Pull - Release;
                    logAng = 2f * Mathf.Exp(-k * 3f) * Mathf.Cos(k * 9f) - 6f * Mathf.Sin(Mathf.Min(1f, k * 2f) * Mathf.PI) * Mathf.Exp(-k * 1.5f);   // отскок и затухание
                    if (k > 3f) t = -1f;
                }
            }
            // колокол — тяжёлый маятник с вязким затуханием
            swayV += (-sway * 4f - swayV * 0.6f) * dt;
            sway += swayV * dt;
            if (bell != null) bell.localRotation = bell0 * Quaternion.Euler(0f, 0f, sway);
            if (log != null) log.localRotation = log0 * Quaternion.Euler(0f, 0f, logAng);
        }

        /// <summary>
        /// Удар бонсё: фундамент ~98 Гц и негармоничные обертоны (×0,5 «гул», ×2,0, 2,7, 3,6, 4,9, 6,3), у каждого — пара близких
        /// частот (биения «ва-а-ан»), высокие гаснут быстрее; глухой стук бревна в начале. 14 с, моно, 22 кГц.
        /// </summary>
        static float[] TollData()
        {
            const int rate = 22050;
            const float T = 14f, f0 = 98f;
            int n = (int)(rate * T);
            float[] d = new float[n];
            float[] mul = { 0.5f, 1f, 2.0f, 2.7f, 3.6f, 4.9f, 6.3f };
            float[] amp = { 0.55f, 1f, 0.55f, 0.42f, 0.3f, 0.2f, 0.12f };
            float[] dec = { 0.18f, 0.28f, 0.42f, 0.6f, 0.85f, 1.2f, 1.7f };
            float[] beat = { 0.6f, 1.1f, 1.6f, 2.1f, 2.7f, 3.3f, 3.9f };
            System.Random r = new System.Random(5);
            float lp = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, v = 0f;
                for (int k = 0; k < mul.Length; k++)
                {
                    float f = f0 * mul[k], e = amp[k] * Mathf.Exp(-t * dec[k]);
                    v += e * (Mathf.Sin(2f * Mathf.PI * f * t) + 0.7f * Mathf.Sin(2f * Mathf.PI * (f + beat[k]) * t + k));
                }
                // стук бревна: глухой шумовой удар первых 80 мс
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                lp += 0.08f * (x - lp);
                v += lp * 6f * Mathf.Exp(-t * 40f);
                v *= Mathf.Min(1f, t * 400f);
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.85f / peak;
            return d;
        }
    }
}
