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
    /// </summary>
    public class CameraOrbit : MonoBehaviour
    {
        public static CameraOrbit Instance { get; private set; }

        public Vector3 targetOffset = new Vector3(0f, 1.15f, 0f);   // точка, на которую смотрим (над центром арены)
        public float defaultDistance = 5.2f;
        public float minDistance = 2.4f, maxDistance = 8.5f;
        public float minPitch = -6f, maxPitch = 55f;
        [Tooltip("Градусов поворота на высоту экрана.")]
        public float yawPerScreen = 180f, pitchPerScreen = 90f;
        public float smoothTime = 0.08f;
        [Range(0f, 1f)] public float followMidpoint = 0.5f;          // насколько камера следует за серединой между бойцами (X и Z)

        float shakeAmp;
        public void Shake(float amount) { shakeAmp = Mathf.Max(shakeAmp, amount); }

        float yaw, pitch, dist;
        float yawT, pitchT, distT, yawV, pitchV, distV;
        float midX, midZ, midVx, midVz;
        FighterController[] fighters;
        readonly HashSet<int> camTouches = new HashSet<int>();
        float lastTapTime = -10f;
        Vector2 lastTapPos;
        Vector2 lastMouse;

        void Awake()
        {
            Instance = this;
            ResetView(true);
        }

        void Start()
        {
            fighters = FindObjectsByType<FighterController>(FindObjectsSortMode.None);
        }

        public void ResetView(bool instant = false)
        {
            yawT = 0f; pitchT = 0f; distT = defaultDistance;
            if (instant) { yaw = yawT; pitch = pitchT; dist = distT; }
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            ReadTouches();
            ReadMouse();
#endif
            pitchT = Mathf.Clamp(pitchT, minPitch, maxPitch);
            distT = Mathf.Clamp(distT, minDistance, maxDistance);
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        void ReadTouches()
        {
            int tc = Input.touchCount;
            List<Touch> mine = null;
            for (int i = 0; i < tc; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    bool onButton = PrototypeInput.Instance != null && PrototypeInput.Instance.IsOverButton(t.position);
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
                    if (mine == null) mine = new List<Touch>(2);
                    mine.Add(t);
                }
            }
            if (mine == null) return;

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
            lastMouse = m;
            distT -= Input.mouseScrollDelta.y * 0.4f;
        }
#endif

        void LateUpdate()
        {
            yaw = Mathf.SmoothDamp(yaw, yawT, ref yawV, smoothTime);
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

            Vector3 focus = new Vector3(midX, 0f, midZ) + targetOffset;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus + rot * new Vector3(0f, 0f, -dist);
            if (pos.y < 0.15f) pos.y = 0.15f;
            // камера не заходит в стены храма, ворота и сады за пределами арены
            pos.x = Mathf.Clamp(pos.x, -5.7f, 5.7f);
            pos.z = Mathf.Clamp(pos.z, -6.5f, 3.2f);
            pos.y = Mathf.Min(pos.y, 6f);
            if (shakeAmp > 0.0005f)
            {
                pos += Random.insideUnitSphere * shakeAmp;
                shakeAmp *= Mathf.Exp(-14f * Time.unscaledDeltaTime);
            }
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(focus - pos, Vector3.up);
        }
    }
}
