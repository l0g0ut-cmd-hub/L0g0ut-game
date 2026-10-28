using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// «Хлебные крошки» для диагностики вылетов на телефоне без logcat:
    /// ключевые шаги загрузки боя записываются на диск; если приложение упало, при следующем запуске меню покажет,
    /// на каком шаге это случилось, и последнее исключение.
    /// </summary>
    public static class Breadcrumbs
    {
        const string Key = "crumb_step", ErrKey = "crumb_error", OpenKey = "crumb_open";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type != LogType.Exception && type != LogType.Error) return;
                string s = msg + "\n" + (stack ?? "");
                if (s.Length > 600) s = s.Substring(0, 600);
                PlayerPrefs.SetString(ErrKey, s);
            };
        }

        /// <summary>Отметить шаг (пишется на диск сразу).</summary>
        public static void Mark(string step)
        {
            PlayerPrefs.SetString(Key, step);
            PlayerPrefs.SetInt(OpenKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Бой успешно идёт — шаги загрузки пройдены.</summary>
        public static void Clear()
        {
            PlayerPrefs.SetInt(OpenKey, 0);
            PlayerPrefs.Save();
        }

        /// <summary>Отчёт о прошлом вылете (или null) — показывается один раз.</summary>
        public static string TakeReport()
        {
            string r = null;
            if (PlayerPrefs.GetInt(OpenKey, 0) == 1)
                r = "Прошлый запуск оборвался на шаге: " + PlayerPrefs.GetString(Key, "?");
            string e = PlayerPrefs.GetString(ErrKey, "");
            if (!string.IsNullOrEmpty(e)) r = (r != null ? r + "\n" : "") + "Ошибка: " + e.Split('\n')[0];
            PlayerPrefs.SetInt(OpenKey, 0);
            PlayerPrefs.SetString(ErrKey, "");
            PlayerPrefs.Save();
            return r;
        }
    }
}
