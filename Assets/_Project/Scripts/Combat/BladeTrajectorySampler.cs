using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Обнаружение контакта клинка с телом ПО ТРАЕКТОРИИ, а не по конечной позиции кадра (ТЗ, раздел 11).
    ///
    /// Каждый кадр, пока открыто окно контакта:
    ///  1. берём позу основания клинка в прошлом и текущем кадре;
    ///  2. делим движение на подшаги (по углу поворота и по линейному смещению), позу между ними
    ///     интерполируем (Lerp позиции + Slerp вращения) — так учитывается ВРАЩЕНИЕ клинка, а не только перенос;
    ///  3. для нескольких точек вдоль клинка выполняем SphereCast между позициями соседних подшагов;
    ///  4. отдельно делаем OverlapSphere: каст не видит коллайдер, внутри которого уже стоит сфера;
    ///  5. контакты внутри подшага сортируются, каждая зона регистрируется один раз за удар.
    ///
    /// Выполняется в LateUpdate, т.е. после того как оружие и боец уже сдвинулись в этом кадре:
    /// проверяется ровно то, что игрок видит на экране.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(WeaponController))]
    public class BladeTrajectorySampler : MonoBehaviour
    {
        [Header("Sampling")]
        [Range(2, 12)] public int samplePoints = 6;
        [Tooltip("Макс. угол поворота клинка между соседними подшагами, град.")]
        public float maxAngleStepDeg = 3f;
        [Tooltip("Макс. смещение основания клинка между подшагами, м.")]
        public float maxLinearStep = 0.06f;
        [Range(1, 32)] public int maxSubsteps = 16;
        [Tooltip("Контакты медленнее этого (м/с) игнорируются: касание не равно удару.")]
        public float minContactSpeed = 2f;
        public LayerMask hitMask = ~0;

        [Header("Debug")]
        public bool recordDebug = true;

        public event Action<BladeContact> ContactDetected;

        /// <summary>Сколько подшагов понадобилось в последнем проверенном кадре.</summary>
        public int SubstepsLastFrame { get; private set; }
        /// <summary>true, если нужно было больше подшагов, чем разрешает maxSubsteps (возможны пропуски).</summary>
        public bool UndersampledLastFrame { get; private set; }
        /// <summary>Оружие сэмплера. Ленивая инициализация: к свойству могут обратиться раньше, чем выполнится Awake.</summary>
        public WeaponController Weapon { get { if (weapon == null) weapon = GetComponent<WeaponController>(); return weapon; } }

        public struct DebugSegment { public Vector3 a, b; public float time; public float bladeT; }
        const int DebugCapacity = 1024;
        readonly DebugSegment[] ring = new DebugSegment[DebugCapacity];
        int ringHead, ringCount;
        public int DebugCount { get { return ringCount; } }
        /// <summary>i = 0 — самый старый сегмент.</summary>
        public DebugSegment GetDebugSegment(int i) { return ring[(ringHead - ringCount + i + DebugCapacity * 2) % DebugCapacity]; }

        struct Candidate
        {
            public BodyZone zone;
            public Vector3 point, normal, velocity;
            public float bladeT, order;
            public bool overlap;
        }

        WeaponController weapon;
        Vector3 prevPos;
        Quaternion prevRot;
        bool hasPrev;

        readonly HashSet<BodyZone> contactedThisSwing = new HashSet<BodyZone>();
        readonly Dictionary<Collider, BodyZone> zoneCache = new Dictionary<Collider, BodyZone>();
        readonly RaycastHit[] castBuf = new RaycastHit[16];
        readonly Collider[] overlapBuf = new Collider[16];
        readonly List<Candidate> candidates = new List<Candidate>(32);
        static readonly Comparison<Candidate> ByOrder = delegate (Candidate x, Candidate y) { return x.order.CompareTo(y.order); };

        void Awake() { weapon = GetComponent<WeaponController>(); }
        void OnEnable() { if (weapon == null) weapon = GetComponent<WeaponController>(); weapon.ContactWindowOpened += OnWindowOpened; }
        void OnDisable() { if (weapon != null) weapon.ContactWindowOpened -= OnWindowOpened; }

        // новый удар — новый набор «уже задетых» зон
        void OnWindowOpened(WeaponController w) { contactedThisSwing.Clear(); }

        public void ResetTracking() { hasPrev = false; contactedThisSwing.Clear(); ringCount = 0; }

        void LateUpdate()
        {
            if (weapon == null || weapon.bladeRoot == null) return;
            Vector3 curPos = weapon.bladeRoot.position;
            Quaternion curRot = weapon.bladeRoot.rotation;

            if (!hasPrev) { prevPos = curPos; prevRot = curRot; hasPrev = true; return; }

            float dt = Time.deltaTime;
            if (weapon.ContactWindowOpen && dt > 1e-5f) Sweep(prevPos, prevRot, curPos, curRot, dt);

            prevPos = curPos;
            prevRot = curRot;
        }

        void Sweep(Vector3 p0, Quaternion r0, Vector3 p1, Quaternion r1, float dt)
        {
            // коллайдеры тел могли сдвинуться трансформом в этом кадре — синхронизируем физический мир
            Physics.SyncTransforms();

            float angle = Quaternion.Angle(r0, r1);
            float linear = (p1 - p0).magnitude;
            int needed = Mathf.CeilToInt(Mathf.Max(angle / maxAngleStepDeg, linear / maxLinearStep));
            int n = Mathf.Clamp(needed, 1, maxSubsteps);
            SubstepsLastFrame = n;
            UndersampledLastFrame = needed > maxSubsteps;
            float subDt = dt / n;

            for (int i = 1; i <= n; i++)
            {
                float s0 = (i - 1) / (float)n;
                float s1 = i / (float)n;
                Vector3 pa = Vector3.Lerp(p0, p1, s0);
                Vector3 pb = Vector3.Lerp(p0, p1, s1);
                Quaternion qa = Quaternion.Slerp(r0, r1, s0);
                Quaternion qb = Quaternion.Slerp(r0, r1, s1);

                candidates.Clear();
                for (int k = 1; k <= samplePoints; k++)
                {
                    float t = k / (float)samplePoints;
                    Vector3 a = pa + qa * (Vector3.forward * (weapon.bladeLength * t));
                    Vector3 b = pb + qb * (Vector3.forward * (weapon.bladeLength * t));
                    ProcessSample(a, b, t, subDt, i == 1);
                }

                candidates.Sort(ByOrder);
                for (int c = 0; c < candidates.Count; c++) Register(candidates[c]);
            }
        }

        void ProcessSample(Vector3 a, Vector3 b, float t, float subDt, bool checkStartOverlap)
        {
            Vector3 seg = b - a;
            float dist = seg.magnitude;
            Vector3 vel = seg / subDt;

            if (recordDebug) PushDebug(a, b, t);
            if (vel.sqrMagnitude < minContactSpeed * minContactSpeed) return;

            float r = weapon.bladeRadius;

            // Начало подшага: каст не увидит то, внутри чего сфера уже стоит
            if (checkStartOverlap) CollectOverlaps(a, r, t, vel);

            if (dist > 1e-4f)
            {
                int hits = Physics.SphereCastNonAlloc(a, r, seg / dist, castBuf, dist, hitMask, QueryTriggerInteraction.Ignore);
                for (int j = 0; j < hits; j++)
                {
                    RaycastHit h = castBuf[j];
                    BodyZone zone = ResolveZone(h.collider);
                    if (zone == null) continue;

                    Candidate c = new Candidate();
                    c.zone = zone; c.velocity = vel; c.bladeT = t;
                    c.order = h.distance / dist;
                    if (h.distance <= 0f && h.point == Vector3.zero)
                    {
                        // часть версий Unity для стартового перекрытия отдаёт distance=0 и point=0
                        c.point = h.collider.ClosestPoint(a);
                        c.normal = SafeNormal(a - c.point, vel);
                        c.overlap = true;
                    }
                    else { c.point = h.point; c.normal = h.normal; c.overlap = false; }
                    candidates.Add(c);
                }
            }

            // Конец подшага (он же начало следующего)
            CollectOverlaps(b, r, t, vel);
        }

        void CollectOverlaps(Vector3 pos, float r, float t, Vector3 vel)
        {
            int count = Physics.OverlapSphereNonAlloc(pos, r, overlapBuf, hitMask, QueryTriggerInteraction.Ignore);
            for (int j = 0; j < count; j++)
            {
                BodyZone zone = ResolveZone(overlapBuf[j]);
                if (zone == null) continue;
                Vector3 closest = overlapBuf[j].ClosestPoint(pos);
                Candidate c = new Candidate();
                c.zone = zone; c.velocity = vel; c.bladeT = t;
                c.point = closest;
                c.normal = SafeNormal(pos - closest, vel);
                c.order = 1f; // позже кастовых контактов того же подшага
                c.overlap = true;
                candidates.Add(c);
            }
        }

        static Vector3 SafeNormal(Vector3 v, Vector3 fallbackVel)
        {
            if (v.sqrMagnitude > 1e-8f) return v.normalized;
            return fallbackVel.sqrMagnitude > 1e-8f ? -fallbackVel.normalized : Vector3.up;
        }

        BodyZone ResolveZone(Collider col)
        {
            if (col == null) return null;
            BodyZone zone;
            if (!zoneCache.TryGetValue(col, out zone))
            {
                zone = col.GetComponentInParent<BodyZone>();
                zoneCache[col] = zone;
            }
            if (zone == null || zone.owner == weapon.owner) return null; // не бьём себя
            return zone;
        }

        void Register(Candidate c)
        {
            // одна зона — один контакт за удар (защита от дублей между подшагами/кадрами/кастом и overlap)
            if (!contactedThisSwing.Add(c.zone)) return;

            BladeContact contact = new BladeContact();
            contact.weapon = weapon;
            contact.zone = c.zone;
            contact.point = c.point;
            contact.normal = c.normal;
            contact.bladeVelocity = c.velocity;
            contact.bladeT = c.bladeT;
            contact.swingId = weapon.SwingId;
            contact.fromOverlap = c.overlap;
            if (ContactDetected != null) ContactDetected(contact);
        }

        void PushDebug(Vector3 a, Vector3 b, float t)
        {
            DebugSegment s = new DebugSegment();
            s.a = a; s.b = b; s.bladeT = t; s.time = Time.time;
            ring[ringHead] = s;
            ringHead = (ringHead + 1) % DebugCapacity;
            if (ringCount < DebugCapacity) ringCount++;
        }
    }
}
