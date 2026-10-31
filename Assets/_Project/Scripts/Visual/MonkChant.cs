using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Ночная служба в храме: сидящий за сёдзи монах читает сутру — глухо, сквозь бумагу и стены. Ровный стук мокугё
    /// (деревянная «рыба»: полый тук-тук на каждый слог) и монотонное пение двумя голосами «на-му-а-ми-да-бу-цу»
    /// на одной ноте с опусканием в конце строки. Сеансы по 30–70 с с паузами в минуту-две; только когда в храме горит
    /// свет (ночь, рассвет, закат). Петля синтезируется в фоне при запуске (формантный голос + резонатор дерева).
    /// </summary>
    public class MonkChant : MonoBehaviour
    {
        AudioSource src;
        static AudioClip loop;
        static float[] loopData; static volatile bool ready; static bool started;
        float sessionEnd = -1f, nextAt, vol;
        const int Rate = 22050;
        const float Beat = 0.56f;   // ~107 ударов в минуту
        const int Beats = 16;

        void Start()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false; src.loop = true; src.volume = 0f;
            src.spatialBlend = 0.65f; src.minDistance = 4f; src.maxDistance = 30f; src.rolloffMode = AudioRolloffMode.Linear;
            AudioLowPassFilter lp = gameObject.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 1900f;   // сквозь бумагу и дерево
            if (loop == null && !started) { started = true; System.Threading.ThreadPool.QueueUserWorkItem(_ => { loopData = Synth(); ready = true; }); }
            nextAt = Time.time + Random.Range(12f, 30f);
        }

        void Update()
        {
            if (loop == null && ready && loopData != null)
            {
                loop = AudioClip.Create("monk_chant", loopData.Length, 1, Rate, false);
                loop.SetData(loopData, 0); loopData = null;
            }
            if (loop == null || src == null) return;
            DayNight dn = DayNight.Instance;
            bool lit = dn == null || !dn.Day || dn.Dawn || dn.Dusk;
            float now = Time.time;
            if (sessionEnd < 0f && lit && now >= nextAt)
            {
                sessionEnd = now + Random.Range(30f, 70f);
                src.clip = loop; src.time = 0f; src.Play();
            }
            bool on = sessionEnd > 0f && now < sessionEnd && lit;
            vol = Mathf.MoveTowards(vol, on ? 0.16f : 0f, Time.deltaTime * (on ? 0.08f : 0.05f));
            src.volume = vol;
            if (sessionEnd > 0f && !on && vol <= 0.001f)
            {
                src.Stop(); sessionEnd = -1f; nextAt = now + Random.Range(60f, 140f);
            }
        }

        // гласные: F1, F2, F3 (мужской голос)
        static readonly float[,] Vow = { { 730f, 1090f, 2440f }, { 300f, 870f, 2240f }, { 270f, 2290f, 3010f } };   // а, у, и
        // строка «на-му-а-ми-да-бу-цу» ×2 и две паузы: гласная (−1 — тишина), ступень высоты
        static readonly int[] Syl = { 0, 1, 0, 2, 0, 1, 1, 0, 1, 0, 2, 0, 1, 1, -1, -1 };
        static readonly float[] Step = { 1f, 1f, 1f, 1f, 1f, 1f, 0.89f, 1f, 1f, 1f, 1f, 1f, 1f, 0.84f, 1f, 1f };

        /// <summary>Петля 16 долей: два голоса (105 и 98 Гц, дрожание) через три форманты + мокугё на каждую долю.</summary>
        public static float[] Synth()
        {
            int per = (int)(Rate * Beat), n = per * Beats;
            float[] d = new float[n];
            System.Random rnd = new System.Random(108);
            float[] f0s = { 105f, 98.5f };
            for (int v = 0; v < 2; v++)
            {
                float ph = 0f, prevG = 0f, jit = 0f;
                float[] y1 = new float[3], y2 = new float[3];
                float f1 = Vow[0, 0], f2 = Vow[0, 1], f3 = Vow[0, 2];
                float[] ff = new float[3], bw = { 90f, 110f, 160f }, gk = { 1f, 0.45f, 0.18f };
                for (int i = 0; i < n; i++)
                {
                    int b = i / per; float u = (i - b * per) / (float)per;
                    int vw = Syl[b];
                    // формантная цель — гласная слога, переход плавный (сглаживание ~30 мс)
                    int tv = vw < 0 ? 1 : vw;
                    float a = 1f - Mathf.Exp(-1f / (Rate * 0.03f));
                    f1 += (Vow[tv, 0] - f1) * a; f2 += (Vow[tv, 1] - f2) * a; f3 += (Vow[tv, 2] - f3) * a;
                    // согласная в начале слога — короткий провал громкости; в паузах тишина
                    float env = vw < 0 ? 0f : Mathf.SmoothStep(0f, 1f, (u - 0.06f) / 0.1f) * Mathf.SmoothStep(0f, 1f, (1.02f - u) / 0.12f);
                    if (vw >= 0 && b + 1 < Beats && Syl[b + 1] >= 0) env = Mathf.Max(env, 0.35f * Mathf.SmoothStep(0f, 1f, (u - 0.5f) / 0.5f));   // слоги сливаются
                    if ((i & 127) == 0) jit = ((float)rnd.NextDouble() - 0.5f) * 0.012f;
                    float f0 = f0s[v] * Step[b] * (1f + jit + 0.004f * Mathf.Sin(i * 2f * Mathf.PI * 5.1f / Rate + v));
                    ph += f0 / Rate; if (ph >= 1f) ph -= 1f;
                    float g = ph < 0.6f ? 0.5f * (1f - Mathf.Cos(Mathf.PI * ph / 0.6f)) : Mathf.Max(0f, Mathf.Cos(Mathf.PI * (ph - 0.6f) / 0.5f));
                    float exc = (g - prevG) * 25f; prevG = g;
                    exc += ((float)rnd.NextDouble() * 2f - 1f) * 0.04f;   // придых
                    float sum = 0f;
                    ff[0] = f1; ff[1] = f2; ff[2] = f3;
                    for (int k = 0; k < 3; k++)
                    {
                        float r = Mathf.Exp(-Mathf.PI * bw[k] / Rate), w = 2f * Mathf.PI * ff[k] / Rate;
                        float y = (1f - r) * exc + 2f * r * Mathf.Cos(w) * y1[k] - r * r * y2[k];
                        y2[k] = y1[k]; y1[k] = y; sum += y * gk[k];
                    }
                    d[i] += sum * env * (v == 0 ? 1f : 0.8f);
                }
            }
            float peak = 1e-4f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            for (int i = 0; i < n; i++) d[i] *= 0.55f / peak;
            // мокугё: полый деревянный удар — две моды (380 и 1020 Гц) и щелчок, на каждую долю (в паузах тоже)
            for (int b = 0; b < Beats; b++)
            {
                int s0 = b * per;
                float acc = b % 8 == 0 ? 1f : 0.8f;
                for (int j = 0; j < (int)(Rate * 0.3f) && s0 + j < n; j++)
                {
                    float t = j / (float)Rate;
                    float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    float k = Mathf.Sin(2f * Mathf.PI * 380f * t) * Mathf.Exp(-t * 26f) + 0.35f * Mathf.Sin(2f * Mathf.PI * 1020f * t) * Mathf.Exp(-t * 55f) + x * Mathf.Exp(-t * 500f) * 0.5f;
                    d[s0 + j] += k * 0.42f * acc * Mathf.Min(1f, t * 3000f);
                }
            }
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i], -1f, 1f);
            return d;
        }
    }
}
