using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// «Хлебные крошки» для диагностики вылетов на телефоне без logcat. Пишутся СИНХРОННО в файл
    /// (PlayerPrefs на Android сохраняются асинхронно и при вылете теряются). Каждый шаг загрузки боя, первые кадры,
    /// ошибки и исключения — в файл; если приложение упало, меню при следующем запуске покажет последние строки.
    /// </summary>
    public static class Breadcrumbs
    {
        static string path;
        static bool open;
        static int frames = -1;

        static string PathF { get { if (path == null) path = Path.Combine(Application.persistentDataPath, "crumbs.txt"); return path; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
                string s = msg + " | " + (stack ?? "").Replace('\n', ' ');
                if (s.Length > 400) s = s.Substring(0, 400);
                Write("ОШИБКА: " + s);
            };
            SceneManager.sceneLoaded += (sc, mode) => { if (open) Write("сцена загружена: " + sc.name); };
        }

        static void Write(string line)
        {
            try { File.AppendAllText(PathF, System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\n"); } catch { }
        }

        /// <summary>Начать запись (перед загрузкой боя): файл очищается.</summary>
        public static void Begin(string step)
        {
            try { File.WriteAllText(PathF, "OPEN\n"); } catch { }
            open = true; frames = 0;
            Write(step);
        }

        /// <summary>Отметить шаг.</summary>
        public static void Mark(string step) { if (open) Write(step); }

        /// <summary>Считать кадры боя; после 60 кадров загрузка считается успешной и файл закрывается.</summary>
        public static void Frame()
        {
            if (!open || frames < 0) return;
            frames++;
            if (frames == 1 || frames == 5 || frames == 20) Write("кадр " + frames);
            if (frames >= 60) { open = false; try { File.WriteAllText(PathF, "OK\n"); } catch { } }
        }

        /// <summary>Отчёт о прошлом вылете (или null) — показывается один раз.</summary>
        public static string TakeReport()
        {
            try
            {
                if (!File.Exists(PathF)) return null;
                string[] lines = File.ReadAllLines(PathF);
                File.WriteAllText(PathF, "OK\n");
                if (lines.Length == 0 || lines[0] != "OPEN") return null;
                int from = Mathf.Max(1, lines.Length - 6);
                string r = "Прошлый запуск оборвался. Последние шаги:";
                for (int i = from; i < lines.Length; i++) r += "\n" + lines[i];
                return r;
            }
            catch { return null; }
        }
    }
}
