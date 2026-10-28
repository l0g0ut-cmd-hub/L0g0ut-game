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
                 Resume, Restart, CamReset, AiToggle, DebugToggle, ToMenu, OverAgain, OverMenu, Block, Vibro, Difficulty, Super, Gfx }
        const int Count = 23;

        const float StickRadius = 84f;

        public static PrototypeInput Instance { get; private set; }
        void Awake() { Instance = this; }

        static readonly AttackType[] EnemyTypes = { AttackType.Overhead, AttackType.Rising, AttackType.LegSweep, AttackType.Thrust, AttackType.Slash, AttackType.Energy };

        readonly bool[] held = new bool[Count];
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
        float scale = 1f;

        int stickFinger = -1;
        bool mouseStick;
        bool stickActive;
        Vector2 stickCenter = new Vector2(120f, 400f);
        Vector2 stickKnob = new Vector2(120f, 400f);
        Vector2 stickVec;

        DuelManager duel;
        EnemyAI ai;
        CombatDebugVisualizer debugViz;

        void Start()
        {
            Haptics.Enabled = PlayerPrefs.GetInt("haptics", 1) == 1;
            duel = FindFirstObjectByType<DuelManager>();
            ai = FindFirstObjectByType<EnemyAI>();
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
            Layout();
            Poll();

            Vector2 kb = Vector2.zero;
            bool jump = down[(int)B.Jump], dash = down[(int)B.Dash];
            bool aThrust = down[(int)B.Thrust], aSlash = down[(int)B.Slash];
            bool aOver = down[(int)B.Overhead], aRise = down[(int)B.Rising], aLegs = down[(int)B.Legs], aEnergy = down[(int)B.Energy];
            bool enemyAttack = false, enemyCycle = false, toggleAi = down[(int)B.AiToggle];
            bool reset = down[(int)B.Restart] || down[(int)B.OverAgain];
            bool menu = down[(int)B.ToMenu] || down[(int)B.OverMenu];
            bool pauseBtn = down[(int)B.Pause] || down[(int)B.Resume];
            bool superBtn = down[(int)B.Super];
            if (down[(int)B.CamReset] && CameraOrbit.Instance != null) CameraOrbit.Instance.ResetView();
            if (down[(int)B.DebugToggle] && debugViz != null) debugViz.visible = !debugViz.visible;
            if (down[(int)B.Vibro]) { Haptics.Enabled = !Haptics.Enabled; PlayerPrefs.SetInt("haptics", Haptics.Enabled ? 1 : 0); Haptics.Pulse(40); }
            if (down[(int)B.Difficulty]) EnemyAI.Difficulty = (EnemyAI.Difficulty + 1) % 3;
            if (down[(int)B.Gfx]) DuelManager.Gfx = 1 - DuelManager.Gfx;

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
            aThrust |= Input.GetKeyDown(KeyCode.I);
            aSlash |= Input.GetKeyDown(KeyCode.O);
            enemyAttack |= Input.GetKeyDown(KeyCode.L);
            enemyCycle |= Input.GetKeyDown(KeyCode.E);
            toggleAi |= Input.GetKeyDown(KeyCode.T);
            reset |= Input.GetKeyDown(KeyCode.R);
            pauseBtn |= Input.GetKeyDown(KeyCode.Escape);
            if (Input.GetKeyDown(KeyCode.F1) && debugViz != null) { /* сам визуализатор переключает видимость по F1 */ }
#endif
            if (menu) { Time.timeScale = 1f; SceneManager.LoadScene(SceneNames.MainMenu); return; }
            if (reset)
            {
                if (duel != null) { if (Over && down[(int)B.OverAgain] && duel.PlayerWonMatch) duel.NextStage(); else duel.ResetMatch(); }
                else { if (player != null) player.ResetFighter(); if (enemy != null) enemy.ResetFighter(); }
                return;
            }
            if (toggleAi && ai != null) ai.aiEnabled = !ai.aiEnabled;
            if (pauseBtn && duel != null && !Over) duel.SetPaused(!duel.Paused);
            if (Paused || Over) return;

            if (player != null)
            {
                Vector2 mv = ToWorld(Vector2.ClampMagnitude(stickVec + kb, 1f));
                player.SetMoveVector(mv);
                if (jump) player.Jump();
                if (dash) player.Dash(mv);
                if (superBtn && duel != null) duel.TrySuper(player);
                bool blockHeld = held[(int)B.Block];
#if ENABLE_LEGACY_INPUT_MANAGER
                blockHeld |= Input.GetKey(KeyCode.B) || Input.GetKey(KeyCode.LeftControl);
#endif
                player.SetBlock(blockHeld);
                if (aOver) player.Attack(AttackType.Overhead);
                if (aRise) player.Attack(AttackType.Rising);
                if (aLegs) player.Attack(AttackType.LegSweep);
                if (aEnergy) player.Attack(AttackType.Energy);
                if (aThrust) player.Attack(AttackType.Thrust);
                if (aSlash && player.Attack(slashFlip ? AttackType.SlashBack : AttackType.Slash)) slashFlip = !slashFlip;
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
            // правый кластер: большая кнопка «сверху», вокруг неё остальные удары
            Circle(B.Overhead, W - 100f, H - 112f, 56f);
            Circle(B.Thrust, W - 108f, H - 236f, 42f);
            Circle(B.Slash, W - 212f, H - 190f, 42f);
            Circle(B.Rising, W - 216f, H - 84f, 40f);
            Circle(B.Legs, W - 316f, H - 124f, 38f);
            Circle(B.Energy, W - 322f, H - 226f, 36f);
            Circle(B.Jump, W - 214f, H - 296f, 34f);
            Circle(B.Dash, W - 412f, H - 66f, 32f);
            Circle(B.Block, W - 408f, H - 150f, 32f);
            Circle(B.Super, W - 92f, H - 330f, 40f);
            Circle(B.Pause, W * 0.5f, 88f, 18f);   // под таймером раунда

            float cx = W * 0.5f;
            Box(B.Resume, cx - 150f, 116f, 300f, 38f);
            Box(B.Restart, cx - 150f, 160f, 300f, 38f);
            Box(B.Difficulty, cx - 150f, 204f, 300f, 38f);
            Box(B.Gfx, cx - 150f, 248f, 300f, 38f);
            Box(B.Vibro, cx - 150f, 292f, 300f, 38f);
            Box(B.CamReset, cx - 150f, 336f, 300f, 38f);
            Box(B.AiToggle, cx - 150f, 380f, 300f, 38f);
            Box(B.DebugToggle, cx - 150f, 424f, 300f, 38f);
            Box(B.ToMenu, cx - 150f, 468f, 300f, 38f);
            Box(B.OverAgain, cx - 210f, 300f, 200f, 56f);
            Box(B.OverMenu, cx + 10f, 300f, 200f, 56f);

            for (int i = 0; i < Count; i++) active[i] = false;
            if (play)
                foreach (B b in new[] { B.Stick, B.Jump, B.Dash, B.Block, B.Overhead, B.Rising, B.Legs, B.Energy, B.Thrust, B.Slash, B.Pause }) active[(int)b] = true;
            if (play && player != null && player.KiFull) active[(int)B.Super] = true;
            if (paused)
                foreach (B b in new[] { B.Resume, B.Restart, B.Difficulty, B.Gfx, B.Vibro, B.CamReset, B.AiToggle, B.DebugToggle, B.ToMenu }) active[(int)b] = true;
            if (over) { active[(int)B.OverAgain] = true; active[(int)B.OverMenu] = true; }

            labels[(int)B.Resume] = "ПРОДОЛЖИТЬ";
            labels[(int)B.Restart] = "ЗАНОВО";
            labels[(int)B.CamReset] = "КАМЕРА: СБРОС";
            labels[(int)B.AiToggle] = "ВРАГ: " + (ai == null || ai.aiEnabled ? "ИИ ВКЛ" : "СТОИТ");
            labels[(int)B.DebugToggle] = "ОТЛАДКА: " + (debugViz != null && debugViz.visible ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.ToMenu] = "В МЕНЮ";
            labels[(int)B.Gfx] = "ГРАФИКА: " + (DuelManager.Gfx == 1 ? "ВЫСОКАЯ" : "ЭКОНОМ");
            labels[(int)B.Vibro] = "ВИБРАЦИЯ: " + (Haptics.Enabled ? "ВКЛ" : "ВЫКЛ");
            labels[(int)B.Difficulty] = "СЛОЖНОСТЬ: " + EnemyAI.DifficultyName;
            labels[(int)B.OverAgain] = duel != null && duel.PlayerWonMatch ? (Ladder.IsLast ? "С НАЧАЛА" : "ДАЛЕЕ") : "ЕЩЁ РАЗ";
            labels[(int)B.OverMenu] = "В МЕНЮ";

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
                for (int i = 0; i < pointersHeld.Count; i++) if (Hit(b, pointersHeld[i], 0f)) { held[b] = true; break; }
                for (int i = 0; i < pointersDown.Count; i++) if (Hit(b, pointersDown[i], 0f)) { down[b] = true; break; }
            }
        }

        bool OnAnyButton(Vector2 p)
        {
            for (int b = 1; b < Count; b++) if (active[b] && Hit(b, p, 0f)) return true;
            return false;
        }

        // ------------------------------------------------------------------ отрисовка

        static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f, 1f);

        void OnGUI()
        {
            Matrix4x4 old = UiKit.Begin();
            float W = UiKit.Width;
            bool paused = Paused, over = Over;

            if (active[(int)B.Stick]) DrawStick();

            bool canAct = player != null && player.CanAct && player.weapon != null && player.weapon.Phase == SwingPhase.Idle;
            bool air = player != null && !player.Grounded;
            DrawAttack(B.Overhead, Icon.Down, air ? "В ПРЫЖКЕ" : "СВЕРХУ", canAct);
            DrawAttack(B.Thrust, Icon.Thrust, air ? "" : "УКОЛ", canAct && !air);
            DrawAttack(B.Slash, slashFlip ? Icon.SlashBack : Icon.Slash, air ? "" : "РЕЗ", canAct && !air);
            DrawAttack(B.Rising, Icon.Up, air ? "В ПРЫЖКЕ" : "СНИЗУ", canAct);
            DrawAttack(B.Legs, Icon.Low, air ? "" : "ПО НОГАМ", canAct && !air);
            DrawAttack(B.Energy, Icon.Orb, "ШАР", canAct && player.CanEnergy);
            DrawKiRing(B.Energy, player != null ? Mathf.Clamp01(player.Ki / Mathf.Max(1f, player.energyCost)) : 0f);
            DrawAttack(B.Jump, Icon.Jump, "ПРЫЖОК", player != null && player.CanAct && player.Grounded);
            DrawAttack(B.Block, Icon.Shield, "БЛОК", player != null && player.CanAct && !player.GuardBroken && player.weapon != null && player.weapon.Phase == SwingPhase.Idle);
            DrawAttack(B.Dash, Icon.Dash, "РЫВОК", player != null && player.CanAct && player.DashReady01 >= 1f);
            // запас блока: точки вокруг щита тают, пока держишь блок; красные — блок выбит
            if (player != null && (player.Blocking || player.GuardBroken))
                DrawDotRing(B.Block, player.GuardBroken ? 1f : 1f - player.BlockTime / 2.5f, player.GuardBroken ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(0.9f, 0.85f, 0.6f, 1f));
            DrawSuper();
            DrawPause();

            if (paused)
            {
                UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.65f));
                UiKit.Text(new Rect(W * 0.5f - 200f, 62f, 400f, 50f), "ПАУЗА", 40f, Gold);
                for (int b = (int)B.Resume; b <= (int)B.ToMenu; b++) DrawRectButton(b);
                DrawRectButton((int)B.Vibro); DrawRectButton((int)B.Difficulty); DrawRectButton((int)B.Gfx);
                UiKit.Text(new Rect(W * 0.5f - 400f, 510f, 800f, 24f),
                    "ПУТЬ " + (Ladder.Index + 1) + "/" + Ladder.Count + "  ·  ПОБЕД " + Records.Wins + "  ·  ПОРАЖЕНИЙ " + Records.Losses + "  ·  ЛУЧШЕЕ " + Records.BestStage + "/" + Ladder.Count + (Records.Cleared > 0 ? "  ·  ПРОЙДЕН ×" + Records.Cleared : ""),
                    13f, new Color(0.85f, 0.8f, 0.7f, 0.9f));
            }
            if (over) { DrawRectButton((int)B.OverAgain); DrawRectButton((int)B.OverMenu); }

            GUI.matrix = old;
        }

        void DrawRectButton(int b)
        {
            Rect r = rects[b];
            bool h = held[b];
            UiKit.Rect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Gold * 0.8f);
            UiKit.Rect(r, h ? new Color(0.55f, 0.2f, 0.08f, 1f) : new Color(0.09f, 0.07f, 0.07f, 0.96f));
            UiKit.Text(r, labels[b], 22f, h ? Color.white : new Color(0.95f, 0.9f, 0.78f));
        }

        void DrawStick()
        {
            float a = stickActive ? 1f : 0.55f;
            float R = StickRadius;
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

        void DrawAttack(B b, Icon icon, string name, bool ready)
        {
            int i = (int)b;
            if (!active[i]) return;
            Vector2 c = ctr[i]; float r = rad[i];
            bool h = held[i];
            float a = ready ? 1f : 0.45f;
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
            UiKit.Disc(c, r + 8f + pulse * 6f, new Color(0.4f, 0.8f, 1f, 0.18f + 0.15f * pulse));
            UiKit.Disc(c, r, held[i] ? new Color(0.9f, 0.95f, 1f, 1f) : new Color(0.1f, 0.35f, 0.6f, 0.92f));
            UiKit.Ring(c, r, new Color(1f, 0.9f, 0.55f, 1f));
            UiKit.Ring(c, r * 0.8f, new Color(1f, 1f, 1f, 0.35f), true);
            // молния-разрез
            Color w = Color.white;
            UiKit.Line(c + new Vector2(-r * 0.45f, r * 0.4f), c + new Vector2(r * 0.05f, -r * 0.05f), 4f, w);
            UiKit.Line(c + new Vector2(-r * 0.1f, r * 0.05f), c + new Vector2(r * 0.45f, -r * 0.45f), 4f, w);
            UiKit.Text(new Rect(c.x - r, c.y + r * 0.3f, r * 2f, r * 0.5f), "ДУХ!", 12f, new Color(1f, 0.95f, 0.8f), TextAnchor.MiddleCenter, false);
        }

        void DrawPause()
        {
            int i = (int)B.Pause;
            if (!active[i]) return;
            Vector2 c = ctr[i]; float r = rad[i];
            UiKit.Disc(c, r, held[i] ? new Color(0.75f, 0.28f, 0.1f, 0.95f) : new Color(0.06f, 0.05f, 0.07f, 0.75f));
            UiKit.Ring(c, r, new Color(Gold.r, Gold.g, Gold.b, 0.9f));
            UiKit.Rect(new Rect(c.x - 6f, c.y - 7f, 4f, 14f), Color.white);
            UiKit.Rect(new Rect(c.x + 2f, c.y - 7f, 4f, 14f), Color.white);
        }
    }
}
