using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// Ввод и экранное управление: клавиатура (старый Input Manager) + круглые экранные кнопки + плавающий джойстик.
    /// Кнопки читаются ПО КАСАНИЯМ напрямую (Input.touches), IMGUI только рисует их: так можно одновременно ходить и бить.
    ///
    /// Клавиши: WASD/стрелки — ход; Space — прыжок; Shift — рывок; J — сверху; K — снизу; N — по ногам; M — шар; I — укол; O — рез;
    /// L — удар врага; E — тип удара врага; T — ИИ врага вкл/выкл; R — заново; Esc — пауза; F1 — отладка.
    /// </summary>
    public class PrototypeInput : MonoBehaviour
    {
        public FighterController player;
        public FighterController enemy;

        enum B { Stick, Jump, Dash, Overhead, Rising, Legs, Energy, Thrust, Slash, Pause,
                 Resume, Restart, CamReset, AiToggle, DebugToggle, ToMenu, OverAgain, OverMenu, Block, Vibro, Difficulty, Super, Gfx, Heal, CamMode, TimerToggle, Gear, OverGear, Stats, AutoBattle, Immortal, DevMenu, DevBack, AutoLevel, EnemyImmortal, WinFight, Clavicle, Twist, Arena, Ally, AllyFeature, AllyLevel, AllyImmortal, Sound, SoundScope, DayTime, NoRain }
        const int Count = 47;
        bool devOpen;   // пауза: открыто «меню разработчика»

        const float StickRadius = 84f;

        public static PrototypeInput Instance { get; private set; }
        void Awake()
        {
            useGUILayout = false;   // GUILayout нигде не используется: без лишнего прохода Layout IMGUI вдвое дешевле
            Instance = this; Breadcrumbs.Mark("ввод: Awake");
        }

        static readonly AttackType[] EnemyTypes = { AttackType.Overhead, AttackType.Rising, AttackType.LegSweep, AttackType.Thrust, AttackType.Slash, AttackType.Energy };

        readonly bool[] held = new bool[Count];
        bool energyPending; float energyDownAt;
        readonly bool[] down = new bool[Count];
        readonly bool[] active = new bool[Count];
        readonly bool[] round = new bool[Count];
        readonly Vector2[] ctr = new Vector2[Count];
        readonly float[] rad = new float[Count];
        readonly Rect[] rects = new Rect[Count];
        readonly string[] labels = new string[Count];
        readonly List<Vector2> pointersHeld = new List<Vector2>();
        readonly List<Vector2> pointersDown = new List<Vector2>();
        int enemyTypeIndex;
        bool slashFlip;
        AttackType buffered; bool hasBuffered; float bufferedAt;

        // удар сразу; не вышло (идёт прошлый удар) — запомнить последнее нажатие
        void Press(AttackType t)
        {
            if (player.AttackOrChain(t)) { hasBuffered = false; return; }
            buffered = t; hasBuffered = true; bufferedAt = Time.unscaledTime;
        }
        float scale = 1f;

        int stickFinger = -1;
        bool mouseStick;
        bool stickActive;
        Vector2 stickCenter = new Vector2(120f, 400f);
        Vector2 stickKnob = new Vector2(120f, 400f);
        Vector2 stickVec;

        DuelManager duel;
        EnemyAI ai;
        EnemyAI playerAi; bool autoWasOn;   // отладочный автобой
        CombatDebugVisualizer debugViz;

        void Start()
        {
            Haptics.Enabled = PlayerPrefs.GetInt("haptics", 0) == 1;
            duel = FindFirstObjectByType<DuelManager>();
            foreach (EnemyAI e in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) if (!e.autoPlayer && !e.sideFighter) ai = e;
            debugViz = FindFirstObjectByType<CombatDebugVisualizer>();
        }

        bool Paused { get { return duel != null && duel.Paused; } }
        bool Over { get { return duel != null && (duel.State == DuelState.PlayerWon || duel.State == DuelState.PlayerLost) && duel.MatchOver && duel.StateTime > 1.3f; } }

        public bool IsOverButton(Vector2 screenPx)
        {
            float sc = Mathf.Max(0.01f, Screen.height / 540f);
            Vector2 p = new Vector2(screenPx.x / sc, (Screen.height - screenPx.y) / sc);
            for (int b = 0; b < Count; b++)
                if (active[b] && Hit(b, p, 10f)) return true;
            return false;
        }

        bool Hit(int b, Vector2 p, float margin)
        {
            if (round[b]) return (p - ctr[b]).magnitude <= rad[b] + margin;
            Rect r = rects[b];
            r.xMin -= margin; r.yMin -= margin; r.xMax += margin; r.yMax += margin;
            return r.Contains(p);
        }

        void Update()
        {
            if (InventoryScreen.Open || MapScreen.Open) return;   // снаряжение или карта открыты — бой и кнопки не слушают касания
            Layout();
            Poll();
            if (down[(int)B.Gear] || down[(int)B.OverGear]) { Profile.CostMul = 1f; InventoryScreen.Show(0); return; }   // в бою — без кузницы
            if (down[(int)B.Stats]) { InventoryScreen.Show(1); return; }

            Vector2 kb = Vector2.zero;
            bool jump = down[(int)B.Jump], dash = down[(int)B.Dash];
            bool aThrust = down[(int)B.Thrust], aSlash = down[(int)B.Slash];
            bool aOver = down[(int)B.Overhead], aRise = down[(int)B.Rising], aLegs = down[(int)B.Legs], aEnergy = down[(int)B.Energy];
            bool enemyAttack = false, enemyCycle = false, toggleAi = down[(int)B.AiToggle];
            bool reset = down[(int)B.Restart] || down[(int)B.OverAgain];
            bool menu = down[(int)B.ToMenu] || down[(int)B.OverMenu];
            bool pauseBtn = down[(int)B.Pause] || down[(int)B.Resume];
            bool superBtn = down[(int)B.Super];
            bool healBtn = down[(int)B.Heal];
            if (down[(int)B.CamReset] && CameraOrbit.Instance != null) CameraOrbit.Instance.ResetView();
            if (down[(int)B.DebugToggle] && debugViz != null) debugViz.visible = !debugViz.visible;
            if (down[(int)B.Sound]) SoundSettings.Toggle();
            if (down[(int)B.SoundScope]) SoundSettings.NextScope();
            if (down[(int)B.Vibro]) { Haptics.Enabled = !Haptics.Enabled; PlayerPrefs.SetInt("haptics", Haptics.Enabled ? 1 : 0); PlayerPrefs.Save(); if (Haptics.Enabled) Haptics.Pulse(40); }
            if (down[(int)B.Difficulty]) EnemyAI.Difficulty = (EnemyAI.Difficulty + 1) % 3;
            if (down[(int)B.Gfx]) DuelManager.Gfx = 1 - DuelManager.Gfx;
            if (down[(int)B.CamMode]) CameraOrbit.FreeMode = !CameraOrbit.FreeMode;
            if (down[(int)B.TimerToggle]) DuelManager.TimerOn = !DuelManager.TimerOn;

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kb.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kb.x -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) kb.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) kb.y -= 1f;
            jump |= Input.GetKeyDown(KeyCode.Space);
            dash |= Input.GetKeyDown(KeyCode.LeftShift);
            aOver |= Input.GetKeyDown(KeyCode.J);
            aRise |= Input.GetKeyDown(KeyCode.K);
            aLegs |= Input.GetKeyDown(KeyCode.N);
            aEnergy |= Input.GetKeyDown(KeyCode.M);
            superBtn |= Input.GetKeyDown(KeyCode.U);
            healBtn |= Input.GetKeyDown(KeyCode.H);
            aThrust |= Input.GetKeyDown(KeyCode.I);
            aSlash |= Input.GetKeyDown(KeyCode.O);
            enemyAttack |= Input.GetKeyDown(KeyCode.L);
            enemyCycle |= Input.GetKeyDown(KeyCode.E);
            toggleAi |= Input.GetKeyDown(KeyCode.T);
            reset |= Input.GetKeyDown(KeyCode.R);
            pauseBtn |= Input.GetKeyDown(KeyCode.Escape);
            if (Input.GetKeyDown(KeyCode.F1) && debugViz != null) { /* сам визуализатор переключает видимость по F1 */ }
