#if BLADEPATH_QATOUR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// Фототур для проверки экранов на эмуляторе. Компилируется ТОЛЬКО в тестовую сборку
    /// под эмулятор (символ BLADEPATH_QATOUR ставит BuildScript для BladePath-VisualEmulator);
    /// в обычном APK этого кода нет.
    ///
    /// Игра сама проходит экраны: меню → сложность → путник → карта → карточка боя → лагерь →
    /// трактир → снаряжение → бой (вступление, бой, пауза, победа). На каждом экране пишет в лог
    /// «QATOUR_SHOT имя» и держит кадр — скрипт на эмуляторе по этой метке снимает screencap.
    /// Дополнительно кадр сохраняется в persistentDataPath/qa. Конец — «QATOUR_DONE».
    /// Логика игры не меняется: экраны открываются через их же поля и методы.
    /// </summary>
    public class QATour : MonoBehaviour
    {
        const float Hold = 3.5f;   // сколько держать экран после метки: screencap на эмуляторе медленный
        static QATour inst;
        string dir;
        int shotIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null) return;
            GameObject g = new GameObject("QATour");
            DontDestroyOnLoad(g);
            inst = g.AddComponent<QATour>();
        }

        void Start()
        {
            dir = Path.Combine(Application.persistentDataPath, "qa");
            try { Directory.CreateDirectory(dir); } catch { }
            Debug.Log("QATOUR_PATH " + dir);
            StartCoroutine(Tour());
        }

        static object Field(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f != null ? f.GetValue(o) : null;
        }

        static void SetField(object o, string name, object value)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) { Debug.LogWarning("QATOUR_WARN no field " + name); return; }
            if (f.FieldType.IsEnum && value is string) value = Enum.Parse(f.FieldType, (string)value);
            f.SetValue(o, value);
        }

        static object Call(object o, string name, params object[] args)
        {
            MethodInfo m = o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) { Debug.LogWarning("QATOUR_WARN no method " + name); return null; }
            return m.Invoke(o, args);
        }

        IEnumerator Shot(string name)
        {
            // дать кадру дорисоваться (IMGUI, видео), затем метка для screencap и запасной снимок самой игрой
            yield return new WaitForSecondsRealtime(1.2f);
            shotIndex++;
            string file = shotIndex.ToString("00") + "_" + name;
            Debug.Log("QATOUR_SHOT " + file);
            try { ScreenCapture.CaptureScreenshot("qa_" + file + ".png"); }   // на Android имя дописывается к persistentDataPath catch (Exception e) { Debug.LogWarning("QATOUR_WARN capture " + e.Message); }
            yield return new WaitForSecondsRealtime(Hold);
        }

        IEnumerator Step(string what, Func<IEnumerator> body)
        {
            // каждый шаг отдельно: если один экран упадёт, тур идёт дальше
            IEnumerator e = null;
            bool ok = true;
            try { e = body(); } catch (Exception ex) { Debug.LogWarning("QATOUR_FAIL " + what + " " + ex); ok = false; }
            while (ok)
            {
                object cur = null;
                try { if (!e.MoveNext()) ok = false; else cur = e.Current; }
                catch (Exception ex) { Debug.LogWarning("QATOUR_FAIL " + what + " " + ex); ok = false; }
                if (ok) yield return cur;
            }
        }

        IEnumerator Tour()
        {
            // ---------- главное меню ----------
            yield return new WaitForSecondsRealtime(9f);   // заставка студии и первый кадр видео
            MonoBehaviour menu = FindAny("MainMenuController");
            yield return Step("menu", () => Shot("main_menu"));
            if (menu != null)
            {
                yield return Step("difficulty", () => MenuStep(menu, 1, 1, false, "difficulty_warrior"));
                yield return Step("difficulty_master", () => MenuStep(menu, 1, 2, false, "difficulty_master"));
                yield return Step("difficulty_fog", () => MenuStep(menu, 1, 2, true, "difficulty_fog_on"));
                JourneyChoice.FogEnabled = false;
                yield return Step("traveller", () => MenuStep(menu, 2, 1, false, "traveller"));
                // новый поход и переход в сцену боя — тем же путём, что кнопка «В ПУТЬ»
                try
                {
                    JourneyChoice.BeginNew(1, 0);
                    menu.StartCoroutine((IEnumerator)Call(menu, "LoadBattle"));
                }
                catch (Exception ex) { Debug.LogWarning("QATOUR_FAIL start " + ex); }
            }
            else Debug.LogWarning("QATOUR_FAIL no MainMenuController");

            // ---------- карта похода ----------
            float wait = 0f;
            while (SceneManager.GetActiveScene().name != SceneNames.Prototype && wait < 60f) { wait += 0.5f; yield return new WaitForSecondsRealtime(0.5f); }
            yield return new WaitForSecondsRealtime(8f);   // сцена, карта, облака
            yield return Step("map", () => Shot("map"));
            MonoBehaviour map = FindAny("MapScreen");
            if (map != null)
            {
                yield return Step("fight_card", () => MapModal(map, "Fight", null, "fight_card"));
                yield return Step("camp", () => MapModal(map, null, "OpenCamp", "camp", 4f));
                yield return Step("tavern", () => MapModal(map, null, "OpenTavern", "tavern", 5f, false));
                SetField(map, "modal", "None");
            }
            yield return Step("inventory_gear", () => Inventory(0, "inventory_gear"));
            yield return Step("inventory_stats", () => Inventory(1, "inventory_stats"));
            yield return Step("inventory_path", () => Inventory(2, "inventory_path"));
            InventoryScreen.Hide();

            // ---------- бой ----------
            if (map != null)
            {
                int at = -1;
                for (int i = 0; i < Campaign.Nodes.Count; i++) if (Campaign.Nodes[i].Type == NodeType.Duel) { at = i; break; }
                if (at >= 0)
                {
                    try { Call(map, "StartFight", at, false); } catch (Exception ex) { Debug.LogWarning("QATOUR_FAIL fight " + ex); }
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Step("intro", () => Shot("fight_intro"));
                    yield return new WaitForSecondsRealtime(4f);
                    yield return Step("fight", () => Shot("fight"));
                    DuelManager dm = DuelManager.Instance;
                    if (dm != null)
                    {
                        dm.SetPaused(true);
                        yield return Step("pause", () => Shot("pause"));
                        dm.SetPaused(false);
                        yield return new WaitForSecondsRealtime(0.5f);
                        dm.DebugWin();
                        yield return new WaitForSecondsRealtime(2.5f);
                        yield return Step("victory", () => Shot("victory"));
                    }
                }
                else Debug.LogWarning("QATOUR_FAIL no duel node");
            }

            Debug.Log("QATOUR_DONE shots=" + shotIndex);
        }

        IEnumerator MenuStep(MonoBehaviour menu, int step, int difficulty, bool fog, string name)
        {
            SetField(menu, "newJourneyStep", step);
            SetField(menu, "chosenDifficulty", difficulty);
            JourneyChoice.FogEnabled = fog;
            return Shot(name);
        }

        IEnumerator MapModal(MonoBehaviour map, string modal, string open, string name, float settle = 1f, params object[] args)
        {
            if (open != null) Call(map, open, args);
            else { SetField(map, "modal", modal); SetField(map, "modalAt", Time.unscaledTime); }
            yield return new WaitForSecondsRealtime(settle);
            yield return Shot(name);
            SetField(map, "modal", "None");
        }

        IEnumerator Inventory(int tab, string name)
        {
            InventoryScreen.Show(tab);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot(name);
        }

        static MonoBehaviour FindAny(string typeName)
        {
            foreach (MonoBehaviour b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (b != null && b.GetType().Name == typeName) return b;
            return null;
        }
    }
}
#endif
