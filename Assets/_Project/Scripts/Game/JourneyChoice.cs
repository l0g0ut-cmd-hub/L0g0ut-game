using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Identity selected before the first campaign fight. Stored independently
    /// from a save's RPG stats so Continue does not change the chosen traveller.
    /// All three variants share the existing working combat rig; visual accents
    /// and small stat specializations are additive, never replacement models.
    /// </summary>
    public static class JourneyChoice
    {
        public static readonly string[] Names = { "ДЗИН", "АКИКО", "РЭН" };
        public static readonly string[] Titles = { "РОНИН", "ХРАНИТЕЛЬНИЦА", "ТЕНЬ" };
        public static readonly string[] Specialties = { "БАЛАНС", "ЗАЩИТА", "СКОРОСТЬ" };
        public static readonly string[] Biographies =
        {
            "Бывший страж. Равновесие клинка и духа.",
            "Воительница клана. Выдержка и стальная воля.",
            "Странник ночи. Стремительный, как тень."
        };
        public static readonly string[] DifficultyNames =
            { "ЛЕГКО", "ВОИН", "МАСТЕР" };
        public static readonly string[] DifficultyDesc =
        {
            "Щадящий путь. Противник осторожнее, больше времени на реакцию.",
            "Классический путь самурая. Равные возможности.",
            "Без права на ошибку. Враги умнее и опаснее."
        };

        public static int Hero
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("journey_hero", 0), 0, 2); }
        }
        public static Color Accent(int index)
        {
            switch (index)
            {
                case 1: return new Color(0.90f, 0.50f, 0.34f);
                case 2: return new Color(0.62f, 0.76f, 0.96f);
                default: return VisualIdentity.BrassLight;
            }
        }
        // The six user-approved PNGs are distinct from older JPG illustrations.
        // Keep different Resources basenames: Unity does not guarantee which
        // texture is returned when hero_0.jpg and hero_0.png share a path.
        static readonly string[] approvedHeroPaths = new string[3];
        static readonly string[] approvedDifficultyPaths = new string[3];

        static string ApprovedOrFallback(string[] cache, string kind, int index)
        {
            index = Mathf.Clamp(index, 0, 2);
            if (cache[index] == null)
            {
                string approved = "UI/selection/approved_" + kind + "_" + index;
                // Do not report a JPG fallback as an imported approved original.
                if (Resources.Load<Texture2D>(approved) != null)
                    cache[index] = approved;
                else
                {
                    cache[index] = "UI/selection/" + kind + "_" + index;
                    Debug.LogWarning("BladePath: approved selection art missing: " +
                        approved + ". Showing existing legacy JPG until PNG is imported.");
                }
            }
            return cache[index];
        }
        public static string Portrait(int index)
        {
            return ApprovedOrFallback(approvedHeroPaths, "hero", index);
        }
        public static string DifficultyArt(int index)
        {
            return ApprovedOrFallback(approvedDifficultyPaths, "difficulty", index);
        }
        public static void BeginNew(int difficulty, int hero)
        {
            // Only the explicit final Start Path action resets progression.
            // Browsing/backing out of selection never overwrites an existing run.
            PlayerPrefs.SetInt("journey_hero", Mathf.Clamp(hero, 0, 2));
            EnemyAI.Difficulty = Mathf.Clamp(difficulty, 0, 2);
            Profile.Wipe();
            Campaign.Restart();
            Ladder.Index = 0;
            PlayerPrefs.SetInt("journey_started", 1);
            PlayerPrefs.Save();
        }
        public static bool CanContinue
        {
            get
            {
                return PlayerPrefs.GetInt("journey_started", 0) == 1 ||
                    PlayerPrefs.HasKey("campaign_v1") ||
                    Ladder.Index > 0 || Records.Wins > 0 || Records.Losses > 0;
            }
        }
    }
}
