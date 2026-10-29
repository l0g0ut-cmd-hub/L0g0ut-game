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
        }

        public static readonly Stage[] Stages =
        {
            new Stage { name = "ПОВЕРЖЕННЫЙ", title = "Неупокоенный страж ворот", hpMul = 1.0f, dmgMul = 0.85f, speedMul = 1.0f, aggr = -0.05f, kiMul = 1f, dashBias = 0f, heavyBias = 0f, tint = Color.white, aura = new Color(0.75f, 0.35f, 1f) , marks = 1 },
            new Stage { name = "ТЕНЬ КЛАНА", title = "Убийца без лица", hpMul = 1.1f, dmgMul = 1.0f, speedMul = 1.12f, aggr = 0.05f, kiMul = 1f, dashBias = 0.35f, heavyBias = 0f, tint = new Color(0.55f, 0.6f, 0.78f), aura = new Color(0.3f, 0.5f, 1f) , marks = 2 },
            new Stage { name = "КРОВАВЫЙ ОНИ", title = "Демон, пьющий сталь", hpMul = 1.3f, dmgMul = 1.1f, speedMul = 1.05f, aggr = 0.1f, kiMul = 1.1f, dashBias = 0f, heavyBias = 0.5f, tint = new Color(1f, 0.55f, 0.5f), aura = new Color(1f, 0.25f, 0.15f) , marks = 2 },
            new Stage { name = "ПРОКЛЯТЫЙ СЁГУН", title = "Владыка мёртвого храма", hpMul = 1.6f, dmgMul = 1.2f, speedMul = 1.15f, aggr = 0.2f, kiMul = 1.6f, dashBias = 0.15f, heavyBias = 0.2f, tint = new Color(0.82f, 0.72f, 1f), aura = new Color(1f, 0.85f, 0.3f) , marks = 3 },
        };

        public static int Index
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("ladder_stage", 0), 0, Stages.Length - 1); }
            set { PlayerPrefs.SetInt("ladder_stage", Mathf.Clamp(value, 0, Stages.Length - 1)); }
        }
        public static Stage Current { get { return Stages[Index]; } }
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
