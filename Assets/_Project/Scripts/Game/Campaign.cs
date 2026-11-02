using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    public enum NodeType { Start, Duel, Elite, Boss, Shrine, Smith, Merchant, Treasure, Ambush, Event, Rest, Herbalist }

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
        public NodeType Type { get { return (NodeType)type; } }
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
            public int rolls = 1;            // кости пути в запасе (кость — за каждую победу)
            public int steps;                // ходов осталось после броска
            public int lastRoll;             // что выпало
            public bool training;            // бой со странствующим ронином (запасной способ добыть кость)
            public bool knocked;             // проиграл — очнулся у травницы (плашка на карте)
            public List<MapNode> nodes = new List<MapNode>();
            // Only actual moves, never inferred from completed nodes. Persisted across restarts.
            public List<int> travelled = new List<int>();
        }

        public static readonly string[] RegionNames = { "ЛЕС ТЕНЕЙ", "ГОРЫ ОНИ", "ПРОКЛЯТЫЙ СЁГУНАТ" };
        public static readonly string[] TypeNames = { "НАЧАЛО ПУТИ", "ДУЭЛЬ", "СИЛЬНЫЙ ПРОТИВНИК", "ВЛАДЫКА ОБЛАСТИ", "ХРАМ", "КУЗНЕЦ", "ТОРГОВЕЦ", "ТАЙНИК", "ЗАСАДА", "СОБЫТИЕ", "ПРИВАЛ", "ХИЖИНА ТРАВНИЦЫ" };
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
            return D.nodes[to].done && D.nodes[to].next.Contains(from);
        }
        /// <summary>Шаг на уже пройденное место (назад или по своим следам) — бесплатный: кость тратится только на новые места.</summary>
        public static bool FreeHop(int i) { return i >= 0 && i < D.nodes.Count && D.nodes[i].done; }
        public static bool CanGo(int i) { return i >= 0 && i < D.nodes.Count && Current.done && Adjacent(D.at, i) && !InFight && !D.training && (D.steps > 0 || FreeHop(i)); }
        public static int Rolls { get { return D.rolls; } }
        public static int Steps { get { return D.steps; } }
        public static int LastRoll { get { return D.lastRoll; } }
        public static bool Training { get { return D.training; } }
        public static bool Knocked { get { return D.knocked; } }
        public static void AckKnocked() { D.knocked = false; Save(); }
        public static bool Stuck { get { return D.rolls <= 0 && D.steps <= 0 && Current.done && !InFight; } }
        public static int GuideCost { get { return Mathf.RoundToInt((40 + 25 * D.region) * (1f + 0.4f * Profile.Cycle)); } }

        /// <summary>Бросок кости пути: 1–3 хода. Кость тратится.</summary>
        public static int RollDie()
        {
            if (D.rolls <= 0 || D.steps > 0 || !Current.done || InFight || D.training) return 0;
            D.rolls--; D.lastRoll = UnityEngine.Random.Range(1, 4); D.steps = D.lastRoll; Save();
            return D.lastRoll;
        }
        public static void AddRoll(int n = 1) { if (n <= 0) return; D.rolls += n; Save(); }
        public static void CancelTraining() { if (D.training) { D.training = false; Save(); } }
        /// <summary>Проводник: кость за моны.</summary>
        public static bool BuyGuide() { if (!Profile.Spend(GuideCost)) return false; AddRoll(); return true; }
        /// <summary>Вызвать странствующего ронина: бой вне узла, победа — кость пути.</summary>
        public static void BeginTraining()
        {
            if (InFight || D.training || !Current.done) return;
            D.training = true; D.fight = -1;
            Ladder.Index = DuelEnemy(D.region);
            Weather.ForceNight = false;
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
                            : UnityEngine.Random.Range(0, MapScreen.CatEvent);
                    }
                }
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
            // кот-хранитель: в первой области похода он встречается всегда (если там есть событие)
            bool catHere = false; List<int> catSpots = new List<int>();
            for (int ci = 0; ci < dd.nodes.Count; ci++)
                if (dd.nodes[ci].Type == NodeType.Event) { catSpots.Add(ci); if (dd.nodes[ci].ev == MapScreen.CatEvent) catHere = true; }
            if (!catHere && catSpots.Count > 0 && (region == 0 || UnityEngine.Random.value < 0.35f))
                dd.nodes[catSpots[UnityEngine.Random.Range(0, catSpots.Count)]].ev = MapScreen.CatEvent;
            dd.steps = 0; dd.rolls = Mathf.Max(dd.rolls, 1);
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
            NodeType[] t = { NodeType.Duel, NodeType.Elite, NodeType.Shrine, NodeType.Smith, NodeType.Merchant, NodeType.Treasure, NodeType.Ambush, NodeType.Event, NodeType.Rest };
            float[] w = { 30f, l >= 3 ? 13f : 0f, 11f, 8f, 9f, 7f, 8f, 12f, 9f };
            float sum = 0f; foreach (float x in w) sum += x;
            float v = UnityEngine.Random.value * sum;
            for (int i = 0; i < t.Length; i++) { if (v < w[i]) return t[i]; v -= w[i]; }
            return NodeType.Duel;
        }

        // ---------------- движение и бой ----------------

        /// <summary>Герой пришёл в узел (по дороге из текущего).</summary>
        public static void MoveTo(int i)
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
            if (n.IsFight && !n.done) D.steps = 0;   // бой обрывает ход: остаток сгорает
            if (n.Type == NodeType.Herbalist && !n.done) D.rolls++;   // травница даёт кость пути
            if (!n.IsFight && n.Type != NodeType.Event) n.done = true;   // храм, кузнец, торговец, тайник, привал — услуга, путь дальше открыт
            Save();
        }

        public static void Complete(int i) { if (i >= 0 && i < D.nodes.Count) { D.nodes[i].done = true; Save(); } }

        /// <summary>Разбойники из события: встреча становится засадой.</summary>
        public static void TurnToAmbush(int i)
        {
            MapNode n = D.nodes[i];
            n.type = (int)NodeType.Ambush; n.enemy = DuelEnemy(D.region); Save();
        }

        public static void BeginFight(int i)
        {
            D.fight = i;
            MapNode n = D.nodes[i];
            Ladder.Index = n.enemy;
            Weather.ForceNight = n.Type == NodeType.Ambush;
            Save();
        }

        /// <summary>Бой в узле выигран: узел пройден; после владыки — следующая область. true — область сменилась.</summary>
        public static bool FinishFight()
        {
            if (D.training) { D.training = false; D.rolls++; Save(); return false; }   // ронин побеждён — кость пути
            if (!InFight) return false;
            D.rolls++;   // победа — кость пути
            MapNode n = D.nodes[D.fight];
            n.done = true; D.at = D.fight; D.fight = -1;
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
            if (D.training) { D.training = false; Save(); return; }   // ронину проиграл — просто назад на карту, без кости
            if (!InFight) return;
            MapNode lost = D.nodes[D.fight];
            int cp = 0;
            for (int i = 0; i < D.nodes.Count; i++)
            {
                MapNode n = D.nodes[i];
                if (n.Type == NodeType.Herbalist && n.layer <= lost.layer - 2 && n.layer > D.nodes[cp].layer) cp = i;
            }
            D.at = cp; D.nodes[cp].done = true; D.fight = -1; D.steps = 0;
            // Retreat is an instant checkpoint jump, not a walked road segment.
            // Clear previous marks so the map cannot draw an imaginary shortcut.
            if (D.travelled == null) D.travelled = new List<int>();
            D.travelled.Clear(); D.travelled.Add(cp);
            D.blessing = 0; D.cursed = false; D.knocked = true; TickCat();
            if (D.rolls <= 0) D.rolls = 1; // emergency die: defeat must never soft-lock travel
            Weather.ForceNight = false;
            Save();
        }

        public static void SetBlessing(int b) { D.blessing = b; Save(); }

        // ---- кот-хранитель: одно благословение на 3 боя похода (новое заменяет старое, не копится)
        public static bool CatBlessed { get { return D.catBuff > 0 && D.catFights > 0; } }
        public static void SetCatBlessing(int type) { D.catBuff = type; D.catFights = 3; D.catLifeUsed = false; Save(); }
        public static bool CatLuck { get { return InFight && D.catBuff == 1 && D.catFights > 0; } }
        public static bool CatNineLives { get { return InFight && D.catBuff == 2 && D.catFights > 0 && !D.catLifeUsed; } }
        public static void UseCatLife() { D.catLifeUsed = true; D.catBuff = 0; D.catFights = 0; Save(); }
        static void TickCat() { if (D.catFights > 0 && --D.catFights == 0) D.catBuff = 0; }
        public static void Curse() { D.cursed = true; Save(); }

        // ---------------- модификаторы текущего боя (DuelManager) ----------------

        static NodeType FightType { get { MapNode n = FightNode; return n != null ? n.Type : NodeType.Duel; } }
        public static float EnemyHpMul { get { NodeType t = FightType; return !InFight ? 1f : t == NodeType.Elite ? 1.35f : t == NodeType.Ambush ? 1.1f : 1f; } }
        public static float EnemyDmgMul { get { return InFight && FightType == NodeType.Elite ? 1.15f : 1f; } }
        public static int ExtraMarks { get { return InFight && FightType == NodeType.Elite ? 1 : 0; } }
        public static float PlayerDmgMul { get { return D.blessing == 1 ? 1.2f : 1f; } }
        public static float PlayerHpMul { get { return (D.blessing == 3 ? 1.25f : 1f) * (D.cursed ? 0.75f : 1f); } }
        public static bool StartFullKi { get { return D.blessing == 2; } }
        public static bool EnemyFullKi { get { return InFight && FightType == NodeType.Ambush; } }
        public static bool RareDrop { get { NodeType t = FightType; return InFight && (t == NodeType.Elite || t == NodeType.Boss); } }
        public static bool FinalBoss { get { return InFight && FightType == NodeType.Boss && D.region >= RegionNames.Length - 1; } }
        public static float GoldMul { get { NodeType t = FightType; return D.training ? 0.5f : !InFight ? 1f : t == NodeType.Ambush ? 1.5f : t == NodeType.Elite ? 1.3f : 1f; } }

        public static string BlessingText
        {
            get
            {
                string s = D.blessing == 1 ? "Благословение клинка: +20% урона" : D.blessing == 2 ? "Благословение духа: бой с полным духом" : D.blessing == 3 ? "Благословение жизни: +25% здоровья" : "";
                if (D.cursed) s += (s.Length > 0 ? "\n" : "") + "Проклятие лисы: −25% здоровья";
                if (CatBlessed) s += (s.Length > 0 ? "\n" : "") + (D.catBuff == 1 ? "Кошачья удача: 10% ударов мимо" : "Девять жизней: смертельный удар оставит 1 здоровья") + " (боёв: " + D.catFights + ")";
                return s;
            }
        }
    }
}
