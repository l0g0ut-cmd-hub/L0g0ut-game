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
        public float gripFront = 0.08f;   // передняя рука (правая) — у гарды
        public float gripRear = 0.20f;    // задняя рука (левая) — у навершия
        [Tooltip("Расстояние от запястья до центра ладони вдоль пальцев, м (кисть сдвигается так, чтобы рукоять проходила через ладонь).")]
        public float palmOffset = 0.06f;
        [Tooltip("Насколько рукоять смещена от кости кисти к ладони, м (у сжатого кулака рукоять проходит внутри пальцев).")]
        public float palmLift = 0.015f;
        [Tooltip("Высота глаз над костью головы, м (светящиеся глаза врага).")]
        public float eyeHeight = 0.045f;
        [Tooltip("Максимальный излом кисти относительно руки, град. (иначе хват поперёк клинка ломает запястье при вытянутых вперёд руках).")]
        public float maxWristBend = 30f;

        [Header("Тело в ударе (старые профили без ключевых поз)")]
        [Tooltip("Доли поворота клинка (yaw, до bodyYawMax профиля, по умолчанию ±45°), которые отрабатывают таз / поясница / грудь.")]
        public float hipsYawK = 0.15f, spineYawK = 0.20f, chestYawK = 0.25f;
        public float leanBack = -12f;     // наклон корпуса на замахе, град.
        public float leanForward = 14f;   // и в конце удара
        public float lungeForward = 0.16f; // выпад вперёд на ударе, м

        [Header("Стойка: стопы ставит IK, таз — ключевые позы")]
        [Tooltip("Места стоп в стойке, м, в системе бойца (x вправо, z вперёд) для игрока; у врага x зеркалится (yawSign). Какая нога передняя — по знаку x.")]
        public float footFrontX = -0.14f, footFrontZ = 0.48f, footRearX = 0.30f, footRearZ = -0.50f;
        [Tooltip("Разворот стоп в стойке, град. (+ носок вправо), для игрока; у врага зеркалится. Задняя развёрнута наружу — открытая стойка.")]
        public float footFrontYaw = -10f, footRearYaw = 75f;

        [Header("Походка: стопы переступают сами (IK), без клипа ходьбы")]
        public float stepThreshold = 0.08f;   // стопа ушла от своего места стойки дальше — шаг
        public float stepTimeMin = 0.18f, stepTimeMax = 0.26f;
        public float stepThresholdPerSpeed = 0.06f;
        [Tooltip("На ходу стойка собирается (ноги ближе, ~0.35 длины), остановился — снова широкая: иначе ноги не дотягиваются и боец «садится в шпагат».")]
        public float walkStanceZ = 0.35f, walkStanceX = 0.7f;
        public float maxAutoDrop = 0.12f;
        public float stanceDrop = 0.04f;   // колени мягко согнуты в стойке, м
        public float stepLift = 0.07f;     // подъём стопы в шаге, м
        public float heelLift = 0.05f;     // подъём пятки задней ноги (вес на носке), м
        public float postureBack = 6f;     // осанка: грудь назад/вверх, голова поднята (против сутулости модели), град.
        [Range(0f, 1f)] public float headStabilize = 0.75f;   // голова держит горизонт и смотрит на врага, а не болтается с корпусом

        [Header("Отдача при попадании")]
        public float hitKickDecay = 2.8f;  // 1/с

        float l1L, l2L, l1R, l2R;
        float lgL1, lgL2, lgR1, lgR2;
        float lean, yawS, yawV, bS, bV, crouchS, crouchV, kick;
        float pivotRestY, walkS, deadK, footRestH, wLegS;
        struct Foot { public Vector3 pos, from, to; public float yaw, yawFrom, yawTo, u, dur, lift; public bool step; }
        Foot gF, gR;
        bool gaitInit;
        float keyW, moveK;
        Vector3 lastFtPos;
        bool fallDust;
        bool decapitated;
        /// <summary>Смертельный удар в голову: голова «исчезает» (кость сжимается в точку), из шеи бьёт фонтан.</summary>
        public void Decapitate()
        {
            if (decapitated || head == null) return;
            decapitated = true;
            if (Juice.Instance != null) Juice.Instance.Fountain(head, 3.2f);
        }
        BodyZoneId kickZone;
        float kickSide;
        Vector3 baseLocalPos;
        Quaternion baseLocalRot = Quaternion.identity;
        Quaternion bindL, bindR;
        Quaternion footRelL = Quaternion.identity, footRelR = Quaternion.identity, headRel = Quaternion.identity;
        float sideL = 1f, sideR = -1f;
        bool framesReady, legsReady;
        CombatResolver resolver;

        void Start()
        {
            Cache(upperArmL, forearmL, handL, out l1L, out l2L);
            Cache(upperArmR, forearmR, handR, out l1R, out l2R);
            Cache(upperLegL, lowerLegL, footL, out lgL1, out lgL2);
            Cache(upperLegR, lowerLegR, footR, out lgR1, out lgR2);
            baseLocalPos = transform.localPosition;
            wLegS = 1f;   // стойка сразу, без «проседания» в первый кадр
            baseLocalRot = transform.localRotation;
            if (footL != null && footR != null && fighter != null)
            {
                Transform ft = fighter.transform;
                footRestH = Mathf.Min(footL.position.y, footR.position.y) - ft.position.y;
                // поза покоя модели: подошвы стоят на земле — запоминаем, как стопа повёрнута относительно бойца
                footRelL = Quaternion.Inverse(ft.rotation) * footL.rotation;
                footRelR = Quaternion.Inverse(ft.rotation) * footR.rotation;
                if (head != null) headRel = Quaternion.Inverse(ft.rotation) * head.rotation;
                legsReady = upperLegL != null && lowerLegL != null && upperLegR != null && lowerLegR != null;
            }
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
            animator.SetFloat("Move", 0f);   // ходьбу делают шаги стоп (HeroRigDriver.Gait), клип WalkF не нужен
            animator.SetBool("Grounded", fighter.Grounded);
        }

        void OnHit(HitResult r)
        {
            if (r.target != fighter) return;
            if (animator != null) animator.SetTrigger("Hit");
            kick = 1f;
            // реакция зависит от зоны: голова запрокидывается, ноги подламываются, рука/корпус уводят плечи вбок
            kickZone = r.zone;
            Vector3 v = r.contact.bladeVelocity;
            kickSide = fighter != null && v.sqrMagnitude > 1e-4f ? Mathf.Sign(Vector3.Dot(v, fighter.transform.right)) : 0f;
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }

        /// <summary>
        /// Порядок: Animator (Update) → здесь: поза всего тела из WeaponController.Pose (ключевые позы удара или стойка) →
        /// таз (вынос, высота, поворот, наклон) → грудь → голова держит горизонт → ноги IK на стоящие стопы → руки IK на рукоять.
        /// Стопы не скользят: каждая стоит на своём месте, пока клип не «шагнул» ею (канал FF/RF).
        /// </summary>
        void LateUpdate()
        {
            if (fighter == null || bladeRoot == null) return;
            float dt = Time.deltaTime;
            if (decapitated && fighter != null && !fighter.Dead) decapitated = false;   // новый раунд
            if (head != null) head.localScale = decapitated ? Vector3.one * 0.001f : Vector3.one;
            Transform ft = fighter.transform;
            Vector3 right = ft.right, up = ft.up, fwd = ft.forward;

            if (weapon != null && weapon.pivot != null) weapon.pivot.localPosition = weapon.PureLocalPosition;   // снять прошлую поправку досягаемости
            float diagHipA = DiagYaw(upperLegL, upperLegR, ft), diagChestA = DiagYaw(upperArmL, upperArmR, ft);   // поза Animator до драйвера

            // ---------- 1) поза тела
            float[] P = weapon != null ? weapon.Pose : SwingProfile.ReadyPose;
            float ys = weapon != null ? weapon.yawSign : 1f;
            bool swinging = weapon != null && weapon.pivot != null && weapon.Phase != SwingPhase.Idle;
            bool keyed = weapon != null && weapon.Keyed;
            float pf = P[PoseCh.PF], pd = P[PoseCh.PD], hYaw = P[PoseCh.HYAW], hPit = P[PoseCh.HPIT];
            float cYaw = P[PoseCh.CYAW], cPit = P[PoseCh.CPIT], ff = P[PoseCh.FF], rf = P[PoseCh.RF], heel = P[PoseCh.HEEL];

            // старые профили (ещё без ключевых поз): тело достраивается из угла клинка, как раньше
            float bTarget = 0f, yawTarget = 0f, pitchTarget = 0f, crouchTarget = 0f;
            if (swinging && !keyed)
            {
                float t = weapon.PhaseProgress;
                switch (weapon.Phase)
                {
                    case SwingPhase.Windup: bTarget = -Smooth(t); break;
                    case SwingPhase.Strike: bTarget = Mathf.Lerp(-1f, 1f, t); break;
                    case SwingPhase.FollowThrough: bTarget = 1f; break;
                    case SwingPhase.Recovery: bTarget = Mathf.Lerp(1f, 0f, Smooth(t)); break;
                }
                Vector3 e = weapon.CurrentEuler;
                float maxYaw = weapon.CurrentProfile.bodyYawMax;
                yawTarget = Mathf.Clamp(e.y, -maxYaw, maxYaw) * weapon.CurrentProfile.twistMul;
                pitchTarget = Mathf.Lerp(leanBack, leanForward, Mathf.InverseLerp(-120f, 60f, e.x));
                crouchTarget = Mathf.Clamp01((weapon.restHand.y - weapon.HandOffset.y) / 0.45f);
            }
            if (fighter.Dashing) { crouchTarget = Mathf.Max(crouchTarget, 0.35f); pitchTarget = Mathf.Max(pitchTarget, 10f); }
            lean = Mathf.MoveTowards(lean, pitchTarget, 140f * dt);
            yawS = Mathf.SmoothDampAngle(yawS, yawTarget, ref yawV, 0.05f);
            bS = Mathf.SmoothDamp(bS, bTarget, ref bV, 0.04f);
            crouchS = Mathf.SmoothDamp(crouchS, crouchTarget, ref crouchV, 0.07f);
            kick = Mathf.MoveTowards(kick, 0f, hitKickDecay * dt);
            if (!keyed)
            {
                float strikeK = Mathf.Max(0f, bS);
                float lg = weapon != null ? weapon.VisualLunge : 0f;
                SwingProfile prof = swinging ? weapon.CurrentProfile : null;
                hYaw += yawS * hipsYawK; cYaw += yawS * (spineYawK + chestYawK); cPit += lean;
                pf += lg * 0.6f; ff += Mathf.Max(0f, lg); heel += strikeK * 0.5f;
                pd += crouchS * 0.16f + (prof != null ? prof.crouchBoost * strikeK * 0.2f : 0f);
            }

            // дыхание и покачивание в стойке (глушится во время удара)
            float calm = swinging ? 0f : 1f;
            float time = Time.time;
            hYaw += Mathf.Sin(time * 0.8f) * 0.8f * calm;
            cPit += Mathf.Sin(time * 1.1f + 1f) * 0.6f * calm;
            pd += (Mathf.Sin(time * 1.1f + 1f) * 0.006f) * calm;
            float headYaw = Mathf.Sin(time * 0.55f + 2f) * 2.0f * calm;

            // отдача: корпус назад, при ударе по ногам — колени подламываются
            pf -= kick * 0.10f;
            cPit -= kick * 10f;
            bool legHit = kickZone == BodyZoneId.LegLeft || kickZone == BodyZoneId.LegRight;
            if (legHit) pd += 0.12f * kick;

            // стойку (IK ног) держим всегда, пока боец на земле: при ходьбе стопы переступают сами; в прыжке ноги отдаются Animator
            walkS = Mathf.MoveTowards(walkS, Mathf.Abs(fighter.MoveAxis) > 0.1f ? 1f : 0f, 6f * dt);
            bool groundedOk = fighter.Grounded && deadK < 0.01f && legsReady;
            if (!groundedOk) gaitInit = false;
            wLegS = Mathf.MoveTowards(wLegS, groundedOk ? 1f : 0f, dt / 0.1f);
            float wSt = wLegS;
            moveK = Mathf.MoveTowards(moveK, Mathf.Abs(fighter.MoveAxis) > 0.1f && !keyed ? 1f : 0f, dt / 0.25f);
            pd *= 1f - 0.6f * moveK; pf *= 1f - moveK;   // на ходу таз выше и над стопами
            // ритм шага: таз поворачивается за шагающей ногой, грудь — навстречу, корпус чуть подаётся по ходу
            if (stepLift > 0f)
            {
                float sw = (gF.lift - gR.lift) / stepLift * ys;
                hYaw += 5f * sw; cYaw -= 3f * sw;
            }
            cPit += 4f * moveK * Mathf.Sign(fighter.MoveAxis);

            // ---------- 2) таз: вынос вперёд и высота (вся модель) — визуально; зоны поражения едут следом (FollowZones)
            float drop = (stanceDrop + pd) * wSt;
            Vector3 off = new Vector3(0f, -drop, pf);
            transform.localPosition = baseLocalPos + off;
            deadK = Mathf.MoveTowards(deadK, fighter.Dead ? 1f : 0f, dt / 0.8f);
            if (deadK >= 0.97f && !fallDust)
            {
                fallDust = true;   // тело ударилось о землю: облако пыли и глухой удар
                if (Juice.Instance != null) Juice.Instance.Dust(fighter.transform.position - fighter.transform.forward * 0.8f + Vector3.up * 0.05f, Vector3.up, 26);
                if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.04f);
            }
            else if (deadK < 0.01f) fallDust = false;
            if (deadK > 0.001f)
            {
                // падение навзничь вокруг стоп (поворот в системе бойца: +Z вперёд, +X вправо)
                Quaternion q = Quaternion.AngleAxis(-84f * Smooth(deadK), Vector3.right);
                transform.localPosition = q * (baseLocalPos + off);
                transform.localRotation = q * baseLocalRot;
            }
            else transform.localRotation = baseLocalRot;

            // ---------- 3) таз → поясница → грудь (родитель раньше ребёнка); ось наклона повёрнута вместе с тазом
            Rot(hips, hYaw, up);
            Vector3 hipRight = Quaternion.AngleAxis(hYaw, up) * right;
            Rot(hips, hPit, hipRight);
            Rot(spine, cYaw * 0.4f, up);
            Rot(chest, cYaw * 0.6f, up);
            Vector3 chestRight = Quaternion.AngleAxis(hYaw + cYaw, up) * right;
            Rot(spine, cPit * 0.45f - postureBack * 0.4f, chestRight);
            Rot(chest, cPit * 0.55f - postureBack * 0.6f, chestRight);
            if (kick > 0.01f)
            {
                float k2 = kick * kick;
                Rot(chest, -kickSide * k2 * (kickZone == BodyZoneId.ArmLeft || kickZone == BodyZoneId.ArmRight ? 16f : 9f), fwd);
                Rot(spine, kickSide * k2 * 6f, up);
            }
            // голова держит горизонт и смотрит на противника (лишь частично следует за поворотом корпуса)
            if (head != null && deadK < 0.01f)
            {
                Quaternion level = Quaternion.AngleAxis((hYaw + cYaw) * 0.3f + headYaw, up) * ft.rotation * headRel;
                head.rotation = Quaternion.Slerp(head.rotation, level, headStabilize);
                if (kick > 0.01f && kickZone == BodyZoneId.Head) { float k2 = kick * kick; Rot(head, -k2 * 22f, right); Rot(head, kickSide * k2 * 14f, up); }
            }

            // ---------- 4) ноги: IK на стоящие стопы (вес стойки wSt; при ходьбе/прыжке/смерти — Animator)
            float wLeg = legsReady && deadK < 0.01f ? wSt : 0f;
            if (wLeg > 0.001f) SolveLegs(ft, ys, hYaw, ff, rf, heel, wLeg, keyed, dt);
            else if (fighter.Grounded && deadK < 0.01f && footL != null && footR != null)
            {
                float lowest = Mathf.Min(footL.position.y, footR.position.y);   // ходьба: просто ставим нижнюю стопу на землю
                transform.position += Vector3.up * ((ft.position.y + footRestH) - lowest);
            }

            // ---------- 5) руки ставятся относительно плеч в системе корпуса: корпус присел / скрутился / вынесен вперёд — руки с клинком идут за ним
            if (weapon != null && weapon.pivot != null && upperArmR != null && upperArmL != null)
            {
                Vector3 shMid = (upperArmR.position + upperArmL.position) * 0.5f;
                // ключевые позы и стойка задают руки в системе бойца (линия реза не уезжает вбок с поворотом таза);
                // старые профили — в системе скрученного корпуса, как раньше
                float twist = (swinging && !keyed) ? yawS * (hipsYawK + spineYawK + chestYawK) : 0f;
                Quaternion frame = Quaternion.AngleAxis(twist, up) * ft.rotation;
                weapon.pivot.position = shMid + frame * weapon.HandOffset;
            }

            // досягаемость: если рукоять дальше, чем достают руки, меч подтягивается к плечам — кисти всегда на рукояти
            if (upperArmR != null && upperArmL != null && weapon != null && weapon.pivot != null)
            {
                float reachR = (l1R + l2R) * 0.96f, reachL = (l1L + l2L) * 0.96f;
                for (int it = 0; it < 5; it++)
                {
                    Vector3 ax = bladeRoot.forward;
                    Vector3 dr = bladeRoot.position - ax * gripFront - upperArmR.position;
                    Vector3 dl = bladeRoot.position - ax * gripRear - upperArmL.position;
                    float mr = dr.magnitude, ml = dl.magnitude;
                    Vector3 shift = Vector3.zero;
                    if (mr > reachR) shift -= dr / mr * (mr - reachR);
                    if (ml > reachL) shift -= dl / ml * (ml - reachL);
                    if (shift.sqrMagnitude < 1e-6f) break;
                    weapon.pivot.position += shift;
                }
            }

            if (Diag)
            {
                string st = animator == null ? "-" : animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") ? "Idle" : animator.GetCurrentAnimatorStateInfo(0).IsName("Walk") ? "Walk" : animator.GetCurrentAnimatorStateInfo(0).IsName("Jump") ? "Jump" : animator.GetCurrentAnimatorStateInfo(0).IsName("Hit") ? "Hit" : "?";
                diag = string.Format("{0}: anim hips {1:F0} chest {2:F0} | после hips {3:F0} chest {4:F0} | model {5:F0} base {6:F0} | walk {7:F2} leg {8:F2} keyed {9} {10} | hY {11:F0} cY {12:F0} yawS {13:F0} | move {14:F2} gnd {15} | {16} fps {17:F0}",
                    fighter.name, diagHipA, diagChestA, DiagYaw(upperLegL, upperLegR, ft), DiagYaw(upperArmL, upperArmR, ft), transform.localEulerAngles.y, baseLocalRot.eulerAngles.y,
                    walkS, wLeg, keyed, weapon != null ? weapon.Phase.ToString() : "-", hYaw, cYaw, yawS, fighter.MoveAxis, fighter.Grounded, st, 1f / Mathf.Max(1e-4f, Time.smoothDeltaTime));
            }
            if (deadK > 0.5f) { DropSword(); return; }

            // зоны попадания едут за моделью (присед, выпад), иначе клинок бьёт по воздуху рядом с видимым телом
            FollowZones(ft, pf);

            // ---------- 6) руки держат рукоять
            Vector3 axis = bladeRoot.forward;
            Vector3 back = -axis;
            Vector3 outward = ft.right * 1.0f + Vector3.down * 0.5f + back * 0.2f;   // локти разведены в стороны, а не прижаты к корпусу
            Vector3 gripR = bladeRoot.position + back * gripFront;
            Vector3 gripL = bladeRoot.position + back * gripRear;
            Vector3 fR, nR, fL, nL;
            GripFrame(axis, sideR, ft, out fR, out nR);
            GripFrame(axis, sideL, ft, out fL, out nL);
            // кисть не ломается поперёк предплечья: пальцы отклоняются от направления руки (плечо → хват) не больше maxWristBend
            if (upperArmR != null) LimitWrist(gripR - upperArmR.position, maxWristBend, ref fR, ref nR);
            if (upperArmL != null) LimitWrist(gripL - upperArmL.position, maxWristBend, ref fL, ref nL);
            SolveArm(upperArmR, forearmR, handR, l1R, l2R, gripR - fR * palmOffset - nR * palmLift, outward);
            Vector3 outwardL = -ft.right * 1.0f + Vector3.down * 0.5f + back * 0.2f;
            SolveArm(upperArmL, forearmL, handL, l1L, l2L, gripL - fL * palmOffset - nL * palmLift, outwardL);
            if (framesReady)
            {
                OrientHand(handR, bindR, fR, nR);
                OrientHand(handL, bindL, fL, nL);
            }
        }

        /// <summary>
        /// Ноги: каждая стопа стоит на своём месте (мировые координаты) и переступает, когда боец ушёл/повернулся (походка),
        /// а во время удара следует клипу (шаг вперёд и назад). Таз опускается ровно настолько, чтобы обе ноги доставали,
        /// колено смотрит по носку, подошва параллельна земле, у задней ноги пятка может подниматься, стопа в шаге отрывается от земли.
        /// </summary>
        void SolveLegs(Transform ft, float ys, float hYaw, float ff, float rf, float heel, float w, bool keyed, float dt)
        {
            Vector3 up = ft.up, right = ft.right;
            bool frontIsR = footFrontX * ys > 0f;
            Transform fU = frontIsR ? upperLegR : upperLegL, fL_ = frontIsR ? lowerLegR : lowerLegL, fF = frontIsR ? footR : footL;
            Transform rU = frontIsR ? upperLegL : upperLegR, rL_ = frontIsR ? lowerLegL : lowerLegR, rF = frontIsR ? footL : footR;
            float f1 = frontIsR ? lgR1 : lgL1, f2 = frontIsR ? lgR2 : lgL2, r1 = frontIsR ? lgL1 : lgR1, r2 = frontIsR ? lgL2 : lgR2;
            Quaternion fRel = frontIsR ? footRelR : footRelL, rRel = frontIsR ? footRelL : footRelR;

            // позы Animator — для смешивания при прыжке/приземлении
            Quaternion a0 = fU.localRotation, a1 = fL_.localRotation, a2 = fF != null ? fF.localRotation : Quaternion.identity;
            Quaternion b0 = rU.localRotation, b1 = rL_.localRotation, b2 = rF != null ? rF.localRotation : Quaternion.identity;

            // места стоп по стойке и клипу удара
            float ground = ft.position.y, ftYaw = ft.eulerAngles.y;
            float sx = Mathf.Lerp(1f, walkStanceX, moveK), sz = Mathf.Lerp(1f, walkStanceZ, moveK);
            Vector3 tF = ft.position + ft.rotation * new Vector3(footFrontX * ys * sx, 0f, footFrontZ * sz + ff); tF.y = ground;
            Vector3 tR = ft.position + ft.rotation * new Vector3(footRearX * ys * sx, 0f, footRearZ * sz + rf); tR.y = ground;
            float yF = ftYaw + footFrontYaw * ys, yR = ftYaw + footRearYaw * ys;

            // походка: стопы стоят, пока не «отстали» от своих мест, затем переступают по очереди
            Vector3 vel = dt > 1e-5f ? (ft.position - lastFtPos) / dt : Vector3.zero; vel.y = 0f;
            if (vel.sqrMagnitude > 25f) vel = Vector3.zero;   // телепорт (новый раунд, супер-удар)
            lastFtPos = ft.position;
            if (!gaitInit) { gF = new Foot { pos = tF, yaw = yF }; gR = new Foot { pos = tR, yaw = yR }; gaitInit = true; }
            keyW = Mathf.MoveTowards(keyW, keyed ? 1f : 0f, dt / 0.12f);
            if (keyW > 0.001f)
            {
                // удар: стопы ведёт клип, походка садится в те же точки (без рывка в начале и в конце удара)
                Settle(ref gF, tF, yF, keyW); Settle(ref gR, tR, yR, keyW);
            }
            else Gait(tF, yF, tR, yR, vel, dt);

            float sF = weapon != null ? weapon.FootSpeedF : 0f;
            float keyLift = stepLift * Mathf.Clamp01(Mathf.Abs(sF) / 2.5f) * keyW;
            Vector3 frontPos = gF.pos + up * (footRestH + gF.lift + keyLift);
            Vector3 rearPos = gR.pos + up * (footRestH + gR.lift + heel * heelLift);
            float relF = Mathf.DeltaAngle(ftYaw, gF.yaw), relR = Mathf.DeltaAngle(ftYaw, gR.yaw);
            // в шаге таз чуть проседает и снова поднимается
            float bob = 0.015f * (Mathf.Sin(Mathf.PI * Mathf.Clamp01(gF.step ? gF.u : 0f)) + Mathf.Sin(Mathf.PI * Mathf.Clamp01(gR.step ? gR.u : 0f)));
            transform.position -= up * (bob * w);

            // таз ниже, если иначе нога не дотягивается (широкий шаг, вынос таза)
            Rot(fU, -hYaw, up); Rot(rU, -hYaw, up);   // бёдра не скручиваются вместе с тазом
            float need = 0f;
            need = Mathf.Max(need, DropNeeded(fU.position, frontPos, (f1 + f2) * 0.985f, up));
            need = Mathf.Max(need, DropNeeded(rU.position, rearPos, (r1 + r2) * 0.985f, up));
            if (need > 0f) transform.position -= up * (Mathf.Min(need, maxAutoDrop) * w);

            Vector3 poleF = Quaternion.AngleAxis(relF, up) * ft.forward + up * 0.1f;
            Vector3 poleR = Quaternion.AngleAxis(relR, up) * ft.forward + up * 0.1f;
            SolveArm(fU, fL_, fF, f1, f2, frontPos, poleF);
            SolveArm(rU, rL_, rF, r1, r2, rearPos, poleR);
            float liftKF = stepLift > 0f ? (gF.lift + keyLift) / stepLift : 0f, liftKR = stepLift > 0f ? gR.lift / stepLift : 0f;
            if (fF != null) fF.rotation = Quaternion.AngleAxis(relF, up) * Quaternion.AngleAxis(-12f * liftKF, right) * ft.rotation * fRel;   // в шаге носок чуть вверх
            if (rF != null) rF.rotation = Quaternion.AngleAxis(relR, up) * Quaternion.AngleAxis(28f * heel - 12f * liftKR, right) * ft.rotation * rRel;    // пятка поднята — стопа на носке

            if (w < 0.999f)
            {
                fU.localRotation = Quaternion.Slerp(a0, fU.localRotation, w); fL_.localRotation = Quaternion.Slerp(a1, fL_.localRotation, w);
                rU.localRotation = Quaternion.Slerp(b0, rU.localRotation, w); rL_.localRotation = Quaternion.Slerp(b1, rL_.localRotation, w);
                if (fF != null) fF.localRotation = Quaternion.Slerp(a2, fF.localRotation, w);
                if (rF != null) rF.localRotation = Quaternion.Slerp(b2, rF.localRotation, w);
            }
        }

        static void Settle(ref Foot f, Vector3 t, float y, float k)
        {
            f.pos = Vector3.Lerp(f.pos, t, k); f.yaw = Mathf.LerpAngle(f.yaw, y, k); f.step = false; f.lift = 0f;
        }

        static float FootErr(Foot f, Vector3 t, float y)
        {
            Vector3 d = f.pos - t; d.y = 0f;
            return d.magnitude + Mathf.Abs(Mathf.DeltaAngle(f.yaw, y)) / 120f;
        }

        void Gait(Vector3 tF, float yF, Vector3 tR, float yR, Vector3 vel, float dt)
        {
            Advance(ref gF, dt); Advance(ref gR, dt);
            float eF = FootErr(gF, tF, yF), eR = FootErr(gR, tR, yR);
            if (eF > 1.2f || eR > 1.2f) { gF = new Foot { pos = tF, yaw = yF }; gR = new Foot { pos = tR, yaw = yR }; return; }   // телепорт / новый раунд: встать сразу
            float thr = vel.sqrMagnitude > 0.04f ? stepThreshold + stepThresholdPerSpeed * vel.magnitude : stepThreshold * 0.6f;
            const float urgent = 0.25f;   // обычно ноги переступают по очереди; сильно отставшая — шагает, не дожидаясь другой
            // вбок первой шагает нога со стороны движения — ноги не перекрещиваются
            Transform ftr = fighter.transform;
            Vector3 vl = ftr.InverseTransformDirection(vel);
            if (Mathf.Abs(vl.x) > 0.2f)
            {
                float fx = ftr.InverseTransformPoint(gF.pos).x, rx = ftr.InverseTransformPoint(gR.pos).x;
                if ((fx > rx) == (vl.x > 0f)) eF += 0.05f; else eR += 0.05f;
            }
            bool canF = !gF.step && eF > thr && (!gR.step || eF > urgent);
            bool canR = !gR.step && eR > thr && (!gF.step || eR > urgent);
            if (canF && (!canR || eF >= eR)) Begin(ref gF, tF, yF, vel);
            else if (canR) Begin(ref gR, tR, yR, vel);
        }

        void Begin(ref Foot f, Vector3 t, float y, Vector3 vel)
        {
            float dist = (t - f.pos).magnitude;
            f.dur = Mathf.Clamp(stepTimeMin + dist * 0.15f, stepTimeMin, stepTimeMax);
            Transform ftr = fighter.transform;
            Vector3 vl = ftr.InverseTransformDirection(vel);
            f.from = f.pos; f.to = t + ftr.TransformDirection(new Vector3(vl.x * 0.9f, 0f, vl.z * 1.5f)) * f.dur; f.to.y = t.y;   // стопа встаёт с запасом вперёд по ходу — до следующего шага она «в своём месте»
            f.yawFrom = f.yaw; f.yawTo = y; f.u = 0f; f.step = true;
        }

        void Advance(ref Foot f, float dt)
        {
            if (!f.step) return;
            f.u += dt / Mathf.Max(0.05f, f.dur);
            float u = Mathf.Clamp01(f.u), s = Smooth(u);
            f.pos = Vector3.Lerp(f.from, f.to, s);
            f.yaw = Mathf.LerpAngle(f.yawFrom, f.yawTo, s);
            f.lift = stepLift * Mathf.Sin(Mathf.PI * u) * Mathf.Clamp01((f.to - f.from).magnitude / 0.25f);
            if (f.u >= 1f) { f.step = false; f.lift = 0f; }
        }

        /// <summary>На сколько опустить бедро, чтобы нога длины maxLen достала до точки target.</summary>
        static float DropNeeded(Vector3 hip, Vector3 target, float maxLen, Vector3 up)
        {
            Vector3 v = target - hip;
            float vert = -Vector3.Dot(v, up);
            float hor = (v + up * vert).magnitude;
            if (v.magnitude <= maxLen) return 0f;
            float h = Mathf.Sqrt(Mathf.Max(0f, maxLen * maxLen - hor * hor));
            return Mathf.Max(0f, vert - h);
        }

        /// <summary>Диагностика на экране (временно): поворот таза/груди относительно бойца до и после драйвера, состояние.</summary>
        public static bool Diag = false;
        string diag = "";
        static float DiagYaw(Transform l, Transform r, Transform ft)
        {
            if (l == null || r == null) return 0f;
            Vector3 d = ft.InverseTransformDirection(r.position - l.position); d.y = 0f;   // линия «левое → правое» должна идти по +X бойца
            return Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
        }
        void OnGUI()
        {
            if (!Diag || string.IsNullOrEmpty(diag)) return;
            float y = Screen.height * (weapon != null && weapon.yawSign < 0 ? 0.30f : 0.26f);
            GUIStyle g = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 45) };
            g.normal.textColor = Color.black; GUI.Label(new Rect(12, y + 2, Screen.width, 60), diag, g);
            g.normal.textColor = Color.yellow; GUI.Label(new Rect(10, y, Screen.width, 60), diag, g);
        }

        Vector3[] zoneBase;
        void FollowZones(Transform ft, float lunge)
        {
            if (fighter.zones == null) return;
            if (zoneBase == null)
            {
                zoneBase = new Vector3[fighter.zones.Length];
                for (int i = 0; i < zoneBase.Length; i++) zoneBase[i] = fighter.zones[i] != null ? fighter.zones[i].transform.localPosition : Vector3.zero;
            }
            float sink = deadK > 0.01f ? 0f : Mathf.Max(0f, (ft.position.y + baseLocalPos.y) - transform.position.y);
            if (deadK > 0.01f) lunge = 0f;
            for (int i = 0; i < zoneBase.Length; i++)
            {
                BodyZone z = fighter.zones[i];
                if (z == null) continue;
                bool leg = z.zoneId == BodyZoneId.LegLeft || z.zoneId == BodyZoneId.LegRight;
                z.transform.localPosition = zoneBase[i] + new Vector3(0f, -sink * (leg ? 0.5f : 1f), lunge);
            }
            Physics.SyncTransforms();
        }

        /// <summary>Повержен: меч выпадает из рук и ложится на землю рядом.</summary>
        void DropSword()
        {
            if (weapon == null || weapon.pivot == null) return;
            float k = Smooth(Mathf.InverseLerp(0.5f, 1f, deadK));
            Transform pv = weapon.pivot;
            Vector3 lie = new Vector3(0.40f * weapon.yawSign, 0.04f, 0.45f);
            pv.localPosition = Vector3.Lerp(pv.localPosition, lie, k);
            pv.localRotation = Quaternion.Slerp(pv.localRotation, Quaternion.Euler(0f, 70f * weapon.yawSign, 0f), k);
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

        /// <summary>Ограничивает излом запястья: направление пальцев не дальше maxDeg от направления руки; ладонь пересчитывается под него.</summary>
        static void LimitWrist(Vector3 armDir, float maxDeg, ref Vector3 fingers, ref Vector3 palm)
        {
            if (armDir.sqrMagnitude < 1e-6f || fingers.sqrMagnitude < 1e-6f) return;
            armDir.Normalize();
            if (Vector3.Angle(armDir, fingers) <= maxDeg) return;
            fingers = Vector3.RotateTowards(armDir, fingers, maxDeg * Mathf.Deg2Rad, 0f).normalized;
            Vector3 n = Vector3.ProjectOnPlane(palm, fingers);
            if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(fingers, Vector3.up);
            palm = n.normalized;
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
