using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Цикады солнечного дня (как в «Евангелионе»), но живыми периодами: одна запела — «дзи-и-и» абурадзэми или
    /// «ми-ин, мин-мин-мин… ми-и-и» минминдзэми, — иногда подхватила вторая, потом смолкли, пауза, снова. Изредка —
    /// долгое затишье. Одновременно не больше двух-трёх голосов, ближние громче, дальние тише, с разных сторон.
    /// На рассвете — хигураси («кана-кана-кана»); в дождь, снег и ночью цикады молчат (ночью — сверчки, Juice).
    /// Синтез по устройству насекомого (тон звучал птицей): цикада трещит пластинками-тимбалами — сотни знакопеременных
    /// щелчков в секунду, каждый звенит в резонаторе брюшка 3,5–6 кГц; у каждой песни — нарастание, дрожь и спад.
    /// Дальний план (раз в 40–75 с, в ясную погоду): днём над рощей кричит коршун тоби, ночью — неясыть; тихо, с эхом.
    /// </summary>
    public class Cicadas : MonoBehaviour
    {
        AudioSource[] voices;
        AudioSource far;
        static AudioClip[] abura, minmin;
        static AudioClip higurashi, tobi, owl, broom;
        AudioSource sweep; float sweepAt, strokeAt; int strokes;   // рассвет: за храмом монах метёт двор
        float nextAt = -1f, farAt;
        bool wasOn;
        int last = -1;
        bool answer;

        void Start()
        {
            voices = new AudioSource[3];
            for (int i = 0; i < voices.Length; i++) voices[i] = Src();
            far = Src();
            if (abura == null) abura = new[] { AburaCall(71, 7.5f, 4700f, 185f), AburaCall(72, 10f, 5100f, 205f), AburaCall(73, 5.5f, 4400f, 170f) };
            if (minmin == null) minmin = new[] { MinminCall(57, 9, 1f), MinminCall(58, 6, 1.04f), MinminCall(59, 11, 0.96f) };
            if (higurashi == null) higurashi = Higurashi();
            if (tobi == null) tobi = Tobi();
            if (owl == null) owl = Owl();
            if (broom == null) broom = Broom();
            sweep = Src(); sweep.panStereo = -0.55f;
            sweepAt = Time.time + Random.Range(6f, 15f);
            farAt = Time.time + Random.Range(10f, 22f);
        }

        AudioSource Src()
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.spatialBlend = 0f; s.loop = false;
            return s;
        }

        AudioSource FreeVoice()
        {
            foreach (AudioSource v in voices) if (v != null && !v.isPlaying) return v;
            return null;
        }

        void Update()
        {
            Far();
            Sweep();
            Weather w = Weather.Instance;
            bool on = w != null && w.Daytime && !w.Raining && !w.Snowing;
            if (on && !wasOn) nextAt = Time.time + Random.Range(1.5f, 4f);   // наступил день — первая запоёт почти сразу
            if (!on && wasOn) foreach (AudioSource v in voices) if (v != null) v.Stop();
            wasOn = on;
            if (!on || voices == null || Time.time < nextAt) return;
            AudioSource src = FreeVoice();
            if (src == null) { nextAt = Time.time + 1f; return; }
            if (w.Dawn || w.Dusk)
            {
                // рассвет и закат: хигураси («вечерняя цикада») — по одной, печально; иногда отвечает вторая издалека
                if (!Ambience.Claim(higurashi.length, 4f, 12f)) { nextAt = Time.time + Random.Range(1.5f, 3f); return; }   // общая очередь — без залпов
                bool farOne = Random.value < 0.45f;
                Play(src, higurashi, Random.Range(-0.8f, 0.8f), farOne ? Random.Range(0.14f, 0.2f) : Random.Range(0.3f, 0.4f));
                nextAt = Time.time + higurashi.length + Random.Range(5f, 14f);
                return;
            }
            if (answer)
            {
                answer = false;
                AudioClip ac = (Random.value < 0.5f ? abura : minmin)[Random.Range(0, 3)];
                Play(src, ac, Random.Range(-0.85f, 0.85f), Random.Range(0.1f, 0.18f));   // дальняя, тихо
                Ambience.Extend(ac.length, Random.Range(6f, 12f));
                nextAt = Time.time + ac.length + 1f;
                return;
            }
            // кто поёт: чередуем виды, чтобы не тянулось одно и то же
            bool ab = last == 1 ? Random.value < 0.7f : last == 0 ? Random.value < 0.35f : Random.value < 0.55f;
            last = ab ? 0 : 1;
            AudioClip[] set = ab ? abura : minmin;
            AudioClip c = set[Random.Range(0, set.Length)];
            // общая очередь звуков: пока звучит другой (птица, коршун, бамбук…) или не прошла пауза — ждём
            float lull = Random.value < 0.25f ? Random.Range(12f, 25f) : Random.Range(4f, 10f);
            if (!Ambience.Claim(c.length * 0.9f, lull, lull + 3f)) { nextAt = Time.time + Random.Range(1.5f, 3f); last = -1; return; }
            bool near = Random.value < 0.35f;   // ближняя — громко и ярко, дальние — тише
            Play(src, c, Random.Range(-0.85f, 0.85f), near ? Random.Range(0.32f, 0.42f) : Random.Range(0.12f, 0.22f));
            // дальше: иногда подхватывает вторая, обычно — дослушали и пауза, изредка — долгое затишье
            // изредка (10%) подхватывает вторая — через пару секунд, внеочередно; иначе — дослушали и тишина (её держит Ambience)
            if (Random.value < 0.1f) { nextAt = Time.time + Random.Range(2f, 4f); answer = true; }
            else nextAt = Time.time + c.length * 0.9f + 1f;
        }

        static void Play(AudioSource s, AudioClip c, float pan, float vol)
        {
            if (s == null || c == null) return;
            s.clip = c; s.panStereo = pan; s.pitch = Random.Range(0.96f, 1.04f); s.volume = vol;
            s.Play();
        }

        /// <summary>Рассвет: где-то за храмом монах метёт двор бамбуковой метлой — серия «шурх… шурх», пауза, снова.</summary>
        void Sweep()
        {
            Weather w = Weather.Instance;
            if (sweep == null || w == null || !w.Dawn || w.Raining || w.Snowing) { strokes = 0; return; }
            if (strokes <= 0 && Time.time >= sweepAt)
            {
                int n = Random.Range(6, 12);
                if (!Ambience.Claim(n * 0.9f, 4f, 9f)) { sweepAt = Time.time + Random.Range(3f, 6f); return; }
                strokes = n; strokeAt = Time.time; sweep.panStereo = Random.Range(-0.7f, 0.7f);
            }
            if (strokes > 0 && Time.time >= strokeAt)
            {
                strokes--;
                strokeAt = Time.time + Random.Range(0.75f, 1.05f);
                sweep.pitch = Random.Range(0.9f, 1.1f);
                sweep.PlayOneShot(broom, Random.Range(0.08f, 0.13f));
                if (strokes == 0) sweepAt = Time.time + Random.Range(25f, 50f);
            }
        }

        /// <summary>Мах метлы: шорох прутьев по гравию — шум в полосе 1,5–5 кГц, нарастает и обрывается, 0,4 с.</summary>
        static AudioClip Broom()
        {
            int n = (int)(Rate * 0.42f); float[] d = new float[n];
            System.Random r = new System.Random(91); float prev = 0f, hp = 0f, lp = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, x = (float)(r.NextDouble() * 2.0 - 1.0);
                hp = 0.8f * (hp + x - prev); prev = x; lp += 0.5f * (hp - lp);
                float env = Mathf.SmoothStep(0f, 1f, t / 0.25f) * Mathf.Min(1f, (0.42f - t) / 0.06f);
                float grit = 0.7f + 0.3f * (float)r.NextDouble();   // камешки под прутьями
                d[i] = lp * env * grit; peak = Mathf.Max(peak, Mathf.Abs(d[i]));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.8f / peak;
            AudioClip c = AudioClip.Create("broom", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>Дальний голос: коршун днём (не на рассвете), неясыть ночью; в дождь и снег — тишина.</summary>
        void Far()
        {
            Weather w = Weather.Instance;
            if (far == null || w == null || Time.time < farAt) return;
            farAt = Time.time + Random.Range(40f, 75f);
            if (w.Raining || w.Snowing) return;
            AudioClip c = w.Daytime ? (w.Dawn || w.Dusk ? null : tobi) : owl;
            if (c == null) return;
            if (!Ambience.Claim(c.length, 4f, 9f)) { farAt = Time.time + Random.Range(4f, 8f); return; }
            far.clip = c; far.panStereo = Random.Range(-0.65f, 0.65f); far.pitch = Random.Range(0.96f, 1.04f);
            far.volume = w.Daytime ? 0.24f : 0.32f;
            far.Play();
        }

        const int Rate = 22050;

        // ---------- синтез цикад: щелчки тимбала → резонатор ----------

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
        /// Фраза минминдзэми с момента t0: «ми-и-ин» (0,9 с, высота резонанса растёт), pulses пульсов «мин» по 0,27 с
        /// (в каждом высота взлетает и чуть оседает), протяжное «ми-и-и» на спаде (1,3 с). Пишет огибающую и частоту резонанса.
        /// </summary>
        static void MinminPhrase(float t0, float[] env, float[] f, int pulses, float pitch)
        {
            const float intro = 0.9f, tp = 0.27f, outro = 1.3f;
            for (int i = 0; i < env.Length; i++)
            {
                float u = i / (float)Rate - t0;
                float e = 0f, fr = 3400f;
                if (u < 0f) { f[i] = fr * pitch; continue; }
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
                else fr = 3100f;
                env[i] = e; f[i] = fr * pitch;
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

        /// <summary>
        /// Песня одной абурадзэми «дзи-и-и-и»: щелчки тимбала rate Гц через резонатор f0 (и слабый обертон), нарастание ~1 с,
        /// ровный треск с медленными волнами громкости и быстрой дрожью, спад ~1,8 с — как будто выдохлась.
        /// </summary>
        static AudioClip AburaCall(int seed, float dur, float f0, float rate)
        {
            int n = (int)(Rate * dur), m = n + 2000;
            System.Random r = new System.Random(seed * 7919 + 13);
            float[] env = new float[n], x = new float[m], y = new float[m];
            float p1 = (float)r.NextDouble() * 6.28f, p2 = (float)r.NextDouble() * 6.28f, fm = 6f + 2.5f * (float)r.NextDouble();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float att = Mathf.SmoothStep(0f, 1f, t / 1.1f), rel = Mathf.SmoothStep(0f, 1f, (dur - t) / 1.8f);
                float wave = 0.78f + 0.22f * Mathf.Sin(t * 1.3f + p1) * Mathf.Sin(t * 0.47f + p2);
                float trem = 1f - 0.28f * (0.5f + 0.5f * Mathf.Sin(6.2832f * fm * t));
                env[i] = att * rel * wave * trem;
            }
            Clicks(x, env, rate, 0.06f, false, r);
            for (int i = 0; i < n; i++) x[i] += ((float)r.NextDouble() * 2f - 1f) * 0.05f * env[i];   // шорох между щелчками
            Resonate(x, y, f0, null, 480f, 1f);
            Resonate(x, y, f0 * 1.55f, null, 650f, 0.2f);
            float[] d = new float[n];
            System.Array.Copy(y, d, n);
            Finish(d, 0.2f, 1.6f);
            AudioClip c = AudioClip.Create("cicada_abura_" + seed, n, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        /// <summary>Песня минминдзэми: одна фраза («ми-ин», pulses × «мин», «ми-и-и»), жужжащий треск через звонкий резонатор.</summary>
        static AudioClip MinminCall(int seed, int pulses, float pitch)
        {
            float dur = 0.9f + pulses * 0.27f + 1.4f;
            int n = (int)(Rate * dur), m = n + 2000;
            float[] env = new float[n], f = new float[n], x = new float[m], y = new float[m], fv = new float[m];
            MinminPhrase(0.05f, env, f, pulses, pitch);
            for (int i = 0; i < m; i++) fv[i] = i < n ? f[i] : 3100f * pitch;
            System.Random r = new System.Random(seed * 7919 + 29);
            Clicks(x, env, 300f, 0.03f, false, r);
            Resonate(x, y, 0f, fv, 170f, 1f);
            float[] d = new float[n];
            System.Array.Copy(y, d, n);
            for (int i = 0; i < n; i++) d[i] *= Mathf.Min(1f, (n - i) / (Rate * 0.05f));
            Finish(d, 0.22f, 1.4f);
            AudioClip c = AudioClip.Create("cicada_minmin_" + seed, n, 1, Rate, false);
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
