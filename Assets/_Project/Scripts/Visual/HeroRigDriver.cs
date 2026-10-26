using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Ведёт скелет героя. Ноги и корпус играет Animator (стойка / шаг / прыжок / получение удара),
    /// а руки целиком ведёт этот скрипт: двухкостный IK тянет обе кисти к рукояти катаны, которой управляет WeaponController.
    /// Клинок и попадания по-прежнему считаются по pivot катаны, а не по скелету: модель только «держится» за оружие.
    /// Порядок: Animator (в Update) → этот LateUpdate: наклон корпуса от фазы удара → IK рук.
    /// </summary>
    public class HeroRigDriver : MonoBehaviour
    {
        public FighterController fighter;
        public WeaponController weapon;
        public Transform bladeRoot;
        public Animator animator;

        [Header("Кости (задаёт сборщик сцены)")]
        public Transform spine, chest;
        public Transform upperArmL, forearmL, handL;
        public Transform upperArmR, forearmR, handR;

        [Header("Хват (м назад от основания клинка до запястья)")]
        public float gripFront = 0.10f;   // передняя рука (правая) — у гарды
        public float gripRear = 0.24f;    // задняя рука (левая) — у навершия

        [Header("Наклон корпуса в ударе, град.")]
        public float leanBack = -12f;     // на замахе
        public float leanForward = 14f;   // в конце удара
        public float leanSpeed = 140f;    // град/с

        float l1L, l2L, l1R, l2R;
        float lean;
        CombatResolver resolver;

        void Start()
        {
            Cache(upperArmL, forearmL, handL, out l1L, out l2L);
            Cache(upperArmR, forearmR, handR, out l1R, out l2R);
        }

        static void Cache(Transform u, Transform f, Transform h, out float a, out float b)
        {
            a = (u != null && f != null) ? Vector3.Distance(u.position, f.position) : 0.3f;
            b = (f != null && h != null) ? Vector3.Distance(f.position, h.position) : 0.26f;
        }

        void OnDisable()
        {
            if (resolver != null) resolver.HitRegistered -= OnHit;
            resolver = null;
        }

        void Update()
        {
            if (resolver == null && CombatResolver.Instance != null)
            {
                resolver = CombatResolver.Instance;
                resolver.HitRegistered += OnHit;
            }
            if (animator == null || fighter == null) return;
            float move = fighter.MoveAxis;
            animator.SetFloat("Move", move);
            if (Mathf.Abs(move) > 0.1f) animator.SetFloat("WalkSpeed", move < 0f ? -1f : 1f);
            animator.SetBool("Grounded", fighter.Grounded);
        }

        void OnHit(HitResult r)
        {
            if (animator != null && r.target == fighter) animator.SetTrigger("Hit");
        }

        void LateUpdate()
        {
            if (fighter == null || bladeRoot == null) return;

            // 1) корпус слегка откидывается на замахе и подаётся вперёд в ударе
            float target = 0f;
            if (weapon != null && weapon.Phase != SwingPhase.Idle && weapon.pivot != null)
            {
                float pitch = Mathf.DeltaAngle(0f, weapon.pivot.localEulerAngles.x);   // −120 … +85
                target = Mathf.Lerp(leanBack, leanForward, Mathf.InverseLerp(-120f, 60f, pitch));
            }
            lean = Mathf.MoveTowards(lean, target, leanSpeed * Time.deltaTime);
            Vector3 right = fighter.transform.right;
            if (spine != null) spine.rotation = Quaternion.AngleAxis(lean * 0.5f, right) * spine.rotation;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(lean * 0.5f, right) * chest.rotation;

            // 2) руки держат рукоять
            Vector3 back = -bladeRoot.forward;
            Vector3 outward = fighter.transform.right * 0.6f + Vector3.down + back * 0.3f;
            SolveArm(upperArmR, forearmR, handR, l1R, l2R, bladeRoot.position + back * gripFront, outward);
            Vector3 outwardL = -fighter.transform.right * 0.6f + Vector3.down + back * 0.3f;
            SolveArm(upperArmL, forearmL, handL, l1L, l2L, bladeRoot.position + back * gripRear, outwardL);
        }

        /// <summary>Аналитический двухкостный IK: локоть в плоскости (плечо → цель) со смещением в сторону pole.</summary>
        static void SolveArm(Transform upper, Transform fore, Transform hand, float l1, float l2, Vector3 target, Vector3 pole)
        {
            if (upper == null || fore == null || hand == null) return;
            Vector3 s = upper.position;
            Vector3 toT = target - s;
            float dist = Mathf.Clamp(toT.magnitude, 0.05f, (l1 + l2) * 0.999f);
            Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : Vector3.forward;

            float a = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 perp = Vector3.ProjectOnPlane(pole, dir);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.ProjectOnPlane(Vector3.down, dir);
            perp.Normalize();

            Vector3 elbow = s + dir * a + perp * h;
            Vector3 handPos = s + dir * dist;

            // плечо смотрит на локоть, предплечье — на кисть (длины костей не меняются)
            upper.rotation = Quaternion.FromToRotation(fore.position - s, elbow - s) * upper.rotation;
            fore.rotation = Quaternion.FromToRotation(hand.position - fore.position, handPos - fore.position) * fore.rotation;
        }
    }
}
