using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Сохранения в PlayerPrefs с резервной копией (по навыку save-systems): перед записью прежнее удачное сохранение
    /// уходит в «ключ_bak». Если основное не читается (обрезано, испорчено), берётся резервное — раньше при ошибке
    /// чтения игра молча начинала с нуля и следующей записью затирала весь прогресс.
    /// </summary>
    public static class SafeSave
    {
        public static void Write(string key, string json)
        {
            string prev = PlayerPrefs.GetString(key, "");
            if (!string.IsNullOrEmpty(prev) && prev != json && LooksValid(prev)) PlayerPrefs.SetString(key + "_bak", prev);
            PlayerPrefs.SetString(key, json);
            PlayerPrefs.Save();
        }

        /// <summary>Прочитать объект: основное сохранение, при ошибке — резервное; null — сохранений нет или оба испорчены.</summary>
        public static T Read<T>(string key) where T : class
        {
            T v = Try<T>(PlayerPrefs.GetString(key, ""));
            if (v != null) return v;
            v = Try<T>(PlayerPrefs.GetString(key + "_bak", ""));
            if (v != null) Debug.LogWarning("BladePath: сохранение «" + key + "» испорчено — загружена резервная копия");
            return v;
        }

        static T Try<T>(string json) where T : class
        {
            if (!LooksValid(json)) return null;
            try { return JsonUtility.FromJson<T>(json); } catch { return null; }
        }

        static bool LooksValid(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            json = json.Trim();
            return json.Length > 2 && json[0] == '{' && json[json.Length - 1] == '}';   // обрезанная запись не закрыта скобкой
        }
    }
}
