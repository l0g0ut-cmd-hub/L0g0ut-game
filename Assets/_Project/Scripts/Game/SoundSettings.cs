using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Звук: общий выключатель и что звучит — игра и музыка, только игра (бой, шаги, окружение) или только музыка.
    /// Хранится в PlayerPrefs. Музыка — источники Music (кото и барабаны), всё остальное — звуки игры.
    /// </summary>
    public static class SoundSettings
    {
        public const int Both = 0, GameOnly = 1, MusicOnly = 2;
        static readonly string[] ScopeNames = { "ИГРА И МУЗЫКА", "ТОЛЬКО ИГРА", "ТОЛЬКО МУЗЫКА" };
        static readonly string[] ScopeShort = { "ИГРА+МУЗЫКА", "ИГРА", "МУЗЫКА" };
        static bool loaded, on = true;
        static int scope;
        static float nextApply;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            on = PlayerPrefs.GetInt("snd_on", 1) == 1;
            scope = Mathf.Clamp(PlayerPrefs.GetInt("snd_scope", Both), 0, 2);
        }
        public static bool On { get { Load(); return on; } }
        public static int Scope { get { Load(); return scope; } }
        public static bool MusicOn { get { Load(); return on && scope != GameOnly; } }
        public static bool GameOn { get { Load(); return on && scope != MusicOnly; } }
        public static string ScopeName { get { return ScopeNames[Scope]; } }
        public static string ScopeShortName { get { return ScopeShort[Scope]; } }

        public static void Toggle() { Load(); on = !on; Save(); }
        public static void NextScope() { Load(); scope = (scope + 1) % 3; Save(); }
        static void Save()
        {
            PlayerPrefs.SetInt("snd_on", on ? 1 : 0);
            PlayerPrefs.SetInt("snd_scope", scope);
            PlayerPrefs.Save();
            Apply();
        }

        /// <summary>Глушит/включает все источники звука по настройке (новые подхватываются раз в 0,25 с).</summary>
        public static void Apply()
        {
            Load();
            nextApply = Time.unscaledTime + 0.25f;
            AudioSource[] all = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                AudioSource s = all[i];
                if (s == null) continue;
                bool mute = Music.Owns(s) ? !MusicOn : !GameOn;
                if (s.mute != mute) s.mute = mute;
            }
        }
        public static void Tick() { if (Time.unscaledTime >= nextApply) Apply(); }
    }
}
