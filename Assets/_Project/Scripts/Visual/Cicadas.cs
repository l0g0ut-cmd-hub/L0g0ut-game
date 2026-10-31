using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Летний хор цикад (как в «Евангелионе»): в солнечный день над двором стоит сплошное стереофоническое «дзи-и-и»
    /// абурадзэми, сквозь которое волнами пробиваются ритмичные «ми-ин, мин-мин-мин» минминдзэми; громкость хора
    /// медленно дышит. Раз в 25–45 с поверх — ближняя минминдзэми с одной стороны. На рассвете — только хигураси
    /// («кана-кана-кана»), в дождь, снег и ночью цикады молчат (ночью — сверчки, Juice).
    /// Синтез по устройству насекомого, а не тоном (тон звучал птицей): цикада трещит пластинками-тимбалами — сотни
    /// знакопеременных щелчков в секунду, каждый звенит в резонаторе брюшка 3,5–6 кГц. Хор — 10 абурадзэми и 6 минминдзэми
    /// с разной высотой, громкостью (дальностью) и панорамой, петля 12 с без шва.
    /// Дальний план (раз в 40–75 с, в ясную погоду): днём над рощей кричит коршун тоби, ночью — неясыть; тихо, с эхом.
    /// </summary>
    public class Cicadas : MonoBehaviour
    {
        public float minGap = 25f, maxGap = 45f;
        public float chorusVolume = 0.3f;
        AudioSource chorus, near, dawnSrc, far;
        static AudioClip chorusClip, nearClip, higurashi, tobi, owl;
        static float[] chorusData;            // хор считается в фоновом потоке (~0,5 с) — без рывка при загрузке арены
        static volatile bool chorusReady;
        static bool chorusStarted;
        float nextAt = -1f, farAt, chorusK, swellSeed;
        bool wasOn;

        void Start()
        {
            chorus = Src(true); near = Src(false); dawnSrc = Src(false); far = Src(false);
            if (chorusClip == null && !chorusStarted)
            {
                chorusStarted = true;
                System.Threading.ThreadPool.QueueUserWorkItem(_ => { chorusData = ChorusData(); chorusReady = true; });
            }
            if (nearClip == null) nearClip = NearMinmin();
            if (higurashi == null) higurashi = Higurashi();
            if (tobi == null) tobi = Tobi();
            if (owl == null) owl = Owl();
            chorus.clip = chorusClip; chorus.volume = 0f;
            swellSeed = Random.Range(0f, 100f);
            farAt = Time.time + Random.Range(10f, 22f);
        }

        AudioSource Src(bool loop)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0f; s.loop = loop;
            return s;
        }

        void Update()
        {
            Far();
            if (chorusClip == null && chorusReady && chorusData != null)
            {
                chorusClip = AudioClip.Create("cicada_chorus", chorusData.Length / 2, 2, Rate, false);
                chorusClip.SetData(chorusData, 0);
                chorusData = null;
            }
            if (chorus != null && chorus.clip == null && chorusClip != null) chorus.clip = chorusClip;
            Weather w = Weather.Instance;
            bool clear = w != null && w.Daytime && !w.Raining && !w.Snowing;
            bool summer = clear && !w.Dawn;   // хор — только в солнечный день
            // хор: плавно вступает с новым днём (ещё в заставке) и стихает в непогоду; громкость медленно «дышит»
            chorusK = Mathf.MoveTowards(chorusK, summer ? 1f : 0f, Time.unscaledDeltaTime / (summer ? 2.5f : 1f));
            if (chorus != null)
            {
                if (chorusK > 0.001f && !chorus.isPlaying && chorus.clip != null) { chorus.time = Random.Range(0f, 11f); chorus.Play(); }
                if (chorusK <= 0.001f && chorus.isPlaying) chorus.Stop();
                float swell = 0.62f + 0.38f * Mathf.PerlinNoise(swellSeed, Time.time * 0.11f);
                chorus.volume = chorusVolume * chorusK * swell;
            }
            bool on = clear;
            if (on && !wasOn) nextAt = Time.time + Random.Range(4f, 9f);
            if (!on && wasOn) { if (near != null) near.Stop(); if (dawnSrc != null) dawnSrc.Stop(); }
            wasOn = on;
            if (!on || Time.time < nextAt) return;
            if (w.Dawn)
            {
                // рассвет: хигураси — по одной, печально; иногда отвечает вторая издалека
                nextAt = Time.time + Random.Range(10f, 22f);
                Play(dawnSrc, higurashi, Random.Range(-0.8f, 0.8f), Random.Range(0.3f, 0.42f), 0f);
                if (Random.value < 0.4f) Play(near, higurashi, Random.Range(-0.8f, 0.8f), Random.Range(0.14f, 0.2f), Random.Range(2.5f, 5f));
                return;
            }
            nextAt = Time.time + Random.Range(minGap, maxGap);
            Play(near, nearClip, Random.Range(-0.75f, 0.75f), Random.Range(0.3f, 0.42f), 0f);   // ближняя минминдзэми
        }

        static void Play(AudioSource s, AudioClip c, float pan, float vol, float delay)
        {
            if (s == null || c == null) return;
            s.clip = c; s.panStereo = pan; s.pitch = Random.Range(0.96f, 1.04f); s.volume = vol;
            s.PlayDelayed(delay);
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

        const int Rate = 22050;
        const float Loop = 12f;

        // ---------- синтез цикад: щелчки тимбала → резонатор ----------

        /// <summary>
        /// Гладкая периодическая (период — петля) кривая 0…1: сумма гармоник со случайными фазами. Фазы — из общего генератора
        /// (System.Random с соседними зёрнами выдаёт почти одинаковые первые числа — все голоса вступали разом). Считается
        /// раз в 32 сэмпла с линейной интерполяцией: огибающая медленная, а синусов на весь хор иначе — десятки миллионов.
        /// </summary>
        static float[] Periodic(int n, System.Random r, int kmax)
        {
            float[] ph = new float[kmax];
            for (int k = 0; k < kmax; k++) ph[k] = (float)r.NextDouble() * 6.2832f;
            float tot = 0f;
            for (int k = 1; k <= kmax; k++) tot += 1f / k;
            const int step = 32;
            int cn = n / step + 2;
            float[] coarse = new float[cn];
            for (int j = 0; j < cn; j++)
            {
                float a = 0f, t = j * step / (float)Rate;
                for (int k = 1; k <= kmax; k++) a += Mathf.Sin(6.2832f * k * t / Loop + ph[k - 1]) / k;
                coarse[j] = a / tot * 0.5f + 0.5f;
            }
            float[] o = new float[n];
            for (int i = 0; i < n; i++) { int j = i / step; float f = (i - j * step) / (float)step; o[i] = coarse[j] + (coarse[j + 1] - coarse[j]) * f; }
            return o;
        }

        /// <summary>
        /// Щелчки тимбала: знакопеременные импульсы (выгиб — прогиб пластинки) с частотой rate ± jitter, сила — огибающая env
        /// (индекс по кругу, если loop). Пишет в x (длина может быть больше env — хвост за петлёй).
        /// </summary>
        static void Clicks(float[] x, float[] env, float rate, float jitter, bool loop, System.Random r)
        {
            int n = env.Length;
            float t = (float)r.NextDouble() / rate, s = 1f, end = (x.Length - 1) / (float)Rate;
            while (t < end)
            {
                int i = (int)(t * Rate);
                if (i >= x.Length) break;
                float e = loop ? env[i % n] : (i < n ? env[i] : 0f);
                if (e > 0.002f) x[i] += s * e * (0.6f + 0.4f * (float)r.NextDouble());
                s = -s;
                t += 1f / (rate * (1f + jitter * ((float)r.NextDouble() * 2f - 1f)));
            }
        }

        /// <summary>Двухполюсный резонатор (брюшко-резонатор цикады); f — постоянная или по сэмплам (fv). Прибавляет в y с усилением g.</summary>
        static void Resonate(float[] x, float[] y, float f, float[] fv, float bw, float g)
        {
            float rr = Mathf.Exp(-Mathf.PI * bw / Rate), a2 = -rr * rr, norm = (1f - rr) * g;
            float a1c = 2f * rr * Mathf.Cos(2f * Mathf.PI * f / Rate);
            float y1 = 0f, y2 = 0f;
            for (int i = 0; i < x.Length; i++)
            {
                float a1 = fv != null ? 2f * rr * Mathf.Cos(2f * Mathf.PI * fv[i] / Rate) : a1c;
                float v = x[i] + a1 * y1 + a2 * y2;
                y2 = y1; y1 = v;
                y[i] += v * norm;
            }
        }

        /// <summary>
        /// Фраза минминдзэми с начала t0: «ми-и-ин» (0,9 с, высота резонанса растёт), 9 пульсов «мин» по 0,27 с
        /// (в каждом высота взлетает и чуть оседает), протяжное «ми-и-и» на спаде (1,3 с). loop — время по кругу петли.
        /// Пишет максимум огибающей в env и частоту резонанса в f.
        /// </summary>
        static void MinminPhrase(float t0, float[] env, float[] f, bool loop, float pitch)
        {
            const float intro = 0.9f, tp = 0.27f, outro = 1.3f; const int pulses = 9;
            for (int i = 0; i < env.Length; i++)
            {
                float u = i / (float)Rate - t0;
                if (loop) { u %= Loop; if (u < 0f) u += Loop; }
                float e = 0f, fr = 3400f;
                if (u < 0f) continue;
                if (u < intro) { e = Mathf.Pow(u / intro, 0.7f); fr = 3000f + 800f * u / intro; }
                else if (u < intro + pulses * tp)
                {
                    float w = (u - intro) % tp / tp;
                    e = Mathf.Sin(Mathf.PI * Mathf.Clamp01(w / 0.72f)) * 0.95f;
                    fr = 3300f + 700f * Mathf.Clamp01(w * 2.2f) - 300f * Mathf.Clamp01(w - 0.5f);
                }
                else if (u < intro + pulses * tp + outro)
                {
                    float q = (u - intro - pulses * tp) / outro;
                    e = (1f - q) * (1f - q); fr = 3800f - 700f * q;
                }
                if (e > env[i]) { env[i] = e; f[i] = fr * pitch; }
            }
        }

        /// <summary>Нормировка по средней громкости и мягкое насыщение (жёсткость треска, без перегруза).</summary>
        static void Finish(float[] d, float rmsTarget, float drive)
        {
            double acc = 0;
            for (int i = 0; i < d.Length; i++) acc += d[i] * d[i];
            float rms = Mathf.Max(1e-6f, Mathf.Sqrt((float)(acc / d.Length)));
            float k = rmsTarget / rms, th = (float)System.Math.Tanh(drive);
            for (int i = 0; i < d.Length; i++) d[i] = 0.9f * (float)System.Math.Tanh(d[i] * k * drive) / th;
        }

        /// <summary>Стереохор (чередование L/R): 10 абурадзэми (сплошной треск с провалами и дрожью) + 6 минминдзэми (по две фразы за петлю).</summary>
        static float[] ChorusData()
        {
            int n = (int)(Rate * Loop), tail = 2000, m = n + tail;
            float[] L = new float[m], R = new float[m], x = new float[m], y = new float[m];
            System.Random r = new System.Random(7);
            System.Action<float, float> Mix = (pan, g) =>
            {
                float a = (pan + 1f) * Mathf.PI * 0.25f, gl = Mathf.Cos(a) * g, gr = Mathf.Sin(a) * g;
                for (int i = 0; i < m; i++) { L[i] += y[i] * gl; R[i] += y[i] * gr; }
            };
            // абурадзэми
            for (int v = 0; v < 10; v++)
            {
                float f0 = Mathf.Lerp(4000f, 5600f, (float)r.NextDouble()), bw = Mathf.Lerp(350f, 700f, (float)r.NextDouble());
                float rate = Mathf.Lerp(150f, 230f, (float)r.NextDouble());
                float[] env = Periodic(n, r, 5);
                float fm = Mathf.Round(Mathf.Lerp(5f, 9f, (float)r.NextDouble()) * Loop) / Loop, fph = (float)r.NextDouble() * 6.28f;
                for (int i = 0; i < n; i++)
                {
                    float on = Mathf.Clamp01((env[i] - 0.28f) / 0.18f); on = on * on * (3f - 2f * on);
                    env[i] = on * (1f - 0.3f * (0.5f + 0.5f * Mathf.Sin(6.2832f * fm * i / Rate + fph)));
                }
                System.Array.Clear(x, 0, m); System.Array.Clear(y, 0, m);
                Clicks(x, env, rate, 0.06f, true, r);
                for (int i = 0; i < m; i++) x[i] += ((float)r.NextDouble() * 2f - 1f) * 0.05f * env[i % n];   // шорох между щелчками
                Resonate(x, y, f0, null, bw, 1f);
                Resonate(x, y, f0 * 1.55f, null, bw * 1.4f, 0.2f);
                Mix(Mathf.Lerp(-0.9f, 0.9f, (float)r.NextDouble()), Mathf.Pow(Mathf.Lerp(0.25f, 1f, (float)r.NextDouble()), 1.5f));
            }
            // минминдзэми
            float[] me = new float[n], mf = new float[n];
            for (int v = 0; v < 6; v++)
            {
                System.Array.Clear(me, 0, n);
                float t0 = (float)r.NextDouble() * Loop, pitch = Mathf.Lerp(0.95f, 1.05f, (float)r.NextDouble());
                MinminPhrase(t0, me, mf, true, pitch);
                MinminPhrase((t0 + Mathf.Lerp(5.2f, 6.8f, (float)r.NextDouble())) % Loop, me, mf, true, pitch);
                float[] fv = new float[m];
                for (int i = 0; i < m; i++) fv[i] = mf[i % n];
                System.Array.Clear(x, 0, m); System.Array.Clear(y, 0, m);
                Clicks(x, me, 290f, 0.03f, true, r);
                Resonate(x, y, 0f, fv, 200f, 1f);
                Mix(Mathf.Lerp(-0.8f, 0.8f, (float)r.NextDouble()), Mathf.Lerp(0.5f, 0.9f, (float)r.NextDouble()));
            }
            // шов петли: звон за концом — в начало
            for (int i = 0; i < tail; i++) { L[i] += L[n + i]; R[i] += R[n + i]; }
            float[] st = new float[n * 2];
            for (int i = 0; i < n; i++) { st[2 * i] = L[i]; st[2 * i + 1] = R[i]; }
            Finish(st, 0.2f, 1.6f);
            return st;
        }

        /// <summary>Ближняя минминдзэми: одна фраза громко и чётко (уже полоса резонанса — звонче), с жужжащим треском.</summary>
        static AudioClip NearMinmin()
        {
            int n = (int)(Rate * 5.2f), m = n + 2000;
            float[] env = new float[n], f = new float[n], x = new float[m], y = new float[m], fv = new float[m];
            MinminPhrase(0.05f, env, f, false, 1f);
            for (int i = 0; i < m; i++) fv[i] = i < n ? f[i] : 3100f;
            System.Random r = new System.Random(57);
            Clicks(x, env, 300f, 0.03f, false, r);
            Resonate(x, y, 0f, fv, 170f, 1f);
            float[] d = new float[n];
            System.Array.Copy(y, d, n);
            for (int i = 0; i < n; i++) d[i] *= Mathf.Min(1f, (n - i) / (Rate * 0.05f));
            Finish(d, 0.22f, 1.4f);
            AudioClip c = AudioClip.Create("cicada_minmin_near", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>
        /// Хигураси (рассвет): звонкая трель «ке-на-ке-на» — пачки щелчков ~12 в секунду через узкий резонатор ~4,6 кГц
        /// (звенит колокольчиком); за 4,5 с высота сползает к 3,9 кГц, трель замедляется, громкость тает.
        /// </summary>
        static AudioClip Higurashi()
        {
            const float T = 4.8f;
            int n = (int)(Rate * T), m = n + 2000;
            float[] env = new float[n], x = new float[m], y = new float[m], fv = new float[m];
            float acc = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, u = t / T;
                float rate = Mathf.Lerp(13f, 10.5f, u);
                acc += rate / Rate;
                float cyc = acc % 1f;
                float syl = cyc < 0.55f ? Mathf.Sin(Mathf.PI * cyc / 0.55f) : 0f;
                syl *= ((int)acc % 2 == 0) ? 1f : 0.8f;   // «ке» громче «на»
                env[i] = syl * Mathf.SmoothStep(0f, 1f, t / 0.25f) * Mathf.Pow(1f - u, 1.3f);
                fv[i] = Mathf.Lerp(4700f, 3900f, Mathf.Pow(u, 0.8f)) * ((int)acc % 2 == 0 ? 1f : 0.96f);
            }
            for (int i = n; i < m; i++) fv[i] = 3900f;
            Clicks(x, env, 460f, 0.02f, false, new System.Random(61));
            Resonate(x, y, 0f, fv, 110f, 1f);
            float[] d = new float[n];
            System.Array.Copy(y, d, n);
            Finish(d, 0.2f, 1.2f);
            AudioClip c = AudioClip.Create("cicada_higurashi", n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
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
    }
}
