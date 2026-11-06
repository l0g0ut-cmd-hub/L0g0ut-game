using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Процедурная музыка без ассетов: кото (Karplus–Strong, лад «ин»: ре, ми-бемоль, соль, ля, си-бемоль) над низким гулом,
    /// и слой барабанов тайко, который нарастает во время боя и особенно в решающем раунде.
    /// </summary>
    public class Music : MonoBehaviour
    {
        const int Rate = 22050;
        AudioSource koto, drums, battle;
        float battleVol, kotoVol = 0.32f;
        float drumVol;

        static Music inst;
        /// <summary>Источник — музыка (для выключателя «игра / музыка / оба»).</summary>
        public static bool Owns(AudioSource s) { return inst != null && s != null && (s == inst.koto || s == inst.drums || s == inst.battle); }

        void Start()
        {
            inst = this;
            koto = gameObject.AddComponent<AudioSource>();
            koto.clip = KotoLoop(); koto.loop = true; koto.volume = 0.32f; koto.spatialBlend = 0f; koto.Play();
            drums = gameObject.AddComponent<AudioSource>();
            drums.clip = DrumLoop(); drums.loop = true; drums.volume = 0f; drums.spatialBlend = 0f; drums.Play();
            koto.reverbZoneMix = 0.25f; drums.reverbZoneMix = 0.35f;   // музыка почти сухая, эхо двора — для звуков боя
            // музыка боя: «Shamisen Samurai Rock» (MOMIZizm MUSiC, Free Japanese Music Pack) — петля; на карте снова кото
            AudioClip bc = Resources.Load<AudioClip>("Audio/music_battle");
            if (bc != null)
            {
                battle = gameObject.AddComponent<AudioSource>();
                battle.clip = bc; battle.loop = true; battle.volume = 0f; battle.spatialBlend = 0f; battle.reverbZoneMix = 0f;
            }
        }

        void Update()
        {
            DuelManager dm = DuelManager.Instance;
            float want = 0f;
            if (dm != null && dm.State == DuelState.Fighting && !dm.Paused)
            {
                bool deciding = dm.PlayerWins == dm.winsToMatch - 1 && dm.EnemyWins == dm.winsToMatch - 1;
                want = deciding ? 0.5f : 0.3f;
                if (DuelManager.TimerOn && dm.TimeLeft < 10f) want = 0.6f;
            }
            SoundSettings.Tick();
            drumVol = Mathf.MoveTowards(drumVol, want, Time.unscaledDeltaTime * 0.4f);
            drums.volume = drumVol;
            float kotoWant = (dm != null && dm.Paused) ? 0.15f : 0.32f;
            if (battle != null)
            {
                // бой (не карта): сямисэн вместо кото и барабанов; пауза и итог боя — тише; на карте — тишина сямисэна
                bool inFight = dm != null && !MapScreen.Open;
                float bWant = !inFight ? 0f : dm.Paused ? 0.18f : dm.MatchOver ? 0.25f : 0.5f;
                battleVol = Mathf.MoveTowards(battleVol, bWant, Time.unscaledDeltaTime * (bWant > battleVol ? 0.5f : 0.8f));
                battle.volume = battleVol;
                if (battleVol > 0.001f && !battle.isPlaying) battle.Play();
                else if (battleVol <= 0.001f && battle.isPlaying) battle.Pause();   // вернёмся в бой — с того же места
                if (inFight) { kotoWant = 0f; drums.volume = 0f; }
            }
            kotoVol = Mathf.MoveTowards(kotoVol, kotoWant, Time.unscaledDeltaTime * 0.6f);
            koto.volume = kotoVol;
        }

        // ---------------------------------------------------------------- синтез

        static float[] Pluck(float freq, float seconds, float bright, int seed)
        {
            int n = (int)(Rate * seconds);
            float[] o = new float[n];
            int p = Mathf.Max(2, Mathf.RoundToInt(Rate / freq));
            float[] buf = new float[p];
            System.Random r = new System.Random(seed);
            for (int i = 0; i < p; i++) buf[i] = (float)(r.NextDouble() * 2.0 - 1.0);
            // смягчаем начальный шум (меньше «звона» у тёмных струн)
            for (int k = 0; k < 2; k++) for (int i = 1; i < p; i++) buf[i] = Mathf.Lerp(buf[i], buf[i - 1], 1f - bright);
            int idx = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (idx + 1) % p;
                float v = buf[idx];
                buf[idx] = 0.4985f * (buf[idx] + buf[j]);
                o[i] = v;
                idx = j;
            }
            return o;
        }

        static void Mix(float[] dst, float[] src, int at, float gain)
        {
            for (int i = 0; i < src.Length; i++) dst[(at + i) % dst.Length] += src[i] * gain;   // хвост заворачивается в начало — петля бесшовная
        }

        static AudioClip KotoLoop()
        {
            const float bpm = 66f;
            float beat = 60f / bpm;
            int beats = 32;
            int n = (int)(Rate * beat * beats);
            float[] d = new float[n];
            // лад «ин» от ре: D Eb G A Bb, две октавы
            float[] scale = { 146.8f, 155.6f, 196.0f, 220.0f, 233.1f, 293.7f, 311.1f, 392.0f, 440.0f, 466.2f };
            System.Random r = new System.Random(21);
            int[] phrase = { 5, 4, 3, 2, -1, 3, 4, 1, 2, -1, -1, 0, 5, 7, 6, 5, -1, 4, 3, -1, 2, 1, 0, -1, 3, 5, 4, -1, 2, -1, 0, -1 };
            for (int b = 0; b < beats; b++)
            {
                int note = phrase[b % phrase.Length];
                if (note < 0) continue;
                float t = b * beat + (b % 4 == 3 ? beat * 0.5f : 0f);
                Mix(d, Pluck(scale[note], 3.2f, 0.75f, b), (int)(t * Rate), 0.5f);
                if (r.NextDouble() < 0.3) Mix(d, Pluck(scale[note] * 2f, 1.6f, 0.9f, b + 100), (int)((t + beat * 0.5f) * Rate), 0.22f);   // тихий отзвук октавой выше
            }
            // низкий гул (ре) с медленным «дыханием»
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float br = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * t / (beat * beats) * 2f);
                d[i] += (Mathf.Sin(2f * Mathf.PI * 73.4f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 110.0f * t) * 0.25f) * 0.07f * br;
            }
            Normalize(d, 0.7f);
            AudioClip c = AudioClip.Create("koto", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        static AudioClip DrumLoop()
        {
            const float bpm = 132f;
            float step = 60f / bpm / 2f;          // восьмые
            int steps = 32;
            int n = (int)(Rate * step * steps);
            float[] d = new float[n];
            float[] big = Taiko(0.9f, 58f, 1f), mid = Taiko(0.5f, 95f, 0.6f), rim = Rim();
            // ДОН . . ДОН | . КА ДОН . | ДОН . КА . | ДОН ДОН КА КА  (x2 с вариацией)
            string pat = "B..BmrB.B.r.BBrr" + "B..Bm.B.B.rmBmBr";
            for (int i = 0; i < steps; i++)
            {
                int at = (int)(i * step * Rate);
                char ch = pat[i % pat.Length];
                if (ch == 'B') Mix(d, big, at, 0.9f);
                else if (ch == 'm') Mix(d, mid, at, 0.6f);
                else if (ch == 'r') Mix(d, rim, at, 0.35f);
            }
            Normalize(d, 0.8f);
            AudioClip c = AudioClip.Create("taiko_loop", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        static float[] Taiko(float len, float f0, float noise)
        {
            int n = (int)(Rate * len);
            float[] o = new float[n];
            System.Random r = new System.Random(3);
            float ph = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = f0 * (0.75f + 0.5f * Mathf.Exp(-t * 25f));
                ph += 2f * Mathf.PI * f / Rate;
                lp += 0.1f * ((float)(r.NextDouble() * 2.0 - 1.0) - lp);
                o[i] = (Mathf.Sin(ph) * Mathf.Exp(-t * 5f) + lp * noise * Mathf.Exp(-t * 35f)) * Mathf.Min(1f, t * 800f);
            }
            return o;
        }

        static float[] Rim()
        {
            int n = (int)(Rate * 0.12f);
            float[] o = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                o[i] = (Mathf.Sin(2f * Mathf.PI * 820f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 1430f * t)) * Mathf.Exp(-t * 60f);
            }
            return o;
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 1e-4f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }
}
