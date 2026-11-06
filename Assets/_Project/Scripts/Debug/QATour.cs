#if BLADEPATH_QATOUR
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// Фототур для проверки экранов на эмуляторе. Компилируется ТОЛЬКО в тестовую сборку под эмулятор
    /// (BLADEPATH_QATOUR ставит BuildScript для BladePath-VisualEmulator); в обычном APK этого кода нет.
    ///
    /// Игра сама проходит ВСЁ: меню и настройки, сложность, путника, карту и поездку по ней (выбор цели,
    /// маршрут, бросок кости, езда, прибытие), окно каждого места на карте (бой, элита, владыка, засада, храм,
    /// кузнец, торговец, тайник, привал, травница, все 12 событий, «нокаут», «область пройдена», меню
    /// разработчика), лагерь и его окна, трактир и его окна (трактирщик, кости, доска наград, ронин),
    /// снаряжение с предметами, бой (вступление, бой, пауза, меню разработчика, воскрешение, поражение, победа).
    ///
    /// Не застревает:
    ///  • каждый шаг отдельно: исключение или зависший шаг (дольше StepLimit) пропускается, тур идёт дальше;
    ///  • всё ожидание — с пределом времени;
    ///  • недостающее выдаётся «разработчиком»: ходы и кости пути, моны, очки, предметы, бессмертие,
    ///    телепорт к нужному месту, выключенный ИИ врага на снимках боя.
    /// На каждом экране в лог пишется «QATOUR_SHOT имя» — скрипт на эмуляторе снимает screencap. Конец — «QATOUR_DONE».
    /// </summary>
    public class QATour : MonoBehaviour
    {
        const float Settle = 3.5f;     // эмулятор без видеокарты рисует 1–3 кадра в секунду: даём экрану дорисоваться
        const float Hold = 2.5f;       // экран держится, пока screencap забирает кадр
        const float StepLimit = 60f;   // шаг дольше этого — считается зависшим и пропускается
        static QATour inst;
        int shotIndex, fails;
        MonoBehaviour map;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst != null) return;
            GameObject g = new GameObject("QATour");
            DontDestroyOnLoad(g);
            inst = g.AddComponent<QATour>();
        }

        void Start() { StartCoroutine(Tour()); }

        // ---------------- отражение: доступ к закрытым полям экранов, без правок их кода ----------------

        static FieldInfo F(object o, string name)
        {
            return o == null ? null : o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }
        static object Get(object o, string name) { FieldInfo f = F(o, name); return f != null ? f.GetValue(o) : null; }
        static void Set(object o, string name, object value)
        {
            FieldInfo f = F(o, name);
            if (f == null) { Debug.LogWarning("QATOUR_WARN no field " + name); return; }
            if (f.FieldType.IsEnum && value is string) value = Enum.Parse(f.FieldType, (string)value);
            f.SetValue(o, value);
        }
        static object Call(object o, string name, params object[] args)
        {
            foreach (MethodInfo m in o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (m.Name != name) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length < args.Length) continue;
                bool fits = true;
                for (int i = args.Length; i < ps.Length; i++) if (!ps[i].HasDefaultValue) fits = false;
                if (!fits) continue;
                object[] full = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) full[i] = i < args.Length ? args[i] : ps[i].DefaultValue;
                return m.Invoke(o, full);
            }
            Debug.LogWarning("QATOUR_WARN no method " + name);
            return null;
        }
        static MonoBehaviour FindAny(string typeName)
        {
            foreach (MonoBehaviour b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null && b.GetType().Name == typeName) return b;
            return null;
        }

        // ---------------- каркас: снимок, шаг с пределом, ожидание с пределом ----------------

        IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(Settle);
            shotIndex++;
            Debug.Log("QATOUR_SHOT " + shotIndex.ToString("00") + "_" + name);
            yield return new WaitForSecondsRealtime(Hold);
        }

        /// <summary>Шаг тура: исключение или превышение StepLimit — пропуск с пометкой в логе, тур продолжается.</summary>
        IEnumerator Step(string what, Func<IEnumerator> body)
        {
            IEnumerator e = null;
            try { e = body(); } catch (Exception ex) { Fail(what, ex.ToString()); }
            if (e == null) yield break;
            float started = Time.realtimeSinceStartup;
            while (true)
            {
                if (Time.realtimeSinceStartup - started > StepLimit) { Fail(what, "timeout " + StepLimit + "s"); yield break; }
                object cur = null;
                bool more = false;
                try { more = e.MoveNext(); if (more) cur = e.Current; }
                catch (Exception ex) { Fail(what, ex.ToString()); yield break; }
                if (!more) yield break;
                // вложенные корутины (Shot, Until) прокручиваются здесь же — под общим пределом шага
                if (cur is IEnumerator inner) { while (true) { bool m2 = false; object c2 = null; try { m2 = inner.MoveNext(); if (m2) c2 = inner.Current; } catch (Exception ex) { Fail(what, ex.ToString()); break; } if (!m2) break; yield return c2; } }
                else yield return cur;
            }
        }
        void Fail(string what, string why) { fails++; Debug.LogWarning("QATOUR_FAIL " + what + " " + why); }

        static IEnumerator Until(Func<bool> cond, float limit)
        {
            float t = 0f;
            while (t < limit)
            {
                bool done = false;
                try { done = cond(); } catch { }
                if (done) yield break;
                t += 0.25f;
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        // ---------------- «разработчик»: всё, чтобы тур нигде не упёрся ----------------

        static void Supply()
        {
            try
            {
                if (Campaign.Rolls < 3) Campaign.AddRoll(5);
                if (Campaign.Steps < 6) Campaign.DebugSteps(9);
                if (Profile.Gold < 3000) Profile.AddGold(5000);
                DuelManager.Immortal = true;
            }
            catch (Exception ex) { Debug.LogWarning("QATOUR_WARN supply " + ex.Message); }
        }

        void CloseMap()
        {
            if (map == null) return;
            try { Set(map, "modal", "None"); Set(map, "campSub", 0); Set(map, "tavSub", 0); Set(map, "continueRideAt", -1f); } catch { }
        }

        static int NodeOf(NodeType t)
        {
            for (int i = 0; i < Campaign.Nodes.Count; i++) if (Campaign.Nodes[i].Type == t) return i;
            return -1;
        }

        // ---------------- сам тур ----------------

        IEnumerator Tour()
        {
            yield return new WaitForSecondsRealtime(9f);   // заставка студии и первый кадр видео меню
            MonoBehaviour menu = FindAny("MainMenuController");
            yield return Step("menu", () => Shot("main_menu"));
            if (menu != null)
            {
                yield return Step("settings", () => MenuState(menu, 0, true, 1, false, "settings"));
                yield return Step("difficulty_warrior", () => MenuState(menu, 1, false, 1, false, "difficulty_warrior"));
                yield return Step("difficulty_master", () => MenuState(menu, 1, false, 2, false, "difficulty_master"));
                yield return Step("difficulty_fog", () => MenuState(menu, 1, false, 2, true, "difficulty_fog_on"));
                JourneyChoice.FogEnabled = false;
                yield return Step("traveller", () => MenuState(menu, 2, false, 2, false, "traveller"));
                try { JourneyChoice.BeginNew(1, 2); menu.StartCoroutine((IEnumerator)Call(menu, "LoadBattle")); }   // Рэн — в бою видно «скорость»
                catch (Exception ex) { Fail("start", ex.ToString()); }
            }
            else Fail("menu", "no MainMenuController");

            yield return Until(() => SceneManager.GetActiveScene().name == SceneNames.Prototype, 60f);
            yield return Until(() => MapScreen.Open, 40f);
            yield return new WaitForSecondsRealtime(6f);   // экран загрузки «Путь клинка» и проявление карты
            map = FindAny("MapScreen");
            Supply();
            yield return Step("map", () => Shot("map"));

            if (map != null)
            {
                yield return Step("ride", () => Ride());
                yield return Step("node_windows", () => NodeWindows());
                yield return Step("events", () => Events());
                yield return Step("camp", () => Camp());
                yield return Step("tavern", () => Tavern());
                yield return Step("special_modals", () => SpecialModals());
                CloseMap();
            }
            else Fail("map", "no MapScreen");

            yield return Step("inventory", () => Inventory());
            try { InventoryScreen.Hide(); } catch { }

            yield return Step("fight", () => Fight());
            yield return Step("defeat_victory", () => DefeatAndVictory());
            Debug.Log("QATOUR_DONE shots=" + shotIndex + " fails=" + fails);
        }

        IEnumerator MenuState(MonoBehaviour menu, int step, bool settings, int choice, bool fog, string name)
        {
            Set(menu, "settingsOpen", settings);
            Set(menu, "newJourneyStep", step);
            Set(menu, step == 2 ? "chosenHero" : "chosenDifficulty", choice);
            JourneyChoice.FogEnabled = fog;
            yield return Shot(name);
            Set(menu, "settingsOpen", false);
        }

        /// <summary>Поездка по карте: выбор далёкой цели (маршрут со следами и кольцом конца хода), бросок, езда, прибытие.</summary>
        IEnumerator Ride()
        {
            CloseMap();
            int goal = -1, at = Campaign.At, hops = 0;
            while (hops < 4)   // цель — в 2–4 переходах вперёд: виден маршрут и кольцо остановки хода
            {
                MapNode cur = Campaign.Nodes[at];
                if (cur.next == null || cur.next.Count == 0) break;
                at = cur.next[0]; hops++;
                if (hops >= 2) goal = at;
            }
            if (goal < 0) { Fail("ride", "no forward route"); yield break; }

            Campaign.DebugSteps(1);
            Call(map, "SelectNode", goal);
            yield return Shot("map_route_short");          // красные следы сверх хода, кольцо «здесь закончится ход»

            Campaign.DebugSteps(0);
            if (Campaign.Rolls <= 0) Campaign.AddRoll(3);
            Campaign.RollDie();
            Set(map, "rollAt", Time.unscaledTime);
            yield return new WaitForSecondsRealtime(0.2f);
            Debug.Log("QATOUR_SHOT " + (++shotIndex).ToString("00") + "_map_dice_roll");   // кость — сразу, пока крутится
            yield return new WaitForSecondsRealtime(Hold);

            Campaign.DebugSteps(9);
            Call(map, "SelectNode", goal);
            yield return Shot("map_route_full");           // весь маршрут в пределах хода

            Call(map, "TravelOrRoll");
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log("QATOUR_SHOT " + (++shotIndex).ToString("00") + "_map_riding");   // на ходу
            float t = 0f;
            while (t < 35f)   // прибытие: или окно встречи/цели, или доехал
            {
                object mt = Get(map, "moveTo"), md = Get(map, "modal");
                int moveTo = mt is int ? (int)mt : -1;
                string modal = md != null ? md.ToString() : "None";
                if (moveTo < 0 && (modal != "None" || Campaign.At == goal)) break;
                t += 0.5f; yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return Shot("map_arrived");
            CloseMap();
            Supply();
        }

        /// <summary>Окно каждого типа места: телепорт туда и открытие тем же кодом, что и при прибытии.</summary>
        IEnumerator NodeWindows()
        {
            NodeType[] types = { NodeType.Duel, NodeType.Elite, NodeType.Boss, NodeType.Ambush, NodeType.Shrine, NodeType.Smith,
                NodeType.Merchant, NodeType.Treasure, NodeType.Rest, NodeType.Herbalist };
            foreach (NodeType type in types)
            {
                int i = NodeOf(type);
                if (i < 0) { Debug.Log("QATOUR_INFO no node " + type); continue; }
                CloseMap();
                bool ok = true;
                try
                {
                    Campaign.DebugTeleport(i);
                    Campaign.Nodes[i].done = false;   // окно «нового», не пройденного места
                    Set(map, "revisit", false);
                    Set(map, "treasure", type == NodeType.Treasure ? (object)Profile.Treasure(Campaign.Region + 1) : null);
                    Call(map, "OpenNode", i);
                    Set(map, "modalAt", Time.unscaledTime - 2f);
                }
                catch (Exception ex) { Fail("node_" + type, ex.ToString()); ok = false; }
                if (ok) yield return Shot("node_" + type.ToString().ToLower());
                CloseMap();
                Supply();
            }
        }

        /// <summary>Все события: одно место-событие, номер события подставляется по очереди.</summary>
        IEnumerator Events()
        {
            int ev = NodeOf(NodeType.Event);
            if (ev < 0) { Debug.Log("QATOUR_INFO no event node"); yield break; }
            for (int k = 0; k < MapScreen.EventCount; k++)
            {
                CloseMap();
                bool ok = true;
                try
                {
                    Campaign.DebugTeleport(ev);
                    Campaign.Nodes[ev].ev = k;
                    Campaign.Nodes[ev].done = false;
                    Set(map, "revisit", false);
                    Call(map, "OpenNode", ev);
                    Set(map, "modalAt", Time.unscaledTime - 2f);
                }
                catch (Exception ex) { Fail("event_" + k, ex.ToString()); ok = false; }
                if (ok) yield return Shot("event_" + k.ToString("00"));
            }
            CloseMap();
        }

        IEnumerator Camp()
        {
            CloseMap();
            Call(map, "OpenCamp");
            Set(map, "modalAt", Time.unscaledTime - 2f);
            yield return new WaitForSecondsRealtime(3f);   // видео лагеря
            yield return Shot("camp");
            Set(map, "campSub", 1); yield return Shot("camp_altar");
            Set(map, "campSub", 2); yield return Shot("camp_questboard");
            CloseMap();
        }

        IEnumerator Tavern()
        {
            CloseMap();
            Call(map, "OpenTavern", true);
            Set(map, "modalAt", Time.unscaledTime - 2f);
            yield return new WaitForSecondsRealtime(3f);   // видео трактира
            yield return Shot("tavern");
            Set(map, "tavSub", 1); yield return Shot("tavern_keeper");
            try { TavernDiceGame.Enter(true); Set(map, "tavSub", 2); } catch (Exception ex) { Fail("dice", ex.Message); }
            yield return Shot("tavern_dice");
            Set(map, "tavSub", 3); yield return Shot("tavern_bounty");
            Set(map, "tavSub", 4); yield return Shot("tavern_ronin");
            CloseMap();
        }

        IEnumerator SpecialModals()
        {
            foreach (string m in new[] { "Knocked", "RegionClear", "Dev" })
            {
                CloseMap();
                Set(map, "modal", m); Set(map, "modalAt", Time.unscaledTime - 2f);
                yield return Shot("modal_" + m.ToLower());
            }
            CloseMap();
        }

        IEnumerator Inventory()
        {
            Item first = null;   // предметы в сумку — экраны снаряжения не пустые
            for (int k = 0; k < 6; k++)
            {
                Item it = Profile.Roll(Campaign.Region + 1, 90, k == 0);
                if (it != null && Profile.Give(it) && first == null) first = it;
            }
            for (int k = 0; k < 6; k++) Profile.GivePoint();
            InventoryScreen.Show(0);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("inventory_gear");
            MonoBehaviour inv = FindAny("InventoryScreen");
            if (inv != null && first != null) { Set(inv, "sel", first); yield return Shot("inventory_item_selected"); Set(inv, "sel", null); }
            InventoryScreen.Show(1); yield return Shot("inventory_stats");
            InventoryScreen.Show(2); yield return Shot("inventory_path");
            InventoryScreen.Hide();
        }

        DuelManager dm;
        EnemyAI enemyAi;

        IEnumerator Fight()
        {
            if (map == null) yield break;
            CloseMap();
            int at = NodeOf(NodeType.Duel);
            if (at < 0) { Fail("fight", "no duel node"); yield break; }
            Campaign.DebugTeleport(at);
            Campaign.Nodes[at].done = false;
            Call(map, "StartFight", at, false);
            yield return Until(() => (dm = DuelManager.Instance) != null && !MapScreen.Open, 15f);
            if (dm == null) { Fail("fight", "no DuelManager"); yield break; }
            DuelManager.Immortal = true;

            yield return new WaitForSecondsRealtime(0.3f);
            Debug.Log("QATOUR_SHOT " + (++shotIndex).ToString("00") + "_fight_intro");   // вступление — сразу, пока враг в полный рост
            yield return new WaitForSecondsRealtime(Hold);
            yield return Until(() => dm.State == DuelState.Fighting, 25f);
            foreach (EnemyAI e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) if (!e.autoPlayer && !e.sideFighter) enemyAi = e;
            if (enemyAi != null) enemyAi.aiEnabled = false;   // враг стоит: кадр предсказуем, герой не гибнет между снимками
            yield return Shot("fight");

            dm.SetPaused(true);
            yield return Shot("pause");
            MonoBehaviour input = FindAny("PrototypeInput");
            if (input != null) { Set(input, "devOpen", true); yield return Shot("pause_dev_menu"); Set(input, "devOpen", false); }
            dm.SetPaused(false);
        }

        IEnumerator DefeatAndVictory()
        {
            if (dm == null) yield break;
            // воскрешение: смертельный удар без бессмертия
            DuelManager.Immortal = false;
            yield return Until(() => dm.State == DuelState.Fighting, 15f);
            if (dm.player != null && dm.enemy != null)
            {
                Call(dm, "ApplyDamage", dm.player, dm.enemy, 9999f, dm.player.transform.position + Vector3.up, false);
                yield return Until(() => dm.ResurrectOffered, 6f);
                if (dm.ResurrectOffered)
                {
                    yield return new WaitForSecondsRealtime(0.8f);
                    Debug.Log("QATOUR_SHOT " + (++shotIndex).ToString("00") + "_resurrect_offer");   // окно воскрешения живёт 5 с — снимок сразу
                    yield return new WaitForSecondsRealtime(Hold);
                    if (dm.ResurrectOffered) dm.Resurrect();
                }
                yield return new WaitForSecondsRealtime(1.5f);
            }

            // поражение: добиваем героя, пока матч не кончится; каждый раунд — с пределом времени
            for (int round = 0; round < 4 && !dm.MatchOver; round++)
            {
                yield return Until(() => dm.State == DuelState.Fighting, 20f);
                if (dm.player != null && dm.enemy != null)
                    Call(dm, "ApplyDamage", dm.player, dm.enemy, 9999f, dm.player.transform.position + Vector3.up, false);
                yield return Until(() => dm.MatchOver || dm.State == DuelState.PlayerLost, 8f);
                // повторное воскрешение уже потрачено; если окно всё же открылось — ждём, пока оно истечёт само
                yield return Until(() => !dm.ResurrectOffered, DuelManager.ResurrectWindow + 2f);
                yield return Until(() => dm.MatchOver || dm.State == DuelState.Intro || dm.State == DuelState.Fighting, 12f);
            }
            if (dm.MatchOver) { yield return new WaitForSecondsRealtime(2f); yield return Shot("defeat"); }
            else Fail("defeat", "match not over");

            // победа: новый бой и «ВЫИГРАТЬ БОЙ»
            DuelManager.Immortal = true;
            dm.ResetMatch();
            yield return Until(() => dm.State == DuelState.Fighting, 25f);
            dm.DebugWin();
            yield return new WaitForSecondsRealtime(4f);   // красная вспышка удара проходит, появляются кнопки итога
            yield return Shot("victory");
            if (enemyAi != null) enemyAi.aiEnabled = true;
        }
    }
}
#endif
