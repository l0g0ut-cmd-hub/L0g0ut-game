#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Снимки игры в Play Mode (после снимков меню): большая карта Леса теней в разных местах, пошаговый бой
    /// (несколько кадров подряд — видно, вертятся ли модели), осада, поместье, полководец, окна «Героев 3».
    /// Включается веткой unity-ui-qa (VisualQARunner). Ошибки — в status.txt, снимки — build/visual-runtime/game_*.png.
    /// </summary>
    public sealed class GameScreenshotDriver : MonoBehaviour
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        public static bool Done;

        static object Get(Type t, string field, object inst = null)
        {
            FieldInfo f = t.GetField(field, Any); if (f != null) return f.GetValue(inst);
            PropertyInfo p = t.GetProperty(field, Any); return p != null ? p.GetValue(inst) : null;
        }
        static void Set(Type t, string field, object v, object inst = null)
        {
            FieldInfo f = t.GetField(field, Any); if (f != null) { f.SetValue(inst, v); return; }
            PropertyInfo p = t.GetProperty(field, Any); if (p != null && p.CanWrite) p.SetValue(inst, v); else if (p != null) { MethodInfo m = p.GetSetMethod(true); if (m != null) m.Invoke(inst, new[] { v }); }
        }
        static void Call(Type t, string method, params object[] args)
        {
            MethodInfo m = t.GetMethod(method, Any); if (m != null) m.Invoke(null, args);
            else VisualQARunner.Fail("Нет метода " + t.Name + "." + method);
        }

        IEnumerator Start()
        {
            yield return null;
            Step("map");
            try
            {
                PlayerPrefs.DeleteAll();
                Profile.AddGold(20000);
                Call(typeof(Campaign), "Generate", 0);
                MapScreen.Show();
            }
            catch (Exception e) { VisualQARunner.Fail("map show: " + e); }
            yield return new WaitForSecondsRealtime(3f);
            DuelManager.Gfx = 1;
            yield return Shot("game_map_hero");
            yield return Shot("game_map_fx_high_t0");
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("game_map_fx_high_t2");
            DuelManager.Gfx = 0;
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("game_map_fx_low_t0");
            yield return new WaitForSecondsRealtime(2f);
            yield return Shot("game_map_fx_low_t2");
            DuelManager.Gfx = 1;
            object inst = Get(typeof(MapScreen), "inst");
            float[] us = { 0f, 0.33f, 0.66f, 1f };
            float[] vs = { 0f, 1f };
            foreach (float v in vs)
                foreach (float u in us)
                {
                    try { Set(typeof(MapScreen), "manualPan", true, inst); Set(typeof(MapScreen), "panU", u, inst); Set(typeof(MapScreen), "panV", v, inst); } catch (Exception e) { VisualQARunner.Fail("pan " + e.Message); }
                    yield return new WaitForSecondsRealtime(0.8f);
                    yield return Shot("game_map_u" + Mathf.RoundToInt(u * 100) + "_v" + Mathf.RoundToInt(v * 100));
                }
            // бой: три пробных боя, в первом — серия кадров
            for (int b = 0; b < 3; b++)
            {
                Step("battle " + b);
                try { Tactics.ShowDev(0); } catch (Exception e) { VisualQARunner.Fail("battle: " + e); }
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot("game_battle" + b + "_a");
                if (b == 0)
                    for (int k = 0; k < 4; k++) { yield return new WaitForSecondsRealtime(0.6f); yield return Shot("game_battle0_t" + k); }
                if (b == 1) { Set(typeof(Tactics), "bookOpen", true); yield return new WaitForSecondsRealtime(0.5f); yield return Shot("game_spellbook"); Set(typeof(Tactics), "bookOpen", false); }
                if (b == 2)
                {   // окно отряда и окно итога боя (28.11.2026: проверка арта атласов Kit)
                    var all = Get(typeof(Tactics), "stacks") as System.Collections.IList;
                    if (all != null && all.Count > 0)
                    {
                        Set(typeof(Tactics), "info", all[0]); yield return new WaitForSecondsRealtime(0.5f); yield return Shot("game_unit_info");
                        Set(typeof(Tactics), "info", all[all.Count - 1]); yield return new WaitForSecondsRealtime(0.5f); yield return Shot("game_unit_info_enemy");
                        Set(typeof(Tactics), "info", null);
                    }
                    Set(typeof(Tactics), "autoOurs", false);
                    Set(typeof(Tactics), "resultText", "Враг разбит. Сражено врагов: 57, наших потерь: 21.\nДобыча: 402 мон.\nОпыт полководца: +302 — новый уровень!");
                    Set(typeof(Tactics), "won", true); Set(typeof(Tactics), "finished", true);
                    yield return new WaitForSecondsRealtime(0.6f); yield return Shot("game_result_win");
                    Set(typeof(Tactics), "finished", false);
                }
                try { Call(typeof(Tactics), "Close"); } catch (Exception e) { VisualQARunner.Fail("close: " + e.Message); }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Step("siege");
            try { Tactics.ShowSiege(0); } catch (Exception e) { VisualQARunner.Fail("siege: " + e); }
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("game_siege");
            try { Call(typeof(Tactics), "Close"); } catch { }
            Step("estate");
            try { Estate.Show(); } catch (Exception e) { VisualQARunner.Fail("estate: " + e); }
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("game_estate");
            try { General.Show(); } catch (Exception e) { VisualQARunner.Fail("general: " + e); }
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("game_general");
            Set(typeof(General), "Open", false);
            string[] wins = { "OpenMarket", "OpenTavern", "OpenHall", "OpenPuzzle" };
            foreach (string w in wins)
            {
                try { Call(typeof(Realm), w); } catch (Exception e) { VisualQARunner.Fail(w + ": " + e.Message); }
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("game_" + w.Substring(4).ToLower());
                Set(typeof(Realm), "win", Enum.ToObject(typeof(Realm).GetNestedType("Win", Any), 0));
            }
            Set(typeof(Estate), "Open", false);
            try { Realm.NewWeek(); } catch (Exception e) { VisualQARunner.Fail("week: " + e.Message); }
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("game_week");
            Set(typeof(Realm), "win", Enum.ToObject(typeof(Realm).GetNestedType("Win", Any), 0));
            VisualQARunner.Info("GAME_END failures=" + VisualQARunner.Failures);
            Done = true;
        }

        static void Step(string s) { VisualQARunner.Info("STEP " + s); }

        IEnumerator Shot(string name)
        {
            for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();
            string path = Path.Combine(VisualQARunner.DirectoryPath, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 8f;
            while ((!File.Exists(path) || new FileInfo(path).Length < 8192) && Time.realtimeSinceStartup < deadline) yield return null;
            VisualQARunner.Info("SHOT " + name + (File.Exists(path) ? " ok" : " MISSING"));
        }
    }
}
#endif
