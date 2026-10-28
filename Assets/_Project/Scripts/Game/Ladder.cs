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
            public Color tint, aura;
        }

        public static readonly Stage[] Stages =
        {
            new Stage { name = "ПОВЕРЖЕННЫЙ", title = "Неупокоенный страж ворот", hpMul = 1.0f, dmgMul = 0.85f, speedMul = 1.0f, aggr = -0.05f, tint = Color.white, aura = new Color(0.75f, 0.35f, 1f) },
            new Stage { name = "ТЕНЬ КЛАНА", title = "Убийца без лица", hpMul = 1.1f, dmgMul = 1.0f, speedMul = 1.12f, aggr = 0.05f, tint = new Color(0.55f, 0.6f, 0.78f), aura = new Color(0.3f, 0.5f, 1f) },
            new Stage { name = "КРОВАВЫЙ ОНИ", title = "Демон, пьющий сталь", hpMul = 1.3f, dmgMul = 1.1f, speedMul = 1.05f, aggr = 0.1f, tint = new Color(1f, 0.55f, 0.5f), aura = new Color(1f, 0.25f, 0.15f) },
            new Stage { name = "ПРОКЛЯТЫЙ СЁГУН", title = "Владыка мёртвого храма", hpMul = 1.6f, dmgMul = 1.2f, speedMul = 1.15f, aggr = 0.2f, tint = new Color(0.82f, 0.72f, 1f), aura = new Color(1f, 0.85f, 0.3f) },
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
}
