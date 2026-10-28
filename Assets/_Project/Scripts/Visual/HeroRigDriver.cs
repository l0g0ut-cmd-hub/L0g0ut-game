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
        [Tooltip("Максимальный излом кисти относительно руки, град. (иначе хват поперёк клинка ломает запястье при вытянутых вперёд руках).")]
        public float maxWristBend = 30f;

        [Header("Тело в ударе")]
        [Tooltip("Доли поворота клинка (yaw, до bodyYawMax профиля, по умолчанию ±45°), которые отрабатывают таз / поясница / грудь.")]
        public float hipsYawK = 0.15f, spineYawK = 0.20f, chestYawK = 0.25f;
        public float leanBack = -12f;     // наклон корпуса на замахе, град.
        public float leanForward = 14f;   // и в конце удара
        public float lungeForward = 0.16f; // выпад вперёд на ударе, м
        public float lungeBack = 0.05f;    // откат назад на замахе, м
        [Tooltip("Глубина приседа на низких ударах: угол бедра, град. (колено ≈ ×1.8).")]
        public float crouchThigh = 66f;

        [Header("Стойка")]
        [Tooltip("Разнос ног в боевой стойке, град.: передняя нога (на стороне передней руки) вперёд, задняя назад. На ходьбе гасится.")]
        public float stanceStagger = 36f;
        [Tooltip("Глубина боевой стойки (колени согнуты, таз ниже), 0…1; на ходьбе гасится.")]
        [Range(0f, 1f)] public float stanceCrouch = 0.05f;
        public float stanceSpread = 10f;   // ноги разведены в стороны (широкая стойка из референса), град.
        public float stFrontKnee = 0.9f, stRearHip = 0.75f, stRearKnee = 0.05f;   // доли угла шага для колена/бедра

        [Header("Отдача при попадании")]
        public float hitKickDecay = 2.8f;  // 1/с

        float l1L, l2L, l1R, l2R;
        float lean, yawS, yawV, bS, bV, crouchS, crouchV, kick;
        float pivotRestY, walkS, deadK, footRestH;
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
        float sideL = 1f, sideR = -1f;
        bool framesReady;
        CombatResolver resolver;

        void Start()
        {
            Cache(upperArmL, forearmL, handL, out l1L, out l2L);
            Cache(upperArmR, forearmR, handR, out l1R, out l2R);
            baseLocalPos = transform.localPosition;
            baseLocalRot = transform.localRotation;
            if (footL != null && footR != null && fighter != null) footRestH = Mathf.Min(footL.position.y, footR.position.y) - fighter.transform.position.y;
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
            // реакция зависит от зоны: голова запрокидывается, ноги подламываются, рука/корпус уводят плечи вбок
            kickZone = r.zone;
            Vector3 v = r.contact.bladeVelocity;
            kickSide = fighter != null && v.sqrMagnitude > 1e-4f ? Mathf.Sign(Vector3.Dot(v, fighter.transform.right)) : 0f;
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }

        void LateUpdate()
        {
            if (fighter == null || bladeRoot == null) return;
            float dt = Time.deltaTime;
            if (decapitated && fighter != null && !fighter.Dead) decapitated = false;   // новый раунд
            if (head != null) head.localScale = decapitated ? Vector3.one * 0.001f : Vector3.one;
            Transform ft = fighter.transform;
            Vector3 right = ft.right, up = ft.up;
            float sc = transform.lossyScale.y;

            if (weapon != null && weapon.pivot != null) weapon.pivot.localPosition = weapon.PureLocalPosition;   // снять прошлую поправку досягаемости

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
                    Vector3 e = weapon.CurrentEuler;                          // не localEulerAngles: за ±90° по pitch Unity переворачивает yaw/roll
                    float pitch = e.x;                                        // −165 … +85
                    float maxYaw = weapon.CurrentProfile != null ? weapon.CurrentProfile.bodyYawMax : 45f;
                    yawTarget = Mathf.Clamp(e.y, -maxYaw, maxYaw) * (weapon.CurrentProfile != null ? weapon.CurrentProfile.twistMul : 1f);
                    pitchTarget = Mathf.Lerp(leanBack, leanForward, Mathf.InverseLerp(-120f, 60f, pitch));
                }
                crouchTarget = Mathf.Clamp01((weapon.restHand.y - weapon.HandOffset.y) / 0.45f);
            }
            if (fighter.Dashing) { crouchTarget = Mathf.Max(crouchTarget, 0.35f); pitchTarget = Mathf.Max(pitchTarget, 10f); }
            if (kick > 0.05f && kickZone != BodyZoneId.Head && kickZone != BodyZoneId.Torso && kickZone != BodyZoneId.ArmLeft && kickZone != BodyZoneId.ArmRight)
                crouchTarget = Mathf.Max(crouchTarget, 0.55f * kick);   // удар по ногам — колени подламываются
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
            float riseK = prof != null ? Mathf.Max(0f, -bS) * prof.windupRise : 0f;   // замах дзё-дан: ноги сходятся, тело вытягивается
            float cr = Mathf.Clamp01(crouchS + stanceCrouch * (1f - walkS) * (1f - riseK) + (prof != null ? prof.crouchBoost * strikeK : 0f));
            float stride = (1f - walkS) * stanceStagger * (1f - riseK) + (prof != null ? prof.strideDeg * strikeK * (1f - walkS) : 0f);

            // ---------- 2) дыхание и покачивание в стойке (глушится во время удара)
            float calm = swinging ? 0f : 1f;
            float time = Time.time;
            float swayYaw = Mathf.Sin(time * 0.8f) * 2.0f * calm;
            float swayPitch = Mathf.Sin(time * 1.1f + 1f) * 1.2f * calm;
            float headYaw = Mathf.Sin(time * 0.55f + 2f) * 3.0f * calm;

            // ---------- 3) смещение всей модели: выпад / откат / присед / отдача (визуально; зоны поражения не двигаются)
            float lunge = (weapon != null ? weapon.VisualLunge : 0f) - kick * 0.10f;   // руки с клинком едут вместе с телом (WeaponController.VisualLunge)
            Vector3 off = new Vector3(0f, -Mathf.Max(0f, bS) * 0.02f, lunge);   // высоту (присед) задаёт посадка стоп на землю ниже
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

            // ---------- 4) повороты костей сверху вниз (родитель раньше ребёнка)
            Rot(hips, yawS * hipsYawK + swayYaw * 0.4f, up);
            Rot(spine, yawS * spineYawK + swayYaw * 0.3f, up);
            Rot(chest, yawS * chestYawK + swayYaw * 0.3f, up);
            float pitchTotal = lean * 0.5f - kick * 8f + swayPitch * 0.5f;
            Rot(spine, pitchTotal, right);
            Rot(chest, lean * 0.5f - kick * 12f + swayPitch * 0.5f, right);
            Rot(head, headYaw - yawS * 0.25f, up);              // голова частично «смотрит» на противника, а не вслед за клинком
            Rot(head, -pitchTotal * 0.5f + kick * 6f, right);
            if (kick > 0.01f)
            {
                Vector3 fw = ft.forward;
                float k2 = kick * kick;
                if (kickZone == BodyZoneId.Head) { Rot(head, -k2 * 22f, right); Rot(head, kickSide * k2 * 14f, up); }
                Rot(chest, -kickSide * k2 * (kickZone == BodyZoneId.ArmLeft || kickZone == BodyZoneId.ArmRight ? 16f : 9f), fw);
                Rot(spine, kickSide * k2 * 6f, up);
            }

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
                // передняя: бедро вперёд, голень почти вертикально, стопа плашмя; задняя: прямая нога назад, стопа плашмя
                Rot(upperLegR, -st, right); Rot(lowerLegR, st * stFrontKnee, right); Rot(footR, st * (1f - stFrontKnee), right);
                Rot(upperLegL, st * stRearHip, right); Rot(lowerLegL, st * stRearKnee, right); Rot(footL, -st * (stRearHip + stRearKnee), right);
            }

            // ---------- 4b+) широкая стойка: бёдра разведены в стороны, стопы остаются параллельно земле
            float spread = stanceSpread * (1f - walkS) * (1f - riseK);
            if (spread > 0.01f)
            {
                Vector3 fw2 = ft.forward;
                Rot(upperLegL, -spread, fw2); Rot(footL, spread, fw2);
                Rot(upperLegR, spread, fw2); Rot(footR, -spread, fw2);
            }

            // ---------- 4b') стопы стоят на земле: глубина приседа и ширина шага сами решают, насколько опустить таз
            if (fighter.Grounded && deadK < 0.01f && footL != null && footR != null)
            {
                float lowest = Mathf.Min(footL.position.y, footR.position.y);
                transform.position += Vector3.up * ((ft.position.y + footRestH) - lowest);
            }

            // ---------- 4b'') руки ставятся относительно плеч в системе корпуса: корпус присел / скрутился / выпал вперёд — руки с клинком идут за ним
            if (weapon != null && weapon.pivot != null && upperArmR != null && upperArmL != null)
            {
                Vector3 shMid = (upperArmR.position + upperArmL.position) * 0.5f;
                float twist = yawS * (hipsYawK + spineYawK + chestYawK);
                Quaternion frame = Quaternion.AngleAxis(twist, up) * ft.rotation;
                weapon.pivot.position = shMid + frame * weapon.HandOffset;
            }

            // ---------- 4c) досягаемость: если рукоять дальше, чем достают руки (у модели они короткие), меч подтягивается к плечам —
            // кисти всегда остаются на рукояти, а не повисают в воздухе (раньше именно это выглядело как «перелом кисти»)
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

            if (deadK > 0.5f) { DropSword(); return; }

            // ---------- 4d) зоны попадания едут за моделью (присед, выпад), иначе клинок бьёт по воздуху рядом с видимым телом
            FollowZones(ft, lunge);

            // ---------- 5) руки держат рукоять
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
            SolveArm(upperArmR, forearmR, handR, l1R, l2R, gripR - fR * palmOffset - nR * 0.015f, outward);
            Vector3 outwardL = -ft.right * 1.0f + Vector3.down * 0.5f + back * 0.2f;
            SolveArm(upperArmL, forearmL, handL, l1L, l2L, gripL - fL * palmOffset - nL * 0.015f, outwardL);
            if (framesReady)
            {
                OrientHand(handR, bindR, fR, nR);
                OrientHand(handL, bindL, fL, nL);
            }
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
