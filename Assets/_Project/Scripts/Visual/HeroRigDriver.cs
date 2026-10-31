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
        [Tooltip("Точный хват: центр кулака задан в системе кости кисти (по позе покоя: вниз от запястья gripDown, к ладони gripIn, м). " +
                 "Тогда рукоять проходит ровно через кулак при любом повороте кисти, без подгонки palmOffset/palmLift.")]
        public bool gripFromBind;
        [Tooltip("Вынос рук вперёд/вниз сверх позы клинка (м, в системе бойца): у модели с короткими руками и узкими плечами стойка иначе «прижата к себе». В ударе — вполовину.")]
        public float handForward = 0f, handLift = 0f;
        float handBiasK = 1f;
        public float gripDown = 0.111f, gripIn = 0.017f;
        Vector3 gripLocalL, gripLocalR;
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
        public float footFrontX = -0.12f, footFrontZ = 0.38f, footRearX = 0.26f, footRearZ = -0.36f;   // стопы ~0,75 м по ходу: широкая стойка, но не выпад
        [Tooltip("Разворот стоп в стойке, град. (+ носок вправо), для игрока; у врага зеркалится. Задняя развёрнута наружу — открытая стойка.")]
        public float footFrontYaw = -10f, footRearYaw = 75f;

        [Header("Походка: стопы переступают сами (IK), без клипа ходьбы")]
        public float stepThreshold = 0.08f;   // стопа ушла от своего места стойки дальше — шаг
        public float stepTimeMin = 0.18f, stepTimeMax = 0.26f;
        public float stepThresholdPerSpeed = 0.06f;
        [Header("Ходьба: цикл шага (ноги переносятся по очереди и обгоняют друг друга), остановка — шагами в стойку")]
        [Tooltip("Темп, шагов в секунду = base + perSpeed·скорость + lateral·|скорость вбок| (приставной шаг вбок чаще и короче).")]
        public float cadenceBase = 1.5f, cadencePerSpeed = 0.8f, cadenceLateral = 1.4f;
        [Tooltip("Доля шага, когда стопа в воздухе (остальное — опора на обе ноги).")]
        public float swingFrac = 0.8f;
        [Tooltip("На ходу стопы идут по ±walkHalfWidth от оси бойца; вбок — приставным шагом, стопы не сходятся ближе walkMinSep.")]
        public float walkHalfWidth = 0.11f, walkMinSep = 0.12f;
        [Tooltip("Стопа ставится на walkLead длины шага впереди таза (по ходу движения).")]
        public float walkLead = 0.6f;
        public float walkLift = 0.08f;      // подъём стопы на ходу, м
        public float walkCrouch = 0.015f;   // на ходу колени чуть мягче (меньше подбрасывает таз), м
        public float walkFootOut = 8f;      // носки на ходу чуть наружу, град.
        public float walkBob = 0.012f;      // таз поднимается, пока несёт одна нога, м (тихо: боец идёт собранно, а не враскачку)
        public float walkSway = 0.012f;     // таз смещается на опорную ногу, м
        public float walkHipYaw = 5f, walkChestYaw = 6f;   // таз поворачивается за вынесенной ногой, грудь — навстречу, град. на метр разноса стоп
        [Tooltip("Перед отрывом пятка поднимается (перекат на носок): доля цикла и угол.")]
        public float heelPeel = 0.15f, heelPeelDeg = 25f;
        public float maxAutoDrop = 0.12f;
        public float stanceDrop = 0.03f;
        [Tooltip("Мокап-ходьба в бою: насколько таз ниже, чем в клипе (м) — колени согнуты, шаг «в стойке».")]
        public float mocapCrouch = 0.06f;   // колени мягко согнуты в стойке, м
        public float stepLift = 0.07f;     // подъём стопы в шаге, м
        public float heelLift = 0.05f;     // подъём пятки задней ноги (вес на носке), м
        public float postureBack = 6f;     // осанка: грудь назад/вверх, голова поднята (против сутулости модели), град.
        [Range(0f, 1f)] public float headStabilize = 0.75f;   // голова держит горизонт и смотрит на врага, а не болтается с корпусом

        [Header("Мокап-ходьба (Animator: состояние Loco — клипы CMU, запечённые под эту модель, MocapBaker)")]
        public bool mocapLoco;
        public Vector2[] locoDirs;     // направление клипа в системе бойца (x — вправо, y — вперёд)
        public float[] locoSpeeds;     // скорость клипа, м/с (темп в игре = скорость бойца / скорость клипа)

        [Header("Смерть")]
        [Tooltip("Физическое падение (ragdoll) по направлению смертельного удара; без него — прежнее падение навзничь.")]
        public bool useRagdoll = true;
        FighterRagdoll rag;
        Vector3 lastHitDir, lastHitPoint;

        [Header("Отдача при попадании")]
        public float hitKickDecay = 2.8f;  // 1/с

        float l1L, l2L, l1R, l2R;
        float lgL1, lgL2, lgR1, lgR2;
        float lean, yawS, yawV, bS, bV, crouchS, crouchV, kick, kickV;
        [Tooltip("Отдача от удара — пружина: жёсткость (1/с) и затухание. Корпус отлетает, проскакивает чуть вперёд и встаёт.")]
        public float hitStiffness = 15f, hitDamping = 0.45f;
        float pivotRestY, walkS, deadK, footRestH, wLegS;
        // точки подошвы (пятка, подушечка, носок) в системе кости стопы — по позе покоя, где подошва стоит на земле
        Vector3[] solL, solR;
        // фиксация опорной стопы на мокап-ходьбе: точка подошвы, вставшая на землю, держится на месте (поворот к противнику,
        // смесь клипов, неточный темп — стопа не едет), IK ноги дотягивает голеностоп; на отрыве поправка тает
        struct FootLock { public bool on, hasPrev; public int idx; public Vector3 pos, off, prev0, prev1, prev2; public float lift; }
        // темп мокапа под настоящую скорость: снос опорной стопы клипа вдоль хода (м/с) → множитель темпа
        float driftAcc, tempoMul = 1f; int driftN;
        // прыжок: ноги поджаты в воздухе; приземление — короткое проседание
        float airK, landK; bool wasGrounded = true;
        float poolAt = -1f;
        FootLock lockL, lockR;
        /// <summary>Нижняя точка подошвы (мир) — после постановки на землю в этом кадре (для пыли/шагов и проб).</summary>
        public Vector3 SoleL { get; private set; }
        public Vector3 SoleR { get; private set; }
        struct Foot
        {
            public Vector3 pos, from, to;
            public float yaw, yawFrom, yawTo, u, dur, lift;
            public float pit, p0, land, side;   // тангаж стопы (+ пятка вверх), с какого тангажа начат шаг, сколько стоит после шага, сторона (+1 правая)
            public bool step, walkStep, pend;
        }
        Foot gF, gR;
        bool gaitInit, walking;
        float keyW, moveK, postK, walkPhase;
        Vector3 walkVel, walkVelS, tF, tR, wOffF, wOffR;
        float rollK = 1f, hipsYS;   // перекат стопы по ходу; сглаженная высота таза (руки гасят качание)
        float yF, yR;
        Vector3 lastFtPos;
        float locoW, prevWLeg = 1f;
        bool locoMoving, gaitFromBones, footUpL = true, footUpR = true;
        Vector3 lastPosU, velUS;
        /// <summary>Шаг времени для диагностики в редакторе (PoseProbe: в edit mode Time.deltaTime = 0). Меньше нуля — обычный Time.deltaTime.</summary>
        public static float DtOverride = -1f;

        /// <summary>Драйвер активного облика бойца (у врага их несколько, выключенные пропускаются); если активных нет — первый любой.</summary>
        public static HeroRigDriver Of(Component c)
        {
            if (c == null) return null;
            HeroRigDriver any = null;
            foreach (HeroRigDriver d in c.GetComponentsInChildren<HeroRigDriver>(true))
            {
                if (d.gameObject.activeInHierarchy) return d;
                if (any == null) any = d;
            }
            return any;
        }
        static float Dt { get { return DtOverride >= 0f ? DtOverride : Time.deltaTime; } }
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
                float sc = hips != null ? Mathf.Clamp((hips.position.y - ft.position.y) / 0.92f, 0.5f, 2f) : 1f;
                solL = SolePoints(footL, ft, sc); solR = SolePoints(footR, ft, sc);
                // поза покоя модели: подошвы стоят на земле — запоминаем, как стопа повёрнута относительно бойца
                footRelL = Quaternion.Inverse(ft.rotation) * footL.rotation;
                footRelR = Quaternion.Inverse(ft.rotation) * footR.rotation;
                if (head != null) headRel = Quaternion.Inverse(ft.rotation) * head.rotation;
                legsReady = upperLegL != null && lowerLegL != null && upperLegR != null && lowerLegR != null;
            }
            if (weapon != null && weapon.pivot != null) pivotRestY = weapon.pivot.localPosition.y;
            CacheHandFrame(handL, forearmL, out bindL, out sideL);
            if (gripFromBind && fighter != null)
            {
                // в A-позе кисти висят пальцами вниз, ладонь к бедру: центр кулака — вниз от запястья и чуть внутрь
                Transform ftr = fighter.transform;
                if (handL != null) gripLocalL = handL.InverseTransformPoint(handL.position + Vector3.down * gripDown - sideL * ftr.right * gripIn);
                if (handR != null) gripLocalR = handR.InverseTransformPoint(handR.position + Vector3.down * gripDown - sideR * ftr.right * gripIn);
            }
            CacheHandFrame(handR, forearmR, out bindR, out sideR);
            if (useRagdoll && legsReady && fighter != null)
            {
                rag = new FighterRagdoll(this, fighter.transform);
                if (!rag.Ready) rag = null;
            }
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
            animator.SetFloat("Move", 0f);   // старый клип WalkF не нужен: ходьба — мокап (Loco) или шаги стоп (Gait)
            animator.SetBool("Grounded", fighter.Grounded);
            if (mocapLoco)
            {
                // мокап-ходьба: направление хода в системе бойца и темп под настоящую скорость (стопы не скользят)
                float dt = Dt;
                Transform ft = fighter.transform;
                Vector3 v = dt > 1e-5f ? (ft.position - lastPosU) / dt : Vector3.zero; v.y = 0f;
                lastPosU = ft.position;
                if (v.sqrMagnitude > 25f) v = Vector3.zero;
                velUS = Vector3.Lerp(velUS, v, 1f - Mathf.Exp(-dt / 0.08f));
                float spd = velUS.magnitude;
                bool keyed = weapon != null && weapon.Keyed;
                locoMoving = Mathf.Abs(fighter.MoveAxis) > 0.1f && !keyed && fighter.Grounded && !fighter.Dead && spd > 0.15f;
                animator.SetBool("Moving", locoMoving);
                if (spd > 0.05f)
                {
                    Vector3 lv = ft.InverseTransformDirection(velUS / spd);
                    animator.SetFloat("MoveX", lv.x, 0.1f, dt);
                    animator.SetFloat("MoveZ", lv.z, 0.1f, dt);
                    // подстройка темпа: опорная стопа клипа едет по земле вперёд по ходу — клип отстаёт (темп выше), назад — спешит
                    if (driftN > 0 && locoMoving)
                    {
                        float drift = driftAcc / driftN;
                        tempoMul = Mathf.Clamp(tempoMul + 0.9f * drift / Mathf.Max(0.35f, spd) * dt, 0.8f, 1.3f);
                    }
                    else if (!locoMoving) tempoMul = Mathf.MoveTowards(tempoMul, 1f, 0.3f * dt);
                    driftAcc = 0f; driftN = 0;
                    animator.SetFloat("LocoSpeed", Mathf.Clamp(spd / ExpectedLocoSpeed(new Vector2(lv.x, lv.z)) * tempoMul, 0.6f, 1.8f));
                }
            }
        }

        /// <summary>Скорость смеси клипов для направления d (веса как у 2D-смеси: ближние по направлению клипы).</summary>
        static Vector3[] SolePoints(Transform foot, Transform ft, float sc)
        {
            Vector3 a = foot.position, g = new Vector3(a.x, ft.position.y, a.z);
            Vector3 f = ft.forward; f.y = 0f; f.Normalize();
            return new Vector3[] { foot.InverseTransformPoint(g - f * (0.05f * sc)), foot.InverseTransformPoint(g + f * (0.11f * sc)), foot.InverseTransformPoint(g + f * (0.16f * sc)) };
        }

        // кончик носка может уйти в землю на 2 см: пальцы гнутся, а кость стопы одна
        // Замок опоры по замеру CI (покадровый след): клип касается земли пяткой ещё на ходу (до 3,7 м/с) и доезжает 15–18 см —
        // ранний замок держал стопу позади клипа всю опору, а в конце рвался скачком. Теперь:
        //  • замок — только когда точка подошвы в клипе почти остановилась (< 1,2 м/с); пока она скользит у земли быстро —
        //    стопа идёт чуть над землёй (касается, когда остановилась: ставится, а не едет);
        //  • отпускание — когда клип повёл стопу (толчок, > 1,4 м/с) или снос > 15 см; поправка тает за ~0,1 с,
        //    стопа при этом отрывается от земли, а не скользит по камню.
        void LockFoot(ref FootLock lk, Transform up, Transform lo, Transform foot, float l1, float l2, Vector3[] sol, bool use, Transform ft, float dt)
        {
            // нижняя точка подошвы (пятка → подушечка при перекате): держим ту, что сейчас опора
            int j = 0; float y = float.MaxValue;
            for (int i = 0; i < sol.Length; i++) { float py = foot.TransformPoint(sol[i]).y + (i == 2 ? 0.02f : 0f); if (py < y) { y = py; j = i; } }
            Vector3 pj = foot.TransformPoint(sol[j]);
            float h = pj.y - ft.position.y;
            // скорость этой точки в клипе (до IK) по земле: та же точка подошвы прошлого кадра
            Vector3 vel = Vector3.zero;
            if (lk.hasPrev && dt > 1e-5f) { vel = (pj - (j == 0 ? lk.prev0 : j == 1 ? lk.prev1 : lk.prev2)) / dt; vel.y = 0f; }
            float v = vel.magnitude;
            if (sol.Length >= 3) { lk.prev0 = foot.TransformPoint(sol[0]); lk.prev1 = foot.TransformPoint(sol[1]); lk.prev2 = foot.TransformPoint(sol[2]); lk.hasPrev = true; }
            if (!use) lk.on = false;
            else if (!lk.on && h < 0.012f && v < 1.2f) { lk.on = true; lk.idx = j; lk.pos = pj; lk.off = Vector3.zero; }
            else if (lk.on && (h > 0.03f || v > 1.4f)) lk.on = false;
            Vector3 want = Vector3.zero;
            if (lk.on)
            {
                // перекат на другую точку подошвы: новая опора там, где она окажется с текущей поправкой — без скачка
                if (j != lk.idx) { lk.idx = j; lk.pos = pj + lk.off; }
                want = lk.pos - pj; want.y = 0f;
                if (want.sqrMagnitude > 0.15f * 0.15f) { lk.on = false; want = lk.off; }   // ушло слишком далеко — пусть шагает (плавно)
                else if (v < 1.0f && velUS.sqrMagnitude > 0.01f) { driftAcc += Vector3.Dot(vel, velUS.normalized); driftN++; }
            }
            lk.off = lk.on ? want : Vector3.Lerp(lk.off, Vector3.zero, 1f - Mathf.Exp(-dt / 0.07f));
            // подъём над землёй: скользящая у земли стопа (замок не взят) и отпущенная стопа (поправка тает) — не едут по камню
            float lift = 0f;
            if (use && !lk.on)
                lift = Mathf.Max(Mathf.Clamp01((v - 1.0f) / 2f) * 0.045f * Mathf.Clamp01(1f - h / 0.06f), Mathf.Min(0.06f, lk.off.magnitude * 0.5f));
            lk.lift = Mathf.Lerp(lk.lift, lift, 1f - Mathf.Exp(-dt / 0.03f));
            if (lk.off.sqrMagnitude < 1e-8f && lk.lift < 1e-4f) return;
            Quaternion fr = foot.rotation;
            // колено — куда его согнул клип; у почти прямой ноги направление шумное — подталкиваем вперёд (колени смотрят вперёд всегда)
            Vector3 pole = lo.position - (up.position + foot.position) * 0.5f + ft.forward * 0.05f;
            SolveArm(up, lo, foot, l1, l2, foot.position + lk.off + Vector3.up * lk.lift, pole);
            foot.rotation = fr;
        }

        static float SoleY(Transform foot, Vector3[] pts)
        {
            float y = float.MaxValue;
            for (int i = 0; i < pts.Length; i++) y = Mathf.Min(y, foot.TransformPoint(pts[i]).y + (i == 2 ? 0.02f : 0f));
            return y;
        }

        static Vector3 SolePoint(Transform foot, Vector3[] pts)
        {
            Vector3 best = foot.position; float y = float.MaxValue;
            for (int i = 0; i < pts.Length; i++) { Vector3 p = foot.TransformPoint(pts[i]); float py = p.y + (i == 2 ? 0.02f : 0f); if (py < y) { y = py; best = p; } }
            return best;
        }

        float ExpectedLocoSpeed(Vector2 d)
        {
            if (locoDirs == null || locoSpeeds == null || locoDirs.Length == 0) return 1f;
            float ws = 0f, sum = 0f, best = -2f, bestS = 1f;
            for (int i = 0; i < locoDirs.Length && i < locoSpeeds.Length; i++)
            {
                float c = Vector2.Dot(d.normalized, locoDirs[i]);
                if (c > best) { best = c; bestS = locoSpeeds[i]; }
                float w = c > 0f ? c * c : 0f;
                ws += w; sum += w * locoSpeeds[i];
            }
            return Mathf.Max(0.2f, ws > 1e-3f ? sum / ws : bestS);
        }

        void OnHit(HitResult r)
        {
            if (r.target != fighter) return;
            if (animator != null) animator.SetTrigger("Hit");
            // толчок пружины отдачи: в блок — слабый, вскользь — средний, чистый — полный
            float imp = (fighter != null && fighter.Blocking ? 0.35f : 1f) * (r.glancing ? 0.6f : 1f);
            kickV += 26f * imp;   // пик отдачи ≈ 1 через 80 мс, отскок вперёд ≈ 0.2
            // реакция зависит от зоны: голова запрокидывается, ноги подламываются, рука/корпус уводят плечи вбок
            kickZone = r.zone;
            Vector3 v = r.contact.bladeVelocity;
            kickSide = fighter != null && v.sqrMagnitude > 1e-4f ? Mathf.Sign(Vector3.Dot(v, fighter.transform.right)) : 0f;
            lastHitDir = v.sqrMagnitude > 1e-4f ? v.normalized : (fighter != null ? -fighter.transform.forward : Vector3.back);
            lastHitPoint = r.contact.point;
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }

        /// <summary>Постановочная отдача (добивание): толчок пружины без попадания клинка, направление и точка — для падения тела.</summary>
        public void Recoil(Vector3 dir, Vector3 point, float imp, BodyZoneId zone = BodyZoneId.Torso)
        {
            if (animator != null && imp > 0.5f) animator.SetTrigger("Hit");
            kickV += 26f * imp;
            kickZone = zone;
            kickSide = fighter != null ? Mathf.Sign(Vector3.Dot(dir, fighter.transform.right) + 1e-3f) * 0.3f : 0f;
            if (dir.sqrMagnitude > 1e-4f) lastHitDir = dir.normalized;
            lastHitPoint = point;
        }

        /// <summary>
        /// Порядок: Animator (Update) → здесь: поза всего тела из WeaponController.Pose (ключевые позы удара или стойка) →
        /// таз (вынос, высота, поворот, наклон) → грудь → голова держит горизонт → ноги IK на стоящие стопы → руки IK на рукоять.
        /// Стопы не скользят: каждая стоит на своём месте, пока клип не «шагнул» ею (канал FF/RF).
        /// </summary>
        void LateUpdate()
        {
            if (fighter == null || bladeRoot == null) return;
            float dt = Dt;
            if (decapitated && fighter != null && !fighter.Dead) decapitated = false;   // новый раунд
            if (head != null) head.localScale = decapitated ? Vector3.one * 0.001f : Vector3.one;
            Transform ft = fighter.transform;

            // физическое падение: тело живёт само, драйвер не трогает кости (только меч ложится на землю)
            if (rag != null)
            {
                if (rag.Active)
                {
                    if (!fighter.Dead)
                    {
                        rag.Deactivate();
                        if (animator != null) { animator.enabled = true; animator.Rebind(); }
                        gaitInit = false; deadK = 0f; fallDust = false; locoW = 0f;
                    }
                    else
                    {
                        deadK = Mathf.MoveTowards(deadK, 1f, dt / 0.8f);
                        if (deadK >= 0.97f && !fallDust)
                        {
                            fallDust = true;
                            if (Juice.Instance != null && hips != null) Juice.Instance.Dust(new Vector3(hips.position.x, 0.05f, hips.position.z), Vector3.up, 26);
                            poolAt = Time.time + 0.6f;   // кровь растекается, когда тело улеглось
                            if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.04f);
                        }
                        if (poolAt > 0f && Time.time >= poolAt && Juice.Instance != null)
                        {
                            poolAt = -1f;
                            Transform at = chest != null ? chest : hips;
                            if (at != null) Juice.Instance.BloodPool(new Vector3(at.position.x, 0f, at.position.z));
                        }
                        DropSword();
                        return;
                    }
                }
                else if (fighter.Dead && deadK < 0.01f)
                {
                    // скорость отброса — всему телу, удар — задетой части (по направлению клинка, чуть вверх)
                    Vector3 kv = fighter.TakeKnockback();
                    Vector3 push = Vector3.ProjectOnPlane(lastHitDir, Vector3.up).normalized * 1.6f + (kv.sqrMagnitude > 1e-4f ? kv.normalized : -ft.forward) * 1.2f + Vector3.up * 0.5f;
                    if (animator != null) animator.enabled = false;
                    rag.Activate(kv * 0.7f + Vector3.up * 0.4f, push, lastHitPoint.sqrMagnitude > 1e-6f ? lastHitPoint : (hips != null ? hips.position : ft.position));
                    deadK = 0.02f;
                    return;
                }
            }
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
            // приземление: таз проседает и пружинит (0,3 с), в воздухе — поджатые ноги
            bool gnd = fighter.Grounded;
            if (gnd && !wasGrounded && deadK < 0.01f) { landK = 1f; if (Juice.Instance != null) Juice.Instance.Footstep(fighter.transform.position, 1.4f); }
            wasGrounded = gnd;
            landK = Mathf.MoveTowards(landK, 0f, dt / 0.3f);
            // рывок — короткий прыжок-шаг: стопы над землёй, ноги подобраны наполовину
            float airT = !gnd ? 1f : (fighter.Dashing ? 0.7f : 0f);
            airK = Mathf.MoveTowards(airK, airT, dt / (airT < airK ? 0.06f : 0.08f));
            lean = Mathf.MoveTowards(lean, pitchTarget, 140f * dt);
            yawS = Mathf.SmoothDampAngle(yawS, yawTarget, ref yawV, 0.05f);
            bS = Mathf.SmoothDamp(bS, bTarget, ref bV, 0.04f);
            crouchS = Mathf.SmoothDamp(crouchS, crouchTarget, ref crouchV, 0.07f);
            // пружина отдачи (полунеявный Эйлер — устойчив при dt до 1/30)
            kickV += (-hitStiffness * hitStiffness * kick - 2f * hitDamping * hitStiffness * kickV) * dt;
            kick = Mathf.Clamp(kick + kickV * dt, -0.45f, 1.3f);
            if (Mathf.Abs(kick) < 1e-4f && Mathf.Abs(kickV) < 1e-3f) { kick = 0f; kickV = 0f; }
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
            if (legHit) pd += 0.12f * Mathf.Max(0f, kick);

            // стойку (IK ног) держим всегда, пока боец на земле: при ходьбе стопы переступают сами; в прыжке ноги отдаются Animator
            walkS = Mathf.MoveTowards(walkS, Mathf.Abs(fighter.MoveAxis) > 0.1f ? 1f : 0f, 6f * dt);
            bool groundedOk = fighter.Grounded && deadK < 0.01f && legsReady;
            if (!groundedOk) gaitInit = false;
            wLegS = Mathf.MoveTowards(wLegS, groundedOk ? 1f : 0f, dt / 0.1f);
            float wSt = wLegS;
            bool moving = Mathf.Abs(fighter.MoveAxis) > 0.1f && !keyed;
            moveK = Mathf.MoveTowards(moveK, moving ? 1f : 0f, dt / 0.25f);
            // осанка на ходу: таз выше и прямо над стопами, корпус ровнее (поднимается чуть медленнее, чем ноги собираются из стойки)
            postK = Mathf.MoveTowards(postK, moving ? 1f : 0f, dt / (moving ? 0.4f : 0.3f));
            float hYaw0 = hYaw;
            pd *= 1f - 0.85f * postK; pf *= 1f - postK; hYaw *= 1f - 0.85f * postK; hPit *= 1f - 0.6f * postK; cPit *= 1f - 0.5f * postK;
            cYaw += 0.75f * (hYaw0 - hYaw);   // на ходу таз разворачивается по ходу, а грудь с мечом остаётся в стойке — корпус не крутится туда-сюда при каждом старте и остановке

            // мокап-ходьба: ногами и тазом ведёт клип Loco, IK ног уходит; остановился — стопы встают там, где их поставил клип, и шагают в стойку
            locoW = Mathf.MoveTowards(locoW, mocapLoco && locoMoving ? 1f : 0f, dt / 0.2f);
            float procW = 1f - locoW;   // доля процедурной ходьбы

            // стопы (походка) — до тела: таз и грудь в этом же кадре идут за шагом
            float wLeg = legsReady && deadK < 0.01f ? wSt * procW : 0f;
            if (wLeg > 0.001f && prevWLeg <= 0.001f && mocapLoco) { gaitInit = false; gaitFromBones = true; }
            prevWLeg = wLeg;
            if (wLeg > 0.001f) UpdateGait(ft, ys, ff, rf, keyed, dt);
            else lastFtPos = ft.position;
            float walkSx = 0f, walkBobY = 0f;
            if (wLeg > 0.001f && moveK > 0.001f && procW > 0.001f)
            {
                bool frontIsR = footFrontX * ys > 0f;
                Vector3 posR = frontIsR ? gF.pos : gR.pos, posL = frontIsR ? gR.pos : gF.pos;
                float dz = Vector3.Dot(posR - posL, fwd);   // правая стопа впереди — правый бок таза вперёд (поворот с минусом)
                hYaw -= walkHipYaw * dz * moveK * procW; cYaw += walkChestYaw * dz * moveK * procW;
                float swF = gF.step ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(gF.u)) : 0f, swR = gR.step ? Mathf.Sin(Mathf.PI * Mathf.Clamp01(gR.u)) : 0f;
                walkSx = -walkSway * (gF.side * swF + gR.side * swR) * moveK * procW;
                walkBobY = walkBob * (swF + swR) * moveK * procW;
                float spd = walkVelS.magnitude;
                if (spd > 0.05f) cPit += 3f * Vector3.Dot(walkVelS, fwd) / spd * postK;   // корпус чуть подаётся по ходу
            }

            // ---------- 2) таз: вынос вперёд и высота (вся модель) — визуально; зоны поражения едут следом (FollowZones)
            float drop = (stanceDrop * (1f - 0.5f * postK) + pd + walkCrouch * moveK) * wSt * procW;   // на мокапе высоту таза задаёт клип
            drop += 0.11f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, (1f - landK) * 1.6f)) * (landK > 0f ? 1f : 0f) * wSt;   // проседание при приземлении
            Vector3 off = new Vector3(walkSx * wLeg, -drop, pf);
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
            if (wLeg > 0.001f) { SolveLegs(ft, ys, hYaw, heel, walkBobY, wLeg); lockL = default(FootLock); lockR = default(FootLock); }
            else if (fighter.Grounded && deadK < 0.01f && footL != null && footR != null)
            {
                if (locoW > 0.001f && legsReady) { Rot(upperLegL, -hYaw, up); Rot(upperLegR, -hYaw, up); }   // ноги клипа идут по ходу, а не за поворотом таза стойки
                // ходьба: на землю встаёт нижняя точка подошв (пятка при ударе, подушечка при отрыве пятки), а не голеностоп —
                // иначе при перекате на носок стопа уходит в землю, а тело проседает
                float lowest = solL != null ? Mathf.Min(SoleY(footL, solL), SoleY(footR, solR)) : Mathf.Min(footL.position.y, footR.position.y) - footRestH;
                transform.position += Vector3.up * (ft.position.y - lowest);
                // боевой шаг: таз ниже, чем в обычной ходьбе клипа, стопы остаются на своих местах (IK) — колени согнуты, как в стойке,
                // и при переходе стойка ↔ ходьба тело не «вырастает»
                float crouch = mocapCrouch * locoW;
                if (crouch > 1e-4f && legsReady)
                {
                    Vector3 aL = footL.position, aR = footR.position; Quaternion qL = footL.rotation, qR = footR.rotation;
                    transform.position -= Vector3.up * crouch;
                    SolveArm(upperLegL, lowerLegL, footL, lgL1, lgL2, aL, lowerLegL.position - (upperLegL.position + aL) * 0.5f + ft.forward * 0.05f);
                    SolveArm(upperLegR, lowerLegR, footR, lgR1, lgR2, aR, lowerLegR.position - (upperLegR.position + aR) * 0.5f + ft.forward * 0.05f);
                    footL.rotation = qL; footR.rotation = qR;
                }
                if (solL != null && legsReady)
                {
                    bool use = locoW > 0.5f && !fighter.Dashing;   // рывок — прыжок-шаг: стопы не держим на земле (иначе волочатся позади)
                    LockFoot(ref lockL, upperLegL, lowerLegL, footL, lgL1, lgL2, solL, use, ft, dt);
                    LockFoot(ref lockR, upperLegR, lowerLegR, footR, lgR1, lgR2, solR, use, ft, dt);
                }
            }
            else { lockL.on = lockR.on = false; lockL.off = lockR.off = Vector3.zero; lockL.hasPrev = lockR.hasPrev = false; lockL.lift = lockR.lift = 0f; }
            // в воздухе: колени к груди — передняя стопа вверх-вперёд, задняя вверх-назад (по вертикальной скорости сильнее на взлёте)
            if (airK > 0.001f && deadK < 0.01f && footL != null && footR != null && legsReady && wLeg <= 0.001f + (fighter.Dashing ? 1f : 0f))
            {
                float tuck = Smooth(airK);
                bool frontIsL = footFrontX * ys < 0f;
                Vector3 fwdF = ft.forward;
                for (int s = 0; s < 2; s++)
                {
                    bool isL = s == 0;
                    Transform up_ = isL ? upperLegL : upperLegR, lo_ = isL ? lowerLegL : lowerLegR, fo_ = isL ? footL : footR;
                    float l1 = isL ? lgL1 : lgR1, l2 = isL ? lgL2 : lgR2;
                    bool front = isL == frontIsL;
                    Vector3 hipP = up_.position;
                    Vector3 target = hipP + Vector3.down * Mathf.Lerp(l1 + l2 * 0.98f, (l1 + l2) * 0.55f, tuck) + fwdF * (front ? 0.18f : -0.08f) * tuck + ft.right * (isL ? -0.06f : 0.06f) * tuck;
                    Vector3 cur = fo_.position;
                    Quaternion fr0 = fo_.rotation;
                    SolveArm(up_, lo_, fo_, l1, l2, Vector3.Lerp(cur, target, tuck), lo_.position - hipP + fwdF * 0.25f);
                    fo_.rotation = Quaternion.Slerp(fr0, Quaternion.AngleAxis(-25f * tuck, ft.right) * fr0, 0.6f);   // носок вниз
                }
            }
            if (solL != null && footL != null && footR != null) { SoleL = SolePoint(footL, solL); SoleR = SolePoint(footR, solR); }

            // шаги мокапа: стопа опустилась на землю — звук и брызги
            if (locoW > 0.5f && Juice.Instance != null && footL != null && footR != null)
            {
                float gy = ft.position.y;
                float hL = solL != null ? SoleL.y - gy : footL.position.y - gy - footRestH, hR = solR != null ? SoleR.y - gy : footR.position.y - gy - footRestH;
                if (footUpL && hL < 0.012f) { footUpL = false; Juice.Instance.Footstep(new Vector3(footL.position.x, ft.position.y, footL.position.z), 0.8f, Toe(footL, solL, ft), -1); }
                else if (hL > 0.035f) footUpL = true;
                if (footUpR && hR < 0.012f) { footUpR = false; Juice.Instance.Footstep(new Vector3(footR.position.x, ft.position.y, footR.position.z), 0.8f, Toe(footR, solR, ft), 1); }
                else if (hR > 0.035f) footUpR = true;
            }

            // ---------- 5) руки ставятся относительно плеч в системе корпуса: корпус присел / скрутился / вынесен вперёд — руки с клинком идут за ним
            if (weapon != null && weapon.pivot != null && upperArmR != null && upperArmL != null)
            {
                Vector3 shMid = (upperArmR.position + upperArmL.position) * 0.5f;
                // ключевые позы и стойка задают руки в системе бойца (линия реза не уезжает вбок с поворотом таза);
                // старые профили — в системе скрученного корпуса, как раньше
                float twist = (swinging && !keyed) ? yawS * (hipsYawK + spineYawK + chestYawK) : 0f;
                Quaternion frame = Quaternion.AngleAxis(twist, up) * ft.rotation;
                handBiasK = Mathf.MoveTowards(handBiasK, swinging ? 0.5f : 1f, dt / 0.15f);
                weapon.pivot.position = shMid + frame * (weapon.HandOffset + new Vector3(0f, handLift, handForward) * handBiasK);
                // на ходу руки гасят качание таза: клинок держится ровнее корпуса и смотрит на противника
                if (hips != null)
                {
                    float hy = hips.position.y - ft.position.y;
                    hipsYS = Mathf.Abs(hipsYS) < 1e-4f ? hy : Mathf.Lerp(hipsYS, hy, 1f - Mathf.Exp(-dt / 0.25f));
                    weapon.pivot.position -= up * ((hy - hipsYS) * 0.6f * postK) + right * (walkSx * wLeg * 0.6f);
                }
            }

            // досягаемость: если рукоять дальше, чем достают руки, меч подтягивается к плечам — кисти всегда на рукояти
            if (upperArmR != null && upperArmL != null && weapon != null && weapon.pivot != null)
            {
                // в ударе руки выпрямляются почти до конца, в стойке — локти мягко согнуты (~140°, как у врага)
                float rk = Mathf.Lerp(0.965f, 0.935f, Mathf.Clamp01((handBiasK - 0.5f) * 2f));
                float reachR = (l1R + l2R) * rk, reachL = (l1L + l2L) * rk;
                // досягаемость считаем до запястья: при хвате по центру кулака запястье стоит на вынос кулака ближе к плечу,
                // чем рукоять (у героя ~11 см) — иначе меч зря подтягивался к груди и локти зажимались
                Vector3 oR0 = Vector3.zero, oL0 = Vector3.zero;
                if (gripFromBind && framesReady && handR != null && handL != null)
                {
                    Vector3 fR0, nR0, fL0, nL0;
                    GripFrame(bladeRoot.forward, sideR, ft, out fR0, out nR0);
                    GripFrame(bladeRoot.forward, sideL, ft, out fL0, out nL0);
                    oR0 = Quaternion.LookRotation(fR0, nR0) * bindR * Vector3.Scale(gripLocalR, handR.lossyScale);
                    oL0 = Quaternion.LookRotation(fL0, nL0) * bindL * Vector3.Scale(gripLocalL, handL.lossyScale);
                }
                // по одной (худшей) руке за шаг: раньше поправки обеих рук складывались — меч уезжал к груди вдвое дальше нужного,
                // и чем дальше вынесена стойка, тем сильнее зажимались локти (у героя руки короче — локти были ~90°)
                for (int it = 0; it < 8; it++)
                {
                    Vector3 ax = bladeRoot.forward;
                    Vector3 dr = bladeRoot.position - ax * gripFront - oR0 - upperArmR.position;
                    Vector3 dl = bladeRoot.position - ax * gripRear - oL0 - upperArmL.position;
                    float mr = dr.magnitude, ml = dl.magnitude;
                    float er = mr - reachR, el = ml - reachL;
                    if (er <= 1e-4f && el <= 1e-4f) break;
                    weapon.pivot.position -= er >= el ? dr / mr * er : dl / ml * el;
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
            Vector3 outwardL = -ft.right * 1.0f + Vector3.down * 0.5f + back * 0.2f;
            if (gripFromBind && framesReady && handR != null && handL != null)
            {
                // запястье ставим так, чтобы центр кулака (точка в системе кисти) лёг на рукоять после поворота кисти
                Quaternion qR = Quaternion.LookRotation(fR, nR) * bindR, qL = Quaternion.LookRotation(fL, nL) * bindL;
                Vector3 oR = qR * Vector3.Scale(gripLocalR, handR.lossyScale), oL = qL * Vector3.Scale(gripLocalL, handL.lossyScale);
                SolveArm(upperArmR, forearmR, handR, l1R, l2R, gripR - oR, outward);
                SolveArm(upperArmL, forearmL, handL, l1L, l2L, gripL - oL, outwardL);
            }
            else
            {
                SolveArm(upperArmR, forearmR, handR, l1R, l2R, gripR - fR * palmOffset - nR * palmLift, outward);
                SolveArm(upperArmL, forearmL, handL, l1L, l2L, gripL - fL * palmOffset - nL * palmLift, outwardL);
            }
            if (framesReady)
            {
                OrientHand(handR, bindR, fR, nR);
                OrientHand(handL, bindL, fL, nL);
            }
        }

        /// <summary>
        /// Походка (до позы тела): каждая стопа стоит на своём месте в мире.
        /// Стоит — стопы на местах стойки, переступают, когда боец повернулся/сдвинулся (Gait).
        /// Идёт — цикл шага (WalkCycle): ноги по очереди переносятся и обгоняют друг друга, вбок — приставной шаг;
        /// пятка опорной ноги отрывается перед шагом, стопа ставится с пятки.
        /// Удар — стопы ведёт клип (каналы FF/RF), походка садится в те же точки.
        /// </summary>
        void UpdateGait(Transform ft, float ys, float ff, float rf, bool keyed, float dt)
        {
            bool frontIsR = footFrontX * ys > 0f;
            float sideF = frontIsR ? 1f : -1f, sideR = -sideF;
            float ground = ft.position.y, ftYaw = ft.eulerAngles.y;
            Vector3 sF = new Vector3(footFrontX * ys, 0f, footFrontZ + ff), sR = new Vector3(footRearX * ys, 0f, footRearZ + rf);
            wOffF = new Vector3(sideF * walkHalfWidth, 0f, 0f); wOffR = new Vector3(sideR * walkHalfWidth, 0f, 0f);
            tF = ft.position + ft.rotation * Vector3.Lerp(sF, wOffF, moveK); tF.y = ground;
            tR = ft.position + ft.rotation * Vector3.Lerp(sR, wOffR, moveK); tR.y = ground;
            yF = ftYaw + Mathf.Lerp(footFrontYaw * ys, sideF * walkFootOut, moveK);
            yR = ftYaw + Mathf.Lerp(footRearYaw * ys, sideR * walkFootOut, moveK);

            Vector3 vel = dt > 1e-5f ? (ft.position - lastFtPos) / dt : Vector3.zero; vel.y = 0f;
            if (vel.sqrMagnitude > 25f) vel = Vector3.zero;   // телепорт (новый раунд, супер-удар), рывок
            lastFtPos = ft.position;
            walkVel = vel;
            walkVelS = Vector3.Lerp(walkVelS, vel, 1f - Mathf.Exp(-dt / 0.15f));
            float spS = walkVelS.magnitude, kfS = spS > 0.05f ? Vector3.Dot(walkVelS, ft.forward) / spS : 0f;
            rollK = Mathf.Max(0f, kfS) + 0.5f * Mathf.Max(0f, -kfS);   // перекат пятка→носок только по ходу вперёд (назад — вполовину, вбок — без него)
            if (!gaitInit)
            {
                if (gaitFromBones && footL != null && footR != null)
                {
                    // после мокап-ходьбы стопы стоят там, где их поставил клип
                    Transform fb = frontIsR ? footR : footL, rb = frontIsR ? footL : footR;
                    Vector3 pF = fb.position, pR = rb.position; pF.y = ground; pR.y = ground;
                    gF = NewFoot(pF, ftYaw + sideF * walkFootOut, sideF); gR = NewFoot(pR, ftYaw + sideR * walkFootOut, sideR);
                }
                else { gF = NewFoot(tF, yF, sideF); gR = NewFoot(tR, yR, sideR); }
                gaitFromBones = false; gaitInit = true; walking = false;
            }
            gF.side = sideF; gR.side = sideR;
            bool stepF0 = gF.step, stepR0 = gR.step;
            keyW = Mathf.MoveTowards(keyW, keyed ? 1f : 0f, dt / 0.12f);
            if (keyW > 0.001f) { walking = false; Settle(ref gF, tF, yF, keyW); Settle(ref gR, tR, yR, keyW); }
            else if (vel.sqrMagnitude > 0.0225f && Mathf.Abs(fighter.MoveAxis) > 0.1f) WalkCycle(ft, vel, dt);   // идёт сам (отброс ударом — шагами стойки)
            else { walking = false; Gait(tF, yF, tR, yR, vel, dt); }
            FootPitch(ref gF, 0f, dt);
            FootPitch(ref gR, 0.5f, dt);

            // стопа встала — звук и брызги ровно в момент постановки; в ударе — притоп передней ноги (шаг клипа)
            if (Juice.Instance != null && keyW < 0.001f)
            {
                if (stepF0 && !gF.step) Landed(gF);
                if (stepR0 && !gR.step) Landed(gR);
            }
            float sFs = weapon != null ? Mathf.Abs(weapon.FootSpeedF) : 0f;
            if (keyed && Juice.Instance != null && keyStepSpeed > 1.2f && sFs < 0.3f) Juice.Instance.Footstep(gF.pos, 1.3f);
            keyStepSpeed = keyed ? (sFs > 0.3f ? Mathf.Max(keyStepSpeed, sFs) : (sFs < 0.3f ? 0f : keyStepSpeed)) : 0f;
        }

        float keyStepSpeed;

        void Landed(Foot f)
        {
            float d = (f.to - f.from).magnitude;
            if (d < 0.05f) return;   // переставил стопу на месте — не слышно
            Juice.Instance.Footstep(f.pos, Mathf.Clamp(d / 0.5f, 0.3f, 1.2f), Quaternion.Euler(0f, f.yaw, 0f) * Vector3.forward, f.side > 0f ? 1 : -1);
        }

        /// <summary>Куда смотрит носок (по точкам подошвы: пятка → носок), по земле.</summary>
        static Vector3 Toe(Transform foot, Vector3[] sol, Transform ft)
        {
            Vector3 d = sol != null && sol.Length >= 3 ? foot.TransformPoint(sol[2]) - foot.TransformPoint(sol[0]) : ft.forward;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : ft.forward;
        }

        static Foot NewFoot(Vector3 p, float y, float side) { return new Foot { pos = p, from = p, to = p, yaw = y, yawFrom = y, yawTo = y, land = 9f, side = side }; }

        float Cadence(Transform ft, Vector3 vel)
        {
            return cadenceBase + cadencePerSpeed * vel.magnitude + cadenceLateral * Mathf.Abs(Vector3.Dot(vel, ft.right));
        }

        /// <summary>
        /// Ходьба: фаза цикла задаёт темп; передняя стопа (стойки) отрывается при фазе 0, задняя — при 0.5.
        /// Первой идёт стопа, которая отстанет от своего места больше (вперёд — задняя, назад — передняя, вбок — дальняя от хода).
        /// </summary>
        void WalkCycle(Transform ft, Vector3 vel, float dt)
        {
            float c = Cadence(ft, vel);
            float swing = swingFrac / c;
            if (!walking)
            {
                walking = true;
                // стопа уже в шаге стойки (и не на излёте) — цикл подхватывает её, иначе первой идёт отстающая
                if (gF.step && gF.u < 0.6f) { walkPhase = gF.u * swingFrac * 0.5f; BeginWalk(ref gF, wOffF, yF, vel, swing, ft, c); }
                else if (gR.step && gR.u < 0.6f) { walkPhase = 0.5f + gR.u * swingFrac * 0.5f; BeginWalk(ref gR, wOffR, yR, vel, swing, ft, c); }
                else
                {
                    Vector3 eF = gF.pos - vel * 0.3f - (ft.position + ft.rotation * wOffF); eF.y = 0f;
                    Vector3 eR = gR.pos - vel * 0.3f - (ft.position + ft.rotation * wOffR); eR.y = 0f;
                    walkPhase = eF.sqrMagnitude > eR.sqrMagnitude ? 0.999f : 0.499f;
                }
            }
            float prev = walkPhase;
            walkPhase += c * 0.5f * dt;
            bool wrap = walkPhase >= 1f;
            if (wrap) walkPhase -= 1f;
            Advance(ref gF, dt); Advance(ref gR, dt);
            bool hitF = wrap, hitR = (prev < 0.5f && walkPhase >= 0.5f) || (wrap && (prev < 0.5f || walkPhase >= 0.5f));
            if (hitF) gF.pend = true;
            if (gF.pend && !gF.step && (!gR.step || gR.u > 0.85f)) BeginWalk(ref gF, wOffF, yF, vel, hitF ? swing : swing * 0.85f, ft, c);
            if (hitR) gR.pend = true;
            if (gR.pend && !gR.step && (!gF.step || gF.u > 0.85f)) BeginWalk(ref gR, wOffR, yR, vel, hitR ? swing : swing * 0.85f, ft, c);
        }

        /// <summary>Шаг на ходу: стопа встаёт под тазом в момент постановки + walkLead шага вперёд по ходу; вбок ведущая стопа уходит шире, приставляемая — ближе.</summary>
        void BeginWalk(ref Foot f, Vector3 wOff, float y, Vector3 vel, float dur, Transform ft, float c)
        {
            float vF = Vector3.Dot(vel, ft.forward), vL = Vector3.Dot(vel, ft.right);
            float sF = vF / c, sL = vL / c;
            float dO = Mathf.Max(0f, walkMinSep + Mathf.Abs(sL) - 2f * walkHalfWidth);
            bool lat = Mathf.Abs(vL) > 0.05f;
            bool lead = lat && (f.side > 0f) == (vL > 0f);
            float latOff = walkLead * sL + (lat ? Mathf.Sign(vL) * dO * 0.5f * (lead ? 1f : -1f) : 0f);
            Vector3 to = ft.position + vel * dur + ft.rotation * (wOff + new Vector3(latOff, 0f, walkLead * sF)); to.y = ft.position.y;
            if (f.step)
            {
                // стопа уже в воздухе (шаг стойки): доводим по новой траектории, не роняя на землю
                float s = Smooth(Mathf.Clamp01(f.u));
                f.to = to; f.from = (f.pos - to * s) / Mathf.Max(1e-3f, 1f - s); f.walkStep = true; f.pend = false; f.yawTo = y;
                return;
            }
            f.dur = dur; f.from = f.pos; f.to = to; f.p0 = f.pit;
            f.yawFrom = f.yaw; f.yawTo = y; f.u = 0f; f.step = true; f.walkStep = true; f.pend = false;
        }

        /// <summary>Тангаж стопы: в шаге — с носка (пятка была поднята) на пятку (носок вверх), после постановки перекат на всю стопу, перед отрывом пятка поднимается.</summary>
        void FootPitch(ref Foot f, float liftPhase, float dt)
        {
            float p;
            if (f.step) p = f.walkStep ? f.p0 + (-12f * rollK - f.p0) * Smooth(Mathf.Clamp01(f.u)) : -12f * f.lift / Mathf.Max(1e-4f, stepLift);
            else
            {
                p = (f.walkStep && f.land < 0.1f) ? -12f * rollK * (1f - Smooth(Mathf.Clamp01(f.land / 0.1f))) : 0f;
                if (walking)
                {
                    float rem = Mathf.Repeat(liftPhase - walkPhase, 1f);
                    if (rem < heelPeel) p = heelPeelDeg * rollK * Smooth(1f - rem / heelPeel);
                }
                if (!walking && f.pit > p) p = Mathf.Max(p, f.pit - 200f * dt);   // остановился на носке — пятка опускается, а не падает
            }
            f.pit = p;
        }

        /// <summary>
        /// Ноги: IK на стопы походки. Таз опускается ровно настолько, чтобы обе ноги доставали (не больше maxAutoDrop),
        /// колено смотрит по носку, стопа повёрнута по тангажу походки; у задней ноги стойки пятка поднимается по клипу.
        /// </summary>
        void SolveLegs(Transform ft, float ys, float hYaw, float heel, float bob, float w)
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

            float ftYaw = ft.eulerAngles.y;
            float sF = weapon != null ? weapon.FootSpeedF : 0f;
            float keyLift = stepLift * Mathf.Clamp01(Mathf.Abs(sF) / 2.5f) * keyW;
            float riseF = heelLift * Mathf.Max(0f, gF.pit) / 28f, riseR = heelLift * Mathf.Max(0f, gR.pit) / 28f;   // пятка вверх — стопа на носке, лодыжка выше
            Vector3 frontPos = gF.pos + up * (footRestH + gF.lift + keyLift + riseF);
            Vector3 rearPos = gR.pos + up * (footRestH + gR.lift + heel * heelLift + riseR);
            float relF = Mathf.DeltaAngle(ftYaw, gF.yaw), relR = Mathf.DeltaAngle(ftYaw, gR.yaw);
            transform.position += up * (bob * w);   // на одной ноге таз выше, в двойной опоре — ниже

            // таз ниже, если иначе нога не дотягивается (широкий шаг, вынос таза)
            Rot(fU, -hYaw, up); Rot(rU, -hYaw, up);   // бёдра не скручиваются вместе с тазом
            float need = 0f;
            need = Mathf.Max(need, DropNeeded(fU.position, frontPos, (f1 + f2) * 0.985f, up));
            need = Mathf.Max(need, DropNeeded(rU.position, rearPos, (r1 + r2) * 0.985f, up));
            if (need > 0f) transform.position -= up * (Mathf.Min(need, maxAutoDrop) * w);

            // на ходу колени чуть врозь (по носку и немного наружу) — ноги в широких хакама не сходятся «иксом»
            float splay = 0.25f * postK;
            Vector3 poleF = Quaternion.AngleAxis(relF, up) * ft.forward + right * (splay * gF.side) + up * 0.1f;
            Vector3 poleR = Quaternion.AngleAxis(relR, up) * ft.forward + right * (splay * gR.side) + up * 0.1f;
            SolveArm(fU, fL_, fF, f1, f2, frontPos, poleF);
            SolveArm(rU, rL_, rF, r1, r2, rearPos, poleR);
            float pitF = gF.pit - (stepLift > 0f ? 12f * keyLift / stepLift : 0f);   // шаг клипа удара: носок чуть вверх
            float pitR = gR.pit + 28f * heel;                                          // пятка задней ноги по клипу
            if (fF != null) fF.rotation = Quaternion.AngleAxis(relF, up) * Quaternion.AngleAxis(pitF, right) * ft.rotation * fRel;
            if (rF != null) rF.rotation = Quaternion.AngleAxis(relR, up) * Quaternion.AngleAxis(pitR, right) * ft.rotation * rRel;

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
            f.pos = Vector3.Lerp(f.pos, t, k); f.yaw = Mathf.LerpAngle(f.yaw, y, k); f.step = false; f.lift = 0f; f.walkStep = false; f.pend = false;
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
            if (eF > 1.2f || eR > 1.2f) { gF = NewFoot(tF, yF, gF.side); gR = NewFoot(tR, yR, gR.side); return; }   // телепорт / новый раунд: встать сразу
            float thr = vel.sqrMagnitude > 0.04f ? stepThreshold + stepThresholdPerSpeed * vel.magnitude : stepThreshold * 0.6f;
            const float urgent = 0.25f;   // обычно ноги переступают по очереди; сильно отставшая — шагает, не дожидаясь другой
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
            f.yawFrom = f.yaw; f.yawTo = y; f.u = 0f; f.step = true; f.walkStep = false; f.p0 = 0f; f.pend = false;
        }

        void Advance(ref Foot f, float dt)
        {
            if (!f.step) { f.land += dt; return; }
            f.u += dt / Mathf.Max(0.05f, f.dur);
            float u = Mathf.Clamp01(f.u), s = Smooth(u);
            f.pos = Vector3.Lerp(f.from, f.to, s);
            f.yaw = Mathf.LerpAngle(f.yawFrom, f.yawTo, s);
            // стопа выше всего в первой половине переноса (колено сгибается сразу после отрыва)
            f.lift = Mathf.Lerp(stepLift, walkLift, moveK) * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.8f)) * Mathf.Clamp01((f.to - f.from).magnitude / 0.25f);
            if (f.u >= 1f) { f.step = false; f.lift = 0f; f.land = f.walkStep ? 0f : 9f; }
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
