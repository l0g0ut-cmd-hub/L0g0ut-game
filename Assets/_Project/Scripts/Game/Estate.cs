using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Поместье героя (эталон — замок «Героев» 3): девять площадок, постройки за моны и материалы, каждая постройка раз в неделю
    /// даёт бойцов для найма (улучшение — больше и сильнее отряды), в главном доме — знаменосцы и ронины. Нанятые — в армии
    /// (до 7 отрядов), с ней — в пошаговый бой (Tactics). Пока — меню разработчика («ПОМЕСТЬЕ»), неделя — кнопкой.
    /// Картина — UI/Battle/estate_bg (1672×941), постройки — UI/Battle/estate_buildings (3×3).
    /// </summary>
    public static class Estate
    {
        public static bool Open { get; private set; }

        [System.Serializable] public class ArmySlot { public int unit; public int count; }
        [System.Serializable]
        public class Data
        {
            public int[] level = new int[9];        // 0 — пусто, 1 — построено, 2 — улучшено
            public int[] pool = new int[12];        // сколько бойцов каждого отряда можно нанять сейчас
            public int rice = 20, steel = 10, wood = 20, honor;
            public int week = 1;
            public bool builtThisWeek;
            public List<ArmySlot> army = new List<ArmySlot>();
        }
        static Data d;
        public static Data D { get { if (d == null) Load(); return d; } }
        static void Load()
        {
            try { string s = PlayerPrefs.GetString("estate", ""); d = string.IsNullOrEmpty(s) ? null : JsonUtility.FromJson<Data>(s); } catch { d = null; }
            if (d == null)
            {
                d = new Data();
                d.army.Add(new ArmySlot { unit = 0, count = 20 });   // с чего начинается: асигару и лучники из родной деревни
                d.army.Add(new ArmySlot { unit = 1, count = 8 });
            }
            if (d.level == null || d.level.Length != 9) d.level = new int[9];
            if (d.pool == null || d.pool.Length != 12) d.pool = new int[12];
            if (d.army == null) d.army = new List<ArmySlot>();
        }
        public static void Save() { PlayerPrefs.SetString("estate", JsonUtility.ToJson(D)); PlayerPrefs.Save(); }

        // ---------------------------------------------------------------- постройки

        public class Building
        {
            public string name, effect; public int gold, wood, steel, rice, honor;
            public int unit = -1, unit2 = -1;        // кого нанимают (unit2 — после улучшения)
            public Building(string n, string e, int g, int w, int s, int r, int h, int u, int u2) { name = n; effect = e; gold = g; wood = w; steel = s; rice = r; honor = h; unit = u; unit2 = u2; }
        }
        // площадки картины → постройка (как на макете mockup_estate9): верх — казарма, стрельбище, кузня, конюшня;
        // середина — храм, зал онмёдзи; низ — додзё, рынок, башня
        public static readonly Building[] Plots =
        {
            new Building("КАЗАРМА АСИГАРУ", "Асигару-копейщики", 400, 10, 0, 0, 0, 0, -1),
            new Building("СТРЕЛЬБИЩЕ", "Лучники; улучшение — тэппо", 600, 10, 0, 0, 0, 1, 2),
            new Building("КУЗНЯ", "+1 к атаке и защите всех отрядов; улучшение — ещё +1", 800, 0, 10, 0, 0, -1, -1),
            new Building("КОНЮШНЯ", "Боевые псы; улучшение — конные самураи", 1200, 20, 0, 10, 0, 11, 4),
            new Building("ХРАМ", "Сохэи; +1 дух героя в бою", 900, 0, 5, 20, 0, 5, -1),
            new Building("ЗАЛ ОНМЁДЗИ", "Онмёдзи; улучшение — +2 духа в бою", 1500, 0, 5, 0, 1, 8, -1),
            new Building("ДОДЗЁ САМУРАЕВ", "Самураи; улучшение — хатамото", 1500, 0, 15, 0, 0, 3, 10),
            new Building("РЫНОК", "Каждую неделю +5 дерева, +3 стали, +10 риса", 500, 10, 0, 0, 0, -1, -1),
            new Building("БАШНЯ ЯГУРА", "Синоби; +1 к скорости всех отрядов в бою", 1000, 15, 5, 0, 0, 6, -1),
        };
        static readonly Rect[] PlotPx =
        {   // площадки на картине 1672×941: x, y (верх), ширина, высота
            new Rect(336, 155, 240, 95), new Rect(640, 150, 188, 98), new Rect(916, 159, 203, 91), new Rect(1215, 161, 219, 91),
            new Rect(283, 350, 222, 120), new Rect(1217, 350, 245, 115),
            new Rect(300, 560, 271, 130), new Rect(724, 563, 272, 127), new Rect(1130, 560, 275, 130),
        };
        static readonly int[] PlotArt = { 0, 1, 2, 4, 3, 8, 5, 6, 7 };   // ячейка атласа построек
        public static readonly int[] Growth = { 14, 9, 6, 6, 3, 5, 4, 5, 3, 4, 1, 8 };            // в неделю
        public static readonly int[] Price = { 30, 60, 90, 150, 400, 130, 200, 120, 350, 80, 1500, 50 };   // мон за бойца

        public static int Level(int plot) { return D.level[plot]; }
        /// <summary>Бонусы построек в бою.</summary>
        public static int ForgeBonus { get { return D.level[2]; } }
        public static int SpiritBonus { get { return (D.level[4] > 0 ? 1 : 0) + (D.level[5] >= 2 ? 2 : 0); } }
        public static int SpeedBonus { get { return D.level[8] > 0 ? 1 : 0; } }

        static float Mul(int lv) { return lv >= 1 ? 1.5f : 1f; }
        static bool CanPay(Building b, float k) { return Profile.Gold >= Mathf.RoundToInt(b.gold * k) && D.wood >= Mathf.RoundToInt(b.wood * k) && D.steel >= Mathf.RoundToInt(b.steel * k) && D.rice >= Mathf.RoundToInt(b.rice * k) && D.honor >= Mathf.RoundToInt(b.honor * k); }
        static void Pay(Building b, float k) { Profile.AddGold(-Mathf.RoundToInt(b.gold * k)); D.wood -= Mathf.RoundToInt(b.wood * k); D.steel -= Mathf.RoundToInt(b.steel * k); D.rice -= Mathf.RoundToInt(b.rice * k); D.honor -= Mathf.RoundToInt(b.honor * k); }
        static string CostText(Building b, float k)
        {
            List<string> l = new List<string> { Mathf.RoundToInt(b.gold * k) + " мон" };
            if (b.wood > 0) l.Add(Mathf.RoundToInt(b.wood * k) + " дерева");
            if (b.steel > 0) l.Add(Mathf.RoundToInt(b.steel * k) + " стали");
            if (b.rice > 0) l.Add(Mathf.RoundToInt(b.rice * k) + " риса");
            if (b.honor > 0) l.Add(Mathf.RoundToInt(b.honor * k) + " чести");
            return string.Join(", ", l.ToArray());
        }
        static void Build(int plot)
        {
            Building b = Plots[plot]; int lv = D.level[plot];
            if (lv >= 2 || D.builtThisWeek || !CanPay(b, Mul(lv))) return;
            Pay(b, Mul(lv)); D.level[plot] = lv + 1; D.builtThisWeek = true;
            // первая неделя новой постройки — сразу бойцы
            if (lv == 0 && b.unit >= 0) D.pool[b.unit] += Growth[b.unit];
            if (lv == 1 && b.unit2 >= 0) D.pool[b.unit2] += Growth[b.unit2];
            Save();
            UiFx.Play("coins", 0.8f);
            Note(b.name + (lv == 0 ? " построена" : " улучшена") + ". Следующая постройка — через неделю.");
        }

        /// <summary>Новая неделя: прирост бойцов, доход рынка и главного дома, снова можно строить.</summary>
        public static void NewWeek()
        {
            D.week++; D.builtThisWeek = false;
            for (int p = 0; p < 9; p++)
            {
                Building b = Plots[p]; int lv = D.level[p];
                if (lv >= 1 && b.unit >= 0) D.pool[b.unit] += Mathf.RoundToInt(Growth[b.unit] * (lv >= 2 ? 1.5f : 1f));
                if (lv >= 2 && b.unit2 >= 0) D.pool[b.unit2] += Growth[b.unit2];
            }
            D.pool[7] += Growth[7]; D.pool[9] += Growth[9];   // главный дом: ронины и знаменосцы
            D.wood += 5; D.steel += 2; D.rice += 10;
            if (D.level[7] > 0) { D.wood += 5; D.steel += 3; D.rice += 10; }
            Save();
        }

        // ---------------------------------------------------------------- армия

        public static void Hire(int unit, int n)
        {
            n = Mathf.Min(n, D.pool[unit], Profile.Gold / Mathf.Max(1, Price[unit]));
            if (n <= 0) return;
            ArmySlot s = D.army.Find(a => a.unit == unit);
            if (s == null) { if (D.army.Count >= 7) { Note("В армии уже 7 отрядов"); return; } s = new ArmySlot { unit = unit }; D.army.Add(s); }
            s.count += n; D.pool[unit] -= n;
            Profile.AddGold(-n * Price[unit]);
            Save();
            UiFx.Play("coins", 0.8f);
        }
        /// <summary>После боя: в армии остаются выжившие.</summary>
        public static void AfterBattle(List<KeyValuePair<int, int>> survivors, bool won)
        {
            D.army.Clear();
            foreach (KeyValuePair<int, int> kv in survivors) if (kv.Value > 0) D.army.Add(new ArmySlot { unit = kv.Key, count = kv.Value });
            if (won) D.honor += 1;
            Save();
        }

        // ---------------------------------------------------------------- экран

        static readonly Color Gold = new Color(0.9f, 0.76f, 0.47f), Ink = new Color(0.93f, 0.89f, 0.8f), Dim = new Color(0.68f, 0.64f, 0.56f);
        static float W, sc, offX, offY;
        static int selPlot = -1, hireUnit = -1, hireN;
        static bool houseSel;
        static string note; static float noteAt;
        static void Note(string s) { note = s; noteAt = Time.unscaledTime; }

        public static void Show() { Open = true; selPlot = -1; hireUnit = -1; houseSel = false; Load(); }

        static Rect Px(Rect r) { return new Rect(offX + r.x * sc, offY + r.y * sc, r.width * sc, r.height * sc); }

        public static void Draw(float w)
        {
            W = w;
            sc = Mathf.Max(W / 1672f, 540f / 941f); offX = (W - 1672f * sc) * 0.5f; offY = (540f - 941f * sc) * 0.5f;
            Texture2D bg = UiKit.Tex("UI/Battle/estate_bg");
            if (bg != null) GUI.DrawTexture(new Rect(offX, offY, 1672f * sc, 941f * sc), bg);
            else UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.1f, 0.08f, 0.06f));
            if (General.Open) { General.Draw(W); return; }   // экран полководца поверх поместья
            Texture2D bt = UiKit.Tex("UI/Battle/estate_buildings");
            bool modal = hireUnit >= 0;
            // главный дом (уже стоит на картине): касание — знаменосцы и ронины
            Rect house = Px(new Rect(620, 260, 480, 230));
            if (!modal && GUI.Button(house, GUIContent.none, GUIStyle.none)) { houseSel = true; selPlot = -1; }
            if (houseSel) Frame(house, Gold, 2f);
            for (int p = 0; p < 9; p++)
            {
                Rect pr = Px(PlotPx[p]);
                if (D.level[p] > 0 && bt != null)
                {
                    int k = PlotArt[p];
                    float bw = pr.width * 0.98f, bh = bw * 0.82f;   // постройка — по ширине площадки, стоит на её нижнем краю
                    Rect br = new Rect(pr.center.x - bw * 0.5f, pr.yMax + 8f * sc - bh, bw, bh);
                    Texture2D bt2 = D.level[p] >= 2 ? UiKit.Tex("UI/Battle/estate_buildings2") : null;   // улучшенные постройки (если лист есть)
                    GUI.DrawTextureWithTexCoords(br, bt2 != null ? bt2 : bt, new Rect((k % 3) / 3f, 1f - (k / 3 + 1) / 3f, 1f / 3f, 1f / 3f));
                    if (D.level[p] >= 2 && bt2 == null) UiKit.Text(new Rect(br.xMax - 30f, br.y, 30f, 18f), "★★", 12f, Gold);
                    if (p == selPlot) Frame(new Rect(br.x - 3f, br.y - 3f, br.width + 6f, pr.yMax - br.y + 10f), Gold, 2f);
                }
                else
                {
                    UiKit.Disc(pr.center, 15f, new Color(0.12f, 0.06f, 0.03f, 0.85f));
                    UiKit.Ring(pr.center, 15f, p == selPlot ? Color.white : Gold);
                    UiKit.Text(new Rect(pr.center.x - 15f, pr.center.y - 16f, 30f, 30f), "+", 22f, Gold);
                }
                if (!modal && GUI.Button(new Rect(pr.x, pr.y - pr.height * 0.6f, pr.width, pr.height * 1.6f), GUIContent.none, GUIStyle.none)) { selPlot = p; houseSel = false; }
            }
            DrawResources();
            DrawArmy();
            if (selPlot >= 0) DrawCard(selPlot);
            else if (houseSel) DrawHouse();
            if (hireUnit >= 0) DrawHire();
            // кнопки
            if (!modal)
            {
                if (VisualIdentity.KitButton(new Rect(W - 150f, 40f, 140f, 34f), "ВЫЙТИ", true, false)) { Open = false; return; }
                if (VisualIdentity.KitButton(new Rect(W - 150f, 80f, 140f, 34f), "НЕДЕЛЯ " + D.week + " ▸", true, false)) { NewWeek(); Note("Неделя " + D.week + ": новые бойцы для найма, доход поместья."); }
                if (VisualIdentity.KitButton(new Rect(W - 150f, 120f, 140f, 34f), "В БОЙ", D.army.Count > 0, true)) { Open = false; Tactics.ShowArmy(Campaign.Region); return; }
                General.Sync();
                if (VisualIdentity.KitButton(new Rect(W - 150f, 160f, 140f, 34f), General.D.pending > 0 ? "ПОЛКОВОДЕЦ ★" : "ПОЛКОВОДЕЦ", true, General.D.pending > 0)) { General.Show(); return; }
            }
            if (note != null && Time.unscaledTime - noteAt < 3f)
            {
                Rect nr = new Rect(W * 0.5f - 280f, 44f, 560f, 28f);
                UiKit.Rect(nr, new Color(0f, 0f, 0f, 0.8f)); UiKit.Text(nr, note, 14f, Gold);
            }
        }

        static void Frame(Rect r, Color c, float th)
        {
            UiKit.Rect(new Rect(r.x, r.y, r.width, th), c); UiKit.Rect(new Rect(r.x, r.yMax - th, r.width, th), c);
            UiKit.Rect(new Rect(r.x, r.y, th, r.height), c); UiKit.Rect(new Rect(r.xMax - th, r.y, th, r.height), c);
        }

        static void Res(int i, float x, string v)
        {
            Texture2D t = UiKit.Tex("UI/Battle/resource_icons");
            if (t != null) GUI.DrawTextureWithTexCoords(new Rect(x, 3f, 28f, 28f), t, new Rect((i % 4) * 0.25f, 1f - (i / 4 + 1) * 0.5f, 0.25f, 0.5f));
            UiKit.Text(new Rect(x + 32f, 3f, 80f, 28f), v, 14f, new Color(0.97f, 0.88f, 0.65f), TextAnchor.MiddleLeft);
        }
        static void DrawResources()
        {
            UiKit.Rect(new Rect(0f, 0f, W, 34f), new Color(0.08f, 0.04f, 0.02f, 0.85f));
            float step = Mathf.Min(120f, (W - 170f) / 6f);
            int soldiers = 0; foreach (ArmySlot a in D.army) soldiers += a.count;
            Res(0, 10f, Profile.Gold.ToString()); Res(1, 10f + step, D.rice.ToString()); Res(2, 10f + step * 2f, D.steel.ToString());
            Res(3, 10f + step * 3f, D.wood.ToString()); Res(4, 10f + step * 4f, D.honor.ToString()); Res(5, 10f + step * 5f, soldiers.ToString());
        }

        static void DrawArmy()
        {
            Rect r = new Rect(8f, 540f - 70f, 7f * 58f + 12f, 62f);
            UiKit.Rect(r, new Color(0.08f, 0.04f, 0.02f, 0.85f));
            UiKit.Text(new Rect(r.x + 6f, r.y - 18f, 200f, 16f), "АРМИЯ", 12f, Gold, TextAnchor.MiddleLeft);
            for (int i = 0; i < 7; i++)
            {
                Rect s = new Rect(r.x + 6f + i * 58f, r.y + 5f, 52f, 52f);
                UiKit.Ring(s.center, 25f, new Color(0.6f, 0.45f, 0.25f));
                if (i >= D.army.Count) continue;
                Tactics.DrawUnitPortrait(s, D.army[i].unit);
                UiKit.Rect(new Rect(s.x + 14f, s.yMax - 14f, 38f, 14f), new Color(0.1f, 0.03f, 0.02f, 0.9f));
                UiKit.Text(new Rect(s.x + 14f, s.yMax - 14f, 38f, 14f), D.army[i].count.ToString(), 11f, new Color(1f, 0.9f, 0.62f));
            }
        }

        static Rect Card() { return new Rect(W - 340f, 540f - 200f, 330f, 190f); }
        static void CardBg(Rect r)
        {
            UiKit.Rect(r, new Color(0.08f, 0.04f, 0.02f, 0.95f));
            Texture2D pt = UiKit.Tex("UI/editor_panel");
            if (pt != null) UiKit.Slice(r, pt, 0.36f, 0.15f, 0f, 0.075f, 0f, Color.white);
        }

        static void DrawCard(int p)
        {
            Building b = Plots[p]; int lv = D.level[p];
            Rect r = Card(); CardBg(r);
            float x = r.x + 18f, w = r.width - 36f, y = r.y + 14f;
            UiKit.Text(new Rect(x, y, w, 22f), b.name + (lv >= 2 ? " ★★" : lv == 1 ? " ★" : ""), 15f, Gold, TextAnchor.MiddleLeft); y += 24f;
            UiKit.Text(new Rect(x, y, w, 30f), b.effect, 11f, Ink, TextAnchor.UpperLeft); y += 32f;
            int u = lv >= 2 && b.unit2 >= 0 ? b.unit2 : b.unit;
            if (lv > 0 && b.unit >= 0)
            {
                string pools = Tactics.UnitName(b.unit) + ": " + D.pool[b.unit] + (lv >= 2 && b.unit2 >= 0 ? " · " + Tactics.UnitName(b.unit2) + ": " + D.pool[b.unit2] : "");
                UiKit.Text(new Rect(x, y, w, 18f), "Можно нанять — " + pools, 11f, new Color(0.7f, 0.9f, 1f), TextAnchor.MiddleLeft);
            }
            y += 22f;
            if (lv < 2) UiKit.Text(new Rect(x, y, w, 18f), (lv == 0 ? "Построить: " : "Улучшить: ") + CostText(b, Mul(lv)), 11f, CanPay(b, Mul(lv)) ? Ink : new Color(1f, 0.55f, 0.45f), TextAnchor.MiddleLeft);
            if (D.builtThisWeek && lv < 2) UiKit.Text(new Rect(x, y + 16f, w, 16f), "На этой неделе уже строили", 10f, Dim, TextAnchor.MiddleLeft);
            float bw = (w - 8f) / 3f, by = r.yMax - 46f;
            if (lv < 2 && VisualIdentity.KitButton(new Rect(x, by, bw * 1.3f, 34f), lv == 0 ? "ПОСТРОИТЬ" : "УЛУЧШИТЬ", !D.builtThisWeek && CanPay(b, Mul(lv)), true)) Build(p);
            if (lv > 0 && b.unit >= 0 && VisualIdentity.KitButton(new Rect(x + bw * 1.3f + 6f, by, bw, 34f), "НАНЯТЬ", D.pool[b.unit] > 0 || (lv >= 2 && b.unit2 >= 0 && D.pool[b.unit2] > 0), false))
            { hireUnit = lv >= 2 && b.unit2 >= 0 && D.pool[b.unit2] > 0 ? b.unit2 : b.unit; hireN = MaxHire(hireUnit); }
            if (lv >= 2 && b.unit2 >= 0 && b.unit >= 0 && VisualIdentity.KitButton(new Rect(x + bw * 2.3f + 12f, by, bw * 0.7f, 34f), "ЕЩЁ", D.pool[b.unit] > 0, false))
            { hireUnit = b.unit; hireN = MaxHire(hireUnit); }
        }

        static void DrawHouse()
        {
            Rect r = Card(); CardBg(r);
            float x = r.x + 18f, w = r.width - 36f, y = r.y + 14f;
            UiKit.Text(new Rect(x, y, w, 22f), "ГЛАВНЫЙ ДОМ", 15f, Gold, TextAnchor.MiddleLeft); y += 24f;
            UiKit.Text(new Rect(x, y, w, 30f), "Каждую неделю: ронины, знаменосцы, +5 дерева, +2 стали, +10 риса", 11f, Ink, TextAnchor.UpperLeft); y += 34f;
            UiKit.Text(new Rect(x, y, w, 18f), "Ронины: " + D.pool[7] + " · знаменосцы: " + D.pool[9], 11f, new Color(0.7f, 0.9f, 1f), TextAnchor.MiddleLeft);
            float bw = (w - 8f) * 0.5f, by = r.yMax - 46f;
            if (VisualIdentity.KitButton(new Rect(x, by, bw, 34f), "РОНИНЫ", D.pool[7] > 0, false)) { hireUnit = 7; hireN = MaxHire(7); }
            if (VisualIdentity.KitButton(new Rect(x + bw + 8f, by, bw, 34f), "ЗНАМЕНОСЦЫ", D.pool[9] > 0, false)) { hireUnit = 9; hireN = MaxHire(9); }
        }

        static int MaxHire(int u) { return Mathf.Max(0, Mathf.Min(D.pool[u], Profile.Gold / Mathf.Max(1, Price[u]))); }

        static bool dragHire;
        static void DrawHire()
        {
            int u = hireUnit;
            Rect r = new Rect(W * 0.5f - 270f, 80f, 540f, 360f);
            Texture2D win = UiKit.Tex("UI/Battle/recruit_window");
            if (win != null) GUI.DrawTexture(r, win); else CardBg(r);
            Rect pf = new Rect(r.x + r.width * 0.075f, r.y + r.height * 0.155f, r.width * 0.31f, r.height * 0.7f);
            Tactics.DrawUnitPortrait(new Rect(pf.center.x - pf.width * 0.45f, pf.y + 10f, pf.width * 0.9f, pf.width * 0.9f), u);
            UiKit.Text(new Rect(pf.x, pf.y + pf.width * 0.9f + 18f, pf.width, 20f), Tactics.UnitName(u).ToUpper(), 13f, Gold);
            UiKit.Text(new Rect(pf.x, pf.y + pf.width * 0.9f + 40f, pf.width, 18f), Price[u] + " мон за бойца", 11f, Ink);
            string[] st = Tactics.UnitStats(u);
            for (int i = 0; i < 4; i++)
                UiKit.Text(new Rect(r.x + r.width * 0.56f, r.y + r.height * (0.15f + 0.122f * i), r.width * 0.37f, r.height * 0.09f), st[i], 12f, Ink, TextAnchor.MiddleLeft);
            if (st.Length > 4 && st[4].Length > 0) UiKit.Text(new Rect(pf.x - 6f, pf.y + pf.width * 0.9f + 58f, pf.width + 12f, 16f), st[4], 9.5f, Dim);
            int max = MaxHire(u);
            // ползунок картины: от 0,445 до 0,93 ширины, на высоте 0,67
            Rect track = new Rect(r.x + r.width * 0.47f, r.y + r.height * 0.645f, r.width * 0.45f, r.height * 0.05f);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && new Rect(track.x - 10f, track.y - 14f, track.width + 20f, track.height + 28f).Contains(e.mousePosition)) { dragHire = true; e.Use(); }
            if (e.rawType == EventType.MouseUp) dragHire = false;
            if (dragHire && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.Used))
                hireN = Mathf.RoundToInt(Mathf.Clamp01((e.mousePosition.x - track.x) / track.width) * max);
            hireN = Mathf.Clamp(hireN, 0, max);
            UiKit.Text(new Rect(track.x, track.y - 26f, track.width, 20f), "Нанять: " + hireN + " из " + D.pool[u] + " · " + hireN * Price[u] + " мон", 12f, Gold);
            if (win == null) { UiKit.Rect(track, new Color(0f, 0f, 0f, 0.5f)); UiKit.Disc(new Vector2(track.x + track.width * (max > 0 ? hireN / (float)max : 0f), track.center.y), 8f, Gold); }
            else UiKit.Disc(new Vector2(track.x + track.width * (max > 0 ? hireN / (float)max : 0f), track.center.y), 7f, new Color(1f, 0.85f, 0.4f, 0.9f));
            Rect b1 = new Rect(r.x + r.width * 0.455f, r.y + r.height * 0.765f, r.width * 0.215f, r.height * 0.1f);
            Rect b2 = new Rect(r.x + r.width * 0.71f, r.y + r.height * 0.765f, r.width * 0.215f, r.height * 0.1f);
            if (VisualIdentity.KitButton(b1, "НАНЯТЬ", hireN > 0, true)) { Hire(u, hireN); hireUnit = -1; }
            if (VisualIdentity.KitButton(b2, "ОТМЕНА", true, false)) hireUnit = -1;
        }
    }
}
