using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Пошаговый бой отрядов (эталон — «Герои» 3): поле 15×11 шестиугольников, отряды-стопки с числом бойцов, очередь хода
    /// по скорости, ближний и дальний бой, ответный удар раз в раунд, «ждать» и «защита», герой вне поля колдует за дух
    /// (одно умение за раунд). Пока — отдельный режим меню разработчика (MapScreen → «ПОШАГОВЫЙ БОЙ»); арена не тронута.
    /// Бойцы на поле — снимки наших 3D-моделей (EnemySkins / PlayerSkins) через камеру на магенте.
    /// </summary>
    public static class Tactics
    {
        public static bool Open { get; private set; }

        // ---------------------------------------------------------------- бойцы

        public class UnitType
        {
            public string name; public int atk, def, dmin, dmax, hp, spd, shots;
            public bool noRetal;            // по нему не отвечают (синоби, клан теней)
            public bool magic;              // дальний удар без штрафа за расстояние
            // способности («Герои 3»): двойной удар, бесконечные ответы, разгон (+5% урона за клетку пути), стрелок без штрафа вплотную,
            // удар по всем соседним врагам, пробой доспеха (доля защиты цели не в счёт)
            public bool dbl, unlimRet, joust, noMeleePen, sweep; public float pierce;
            public bool fly;                // летает («Герои 3»): над препятствиями, отрядами и стенами
            public bool undead;             // нежить: без боевого духа, не боится проклятий и страха
            public float resist;            // сопротивление умениям (доля)
            public int fireImmune;          // 1 — огонь не берёт (огненный талисман, гром)
            public int atlasCell = -1;      // портрет из атласа нейтралов UI/H3/neutrals (5×4)
            public bool wide;               // большое существо — две клетки («Герои 3»): голова и круп/хвост позади
            public string Ability
            {
                get
                {
                    string a = "";
                    if (wide) a += " · две клетки"; if (fly) a += " · летает"; if (undead) a += " · нежить"; if (resist > 0f) a += " · сопротивление " + Mathf.RoundToInt(resist * 100f) + "%"; if (fireImmune > 0) a += " · огонь не берёт"; if (dbl) a += " · двойной удар"; if (unlimRet) a += " · отвечает всем"; if (joust) a += " · разгон";
                    if (noMeleePen) a += " · вплотную без штрафа"; if (sweep) a += " · бьёт всех рядом"; if (pierce > 0f) a += " · пробивает доспех";
                    if (noRetal) a += " · по нему не отвечают"; if (magic) a += " · бьёт чарами";
                    return a.Length > 0 ? a.Substring(3) : "";
                }
            }
            public string portrait;         // путь к портрету (свой — по ячейке атласа отрядов)
            public string live;             // живая 3D-модель с клипами Meshy: Resources/Units/<live> (+ <live>_albedo); нет — снимок
            public float liveH = 1.7f;      // рост на поле, м
            public int cell = -1;           // ячейка UI/Battle/unit_portraits (4×3)
            public bool up;                 // улучшенный отряд («Герои 3»): портрет из UI/Battle/unit_portraits2
            public int model;               // ≥0 — облик врага (EnemySkins), −1…−3 — облик странника (PlayerSkins 0…2), −99 — нет модели
            public UnitType(string n, int a, int d, int mn, int mx, int h, int s, int sh, int mdl) { name = n; atk = a; def = d; dmin = mn; dmax = mx; hp = h; spd = s; shots = sh; model = mdl; }
        }

        // наши отряды (портреты 1…12 атласа) и облики, на которых они пока стоят
        public static readonly UnitType[] Ours =
        {
            new UnitType("Асигару", 4, 5, 1, 3, 10, 4, 0, 8) { cell = 0, live = "unit_ashigaru" },
            new UnitType("Лучники", 6, 3, 2, 3, 10, 4, 12, -2) { cell = 1, live = "unit_archer" },
            new UnitType("Тэппо", 8, 3, 3, 5, 12, 3, 8, 6) { cell = 2, live = "unit_teppo", pierce = 0.4f },
            new UnitType("Самураи", 8, 8, 4, 6, 25, 5, 0, 0) { cell = 3, live = "unit_samurai", unlimRet = true },
            new UnitType("Конные самураи", 11, 9, 7, 11, 45, 8, 0, 3) { cell = 4, live = "unit_cavalry", liveH = 2.6f, joust = true, wide = true },
            new UnitType("Сохэи", 7, 7, 3, 6, 20, 5, 0, 7) { cell = 5, live = "unit_sohei", resist = 0.2f },
            new UnitType("Синоби", 9, 4, 3, 5, 15, 8, 0, 5) { cell = 6, noRetal = true, live = "unit_shinobi", liveH = 1.68f },
            new UnitType("Ронины", 7, 6, 3, 5, 18, 6, 0, 1) { cell = 7, live = "unit_ronin" },
            new UnitType("Онмёдзи", 9, 6, 4, 7, 20, 5, 10, -3) { cell = 8, magic = true, live = "unit_onmyoji", noMeleePen = true },
            new UnitType("Знаменосцы", 3, 6, 1, 2, 15, 5, 0, -1) { cell = 9, live = "unit_banner" },
            new UnitType("Хатамото", 13, 14, 10, 18, 80, 5, 0, 3) { cell = 10, live = "unit_hatamoto", liveH = 1.85f, dbl = true },
            new UnitType("Боевые псы", 6, 3, 2, 4, 12, 9, 0, -99) { cell = 11, live = "unit_dog", liveH = 0.9f, dbl = true },
            // улучшенные (12…23 — по порядку базовых; улучшаются в армии поместья, когда постройка отряда улучшена)
            new UnitType("Асигару-ветераны", 6, 7, 2, 4, 14, 5, 0, 8) { cell = 0, up = true, live = "unit_ashigaru2" },
            new UnitType("Мастера лука", 8, 4, 3, 5, 12, 5, 16, -2) { cell = 1, up = true, live = "unit_archer2", noMeleePen = true },
            new UnitType("Тэппо-гвардия", 10, 5, 4, 7, 16, 4, 10, 6) { cell = 2, up = true, live = "unit_teppo2", pierce = 0.6f },
            new UnitType("Самураи-ветераны", 11, 10, 6, 8, 32, 6, 0, 0) { cell = 3, up = true, live = "unit_samurai2", unlimRet = true },
            new UnitType("Тяжёлая конница", 14, 12, 9, 14, 60, 9, 0, 3) { cell = 4, up = true, live = "unit_cavalry2", liveH = 2.7f, joust = true, wide = true },
            new UnitType("Сохэи-мастера", 9, 9, 4, 8, 26, 6, 0, 7) { cell = 5, up = true, live = "unit_sohei2", sweep = true, resist = 0.4f },
            new UnitType("Мастера синоби", 12, 6, 4, 7, 18, 10, 0, 5) { cell = 6, up = true, live = "unit_shinobi2", noRetal = true, dbl = true },
            new UnitType("Ронины-мечники", 9, 8, 4, 7, 24, 7, 0, 1) { cell = 7, up = true, live = "unit_ronin2", unlimRet = true },
            new UnitType("Онмёдзи-архимаги", 12, 8, 6, 9, 26, 6, 14, -3) { cell = 8, up = true, magic = true, live = "unit_onmyoji2", noMeleePen = true },
            new UnitType("Знамя клана", 5, 8, 2, 3, 20, 6, 0, -1) { cell = 9, up = true, live = "unit_banner2" },
            new UnitType("Гвардия сёгуна", 16, 18, 12, 22, 100, 6, 0, 3) { cell = 10, up = true, live = "unit_hatamoto2", liveH = 1.9f, dbl = true },
            new UnitType("Псы Они", 9, 5, 3, 6, 18, 10, 0, -99) { cell = 11, up = true, live = "unit_dog2", liveH = 1.0f, dbl = true },
            new UnitType("Павшие воины", 5, 5, 2, 4, 10, 4, 0, 4) { portrait = "UI/portrait_fallen", live = "unit_fallen", undead = true },   // 24: некромантия
        };
        public const int RisenUnit = 24;
        public const int BaseUnits = 12;
        /// <summary>Улучшенная версия отряда (−1 — уже улучшен).</summary>
        public static int UpgradeOf(int u) { return u >= 0 && u < BaseUnits ? u + BaseUnits : -1; }
        public static readonly UnitType[] Foes =
        {
            new UnitType("Разбойники", 5, 3, 2, 3, 10, 5, 0, 6) { portrait = "UI/portrait_bandit", live = "unit_bandit", liveH = 1.68f },
            new UnitType("Ямабуси", 6, 5, 2, 4, 15, 5, 0, 7) { portrait = "UI/portrait_yamabushi", live = "unit_yamabushi", liveH = 1.76f },
            new UnitType("Дезертиры", 4, 5, 1, 3, 10, 4, 0, 8) { portrait = "UI/portrait_ashigaru", live = "unit_ashigaru" },
            new UnitType("Маски кицунэ", 8, 6, 3, 5, 18, 7, 6, 9) { portrait = "UI/portrait_kitsune", magic = true, live = "unit_kitsune", liveH = 1.64f, noMeleePen = true },
            new UnitType("Поверженные", 7, 7, 3, 6, 25, 4, 0, 4) { portrait = "UI/portrait_fallen", live = "unit_fallen", liveH = 1.75f, undead = true },
            new UnitType("Клан теней", 9, 5, 3, 6, 15, 8, 0, 5) { portrait = "UI/portrait_shadow", noRetal = true, live = "unit_shadow" },
            new UnitType("Они", 14, 12, 12, 20, 100, 5, 0, 2) { portrait = "UI/portrait_oni", live = "unit_oni", liveH = 2.3f, sweep = true, wide = true, fireImmune = 1 },
            new UnitType("Сёгун", 16, 16, 20, 30, 180, 6, 0, 3) { portrait = "UI/portrait_shogun", live = "unit_shogun", liveH = 1.85f, dbl = true, unlimRet = true, undead = true, resist = 0.3f },
            new UnitType("Ронины", 7, 6, 3, 5, 18, 6, 0, 1) { portrait = "UI/portrait_ronin", live = "unit_ronin" },
            new UnitType("Алый самурай", 11, 10, 7, 12, 45, 6, 0, 0) { portrait = "UI/portrait_red_samurai", live = "unit_crimson", liveH = 1.78f, dbl = true },
            new UnitType("Тэнгу-воины", 8, 5, 3, 6, 18, 9, 0, 1) { atlasCell = 6, fly = true, live = "unit_tengu" },
            new UnitType("Дайтэнгу", 13, 10, 8, 14, 50, 9, 8, 1) { atlasCell = 6, fly = true, magic = true, resist = 0.3f, live = "unit_daitengu", liveH = 2f },
        };

        public static string UnitName(int u) { return Ours[Mathf.Clamp(u, 0, Ours.Length - 1)].name; }
        public static string[] UnitStats(int u)
        {
            UnitType t = Ours[Mathf.Clamp(u, 0, Ours.Length - 1)];
            return new[] { "Атака " + t.atk + "   урон " + t.dmin + "–" + t.dmax, "Защита " + t.def, "Здоровье " + t.hp, "Скорость " + t.spd + (t.shots > 0 ? "   выстрелов " + t.shots : ""), t.Ability };
        }
        public static void DrawUnitPortrait(Rect r, int u) { DrawPortrait(r, Ours[Mathf.Clamp(u, 0, Ours.Length - 1)]); }

        class Stack
        {
            public int unit = -1;               // номер нашего отряда (Ours) — для армии поместья
            public UnitType t; public bool enemy;
            public int count, start, topHp, shots;
            public int c, r;
            public bool retaliated, waited, defending, moraleUsed;
            public int atkB, defB, spdB, buffRounds, curseRounds;
            public Vector2 pos;                 // где нарисован (виртуальные точки)
            public List<Vector2> path = new List<Vector2>();
            public float lunge; public Vector2 lungeDir; public float flash, die = -1f, born;
            public GameObject go; public Animation an; public string playing; public float actUntil, hitUntil;
            public Material surface;
            public TacticsFigureProfile figureProfile;
            public Transform figureMount;
            public float gaitPhase;
            public Transform wingL, wingR;
            public Quaternion wingRestL, wingRestR;
            public float wingLift;
            public Transform hips;
            public Vector3 restHips;
            public float clipStart, previousStart, actStart, contactAt;
            public Vector2 attackStep;
            public string previousClip;
            public string clipIdle, clipWalk, clipAct, clipHit, clipDie, clipWin;
            public int run;                     // сколько клеток пробежал перед ударом (разгон конницы)
            public float idleYaw;               // насколько стойка разворачивает грудь от носков (боевая стойка — боком); поправка, чтобы грудь смотрела на врага
            public int Total { get { return count <= 0 ? 0 : (count - 1) * t.hp + topHp; } }
            public bool Alive { get { return count > 0; } }
            public int Atk { get { return t.atk + atkB + (curseRounds > 0 ? -4 : 0) + (enemy ? 0 : gAtk + forge); } }
            public int Def { get { int d = t.def + defB + (enemy ? 0 : gDef + forge); return defending ? Mathf.CeilToInt(d * 1.3f) : d; } }
            public int Spd { get { return Mathf.Max(1, t.spd + spdB + (enemy ? -windRounds : tower + gSpd)); } }
        }

        static readonly List<Stack> stacks = new List<Stack>();
        static readonly List<Stack> queue = new List<Stack>();
        static Stack cur;
        static int round, region;
        static bool autoOurs, finished, won;
        static string resultText;

        // герой
        static int spirit, spiritMax, heroLv;
        static bool castThisRound;
        static int gAtk, gDef, gSpd;
        static bool deploy;           // расстановка перед боем (навык «Тактика», как в «Героях 3»): свои отряды переставляются в своих столбцах
        static Stack deploySel;
        static int DeployCols { get { return 1 + 2 * General.Skill(7); } }
        static bool mBallista, mTent, mCart;
        static bool ballistaReady;   // «Артиллерия»: баллиста ждёт цель от игрока (раз в раунд)
        static void BallistaShot(Stack best)
        {
            ballistaReady = false; spellSel = -1;
            int dmg = (gAtk + 2) * Random.Range(5, 9) * Mathf.Max(1, General.Val(9));
            Vector2 from = MachinePos(0) + new Vector2(R * 0.6f, -R * 0.5f);
            shots.Add(new Shot { a = from, b = best.pos + new Vector2(0f, -R * 0.8f), t0 = Time.unscaledTime, dur = 0.45f });
            Hurt(best, dmg); best.flash = 1f; best.hitUntil = Time.unscaledTime + 0.8f;
            Burst("fx_arrow_hit", best.pos + new Vector2(0f, -R * 1.0f), R * 2.8f, 40f);
            texts.Add(new Float { p = best.pos + new Vector2(0f, -R * 2f), s = "−" + dmg, t0 = Time.unscaledTime + 0.3f, col = new Color(1f, 0.85f, 0.4f) });
            Log("Баллиста (наводка) → " + best.t.name + ": " + dmg + " урона.");
            queue.RemoveAll(o => !o.Alive);
            CheckEnd();
        }   // боевые машины (кузня поместья): баллиста, палатка лекаря, повозка с боеприпасами   // полководец (General): атака, защита, «Тактика» — снимаются в начале боя
        static int forge, tower;          // кузня и башня поместья
        static bool fromEstate;           // бой армией поместья: выжившие вернутся в армию
        static int fogRounds, windRounds;
        static int spellSel = -1;       // выбранное умение (ждёт цель)
        static bool bookOpen;

        public class Spell { public int icon, cost; public string name, hint; public int target; /* 0 — враг, 1 — свой, 2 — клетка, 3 — без цели */ }
        static readonly Spell[] Spells =
        {
            new Spell { icon = 0, cost = 2, name = "КРЕСТОВЫЙ РАЗРЕЗ", hint = "Урон отряду врага", target = 0 },
            new Spell { icon = 1, cost = 2, name = "БОЕВОЙ КЛИЧ", hint = "+3 к атаке всех наших, 2 раунда", target = 3 },
            new Spell { icon = 2, cost = 2, name = "СТЕНА ЩИТОВ", hint = "+6 к защите отряда, 2 раунда", target = 1 },
            new Spell { icon = 3, cost = 3, name = "ГРАД СТРЕЛ", hint = "Урон врагам в клетке и вокруг", target = 2 },
            new Spell { icon = 4, cost = 2, name = "ОГНЕННЫЙ ТАЛИСМАН", hint = "Сильный урон отряду врага", target = 0 },
            new Spell { icon = 5, cost = 2, name = "ВЕТЕР КАМИ", hint = "Враги на 2 медленнее, 2 раунда", target = 3 },
            new Spell { icon = 6, cost = 2, name = "ИСЦЕЛЕНИЕ", hint = "Лечит и поднимает павших отряда", target = 1 },
            new Spell { icon = 7, cost = 2, name = "ТУМАН", hint = "Стрелки врага бьют вполсилы, 2 раунда", target = 3 },
            new Spell { icon = 8, cost = 3, name = "ГРОМ", hint = "Удар молнии по отряду врага", target = 0 },
            new Spell { icon = 9, cost = 2, name = "ПРОКЛЯТИЕ", hint = "−4 к атаке врага, бьёт слабейшим ударом", target = 0 },
            new Spell { icon = 10, cost = 1, name = "РЫВОК", hint = "+3 к скорости отряда на раунд", target = 1 },
            // вторая страница книги (иконки UI/Battle/skill_icons2, 4×3)
            new Spell { icon = 11, cost = 3, name = "ЛЕДЯНОЙ КЛИНОК", hint = "Урон и −3 к скорости врага, 2 раунда", target = 0 },
            new Spell { icon = 12, cost = 4, name = "ЗЕМЛЕТРЯСЕНИЕ", hint = "Урон всем врагам; в осаде рушит стену", target = 3 },
            new Spell { icon = 13, cost = 2, name = "КРОВАВОЕ БЕЗУМИЕ", hint = "+6 к атаке отряда, −3 к защите, 2 раунда", target = 1 },
            new Spell { icon = 14, cost = 3, name = "ЩИТ ДУХА", hint = "+4 к защите всех наших, 2 раунда", target = 3 },
            new Spell { icon = 15, cost = 3, name = "ДУХИ ЛИС", hint = "Три удара по случайным врагам", target = 3 },
            new Spell { icon = 16, cost = 2, name = "ТУМАН СТРАХА", hint = "Враг не отвечает на удары и −3 к атаке, 2 раунда", target = 0 },
            new Spell { icon = 17, cost = 4, name = "ЦЕПНАЯ МОЛНИЯ", hint = "Удар по врагу и вполсилы — по двум ближним", target = 0 },
            new Spell { icon = 18, cost = 4, name = "ЦЕЛЕБНЫЙ ДОЖДЬ", hint = "Лечит первых бойцов всех наших отрядов", target = 3 },
            new Spell { icon = 19, cost = 2, name = "ВРАТА ТЕНЕЙ", hint = "+5 к скорости отряда на раунд", target = 1 },
            new Spell { icon = 20, cost = 4, name = "ОСТАНОВКА ВРЕМЕНИ", hint = "Все враги на 3 медленнее, 2 раунда", target = 3 },
            new Spell { icon = 21, cost = 2, name = "БЕРСЕРК", hint = "+10 к атаке отряда, защита −50%, 1 раунд", target = 1 },
            new Spell { icon = 22, cost = 5, name = "ПОДНЯТЬ ПАВШИХ", hint = "Поднимает много павших в отряде", target = 1 },
        };
        public static void DrawSpellIcon(Rect r, int spell) { if (spell >= 0 && spell < Spells.Length) SpellIcon(r, Spells[spell].icon); }
        public static string SpellName(int spell) { return spell >= 0 && spell < Spells.Length ? Spells[spell].name : ""; }
        static void SpellIcon(Rect r, int icon)
        {
            Texture2D sk = UiKit.Tex(icon < 11 ? "UI/Battle/skill_icons" : "UI/Battle/skill_icons2");
            int k = icon < 11 ? icon : icon - 11;
            if (sk != null) GUI.DrawTextureWithTexCoords(r, sk, new Rect((k % 4) * 0.25f, 1f - (k / 4 + 1) / 3f, 0.25f, 1f / 3f));
        }

        // ---------------------------------------------------------------- поле

        const int Cols = 15, Rows = 11;
        const float Sq = 0.56f;
        static float R, hw, hh, ox, oy, W;
        static Vector2 Center(int c, int r) { return new Vector2(ox + c * hw, oy + r * hh + ((c & 1) == 1 ? hh * 0.5f : 0f)); }
        static bool In(int c, int r) { return c >= 0 && c < Cols && r >= 0 && r < Rows; }
        static readonly int[,] NbEven = { { 1, -1 }, { 1, 0 }, { 0, -1 }, { 0, 1 }, { -1, -1 }, { -1, 0 } };
        static readonly int[,] NbOdd = { { 1, 0 }, { 1, 1 }, { 0, -1 }, { 0, 1 }, { -1, 0 }, { -1, 1 } };
        static IEnumerable<Vector2Int> Nb(int c, int r)
        {
            int[,] t = (c & 1) == 1 ? NbOdd : NbEven;
            for (int i = 0; i < 6; i++) { int nc = c + t[i, 0], nr = r + t[i, 1]; if (In(nc, nr)) yield return new Vector2Int(nc, nr); }
        }
        static int Dist(int c1, int r1, int c2, int r2)
        {
            int x1 = c1, z1 = r1 - (c1 - (c1 & 1)) / 2, x2 = c2, z2 = r2 - (c2 - (c2 & 1)) / 2;
            int dx = x1 - x2, dz = z1 - z2, dy = -dx - dz;
            return Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz)));
        }
        static Stack At(int c, int r) { foreach (Stack s in stacks) if (s.Alive && (s.c == c && s.r == r || s.t.wide && s.c + Back(s) == c && s.r == r)) return s; return null; }
        // двухклеточные: вторая клетка — позади головы (наши смотрят вправо — сзади слева, враги — наоборот); (c±1, r) всегда соседняя
        static int Back(Stack s) { return s.enemy ? 1 : -1; }
        static Vector2Int TailOf(Stack s) { return new Vector2Int(s.c + Back(s), s.r); }
        static IEnumerable<Vector2Int> Cells(Stack s) { yield return new Vector2Int(s.c, s.r); if (s.t.wide) yield return TailOf(s); }
        /// <summary>Соседние клетки отряда (у двухклеточного — вокруг обеих клеток, без своих).</summary>
        static IEnumerable<Vector2Int> Around(Stack s)
        {
            HashSet<Vector2Int> seen = new HashSet<Vector2Int>(Cells(s));
            foreach (Vector2Int c in Cells(s)) foreach (Vector2Int n in Nb(c.x, c.y)) if (seen.Add(n)) yield return n;
        }
        /// <summary>Может ли отряд s стоять головой в клетке (c, r): у двухклеточного свободна и клетка позади.</summary>
        static bool Fits(Stack s, int c, int r)
        {
            if (!s.t.wide) return true;
            int tc = c + Back(s);
            if (!In(tc, r) || Rock(tc, r) || WallBlocks(s, tc, r)) return false;
            Stack o = At(tc, r); return o == null || o == s;
        }
        /// <summary>Где рисуется отряд, стоящий головой в клетке: у двухклеточного — между головой и хвостом.</summary>
        static Vector2 Home(Stack s, int c, int r)
        {
            Vector2 p = Center(c, r);
            if (!s.t.wide) return p;
            return (p + Center(c + Back(s), r)) * 0.5f;
        }

        // препятствия (как в «Героях»): на клетку не встать; картинки — UI/Battle/obstacles (4×3), нет — камни-заглушки
        static readonly List<Vector3Int> rocks = new List<Vector3Int>();   // x, y — клетка, z — ячейка атласа
        static bool Rock(int c, int r) { foreach (Vector3Int o in rocks) if (o.x == c && o.y == r) return true; return false; }

        // осада («Герои 3»): стена — столбец WallC, у каждой клетки 2 «жизни» (цела → пробита → рухнула), ворота — ряд GateR
        // (защитники проходят, нападающим — ломать), башни — крайние ряды, стреляют каждый раунд; катапульта нападающих бьёт стену
        const int GateR = 5;
        static bool siege, siegeDef;   // siegeDef — мы обороняем поместье: стена слева (столбец 3), враг штурмует
        static int WallC { get { return siegeDef ? 3 : 11; } }
        static bool Attacker(Stack s) { return s != null && s.enemy == siegeDef; }
        static readonly int[] wallHp = new int[11];
        static bool IsTower(int r) { return r == 0 || r == Rows - 1; }
        static bool WallAt(int c, int r) { return siege && c == WallC && wallHp[r] > 0; }
        static bool WallBlocks(Stack s, int c, int r) { return WallAt(c, r) && !(r == GateR && s != null && !Attacker(s)); }
        static void PlaceRocks()
        {
            rocks.Clear();
            int n = Random.Range(3, 7);
            for (int g = 0; g < 60 && rocks.Count < n; g++)
            {
                int c = Random.Range(3, Cols - 3), r = Random.Range(0, Rows);
                if (Rock(c, r) || At(c, r) != null) continue;
                rocks.Add(new Vector3Int(c, r, Random.Range(0, 12)));
            }
        }
        static Vector2Int Pick(Vector2 p)
        {
            float best = float.MaxValue; Vector2Int h = new Vector2Int(-1, -1);
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    Vector2 d = p - Center(c, r); d.y /= Sq;
                    float m = d.sqrMagnitude;
                    if (m < best) { best = m; h = new Vector2Int(c, r); }
                }
            return best <= R * R * 1.1f ? h : new Vector2Int(-1, -1);
        }

        // досягаемость текущего отряда: клетка → шаги, откуда пришли
        static readonly Dictionary<Vector2Int, int> reach = new Dictionary<Vector2Int, int>();
        static readonly Dictionary<Vector2Int, Vector2Int> from = new Dictionary<Vector2Int, Vector2Int>();
        static void Flood(Stack s)
        {
            reach.Clear(); from.Clear();
            Vector2Int st = new Vector2Int(s.c, s.r);
            Dictionary<Vector2Int, int> dist = new Dictionary<Vector2Int, int>();   // и клетки, над которыми летун только пролетает
            dist[st] = 0; reach[st] = 0;
            Queue<Vector2Int> q = new Queue<Vector2Int>(); q.Enqueue(st);
            while (q.Count > 0)
            {
                Vector2Int a = q.Dequeue(); int d = dist[a];
                if (d >= s.Spd) continue;
                foreach (Vector2Int b in Nb(a.x, a.y))
                {
                    if (dist.ContainsKey(b)) continue;
                    Stack ob = At(b.x, b.y);
                    bool blocked = Rock(b.x, b.y) || WallBlocks(s, b.x, b.y) || ob != null && ob != s || !Fits(s, b.x, b.y);
                    if (blocked && !s.t.fly) continue;
                    dist[b] = d + 1; from[b] = a; q.Enqueue(b);
                    if (!blocked) reach[b] = d + 1;   // летун пролетает над занятым, но садится только на свободное
                }
            }
        }
        static List<Vector2Int> PathTo(Vector2Int to)
        {
            List<Vector2Int> l = new List<Vector2Int>();
            Vector2Int p = to;
            while (from.ContainsKey(p)) { l.Add(p); p = from[p]; }
            l.Reverse();
            return l;
        }
        static bool Blocked(Stack s)   // стрелок, которому мешает враг рядом
        {
            foreach (Vector2Int n in Around(s)) { Stack o = At(n.x, n.y); if (o != null && o.enemy != s.enemy) return true; }
            return false;
        }
        static bool CanShoot(Stack s) { return s.t.shots > 0 && s.shots > 0 && !Blocked(s); }

        // ---------------------------------------------------------------- начало боя

        /// <summary>Отрядные бои на карте: дуэли и засады — пошаговым боем армии поместья (меню разработчика «ОТРЯДЫ»).</summary>
        public static bool MapBattles
        {
            get { return PlayerPrefs.GetInt("tactics_map", 0) == 1; }
            set { PlayerPrefs.SetInt("tactics_map", value ? 1 : 0); PlayerPrefs.Save(); }
        }
        static int mapNode = -1;
        static bool returned;
        /// <summary>Бой закончился, карта — продолжает (поражение, итог).</summary>
        public static bool TakeReturned() { bool r = returned; returned = false; return r; }

        /// <summary>Бой на месте карты: враги — по области, ряду места и отряду (засада — ночью, враг сильнее).</summary>
        public static void ShowNode(int node)
        {
            MapNode n = Campaign.Nodes[node];
            ShowArmy(Campaign.Region);
            mapNode = node;
            stacks.RemoveAll(s => s.enemy);
            int[][] pool = { new[] { 0, 1, 2, 3, 4, 8 }, new[] { 1, 3, 4, 5, 6, 8 }, new[] { 4, 5, 6, 8, 9, 7 } };
            int reg = Mathf.Clamp(Campaign.Region, 0, 2);
            int nst = Mathf.Clamp(3 + (n.extra != null ? n.extra.Count : 0) + (n.Type == NodeType.Ambush ? 1 : 0), 3, 6);
            float power = (1f + 0.15f * n.layer) * (1f + 0.4f * Profile.Cycle) * (n.Type == NodeType.Ambush ? 1.25f : 1f);
            int[] rows = { 5, 3, 7, 1, 9, 0 };
            for (int i = 0; i < nst; i++)
            {
                UnitType t = Foes[pool[reg][Random.Range(0, pool[reg].Length - (i == 0 ? 0 : 1))]];
                int cnt = Mathf.Max(1, Mathf.RoundToInt(120f * power / Mathf.Max(8, t.hp) * Random.Range(0.7f, 1.3f)));
                Add(t, cnt, true, Cols - 1, rows[i]);
            }
            round = 0; queue.Clear(); log.Clear();
            NewRound();
            Log((n.Type == NodeType.Ambush ? "Засада! " : "Бой на дороге. ") + "Раунд 1.");
        }

        /// <summary>Бой армией поместья (Estate.D.army) против отрядов области.</summary>
        public static void ShowArmy(int reg)
        {
            ShowDev(reg);
            stacks.RemoveAll(s => !s.enemy);
            int[] rows = { 5, 3, 7, 1, 9, 0, 10 };
            for (int i = 0; i < Estate.D.army.Count && i < 7; i++)
            {
                Estate.ArmySlot a = Estate.D.army[i];
                Add(Ours[Mathf.Clamp(a.unit, 0, Ours.Length - 1)], a.count, false, 0, rows[i]);
                stacks[stacks.Count - 1].unit = a.unit;
            }
            fromEstate = true;
            forge = Estate.ForgeBonus; tower = Estate.SpeedBonus;
            mBallista = Estate.D.machines[0]; mTent = Estate.D.machines[1]; mCart = Estate.D.machines[2];
            spiritMax += Estate.SpiritBonus; spirit = spiritMax;
            round = 0; queue.Clear(); log.Clear();
            NewRound();
            Log("Армия поместья вступает в бой. Раунд 1.");
        }

        static System.Action<bool> neutralDone;
        /// <summary>Сила бойца (для бегства и найма нейтралов): здоровье × (атака + защита).</summary>
        public static int OursPower(int u) { UnitType t = Ours[Mathf.Clamp(u, 0, Ours.Length - 1)]; return t.hp * (t.atk + t.def); }
        public static int FoePower(int f) { UnitType t = Foes[Mathf.Clamp(f, 0, Foes.Length - 1)]; return t.hp * (t.atk + t.def); }
        public static string FoeName(int f) { return Foes[Mathf.Clamp(f, 0, Foes.Length - 1)].name; }
        /// <summary>Бой с армией вражеского полководца (Realm): смешанные отряды, сила растёт с его уровнем.</summary>
        public static void ShowWarband(int level, System.Action<bool> done)
        {
            ShowArmy(Campaign.Region);
            stacks.RemoveAll(o => o.enemy);
            int[] mix = { 9, 4, 5, 2, 6, 7, 10 }; int[] rows = { 1, 3, 5, 7, 9 };
            for (int i = 0; i < 5; i++) { UnitType t = Foes[mix[(i + level) % mix.Length]]; Add(t, Mathf.Max(1, Mathf.RoundToInt((10 + 7 * level) * 25f / Mathf.Max(10, t.hp))), true, Cols - 1, rows[i]); }
            foreach (Stack st in stacks) st.pos = Vector2.zero;
            neutralDone = done;
            round = 0; queue.Clear(); log.Clear(); NewRound();
            Log("Армия вражеского полководца! Раунд 1.");
        }
        /// <summary>Бой армией поместья с нейтральным отрядом карты (Realm): численность делится на отряды, как в «Героях».</summary>
        public static void ShowNeutral(int foe, int count, System.Action<bool> done)
        {
            ShowArmy(Campaign.Region);
            stacks.RemoveAll(s => s.enemy);
            UnitType t = Foes[Mathf.Clamp(foe, 0, Foes.Length - 1)];
            int parts = Mathf.Clamp(count / 6 + 1, 1, 5);
            int[] rows = { 5, 3, 7, 1, 9 };
            for (int i = 0; i < parts; i++) Add(t, Mathf.Max(1, count / parts + (i < count % parts ? 1 : 0)), true, Cols - 1, rows[i]);
            foreach (Stack s in stacks) s.pos = Vector2.zero;
            neutralDone = done;
            round = 0; queue.Clear(); log.Clear(); NewRound();
            Log("Нейтралы: " + t.name + " (" + count + "). Раунд 1.");
        }

        /// <summary>Пробный бой (меню разработчика): армия героя против отрядов области.</summary>
        public static void ShowDev(int reg)
        {
            fromEstate = false; forge = 0; tower = 0; mapNode = -1; siege = false; siegeDef = false; neutralDone = null;
            region = Mathf.Clamp(reg, 0, 2);
            stacks.Clear(); queue.Clear(); log.Clear(); texts.Clear(); shots.Clear();
            heroLv = Mathf.Max(1, Profile.Level);
            General.Sync();
            mBallista = mTent = mCart = true;   // пробный бой — со всеми машинами
            gAtk = General.Atk; gDef = General.Def; gSpd = General.Val(7) + General.ArtSpeed;
            spiritMax = 4 + General.Know + General.Val(5); spirit = spiritMax;
            fogRounds = 0; windRounds = 0; castThisRound = false; spellSel = -1; bookOpen = false; info = null;
            autoOurs = false; finished = false; won = false; round = 0;
            int k = 1 + Profile.Cycle;
            // тестовый бой разработчика: каждый раз 7 случайных наших и 7 случайных врагов — так видно все живые модели
            int[] rowsT = { 0, 2, 4, 5, 6, 8, 10 };
            List<int> ou = new List<int>(); for (int i = 0; i < Ours.Length; i++) ou.Add(i);
            List<int> fo = new List<int>(); for (int i = 0; i < Foes.Length; i++) fo.Add(i);
            for (int i = 0; i < 7; i++)
            {
                int a = Random.Range(i, ou.Count); int x = ou[a]; ou[a] = ou[i]; ou[i] = x;
                int b = Random.Range(i, fo.Count); int y = fo[b]; fo[b] = fo[i]; fo[i] = y;
                UnitType tu = Ours[ou[i]];
                Add(tu, Mathf.Max(1, Mathf.RoundToInt(180f * k / Mathf.Max(10, tu.hp))), false, 0, rowsT[i]);
                UnitType tf = Foes[fo[i]];
                Add(tf, Mathf.Max(1, Mathf.RoundToInt(170f * k * (1f + 0.3f * region) / Mathf.Max(10, tf.hp))), true, Cols - 1, rowsT[i]);
            }
            Open = true;
            snapsTaken = false;
            PlaceRocks();
            NewRound();
            running = null; busyUntil = Time.unscaledTime + 0.6f;
            Log("Бой начался. Раунд 1.");
        }

        /// <summary>Выкуп при сдаче («Герои 3»): половина цены уцелевших бойцов.</summary>
        static int Ransom()
        {
            int v = 0;
            foreach (Stack st in stacks) if (st.Alive && !st.enemy) v += st.count * (st.unit >= 0 && st.unit < Estate.Price.Length ? Estate.Price[st.unit] : st.t.hp * 3) / 2;
            return Mathf.Max(100, v);
        }
        static List<KeyValuePair<int, int>> Survivors()
        {
            List<KeyValuePair<int, int>> sv = new List<KeyValuePair<int, int>>();
            foreach (Stack s in stacks) if (!s.enemy && s.unit >= 0) sv.Add(new KeyValuePair<int, int>(s.unit, Mathf.Max(0, s.count)));
            return sv;
        }

        static void Add(UnitType t, int n, bool enemy, int c, int r)
        {
            if (t.wide) c = enemy ? Mathf.Min(c, Cols - 2) : Mathf.Max(c, 1);   // двухклеточному — место под круп у края
            stacks.Add(new Stack { t = t, enemy = enemy, count = n, start = n, topHp = t.hp, shots = t.shots, c = c, r = r, born = Time.unscaledTime });
        }

        static Vector2 MachinePos(int m) { Vector2 c = Center(0, 4 + m * 3); return new Vector2(ox - R * 2.1f, c.y); }
        /// <summary>Начало раунда: баллиста бьёт сильнейший отряд врага, лекарь лечит раненого.</summary>
        static void Machines()
        {
            ballistaReady = mBallista && General.Skill(9) > 0 && !autoOurs;
            if (mBallista && !ballistaReady)
            {
                Stack best = null;
                foreach (Stack o in stacks) if (o.Alive && o.enemy && (best == null || o.Total > best.Total)) best = o;
                if (best != null)
                {
                    int dmg = (gAtk + 2) * Random.Range(5, 9);
                    Vector2 from = MachinePos(0) + new Vector2(R * 0.6f, -R * 0.5f);
                    shots.Add(new Shot { a = from, b = best.pos + new Vector2(0f, -R * 0.8f), t0 = Time.unscaledTime, dur = 0.45f });
                    Hurt(best, dmg); best.flash = 1f; best.hitUntil = Time.unscaledTime + 0.8f;
                    Burst("fx_arrow_hit", best.pos + new Vector2(0f, -R * 1.0f), R * 2.8f, 40f);
                    texts.Add(new Float { p = best.pos + new Vector2(0f, -R * 2f), s = "−" + dmg, t0 = Time.unscaledTime + 0.3f, col = new Color(1f, 0.85f, 0.4f) });
                    Log("Баллиста → " + best.t.name + ": " + dmg + " урона.");
                }
            }
            if (mTent)
            {
                Stack hurt = null;
                foreach (Stack o in stacks) if (o.Alive && !o.enemy && o.topHp < o.t.hp && (hurt == null || o.topHp * hurt.t.hp < hurt.topHp * o.t.hp)) hurt = o;
                if (hurt != null)
                {
                    int h = Mathf.Min(hurt.t.hp - hurt.topHp, Random.Range(10, 26));
                    hurt.topHp += h;
                    texts.Add(new Float { p = hurt.pos + new Vector2(0f, -R * 2f), s = "+" + h, t0 = Time.unscaledTime, col = new Color(0.55f, 1f, 0.55f) });
                    Burst("fx_heal", hurt.pos + new Vector2(0f, -R * 0.9f), R * 2.6f);
                }
            }
        }
        /// <summary>Враг штурмует поместье: наша армия за стеной (столбец 3), башни бьют штурмующих; итог — Realm.</summary>
        public static void ShowSiegeDefense(int foeLevel, System.Action<bool> done)
        {
            ShowArmy(Campaign.Region);
            if (Estate.D.army.Count == 0) { stacks.RemoveAll(o => !o.enemy); Add(Ours[0], 10 + 5 * Realm.D.fort, false, 0, 5); }   // гарнизон
            stacks.RemoveAll(o => o.enemy);
            int[] mix = { 0, 2, 5, 6, 4, 7 }; int[] rows = { 1, 3, 5, 7, 9 };
            for (int i = 0; i < 5; i++) { UnitType t = Foes[mix[(i + foeLevel) % mix.Length]]; Add(t, Mathf.Max(1, Mathf.RoundToInt((8 + 6 * foeLevel) * 25f / Mathf.Max(10, t.hp))), true, Cols - 1, rows[i]); }
            siege = true; siegeDef = true;
            for (int r = 0; r < Rows; r++) wallHp[r] = 1 + Mathf.Min(2, Realm.D.fort);
            rocks.RemoveAll(o => o.x <= 4);
            foreach (Stack st in stacks) st.pos = Vector2.zero;
            neutralDone = done;
            round = 0; queue.Clear(); log.Clear(); NewRound();
            Log("Враг штурмует поместье! Держите стену — башни бьют штурмующих.");
        }

        public static void ShowSiege(int reg)
        {
            ShowDev(reg);
            siege = true; siegeDef = false;
            for (int r = 0; r < Rows; r++) wallHp[r] = 2;
            rocks.RemoveAll(o => o.x >= WallC - 1);
            int i = 0;
            int[,] spot = { { 13, 1 }, { 13, 3 }, { 13, 5 }, { 13, 7 }, { 13, 9 }, { 14, 2 }, { 14, 8 }, { 12, 4 }, { 12, 6 } };
            foreach (Stack st in stacks) if (st.enemy && i < 9) { st.c = Mathf.Min(spot[i, 0], st.t.wide ? Cols - 2 : Cols - 1); st.r = spot[i, 1]; st.pos = Vector2.zero; i++; }
            Log("Осада! Ломайте стену и ворота — защитники за стеной получают от стрел вполовину.");
        }
        static void SiegeRound()
        {
            if (!siege) return;
            // катапульта нападающих: по воротам, потом — по случайной целой стене
            int t = wallHp[GateR] > 0 ? GateR : -1;
            if (t < 0) { List<int> ok = new List<int>(); for (int r = 1; r < Rows - 1; r++) if (wallHp[r] > 0) ok.Add(r); if (ok.Count > 0) t = ok[Random.Range(0, ok.Count)]; }
            if (t >= 0 && Random.value < 0.75f)
            {
                wallHp[t]--; Burst("fx_arrow_hit", Center(WallC, t) + new Vector2(0f, -R), R * 3.6f, 30f);
                Log("Катапульта: " + (t == GateR ? "ворота" : "стена") + (wallHp[t] == 0 ? " рухнули." : " пробиты."));
            }
            else if (t >= 0) Log("Катапульта промахнулась.");
            // башни защитников
            for (int r = 0; r < Rows; r += Rows - 1)
            {
                if (!IsTower(r) || wallHp[r] <= 0) continue;
                Stack best = null;
                foreach (Stack o in stacks) if (o.Alive && Attacker(o) && (best == null || o.Total > best.Total)) best = o;
                if (best == null) continue;
                int dmg = siegeDef ? Random.Range(10, 18) * (1 + Estate.ForgeBonus) * (1 + Realm.D.fort) : Random.Range(8, 15) * (1 + Profile.Cycle);
                shots.Add(new Shot { a = Center(WallC, r) + new Vector2(0f, -R * 2f), b = best.pos + new Vector2(0f, -R * 0.8f), t0 = Time.unscaledTime, dur = 0.45f });
                Hurt(best, dmg); best.flash = 1f; best.hitUntil = Time.unscaledTime + 0.8f;
                texts.Add(new Float { p = best.pos + new Vector2(0f, -R * 2f), s = "−" + dmg, t0 = Time.unscaledTime + 0.3f, col = new Color(1f, 0.45f, 0.35f) });
                Log("Башня → " + best.t.name + ": " + dmg + " урона.");
            }
        }
        static IEnumerator DoWallHit(Stack a, int r)
        {
            a.lungeDir = (Center(WallC, r) - a.pos).normalized;
            a.attackStep = a.lungeDir * R * 0.48f;
            if (a.an != null) { yield return Act(a, 0.45f); } else { a.lunge = 1f; yield return 0.18f; }
            wallHp[r] = Mathf.Max(0, wallHp[r] - 1);
            Burst("fx_slash_hit", Center(WallC, r) + new Vector2(0f, -R * 1.1f), R * 3f, 36f);
            Log(a.t.name + ": " + (r == GateR ? "ворота" : IsTower(r) ? "башня" : "стена") + (wallHp[r] == 0 ? " разрушены." : " пробиты."));
            UiFx.Play("ui_taiko_heavy", 0.5f);
            yield return 0.3f;
            EndTurn();
        }
        static bool NextToWall(Stack s, int r) { foreach (Vector2Int n in Nb(s.c, s.r)) if (n.x == WallC && n.y == r) return true; return false; }
        static void DrawWall()
        {
            if (!siege) return;
            Texture2D t = UiKit.Tex("UI/Battle/siege_parts");
            for (int r = 0; r < Rows; r++)
            {
                Vector2 p = Center(WallC, r);
                int k = IsTower(r) ? (wallHp[r] > 0 ? 6 : 7) : r == GateR ? (wallHp[r] == 2 ? 3 : wallHp[r] == 1 ? 4 : 5) : (wallHp[r] == 2 ? 0 : wallHp[r] == 1 ? 1 : 2);
                if (t == null) { if (wallHp[r] > 0) UiKit.Disc(p, R * 0.7f, new Color(0.5f, 0.48f, 0.44f)); continue; }
                float w = R * 2.5f, h = w * 362f / 362f;
                Rect uvw = new Rect((k % 4) * 0.25f, 1f - (k / 4 + 1) / 3f, 0.25f, 1f / 3f);
                if (siegeDef) uvw = new Rect(uvw.xMax, uvw.y, -uvw.width, uvw.height);   // штурм справа — стена развёрнута
                GUI.DrawTextureWithTexCoords(new Rect(p.x - w * 0.5f, p.y - h * 0.72f, w, h), t, uvw);
            }
        }

        static void DrawMachines()
        {
            Texture2D t = UiKit.Tex("UI/Battle/war_machines");
            if (t == null) return;
            bool[] on = { mBallista, mTent, mCart };
            for (int m = 0; m < 3; m++)
            {
                if (!on[m]) continue;
                Vector2 p = MachinePos(m); float w = R * 2.4f, h = w * 0.9f;
                GUI.DrawTextureWithTexCoords(new Rect(p.x - w * 0.5f, p.y - h * 0.7f, w, h), t, new Rect(m / 3f, 0f, 1f / 3f, 1f));
            }
        }

        static void NewRound()
        {
            round++;
            castThisRound = false;
            deploy = round == 1 && General.Skill(7) > 0 && !finished; deploySel = null;
            if (round > 1)
            {
                if (fogRounds > 0) fogRounds--;
                if (windRounds > 0) windRounds--;
                foreach (Stack s in stacks)
                {
                    s.retaliated = false; s.waited = false; s.moraleUsed = false;
                    if (s.buffRounds > 0 && --s.buffRounds == 0) { s.atkB = 0; s.defB = 0; s.spdB = 0; }
                    if (s.curseRounds > 0) s.curseRounds--;
                }
            }
            if (round > 1) { Machines(); SiegeRound(); if (CheckEnd()) return; }   // машины — с начала второго раунда (первый — расстановка)
            queue.Clear();
            foreach (Stack s in stacks) if (s.Alive) queue.Add(s);
            queue.Sort((a, b) => a.Spd != b.Spd ? b.Spd.CompareTo(a.Spd) : a.enemy.CompareTo(b.enemy));
            if (round > 1) Log("Раунд " + round + ".");
            NextTurn();
        }

        static void NextTurn()
        {
            queue.RemoveAll(s => !s.Alive);
            if (CheckEnd()) return;
            if (queue.Count == 0) { NewRound(); return; }
            cur = queue[0];
            cur.defending = false;
            if (!cur.enemy && !cur.t.undead && Morale < 0 && Random.value < -Morale / 12f)
            {   // дурной дух: отряд дрогнул и пропускает ход
                texts.Add(new Float { p = cur.pos + new Vector2(0f, -R * 2.6f), s = "ДРОГНУЛИ", t0 = Time.unscaledTime, col = new Color(0.7f, 0.6f, 1f) });
                Log(cur.t.name + ": дух пал — пропускают ход."); queue.RemoveAt(0); NextTurn(); return;
            }
            Flood(cur);
        }

        static bool CheckEnd()
        {
            bool ours = false, theirs = false;
            foreach (Stack s in stacks) if (s.Alive) { if (s.enemy) theirs = true; else ours = true; }
            if (ours && theirs) return false;
            finished = true; won = ours; cur = null;
            int lost = 0, killed = 0, gold = 0, raised = 0;
            if (won && General.Val(6) > 0)   // «Знахарство»: часть павших поднимается после победы
                foreach (Stack s in stacks)
                {
                    if (s.enemy) continue;
                    int up = Mathf.FloorToInt((s.start - Mathf.Max(0, s.count)) * General.Pct(6));
                    if (up > 0) { if (s.count <= 0) { s.count = 0; s.topHp = s.t.hp; } s.count += up; raised += up; }
                }
            foreach (Stack s in stacks)
            {
                if (s.enemy) { killed += s.start - Mathf.Max(0, s.count); gold += (s.start - Mathf.Max(0, s.count)) * s.t.hp / 3; }
                else lost += s.start - Mathf.Max(0, s.count);
            }
            if (fromEstate)
            {
                List<KeyValuePair<int, int>> sv = new List<KeyValuePair<int, int>>();
                foreach (Stack s in stacks) if (!s.enemy && s.unit >= 0) sv.Add(new KeyValuePair<int, int>(s.unit, Mathf.Max(0, s.count)));
                Estate.AfterBattle(sv, ours);
            }
            string loot = "";
            if (won && fromEstate && General.Val(10) > 0)
            {   // «Некромантия»: часть сражённых врагов встаёт нашими павшими воинами
                int hpKilled = 0; foreach (Stack st in stacks) if (st.enemy) hpKilled += (st.start - Mathf.Max(0, st.count)) * st.t.hp;
                int risen = Mathf.FloorToInt(hpKilled * General.Pct(10) / Ours[RisenUnit].hp);
                if (risen > 0 && Estate.AddTroops(RisenUnit, risen)) loot += "\nПавших подняли: " + risen + " (в армии).";
            }
            if (won && Random.value < 0.25f) { int art = General.RandomArt(); if (General.Give(art)) loot = "\nТрофей: " + General.Arts[art].name + " (в рюкзаке полководца)."; }
            int xp = 0; foreach (Stack s in stacks) if (s.enemy) xp += (s.start - Mathf.Max(0, s.count)) * s.t.hp;
            xp = won ? Mathf.Max(10, xp / 4) : 0;
            if (xp > 0) { int lv = Profile.AddXp(xp); General.Sync(); loot += "\nОпыт полководца: +" + xp + (lv > 0 ? " — новый уровень!" : ""); }
            if (won) { Profile.AddGold(gold); resultText = "Враг разбит. Сражено врагов: " + killed + ", наших потерь: " + lost + "." + (raised > 0 ? " Знахари подняли: " + raised + "." : "") + "\nДобыча: " + gold + " мон." + loot; }
            else resultText = "Армия разбита. Наших потерь: " + lost + ".";
            return true;
        }

        // ---------------------------------------------------------------- действия (корутины по кадрам)

        // действия — корутины без MonoBehaviour: вложенные (yield return IEnumerator) идут стопкой, число — пауза в секундах
        static readonly System.Collections.Generic.Stack<IEnumerator> run = new System.Collections.Generic.Stack<IEnumerator>();
        static IEnumerator running { get { return run.Count > 0 ? run.Peek() : null; } set { run.Clear(); if (value != null) run.Push(value); } }
        static float waitUntil, busyUntil;
        static void Run(IEnumerator e) { running = e; waitUntil = 0f; }
        static void Step()
        {
            for (int guard = 0; guard < 32 && run.Count > 0; guard++)
            {
                if (Time.unscaledTime < waitUntil) return;
                IEnumerator top = run.Peek();
                if (!top.MoveNext())
                {
                    run.Pop();
                    if (run.Count == 0) { busyUntil = Time.unscaledTime + 0.15f; return; }
                    continue;
                }
                object y = top.Current;
                if (y is IEnumerator) { run.Push((IEnumerator)y); continue; }
                if (y is float) { waitUntil = Time.unscaledTime + (float)y; return; }
                return;   // null — до следующего кадра
            }
        }
        static bool Busy { get { return running != null || Time.unscaledTime < busyUntil; } }

        static IEnumerator MoveAlong(Stack s, List<Vector2Int> path)
        {
            foreach (Vector2Int p in path) s.path.Add(Home(s, p.x, p.y));
            if (path.Count > 0) { s.c = path[path.Count - 1].x; s.r = path[path.Count - 1].y; }
            while (s.path.Count > 0) yield return null;
        }

        static IEnumerator DoMove(Stack s, Vector2Int to)
        {
            yield return MoveAlong(s, PathTo(to));
            EndTurn();
        }

        static IEnumerator DoMelee(Stack a, Stack d, Vector2Int stand)
        {
            a.run = 0;
            if (stand.x != a.c || stand.y != a.r) { List<Vector2Int> pth = PathTo(stand); a.run = pth.Count; yield return MoveAlong(a, pth); }
            yield return Strike(a, d, false);
            a.run = 0;
            if (d.Alive && a.Alive && (!d.retaliated || d.t.unlimRet) && !a.t.noRetal && !finished)
            {
                d.retaliated = true;
                yield return 0.15f;
                yield return Strike(d, a, false);
            }
            if (a.t.dbl && a.Alive && d.Alive && !finished) { yield return 0.15f; Log(a.t.name + ": второй удар."); yield return Strike(a, d, false); }
            EndTurn();
        }

        static IEnumerator DoShoot(Stack a, Stack d)
        {
            if (!(mCart && !a.enemy)) a.shots--;   // повозка с боеприпасами: стрелы не кончаются
            a.lungeDir = (d.pos - a.pos).normalized;
            a.attackStep = Vector2.zero;
            if (a.an != null) yield return Act(a, 0.55f);   // натянул лук / вскинул ружьё / сложил печать
            if (a.t == Ours[2] || a.t == Ours[14]) Burst("fx_musket", a.pos + new Vector2(R * (a.enemy ? -0.9f : 0.9f), -R * 1.3f), R * 3.2f, 36f);
            Vector2 from = a.pos + new Vector2(0f, -R * 1.1f), to = d.pos + new Vector2(0f, -R * 0.8f);
            shots.Add(new Shot { a = from, b = to, t0 = Time.unscaledTime, dur = 0.35f, magic = a.t.magic });
            yield return 0.35f;
            yield return Strike(a, d, true);
            EndTurn();
        }

        static IEnumerator Strike(Stack a, Stack d, bool ranged)
        {
            if (a.an != null && !ranged)
            {   // живой боец: клип удара, урон — на середине замаха
                a.lungeDir = (d.pos - a.pos).normalized;
                a.attackStep = Vector2.ClampMagnitude((d.pos - a.pos) * 0.43f, R * 0.78f);
                yield return Act(a, 0.45f);
            }
            else if (!ranged) { a.lungeDir = (d.pos - a.pos).normalized; a.lunge = 1f; yield return 0.18f; }
            int dmg = Damage(a, d, ranged, false);
            bool lucky = !a.enemy && Luck > 0 && Random.value < Luck / 12f;   // удача («Герои»): двойной урон
            if (lucky) { dmg *= 2; texts.Add(new Float { p = a.pos + new Vector2(0f, -R * 2.6f), s = "УДАЧА!", t0 = Time.unscaledTime, col = new Color(0.6f, 1f, 0.5f) }); }
            int before = d.count;
            Hurt(d, dmg);
            d.flash = 1f; d.hitUntil = Time.unscaledTime + 0.55f;
            if (!ranged) Burst("fx_slash_hit", d.pos + new Vector2(0f, -R * 1.1f), R * 3f, 36f);
            if (!ranged && a.t.sweep)
                foreach (Vector2Int n in Nb(a.c, a.r))
                {
                    Stack o = At(n.x, n.y);
                    if (o == null || o == d || o.enemy == a.enemy || !o.Alive) continue;
                    int od = Damage(a, o, false, false); Hurt(o, od); o.flash = 1f; o.hitUntil = Time.unscaledTime + 0.55f;
                    Burst("fx_slash_hit", o.pos + new Vector2(0f, -R * 1.1f), R * 2.6f, 36f);
                    texts.Add(new Float { p = o.pos + new Vector2(0f, -R * 2f), s = "−" + od, t0 = Time.unscaledTime, col = o.enemy ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.45f, 0.35f) });
                }
            if (ranged && !a.t.magic) Burst("fx_arrow_hit", d.pos + new Vector2(0f, -R * 1.0f), R * 2.6f, 40f);
            int dead = before - Mathf.Max(0, d.count);
            texts.Add(new Float { p = d.pos + new Vector2(0f, -R * 2f), s = "−" + dmg + (dead > 0 ? "  (" + dead + " пало)" : ""), t0 = Time.unscaledTime, col = d.enemy ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.45f, 0.35f) });
            Log((a.enemy ? "Враг: " : "") + a.t.name + " → " + d.t.name + ": " + dmg + " урона" + (dead > 0 ? ", пало " + dead : "") + ".");
            UiFx.Play("ui_taiko_heavy", ranged ? 0.3f : 0.5f);
            yield return Mathf.Max(0.3f, a.actUntil - Time.unscaledTime);
        }

        static int Damage(Stack a, Stack d, bool ranged, bool estimate)
        {
            float b = 0f;
            if (estimate) b = (a.t.dmin + a.t.dmax) * 0.5f * a.count;
            else if (a.curseRounds > 0) b = a.t.dmin * a.count;
            else if (a.count <= 10) for (int i = 0; i < a.count; i++) b += Random.Range(a.t.dmin, a.t.dmax + 1);
            else b = Random.Range(a.t.dmin, a.t.dmax + 1) * 0.2f * a.count + (a.t.dmin + a.t.dmax) * 0.5f * a.count * 0.8f;
            int A = a.Atk, D = Mathf.RoundToInt(d.Def * (1f - a.t.pierce));
            float m = A >= D ? Mathf.Min(4f, 1f + 0.05f * (A - D)) : Mathf.Max(0.3f, 1f - 0.025f * (D - A));
            if (ranged && !a.t.magic && Dist(a.c, a.r, d.c, d.r) > 10) m *= 0.5f;
            if (!ranged && a.t.shots > 0 && !a.t.noMeleePen) m *= 0.5f;   // стрелок вплотную — вполсилы
            if (!ranged && a.t.joust) m *= 1f + 0.05f * a.run;          // разгон: +5% за клетку
            if (ranged && a.enemy && fogRounds > 0) m *= 0.5f;
            if (ranged && siege && Attacker(a) && (siegeDef ? d.c < WallC : d.c > WallC) && wallHp[d.r] > 0) m *= 0.5f;   // по защитникам за целой стеной
            if (!a.enemy) m *= 1f + General.Pct(ranged ? 0 : 1) + (ranged ? General.ArtRanged : 0f);   // «Стрельба» / «Нападение», артефакты стрелков
            if (!d.enemy) m *= 1f - General.Pct(2);               // «Доспехи»
            return Mathf.Max(1, Mathf.RoundToInt(b * m));
        }

        static void Hurt(Stack d, int dmg)
        {
            int left = d.Total - dmg;
            if (left <= 0) { d.count = 0; d.topHp = 0; d.die = Time.unscaledTime; Burst("fx_death", d.pos + new Vector2(0f, -R * 0.9f), R * 3.4f, 30f); return; }
            d.count = Mathf.CeilToInt(left / (float)d.t.hp);
            d.topHp = left - (d.count - 1) * d.t.hp;
        }

        static void EndTurn()
        {
            Stack s = cur;
            if (s != null) queue.Remove(s);
            // боевой дух («Герои»): знаменосцы рядом с армией — шанс, что отряд сходит ещё раз (раз за раунд)
            if (s != null && s.Alive && !s.enemy && !s.t.undead && !s.moraleUsed && !finished && Morale > 0 && Random.value < Morale / 12f)
            {
                s.moraleUsed = true;
                queue.Insert(0, s);
                texts.Add(new Float { p = s.pos + new Vector2(0f, -R * 2.6f), s = "БОЕВОЙ ДУХ!", t0 = Time.unscaledTime, col = new Color(1f, 0.9f, 0.4f) });
                Log(s.t.name + ": боевой дух — ещё ход!");
            }
            NextTurn();
        }
        /// <summary>Боевой дух наших: живые знаменосцы (+1 за отряд, до 3).</summary>
        static int Morale
        {
            get
            {
                int m = 0; bool und = false, liv = false;
                foreach (Stack s in stacks)
                {
                    if (!s.Alive || s.enemy) continue;
                    if (s.t == Ours[9]) m += 1; else if (s.t == Ours[21]) m += 2;
                    if (s.t.undead) und = true; else liv = true;
                }
                if (und && liv) m -= 1;   // живые рядом с нежитью — дух падает
                return Mathf.Min(3, m + General.Val(3) + General.ArtMorale);
            }
        }
        /// <summary>Удача наших: храм поместья и святилище (пока — храм).</summary>
        static int Luck { get { return (fromEstate ? Estate.D.level[4] : 0) + General.Val(4) + General.ArtLuck; } }

        static void Wait()
        {
            if (cur == null || cur.waited) return;
            cur.waited = true;
            queue.Remove(cur); queue.Add(cur);
            Log(cur.t.name + " ждут.");
            NextTurn();
        }
        static void Defend()
        {
            if (cur == null) return;
            Stack s = cur;
            Log(s.t.name + " в защите.");
            queue.Remove(s); NextTurn();
            s.defending = true;   // до своего следующего хода
        }

        // ---------------------------------------------------------------- умения героя

        static void Cast(Spell sp, Stack target, Vector2Int cell)
        {
            if (castThisRound || spirit < sp.cost) return;
            spirit -= sp.cost; castThisRound = true; spellSel = -1; bookOpen = false;
            GeneralCast();
            if (target != null && target.enemy && sp.target == 0)
            {   // сопротивление и иммунитеты («Герои 3»)
                bool imm = target.t.fireImmune > 0 && (sp.icon == 4 || sp.icon == 8) || target.t.undead && (sp.icon == 9 || sp.icon == 16);
                if (imm || target.t.resist > 0f && Random.value < target.t.resist)
                {
                    texts.Add(new Float { p = target.pos + new Vector2(0f, -R * 2.4f), s = imm ? "НЕ ДЕЙСТВУЕТ" : "СОПРОТИВЛЕНИЕ", t0 = Time.unscaledTime, col = new Color(0.7f, 0.85f, 1f) });
                    Log(target.t.name + ": умение не подействовало."); UiFx.Play("ui_taiko_heavy", 0.3f);
                    if (cur != null && cur.Alive) Flood(cur);
                    return;
                }
            }
            int lv = General.Power;   // сила умений — «Сила духа» полководца
            Vector2 at = target != null ? target.pos : Center(cell.x, cell.y);
            bool sheet = false;
            if (sp.icon == 3) sheet = Burst(SpellFx[3], at + new Vector2(0f, -R * 0.6f), R * 6f, 26f);
            else if (sp.icon < SpellFx.Length && sp.target != 3) sheet = Burst(SpellFx[sp.icon], at + new Vector2(0f, -R * 0.9f), R * (sp.icon == 8 ? 5.5f : 4.2f));
            else if (sp.icon < SpellFx.Length) foreach (Stack o in stacks) if (o.Alive && o.enemy == (sp.icon == 5 || sp.icon == 12 || sp.icon == 15 || sp.icon == 20)) sheet |= Burst(SpellFx[sp.icon], o.pos + new Vector2(0f, -R * 0.9f), R * 3.5f);
            if (!sheet) casts.Add(new Float { p = at, s = "", t0 = Time.unscaledTime, icon = sp.icon });
            switch (sp.icon)
            {
                case 0: HurtSpell(target, 30 + 15 * lv); break;
                case 1: foreach (Stack s in stacks) if (s.Alive && !s.enemy) { s.atkB = 3; s.buffRounds = 2; } Log("Боевой клич: +3 к атаке наших."); break;
                case 2: target.defB = 6; target.buffRounds = 2; Log("Стена щитов: " + target.t.name + " +6 к защите."); break;
                case 3:
                {
                    List<Stack> hit = new List<Stack>();
                    Stack c0 = At(cell.x, cell.y); if (c0 != null && c0.enemy) hit.Add(c0);
                    foreach (Vector2Int n in Nb(cell.x, cell.y)) { Stack o = At(n.x, n.y); if (o != null && o.enemy) hit.Add(o); }
                    foreach (Stack o in hit) HurtSpell(o, 20 + 8 * lv);
                    if (hit.Count == 0) Log("Град стрел ушёл в землю.");
                    break;
                }
                case 4: HurtSpell(target, 45 + 12 * lv); break;
                case 5: windRounds = 2; Log("Ветер ками: враги медленнее."); break;
                case 6:
                {
                    int heal = 50 + 10 * lv, max = target.start * target.t.hp;
                    int total = Mathf.Min(max, target.Total + heal);
                    target.count = Mathf.CeilToInt(total / (float)target.t.hp); target.topHp = total - (target.count - 1) * target.t.hp;
                    texts.Add(new Float { p = target.pos + new Vector2(0f, -R * 2f), s = "+" + heal, t0 = Time.unscaledTime, col = new Color(0.5f, 1f, 0.6f) });
                    Log("Исцеление: " + target.t.name + " — теперь " + target.count + ".");
                    break;
                }
                case 7: fogRounds = 2; Log("Туман: стрелки врага бьют вполсилы."); break;
                case 8: HurtSpell(target, 70 + 20 * lv); break;
                case 9: target.curseRounds = 2; Log("Проклятие на " + target.t.name + "."); break;
                case 10: target.spdB = 3; target.buffRounds = 1; Log("Рывок: " + target.t.name + " быстрее."); break;
                case 11: HurtSpell(target, 35 + 12 * lv); target.spdB = -3; target.buffRounds = 2; break;
                case 12:
                    foreach (Stack o in new List<Stack>(stacks)) if (o.Alive && o.enemy) HurtSpell(o, 15 + 6 * lv);
                    if (siege) for (int r = 0; r < Rows; r++) if (wallHp[r] > 0 && Random.value < 0.35f) { wallHp[r]--; Log("Землетрясение: стена дрогнула."); }
                    break;
                case 13: target.atkB = 6; target.defB = -3; target.buffRounds = 2; Log("Кровавое безумие: " + target.t.name + "."); break;
                case 14: foreach (Stack o in stacks) if (o.Alive && !o.enemy) { o.defB = 4; o.buffRounds = 2; } Log("Щит духа: +4 к защите наших."); break;
                case 15:
                    for (int q = 0; q < 3; q++)
                    {
                        List<Stack> fs = stacks.FindAll(o => o.Alive && o.enemy); if (fs.Count == 0) break;
                        HurtSpell(fs[Random.Range(0, fs.Count)], 18 + 7 * lv);
                    }
                    break;
                case 16: target.retaliated = true; target.atkB = -3; target.buffRounds = 2; Log("Туман страха: " + target.t.name + " не ответит."); break;
                case 17:
                {
                    HurtSpell(target, 45 + 14 * lv);
                    List<Stack> near = stacks.FindAll(o => o.Alive && o.enemy && o != target);
                    near.Sort((a, b) => Dist(a.c, a.r, target.c, target.r).CompareTo(Dist(b.c, b.r, target.c, target.r)));
                    for (int q = 0; q < 2 && q < near.Count; q++) { HurtSpell(near[q], (45 + 14 * lv) / 2); Burst("fx_thunder", near[q].pos + new Vector2(0f, -R * 0.9f), R * 4f); }
                    break;
                }
                case 18: foreach (Stack o in stacks) if (o.Alive && !o.enemy) { int h = Mathf.Min(o.t.hp - o.topHp, 25 + 8 * lv); o.topHp += h; if (h > 0) texts.Add(new Float { p = o.pos + new Vector2(0f, -R * 2f), s = "+" + h, t0 = Time.unscaledTime, col = new Color(0.5f, 1f, 0.6f) }); } Log("Целебный дождь."); break;
                case 19: target.spdB = 5; target.buffRounds = 1; Log("Врата теней: " + target.t.name + " быстрее."); break;
                case 20: windRounds = Mathf.Max(windRounds, 2); foreach (Stack o in stacks) if (o.Alive && o.enemy) { o.spdB = -1; o.buffRounds = 2; } Log("Время замедлилось для врагов."); break;
                case 21: target.atkB = 10; target.defB = -Mathf.CeilToInt(target.t.def * 0.5f); target.buffRounds = 1; Log("Берсерк: " + target.t.name + "."); break;
                case 22:
                {
                    int heal = 120 + 30 * lv, max = target.start * target.t.hp;
                    int total = Mathf.Min(max, target.Total + heal);
                    target.count = Mathf.CeilToInt(total / (float)target.t.hp); target.topHp = total - (target.count - 1) * target.t.hp;
                    texts.Add(new Float { p = target.pos + new Vector2(0f, -R * 2f), s = "+" + heal, t0 = Time.unscaledTime, col = new Color(0.5f, 1f, 0.6f) });
                    Log("Павшие встают: " + target.t.name + " — теперь " + target.count + ".");
                    break;
                }
            }
            UiFx.Play("ui_taiko_heavy", 0.6f);
            if (cur != null && cur.Alive) Flood(cur);
            if (CheckEnd()) return;
            queue.RemoveAll(s => !s.Alive);
            if (cur != null && !cur.Alive) NextTurn();
        }
        static void HurtSpell(Stack s, int dmg)
        {
            int before = s.count;
            Hurt(s, dmg); s.flash = 1f;
            int dead = before - Mathf.Max(0, s.count);
            texts.Add(new Float { p = s.pos + new Vector2(0f, -R * 2f), s = "−" + dmg + (dead > 0 ? "  (" + dead + " пало)" : ""), t0 = Time.unscaledTime, col = new Color(1f, 0.85f, 0.4f) });
            Log("Умение героя: " + s.t.name + " −" + dmg + (dead > 0 ? ", пало " + dead : "") + ".");
        }

        // ---------------------------------------------------------------- ход врага (и автобой)

        static IEnumerator AiTurn(Stack s)
        {
            yield return 0.35f;
            List<Stack> foes = new List<Stack>();
            foreach (Stack o in stacks) if (o.Alive && o.enemy != s.enemy) foes.Add(o);
            if (foes.Count == 0) { EndTurn(); yield break; }
            if (CanShoot(s))
            {
                Stack best = null; float bv = -1f;
                foreach (Stack o in foes) { float v = Damage(s, o, true, true) + o.Atk * o.count * 0.5f; if (v > bv) { bv = v; best = o; } }
                yield return DoShoot(s, best);
                yield break;
            }
            // ударить, кого можно, — того, кому больнее
            Stack target = null; Vector2Int stand = new Vector2Int(-1, -1); float tv = -1f;
            foreach (Stack o in foes)
                foreach (Vector2Int n in Around(o))
                {
                    if (!reach.ContainsKey(n)) continue;
                    float v = Mathf.Min(Damage(s, o, false, true), o.Total) * 2f + o.Atk * o.count * 0.3f - reach[n] * 0.1f;
                    if (v > tv) { tv = v; target = o; stand = n; }
                }
            if (target != null) { yield return DoMelee(s, target, stand); yield break; }
            if (siege && Attacker(s)) for (int r = 0; r < Rows; r++) if (WallAt(WallC, r) && NextToWall(s, r)) { yield return DoWallHit(s, r); yield break; }
            // иначе — к ближайшему
            Stack near = null; int nd = int.MaxValue;
            foreach (Stack o in foes) { int d = Dist(s.c, s.r, o.c, o.r); if (d < nd) { nd = d; near = o; } }
            Vector2Int go = new Vector2Int(s.c, s.r); int gd = nd;
            foreach (KeyValuePair<Vector2Int, int> kv in reach)
            {
                int d = Dist(kv.Key.x, kv.Key.y, near.c, near.r);
                if (d < gd || d == gd && kv.Value > reach[go]) { gd = d; go = kv.Key; }
            }
            if (go.x == s.c && go.y == s.r) { Defend(); yield break; }
            yield return DoMove(s, go);
        }

        // ---------------------------------------------------------------- картинки бойцов (снимки 3D-моделей)

        static readonly Dictionary<int, Texture2D> snaps = new Dictionary<int, Texture2D>();
        static bool snapsTaken;
        static Texture2D Snap(int model)
        {
            Texture2D t; return snaps.TryGetValue(model, out t) ? t : null;
        }

        /// <summary>Снять облики, нужные в бою: модель — на отдельном слое, фон магента, магента — в прозрачность.</summary>
        static void TakeSnaps()
        {
            snapsTaken = true;
            DuelManager dm = DuelManager.Instance;
            if (dm == null) return;
            HashSet<int> need = new HashSet<int>();
            foreach (Stack s in stacks) if (s.go == null && s.t.atlasCell < 0 && s.t.model > -99 && !snaps.ContainsKey(s.t.model)) need.Add(s.t.model);
            if (need.Count == 0) return;
            const int SW = 192, SH = 288, Layer = 30;
            RenderTexture rt = RenderTexture.GetTemporary(SW, SH, 24, RenderTextureFormat.ARGB32);
            GameObject cg = new GameObject("TacticsSnapCam");
            Camera cam = cg.AddComponent<Camera>();
            cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(1f, 0f, 1f, 1f);
            cam.cullingMask = 1 << Layer; cam.fieldOfView = 24f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 30f; cam.targetTexture = rt;
            EnemySkins es = dm.enemy != null ? dm.enemy.GetComponent<EnemySkins>() : null;
            PlayerSkins ps = dm.player != null ? dm.player.GetComponent<PlayerSkins>() : null;
            int esWas = es != null ? es.Active : 0;
            RenderTexture keep = RenderTexture.active;
            foreach (int m in need)
            {
                FighterController f = m >= 0 ? dm.enemy : dm.player;
                if (f == null || !f.gameObject.activeInHierarchy) continue;
                if (m >= 0) { if (es == null || m >= es.models.Length || es.models[m] == null) continue; es.Apply(m); }
                else { int pi = -m - 1; if (ps == null || ps.models == null || pi >= ps.models.Length || ps.models[pi] == null) continue; ps.Apply(pi); }
                Transform tr = f.transform;
                Vector3 target = tr.position + Vector3.up * 0.95f;
                Vector3 dir = (tr.right * 0.86f + tr.forward * 0.5f + Vector3.up * 0.32f).normalized;
                cam.transform.position = target + dir * 6.2f;
                cam.transform.LookAt(target);
                Renderer[] rs = f.GetComponentsInChildren<Renderer>();
                int[] was = new int[rs.Length];
                for (int i = 0; i < rs.Length; i++) { was[i] = rs[i].gameObject.layer; rs[i].gameObject.layer = Layer; }
                cam.Render();
                for (int i = 0; i < rs.Length; i++) if (rs[i] != null) rs[i].gameObject.layer = was[i];
                RenderTexture.active = rt;
                Texture2D t = new Texture2D(SW, SH, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, SW, SH), 0, 0);
                Color32[] px = t.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {   // магента → прозрачность, край — без розовой каймы
                    Color32 c = px[i];
                    int mg = Mathf.Min(c.r, c.b) - c.g;
                    if (mg > 150) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    if (mg > 40)
                    {
                        float a = 1f - (mg - 40) / 110f;
                        byte g = c.g;
                        px[i] = new Color32((byte)Mathf.Min(c.r, g + 40), g, (byte)Mathf.Min(c.b, g + 40), (byte)(a * 255));
                    }
                }
                t.SetPixels32(px); t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
                snaps[m] = t;
            }
            RenderTexture.active = keep;
            cam.targetTexture = null; Object.Destroy(cg); RenderTexture.ReleaseTemporary(rt);
            if (es != null) es.Apply(esWas);
            if (ps != null && !CharacterLook.ApplyTo(ps)) ps.Apply(JourneyChoice.Hero);
        }

        // ---------------------------------------------------------------- живые бойцы (3D-модели с клипами Meshy)
        // Сцена далеко внизу (слой 30), ортокамера под 34° — как сплющены клетки (Sq = sin 34°): клетка на поле = клетка на земле.
        // Камера рисует в прозрачную текстуру поверх картины поля; бойцы стоят на земле там, где их клетка на экране.

        const int LiveLayer = 30;
        static readonly Vector3 StageAt = new Vector3(0f, -1000f, 0f);
        static GameObject stage; static Camera liveCam; static RenderTexture liveRT;
        static float PPM { get { return R * 2.45f / (1.7f * 0.829f); } }   // точек на метр (рост 1,7 м = как снимок)

        static IEnumerator Act(Stack s, float impact)
        {
            float duration = ClipLen(s, s.clipAct, 0.7f);
            s.actUntil = Time.unscaledTime + duration;
            s.actStart = Time.unscaledTime;
            impact = ContactPhase(s.clipAct, impact);
            s.contactAt = s.actStart + duration * impact;
            if (s.clipAct != null) Play(s, s.clipAct, true);
            else Play(s, s.clipIdle, false);
            yield return duration * impact;
        }
        static float ClipLen(Stack s, string c, float def)
        {
            if (s.an == null || c == null || s.an[c] == null) return def;
            return Mathf.Clamp(s.an[c].length, 0.3f, 2.2f);
        }
        /// <summary>Угол, на который клип стойки поворачивает плечи относительно позы покоя (вокруг вертикали).</summary>
        static float ChestYaw(Stack s)
        {
            if (s.an == null || s.clipIdle == null) return 0f;
            Transform l = null, r = null;
            foreach (Transform t in s.go.GetComponentsInChildren<Transform>(true))
            {
                if (l == null && t.name.EndsWith("LeftArm")) l = t;
                if (r == null && t.name.EndsWith("RightArm")) r = t;
            }
            if (l == null || r == null) return 0f;
            Transform root = s.go.transform;
            Vector3 rest = root.InverseTransformVector(r.position - l.position);
            AnimationState st = s.an[s.clipIdle];
            st.enabled = true; st.weight = 1f; st.time = st.length * 0.3f; s.an.Sample();
            Vector3 pose = root.InverseTransformVector(r.position - l.position);
            st.enabled = false;
            rest.y = 0f; pose.y = 0f;
            if (rest.sqrMagnitude < 1e-6f || pose.sqrMagnitude < 1e-6f) return 0f;
            return Mathf.Clamp(Vector3.SignedAngle(rest, pose, Vector3.up), -70f, 70f);
        }

        static void Play(Stack s, string clip, bool restart)
        {
            if (s.an == null || clip == null || s.an[clip] == null) return;
            if (s.playing == clip && !restart) return;
            if (s.previousClip != null) s.an[s.previousClip].enabled = false;
            s.previousClip = clip != s.playing ? s.playing : null;
            s.previousStart = s.clipStart;
            s.playing = clip;
            s.clipStart = Time.unscaledTime;
        }

        // The board advances on unscaled time, including while the duel is paused.
        static void SamplePose(Stack s, float now)
        {
            if (s.an == null || s.playing == null) return;
            AnimationState current = s.an[s.playing];
            float elapsed = now - s.clipStart;
            float blend = s.previousClip == null ? 1f : Mathf.Clamp01(elapsed / 0.12f);
            if (s.previousClip != null)
            {
                AnimationState previous = s.an[s.previousClip];
                previous.enabled = blend < 1f;
                previous.weight = 1f - blend;
                previous.time = PoseTime(s, previous, now - s.previousStart);
                if (blend >= 1f) s.previousClip = null;
            }
            current.enabled = true;
            current.weight = blend;
            current.time = PoseTime(s, current, elapsed);
            s.an.Sample();
            // The hex path owns horizontal travel. Keep vertical motion from the gait.
            if (s.hips != null && s.playing == s.clipWalk && (s.figureProfile == null || s.figureProfile.advance == null))
            {
                Vector3 local = s.go.transform.InverseTransformPoint(s.hips.position);
                local.x = Mathf.Lerp(local.x, s.restHips.x, blend);
                local.z = Mathf.Lerp(local.z, s.restHips.z, blend);
                s.hips.position = s.go.transform.TransformPoint(local);
            }
        }

        static float PoseTime(Stack s, AnimationState clip, float elapsed)
        {
            if (clip.length <= 0f) return 0f;
            if (clip.name == s.clipWalk)
            {
                if (s.figureProfile != null && s.figureProfile.advance != null) return s.gaitPhase * clip.length;
                float stride = s.t.joust ? 2.3f : s.t.cell == 11 ? 1.05f : 1.45f;
                float cycles = elapsed * BoardSpeed(s) / (PPM * stride);
                return Mathf.Repeat(cycles, 1f) * clip.length;
            }
            if (clip.wrapMode == WrapMode.Loop) return Mathf.Repeat(elapsed, clip.length);
            if (clip.name == s.clipAct)
                elapsed *= clip.length / ClipLen(s, s.clipAct, 1f);
            return Mathf.Clamp(elapsed, 0f, clip.length);
        }

        // Fit the visible surface in its combat pose. Imported skin bounds include other clips,
        // and head pivots sit at different heights inside different helmets.
        static Transform SpawnFigure(GameObject prefab, Transform anchor)
        {
            Transform mount = new GameObject("Figure").transform;
            mount.SetParent(anchor, false);
            Object.Instantiate(prefab, mount, false);
            return mount;
        }

        static void FitFigure(GameObject anchor, Transform figure, float height)
        {
            var ys = new List<float>();
            Mesh snapshot = new Mesh();
            foreach (var skin in figure.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.BakeMesh(snapshot);
                Matrix4x4 toAnchor = anchor.transform.worldToLocalMatrix * skin.localToWorldMatrix;
                foreach (Vector3 v in snapshot.vertices) ys.Add(toAnchor.MultiplyPoint3x4(v).y);
            }
            Object.Destroy(snapshot);
            if (ys.Count == 0) return;
            ys.Sort();
            float sole = ys[ys.Count / 500], top = ys[ys.Count - 1 - ys.Count / 200];
            if (top <= sole + .001f) return;
            float k = height / (top - sole);
            figure.localScale *= k;
            figure.localPosition = figure.localPosition * k - Vector3.up * (sole * k);
        }

        static float ContactPhase(string clip, float fallback)
        {
            string n = (clip ?? "").ToLowerInvariant();
            if (n.Contains("right_hand_sword_slash")) return .36f;
            if (n.Contains("thrust_slash") || n.Contains("double_combo_attack") || n.Contains("left_slash")) return .24f;
            if (n.Contains("side_shot")) return .73f;
            return fallback;
        }

        static void SetupLive()
        {
            CleanupLive();
            bool any = Resources.Load<GameObject>("Units/unit_general") != null;   // полководец на коне у края поля («Герои 3»)
            foreach (Stack s in stacks) if (!string.IsNullOrEmpty(s.t.live) && Resources.Load<GameObject>("Units/" + s.t.live) != null) any = true;
            if (!any) return;
            stage = new GameObject("TacticsStage");
            stage.transform.position = StageAt;
            GameObject cg = new GameObject("TacticsCam"); cg.transform.SetParent(stage.transform, false);
            liveCam = cg.AddComponent<Camera>();
            liveCam.enabled = false; liveCam.orthographic = true; liveCam.useOcclusionCulling = false; liveCam.allowHDR = false;
            liveCam.clearFlags = CameraClearFlags.SolidColor; liveCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            liveCam.cullingMask = 1 << LiveLayer; liveCam.nearClipPlane = 0.1f; liveCam.farClipPlane = 200f; liveCam.allowMSAA = false;
            GameObject lg = new GameObject("TacticsSun"); lg.transform.SetParent(stage.transform, false);
            Light l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.15f; l.color = new Color(1f, 0.93f, 0.82f);
            l.cullingMask = 1 << LiveLayer; lg.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            SpawnGeneral();
            foreach (Stack s in stacks)
            {
                if (string.IsNullOrEmpty(s.t.live)) continue;
                string resource = s.t.live;
                GameObject pf = Resources.Load<GameObject>("Units/" + resource);
                if (pf == null && s.t.up && s.t.cell >= 0 && s.t.cell < BaseUnits)
                {
                    resource = Ours[s.t.cell].live;
                    pf = Resources.Load<GameObject>("Units/" + resource);
                }
                if (pf == null) continue;
                GameObject go = new GameObject("Unit_" + s.t.live);
                go.transform.SetParent(stage.transform, false);
                Transform figure = SpawnFigure(pf, go.transform);
                foreach (Transform tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = LiveLayer;
                Texture2D tex = Resources.Load<Texture2D>("Units/" + resource + "_albedo");
                Shader sh = Resources.Load<Shader>("TacticsUnit");
                s.surface = new Material(sh != null ? sh : Shader.Find("Standard"));
                if (tex != null) s.surface.mainTexture = tex;
                TacticsFigureProfile.Dress(go,s.surface);
                s.figureMount = figure;
                s.figureProfile = Resources.Load<TacticsFigureProfile>("ArmyFigures/" + resource);
                s.go = go; s.an = go.GetComponentInChildren<Animation>();
                bool dog = s.t.cell == 11;
                if (s.figureProfile != null) s.figureProfile.Fit(figure);
                else if (dog)
                {
                    // Retain the approved dog rig, scale and cycle.
                    Bounds bounds = new Bounds(); bool first = true;
                    foreach (Renderer r in figure.GetComponentsInChildren<Renderer>())
                    {
                        if (first) bounds = r.bounds; else bounds.Encapsulate(r.bounds);
                        first = false;
                    }
                    if (bounds.size.y > .01f) go.transform.localScale *= s.t.liveH / bounds.size.y;
                }
                if (s.an != null)
                {
                    s.an.Stop();
                    s.an.playAutomatically = false;
                    s.an.cullingType = AnimationCullingType.AlwaysAnimate;
                    List<string> names = new List<string>();
                    foreach (AnimationState st in s.an) names.Add(st.name);
                    // по очереди, чтобы «Shot_and_Fall» был смертью, а «Gunshot_Reaction» — попаданием, а не выстрелом
                    s.clipDie = Find(names, "dying", "dead", "death", "fall", "knock");
                    List<string> left = new List<string>(names); left.Remove(s.clipDie);
                    s.clipHit = Find(left, "reaction", "hit"); left.Remove(s.clipHit);
                    s.clipWin = Find(left, "victory", "cheer", "howl", "idle1"); left.Remove(s.clipWin);   // «wolf_idle1» — вой пса
                    s.clipIdle = Find(left, "idle", "stance"); left.Remove(s.clipIdle);
                    s.clipWalk = Find(left, "walk", "canter", "run"); left.Remove(s.clipWalk);
                    s.clipAct = Find(left, "shot", "shoot", "slash", "thrust", "spell", "cast", "attack", "combo", "jump");
                    if (s.clipAct == null) s.clipAct = Find(left, "bite", "strike", "swing", "punch", "kick", "fire");
                    if (s.clipIdle == null && names.Count > 0) s.clipIdle = names[0];
                    foreach (AnimationState st in s.an)
                    {
                        st.speed = 0f;
                        st.wrapMode = st.name == s.clipIdle || st.name == s.clipWalk ? WrapMode.Loop : st.name == s.clipDie ? WrapMode.ClampForever : WrapMode.Once;
                    }
                    s.idleYaw = ChestYaw(s);
                    if (s.clipIdle != null)
                        s.an.GetClip(s.clipIdle).SampleAnimation(s.an.gameObject, s.an[s.clipIdle].length * .3f);
                    if (!dog && s.figureProfile == null)
                    {
                        float height = s.t.joust ? 1.95f : resource == "unit_oni" ? 1.7f : 1.40f;
                        FitFigure(go, figure, height);
                    }
                    foreach (Transform bone in figure.GetComponentsInChildren<Transform>())
                    {
                        if (bone.name == "Hips") { s.hips = bone; s.restHips = go.transform.InverseTransformPoint(bone.position); }
                        if (bone.name == "TenguWingL") { s.wingL = bone; s.wingRestL = bone.localRotation; }
                        if (bone.name == "TenguWingR") { s.wingR = bone; s.wingRestR = bone.localRotation; }
                    }
                    if (s.figureProfile != null && s.figureProfile.advance != null)
                    {
                        var clip = s.figureProfile.advance;
                        s.an.AddClip(clip, clip.name);
                        s.clipWalk = clip.name;
                        var state = s.an[s.clipWalk];
                        state.speed = 0f;
                        state.wrapMode = WrapMode.Loop;
                    }
                    Play(s, s.clipIdle, true);
                }
            }
        }
        static string Find(List<string> names, params string[] keys)
        {
            foreach (string k in keys) { if (k == null) continue; foreach (string n in names) if (n.Substring(n.LastIndexOf('|') + 1).ToLowerInvariant().Contains(k)) return n; }
            return null;
        }
        static Material generalMaterial;
        static GameObject generalGo; static Animation generalAn; static string genIdle, genCast;
        static Vector2 GeneralPos() { Vector2 c = Center(0, 1); return new Vector2(ox - R * 2.3f, c.y); }
        static void SpawnGeneral()
        {
            GameObject pf = Resources.Load<GameObject>("Units/unit_general");
            if (pf == null || stage == null) return;
            generalGo = new GameObject("General");
            generalGo.transform.SetParent(stage.transform, false);
            Transform figure = SpawnFigure(pf, generalGo.transform);
            foreach (Transform tr in generalGo.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = LiveLayer;
            Texture2D tex = Resources.Load<Texture2D>("Units/unit_general_albedo");
            generalMaterial = new Material(Resources.Load<Shader>("TacticsUnit"));
            generalMaterial.mainTexture = tex;
            TacticsFigureProfile.Dress(generalGo,generalMaterial);
            generalAn = generalGo.GetComponentInChildren<Animation>();
            genIdle = genCast = null;
            if (generalAn != null)
            {
                generalAn.cullingType = AnimationCullingType.AlwaysAnimate;
                foreach (AnimationState st in generalAn)
                {
                    string n = st.name.ToLower();
                    if (n.Contains("idle")) genIdle = st.name; else if (n.Contains("jump")) genCast = st.name;
                    st.wrapMode = n.Contains("idle") ? WrapMode.Loop : WrapMode.Once;
                }
                if (genIdle != null)
                {
                    generalAn.GetClip(genIdle).SampleAnimation(generalAn.gameObject, 0f);
                    generalAn.Play(genIdle);
                }
            }
            var profile = Resources.Load<TacticsFigureProfile>("ArmyFigures/unit_general");
            if (profile != null) profile.Fit(figure,90f);
            else FitFigure(generalGo, figure, 2.05f);
        }
        /// <summary>Полководец колдует — конь встаёт на дыбы.</summary>
        static void GeneralCast()
        {
            if (generalAn == null || genCast == null) return;
            generalAn[genCast].time = 0f; generalAn.Play(genCast);
            if (genIdle != null) generalAn.CrossFadeQueued(genIdle, 0.25f, QueueMode.CompleteOthers);
        }

        static void CleanupLive()
        {
            if (generalMaterial != null) Object.Destroy(generalMaterial);
            generalGo = null; generalAn = null;
            if (stage != null) Object.Destroy(stage);
            stage = null; liveCam = null;
            if (liveRT != null) { liveRT.Release(); Object.Destroy(liveRT); liveRT = null; }
            foreach (Stack s in stacks) { if (s.surface != null) Object.Destroy(s.surface); s.surface = null; s.go = null; s.an = null; s.figureProfile = null; s.figureMount = null; s.gaitPhase = 0f; s.wingL = s.wingR = null; s.hips = null; s.playing = null; s.previousClip = null; }
        }
        static void Close() { CleanupLive(); Open = false; }

        static Vector2 UnitPosition(Stack s)
        {
            float now = Time.unscaledTime;
            if (s.Alive && s.an != null && now < s.actUntil)
            {
                float weight = now < s.contactAt
                    ? Mathf.InverseLerp(s.actStart, s.contactAt, now)
                    : 1f - Mathf.InverseLerp(s.contactAt, s.actUntil, now);
                return s.pos + s.attackStep * Mathf.SmoothStep(0f, 1f, weight);
            }
            return s.pos + s.lungeDir * Mathf.Sin(s.lunge * Mathf.PI) * R * 0.6f;
        }

        static Vector3 Ground(Vector2 v)
        {
            Ray ray = liveCam.ViewportPointToRay(new Vector3(v.x / W, 1f - v.y / 540f, 0f));
            float t = (StageAt.y - ray.origin.y) / ray.direction.y;
            return ray.origin + ray.direction * t;
        }

        /// <summary>Кадр живых бойцов: где стоят, куда смотрят, какой клип; камера — в прозрачную текстуру.</summary>
        static void DrawLive()
        {
            if (liveCam == null || Event.current.type != EventType.Repaint) return;
            int pw = Mathf.Max(64, Mathf.RoundToInt(W * UiKit.Scale)), ph = Mathf.Max(64, Mathf.RoundToInt(540f * UiKit.Scale));
            if (liveRT == null || liveRT.width != pw || liveRT.height != ph)
            {
                if (liveRT != null) { liveRT.Release(); Object.Destroy(liveRT); }
                liveRT = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32); liveRT.antiAliasing = 1;
            }
            liveCam.targetTexture = liveRT;
            liveCam.orthographicSize = 270f / PPM;
            liveCam.aspect = W / 540f;
            liveCam.transform.rotation = Quaternion.Euler(34f, 0f, 0f);
            Vector3 centerGround = StageAt;
            liveCam.transform.position = centerGround - liveCam.transform.forward * 60f;
            float now = Time.unscaledTime;
            if (generalGo != null)
            {
                generalGo.transform.position = Ground(GeneralPos());
                generalGo.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            }
            foreach (Stack s in stacks)
            {
                if (s.go == null) continue;
                bool dead = !s.Alive;
                if (dead && s.die > 0f && now - s.die > 6f) { s.go.SetActive(false); continue; }   // тело лежит — потом убирается
                Vector2 p = UnitPosition(s);
                s.go.transform.position = Ground(p);
                // направление: на ходу — по пути, иначе — к врагам
                Vector2 dir = s.path.Count > 0 ? s.path[0] - s.pos : new Vector2(s.enemy ? -1f : 1f, 0f);   // армии смотрят строго друг на друга (владелец)
                if (s.actUntil > now && s.lungeDir.sqrMagnitude > 0.01f) dir = s.lungeDir;
                if (dir.sqrMagnitude > 0.01f)
                {
                    Vector3 wd = new Vector3(dir.x, 0f, -dir.y / 0.559f);
                    Quaternion want = Quaternion.LookRotation(wd.normalized, Vector3.up);
                    if (s.path.Count == 0 && s.actUntil <= now && s.playing == s.clipIdle) want *= Quaternion.Euler(0f, -s.idleYaw * 0.85f, 0f);   // в стойке грудью к врагу
                    s.go.transform.rotation = Quaternion.Slerp(s.go.transform.rotation, want, Mathf.Min(1f, Time.unscaledDeltaTime * 10f));
                }
                // вспышка попадания — цвет материала (у снимков — цвет картинки)
                if (s.surface != null) s.surface.color = Color.Lerp(s.enemy ? new Color(1.05f, 0.95f, 0.92f) : new Color(1.12f, 1.1f, 1.06f), new Color(1.6f, 0.45f, 0.4f), s.flash);
                if (dead && s.clipDie == null)
                {   // клипа смерти нет (конь): заваливается набок и уходит в землю
                    float k = Mathf.Clamp01((now - s.die) / 0.9f);
                    s.go.transform.position += Vector3.down * Mathf.Clamp01((now - s.die - 2f) / 3f) * s.t.liveH * 0.6f;
                    Transform tr = s.go.transform;
                    tr.rotation = Quaternion.AngleAxis(-82f * k * k, tr.forward) * Quaternion.LookRotation(tr.forward, Vector3.up);
                }
                else if (dead) Play(s, s.clipDie, s.playing != s.clipDie);
                else if (s.path.Count > 0) Play(s, s.t.fly ? s.clipIdle : s.clipWalk ?? s.clipIdle, false);
                else if (s.actUntil > now) { }
                else if (s.hitUntil > now && s.clipHit != null) { if (s.playing != s.clipHit) Play(s, s.clipHit, true); }
                else if (finished && won && !s.enemy && s.clipWin != null) Play(s, s.clipWin, false);
                else Play(s, s.clipIdle, false);
                if (s.figureProfile != null) s.figureProfile.Fit(s.figureMount,s.go.transform.eulerAngles.y);
                SamplePose(s, now);
                if (s.wingL != null && s.wingR != null)
                {
                    s.wingLift = Mathf.MoveTowards(s.wingLift, !dead && s.path.Count > 0 ? 1f : 0f, Time.unscaledDeltaTime * 4f);
                    float beat = Mathf.Sin((now - s.born) * 8f);
                    float angle = (8f + 23f * beat) * s.wingLift;
                    s.wingL.localRotation = s.wingRestL * Quaternion.Euler(0f, 0f, angle);
                    s.wingR.localRotation = s.wingRestR * Quaternion.Euler(0f, 0f, -angle);
                    s.go.transform.position += Vector3.up * ((.28f + .045f * beat) * s.wingLift);
                }
            }
            liveCam.Render();
            Color keepColor = GUI.color; GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, W, 540f), liveRT, ScaleMode.StretchToFill, true);
            GUI.color = keepColor;
        }

        // ---------------------------------------------------------------- рисование

        static readonly Color Gold = new Color(0.9f, 0.76f, 0.47f), Ink = new Color(0.93f, 0.89f, 0.8f), Dim = new Color(0.68f, 0.64f, 0.56f);
        struct Shot { public Vector2 a, b; public float t0, dur; public bool magic; }
        class Float { public Vector2 p; public string s; public float t0; public Color col = Color.white; public int icon = -1; }
        static readonly List<Shot> shots = new List<Shot>();

        // покадровые эффекты (листы 8×6 = 48 кадров на прозрачном — из генераций владельца, UI/Battle/fx_<имя>):
        // кадр рисуется целиком, следующий — поверх с долей (без мигания), как у водопадов карты
        class Fx { public string sheet; public Vector2 p; public float t0, size, fps = 30f; public bool loop; }
        static readonly List<Fx> fxs = new List<Fx>();
        static readonly string[] SpellFx = { "fx_cross", "fx_warcry", "fx_shield", "fx_arrows", "fx_fire", "fx_wind", "fx_heal", "fx_fog", "fx_thunder", "fx_curse", "fx_dash",
            "fx_wind", "fx_arrow_hit", "fx_warcry", "fx_shield", "fx_fire", "fx_curse", "fx_thunder", "fx_heal", "fx_dash", "fx_wind", "fx_warcry", "fx_heal" };
        /// <summary>Эффект в точке поля; false — листа нет (тогда рисуется простой эффект).</summary>
        static bool Burst(string sheet, Vector2 p, float size, float fps = 30f)
        {
            if (string.IsNullOrEmpty(sheet) || UiKit.Tex("UI/Battle/" + sheet) == null) return false;
            fxs.Add(new Fx { sheet = sheet, p = p, t0 = Time.unscaledTime, size = size, fps = fps });
            return true;
        }
        static Rect FxCell(int i) { int c = i % 8, r = i / 8; return new Rect(c / 8f, 1f - (r + 1) / 6f, 1f / 8f, 1f / 6f); }
        static void DrawSheet(Texture2D t, Rect r, float frame, float alpha, bool loop)
        {
            int i0 = Mathf.FloorToInt(frame); float k = frame - i0;
            int i1 = loop ? (i0 + 1) % 48 : Mathf.Min(47, i0 + 1);
            i0 = loop ? i0 % 48 : Mathf.Min(47, i0);
            Color oc = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha); GUI.DrawTextureWithTexCoords(r, t, FxCell(i0));
            GUI.color = new Color(1f, 1f, 1f, alpha * k); GUI.DrawTextureWithTexCoords(r, t, FxCell(i1));
            GUI.color = oc;
        }
        static void DrawFxSheets()
        {
            if (Event.current.type != EventType.Repaint) return;
            float now = Time.unscaledTime;
            // туман над полем врага, пока действует
            Texture2D fog = fogRounds > 0 ? UiKit.Tex("UI/Battle/fx_fog") : null;
            if (fog != null)
                for (int i = 0; i < 3; i++)
                {
                    float w = R * 9f, h = w * 170f / 192f;
                    Vector2 c = Center(Cols - 3, 2 + i * 3) + new Vector2(Mathf.Sin(now * 0.2f + i) * R, 0f);
                    DrawSheet(fog, new Rect(c.x - w * 0.5f, c.y - h * 0.6f, w, h), (now * 12f + i * 16f), 0.55f, true);
                }
            for (int i = fxs.Count - 1; i >= 0; i--)
            {
                Fx f = fxs[i];
                float fr = (now - f.t0) * f.fps;
                if (fr >= 47f) { fxs.RemoveAt(i); continue; }
                Texture2D t = UiKit.Tex("UI/Battle/" + f.sheet);
                if (t == null) { fxs.RemoveAt(i); continue; }
                float h = f.size * 170f / 192f;
                DrawSheet(t, new Rect(f.p.x - f.size * 0.5f, f.p.y - h * 0.5f, f.size, h), fr, 1f, false);
            }
        }
        static readonly List<Float> texts = new List<Float>(), casts = new List<Float>();
        static readonly List<string> log = new List<string>();
        static void Log(string s) { log.Add(s); if (log.Count > 4) log.RemoveAt(0); }
        static Stack info;

        public static void Draw(float w)
        {
            W = w;
            R = Mathf.Min(W * 0.86f / 23f, 380f / (11.5f * 1.7320508f * Sq));
            hw = 1.5f * R; hh = 1.7320508f * R * Sq;
            ox = (W - (hw * (Cols - 1) + 2f * R)) * 0.5f + R; oy = 88f + hh * 0.5f;
            Event e = Event.current;
            if (e.type == EventType.Repaint)
            {
                if (!snapsTaken) { SetupLive(); TakeSnaps(); }
                Animate();
                Step();
                if (!deploy && !Busy && !finished && cur != null && (cur.enemy || autoOurs)) Run(AiTurn(cur));
            }

            Texture2D bg = UiKit.Tex("UI/Battle/battle_field_" + region);
            if (bg != null) GUI.DrawTexture(new Rect(0f, 0f, W, 540f), bg, ScaleMode.ScaleAndCrop);
            else UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0.12f, 0.14f, 0.1f));

            DrawGrid();
            DrawRocks();
            DrawWall();
            DrawMachines();
            foreach (Stack stack in stacks)
                if (stack.Alive) UiKit.Disc(UnitPosition(stack) + new Vector2(0f, 2f), R * .5f, new Color(0f, 0f, 0f, .28f));
            DrawLive();
            DrawStacks();
            DrawFx();
            DrawFxSheets();
            DrawQueue();
            DrawHud();
            DrawBallistaButton();
            if (deploy && !finished)
            {
                UiKit.Text(new Rect(0f, 70f, W, 22f), "РАССТАНОВКА («Тактика»): коснись отряда, затем клетки в синих столбцах", 13f, new Color(0.6f, 0.85f, 1f));
                if (VisualIdentity.KitButton(new Rect(W * 0.5f - 80f, 96f, 160f, 34f), "В БОЙ", true, true)) { deploy = false; deploySel = null; if (cur != null && !cur.enemy) Flood(cur); }
            }
            if (bookOpen) DrawBook();
            if (info != null) DrawInfo();
            if (finished) DrawResult();
            else if (!bookOpen && info == null) HandleInput();
        }

        static float BoardSpeed(Stack s)
        {
            return R * Mathf.Lerp(2.8f, 4.2f, Mathf.InverseLerp(3f, 11f, s.Spd));
        }

        static void Animate()
        {
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            foreach (Stack s in stacks)
            {
                Vector2 home = Home(s, s.c, s.r);
                if (s.pos == Vector2.zero) s.pos = home;
                if (s.path.Count > 0)
                {
                    Vector2 t = s.path[0];
                    Vector2 step = t - s.pos;
                    float groundDistance = new Vector2(step.x, step.y / Sq).magnitude;
                    float travel = BoardSpeed(s) * dt;
                    bool advancing = s.figureProfile != null && s.figureProfile.advance != null;
                    if (advancing && groundDistance > .001f)
                    {
                        Vector3 heading = new Vector3(step.x, 0f, -step.y / Sq) / groundDistance;
                        float alignment = Vector3.Dot(s.go.transform.forward, heading);
                        travel *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.4f, .85f, alignment));
                    }
                    if (advancing) s.gaitPhase = Mathf.Repeat(s.gaitPhase + Mathf.Min(groundDistance, travel) / (PPM * s.figureProfile.stride), 1f);
                    s.pos = groundDistance <= travel ? t : s.pos + step * (travel / groundDistance);
                    if ((s.pos - t).sqrMagnitude < 0.5f) { s.pos = t; s.path.RemoveAt(0); }
                }
                else s.pos = Vector2.MoveTowards(s.pos, home, hw * 10f * dt);
                s.lunge = Mathf.Max(0f, s.lunge - dt * 4.5f);
                s.flash = Mathf.Max(0f, s.flash - dt * 3f);
            }
        }

        static void Hex(Vector2 c, float k, Color col)
        {
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Cos(a) * R * k, Mathf.Sin(a) * R * k * Sq);
                if (i > 0) UiKit.Line(prev, p, 1f, col);
                prev = p;
            }
        }
        static void HexFill(Vector2 c, float k, Color col)
        {   // заливка — полосами (IMGUI без мешей)
            int n = 8;
            for (int i = 0; i < n; i++)
            {
                float y0 = -1f + 2f * i / n, y1 = -1f + 2f * (i + 1) / n, ym = (y0 + y1) * 0.5f;
                float half = R * k * (1f - Mathf.Abs(ym) * 0.5f);
                UiKit.Rect(new Rect(c.x - half, c.y + y0 * R * k * Sq * 0.866f, half * 2f, (y1 - y0) * R * k * Sq * 0.866f + 0.5f), col);
            }
        }
        static void HexSprite(string tex, Vector2 c)
        {
            Texture2D t = UiKit.Tex(tex);
            if (t == null) { Hex(c, 0.95f, Gold); return; }
            float w = R * 2.15f, h = w * Sq * 0.98f;
            GUI.DrawTexture(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h), t);
        }

        static Vector2 hover = new Vector2(-1f, -1f);
        static void DrawGrid()
        {
            if (Event.current.type != EventType.Repaint) return;
            bool ourTurn = cur != null && !cur.enemy && !autoOurs && !Busy && !finished && !deploy;
            if (deploy)
            {
                for (int c = 0; c < Mathf.Min(Cols, DeployCols); c++) for (int r = 0; r < Rows; r++) HexFill(Center(c, r), 0.93f, new Color(0.4f, 0.75f, 1f, 0.16f));
                if (deploySel != null) foreach (Vector2Int cc in Cells(deploySel)) HexSprite("UI/Battle/hex_gold", Center(cc.x, cc.y));
            }
            for (int c = 0; c < Cols; c++)
                for (int r = 0; r < Rows; r++)
                {
                    Vector2 p = Center(c, r);
                    if (ourTurn && reach.ContainsKey(new Vector2Int(c, r))) HexFill(p, 0.93f, new Color(0.95f, 0.8f, 0.4f, 0.16f));
                    Hex(p, 0.98f, new Color(0.08f, 0.05f, 0.02f, 0.35f));
                }
            if (cur != null && !finished) foreach (Vector2Int cc in Cells(cur)) HexSprite("UI/Battle/hex_gold", Center(cc.x, cc.y));
            if (ourTurn)
            {
                Vector2Int h = Pick(hover);
                Stack o = h.x >= 0 ? At(h.x, h.y) : null;
                if (o != null && o.enemy && (CanShoot(cur) || CanMeleeAny(o))) foreach (Vector2Int cc in Cells(o)) HexSprite("UI/Battle/hex_red", Center(cc.x, cc.y));
            }
        }
        static bool CanMeleeAny(Stack o) { foreach (Vector2Int n in Around(o)) if (StandFor(cur, n)) return true; return false; }
        /// <summary>Может ли отряд встать головой в клетку n, чтобы ударить (досягаемо; у двухклеточного — сосед головой).</summary>
        static bool StandFor(Stack a, Vector2Int n) { return a != null && reach.ContainsKey(n); }

        static void DrawRocks()
        {
            Texture2D t = UiKit.Tex("UI/Battle/obstacles");
            foreach (Vector3Int o in rocks)
            {
                Vector2 p = Center(o.x, o.y);
                if (t != null)
                {
                    float w = R * 2.6f, h = w * 341f / 384f;
                    GUI.DrawTextureWithTexCoords(new Rect(p.x - w * 0.5f, p.y - h * 0.62f, w, h), t, new Rect((o.z % 4) * 0.25f, 1f - (o.z / 4 + 1) / 3f, 0.25f, 1f / 3f));
                }
                else
                {
                    UiKit.Disc(p + new Vector2(0f, 3f), R * 0.62f, new Color(0f, 0f, 0f, 0.35f));
                    UiKit.Disc(p - new Vector2(0f, R * 0.18f), R * 0.55f, new Color(0.36f, 0.34f, 0.3f, 0.95f));
                    UiKit.Disc(p - new Vector2(R * 0.12f, R * 0.32f), R * 0.3f, new Color(0.5f, 0.48f, 0.42f, 0.95f));
                }
            }
        }

        static void DrawStacks()
        {
            List<Stack> order = new List<Stack>(stacks);
            order.Sort((a, b) => a.pos.y.CompareTo(b.pos.y));
            Texture2D plaque = UiKit.Tex("UI/Battle/count_plaque");
            foreach (Stack s in order)
            {
                float fade = 1f;
                if (!s.Alive) { if (s.die < 0f) continue; fade = 1f - (Time.unscaledTime - s.die) / 0.8f; if (fade <= 0f) continue; }
                Vector2 p = UnitPosition(s);
                float bob = Mathf.Sin(Time.unscaledTime * 2.2f + s.c * 1.7f + s.r) * 1.2f;
                // тень

                if (s.go != null)
                {   // живой боец — уже нарисован камерой; здесь — только число бойцов
                    if (!s.Alive) continue;
                    goto plaqueOnly;
                }
                Texture2D t = s.t.atlasCell < 0 ? Snap(s.t.model) : null;
                Color oc = GUI.color;
                Color tint = Color.Lerp(Color.white, new Color(1f, 0.35f, 0.3f), s.flash);
                tint.a = fade;
                GUI.color = tint;
                if (t != null)
                {
                    float h = R * 2.45f * s.t.liveH / 1.7f, w = h * t.width / t.height;
                    Rect rr = new Rect(p.x - w * 0.5f, p.y - h * 0.93f + bob, w, h);
                    bool flip = s.enemy;   // снимки смотрят вправо — враги разворачиваются к нам
                    GUI.DrawTextureWithTexCoords(rr, t, flip ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f));
                }
                else
                {
                    Texture2D pt = Portrait(s.t);
                    float d = s.t.atlasCell >= 0 ? R * 2.2f * s.t.liveH / 1.7f : R * 1.3f;
                    if (pt != null) DrawPortrait(new Rect(p.x - d * 0.5f, p.y - d * 1.05f + bob, d, d), s.t);
                    else UiKit.Disc(p - new Vector2(0f, R * 0.6f), R * 0.5f, s.enemy ? new Color(0.6f, 0.2f, 0.15f) : new Color(0.3f, 0.4f, 0.6f));
                }
                GUI.color = oc;
                if (!s.Alive) continue;
                plaqueOnly:
                // число бойцов
                Rect pr = new Rect(p.x + R * 0.25f, p.y - R * 0.05f, R * 1.25f, R * 0.5f);
                if (plaque != null) GUI.DrawTexture(pr, plaque); else UiKit.Rect(pr, new Color(0.15f, 0.05f, 0.03f, 0.9f));
                UiKit.Text(pr, s.count.ToString(), Mathf.Max(10f, R * 0.3f), s.enemy ? new Color(1f, 0.62f, 0.5f) : new Color(1f, 0.9f, 0.62f));
                if (s.defending) UiKit.Text(new Rect(pr.x - R * 0.5f, pr.y, R * 0.5f, pr.height), "⛨", R * 0.32f, Gold);
            }
        }

        static Texture2D Portrait(UnitType t)
        {
            if (t.cell >= 0) return UiKit.Tex(t.up ? "UI/Battle/unit_portraits2" : "UI/Battle/unit_portraits");
            if (t.atlasCell >= 0) return UiKit.Tex("UI/H3/neutrals");
            return string.IsNullOrEmpty(t.portrait) ? null : UiKit.Tex(t.portrait);
        }
        static void DrawPortrait(Rect r, UnitType t)
        {
            Texture2D p = Portrait(t);
            if (p == null) { UiKit.Disc(r.center, r.width * 0.5f, Dim); return; }
            if (t.cell >= 0) GUI.DrawTextureWithTexCoords(r, p, new Rect((t.cell % 4) * 0.25f, 1f - (t.cell / 4 + 1) / 3f, 0.25f, 1f / 3f));
            else if (t.atlasCell >= 0) GUI.DrawTextureWithTexCoords(r, p, new Rect((t.atlasCell % 5) * 0.2f + 0.02f, 1f - (t.atlasCell / 5 + 1) * 0.25f + 0.02f, 0.16f, 0.2f));
            else GUI.DrawTexture(r, p);
        }

        static void DrawFx()
        {
            float now = Time.unscaledTime;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                Shot s = shots[i]; float k = (now - s.t0) / s.dur;
                if (k >= 1f) { shots.RemoveAt(i); continue; }
                Vector2 p = Vector2.Lerp(s.a, s.b, k) + new Vector2(0f, -Mathf.Sin(k * Mathf.PI) * R * (s.magic ? 0.4f : 1.4f));
                Vector2 q = Vector2.Lerp(s.a, s.b, Mathf.Max(0f, k - 0.08f)) + new Vector2(0f, -Mathf.Sin(Mathf.Max(0f, k - 0.08f) * Mathf.PI) * R * (s.magic ? 0.4f : 1.4f));
                // снаряд из листа владельца (петля, нарисован летящим вправо) — повёрнут по касательной полёта
                Texture2D pt = Event.current.type == EventType.Repaint ? UiKit.Tex(s.magic ? "UI/Battle/fx_orb_fly" : "UI/Battle/fx_arrow_fly") : null;
                if (pt != null && (p - q).sqrMagnitude > 0.01f)
                {
                    float ang = Mathf.Atan2(p.y - q.y, p.x - q.x) * Mathf.Rad2Deg, sz = R * (s.magic ? 2.2f : 2.6f), h = sz * 170f / 192f;
                    Matrix4x4 om = GUI.matrix;
                    GUIUtility.RotateAroundPivot(ang, p);
                    DrawSheet(pt, new Rect(p.x - sz * 0.5f, p.y - h * 0.5f, sz, h), (now - s.t0) * 40f, 1f, true);
                    GUI.matrix = om;
                }
                else if (s.magic) { UiKit.Disc(p, R * 0.22f, new Color(1f, 0.7f, 0.3f, 0.9f)); UiKit.Disc(p, R * 0.4f, new Color(1f, 0.5f, 0.2f, 0.25f)); }
                else UiKit.Line(q, p, 2f, new Color(0.25f, 0.18f, 0.1f, 1f));
            }
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                Float f = texts[i]; float k = (now - f.t0) / 1.4f;
                if (k >= 1f) { texts.RemoveAt(i); continue; }
                Color c = f.col; c.a = 1f - k * k;
                Rect r = new Rect(f.p.x - 120f, f.p.y - k * 34f, 240f, 24f);
                UiKit.Text(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), f.s, 16f, new Color(0f, 0f, 0f, c.a));
                UiKit.Text(r, f.s, 16f, c);
            }
            for (int i = casts.Count - 1; i >= 0; i--)
            {
                Float f = casts[i]; float k = (now - f.t0) / 0.9f;
                if (k >= 1f) { casts.RemoveAt(i); continue; }
                float s = R * (1.2f + k * 1.6f);
                Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 1f - k);
                SpellIcon(new Rect(f.p.x - s * 0.5f, f.p.y - R - s * 0.5f, s, s), f.icon);
                GUI.color = oc;
                UiKit.Ring(f.p, R * (0.6f + k * 2.2f), new Color(1f, 0.85f, 0.5f, 0.8f * (1f - k)));
            }
        }

        static void DrawQueue()
        {
            Texture2D q = UiKit.Tex("UI/Battle/queue_bar");
            float qw = Mathf.Min(W * 0.42f, 380f), qh = q != null ? qw * q.height / q.width : 60f;
            Rect qr = new Rect((W - qw) * 0.5f, 4f, qw, qh);
            if (q != null) GUI.DrawTexture(qr, q); else UiKit.Rect(qr, new Color(0.1f, 0.05f, 0.03f, 0.9f));
            // очередь: этот раунд, затем следующий (по скорости)
            List<Stack> l = new List<Stack>(queue);
            List<Stack> next = new List<Stack>();
            foreach (Stack s in stacks) if (s.Alive) next.Add(s);
            next.Sort((a, b) => a.Spd != b.Spd ? b.Spd.CompareTo(a.Spd) : a.enemy.CompareTo(b.enemy));
            int sep = l.Count;
            l.AddRange(next);
            float d = qh * 0.42f;
            for (int i = 0; i < 10 && i < l.Count; i++)
            {
                Vector2 c = new Vector2(qr.x + qw * (0.0824f + 0.0927f * i), qr.y + qh * 0.5f);
                Rect pr = new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d);
                Color oc = GUI.color; if (i >= sep) GUI.color = new Color(1f, 1f, 1f, 0.55f);
                DrawPortrait(pr, l[i].t);
                GUI.color = oc;
                UiKit.Ring(c, d * 0.5f + 1f, l[i].enemy ? new Color(0.85f, 0.25f, 0.2f, 0.9f) : new Color(0.4f, 0.75f, 1f, 0.9f));
                if (i == 0 && cur != null) UiKit.Ring(c, d * 0.5f + 3.5f, Gold, true);
                if (i == sep && sep < l.Count) UiKit.Line(new Vector2(c.x - qw * 0.046f, qr.y + qh * 0.2f), new Vector2(c.x - qw * 0.046f, qr.y + qh * 0.8f), 2f, Gold);
            }
            UiKit.Text(new Rect(qr.xMax + 8f, qr.y + 6f, 120f, 22f), "РАУНД " + round, 14f, Gold, TextAnchor.MiddleLeft);
        }

        static readonly int[] QuickSpells = { 0, 1, 4, 6 };
        static void DrawBallistaButton()
        {
            if (!ballistaReady || finished || cur == null || cur.enemy || Busy) return;
            Rect r = new Rect(80f + 4f * 38f + 8f, 540f - 38f, 96f, 34f);
            if (VisualIdentity.KitButton(r, spellSel == -2 ? "ЦЕЛЬ?" : "БАЛЛИСТА", true, spellSel == -2)) spellSel = spellSel == -2 ? -1 : -2;
        }
        static void DrawHud()
        {
            // герой
            Rect hp = new Rect(10f, 540f - 76f, 64f, 64f);
            General.DrawFace(hp);
            UiKit.Ring(hp.center, 33f, Gold);
            UiKit.Text(new Rect(80f, 540f - 76f, 340f, 20f), "ГЕРОЙ · УР. " + heroLv + "   А" + gAtk + " З" + gDef + " С" + General.Power + " Зн" + General.Know, 13f, Gold, TextAnchor.MiddleLeft);
            UiKit.Text(new Rect(80f, 540f - 58f, 220f, 18f), "Дух " + spirit + "/" + spiritMax + (castThisRound ? " · умение уже было" : ""), 12f, new Color(0.6f, 0.8f, 1f), TextAnchor.MiddleLeft);
            for (int i = 0; i < QuickSpells.Length; i++)
            {
                Spell sp = Spells[QuickSpells[i]];
                Rect r = new Rect(80f + i * 38f, 540f - 38f, 34f, 34f);
                bool can = CanCast(sp);
                Color oc = GUI.color; GUI.color = can ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                SpellIcon(r, sp.icon);
                GUI.color = oc;
                if (spellSel == QuickSpells[i]) UiKit.Ring(r.center, 19f, Gold, true);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) && can) Select(QuickSpells[i]);
            }
            // кнопки
            string[] tips = { "ЖДАТЬ", "ЗАЩИТА", autoOurs ? "АВТО: ВКЛ" : "АВТОБОЙ", "БЕГСТВО", "СДАТЬСЯ", "УМЕНИЯ" };
            float bs = 50f, bx = W - 6f * (bs + 4f) - 6f, by = 540f - bs - 8f;
            bool ourTurn = cur != null && !cur.enemy && !Busy && !finished && !autoOurs;
            for (int i = 0; i < 6; i++)
            {
                Rect r = new Rect(bx + i * (bs + 4f), by, bs, bs);
                Texture2D t = UiKit.Tex("UI/Battle/btn_" + i);
                bool on = i == 2 || i == 3 || i == 4 || ourTurn && (i != 0 || !cur.waited) || i == 5 && !finished;
                Color oc = GUI.color; GUI.color = on ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                if (t != null) GUI.DrawTexture(r, t); else UiKit.Rect(r, new Color(0.3f, 0.1f, 0.05f));
                GUI.color = oc;
                UiKit.Text(new Rect(r.x - 6f, r.yMax - 2f, r.width + 12f, 10f), tips[i], 7.5f, Ink);
                if (!GUI.Button(r, GUIContent.none, GUIStyle.none) || !on) continue;
                switch (i)
                {
                    case 0: Wait(); break;
                    case 1: Defend(); break;
                    case 2: autoOurs = !autoOurs; break;
                    case 3: case 4:
                    {
                        int ransom = Ransom();
                        if (i == 4 && Profile.Gold < ransom) { Log("Выкуп " + ransom + " мон — не хватает."); break; }
                        if (fromEstate) Estate.AfterBattle(i == 4 ? Survivors() : new List<KeyValuePair<int, int>>(), false);   // сдался — армия цела, бежал — рассеялась
                        finished = true; won = false;
                        resultText = i == 3 ? "Ты отступил. Армия рассеялась, полководец ушёл." : "Ты сдался и заплатил выкуп " + ransom + " мон: армия цела.";
                        if (i == 4) Profile.AddGold(-ransom);
                        break;
                    }
                    case 5: bookOpen = !bookOpen; break;
                }
            }
            // ход и подсказка
            string line = finished ? "" : cur == null ? "" : cur.enemy ? "Ход врага: " + cur.t.name + " ×" + cur.count
                : spellSel >= 0 ? Spells[spellSel].name + ": " + (Spells[spellSel].target == 0 ? "выбери отряд врага" : Spells[spellSel].target == 1 ? "выбери свой отряд" : "выбери клетку")
                : cur.t.name + " ×" + cur.count + " — " + (CanShoot(cur) ? "стреляй по врагу или иди" : "иди или бей (до " + cur.Spd + " клеток)");
            if (line.Length > 0)
            {
                Rect lr = new Rect(W * 0.5f - 230f, 540f - 34f, 460f, 26f);
                UiKit.Rect(lr, new Color(0.06f, 0.03f, 0.02f, 0.82f));
                UiKit.Text(lr, line, 13f, Ink);
            }
            for (int i = 0; i < log.Count; i++)
                UiKit.Text(new Rect(10f, 92f + i * 16f, 360f, 16f), log[i], 10.5f, new Color(1f, 1f, 1f, 0.5f + 0.5f * i / Mathf.Max(1, log.Count - 1)), TextAnchor.MiddleLeft);
        }

        static bool CanCast(Spell sp) { return Realm.Knows(System.Array.IndexOf(Spells, sp)) && !deploy && !castThisRound && spirit >= sp.cost && cur != null && !cur.enemy && !Busy && !finished; }
        static void Select(int i)
        {
            Spell sp = Spells[i];
            bookOpen = false;
            if (sp.target == 3) { Cast(sp, null, new Vector2Int(-1, -1)); return; }
            spellSel = spellSel == i ? -1 : i;
        }

        static int bookPage, bookFocus = -1;
        static readonly Vector2[] BookSock =
        {   // гнёзда книги UI/Battle/spell_book (1586×992): левая страница, затем правая; по рядам
            new Vector2(313, 320), new Vector2(548, 320), new Vector2(313, 518), new Vector2(548, 518), new Vector2(313, 717), new Vector2(548, 717),
            new Vector2(1003, 320), new Vector2(1231, 320), new Vector2(1003, 518), new Vector2(1231, 518), new Vector2(1003, 717), new Vector2(1231, 717)
        };
        static void DrawBook()
        {
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.5f));
            Texture2D bk = UiKit.Tex("UI/Battle/spell_book");
            if (bk == null) { DrawBookPlain(); return; }
            float k = Mathf.Min((W - 40f) / 1586f, 500f / 992f);
            Rect r = new Rect((W - 1586f * k) * 0.5f, (540f - 992f * k) * 0.5f, 1586f * k, 992f * k);
            GUI.DrawTexture(r, bk);
            int pages = (Spells.Length + 11) / 12;
            bookPage = Mathf.Clamp(bookPage, 0, pages - 1);
            string[] tabs = { "I", "II", "III", "IV" };
            for (int t = 0; t < 4; t++)
            {
                Rect tr = new Rect(r.x + 1430f * k, r.y + (222f + t * 150f) * k, 140f * k, 80f * k);
                if (t >= pages) continue;
                UiKit.Text(new Rect(tr.x, tr.y, tr.width * 0.7f, tr.height), tabs[t], 14f, t == bookPage ? Gold : Dim);
                if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) { bookPage = t; bookFocus = -1; }
            }
            Spell fs = bookFocus >= 0 && bookFocus < Spells.Length ? Spells[bookFocus] : null;
            UiKit.Text(new Rect(r.x + 280f * k, r.y + 135f * k, 340f * k, 62f * k), fs != null ? fs.name : "УМЕНИЯ ГЕРОЯ", 12f, Gold);
            UiKit.Text(new Rect(r.x + 925f * k, r.y + 135f * k, 345f * k, 62f * k), fs != null ? fs.hint + " · дух " + fs.cost : "Дух " + spirit + "/" + spiritMax + (castThisRound ? " · уже было" : ""), 9.5f, Ink);
            for (int q = 0; q < 12; q++)
            {
                int i = bookPage * 12 + q;
                if (i >= Spells.Length) break;
                Spell sp = Spells[i];
                float rad = 82f * k; Vector2 c = new Vector2(r.x + BookSock[q].x * k, r.y + BookSock[q].y * k);
                Rect ic = new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f);
                bool can = CanCast(sp);
                Color oc = GUI.color; GUI.color = can ? Color.white : new Color(1f, 1f, 1f, 0.38f);
                SpellIcon(ic, sp.icon);
                GUI.color = oc;
                if (i == bookFocus) UiKit.Ring(c, rad + 3f, Gold, true);
                Rect cr = new Rect(c.x + rad * 0.45f, c.y + rad * 0.5f, rad * 0.6f, rad * 0.45f);
                UiKit.Rect(cr, new Color(0.08f, 0.03f, 0.02f, 0.85f)); UiKit.Text(cr, sp.cost.ToString(), 10f, can ? Gold : Dim);
                // первое касание — описание, второе — применить
                if (GUI.Button(ic, GUIContent.none, GUIStyle.none)) { if (bookFocus == i && can) { bookFocus = -1; Select(i); return; } bookFocus = i; }
            }
            if (VisualIdentity.KitButton(new Rect(r.center.x - 60f, r.yMax - 4f - 30f, 120f, 30f), "ЗАКРЫТЬ", true, false)) { bookOpen = false; bookFocus = -1; }
        }
        static void DrawBookPlain()
        {
            Rect r = new Rect(W * 0.5f - 300f, 60f, 600f, 420f);
            UiKit.Rect(r, new Color(0.06f, 0.03f, 0.02f, 0.97f));
            UiKit.Text(new Rect(r.x, r.y + 12f, r.width, 26f), "УМЕНИЯ ГЕРОЯ · дух " + spirit + "/" + spiritMax, 17f, Gold);
            for (int i = 0; i < Spells.Length; i++)
            {
                Spell sp = Spells[i];
                float x = r.x + 20f + (i % 3) * 190f, y = r.y + 44f + (i / 3) * 42f;
                Rect ic = new Rect(x, y, 36f, 36f); bool can = CanCast(sp);
                Color oc = GUI.color; GUI.color = can ? Color.white : new Color(1f, 1f, 1f, 0.4f); SpellIcon(ic, sp.icon); GUI.color = oc;
                UiKit.Text(new Rect(x + 40f, y, 140f, 36f), sp.name + " · " + sp.cost, 9.5f, can ? Gold : Dim, TextAnchor.MiddleLeft);
                if (GUI.Button(new Rect(x, y, 180f, 38f), GUIContent.none, GUIStyle.none) && can) Select(i);
            }
            if (VisualIdentity.KitButton(new Rect(r.center.x - 70f, r.yMax - 40f, 140f, 32f), "ЗАКРЫТЬ", true, false)) bookOpen = false;
        }

        static void DrawInfo()
        {
            Stack s = info;
            Rect r = new Rect(W * 0.5f - 260f, 120f, 520f, 280f);
            Texture2D win = UiKit.Tex("UI/Battle/recruit_window");
            if (win != null) GUI.DrawTexture(r, win); else UiKit.Rect(r, new Color(0.08f, 0.04f, 0.03f, 0.97f));
            Rect pf = new Rect(r.x + r.width * 0.075f, r.y + r.height * 0.155f, r.width * 0.31f, r.height * 0.7f);
            DrawPortrait(new Rect(pf.center.x - pf.width * 0.45f, pf.center.y - pf.width * 0.45f, pf.width * 0.9f, pf.width * 0.9f), s.t);
            UiKit.Text(new Rect(pf.x, pf.yMax - 26f, pf.width, 22f), s.t.name.ToUpper(), 13f, Gold);
            string[] vals = { "Атака " + s.Atk + "   урон " + s.t.dmin + "–" + s.t.dmax, "Защита " + s.Def, "Здоровье " + s.t.hp + "   у первого " + s.topHp, "Скорость " + s.Spd + (s.t.shots > 0 ? "   выстрелов " + s.shots : "") };
            for (int i = 0; i < 4; i++)
                UiKit.Text(new Rect(r.x + r.width * 0.56f, r.y + r.height * (0.15f + 0.122f * i), r.width * 0.37f, r.height * 0.09f), vals[i], 11.5f, Ink, TextAnchor.MiddleLeft);
            string extra = "Бойцов: " + s.count + " из " + s.start + (s.t.Ability.Length > 0 ? " · " + s.t.Ability : "");
            UiKit.Text(new Rect(r.x + r.width * 0.45f, r.y + r.height * 0.64f, r.width * 0.5f, 18f), extra, 10.5f, Dim);
            if (VisualIdentity.KitButton(new Rect(r.x + r.width * 0.7f, r.y + r.height * 0.77f, r.width * 0.22f, r.height * 0.12f), "ЗАКРЫТЬ", true, false)) info = null;
        }

        static void DrawResult()
        {
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.55f));
            Texture2D win = UiKit.Tex("UI/Battle/result_window");
            if (win == null) { DrawResultPlain(); return; }
            // окно итога (картина владельца 1448×1086): наши потери слева, вражеские справа, добыча, кнопка
            float k = Mathf.Min((W - 40f) / 1448f, 500f / 1086f);
            Rect r = new Rect((W - 1448f * k) * 0.5f, (540f - 1086f * k) * 0.5f, 1448f * k, 1086f * k);
            System.Func<float, float, float, float, Rect> Px = (x, y, w, h) => new Rect(r.x + x * k, r.y + y * k, w * k, h * k);
            GUI.DrawTexture(r, win);
            UiKit.Text(Px(238, 50, 937, 80), won ? "ПОБЕДА" : "ПОРАЖЕНИЕ", 22f, won ? Gold : new Color(1f, 0.55f, 0.45f));
            UiKit.Text(Px(122, 198, 525, 79), "НАШИ ПОТЕРИ", 12f, Ink);
            UiKit.Text(Px(779, 196, 533, 81), "ПОТЕРИ ВРАГА", 12f, Ink);
            float[] cx = { 226f, 391f, 557f }, cy = { 368f, 528f, 688f };
            for (int side = 0; side < 2; side++)
            {
                int q = 0;
                foreach (Stack st in stacks)
                {
                    if (st.enemy != (side == 1) || q >= 9) continue;
                    int lost = st.start - Mathf.Max(0, st.count);
                    Vector2 c = new Vector2(r.x + (cx[q % 3] + side * 664f) * k, r.y + cy[q / 3] * k);
                    float d = 128f * k;
                    Color oc = GUI.color; if (lost <= 0) GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    DrawPortrait(new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d), st.t);
                    GUI.color = oc;
                    Rect pl = new Rect(c.x - d * 0.42f, c.y + d * 0.22f, d * 0.84f, d * 0.32f);
                    UiKit.Rect(pl, new Color(0.08f, 0.03f, 0.02f, 0.85f));
                    UiKit.Text(pl, lost > 0 ? "−" + lost : "0", 11f, lost > 0 ? new Color(1f, 0.6f, 0.5f) : Dim);
                    q++;
                }
            }
            string line = resultText.Replace("\n", "  ");
            UiKit.Text(Px(330, 831, 790, 65), line, 9.5f, Ink);
            string main = mapNode >= 0 || neutralDone != null ? "НА КАРТУ" : fromEstate ? "В ПОМЕСТЬЕ" : "ЕЩЁ БОЙ";
            Rect mb = Px(476, 914, 477, 76);
            UiKit.Text(mb, main, 14f, Gold);
            if (GUI.Button(mb, GUIContent.none, GUIStyle.none))
            {
                if (neutralDone != null) { System.Action<bool> cb = neutralDone; neutralDone = null; cb(won); Close(); returned = true; }
                else if (mapNode >= 0) { if (won) Campaign.FinishFight(); else Campaign.LoseFight(); mapNode = -1; Close(); returned = true; }
                else if (fromEstate) { Close(); Estate.Show(); }
                else ShowDev((region + 1) % 3);
                return;
            }
            if (mapNode < 0 && neutralDone == null && VisualIdentity.KitButton(new Rect(r.xMax - 120f, r.yMax - 10f - 30f * 1f - 6f, 100f, 30f), "ВЫЙТИ", true, false)) Close();
        }

        static void DrawResultPlain()
        {
            Rect r = new Rect(W * 0.5f - 230f, 170f, 460f, 200f);
            UiKit.Rect(r, new Color(0.06f, 0.03f, 0.02f, 0.95f));
            Texture2D pt = UiKit.Tex("UI/editor_panel");
            if (pt != null) UiKit.Slice(r, pt, 0.42f, 0.15f, 0f, 0.075f, 0f, Color.white);
            UiKit.Text(new Rect(r.x, r.y + 16f, r.width, 30f), won ? "ПОБЕДА" : "ПОРАЖЕНИЕ", 24f, won ? Gold : new Color(1f, 0.5f, 0.4f));
            UiKit.Text(new Rect(r.x + 24f, r.y + 54f, r.width - 48f, 70f), resultText, 13f, Ink);
            if (mapNode >= 0)
            {
                if (VisualIdentity.KitButton(new Rect(r.center.x - 100f, r.yMax - 54f, 200f, 38f), "НА КАРТУ", true, true))
                { if (won) Campaign.FinishFight(); else Campaign.LoseFight(); mapNode = -1; Close(); returned = true; }
                return;
            }
            if (fromEstate) { if (VisualIdentity.KitButton(new Rect(r.x + 30f, r.yMax - 54f, 180f, 38f), "В ПОМЕСТЬЕ", true, true)) { Close(); Estate.Show(); } }
            else if (VisualIdentity.KitButton(new Rect(r.x + 30f, r.yMax - 54f, 180f, 38f), "ЕЩЁ БОЙ", true, true)) ShowDev((region + 1) % 3);
            if (VisualIdentity.KitButton(new Rect(r.xMax - 210f, r.yMax - 54f, 180f, 38f), "ВЫЙТИ", true, false)) Close();
        }

        // ---------------------------------------------------------------- касания

        static float downAt; static Vector2 downPos; static bool downOn;
        static void HandleInput()
        {
            Event e = Event.current;
            if (e.isMouse) hover = e.mousePosition;
            if (e.type == EventType.MouseDown && e.mousePosition.y > 80f && e.mousePosition.y < 540f - 64f) { downOn = true; downAt = Time.unscaledTime; downPos = e.mousePosition; }
            if (e.type == EventType.Repaint && downOn && Time.unscaledTime - downAt > 0.5f)
            {   // долгое нажатие — сведения об отряде
                downOn = false;
                Vector2Int h = Pick(downPos); Stack s = h.x >= 0 ? At(h.x, h.y) : null;
                if (s != null) info = s;
                return;
            }
            if (e.type != EventType.MouseUp || !downOn) return;
            if (deploy)
            {   // расстановка: коснуться своего отряда, потом свободной клетки в своих столбцах
                downOn = false; e.Use();
                Vector2Int dc = Pick(e.mousePosition); if (dc.x < 0) return;
                Stack ds = At(dc.x, dc.y);
                if (ds != null && !ds.enemy) { deploySel = ds; return; }
                if (ds == null && deploySel != null && dc.x < DeployCols && !Rock(dc.x, dc.y) && Fits(deploySel, dc.x, dc.y) && (!deploySel.t.wide || dc.x + Back(deploySel) >= 0))
                { deploySel.c = dc.x; deploySel.r = dc.y; deploySel = null; }
                return;
            }
            downOn = false;
            if (cur == null || cur.enemy || autoOurs || Busy) return;
            Vector2Int c = Pick(e.mousePosition);
            if (c.x < 0) return;
            Stack o = At(c.x, c.y);
            e.Use();
            if (spellSel == -2)
            {   // наводка баллисты
                if (o != null && o.enemy) BallistaShot(o); else spellSel = -1;
                return;
            }
            if (spellSel >= 0)
            {
                Spell sp = Spells[spellSel];
                if (sp.target == 2) Cast(sp, null, c);
                else if (o != null && (sp.target == 0) == o.enemy) Cast(sp, o, c);
                return;
            }
            if (o == null)
            {
                if (WallAt(c.x, c.y) && Attacker(cur) && NextToWall(cur, c.y)) { Run(DoWallHit(cur, c.y)); return; }   // осада: бить стену/ворота рядом
                if (reach.ContainsKey(c) && (c.x != cur.c || c.y != cur.r)) Run(DoMove(cur, c));
                return;
            }
            if (!o.enemy) { info = o; return; }
            if (CanShoot(cur)) { Run(DoShoot(cur, o)); return; }
            // встать рядом — на клетку со стороны касания
            Vector2Int best = new Vector2Int(-1, -1); float bd = float.MaxValue;
            foreach (Vector2Int n in Around(o))
            {
                if (!reach.ContainsKey(n)) continue;
                Vector2 d = Center(n.x, n.y) - e.mousePosition; d.y /= Sq;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = n; }
            }
            if (best.x >= 0) Run(DoMelee(cur, o, best));
            else info = o;
        }
    }
}
