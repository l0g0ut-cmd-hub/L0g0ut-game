using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// ВРЕМЕННЫЙ ввод для проверки прототипа: клавиатура (старый Input Manager) + экранные кнопки + плавающий джойстик.
    /// Не является сенсорным управлением из раздела 6 ТЗ — оно делается на этапе 3 (жесты + Input System).
    ///
    /// Экранные кнопки читаются ПО КАСАНИЯМ напрямую (Input.touches), а IMGUI только рисует их:
    /// IMGUI обрабатывает лишь одно касание, из-за чего нельзя было одновременно ходить и бить.
    ///
    /// Ходьба — по всей площадке: джойстик (палец в левой нижней зоне, центр джойстика там, где коснулись) или WASD / стрелки.
    /// Направления считаются относительно камеры: «вверх» = от камеры вглубь сцены.
    /// Клавиши: WASD/стрелки — ход; Space — прыжок; J — сверху; K — снизу; N — по ногам; M — шар; I — укол; O — рез;
    /// L — удар врага; E — сменить тип удара врага; R — сброс; Esc — в меню; F1 — отладка.
    /// </summary>
    public class PrototypeInput : MonoBehaviour
    {
        public FighterController player;
        public FighterController enemy;

        enum B { Stick, Jump, Overhead, Rising, Legs, Energy, EnemyAttack, EnemyType, Reset, Menu, CamReset, Thrust, Slash }
        const int Count = 13;

        const float StickRadius = 84f;   // виртуальные единицы (высота экрана = 540)

        public static PrototypeInput Instance { get; private set; }
        void Awake() { Instance = this; }

        /// <summary>Попадает ли точка экрана (в пикселях) на экранную кнопку или зону джойстика — такие касания камера не берёт.</summary>
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

        // плавающий джойстик
        int stickFinger = -1;
        bool mouseStick;
        bool stickActive;
        Vector2 stickCenter = new Vector2(120f, 400f);
        Vector2 stickKnob = new Vector2(120f, 400f);
        Vector2 stickVec;       // x вправо, y вверх по экрану, длина 0…1
        Texture2D disc, ring;

        void Update()
        {
            Layout();
            Poll();

            Vector2 kb = Vector2.zero;
            bool jump = down[(int)B.Jump];
            bool aThrust = down[(int)B.Thrust], aSlash = down[(int)B.Slash];
            bool aOver = down[(int)B.Overhead], aRise = down[(int)B.Rising], aLegs = down[(int)B.Legs], aEnergy = down[(int)B.Energy];
            bool enemyAttack = down[(int)B.EnemyAttack], enemyCycle = down[(int)B.EnemyType];
            bool reset = down[(int)B.Reset], menu = down[(int)B.Menu], camReset = down[(int)B.CamReset];
            if (camReset && CameraOrbit.Instance != null) CameraOrbit.Instance.ResetView();

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kb.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kb.x -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) kb.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) kb.y -= 1f;
            jump |= Input.GetKeyDown(KeyCode.Space);
            aOver |= Input.GetKeyDown(KeyCode.J);
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
                player.SetMoveVector(ToWorld(Vector2.ClampMagnitude(stickVec + kb, 1f)));
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

        /// <summary>Экранный вектор (вправо/вверх) → мировой X/Z относительно взгляда камеры.</summary>
        static Vector2 ToWorld(Vector2 v)
        {
            if (v.sqrMagnitude < 1e-6f) return Vector2.zero;
            Transform cam = CameraOrbit.Instance != null ? CameraOrbit.Instance.transform : (Camera.main != null ? Camera.main.transform : null);
            Vector3 f = cam != null ? cam.forward : Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = cam != null ? cam.up : Vector3.forward;
            f.y = 0f;
            f.Normalize();
            Vector3 r = new Vector3(f.z, 0f, -f.x);   // вправо от взгляда камеры
            Vector3 w = r * v.x + f * v.y;
            return new Vector2(w.x, w.z);
        }

        // Раскладка в «виртуальных» единицах: высота экрана = 540, ширина по аспекту.
        void Layout()
        {
            scale = Mathf.Max(0.01f, Screen.height / 540f);
            float W = Screen.width / scale, H = 540f;

            rects[(int)B.Stick] = new Rect(0f, H - 330f, 360f, 330f);          // зона, где джойстик ловит палец
            rects[(int)B.Jump] = new Rect(W - 352f, H - 108f, 104f, 100f);

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

            if (!stickActive) { stickCenter = new Vector2(124f, H - 124f); stickKnob = stickCenter; }
        }

        void BeginStick(Vector2 p)
        {
            float H = 540f;
            // центр там, где коснулись, но так, чтобы круг помещался на экране
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
            Vector2 v = new Vector2(d.x, -d.y) / StickRadius;   // экран: y вниз → инвертируем
            float len = v.magnitude;
            const float dead = 0.14f;
            stickVec = len < dead ? Vector2.zero : v / len * Mathf.Clamp01((len - dead) / (1f - dead));
        }

        void EndStick()
        {
            stickActive = false;
            stickFinger = -1;
            mouseStick = false;
            stickVec = Vector2.zero;
        }

        void Poll()
        {
            Array.Clear(held, 0, Count);
            Array.Clear(down, 0, Count);
            pointersHeld.Clear();
            pointersDown.Clear();
#if ENABLE_LEGACY_INPUT_MANAGER
            int tc = Input.touchCount;
            bool stickSeen = false;
            for (int i = 0; i < tc; i++)
            {
                Touch t = Input.GetTouch(i);
                Vector2 p = new Vector2(t.position.x / scale, (Screen.height - t.position.y) / scale);
                bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;

                if (t.phase == TouchPhase.Began && stickFinger < 0 && rects[(int)B.Stick].Contains(p))
                {
                    stickFinger = t.fingerId;
                    BeginStick(p);
                }
                if (t.fingerId == stickFinger)
                {
                    if (ended) EndStick();
                    else { stickSeen = true; MoveStick(p); }
                    continue;   // палец джойстика кнопки не нажимает
                }
                if (ended) continue;
                pointersHeld.Add(p);
                if (t.phase == TouchPhase.Began) pointersDown.Add(p);
            }
            if (stickFinger >= 0 && !stickSeen) EndStick();
            if (tc == 0)   // мышь — только когда нет касаний (иначе Unity дублирует касание как мышь)
            {
                Vector2 m = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
                if (Input.GetMouseButtonDown(0) && rects[(int)B.Stick].Contains(m)) { mouseStick = true; BeginStick(m); }
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
            for (int b = 0; b < Count; b++)
            {
                for (int i = 0; i < pointersHeld.Count; i++) if (rects[b].Contains(pointersHeld[i])) { held[b] = true; break; }
                for (int i = 0; i < pointersDown.Count; i++) if (rects[b].Contains(pointersDown[i])) { down[b] = true; break; }
            }
        }

        static Texture2D MakeDisc(int n, bool ringOnly)
        {
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            float c = (n - 1) * 0.5f, R = n * 0.5f - 1.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(R - d + 0.5f);                       // сглаженный край
                    if (ringOnly) a = Mathf.Min(a, Mathf.Clamp01(d - (R - 4f) + 0.5f));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
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
            if (disc == null) { disc = MakeDisc(128, false); ring = MakeDisc(128, true); }

            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            Color oldColor = GUI.color;
            for (int b = 0; b < Count; b++)
            {
                if (b == (int)B.Stick) continue;
                GUI.color = held[b] ? new Color(1f, 0.55f, 0.3f, 0.95f) : new Color(1f, 1f, 1f, 0.72f);
                GUI.Box(rects[b], labels[b], style);
            }

            // джойстик: основание + кольцо + ручка
            float a = stickActive ? 1f : 0.55f;
            float R = StickRadius;
            GUI.color = new Color(0.05f, 0.06f, 0.1f, 0.55f * a);
            GUI.DrawTexture(new Rect(stickCenter.x - R, stickCenter.y - R, R * 2f, R * 2f), disc);
            GUI.color = new Color(1f, 1f, 1f, 0.55f * a);
            GUI.DrawTexture(new Rect(stickCenter.x - R, stickCenter.y - R, R * 2f, R * 2f), ring);
            float k = 34f;
            GUI.color = stickActive ? new Color(1f, 0.55f, 0.3f, 0.95f) : new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(stickKnob.x - k, stickKnob.y - k, k * 2f, k * 2f), disc);

            GUI.color = oldColor;
            GUI.matrix = old;
        }
    }
}
