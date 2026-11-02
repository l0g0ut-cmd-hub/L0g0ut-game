using UnityEngine;

namespace BladePath
{
    /// <summary>Three travellers sharing the same campaign, equipment and combat rules.
    /// Selection is saved independently from campaign_v1, so Continue keeps its hero.
    /// New Game commits the pending selection only after the confirmation step.</summary>
    public static class HeroRoster
    {
        public const int Count = 3;
        public static readonly string[] Names = { "ДЗИН", "АКИКО", "РЭН" };
        public static readonly string[] Titles =
        {
            "РОНИН · РАВНОВЕСИЕ",
            "ЗАЩИТНИЦА · СТОЙКОСТЬ",
            "СТРАННИК · СКОРОСТЬ"
        };
        public static readonly string[] Descriptions =
        {
            "Точный клинок. Уверенное отражение атак.",
            "Броня и стойкость. Выдерживает сильные удары.",
            "Быстрые выпады. Легко меняет дистанцию."
        };
        public static readonly string[] PreviewArt =
        {
            "UI/selection_hero_jin", "UI/selection_hero_akiko", "UI/selection_hero_ren"
        };

        public static int Selected
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("hero_choice", 0), 0, Count - 1); }
            set
            {
                PlayerPrefs.SetInt("hero_choice", Mathf.Clamp(value, 0, Count - 1));
                PlayerPrefs.Save();
            }
        }

        public static float ParryMultiplier { get { return Selected == 0 ? 1.08f : 1f; } }
        public static int BonusHealth { get { return Selected == 1 ? 12 : 0; } }
        public static float ArmorMultiplier { get { return Selected == 1 ? 0.96f : 1f; } }
        public static float MoveMultiplier { get { return Selected == 2 ? 1.08f : 1f; } }
        public static float DashMultiplier { get { return Selected == 2 ? 0.94f : 1f; } }
    }
}
