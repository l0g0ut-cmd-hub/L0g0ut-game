using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// «Путь клинка»: лестница противников. Каждый следующий крепче, быстрее и злее; прогресс хранится между запусками.
    /// </summary>
    public static class Ladder
    {
        public struct Stage
        {
            public string name, title;
            public float hpMul, dmgMul, speedMul, aggr;
            public float kiMul, dashBias, heavyBias;   // особенности: копит дух, уходит рывками, любит тяжёлые удары
            public Color tint, aura;
            public int marks;   // метки жизни (как у боссов Sekiro): столько раз нужно добить
            public int model;   // облик врага (EnemySkins): 0 — самурай в красном доспехе, 1 — ронин, 2 — Они, 3 — сёгун, 4 — Поверженный, 5 — Тень клана (Meshy),
                                //   6 — лесной разбойник, 7 — ямабуси, 8 — асигару-дезертир, 9 — маска кицунэ (Meshy по концептам владельца, 2026-11-18)
            public string art, trait;   // у обликов Леса теней: картина врага (UI/…) и повадки — у ступеней они в MapScreen по номеру
        }

        public static readonly Stage[] Stages =
        {
            new Stage { name = "ПОВЕРЖЕННЫЙ", title = "Неупокоенный страж ворот", hpMul = 1.0f, dmgMul = 0.85f, speedMul = 1.0f, aggr = -0.05f, kiMul = 1f, dashBias = 0f, heavyBias = 0f, tint = Color.white, aura = new Color(0.75f, 0.35f, 1f) , marks = 1, model = 4 },
            new Stage { name = "ТЕНЬ КЛАНА", title = "Убийца без лица", hpMul = 1.1f, dmgMul = 1.0f, speedMul = 1.12f, aggr = 0.05f, kiMul = 1f, dashBias = 0.35f, heavyBias = 0f, tint = new Color(0.9f, 0.92f, 1f), aura = new Color(0.3f, 0.5f, 1f) , marks = 2, model = 5 },
            new Stage { name = "КРОВАВЫЙ ОНИ", title = "Демон, пьющий сталь", hpMul = 1.3f, dmgMul = 1.1f, speedMul = 1.05f, aggr = 0.1f, kiMul = 1.1f, dashBias = 0f, heavyBias = 0.5f, tint = new Color(1f, 0.9f, 0.86f), aura = new Color(1f, 0.25f, 0.15f) , marks = 2, model = 2 },
            new Stage { name = "ПРОКЛЯТЫЙ СЁГУН", title = "Владыка мёртвого храма", hpMul = 1.6f, dmgMul = 1.2f, speedMul = 1.15f, aggr = 0.2f, kiMul = 1.6f, dashBias = 0.15f, heavyBias = 0.2f, tint = new Color(0.94f, 0.9f, 1f), aura = new Color(1f, 0.85f, 0.3f) , marks = 3, model = 3 },
        };

        /// <summary>
        /// Обитатели Леса теней: обычные бои первой области — не только Поверженный. Сила — как у первой ступени
        /// (ступень «Пути» не меняется), отличаются облик, имя и манера боя.
        /// </summary>
        public static readonly Stage[] Foes =
        {
            new Stage { name = "ЛЕСНОЙ РАЗБОЙНИК", title = "Нож из кустов — и кошель с тебя", hpMul = 0.85f, dmgMul = 0.85f, speedMul = 1.12f, aggr = 0.12f, kiMul = 1f, dashBias = 0.2f, heavyBias = 0f,
                        tint = Color.white, aura = new Color(1f, 0.5f, 0.15f), marks = 1, model = 6, art = "UI/enemy_full_bandit", trait = "наскакивает без передышки, но хлипок" },
            new Stage { name = "ЯМАБУСИ", title = "Горный отшельник, копящий дух", hpMul = 1.05f, dmgMul = 0.85f, speedMul = 0.95f, aggr = -0.05f, kiMul = 1.5f, dashBias = 0f, heavyBias = 0.15f,
                        tint = Color.white, aura = new Color(1f, 0.8f, 0.3f), marks = 1, model = 7, art = "UI/enemy_full_yamabushi", trait = "терпелив и быстро копит дух — не давай ему отдышаться" },
            new Stage { name = "АСИГАРУ-ДЕЗЕРТИР", title = "Бежал с поля боя — но не от драки", hpMul = 1.15f, dmgMul = 0.95f, speedMul = 0.9f, aggr = 0f, kiMul = 1f, dashBias = 0f, heavyBias = 0.45f,
                        tint = Color.white, aura = new Color(1f, 0.3f, 0.15f), marks = 1, model = 8, art = "UI/enemy_full_ashigaru", trait = "медленный, рубит сверху — уходи вбок" },
            new Stage { name = "МАСКА КИЦУНЭ", title = "Убийца, что смеётся из тумана", hpMul = 0.9f, dmgMul = 0.9f, speedMul = 1.15f, aggr = 0.05f, kiMul = 1f, dashBias = 0.4f, heavyBias = 0f,
                        tint = Color.white, aura = new Color(1f, 0.35f, 0.5f), marks = 1, model = 9, art = "UI/enemy_full_kitsune", trait = "уходит рывками и заходит сбоку" },
        };

        public static int Index
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("ladder_stage", 0), 0, Stages.Length - 1); }
            // новая ступень — облик по умолчанию (облик Леса теней ставит только начало боя на месте карты: Campaign.BeginFight)
            set { PlayerPrefs.SetInt("ladder_stage", Mathf.Clamp(value, 0, Stages.Length - 1)); PlayerPrefs.SetInt("ladder_foe", 0); }
        }
        /// <summary>Облик Леса теней текущего боя: 0 — нет (ступень как есть), 1…Foes.Length — Foes[Foe − 1].</summary>
        public static int Foe
        {
            get { int f = PlayerPrefs.GetInt("ladder_foe", 0); return f == DojoFoe || f > 0 && f <= Foes.Length ? f : 0; }
            set { PlayerPrefs.SetInt("ladder_foe", value == DojoFoe ? DojoFoe : Mathf.Clamp(value, 0, Foes.Length)); }
        }
        /// <summary>Облик «ученик додзё»: сила ступени области, облик — самурай в красном доспехе (не Поверженный — это живой ученик сэнсэя).</summary>
        public const int DojoFoe = 50;
        public static Stage Current
        {
            get
            {
                int f = Foe;
                if (f == DojoFoe)
                {
                    Stage s = Stages[Index];
                    s.name = "СТАРШИЙ УЧЕНИК"; s.title = "Лучший клинок додзё"; s.model = 0;
                    s.tint = Color.white; s.aura = new Color(1f, 0.7f, 0.3f); s.dashBias = 0.1f; s.heavyBias = 0.15f;
                    return s;
                }
                return f > 0 ? Foes[f - 1] : Stages[Index];
            }
        }
        /// <summary>Меню разработчика: −1 — враги как на карте, 0 — всегда Поверженный, 1…4 — всегда этот обитатель леса (обычные бои Леса теней).</summary>
        public static int DevFoe
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("dev_forest_foe", -1), -1, Foes.Length); }
            set { PlayerPrefs.SetInt("dev_forest_foe", Mathf.Clamp(value, -1, Foes.Length)); PlayerPrefs.Save(); }
        }
        /// <summary>Противник места карты (ступень или облик Леса теней).</summary>
        public static Stage Of(int enemy, int foe) { return foe > 0 && foe <= Foes.Length ? Foes[foe - 1] : Stages[Mathf.Clamp(enemy, 0, Stages.Length - 1)]; }
        /// <summary>Случайный противник обычного боя первой области: Поверженный (0) или один из обитателей леса (1…4).</summary>
        public static int RollForestFoe() { return Random.value < 0.25f ? 0 : 1 + Random.Range(0, Foes.Length); }
        public static bool IsLast { get { return Index >= Stages.Length - 1; } }
        public static int Count { get { return Stages.Length; } }
    }

    /// <summary>Рекорды между запусками: победы, поражения, дальняя ступень «Пути», сколько раз пройден.</summary>
    public static class Records
    {
        public static int Wins { get { return PlayerPrefs.GetInt("rec_wins", 0); } }
        public static int Losses { get { return PlayerPrefs.GetInt("rec_losses", 0); } }
        public static int BestStage { get { return PlayerPrefs.GetInt("rec_best", 0); } }
        public static int Cleared { get { return PlayerPrefs.GetInt("rec_cleared", 0); } }

        /// <summary>Подстраховка: после поражений подряд на одной ступени враг бьёт чуть слабее (до −20%).</summary>
        public static float Assist { get { return 1f - 0.07f * Mathf.Min(3, PlayerPrefs.GetInt("rec_losestreak", 0)); } }

        public static void OnMatch(bool won, int stage)
        {
            PlayerPrefs.SetInt("rec_losestreak", won ? 0 : PlayerPrefs.GetInt("rec_losestreak", 0) + 1);
            if (won)
            {
                PlayerPrefs.SetInt("rec_wins", Wins + 1);
                PlayerPrefs.SetInt("rec_best", Mathf.Max(BestStage, stage + 1));
                if (stage >= Ladder.Count - 1) PlayerPrefs.SetInt("rec_cleared", Cleared + 1);
            }
            else PlayerPrefs.SetInt("rec_losses", Losses + 1);
            PlayerPrefs.Save();
        }
    }
}
