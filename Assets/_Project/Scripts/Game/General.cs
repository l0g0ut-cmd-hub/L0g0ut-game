using System;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Полководец (герой армии, как в «Героях 3»): 4 основных параметра — Атака, Защита, Сила духа, Знание —
    /// и вторичные навыки (до 6, у каждого 3 ступени: основы → опытный → мастер). Уровень — общий с героем (Profile.Level):
    /// на каждом новом уровне +1 к случайному параметру и выбор из двух навыков. Хранится в PlayerPrefs «general».
    /// </summary>
    public static class General
    {
        [Serializable]
        public class Data
        {
            public int atk = 1, def = 1, power = 1, know = 1;
            public int ups = 1;                         // до какого уровня уже раздали прибавки
            public int[] skill = new int[SkillCount];  // ступень навыка 0…3
            public int pending;                         // сколько выборов навыка ждёт
            public int offerA = -1, offerB = -1;
            public int lastPrimary = -1;
        }

        public const int SkillCount = 8, MaxSkills = 6;
        public static readonly string[] PrimaryNames = { "АТАКА", "ЗАЩИТА", "СИЛА ДУХА", "ЗНАНИЕ" };
        public static readonly string[] PrimaryHint =
        {
            "+1 к атаке всех отрядов", "+1 к защите всех отрядов", "сила умений героя", "запас духа в бою"
        };
        public static readonly string[] SkillNames = { "СТРЕЛЬБА", "НАПАДЕНИЕ", "ДОСПЕХИ", "ЛИДЕРСТВО", "УДАЧА", "МИСТИЦИЗМ", "ЗНАХАРСТВО", "ТАКТИКА" };
        static readonly string[] SkillHint =
        {
            "урон стрелков +{0}%", "урон в ближнем бою +{0}%", "получаемый урон −{0}%", "боевой дух +{0}",
            "удача +{0}", "дух в бою +{0}", "после боя встают {0}% павших", "скорость отрядов +{0}"
        };
        static readonly int[][] SkillVal =
        {
            new[] { 0, 10, 25, 50 }, new[] { 0, 10, 20, 30 }, new[] { 0, 5, 10, 15 }, new[] { 0, 1, 2, 3 },
            new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 3 }, new[] { 0, 10, 20, 30 }, new[] { 0, 0, 1, 2 }
        };
        public static readonly string[] Ranks = { "", "ОСНОВЫ", "ОПЫТНЫЙ", "МАСТЕР" };
        static readonly int[] PrimaryWeight = { 35, 35, 15, 15 };   // воевода: больше атаки и защиты

        static Data d;
        public static Data D { get { if (d == null) Load(); return d; } }

        static void Load()
        {
            try { d = JsonUtility.FromJson<Data>(PlayerPrefs.GetString("general", "")); } catch { d = null; }
            if (d == null) d = new Data();
            if (d.skill == null || d.skill.Length != SkillCount) { int[] s = new int[SkillCount]; if (d.skill != null) Array.Copy(d.skill, s, Mathf.Min(SkillCount, d.skill.Length)); d.skill = s; }
        }
        public static void Save() { PlayerPrefs.SetString("general", JsonUtility.ToJson(D)); PlayerPrefs.Save(); }

        public static int Atk { get { Sync(); return D.atk; } }
        public static int Def { get { Sync(); return D.def; } }
        public static int Power { get { Sync(); return D.power; } }
        public static int Know { get { Sync(); return D.know; } }
        public static int Skill(int i) { return D.skill[i]; }
        public static int Val(int i) { return SkillVal[i][D.skill[i]]; }
        public static float Pct(int i) { return Val(i) / 100f; }
        public static string Hint(int i, int rank) { return string.Format(SkillHint[i], SkillVal[i][Mathf.Clamp(rank, 0, 3)]); }
        public static int Known { get { int n = 0; foreach (int s in D.skill) if (s > 0) n++; return n; } }

        /// <summary>Догоняет уровень героя: прибавки к параметрам и выборы навыков.</summary>
        public static void Sync()
        {
            int lv = Mathf.Max(1, Profile.Level);
            bool changed = false;
            while (D.ups < lv)
            {
                D.ups++;
                int r = UnityEngine.Random.Range(0, 100), k = 0;
                while (k < 3 && r >= PrimaryWeight[k]) { r -= PrimaryWeight[k]; k++; }
                if (k == 0) D.atk++; else if (k == 1) D.def++; else if (k == 2) D.power++; else D.know++;
                D.lastPrimary = k;
                D.pending++;
                changed = true;
            }
            if (D.pending > 0 && D.offerA < 0) { MakeOffer(); changed = true; }
            if (changed) Save();
        }

        static bool CanTake(int i) { return D.skill[i] < 3 && (D.skill[i] > 0 || Known < MaxSkills); }
        static void MakeOffer()
        {
            // как в «Героях»: один вариант — улучшить уже известный навык (если есть), второй — новый
            System.Collections.Generic.List<int> up = new System.Collections.Generic.List<int>(), nw = new System.Collections.Generic.List<int>();
            for (int i = 0; i < SkillCount; i++) { if (!CanTake(i)) continue; if (D.skill[i] > 0) up.Add(i); else nw.Add(i); }
            if (up.Count + nw.Count == 0) { D.pending = 0; D.offerA = D.offerB = -1; return; }
            int a = up.Count > 0 ? up[UnityEngine.Random.Range(0, up.Count)] : nw[UnityEngine.Random.Range(0, nw.Count)];
            nw.Remove(a); up.Remove(a);
            int b = nw.Count > 0 ? nw[UnityEngine.Random.Range(0, nw.Count)] : up.Count > 0 ? up[UnityEngine.Random.Range(0, up.Count)] : -1;
            D.offerA = a; D.offerB = b;
        }
        public static void Choose(int i)
        {
            if (i < 0 || i >= SkillCount) return;
            if (CanTake(i)) D.skill[i]++;
            D.pending = Mathf.Max(0, D.pending - 1); D.offerA = D.offerB = -1;
            if (D.pending > 0) MakeOffer();
            Save();
        }

        // ---------------------------------------------------------------- экран полководца

        public static bool Open { get; private set; }
        public static void Show() { Open = true; Sync(); }
        static readonly Color Gold = new Color(0.9f, 0.76f, 0.47f), Ink = new Color(0.93f, 0.89f, 0.8f), Dim = new Color(0.68f, 0.64f, 0.56f);

        public static void Draw(float W)
        {
            Sync();
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.72f));
            Rect r = new Rect(W * 0.5f - 330f, 40f, 660f, 460f);
            UiKit.Rect(r, new Color(0.08f, 0.04f, 0.02f, 0.97f));
            Texture2D pt = UiKit.Tex("UI/editor_panel");
            if (pt != null) UiKit.Slice(r, pt, 0.36f, 0.15f, 0f, 0.075f, 0f, Color.white);
            UiKit.Text(new Rect(r.x, r.y + 14f, r.width, 30f), "ПОЛКОВОДЕЦ · УРОВЕНЬ " + Profile.Level, 20f, Gold);
            // портрет
            Rect pr = new Rect(r.x + 30f, r.y + 60f, 120f, 120f);
            Tactics.DrawUnitPortrait(pr, 10);
            UiKit.Ring(pr.center, 61f, Gold, true);
            // основные параметры
            for (int i = 0; i < 4; i++)
            {
                int v = i == 0 ? D.atk : i == 1 ? D.def : i == 2 ? D.power : D.know;
                Rect sr = new Rect(r.x + 170f + i * 118f, r.y + 62f, 110f, 116f);
                UiKit.Rect(sr, new Color(0.16f, 0.09f, 0.05f, 0.9f));
                UiKit.Text(new Rect(sr.x, sr.y + 6f, sr.width, 20f), PrimaryNames[i], 12f, Gold);
                UiKit.Text(new Rect(sr.x, sr.y + 28f, sr.width, 44f), v.ToString(), 34f, Ink);
                UiKit.Text(new Rect(sr.x + 4f, sr.y + 76f, sr.width - 8f, 36f), PrimaryHint[i], 10f, Dim);
            }
            // навыки
            UiKit.Text(new Rect(r.x + 30f, r.y + 196f, 300f, 22f), "НАВЫКИ (" + Known + " из " + MaxSkills + ")", 14f, Gold, TextAnchor.MiddleLeft);
            int n = 0;
            for (int i = 0; i < SkillCount; i++)
            {
                if (D.skill[i] == 0) continue;
                Rect kr = new Rect(r.x + 30f + (n % 3) * 202f, r.y + 222f + (n / 3) * 58f, 194f, 52f);
                UiKit.Rect(kr, new Color(0.16f, 0.09f, 0.05f, 0.9f));
                UiKit.Text(new Rect(kr.x + 8f, kr.y + 4f, kr.width - 16f, 18f), SkillNames[i] + " · " + Ranks[D.skill[i]], 12f, Ink, TextAnchor.MiddleLeft);
                UiKit.Text(new Rect(kr.x + 8f, kr.y + 26f, kr.width - 16f, 18f), Hint(i, D.skill[i]), 11f, Dim, TextAnchor.MiddleLeft);
                n++;
            }
            if (n == 0) UiKit.Text(new Rect(r.x + 30f, r.y + 226f, 600f, 20f), "Пока нет. Навыки даются с каждым новым уровнем.", 12f, Dim, TextAnchor.MiddleLeft);
            // выбор навыка при повышении уровня
            if (D.pending > 0 && D.offerA >= 0)
            {
                Rect cr = new Rect(r.x + 20f, r.y + 340f, r.width - 40f, 70f);
                UiKit.Rect(cr, new Color(0.25f, 0.12f, 0.05f, 0.95f));
                string pl = D.lastPrimary >= 0 ? "  (+1 " + PrimaryNames[D.lastPrimary].ToLower() + ")" : "";
                UiKit.Text(new Rect(cr.x, cr.y + 2f, cr.width, 18f), "НОВЫЙ УРОВЕНЬ" + pl + " — выбери навык:", 12f, Gold);
                int[] o = { D.offerA, D.offerB };
                for (int k = 0; k < 2; k++)
                {
                    if (o[k] < 0) continue;
                    int nr = D.skill[o[k]] + 1;
                    Rect br = new Rect(cr.x + 10f + k * (cr.width * 0.5f), cr.y + 22f, cr.width * 0.5f - 20f, 42f);
                    if (VisualIdentity.KitButton(br, SkillNames[o[k]] + " · " + Ranks[nr], true, k == 0)) { Choose(o[k]); return; }
                    UiKit.Text(new Rect(br.x, br.yMax - 13f, br.width, 12f), Hint(o[k], nr), 9f, Ink);
                }
            }
            if (VisualIdentity.KitButton(new Rect(r.xMax - 160f, r.yMax - 46f, 140f, 34f), "ЗАКРЫТЬ", true, false)) Open = false;
        }
    }
}
