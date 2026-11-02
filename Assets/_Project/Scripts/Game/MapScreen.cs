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

        static readonly Color Gold = VisualIdentity.Brass;
        static readonly Color Ink = VisualIdentity.Parchment;
        static readonly Color Dim = VisualIdentity.Subdued;
        static readonly string[] EnemyFullArt = { "UI/enemy_full_fallen", "UI/enemy_full_shadow", "UI/enemy_full_oni", "UI/enemy_full_shogun" };

        enum Modal { None, Fight, Shrine, Smith, Merchant, Treasure, Rest, Event, RegionClear, Herbalist, Knocked }
        Modal modal;
        int sel = -1;
        readonly List<int> previewPath = new List<int>(16);
        int plannedRegion = -1;   // keep a chosen destination across intermediate encounters
        readonly Queue<int> routeQueue = new Queue<int>(16);
        int moveFrom = -1, moveTo = -1; float moveStart;
        const float MoveTime = 1.25f;
        float panU = -1f, shownAt, modalAt, rollAt = -10f;
        bool manualPan, panArmed, panDragging;
        Vector2 panTouchOrigin;
        float panAtOrigin;
        const float RollTime = 0.9f;
        Reward treasure;
        bool freshTreasure, revisit;
        bool routeAutopilot;
        float nextRouteAt;
        float toastAt = -100f;
        readonly List<string> toastLines = new List<string>(3);
        List<Item> stock; int stockNode = -1;

        void OnShow()
        {
            int resume = !Campaign.Knocked && Campaign.ClearedRegion < 0 && plannedRegion == Campaign.Region ? sel : -1;
            shownAt = Time.unscaledTime; sel = -1; previewPath.Clear(); moveTo = -1; modal = Modal.None; panU = -1f;
            routeAutopilot = false; nextRouteAt = 0f; toastLines.Clear();
            manualPan = false; panArmed = false; panDragging = false;
            if (resume >= 0 && resume < Campaign.Nodes.Count && resume != Campaign.At) SelectNode(resume);
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
            // Revisiting a cleared road does not repeat paid treasure or defeated enemies.
            if (revisit && n.done && !Campaign.InFight &&
                (n.IsFight || n.Type == NodeType.Treasure || n.Type == NodeType.Event))
            {
                modal = Modal.None;
                return;
            }
            switch (n.Type)
            {
                case NodeType.Duel: case NodeType.Elite: case NodeType.Boss: case NodeType.Ambush: OpenModal(Modal.Fight); break;
                case NodeType.Shrine: OpenModal(Modal.Shrine); break;
                case NodeType.Smith: OpenModal(Modal.Smith); break;
                case NodeType.Merchant:
                    if (stockNode != i) { stockNode = i; stock = new List<Item>(); for (int k = 0; k < 3; k++) stock.Add(Profile.Roll(Campaign.Region + 1, 60, k == 0 && Random.value < 0.4f)); Item sc = Random.value < 0.5f ? Profile.NewScroll() : null; if (sc != null) stock[2] = sc; }
                    OpenModal(Modal.Merchant); break;
                case NodeType.Treasure: if (treasure != null) OpenModal(Modal.Treasure); else modal = Modal.None; break;
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
            int destination = sel;
            if (destination >= 0 && destination != i && NextPreviewStep == i)
                routeAutopilot = true;
            revisit = Campaign.Nodes[i].done;
            freshTreasure = Campaign.Nodes[i].Type == NodeType.Treasure && !revisit;
            treasure = null;
            moveFrom = Campaign.At; moveTo = i; moveStart = Time.unscaledTime; sel = -1; previewPath.Clear(); stockNode = -1;
            manualPan = false; // resume following the animated samurai as the journey proceeds
            Campaign.MoveTo(i);
            // If the player plotted several waypoints, retain the remaining
            // route after the next encounter is resolved. Never skip an encounter.
            if (destination >= 0 && destination != i && destination < Campaign.Nodes.Count)
                SelectNode(destination);
            if (Juice.Instance != null) Juice.Instance.DashSound();
        }

        void Update()
        {
            if (!Open) return;
            HandleMapPan();
            if (moveTo >= 0)
            {
                if (Time.unscaledTime - moveStart >= MoveTime)
                {
                    int i = moveTo; moveTo = -1;
                    if (freshTreasure) treasure = Profile.Treasure(Campaign.Region + 1);
                    freshTreasure = false; // rollback/revisit must not grant a second chest
                    if (Juice.Instance != null) Juice.Instance.Taiko(0.6f, 1.25f);
                    Haptics.Pulse(20);
                    OpenNode(i);
                    nextRouteAt = Time.unscaledTime + 0.45f;
                }
                return;
            }
            // Auto-walk only after a completed encounter and only while the die has steps.
            // Every encounter card still interrupts until the player deals with it.
            if (routeAutopilot && modal == Modal.None && !InventoryScreen.Open &&
                Time.unscaledTime >= nextRouteAt)
            {
                int next = NextPreviewStep;
                if (next >= 0 && Campaign.CanGo(next))
                    Go(next);
                else if (next < 0 || Campaign.Steps <= 0 || !Campaign.Current.done)
                    routeAutopilot = false;
            }
        }

        // Finger/mouse drag moves the world map; a small recenter button
        // restores the normal Heroes-style camera following the samurai.
        void HandleMapPan()
        {
            if (modal != Modal.None || InventoryScreen.Open || moveTo >= 0 || viewW <= 0f)
            {
                panArmed = false;
                panDragging = false;
                return;
            }
            Vector3 mouse = Input.mousePosition;
            Vector2 pt = new Vector2(mouse.x / UiKit.Scale,
                (Screen.height - mouse.y) / UiKit.Scale);
            if (Input.GetMouseButtonDown(0))
            {
                panArmed = pt.x >= mapX + 4f && pt.x <= mapX + viewW - 4f &&
                    pt.y >= 5f && pt.y <= MapH - 5f;
                // Tapping a destination medal should select the node, not start dragging.
                if (panArmed)
                {
                    foreach (MapNode node in Campaign.Nodes)
                    {
                        if ((P(node) - pt).sqrMagnitude < 38f * 38f)
                        {
                            panArmed = false;
                            break;
                        }
                    }
                }
                if (panArmed)
                {
                    panTouchOrigin = pt;
                    panAtOrigin = panU;
                    panDragging = false;
                }
            }
            if (panArmed && Input.GetMouseButton(0))
            {
                if (!panDragging && (pt - panTouchOrigin).sqrMagnitude > 9f * 9f)
                {
                    panDragging = true;
                    manualPan = true;
                    routeAutopilot = false;
                }
                if (panDragging)
                    panU = Mathf.Clamp(panAtOrigin - (pt.x - panTouchOrigin.x) / MapFullW,
                        0f, Mathf.Max(0f, 1f - viewW / MapFullW));
            }
            if (Input.GetMouseButtonUp(0))
            {
                panArmed = false;
                panDragging = false;
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
            panU = panU < 0f ? target : manualPan ? panU :
                Mathf.Lerp(panU, target, Mathf.Clamp01(Time.unscaledDeltaTime * 3f));
            panU = Mathf.Clamp(panU, 0f, Mathf.Max(0f, 1f - uw));
            u0 = panU;
            Texture2D mt = UiKit.Tex(Campaign.MapTexture(Campaign.Region));
            if (mt != null) GUI.DrawTextureWithTexCoords(new Rect(mapX, 0f, viewW, MapH), mt, new Rect(u0, 0f, uw, 1f));
            DrawPaintedMist();
            DrawLandmarkLights();
            DrawRoads();
            DrawJourneyFootprints();
            DrawNodes(interactive && modal == Modal.None && moveTo < 0);
            DrawHero();
            DrawJourneyLegend();
            // туман по краям и название области
            Texture2D fog = SoftSprites.Fog;
            Color oc = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.55f);
            if (fog != null) { GUI.DrawTexture(new Rect(mapX - 120f, -120f, 300f, 300f), fog); GUI.DrawTexture(new Rect(mapX + viewW - 120f, 360f, 260f, 260f), fog); }
            GUI.color = oc;
            Texture2D crest = UiKit.Tex("UI/kamon_feather");
            if (crest != null)
                UiKit.Image(new Vector2(mapX + 34f, 27f), 19f, crest,
                    new Color(1f, 0.88f, 0.63f, 0.76f));
            UiKit.Text(new Rect(mapX + 59f, 8f, 355f, 36f),
                Campaign.RegionNames[Campaign.Region], 25f, Gold, TextAnchor.MiddleLeft);
            if (Profile.Cycle > 0) UiKit.Text(new Rect(mapX + 18f, 40f, 300f, 20f), "КРУГ " + (Profile.Cycle + 1) + ": враги крепче", 12f, new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft);
            // Only show the re-center control when the map is wider than the screen.
            if (uw < 0.999f && Btn(new Rect(mapX + viewW - 116f, 14f, 104f, 33f),
                manualPan ? "К ГЕРОЮ" : "ОБЗОР", interactive && modal == Modal.None && moveTo < 0))
            {
                manualPan = !manualPan;
                if (!manualPan) panU = -1f;
            }
            DrawPanel(new Rect(W - panel, 0f, panel, 540f), interactive && modal == Modal.None && moveTo < 0);
            if (modal != Modal.None) DrawModal(interactive);
            else if (interactive) DrawToast();
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

        // Keep all map badges on the SAME high-detail illustrated atlas.
        // Pack02's placeholders reused the same clouds/medallions for different
        // locations; the authored map_icons atlas has a distinct glyph per type.
        // Fog of war still hides the description but no longer substitutes every
        // destination's silhouette with the same grey cloud.
        static int MapBadgeCell(MapNode n, int index)
        {
            // Do not replace completed or distant locations with a universal cloud.
            // A shrine remains a shrine, a duel stays crossed swords, etc.
            return IconOf(n.Type);
        }

        static readonly string[] ShortTypeNames =
        {
            "НАЧАЛО", "ДУЭЛЬ", "ЭЛИТА", "БОСС", "ХРАМ", "КУЗНЕЦ",
            "ТОРГОВЕЦ", "СУНДУК", "ЗАСАДА", "СОБЫТИЕ", "ПРИВАЛ", "ТРАВНИЦА"
        };

        static void HerbalistMark(Vector2 c, float radius)
        {
            // A green herbal stamp distinguishes the healer from the starting flag.
            UiKit.Disc(c, radius, new Color(0.07f, 0.17f, 0.12f, 0.95f));
            UiKit.Ring(c, radius, new Color(0.58f, 0.80f, 0.46f, 0.85f), true);
            UiKit.Line(c + new Vector2(0f, radius * 0.58f),
                c + new Vector2(0f, -radius * 0.56f), 2.1f,
                new Color(0.83f, 0.94f, 0.58f, 0.95f));
            UiKit.Disc(c + new Vector2(-radius * 0.35f, -radius * 0.12f),
                radius * 0.30f, new Color(0.43f, 0.80f, 0.39f, 0.98f));
            UiKit.Disc(c + new Vector2(radius * 0.35f, -radius * 0.30f),
                radius * 0.27f, new Color(0.48f, 0.81f, 0.43f, 0.98f));
        }

        static Color MapBadgeTint(MapNode n, bool known, int index)
        {
            if (!known) return new Color(0.56f, 0.65f, 0.70f, 0.80f);
            if (n.done && index != Campaign.At && n.Type != NodeType.Start)
                return new Color(0.68f, 0.69f, 0.67f, 0.75f);
            return Color.white;
        }

        bool Known(MapNode n)
        {
            MapNode c = Campaign.Current;
            return n.done || n.Type == NodeType.Boss || n.Type == NodeType.Start || n.layer <= c.layer + 1;
        }

        // Shortest route through the authored road graph, including future nodes.
        // This is a PREVIEW, not automatic traversal: each intermediate encounter
        // must be resolved before the next step is allowed by Campaign.CanGo.
        void SelectNode(int index)
        {
            sel = index;
            plannedRegion = Campaign.Region;
            previewPath.Clear();
            List<MapNode> nodes = Campaign.Nodes;
            if (index < 0 || index >= nodes.Count) return;
            int start = Campaign.At;
            if (start == index) { previewPath.Add(start); return; }

            int[] parents = new int[nodes.Count];
            for (int i = 0; i < parents.Length; i++) parents[i] = -1;
            routeQueue.Clear();
            parents[start] = start;
            routeQueue.Enqueue(start);
            while (routeQueue.Count > 0 && parents[index] < 0)
            {
                int at = routeQueue.Dequeue();
                foreach (int next in nodes[at].next)
                {
                    if (next < 0 || next >= parents.Length || parents[next] >= 0) continue;
                    parents[next] = at;
                    routeQueue.Enqueue(next);
                }
            }
            if (parents[index] < 0) return;
            int cur = index;
            while (cur != start)
            {
                previewPath.Add(cur);
                cur = parents[cur];
            }
            previewPath.Add(start);
            previewPath.Reverse();
        }

        int NextPreviewStep
        {
            get { return previewPath.Count > 1 && previewPath[0] == Campaign.At ? previewPath[1] : -1; }
        }

        // Heroes-style walking footprints: ochre for the travelled route,
        // pale green/gold within the die's remaining moves, red beyond its range.
        // A bounded cached texture is used instead of thousands of ParticleSystems.
        void DrawJourneyFootprints()
        {
            if (Event.current.type != EventType.Repaint) return;
            IList<int> trail = Campaign.Travelled;
            List<MapNode> nodes = Campaign.Nodes;
            for (int i = 1; i < trail.Count; i++)
            {
                int a = trail[i - 1], b = trail[i];
                if (a < 0 || a >= nodes.Count || b < 0 || b >= nodes.Count) continue;
                float progress = (moveTo == b && moveFrom == a && i == trail.Count - 1)
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - moveStart) / MoveTime))
                    : 1f;
                DrawFootprintsOnRoad(a, b, new Color(0.64f, 0.33f, 0.16f, 0.68f), progress, false);
            }

            if (moveTo >= 0)
            {
                DrawFootprintsOnRoad(moveFrom, moveTo,
                    new Color(1f, 0.84f, 0.43f, 0.92f),
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - moveStart) / MoveTime)), true);
                return;
            }

            // Before selecting a destination, preview available next roads subtly.
            if (previewPath.Count <= 1 && Campaign.Steps > 0 && Campaign.Current.done && !Campaign.InFight)
            {
                foreach (int next in Campaign.Current.next)
                    DrawFootprintsOnRoad(Campaign.At, next,
                        new Color(0.88f, 0.79f, 0.48f, 0.48f), 1f, false);
            }

            for (int i = 1; i < previewPath.Count; i++)
            {
                bool withinDie = i <= Campaign.Steps;
                Color tint = withinDie && Campaign.Current.done && !Campaign.InFight
                    ? new Color(0.67f, 1f, 0.65f, 0.95f)
                    : new Color(1f, 0.35f, 0.29f, 0.67f);
                DrawFootprintsOnRoad(previewPath[i - 1], previewPath[i], tint, 1f, withinDie);
            }
        }

        void DrawFootprintsOnRoad(int from, int to, Color tint, float progress, bool shimmer)
        {
            List<MapNode> nodes = Campaign.Nodes;
            if (from < 0 || from >= nodes.Count || to < 0 || to >= nodes.Count || progress <= 0f) return;
            Vector2 a = new Vector2(nodes[from].x, nodes[from].y);
            Vector2 b = new Vector2(nodes[to].x, nodes[to].y);
            float len = Vector2.Distance(new Vector2(a.x * MapFullW, a.y * MapH),
                                          new Vector2(b.x * MapFullW, b.y * MapH));
            int count = Mathf.Clamp(Mathf.RoundToInt(len / 17f), 5, 22);
            Texture2D feet = JourneyFootprintArt.Footsteps;
            Color prev = GUI.color;
            Matrix4x4 original = GUI.matrix;
            for (int j = 1; j < count; j++)
            {
                float t = j / (float)count;
                if (t < 0.13f || t > 0.87f || t > progress) continue;
                Vector2 uv = Bez(a, b, Bend(from, to), t);
                Vector2 point = new Vector2(mapX + (uv.x - u0) * MapFullW, uv.y * MapH);
                if (point.x < mapX + 7f || point.x > mapX + viewW - 7f) continue;
                Vector2 back = Bez(a, b, Bend(from, to), Mathf.Max(0f, t - 0.025f));
                Vector2 ahead = Bez(a, b, Bend(from, to), Mathf.Min(1f, t + 0.025f));
                Vector2 tangent = new Vector2((ahead.x - back.x) * MapFullW, (ahead.y - back.y) * MapH);
                float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + 90f;
                float pulse = shimmer ? 0.84f + 0.16f * Mathf.Sin(Time.unscaledTime * 4.5f - j * 0.55f) : 1f;
                GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * pulse);
                GUIUtility.RotateAroundPivot(angle, point * UiKit.Scale);
                GUI.DrawTexture(new Rect(point.x - 8f, point.y - 8f, 16f, 16f), feet);
                GUI.matrix = original;
            }
            GUI.color = prev;
        }

        void DrawRoads()
        {
            // Static path decoration needs only Repaint, not IMGUI Layout / input passes.
            if (Event.current.type != EventType.Repaint) return;
            Texture2D ic = UiKit.Tex("UI/map_icons");
            if (ic == null) return;
            List<MapNode> ns = Campaign.Nodes;
            MapNode cur = Campaign.Current;
            Color oc = GUI.color;
            for (int a = 0; a < ns.Count; a++)
            {
                foreach (int b in ns[a].next)
                {
                    bool walked = Campaign.WasTravelled(a, b) && !(moveFrom == a && moveTo == b);
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
                // Focused marker effects use the existing cached UI disc texture:
                // glowing boss warning, reachable nodes and the hero's current checkpoint.
                if (known && (can || i == Campaign.At || n.Type == NodeType.Boss) && !n.done)
                {
                    float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (n.Type == NodeType.Boss ? 2.3f : 3.5f) + i * 0.37f);
                    float reach = can ? 0.12f + wave * 0.08f : 0.08f + wave * 0.05f;
                    Color halo = n.Type == NodeType.Boss
                        ? new Color(0.85f, 0.08f, 0.035f, reach)
                        : new Color(0.92f, 0.58f, 0.2f, reach);
                    UiKit.Disc(p, s * (0.64f + wave * 0.12f), halo);
                }
                int k = MapBadgeCell(n, i);
                if (i == sel && ic != null) { GUI.color = Color.white; GUI.DrawTextureWithTexCoords(new Rect(p.x - s * 0.75f, p.y - s * 0.5f, s * 1.5f, s), ic, Cell(12)); }
                if (i == sel && Event.current.type == EventType.Repaint)
                {
                    float halo = s * (0.58f + 0.025f * Mathf.Sin(Time.unscaledTime * 4f));
                    UiKit.Ring(p, halo, new Color(1f, 0.81f, 0.40f, 0.80f), true);
                }
                GUI.color = MapBadgeTint(n, known, i);
                if (ic != null)
                    GUI.DrawTextureWithTexCoords(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), ic, Cell(k));
                else UiKit.Disc(p, s * 0.45f, can ? Gold : Dim);
                GUI.color = oc;
                if (n.Type == NodeType.Herbalist)
                    HerbalistMark(p + new Vector2(s * 0.26f, s * 0.27f), 10f);
                // Only nearby / selected nodes show labels. Avoid crowding the panorama.
                if ((can || i == sel) && known && n.Type != NodeType.Start)
                {
                    float tw = 90f;
                    Rect label = new Rect(p.x - tw * 0.5f, p.y + s * 0.45f + 3f, tw, 18f);
                    UiKit.Rect(label, new Color(0.028f, 0.023f, 0.02f, 0.75f));
                    UiKit.Text(label, ShortTypeNames[(int)n.Type], 10.5f,
                        new Color(0.96f, 0.88f, 0.68f, 0.98f), TextAnchor.MiddleCenter, false);
                }
                int routeIndex = previewPath.IndexOf(i);
                if (routeIndex > 0)
                {
                    Vector2 chip = p + new Vector2(s * 0.36f, -s * 0.35f);
                    bool inRange = routeIndex <= Campaign.Steps;
                    Color chipColor = inRange ? new Color(0.20f, 0.38f, 0.22f, 0.95f) : new Color(0.46f, 0.12f, 0.10f, 0.94f);
                    UiKit.Disc(chip, 12f, chipColor);
                    UiKit.Text(new Rect(chip.x - 12f, chip.y - 12f, 24f, 24f),
                        routeIndex.ToString(), 12f, Ink, TextAnchor.MiddleCenter);
                }
                if (!interactive) continue;
                Rect hit = new Rect(p.x - 34f, p.y - 34f, 68f, 68f);
                if (GUI.Button(hit, GUIContent.none, GUIStyle.none))
                {
                    if (can && sel == i) Go(i);
                    else if (i == Campaign.At && (n.Type == NodeType.Smith || n.Type == NodeType.Merchant)) OpenNode(i);
                    else { SelectNode(i); Haptics.Pulse(10); }   // preview route before choosing to move
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
            if (Event.current.type != EventType.Repaint) return;
            Texture2D ht = UiKit.Tex("UI/map_hero"), ic = UiKit.Tex("UI/map_icons");
            Vector2 uv = HeroUV();
            Vector2 p = new Vector2(mapX + (uv.x - u0) * MapFullW, uv.y * MapH);
            bool moving = moveTo >= 0;
            // Shadow and warm aureole make the tiny animated hero readable over foliage.
            UiKit.Disc(p + new Vector2(0f, 8f), moving ? 16f : 12f, new Color(0.035f, 0.02f, 0.01f, 0.45f));
            if (moving) UiKit.Disc(p, 21f + 2f * Mathf.Sin(Time.unscaledTime * 7f), new Color(0.98f, 0.70f, 0.28f, 0.18f));
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

        // Panoramic mist layers drift slowly over the painted forest but do not
        // obscure routes or medallions. One 256x128 texture, three draw calls.
        void DrawPaintedMist()
        {
            if (Event.current.type != EventType.Repaint || viewW < 160f) return;
            Texture2D mist = JourneyMistArt.Texture;
            if (mist == null) return;
            Color previous = GUI.color;
            GUI.BeginGroup(new Rect(mapX, 0f, viewW, MapH));
            float clock = Time.unscaledTime;
            Color baseTint = Campaign.Region == 2
                ? new Color(0.97f, 0.65f, 0.62f, 0.14f)
                : Campaign.Region == 1
                    ? new Color(0.79f, 0.87f, 1f, 0.13f)
                    : new Color(0.95f, 0.94f, 0.86f, 0.12f);
            for (int i = 0; i < 3; i++)
            {
                float u = Mathf.Repeat(0.09f + i * 0.34f + clock * (0.0018f + i * 0.0007f), 1.2f);
                float x = (u - u0) * MapFullW;
                float y = i == 0 ? 85f : i == 1 ? 260f : 402f;
                GUI.color = baseTint;
                GUI.DrawTexture(new Rect(x - 195f, y - 55f, 390f, 115f),
                    mist, ScaleMode.StretchToFill, true);
            }
            GUI.EndGroup();
            GUI.color = previous;
        }

        // Landmarks are lit gently without new Light objects or full-screen bloom.
        void DrawLandmarkLights()
        {
            if (Event.current.type != EventType.Repaint) return;
            List<MapNode> nodes = Campaign.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                MapNode n = nodes[i];
                if (n.done || !Known(n)) continue;
                bool boss = n.Type == NodeType.Boss;
                bool shrine = n.Type == NodeType.Shrine;
                bool herb = n.Type == NodeType.Herbalist;
                bool treasure = n.Type == NodeType.Treasure;
                if (!boss && !shrine && !herb && !treasure) continue;
                Vector2 point = P(n);
                if (point.x < mapX + 24f || point.x > mapX + viewW - 24f) continue;
                float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime *
                    (boss ? 1.9f : 2.8f) + i * 1.2f);
                Color tint = boss ? new Color(1f, 0.20f, 0.08f, 0.09f + wave * 0.06f)
                    : herb ? new Color(0.50f, 1f, 0.35f, 0.08f + wave * 0.05f)
                    : shrine ? new Color(1f, 0.77f, 0.42f, 0.06f + wave * 0.055f)
                    : new Color(1f, 0.80f, 0.32f, 0.05f + wave * 0.045f);
                UiKit.Disc(point, (boss ? 65f : 48f) + wave * 5f, tint);
            }
        }

        static readonly string[] JourneyLegendNames =
            { "ПРОЙДЕНО", "В ПРЕДЕЛАХ ХОДА", "НЕДОСТУПНО" };
        static readonly Color[] JourneyLegendColors =
            { new Color(0.72f, 0.42f, 0.22f),
              new Color(0.70f, 0.95f, 0.62f),
              new Color(1f, 0.39f, 0.31f) };
        static readonly float[] JourneyLegendWidths = { 105f, 151f, 111f };

        // Low-profile route legend: never obscures the map or interactive cards.
        void DrawJourneyLegend()
        {
            if (Event.current.type != EventType.Repaint || viewW < 350f) return;
            const float h = 31f;
            float w = Mathf.Min(viewW - 20f, 373f);
            Rect bar = new Rect(mapX + 10f, MapH - h - 9f, w, h);
            UiKit.Rect(bar, new Color(0.035f, 0.025f, 0.020f, 0.79f));
            UiKit.Rect(new Rect(bar.x, bar.y, 2f, h), new Color(Gold.r, Gold.g, Gold.b, 0.65f));
            float px = bar.x + 10f;
            for (int i = 0; i < JourneyLegendNames.Length; i++)
            {
                UiKit.Disc(new Vector2(px + 5f, bar.y + 15f), 5f, JourneyLegendColors[i]);
                UiKit.Text(new Rect(px + 15f, bar.y + 5f, JourneyLegendWidths[i] - 14f, 21f),
                    JourneyLegendNames[i], 9.2f, Dim, TextAnchor.MiddleLeft, false);
                px += JourneyLegendWidths[i];
            }
        }

        // ------------------------------------------------------------------ панель справа

        void DrawPanel(Rect r, bool interactive)
        {
            VisualIdentity.Surface(r, 1f, false);
            float x = r.x + 16f, w = r.width - 32f, y = 12f;
            VisualIdentity.Heading(new Rect(x, y, w, 36f), "ПОХОД");
            y += 38f;
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
            // Three-step travel gauge below the die: coloured segments correspond
            // to remaining movement points, matching the trail's range.
            for (int k = 0; k < 3; k++)
            {
                bool available = k < Campaign.Steps;
                Color segment = available ? new Color(0.70f, 0.92f, 0.58f, 0.92f)
                    : new Color(0.35f, 0.28f, 0.20f, 0.72f);
                UiKit.Rect(new Rect(x + 54f + k * 43f, y + 43f, 36f, 4f), segment);
            }
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
            VisualIdentity.Divider(new Rect(x, y, w, 1f)); y += 11f;

            int show = sel >= 0 ? sel : -1;
            if (show >= 0)
            {
                MapNode n = Campaign.Nodes[show];
                bool known = Known(n);
                Texture2D ic = UiKit.Tex("UI/map_icons");
                if (ic != null)
                {
                    Color previousTint = GUI.color;
                    GUI.color = MapBadgeTint(n, known, show);
                    GUI.DrawTextureWithTexCoords(new Rect(x, y, 64f, 64f), ic,
                        Cell(MapBadgeCell(n, show)));
                    GUI.color = previousTint;
                    if (n.Type == NodeType.Herbalist)
                        HerbalistMark(new Vector2(x + 53f, y + 53f), 11f);
                }
                UiKit.Text(new Rect(x + 72f, y + 6f, w - 72f, 24f), known ? Campaign.TypeNames[n.type] : "НЕИЗВЕСТНО", 16f, Ink, TextAnchor.MiddleLeft);
                if (known && n.IsFight) UiKit.Text(new Rect(x + 72f, y + 32f, w - 72f, 20f), Ladder.Stages[Mathf.Clamp(n.enemy, 0, Ladder.Count - 1)].name, 12f, new Color(1f, 0.6f, 0.45f), TextAnchor.MiddleLeft, false);
                y += 72f;
                string hint = known ? Campaign.TypeHints[n.type] : "Туман скрывает, что ждёт впереди. Подойди ближе.";
                foreach (string line in Wrap(hint, 34)) { UiKit.Text(new Rect(x, y, w, 17f), line, 12f, Dim, TextAnchor.MiddleLeft, false); y += 17f; }
                y += 10f;
                int nextStep = NextPreviewStep;
                if (nextStep >= 0)
                {
                    int totalMoves = previewPath.Count - 1;
                    int available = Mathf.Min(Campaign.Steps, totalMoves);
                    bool enough = totalMoves <= Campaign.Steps;
                    Color routeColor = enough ? new Color(0.65f, 0.94f, 0.65f) : new Color(1f, 0.61f, 0.48f);
                    UiKit.Text(new Rect(x, y, w, 18f),
                        "МАРШРУТ: " + totalMoves + " шаг.   ·   доступно: " + available,
                        12f, routeColor, TextAnchor.MiddleLeft, false);
                    y += 20f;
                    // Travel remains one node at a time, so fight/event encounters
                    // are never skipped by the route preview.
                    if (Campaign.CanGo(nextStep) &&
                        Btn(new Rect(x, y, w, 40f), totalMoves == 1 ? "ИДТИ ПО СЛЕДАМ" : "НАЧАТЬ ПУТЬ (1 / " + totalMoves + ")", interactive))
                        Go(nextStep);
                    else
                        UiKit.Text(new Rect(x, y, w, 18f),
                            Campaign.Steps == 0 ? "Нужен бросок кости пути" : "Следующий переход пока закрыт",
                            11f, Dim, TextAnchor.MiddleLeft, false);
                }
                else if (Campaign.CanGo(show) && Btn(new Rect(x, y, w, 40f), "ИДТИ", interactive))
                    Go(show);
            }
            else
            {
                string tip = Campaign.InFight ? "Бой ещё не окончен." : !Campaign.Current.done ? "Ты на месте." : Campaign.Steps > 0 ? "Выбери цель. Светлые следы — доступные шаги, красные — сверх броска." : "Брось кость пути, чтобы идти дальше.";
                foreach (string line in Wrap(tip, 34)) { UiKit.Text(new Rect(x, y, w, 17f), line, 12f, Dim, TextAnchor.MiddleLeft, false); y += 17f; }
            }
            // The empty lower half of the old sidebar now shows a contextual
            // illustrated location preview. Never reveal a fogged node's event art.
            MapNode artNode = show >= 0 ? Campaign.Nodes[show] : Campaign.Current;
            bool revealArt = show < 0 || Known(artNode);
            string artwork = revealArt ? NodePreviewArt(artNode) : "UI/path_panorama";
            float artTop = Mathf.Max(273f, y + 11f);
            float artHeight = Mathf.Min(136f, 416f - artTop);
            if (artHeight > 45f)
                DrawSidebarIllustration(new Rect(x, artTop, w, artHeight), artwork);
            if (Btn(new Rect(x, 540f - 112f, w, 44f), "СНАРЯЖЕНИЕ" + (Profile.Points > 0 ? " (+" + Profile.Points + ")" : ""), interactive)) InventoryScreen.Show(0);
            if (Btn(new Rect(x, 540f - 60f, w, 44f), "В МЕНЮ", interactive)) { Hide(); Time.timeScale = 1f; SceneManager.LoadScene(SceneNames.MainMenu); }
        }

        static string NodePreviewArt(MapNode node)
        {
            if (node.IsFight)
            {
                int idx = Mathf.Clamp(node.enemy, 0, EnemyFullArt.Length - 1);
                return EnemyFullArt[idx];
            }
            switch (node.Type)
            {
                case NodeType.Event: return "UI/event_" + Mathf.Clamp(node.ev, 0, 3);
                case NodeType.Shrine: return "UI/npc_2";
                case NodeType.Smith: return "UI/npc_1";
                case NodeType.Merchant: return "UI/npc_0";
                case NodeType.Rest: return "UI/npc_3";
                case NodeType.Herbalist: return "UI/pack02/herbalist";
                case NodeType.Treasure: return "UI/chest_open";
                default: return "UI/path_panorama";
            }
        }

        static void DrawSidebarIllustration(Rect r, string path)
        {
            if (UiKit.Tex(path) == null) return;
            VisualIdentity.Portrait(r, path);
            // Embossed gold base to match the right-hand panel and give the
            // panorama the same treatment as encounter illustration cards.
            UiKit.Rect(new Rect(r.x + 8f, r.yMax - 9f, r.width - 16f, 2f),
                new Color(VisualIdentity.Brass.r, VisualIdentity.Brass.g,
                    VisualIdentity.Brass.b, 0.50f));
        }

        static bool Btn(Rect r, string label, bool enabled = true, bool red = false)
        {
            return VisualIdentity.Button(r, label, enabled, red);
        }

        /// <summary>Кость пути: костяной кубик с точками (1–3), во время броска крутится.</summary>
        static void Die(Rect r, int face, float spin, bool lit)
        {
            Texture2D dice = JourneyDiceArt.Atlas;
            Matrix4x4 previous = GUI.matrix;
            UiKit.Disc(r.center + new Vector2(0f, 3f), r.width * 0.58f,
                new Color(0.01f, 0.01f, 0.01f, 0.42f));
            if (lit)
            {
                float glow = 0.17f + 0.05f * Mathf.Sin(Time.unscaledTime * 4f);
                UiKit.Disc(r.center, r.width * 0.72f,
                    new Color(0.90f, 0.62f, 0.25f, glow));
            }
            if (spin != 0f) GUIUtility.RotateAroundPivot(spin, r.center * UiKit.Scale);
            int index = Mathf.Clamp(face, 1, 3) - 1;
            GUI.DrawTextureWithTexCoords(r, dice, new Rect(index / 3f, 0f, 1f / 3f, 1f));
            GUI.matrix = previous;
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
            UiKit.Rect(new Rect(0f, 0f, W, 540f),
                new Color(0.015f, 0.012f, 0.012f, 0.65f * a));
            Rect r = new Rect((W - w) * 0.5f, (540f - h) * 0.5f + (1f - a) * 20f, w, h);
            VisualIdentity.Surface(r, a);
            return r;
        }

        // Generated art pack is optional. Existing Resources assets remain the fallback.
        static string PackArt(string name, string fallback)
        {
            string path = "UI/pack01/" + name;
            return UiKit.Tex(path) != null ? path : fallback;
        }

        static string Pack02Art(string name, string fallback)
        {
            string path = "UI/pack02/" + name;
            return UiKit.Tex(path) != null ? path : fallback;
        }

        void Picture(Rect r, string tex)
        {
            VisualIdentity.Portrait(r, tex);
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
                    Picture(new Rect(r.x, r.y, 250f, r.height), Pack02Art("herbalist", UiKit.Tex("UI/npc_4") != null ? "UI/npc_4" : "UI/npc_2"));
                    float x = r.x + 270f, w = r.width - 290f, y = r.y + 22f;
                    UiKit.Text(new Rect(x, y, w, 26f), "ХИЖИНА ТРАВНИЦЫ", 20f, Gold, TextAnchor.MiddleLeft); y += 34f;
                    string tx = modal == Modal.Knocked
                        ? "Ты очнулся на циновке. Пахнет полынью и дымом. «Тебя принесли с дороги еле живого. Отлежался — иди. Вот, возьми на удачу». Травница даёт кость пути."
                        : "Старая травница помешивает отвар. «Ходят тут всякие с мечами… Если что — несите сюда». Она вкладывает тебе в ладонь кость пути.";
                    Lines(x, y, w, tx, 40, 13f, Ink);
                    if (Btn(new Rect(x, r.yMax - 64f, w, 46f), "В ПУТЬ", ok, true)) { if (modal == Modal.Knocked) Campaign.AckKnocked(); modal = Modal.None; }
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

        void Result(string message)
        {
            Campaign.Complete(Campaign.At);
            toastLines.Clear();
            foreach (string line in Wrap(message, 48))
            {
                if (toastLines.Count >= 3) break;
                toastLines.Add(line);
            }
            toastAt = Time.unscaledTime;
            modal = Modal.None; // narrative rewards no longer demand an extra "ДАЛЕЕ" tap
            nextRouteAt = Time.unscaledTime + 1.0f;
        }

        void DrawToast()
        {
            if (toastLines.Count == 0) return;
            float age = Time.unscaledTime - toastAt;
            if (age < 0f || age >= 4.5f) return;
            float fade = Mathf.Clamp01(age / 0.2f) * Mathf.Clamp01((4.5f - age) / 0.7f);
            float w = Mathf.Min(460f, viewW - 30f);
            float h = 26f + toastLines.Count * 19f;
            Rect r = new Rect(mapX + 15f, 540f - h - 15f, w, h);
            VisualIdentity.Surface(r, fade, false);
            UiKit.Rect(new Rect(r.x, r.y, 3f, r.height),
                new Color(Gold.r, Gold.g, Gold.b, fade));
            for (int i = 0; i < toastLines.Count; i++)
                UiKit.Text(new Rect(r.x + 12f, r.y + 8f + i * 19f, r.width - 22f, 19f),
                    toastLines[i], 12f, new Color(Ink.r, Ink.g, Ink.b, fade),
                    TextAnchor.MiddleLeft, false);
        }

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
            // The authored illustrated event series is a real painted asset set.
            // The old pack02 variants were tiny procedural placeholders that
            // made all four encounters appear almost identical.
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
