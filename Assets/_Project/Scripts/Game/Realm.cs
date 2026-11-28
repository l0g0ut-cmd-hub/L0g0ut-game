using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Слой приключений «Героев 3» на большой карте Леса теней: календарь (день — бросок кости пути, неделя, месяц, событие недели),
    /// рудники с флагом и доходом, россыпи, сундуки (золото или опыт), нейтральные отряды (бой армией поместья), жилища существ,
    /// обелиски и карта-головоломка с Граалем, рынок (обмен ресурсов), таверна (полководцы и слухи), зал магии (изучение умений),
    /// управа и укрепления поместья. Арт — UI/H3/* (картины владельца). Хранится в PlayerPrefs «realm».
    /// </summary>
    public static class Realm
    {
        [Serializable]
        public class Data
        {
            public int day = 1;
            public int sulfur, crystal, mercury, jade;
            public int hall, fort, mage = 1;          // управа 0…3, укрепления 0…3, этажей зала магии 1…5
            public int week = -1;                     // событие недели (ячейка UI/H3/week_emblems), −1 — нет
            public List<int> done = new List<int>();  // взятые/побеждённые объекты карты
            public List<int> owned = new List<int>(); // наши рудники и жилища
            public int obelisks;                      // битовая маска посещённых обелисков
            public bool grail;
            public int tavernA = -1, tavernB = -1;
            public bool grailBuilt, silo;
            public int stableDay;                     // до какого дня действует конюшня (+1 шаг)
            // вражеский полководец: шаг маршрута, направление, уровень армии, жив ли, день возвращения
            public int egStep, egDir = 1, egLevel = 1, egBack;
            public bool egDead;
            public List<int> enemyOwned = new List<int>();
        }
        static Data d;
        public static Data D { get { if (d == null) Load(); return d; } }
        static void Load()
        {
            try { d = JsonUtility.FromJson<Data>(PlayerPrefs.GetString("realm", "")); } catch { d = null; }
            if (d == null) d = new Data();
            if (d.done == null) d.done = new List<int>();
            if (d.owned == null) d.owned = new List<int>();
            if (d.enemyOwned == null) d.enemyOwned = new List<int>();
        }
        public static void Save() { PlayerPrefs.SetString("realm", JsonUtility.ToJson(D)); PlayerPrefs.Save(); }

        /// <summary>Слой работает на большой карте Леса теней.</summary>
        public static bool Active { get { return Campaign.Region == 0 && MapGeom.TexHt[0] > 1024; } }

        // ---------------------------------------------------------------- объекты карты
        public class Obj
        {
            public int kind, cell; public Vector2 at, anchor;   // 0 рудник, 1 россыпь, 2 сундук, 3 нейтралы, 4 жилище, 5 обелиск, 6 артефакт под охраной
            public Obj(int k, int c, float x, float y, float ax, float ay) { kind = k; cell = c; at = new Vector2(x, y); anchor = new Vector2(ax, ay); }
        }
        public static readonly Obj[] Objs =
        {
            new Obj(0, 0, 0.2197f, 0.8476f, 0.2451f, 0.8756f),   // A1
            new Obj(0, 1, 0.2502f, 0.4942f, 0.2219f, 0.5087f),   // B3
            new Obj(0, 2, 0.3486f, 0.528f, 0.3394f, 0.5933f),   // A4
            new Obj(0, 3, 0.2278f, 0.7251f, 0.1985f, 0.7251f),   // B1
            new Obj(0, 4, 0.6929f, 0.8141f, 0.7021f, 0.7488f),   // N3
            new Obj(0, 5, 0.8696f, 0.5796f, 0.8403f, 0.5796f),   // N7
            new Obj(1, 0, 0.1924f, 0.8131f, 0.2095f, 0.8333f),   // S (у деревни: старт перенесён от храма, карта v16)
            new Obj(1, 1, 0.2558f, 0.9484f, 0.2812f, 0.9204f),   // A2
            new Obj(1, 2, 0.3092f, 0.8448f, 0.2729f, 0.7755f),   // B2
            new Obj(1, 3, 0.44f, 0.6841f, 0.4146f, 0.6561f),   // A5
            new Obj(1, 4, 0.5156f, 0.8837f, 0.5449f, 0.8837f),   // N9
            new Obj(2, 0, 0.2415f, 0.3624f, 0.2698f, 0.3769f),   // X3
            new Obj(2, 1, 0.4136f, 0.7747f, 0.4419f, 0.7892f),   // E
            new Obj(2, 2, 0.0376f, 0.2539f, 0.0659f, 0.2394f),   // X1
            new Obj(2, 3, 0.8184f, 0.7631f, 0.793f, 0.7351f),   // N4
            new Obj(2, 4, 0.1939f, 0.1285f, 0.2222f, 0.143f),   // X9
            new Obj(3, 3, 0.3042f, 0.67f, 0.2759f, 0.6555f),   // A3
            new Obj(3, 11, 0.3144f, 0.5455f, 0.3394f, 0.5933f),   // A4
            new Obj(3, 12, 0.5011f, 0.7949f, 0.4834f, 0.7363f),   // N1
            new Obj(3, 0, 0.5625f, 0.7411f, 0.5908f, 0.7556f),   // N2
            new Obj(3, 10, 0.897f, 0.8103f, 0.8716f, 0.8383f),   // N6
            new Obj(3, 15, 0.9434f, 0.5037f, 0.918f, 0.4757f),   // N8
            new Obj(3, 19, 0.9588f, 0.7065f, 0.968f, 0.7718f),   // X2
            new Obj(3, 2, 0.0601f, 0.0891f, 0.1045f, 0.1381f),   // X4
            new Obj(4, 2, 0.6357f, 0.3636f, 0.6074f, 0.3781f),   // X7
            new Obj(4, 3, 0.3994f, 0.1959f, 0.4248f, 0.2239f),   // X8
            new Obj(4, 4, 0.9563f, 0.164f, 0.9119f, 0.115f),   // X5
            new Obj(4, 5, 0.7058f, 0.2573f, 0.7341f, 0.2718f),   // X6
            new Obj(5, 0, 0.2127f, 0.574f, 0.2219f, 0.5087f),   // B3
            new Obj(5, 1, 0.6162f, 0.7836f, 0.5908f, 0.7556f),   // N2
            new Obj(5, 2, 0.9231f, 0.3541f, 0.9514f, 0.3396f),   // Z
            new Obj(6, 0, 0.8798f, 0.8969f, 0.8716f, 0.8383f),   // N6: артефакт под охраной
            new Obj(6, 4, 0.2358f, 0.852f, 0.2095f, 0.8333f),   // S: артефакт под охраной (правее деревни, карта v16)
            new Obj(6, 8, 0.5367f, 0.9423f, 0.5449f, 0.8837f),   // N9: артефакт под охраной
            new Obj(6, 13, 0.3043f, 0.4429f, 0.2698f, 0.3769f),   // X3: артефакт под охраной
        };
        static readonly int[] ArtGuardFoe = { 6, 9, 3, 4 }; static readonly int[] ArtGuardN = { 4, 14, 8, 10 };
        static int ArtGuard(int i) { int k = 0; for (int j = 0; j < i; j++) if (Objs[j].kind == 6) k++; return k; }
        public static int StepBonus { get { return General.Val(8) + (D.stableDay >= D.day ? 1 : 0); } }
        static readonly string[] MineNames = { "ЗОЛОТОЙ РУДНИК", "ЛЕСОПИЛКА", "ЖЕЛЕЗНЫЙ РУДНИК", "РИСОВЫЕ ТЕРРАСЫ", "СЕРНАЯ ЯМА", "ПЕЩЕРА КРИСТАЛЛОВ" };
        static readonly string[] MineGive = { "+100 мон в день", "+2 дерева в день", "+1 сталь в день", "+4 риса в день", "+1 сера в день", "+1 кристалл в день" };
        static readonly string[] PickNames = { "РОССЫПЬ МОНЕТ", "БРЁВНА", "СЛИТКИ СТАЛИ", "МЕШКИ РИСА", "КРИСТАЛЛЫ" };
        static readonly string[] ChestNames = { "СУНДУК", "ОТКРЫТЫЙ СУНДУК", "ПРОКЛЯТЫЙ СУНДУК", "СВИТОК ПРИЁМОВ", "БРОШЕННЫЙ ЛАГЕРЬ" };
        // нейтралы: ячейка UI/H3/neutrals → отряд врага Tactics.Foes, базовая численность
        static readonly string[] NeutNames = { "Разбойники", "Дезертиры", "Ронины", "Ямабуси", "Маски кицунэ", "Каппы", "Тэнгу", "Дзёрогумо", "Гаки", "Нуэ",
                                              "Они", "Павшие самураи", "Клан теней", "Волки", "Онрё", "Великан Они", "Змей", "Омукадэ", "Девятихвостая", "Мёртвый сёгун" };
        static readonly int[] NeutFoe = { 0, 2, 8, 1, 3, 0, 1, 3, 4, 6, 6, 4, 5, 0, 4, 6, 6, 6, 3, 7 };
        static readonly int[] NeutCount = { 30, 25, 14, 18, 10, 20, 12, 8, 20, 4, 6, 14, 12, 16, 10, 3, 2, 3, 4, 2 };
        static readonly int[] DwellUnit = { 0, 1, 7, 5, 6, 8, 11, 4, 10, 3 };   // ячейка UI/H3/dwellings → отряд (Tactics.Ours)
        static readonly string[] DwellNames = { "ЛАГЕРЬ АСИГАРУ", "СТРЕЛЬБИЩЕ", "ПРИДОРОЖНЫЙ ПОСТОЯЛЫЙ ДВОР", "ВРАТА ГОРНОГО ХРАМА", "ДОМ СИНОБИ", "ПАГОДА ОНМЁДЗИ",
                                                "ПСАРНЯ", "КОНЮШНИ КЛАНА", "ЛОГОВО ОНИ", "СВЯТИЛИЩЕ ПАВШИХ" };
        public static string Size(int n) { return n < 5 ? "несколько" : n < 10 ? "стая" : n < 20 ? "толпа" : n < 50 ? "орда" : "легион"; }
        static int NeutN(int i) { return Mathf.RoundToInt(NeutCount[Objs[i].cell] * (1f + 0.4f * Profile.Cycle) * (D.week == 2 ? 1.5f : 1f)); }

        static bool Near(int i)
        {
            List<MapNode> ns = Campaign.Nodes; int at = Campaign.At;
            if (at < 0 || at >= ns.Count) return false;
            return Mathf.Abs(ns[at].x - Objs[i].anchor.x) < 0.004f && Mathf.Abs(ns[at].y - Objs[i].anchor.y) < 0.005f;
        }
        static bool Gone(int i) { Obj o = Objs[i]; return (o.kind == 1 || o.kind == 2 || o.kind == 3 || o.kind == 6) && D.done.Contains(i); }

        /// <summary>Объекты карты: рисуются после построек; касание — окно объекта (герой должен стоять у места рядом).</summary>
        public static void DrawObjects(Func<Vector2, Vector2> toScreen, float viewX, float viewW, bool input)
        {
            if (!Active) return;
            Texture2D mines = UiKit.Tex("UI/H3/mines"), neut = UiKit.Tex("UI/H3/neutrals"), dw = UiKit.Tex("UI/H3/dwellings"), flags = UiKit.Tex("UI/H3/flags"), ob = UiKit.Tex("UI/MapObjects/objects_dark_0");
            for (int i = 0; i < Objs.Length; i++)
            {
                if (Gone(i)) continue;
                Obj o = Objs[i]; Vector2 p = toScreen(o.at);
                if (p.x < viewX - 80f || p.x > viewX + viewW + 80f) continue;
                float w = 70f, h = 56f; Texture2D t = null; Rect uv = new Rect();
                switch (o.kind)
                {
                    case 0: t = mines; uv = Cell(o.cell, 5, 4); break;
                    case 1: t = mines; uv = Cell(10 + o.cell, 5, 4); w = 52f; h = 42f; break;
                    case 2: t = mines; uv = Cell(15 + o.cell, 5, 4); w = 52f; h = 42f; break;
                    case 3: t = neut; uv = Cell(o.cell, 5, 4); w = 66f; h = 53f; break;
                    case 4: t = dw; uv = Cell(o.cell, 5, 2); w = 74f; h = 59f; break;
                    case 5: t = ob; uv = Cell(9, 5, 4); w = 60f; h = 48f; break;
                    case 6: t = UiKit.Tex("UI/artifacts"); uv = Cell(o.cell, 4, 4); w = 40f; h = 40f; break;
                }
                Rect r = new Rect(p.x - w * 0.5f, p.y - h * 0.9f, w, h);
                if (Event.current.type == EventType.Repaint && t != null)
                {
                    Color oc = GUI.color; GUI.color = o.kind == 5 && (D.obelisks & (1 << o.cell)) != 0 ? new Color(0.75f, 0.75f, 0.8f) : Color.white;
                    GUI.DrawTextureWithTexCoords(r, t, uv); GUI.color = oc;
                    if ((o.kind == 0 || o.kind == 4) && flags != null)   // флаг владельца на шесте: наш — алый, ничей — серый
                    {
                        int f = D.owned.Contains(i) ? 0 : D.enemyOwned.Contains(i) ? 4 : 7;
                        GUI.DrawTextureWithTexCoords(new Rect(r.x + w * 0.02f, r.y - h * 0.12f, w * 0.26f, h * 0.5f), flags, Cell(f, 4, 2));
                    }
                    if (o.kind == 3)
                    {
                        Rect lr = new Rect(p.x - 40f, r.yMax - 4f, 80f, 14f);
                        UiKit.Rect(lr, new Color(0.08f, 0.03f, 0.02f, 0.75f));
                        UiKit.Text(lr, Size(NeutN(i)), 9.5f, new Color(1f, 0.7f, 0.55f));
                    }
                }
                if (input && GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    if (!Near(i)) Note("Доберись до места рядом — тогда можно " + (o.kind == 3 ? "напасть" : "зайти") + ".");
                    else { sel = i; win = Win.Object; }
                }
            }
            DrawEnemy(toScreen, viewX, viewW, input);
            if (Event.current.type == EventType.Repaint && note != null && Time.unscaledTime - noteAt < 3f)
                UiKit.Text(new Rect(viewX, 470f, viewW, 24f), note, 14f, new Color(1f, 0.85f, 0.55f));
        }
        static string note; static float noteAt;
        static void Note(string s) { note = s; noteAt = Time.unscaledTime; }
        static Rect Cell(int k, int cols, int rows) { return new Rect((k % cols) / (float)cols, 1f - (k / cols + 1) / (float)rows, 1f / cols, 1f / rows); }

        // ---------------------------------------------------------------- календарь
        public static int Day { get { return D.day; } }
        public static string DateText { get { int dd = D.day - 1; return "День " + (dd % 7 + 1) + " · неделя " + (dd / 7 % 4 + 1) + " · месяц " + (dd / 28 + 1); } }
        static readonly string[] WeekNames = { "НЕДЕЛЯ ВОЛКА", "НЕДЕЛЯ ЖУРАВЛЯ", "НЕДЕЛЯ ОНИ", "НЕДЕЛЯ ЛИСЫ", "НЕДЕЛЯ МОРА", "НЕДЕЛЯ ИЗОБИЛИЯ",
                                               "НЕДЕЛЯ КЛИНКА", "НЕДЕЛЯ БУРИ", "НЕДЕЛЯ КРОВАВОЙ ЛУНЫ", "НЕДЕЛЯ ДРАКОНА", "НЕДЕЛЯ ПРИЗРАКА", "НЕДЕЛЯ СОЛНЦА" };
        static readonly string[] WeekText =
        {
            "Боевых псов в поместье — вдвое больше.", "Лучников в поместье — вдвое больше.", "Нейтральные отряды на дорогах — в полтора раза сильнее.",
            "Онмёдзи в поместье — вдвое больше.", "Прирост в поместье — вполовину.", "Каждый рудник даёт вдвое больше.",
            "Самураев в поместье — вдвое больше.", "Конница в поместье — вдвое больше.", "Ронинов в поместье — вдвое больше.",
            "+5 кристаллов и +5 серы.", "Синоби в поместье — вдвое больше.", "+1000 мон в казну."
        };
        static readonly int[] WeekUnit = { 11, 1, -1, 8, -1, -1, 3, 4, 7, -1, 6, -1 };
        /// <summary>Новый день (бросок кости пути): доход рудников и управы; каждые 7 — неделя.</summary>
        public static void NewDay()
        {
            D.day++;
            int k = D.week == 5 ? 2 : 1;
            foreach (int i in D.owned)
            {
                if (i < 0 || i >= Objs.Length || Objs[i].kind != 0) continue;
                switch (Objs[i].cell)
                {
                    case 0: Profile.AddGold(100 * k); break; case 1: Estate.D.wood += 2 * k; break; case 2: Estate.D.steel += 1 * k; break;
                    case 3: Estate.D.rice += 4 * k; break; case 4: D.sulfur += k; break; case 5: D.crystal += k; break;
                }
            }
            if (D.hall > 0) Profile.AddGold(new[] { 0, 100, 200, 400 }[Mathf.Clamp(D.hall, 0, 3)]);
            if (D.silo) { D.crystal++; D.sulfur++; }
            if (D.grailBuilt) Profile.AddGold(1000);
            EnemyDay();
            Estate.Save(); Save();
        }
        /// <summary>Новая неделя (после Estate.NewWeek): событие недели, укрепления, жилища на карте.</summary>
        public static void NewWeek()
        {
            D.week = UnityEngine.Random.Range(0, 12);
            float fortK = new[] { 0f, 0.5f, 1f, 1.5f }[Mathf.Clamp(D.fort, 0, 3)] + (D.grailBuilt ? 0.5f : 0f);
            for (int u = 0; u < Tactics.BaseUnits; u++)
            {
                if (Estate.D.pool[u] <= 0) continue;
                int add = Mathf.RoundToInt(Estate.Growth[u] * fortK);
                if (WeekUnit[D.week] == u) add += Estate.Growth[u];
                if (D.week == 4) add -= Estate.Growth[u] / 2;
                Estate.D.pool[u] = Mathf.Max(0, Estate.D.pool[u] + add);
            }
            foreach (int i in D.owned) if (i >= 0 && i < Objs.Length && Objs[i].kind == 4) { int u = DwellUnit[Objs[i].cell]; Estate.D.pool[u] += Estate.Growth[u]; }
            if (D.week == 9) { D.crystal += 5; D.sulfur += 5; }
            if (D.week == 11) Profile.AddGold(1000);
            D.tavernA = D.tavernB = -1;
            Estate.Save(); Save();
            win = Win.Week;
            if (!D.egDead) D.egLevel++;
            // каждый месяц живой вражеский полководец идёт на поместье
            if (!D.egDead && (D.day - 1) / 7 % 4 == 3) pendingSiege = true;
        }

        // ---------------------------------------------------------------- окна
        enum Win { None, Object, Market, Tavern, Hall, Week, Puzzle, Enemy, Siege }
        static Win win; static int sel = -1;
        public static bool Busy { get { return win != Win.None; } }
        public static void OpenMarket() { win = Win.Market; }
        public static void OpenTavern() { win = Win.Tavern; }
        public static void OpenHall() { win = Win.Hall; }
        public static void OpenPuzzle() { win = Win.Puzzle; }
        static readonly Color Gold = new Color(0.9f, 0.76f, 0.47f), Ink = new Color(0.93f, 0.89f, 0.8f), Dim = new Color(0.68f, 0.64f, 0.56f);

        static Rect Fit(float W, float tw, float th, float maxH, out float k)
        {
            k = Mathf.Min((W - 30f) / tw, maxH / th);
            return new Rect((W - tw * k) * 0.5f, (540f - th * k) * 0.5f, tw * k, th * k);
        }
        public static void DrawWindow(float W)
        {
            if (win == Win.None) return;
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.6f));
            switch (win)
            {
                case Win.Object: DrawObject(W); break;
                case Win.Market: DrawMarket(W); break;
                case Win.Tavern: DrawTavern(W); break;
                case Win.Hall: DrawHall(W); break;
                case Win.Week: DrawWeek(W); if (win == Win.None && pendingSiege) { pendingSiege = false; win = Win.Siege; } break;
                case Win.Puzzle: DrawPuzzle(W); break;
                case Win.Enemy: DrawEnemyWin(W, false); break;
                case Win.Siege: DrawEnemyWin(W, true); break;
            }
        }
        static bool Btn(Rect r, string s, bool ok = true, bool red = false) { return VisualIdentity.KitButton(r, s, ok, red); }

        static void DrawObject(float W)
        {
            if (sel < 0 || sel >= Objs.Length) { win = Win.None; return; }
            Obj o = Objs[sel];
            Rect r = new Rect(W * 0.5f - 260f, 130f, 520f, 280f);
            UiKit.Rect(r, new Color(0.08f, 0.04f, 0.02f, 0.97f));
            Texture2D pt = UiKit.Tex("UI/editor_panel"); if (pt != null) UiKit.Slice(r, pt, 0.36f, 0.15f, 0f, 0.075f, 0f, Color.white);
            Rect pic = new Rect(r.x + 24f, r.y + 50f, 170f, 136f);
            string title = "", text = ""; Texture2D t = null; Rect uv = new Rect();
            Texture2D mines = UiKit.Tex("UI/H3/mines");
            switch (o.kind)
            {
                case 0: title = MineNames[o.cell]; text = (D.owned.Contains(sel) ? "Рудник наш. " : "Ничей рудник: поставь свой флаг. ") + MineGive[o.cell] + "."; t = mines; uv = Cell(o.cell, 5, 4); break;
                case 1: title = PickNames[o.cell]; text = "Можно забрать."; t = mines; uv = Cell(10 + o.cell, 5, 4); break;
                case 2: title = ChestNames[o.cell]; text = o.cell == 2 ? "Цепи и череп. Внутри что-то ценное — и проклятое." : o.cell == 3 ? "Свиток тайных приёмов: опыт полководцу." : o.cell == 4 ? "Брошенные припасы." : "Взять золото или раздать воинам (опыт)?"; t = mines; uv = Cell(15 + o.cell, 5, 4); break;
                case 3: { int n = NeutN(sel); title = NeutNames[o.cell].ToUpper(); text = "На дороге — " + Size(n) + " (" + n + "). " + (Estate.D.army.Count > 0 ? "Сразиться армией поместья?" : "Армии нет — найми бойцов в поместье."); t = UiKit.Tex("UI/H3/neutrals"); uv = Cell(o.cell, 5, 4); break; }
                case 4: { int u = DwellUnit[o.cell]; title = DwellNames[o.cell]; text = (D.owned.Contains(sel) ? "Жилище наше: каждую неделю " : "Поставь флаг — каждую неделю ") + Tactics.UnitName(u).ToLower() + " приходят в поместье для найма. Сейчас можно нанять: " + Estate.D.pool[u] + "."; t = UiKit.Tex("UI/H3/dwellings"); uv = Cell(o.cell, 5, 2); break; }
                case 6: { int g = ArtGuard(sel); title = General.Arts[o.cell].name.ToUpper(); text = "Артефакт на земле. Охрана: " + Tactics.FoeName(ArtGuardFoe[g]).ToLower() + ", " + Size(ArtGuardN[g]) + ". " + General.ArtText(o.cell) + "."; t = UiKit.Tex("UI/artifacts"); uv = Cell(o.cell, 4, 4); break; }
                case 5: { int got = 0; for (int b = 0; b < 3; b++) if ((D.obelisks & (1 << b)) != 0) got++; title = "ОБЕЛИСК"; text = "Знаки на камне — часть древней карты. Открыто обелисков: " + got + " из 3."; t = UiKit.Tex("UI/MapObjects/objects_dark_0"); uv = Cell(9, 5, 4); break; }
            }
            if (t != null) GUI.DrawTextureWithTexCoords(pic, t, uv);
            UiKit.Text(new Rect(r.x + 20f, r.y + 14f, r.width - 40f, 26f), title, 17f, Gold);
            UiKit.Text(new Rect(pic.xMax + 16f, r.y + 54f, r.xMax - pic.xMax - 36f, 130f), text, 12f, Ink, TextAnchor.UpperLeft);
            float by = r.yMax - 52f, bx = r.x + 24f;
            switch (o.kind)
            {
                case 0:
                    if (!D.owned.Contains(sel) && Btn(new Rect(bx, by, 200f, 36f), "ПОСТАВИТЬ ФЛАГ", true, true)) { D.owned.Add(sel); Save(); UiFx.Play("ui_taiko_heavy", 0.5f); win = Win.None; return; }
                    break;
                case 1:
                    if (Btn(new Rect(bx, by, 200f, 36f), "ЗАБРАТЬ", true, true))
                    {
                        switch (o.cell) { case 0: Profile.AddGold(600); break; case 1: Estate.D.wood += 10; break; case 2: Estate.D.steel += 6; break; case 3: Estate.D.rice += 20; break; case 4: D.crystal += 4; break; }
                        D.done.Add(sel); Estate.Save(); Save(); UiFx.Play("coins", 0.8f); win = Win.None; return;
                    }
                    break;
                case 2:
                    if (o.cell <= 1)
                    {
                        if (Btn(new Rect(bx, by, 170f, 36f), "1000 МОН", true, true)) { Profile.AddGold(1000); Take(); return; }
                        if (Btn(new Rect(bx + 180f, by, 170f, 36f), "ОПЫТ 500")) { Exp(500); Take(); return; }
                    }
                    else if (Btn(new Rect(bx, by, 200f, 36f), "ОТКРЫТЬ", true, true))
                    {
                        if (o.cell == 2) { int a = General.RandomArt(); if (General.Give(a)) Note("Артефакт: " + General.Arts[a].name + ". Проклятие забрало 300 мон."); Profile.AddGold(-Mathf.Min(Profile.Gold, 300)); }
                        else if (o.cell == 3) Exp(800);
                        else { Estate.D.rice += 10; Estate.D.wood += 5; Profile.AddGold(300); Estate.Save(); }
                        Take(); return;
                    }
                    break;
                case 3:
                {
                    int n = NeutN(sel), ours = ArmyPower(), theirs = Tactics.FoePower(NeutFoe[o.cell]) * n;
                    int joinU = JoinUnit(o.cell);
                    if (ours >= theirs * 4)
                    {   // слабые бегут при виде армии
                        UiKit.Text(new Rect(pic.xMax + 16f, r.y + 160f, r.xMax - pic.xMax - 36f, 40f), "Отряд дрогнул и готов бежать.", 12f, Gold, TextAnchor.UpperLeft);
                        if (Btn(new Rect(bx + 210f, by, 160f, 36f), "ОТПУСТИТЬ")) { D.done.Add(sel); Save(); win = Win.None; return; }
                    }
                    else if (joinU >= 0 && ours >= theirs * 2)
                    {   // люди просятся на службу
                        int cost = n * Estate.Price[joinU];
                        UiKit.Text(new Rect(pic.xMax + 16f, r.y + 160f, r.xMax - pic.xMax - 36f, 40f), "Просятся на службу за " + cost + " мон.", 12f, Gold, TextAnchor.UpperLeft);
                        if (Btn(new Rect(bx + 210f, by, 160f, 36f), "ПРИНЯТЬ", Profile.Gold >= cost))
                        { if (Estate.AddTroops(joinU, n)) { Profile.AddGold(-cost); D.done.Add(sel); Save(); Note("К армии примкнули: " + n + "."); } else Note("В армии уже 7 отрядов."); win = Win.None; return; }
                    }
                }
                    if (Btn(new Rect(bx, by, 200f, 36f), "СРАЗИТЬСЯ", Estate.D.army.Count > 0, true))
                    {
                        int idx = sel; win = Win.None;
                        Tactics.ShowNeutral(NeutFoe[o.cell], NeutN(idx), won =>
                        {
                            if (!won) return;
                            D.done.Add(idx); Save();
                            Profile.AddGold(50 * NeutCount[Objs[idx].cell]);
                        });
                        return;
                    }
                    break;
                case 4:
                {
                    int u = DwellUnit[o.cell];
                    if (!D.owned.Contains(sel) && Btn(new Rect(bx, by, 200f, 36f), "ПОСТАВИТЬ ФЛАГ", true, true)) { D.owned.Add(sel); Estate.D.pool[u] += Estate.Growth[u]; Estate.Save(); Save(); }
                    int n = Mathf.Min(Estate.D.pool[u], Profile.Gold / Mathf.Max(1, Estate.Price[u]));
                    if (Btn(new Rect(bx + 210f, by, 170f, 36f), "НАНЯТЬ " + n, n > 0)) { Estate.Hire(u, n); Note("Нанято: " + n + " — в армии поместья."); }
                    break;
                }
                case 6:
                    if (Btn(new Rect(bx, by, 260f, 36f), "СРАЗИТЬСЯ С ОХРАНОЙ", Estate.D.army.Count > 0, true))
                    {
                        int idx = sel, g = ArtGuard(sel); win = Win.None;
                        Tactics.ShowNeutral(ArtGuardFoe[g], Mathf.RoundToInt(ArtGuardN[g] * (1f + 0.4f * Profile.Cycle)), won =>
                        {
                            if (!won) return;
                            D.done.Add(idx); Save();
                            if (!General.Give(Objs[idx].cell)) Note("Рюкзак полон — артефакт оставлен.");
                        });
                        return;
                    }
                    break;
                case 5:
                    if ((D.obelisks & (1 << o.cell)) == 0 && Btn(new Rect(bx, by, 200f, 36f), "ПРОЧЕСТЬ ЗНАКИ", true, true)) { D.obelisks |= 1 << o.cell; Save(); win = Win.Puzzle; return; }
                    if (Btn(new Rect(bx + 210f, by, 170f, 36f), "КАРТА")) { win = Win.Puzzle; return; }
                    break;
            }
            if (Btn(new Rect(r.xMax - 150f, by, 126f, 36f), "УЙТИ")) win = Win.None;
        }
        static int ArmyPower() { int v = 0; foreach (Estate.ArmySlot a in Estate.D.army) v += a.count * Tactics.OursPower(a.unit); return v; }
        static int JoinUnit(int cell) { switch (cell) { case 0: case 1: return 0; case 2: return 7; case 3: return 5; case 4: return 8; } return -1; }
        static void Take() { D.done.Add(sel); Save(); UiFx.Play("coins", 0.8f); win = Win.None; }
        static void Exp(int v) { int up = Profile.AddXp(v); General.Sync(); Note("+" + v + " опыта" + (up > 0 ? " — новый уровень!" : "")); }

        // ---------------------------------------------------------------- рынок
        static readonly string[] ResNames = { "МОНЫ", "РИС", "СТАЛЬ", "ДЕРЕВО", "СЕРА", "КРИСТАЛЛЫ", "РТУТЬ", "НЕФРИТ" };
        static readonly int[] ResValue = { 1, 25, 60, 30, 120, 150, 130, 140 };   // цена в монах
        static int Res(int i)
        {
            switch (i) { case 0: return Profile.Gold; case 1: return Estate.D.rice; case 2: return Estate.D.steel; case 3: return Estate.D.wood; case 4: return D.sulfur; case 5: return D.crystal; case 6: return D.mercury; default: return D.jade; }
        }
        static void AddRes(int i, int v)
        {
            switch (i) { case 0: Profile.AddGold(v); break; case 1: Estate.D.rice += v; break; case 2: Estate.D.steel += v; break; case 3: Estate.D.wood += v; break; case 4: D.sulfur += v; break; case 5: D.crystal += v; break; case 6: D.mercury += v; break; default: D.jade += v; break; }
        }
        public static void ResIcon(Rect r, int i)
        {
            if (i < 4) { Texture2D t = UiKit.Tex("UI/Battle/resource_icons"); int[] map = { 0, 1, 2, 3 }; int k = map[i]; if (t != null) GUI.DrawTextureWithTexCoords(r, t, new Rect((k % 4) * 0.25f, 1f - (k / 4 + 1) * 0.5f, 0.25f, 0.5f)); }
            else { Texture2D t = UiKit.Tex("UI/H3/resource_icons2"); int k = i - 4; if (t != null) GUI.DrawTextureWithTexCoords(r, t, new Rect(k * 0.25f, 0.5f, 0.25f, 0.5f)); }
        }
        static int give = 0, take = 5; static float amount = 0.25f; static bool drag;
        static float Rate { get { return 0.35f + 0.15f * Mathf.Clamp(Estate.D.level[7], 0, 2) + (D.hall >= 3 ? 0.1f : 0f); } }
        static void DrawMarket(float W)
        {
            float k; Rect r = Fit(W, 1672f, 941f, 520f, out k);
            Texture2D t = UiKit.Tex("UI/H3/market"); if (t != null) GUI.DrawTexture(r, t);
            Func<float, float, float, float, Rect> Px = (x, y, w, h) => new Rect(r.x + x * k, r.y + y * k, w * k, h * k);
            UiKit.Text(Px(590, 72, 490, 66), "РЫНОК · курс " + Mathf.RoundToInt(Rate * 100f) + "%", 13f, Gold);
            float[] cx = { 120f, 260f, 398f, 535f };
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < 8; i++)
                {
                    Rect s = Px(cx[i % 4] + side * 910f, i < 4 ? 332f : 486f, 118f, 100f);
                    ResIcon(new Rect(s.x + s.width * 0.15f, s.y + s.height * 0.05f, s.width * 0.7f, s.height * 0.7f), i);
                    UiKit.Text(new Rect(s.x, s.yMax - 2f, s.width, 13f), side == 0 ? Res(i).ToString() : ResNames[i], 8.5f, Ink);
                    bool on = side == 0 ? give == i : take == i;
                    if (on) UiKit.Ring(s.center, s.width * 0.42f, Gold, true);
                    if (GUI.Button(s, GUIContent.none, GUIStyle.none)) { if (side == 0) give = i; else take = i; }
                }
            // ползунок
            Rect track = Px(380, 690, 655, 36);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && track.Contains(e.mousePosition)) drag = true;
            if (e.type == EventType.MouseUp) drag = false;
            if (drag && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown)) { amount = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width); e.Use(); }
            UiKit.Rect(new Rect(track.x + track.width * amount - 4f, track.y, 8f, track.height), Gold);
            int n = Mathf.FloorToInt(Res(give) * amount);
            int got = give == take ? 0 : Mathf.FloorToInt(n * (float)ResValue[give] / ResValue[take] * Rate);
            UiKit.Text(Px(1080, 675, 235, 66), n + " → " + got, 11f, Ink);
            if (GUI.Button(Px(405, 768, 390, 84), GUIContent.none, GUIStyle.none) && got > 0) { AddRes(give, -n); AddRes(take, got); Estate.Save(); Save(); UiFx.Play("coins", 0.8f); amount = 0f; }
            UiKit.Text(Px(405, 768, 390, 84), "ОБМЕНЯТЬ", 14f, got > 0 ? Gold : Dim);
            if (GUI.Button(Px(878, 768, 390, 84), GUIContent.none, GUIStyle.none)) win = Win.None;
            UiKit.Text(Px(878, 768, 390, 84), "ЗАКРЫТЬ", 14f, Gold);
        }

        // ---------------------------------------------------------------- таверна
        static readonly string[] HeroNames = { "Старый даймё Ода", "Томоэ из Каи", "Одноглазый Масамунэ", "Онмёдзи Сэймэй", "Куноити Тиё", "Монах Бэнкэй", "Купец Ёдоя", "Мёртвый князь Тайра" };
        static readonly int[] HeroStat = { 1, 0, 0, 3, 0, 1, 3, 2 };
        static readonly int[] HeroSkill = { 3, 1, 1, 5, 7, 2, 4, 6 };
        static readonly string[] Rumors =
        {
            "Говорят, у каменных арок под мостом кто-то копал по ночам.", "В пещере кристаллов слышат пение — там уже никого нет.",
            "Мёртвый сёгун ждёт у тория. С ним — его стража.", "Обелиски помнят старую карту. Прочти все три.",
            "Рынок в поместье даст лучший курс, если его расширить.", "Каппы боятся огня, но любят огурцы. И пальцы."
        };
        static void DrawTavern(float W)
        {
            if (D.tavernA < 0) { D.tavernA = UnityEngine.Random.Range(0, 8); D.tavernB = (D.tavernA + UnityEngine.Random.Range(1, 8)) % 8; Save(); }
            float k; Rect r = Fit(W, 1672f, 941f, 520f, out k);
            Texture2D t = UiKit.Tex("UI/H3/tavern"); if (t != null) GUI.DrawTexture(r, t);
            Func<float, float, float, float, Rect> Px = (x, y, w, h) => new Rect(r.x + x * k, r.y + y * k, w * k, h * k);
            UiKit.Text(Px(550, 72, 570, 66), "ТАВЕРНА", 15f, Gold);
            Texture2D hp = UiKit.Tex("UI/Battle/hero_portraits");
            int[] who = { D.tavernA, D.tavernB }; float[] cx = { 412f, 885f };
            for (int q = 0; q < 2; q++)
            {
                int h = who[q];
                Rect pr = Px(cx[q] - 140f, 225f, 280f, 280f);
                if (hp != null) GUI.DrawTextureWithTexCoords(pr, hp, new Rect((h % 4) * 0.25f, h < 4 ? 0.5f : 0f, 0.25f, 0.5f));
                UiKit.Text(Px(cx[q] - 175f, 550f, 350f, 70f), HeroNames[h], 10.5f, Ink);
                UiKit.Text(Px(cx[q] - 130f, 640f, 260f, 60f), "2500 мон", 10f, Gold);
                if (GUI.Button(pr, GUIContent.none, GUIStyle.none)) sel = q;
                if (sel == q) UiKit.Ring(pr.center, pr.width * 0.5f, Gold, true);
            }
            UiKit.Text(Px(1250, 280, 280, 340), Rumors[(D.day / 7) % Rumors.Length], 10f, new Color(0.25f, 0.15f, 0.08f), TextAnchor.MiddleCenter, false);
            int pick = sel == 0 || sel == 1 ? who[sel] : -1;
            bool ok = pick >= 0 && Profile.Gold >= 2500;
            if (GUI.Button(Px(300, 745, 480, 105), GUIContent.none, GUIStyle.none) && ok)
            {   // новый полководец ведёт армию: его облик, черта и навык
                Profile.AddGold(-2500);
                General.D.face = pick;
                int st = HeroStat[pick]; if (st == 0) General.D.atk++; else if (st == 1) General.D.def++; else if (st == 2) General.D.power++; else General.D.know++;
                if (General.D.skill[HeroSkill[pick]] == 0 && General.Known < General.MaxSkills) General.D.skill[HeroSkill[pick]] = 1;
                General.Save(); D.tavernA = D.tavernB = -1; Save(); Note("Армию ведёт " + HeroNames[pick] + "."); win = Win.None; return;
            }
            UiKit.Text(Px(300, 745, 480, 105), "НАНЯТЬ", 14f, ok ? Gold : Dim);
            if (GUI.Button(Px(890, 745, 485, 105), GUIContent.none, GUIStyle.none)) { sel = -1; win = Win.None; }
            UiKit.Text(Px(890, 745, 485, 105), "ЗАКРЫТЬ", 14f, Gold);
        }

        // ---------------------------------------------------------------- зал магии: школы и этажи
        static readonly string[] Schools = { "ОГОНЬ", "ВОДА", "ЗЕМЛЯ", "ВОЗДУХ" };
        // умение (номер в Tactics.Spells) → школа и этаж (1…5)
        static readonly int[] SpellSchool = { 0, 3, 2, 2, 0, 3, 1, 1, 0, 2, 3, 1, 2, 0, 2, 3, 3, 3, 1, 3, 3, 0, 1 };
        static readonly int[] SpellFloor = { 1, 1, 1, 2, 2, 2, 1, 2, 3, 3, 1, 2, 4, 2, 3, 3, 3, 4, 3, 2, 5, 4, 5 };
        /// <summary>Знает ли полководец умение (зал магии построен до его этажа).</summary>
        public static int SchoolOf(int spell) { return spell >= 0 && spell < SpellSchool.Length ? SpellSchool[spell] : 0; }
        public static string SchoolName(int school) { return Schools[Mathf.Clamp(school, 0, Schools.Length - 1)]; }
        public static bool Knows(int spell) { return spell < 0 || spell >= SpellFloor.Length || SpellFloor[spell] <= Mathf.Max(1, D.mage); }
        static readonly int[] FloorGold = { 0, 0, 1000, 2000, 3000, 5000 }, FloorCrystal = { 0, 0, 3, 6, 10, 15 };
        static int school;
        static void DrawHall(float W)
        {
            float k; Rect r = Fit(W, 1672f, 941f, 520f, out k);
            Texture2D t = UiKit.Tex("UI/H3/mage_hall"); if (t != null) GUI.DrawTexture(r, t);
            Func<float, float, float, float, Rect> Px = (x, y, w, h) => new Rect(r.x + x * k, r.y + y * k, w * k, h * k);
            UiKit.Text(Px(605, 72, 455, 62), "ЗАЛ МАГИИ · этажей " + D.mage, 13f, Gold);
            for (int s = 0; s < 4; s++)
            {
                Rect tr = Px(72, 178 + s * 125f, 285, 98);
                UiKit.Text(tr, Schools[s], 13f, s == school ? Gold : Dim);
                if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) school = s;
            }
            // этажи сверху вниз: 5…1
            float[] fy = { 228f, 348f, 468f, 595f, 718f }, fx0 = { 750f, 740f, 722f, 712f, 692f }, fdx = { 87f, 103f, 116f, 132f, 145f };
            for (int f = 1; f <= 5; f++)
            {
                int row = 5 - f, q = 0;
                for (int sp = 0; sp < SpellFloor.Length && q < 3; sp++)
                {
                    if (SpellSchool[sp] != school || SpellFloor[sp] != f) continue;
                    Vector2 c = new Vector2(r.x + (fx0[row] + q * fdx[row]) * k, r.y + fy[row] * k); float rad = 38f * k;
                    Color oc = GUI.color; GUI.color = f <= D.mage ? Color.white : new Color(0.35f, 0.35f, 0.4f, 0.8f);
                    Tactics.DrawSpellIcon(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), sp);
                    GUI.color = oc;
                    if (GUI.Button(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), GUIContent.none, GUIStyle.none)) Note(Tactics.SpellName(sp) + (f <= D.mage ? " — изучено" : " — нужен " + f + "-й этаж"));
                    q++;
                }
            }
            bool can = D.mage < 5 && Profile.Gold >= FloorGold[D.mage + 1] && D.crystal >= FloorCrystal[D.mage + 1];
            string lbl = D.mage < 5 ? "ЭТАЖ " + (D.mage + 1) + ": " + FloorGold[D.mage + 1] + " мон, " + FloorCrystal[D.mage + 1] + " крист." : "ВСЕ ЭТАЖИ";
            Rect b = Px(498, 790, 680, 80);
            UiKit.Text(b, lbl, 11f, can ? Gold : Dim);
            if (GUI.Button(b, GUIContent.none, GUIStyle.none) && can) { Profile.AddGold(-FloorGold[D.mage + 1]); D.crystal -= FloorCrystal[D.mage + 1]; D.mage++; Save(); UiFx.Play("ui_taiko_heavy", 0.6f); }
            if (Btn(new Rect(r.xMax - 140f, r.y + 8f, 120f, 30f), "ЗАКРЫТЬ")) win = Win.None;
            if (note != null && Time.unscaledTime - noteAt < 3f) UiKit.Text(new Rect(r.x, r.yMax - 24f, r.width, 20f), note, 12f, Gold);
        }

        // ---------------------------------------------------------------- событие недели
        static void DrawWeek(float W)
        {
            if (D.week < 0) { win = Win.None; return; }
            float k; Rect r = Fit(W, 1536f, 1024f, 470f, out k);
            Texture2D t = UiKit.Tex("UI/H3/week_window"); if (t != null) GUI.DrawTexture(r, t);
            Func<float, float, float, float, Rect> Px = (x, y, w, h) => new Rect(r.x + x * k, r.y + y * k, w * k, h * k);
            UiKit.Text(Px(240, 60, 1056, 110), WeekNames[D.week], 17f, Gold);
            Texture2D em = UiKit.Tex("UI/H3/week_emblems");
            if (em != null) GUI.DrawTextureWithTexCoords(Px(605, 232, 326, 326), em, Cell(D.week, 4, 3));
            UiKit.Text(Px(200, 615, 1135, 160), DateText + ". " + WeekText[D.week], 12f, new Color(0.22f, 0.12f, 0.06f), TextAnchor.MiddleCenter, false);
            Rect b = Px(425, 810, 685, 105);
            UiKit.Text(b, "ДАЛЬШЕ", 15f, Gold);
            if (GUI.Button(b, GUIContent.none, GUIStyle.none)) win = Win.None;
        }

        // ---------------------------------------------------------------- карта-головоломка и Грааль
        static readonly Vector2 GrailAt = new Vector2(0.5908f, 0.7556f);   // место N2 у моста — рядом крестик на карте
        static void DrawPuzzle(float W)
        {
            float k; Rect r = Fit(W, 2001f, 786f, 470f, out k);
            Texture2D t = UiKit.Tex("UI/H3/puzzle_map");
            if (t != null) GUI.DrawTexture(r, t);
            // 18 кусков (6×3): каждый обелиск открывает свои 6
            for (int i = 0; i < 18; i++)
            {
                int owner = (i * 7 + 3) % 3;
                if ((D.obelisks & (1 << owner)) != 0) continue;
                Rect pc = new Rect(r.x + (i % 6) * r.width / 6f, r.y + (i / 6) * r.height / 3f, r.width / 6f + 1f, r.height / 3f + 1f);
                UiKit.Rect(pc, new Color(0.07f, 0.05f, 0.04f, 0.97f));
                UiKit.Text(pc, "?", 22f, new Color(0.5f, 0.4f, 0.3f));
            }
            bool all = D.obelisks == 7;
            UiKit.Text(new Rect(r.x, r.yMax + 4f, r.width, 20f), D.grail ? "Грааль найден." : all ? "Карта собрана: копай у крестика — у моста за каменными арками." : "Прочти все обелиски, чтобы собрать карту.", 12f, Gold);
            List<MapNode> ns = Campaign.Nodes; int at = Campaign.At;
            bool here = at >= 0 && at < ns.Count && Mathf.Abs(ns[at].x - GrailAt.x) < 0.004f && Mathf.Abs(ns[at].y - GrailAt.y) < 0.005f;
            if (all && !D.grail && Btn(new Rect(r.x + 10f, r.y + 8f, 150f, 32f), "КОПАТЬ", here, true))
            {
                D.grail = true; General.D.atk += 3; General.D.def += 3; General.D.power += 3; General.D.know += 3; General.Save();
                Profile.AddGold(5000); Save(); Note("ГРААЛЬ! +3 ко всем параметрам полководца, 5000 мон.");
            }
            if (Btn(new Rect(r.xMax - 130f, r.y + 8f, 120f, 32f), "ЗАКРЫТЬ")) win = Win.None;
        }

        // ---------------------------------------------------------------- строка ресурсов и даты на карте
        const float HudRow = 24f;
        const int HudResources = 8;

        public static float HudHeight(float width)
        {
            if (width >= 580f) return HudRow;
            return width >= 320f ? 2f * HudRow : 3f * HudRow;
        }

        public static void DrawHud(Rect at, bool input)
        {
            if (!Active || at.width <= 0f) return;
            UiKit.Rect(at, new Color(0f, 0f, 0f, 0.55f));
            Rect button = new Rect(at.xMax - 72f, at.y + 2f, 68f, HudRow - 4f);
            bool inline = at.height == HudRow;
            float dateWidth = inline ? 158f : button.x - at.x - 10f;
            UiKit.Text(new Rect(at.x + 6f, at.y, dateWidth, HudRow), DateText, 11f, Gold, TextAnchor.MiddleLeft);
            int columns = at.height > 2f * HudRow ? 4 : HudResources;
            float left = at.x + 6f + (inline ? dateWidth + 4f : 0f);
            float right = inline ? button.x - 5f : at.xMax - 6f;
            float slot = (right - left) / columns;
            for (int i = 0; i < HudResources; i++)
            {
                float x = left + i % columns * slot;
                float y = at.y + (inline ? 0f : HudRow) + i / columns * HudRow;
                ResIcon(new Rect(x, y + 3f, 18f, 18f), i);
                UiKit.Text(new Rect(x + 19f, y, slot - 20f, HudRow),
                    Res(i).ToString(), 10f, Ink, TextAnchor.MiddleLeft);
            }
            if (VisualIdentity.KitButton(button, "КАРТА", input, false)) win = Win.Puzzle;
        }

        // ---------------------------------------------------------------- вражеский полководец («Герои 3»)
        // ходит по маршруту через карту (туда и обратно), захватывает наши рудники и жилища, нападает на армию, раз в месяц — на поместье
        static readonly Vector2[] EnemyRoute =
        {
            new Vector2(0.9514f, 0.3396f), new Vector2(0.918f, 0.4757f), new Vector2(0.8403f, 0.5796f), new Vector2(0.793f, 0.7351f), new Vector2(0.7021f, 0.7488f),
            new Vector2(0.5908f, 0.7556f), new Vector2(0.4834f, 0.7363f), new Vector2(0.4146f, 0.6561f), new Vector2(0.3394f, 0.5933f), new Vector2(0.2759f, 0.6555f), new Vector2(0.1985f, 0.7251f)
        };
        static bool pendingSiege;
        static Vector2 EnemyAt { get { return EnemyRoute[Mathf.Clamp(D.egStep, 0, EnemyRoute.Length - 1)]; } }
        static bool HeroAt(Vector2 p)
        {
            List<MapNode> ns = Campaign.Nodes; int at = Campaign.At;
            return at >= 0 && at < ns.Count && Mathf.Abs(ns[at].x - p.x) < 0.004f && Mathf.Abs(ns[at].y - p.y) < 0.005f;
        }
        static void EnemyDay()
        {
            if (D.egDead) { if (D.day >= D.egBack) { D.egDead = false; D.egStep = 0; D.egDir = 1; D.egLevel += 2; } return; }
            int ns = D.egStep + D.egDir;
            if (ns < 0 || ns >= EnemyRoute.Length) { D.egDir = -D.egDir; ns = D.egStep + D.egDir; }
            D.egStep = Mathf.Clamp(ns, 0, EnemyRoute.Length - 1);
            Vector2 p = EnemyAt;
            for (int i = 0; i < Objs.Length; i++)
            {   // захват рудников и жилищ у места, где он встал
                if (Objs[i].kind != 0 && Objs[i].kind != 4) continue;
                if ((Objs[i].anchor - p).sqrMagnitude > 0.0001f) continue;
                if (D.owned.Remove(i) || !D.enemyOwned.Contains(i)) { if (!D.enemyOwned.Contains(i)) D.enemyOwned.Add(i); Note("Вражеский полководец захватил " + (Objs[i].kind == 0 ? MineNames[Objs[i].cell].ToLower() : DwellNames[Objs[i].cell].ToLower()) + "."); }
            }
            if (HeroAt(p)) win = Win.Enemy;
        }
        static void DrawEnemy(Func<Vector2, Vector2> toScreen, float viewX, float viewW, bool input)
        {
            if (D.egDead) return;
            Vector2 p = toScreen(EnemyAt);
            if (p.x < viewX - 80f || p.x > viewX + viewW + 80f) return;
            Texture2D t = UiKit.Tex("UI/H3/neutrals"), f = UiKit.Tex("UI/H3/flags");
            Rect r = new Rect(p.x - 34f, p.y - 62f, 68f, 54f);
            if (Event.current.type == EventType.Repaint)
            {
                if (t != null) GUI.DrawTextureWithTexCoords(r, t, Cell(19, 5, 4));
                if (f != null) GUI.DrawTextureWithTexCoords(new Rect(r.xMax - 14f, r.y - 14f, 18f, 32f), f, Cell(4, 4, 2));
                Rect lr = new Rect(p.x - 52f, r.yMax - 2f, 104f, 14f);
                UiKit.Rect(lr, new Color(0.15f, 0.05f, 0.18f, 0.8f)); UiKit.Text(lr, "ВРАГ · ур. " + D.egLevel, 9.5f, new Color(0.85f, 0.7f, 1f));
            }
            if (input && GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (HeroAt(EnemyAt)) win = Win.Enemy;
                else Note("Вражеский полководец (ур. " + D.egLevel + "). Встань на его место — будет бой.");
            }
        }
        static void DrawEnemyWin(float W, bool siege)
        {
            Rect r = new Rect(W * 0.5f - 260f, 140f, 520f, 250f);
            UiKit.Rect(r, new Color(0.1f, 0.03f, 0.08f, 0.97f));
            Texture2D pt = UiKit.Tex("UI/editor_panel"); if (pt != null) UiKit.Slice(r, pt, 0.36f, 0.15f, 0f, 0.075f, 0f, Color.white);
            Texture2D t = UiKit.Tex("UI/H3/neutrals");
            if (t != null) GUI.DrawTextureWithTexCoords(new Rect(r.x + 24f, r.y + 50f, 150f, 120f), t, Cell(19, 5, 4));
            UiKit.Text(new Rect(r.x, r.y + 14f, r.width, 26f), siege ? "ОСАДА ПОМЕСТЬЯ" : "ВРАЖЕСКИЙ ПОЛКОВОДЕЦ", 17f, new Color(0.9f, 0.7f, 1f));
            string txt = siege ? "Армия врага (ур. " + D.egLevel + ") у стен поместья. Стены: " + new[] { "частокол", "форт", "крепость", "замок" }[Mathf.Clamp(D.fort, 0, 3)] + ". Обороняйся — или откупись."
                               : "Он преградил путь (армия ур. " + D.egLevel + "). Бой армией поместья — или отступить на прежнее место.";
            UiKit.Text(new Rect(r.x + 190f, r.y + 54f, r.width - 210f, 120f), txt, 12f, Ink, TextAnchor.UpperLeft);
            float by = r.yMax - 52f;
            if (Btn(new Rect(r.x + 24f, by, 200f, 36f), siege ? "ОБОРОНЯТЬСЯ" : "СРАЗИТЬСЯ", true, true))
            {
                win = Win.None;
                System.Action<bool> done = won =>
                {
                    if (won) { D.egDead = true; D.egBack = D.day + 14; D.enemyOwned.Clear(); Profile.AddGold(1500 + 500 * D.egLevel); int a = General.RandomArt(); General.Give(a); Note("Вражеский полководец разбит! Его земли свободны. Трофей: " + General.Arts[a].name + "."); }
                    else if (siege) { Estate.D.rice = Estate.D.rice / 2; Estate.D.steel /= 2; Estate.D.wood /= 2; for (int u = 0; u < Estate.D.pool.Length; u++) Estate.D.pool[u] /= 2; Estate.Save(); Note("Поместье разграблено."); }
                    Save();
                };
                if (siege) Tactics.ShowSiegeDefense(D.egLevel, done); else Tactics.ShowWarband(D.egLevel, done);
                return;
            }
            int ransom = 800 * D.egLevel;
            if (siege && Btn(new Rect(r.x + 234f, by, 150f, 36f), "ОТКУП " + ransom, Profile.Gold >= ransom)) { Profile.AddGold(-ransom); win = Win.None; Note("Враг ушёл с выкупом."); return; }
            if (!siege && Btn(new Rect(r.x + 234f, by, 150f, 36f), "ОТСТУПИТЬ")) { D.egStep = Mathf.Clamp(D.egStep - D.egDir, 0, EnemyRoute.Length - 1); win = Win.None; }
        }
        /// <summary>Здание Грааля в поместье (после раскопок): +1000 мон в день, прирост +50%.</summary>
        public static bool CanBuildGrail { get { return D.grail && !D.grailBuilt; } }
        public static void BuildGrail() { if (!CanBuildGrail) return; D.grailBuilt = true; Save(); }
        public static void BuildSilo() { if (D.silo || Profile.Gold < 2000) return; Profile.AddGold(-2000); D.silo = true; Save(); }
    }
}
