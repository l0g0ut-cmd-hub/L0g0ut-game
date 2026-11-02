using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Облёт камеры вокруг дуэлянтов.
    ///  • Один палец по свободной части экрана — вращение (влево-вправо — вокруг, вверх-вниз — высота камеры).
    ///  • Два пальца (щипок) — приближение/отдаление.
    ///  • Двойной тап по свободному месту или кнопка «Камера: сброс» — вернуть стандартный вид сбоку.
    /// Касания, начатые на экранных кнопках, камеру не двигают (можно ходить и бить, не задевая её).
    /// ПК/редактор: левая или правая кнопка мыши — вращение, колесо — зум.
    /// Камера только смотрит: на бой и попадания не влияет; джойстик ходьбы считает направления относительно неё.
    /// Режим «свободная камера» (настройки): шире пределы наклона и зума, два пальца ещё и двигают точку обзора, нет
    /// автоматических наездов (вход в раунд, добивание, супер, облёт после KO, автоотъезд) и ограды двором — можно
    /// рассмотреть храм, сад и задний двор. Сброс — двойной тап или кнопка в паузе.
    /// </summary>
    public class CameraOrbit : MonoBehaviour
    {
        public static CameraOrbit Instance { get; private set; }
        /// <summary>Свободная камера (настройка, хранится в PlayerPrefs «freecam»).</summary>
        public static bool FreeMode
        {
            get { if (freeCached < 0) freeCached = PlayerPrefs.GetInt("freecam", 0); return freeCached == 1; }   // кэш: не дёргать PlayerPrefs каждый кадр
            set { freeCached = value ? 1 : 0; PlayerPrefs.SetInt("freecam", freeCached); PlayerPrefs.Save(); if (Instance != null) Instance.ResetView(); }
        }
        static int freeCached = -1;
        Vector3 pan, panT, panV;
        /// <summary>Точка, на которую смотрит камера (для пряток предметов, заслоняющих бойцов).</summary>
        public Vector3 Focus { get; private set; } = new Vector3(0f, 1.1f, 0f);   // смещение точки обзора (только в свободном режиме)

        public Vector3 targetOffset = new Vector3(0f, 1.15f, 0f);   // точка, на которую смотрим (над центром арены)
        public float defaultDistance = 5.2f;
        public float minDistance = 2.4f, maxDistance = 8.5f;
        public float minPitch = -6f, maxPitch = 55f;
        [Tooltip("Градусов поворота на высоту экрана.")]
        public float yawPerScreen = 180f, pitchPerScreen = 90f;
        public float smoothTime = 0.08f;
        [Range(0f, 1f)] public float followMidpoint = 0.5f;          // насколько камера следует за серединой между бойцами (X и Z)

        float shakeAmp;
        float koK, cineYaw;   // кинематографичный облёт после KO
        bool introPlaying;
        float superK, autoD, autoV;
        float dbK, dbYaw; Vector3 dbFocus;   // добивание: камера ближе, фокус на груди противника
        public void Shake(float amount) { shakeAmp = Mathf.Max(shakeAmp, amount); }
        /// <summary>Резкий «наезд» объективом на тяжёлом ударе (FOV уменьшается и возвращается).</summary>
        public void Punch(float degrees) { fovPunch = Mathf.Max(fovPunch, degrees); }
        float fovPunch, baseFov = -1f;
        Camera cam;

        float yaw, pitch, dist;
        float yawT, pitchT, distT, yawV, pitchV, distV;
        float midX, midZ, midVx, midVz;
        FighterController[] fighters;
        readonly HashSet<int> camTouches = new HashSet<int>();
        readonly List<Touch> touchBuffer = new List<Touch>(2);
        float lastTapTime = -10f;
        Vector2 lastTapPos;
        Vector2 lastMouse;

        void Awake()
        {
            useGUILayout = false;   // GUILayout нигде не используется: без лишнего прохода Layout IMGUI вдвое дешевле
            Breadcrumbs.Mark("камера: Awake");
            Instance = this;
            ResetView(true);
        }

        void Start()
        {
            fighters = FindObjectsByType<FighterController>(FindObjectsSortMode.None);
        }

        public void ResetView(bool instant = false)
        {
            yawT = 0f; pitchT = 0f; distT = defaultDistance; panT = Vector3.zero;
            if (instant) { yaw = yawT; pitch = pitchT; dist = distT; pan = panT; }
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            ReadTouches();
            ReadMouse();
#endif
            bool free = FreeMode;
            pitchT = Mathf.Clamp(pitchT, free ? -10f : minPitch, free ? 85f : maxPitch);
            distT = Mathf.Clamp(distT, free ? 1.2f : minDistance, free ? 18f : maxDistance);
            if (!free) panT = Vector3.zero;
            panT.x = Mathf.Clamp(panT.x, -12f, 12f); panT.z = Mathf.Clamp(panT.z, -12f, 14f); panT.y = 0f;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        void ReadTouches()
        {
            int tc = Input.touchCount;
            List<Touch> mine = touchBuffer;
            mine.Clear();
            for (int i = 0; i < tc; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    bool onButton = PrototypeInput.Instance != null && PrototypeInput.Instance.IsOverButton(t.position);
                    diagTouch = string.Format("камера: касание ({0:F0},{1:F0}) из {2}x{3} на кнопке={4}", t.position.x, t.position.y, Screen.width, Screen.height, onButton);
                    if (!onButton)
                    {
                        camTouches.Add(t.fingerId);
                        if (Time.unscaledTime - lastTapTime < 0.3f && (t.position - lastTapPos).magnitude < 80f) { ResetView(); lastTapTime = -10f; }
                        else { lastTapTime = Time.unscaledTime; lastTapPos = t.position; }
                    }
                }
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { camTouches.Remove(t.fingerId); continue; }
                if (camTouches.Contains(t.fingerId))
                {
                    mine.Add(t);
                }
            }
            if (mine.Count == 0) return;

            float h = Mathf.Max(1f, Screen.height);
            if (mine.Count == 1)
            {
                Vector2 d = mine[0].deltaPosition;
                yawT -= d.x / h * yawPerScreen;
                pitchT -= d.y / h * pitchPerScreen;
            }
            else
            {
                Touch a = mine[0], b = mine[1];
                float prev = ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude;
                float cur = (a.position - b.position).magnitude;
                distT -= (cur - prev) / h * (maxDistance - minDistance) * 1.2f;
                if (FreeMode) PanBy((a.deltaPosition + b.deltaPosition) * 0.5f, h);   // два пальца вместе — сдвиг точки обзора
            }
        }

        void ReadMouse()
        {
            if (Input.touchCount > 0) return;
            Vector2 m = Input.mousePosition;
            bool press = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            if (press) lastMouse = m;
            bool onButton = PrototypeInput.Instance != null && PrototypeInput.Instance.IsOverButton(m);
            if ((Input.GetMouseButton(1) || (Input.GetMouseButton(0) && !onButton)) && !press)
            {
                Vector2 d = m - lastMouse;
                float h = Mathf.Max(1f, Screen.height);
                yawT -= d.x / h * yawPerScreen;
                pitchT -= d.y / h * pitchPerScreen;
            }
            if (Input.GetMouseButton(2) && FreeMode && !press) PanBy(m - lastMouse, Mathf.Max(1f, Screen.height));   // средняя кнопка — сдвиг
            lastMouse = m;
            distT -= Input.mouseScrollDelta.y * 0.4f;
        }

        /// <summary>Сдвиг точки обзора по земле вслед за пальцами (в плоскости экрана, без подъёма).</summary>
        void PanBy(Vector2 d, float h)
        {
            Vector3 right = transform.right; right.y = 0f; right.Normalize();
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.up; fwd.y = 0f; fwd.Normalize();
            panT -= (right * d.x + fwd * d.y) / h * Mathf.Max(2f, dist) * 1.1f;
        }
#endif

        string diagTouch = "";
        void OnGUI()
        {
            if (!HeroRigDriver.Diag) return;
            GUIStyle g = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 45) };
            string t = diagTouch + string.Format(" | yaw {0:F0} pitch {1:F0} | пальцев камеры {2}", yaw, pitch, camTouches.Count);
            g.normal.textColor = Color.black; GUI.Label(new Rect(12, Screen.height * 0.36f + 2, Screen.width, 40), t, g);
            g.normal.textColor = Color.cyan; GUI.Label(new Rect(10, Screen.height * 0.36f, Screen.width, 40), t, g);
        }

        void LateUpdate()
        {
            bool free = FreeMode;
            yaw = Mathf.SmoothDamp(yaw, yawT, ref yawV, smoothTime);
            pan = Vector3.SmoothDamp(pan, panT, ref panV, smoothTime * 1.5f, Mathf.Infinity, Time.unscaledDeltaTime);
            pitch = Mathf.SmoothDamp(pitch, pitchT, ref pitchV, smoothTime);
            dist = Mathf.SmoothDamp(dist, distT, ref distV, smoothTime);

            float targetX = 0f, targetZ = 0f;
            if (fighters != null && fighters.Length > 0)
            {
                float sx = 0f, sz = 0f; int n = 0;
                for (int i = 0; i < fighters.Length; i++) if (fighters[i] != null) { sx += fighters[i].transform.position.x; sz += fighters[i].transform.position.z; n++; }
                if (n > 0) { targetX = sx / n * followMidpoint; targetZ = sz / n * followMidpoint; }
            }
            midX = Mathf.SmoothDamp(midX, targetX, ref midVx, 0.25f);
            midZ = Mathf.SmoothDamp(midZ, targetZ, ref midVz, 0.25f);

            // KO: камера медленно облетает бойцов и подходит ближе
            DuelManager dm = DuelManager.Instance;
            bool ko = !free && dm != null && (dm.State == DuelState.PlayerWon || dm.State == DuelState.PlayerLost || dm.ResurrectOffered);
            float udt = Time.unscaledDeltaTime;
            koK = Mathf.MoveTowards(koK, ko ? 1f : 0f, udt / (ko ? 0.9f : 0.5f));
            if (ko) cineYaw += 14f * udt * koK;
            else cineYaw = Mathf.Lerp(cineYaw, 0f, 1f - Mathf.Exp(-5f * udt));
            float ck = koK * koK * (3f - 2f * koK);
            // начало раунда: камера «влетает» сверху-сбоку к обычному виду
            float ik = 0f;
            if (!free && dm != null && dm.State == DuelState.Intro)
            {
                float u = Mathf.Clamp01(dm.StateTime / Mathf.Max(0.1f, dm.IntroLength * 0.9f));
                ik = 1f - u * u * (3f - 2f * u);
                if (!introPlaying) { cineYaw = 0f; introPlaying = true; }
            }
            else introPlaying = false;

            Vector3 focus = new Vector3(midX, 0f, midZ) + targetOffset - Vector3.up * 0.25f * ck + pan;

            // автокадрирование: бойцы разошлись — камера отъезжает, чтобы оба были в кадре
            float sep = 0f;
            if (fighters != null && fighters.Length >= 2 && fighters[0] != null && fighters[1] != null)
            {
                Vector3 dd = fighters[0].transform.position - fighters[1].transform.position; dd.y = 0f;
                sep = dd.magnitude;
            }
            float want = free ? 0f : Mathf.Max(0f, 2.4f + sep * 1.35f - dist);
            autoD = Mathf.SmoothDamp(autoD, want, ref autoV, 0.35f, Mathf.Infinity, udt);
            // супер-удар: наезд на атакующего
            float sk = 0f;
            if (!free && dm != null && dm.SuperActive && dm.SuperTime < DuelManager.SuperWindup)
            {
                sk = Mathf.Clamp01(dm.SuperTime / 0.25f);
                Vector3 sp = dm.SuperBy.transform.position;
                focus = Vector3.Lerp(focus, new Vector3(sp.x, 0f, sp.z) + targetOffset, sk * 0.8f);
            }
            superK = Mathf.MoveTowards(superK, sk, udt / 0.15f);
            // добивание: наезд на пару, камера чуть заходит со стороны жертвы — видно лицо атакующего и клинок, вышедший из спины
            bool db = !free && dm != null && dm.DeathblowActive && dm.DeathblowTarget != null;
            if (db)
            {
                Vector3 a = dm.DeathblowBy.transform.position, t = dm.DeathblowTarget.transform.position;
                Vector3 c = Vector3.Lerp(a, t, 0.55f);
                dbFocus = new Vector3(c.x, 1.1f + Mathf.Max(0f, a.y) * 0.55f, c.z);   // с прыжка — кадр поднимается за атакующим в воздухе
                // ракурс свой у каждого приёма и медленный облёт во время добивания
                dbYaw = (-dm.DeathblowCamYaw - 5f * Mathf.Min(dm.DeathblowAge, 2f)) * Mathf.Sign(t.x - a.x + 1e-4f);
            }
            dbK = Mathf.MoveTowards(dbK, db ? 1f : 0f, udt / (db ? 0.3f : 0.9f));
            float dbs = dbK * dbK * (3f - 2f * dbK);
            if (dbs > 0f) focus = Vector3.Lerp(focus, dbFocus, dbs);
            Quaternion rot = Quaternion.Euler(pitch + 4f * ck + 14f * ik - 4f * superK - 3f * dbs, yaw + cineYaw - 40f * ik + 22f * superK + dbYaw * dbs, 0f);
            Vector3 pos = focus + rot * new Vector3(0f, 0f, -(dist + autoD) * (1f - 0.3f * ck + 0.5f * ik - 0.45f * superK) * (1f - 0.33f * dbs));
            if (pos.y < 0.15f) pos.y = 0.15f;
            // лёгкое «дыхание» камеры с рук
            float tt = Time.unscaledTime;
            pos += new Vector3(Mathf.PerlinNoise(tt * 0.35f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, tt * 0.3f) - 0.5f, 0f) * 0.03f;
            // камера не заходит в стены храма, ворота и сады за пределами арены
            if (free)
            {
                // свободная: только внутри мира (лес вокруг двора), не выше 14 м
                pos.x = Mathf.Clamp(pos.x, -18f, 18f);
                pos.z = Mathf.Clamp(pos.z, -16f, 18f);
                pos.y = Mathf.Min(pos.y, 14f);
            }
            else
            {
                pos.x = Mathf.Clamp(pos.x, -5.7f, 5.7f);
                pos.z = Mathf.Clamp(pos.z, -6.5f, 3.2f);
                pos.y = Mathf.Min(pos.y, 6f);
            }
            if (shakeAmp > 0.0005f)
            {
                // Smooth camera impulse: noisy handheld impact rather than
                // frame-to-frame random jumps that feel harsher at low FPS.
                float shakeTime = Time.unscaledTime * 31f;
                Vector3 impulse = new Vector3(
                    Mathf.PerlinNoise(shakeTime, 17.3f) - 0.5f,
                    Mathf.PerlinNoise(9.1f, shakeTime) - 0.5f,
                    Mathf.PerlinNoise(shakeTime, 41.2f) - 0.5f) * (2f * shakeAmp);
                pos += impulse;
                shakeAmp *= Mathf.Exp(-14f * Time.unscaledDeltaTime);
            }
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(focus - pos, Vector3.up);
            Focus = focus;
            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null)
            {
                if (baseFov < 0f) baseFov = cam.fieldOfView;
                fovPunch = Mathf.MoveTowards(fovPunch, 0f, udt * 18f);
                cam.fieldOfView = baseFov - fovPunch - 3f * superK;
            }
        }
    }
}