#endif
            if (duel != null && duel.IaiWaiting && !Paused && (aOver || aRise || aLegs || aThrust || aSlash)) { duel.IaiPress(); return; }   // выхват (под паузой — нет)
            if (duel != null && duel.LockActive && !Paused && (aOver || aRise || aLegs || aThrust || aSlash || aEnergy)) { duel.LockPush(); return; }   // сцепка клинков: любой удар — давить
            if (menu) { Time.timeScale = 1f; SceneManager.LoadScene(SceneNames.MainMenu); return; }
            if (reset)
            {
                if (duel != null)
                {
                    // победа — на карту похода (узел пройден; после владыки — следующая область); поражение — тот же бой ещё раз
                    if (Over && down[(int)B.OverAgain] && duel.PlayerWonMatch) { Campaign.FinishFight(); Time.timeScale = 1f; MapScreen.Show(); }
                    else if (Over && down[(int)B.OverAgain] && (Campaign.InFight || Campaign.Training)) { Campaign.LoseFight(); Time.timeScale = 1f; MapScreen.Show(); }   // проиграл — к травнице
                    else duel.ResetMatch();
                }
                else { if (player != null) player.ResetFighter(); if (enemy != null) enemy.ResetFighter(); }
                return;
            }
            if (toggleAi && ai != null) ai.aiEnabled = !ai.aiEnabled;
            if (down[(int)B.Immortal]) DuelManager.Immortal = !DuelManager.Immortal;
            if (down[(int)B.EnemyImmortal]) DuelManager.EnemyImmortal = !DuelManager.EnemyImmortal;
            if (down[(int)B.AutoLevel]) DuelManager.AutoLevel = (DuelManager.AutoLevel + 1) % 3;
            if (down[(int)B.DevMenu]) devOpen = true;
            if (down[(int)B.DevBack]) devOpen = false;
            if (down[(int)B.WinFight] && duel != null) { devOpen = false; duel.DebugWin(); return; }
            if (down[(int)B.Clavicle]) HeroRigDriver.DebugAnimSword = !HeroRigDriver.DebugAnimSword;   // мокап рук (клипы Mixamo) / старый IK
            if (down[(int)B.Twist]) { float[] tw = { 0f, 0.25f, 0.45f, 0.6f }; int ti = 0; for (int k = 0; k < tw.Length; k++) if (Mathf.Abs(tw[k] - HeroRigDriver.DebugTwistShare) < 0.01f) ti = k; HeroRigDriver.DebugTwistShare = tw[(ti + 1) % tw.Length]; }
            if (down[(int)B.AutoBattle]) DuelManager.AutoBattle = !DuelManager.AutoBattle;
            if (down[(int)B.Arena]) ArenaVariants.Toggle();   // тест цельной арены из Meshy
            // время суток и дождь — сразу пересчитать погоду и свет арены (без перезапуска боя)
            if (down[(int)B.DayTime]) { DayCycle.NextMode(); if (Weather.Instance != null) Weather.Instance.Roll(); }
            if (down[(int)B.NoRain]) { Weather.NoRain = !Weather.NoRain; if (Weather.Instance != null) Weather.Instance.Roll(); }
            if (down[(int)B.AllyLevel]) SideDuel.AiLevel = (SideDuel.AiLevel + 1) % 3;
            if (down[(int)B.AllyImmortal]) SideDuel.ImmortalMode = (SideDuel.ImmortalMode + 1) % 4;
            if (down[(int)B.AllyFeature]) { SideDuel.Enabled = !SideDuel.Enabled; if (duel != null) { devOpen = false; duel.SetPaused(false); duel.ResetMatch(); } return; }
            if (down[(int)B.Ally]) { Profile.SetAlly(Profile.Ally >= 2 || Profile.AllyDown ? -1 : Profile.Ally + 1); /* нет → ученик → воин → мастер → нет */ if (duel != null) { devOpen = false; duel.SetPaused(false); duel.ResetMatch(); } return; }
            // автобой (отладка): игроком управляет тот же ИИ, что и врагом
            if (player != null && (DuelManager.AutoBattle || playerAi != null))
            {
                if (playerAi == null) { playerAi = player.gameObject.AddComponent<EnemyAI>(); playerAi.autoPlayer = true; }
                playerAi.self = player; playerAi.target = enemy; playerAi.aiEnabled = DuelManager.AutoBattle; playerAi.difficultyOverride = DuelManager.AutoLevel; playerAi.hold = SideDuel.Held(player);
                if (!DuelManager.AutoBattle && autoWasOn) { player.SetMoveVector(Vector2.zero); player.SetBlock(false); }
                autoWasOn = DuelManager.AutoBattle;
            }
            if (pauseBtn && duel != null && !Over) duel.SetPaused(!duel.Paused);
            if (Paused || Over) return;

            if (SideDuel.Held(player)) { hasBuffered = false; return; }   // ждёт у края арены, пока бьётся соратник
            if (player != null && !DuelManager.AutoBattle)
            {
                Vector2 mv = ToWorld(Vector2.ClampMagnitude(stickVec + kb, 1f));
                player.SetMoveVector(mv);
                if (jump) player.Jump();
                if (dash) player.Dash(mv);
                if (superBtn && duel != null) duel.TrySuper(player);
                if (healBtn) player.TryDrink();
                bool blockHeld = held[(int)B.Block];
#if ENABLE_LEGACY_INPUT_MANAGER
                blockHeld |= Input.GetKey(KeyCode.B) || Input.GetKey(KeyCode.LeftControl);
#endif
                player.SetBlock(blockHeld);
                // буфер ввода: нажал во время удара — следующий начнётся сам, как только можно (серия из восстановления)
                // «ДУХ»: короткое нажатие — шар (на отпускании), удержание — накопление удара с узором на полу
                bool eHeld = held[(int)B.Energy];
#if ENABLE_LEGACY_INPUT_MANAGER
                eHeld |= Input.GetKey(KeyCode.M);
#endif
                ChargeSystem cs = ChargeSystem.Instance;
                bool charging = cs != null && cs.IsCharging(player);
                if (aEnergy) { energyPending = true; energyDownAt = Time.unscaledTime; }
                if (energyPending && eHeld && cs != null && Time.unscaledTime - energyDownAt >= 0.28f && cs.Begin(player)) { energyPending = false; charging = true; }
                if (charging)
                {
                    // другой удар во время накопления — выпустить накопленное этим ударом; рывок/прыжок/блок — сорвать
                    AttackType rt = AttackType.Overhead; bool other = false;
                    if (aRise) { rt = AttackType.Rising; other = true; }
                    else if (aThrust) { rt = AttackType.Thrust; other = true; }
                    else if (aSlash) { rt = AttackType.Slash; other = true; }
                    else if (aLegs) { rt = AttackType.LegSweep; other = true; }
                    else if (aOver) other = true;
                    if (dash || jump || blockHeld) cs.Cancel(player);
                    else if (other || !eHeld) cs.Release(player, rt);
                    aOver = aRise = aLegs = aThrust = aSlash = false;
                }
                else if (energyPending && !eHeld) { energyPending = false; player.Attack(AttackType.Energy); }
                if (aOver) Press(AttackType.Overhead);
                if (aRise) Press(AttackType.Rising);
                if (aLegs) Press(AttackType.LegSweep);
                if (aThrust) Press(AttackType.Thrust);
                if (aSlash) { Press(slashFlip ? AttackType.SlashBack : AttackType.Slash); slashFlip = !slashFlip; }
                if (hasBuffered && Time.unscaledTime - bufferedAt < 0.35f && player.AttackOrChain(buffered)) hasBuffered = false;
                else if (hasBuffered && Time.unscaledTime - bufferedAt >= 0.35f) hasBuffered = false;
            }
            if (enemyCycle) enemyTypeIndex = (enemyTypeIndex + 1) % EnemyTypes.Length;
            if (enemy != null && enemyAttack) enemy.Attack(EnemyTypes[enemyTypeIndex]);
        }

        static Vector2 ToWorld(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-6f) return Vector2.zero;
            Transform cam = CameraOrbit.Instance != null ? CameraOrbit.Instance.transform : (Camera.main != null ? Camera.main.transform : null);
            Vector3 f = cam != null ? cam.forward : Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = cam != null ? cam.up : Vector3.forward;
            f.y = 0f;
            f.Normalize();
            Vector3 r = new Vector3(f.z, 0f, -f.x);
            Vector3 w = r * v.x + f * v.y;
            return new Vector2(w.x, w.z);
        }

        void Circle(B b, float x, float y, float r) { int i = (int)b; round[i] = true; ctr[i] = new Vector2(x, y); rad[i] = r; rects[i] = new Rect(x - r, y - r, r * 2f, r * 2f); }
        void Box(B b, float x, float y, float w, float h) { int i = (int)b; round[i] = false; rects[i] = new Rect(x, y, w, h); ctr[i] = rects[i].center; }

        void Layout()
        {
            scale = Mathf.Max(0.01f, Screen.height / 540f);
            float W = Screen.width / scale, H = 540f;
            bool paused = Paused, over = Over, play = !paused && !over;

            Box(B.Stick, 0f, H - 330f, 360f, 330f);
            // правый кластер — компактная дуга (по жалобе «интерфейс атак закрывает пол-экрана»): в углу главная «сверху»,
            // по дуге вплотную — укол, рез, снизу, по ногам; вторым поясом — блок, рывок, прыжок, шар, тыква; «дух» — над дугой.
            // Занимает ~300×320 вместо ~450×370, кнопки полупрозрачные, пока их не жмут.
            Vector2 C = new Vector2(W - 82f, H - 82f);
            System.Func<float, float, Vector2> At = (deg, d) => C + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * d;
            Vector2 p;
            // три удара (рез и «снизу» убраны — делали то же, что остальные): «сверху» в углу под большим пальцем,
            // по ближней дуге — укол (вверх), блок (по диагонали, главная защита — в один короткий жест), по ногам (влево);
            // вторым поясом — рывок, прыжок, шар, тыква. Кнопки удара крупнее прежних: промахиваться труднее.
            Circle(B.Overhead, C.x, C.y, 50f);
            p = At(270f, 122f); Circle(B.Thrust, p.x, p.y, 34f);
            p = At(225f, 126f); Circle(B.Block, p.x, p.y, 36f);
            p = At(180f, 122f); Circle(B.Legs, p.x, p.y, 34f);
            p = At(170f, 212f); Circle(B.Heal, p.x, p.y, 23f);   // ниже 165° уходит за нижний край экрана
            p = At(194f, 212f); Circle(B.Dash, p.x, p.y, 28f);
            p = At(232f, 214f); Circle(B.Jump, p.x, p.y, 28f);
            p = At(268f, 212f); Circle(B.Energy, p.x, p.y, 25f);
            // «дух» — рядом с дугой внизу, под большим пальцем: на месте над дугой (238°, 286) кнопка
            // висела посреди экрана поверх боя
            p = At(186f, 292f); Circle(B.Super, p.x, p.y, 30f);
            Circle(B.Pause, W * 0.5f, 88f, 18f);   // под таймером раунда

            float cx = W * 0.5f;
            // пауза в две колонки: слева — бой и выход, справа — камера, таймер и отладка
            float lx = cx - 316f, rx = cx + 16f;
            Box(B.Resume, lx, 120f, 300f, 40f);
            Box(B.Restart, lx, 170f, 300f, 40f);
            Box(B.Difficulty, lx, 220f, 300f, 40f);
            Box(B.Gfx, lx, 270f, 300f, 40f);
            Box(B.Vibro, lx, 320f, 300f, 40f);
            // две колонки по шесть кнопок: «СНАРЯЖЕНИЕ» — рядом с «ПРОКАЧКОЙ», справа не остаётся пустой дыры
            Box(B.ToMenu, lx, 370f, 300f, 40f);
            Box(B.CamMode, rx, 120f, 300f, 40f);
            Box(B.CamReset, rx, 170f, 300f, 40f);
            Box(B.TimerToggle, rx, 220f, 300f, 40f);
            Box(B.DevMenu, rx, 270f, 300f, 40f);
            Box(B.Stats, rx, 320f, 300f, 40f);
            Box(B.Gear, rx, 370f, 300f, 40f);
            // меню разработчика (вместо обычных кнопок паузы): отладка и проверка боя
            // меню разработчика — три колонки по шесть кнопок (надписи сами ужимаются по ширине кнопки)
            {
                B[] dev = { B.AiToggle, B.AutoBattle, B.DebugToggle,
                            B.AutoLevel, B.EnemyImmortal, B.Immortal,
                            B.WinFight, B.Arena, B.Clavicle,
                            B.Twist, B.AllyFeature, B.Ally,
                            B.AllyLevel, B.AllyImmortal, B.DayTime,
                            B.NoRain, B.Sound, B.SoundScope };
                for (int k = 0; k < dev.Length; k++)
                    Box(dev[k], cx - 313f + (k % 3) * 213f, 120f + (k / 3) * 46f, 200f, 38f);
                Box(B.DevBack, cx - 150f, 404f, 300f, 38f);
            }
            Box(B.OverAgain, cx - 210f, 314f, 200f, 56f);   // ниже панели итогов боя
            Box(B.OverMenu, cx + 10f, 314f, 200f, 56f);
            Box(B.OverGear, cx - 115f, 378f, 230f, 56f);   // сразу под «НА КАРТУ» / «В МЕНЮ», а не на дне экрана

            for (int i = 0; i < Count; i++) active[i] = false;
            if (play)
                foreach (B b in new[] { B.Stick, B.Jump, B.Dash, B.Block, B.Overhead, B.Legs, B.Energy, B.Thrust, B.Pause, B.Heal }) active[(int)b] = true;
            if (play && player != null && player.KiFull) active[(int)B.Super] = true;
            if (!paused) devOpen = false;
            if (paused && devOpen)
                foreach (B b in new[] { B.AiToggle, B.DebugToggle, B.EnemyImmortal, B.AutoBattle, B.AutoLevel, B.Immortal, B.WinFight, B.Clavicle, B.Twist, B.Arena, B.Ally, B.AllyFeature, B.AllyLevel, B.AllyImmortal, B.DayTime, B.NoRain, B.Sound, B.SoundScope, B.DevBack }) active[(int)b] = true;
            else if (paused)
                foreach (B b in new[] { B.Resume, B.Restart, B.Difficulty, B.Gfx, B.Vibro, B.CamReset, B.ToMenu, B.CamMode, B.TimerToggle, B.Gear, B.Stats, B.DevMenu }) active[(int)b] = true;
            if (over) { active[(int)B.OverAgain] = true; active[(int)B.OverMenu] = true; active[(int)B.OverGear] = true; }

            labels[(int)B.Resume] = "ПРОДОЛЖИТЬ";
            labels[(int)B.Restart] = "ЗАНОВО";
            labels[(int)B.CamReset] = "КАМЕРА: СБРОС";
            labels[(int)B.AiToggle] = "ВРАГ: " + (ai == null || ai.aiEnabled ? "ИИ ВКЛ" : "СТОИТ");
            labels[(int)B.DebugToggle] = "ЗОНЫ ПОРАЖЕНИЯ: " + (debugViz != null && debugViz.visible ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.AutoBattle] = "АВТОБОЙ: " + (DuelManager.AutoBattle ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.Immortal] = "БЕССМЕРТИЕ ИГРОКА: " + (DuelManager.Immortal ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.EnemyImmortal] = "БЕССМЕРТИЕ ВРАГА: " + (DuelManager.EnemyImmortal ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.AutoLevel] = "ИИ ИГРОКА: " + (DuelManager.AutoLevel == 0 ? "УЧЕНИК" : DuelManager.AutoLevel == 1 ? "ВОИН" : "МАСТЕР");
            labels[(int)B.DevMenu] = "МЕНЮ РАЗРАБОТЧИКА";
            labels[(int)B.DevBack] = "НАЗАД";
            labels[(int)B.WinFight] = "ВЫИГРАТЬ БОЙ";
            labels[(int)B.AllyLevel] = "ИИ СПУТНИКОВ: " + (SideDuel.AiLevel == 0 ? "УЧЕНИК" : SideDuel.AiLevel == 1 ? "ВОИН" : "МАСТЕР");
            labels[(int)B.AllyImmortal] = "БЕССМЕРТИЕ СПУТН.: " + SideDuel.ImmortalNames[SideDuel.ImmortalMode];
            labels[(int)B.AllyFeature] = "СОРАТНИКИ: " + (SideDuel.Enabled ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.Ally] = "СОРАТНИК: " + (Profile.Ally < 0 ? "НЕТ" : Profile.AllyDown ? "ПАЛ" : Profile.AllyTierNames[Mathf.Clamp(Profile.Ally, 0, 2)]);
            labels[(int)B.DayTime] = "ВРЕМЯ СУТОК: " + DayCycle.ModeName(true);
            labels[(int)B.NoRain] = "ДОЖДЬ И ГРОЗА: " + (Weather.NoRain ? "ВЫКЛ" : "ВКЛ");
            labels[(int)B.Arena] = "АРЕНА: " + ArenaVariants.Names[Mathf.Clamp(ArenaVariants.Current, 0, ArenaVariants.Names.Length - 1)];
            labels[(int)B.Clavicle] = "РУКИ: " + (HeroRigDriver.DebugAnimSword ? "МОКАП" : "СТАРЫЙ IK");
            labels[(int)B.Twist] = "РУКИ: СКРУТКА " + Mathf.RoundToInt(HeroRigDriver.DebugTwistShare * 100f) + "%";
            labels[(int)B.ToMenu] = "В МЕНЮ";
            labels[(int)B.Gfx] = "ГРАФИКА: " + (DuelManager.Gfx == 1 ? "ВЫСОКАЯ" : "ЭКОНОМ");
            labels[(int)B.Vibro] = "ВИБРАЦИЯ: " + (Haptics.Enabled ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.Sound] = "ЗВУК: " + (SoundSettings.On ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.SoundScope] = "ЗВУЧИТ: " + SoundSettings.ScopeName;
            labels[(int)B.Difficulty] = "СЛОЖНОСТЬ: " + EnemyAI.DifficultyName;
            labels[(int)B.CamMode] = "КАМЕРА: " + (CameraOrbit.FreeMode ? "СВОБОДНАЯ" : "БОЕВАЯ");
            labels[(int)B.TimerToggle] = "ТАЙМЕР БОЯ: " + (DuelManager.TimerOn ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.OverAgain] = duel != null && duel.PlayerWonMatch ? "НА КАРТУ" : Campaign.InFight ? "К ТРАВНИЦЕ" : Campaign.Training ? "НА КАРТУ" : "ЕЩЁ РАЗ";
            labels[(int)B.OverMenu] = "В МЕНЮ";
            labels[(int)B.Gear] = "СНАРЯЖЕНИЕ";
            labels[(int)B.Stats] = "ПРОКАЧКА" + (Profile.Points > 0 ? "  (+" + Profile.Points + ")" : "");
            labels[(int)B.OverGear] = "СНАРЯЖЕНИЕ" + (Profile.Points > 0 ? " (+" + Profile.Points + ")" : "");

            if (!stickActive) { stickCenter = new Vector2(124f, H - 124f); stickKnob = stickCenter; }
        }

        void BeginStick(Vector2 p)
        {
            float H = 540f;
            stickCenter = new Vector2(Mathf.Clamp(p.x, StickRadius + 12f, 360f), Mathf.Clamp(p.y, H - 330f + StickRadius, H - StickRadius - 12f));
            stickKnob = stickCenter;
            stickActive = true;
        }

        void MoveStick(Vector2 p)
        {
            Vector2 d = p - stickCenter;
            float m = d.magnitude;
            if (m > StickRadius) d = d / m * StickRadius;
            stickKnob = stickCenter + d;
            Vector2 v = new Vector2(d.x, -d.y) / StickRadius;
            float len = v.magnitude;
            const float dead = 0.14f;
            stickVec = len < dead ? Vector2.zero : v / len * Mathf.Clamp01((len - dead) / (1f - dead));
        }

        void EndStick()
        {
            stickActive = false; stickFinger = -1; mouseStick = false; stickVec = Vector2.zero;
        }

        void Poll()
        {
            Array.Clear(held, 0, Count);
            Array.Clear(down, 0, Count);
            pointersHeld.Clear();
            pointersDown.Clear();
            bool stickOk = active[(int)B.Stick];
            if (!stickOk && stickActive) EndStick();
#if ENABLE_LEGACY_INPUT_MANAGER
            int tc = Input.touchCount;
            bool stickSeen = false;
            for (int i = 0; i < tc; i++)
            {
                Touch t = Input.GetTouch(i);
                Vector2 p = new Vector2(t.position.x / scale, (Screen.height - t.position.y) / scale);
                bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;

                if (stickOk && t.phase == TouchPhase.Began && stickFinger < 0 && rects[(int)B.Stick].Contains(p) && !OnAnyButton(p))
                {
                    stickFinger = t.fingerId;
                    BeginStick(p);
                }
                if (t.fingerId == stickFinger)
                {
                    if (ended) EndStick();
                    else { stickSeen = true; MoveStick(p); }
                    continue;
                }
                if (ended) continue;
                pointersHeld.Add(p);
                if (t.phase == TouchPhase.Began) pointersDown.Add(p);
            }
            if (stickFinger >= 0 && !stickSeen) EndStick();
            if (tc == 0)
            {
                Vector2 m = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
                if (stickOk && Input.GetMouseButtonDown(0) && rects[(int)B.Stick].Contains(m) && !OnAnyButton(m)) { mouseStick = true; BeginStick(m); }
                if (mouseStick)
                {
                    if (Input.GetMouseButton(0)) MoveStick(m); else EndStick();
                }
                else
                {
                    if (Input.GetMouseButton(0)) pointersHeld.Add(m);
                    if (Input.GetMouseButtonDown(0)) pointersDown.Add(m);
                }
            }
#endif
            for (int b = 1; b < Count; b++)
            {
                if (!active[b]) continue;
                float mg = round[b] ? 5f : 0f;   // кнопки стали меньше — палец попадает с небольшим запасом
                for (int i = 0; i < pointersHeld.Count; i++) if (Hit(b, pointersHeld[i], mg)) { held[b] = true; break; }
                for (int i = 0; i < pointersDown.Count; i++) if (Hit(b, pointersDown[i], mg)) { down[b] = true; break; }
            }
        }

        bool OnAnyButton(Vector2 p)
        {
            for (int b = 1; b < Count; b++) if (active[b] && Hit(b, p, 0f)) return true;
            return false;
        }

        // ------------------------------------------------------------------ отрисовка

        static readonly Color Gold = VisualIdentity.Brass;

        void OnGUI()
        {
            if (InventoryScreen.Open || MapScreen.Open) return;
            // пауза — поверх всего боевого интерфейса: вступление раунда, «НАЧАЛИ!», карточка врага, объявления не ложатся на её кнопки
            int previousInputDepth = GUI.depth;
            GUI.depth = Paused || Over ? -100 : 0;  // victory/pause buttons ALWAYS above HUD vignette and dark overlay
            Matrix4x4 old = UiKit.Begin();
            float W = UiKit.Width;
            bool paused = Paused, over = Over;

            if (active[(int)B.Stick]) DrawStick();
            // первый раунд: пока враг представляется в полный рост справа, кнопки атак приглушены и не спорят с ним
            // ровно пока виден враг в полный рост (та же кривая, что у картинки в DuelHud) и никогда — во время выхвата:
            // на «РУБИ!» кнопку «сверху» нужно видеть сразу
            introDim = 1f;
            if (duel != null && duel.State == DuelState.Intro && duel.Round == 1 && !paused && !duel.IaiWaiting)
            {
                float ia = Mathf.Clamp01(duel.StateTime / 0.5f) * Mathf.Clamp01((duel.IntroLength - duel.StateTime) / 0.5f);
                introDim = 1f - 0.82f * ia;
            }

            bool canAct = player != null && player.CanAct && player.weapon != null && player.weapon.Phase == SwingPhase.Idle;
            bool air = player != null && !player.Grounded;
            // выхват: кнопки удара на «РУБИ!» — в полную силу (боец замер во вступлении и формально «не может действовать»,
            // из-за этого кнопка рисовалась тусклой). Свечение — ПОД кнопкой, иначе оранжевое пятно закрывало сам значок.
            bool iai = duel != null && duel.IaiWaiting;
            if (iai) canAct = true;
            if (duel != null && duel.IaiCued && active[(int)B.Overhead] && Event.current.type == EventType.Repaint)
            {
                Vector2 ic = ctr[(int)B.Overhead]; float ir = rad[(int)B.Overhead];
                float ip = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 14f);
                Texture2D glow = UiKit.Tex("FX/glow_pool");
                if (glow != null)
                {
                    Color oc = GUI.color; GUI.color = new Color(1f, 0.35f, 0.15f, 0.55f + 0.35f * ip);
                    float gr = ir * (1.9f + 0.25f * ip);
                    GUI.DrawTexture(new Rect(ic.x - gr, ic.y - gr, gr * 2f, gr * 2f), glow);
                    GUI.color = oc;
                }
            }
            DrawAttack(B.Overhead, Icon.Down, air ? "В ПРЫЖКЕ" : "СВЕРХУ", canAct);
            if (duel != null && duel.IaiCued && active[(int)B.Overhead] && Event.current.type == EventType.Repaint)
            {
                Vector2 ic = ctr[(int)B.Overhead]; float ir = rad[(int)B.Overhead];
                float ip = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 14f);
                UiKit.Ring(ic, ir * (1.08f + 0.1f * ip), new Color(1f, 0.55f, 0.25f, 0.95f));
                UiKit.Ring(ic, ir * (1.25f + 0.25f * ip), new Color(1f, 0.4f, 0.2f, 0.6f * (1f - ip)), true);
            }
            DrawAttack(B.Thrust, Icon.Thrust, air ? "" : "УКОЛ", canAct && !air);
            DrawAttack(B.Slash, slashFlip ? Icon.SlashBack : Icon.Slash, air ? "" : "РЕЗ", canAct && !air);
            DrawAttack(B.Rising, Icon.Up, air ? "В ПРЫЖКЕ" : "СНИЗУ", canAct);
            DrawAttack(B.Legs, Icon.Low, air ? "" : "ПО НОГАМ", canAct && !air);
            bool chg = player != null && ChargeSystem.Instance != null && ChargeSystem.Instance.IsCharging(player);
            if (chg)
            {
                int cl = ChargeSystem.Instance.Level(player);
                DrawAttack(B.Energy, Icon.Orb, cl < 0 ? "НАКОПЛЕНИЕ" : cl == 0 ? "НАТИСК" : cl == 1 ? "ПРОРЫВ" : cl == 2 ? "СОКРУШЕНИЕ" : "ЯРОСТЬ", true);
                DrawKiRing(B.Energy, ChargeSystem.Instance.Progress(player));
            }
            else
            {
                DrawAttack(B.Energy, Icon.Orb, "ШАР", canAct && player.CanEnergy);
                DrawKiRing(B.Energy, player != null ? Mathf.Clamp01(player.Ki / Mathf.Max(1f, player.energyCost)) : 0f);
            }
            DrawAttack(B.Jump, Icon.Jump, "ПРЫЖОК", player != null && player.CanAct && player.Grounded);
            DrawAttack(B.Block, Icon.Shield, "БЛОК", player != null && player.CanAct && !player.GuardBroken && player.weapon != null && player.weapon.Phase == SwingPhase.Idle);
            DrawAttack(B.Dash, Icon.Dash, "РЫВОК", player != null && player.CanAct && player.DashReady01 >= 1f);
            DrawAttack(B.Heal, Icon.Orb, "ТЫКВА", player != null && player.CanDrink);
            if (player != null && active[(int)B.Heal])
            {
                // глотков осталось: точки под кнопкой
                Vector2 hc = ctr[(int)B.Heal]; float hr = rad[(int)B.Heal];
                for (int g = 0; g < player.gourdMax; g++)
                {
                    Vector2 p = hc + new Vector2((g - (player.gourdMax - 1) * 0.5f) * 11f, -hr - 8f);
                    UiKit.Disc(p, 3.8f, g < player.Gourds ? new Color(1f, 0.72f, 0.3f, 0.95f) : new Color(0.3f, 0.3f, 0.32f, 0.6f));
                }
            }
            // запас блока: точки вокруг щита тают, пока держишь блок; красные — блок выбит
            if (player != null && (player.Blocking || player.GuardBroken))
                DrawDotRing(B.Block, player.GuardBroken ? 1f : 1f - player.BlockTime / 2.5f, player.GuardBroken ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(0.9f, 0.85f, 0.6f, 1f));
            DrawSuper();
            DrawPause();

            if (!paused) pausedAt = -1f; else if (pausedAt < 0f) pausedAt = Time.unscaledTime;
            if (paused)
            {
                UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.65f));
                Rect pr = new Rect(W * 0.5f - 340f, 54f, 680f, 400f);
                // касание мимо окна паузы — продолжить бой сразу (кроме меню разработчика и открытого снаряжения;
                // первые 0,3 с не считаются — то же касание, что открыло паузу, не закрывает её)
                if (!devOpen && !InventoryScreen.Open && duel != null && Time.unscaledTime - pausedAt > 0.3f
                    && Event.current.type == EventType.MouseDown && !new Rect(pr.x - 10f, 48f, pr.width + 20f, pr.height + 56f).Contains(Event.current.mousePosition))
                { duel.SetPaused(false); Event.current.Use(); }   // обычная пауза — с рядом «звук»   // меню разработчика — прежнего размера   // свиток-подложка под кнопками паузы
                Texture2D scroll = null;   // единый стиль: окно чёрного лака вместо свитка
                if (scroll != null)
                {
                    // развёрнутый свиток макимоно: бумага с роликами по краям, на ней — горы тушью; ветка сакуры свисает с угла
                    Color o = GUI.color; GUI.color = new Color(0.86f, 0.82f, 0.74f, 1f);
                    // бумага в текстуре — x 14,6…86 %, y 15,8…85,7 %: растягиваем так, чтобы лист лёг на (pr.x−10, 48, pr.w+20, 432)
                    Rect paper = new Rect(pr.x - 10f, 48f, pr.width + 20f, pr.height + 56f);
                    float tw = paper.width / 0.714f, th = paper.height / 0.699f;
                    GUI.DrawTexture(new Rect(paper.x - 0.146f * tw, paper.y - 0.158f * th, tw, th), scroll, ScaleMode.StretchToFill);
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    Texture2D mt = UiKit.Tex("UI/mountains");
                    if (mt != null) GUI.DrawTexture(new Rect(paper.x + 20f, paper.yMax - 130f, paper.width - 40f, 124f), mt, ScaleMode.StretchToFill);
                    GUI.color = o;
                    UiKit.Text(new Rect(W * 0.5f - 200f, 62f, 400f, 50f), "ПАУЗА", 40f, new Color(0.16f, 0.09f, 0.06f, 1f));
                    Texture2D br = UiKit.Tex("UI/sakura_branch");
                    if (br != null) GUI.DrawTexture(new Rect(paper.x - 150f, -30f, 180f, 180f), br, ScaleMode.ScaleToFit);
                }
                else
                {
                    VisualIdentity.KitSurface(pr, 0.98f);
                    VisualIdentity.TitlePlaque(new Rect(W * 0.5f - 200f, 64f, 400f, 44f), "ПАУЗА", 30f, TextAnchor.MiddleCenter, Gold);
                }
                if (devOpen)
                {
                    UiKit.Rect(new Rect(W * 0.5f - 200f, 64f, 400f, 46f), new Color(0.12f, 0.05f, 0.03f, 0.9f));
                    UiKit.Text(new Rect(W * 0.5f - 200f, 64f, 400f, 46f), "МЕНЮ РАЗРАБОТЧИКА", 24f, Gold);
                    foreach (B b in new[] { B.AiToggle, B.DebugToggle, B.EnemyImmortal, B.AutoBattle, B.AutoLevel, B.Immortal, B.WinFight, B.Clavicle, B.Twist, B.Arena, B.Ally, B.AllyFeature, B.AllyLevel, B.AllyImmortal, B.DayTime, B.NoRain, B.Sound, B.SoundScope, B.DevBack }) DrawRectButton((int)b);
                }
                else
                {
                    DrawRectButton((int)B.Resume); DrawRectButton((int)B.Restart); DrawRectButton((int)B.CamReset); DrawRectButton((int)B.ToMenu);
                    DrawRectButton((int)B.Vibro); DrawRectButton((int)B.Difficulty); DrawRectButton((int)B.Gfx);
                    DrawRectButton((int)B.CamMode); DrawRectButton((int)B.TimerToggle); DrawRectButton((int)B.Gear); DrawRectButton((int)B.Stats);
                    DrawRectButton((int)B.DevMenu);
                }
                UiKit.Text(new Rect(W * 0.5f - 400f, 478f, 800f, 24f),
                    "ПУТЬ " + (Ladder.Index + 1) + "/" + Ladder.Count + "  ·  ПОБЕД " + Records.Wins + "  ·  ПОРАЖЕНИЙ " + Records.Losses + "  ·  ЛУЧШЕЕ " + Records.BestStage + "/" + Ladder.Count + (Records.Cleared > 0 ? "  ·  ПРОЙДЕН ×" + Records.Cleared : ""),
                    13f, new Color(0.85f, 0.8f, 0.7f, 0.9f));
            }
            // Match-result actions are painted by DuelHud AFTER its full-screen
            // dark overlay in the SAME OnGUI pass. Rendering them here in a
            // separate MonoBehaviour allowed the overlay to obscure the buttons.
            GUI.matrix = old;
            GUI.depth = previousInputDepth;
        }

        /// <summary>Draw only match-result actions in DuelHud's final foreground
        /// pass. Input remains touch-driven here; the HUD does not handle clicks.
        /// Caller must have applied UiKit.Begin() and painted the dimming first.
        /// </summary>
        public void DrawResultActionsFromHud()
        {
            if (!isActiveAndEnabled || !Over || InventoryScreen.Open || MapScreen.Open) return;
            // Paint the complete buttons after the overlay, not at the mercy
            // of ordering between different MonoBehaviour.OnGUI callbacks.
            DrawRectButton((int)B.OverAgain);
            DrawRectButton((int)B.OverMenu);
            DrawRectButton((int)B.OverGear);
        }

        void DrawRectButton(int b)
        {
            Rect r = rects[b];
            bool h = held[b];
            // Buttons are drawn here, but input is processed by the existing
            // touch controller. Do not use GUI.Button (would double-activate).
            // единый стиль: чёрная лаковая кнопка с золотой окантовкой (нажатая — вдавленная)
            bool main = b == (int)B.OverAgain || b == (int)B.Resume;   // главное действие — кроваво-красная кнопка
            bool resultButton = b == (int)B.OverAgain || b == (int)B.OverMenu || b == (int)B.OverGear;
            Color oldTint = GUI.color;
            Color oldContentTint = GUI.contentColor;
            Color oldBackgroundTint = GUI.backgroundColor;
            GUI.color = Color.white;
            if (resultButton)
            {
                // Result actions must be full-bright regardless of other
                // IMGUI controls or the background victory vignette.
                GUI.contentColor = Color.white;
                GUI.backgroundColor = Color.white;
            }
            Texture2D wood = UiKit.Tex(main ? (h ? "UI/Kit/ui_btn_red_pressed" : "UI/Kit/ui_btn_red") : (h ? "UI/Kit/ui_btn_pressed" : "UI/Kit/ui_btn"));
            if (wood == null) wood = UiKit.Tex("UI/plank");
            if (wood != null)
            {
                Rect vr = new Rect(r.x - r.height * 0.10f, r.y - r.height * 0.06f, r.width + r.height * 0.20f, r.height * 1.12f);
                // Match-result buttons use the complete original artwork. Segmenting
                // this sprite repeats/distorts its lacquer fill and darkens the face.
                // Do not place a second rectangle over the painted button.
                if (resultButton) GUI.DrawTexture(vr, wood, ScaleMode.StretchToFill, true);
                else UiKit.Slice(vr, wood, vr.height / wood.height, 0.14f, 0.12f, 0.5f, 0f, Color.white);
            }
            else VisualIdentity.Surface(r, 0.98f, false);
            // надпись подгоняется под гладкую середину кнопки (без завитков): «СНАРЯЖЕНИЕ (+10)» не вылезает
            UiKit.Text(VisualIdentity.TextArea(r), labels[b], Mathf.Min(18f, r.height * 0.44f),
                resultButton ? new Color(1f, 0.99f, 0.91f, 1f)
                    : h ? VisualIdentity.BrassLight : VisualIdentity.Parchment);
            GUI.backgroundColor = oldBackgroundTint;
            GUI.contentColor = oldContentTint;
            GUI.color = oldTint;
        }

        void DrawStick()
        {
            float a = stickActive ? 1f : 0.55f;
            float R = StickRadius;
            Texture2D sb = UiKit.Tex("UI/stick_base"), sk = UiKit.Tex("UI/stick_knob");
            if (sb != null && sk != null)
            {
                UiKit.Image(stickCenter, R * 1.05f, sb, new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 0.95f, a)));
                UiKit.Image(stickKnob, 34f, sk, stickActive ? new Color(1f, 0.85f, 0.75f, 1f) : new Color(1f, 1f, 1f, 0.75f));
                return;
            }
            UiKit.Disc(stickCenter, R, new Color(0.05f, 0.06f, 0.1f, 0.5f * a));
            UiKit.Ring(stickCenter, R, new Color(Gold.r, Gold.g, Gold.b, 0.7f * a));
            UiKit.Ring(stickCenter, R * 0.55f, new Color(1f, 1f, 1f, 0.12f * a), true);
            // стрелки направлений
            for (int i = 0; i < 4; i++)
            {
                float ang = i * 90f * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 p = stickCenter + d * (R * 0.8f);
                Vector2 n = new Vector2(-d.y, d.x);
                Color c = new Color(1f, 1f, 1f, 0.35f * a);
                UiKit.Line(p - d * 6f + n * 7f, p + d * 3f, 2f, c);
                UiKit.Line(p - d * 6f - n * 7f, p + d * 3f, 2f, c);
            }
            float k = 34f;
            UiKit.Disc(stickKnob, k, stickActive ? new Color(1f, 0.55f, 0.3f, 0.95f) : new Color(0.85f, 0.85f, 0.88f, 0.55f));
            UiKit.Ring(stickKnob, k, new Color(0f, 0f, 0f, 0.5f));
        }

        enum Icon { Down, Up, Low, Thrust, Slash, SlashBack, Orb, Jump, Dash, Shield }

        float introDim = 1f;
        void DrawAttack(B b, Icon icon, string name, bool ready)
        {
            int i = (int)b;
            if (!active[i]) return;
            Vector2 c = ctr[i]; float r = rad[i];
            bool h = held[i];
            float a = (ready ? 1f : 0.45f) * introDim;
            // кнопки в стиле референса: каменный круг в металлической оправе; у главных — свой нарисованный значок
            string full = null;
            switch (b)
            {
                case B.Overhead: full = "UI/btn_overhead"; break;
                case B.Rising: full = "UI/btn_rising"; break;
                case B.Legs: full = "UI/btn_legs"; break;
                case B.Thrust: full = "UI/btn_thrust"; break;
                case B.Slash: full = "UI/btn_slash"; break;
                case B.Energy: full = "UI/btn_energy"; break;
                case B.Block: full = "UI/btn_block"; break;
                case B.Dash: full = "UI/btn_dash"; break;
                case B.Jump: full = "UI/btn_jump"; break;
                case B.Heal: full = "UI/btn_heal"; break;
            }
            Texture2D ft = full != null ? UiKit.Tex(full) : null;
            Texture2D bt = ft != null ? ft : UiKit.Tex("UI/btn_base");
            if (bt != null)
            {
                // в покое кнопки полупрозрачные (сквозь них видно бой), нажатая — яркая; главная «сверху» чуть плотнее
                float idle = b == B.Overhead ? (duel != null && duel.IaiCued ? 1f : 0.78f) : 0.6f;   // на «РУБИ!» — без прозрачности
                Color tint = h ? new Color(1f, 0.7f, 0.55f, 1f) : new Color(1f, 1f, 1f, (ready ? idle : idle * 0.5f) * introDim);
                float rr = r * (h ? 0.94f : 1f);
                UiKit.Disc(c + new Vector2(0f, 3f), r * 0.98f, new Color(0f, 0f, 0f, 0.16f * a));
                UiKit.Image(c, rr * 1.12f, bt, tint);   // новые кнопки: круг в золотом кольце с шипами — шипы выходят за круг касания
                if (h) UiKit.Disc(c, rr * 0.86f, new Color(1f, 0.45f, 0.15f, 0.22f));
                if (ft == null) DrawIcon(icon, c + new Vector2(0f, -r * 0.08f), r * 0.46f, new Color(0.85f, 0.85f, 0.85f, 0.95f * a), r >= 50f ? 4.5f : 3.5f);
                if (!string.IsNullOrEmpty(name) && ft == null)
                    UiKit.Text(new Rect(c.x - r, c.y + r * 0.42f, r * 2f, r * 0.42f), name, Mathf.Max(8f, r * 0.22f), new Color(0.85f, 0.82f, 0.75f, 0.8f * a), TextAnchor.MiddleCenter, false);
                return;
            }
            UiKit.Disc(c + new Vector2(0f, 3f), r, new Color(0f, 0f, 0f, 0.35f * a));
            UiKit.Disc(c, r, h ? new Color(0.75f, 0.28f, 0.1f, 0.95f) : new Color(0.06f, 0.05f, 0.07f, 0.72f * Mathf.Lerp(0.6f, 1f, a)));
            UiKit.Ring(c, r, new Color(Gold.r, Gold.g, Gold.b, 0.95f * a));
            UiKit.Ring(c, r * 0.86f, new Color(1f, 1f, 1f, 0.12f * a), true);
            DrawIcon(icon, c + new Vector2(0f, -r * 0.12f), r * 0.5f, new Color(1f, 1f, 1f, 0.95f * a), r >= 50f ? 4f : 3f);
            if (!string.IsNullOrEmpty(name))
                UiKit.Text(new Rect(c.x - r, c.y + r * 0.45f, r * 2f, r * 0.45f), name, Mathf.Max(8f, r * 0.24f), new Color(0.95f, 0.88f, 0.7f, a), TextAnchor.MiddleCenter, false);
        }

        /// <summary>Кольцо из точек вокруг кнопки шара: заполненность = накопленный дух до стоимости шара.</summary>
        void DrawKiRing(B b, float k)
        {
            int i = (int)b;
            if (!active[i]) return;
            Vector2 c = ctr[i]; float r = rad[i] + 6f;
            const int N = 20;
            for (int s = 0; s < N; s++)
            {
                float ang = -Mathf.PI * 0.5f + s / (float)N * Mathf.PI * 2f;
                bool on = s < Mathf.FloorToInt(k * N + 0.001f);
                Color col = on ? (k >= 1f ? new Color(0.5f, 0.85f, 1f, 1f) : new Color(0.35f, 0.6f, 0.95f, 0.95f)) : new Color(0f, 0f, 0f, 0.4f);
                col.a *= introDim;
                UiKit.Disc(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r, 2.4f, col);
            }
        }

        static void Arrow(Vector2 from, Vector2 to, float s, float th, Color c)
        {
            UiKit.Line(from, to, th, c);
            Vector2 d = (to - from).normalized, n = new Vector2(-d.y, d.x);
            UiKit.Line(to, to - d * s + n * s * 0.6f, th, c);
            UiKit.Line(to, to - d * s - n * s * 0.6f, th, c);
        }

        static void DrawIcon(Icon icon, Vector2 c, float s, Color col, float th)
        {
            switch (icon)
            {
                case Icon.Down: Arrow(c + new Vector2(0f, -s), c + new Vector2(0f, s), s * 0.5f, th, col); break;
                case Icon.Up: Arrow(c + new Vector2(-s * 0.7f, s * 0.8f), c + new Vector2(s * 0.7f, -s * 0.8f), s * 0.5f, th, col); break;
                case Icon.Low: Arrow(c + new Vector2(-s, s * 0.5f), c + new Vector2(s, s * 0.5f), s * 0.45f, th, col); UiKit.Line(c + new Vector2(-s, s * 0.9f), c + new Vector2(s, s * 0.9f), th * 0.6f, new Color(col.r, col.g, col.b, col.a * 0.5f)); break;
                case Icon.Thrust: Arrow(c + new Vector2(-s, 0f), c + new Vector2(s, 0f), s * 0.5f, th, col); break;
                case Icon.Slash:
                case Icon.SlashBack:
                    {
                        float sg = icon == Icon.Slash ? 1f : -1f;
                        Vector2 prev = Vector2.zero; bool has = false;
                        for (int i = 0; i <= 8; i++)
                        {
                            float a = Mathf.Lerp(-70f, 70f, i / 8f) * Mathf.Deg2Rad;
                            Vector2 p = c + new Vector2(Mathf.Sin(a) * s * sg, -Mathf.Cos(a) * s * 0.55f + s * 0.5f);
                            if (has) { if (i == 8) Arrow(prev, p, s * 0.5f, th, col); else UiKit.Line(prev, p, th, col); }
                            prev = p; has = true;
                        }
                        break;
                    }
                case Icon.Orb: UiKit.Disc(c, s * 0.55f, col); UiKit.Ring(c, s * 0.9f, new Color(col.r, col.g, col.b, col.a * 0.7f), true); break;
                case Icon.Jump:
                    UiKit.Line(c + new Vector2(-s * 0.7f, s * 0.2f), c + new Vector2(0f, -s * 0.5f), th, col);
                    UiKit.Line(c + new Vector2(s * 0.7f, s * 0.2f), c + new Vector2(0f, -s * 0.5f), th, col);
                    UiKit.Line(c + new Vector2(-s * 0.7f, s * 0.8f), c + new Vector2(0f, 0.1f * s), th, new Color(col.r, col.g, col.b, col.a * 0.6f));
                    UiKit.Line(c + new Vector2(s * 0.7f, s * 0.8f), c + new Vector2(0f, 0.1f * s), th, new Color(col.r, col.g, col.b, col.a * 0.6f));
                    break;
                case Icon.Shield:
                    {
                        Vector2[] pts = { new Vector2(-0.75f, -0.8f), new Vector2(0.75f, -0.8f), new Vector2(0.75f, 0.1f), new Vector2(0f, 0.95f), new Vector2(-0.75f, 0.1f) };
                        for (int i = 0; i < pts.Length; i++) UiKit.Line(c + pts[i] * s, c + pts[(i + 1) % pts.Length] * s, th, col);
                        UiKit.Line(c + new Vector2(0f, -0.6f) * s, c + new Vector2(0f, 0.55f) * s, th * 0.6f, new Color(col.r, col.g, col.b, col.a * 0.6f));
                        break;
                    }
                case Icon.Dash:
                    UiKit.Line(c + new Vector2(-s * 0.9f, -s * 0.6f), c + new Vector2(-s * 0.2f, 0f), th, col);
                    UiKit.Line(c + new Vector2(-s * 0.9f, s * 0.6f), c + new Vector2(-s * 0.2f, 0f), th, col);
                    UiKit.Line(c + new Vector2(0f, -s * 0.6f), c + new Vector2(s * 0.7f, 0f), th, col);
                    UiKit.Line(c + new Vector2(0f, s * 0.6f), c + new Vector2(s * 0.7f, 0f), th, col);
                    break;
            }
        }

        void DrawDotRing(B b, float k, Color on)
        {
            int i = (int)b;
            if (!active[i]) return;
            Vector2 c = ctr[i]; float r = rad[i] + 6f;
            const int N = 16;
            for (int s = 0; s < N; s++)
            {
                float ang = -Mathf.PI * 0.5f + s / (float)N * Mathf.PI * 2f;
                bool lit = s < Mathf.CeilToInt(Mathf.Clamp01(k) * N - 0.001f);
                UiKit.Disc(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r, 2.4f, lit ? on : new Color(0f, 0f, 0f, 0.4f));
            }
        }

        void DrawSuper()
        {
            int i = (int)B.Super;
            if (!active[i]) return;
            Vector2 c = ctr[i]; float r = rad[i];
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
            UiKit.Disc(c, r + 8f + pulse * 6f, new Color(0.4f, 0.8f, 1f, (0.18f + 0.15f * pulse) * introDim));
            Texture2D st = UiKit.Tex("UI/btn_super");
            if (st != null)
            {
                UiKit.Image(c, r * (held[i] ? 1.15f : 1.25f), st, held[i] ? new Color(1f, 1f, 1f) : new Color(0.85f + 0.15f * pulse, 0.9f + 0.1f * pulse, 1f, introDim));
                return;
            }
            UiKit.Disc(c, r, held[i] ? new Color(0.9f, 0.95f, 1f, 1f) : new Color(0.1f, 0.35f, 0.6f, 0.92f));
            UiKit.Ring(c, r, new Color(1f, 0.9f, 0.55f, 1f));
            UiKit.Ring(c, r * 0.8f, new Color(1f, 1f, 1f, 0.35f), true);
            // молния-разрез
            Color w = Color.white;
            UiKit.Line(c + new Vector2(-r * 0.45f, r * 0.4f), c + new Vector2(r * 0.05f, -r * 0.05f), 4f, w);
            UiKit.Line(c + new Vector2(-r * 0.1f, r * 0.05f), c + new Vector2(r * 0.45f, -r * 0.45f), 4f, w);
            UiKit.Text(new Rect(c.x - r, c.y + r * 0.3f, r * 2f, r * 0.5f), "ДУХ!", 12f, new Color(1f, 0.95f, 0.8f), TextAnchor.MiddleCenter, false);
        }

        float pausedAt = -1f;   // когда открылась пауза (касание мимо окна закрывает её не сразу же)

        void DrawPause()
        {
            int i = (int)B.Pause;
            if (!active[i]) return;
            Vector2 c = ctr[i]; float r = rad[i];
            Texture2D pt = UiKit.Tex("UI/btn_pause");
            if (pt != null) { UiKit.Image(c, r * 1.35f, pt, held[i] ? new Color(1f, 0.7f, 0.55f) : Color.white); return; }
            UiKit.Disc(c, r, held[i] ? new Color(0.75f, 0.28f, 0.1f, 0.95f) : new Color(0.06f, 0.05f, 0.07f, 0.75f));
            UiKit.Ring(c, r, new Color(Gold.r, Gold.g, Gold.b, 0.9f));
            UiKit.Rect(new Rect(c.x - 6f, c.y - 7f, 4f, 14f), Color.white);
            UiKit.Rect(new Rect(c.x + 2f, c.y - 7f, 4f, 14f), Color.white);
        }
    }
}
