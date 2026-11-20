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
            public int face;                            // портрет полководца: UI/Battle/hero_portraits (4×2)
            public int[] equip = { -1, -1, -1, -1, -1, -1, -1, -1, -1 };   // артефакт в слоте (−1 — пусто)
            public System.Collections.Generic.List<int> bag = new System.Collections.Generic.List<int>();
        }

        // ---------------------------------------------------------------- артефакты (иконки UI/artifacts 4×4)
        public const int SlotCount = 9, BagSize = 8;
        public static readonly string[] SlotNames = { "ГОЛОВА", "ШЕЯ", "ГРУДЬ", "ЛЕВАЯ РУКА", "ПРАВАЯ РУКА", "ПОЯС", "КОЛЬЦО", "КОЛЬЦО", "ОСОБОЕ" };
        public class Art { public string name; public int slot, atk, def, pow, know, morale, luck, spd, ranged; }
        public static readonly Art[] Arts =
        {
            new Art { name = "Небесный клинок", slot = 4, atk = 3 },
            new Art { name = "Алое копьё", slot = 4, atk = 2, def = 1 },
            new Art { name = "Драконий лук", slot = 3, ranged = 15 },
            new Art { name = "Золотое ружьё", slot = 3, atk = 1, ranged = 10 },
            new Art { name = "Шлем дракона", slot = 0, know = 2 },
            new Art { name = "Золотой доспех", slot = 2, def = 3 },
            new Art { name = "Наручи хатамото", slot = 8, def = 2 },
            new Art { name = "Маска демона", slot = 0, atk = 1, morale = 1 },
            new Art { name = "Нефритовая магатама", slot = 1, pow = 2 },
            new Art { name = "Кольцо с рубином", slot = 6, atk = 1, def = 1 },
            new Art { name = "Лаковое инро", slot = 5, def = 1, know = 1 },
            new Art { name = "Веер полководца", slot = 8, spd = 1 },
            new Art { name = "Священное зеркало", slot = 8, pow = 1, know = 1 },
            new Art { name = "Свиток тайных приёмов", slot = 8, atk = 1, def = 1, pow = 1, know = 1 },
            new Art { name = "Светлые чётки", slot = 6, luck = 1 },
            new Art { name = "Золотой лис", slot = 8, luck = 1, morale = 1 },
        };
        public static string ArtText(int id)
        {
            Art a = Arts[id]; string t = "";
            if (a.atk != 0) t += ", атака +" + a.atk; if (a.def != 0) t += ", защита +" + a.def; if (a.pow != 0) t += ", сила духа +" + a.pow;
            if (a.know != 0) t += ", знание +" + a.know; if (a.morale != 0) t += ", дух войска +" + a.morale; if (a.luck != 0) t += ", удача +" + a.luck;
            if (a.spd != 0) t += ", скорость +" + a.spd; if (a.ranged != 0) t += ", урон стрелков +" + a.ranged + "%";
            return a.name + " (" + SlotNames[a.slot].ToLower() + ")" + (t.Length > 0 ? ": " + t.Substring(2) : "");
        }
        static int Sum(System.Func<Art, int> f) { int v = 0; foreach (int id in D.equip) if (id >= 0 && id < Arts.Length) v += f(Arts[id]); return v; }
        // наборы артефактов («Герои 3»): все вещи набора надеты — общий бонус
        public static readonly string[] SetNames = { "ДОСПЕХ ДРАКОНА", "ТАЙНЫ ОНМЁДЗИ" };
        static readonly int[][] SetItems = { new[] { 0, 4, 5 }, new[] { 8, 10, 14 } };
        public static readonly string[] SetText = { "клинок, шлем и доспех дракона: +3 к атаке и защите", "магатама, инро и чётки: +2 к силе духа и знанию" };
        public static bool SetOn(int k) { foreach (int id in SetItems[k]) if (System.Array.IndexOf(D.equip, id) < 0) return false; return true; }
        static int SetAtk { get { return SetOn(0) ? 3 : 0; } }
        static int SetPow { get { return SetOn(1) ? 2 : 0; } }
        public static int ArtMorale { get { return Sum(a => a.morale); } }
        public static int ArtLuck { get { return Sum(a => a.luck); } }
        public static int ArtSpeed { get { return Sum(a => a.spd); } }
        public static float ArtRanged { get { return Sum(a => a.ranged) / 100f; } }
        /// <summary>Артефакт в рюкзак (добыча боя, сундук); false — рюкзак полон.</summary>
        public static bool Give(int id)
        {
            if (id < 0 || id >= Arts.Length || D.bag.Count >= BagSize) return false;
            D.bag.Add(id); Save(); return true;
        }
        public static int RandomArt() { return UnityEngine.Random.Range(0, Arts.Length); }
        static void Equip(int bagIndex)
        {
            int id = D.bag[bagIndex]; Art a = Arts[id];
            int slot = a.slot;
            if (slot == 6 && D.equip[6] >= 0 && D.equip[7] < 0) slot = 7;   // второе кольцо
            D.bag.RemoveAt(bagIndex);
            if (D.equip[slot] >= 0) D.bag.Add(D.equip[slot]);
            D.equip[slot] = id; Save();
        }
        static void Unequip(int slot)
        {
            if (D.equip[slot] < 0 || D.bag.Count >= BagSize) return;
            D.bag.Add(D.equip[slot]); D.equip[slot] = -1; Save();
        }

        public const int SkillCount = 11, MaxSkills = 6;
        public static readonly string[] PrimaryNames = { "АТАКА", "ЗАЩИТА", "СИЛА ДУХА", "ЗНАНИЕ" };
        public static readonly string[] PrimaryHint =
        {
            "+1 к атаке всех отрядов", "+1 к защите всех отрядов", "сила умений героя", "запас духа в бою"
        };
        public static readonly string[] SkillNames = { "СТРЕЛЬБА", "НАПАДЕНИЕ", "ДОСПЕХИ", "ЛИДЕРСТВО", "УДАЧА", "МИСТИЦИЗМ", "ЗНАХАРСТВО", "ТАКТИКА", "ЛОГИСТИКА", "АРТИЛЛЕРИЯ", "НЕКРОМАНТИЯ" };
        static readonly string[] SkillHint =
        {
            "урон стрелков +{0}%", "урон в ближнем бою +{0}%", "получаемый урон −{0}%", "боевой дух +{0}",
            "удача +{0}", "дух в бою +{0}", "после боя встают {0}% павших", "скорость отрядов +{0}", "шагов пути +{0} в день", "баллиста в руках, урон ×{0}", "{0}% сражённых врагов встают нашими павшими"
        };
        static readonly int[][] SkillVal =
        {
            new[] { 0, 10, 25, 50 }, new[] { 0, 10, 20, 30 }, new[] { 0, 5, 10, 15 }, new[] { 0, 1, 2, 3 },
            new[] { 0, 1, 2, 3 }, new[] { 0, 1, 2, 3 }, new[] { 0, 10, 20, 30 }, new[] { 0, 0, 1, 2 },
            new[] { 0, 1, 1, 2 }, new[] { 0, 1, 2, 3 }, new[] { 0, 10, 20, 30 }
        };
        public static readonly string[] Ranks = { "", "ОСНОВЫ", "ОПЫТНЫЙ", "МАСТЕР" };
        static readonly int[] PrimaryWeight = { 35, 35, 15, 15 };   // воевода: больше атаки и защиты

        static Data d;
        public static Data D { get { if (d == null) Load(); return d; } }

        static void Load()
        {
            try { d = JsonUtility.FromJson<Data>(PlayerPrefs.GetString("general", "")); } catch { d = null; }
            if (d == null) d = new Data();
            if (d.equip == null || d.equip.Length != SlotCount) d.equip = new[] { -1, -1, -1, -1, -1, -1, -1, -1, -1 };
            if (d.bag == null) d.bag = new System.Collections.Generic.List<int>();
            if (d.skill == null || d.skill.Length != SkillCount) { int[] s = new int[SkillCount]; if (d.skill != null) Array.Copy(d.skill, s, Mathf.Min(SkillCount, d.skill.Length)); d.skill = s; }
        }
        public static void Save() { PlayerPrefs.SetString("general", JsonUtility.ToJson(D)); PlayerPrefs.Save(); }

        public static int Atk { get { Sync(); return D.atk + Sum(a => a.atk) + SetAtk; } }
        public static int Def { get { Sync(); return D.def + Sum(a => a.def) + SetAtk; } }
        public static int Power { get { Sync(); return D.power + Sum(a => a.pow) + SetPow; } }
        public static int Know { get { Sync(); return D.know + Sum(a => a.know) + SetPow; } }
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

        static string tip = "";
        static float tipAt;
        static readonly Vector2[] SlotPx =
        {   // гнёзда куклы на картине UI/general_window (1672×941)
            new Vector2(318, 195), new Vector2(322, 341), new Vector2(322, 432), new Vector2(172, 504), new Vector2(480, 504),
            new Vector2(320, 585), new Vector2(172, 660), new Vector2(480, 660), new Vector2(318, 792)
        };

        public static void Draw(float W)
        {
            Sync();
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.75f));
            Texture2D win = UiKit.Tex("UI/general_window");
            float k = Mathf.Min((W - 20f) / 1672f, 520f / 941f);
            Rect r = new Rect((W - 1672f * k) * 0.5f, (540f - 941f * k) * 0.5f, 1672f * k, 941f * k);
            System.Func<float, float, float, float, Rect> Px = (x, y, w, h) => new Rect(r.x + x * k, r.y + y * k, w * k, h * k);
            if (win != null) GUI.DrawTexture(r, win); else UiKit.Rect(r, new Color(0.08f, 0.04f, 0.02f, 0.97f));
            Texture2D art = UiKit.Tex("UI/artifacts");
            UiKit.Text(Px(612, 74, 440, 54), "ПОЛКОВОДЕЦ · УР. " + Profile.Level, 15f, Gold);
            Rect fr = Px(120, 95, 130, 130);
            DrawFace(fr);
            if (GUI.Button(fr, GUIContent.none, GUIStyle.none)) { D.face = (D.face + 1) % 8; Save(); }
            // основные параметры
            for (int i = 0; i < 4; i++)
            {
                int v = i == 0 ? Atk : i == 1 ? Def : i == 2 ? Power : Know;
                float x = new[] { 656f, 903f, 1148f, 1395f }[i];
                UiKit.Text(Px(x, 170, 166, 40), PrimaryNames[i], 11f, Gold);
                UiKit.Text(Px(x, 205, 166, 80), v.ToString(), 30f, Ink);
                UiKit.Text(Px(x, 300, 166, 40), PrimaryHint[i], 8.5f, Dim);
            }
            // навыки
            int n = 0;
            for (int i = 0; i < SkillCount && n < 6; i++)
            {
                if (D.skill[i] == 0) continue;
                Rect kr = Px(new[] { 617f, 949f, 1281f }[n % 3], n < 3 ? 419f : 545f, 312f, 100f);
                UiKit.Text(new Rect(kr.x + 6f, kr.y + 4f, kr.width - 12f, kr.height * 0.5f - 2f), SkillNames[i] + " · " + Ranks[D.skill[i]], 10.5f, Ink);
                UiKit.Text(new Rect(kr.x + 6f, kr.y + kr.height * 0.5f, kr.width - 12f, kr.height * 0.45f), Hint(i, D.skill[i]), 9f, Dim);
                n++;
            }
            if (n == 0) UiKit.Text(Px(617, 419, 976, 100), "Навыков пока нет — они даются с каждым новым уровнем", 10.5f, Dim);
            // кукла: снаряжение
            for (int sl = 0; sl < SlotCount; sl++)
            {
                Rect sr = Px(SlotPx[sl].x - 36f, SlotPx[sl].y - 36f, 72f, 72f);
                int id = D.equip[sl];
                if (id >= 0 && art != null) GUI.DrawTextureWithTexCoords(sr, art, Cell(id));
                if (GUI.Button(sr, GUIContent.none, GUIStyle.none))
                {
                    if (id >= 0) { tip = "Снято: " + ArtText(id); Unequip(sl); }
                    else tip = SlotNames[sl] + " — пусто. Надень артефакт из рюкзака.";
                    tipAt = Time.unscaledTime;
                }
            }
            // рюкзак
            for (int b = 0; b < BagSize; b++)
            {
                Rect br = Px(625f + 120.3f * b, 719f, 110f, 112f);
                if (b >= D.bag.Count) continue;
                int id = D.bag[b];
                if (art != null) GUI.DrawTextureWithTexCoords(new Rect(br.x + 3f, br.y + 3f, br.width - 6f, br.width - 6f), art, Cell(id));
                if (GUI.Button(br, GUIContent.none, GUIStyle.none)) { tip = "Надето: " + ArtText(id); tipAt = Time.unscaledTime; Equip(b); return; }
            }
            if (Time.unscaledTime - tipAt < 4f && tip.Length > 0)
                UiKit.Text(Px(600, 856, 1000, 40), tip, 10f, Gold);
            else
            {
                string sets = ""; for (int q = 0; q < SetNames.Length; q++) if (SetOn(q)) sets += (sets.Length > 0 ? " · " : "") + SetNames[q] + " — " + SetText[q];
                UiKit.Text(Px(600, 856, 1000, 40), sets.Length > 0 ? "Набор собран: " + sets : "Наборы: " + SetNames[0].ToLower() + " (клинок, шлем, доспех), " + SetNames[1].ToLower() + " (магатама, инро, чётки)", 9.5f, sets.Length > 0 ? Gold : Dim);
            }
            // выбор навыка при повышении уровня — поверх окна
            if (D.pending > 0 && D.offerA >= 0)
            {
                Rect cr = new Rect(W * 0.5f - 300f, 540f * 0.5f - 60f, 600f, 120f);
                UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.45f));
                UiKit.Rect(cr, new Color(0.18f, 0.08f, 0.04f, 0.97f));
                string pl = D.lastPrimary >= 0 ? "  (+1 " + PrimaryNames[D.lastPrimary].ToLower() + ")" : "";
                UiKit.Text(new Rect(cr.x, cr.y + 8f, cr.width, 22f), "НОВЫЙ УРОВЕНЬ" + pl + " — выбери навык", 13f, Gold);
                int[] o = { D.offerA, D.offerB };
                for (int q = 0; q < 2; q++)
                {
                    if (o[q] < 0) continue;
                    int nr = D.skill[o[q]] + 1;
                    Rect br = new Rect(cr.x + 14f + q * (cr.width * 0.5f), cr.y + 40f, cr.width * 0.5f - 28f, 40f);
                    if (VisualIdentity.KitButton(br, SkillNames[o[q]] + " · " + Ranks[nr], true, q == 0)) { Choose(o[q]); return; }
                    UiKit.Text(new Rect(br.x, br.yMax + 4f, br.width, 16f), Hint(o[q], nr), 10f, Ink);
                }
                return;
            }
            if (VisualIdentity.KitButton(new Rect(r.xMax - 150f, r.y + 6f, 120f, 30f), "ЗАКРЫТЬ", true, false)) Open = false;
            if (VisualIdentity.KitButton(new Rect(r.x + 20f, r.y + 6f, 150f, 30f), "+ АРТЕФАКТ (разраб)", D.bag.Count < BagSize, false)) { int id = RandomArt(); Give(id); tip = "Получен: " + ArtText(id); tipAt = Time.unscaledTime; }
        }
        /// <summary>Портрет полководца (касание в окне — следующий).</summary>
        public static void DrawFace(Rect r)
        {
            Texture2D t = UiKit.Tex("UI/Battle/hero_portraits");
            if (t == null) { Tactics.DrawUnitPortrait(r, 10); return; }
            int f = Mathf.Clamp(D.face, 0, 7);
            GUI.DrawTextureWithTexCoords(r, t, new Rect((f % 4) * 0.25f, f < 4 ? 0.5f : 0f, 0.25f, 0.5f));
        }
        static Rect Cell(int id) { return new Rect((id % 4) * 0.25f, 1f - (id / 4 + 1) * 0.25f, 0.25f, 0.25f); }
    }
}
