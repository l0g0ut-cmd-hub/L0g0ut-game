using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Ведёт скелет бойца поверх Animator. Ноги, таз и позвоночник играет Animator (стойка / шаг / прыжок / получение удара),
    /// а этот скрипт в LateUpdate добавляет «живость» и ведёт руки:
    ///  1) тело следует за клинком: корпус скручивается и наклоняется вслед за позой катаны (таз → поясница → грудь),
    ///     выпад вперёд на ударе, откат назад на замахе, приседание на низких ударах (ноги сгибаются, корпус опускается);
    ///  2) отдача при получении удара (затухающий толчок назад);
    ///  3) дыхание и лёгкое покачивание в стойке;
    ///  4) руки: двухкостный IK тянет обе кисти к рукояти, а кисти разворачиваются так, чтобы ладони охватывали её.
    /// Клинок и попадания по-прежнему считаются по pivot катаны, а не по скелету: модель только «держится» за оружие,
    /// а выпад/присед — чисто визуальные (зоны поражения не двигаются).
    /// Порядок: Animator (в Update) → этот LateUpdate: тело → IK рук.
    /// </summary>
    public class HeroRigDriver : MonoBehaviour
    {
        public FighterController fighter;
        public WeaponController weapon;
        public Transform bladeRoot;
        public Animator animator;

        [Header("Кости (задаёт сборщик сцены)")]
        public Transform hips, spine, chest, head;
        public Transform upperArmL, forearmL, handL;
        public Transform upperArmR, forearmR, handR;
        public Transform upperLegL, lowerLegL, footL;
        public Transform upperLegR, lowerLegR, footR;

        [Header("Хват (м назад от основания клинка до запястья)")]
        public float gripFront = 0.10f;   // передняя рука (правая) — у гарды
        public float gripRear = 0.24f;    // задняя рука (левая) — у навершия
        [Tooltip("Расстояние от запястья до центра ладони вдоль пальцев, м (кисть сдвигается так, чтобы рукоять проходила через ладонь).")]
        public float palmOffset = 0.06f;

        [Header("Тело в ударе")]
        [Tooltip("Доли поворота клинка (yaw, до ±45°), которые отрабатывают таз / поясница / грудь.")]
        public float hipsYawK = 0.15f, spineYawK = 0.20f, chestYawK = 0.25f;
        public float leanBack = -12f;     // наклон корпуса на замахе, град.
        public float leanForward = 14f;   // и в конце удара
        public float lungeForward = 0.16f; // выпад вперёд на ударе, м
        public float lungeBack = 0.05f;    // откат назад на замахе, м
        [Tooltip("Глубина приседа на низких ударах: угол бедра, град. (колено ≈ ×1.8).")]
        public float crouchThigh = 38f;

        [Header("Стойка")]
        [Tooltip("Разнос ног в боевой стойке, град.: передняя нога (на стороне передней руки) вперёд, задняя назад. На ходьбе гасится.")]
        public float stanceStagger = 18f;
        [Tooltip("Глубина боевой стойки (колени согнуты, таз ниже), 0…1; на ходьбе гасится.")]
        [Range(0f, 1f)] public float stanceCrouch = 0.45f;

        [Header("Отдача при попадании")]
        public float hitKickDecay = 2.8f;  // 1/с

        float l1L, l2L, l1R, l2R;
        float lean, yawS, yawV, bS, bV, crouchS, crouchV, kick;
        float pivotRestY, walkS;
        Vector3 baseLocalPos;
        Quaternion bindL, bindR;
        float sideL = 1f, sideR = -1f;
        bool framesReady;
        CombatResolver resolver;

        void Start()
        {
            Cache(upperArmL, forearmL, handL, out l1L, out l2L);
            Cache(upperArmR, forearmR, handR, out l1R, out l2R);
            baseLocalPos = transform.localPosition;
            if (weapon != null && weapon.pivot != null) pivotRestY = weapon.pivot.localPosition.y;
            CacheHandFrame(handL, forearmL, out bindL, out sideL);
            CacheHandFrame(handR, forearmR, out bindR, out sideR);
        }

        static void Cache(Transform u, Transform f, Transform h, out float a, out float b)
        {
            a = (u != null && f != null) ? Vector3.Distance(u.position, f.position) : 0.3f;
            b = (f != null && h != null) ? Vector3.Distance(f.position, h.position) : 0.26f;
        }

        /// <summary>Запоминает позу кисти в покое: направление пальцев ≈ продолжение предплечья, ладонь смотрит к оси тела.</summary>
        void CacheHandFrame(Transform hand, Transform fore, out Quaternion bind, out float side)
        {
            bind = Quaternion.identity; side = 1f;
            if (hand == null || fore == null || fighter == null) return;
            Transform ft = fighter.transform;
            side = Mathf.Sign(Vector3.Dot(hand.position - ft.position, ft.right));
            if (side == 0f) side = 1f;
            Vector3 f = (hand.position - fore.position).normalized;
            Vector3 n = Vector3.ProjectOnPlane(-side * ft.right, f);
            if (n.sqrMagnitude < 1e-6f || f.sqrMagnitude < 1e-6f) return;
            bind = Quaternion.Inverse(Quaternion.LookRotation(f, n.normalized)) * hand.rotation;
            framesReady = true;
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
            if (r.target != fighter) return;
            if (animator != null) animator.SetTrigger("Hit");
            kick = 1f;
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }

        void LateUpdate()
        {
            if (fighter == null || bladeRoot == null) return;
            float dt = Time.deltaTime;
            Transform ft = fighter.transform;
            Vector3 right = ft.right, up = ft.up;
            float sc = transform.lossyScale.y;

            // ---------- 1) состояние удара → «поза тела» b: −1 замах, +1 конец удара, 0 покой
            float bTarget = 0f, yawTarget = 0f, pitchTarget = 0f, crouchTarget = 0f;
            bool swinging = weapon != null && weapon.pivot != null && weapon.Phase != SwingPhase.Idle;
            if (weapon != null && weapon.pivot != null)
            {
                float t = weapon.PhaseProgress;
                switch (weapon.Phase)
                {
                    case SwingPhase.Windup: bTarget = -Smooth(t); break;
                    case SwingPhase.Strike: bTarget = Mathf.Lerp(-1f, 1f, t); break;
                    case SwingPhase.FollowThrough: bTarget = 1f; break;
                    case SwingPhase.Recovery: bTarget = Mathf.Lerp(1f, 0f, Smooth(t)); break;
                }
                if (swinging)
                {
                    Vector3 e = weapon.pivot.localEulerAngles;
                    float pitch = Mathf.DeltaAngle(0f, e.x);                 // −120 … +85
                    yawTarget = Mathf.Clamp(Mathf.DeltaAngle(0f, e.y), -45f, 45f);
                    pitchTarget = Mathf.Lerp(leanBack, leanForward, Mathf.InverseLerp(-120f, 60f, pitch));
                }
                crouchTarget = Mathf.Clamp01((pivotRestY - weapon.pivot.localPosition.y) / 0.45f);
            }
            lean = Mathf.MoveTowards(lean, pitchTarget, 140f * dt);
            yawS = Mathf.SmoothDampAngle(yawS, yawTarget, ref yawV, 0.05f);
            bS = Mathf.SmoothDamp(bS, bTarget, ref bV, 0.04f);
            crouchS = Mathf.SmoothDamp(crouchS, crouchTarget, ref crouchV, 0.07f);
            kick = Mathf.MoveTowards(kick, 0f, hitKickDecay * dt);

            // боевая стойка действует, пока боец стоит на земле и не идёт; в удар её дополняют шаг и присед из профиля атаки
            walkS = Mathf.MoveTowards(walkS, Mathf.Abs(fighter.MoveAxis) > 0.1f || !fighter.Grounded ? 1f : 0f, 6f * dt);
            SwingProfile prof = (weapon != null && swinging) ? weapon.CurrentProfile : null;
            float strikeK = Mathf.Max(0f, bS);
            float stepM = prof != null ? prof.bodyStep : lungeForward;
            float cr = Mathf.Clamp01(crouchS + stanceCrouch * (1f - walkS) + (prof != null ? prof.crouchBoost * strikeK : 0f));
            float stride = (1f - walkS) * stanceStagger + (prof != null ? prof.strideDeg * strikeK * (1f - walkS) : 0f);

            // ---------- 2) дыхание и покачивание в стойке (глушится во время удара)
            float calm = swinging ? 0f : 1f;
            float time = Time.time;
            float swayYaw = Mathf.Sin(time * 0.8f) * 2.0f * calm;
            float swayPitch = Mathf.Sin(time * 1.1f + 1f) * 1.2f * calm;
            float headYaw = Mathf.Sin(time * 0.55f + 2f) * 3.0f * calm;

            // ---------- 3) смещение всей модели: выпад / откат / присед / отдача (визуально; зоны поражения не двигаются)
            float lunge = strikeK * stepM - Mathf.Max(0f, -bS) * lungeBack - kick * 0.10f;
            float thighRad = cr * crouchThigh * Mathf.Deg2Rad;
            float kneeRad = cr * crouchThigh * 1.8f * Mathf.Deg2Rad;
            float drop = sc * (0.45f * (1f - Mathf.Cos(thighRad)) + 0.41f * (1f - Mathf.Cos(Mathf.Max(0f, kneeRad - thighRad))))
                       + sc * 0.78f * (1f - Mathf.Cos(stride * Mathf.Deg2Rad));   // ноги врозь — таз ниже, иначе стопы повисают над полом
            Vector3 off = new Vector3(0f, -drop - Mathf.Max(0f, bS) * 0.02f, lunge);
            transform.localPosition = baseLocalPos + off;

            // ---------- 4) повороты костей сверху вниз (родитель раньше ребёнка)
            Rot(hips, yawS * hipsYawK + swayYaw * 0.4f, up);
            Rot(spine, yawS * spineYawK + swayYaw * 0.3f, up);
            Rot(chest, yawS * chestYawK + swayYaw * 0.3f, up);
            float pitchTotal = lean * 0.5f - kick * 8f + swayPitch * 0.5f;
            Rot(spine, pitchTotal, right);
            Rot(chest, lean * 0.5f - kick * 12f + swayPitch * 0.5f, right);
            Rot(head, headYaw - yawS * 0.25f, up);              // голова частично «смотрит» на противника, а не вслед за клинком
            Rot(head, -pitchTotal * 0.5f + kick * 6f, right);

            if (cr > 0.01f)
            {
                float f = cr * crouchThigh, k = f * 1.8f;
                Rot(upperLegL, -f, right); Rot(lowerLegL, k, right); Rot(footL, f - k, right);
                Rot(upperLegR, -f, right); Rot(lowerLegR, k, right); Rot(footR, f - k, right);
            }

            // ---------- 4b) разнос ног: передняя нога (на стороне передней руки) вперёд, задняя назад; на ударе шаг шире
            float st = stride;
            if (st > 0.01f)
            {
                Rot(upperLegR, -st, right); Rot(lowerLegR, st * 0.4f, right); Rot(footR, st * 0.6f, right);
                Rot(upperLegL, st * 0.6f, right); Rot(lowerLegL, st * 0.5f, right); Rot(footL, -st * 1.1f, right);
            }

            // ---------- 5) руки держат рукоять
            Vector3 axis = bladeRoot.forward;
            Vector3 back = -axis;
            Vector3 outward = ft.right * 0.6f + Vector3.down + back * 0.3f;
            Vector3 gripR = bladeRoot.position + back * gripFront;
            Vector3 gripL = bladeRoot.position + back * gripRear;
            Vector3 fR, nR, fL, nL;
            GripFrame(axis, sideR, ft, out fR, out nR);
            GripFrame(axis, sideL, ft, out fL, out nL);
            SolveArm(upperArmR, forearmR, handR, l1R, l2R, gripR - fR * palmOffset - nR * 0.015f, outward);
            Vector3 outwardL = -ft.right * 0.6f + Vector3.down + back * 0.3f;
            SolveArm(upperArmL, forearmL, handL, l1L, l2L, gripL - fL * palmOffset - nL * 0.015f, outwardL);
            if (framesReady)
            {
                OrientHand(handR, bindR, fR, nR);
                OrientHand(handL, bindL, fL, nL);
            }
        }

        static void Rot(Transform t, float angle, Vector3 axis)
        {
            if (t != null && Mathf.Abs(angle) > 1e-3f) t.rotation = Quaternion.AngleAxis(angle, axis) * t.rotation;
        }

        /// <summary>Нужные направления кисти: пальцы поперёк клинка (вниз-внутрь), ладонь обращена к рукояти.</summary>
        static void GripFrame(Vector3 axis, float side, Transform ft, out Vector3 fingers, out Vector3 palm)
        {
            Vector3 inward = -side * ft.right;
            Vector3 f = Vector3.ProjectOnPlane(Vector3.down * 0.8f + inward * 0.5f, axis);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(inward, axis);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(ft.forward, axis);
            fingers = f.normalized;
            Vector3 n = Vector3.Cross(fingers, axis).normalized;
            if (Vector3.Dot(n, inward) < 0f) n = -n;
            palm = n;
        }

        static void OrientHand(Transform hand, Quaternion bind, Vector3 fingers, Vector3 palm)
        {
            if (hand == null || fingers.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-6f) return;
            hand.rotation = Quaternion.LookRotation(fingers, palm) * bind;
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
