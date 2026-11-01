using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// Карта похода между боями (в духе «Героев»): нарисованная область, дороги тушью, медальоны узлов, всадник едет по дороге.
    /// Справа — панель: область, герой, выбранный узел, кнопки. Пришёл в узел — карточка: бой, храм, кузнец, торговец, тайник,
    /// привал, событие с выбором. Дальние узлы скрыты туманом, пока не подойдёшь. Поверх боя (бой стоит), под экраном снаряжения.
    /// </summary>
    public class MapScreen : MonoBehaviour
    {
        public static bool Open { get; private set; }
        static MapScreen inst;

        public static void Ensure() { if (inst == null) inst = new GameObject("MapScreen").AddComponent<MapScreen>(); }
        public static void Show() { Ensure(); Open = true; inst.OnShow(); }
        static void Hide() { Open = false; Profile.CostMul = 1f; }

        static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f, 1f);
        static readonly Color Ink = new Color(0.95f, 0.9f, 0.8f, 1f);
        static readonly Color Dim = new Color(0.72f, 0.66f, 0.58f, 1f);
        static readonly string[] EnemyFullArt = { "UI/enemy_full_fallen", "UI/enemy_full_shadow", "UI/enemy_full_oni", "UI/enemy_full_shogun" };

        enum Modal { None, Fight, Shrine, Smith, Merchant, Treasure, Rest, Event, Result, RegionClear, Herbalist, Knocked }
        Modal modal;
        int sel = -1;
        int moveFrom = -1, moveTo = -1; float moveStart;
        const float MoveTime = 1.25f;
        float panU = -1f, shownAt, modalAt, rollAt = -10f;
        const float RollTime = 0.9f;
        string resultText; Reward treasure;
        List<Item> stock; int stockNode = -1;

        void OnShow()
        {
            shownAt = Time.unscaledTime; sel = -1; moveTo = -1; modal = Modal.None; panU = -1f;
            Campaign.CancelTraining();
            if (Campaign.Knocked) OpenModal(Modal.Knocked);
            else if (Campaign.ClearedRegion >= 0) OpenModal(Modal.RegionClear);
            else if (Campaign.InFight || (!Campaign.Current.done && Campaign.Current.Type != NodeType.Start)) OpenNode(Campaign.At);
        }

        void OpenModal(Modal m) { modal = m; modalAt = Time.unscaledTime; }

        void OpenNode(int i)
        {
            MapNode n = Campaign.Nodes[i];
            Profile.CostMul = n.Type == NodeType.Smith ? 0.5f : 1f;
            switch (n.Type)
            {
                case NodeType.Duel: case NodeType.Elite: case NodeType.Boss: case NodeType.Ambush: OpenModal(Modal.Fight); break;
                case NodeType.Shrine: OpenModal(Modal.Shrine); break;
                case NodeType.Smith: OpenModal(Modal.Smith); break;
                case NodeType.Merchant:
                    if (stockNode != i) { stockNode = i; stock = new List<Item>(); for (int k = 0; k < 3; k++) stock.Add(Profile.Roll(Campaign.Region + 1, 60, k == 0 && Random.value < 0.4f)); Item sc = Random.value < 0.5f ? Profile.NewScroll() : null; if (sc != null) stock[2] = sc; }
                    OpenModal(Modal.Merchant); break;
                case NodeType.Treasure: OpenModal(Modal.Treasure); break;
                case NodeType.Rest: OpenModal(Modal.Rest); break;
                case NodeType.Event: OpenModal(Modal.Event); break;
                case NodeType.Herbalist: OpenModal(Modal.Herbalist); break;
                default: modal = Modal.None; break;
            }
        }

        void Go(int i)
        {
            if (!Campaign.CanGo(i)) return;
            Profile.CostMul = 1f;
            moveFrom = Campaign.At; moveTo = i; moveStart = Time.unscaledTime; sel = -1; stockNode = -1;
            Campaign.MoveTo(i);
            if (Juice.Instance != null) Juice.Instance.DashSound();
        }

        void Update()
        {
            if (!Open || moveTo < 0) return;
            if (Time.unscaledTime - moveStart >= MoveTime)
            {
                int i = moveTo; moveTo = -1;
                if (Campaign.Nodes[i].Type == NodeType.Treasure) treasure = Profile.Treasure(Campaign.Region + 1);   // сундук открывается один раз
                if (Juice.Instance != null) Juice.Instance.Taiko(0.6f, 1.25f);
                Haptics.Pulse(20);
                OpenNode(i);
            }
        }

        // ------------------------------------------------------------------ отрисовка

        float W, mapX, mapW, viewW, u0;   // карта: 810×540 при высоте экрана 540; видимое окно — [u0, u0 + viewW/810]
        const float MapH = 540f, MapFullW = 810f;

        Vector2 P(MapNode n) { return new Vector2(mapX + (n.x - u0) * MapFullW, n.y * MapH); }

        void OnGUI()
        {
            if (!Open) return;
            GUI.depth = -15;   // над боем и его кнопками, под экраном снаряжения
            Matrix4x4 old = UiKit.Begin();
            W = UiKit.Width;
            bool interactive = !InventoryScreen.Open;
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.03f, 0.025f, 0.02f, 1f));
            float panel = Mathf.Clamp(W * 0.3f, 250f, 340f);
            viewW = Mathf.Min(MapFullW, W - panel);
            mapX = Mathf.Max(0f, (W - panel - MapFullW) * 0.5f);
            float uw = viewW / MapFullW;
            // окно карты едет за всадником
            Vector2 hu = HeroUV();
            float target = Mathf.Clamp(hu.x - uw * 0.45f, 0f, 1f - uw);
            panU = panU < 0f ? target : Mathf.Lerp(panU, target, Mathf.Clamp01(Time.unscaledDeltaTime * 3f));
            u0 = panU;
            Texture2D mt = UiKit.Tex(Campaign.MapTexture(Campaign.Region));
            if (mt != null) GUI.DrawTextureWithTexCoords(new Rect(mapX, 0f, viewW, MapH), mt, new Rect(u0, 0f, uw, 1f));
            DrawRoads();
            DrawNodes(interactive && modal == Modal.None && moveTo < 0);
            DrawHero();
            // туман по краям и название области
            Texture2D fog = SoftSprites.Fog;
            Color oc = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.55f);
            if (fog != null) { GUI.DrawTexture(new Rect(mapX - 120f, -120f, 300f, 300f), fog); GUI.DrawTexture(new Rect(mapX + viewW - 120f, 360f, 260f, 260f), fog); }
            GUI.color = oc;
            UiKit.Text(new Rect(mapX + 16f, 8f, 400f, 36f), Campaign.RegionNames[Campaign.Region], 26f, Gold, TextAnchor.MiddleLeft);
            if (Profile.Cycle > 0) UiKit.Text(new Rect(mapX + 18f, 40f, 300f, 20f), "КРУГ " + (Profile.Cycle + 1) + ": враги крепче", 12f, new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft);
            DrawPanel(new Rect(W - panel, 0f, panel, 540f), interactive && modal == Modal.None && moveTo < 0);
            if (modal != Modal.None) DrawModal(interactive);
            GUI.matrix = old;
        }

        Vector2 HeroUV()
        {
            List<MapNode> ns = Campaign.Nodes;
            if (moveTo >= 0 && moveFrom >= 0)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - moveStart) / MoveTime);
                t = t * t * (3f - 2f * t);
                Vector2 a = new Vector2(ns[moveFrom].x, ns[moveFrom].y), b = new Vector2(ns[moveTo].x, ns[moveTo].y);
                return Bez(a, b, Bend(moveFrom, moveTo), t);
            }
            MapNode c = Campaign.Current; return new Vector2(c.x, c.y);
        }

        // дорога — лёгкая дуга (изгиб постоянный для пары узлов)
        static float Bend(int a, int b) { int h = (a * 73 + b * 151) % 7; return (h - 3) * 0.018f; }
        static Vector2 Bez(Vector2 a, Vector2 b, float bend, float t)
        {
            Vector2 d = b - a, perp = new Vector2(-d.y, d.x * 1.5f).normalized;
            Vector2 c = (a + b) * 0.5f + perp * bend;
            return (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;
        }

        static Rect Cell(int k) { return new Rect((k % 4) * 0.25f, 1f - (k / 4 + 1) * 0.25f, 0.25f, 0.25f); }
        static int IconOf(NodeType t)
        {
            switch (t)
            {
                case NodeType.Duel: return 0; case NodeType.Elite: return 1; case NodeType.Boss: return 2; case NodeType.Shrine: return 3;
                case NodeType.Smith: return 4; case NodeType.Merchant: return 5; case NodeType.Treasure: return 6; case NodeType.Ambush: return 7;
                case NodeType.Event: return 8; case NodeType.Rest: return 9; case NodeType.Start: return 13; case NodeType.Herbalist: return 13;
            }
            return 11;
        }

        bool Known(MapNode n)
        {
            MapNode c = Campaign.Current;
            return n.done || n.Type == NodeType.Boss || n.Type == NodeType.Start || n.layer <= c.layer + 1;
        }

        void DrawRoads()
        {
            Texture2D ic = UiKit.Tex("UI/map_icons");
            if (ic == null) return;
            List<MapNode> ns = Campaign.Nodes;
            MapNode cur = Campaign.Current;
            Color oc = GUI.color;
            for (int a = 0; a < ns.Count; a++)
            {
                foreach (int b in ns[a].next)
                {
                    bool walked = ns[a].done && (ns[b].done || b == Campaign.At) && (ns[b].done || moveTo != b);
                    bool open = a == Campaign.At && cur.done && moveTo < 0;
                    float alpha = walked ? 0.95f : open ? 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 5f) : Known(ns[b]) ? 0.45f : 0.22f;
                    Color col = walked ? new Color(0.55f, 0.06f, 0.04f, alpha) : new Color(0.05f, 0.04f, 0.04f, alpha);
                    Vector2 A = new Vector2(ns[a].x, ns[a].y), B = new Vector2(ns[b].x, ns[b].y);
                    float len = ((B - A) * new Vector2(MapFullW, MapH)).magnitude;
                    int dots = Mathf.Max(3, (int)(len / 14f));
                    for (int k = 1; k < dots; k++)
                    {
                        float t = k / (float)dots;
                        if (t < 0.12f || t > 0.88f) continue;   // не залезать на медальоны
                        Vector2 p = Bez(A, B, Bend(a, b), t);
                        Vector2 s = new Vector2(mapX + (p.x - u0) * MapFullW, p.y * MapH);
                        if (s.x < mapX - 6f || s.x > mapX + viewW + 6f) continue;
                        float r = walked ? 4.2f : 3.4f;
                        GUI.color = col;
                        GUI.DrawTextureWithTexCoords(new Rect(s.x - r, s.y - r, r * 2f, r * 2f), ic, Cell(14));
                    }
                    if (open)
                    {
                        // стрелка тушью ближе к концу открытой дороги
                        Vector2 p1 = Bez(A, B, Bend(a, b), 0.74f), p2 = Bez(A, B, Bend(a, b), 0.8f);
                        Vector2 s1 = new Vector2(mapX + (p1.x - u0) * MapFullW, p1.y * MapH), s2 = new Vector2(mapX + (p2.x - u0) * MapFullW, p2.y * MapH);
                        float ang = Mathf.Atan2(s2.y - s1.y, s2.x - s1.x) * Mathf.Rad2Deg;
                        Matrix4x4 m = GUI.matrix;
                        GUIUtility.RotateAroundPivot(ang, s2 * UiKit.Scale);
                        GUI.color = new Color(0.05f, 0.04f, 0.04f, alpha);
                        GUI.DrawTextureWithTexCoords(new Rect(s2.x - 13f, s2.y - 9f, 26f, 18f), ic, Cell(15));
                        GUI.matrix = m;
                    }
                }
            }
            GUI.color = oc;
        }

        void DrawNodes(bool interactive)
        {
            Texture2D ic = UiKit.Tex("UI/map_icons");
            List<MapNode> ns = Campaign.Nodes;
            Color oc = GUI.color;
            for (int i = 0; i < ns.Count; i++)
            {
                MapNode n = ns[i];
                Vector2 p = P(n);
                if (p.x < mapX - 40f || p.x > mapX + viewW + 40f) continue;
                bool can = Campaign.CanGo(i) && moveTo < 0;
                float s = n.Type == NodeType.Boss ? 74f : n.Type == NodeType.Start ? 46f : 52f;
                if (can) s *= 1f + 0.07f * Mathf.Sin(Time.unscaledTime * 4f + i);
                if (i == sel) s *= 1.18f;
                bool known = Known(n);
                int k = !known ? 11 : (n.done && n.Type != NodeType.Start && i != Campaign.At) ? 10 : IconOf(n.Type);
                if (i == sel && ic != null) { GUI.color = Color.white; GUI.DrawTextureWithTexCoords(new Rect(p.x - s * 0.75f, p.y - s * 0.5f, s * 1.5f, s), ic, Cell(12)); }
                GUI.color = !known ? new Color(1f, 1f, 1f, 0.85f) : n.done && i != Campaign.At && n.Type != NodeType.Start ? new Color(0.8f, 0.78f, 0.74f, 0.9f) : Color.white;
                if (ic != null) GUI.DrawTextureWithTexCoords(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), ic, Cell(k));
                else UiKit.Disc(p, s * 0.45f, can ? Gold : Dim);
                GUI.color = oc;
                if (!interactive) continue;
                Rect hit = new Rect(p.x - 34f, p.y - 34f, 68f, 68f);
                if (GUI.Button(hit, GUIContent.none, GUIStyle.none))
                {
                    if (can) { if (sel == i) Go(i); else { sel = i; Haptics.Pulse(10); } }
                    else if (i == Campaign.At && (n.Type == NodeType.Smith || n.Type == NodeType.Merchant)) OpenNode(i);   // вернуться к кузнецу / торговцу
                    else sel = i;   // посмотреть, что там (если узнаваем)
                }
            }
            // знамя в начале пути
            MapNode st = ns[0];
            Texture2D ht = UiKit.Tex("UI/map_hero");
            if (ht != null && st.Type == NodeType.Start)
            {
                Vector2 p = P(st) + new Vector2(-30f, -8f);
                int f = 12 + (int)(Time.unscaledTime * 6f) % 4;
                GUI.DrawTextureWithTexCoords(new Rect(p.x - 26f, p.y - 46f, 52f, 52f), ht, Cell(f));
            }
        }

        void DrawHero()
        {
            Texture2D ht = UiKit.Tex("UI/map_hero"), ic = UiKit.Tex("UI/map_icons");
            Vector2 uv = HeroUV();
            Vector2 p = new Vector2(mapX + (uv.x - u0) * MapFullW, uv.y * MapH);
            bool moving = moveTo >= 0;
            if (ic != null && !moving)
            {
                Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f));
                GUI.DrawTextureWithTexCoords(new Rect(p.x - 36f, p.y - 6f, 72f, 30f), ic, Cell(12));
                GUI.color = oc;
            }
            if (ht == null) { UiKit.Disc(p, 10f, Gold); return; }
            int f = moving ? (int)(Time.unscaledTime * 12f) % 8 : 8 + (int)(Time.unscaledTime * 5f) % 4;
            bool left = moving && Campaign.Nodes[moveTo].x < Campaign.Nodes[moveFrom].x;
            Rect uvr = Cell(f);
            if (left) uvr = new Rect(uvr.x + uvr.width, uvr.y, -uvr.width, uvr.height);
            float s = 92f, bob = moving ? Mathf.Abs(Mathf.Sin(Time.unscaledTime * 12f)) * 2f : 0f;
            GUI.DrawTextureWithTexCoords(new Rect(p.x - s * 0.5f, p.y - s * 0.78f - bob, s, s), ht, uvr);
        }

        // ------------------------------------------------------------------ панель справа

        void DrawPanel(Rect r, bool interactive)
        {
            UiKit.Rect(r, new Color(0.06f, 0.045f, 0.04f, 0.97f));
            UiKit.Rect(new Rect(r.x, r.y, 2f, r.height), new Color(Gold.r, Gold.g, Gold.b, 0.7f));
            float x = r.x + 14f, w = r.width - 28f, y = 12f;
            UiKit.Text(new Rect(x, y, w, 26f), "ПОХОД", 20f, Gold, TextAnchor.MiddleLeft); y += 30f;
            UiKit.Text(new Rect(x, y, w, 18f), "Уровень " + Profile.Level + (Profile.Points > 0 ? "   · очков: " + Profile.Points : ""), 13f, Ink, TextAnchor.MiddleLeft, false); y += 20f;
            Texture2D coins = UiKit.Tex("UI/coins");
            if (coins != null) GUI.DrawTexture(new Rect(x - 2f, y - 1f, 20f, 20f), coins);
            UiKit.Text(new Rect(x + (coins != null ? 22f : 0f), y, w, 18f), Profile.Gold + " мон", 13f, Gold, TextAnchor.MiddleLeft, false); y += 24f;
            string bl = Campaign.BlessingText;
            if (bl.Length > 0) { foreach (string line in Wrap(bl, 34)) { UiKit.Text(new Rect(x, y, w, 16f), line, 11.5f, line.StartsWith("Проклятие") ? new Color(1f, 0.5f, 0.45f) : new Color(0.6f, 0.95f, 0.7f), TextAnchor.MiddleLeft, false); y += 16f; } }
            y += 6f;
            // кости пути: запас, выпавшее, бросок; без костей — проводник за моны или бой с ронином
            bool rolling = Time.unscaledTime - rollAt < RollTime;
            int face = rolling ? 1 + (int)(Time.unscaledTime * 14f) % 3 : Campaign.Steps > 0 ? Campaign.Steps : Mathf.Max(1, Campaign.LastRoll);
            Die(new Rect(x, y, 44f, 44f), face, rolling ? (Time.unscaledTime * 900f) % 360f : 0f, Campaign.Steps > 0 || rolling);
            UiKit.Text(new Rect(x + 54f, y, w - 54f, 22f), "КОСТИ ПУТИ: " + Campaign.Rolls, 14f, Ink, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(x + 54f, y + 22f, w - 54f, 20f), rolling ? "бросок…" : Campaign.Steps > 0 ? "ходов: " + Campaign.Steps : "ход окончен", 12f, Campaign.Steps > 0 ? new Color(0.6f, 0.95f, 0.7f) : Dim, TextAnchor.MiddleLeft, false);
            y += 52f;
            bool canRoll = Campaign.Rolls > 0 && Campaign.Steps <= 0 && Campaign.Current.done && !Campaign.InFight && !Campaign.Training && !rolling;
            if (canRoll && Btn(new Rect(x, y, w, 42f), "БРОСИТЬ КОСТЬ", interactive, true)) { Campaign.RollDie(); rollAt = Time.unscaledTime; Haptics.Pulse(30); if (Juice.Instance != null) Juice.Instance.Taiko(0.7f, 1.3f); }
            if (canRoll) y += 48f;
            if (Campaign.Stuck && !rolling)
            {
                UiKit.Text(new Rect(x, y, w, 16f), "Костей нет. Кость — за каждую победу.", 11.5f, new Color(1f, 0.7f, 0.5f), TextAnchor.MiddleLeft, false); y += 20f;
                int gc = Campaign.GuideCost;
                if (Btn(new Rect(x, y, w, 40f), "ПРОВОДНИК (−" + gc + " МОН)", interactive && Profile.Gold >= gc)) { if (Campaign.BuyGuide()) Haptics.Pulse(20); }
                y += 46f;
                if (Btn(new Rect(x, y, w, 40f), "ВЫЗВАТЬ РОНИНА НА БОЙ", interactive, true)) { Campaign.BeginTraining(); Hide(); if (DuelManager.Instance != null) DuelManager.Instance.ResetMatch(); }
                y += 46f;
            }
            UiKit.Rect(new Rect(x, y, w, 1f), new Color(Gold.r, Gold.g, Gold.b, 0.35f)); y += 10f;

            int show = sel >= 0 ? sel : -1;
            if (show >= 0)
            {
                MapNode n = Campaign.Nodes[show];
                bool known = Known(n);
                Texture2D ic = UiKit.Tex("UI/map_icons");
                if (ic != null) GUI.DrawTextureWithTexCoords(new Rect(x, y, 64f, 64f), ic, Cell(known ? IconOf(n.Type) : 11));
                UiKit.Text(new Rect(x + 72f, y + 6f, w - 72f, 24f), known ? Campaign.TypeNames[n.type] : "НЕИЗВЕСТНО", 16f, Ink, TextAnchor.MiddleLeft);
                if (known && n.IsFight) UiKit.Text(new Rect(x + 72f, y + 32f, w - 72f, 20f), Ladder.Stages[Mathf.Clamp(n.enemy, 0, Ladder.Count - 1)].name, 12f, new Color(1f, 0.6f, 0.45f), TextAnchor.MiddleLeft, false);
                y += 72f;
                string hint = known ? Campaign.TypeHints[n.type] : "Туман скрывает, что ждёт впереди. Подойди ближе.";
                foreach (string line in Wrap(hint, 34)) { UiKit.Text(new Rect(x, y, w, 17f), line, 12f, Dim, TextAnchor.MiddleLeft, false); y += 17f; }
                y += 10f;
                if (Campaign.CanGo(show) && Btn(new Rect(x, y, w, 44f), "ИДТИ", interactive)) Go(show);
            }
            else
            {
                string tip = Campaign.InFight ? "Бой ещё не окончен." : !Campaign.Current.done ? "Ты на месте." : Campaign.Steps > 0 ? "Коснись узла на дороге — и ещё раз, чтобы идти. Бой обрывает ход." : "Брось кость пути, чтобы идти дальше.";
                foreach (string line in Wrap(tip, 34)) { UiKit.Text(new Rect(x, y, w, 17f), line, 12f, Dim, TextAnchor.MiddleLeft, false); y += 17f; }
            }
            if (Btn(new Rect(x, 540f - 112f, w, 44f), "СНАРЯЖЕНИЕ" + (Profile.Points > 0 ? " (+" + Profile.Points + ")" : ""), interactive)) InventoryScreen.Show(0);
            if (Btn(new Rect(x, 540f - 60f, w, 44f), "В МЕНЮ", interactive)) { Hide(); Time.timeScale = 1f; SceneManager.LoadScene(SceneNames.MainMenu); }
        }

        static bool Btn(Rect r, string label, bool enabled = true, bool red = false) { return InventoryScreen.Button(r, label, enabled, red); }

        /// <summary>Кость пути: костяной кубик с точками (1–3), во время броска крутится.</summary>
        static void Die(Rect r, int face, float spin, bool lit)
        {
            Texture2D dt = UiKit.Tex("UI/map_dice");
            Matrix4x4 m = GUI.matrix;
            if (spin != 0f) GUIUtility.RotateAroundPivot(spin, r.center * UiKit.Scale);
            if (dt != null) GUI.DrawTextureWithTexCoords(r, dt, new Rect(((face - 1) % 4) * 0.25f, 0.75f, 0.25f, 0.25f));
            else
            {
                UiKit.Rect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), lit ? Gold : new Color(0.4f, 0.35f, 0.3f));
                UiKit.Rect(r, new Color(0.93f, 0.89f, 0.8f));
                Vector2 c = r.center; float o = r.width * 0.26f, pr = r.width * 0.09f;
                Color pc = new Color(0.55f, 0.06f, 0.05f);
                if (face == 1 || face == 3) UiKit.Disc(c, pr * (face == 1 ? 1.5f : 1f), pc);
                if (face >= 2) { UiKit.Disc(c + new Vector2(-o, -o), pr, pc); UiKit.Disc(c + new Vector2(o, o), pr, pc); }
            }
            GUI.matrix = m;
        }

        static List<string> Wrap(string s, int max)
        {
            List<string> res = new List<string>();
            foreach (string para in s.Split('\n'))
            {
                string line = "";
                foreach (string word in para.Split(' '))
                {
                    if (line.Length > 0 && line.Length + 1 + word.Length > max) { res.Add(line); line = word; }
                    else line = line.Length > 0 ? line + " " + word : word;
                }
                if (line.Length > 0) res.Add(line);
            }
            return res;
        }

        // ------------------------------------------------------------------ карточки узлов

        Rect Card(float w, float h)
        {
            float a = Mathf.Clamp01((Time.unscaledTime - modalAt) / 0.2f);
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.55f * a));
            Rect r = new Rect((W - w) * 0.5f, (540f - h) * 0.5f + (1f - a) * 20f, w, h);
            UiKit.Rect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), new Color(Gold.r, Gold.g, Gold.b, 0.8f));
            UiKit.Rect(r, new Color(0.07f, 0.05f, 0.045f, 0.98f));
            UiKit.Corners(r, 18f, 0.9f);
            return r;
        }

        void Picture(Rect r, string tex)
        {
            Texture2D t = UiKit.Tex(tex);
            if (t != null) GUI.DrawTexture(r, t, ScaleMode.ScaleAndCrop);
            UiKit.Rect(new Rect(r.xMax - 1f, r.y, 1f, r.height), new Color(Gold.r, Gold.g, Gold.b, 0.6f));
        }

        float Lines(float x, float y, float w, string s, int max, float size, Color c)
        {
            foreach (string line in Wrap(s, max)) { UiKit.Text(new Rect(x, y, w, size + 6f), line, size, c, TextAnchor.MiddleLeft, false); y += size + 6f; }
            return y;
        }

        void DrawModal(bool interactive)
        {
            MapNode n = Campaign.InFight ? Campaign.FightNode : Campaign.Current;
            int at = Campaign.InFight ? Campaign.Nodes.IndexOf(n) : Campaign.At;
            bool ok = interactive && Time.unscaledTime - modalAt > 0.25f;
            switch (modal)
            {
                case Modal.Fight:
                {
                    Rect r = Card(640f, 380f);
                    int e = Mathf.Clamp(n.enemy, 0, Ladder.Count - 1);
                    Texture2D art = UiKit.Tex(EnemyFullArt[e]);
                    if (art != null) { float ah = r.height - 20f, aw = ah * art.width / Mathf.Max(1f, art.height); GUI.DrawTexture(new Rect(r.x + 10f, r.y + 10f, Mathf.Min(aw, 260f), ah), art, ScaleMode.ScaleToFit); }
                    float x = r.x + 280f, w = r.width - 300f, y = r.y + 22f;
                    UiKit.Text(new Rect(x, y, w, 22f), Campaign.TypeNames[n.type], 15f, n.Type == NodeType.Boss ? new Color(1f, 0.4f, 0.3f) : Gold, TextAnchor.MiddleLeft); y += 28f;
                    UiKit.Text(new Rect(x, y, w, 34f), Ladder.Stages[e].name, 26f, Ink, TextAnchor.MiddleLeft); y += 36f;
                    UiKit.Text(new Rect(x, y, w, 20f), Ladder.Stages[e].title, 13f, Dim, TextAnchor.MiddleLeft, false); y += 30f;
                    string mods = "";
                    if (n.Type == NodeType.Elite) mods += "Сильнее обычного: +35% здоровья, +15% урона, лишняя жизнь.\nДобыча не ниже редкой, мон больше.\n";
                    if (n.Type == NodeType.Ambush) mods += "Засада: бой ночью, враг начинает с полным духом.\nМон в полтора раза больше.\n";
                    if (n.Type == NodeType.Boss) mods += "Владыка области. Победа откроет путь дальше" + (Campaign.Region >= 2 ? " — и новый круг." : ".") + "\n";
                    if (Campaign.BlessingText.Length > 0) mods += Campaign.BlessingText + "\n";
                    if (mods.Length == 0) mods = "Честный поединок.";
                    y = Lines(x, y, w, mods.TrimEnd('\n'), 40, 12.5f, new Color(0.9f, 0.85f, 0.75f));
                    if (Btn(new Rect(x, r.yMax - 64f, w * 0.58f, 48f), "В БОЙ", ok, true)) StartFight(at);
                    if (Btn(new Rect(x + w * 0.62f, r.yMax - 64f, w * 0.38f, 48f), "СНАРЯЖЕНИЕ", ok)) InventoryScreen.Show(0);
                    break;
                }
                case Modal.Shrine:
                {
                    Rect r = Card(660f, 380f);
                    Picture(new Rect(r.x, r.y, 260f, r.height), "UI/npc_2");
                    float x = r.x + 280f, w = r.width - 300f, y = r.y + 22f;
                    UiKit.Text(new Rect(x, y, w, 26f), "ХРАМ", 20f, Gold, TextAnchor.MiddleLeft); y += 34f;
                    y = Lines(x, y, w, "«Ками этих мест слышат тебя, путник. Выбери, чего просить на следующий бой.»", 40, 13f, Ink) + 12f;
                    string[] names = { "КЛИНОК: +20% УРОНА", "ДУХ: ПОЛНЫЙ ДУХ В НАЧАЛЕ", "ЖИЗНЬ: +25% ЗДОРОВЬЯ" };
                    for (int k = 0; k < 3; k++)
                        if (Btn(new Rect(x, y + k * 54f, w, 46f), names[k], ok)) { Campaign.SetBlessing(k + 1); Haptics.Pulse(30); Result("Жрица взмахнула гохэем. Благословение с тобой до следующей победы."); }
                    break;
                }
                case Modal.Smith:
                {
                    Rect r = Card(660f, 360f);
                    Picture(new Rect(r.x, r.y, 260f, r.height), "UI/npc_1");
                    float x = r.x + 280f, w = r.width - 300f, y = r.y + 22f;
                    UiKit.Text(new Rect(x, y, w, 26f), "КУЗНЕЦ", 20f, Gold, TextAnchor.MiddleLeft); y += 34f;
                    y = Lines(x, y, w, "«Покажи свою сталь. Для того, кто идёт к владыке, — полцены.»\n\nПока ты здесь, улучшение снаряжения стоит вдвое дешевле. Вернуться к кузнецу можно, коснувшись его на карте.", 40, 13f, Ink);
                    if (Btn(new Rect(x, r.yMax - 118f, w, 46f), "К НАКОВАЛЬНЕ", ok, true)) { Profile.CostMul = 0.5f; InventoryScreen.Show(0); }
                    if (Btn(new Rect(x, r.yMax - 64f, w, 46f), "УЙТИ", ok)) modal = Modal.None;
                    break;
                }
                case Modal.Merchant:
                {
                    Rect r = Card(720f, 400f);
                    Picture(new Rect(r.x, r.y, 230f, r.height), "UI/npc_0");
                    float x = r.x + 248f, w = r.width - 266f, y = r.y + 18f;
                    UiKit.Text(new Rect(x, y, w, 26f), "ТОРГОВЕЦ", 20f, Gold, TextAnchor.MiddleLeft);
                    UiKit.Text(new Rect(x, y, w, 26f), Profile.Gold + " мон", 15f, Gold, TextAnchor.MiddleRight); y += 32f;
                    y = Lines(x, y, w, "«Всё с дальних дорог, господин. Дёшево не отдам — но и не обману.»", 46, 12.5f, Dim) + 6f;
                    if (stock != null)
                        for (int k = 0; k < stock.Count; k++)
                        {
                            Item it = stock[k];
                            Rect row = new Rect(x, y + k * 78f, w, 72f);
                            UiKit.Rect(row, new Color(0.11f, 0.085f, 0.075f, 0.95f));
                            if (it == null) { UiKit.Text(row, "продано", 13f, Dim); continue; }
                            InventoryScreen.ItemIcon(new Rect(row.x + 6f, row.y + 6f, 60f, 60f), it);
                            Color rc = Profile.RarityColors[Mathf.Clamp(it.rarity, 0, 3)];
                            UiKit.Text(new Rect(row.x + 76f, row.y + 6f, w - 200f, 22f), it.name, 14f, rc, TextAnchor.MiddleLeft, false);
                            string desc = it.slot >= Profile.SlotNames.Length - 1 ? "Свиток с приёмом" : Profile.SlotNames[Mathf.Clamp(it.slot, 0, Profile.SlotNames.Length - 1)] + " · " + Profile.RarityNames[Mathf.Clamp(it.rarity, 0, 3)];
                            if (it.mods != null && it.mods.Count > 0) desc += " · " + Profile.ModText((ModId)it.mods[0].stat, it.mods[0].value);
                            UiKit.Text(new Rect(row.x + 76f, row.y + 30f, w - 200f, 18f), desc, 11f, Dim, TextAnchor.MiddleLeft, false);
                            int price = Profile.BuyPrice(it);
                            if (Btn(new Rect(row.xMax - 118f, row.y + 14f, 110f, 44f), price + " МОН", ok && Profile.Gold >= price))
                                if (Profile.Spend(price)) { Profile.Give(it); stock[k] = null; Haptics.Pulse(25); if (Juice.Instance != null) Juice.Instance.Taiko(0.5f, 1.4f); }
                        }
                    if (Btn(new Rect(r.xMax - 150f, r.yMax - 56f, 132f, 42f), "УЙТИ", ok)) modal = Modal.None;
                    break;
                }
                case Modal.Treasure:
                {
                    Rect r = Card(560f, 340f);
                    float x = r.x + 24f, y = r.y + 20f, w = r.width - 48f;
                    UiKit.Text(new Rect(x, y, w, 28f), "ТАЙНИК", 22f, Gold); y += 36f;
                    Texture2D ca = UiKit.Tex("UI/chest_anim");
                    float t = Time.unscaledTime - modalAt;
                    if (ca != null) { int cf = t < 0.25f ? 0 : t < 0.45f ? 1 : t < 0.65f ? 2 : 3; GUI.DrawTextureWithTexCoords(new Rect(r.center.x - 60f, y, 120f, 120f), ca, new Rect(cf * 0.25f, 0f, 0.25f, 1f)); }
                    y += 128f;
                    if (treasure != null && t > 0.6f)
                    {
                        UiKit.Text(new Rect(x, y, w, 22f), "+" + treasure.gold + " мон   ·   +" + treasure.xp + " опыта" + (treasure.levels > 0 ? "   ·   новый уровень!" : ""), 14f, Ink); y += 28f;
                        float iw = 58f, sx = r.center.x - treasure.items.Count * (iw + 8f) * 0.5f;
                        for (int k = 0; k < treasure.items.Count; k++) InventoryScreen.ItemIcon(new Rect(sx + k * (iw + 8f), y, iw, iw), treasure.items[k]);
                    }
                    if (Btn(new Rect(r.center.x - 90f, r.yMax - 58f, 180f, 44f), "ЗАБРАТЬ", ok && t > 0.6f)) modal = Modal.None;
                    break;
                }
                case Modal.Rest:
                {
                    Rect r = Card(660f, 360f);
                    Picture(new Rect(r.x, r.y, 260f, r.height), "UI/npc_3");
                    float x = r.x + 280f, w = r.width - 300f, y = r.y + 22f;
                    UiKit.Text(new Rect(x, y, w, 26f), "ПРИВАЛ", 20f, Gold, TextAnchor.MiddleLeft); y += 34f;
                    y = Lines(x, y, w, "У костра греется старый мастер меча. «Отдохни. Или возьми боккэн — покажу, чего тебе не хватает.»", 40, 13f, Ink) + 14f;
                    int xpGain = Mathf.RoundToInt(Profile.XpToNext * 0.5f);
                    if (Btn(new Rect(x, y, w, 46f), "ТРЕНИРОВКА: +1 ОЧКО", ok, true)) { Profile.GivePoint(); Result("Час с боккэном. Мышцы ноют, но рука стала твёрже: +1 очко характеристик."); }
                    if (Btn(new Rect(x, y + 54f, w, 46f), "МЕДИТАЦИЯ: +" + xpGain + " ОПЫТА", ok)) { int lv = Profile.AddXp(xpGain); Result("Ты сидишь у огня до рассвета. Ум ясен: +" + xpGain + " опыта" + (lv > 0 ? " и новый уровень!" : ".")); }
                    break;
                }
                case Modal.Event: DrawEvent(n, at, ok); break;
                case Modal.Herbalist:
                case Modal.Knocked:
                {
                    Rect r = Card(640f, 340f);
                    Picture(new Rect(r.x, r.y, 250f, r.height), UiKit.Tex("UI/npc_4") != null ? "UI/npc_4" : "UI/npc_2");
                    float x = r.x + 270f, w = r.width - 290f, y = r.y + 22f;
                    UiKit.Text(new Rect(x, y, w, 26f), "ХИЖИНА ТРАВНИЦЫ", 20f, Gold, TextAnchor.MiddleLeft); y += 34f;
                    string tx = modal == Modal.Knocked
                        ? "Ты очнулся на циновке. Пахнет полынью и дымом. «Тебя принесли с дороги еле живого. Отлежался — иди. Вот, возьми на удачу». Травница даёт кость пути."
                        : "Старая травница помешивает отвар. «Ходят тут всякие с мечами… Если что — несите сюда». Она вкладывает тебе в ладонь кость пути.";
                    Lines(x, y, w, tx, 40, 13f, Ink);
                    if (Btn(new Rect(x, r.yMax - 64f, w, 46f), "В ПУТЬ", ok, true)) { if (modal == Modal.Knocked) Campaign.AckKnocked(); modal = Modal.None; }
                    break;
                }
                case Modal.Result:
                {
                    Rect r = Card(520f, 240f);
                    float y = Lines(r.x + 26f, r.y + 30f, r.width - 52f, resultText, 52, 14f, Ink);
                    if (Btn(new Rect(r.center.x - 90f, r.yMax - 60f, 180f, 44f), "ДАЛЕЕ", ok)) modal = Modal.None;
                    break;
                }
                case Modal.RegionClear:
                {
                    Rect r = Card(600f, 260f);
                    int prev = Mathf.Clamp(Campaign.ClearedRegion, 0, 2);
                    UiKit.Text(new Rect(r.x, r.y + 26f, r.width, 24f), "ОБЛАСТЬ ПРОЙДЕНА", 16f, Gold);
                    UiKit.Text(new Rect(r.x, r.y + 56f, r.width, 40f), Campaign.RegionNames[prev], 30f, Ink);
                    UiKit.Text(new Rect(r.x, r.y + 110f, r.width, 22f), "Впереди — " + Campaign.RegionNames[Campaign.Region] + (Campaign.Region == 0 ? " (новый круг: враги крепче, добыча богаче)" : ""), 13f, Dim);
                    if (Btn(new Rect(r.center.x - 100f, r.yMax - 66f, 200f, 48f), "В ПУТЬ", ok, true)) { Campaign.AckCleared(); modal = Modal.None; }
                    break;
                }
            }
        }

        void Result(string s) { resultText = s; Campaign.Complete(Campaign.At); OpenModal(Modal.Result); }

        void StartFight(int at)
        {
            Campaign.BeginFight(at);
            Hide();
            DuelManager dm = DuelManager.Instance;
            if (dm != null) dm.ResetMatch();
        }

        // ---------------- события на дороге ----------------

        static readonly string[] EvTitle = { "РАНЕНЫЙ РОНИН", "СТАРЫЙ МОНАХ", "РАЗБОЙНИКИ", "БЕЛАЯ ЛИСА" };
        static readonly string[] EvText =
        {
            "У каменного фонаря сидит раненый ронин, зажимая бок. «Перевяжи меня, путник… я не останусь в долгу».",
            "У горного святилища старый монах протягивает тебе свиток. «Путь клинка — это путь духа. Возьми — или попроси благословения».",
            "Из бамбука с факелами выскакивают разбойники. Копья нацелены тебе в грудь: «Кошель — или жизнь!»",
            "На замшелом камне сидит белая лиса с девятью хвостами. Вокруг пляшут синие огоньки и зовут за собой в чащу.",
        };

        void DrawEvent(MapNode n, int at, bool ok)
        {
            int ev = Mathf.Clamp(n.ev, 0, 3);
            Rect r = Card(720f, 400f);
            Picture(new Rect(r.x, r.y, 330f, r.height), "UI/event_" + ev);
            float x = r.x + 348f, w = r.width - 366f, y = r.y + 22f;
            UiKit.Text(new Rect(x, y, w, 26f), EvTitle[ev], 19f, Gold, TextAnchor.MiddleLeft); y += 34f;
            Lines(x, y, w, EvText[ev], 38, 13f, Ink);
            Rect b1 = new Rect(x, r.yMax - 118f, w, 46f), b2 = new Rect(x, r.yMax - 64f, w, 46f);
            int stage = Campaign.Region + 1;
            switch (ev)
            {
                case 0:
                    if (Btn(b1, "ПЕРЕВЯЗАТЬ (−30 МОН)", ok && Profile.Gold >= 30, true) && Profile.Spend(30))
                    {
                        Item it = Profile.Make(Random.Range(0, Profile.SlotCount), Random.value < 0.35f ? 2 : 1, 1f + 0.22f * stage + 0.4f * Profile.Cycle);
                        Profile.Give(it);
                        Result("Ронин отдаёт тебе своё: «" + it.name + "». «Пусть служит лучше, чем мне».");
                    }
                    if (Btn(b2, "ПРОЙТИ МИМО", ok)) Result("Ты идёшь дальше. За спиной долго слышен его кашель.");
                    break;
                case 1:
                    if (Btn(b1, "ПРИНЯТЬ СВИТОК", ok, true))
                    {
                        Item sc = Profile.NewScroll();
                        if (sc != null) { Profile.Give(sc); Result("В свитке — приём: " + sc.name + ". Прочти его в снаряжении."); }
                        else { int lv = Profile.AddXp(120); Result("Ты уже знаешь всё, что в свитке. Но слова монаха стоят опыта: +120" + (lv > 0 ? ", новый уровень!" : ".")); }
                    }
                    if (Btn(b2, "ПОПРОСИТЬ БЛАГОСЛОВЕНИЯ", ok)) { Campaign.SetBlessing(2); Result("Монах касается твоего лба чётками. В следующем бою ты начнёшь с полным духом."); }
                    break;
                case 2:
                    int toll = Mathf.Max(20, Mathf.RoundToInt(Profile.Gold * 0.4f));
                    if (Btn(b1, "СРАЗИТЬСЯ", ok, true)) { Campaign.TurnToAmbush(at); OpenModal(Modal.Fight); }
                    if (Btn(b2, "ОТКУПИТЬСЯ (−" + toll + " МОН)", ok)) { Profile.AddGold(-toll); Result("Разбойники пересчитывают моны и растворяются в бамбуке."); }
                    break;
                case 3:
                    if (Btn(b1, "ПОЙТИ ЗА ОГНЯМИ", ok, true))
                    {
                        if (Random.value < 0.5f)
                        {
                            Item it = Profile.Make(Random.Range(0, Profile.SlotCount), 3, 1f + 0.22f * stage + 0.4f * Profile.Cycle);
                            Profile.Give(it);
                            Result("Огни приводят к поляне, где в корнях спрятан дар лисы: «" + it.name + "».");
                        }
                        else { Campaign.Curse(); Result("Огни кружат тебя до рассвета. Лиса смеётся. Проклятие: −25% здоровья в следующем бою."); }
                    }
                    if (Btn(b2, "ОСТАВИТЬ ПОДНОШЕНИЕ (−25 МОН)", ok && Profile.Gold >= 25) && Profile.Spend(25)) { Campaign.SetBlessing(3); Result("Лиса берёт моны и исчезает. Тепло разливается по телу: +25% здоровья в следующем бою."); }
                    break;
            }
        }
    }
}
