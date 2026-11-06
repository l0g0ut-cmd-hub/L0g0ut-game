using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    public enum NodeType { Start, Duel, Elite, Boss, Shrine, Smith, Merchant, Treasure, Ambush, Event, Rest, Herbalist, Onsen, Dojo, Gamble, ClanShrine, HunterCamp }

    [Serializable]
    public class MapNode
    {
        public int layer;
        public float x, y;          // на карте области, доли ширины и высоты
        public int type;            // NodeType
        public int enemy;           // ступень Ladder для боя
        public int ev = -1;         // событие: 0 раненый ронин, 1 монах, 2 разбойники, 3 кицунэ, 4 чайный домик, 5 дева в храме, 6 монах на мосту, 7 беженцы, 8 кот-хранитель
        public bool done;
        public List<int> next = new List<int>();
        public List<int> extra = new List<int>();   // отряд: противники, которые вступают по очереди после первого (ступени Ladder)
        public NodeType Type { get { return (NodeType)type; } }
        public bool IsTavern { get { return Type == NodeType.Rest && ev == MapScreen.TavernEv; } }
        public bool IsFight { get { NodeType t = Type; return t == NodeType.Duel || t == NodeType.Elite || t == NodeType.Boss || t == NodeType.Ambush; } }
    }

    /// <summary>
    /// Поход между боями: три области (Лес теней → Горы Они → Проклятый сёгунат), на карте каждой — развилки из узлов:
    /// дуэль, сильный противник, засада, храм, кузнец, торговец, тайник, привал у наставника, событие; в конце — владыка области.
    /// После третьего владыки — новый круг (враги крепче, добыча богаче) и снова Лес теней. Прогресс хранится между запусками.
    /// </summary>
    public static class Campaign
    {
        [Serializable]
        class Data
        {
            public int region;
            public int roadsVer;        // 2 — места на новых дорогах (2026-11-03)
            public int catBuff;         // благословение кота-хранителя: 0 нет, 1 «Кошачья удача», 2 «Девять жизней»
            public int catFights;       // сколько боёв ещё действует
            public bool catLifeUsed;    // «Девять жизней» уже спасли
            public int at;
            public int fight = -1;      // узел, бой в котором идёт (−1 — не в бою)
            public int blessing;        // 0 — нет, 1 — клинка (+20% урона), 2 — духа (полный дух в начале), 3 — жизни (+25% здоровья)
            public bool cursed;         // проклятие кицунэ: −25% здоровья в следующем бою
            public int clearedRegion = -1;   // только что пройденная область (для плашки на карте)
            public int bounty;              // заказ из трактира: награда за сильного противника (за владыку — вдвое); 0 — нет заказа
            public int foe;                 // который по счёту противник отряда сейчас на арене (0 — первый)
            public bool rematch;            // идёт бой-реванш на уже пройденном месте (награда урезана, без добычи)
            public int rolls = 1;            // кости пути в запасе (кость — за каждую победу)
            public int heldSteps;            // остаток хода, прерванного боем: возвращается после победы
            public int steps;                // ходов осталось после броска
            public int lastRoll;             // что выпало
            public bool training;            // бой со странствующим ронином (запасной способ добыть кость)
            public bool knocked;             // проиграл — очнулся у травницы (плашка на карте)
            public List<MapNode> nodes = new List<MapNode>();
            // Only actual moves, never inferred from completed nodes. Persisted across restarts.
            public List<int> travelled = new List<int>();
            public List<Vector2> seen = new List<Vector2>();
            public int tower = -1;          // смотровая башня (место, с которого видно далеко); −1 — ещё не выбрана
            public bool towerLit;           // на башне зажжён огонь: туман вокруг неё разошёлся
            public bool towerTold;          // дозорный на башне уже рассказал о долине (окно показано)
            public int trainKind;           // бой вне места: 0 — странствующий ронин (кость пути), 1 — додзё (очко), 2 — ронин в розыске
            public int hunt = -1;           // место, где сейчас стоит ронин в розыске (−1 — заказа нет)
            public int huntReward;          // награда охотников за его голову
            public bool huntDone;           // в этой области заказ уже выполнен
            public bool roninDebt;          // перевязал раненого ронина: он ещё встретится (вернёт долг или предаст)
            public string note = "";        // сообщение для карты после боя вне места (додзё, охота)
        }

        public static readonly string[] RegionNames = { "ЛЕС ТЕНЕЙ", "ГОРЫ ОНИ", "ПРОКЛЯТЫЙ СЁГУНАТ" };
        public static readonly string[] TypeNames = { "НАЧАЛО ПУТИ", "ДУЭЛЬ", "СИЛЬНЫЙ ПРОТИВНИК", "ВЛАДЫКА ОБЛАСТИ", "ХРАМ", "КУЗНЕЦ", "ТОРГОВЕЦ", "ТАЙНИК", "ЗАСАДА", "СОБЫТИЕ", "ПРИВАЛ", "ХИЖИНА ТРАВНИЦЫ",
                                                       "ГОРЯЧИЙ ИСТОЧНИК", "ДОДЗЁ", "ИГОРНЫЙ ДОМ", "СВЯТИЛИЩЕ КЛАНА", "СТОЯНКА ОХОТНИКОВ" };
        public static readonly string[] TypeHints =
        {
            "Отсюда начинается путь по области.",
            "Поединок один на один. Опыт, моны и добыча.",
            "Враг крепче обычного и с лишней жизнью. Зато добыча — не ниже редкой.",
            "Хозяин этих земель. Победишь — откроется следующая область.",
            "Жрица дарует благословение на следующий бой.",
            "Кузнец улучшит снаряжение за полцены.",
            "Странствующий торговец продаёт снаряжение и свитки.",
            "Спрятанный сундук: моны и добыча без боя.",
            "Бой ночью, враг начинает с полным духом. Мон — в полтора раза больше.",
            "Встреча на дороге. Чем она кончится — решать тебе.",
            "У костра — старый наставник: тренировка или медитация.",
            "Здесь выхаживают проигравших. Заглянешь — травница даст кость пути. Проиграешь дальше — очнёшься здесь.",
            "Горячий источник в скалах: снимает проклятие и даёт сил на следующий бой.",
            "Сэнсэй ставит против тебя ученика с боккэном. Выстоишь — очко характеристик.",
            "Чо-хан по-крупному: ставки втрое выше, чем в трактире, и пять партий.",
            "Мастер святилища перекуёт знак клана на надетом снаряжении — ради бонуса комплекта.",
            "Охотники за головами дают заказ на ронина, который бродит по дорогам области.",
        };

        // места узлов на каждой карте (доли ширины/высоты), по слоям от начала (внизу слева) к владыке (вверху справа)
        // ROADS-SPOTS BEGIN (сгенерировано tools/gen_map_roads.py из tools/map_roads.json — правьте JSON)
        static readonly Vector2[][][] Spots =
        {
            new[]   // лес теней
            {
                new[] { new Vector2(0.215f, 0.722f) },
                new[] { new Vector2(0.225f, 0.585f), new Vector2(0.3f, 0.885f) },
                new[] { new Vector2(0.465f, 0.66f), new Vector2(0.575f, 0.615f) },
                new[] { new Vector2(0.52f, 0.52f), new Vector2(0.67f, 0.55f) },
                new[] { new Vector2(0.605f, 0.462f), new Vector2(0.715f, 0.445f) },
                new[] { new Vector2(0.785f, 0.395f), new Vector2(0.875f, 0.3f) },
                new[] { new Vector2(0.858f, 0.16f) },
            },
            new[]   // горы они
            {
                new[] { new Vector2(0.19f, 0.67f) },
                new[] { new Vector2(0.272f, 0.555f), new Vector2(0.345f, 0.805f) },
                new[] { new Vector2(0.195f, 0.415f), new Vector2(0.37f, 0.445f) },
                new[] { new Vector2(0.445f, 0.47f), new Vector2(0.58f, 0.535f) },
                new[] { new Vector2(0.66f, 0.37f), new Vector2(0.73f, 0.465f) },
                new[] { new Vector2(0.76f, 0.35f), new Vector2(0.89f, 0.43f) },
                new[] { new Vector2(0.805f, 0.235f) },
            },
            new[]   // сёгунат
            {
                new[] { new Vector2(0.335f, 0.665f) },
                new[] { new Vector2(0.13f, 0.46f), new Vector2(0.375f, 0.79f) },
                new[] { new Vector2(0.19f, 0.378f), new Vector2(0.465f, 0.612f) },
                new[] { new Vector2(0.588f, 0.455f), new Vector2(0.63f, 0.58f) },
                new[] { new Vector2(0.72f, 0.42f), new Vector2(0.78f, 0.6f) },
                new[] { new Vector2(0.9f, 0.468f), new Vector2(0.86f, 0.585f) },
                new[] { new Vector2(0.87f, 0.19f) },
            },
        };
        // ROADS-SPOTS END
        // Места до перекладки дорог (2026-11-03): по ним старые сохранения переносятся на новые места.
        static readonly Vector2[][][] OldSpots =
        {
            new[]   // лес теней
            {
                new[] { V(27, 66) },
                new[] { V(22, 47), V(31, 79) },
                new[] { V(45, 66), V(62, 64) },
                new[] { V(50, 47), V(68, 47) },
                new[] { V(58, 37), V(75, 40) },
                new[] { V(64, 27), V(83, 33) },
                new[] { V(86, 17) },
            },
            new[]   // горы они
            {
                new[] { V(22, 72) },
                new[] { V(28, 47), V(33, 64) },
                new[] { V(18, 32), V(38, 73) },
                new[] { V(44, 59), V(57, 48) },
                new[] { V(63, 36), V(82, 48) },
                new[] { V(74, 27), V(92, 36) },
                new[] { V(81, 17) },
            },
            new[]   // сёгунат
            {
                new[] { V(27, 62) },
                new[] { V(36, 47), V(38, 76) },
                new[] { V(22, 34), V(46, 60) },
                new[] { V(56, 38), V(64, 60) },
                new[] { V(70, 45), V(83, 58) },
                new[] { V(68, 28), V(88, 40) },
                new[] { V(82, 15) },
            },
        };
        static Vector2 V(float x, float y) { return new Vector2(x / 100f, y / 100f); }

        static Data d;
        static Data D { get { if (d == null) Load(); return d; } }

        static void Load()
        {
            string s = PlayerPrefs.GetString("campaign_v1", "");
            if (!string.IsNullOrEmpty(s)) { try { d = JsonUtility.FromJson<Data>(s); } catch { d = null; } }
            if (d == null || d.nodes == null || d.nodes.Count == 0) { d = new Data(); Generate(0); }
            if (d.at < 0 || d.at >= d.nodes.Count) d.at = 0;
            MigrateSpots(d);
            // Old campaign_v1 saves have no travel history: start from the current position.
            if (d.travelled == null) d.travelled = new List<int>();
            if (d.travelled.Count == 0 || d.travelled[d.travelled.Count - 1] != d.at)
            {
                d.travelled.Clear(); d.travelled.Add(d.at);
            }
        }
        /// <summary>Старое сохранение: каждое место переезжает на новое место своего слоя (ближайшее старое → тот же номер).</summary>
        static void MigrateSpots(Data dd)
        {
            if (dd.roadsVer >= 2) return;
            int r = Mathf.Clamp(dd.region, 0, Spots.Length - 1);
            foreach (MapNode n in dd.nodes)
            {
                int l = Mathf.Clamp(n.layer, 0, Mathf.Min(OldSpots[r].Length, Spots[r].Length) - 1);
                Vector2[] olds = OldSpots[r][l], news = Spots[r][l];
                int best = 0; float bd = float.MaxValue;
                for (int k = 0; k < olds.Length; k++)
                {
                    float dist = (olds[k] - new Vector2(n.x, n.y)).sqrMagnitude;
                    if (dist < bd) { bd = dist; best = k; }
                }
                Vector2 p = news[Mathf.Min(best, news.Length - 1)];
                n.x = p.x; n.y = p.y;
            }
            dd.roadsVer = 2;
        }
        public static void Save() { PlayerPrefs.SetString("campaign_v1", JsonUtility.ToJson(D)); PlayerPrefs.Save(); }
        public static void Restart()
        {
            d = new Data();
            Generate(0);
            d.rolls = 1;
            d.steps = 0;
            d.knocked = false;
            d.training = false;
            d.clearedRegion = -1;
            d.fight = -1;
            Save();
        }


        public static int Region { get { return D.region; } }
        public static List<MapNode> Nodes { get { return D.nodes; } }
        public static int At { get { return D.at; } }
        public static IList<int> Travelled { get { return D.travelled; } }
        /// <summary>Точки, вокруг которых туман войны уже разошёлся (в порядке прохождения).</summary>
        public static IList<Vector2> Seen
        {
            get
            {
                if (D.seen == null) D.seen = new List<Vector2>();
                if (D.seen.Count == 0)   // старое сохранение или новая область: всё пройденное и текущее место
                {
                    for (int i = 0; i < D.travelled.Count; i++)
                    {
                        if (i > 0) { TerrainRoads.Bind(D.region, D.nodes); TerrainRoads.Path road = TerrainRoads.Route(D.travelled[i - 1], D.travelled[i]); for (int q = 1; q < 8; q++) D.seen.Add(road.Position(q / 8f)); }
                        MapNode m = D.nodes[D.travelled[i]]; D.seen.Add(new Vector2(m.x, m.y));
                    }
                    if (D.seen.Count == 0) D.seen.Add(new Vector2(Current.x, Current.y));
                }
                return D.seen;
            }
        }
        /// <summary>Смотровая башня области: место в середине пути (не начало, не владыка, не травница).</summary>
        public static int Tower
        {
            get
            {
                if (D.tower < 0 || D.tower >= D.nodes.Count)
                {
                    int L = 0; foreach (MapNode n in D.nodes) L = Mathf.Max(L, n.layer + 1);
                    List<int> pick = new List<int>();
                    for (int i = 0; i < D.nodes.Count; i++)
                    {
                        MapNode n = D.nodes[i];
                        if (n.layer >= 2 && n.layer <= L - 3 && n.Type != NodeType.Herbalist && n.Type != NodeType.Boss) pick.Add(i);
                    }
                    if (pick.Count == 0) for (int i = 1; i < D.nodes.Count - 1; i++) pick.Add(i);
                    D.tower = pick.Count > 0 ? pick[(D.region * 7 + D.nodes.Count) % pick.Count] : 0;
                    Save();
                }
                return D.tower;
            }
        }
        public static bool TowerLit { get { return D.towerLit; } }

        /// <summary>Герой здесь: туман вокруг расходится. Точки реже ~12 точек карты не копятся; сохраняется по прибытии.</summary>
        public static void Reveal(Vector2 p)
        {
            IList<Vector2> s = Seen;
            Vector2 last = s[s.Count - 1];
            if (!D.towerLit && D.nodes.Count > 0)
            {   // герой у подножия смотровой башни — на ней загорается сигнальный огонь
                MapNode tw = D.nodes[Tower];
                if (((new Vector2(tw.x, tw.y) - p) * new Vector2(810f, 540f)).sqrMagnitude < 30f * 30f) { D.towerLit = true; Save(); }
            }
            if (((last - p) * new Vector2(810f, 540f)).sqrMagnitude < 144f) return;
            D.seen.Add(p);
            if (D.seen.Count > 600) D.seen.RemoveRange(0, D.seen.Count - 600);
        }
        public static bool WasTravelled(int from, int to)
        {
            List<int> path = D.travelled;
            for (int i = 1; i < path.Count; i++)
                if (path[i - 1] == from && path[i] == to) return true;
            return false;
        }
        public static MapNode Current { get { return D.nodes[D.at]; } }
        public static bool InFight { get { return D.fight >= 0 && D.fight < D.nodes.Count; } }
        public static MapNode FightNode { get { return InFight ? D.nodes[D.fight] : null; } }
        public static int Blessing { get { return D.blessing; } }
        public static bool Cursed { get { return D.cursed; } }
        public static int ClearedRegion { get { return D.clearedRegion; } }
        public static void AckCleared() { D.clearedRegion = -1; Save(); }
        /// <summary>
        /// Соседние места на карте: вперёд по дороге (next) и назад — к уже пройденному месту,
        /// откуда дорога ведёт сюда. Шаг назад тратит ход кости, как и шаг вперёд.
        /// </summary>
        public static bool Adjacent(int from, int to)
        {
            if (from < 0 || to < 0 || from >= D.nodes.Count || to >= D.nodes.Count || from == to) return false;
            if (D.nodes[from].next.Contains(to)) return true;
            if (Lateral(from, to)) return true;
            return D.nodes[to].done && D.nodes[to].next.Contains(from);
        }

        // Соседи «вбок»: места одного ряда, между которыми по нарисованной карте идёт своя дорога, не проходящая
        // через другие места (торговец у моста рядом с тайником и т.п.). Раньше такой шаг был запрещён — «дороги туда нет».
        static readonly HashSet<int> lateral = new HashSet<int>();
        static string lateralKey;
        static bool Lateral(int a, int b)
        {
            if (D.nodes[a].layer != D.nodes[b].layer) return false;
            string key = D.region + ":" + D.nodes.Count + ":" + D.nodes[0].x + ":" + D.nodes[D.nodes.Count - 1].y;
            if (key != lateralKey)
            {
                lateralKey = key; lateral.Clear();
                TerrainRoads.Bind(D.region, D.nodes);
                Vector2 px = new Vector2(810f, 540f);
                for (int i = 0; i < D.nodes.Count; i++)
                    for (int j = i + 1; j < D.nodes.Count; j++)
                    {
                        MapNode ni = D.nodes[i], nj = D.nodes[j];
                        if (ni.layer != nj.layer) continue;
                        Vector2 pi = new Vector2(ni.x, ni.y), pj = new Vector2(nj.x, nj.y);
                        float straight = Vector2.Scale(pi - pj, px).magnitude;
                        if (straight > 230f) continue;
                        TerrainRoads.Path road = TerrainRoads.Route(i, j);
                        if (road.lengthPixels > straight * 1.7f + 40f) continue;   // дорога в обход через полкарты — не соседи
                        bool through = false;
                        for (int q = 1; q < 12 && !through; q++)
                        {
                            Vector2 rp = road.Position(q / 12f);
                            for (int o = 0; o < D.nodes.Count && !through; o++)
                                if (o != i && o != j && Vector2.Scale(rp - new Vector2(D.nodes[o].x, D.nodes[o].y), px).sqrMagnitude < 24f * 24f) through = true;
                        }
                        if (!through) { lateral.Add(i * 1000 + j); lateral.Add(j * 1000 + i); }
                    }
            }
            return lateral.Contains(a * 1000 + b);
        }
        /// <summary>Шаг на уже пройденное место (назад или по своим следам) — бесплатный: кость тратится только на новые места.</summary>
        public static bool FreeHop(int i) { return i >= 0 && i < D.nodes.Count && D.nodes[i].done; }
        /// <summary>С текущего места можно уходить: оно пройдено — или это услуга, через которую проехали насквозь
        /// (она остаётся непосещённой, чтобы к ней вернуться, но держать героя не должна).</summary>
        public static bool CurrentSettled { get { return Current.done || IsService(Current); } }
        public static bool CanGo(int i) { return i >= 0 && i < D.nodes.Count && CurrentSettled && Adjacent(D.at, i) && !InFight && !D.training && (D.steps > 0 || FreeHop(i)); }
        public static int Rolls { get { return D.rolls; } }
        public static int Steps { get { return D.steps; } }
        public static int LastRoll { get { return D.lastRoll; } }
        public static bool Training { get { return D.training; } }
        /// <summary>Идёт бой с ронином в розыске (охота за головой).</summary>
        public static bool Hunting { get { return D.training && D.trainKind == 2; } }
        public static bool Dojo { get { return D.training && D.trainKind == 1; } }
        public static bool Knocked { get { return D.knocked; } }
        public static void AckKnocked() { D.knocked = false; Save(); }
        public static bool Stuck { get { return D.rolls <= 0 && D.steps <= 0 && CurrentSettled && !InFight; } }
        public static int GuideCost { get { return Mathf.RoundToInt((40 + 25 * D.region) * (1f + 0.4f * Profile.Cycle)); } }

        /// <summary>Бросок кости пути: 1–3 хода. Кость тратится.</summary>
        public static int RollDie()
        {
            if (D.rolls <= 0 || D.steps > 0 || !CurrentSettled || InFight || D.training) return 0;
            D.rolls--; D.lastRoll = UnityEngine.Random.Range(1, 4); D.steps = D.lastRoll;
            MoveHunt(0.6f);   // ронин в розыске тоже не стоит на месте
            Save();
            return D.lastRoll;
        }
        public static void AddRoll(int n = 1) { if (n <= 0) return; D.rolls += n; Save(); }
        public static void CancelTraining() { if (D.training) { D.training = false; Save(); } }
        /// <summary>Проводник: кость за моны.</summary>
        public static bool BuyGuide() { if (!Profile.Spend(GuideCost)) return false; AddRoll(); return true; }
        /// <summary>Вызвать странствующего ронина: бой вне узла, победа — кость пути.</summary>
        public static void BeginTraining()
        {
            if (InFight || D.training || !CurrentSettled) return;
            D.training = true; D.trainKind = 0; D.fight = -1;
            Ladder.Index = DuelEnemy(D.region);
            Weather.ForceNight = false;
            Save();
        }
        /// <summary>Бой на боккэнах в додзё (вне места): победа — очко характеристик.</summary>
        public static void BeginDojo()
        {
            if (InFight || D.training) return;
            D.training = true; D.trainKind = 1; D.fight = -1;
            Ladder.Index = DuelEnemy(D.region);
            Weather.ForceNight = false;
            Save();
        }

        // ---- охота за головой: заказ на стоянке охотников, ронин бродит по дорогам (шаг при каждом броске кости)
        public static int HuntAt { get { return D.hunt >= 0 && D.hunt < D.nodes.Count ? D.hunt : -1; } }
        public static int HuntReward { get { return D.huntReward; } }
        public static bool HuntDone { get { return D.huntDone; } }
        public static int NewHuntReward { get { return Mathf.RoundToInt((150 + 60 * D.region) * (1f + 0.4f * Profile.Cycle)); } }
        static bool HuntSpot(int i)
        {
            MapNode n = D.nodes[i];
            int L = 0; foreach (MapNode m in D.nodes) L = Mathf.Max(L, m.layer + 1);
            return i != D.at && n.Type != NodeType.Boss && n.Type != NodeType.Start && n.Type != NodeType.HunterCamp && n.layer <= L - 2;
        }
        public static void TakeHunt()
        {
            if (HuntAt >= 0 || D.huntDone) return;
            List<int> pick = new List<int>();
            for (int i = 0; i < D.nodes.Count; i++) if (HuntSpot(i) && D.nodes[i].layer >= Current.layer + 2) pick.Add(i);
            if (pick.Count == 0) for (int i = 0; i < D.nodes.Count; i++) if (HuntSpot(i)) pick.Add(i);
            if (pick.Count == 0) return;
            D.hunt = pick[UnityEngine.Random.Range(0, pick.Count)];
            D.huntReward = NewHuntReward;
            Save();
        }
        /// <summary>Ронин уходит на соседнее место (по дорогам в обе стороны); на героя не выходит сам.</summary>
        public static void MoveHunt(float chance)
        {
            int h = HuntAt;
            if (h < 0 || UnityEngine.Random.value > chance) return;
            List<int> nb = new List<int>();
            for (int i = 0; i < D.nodes.Count; i++)
                if (i != h && (D.nodes[h].next.Contains(i) || D.nodes[i].next.Contains(h) || Lateral(h, i)) && HuntSpot(i)) nb.Add(i);
            if (nb.Count > 0) D.hunt = nb[UnityEngine.Random.Range(0, nb.Count)];
        }
        public static void BeginHunt()
        {
            if (InFight || D.training || HuntAt < 0) return;
            D.training = true; D.trainKind = 2; D.fight = -1;
            Ladder.Index = Mathf.Clamp(D.region, 0, Ladder.Count - 1);
            Weather.ForceNight = false;
            Save();
        }
        /// <summary>Отступил от ронина — он уходит с этого места.</summary>
        public static void HuntRetreat() { MoveHunt(1f); Save(); }
        /// <summary>Сообщение карты после боя вне места (забирается один раз).</summary>
        public static string TakeNote() { string s = D.note; if (!string.IsNullOrEmpty(s)) { D.note = ""; Save(); } return s; }

        // ---- смотровая башня: дозорный рассказывает о долине (окно — один раз за область)
        public static bool TowerTold { get { return D.towerTold; } }
        public static void TellTower() { D.towerTold = true; Save(); }

        /// <summary>Гадалка: туман расходится над местами двух следующих рядов.</summary>
        public static void RevealAhead()
        {
            IList<Vector2> s = Seen;
            int l0 = Current.layer;
            foreach (MapNode n in D.nodes) if (n.layer > l0 && n.layer <= l0 + 2) s.Add(new Vector2(n.x, n.y));
            Save();
        }
        /// <summary>Остаток хода сгорает (лесной пожар: спасал коня).</summary>
        public static void ClearSteps() { D.steps = 0; Save(); }
        /// <summary>Событие становится боем (дуэль на рассвете — сильный противник; предательство ронина — с бандой).</summary>
        public static void TurnToFight(int i, NodeType t, int squad = 0)
        {
            MapNode n = D.nodes[i];
            n.type = (int)t;
            n.enemy = t == NodeType.Elite ? Mathf.Clamp(D.region, 0, Ladder.Count - 1) : DuelEnemy(D.region);
            if (n.extra == null) n.extra = new List<int>();
            n.extra.Clear();
            for (int k = 0; k < squad; k++) n.extra.Add(DuelEnemy(D.region));
            Save();
        }

        public static string MapTexture(int region) { return "UI/map_" + Mathf.Clamp(region, 0, 2); }

        // ---------------- генерация карты области ----------------

        static void Generate(int region)
        {
            Data dd = d;
            dd.region = region; dd.nodes.Clear(); dd.at = 0; dd.fight = -1; dd.roadsVer = 2;
            if (dd.travelled == null) dd.travelled = new List<int>();
            dd.travelled.Clear(); dd.travelled.Add(0);
            if (dd.seen == null) dd.seen = new List<Vector2>();
            dd.tower = -1; dd.towerLit = false; dd.towerTold = false;
            dd.hunt = -1; dd.huntDone = false; dd.huntReward = 0;
            dd.seen.Clear();   // новая область — снова в тумане (первая точка — начало пути, см. Seen)
            Vector2[][] spots = Spots[Mathf.Clamp(region, 0, Spots.Length - 1)];
            int L = spots.Length;
            List<int>[] layers = new List<int>[L];
            for (int l = 0; l < L; l++)
            {
                layers[l] = new List<int>();
                Vector2[] c = spots[l];
                List<int> pick = new List<int>();
                for (int i = 0; i < c.Length; i++) pick.Add(i);
                if (c.Length > 1 && l > 1 && l < L - 2 && UnityEngine.Random.value < 0.18f) pick.RemoveAt(UnityEngine.Random.Range(0, pick.Count));   // иногда дорога сходится в одну точку
                foreach (int i in pick)
                {
                    // Места стоят точно на нарисованных дорогах (tools/map_roads.json): без случайного сдвига,
                    // иначе последний отрезок пути съезжает с тропы.
                    Vector2 p = c[i];
                    layers[l].Add(dd.nodes.Count);
                    dd.nodes.Add(new MapNode { layer = l, x = p.x, y = p.y });
                }
            }
            // дороги: каждый узел ведёт хотя бы в один следующий, в каждый узел ведёт хотя бы одна дорога; иногда — перекрёсток
            for (int l = 0; l < L - 1; l++)
            {
                List<int> A = layers[l], B = layers[l + 1];
                bool[] hasIn = new bool[B.Count];
                for (int i = 0; i < A.Count; i++)
                {
                    int j = A.Count == 1 ? -1 : Mathf.RoundToInt(i * (B.Count - 1) / (float)(A.Count - 1));
                    if (j < 0) { for (int k = 0; k < B.Count; k++) { dd.nodes[A[i]].next.Add(B[k]); hasIn[k] = true; } continue; }
                    dd.nodes[A[i]].next.Add(B[j]); hasIn[j] = true;
                }
                for (int k = 0; k < B.Count; k++)
                {
                    if (hasIn[k]) continue;
                    int i = B.Count == 1 ? 0 : Mathf.RoundToInt(k * (A.Count - 1) / (float)(B.Count - 1));
                    dd.nodes[A[i]].next.Add(B[k]);
                }
                if (A.Count > 1 && B.Count > 1 && UnityEngine.Random.value < 0.55f)
                {
                    int i = UnityEngine.Random.Range(0, A.Count), k = Mathf.Clamp(i + (UnityEngine.Random.value < 0.5f ? 1 : -1), 0, B.Count - 1);
                    if (!dd.nodes[A[i]].next.Contains(B[k])) dd.nodes[A[i]].next.Add(B[k]);
                }
            }
            // что в узлах
            for (int l = 0; l < L; l++)
            {
                foreach (int ni in layers[l])
                {
                    MapNode n = dd.nodes[ni];
                    if (l == 0) { n.type = (int)NodeType.Start; n.done = true; continue; }
                    if (l == L - 1) { n.type = (int)NodeType.Boss; n.enemy = Mathf.Clamp(region + 1, 0, Ladder.Count - 1); continue; }
                    n.type = (int)RollType(l, L);
                    n.enemy = DuelEnemy(region);
                    if (n.Type == NodeType.Elite) n.enemy = Mathf.Clamp(region, 0, Ladder.Count - 1);
                    // кот-хранитель — редкое событие (~1 из 15), остальные равновероятны
                    if (n.Type == NodeType.Event)
                    {
                        // кот-хранитель ~7%, встречи со спутниками ~12% (не тот, что уже с героем), остальное — обычные события
                        float r = UnityEngine.Random.value;
                        int comp = UnityEngine.Random.Range(0, 3);
                        n.ev = r < 0.07f ? MapScreen.CatEvent
                            : r < 0.19f && comp != Profile.Companion ? MapScreen.CompanionEvent + comp
                            : MapScreen.RandomEvent();
                    }
                }
            }
            // отряды: иногда на дороге ждут двое-трое — бьются по очереди (второй вступает, как только пал первый)
            foreach (MapNode sq in dd.nodes)
            {
                if (sq.extra == null) sq.extra = new List<int>();
                sq.extra.Clear();
                if ((sq.Type != NodeType.Duel && sq.Type != NodeType.Ambush && sq.Type != NodeType.Elite) || sq.layer < 2) continue;
                float rr = UnityEngine.Random.value;
                if (rr < 0.2f) sq.extra.Add(DuelEnemy(region));
                if (rr < 0.2f && region >= 1 && UnityEngine.Random.value < 0.35f) sq.extra.Add(DuelEnemy(region));
            }
            // одна и та же услуга (кузница, привал, торговец, храм) не стоит дважды подряд на одной дороге — второе место становится боем или событием
            for (int pass = 0; pass < 2; pass++)
                foreach (MapNode a in dd.nodes)
                    foreach (int bi in a.next)
                    {
                        MapNode b = dd.nodes[bi];
                        NodeType ta = a.Type;
                        bool service = ta == NodeType.Smith || ta == NodeType.Rest || ta == NodeType.Merchant || ta == NodeType.Shrine
                            || ta == NodeType.Onsen || ta == NodeType.Dojo || ta == NodeType.Gamble || ta == NodeType.ClanShrine;
                        if (!service || b.Type != ta) continue;
                        b.type = (int)(UnityEngine.Random.value < 0.6f ? NodeType.Duel : NodeType.Event);
                        b.enemy = DuelEnemy(region);
                        if (b.Type == NodeType.Event) b.ev = MapScreen.RandomEvent();
                    }
            // перед владыкой — хотя бы одна передышка (храм или привал), если есть выбор
            List<int> pre = layers[L - 2];
            if (pre.Count > 1)
            {
                bool rest = false;
                foreach (int ni in pre) { NodeType t = dd.nodes[ni].Type; if (t == NodeType.Shrine || t == NodeType.Rest) rest = true; }
                if (!rest) dd.nodes[pre[UnityEngine.Random.Range(0, pre.Count)]].type = (int)(UnityEngine.Random.value < 0.5f ? NodeType.Shrine : NodeType.Rest);
            }
            // хижина травницы посреди области — сюда откидывает после поражения (иначе — к началу пути)
            List<int> mid = layers[L / 2];
            dd.nodes[mid[UnityEngine.Random.Range(0, mid.Count)]].type = (int)NodeType.Herbalist;
            // трактиры: половина привалов — придорожные трактиры (ev = TavernEv); в каждой области хотя бы один
            {
                int tav = 0;
                foreach (MapNode tn in dd.nodes) if (tn.Type == NodeType.Rest && UnityEngine.Random.value < 0.5f) { tn.ev = MapScreen.TavernEv; tav++; }
                if (tav == 0)
                {
                    int pickT = -1;
                    for (int ti = 0; ti < dd.nodes.Count && pickT < 0; ti++) if (dd.nodes[ti].Type == NodeType.Rest) pickT = ti;
                    for (int ti = 0; ti < dd.nodes.Count && pickT < 0; ti++)
                        if (dd.nodes[ti].Type == NodeType.Duel && dd.nodes[ti].layer >= 2 && dd.nodes[ti].layer <= L - 3 && dd.nodes[ti].extra.Count == 0) pickT = ti;
                    if (pickT >= 0) { dd.nodes[pickT].type = (int)NodeType.Rest; dd.nodes[pickT].ev = MapScreen.TavernEv; }
                }
            }
            // кот-хранитель: в первой области похода он встречается всегда (если там есть событие)
            bool catHere = false; List<int> catSpots = new List<int>();
            for (int ci = 0; ci < dd.nodes.Count; ci++)
                if (dd.nodes[ci].Type == NodeType.Event) { catSpots.Add(ci); if (dd.nodes[ci].ev == MapScreen.CatEvent) catHere = true; }
            if (!catHere && catSpots.Count > 0 && (region == 0 || UnityEngine.Random.value < 0.35f))
                dd.nodes[catSpots[UnityEngine.Random.Range(0, catSpots.Count)]].ev = MapScreen.CatEvent;
            // спутник: в первой области похода встреча с ним есть всегда (если спутника ещё нет), дальше — как выпадет
            if (Profile.Companion < 0 && (region == 0 || UnityEngine.Random.value < 0.5f))
            {
                bool compHere = false; int spot = -1;
                for (int ci = 0; ci < dd.nodes.Count; ci++)
                {
                    MapNode cn = dd.nodes[ci];
                    if (cn.Type == NodeType.Event && cn.ev >= MapScreen.CompanionEvent && cn.ev < MapScreen.CompanionEvent + 3) compHere = true;
                    else if (cn.Type == NodeType.Event && cn.ev != MapScreen.CatEvent && spot < 0) spot = ci;
                }
                if (!compHere && spot < 0)   // свободного события нет — обычный бой в середине пути становится встречей
                    for (int ci = 0; ci < dd.nodes.Count && spot < 0; ci++)
                        if (dd.nodes[ci].Type == NodeType.Duel && dd.nodes[ci].layer >= 2 && dd.nodes[ci].layer <= L - 3) spot = ci;
                if (!compHere && spot >= 0)
                {
                    dd.nodes[spot].type = (int)NodeType.Event;
                    dd.nodes[spot].ev = MapScreen.CompanionEvent + UnityEngine.Random.Range(0, 3);
                }
            }
            // стоянка охотников за головами: одна на область, в начале пути (заказ на бродячего ронина)
            {
                List<int> hc = new List<int>();
                for (int ci = 0; ci < dd.nodes.Count; ci++)
                {
                    MapNode cn = dd.nodes[ci];
                    if ((cn.layer == 1 || cn.layer == 2) && (cn.Type == NodeType.Duel || cn.Type == NodeType.Event && MapScreen.PlainEvent(cn.ev)) && (cn.extra == null || cn.extra.Count == 0)) hc.Add(ci);
                }
                if (hc.Count > 0) dd.nodes[hc[UnityEngine.Random.Range(0, hc.Count)]].type = (int)NodeType.HunterCamp;
            }
            // раненый ронин, которого перевязали в прошлой области, встречается снова
            if (dd.roninDebt && PlaceRoninReturn(dd, 0)) dd.roninDebt = false;
            dd.steps = 0; dd.rolls = Mathf.Max(dd.rolls, 1);
            Save();
        }

        /// <summary>Встреча с ронином, которого ты перевязал: 65% — возвращает долг, 35% — предаёт (с бандой).
        /// Ставится на ещё не пройденное событие или бой не ближе двух рядов впереди.</summary>
        static bool PlaceRoninReturn(Data dd, int fromLayer)
        {
            int L = 0; foreach (MapNode n in dd.nodes) L = Mathf.Max(L, n.layer + 1);
            List<int> pick = new List<int>();
            for (int i = 0; i < dd.nodes.Count; i++)
            {
                MapNode n = dd.nodes[i];
                if (n.done || i == dd.at || n.layer < fromLayer + 2 || n.layer < 2 || n.layer > L - 2) continue;
                if ((n.Type == NodeType.Event && MapScreen.PlainEvent(n.ev)) || (n.Type == NodeType.Duel && (n.extra == null || n.extra.Count == 0))) pick.Add(i);
            }
            if (pick.Count == 0) return false;
            MapNode m = dd.nodes[pick[UnityEngine.Random.Range(0, pick.Count)]];
            m.type = (int)NodeType.Event;
            m.ev = UnityEngine.Random.value < 0.65f ? MapScreen.RoninRepays : MapScreen.RoninBetrays;
            return true;
        }
        /// <summary>Ронин перевязан: дальше по дороге (или в следующей области) он встретится снова.</summary>
        public static void RoninOwes()
        {
            if (!PlaceRoninReturn(D, Current.layer)) D.roninDebt = true;
            Save();
        }

        static int DuelEnemy(int region)
        {
            if (region <= 0) return 0;
            if (region == 1) return UnityEngine.Random.value < 0.5f ? 0 : 1;
            return UnityEngine.Random.value < 0.5f ? 1 : 2;
        }

        static NodeType RollType(int l, int L)
        {
            if (l == 1) { float r = UnityEngine.Random.value; return r < 0.7f ? NodeType.Duel : r < 0.85f ? NodeType.Event : NodeType.Treasure; }
            NodeType[] t = { NodeType.Duel, NodeType.Elite, NodeType.Shrine, NodeType.Smith, NodeType.Merchant, NodeType.Treasure, NodeType.Ambush, NodeType.Event, NodeType.Rest,
                             NodeType.Onsen, NodeType.Dojo, NodeType.Gamble, NodeType.ClanShrine };
            float[] w = { 30f, l >= 3 ? 13f : 0f, 9f, 8f, 8f, 7f, 8f, 14f, 7f, 5f, l >= 2 ? 5f : 0f, 4f, l >= 2 ? 4f : 0f };
            float sum = 0f; foreach (float x in w) sum += x;
            float v = UnityEngine.Random.value * sum;
            for (int i = 0; i < t.Length; i++) { if (v < w[i]) return t[i]; v -= w[i]; }
            return NodeType.Duel;
        }

        // ---------------- движение и бой ----------------

        /// <summary>Герой пришёл в узел (по дороге из текущего).</summary>
        /// <summary>Место-услуга (храм, кузнец, торговец, привал/трактир, травница): его можно проехать насквозь.</summary>
        public static bool IsService(MapNode n)
        {
            NodeType t = n.Type;
            return t == NodeType.Shrine || t == NodeType.Smith || t == NodeType.Merchant || t == NodeType.Rest || t == NodeType.Herbalist
                || t == NodeType.Onsen || t == NodeType.Dojo || t == NodeType.Gamble || t == NodeType.ClanShrine || t == NodeType.HunterCamp;
        }

        /// <param name="passing">проезд насквозь к другой цели: услуга не тратится (не «пройдена», травница не дарит кость) — можно вернуться</param>
        public static void MoveTo(int i, bool passing = false)
        {
            // Проверяем маршрут и доступные ходы здесь, а не только в UI.
            // Иначе прямой вызов MoveTo обходит ограничения похода.
            if (!CanGo(i)) return;
            D.at = i;
            if (D.travelled == null) D.travelled = new List<int>();
            D.travelled.Add(i);
            // Keep the footprint trail bounded, while retaining the current node at the tail.
            if (D.travelled.Count > 48) D.travelled.RemoveRange(0, D.travelled.Count - 48);
            MapNode n = D.nodes[i];
            if (!n.done) D.steps = Mathf.Max(0, D.steps - 1);   // по пройденным местам — без траты хода
            if (n.IsFight && !n.done) { D.heldSteps = D.steps; D.steps = 0; }   // бой прерывает ход: остаток ждёт исхода боя
            bool skip = passing && IsService(n);
            if (n.Type == NodeType.Herbalist && !n.done && !skip) D.rolls++;   // травница даёт кость пути
            if (!n.IsFight && n.Type != NodeType.Event && !skip) n.done = true;   // храм, кузнец, торговец, тайник, привал — услуга, путь дальше открыт
            Save();
        }

        public static void Complete(int i) { if (i >= 0 && i < D.nodes.Count) { D.nodes[i].done = true; Save(); } }

        /// <summary>Разбойники из события: встреча становится засадой.</summary>
        public static void TurnToAmbush(int i)
        {
            MapNode n = D.nodes[i];
            n.type = (int)NodeType.Ambush; n.enemy = DuelEnemy(D.region); Save();
        }

        /// <summary>Бой-реванш: повторный бой на пройденном месте, где стоит герой. Монеты вполовину, без добычи и костей пути.</summary>
        public static bool CanRematch { get { MapNode c = Current; return !InFight && !D.training && c.done && c.IsFight && c.Type != NodeType.Boss; } }
        public static bool Rematch { get { return InFight && D.rematch; } }
        public static void BeginRematch() { if (!CanRematch) return; BeginFight(D.at, true); }

        /// <summary>Сколько противников отряда ещё ждут своей очереди.</summary>
        public static int FoesLeft { get { MapNode n = FightNode; return n == null || n.extra == null ? 0 : Mathf.Max(0, n.extra.Count - D.foe); } }
        public static int FoeIndex { get { return InFight ? D.foe : 0; } }
        /// <summary>Следующий противник отряда выходит на арену: его ступень Ladder (или −1, если очередь пуста).</summary>
        public static int NextFoe()
        {
            if (FoesLeft <= 0) return -1;
            int st = FightNode.extra[D.foe]; D.foe++; Save();
            return st;
        }
        public static string SquadText(MapNode n)
        {
            if (n == null || n.extra == null || n.extra.Count == 0) return "";
            string s = "Отряд: после него выйдут ";
            for (int i = 0; i < n.extra.Count; i++) s += (i > 0 ? ", " : "") + Ladder.Stages[Mathf.Clamp(n.extra[i], 0, Ladder.Count - 1)].name;
            return s + " — по одному раунду, без передышки.";
        }

        // ---- отладка (меню разработчика на карте)
        /// <summary>Мгновенно перенести героя на место i (без хода и проверок дороги).</summary>
        public static void DebugTeleport(int i)
        {
            if (i < 0 || i >= D.nodes.Count) return;
            D.at = i; D.fight = -1; D.rematch = false;
            if (D.travelled == null) D.travelled = new List<int>();
            D.travelled.Add(i);
            if (D.seen == null) D.seen = new List<Vector2>();
            D.seen.Add(new Vector2(D.nodes[i].x, D.nodes[i].y));
            MapNode n = D.nodes[i];
            if (!n.IsFight && n.Type != NodeType.Event) n.done = true;
            Save();
        }
        public static void DebugSteps(int n) { D.steps = n; D.lastRoll = n; Save(); }
        public static void DebugNextRegion() { Generate((D.region + 1) % RegionNames.Length); }
        public static void DebugSkipSquad() { MapNode n = FightNode; if (n != null && n.extra != null) { D.foe = n.extra.Count; Save(); } }

        public static void BeginFight(int i, bool rematch = false)
        {
            D.rematch = rematch; D.foe = 0;
            D.fight = i;
            MapNode n = D.nodes[i];
            Ladder.Index = n.enemy;
            Weather.ForceNight = n.Type == NodeType.Ambush;
            Save();
        }

        /// <summary>Бой в узле выигран: узел пройден; после владыки — следующая область. true — область сменилась.</summary>
        public static bool FinishFight()
        {
            if (D.training)
            {
                int kind = D.trainKind; D.training = false; D.trainKind = 0;
                if (kind == 1) { Profile.GivePoint(); D.note = "Сэнсэй опускает боккэн и кланяется: «Ты слушаешь клинок». +1 очко характеристик."; }
                else if (kind == 2)
                {
                    Profile.AddGold(D.huntReward);
                    D.note = "Голова ронина — твоя. Охотники платят по листку: +" + D.huntReward + " мон.";
                    D.hunt = -1; D.huntDone = true; D.huntReward = 0;
                }
                else D.rolls++;   // ронин побеждён — кость пути
                Save(); return false;
            }
            if (!InFight) return false;
            if (D.rematch) { D.rematch = false; D.fight = -1; Weather.ForceNight = false; Save(); return false; }   // реванш: место уже пройдено
            D.rolls++;   // победа — кость пути
            MapNode n = D.nodes[D.fight];
            if (D.bounty > 0 && (n.Type == NodeType.Elite || n.Type == NodeType.Boss)) { Profile.AddGold(n.Type == NodeType.Boss ? D.bounty * 2 : D.bounty); D.bounty = 0; }   // заказ из трактира выполнен
            n.done = true; D.at = D.fight; D.fight = -1;
            D.steps += D.heldSteps; D.heldSteps = 0;   // победа: недоиспользованные шаги хода возвращаются
            D.blessing = 0; D.cursed = false; TickCat();
            Weather.ForceNight = false;
            if (n.Type == NodeType.Boss)
            {
                D.clearedRegion = D.region;
                Generate((D.region + 1) % RegionNames.Length);
                return true;
            }
            Save();
            return false;
        }

        /// <summary>
        /// Бой проигран: героя откидывает к хижине травницы минимум за два слоя позади (или к началу области). Благословение пропадает,
        /// проклятие снято, ходы сгорают; если костей нет — травница даёт одну, чтобы не застрять.
        /// </summary>
        public static void LoseFight()
        {
            if (D.training)
            {   // бой вне места проигран — просто назад на карту; ронин в розыске уходит дальше
                int kind = D.trainKind; D.training = false; D.trainKind = 0;
                if (kind == 1) D.note = "Ученик выбивает боккэн у тебя из рук. Сэнсэй: «Приходи, когда научишься падать».";
                if (kind == 2) { MoveHunt(1f); D.note = "Ронин вытирает клинок и уходит в туман. Заказ ещё в силе — он где-то на дорогах."; }
                Save(); return;
            }
            if (!InFight) return;
            if (D.rematch) { D.rematch = false; D.fight = -1; Weather.ForceNight = false; Save(); return; }   // реванш проигран — просто назад на карту
            MapNode lost = D.nodes[D.fight];
            int cp = 0;
            for (int i = 0; i < D.nodes.Count; i++)
            {
                MapNode n = D.nodes[i];
                if (n.Type == NodeType.Herbalist && n.layer <= lost.layer - 2 && n.layer > D.nodes[cp].layer) cp = i;
            }
            D.at = cp; D.nodes[cp].done = true; D.fight = -1; D.steps = 0; D.heldSteps = 0;
            // Retreat is an instant checkpoint jump, not a walked road segment.
            // Clear previous marks so the map cannot draw an imaginary shortcut.
            if (D.travelled == null) D.travelled = new List<int>();
            D.travelled.Clear(); D.travelled.Add(cp);
            if (D.seen != null && D.seen.Count > 0) D.seen.Add(new Vector2(D.nodes[cp].x, D.nodes[cp].y));   // очнулся у травницы — вокруг неё видно
            D.blessing = 0; D.cursed = false; D.knocked = true; TickCat();
            if (D.rolls <= 0) D.rolls = 1; // emergency die: defeat must never soft-lock travel
            Weather.ForceNight = false;
            Save();
        }

        public static void SetBlessing(int b) { D.blessing = b; Save(); }
        public static void LiftCurse() { D.cursed = false; Save(); }
        public static bool HasBounty { get { return D.bounty > 0; } }
        public static void TakeBounty(int reward) { D.bounty = reward; Save(); }

        // ---- кот-хранитель: одно благословение на 3 боя похода (новое заменяет старое, не копится)
        public static bool CatBlessed { get { return D.catBuff > 0 && D.catFights > 0; } }
        public static void SetCatBlessing(int type) { D.catBuff = type; D.catFights = 3; D.catLifeUsed = false; Save(); }
        public static bool CatLuck { get { return InFight && D.catBuff == 1 && D.catFights > 0; } }
        public static bool CatNineLives { get { return InFight && D.catBuff == 2 && D.catFights > 0 && !D.catLifeUsed; } }
        public static void UseCatLife() { D.catLifeUsed = true; D.catBuff = 0; D.catFights = 0; Save(); }
        static void TickCat() { if (D.catFights > 0 && --D.catFights == 0) D.catBuff = 0; }
        public static void Curse() { D.cursed = true; Save(); }

        // ---------------- модификаторы текущего боя (DuelManager) ----------------

        public static NodeType FightType { get { MapNode n = FightNode; return n != null ? n.Type : NodeType.Duel; } }
        public static float EnemyHpMul { get { NodeType t = FightType; return Hunting ? 1.35f : !InFight ? 1f : t == NodeType.Elite ? 1.35f : t == NodeType.Ambush ? 1.1f : 1f; } }
        public static float EnemyDmgMul { get { return Hunting || (InFight && FightType == NodeType.Elite) ? 1.15f : 1f; } }
        public static int ExtraMarks { get { return Hunting || (InFight && FightType == NodeType.Elite) ? 1 : 0; } }
        public static float PlayerDmgMul { get { return D.blessing == 1 ? 1.2f : 1f; } }
        public static float PlayerHpMul { get { return (D.blessing == 3 ? 1.25f : 1f) * (D.cursed ? 0.75f : 1f); } }
        public static bool StartFullKi { get { return D.blessing == 2; } }
        public static bool EnemyFullKi { get { return InFight && FightType == NodeType.Ambush; } }
        public static bool RareDrop { get { NodeType t = FightType; return Hunting || (InFight && (t == NodeType.Elite || t == NodeType.Boss)); } }
        public static bool FinalBoss { get { return InFight && FightType == NodeType.Boss && D.region >= RegionNames.Length - 1; } }
        public static float GoldMul
        {
            get
            {
                NodeType t = FightType;
                float m = Hunting ? 1.5f : D.training ? 0.5f : !InFight ? 1f : t == NodeType.Ambush ? 1.5f : t == NodeType.Elite ? 1.3f : 1f;
                MapNode n = FightNode;
                if (InFight && n != null && n.extra != null) m *= 1f + 0.4f * n.extra.Count;   // отряд — больше мон
                return m;
            }
        }

        public static string BlessingText
        {
            get
            {
                string s = D.blessing == 1 ? "Благословение клинка: +20% урона" : D.blessing == 2 ? "Благословение духа: бой с полным духом" : D.blessing == 3 ? "Благословение жизни: +25% здоровья" : "";
                if (D.cursed) s += (s.Length > 0 ? "\n" : "") + "Проклятие лисы: −25% здоровья";
                if (CatBlessed) s += (s.Length > 0 ? "\n" : "") + (D.catBuff == 1 ? "Кошачья удача: 10% ударов мимо" : "Девять жизней: смертельный удар оставит 1 здоровья") + " (боёв: " + D.catFights + ")";
                if (D.bounty > 0) s += (s.Length > 0 ? "\n" : "") + "Заказ на голову: " + D.bounty + " мон за сильного врага";
                return s;
            }
        }
    }
}
