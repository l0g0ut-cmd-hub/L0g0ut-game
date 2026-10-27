using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// ВРЕМЕННЫЙ ввод для проверки прототипа: клавиатура (старый Input Manager) + экранные кнопки.
    /// Не является сенсорным управлением из раздела 6 ТЗ — оно делается на этапе 3 (жесты + Input System).
    ///
    /// Экранные кнопки читаются ПО КАСАНИЯМ напрямую (Input.touches), а IMGUI только рисует их:
    /// IMGUI обрабатывает лишь одно касание, из-за чего нельзя было одновременно держать «вперёд» и бить.
    ///
    /// Клавиши: A/D, ←/→ — ход; W/↑ — прыжок; J — сверху; K — снизу; N — по ногам; M — шар;
    /// L — удар врага; E — сменить тип удара врага; R — сброс; Esc — в меню; F1 — отладка.
    /// </summary>
    public class PrototypeInput : MonoBehaviour
    {
        public FighterController player;
        public FighterController enemy;

        enum B { Left, Right, Jump, Overhead, Rising, Legs, Energy, EnemyAttack, EnemyType, Reset, Menu, CamReset, Thrust, Slash }
        const int Count = 14;

        public static PrototypeInput Instance { get; private set; }
        void Awake() { Instance = this; }

        /// <summary>Попадает ли точка экрана (в пикселях) на экранную кнопку — такие касания камера не берёт.</summary>
        public bool IsOverButton(Vector2 screenPx)
        {
            float sc = Mathf.Max(0.01f, Screen.height / 540f);
            Vector2 p = new Vector2(screenPx.x / sc, (Screen.height - screenPx.y) / sc);
            for (int b = 0; b < Count; b++)
            {
                Rect r = rects[b];
                r.xMin -= 8f; r.yMin -= 8f; r.xMax += 8f; r.yMax += 8f;   // небольшой запас на палец
                if (r.Contains(p)) return true;
            }
            return false;
        }

        static readonly AttackType[] EnemyTypes = new AttackType[] { AttackType.Overhead, AttackType.Rising, AttackType.LegSweep, AttackType.Thrust, AttackType.Slash, AttackType.Energy };

        readonly bool[] held = new bool[Count];
        readonly bool[] down = new bool[Count];
        readonly Rect[] rects = new Rect[Count];
        readonly string[] labels = new string[Count];
        readonly List<Vector2> pointersHeld = new List<Vector2>();
        readonly List<Vector2> pointersDown = new List<Vector2>();
        int enemyTypeIndex;
        bool slashFlip;   // чередование направления горизонтального реза
        float scale = 1f;
        GUIStyle style;

        void Update()
        {
            Layout();
            Poll();

            float move = 0f;
            if (held[(int)B.Right]) move += 1f;
            if (held[(int)B.Left]) move -= 1f;
            bool jump = down[(int)B.Jump];
            bool aThrust = down[(int)B.Thrust], aSlash = down[(int)B.Slash];
            bool aOver = down[(int)B.Overhead], aRise = down[(int)B.Rising], aLegs = down[(int)B.Legs], aEnergy = down[(int)B.Energy];
            bool enemyAttack = down[(int)B.EnemyAttack], enemyCycle = down[(int)B.EnemyType];
            bool reset = down[(int)B.Reset], menu = down[(int)B.Menu], camReset = down[(int)B.CamReset];
            if (camReset && CameraOrbit.Instance != null) CameraOrbit.Instance.ResetView();

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= 1f;
            jump |= Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            aOver |= Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Space);
            aRise |= Input.GetKeyDown(KeyCode.K);
            aLegs |= Input.GetKeyDown(KeyCode.N);
            aEnergy |= Input.GetKeyDown(KeyCode.M);
            aThrust |= Input.GetKeyDown(KeyCode.I);
            aSlash |= Input.GetKeyDown(KeyCode.O);
            enemyAttack |= Input.GetKeyDown(KeyCode.L);
            enemyCycle |= Input.GetKeyDown(KeyCode.E);
            reset |= Input.GetKeyDown(KeyCode.R);
            menu |= Input.GetKeyDown(KeyCode.Escape);
#endif
            if (menu) { SceneManager.LoadScene(SceneNames.MainMenu); return; }

            if (player != null)
            {
                player.SetMoveInput(move);
                if (jump) player.Jump();
                if (aOver) player.Attack(AttackType.Overhead);
                if (aRise) player.Attack(AttackType.Rising);
                if (aLegs) player.Attack(AttackType.LegSweep);
                if (aEnergy) player.Attack(AttackType.Energy);
                if (aThrust) player.Attack(AttackType.Thrust);
                if (aSlash && player.Attack(slashFlip ? AttackType.SlashBack : AttackType.Slash)) slashFlip = !slashFlip;
            }
            if (enemyCycle) enemyTypeIndex = (enemyTypeIndex + 1) % EnemyTypes.Length;
            if (enemy != null && enemyAttack) enemy.Attack(EnemyTypes[enemyTypeIndex]);
            if (reset)
            {
                if (player != null) player.ResetFighter();
                if (enemy != null) enemy.ResetFighter();
            }
        }

        // Раскладка в «виртуальных» единицах: высота экрана = 540, ширина по аспекту.
        void Layout()
        {
            scale = Mathf.Max(0.01f, Screen.height / 540f);
            float W = Screen.width / scale, H = 540f;

            rects[(int)B.Left] = new Rect(16f, H - 116f, 100f, 100f);
            rects[(int)B.Right] = new Rect(124f, H - 116f, 100f, 100f);
            rects[(int)B.Jump] = new Rect(70f, H - 224f, 100f, 100f);

            rects[(int)B.Overhead] = new Rect(W - 236f, H - 200f, 108f, 92f);
            rects[(int)B.Rising] = new Rect(W - 120f, H - 200f, 108f, 92f);
            rects[(int)B.Legs] = new Rect(W - 236f, H - 100f, 108f, 92f);
            rects[(int)B.Energy] = new Rect(W - 120f, H - 100f, 108f, 92f);

            rects[(int)B.Thrust] = new Rect(W - 236f, H - 300f, 108f, 92f);
            rects[(int)B.Slash] = new Rect(W - 120f, H - 300f, 108f, 92f);

            rects[(int)B.Reset] = new Rect(W - 88f, 8f, 80f, 34f);
            rects[(int)B.Menu] = new Rect(W - 176f, 8f, 80f, 34f);
            rects[(int)B.EnemyType] = new Rect(W - 188f, 48f, 180f, 34f);
            rects[(int)B.EnemyAttack] = new Rect(W - 188f, 88f, 180f, 34f);
            rects[(int)B.CamReset] = new Rect(W - 188f, 128f, 180f, 34f);

            bool air = player != null && !player.Grounded;
            labels[(int)B.Left] = "< назад";
            labels[(int)B.Right] = "вперёд >";
            labels[(int)B.Jump] = "ПРЫЖОК";
            labels[(int)B.Overhead] = air ? "В ПРЫЖКЕ" : "СВЕРХУ";
            labels[(int)B.Rising] = air ? "В ПРЫЖКЕ" : "СНИЗУ";
            labels[(int)B.Legs] = air ? "(по ногам\nс земли)" : "ПО НОГАМ";
            labels[(int)B.Energy] = "ШАР";
            labels[(int)B.Thrust] = air ? "(укол\nс земли)" : "УКОЛ";
            labels[(int)B.Slash] = air ? "(рез\nс земли)" : (slashFlip ? "РЕЗ <" : "РЕЗ >");
            labels[(int)B.Reset] = "Сброс";
            labels[(int)B.Menu] = "Меню";
            labels[(int)B.EnemyType] = "Враг: " + SwingProfile.Get(EnemyTypes[enemyTypeIndex]).label + "  ▸";
            labels[(int)B.EnemyAttack] = "ВРАГ БЬЁТ";
            labels[(int)B.CamReset] = "Камера: сброс";
        }

        void Poll()
        {
            Array.Clear(held, 0, Count);
            Array.Clear(down, 0, Count);
            pointersHeld.Clear();
            pointersDown.Clear();
#if ENABLE_LEGACY_INPUT_MANAGER
            int tc = Input.touchCount;
            for (int i = 0; i < tc; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                Vector2 p = new Vector2(t.position.x / scale, (Screen.height - t.position.y) / scale);
                pointersHeld.Add(p);
                if (t.phase == TouchPhase.Began) pointersDown.Add(p);
            }
            if (tc == 0)   // мышь — только когда нет касаний (иначе Unity дублирует касание как мышь)
            {
                Vector2 m = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
                if (Input.GetMouseButton(0)) pointersHeld.Add(m);
                if (Input.GetMouseButtonDown(0)) pointersDown.Add(m);
            }
#endif
            for (int b = 0; b < Count; b++)
            {
                for (int i = 0; i < pointersHeld.Count; i++) if (rects[b].Contains(pointersHeld[i])) { held[b] = true; break; }
                for (int i = 0; i < pointersDown.Count; i++) if (rects[b].Contains(pointersDown[i])) { down[b] = true; break; }
            }
        }

        // IMGUI только рисует кнопки; нажатия считает Poll().
        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontSize = 14;
                style.wordWrap = true;
                style.normal.textColor = Color.white;
            }
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            Color oldColor = GUI.color;
            for (int b = 0; b < Count; b++)
            {
                GUI.color = held[b] ? new Color(1f, 0.55f, 0.3f, 0.95f) : new Color(1f, 1f, 1f, 0.72f);
                GUI.Box(rects[b], labels[b], style);
            }
            GUI.color = oldColor;
            GUI.matrix = old;
        }
    }
}
