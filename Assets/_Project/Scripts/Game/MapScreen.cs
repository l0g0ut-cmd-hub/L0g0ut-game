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
        void Awake()
        {
            useGUILayout = false;   // GUILayout нигде не используется: без лишнего прохода Layout IMGUI вдвое дешевле
        }

        public static bool Open { get; private set; }
        static MapScreen inst;

        public static void Ensure() { if (inst == null) inst = new GameObject("MapScreen").AddComponent<MapScreen>(); }
        public static void Show() { Ensure(); Open = true; inst.OnShow(); }
        static void Hide() { Open = false; Profile.CostMul = 1f; }

        static readonly Color Gold = VisualIdentity.Brass;
        static readonly Color Ink = VisualIdentity.Parchment;
        static readonly Color Dim = VisualIdentity.Subdued;
        static readonly string[] EnemyTraits = { "обычный бой — учись отражать", "уходит в дым за спину, финтит", "бьёт по земле — перепрыгивай волну", "три фазы: залпы духа, затем дым" };
        static readonly string[] EnemyFullArt = { "UI/enemy_full_fallen", "UI/enemy_full_shadow", "UI/enemy_full_oni", "UI/enemy_full_shogun" };

        enum Modal { None, Fight, Shrine, Smith, Merchant, Treasure, Rest, Event, RegionClear, Herbalist, Knocked, Camp, Tavern, Dev }
        Modal modal;
        int sel = -1;
        readonly List<int> previewPath = new List<int>(16);
        int plannedRegion = -1;   // keep a chosen destination across intermediate encounters
        readonly Queue<int> routeQueue = new Queue<int>(16);
        int moveFrom = -1, moveTo = -1; float moveStart;
        int routeGoal = -1;   // distant selected node while travelling one hop towards it
        readonly List<int> ridePlan = new List<int>();   // весь маршрут поездки к routeGoal (как его показали игроку)
        float continueRideAt = -1f;   // next hop of a multi-step ride (after a short pause on a passed place)
        float moveDuration = 1.25f;
        float rideCycleAt;
        float panU = -1f, shownAt, modalAt, rollAt = -10f;
        bool manualPan, panArmed, panDragging;
        Vector2 panTouchOrigin;
        float panAtOrigin;
        const float RollTime = 0.9f;
        bool goAfterRoll;   // «В ПУТЬ» без ходов: кость бросается сама, после её анимации всадник трогается
        bool rideFast;      // касание карты во время езды — всадник скачет втрое быстрее до конца поездки

        int TravelTarget()
        {
            return sel >= 0 && Campaign.CanGo(sel) ? sel : sel >= 0 && Campaign.CanGo(NextPreviewStep) ? NextPreviewStep : -1;
        }

        /// <summary>Нужно бросить кость, чтобы выехать к выбранной цели (ходов нет, кость есть, дальше — новое место).</summary>
        bool NeedRollToGo()
        {
            int next = sel >= 0 ? (NextPreviewStep >= 0 ? NextPreviewStep : sel) : -1;
            return next >= 0 && Campaign.Steps <= 0 && Campaign.Rolls > 0 && Campaign.CurrentSettled && !Campaign.InFight
                && !Campaign.Training && moveTo < 0 && !Campaign.FreeHop(next) && Campaign.Adjacent(Campaign.At, next);
        }

        /// <summary>«Ехать» к выбранной цели. Кость бросается только своей кнопкой «БРОСИТЬ КОСТЬ».</summary>
        void TravelOrRoll()
        {
            int t = TravelTarget();
            if (t >= 0 && Campaign.CanGo(t)) Go(t);
        }
        Reward treasure;
        bool freshTreasure, revisit;
        bool routeAutopilot;
        float nextRouteAt;
        float toastAt = -100f;
        readonly List<string> toastLines = new List<string>(3);
        List<Item> stock; int stockNode = -1;
        readonly Dictionary<int, KeyValuePair<Vector2, List<Item>>> stocks = new Dictionary<int, KeyValuePair<Vector2, List<Item>>>();

        void OnShow()
        {
            int resume = !Campaign.Knocked && Campaign.ClearedRegion < 0 && plannedRegion == Campaign.Region ? sel : -1;
            Weather.MapSky = -1;   // вне боя с карты (тренировка с ронином) — погода как раньше
            shownAt = Time.unscaledTime; sel = -1; previewPath.Clear(); moveTo = -1; modal = Modal.None; panU = -1f; continueRideAt = -1f;
            routeAutopilot = false; nextRouteAt = 0f; toastLines.Clear();
            manualPan = false; panArmed = false; panDragging = false;
            if (resume >= 0 && resume < Campaign.Nodes.Count && resume != Campaign.At) SelectNode(resume);
            TerrainRoads.Bind(Campaign.Region, Campaign.Nodes);
            Campaign.CancelTraining();
            if (Campaign.Knocked) OpenModal(Modal.Knocked);
            else if (Campaign.ClearedRegion >= 0) OpenModal(Modal.RegionClear);
            else if (Campaign.InFight || (!Campaign.CurrentSettled && Campaign.Current.Type != NodeType.Start)) OpenNode(Campaign.At);
        }

        void OpenModal(Modal m) { modal = m; modalAt = Time.unscaledTime; }

        void OpenNode(int i)
        {
            MapNode n = Campaign.Nodes[i];
            Profile.CostMul = n.Type == NodeType.Smith ? 0.5f : 1f;
            // Revisiting a cleared road does not repeat paid treasure or defeated enemies.
            // Shrine blessing and rest training are one-time too: walking back
            // and forth must not farm them. Smith, merchant and herbalist reopen.
            if (revisit && n.done && !Campaign.InFight &&
                (n.IsFight || n.Type == NodeType.Treasure || n.Type == NodeType.Event ||
                 n.Type == NodeType.Shrine || n.Type == NodeType.Rest))
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
                    if (stockNode != i || stock == null)
                    {
                        // A merchant keeps the same goods when you come back: revisits
                        // must not reroll the stock. Position guards against a new map.
                        KeyValuePair<Vector2, List<Item>> kept;
                        Vector2 at = new Vector2(n.x, n.y);
                        if (stocks.TryGetValue(i, out kept) && kept.Key == at) stock = kept.Value;
                        else
                        {
                            stock = new List<Item>(); for (int k = 0; k < 3; k++) stock.Add(Profile.Roll(Campaign.Region + 1, 60, k == 0 && Random.value < 0.4f)); Item sc = Random.value < 0.5f ? Profile.NewScroll() : null; if (sc != null) stock[2] = sc;
                            stocks[i] = new KeyValuePair<Vector2, List<Item>>(at, stock);
                        }
                        stockNode = i;
                    }
                    OpenModal(Modal.Merchant); break;
                case NodeType.Treasure: if (treasure != null) OpenModal(Modal.Treasure); else modal = Modal.None; break;
                case NodeType.Rest: if (n.IsTavern && UiKit.Tex("UI/tavern") != null) OpenTavern(); else OpenModal(Modal.Rest); break;
                case NodeType.Event: OpenModal(Modal.Event); break;
                case NodeType.Herbalist: OpenModal(Modal.Herbalist); break;
                default: modal = Modal.None; break;
            }
        }

        void Go(int i)
        {
            if (moveTo >= 0 || modal != Modal.None || Campaign.InFight || !Campaign.CanGo(i)) return;
            Profile.CostMul = 1f;
            // Travel goes to the selected node or along the highlighted route towards it.
            // One tap on В ПУТЬ rides the whole route; the rider stops at every place
            // that opens an encounter (fight, event, chest, service) and when steps run out.
            if (sel != i && NextPreviewStep != i) return;
            routeGoal = sel != i ? sel : -1;   // remember the distant goal for the next hop
            // маршрут, который игрок видел при выборе, — по нему и едем до цели, не перестраивая на каждом перегоне
            if (routeGoal >= 0 && previewPath.Count > 1 && previewPath[0] == Campaign.At) { ridePlan.Clear(); ridePlan.AddRange(previewPath); }
            else if (routeGoal < 0) ridePlan.Clear();
            routeAutopilot = false;
            revisit = Campaign.Nodes[i].done;
            freshTreasure = Campaign.Nodes[i].Type == NodeType.Treasure && !revisit;
            treasure = null;
            moveFrom = Campaign.At; moveTo = i; moveStart = Time.unscaledTime;
            TerrainRoads.Bind(Campaign.Region, Campaign.Nodes);
            moveDuration = Mathf.Clamp(TerrainRoads.Route(moveFrom, moveTo).lengthPixels / 145f,
                0.85f, 7.5f) / (rideFast ? 3f : 1f);
            rideCycleAt = Time.unscaledTime;
            sel = -1; previewPath.Clear(); stockNode = -1;
            manualPan = false; // resume following the animated samurai as the journey proceeds
            // Campaign.MoveTo must run ON ARRIVAL, not at departure: updating
            // Campaign.At early made the marker, map selection and save disagree
            // with the rider visibly travelling between nodes.
            if (Juice.Instance != null) Juice.Instance.DashSound();
        }

        void Update()
        {
            if (!Open) return;
            HandleMapPan();
            if (moveTo >= 0)
            {
                if (!rideFast && modal == Modal.None && !InventoryScreen.Open && Input.GetMouseButtonDown(0))
                {
                    // ускорение без скачка: прогресс перегона сохраняется, сокращается только остаток
                    float done01 = Mathf.Clamp01((Time.unscaledTime - moveStart) / moveDuration);
                    rideFast = true; moveDuration /= 3f; moveStart = Time.unscaledTime - done01 * moveDuration;
                }
                Campaign.Reveal(HeroUV());   // туман расходится вокруг фигурки прямо на ходу
                if (Time.unscaledTime - moveStart >= moveDuration)
                {
                    int i = moveTo; moveTo = -1;
                    // услуга по пути к другой цели (травница, кузнец, торговец, храм, привал, трактир) не останавливает
                    // всадника и не тратится — к ней можно вернуться; бой, событие и тайник по-прежнему останавливают
                    bool serviceOnTheWay = routeGoal >= 0 && routeGoal != i && !revisit && Campaign.IsService(Campaign.Nodes[i]);
                    Campaign.MoveTo(i, serviceOnTheWay); // persist only after the rider arrives
                    if (Campaign.At != i)
                    {
                        // шаг не засчитан (правила похода не пустили) — поездка прекращается, а не продолжается
                        // с прежнего места: иначе всадник метался между точками
                        Debug.LogWarning("Map: arrival at " + i + " rejected, staying at " + Campaign.At);
                        routeGoal = -1; continueRideAt = -1f; ridePlan.Clear(); rideFast = false; sel = -1; previewPath.Clear();
                        return;
                    }
                    if (freshTreasure) treasure = Profile.Treasure(Campaign.Region + 1);
                    freshTreasure = false; // rollback/revisit must not grant a second chest
                    if (Juice.Instance != null) Juice.Instance.Taiko(0.6f, 1.25f);
                    Haptics.Pulse(20);
                    int goal = routeGoal; routeGoal = -1;
                    // тайник по пути к другой цели: награда уже начислена при прибытии — показываем её сообщением, не останавливаясь
                    bool chestOnTheWay = goal >= 0 && goal != i && treasure != null;
                    if (chestOnTheWay)
                    {
                        TavToast("Тайник по пути: +" + treasure.gold + " мон, +" + treasure.xp + " опыта"
                            + (treasure.items.Count > 0 ? ", предметов: " + treasure.items.Count : "") + (treasure.levels > 0 ? ". Новый уровень!" : "."));
                        treasure = null;
                    }
                    bool passing = goal >= 0 && goal != i && (revisit || serviceOnTheWay || chestOnTheWay);
                    // Riding THROUGH an already visited place does not reopen its smith or
                    // merchant; a new place always opens its encounter and ends the ride.
                    if (!passing) OpenNode(i); else modal = Modal.None;
                    if (goal >= 0 && goal != Campaign.At && goal < Campaign.Nodes.Count)
                    {
                        // продолжаем по сохранённому маршруту; перестраиваем, только если он больше не ведёт от текущего места
                        int here = ridePlan.IndexOf(Campaign.At);
                        if (here >= 0 && here + 1 < ridePlan.Count && ridePlan[ridePlan.Count - 1] == goal)
                        {
                            sel = goal; plannedRegion = Campaign.Region;
                            previewPath.Clear();
                            for (int k = here; k < ridePlan.Count; k++) previewPath.Add(ridePlan[k]);
                        }
                        else SelectNode(goal);
                        // nothing to resolve here: continue the same ride to the goal
                        if (modal == Modal.None && Campaign.CanGo(NextPreviewStep)) continueRideAt = Time.unscaledTime + (rideFast ? 0.1f : 0.3f);
                    }
                    nextRouteAt = Time.unscaledTime + 0.45f;
                    if (continueRideAt < 0f) rideFast = false;   // поездка закончилась — следующая снова в обычном темпе
                }
                return;
            }
            if (goAfterRoll && Time.unscaledTime - rollAt >= RollTime)
            {
                goAfterRoll = false;
                int t = TravelTarget();
                if (modal == Modal.None && t >= 0) Go(t);
            }
            // Continue a ride started by one В ПУТЬ tap, but only while nothing is open:
            // an encounter on the way always stops the rider.
            if (continueRideAt > 0f && Time.unscaledTime >= continueRideAt)
            {
                continueRideAt = -1f;
                int hop = NextPreviewStep;
                if (modal == Modal.None && !Campaign.InFight && Campaign.CanGo(hop)) Go(hop);
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
            // панель справа ужимается, чтобы карта влезла целиком (раньше на широком телефоне не хватало ~10 px — карта «листалась» впустую)
            float panel = Mathf.Clamp(Mathf.Min(W * 0.3f, W - MapFullW), 250f, 340f);
            viewW = Mathf.Min(MapFullW, W - panel);
            mapX = Mathf.Max(0f, (W - panel - MapFullW) * 0.5f);
            float uw = viewW / MapFullW;
            // окно карты едет за всадником
            TerrainRoads.Bind(Campaign.Region, Campaign.Nodes);
            Vector2 hu = HeroUV();
            float target = Mathf.Clamp(hu.x - uw * 0.45f, 0f, 1f - uw);
            panU = panU < 0f ? target : manualPan ? panU :
                Mathf.Lerp(panU, target, Mathf.Clamp01(Time.unscaledDeltaTime * 3f));
            panU = Mathf.Clamp(panU, 0f, Mathf.Max(0f, 1f - uw));
            u0 = panU;
            Texture2D mt = UiKit.Tex(Campaign.MapTexture(Campaign.Region));
            if (mt != null) GUI.DrawTextureWithTexCoords(new Rect(mapX, 0f, viewW, MapH), mt, new Rect(u0, 0f, uw, 1f));
            MapLife.Draw(Campaign.Region, new Rect(mapX, 0f, viewW, MapH), u0, uw);   // вода бежит, огни мерцают, пар, лава, блуждающие огни
            DrawPaintedMist();
            DrawLandmarkLights();
            DrawAmbientLandmarkMotes();
            DrawRoads();
            DrawJourneyFootprints();
            DrawRouteGlints();
            DrawFog(uw);
            DrawBuildings();
            DrawTower();
            // погода — после зданий, до значков: облака загораживают постройки, но не значки мест
            MapClouds.Draw(Campaign.Region, new Rect(mapX, 0f, viewW, MapH), u0, uw);
            nodeLabels.Clear();
            DrawNodes(interactive && modal == Modal.None && moveTo < 0);
            DrawHero();
            DrawNodeLabels();
            MapBirds.Draw(new Rect(mapX, 0f, viewW, MapH), u0, uw);   // ворон или стайка цапель пролетают над всем — и над значками (под ними значки видно, это не облака)
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
            // карту листают пальцем; увёл её от героя — появляется «К ГЕРОЮ» (кнопка «ОБЗОР» ничего не делала — убрана)
            if (uw < 0.999f && manualPan && Btn(new Rect(mapX + viewW - 116f, 14f, 104f, 33f), "К ГЕРОЮ", interactive && modal == Modal.None && moveTo < 0))
            {
                manualPan = false; panU = -1f;
            }
            // меню разработчика (отладка): телепорт, лагерь, трактир, следующая область
            if (Btn(new Rect(mapX + viewW - (uw < 0.999f ? 226f : 116f), 14f, 104f, 33f), devTeleport ? "ОТМЕНА" : "РАЗРАБ.", interactive && (modal == Modal.None || modal == Modal.Dev) && moveTo < 0))
            {
                if (devTeleport) devTeleport = false; else OpenModal(Modal.Dev);
            }
            if (devTeleport) UiKit.Text(new Rect(mapX, 52f, viewW, 24f), "ТЕЛЕПОРТ: нажми на любую точку карты", 15f, new Color(1f, 0.6f, 0.4f));
            DrawPanel(new Rect(W - panel, 0f, panel, 540f), interactive && modal == Modal.None && moveTo < 0);
            if (modal != Modal.None) { DrawModal(interactive); if ((modal == Modal.Camp || modal == Modal.Tavern) && interactive) DrawToast(); }
            else if (interactive) DrawToast();
            GUI.matrix = old;
        }

        Vector2 HeroUV()
        {
            List<MapNode> ns = Campaign.Nodes;
            if (moveTo >= 0 && moveFrom >= 0)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - moveStart) / moveDuration);
                t = t * t * (3f - 2f * t);
                return TerrainRoads.Route(moveFrom, moveTo).Position(t);
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

        // Painted herbalist medallion (same style as map_icons). The drawn green
        // stamp over the start flag remains only as a fallback.
        const string HerbalistIcon = "UI/map_icon_herbalist";
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

        /// <summary>Радиус обзора героя в точках карты 810×540: ворон-спутник видит дальше.</summary>
        static float SightR { get { return Profile.Companion == Profile.CompanionRaven ? 175f : 125f; } }

        /// <summary>
        /// Место видно, если туман над ним разошёлся (герой подходил достаточно близко), как в «Героях 3».
        /// Владыка области виден всегда — это цель похода.
        /// </summary>
        // ---- здания на карте (UI/map_buildings: 4×2 — трактир, кузница, лавка, привал, хижина травницы, храм, тайник, засада)
        // Стоят рядом со своим значком, как смотровая башня: значок — куда нажать, здание — что там. Огонь в них мерцает.
        static readonly Vector3[][] BuildingLights =
        {
            new[] { new Vector3(0.58f, 0.62f, 0.16f), new Vector3(0.30f, 0.59f, 0.1f) },    // трактир: фонарь, окно
            new[] { new Vector3(0.35f, 0.55f, 0.2f) },                                        // кузница: горн
            new Vector3[0],                                                                   // лавка
            new[] { new Vector3(0.375f, 0.68f, 0.2f) },                                       // привал: костёр
            new[] { new Vector3(0.68f, 0.5f, 0.1f) },                                         // травница: окно
            new[] { new Vector3(0.29f, 0.43f, 0.1f), new Vector3(0.8f, 0.41f, 0.1f), new Vector3(0.49f, 0.43f, 0.1f) },   // храм: фонари
            new[] { new Vector3(0.46f, 0.48f, 0.16f) },                                       // тайник: золото
            new Vector3[0],                                                                   // засада
        };
        static int BuildingOf(MapNode n)
        {
            if (n.IsTavern) return 0;
            switch (n.Type)
            {
                case NodeType.Smith: return 1; case NodeType.Merchant: return 2; case NodeType.Rest: return 3;
                case NodeType.Herbalist: return 4; case NodeType.Shrine: return 5; case NodeType.Treasure: return 6; case NodeType.Ambush: return 7;
                default: return -1;
            }
        }
        // ---- стоянки и места событий (UI/map_camps_<область>: 4×2 — одиночка, застава, лагерь отряда, логово, перекрёсток, руины, могилы, разорённая стоянка)
        static int CampOf(MapNode n, int i)
        {
            switch (n.Type)
            {
                case NodeType.Duel: case NodeType.Elite:
                    if (n.done) return 7;
                    if (n.extra != null && n.extra.Count > 0) return 2;
                    return n.Type == NodeType.Elite ? 1 : 0;
                case NodeType.Event: return 4 + (i * 7 + 3) % 3;   // перекрёсток, руины или могилы — по месту, всегда одно и то же
                default: return -1;   // логово главаря уже нарисовано на карте области
            }
        }

        // место здания у значка: из нескольких сторон выбирается та, что дальше всего от дорог, воды (мост!) и других мест
        static readonly Vector2[] BuildingSpots = { new Vector2(38f, 0f), new Vector2(-38f, 0f), new Vector2(30f, -22f), new Vector2(-30f, -22f), new Vector2(0f, -34f), new Vector2(34f, 24f), new Vector2(-34f, 24f),
                                                    new Vector2(54f, -12f), new Vector2(-54f, -12f), new Vector2(42f, -40f), new Vector2(-42f, -40f) };   // дальние — когда у ближних вода
        readonly Dictionary<int, Vector2> buildingOff = new Dictionary<int, Vector2>();
        string buildingKey;
        Vector2 BuildingOffset(int i, int tower)
        {
            List<MapNode> ns = Campaign.Nodes;
            string key = Campaign.Region + ":" + ns.Count + ":" + ns[0].x + ":" + tower;
            if (key != buildingKey) { buildingKey = key; buildingOff.Clear(); }
            Vector2 got;
            if (buildingOff.TryGetValue(i, out got)) return got;
            TerrainRoads.Bind(Campaign.Region, ns);
            Vector2 k = new Vector2(810f, 540f), np = new Vector2(ns[i].x, ns[i].y);
            // точки дорог вокруг (все дороги места и соседей)
            List<Vector2> road = new List<Vector2>(64);
            for (int a = 0; a < ns.Count; a++)
                foreach (int b in ns[a].next)
                {
                    if ((Vector2.Scale(new Vector2(ns[a].x, ns[a].y) - np, k)).sqrMagnitude > 260f * 260f && (Vector2.Scale(new Vector2(ns[b].x, ns[b].y) - np, k)).sqrMagnitude > 260f * 260f) continue;
                    TerrainRoads.Path pth = TerrainRoads.Route(a, b);
                    for (int q = 0; q <= 12; q++) road.Add(Vector2.Scale(pth.Position(q / 12f), k));
                }
            float bestScore = -1e9f; Vector2 best = BuildingSpots[0];
            // экранные смещения ~ точкам карты 810 (карта на экране почти в масштабе); центр здания — чуть выше основания
            foreach (Vector2 sp in BuildingSpots)
            {
                if (i == tower && sp.x > 10f && sp.y < 5f) continue;   // справа-сверху стоит смотровая башня
                Vector2 basePt = Vector2.Scale(np, k) + sp, center = basePt + new Vector2(0f, -26f);
                float dRoad = 1e9f; foreach (Vector2 rp in road) dRoad = Mathf.Min(dRoad, Mathf.Min((rp - basePt).magnitude, (rp - center).magnitude));
                float dWater = 1e9f;   // по основанию, центру и бокам спрайта (74 точки шириной)
                foreach (Vector2 wp in new[] { basePt, center, basePt + new Vector2(-30f, -8f), basePt + new Vector2(30f, -8f) })
                    dWater = Mathf.Min(dWater, MapLife.DistToWater(Campaign.Region, new Vector2(wp.x / 810f, wp.y / 540f)));
                float dNode = 1e9f; for (int o = 0; o < ns.Count; o++) if (o != i) dNode = Mathf.Min(dNode, (Vector2.Scale(new Vector2(ns[o].x, ns[o].y), k) - center).magnitude);
                // река широкая (линия — её середина): ближе ~60 точек к середине здание стоит на воде или на самом берегу — сильный штраф
                float far = sp.magnitude > 50f ? 6f : 0f;
                float score = Mathf.Min(dRoad, 40f) + 2.5f * Mathf.Min(dWater, 80f) + 0.8f * Mathf.Min(dNode, 60f) - (sp == BuildingSpots[0] ? 0f : 3f) - far
                              - (dWater < 60f ? (60f - dWater) * 6f : 0f);
                if (score > bestScore) { bestScore = score; best = sp; }
            }
            buildingOff[i] = best;
            return best;
        }

        void DrawBuildings()
        {
            if (Event.current.type != EventType.Repaint) return;
            // свой набор у каждой области: лес (_0), горы Они (общий), сёгунат (_2)
            Texture2D atlas = Campaign.Region != 1 ? UiKit.Tex("UI/map_buildings_" + Campaign.Region) : null, glow = UiKit.Tex("FX/glow_pool");
            if (atlas == null) atlas = UiKit.Tex("UI/map_buildings");
            Texture2D camps = UiKit.Tex("UI/map_camps_" + Campaign.Region);   // стоянки у боёв и места событий — свой набор у каждой области
            if (atlas == null && camps == null) return;
            Color oc = GUI.color;
            float t = Time.unscaledTime;
            List<MapNode> ns = Campaign.Nodes;
            int tower = JourneyChoice.FogEnabled && ns.Count > 0 ? Campaign.Tower : -1;
            for (int i = 0; i < ns.Count; i++)
            {
                MapNode n = ns[i];
                int b = atlas != null ? BuildingOf(n) : -1;
                int cp = b < 0 && camps != null ? CampOf(n, i) : -1;
                if ((b < 0 && cp < 0) || !Known(n)) continue;
                if (b == 7 && n.done) continue;   // засада после боя — убрана с дороги
                Vector2 p = P(n);
                if (p.x < mapX - 80f || p.x > mapX + viewW + 80f) continue;
                const float size = 74f;
                Vector2 off = BuildingOffset(i, tower);
                Rect r = new Rect(p.x + off.x - size * 0.5f, p.y + off.y - size * 0.78f, size, size);
                if (cp >= 0)
                {
                    // стоянка: после победы — разорённая (ячейка 8), место события после посещения приглушено
                    GUI.color = n.done && cp >= 4 && cp <= 6 ? new Color(0.72f, 0.7f, 0.68f, 0.9f) : Color.white;
                    GUI.DrawTextureWithTexCoords(r, camps, new Rect((cp % 4) * 0.25f, cp < 4 ? 0.5f : 0f, 0.25f, 0.5f));
                    continue;
                }
                bool dim = n.done && b != 0 && b != 1 && b != 2;   // пройденные приглушены (трактир, кузница и лавка — живые всегда)
                GUI.color = dim ? new Color(0.72f, 0.7f, 0.68f, 0.9f) : Color.white;
                GUI.DrawTextureWithTexCoords(r, atlas, new Rect((b % 4) * 0.25f, b < 4 ? 0.5f : 0f, 0.25f, 0.5f));
                if (glow == null || (b == 6 && n.done)) continue;
                foreach (Vector3 l in BuildingLights[b])
                {
                    float fl = 0.6f + 0.4f * Mathf.PerlinNoise(t * 3f + i * 3.1f + l.x * 9f, l.y * 7f);
                    float g = l.z * size * (1f + 0.25f * fl);
                    Vector2 c = new Vector2(r.x + l.x * size, r.y + l.y * size);
                    GUI.color = b == 6 ? new Color(1f, 0.85f, 0.35f, 0.45f * fl) : new Color(1f, 0.6f, 0.25f, (dim ? 0.25f : 0.5f) * fl);
                    GUI.DrawTexture(new Rect(c.x - g, c.y - g, g * 2f, g * 2f), glow);
                }
            }
            GUI.color = oc;
        }

        bool devTeleport;   // меню разработчика: следующее нажатие на карту — телепорт

        void DrawDev(bool ok)
        {
            Rect r = Card(520f, 470f);
            float x = r.x + 24f, w = r.width - 48f, y = r.y + 18f;
            VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "МЕНЮ РАЗРАБОТЧИКА", 20f); y += 38f;
            float bw = (w - 10f) * 0.5f;
            if (Btn(new Rect(x, y, bw, 44f), "ТЕЛЕПОРТ", ok, true)) { modal = Modal.None; devTeleport = true; return; }
            if (Btn(new Rect(x + bw + 10f, y, bw, 44f), "ЛАГЕРЬ", ok)) { OpenCamp(); return; }
            y += 52f;
            if (Btn(new Rect(x, y, bw, 44f), "ТРАКТИР", ok && UiKit.Tex("UI/tavern") != null)) { OpenTavern(); return; }
            if (Btn(new Rect(x + bw + 10f, y, bw, 44f), "СЛЕД. ОБЛАСТЬ", ok)) { Campaign.DebugNextRegion(); modal = Modal.None; sel = -1; previewPath.Clear(); panU = -1f; TerrainRoads.Bind(Campaign.Region, Campaign.Nodes); return; }
            y += 52f;
            if (Btn(new Rect(x, y, bw, 44f), "+3 КОСТИ ПУТИ", ok)) { Campaign.AddRoll(3); }
            if (Btn(new Rect(x + bw + 10f, y, bw, 44f), "+500 МОН", ok)) { Profile.AddGold(500); }
            y += 52f;
            if (Btn(new Rect(x, y, bw, 44f), "+1 ОЧКО", ok)) { Profile.GivePoint(); }
            if (Btn(new Rect(x + bw + 10f, y, bw, 44f), JourneyChoice.FogEnabled ? "ТУМАН: ВКЛ" : "ТУМАН: ВЫКЛ", ok)) { JourneyChoice.FogEnabled = !JourneyChoice.FogEnabled; }
            y += 52f;
            if (Btn(new Rect(x, y, w, 44f), "ВЫБРОСИТЬ КОСТЬ: 3 ХОДА", ok)) { Campaign.DebugSteps(3); }
            y += 52f;
            if (Btn(new Rect(x, y, w, 44f), SideDuel.Enabled ? "СОРАТНИКИ: ВКЛ" : "СОРАТНИКИ: ВЫКЛ", ok)) { SideDuel.Enabled = !SideDuel.Enabled; }
            if (Btn(new Rect(r.center.x - 80f, r.yMax - 52f, 160f, 40f), "ЗАКРЫТЬ", ok)) modal = Modal.None;
        }

        bool Known(MapNode n)
        {
            if (devTeleport) return true;
            if (!JourneyChoice.FogEnabled) return true;   // туман войны выключен на экране сложности — всё видно
            if (n.done || n.Type == NodeType.Boss || n.Type == NodeType.Start) return true;
            return FogOfWar.At(new Vector2(n.x, n.y)) < 0.5f;
        }

        /// <summary>Соседнее место в тумане: дорога туда видна, а что там — нет (метка «?»).</summary>
        bool Hinted(int i) { return JourneyChoice.FogEnabled && Campaign.Adjacent(Campaign.At, i); }

        const float TowerR = 290f;   // смотровая башня открывает вокруг себя, точки карты 810×540
        float towerLitAt = -100f; bool towerWasLit;

        /// <summary>Радиус, открытый башней сейчас: огонь разгорается за 1,8 с (если зажжён при открытой карте).</summary>
        float TowerReveal()
        {
            if (!Campaign.TowerLit) { towerWasLit = false; return 0f; }
            if (!towerWasLit) { towerWasLit = true; towerLitAt = shownAt > Time.unscaledTime - 0.2f ? -100f : Time.unscaledTime; }
            float k = Mathf.Clamp01((Time.unscaledTime - towerLitAt) / 1.8f);
            return TowerR * (1f - (1f - k) * (1f - k));
        }

        /// <summary>
        /// Смотровая башня на карте: силуэт башни (UI/map_watchtower, если есть) у места; пока огонь не зажжён —
        /// тлеющая искра, видная даже сквозь туман; зажжённая — сигнальный костёр с ореолом.
        /// </summary>
        void DrawTower()
        {
            if (!JourneyChoice.FogEnabled || Event.current.type != EventType.Repaint || Campaign.Nodes.Count == 0) return;
            MapNode n = Campaign.Nodes[Campaign.Tower];
            Vector2 p = P(n) + new Vector2(30f, -30f);
            if (p.x < mapX - 60f || p.x > mapX + viewW + 60f) return;
            bool lit = Campaign.TowerLit;
            float t = Time.unscaledTime;
            Color oc = GUI.color;
            Texture2D glow = UiKit.Tex("FX/glow_pool"), fire = UiKit.Tex("FX/brazier_fire"), tower = UiKit.Tex("UI/map_watchtower");
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.1f) * Mathf.Sin(t * 1.7f + 1f);
            if (glow != null)
            {
                float g = lit ? 78f + 10f * pulse : 40f + 6f * pulse;
                GUI.color = new Color(1f, 0.62f, 0.22f, lit ? 0.5f : 0.32f);
                GUI.DrawTexture(new Rect(p.x - g, p.y - 40f - g, g * 2f, g * 2f), glow);
            }
            Vector2 basket = new Vector2(p.x, p.y - 36f);   // где жаровня (у запасной вышки)
            if (tower != null)
            {
                // UI/map_watchtower — квадрат 512, основание внизу; жаровня на площадке в точке (0,52; 0,225) картинки
                Rect tr = new Rect(p.x - 36f, p.y - 64f, 72f, 72f);
                GUI.color = lit ? Color.white : new Color(0.82f, 0.8f, 0.86f, 1f);
                GUI.DrawTexture(tr, tower);
                basket = new Vector2(tr.x + 0.52f * tr.width, tr.y + 0.225f * tr.height);
            }
            else
            {   // запасной силуэт: деревянная вышка из линий
                Color wood = new Color(0.22f, 0.14f, 0.08f, 0.95f);
                UiKit.Line(new Vector2(p.x - 13f, p.y + 6f), new Vector2(p.x - 7f, p.y - 34f), 3f, wood);
                UiKit.Line(new Vector2(p.x + 13f, p.y + 6f), new Vector2(p.x + 7f, p.y - 34f), 3f, wood);
                UiKit.Line(new Vector2(p.x - 11f, p.y - 8f), new Vector2(p.x + 11f, p.y - 8f), 2f, wood);
                UiKit.Line(new Vector2(p.x - 12f, p.y - 8f), new Vector2(p.x + 9f, p.y - 24f), 1.6f, wood);
                UiKit.Rect(new Rect(p.x - 12f, p.y - 38f, 24f, 5f), wood);
            }
            if (fire != null)
            {
                int f = (int)(t * 12f) % 8;
                float fs = lit ? 34f : 16f;
                GUI.color = lit ? Color.white : new Color(1f, 0.7f, 0.5f, 0.75f);
                GUI.DrawTextureWithTexCoords(new Rect(basket.x - fs * 0.5f, basket.y - fs * 0.85f, fs, fs), fire,
                    new Rect((f % 4) * 0.25f, f < 4 ? 0.5f : 0f, 0.25f, 0.5f));
            }
            GUI.color = oc;
        }

        // ---- fog of war (FogOfWar.cs): расходится вокруг героя по мере движения и не возвращается
        void DrawFog(float uw)
        {
            if (!JourneyChoice.FogEnabled) return;
            MapNode tw = Campaign.Nodes.Count > 0 ? Campaign.Nodes[Campaign.Tower] : null;
            FogOfWar.Draw(new Rect(mapX, 0f, viewW, MapH), u0, uw, Campaign.Seen, SightR, Campaign.Region + ":" + SightR,
                tw != null ? new Vector2(tw.x, tw.y) : Vector2.zero, TowerReveal());
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
            if (index < 0 || index >= nodes.Count) { sel = -1; return; }
            int start = Campaign.At;
            if (start == index) { previewPath.Add(start); return; }

            // A step back is only ever a direct hop to a NEIGHBOURING visited place the
            // player tapped. Longer routes go FORWARD only: a forward-and-back search made
            // taps on distant markers send the rider back through already passed places.
            if (!nodes[start].next.Contains(index) && Campaign.Adjacent(start, index))
            {
                previewPath.Add(start); previewPath.Add(index);
                return;
            }
            // Кратчайший маршрут: сначала меньше ходов кости (новые места), затем короче дорога. Раньше маршрут
            // искался в ширину только по дорогам «вперёд», а если цель лежала вбок — откатом по пройденным
            // местам: всадник делал крюк через уже пройденного кузнеца, хотя к сундуку вела своя дорога.
            // Правила те же: вперёд — по дорогам похода; вбок и назад — только через пройденные места (или в саму цель).
            TerrainRoads.Bind(Campaign.Region, nodes);
            int[] parents = new int[nodes.Count];
            float[] cost = new float[nodes.Count];
            bool[] closed = new bool[nodes.Count];
            for (int i = 0; i < parents.Length; i++) { parents[i] = -1; cost[i] = float.MaxValue; }
            parents[start] = start; cost[start] = 0f;
            while (true)
            {
                int at = -1;
                for (int i = 0; i < cost.Length; i++)
                    if (!closed[i] && cost[i] < float.MaxValue && (at < 0 || cost[i] < cost[at])) at = i;
                if (at < 0 || at == index) break;
                closed[at] = true;
                for (int next = 0; next < nodes.Count; next++)
                {
                    if (closed[next] || next == at) continue;
                    bool forward = nodes[at].next.Contains(next);
                    if (!forward && !(Campaign.Adjacent(at, next) && (nodes[next].done || next == index))) continue;
                    if (!forward && !nodes[next].done && next != index) continue;   // через неизведанное — только вперёд
                    float c = cost[at] + (nodes[next].done ? 0f : 100000f) + TerrainRoads.Route(at, next).lengthPixels;
                    if (c < cost[next]) { cost[next] = c; parents[next] = at; }
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

        /// <summary>Сколько ходов кости стоит маршрут до его k-й точки: пройденные места бесплатны.</summary>
        int PathCost(int k)
        {
            int c = 0;
            for (int i = 1; i <= k && i < previewPath.Count; i++)
            {
                int n = previewPath[i];
                if (n >= 0 && n < Campaign.Nodes.Count && !Campaign.Nodes[n].done) c++;
            }
            return c;
        }

        /// <summary>«1 шаг», «2 шага», «5 шагов».</summary>
        static string Steps(int n)
        {
            int a = Mathf.Abs(n) % 100, b = a % 10;
            string w = a >= 11 && a <= 14 ? "шагов" : b == 1 ? "шаг" : b >= 2 && b <= 4 ? "шага" : "шагов";
            return n + " " + w;
        }

        /// <summary>Где остановится всадник на этом ходу: последняя точка маршрута, до которой хватает шагов кости.</summary>
        int StopOfThisTurn()
        {
            int stop = -1;
            for (int k = 1; k < previewPath.Count; k++) if (PathCost(k) <= Campaign.Steps) stop = k; else break;
            return stop;
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
                    ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - moveStart) / moveDuration))
                    : 1f;
                DrawFootprintsOnRoad(a, b, new Color(0.64f, 0.33f, 0.16f, 0.68f), progress, false);
            }

            if (moveTo >= 0)
            {
                DrawFootprintsOnRoad(moveFrom, moveTo,
                    new Color(1f, 0.84f, 0.43f, 0.92f),
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - moveStart) / moveDuration)), true);
                return;
            }

            // Before selecting a destination, preview available next roads subtly.
            if (previewPath.Count <= 1 && Campaign.Steps > 0 && Campaign.CurrentSettled && !Campaign.InFight)
            {
                for (int next = 0; next < Campaign.Nodes.Count; next++)
                    if (Campaign.Adjacent(Campaign.At, next))
                        DrawFootprintsOnRoad(Campaign.At, next,
                            new Color(0.88f, 0.79f, 0.48f, 0.48f), 1f, false);
            }

            for (int i = 1; i < previewPath.Count; i++)
            {
                bool withinDie = PathCost(i) <= Campaign.Steps;
                Color tint = withinDie && Campaign.CurrentSettled && !Campaign.InFight
                    ? new Color(0.67f, 1f, 0.65f, 0.95f)
                    : new Color(1f, 0.35f, 0.29f, 0.67f);
                DrawFootprintsOnRoad(previewPath[i - 1], previewPath[i], tint, 1f, withinDie);
            }
            // где всадник остановится на этом ходу, если цель дальше броска: пульсирующее кольцо на месте остановки
            int stopK = StopOfThisTurn();
            if (stopK > 0 && stopK < previewPath.Count - 1 && Campaign.CurrentSettled && !Campaign.InFight)
            {
                MapNode sn = Campaign.Nodes[previewPath[stopK]];
                Vector2 sp = P(sn);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                UiKit.Ring(sp, 30f + 4f * pulse, new Color(0.67f, 1f, 0.65f, 0.55f + 0.35f * pulse), true);
                UiKit.Ring(sp, 34f + 4f * pulse, new Color(0.67f, 1f, 0.65f, 0.25f * pulse), true);
            }
        }

        void DrawFootprintsOnRoad(int from, int to, Color tint, float progress, bool shimmer)
        {
            List<MapNode> nodes = Campaign.Nodes;
            if (from < 0 || from >= nodes.Count || to < 0 || to >= nodes.Count || progress <= 0f) return;
            Vector2 a = new Vector2(nodes[from].x, nodes[from].y);
            Vector2 b = new Vector2(nodes[to].x, nodes[to].y);
            float len = TerrainRoads.Route(from, to).lengthPixels;
            int count = Mathf.Clamp(Mathf.RoundToInt(len / 17f), 5, 22);
            Texture2D feet = JourneyFootprintArt.Footsteps;
            Color prev = GUI.color;
            Matrix4x4 original = GUI.matrix;
            for (int j = 1; j < count; j++)
            {
                float t = j / (float)count;
                if (t < 0.13f || t > 0.87f || t > progress) continue;
                Vector2 uv = TerrainRoads.Route(from, to).Position(t);
                Vector2 point = new Vector2(mapX + (uv.x - u0) * MapFullW, uv.y * MapH);
                if (point.x < mapX + 7f || point.x > mapX + viewW - 7f) continue;
                TerrainRoads.Path road = TerrainRoads.Route(from, to);
                Vector2 back = road.Position(Mathf.Max(0f, t - 0.015f));
                Vector2 ahead = road.Position(Mathf.Min(1f, t + 0.015f));
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
                    bool walked = (Campaign.WasTravelled(a, b) || Campaign.WasTravelled(b, a)) &&
                        !(moveFrom == a && moveTo == b) && !(moveFrom == b && moveTo == a);
                    // A road also pulses when it leads back from here to a visited place.
                    bool open = (a == Campaign.At || (b == Campaign.At && ns[a].done)) && cur.done && moveTo < 0;
                    float alpha = walked ? 0.95f : open ? 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 5f) : Known(ns[b]) ? 0.45f : 0.22f;
                    Color col = walked ? new Color(0.55f, 0.06f, 0.04f, alpha) : new Color(0.05f, 0.04f, 0.04f, alpha);
                    TerrainRoads.Path road = TerrainRoads.Route(a, b);
                    float len = road.lengthPixels;
                    int dots = Mathf.Max(3, (int)(len / 14f));
                    for (int k = 1; k < dots; k++)
                    {
                        float t = k / (float)dots;
                        if (t < 0.12f || t > 0.88f) continue;   // не залезать на медальоны
                        Vector2 p = road.Position(t);
                        Vector2 s = new Vector2(mapX + (p.x - u0) * MapFullW, p.y * MapH);
                        if (s.x < mapX - 6f || s.x > mapX + viewW + 6f) continue;
                        float r = walked ? 4.2f : 3.4f;
                        GUI.color = col;
                        GUI.DrawTextureWithTexCoords(new Rect(s.x - r, s.y - r, r * 2f, r * 2f), ic, Cell(14));
                    }
                    if (open)
                    {
                        // стрелка тушью ближе к концу открытой дороги
                        Vector2 p1 = road.Position(0.74f), p2 = road.Position(0.8f);
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

        readonly List<KeyValuePair<Rect, string>> nodeLabels = new List<KeyValuePair<Rect, string>>();
        void DrawNodeLabels()
        {
            foreach (var l in nodeLabels)
            {
                VisualIdentity.RowPanel(new Rect(l.Key.x - 3f, l.Key.y - 2f, l.Key.width + 6f, l.Key.height + 4f), 0.95f);
                UiKit.Text(l.Key, l.Value, 10.5f, new Color(0.96f, 0.88f, 0.68f, 0.98f), TextAnchor.MiddleCenter, false);
            }
        }

        /// <summary>Мягкая овальная тень (FX/glow_pool) на земле карты. Только в Repaint.</summary>
        static void SoftShadow(Vector2 c, float w, float h, float alpha)
        {
            if (Event.current.type != EventType.Repaint) return;
            Texture2D t = UiKit.Tex("FX/glow_pool");
            if (t == null) return;
            Color o = GUI.color;
            GUI.color = new Color(0.02f, 0.015f, 0.01f, alpha);
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h), t);
            GUI.color = o;
        }

        void DrawNodes(bool interactive)
        {
            Texture2D ic = UiKit.Tex("UI/map_icons");
            List<MapNode> ns = Campaign.Nodes;
            Color oc = GUI.color;
            // Marker hit rectangles overlap on dense maps. Only the nearest
            // visible medal can accept this touch, never another one behind it.
            int hitNode = -1;
            if (interactive && !panDragging)
            {
                Vector3 mouse = Input.mousePosition;
                Vector2 point = new Vector2(mouse.x / UiKit.Scale,
                    (Screen.height - mouse.y) / UiKit.Scale);
                float nearestSq = 32f * 32f;
                if (point.x >= mapX && point.x <= mapX + viewW &&
                    point.y >= 0f && point.y <= MapH)
                {
                    for (int candidate = 0; candidate < ns.Count; candidate++)
                    {
                        Vector2 position = P(ns[candidate]);
                        if (position.x < mapX || position.x > mapX + viewW) continue;
                        float d2 = (position - point).sqrMagnitude;
                        if (d2 < nearestSq) { nearestSq = d2; hitNode = candidate; }
                    }
                }
            }
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
                // под туманом войны места скрыты; соседнее место видно как «?» — дорога туда ведёт, а что там — нет
                if (!known && n.Type != NodeType.Boss && !Hinted(i)) continue;
                SoftShadow(new Vector2(p.x + s * 0.06f, p.y + s * 0.40f), s * 0.95f, s * 0.30f, 0.42f);   // медальон стоит на земле, а не висит над картой
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
                Texture2D herb = n.Type == NodeType.Herbalist ? UiKit.Tex(HerbalistIcon) : null;
                if (!known)
                {
                    Texture2D unk = UiKit.Tex("UI/map_unknown");   // медальон «неизвестное место»: туман и знак вопроса
                    if (unk != null) { GUI.color = new Color(1f, 1f, 1f, can ? 1f : 0.85f); GUI.DrawTexture(new Rect(p.x - s * 0.46f, p.y - s * 0.46f, s * 0.92f, s * 0.92f), unk); }
                    else
                    {
                        UiKit.Disc(p, s * 0.4f, new Color(0.10f, 0.08f, 0.13f, 0.88f));
                        UiKit.Ring(p, s * 0.4f, new Color(0.75f, 0.68f, 0.9f, can ? 0.9f : 0.55f), false);
                        UiKit.Text(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), "?", s * 0.5f, new Color(0.9f, 0.86f, 1f, 0.95f), TextAnchor.MiddleCenter);
                    }
                }
                else if (herb != null)
                    GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), herb);
                else if (n.IsTavern && UiKit.Tex("UI/map_tavern") != null)
                    GUI.DrawTexture(new Rect(p.x - s * 0.47f, p.y - s * 0.47f, s * 0.94f, s * 0.94f), UiKit.Tex("UI/map_tavern"));
                else if (ic != null)
                    GUI.DrawTextureWithTexCoords(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), ic, Cell(k));
                else UiKit.Disc(p, s * 0.45f, can ? Gold : Dim);   // атлас значков не загрузился — простой кружок
                // отряд: малый медальон с тремя воинами у края значка
                if (known && !n.done && n.extra != null && n.extra.Count > 0 && UiKit.Tex("UI/map_squad") != null)
                {
                    Color pc = GUI.color; GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(p.x - s * 0.62f, p.y - s * 0.62f, s * 0.5f, s * 0.5f), UiKit.Tex("UI/map_squad"));
                    UiKit.Text(new Rect(p.x - s * 0.62f, p.y - s * 0.2f, s * 0.5f, 14f), "×" + (n.extra.Count + 1), 11f, new Color(1f, 0.6f, 0.5f));
                    GUI.color = pc;
                }
                GUI.color = oc;
                if (n.Type == NodeType.Herbalist && herb == null)
                    HerbalistMark(p + new Vector2(s * 0.26f, s * 0.27f), 10f);
                // Only nearby / selected nodes show labels. Avoid crowding the panorama.
                if ((can || i == sel) && known && n.Type != NodeType.Start)
                {
                    float tw = 90f;
                    // подписи рисуются после всадника (DrawNodeLabels), иначе он закрывает «ТРАВНИЦА», «СУНДУК»
                    nodeLabels.Add(new KeyValuePair<Rect, string>(new Rect(p.x - tw * 0.5f, p.y + s * 0.45f + 3f, tw, 18f),
                        n.IsTavern ? "ТРАКТИР" : ShortTypeNames[(int)n.Type]));
                }
                int routeIndex = previewPath.IndexOf(i);
                if (routeIndex > 0)
                {
                    Vector2 chip = p + new Vector2(s * 0.36f, -s * 0.35f);
                    bool inRange = PathCost(routeIndex) <= Campaign.Steps;
                    Color chipColor = inRange ? new Color(0.20f, 0.38f, 0.22f, 0.95f) : new Color(0.46f, 0.12f, 0.10f, 0.94f);
                    UiKit.Disc(chip, 12f, chipColor);
                    UiKit.Text(new Rect(chip.x - 12f, chip.y - 12f, 24f, 24f),
                        routeIndex.ToString(), 12f, Ink, TextAnchor.MiddleCenter);
                }
                if (!interactive || i != hitNode || panDragging) continue;
                Rect hit = new Rect(p.x - 32f, p.y - 32f, 64f, 64f);
                if (GUI.Button(hit, GUIContent.none, GUIStyle.none))
                {
                    // Selecting a marker never launches movement. The sidebar
                    // confirms exactly that selected node, no surprise detours.
                    continueRideAt = -1f;   // a new tap always takes over from an ongoing ride
                    if (devTeleport)
                    {
                        devTeleport = false; sel = -1; previewPath.Clear();
                        Campaign.DebugTeleport(i); manualPan = false; panU = -1f;
                        if (!Campaign.Nodes[i].done || Campaign.Nodes[i].IsTavern) OpenNode(i);
                        Haptics.Pulse(20);
                        continue;
                    }
                    if (i == Campaign.At && (n.Type == NodeType.Smith || n.Type == NodeType.Merchant)) OpenNode(i);
                    // повторное касание уже выбранного места — то же, что «В ПУТЬ» (с броском кости, если ходов нет)
                    else if (i == sel && moveTo < 0 && Time.unscaledTime - rollAt >= RollTime) TravelOrRoll();
                    else { SelectNode(i); Haptics.Pulse(10); }
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
            // Each traveller rides their own 16-frame sheet: Dzin map_rider_16,
            // Akiko map_rider_16_hero1, Ren map_rider_16_hero2 (Dzin as fallback).
            Texture2D riderAtlas = JourneyChoice.Hero > 0
                ? UiKit.Tex("UI/map_rider_16_hero" + JourneyChoice.Hero) : null;
            if (riderAtlas == null) riderAtlas = UiKit.Tex("UI/map_rider_16");
            // The new rider sheet must be a four-by-four texture; fall back to
            // the approved old painted rider if an incomplete atlas is imported.
            bool newSheet = riderAtlas != null && riderAtlas.width >= 512 &&
                riderAtlas.height >= 512 && riderAtlas.width == riderAtlas.height &&
                riderAtlas.width % 4 == 0;
            Texture2D ht = newSheet ? riderAtlas : UiKit.Tex("UI/map_hero");
            Texture2D ic = UiKit.Tex("UI/map_icons");
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
            // Preserve the existing painted rider until a complete 4x4 atlas
            // is imported; the 16-frame artwork is never guessed at runtime.
            int count = newSheet ? 16 : moving ? 8 : 4;
            int offset = newSheet ? 0 : moving ? 0 : 8;
            // Match hoofbeat frames to the distance actually travelled.
            // Pauses, slow roads and resumed animations no longer make the
            // horse gallop in place or skate across the painted terrain.
            float travelProgress = moving
                ? Mathf.Clamp01((Time.unscaledTime - moveStart) / Mathf.Max(0.01f, moveDuration))
                : 0f;
            // HeroUV uses cubic ease-in/out, so the gait must sample the same
            // travelled arc-length rather than an unsmoothed clock fraction.
            float easedTravel = travelProgress * travelProgress * (3f - 2f * travelProgress);
            float distanceCovered = moving
                ? TerrainRoads.Route(moveFrom, moveTo).lengthPixels * easedTravel : 0f;
            float pose = moving ? distanceCovered * (newSheet ? 12f : 9f) / 145f : 0f;
            int first = Mathf.FloorToInt(pose) % count;
            Vector2 direction = moving
                ? TerrainRoads.Route(moveFrom, moveTo).Direction(easedTravel)
                : Vector2.right;
            bool left = moving && direction.x < -0.10f;
            float size = newSheet ? 104f : 92f;
            float bob = moving ? (0.5f - 0.5f * Mathf.Cos(pose * Mathf.PI)) * 1.25f : 0f;
            Rect riderRect = new Rect(p.x - size * 0.5f, p.y - size * 0.78f - bob, size, size);
            SoftShadow(new Vector2(p.x + 4f, p.y + size * 0.17f), size * 0.62f * (1f - bob * 0.04f), size * 0.16f, 0.5f);   // тень под конём
            Rect currentUV = Cell(offset + first);
            if (left) currentUV = new Rect(currentUV.x + currentUV.width, currentUV.y,
                -currentUV.width, currentUV.height);
            GUI.DrawTextureWithTexCoords(riderRect, ht, currentUV);

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

        // A few slow, hand-placed-looking golden motes make shrines and safe
        // checkpoints feel alive. They complement the existing illustrated
        // badges and avoid particles, textures, animations or new renderers.
        void DrawAmbientLandmarkMotes()
        {
            if (Event.current.type != EventType.Repaint ||
                DuelManager.Gfx != 1 || viewW < 320f) return;
            List<MapNode> nodes = Campaign.Nodes;
            float clock = Time.unscaledTime;
            for (int i = 0; i < nodes.Count; i++)
            {
                MapNode node = nodes[i];
                if (!Known(node) || node.done) continue;
                bool sanctuary = node.Type == NodeType.Shrine ||
                                 node.Type == NodeType.Herbalist;
                bool treasure = node.Type == NodeType.Treasure;
                if (!sanctuary && !treasure) continue;
                Vector2 anchor = P(node);
                if (anchor.x < mapX + 55f || anchor.x > mapX + viewW - 55f) continue;
                Color accent = node.Type == NodeType.Herbalist
                    ? VisualIdentity.Jade : VisualIdentity.BrassLight;
                for (int k = 0; k < 3; k++)
                {
                    float phase = clock * (0.38f + k * 0.13f) + i * 4.13f + k * 2.26f;
                    float x = anchor.x + Mathf.Sin(phase * 0.9f) * (29f + k * 9f);
                    float y = anchor.y + Mathf.Sin(phase * 1.2f) * 13f -
                        13f - k * 8f;
                    if (x < mapX + 6f || x > mapX + viewW - 6f) continue;
                    float alpha = (0.16f + 0.16f * Mathf.Pow(
                        Mathf.Sin(phase * 1.6f), 2f)) * (treasure ? 0.72f : 1f);
                    UiKit.Disc(new Vector2(x, y), 2f + k * 0.45f,
                        new Color(accent.r, accent.g, accent.b, alpha));
                }
            }
        }

        // A moving light bead follows the *chosen* route, while the original
        // footsteps remain the navigation guide. Capped to one tiny highlight
        // per visible edge; avoids obscuring the intricate map art.
        void DrawRouteGlints()
        {
            if (Event.current.type != EventType.Repaint ||
                previewPath.Count < 2 || moveTo >= 0 ||
                DuelManager.Gfx != 1 || !Campaign.CurrentSettled) return;
            List<MapNode> nodes = Campaign.Nodes;
            float clock = Time.unscaledTime;
            for (int i = 1; i < previewPath.Count; i++)
            {
                if (PathCost(i) > Campaign.Steps) break;
                int from = previewPath[i - 1], to = previewPath[i];
                if (from < 0 || from >= nodes.Count || to < 0 || to >= nodes.Count) continue;
                float t = Mathf.Repeat(clock * 0.26f - i * 0.22f, 1f);
                if (t < 0.12f || t > 0.88f) continue;
                Vector2 a = new Vector2(nodes[from].x, nodes[from].y);
                Vector2 b = new Vector2(nodes[to].x, nodes[to].y);
                Vector2 uv = TerrainRoads.Route(from, to).Position(t);
                Vector2 point = new Vector2(mapX + (uv.x - u0) * MapFullW,
                    uv.y * MapH);
                if (point.x < mapX + 9f || point.x > mapX + viewW - 9f) continue;
                UiKit.Disc(point, 5f, new Color(0.82f, 1f, 0.65f, 0.14f));
                UiKit.Disc(point, 2.0f, new Color(0.92f, 1f, 0.77f, 0.50f));
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
            // под названием области (внизу закрывала подписи узлов — «СУНДУК» у нижнего края)
            Rect bar = new Rect(mapX + 14f, Profile.Cycle > 0 ? 62f : 46f, w, h);
            VisualIdentity.RowPanel(bar, 0.95f);
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

        // Bottom of the panel's text area: the fixed В ПУТЬ button starts at 540 − 166.
        const float PanelTextBottom = 540f - 166f - 6f;

        void DrawPanel(Rect r, bool interactive)
        {
            VisualIdentity.KitSidePanel(r);
            float x = r.x + 16f, w = r.width - 40f, y = 12f;   // справа кромка панели у края экрана — запас
            VisualIdentity.Heading(new Rect(x, y, w, 36f), "ПОХОД");
            // Quiet progress caption in the existing heading's unused right side;
            // no panel resize, no new button and no changes to the original art.
            if (w >= 210f)
            {
                int visited = 0;
                List<MapNode> regionNodes = Campaign.Nodes;
                for (int i = 0; i < regionNodes.Count; i++)
                    if (regionNodes[i].done) visited++;
                string journeyStatus = routeAutopilot && moveTo >= 0
                    ? "В ПУТИ" : visited + " / " + regionNodes.Count + " УЗЛОВ";
                UiKit.Text(new Rect(x + w - 118f, y + 5f, 117f, 24f),
                    journeyStatus, 10.5f, VisualIdentity.Subdued,
                    TextAnchor.MiddleRight, false);
            }
            y += 38f;
            UiKit.Text(new Rect(x, y, w, 18f), "Уровень " + Profile.Level + (Profile.Points > 0 ? "   · очков: " + Profile.Points : ""), 13f, Ink, TextAnchor.MiddleLeft, false); y += 20f;
            Texture2D coins = UiKit.Tex("UI/coins");
            if (coins != null) GUI.DrawTexture(new Rect(x - 2f, y - 1f, 20f, 20f), coins);
            UiKit.Text(new Rect(x + (coins != null ? 22f : 0f), y, w, 18f), Profile.Gold + " мон", 13f, Gold, TextAnchor.MiddleLeft, false); y += 24f;
            string bl = Campaign.BlessingText;
            if (SideDuel.Enabled && Profile.Ally >= 0)   // соратник: сила найма, ранен — к травнице
                bl = (Profile.AllyDown ? "Соратник ранен — его выходит травница" : "Соратник: " + Profile.AllyTierNames[SideDuel.Tier].ToLower()) + (bl.Length > 0 ? "\n" + bl : "");
            if (bl.Length > 0)
            {
                // благословения, проклятие, кот и заказ — медальоном (UI/buff_icons 3×2) и строкой рядом
                Texture2D bi = UiKit.Tex("UI/buff_icons");
                foreach (string entry in bl.Split('\n'))
                {
                    int icon = entry.StartsWith("Благословение клинка") ? 0 : entry.StartsWith("Благословение духа") ? 1 : entry.StartsWith("Благословение жизни") ? 2
                        : entry.StartsWith("Проклятие") ? 3 : entry.StartsWith("Кошачья") || entry.StartsWith("Девять") ? 4 : entry.StartsWith("Заказ") ? 5 : -1;
                    float tx = x + (bi != null && icon >= 0 ? 24f : 0f);
                    Color col = icon == 3 || entry.StartsWith("Соратник ранен") ? new Color(1f, 0.5f, 0.45f) : icon == 5 ? Gold : new Color(0.6f, 0.95f, 0.7f);
                    float y0 = y;
                    foreach (string line in Wrap(entry, 31)) { UiKit.Text(new Rect(tx, y, w - (tx - x), 16f), line, 11.5f, col, TextAnchor.MiddleLeft, false); y += 16f; }
                    if (bi != null && icon >= 0)
                        GUI.DrawTextureWithTexCoords(new Rect(x, y0 - 1f, 20f, 20f), bi, new Rect((icon % 3) / 3f, icon < 3 ? 0.5f : 0f, 1f / 3f, 0.5f));
                    y = Mathf.Max(y, y0 + 22f);
                }
            }
            y += 6f;
            // кости пути: запас, выпавшее, бросок; без костей — проводник за моны или бой с ронином
            bool rolling = Time.unscaledTime - rollAt < RollTime;
            int face = rolling ? 1 + (int)(Time.unscaledTime * 14f) % 3 : Campaign.Steps > 0 ? Campaign.Steps : Mathf.Max(1, Campaign.LastRoll);
            Die(new Rect(x, y, 50f, 50f), face,
                rolling ? (Time.unscaledTime * 900f) % 360f : 0f,
                Campaign.Steps > 0 || rolling);
            UiKit.Text(new Rect(x + 61f, y, w - 61f, 22f),
                "КОСТИ ПУТИ: " + Campaign.Rolls, 14f, Ink, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(x + 61f, y + 22f, w - 61f, 20f),
                rolling ? "бросок…" : Campaign.Steps > 0
                    ? "ходов: " + Campaign.Steps : Campaign.Stuck ? "костей нет — кость за победу" : "ход окончен",
                12f, Campaign.Steps > 0 ? VisualIdentity.Jade : Dim,
                TextAnchor.MiddleLeft, false);
            // Three-step travel gauge below the die: coloured segments correspond
            // to remaining movement points, matching the trail's range.
            for (int k = 0; k < 3; k++)
            {
                bool available = k < Campaign.Steps;
                Color segment = available ? new Color(0.70f, 0.92f, 0.58f, 0.92f)
                    : new Color(0.35f, 0.28f, 0.20f, 0.72f);
                UiKit.Rect(new Rect(x + 61f + k * 43f, y + 44f, 36f, 4f), segment);
            }
            y += 57f;
            bool canRoll = Campaign.Rolls > 0 && Campaign.Steps <= 0 && Campaign.CurrentSettled && !Campaign.InFight && !Campaign.Training && !rolling;
            if (canRoll && Btn(new Rect(x, y, w, 42f), "БРОСИТЬ КОСТЬ", interactive, true)) { Campaign.RollDie(); rollAt = Time.unscaledTime; Haptics.Pulse(30); if (Juice.Instance != null) Juice.Instance.Taiko(0.7f, 1.3f); }
            if (canRoll) y += 48f;
            if (Campaign.Stuck && !rolling)
            {
                int gc = Campaign.GuideCost;
                if (Btn(new Rect(x, y, w, 40f), "ПРОВОДНИК (−" + gc + " МОН)", interactive && Profile.Gold >= gc)) { if (Campaign.BuyGuide()) TavToast("Проводник указал дорогу: +1 кость пути за " + gc + " мон. Брось кость."); }   // раньше молча: кнопки просто исчезали
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
                // мало места (кнопки проводника и ронина) — значок меньше, без описания: заголовок и маршрут не лезут друг на друга
                bool compact = PanelTextBottom - y < 120f;
                float ics = compact ? 36f : 64f;
                if (!known)
                {
                    Texture2D unk = UiKit.Tex("UI/map_unknown");
                    if (unk != null) GUI.DrawTexture(new Rect(x, y, ics, ics), unk);
                    else
                    {
                        UiKit.Disc(new Vector2(x + ics * 0.5f, y + ics * 0.5f), ics * 0.44f, new Color(0.10f, 0.08f, 0.13f, 0.9f));
                        UiKit.Text(new Rect(x, y, ics, ics), "?", 34f, new Color(0.9f, 0.86f, 1f, 0.95f), TextAnchor.MiddleCenter);
                    }
                }
                else if (ic != null)
                {
                    Color previousTint = GUI.color;
                    GUI.color = MapBadgeTint(n, known, show);
                    Texture2D herb = n.Type == NodeType.Herbalist ? UiKit.Tex(HerbalistIcon) : null;
                    if (herb != null) GUI.DrawTexture(new Rect(x, y, ics, ics), herb);
                    else if (n.IsTavern && UiKit.Tex("UI/map_tavern") != null) GUI.DrawTexture(new Rect(x, y, ics, ics), UiKit.Tex("UI/map_tavern"));
                    else GUI.DrawTextureWithTexCoords(new Rect(x, y, ics, ics), ic,
                        Cell(MapBadgeCell(n, show)));
                    GUI.color = previousTint;
                    if (n.Type == NodeType.Herbalist && herb == null)
                        HerbalistMark(new Vector2(x + ics * 0.83f, y + ics * 0.83f), ics * 0.17f);
                }
                UiKit.Text(new Rect(x + ics + 8f, y + (compact ? 0f : 6f), w - ics - 8f, compact ? 20f : 24f), known ? NodeName(n) : "НЕИЗВЕСТНО", compact ? 14f : 16f, Ink, TextAnchor.MiddleLeft);
                if (n.done && show != Campaign.At && n.Type != NodeType.Start)
                {
                    // Visited place: say what a return trip offers.
                    bool reusable = n.Type == NodeType.Smith || n.Type == NodeType.Merchant || n.Type == NodeType.Herbalist;
                    UiKit.Text(new Rect(x + ics + 8f, y + (compact ? 20f : 32f), w - ics - 8f, compact ? 16f : 20f),
                        reusable ? "ПРОЙДЕНО · можно вернуться" : "ПРОЙДЕНО · уже пусто", 12f,
                        reusable ? VisualIdentity.Jade : Dim, TextAnchor.MiddleLeft, false);
                }
                else if (known && n.IsFight) UiKit.Text(new Rect(x + ics + 8f, y + (compact ? 20f : 32f), w - ics - 8f, compact ? 16f : 20f), Ladder.Stages[Mathf.Clamp(n.enemy, 0, Ladder.Count - 1)].name, 12f, new Color(1f, 0.6f, 0.45f), TextAnchor.MiddleLeft, false);
                y += compact ? ics + 6f : 72f;
                string hint = compact ? "" : known ? NodeHint(n) : "Туман скрывает, что ждёт впереди. Подойди ближе.";
                // Text never runs under the fixed В ПУТЬ button: the route line has a reserved
                // slot just above it and the description is cut to the space that is left.
                const float routeSlot = PanelTextBottom - 40f;
                foreach (string line in Wrap(hint, 34))
                {
                    if (y + 17f > routeSlot - 4f) break;
                    UiKit.Text(new Rect(x, y, w, 17f), line, 12f, Dim, TextAnchor.MiddleLeft, false); y += 17f;
                }
                // назад вверх не прыгаем: при нехватке места строка маршрута шла поверх заголовка
                y = compact ? y : Mathf.Min(y + 10f, Mathf.Max(y, routeSlot));
                int nextStep = NextPreviewStep;
                if (nextStep < 0 && show != Campaign.At && previewPath.Count <= 1)
                    UiKit.Text(new Rect(x, y, w, 18f), "Отсюда дороги туда нет", 12f, new Color(1f, 0.61f, 0.48f), TextAnchor.MiddleLeft, false);
                if (nextStep >= 0)
                {
                    int totalMoves = PathCost(previewPath.Count - 1);
                    int available = Mathf.Min(Campaign.Steps, totalMoves);
                    bool enough = totalMoves <= Campaign.Steps;
                    Color routeColor = enough ? new Color(0.65f, 0.94f, 0.65f) : new Color(1f, 0.61f, 0.48f);
                    UiKit.Text(new Rect(x, y, w, 18f),
                        totalMoves == 0 ? "МАРШРУТ по своим следам — бесплатно" : "МАРШРУТ: " + Steps(totalMoves) + "   ·   доступно: " + available,
                        12f, routeColor, TextAnchor.MiddleLeft, false);
                    y += 20f;
                    // места-услуги по пути не останавливают всадника — предупреждаем, чтобы это не было сюрпризом
                    string through = "";
                    for (int k = 1; k + 1 < previewPath.Count; k++)
                    {
                        MapNode pn = Campaign.Nodes[previewPath[k]];
                        if (!pn.done && Campaign.IsService(pn)) through += (through.Length > 0 ? ", " : "") + NodeName(pn).ToLower();
                    }
                    if (through.Length > 0 && !compact && y + 18f <= PanelTextBottom)
                    {
                        UiKit.Text(new Rect(x, y, w, 18f), "По пути: " + through + " — проедешь мимо", 11f, Dim, TextAnchor.MiddleLeft, false);
                        y += 18f;
                    }
                    if (compact || y + 18f > PanelTextBottom) { }   // подсказки под маршрутом нет места
                    else
                    // Travel remains one node at a time, so fight/event encounters
                    // are never skipped by the route preview.
                    // The same stable button remains present while the die is empty.
                    // Never make the player reselect a marker after rolling.
                    // Movement control is pinned below the illustration; dynamic
                    // descriptions must never push it below the sidebar viewport.
                    if (!Campaign.CanGo(show))
                        UiKit.Text(new Rect(x, y, w, 18f),
                            Campaign.Steps == 0 ? "Сначала брось кость пути"
                                : enough ? "«В ПУТЬ» — доедешь до цели" : "«В ПУТЬ» — пройдёшь " + Steps(available) + " к цели",
                            11f, Dim, TextAnchor.MiddleLeft, false);
                }
                else if (show != Campaign.At && Campaign.Nodes[Campaign.At].next.Contains(show))
                {
                    // Direct destinations use the same fixed bottom travel button.
                }
            }
            else
            {
                string tip = Campaign.InFight ? "Бой ещё не окончен." : !Campaign.CurrentSettled ? "Ты на месте." : Campaign.Steps > 0 ? "Выбери цель. Светлые следы — доступные шаги, красные — сверх броска." : "Брось кость пути, чтобы идти дальше. Назад по своим следам — без кости.";
                foreach (string line in Wrap(tip, Mathf.Max(34, Mathf.FloorToInt(w / (12f * 0.53f)))))   // перенос по ширине панели, без пустоты справа
                    { if (y + 17f > PanelTextBottom) break; UiKit.Text(new Rect(x, y, w, 17f), line, 12f, Dim, TextAnchor.MiddleLeft, false); y += 17f; }
            }
            // The empty lower half of the old sidebar now shows a contextual
            // illustrated location preview. Never reveal a fogged node's event art.
            MapNode artNode = show >= 0 ? Campaign.Nodes[show] : Campaign.Current;
            bool revealArt = show < 0 || Known(artNode);
            string artwork = revealArt ? NodePreviewArt(artNode) : "UI/path_panorama";
            float artTop = Mathf.Max(273f, y + 11f);
            float artSpace = 365f - artTop;
            // A 45-pixel letterbox was cutting an enemy in half above В ПУТЬ.
            // Show an intact preview or no preview; never a thin cropped strip.
            float artHeight = artSpace >= 94f ? Mathf.Min(110f, artSpace) : 0f;
            if (artHeight >= 94f)
            {
                Rect illustration = new Rect(x, artTop, w, artHeight);
                DrawSidebarIllustration(illustration, artwork);
                Rect portrait = new Rect(illustration.x + 7f,
                    illustration.y + 7f, 47f, 58f);
                string heroArt = JourneyChoice.FaceSquare(JourneyChoice.Hero);
                if (UiKit.Tex(heroArt) == null) heroArt = JourneyChoice.Portrait(JourneyChoice.Hero);
                if (UiKit.Tex(heroArt) != null)
                    VisualIdentity.Portrait(portrait, heroArt);
                // Keep portrait colors untouched; accent belongs to the frame.
                VisualIdentity.Inset(portrait, JourneyChoice.Accent(JourneyChoice.Hero));
            }
            // Keep the travel action visible at all times, independently of
            // node descriptions, die availability and illustration height.
            // Never substitute the first BFS waypoint for the selected target.
            // A distant node remains a PREVIEW until the user chooses an adjacent
            // medal, preventing accidental trips to a different location.
            // A distant selected node travels one hop along the highlighted route
            // (NextPreviewStep); the goal stays selected for the next tap.
            int travelTarget = sel >= 0 && Campaign.CanGo(sel) ? sel
                : sel >= 0 && Campaign.CanGo(NextPreviewStep) ? NextPreviewStep : -1;
            bool travelReady = !rolling && moveTo < 0 && travelTarget >= 0 &&
                Campaign.CanGo(travelTarget);
            // бой-реванш: герой стоит на пройденном бою — можно сразиться снова (монеты вполовину, без добычи)
            if (sel < 0 && moveTo < 0 && !rolling && Campaign.CanRematch &&
                Btn(new Rect(x, 540f - 218f, w, 44f), "БОЙ-РЕВАНШ", interactive, true))
            {
                Campaign.BeginRematch();
                StartFight(Campaign.At, true);
            }
            if (Btn(new Rect(x, 540f - 166f, w, 44f), "В ПУТЬ",
                interactive && travelReady, true))
                TravelOrRoll();
            if (Btn(new Rect(x, 540f - 112f, w, 44f), "СНАРЯЖЕНИЕ" + (Profile.Points > 0 ? " (+" + Profile.Points + ")" : ""), interactive)) { Profile.CostMul = 1f; InventoryScreen.Show(0); }
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
                case NodeType.Event: return "UI/event_" + Mathf.Clamp(node.ev, 0, EventCount - 1);
                case NodeType.Shrine: return "UI/npc_2";
                case NodeType.Smith: return "UI/npc_1";
                case NodeType.Merchant: return "UI/npc_0";
                case NodeType.Rest: return node.IsTavern && UiKit.Tex("UI/tavern") != null ? "UI/tavern" : "UI/npc_3";
                case NodeType.Herbalist: return "UI/npc_4";   // своя травница (до 2026-11-02 был мастер с привала)
                case NodeType.Treasure: return "UI/chest_open";
                default: return "UI/path_panorama";
            }
        }

        static void DrawSidebarIllustration(Rect r, string path)
        {
            // Never leave a blank/flat placeholder in the adventure panel.
            string art = UiKit.Tex(path) != null ? path : "UI/path_panorama";
            VisualIdentity.Portrait(r, art);
            // Embossed gold base to match the right-hand panel and give the
            // panorama the same treatment as encounter illustration cards.
            UiKit.Rect(new Rect(r.x + 8f, r.yMax - 9f, r.width - 16f, 2f),
                new Color(VisualIdentity.Brass.r, VisualIdentity.Brass.g,
                    VisualIdentity.Brass.b, 0.50f));
        }

        static bool Btn(Rect r, string label, bool enabled = true, bool red = false)
        {
            return VisualIdentity.KitButton(r, label, enabled, red);
        }

        /// <summary>Кость пути: костяной кубик с точками (1–3), во время броска крутится.</summary>
        static void Die(Rect r, int face, float spin, bool lit)
        {
            // Reviewed isometric artwork: distinct 1/2/3-pip faces, bevelled
            // ivory, warm bronze sides and a transparent silhouette.
            // The old flat procedural square is only a safety fallback;
            // Android CI verifies the illustrated PNG is included.
            Texture2D dice = UiKit.Tex("UI/map_dice_3d");
            if (dice == null) dice = JourneyDiceArt.Atlas;
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
                new Color(0.015f, 0.012f, 0.012f, 0.82f * a));   // карта и боковая панель под окном уходят в тень, их кнопки не путаются с кнопками окна
            Rect r = new Rect((W - w) * 0.5f, (540f - h) * 0.5f + (1f - a) * 20f, w, h);
            VisualIdentity.KitSurface(r, a);
            return r;
        }

        void Picture(Rect r, string tex)
        {
            r = new Rect(r.x + 14f, r.y + 14f, r.width - 14f, r.height - 28f);   // картинка внутри рамки окна, а не поверх её золота
            VisualIdentity.Portrait(r, UiKit.Tex(tex) != null ? tex : "UI/path_panorama");
        }

        /// <summary>Высота текста так, как его разложит Lines: по ней окна подстраивают свою высоту под содержимое.</summary>
        static float LinesH(float w, string s, int max, float size)
        {
            max = Mathf.Max(max, Mathf.FloorToInt(w / (size * 0.53f)));
            return Wrap(s, max).Count * (size + 6f);
        }

        float Lines(float x, float y, float w, string s, int max, float size, Color c)
        {
            // перенос по реальной ширине окна (≈0,5 кегля на букву), а не по заниженному числу символов:
            // раньше текст рвался на середине окна, правая половина пустовала
            max = Mathf.Max(max, Mathf.FloorToInt(w / (size * 0.53f)));
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
                    Rect r = Card(640f, 350f);
                    int e = Mathf.Clamp(n.enemy, 0, Ladder.Count - 1);
                    Texture2D art = UiKit.Tex(EnemyFullArt[e]);
                    if (art != null)
                    {
                        float ah = r.height - 20f, aw = ah * art.width / Mathf.Max(1f, art.height);
                        Rect ar = new Rect(r.x + 10f, r.y + 10f, Mathf.Min(aw, 260f), ah);
                        // Keep the enemy artwork bright. The Portrait routine draws its
                        // inset only *behind* the picture; never place another inset over it.
                        VisualIdentity.Portrait(ar, EnemyFullArt[e]);
                        // Reuse the existing gilded panel-frame asset so the portrait
                        // matches the card and the rest of the UI, rather than adding
                        // a plain, uneven black border or generating a new texture.
                        Texture2D portraitFrame = UiKit.Tex("UI/Kit/ui_panel_frame");
                        if (portraitFrame != null)
                        {
                            Rect edge = new Rect(ar.x - 5f, ar.y - 5f, ar.width + 10f, ar.height + 10f);
                            UiKit.Slice(edge, portraitFrame, 0.27f, 0.2f, 0.1f, 0.3f, 0.12f, Color.white);
                        }
                    }
                    float x = r.x + 258f, w = r.width - 278f, y = r.y + 22f;   // портрет теперь 2:3 и уже — текст ближе к нему
                    UiKit.Text(new Rect(x, y, w, 18f), Campaign.TypeNames[n.type], 13f, n.Type == NodeType.Boss ? new Color(1f, 0.4f, 0.3f) : Dim, TextAnchor.MiddleLeft); y += 22f;
                    // имя противника — крупно: сразу видно, с кем бой
                    UiKit.Text(new Rect(x, y, w, 30f), Ladder.Stages[e].name.ToUpper(), 24f, n.Type == NodeType.Boss ? new Color(1f, 0.4f, 0.3f) : Gold, TextAnchor.MiddleLeft); y += 34f;
                    UiKit.Text(new Rect(x, y, w, 20f), Ladder.Stages[e].title, 13f, new Color(0.9f, 0.85f, 0.75f), TextAnchor.MiddleLeft, false); y += 28f;
                    VisualIdentity.Divider(new Rect(x, y - 6f, w, 1f)); y += 6f;
                    int sky = MapClouds.SkyAt(Campaign.Region, new Vector2(n.x, n.y));
                    Texture2D wi = UiKit.Tex("UI/weather_icons");   // 3 медальона в ряд: ясно, дождь, гроза
                    if (wi != null)
                    {
                        GUI.DrawTextureWithTexCoords(new Rect(x, y - 6f, 30f, 30f), wi, new Rect(sky / 3f, 0f, 1f / 3f, 1f));
                        x += 36f; w -= 36f;
                    }
                    UiKit.Text(new Rect(x, y, w, 18f), "Погода: " + MapClouds.SkyName(sky) + (sky == 2 ? " — бой под молниями" : sky == 1 ? " — бой под дождём" : ""),
                        12f, sky == 2 ? new Color(0.75f, 0.7f, 1f) : sky == 1 ? new Color(0.65f, 0.8f, 1f) : Dim, TextAnchor.MiddleLeft, false); y += 26f;
                    if (wi != null) { x -= 36f; w += 36f; }
                    string mods = "";
                    if (n.Type == NodeType.Elite) mods += "Сильнее обычного: +35% здоровья, +15% урона, лишняя жизнь.\nДобыча не ниже редкой, мон больше.\n";
                    if (n.Type == NodeType.Ambush) mods += "Засада: бой ночью, враг начинает с полным духом.\nМон в полтора раза больше.\n";
                    if (n.extra != null && n.extra.Count > 0) mods += Campaign.SquadText(n) + "\n";
                    if (n.Type == NodeType.Boss) mods += "Владыка области. Победа откроет путь дальше" + (Campaign.Region >= 2 ? " — и новый круг." : ".") + "\n";
                    if (Campaign.BlessingText.Length > 0) mods += Campaign.BlessingText + "\n";
                    if (mods.Length == 0) mods = "Честный поединок.";
                    y = Lines(x, y, w, mods.TrimEnd('\n'), 40, 12.5f, new Color(0.9f, 0.85f, 0.75f));
                    // повадки и сила врага — те же, что на вкладке «Путь» в снаряжении (и пустоты над кнопками нет)
                    if (y < r.yMax - 170f)
                    {
                        Ladder.Stage st = Ladder.Stages[e];
                        y += 8f;
                        UiKit.Text(new Rect(x, y, w, 18f), "Повадки: " + EnemyTraits[Mathf.Clamp(e, 0, EnemyTraits.Length - 1)], 12.5f,
                            new Color(1f, 0.65f, 0.45f), TextAnchor.MiddleLeft, false); y += 22f;
                        UiKit.Text(new Rect(x, y, w, 18f), "Жизней: " + Mathf.Max(1, st.marks) + "   ·   здоровье ×" + (st.hpMul * Profile.CycleHp).ToString("0.0")
                            + "   ·   урон ×" + (st.dmgMul * Profile.CycleDmg).ToString("0.0"), 12f, Dim, TextAnchor.MiddleLeft, false); y += 22f;
                    }
                    // Both actions need the full panel width. The old 38%-wide
                    // equipment button clipped its label and spilled outside the modal.
                    if (Btn(new Rect(x, r.yMax - 116f, w, 44f), "В БОЙ", ok, true))
                        StartFight(at);
                    if (Btn(new Rect(x, r.yMax - 64f, w, 44f), "СНАРЯЖЕНИЕ", ok))
                        { Profile.CostMul = 1f; InventoryScreen.Show(0); }
                    break;
                }
                case Modal.Shrine:
                {
                    Rect r = Card(660f, 380f);
                    Picture(new Rect(r.x, r.y, 260f, r.height), "UI/npc_2");
                    float x = r.x + 280f, w = r.width - 300f, y = r.y + 22f;
                    VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "ХРАМ", 20f, TextAnchor.MiddleLeft); y += 34f;
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
                    VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "КУЗНЕЦ", 20f, TextAnchor.MiddleLeft); y += 34f;
                    y = Lines(x, y, w, "«Покажи свою сталь. Для того, кто идёт к владыке, — полцены.»\n\nУлучшить снаряжение и вставить руну можно только у кузнеца — здесь за полцены. Вернуться к нему можно, коснувшись его на карте.", 40, 13f, Ink);
                    if (Btn(new Rect(x, r.yMax - 118f, w, 46f), "К НАКОВАЛЬНЕ", ok, true)) { Profile.CostMul = 0.5f; InventoryScreen.Show(0); }
                    if (Btn(new Rect(x, r.yMax - 64f, w, 46f), "УЙТИ", ok)) modal = Modal.None;
                    break;
                }
                case Modal.Merchant:
                {
                    Rect r = Card(720f, 400f);
                    Picture(new Rect(r.x, r.y, 230f, r.height), "UI/npc_0");
                    float x = r.x + 248f, w = r.width - 266f, y = r.y + 18f;
                    VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "ТОРГОВЕЦ", 20f, TextAnchor.MiddleLeft);
                    UiKit.Text(new Rect(x, y, w, 26f), Profile.Gold + " мон", 15f, Gold, TextAnchor.MiddleRight); y += 32f;
                    y = Lines(x, y, w, "«Всё с дальних дорог, господин. Дёшево не отдам — но и не обману.»", 46, 12.5f, Dim) + 6f;
                    if (stock != null)
                        for (int k = 0; k < stock.Count; k++)
                        {
                            Item it = stock[k];
                            Rect row = new Rect(x, y + k * 78f, w, 72f);
                            VisualIdentity.RowPanel(row);
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
                    if (Btn(new Rect(x, r.yMax - 56f, 200f, 42f), "ПРОДАТЬ СВОЁ", ok, true)) { Profile.CostMul = 1f; InventoryScreen.Show(0); InventoryScreen.AtMarket = true; }   // продажа — только у торговца
                    if (Btn(new Rect(r.xMax - 150f, r.yMax - 56f, 132f, 42f), "УЙТИ", ok)) modal = Modal.None;
                    break;
                }
                case Modal.Treasure:
                {
                    Rect r = Card(560f, 340f);
                    float x = r.x + 24f, y = r.y + 20f, w = r.width - 48f;
                    VisualIdentity.TitlePlaque(new Rect(x, y, w, 28f), "ТАЙНИК", 22f); y += 36f;
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
                    VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "ПРИВАЛ", 20f, TextAnchor.MiddleLeft); y += 34f;
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
                    Picture(new Rect(r.x, r.y, 250f, r.height), "UI/npc_4");   // хижина травницы и пробуждение после поражения
                    float x = r.x + 270f, w = r.width - 290f, y = r.y + 22f;
                    VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "ХИЖИНА ТРАВНИЦЫ", 20f, TextAnchor.MiddleLeft); y += 34f;
                    string tx = modal == Modal.Knocked
                        ? "Ты очнулся на циновке. Пахнет полынью и дымом. «Тебя принесли с дороги еле живого. Отлежался — иди. Вот, возьми на удачу». Травница даёт кость пути."
                        : revisit
                            ? "Травница поднимает глаза от отвара. «Снова ты? Посиди у огня, переведи дух». Кость пути она уже отдала тебе в прошлый раз."
                            : "Старая травница помешивает отвар. «Ходят тут всякие с мечами… Если что — несите сюда». Она вкладывает тебе в ладонь кость пути.";
                    y = Lines(x, y, w, tx, 40, 13f, Ink) + 8f;
                    if (SideDuel.Enabled && Profile.Ally >= 0 && Profile.AllyDown && Btn(new Rect(x, y, w, 42f), "ВЫХОДИТЬ СОРАТНИКА", ok))
                    {
                        Profile.SetAllyDown(false);
                        TavToast("Травница промывает раны соратника отваром полыни. К утру он снова на ногах и берётся за меч.");
                    }
                    if (Btn(new Rect(x, r.yMax - 64f, w, 46f), "В ПУТЬ", ok, true)) { if (modal == Modal.Knocked) Campaign.AckKnocked(); modal = Modal.None; }
                    break;
                }
                case Modal.Camp: DrawCamp(ok); break;
                case Modal.Tavern: DrawTavern(ok); break;
                case Modal.Dev: DrawDev(ok); break;
                case Modal.RegionClear:
                {
                    int prev = Mathf.Clamp(Campaign.ClearedRegion, 0, 2);
                    Texture2D victory = UiKit.Tex("UI/region_clear_" + prev);
                    if (victory != null)
                    {
                        // Full-screen painting of the freed region; text on a dark band below.
                        float a = Mathf.Clamp01((Time.unscaledTime - modalAt) / 0.6f);
                        Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
                        GUI.DrawTexture(new Rect(0f, 0f, W, 540f), victory, ScaleMode.ScaleAndCrop);
                        GUI.color = oc;
                        UiKit.Rect(new Rect(0f, 300f, W, 240f), new Color(0.01f, 0.008f, 0.008f, 0.62f * a));
                        UiKit.Text(new Rect(0f, 306f, W, 24f), "ОБЛАСТЬ ПРОЙДЕНА", 16f, new Color(Gold.r, Gold.g, Gold.b, a));
                        // название — на ленте с маской они (UI/Kit): маска над лентой, середина свободна под надпись
                        Texture2D oni = UiKit.Tex("UI/Kit/hud_banner_oni");
                        if (oni != null)
                        {
                            float bw = Mathf.Min(W - 40f, 560f), bh = bw * oni.height / oni.width;
                            Color oc2 = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
                            GUI.DrawTexture(new Rect(W * 0.5f - bw * 0.5f, 400f - bh * 0.575f, bw, bh), oni);
                            GUI.color = oc2;
                        }
                        UiKit.Text(new Rect(0f, 379f, W, 42f), Campaign.RegionNames[prev], 30f, new Color(Ink.r, Ink.g, Ink.b, a));
                        UiKit.Text(new Rect(0f, 458f, W, 22f), "Впереди — " + Campaign.RegionNames[Campaign.Region] + (Campaign.Region == 0 ? " (новый круг: враги крепче, добыча богаче)" : ""), 13f, new Color(Dim.r, Dim.g, Dim.b, a));
                        if (Btn(new Rect(W * 0.5f - 100f, 486f, 200f, 44f), "В ЛАГЕРЬ", ok && a >= 1f, true)) { Campaign.AckCleared(); OpenCamp(); }
                        break;
                    }
                    Rect r = Card(600f, 260f);
                    UiKit.Text(new Rect(r.x, r.y + 26f, r.width, 24f), "ОБЛАСТЬ ПРОЙДЕНА", 16f, Gold);
                    UiKit.Text(new Rect(r.x, r.y + 56f, r.width, 40f), Campaign.RegionNames[prev], 30f, Ink);
                    UiKit.Text(new Rect(r.x, r.y + 110f, r.width, 22f), "Впереди — " + Campaign.RegionNames[Campaign.Region] + (Campaign.Region == 0 ? " (новый круг: враги крепче, добыча богаче)" : ""), 13f, Dim);
                    if (Btn(new Rect(r.center.x - 100f, r.yMax - 66f, 200f, 48f), "В ЛАГЕРЬ", ok, true)) { Campaign.AckCleared(); OpenCamp(); }
                    break;
                }
            }
        }

        // ---------------- лагерь между областями (UI/camp: картина 1915×821 с надписями, зоны нажатия) ----------------
        // Кузница — снаряжение за полцены и руны; алтарь — благословение на бой; тренировка — +1 очко;
        // доска заданий — одна награда на выбор. Каждая услуга — один раз за лагерь.
        const float CampW = 1915f, CampH = 821f;
        static readonly Rect CampForge = new Rect(60f, 40f, 640f, 360f), CampAltar = new Rect(40f, 400f, 560f, 380f),
            CampTrain = new Rect(1180f, 390f, 700f, 390f), CampGo = new Rect(712f, 686f, 488f, 89f);
        int campSub;          // 0 — лагерь, 1 — алтарь, 2 — доска заданий
        bool campAltar, campTrain, campBoard;
        void OpenCamp()
        {
            campSub = 0; campAltar = campTrain = campBoard = false;
            if (UiKit.Tex("UI/camp") == null) { modal = Modal.None; return; }
            OpenModal(Modal.Camp);
        }
        // одна и та же кнопка выхода в лагере («В ПУТЬ») и в трактире («В ЛАГЕРЬ»)
        Rect ExitButtonRect() { return new Rect(W * 0.5f - 110f, 540f - 58f, 220f, 44f); }
        Rect CampRect(Rect z, float s, float ox, float oy) { return new Rect(ox + z.x * s, oy + z.y * s, z.width * s, z.height * s); }
        // кнопка у объекта на картинке: на узком экране (16:9) картинка обрезана по бокам — кнопку не выпускаем за край
        Rect OnScreen(Rect r) { r.x = Mathf.Clamp(r.x, 8f, Mathf.Max(8f, W - r.width - 8f)); return r; }
        void DrawCamp(bool ok)
        {
            Texture2D art = UiKit.Tex("UI/camp");
            float s = Mathf.Max(W / CampW, 540f / CampH), ox = (W - CampW * s) * 0.5f, oy = (540f - CampH * s) * 0.5f;
            // статичная картинка — всегда как подложка; видео поверх когда готово (нет чёрных квадратов при загрузке)
            if (art != null) GUI.DrawTexture(new Rect(ox, oy, CampW * s, CampH * s), art, ScaleMode.StretchToFill, false);
            CampVideo.Preload();
            Texture vid = CampVideo.Frame();
            if (vid != null) GUI.DrawTexture(new Rect(ox, oy, CampW * s, CampH * s), vid, ScaleMode.StretchToFill, false);
            if (campSub == 1)
            {
                Rect r = Card(800f, 340f);
                Picture(new Rect(r.x, r.y, 250f, r.height), "UI/npc_altar");   // иллюстрация слева, как у торговца и событий
                float x = r.x + 268f, w = r.width - 292f, y = r.y + 22f;
                VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "АЛТАРЬ", 20f); y += 44f;
                string[] names = { "КЛИНОК: +20% УРОНА", "ДУХ: ПОЛНЫЙ ДУХ В НАЧАЛЕ", "ЖИЗНЬ: +25% ЗДОРОВЬЯ" };
                for (int k = 0; k < 3; k++)
                {
                    if (!Btn(new Rect(x, y + k * 54f, w, 46f), names[k], ok, k == 0)) continue;
                    Campaign.SetBlessing(k + 1);
                    campAltar = true;
                    campSub = 0;
                    string[] blessingNotices =
                    {
                        "Благословение клинка: +20% урона в следующем бою.",
                        "Благословение духа: полный дух в начале следующего боя.",
                        "Благословение жизни: +25% здоровья в следующем бою."
                    };
                    TavToast(blessingNotices[k]); // Same centered ornate notification as training.
                    return; // Avoid processing other altar hitboxes in this OnGUI event.
                }
                if (Btn(new Rect(x + w * 0.5f - 80f, r.yMax - 54f, 160f, 40f), "НАЗАД", ok)) campSub = 0;
                return;
            }
            if (campSub == 2)
            {
                Rect r = Card(800f, 340f);
                Picture(new Rect(r.x, r.y, 250f, r.height), "UI/npc_questboard");   // иллюстрация слева, как у торговца и событий
                float x = r.x + 268f, w = r.width - 292f, y = r.y + 22f;
                VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "ДОСКА ЗАДАНИЙ", 20f); y += 44f;
                int gold = 60 + 30 * Campaign.Region + 40 * Profile.Cycle;
                if (Btn(new Rect(x, y, w, 46f), "ПРОВОДИТЬ КАРАВАН: +1 КОСТЬ ПУТИ", ok, true)) { Campaign.AddRoll(); campBoard = true; campSub = 0; TavToast("Задание выполнено: +1 кость пути."); return; }
                if (Btn(new Rect(x, y + 54f, w, 46f), "НАЙТИ БЕГЛЕЦА: +" + gold + " МОН", ok)) { Profile.AddGold(gold); campBoard = true; campSub = 0; TavToast("Задание выполнено: +" + gold + " мон."); return; }
                if (Btn(new Rect(x, y + 108f, w, 46f), "ОХОТА НА ОБОРОТНЯ: ПРЕДМЕТ", ok)) { Profile.Give(Profile.Roll(Campaign.Region + 1, 80, false)); campBoard = true; campSub = 0; TavToast("Задание выполнено: получен предмет."); return; }
                if (Btn(new Rect(x + w * 0.5f - 80f, r.yMax - 54f, 160f, 40f), "НАЗАД", ok)) campSub = 0;
                return;
            }
            // Small, visibly clickable plaques; only the buttons are drawn,
            // never a tint/rectangle on the original camp artwork or video.
            Rect forgeButton = OnScreen(CampRect(new Rect(335f, 159f, 205f, 46f), s, ox, oy));
            Rect altarButton = OnScreen(CampRect(new Rect(240f, 544f, 185f, 46f), s, ox, oy));
            Rect trainButton = OnScreen(CampRect(new Rect(1480f, 450f, 215f, 46f), s, ox, oy));
            bool enabled = ok && !InventoryScreen.Open;
            // Separate navigation buttons: the quest board no longer opens when tapping the distant hill house.
            // Match the tavern button style/height and keep a 12-unit gap.
            if (Btn(new Rect(W - 450f, 14f, 242f, 42f), "ДОСКА ЗАДАНИЙ", enabled && !campBoard)) { campSub = 2; return; }
            if (Btn(new Rect(W - 196f, 14f, 180f, 42f), "ТРАКТИР", enabled && UiKit.Tex("UI/tavern") != null)) { Profile.CostMul = 1f; OpenTavern(true); return; }
            if (Btn(ExitButtonRect(), "В ПУТЬ", enabled, true)) { Profile.CostMul = 1f; modal = Modal.None; Haptics.Pulse(20); return; }
            if (Btn(forgeButton, "КУЗНИЦА", enabled)) { Profile.CostMul = 0.5f; InventoryScreen.Show(0); return; }
            // использованное за этот привал гаснет — как в трактире и у доски заданий
            if (Btn(altarButton, "АЛТАРЬ", enabled && !campAltar)) { campSub = 1; return; }
            if (Btn(trainButton, "ТРЕНИРОВКА", enabled && !campTrain))
            {
                Profile.GivePoint();
                campTrain = true;
                TavToast("Тренировка у соломенных чучел: +1 очко характеристик.");
                return;
            }
        }

        // ---------------- трактир (UI/tavern: мрачная идзакая 1916×821, зоны нажатия поверх картины) ----------------
        // Трактирщик — чарка сакэ (благословение на бой); игроки в кости — ставка; доска наград — заказ на сильного врага;
        // пьяный ронин — слухи (кость пути / тайник); очаг — снять проклятие. Каждое — один раз за визит.
        // Живость: огонь очага дышит, фонари мерцают, из котла поднимается пар, под потолком стелется дым.
        const float TavW = 1916f, TavH = 821f;
        static readonly Rect TavKeeper = new Rect(80f, 60f, 330f, 320f), TavDice = new Rect(1120f, 210f, 380f, 270f),
            TavBoard = new Rect(1530f, 80f, 270f, 290f), TavDrunk = new Rect(1270f, 430f, 560f, 330f), TavHearth = new Rect(840f, 380f, 440f, 200f);
        static int AllyPrice { get { return 120 + 40 * Campaign.Region; } }
        /// <summary>Цена соратника по силе: ученик дёшев, воин дороже, мастер — самый дорогой.</summary>
        static int AllyPriceOf(int tier) { return Mathf.RoundToInt(AllyPrice * (tier == 0 ? 0.6f : tier == 1 ? 1.2f : 2.4f) / 5f) * 5; }
        int tavSub;           // 0 — зал, 1 — трактирщик, 2 — кости, 3 — доска, 4 — ронин
        bool tavKeeper, tavDice, tavBoard, tavDrunk, tavHearth, tavFromCamp, tavDiceFresh;
        void OpenTavern(bool fromCamp = false)
        {
            tavSub = 0; tavKeeper = tavDice = tavBoard = tavDrunk = tavHearth = false; tavFromCamp = fromCamp; tavDiceFresh = true;
            TavernVideo.Preload();
            OpenModal(Modal.Tavern);
        }
        void TavToast(string text)
        {
            toastLines.Clear();
            foreach (string line in Wrap(text, 48)) { if (toastLines.Count >= 3) break; toastLines.Add(line); }
            toastAt = Time.unscaledTime; Haptics.Pulse(25);
        }
        void DrawTavern(bool ok)
        {
            Texture2D art = UiKit.Tex("UI/tavern");
            float s = Mathf.Max(W / TavW, 540f / TavH), ox = (W - TavW * s) * 0.5f, oy = (540f - TavH * s) * 0.5f;
            // живой трактир (видео, та же раскадровка); пока не готово — картинка с огнём и паром кодом
            Texture vid = TavernVideo.Frame();
            if (vid != null) GUI.DrawTexture(new Rect(ox, oy, TavW * s, TavH * s), vid, ScaleMode.StretchToFill, false);
            else
            {
                if (art != null) GUI.DrawTexture(new Rect(ox, oy, TavW * s, TavH * s), art, ScaleMode.StretchToFill, false);
                if (TavernVideo.Failed) DrawTavernLife(s, ox, oy);   // видео готовится — та же картинка без эффектов (совпадает с первым кадром)
            }
            int gold = Profile.Gold;
            if (tavSub == 1)
            {
                const string keeperText = "Одноглазый не улыбается. «Сакэ — двадцать мон. Крепкое. Наутро рука твёрже — или не проснёшься».";
                // высота окна — по содержимому: заголовок, текст, кнопка действия, «НАЗАД»
                Rect r = Card(800f, Mathf.Max(320f, 22f + 36f + LinesH(508f, keeperText, 52, 13f) + 12f + 46f + 18f + 54f));
                Picture(new Rect(r.x, r.y, 250f, r.height), "UI/npc_keeper");   // иллюстрация слева, как у торговца и событий
                float x = r.x + 268f, w = r.width - 292f, y = r.y + 22f;
                VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "ТРАКТИРЩИК", 20f); y += 36f;
                y = Lines(x, y, w, keeperText, 52, 13f, Ink) + 12f;
                if (Btn(new Rect(x, y, w, 46f), "ЧАРКА САКЭ — 20 МОН", ok && gold >= 20, true))
                {
                    Profile.AddGold(-20); int b = Random.Range(1, 4); Campaign.SetBlessing(b); tavKeeper = true; tavSub = 0;
                    TavToast(b == 1 ? "Сакэ жжёт горло. Рука налилась силой: +20% урона в следующем бою." : b == 2 ? "Тепло разливается по груди: следующий бой — с полным духом." : "Хмель гонит боль прочь: +25% здоровья в следующем бою.");
                }
                if (Btn(new Rect(x + w * 0.5f - 80f, r.yMax - 54f, 160f, 40f), "НАЗАД", ok)) tavSub = 0;
                return;
            }
            if (tavSub == 2)
            {
                // чо-хан отдельным экраном: стол, жулик напротив, кости на физике (TavernDiceGame)
                if (TavernDiceGame.Draw(ok, W, Gold, Ink, Dim)) { tavSub = 0; tavDice = TavernDiceGame.RoundsLeft <= 0; }
                return;
            }
            if (tavSub == 3)
            {
                int reward = 80 + 40 * Campaign.Region + 40 * Profile.Cycle;
                string boardText = Campaign.HasBounty ? "Заказ уже взят: победи сильного противника или владыку области — награда твоя."
                    : "Листки с лицами. «Голова сильного бойца на дороге — " + reward + " мон. Голова владыки — вдвое».";
                Rect r = Card(800f, Mathf.Max(320f, 22f + 36f + LinesH(508f, boardText, 52, 13f) + 12f + (Campaign.HasBounty ? 0f : 46f + 18f) + 54f));
                Picture(new Rect(r.x, r.y, 250f, r.height), "UI/npc_bounty");   // иллюстрация слева, как у торговца и событий
                float x = r.x + 268f, w = r.width - 292f, y = r.y + 22f;
                VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "ДОСКА НАГРАД", 20f); y += 36f;
                y = Lines(x, y, w, boardText, 52, 13f, Ink) + 12f;
                if (!Campaign.HasBounty && Btn(new Rect(x, y, w, 46f), "ВЗЯТЬ ЗАКАЗ", ok, true)) { Campaign.TakeBounty(reward); tavBoard = true; tavSub = 0; TavToast("Заказ взят. Победи сильного противника или владыку — получишь " + reward + " мон (за владыку вдвое)."); }
                if (Btn(new Rect(x + w * 0.5f - 80f, r.yMax - 54f, 160f, 40f), "НАЗАД", ok)) tavSub = 0;
                return;
            }
            if (tavSub == 4)
            {
                // пьяный ронин и его приятели: расспросить — или нанять соратника по силе (бьётся на арене с подручным врага)
                bool has = Profile.Ally >= 0;
                string intro = has
                    ? (Profile.AllyDown ? "«Твой соратник лежит у травницы — отнеси его к ней». Можно взять и другого — прежний уйдёт." : "«С тобой уже ходит " + Profile.AllyTierNames[SideDuel.Tier].ToLower() + ". Хочешь сильнее — плати, прежний уйдёт».")
                    : "Ронин трезвеет, стоит звякнуть кошелем. «Пока ты бьёшься с главным, мы займём его подручного. Сколько дашь — так и будем рубиться».";
                // окно по содержимому: без пустой полосы между выбором соратника и нижними кнопками
                Rect r = Card(900f, Mathf.Max(340f, 22f + 36f + LinesH(608f, intro, 58, 13f) + 10f + 3f * 48f + 10f + 60f));
                Picture(new Rect(r.x, r.y, 250f, r.height), "UI/npc_ronin");   // иллюстрация слева, как у торговца и событий
                float x = r.x + 268f, w = r.width - 292f, y = r.y + 22f;
                VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), "ПЬЯНЫЙ РОНИН", 20f); y += 36f;
                y = Lines(x, y, w, intro, 58, 13f, Ink) + 10f;
                string[] who = { "УЧЕНИК", "ВОИН", "МАСТЕР" };
                string[] hint = { "дёшево, слабый — продержится, но вряд ли победит", "крепкий рубака — на равных с подручным", "опасный клинок — может одолеть даже владыку" };
                for (int t = 0; t < 3; t++)
                {
                    int price = AllyPriceOf(t);
                    bool same = has && !Profile.AllyDown && Profile.Ally == t;
                    if (Btn(new Rect(x, y, w * 0.5f, 40f), who[t] + " — " + price + " МОН", ok && gold >= price && !same, t == 2))
                    {
                        Profile.AddGold(-price); Profile.SetAlly(t); tavDrunk = true; tavSub = 0;
                        TavToast("С тобой идёт соратник (" + who[t].ToLower() + "). В бою он схватится с подручным врага в стороне; падёт — выходит травница.");
                        return;
                    }
                    UiKit.Text(new Rect(x + w * 0.5f + 14f, y, w * 0.5f - 14f, 40f), same ? "уже с тобой" : hint[t], 11.5f, same ? Gold : new Color(0.82f, 0.78f, 0.7f), TextAnchor.MiddleLeft, false);
                    y += 48f;
                }
                if (Btn(new Rect(x, r.yMax - 56f, w * 0.5f - 6f, 40f), "РАССПРОСИТЬ", ok)) { tavSub = 0; DrunkRoninTalk(); return; }
                if (Btn(new Rect(x + w * 0.5f + 6f, r.yMax - 56f, w * 0.5f - 6f, 40f), "НАЗАД", ok)) tavSub = 0;
                return;
            }
            // Existing tavern photo/video/intentional static overlay are untouched.
            // Replace the five tiny plain labels and huge invisible hitboxes
            // with compact Kit buttons positioned beside their original objects.
            Rect keeperButton = OnScreen(CampRect(new Rect(137f, 432f, 237f, 44f), s, ox, oy));
            Rect diceButton = OnScreen(CampRect(new Rect(1236f, 424f, 185f, 44f), s, ox, oy));
            Rect boardButton = OnScreen(CampRect(new Rect(1610f, 40f, 200f, 44f), s, ox, oy));
            Rect drunkButton = OnScreen(CampRect(new Rect(1480f, 670f, 244f, 44f), s, ox, oy));
            Rect hearthButton = OnScreen(CampRect(new Rect(900f, 520f, 160f, 44f), s, ox, oy));
            Texture2D plaque = UiKit.Tex("UI/Kit/hud_banner");
            // как ленты объявлений в бою: рваные концы не растягиваются, тянется только тёмная середина —
            // плашка невысокая, а длинное название целиком в её тёмной части (центр тёмной полосы — 53% высоты)
            const float plaqueW = 520f, plaqueH = 64f, plaqueY = 4f;
            if (plaque != null)
                UiKit.Slice(new Rect(W * 0.5f - plaqueW * 0.5f, plaqueY, plaqueW, plaqueH), plaque, plaqueH / plaque.height, 0.2f, 0.08f, 0.5f, 0f, Color.white);
            UiKit.Text(new Rect(0f, plaqueY + plaqueH * 0.53f - 15f, W, 30f), "ТРАКТИР «ЧЁРНЫЙ ФОНАРЬ»", 18f, Gold);
            bool enabled = ok && !InventoryScreen.Open;
            if (Btn(ExitButtonRect(), tavFromCamp ? "В ЛАГЕРЬ" : "УЙТИ", enabled, true))
            {
                if (tavFromCamp) { modal = Modal.Camp; modalAt = Time.unscaledTime; } else modal = Modal.None;
                Haptics.Pulse(20); return;
            }
            if (Btn(keeperButton, "ТРАКТИРЩИК", enabled && !tavKeeper)) { tavSub = 1; return; }
            if (Btn(diceButton, "КОСТИ", enabled && !tavDice)) { tavSub = 2; TavernDiceGame.Enter(tavDiceFresh); tavDiceFresh = false; return; }
            if (Btn(boardButton, "НАГРАДЫ", enabled && !tavBoard)) { tavSub = 3; return; }
            if (Btn(drunkButton, "ПЬЯНЫЙ РОНИН", enabled && !tavDrunk)) { if (SideDuel.Enabled) tavSub = 4; else DrunkRoninTalk(); return; }
            if (Btn(hearthButton, "ОЧАГ", enabled && !tavHearth))
            {
                tavHearth = true;
                if (Campaign.Cursed) { Campaign.LiftCurse(); TavToast("Ты долго смотришь в огонь. Тяжесть проклятия уходит с дымом."); }
                else { Profile.AddXp(Mathf.RoundToInt(Profile.XpToNext * 0.15f)); TavToast("Тепло очага, треск углей. Ум проясняется: немного опыта."); }
            }
        }

        void DrunkRoninTalk()
        {
            {
                tavDrunk = true;
                if (Random.value < 0.5f) { Campaign.AddRoll(); TavToast("Ронин приоткрывает глаз: «Короткая тропа за старым мостом…» — и снова храпит. +1 кость пути."); }
                else { int g = 15 + 10 * Campaign.Region; Profile.AddGold(g); TavToast("Ронин бормочет о засаде и роняет кошель. Ты возвращаешь его… почти весь: +" + g + " мон."); }
            }
        }

        void TavLabel(Rect z, string text, bool used)
        {
            // использованное — только приглушённая подпись (тёмный квадрат поверх арта оставался пятном)
            Rect l = new Rect(z.center.x - 70f, z.yMax - 26f, 140f, 22f);
            UiKit.Rect(l, new Color(0.03f, 0.02f, 0.02f, used ? 0.5f : 0.72f));
            UiKit.Text(l, text, 11f, used ? Dim : Gold);
        }

        /// <summary>Живой трактир: огонь очага, мерцание фонарей, пар над котлом, дым под потолком.</summary>
        void DrawTavernLife(float s, float ox, float oy)
        {
            if (Event.current.type != EventType.Repaint) return;
            float t = Time.unscaledTime;
            Texture2D glow = UiKit.Tex("FX/glow_pool"), fire = UiKit.Tex("FX/brazier_fire"), mist = SoftSprites.Fog;
            Color oc = GUI.color;
            System.Func<float, float, Vector2> TP = (x, y) => new Vector2(ox + x * s, oy + y * s);
            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(t * 3.1f, 0.3f);
            if (glow != null)
            {
                // очаг
                Vector2 h = TP(1050f, 470f);
                GUI.color = new Color(1f, 0.55f, 0.2f, 0.32f * flick);
                GUI.DrawTexture(new Rect(h.x - 230f * s, h.y - 150f * s, 460f * s, 300f * s), glow);
                // фонари
                Vector2[] lamps = { TP(455f, 150f), TP(1325f, 185f) };
                for (int i = 0; i < lamps.Length; i++)
                {
                    float f = 0.8f + 0.2f * Mathf.PerlinNoise(t * 2.3f + i * 7f, 1.7f);
                    GUI.color = new Color(1f, 0.7f, 0.35f, 0.38f * f);
                    float r = (130f + 10f * f) * s;
                    GUI.DrawTexture(new Rect(lamps[i].x - r, lamps[i].y - r, r * 2f, r * 2f), glow);
                }
            }
            if (fire != null)
            {
                // огонь под котлом: три невысоких языка по углям (раньше один большой костёр закрывал сам котёл)
                float[] fx = { 1000f, 1042f, 1084f };
                for (int i = 0; i < fx.Length; i++)
                {
                    int fr = ((int)(t * 13f) + i * 3) % 8;
                    float fh = (44f + 10f * Mathf.PerlinNoise(t * 2.7f + i * 5f, 0.5f)) * (i == 1 ? 1.15f : 0.9f), fw = fh * 1.25f;
                    Vector2 b = TP(fx[i] + 3f * Mathf.Sin(t * 3f + i), 497f);
                    GUI.color = new Color(1f, 0.92f, 0.8f, 0.82f);
                    GUI.DrawTextureWithTexCoords(new Rect(b.x - fw * 0.5f * s, b.y - fh * s, fw * s, fh * s), fire, new Rect((fr % 4) * 0.25f, fr < 4 ? 0.5f : 0f, 0.25f, 0.5f));
                }
            }
            if (glow != null)
            {
                // искры от углей
                for (int k = 0; k < 12; k++)
                {
                    float sp = 0.35f + 0.25f * Mathf.Repeat(k * 0.618f, 1f);
                    float ph = Mathf.Repeat(t * sp + k * 0.377f, 1f);
                    float x0 = 975f + 130f * Mathf.Repeat(k * 0.41f, 1f);
                    Vector2 p = TP(x0 + 14f * Mathf.Sin(t * 2f + k * 1.7f) * ph, 488f - ph * (70f + 50f * Mathf.Repeat(k * 0.73f, 1f)));
                    float r = (3.5f - 2f * ph) * s;
                    GUI.color = new Color(1f, 0.6f + 0.3f * (1f - ph), 0.25f, (1f - ph) * 0.9f);
                    GUI.DrawTexture(new Rect(p.x - r * 2f, p.y - r * 2f, r * 4f, r * 4f), glow);
                }
            }
            Texture2D wisp = UiKit.Tex("FX/smoke_wisp");
            if (wisp == null) wisp = mist;
            if (wisp != null)
            {
                // пар из котла: клубы поднимаются от крышки, тянутся влево вверх по нарисованному пару, крутятся и тают
                Matrix4x4 m0 = GUI.matrix;
                for (int k = 0; k < 7; k++)
                {
                    float ph = Mathf.Repeat(t * 0.13f + k / 7f, 1f);
                    float x = Mathf.Lerp(1030f, 950f, ph) + 22f * Mathf.Sin(t * 0.7f + k * 1.9f) * ph;
                    float y = Mathf.Lerp(395f, 110f, ph);
                    Vector2 p = TP(x, y);
                    float sz = (70f + 190f * ph) * s;
                    float a = 0.42f * Mathf.Sin(ph * Mathf.PI) * (k % 2 == 0 ? 1f : 0.75f);
                    GUI.matrix = m0;
                    GUIUtility.RotateAroundPivot((k * 53f + t * (k % 2 == 0 ? 9f : -7f)) % 360f, (Vector2)m0.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f)));
                    GUI.color = new Color(0.82f, 0.8f, 0.86f, a);
                    GUI.DrawTexture(new Rect(p.x - sz * 0.5f, p.y - sz * 0.5f, sz, sz), wisp);
                }
                GUI.matrix = m0;
            }
            if (mist != null)
            {
                // дым под потолком
                for (int k = 0; k < 3; k++)
                {
                    float x = Mathf.Repeat(t * 12f + k * 640f, TavW + 600f) - 300f;
                    Vector2 p = TP(x, 60f + 25f * k);
                    GUI.color = new Color(0.55f, 0.5f, 0.6f, 0.16f);
                    GUI.DrawTexture(new Rect(p.x - 300f * s, p.y - 60f * s, 600f * s, 120f * s), mist);
                }
            }
            GUI.color = oc;
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
            float w = Mathf.Min(680f, W - 90f);
            float h = 40f + toastLines.Count * 19f;
            // Centre reward notifications over the current camp/tavern/map view.
            Rect r = new Rect((W - w) * 0.5f, (540f - h) * 0.5f, w, h);
            VisualIdentity.KitSurface(r, fade, false);
            for (int i = 0; i < toastLines.Count; i++)
                UiKit.Text(new Rect(r.x + 54f, r.y + (h - toastLines.Count * 19f) * 0.5f + i * 19f, r.width - 108f, 19f),
                    toastLines[i], 12f, new Color(Ink.r, Ink.g, Ink.b, fade),
                    TextAnchor.MiddleCenter, false);
        }

        void StartFight(int at, bool rematchStarted = false)
        {
            // погода арены — от облака над местом боя в этот момент
            MapNode fn = Campaign.Nodes[at];
            Weather.MapSky = MapClouds.SkyAt(Campaign.Region, new Vector2(fn.x, fn.y));
            if (!rematchStarted) Campaign.BeginFight(at);
            Hide();
            DuelManager dm = DuelManager.Instance;
            if (dm != null) dm.ResetMatch();
        }

        // ---------------- события на дороге ----------------

        /// <summary>Привал с ev = TavernEv — придорожный трактир (свой значок, вход в трактир).</summary>
        public const int TavernEv = 99;
        static string NodeName(MapNode n) { return n.IsTavern ? "ТРАКТИР" : Campaign.TypeNames[n.type]; }
        static string NodeHint(MapNode n) { return n.IsTavern ? "Мрачный трактир у дороги: сакэ, кости, заказы на головы и пьяные слухи." : Campaign.TypeHints[n.type]; }

        public const int EventCount = 12, CatEvent = 8, CompanionEvent = 9;   // 9 ворон, 10 волк, 11 змея
        static readonly string[] EvTitle = { "РАНЕНЫЙ РОНИН", "СТАРЫЙ МОНАХ", "РАЗБОЙНИКИ", "БЕЛАЯ ЛИСА",
                                             "ЧАЙНЫЙ ДОМИК", "ДЕВА В ХРАМЕ", "МОНАХ НА МОСТУ", "БЕЖЕНЦЫ", "ХРАНИТЕЛЬ ДЕВЯТИ ЖИЗНЕЙ",
                                             "ВОРОН НА ПОЛЕ БИТВЫ", "ВОЛК В КАПКАНЕ", "ЗМЕЯ У АЛТАРЯ" };
        static readonly string[] EvText =
        {
            "У каменного фонаря сидит раненый ронин, зажимая бок. «Перевяжи меня, путник… я не останусь в долгу».",
            "У горного святилища старый монах протягивает тебе свиток. «Путь клинка — это путь духа. Возьми — или попроси благословения».",
            "Из бамбука с факелами выскакивают разбойники. Копья нацелены тебе в грудь: «Кошель — или жизнь!»",
            "На замшелом камне сидит белая лиса с девятью хвостами. Вокруг пляшут синие огоньки и зовут за собой в чащу.",
            "У дороги — чайный домик. Слепой старик с белыми глазами разливает чай в две чаши: «Садись, путник. Я слышал твои шаги ещё у моста».",
            "В разрушенном храме над поверженным Буддой парит дева в белом. У её ног — старый меч в ножнах. «Он ждал хозяина сто лет…»",
            "На узком мосту стоит исполин-монах с нагинатой. За спиной — гора отнятых мечей. «Ещё один клинок в мою коллекцию? Проходи — если сможешь».",
            "Под дождём беженцы бьются над сломанной повозкой: мать с ребёнком и старик. Из кустов на них глядят жёлтые глаза волка.",
            "На ступенях заброшенного святилища сидит огромный пушистый кот, рядом — пустая миска для подношений. Он лениво щурится: кажется, о твоём пути он знает больше, чем должен.",
            "Старое поле битвы на закате. На рукояти воткнутого в землю меча сидит огромный ворон с красными глазами и смотрит прямо на тебя, будто выбирает хозяина.",
            "На снежной тропе раненый волк попал в капкан и скалит зубы. Из тумана за ним следят горящие глаза стаи.",
            "В бамбуковой чаще вокруг статуи божества обвилась изумрудная змея с золотым узором. На алтаре — пустая чаша для подношений.",
        };

         void DrawEvent(MapNode n, int at, bool ok)
        {
            int ev = Mathf.Clamp(n.ev, 0, EventCount - 1);
            // окно по тексту события: длинный текст не уходит под кнопки, короткий — без пустоты (не ниже 360)
            Rect r = Card(720f, Mathf.Clamp(22f + 36f + LinesH(354f, EvText[ev], 38, 13f) + 16f + 118f + 12f, 360f, 500f));
            // The authored illustrated event series is a real painted asset set.
            // The old pack02 variants were tiny procedural placeholders that
            // made all four encounters appear almost identical.
            Picture(new Rect(r.x, r.y, 330f, r.height), "UI/event_" + ev);
            float x = r.x + 348f, w = r.width - 366f, y = r.y + 22f;
            VisualIdentity.TitlePlaque(new Rect(x, y, w, 26f), EvTitle[ev], 19f, TextAnchor.MiddleLeft); y += 36f;
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
                case 4:   // чайный домик слепого мастера: дух или очко характеристик
                    if (Btn(b1, "ВЫПИТЬ ЧАЙ", ok, true))
                    {
                        Campaign.SetBlessing(2);
                        int lv = Profile.AddXp(40 * stage);
                        Result("Горький чай проясняет голову. В следующем бою — полный дух, +" + (40 * stage) + " опыта" + (lv > 0 ? " и новый уровень!" : "."));
                    }
                    if (Btn(b2, "СПРОСИТЬ О ПУТИ МЕЧА", ok))
                    {
                        Profile.GivePoint();
                        Result("«Глаза лгут, клинок — нет». До темноты он учит тебя слушать сталь: +1 очко характеристик.");
                    }
                    break;
                case 5:   // дева-призрак: меч ценой проклятия или благословение за свечи
                    if (Btn(b1, "ВЗЯТЬ МЕЧ", ok, true))
                    {
                        Item it = Profile.Make(0, Random.value < 0.4f ? 3 : 2, 1f + 0.25f * stage + 0.4f * Profile.Cycle);
                        Profile.Give(it);
                        Campaign.Curse();
                        Result("Меч твой: «" + it.name + "». Но дева идёт следом. Проклятие: −25% здоровья в следующем бою.");
                    }
                    if (Btn(b2, "ЗАЖЕЧЬ СВЕЧИ (−20 МОН)", ok && Profile.Gold >= 20) && Profile.Spend(20))
                    {
                        Campaign.SetBlessing(1);
                        Result("Дева склоняет голову и тает в лунном свете. Её сила с тобой: +20% урона в следующем бою.");
                    }
                    break;
                case 6:   // монах на мосту: бой или поклон
                    if (Btn(b1, "ПРИНЯТЬ ВЫЗОВ", ok, true)) { Campaign.TurnToAmbush(at); OpenModal(Modal.Fight); }
                    if (Btn(b2, "ПОКЛОНИТЬСЯ И УЙТИ", ok))
                    {
                        int lv = Profile.AddXp(30 * stage);
                        Result("Монах хохочет: «Мудрый, кто знает свой предел!» Ты переходишь реку вброд: +" + (30 * stage) + " опыта" + (lv > 0 ? ", новый уровень!" : "."));
                    }
                    break;
                case 8:   // кот-хранитель: 4 выбора, одно благословение кота за раз (новое заменяет старое)
                {
                    float bh = 40f, gap = 6f, top = r.yMax - 4f * (bh + gap) - 12f;
                    Rect c1 = new Rect(x, top, w, bh), c2 = new Rect(x, top + (bh + gap), w, bh),
                         c3 = new Rect(x, top + 2f * (bh + gap), w, bh), c4 = new Rect(x, top + 3f * (bh + gap), w, bh);
                    if (Btn(c1, "ПРЕДЛОЖИТЬ ЕДУ (−15 МОН)", ok && Profile.Gold >= 15, true) && Profile.Spend(15))
                    {
                        Campaign.SetCatBlessing(1);
                        Result("Кот съедает рис до последнего зерна и трётся о ножны. «Кошачья удача»: 10% ударов по тебе пройдут мимо в следующих 3 боях.");
                    }
                    if (Btn(c2, "ПОГЛАДИТЬ КОТА", ok))
                    {
                        if (Random.value < 0.7f) { Campaign.SetCatBlessing(2); Result("Кот закрывает глаза и мурлычет, как далёкий гром. «Девять жизней»: в следующих 3 боях смертельный удар однажды оставит тебе 1 здоровья."); }
                        else Result("Кот молниеносно царапает руку и отворачивается. Похоже, ты ему не понравился.");
                    }
                    if (Btn(c3, "ОСТАВИТЬ МОНЕТУ (−20 МОН)", ok && Profile.Gold >= 20) && Profile.Spend(20))
                    {
                        Item it = Profile.Roll(stage, 60, false);
                        Profile.Give(it);
                        Result("Кот лапой сталкивает монету в миску, а из-под ступени выкатывается свёрток: «" + it.name + "».");
                    }
                    if (Btn(c4, "ПРОЙТИ МИМО", ok)) Result("Кот провожает тебя долгим презрительным взглядом.");
                    break;
                }
                case 9: case 10: case 11:   // спутники: ворон, волк, змея (одновременно — только один)
                {
                    int comp = ev - CompanionEvent;
                    int had = Profile.Companion;
                    string swap = had >= 0 && had != comp ? " (вместо: " + Profile.CompanionNames[had].ToLower() + ")" : "";
                    string[] take = { "ПРОТЯНУТЬ РУКУ", "ОСВОБОДИТЬ ИЗ КАПКАНА", "ОСТАВИТЬ ПОДНОШЕНИЕ (−30 МОН)" };
                    bool can = had != comp && (comp != 2 || Profile.Gold >= 30);
                    if (Btn(b1, take[comp] + swap, ok && can, true) && (comp != 2 || Profile.Spend(30)))
                    {
                        Profile.SetCompanion(comp);
                        bool fog = JourneyChoice.FogEnabled;
                        if (comp == 0) Result("Ворон каркает и садится тебе на плечо. Теперь он твой спутник: " + Profile.CompanionHint(0, fog));
                        else if (comp == 1)
                        {
                            bool bite = Random.value < 0.3f;
                            if (bite) Campaign.Curse();
                            Result((bite ? "Освобождённый волк в испуге хватает тебя за руку (−25% здоровья в следующем бою), но потом идёт следом. " : "Волк долго смотрит тебе в глаза и идёт следом. ") + Profile.CompanionHint(1, fog));
                        }
                        else Result("Змея пробует подношение языком и обвивается вокруг твоих ножен. " + Profile.CompanionHint(2, fog));
                    }
                    if (had == comp) UiKit.Text(new Rect(b1.x, b1.y - 22f, b1.width, 18f), "Этот спутник уже с тобой", 11f, Dim, TextAnchor.MiddleLeft, false);
                    if (Btn(b2, "ПРОЙТИ МИМО", ok)) Result("Ты идёшь дальше своей дорогой.");
                    break;
                }
                case 7:   // беженцы: кость пути за помощь или благословение за деньги
                    if (Btn(b1, "ПОМОЧЬ С ПОВОЗКОЙ", ok, true))
                    {
                        Campaign.AddRoll();
                        Result("Вы ставите колесо, волк уходит в чащу. Старик отдаёт тебе кость пути: «Тебе нужнее».");
                    }
                    if (Btn(b2, "ДАТЬ ДЕНЕГ (−40 МОН)", ok && Profile.Gold >= 40) && Profile.Spend(40))
                    {
                        Campaign.SetBlessing(3);
                        Result("Мать прячет моны за пазуху и молится за тебя. +25% здоровья в следующем бою.");
                    }
                    break;
            }
        }
    }
}
