using System;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Управляет движением катаны: конечный автомат удара (замах → удар → проводка → восстановление) по SwingProfile.
    /// Анимация здесь — только программное движение pivot; попадания клинком определяет BladeTrajectorySampler,
    /// снарядами — EnergyProjectile.
    ///
    /// Соглашение о геометрии: bladeRoot стоит у гарды/основания клинка, клинок идёт вдоль его локальной +Z.
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [Header("Geometry")]
        public FighterController owner;
        [Tooltip("Вращается при ударе (рука/плечо). Локальные углы — относительно бойца.")]
        public Transform pivot;
        [Tooltip("Основание клинка; клинок идёт вдоль локальной +Z.")]
        public Transform bladeRoot;
        public float bladeLength = 0.8f;
        [Tooltip("Эффективная «толщина» клинка для проверок траектории (радиус сферы), м.")]
        public float bladeRadius = 0.025f;
        [Tooltip("+1/-1: зеркалит боковое отклонение (yaw) ударов для руки с другой стороны. Без этого удары врага мажут.")]
        public int yawSign = 1;
        public Vector3 restEuler = new Vector3(-30f, 0f, 0f);
        [Tooltip("Положение рук в стойке относительно середины плеч в системе корпуса (x вправо, y вверх, z вперёд), м.")]
        public Vector3 restHand = new Vector3(0.06f, -0.40f, 0.30f);   // чудан: руки у пупка и вперёд (= SwingProfile.ReadyPose)
        [Tooltip("Блок: клинок почти вертикально перед лицом, руки у груди.")]
        public Vector3 guardEuler = new Vector3(-82f, 8f, 0f);
        public Vector3 guardHand = new Vector3(0.03f, -0.02f, 0.40f);   // блок: клинок вертикально перед собой на вытянутых руках
        float blockK, stagK;
        [Tooltip("Слом равновесия: клинок опущен остриём к земле, руки внизу, корпус осел.")]
        public Vector3 staggerEuler = new Vector3(62f, -25f, 15f);
        public Vector3 staggerHand = new Vector3(0.16f, -0.70f, 0.22f);
        [Tooltip("Насколько меньше раскрыт корпус в стойке, град (таз, грудь): переднюю руку на цубе держит дальнее плечо — при сильном развороте к камере руки прижаты к груди. Вычитается из всех поз (и ударов), динамика удара сохраняется.")]
        public float openHips, openChest;
        [Tooltip("Стойка (камаэ): 0 — тюдан (остриё на противника), 1 — хассо (клинок вертикально у правого плеча), 2 — вакигамаэ (клинок у бедра остриём назад, спрятан за телом). Враг меняет стойки — по ней читается, чем он ударит.")]
        public int stance;
        float stanceK1, stanceK2;
        /// <summary>Насколько боец сейчас в стойке хассо / вакигамаэ (0…1, плавно).</summary>
        public float StanceK(int s) { return s == 1 ? stanceK1 : s == 2 ? stanceK2 : 1f - Mathf.Max(stanceK1, stanceK2); }

        public SwingProfile CurrentProfile { get; private set; }
        public SwingPhase Phase { get; private set; }
        public int SwingId { get; private set; }
        public string AttackLabel { get { return CurrentProfile != null ? CurrentProfile.label : "-"; } }

        /// <summary>Клинок «жив» для проверки попаданий. Для дальней атаки всегда false.</summary>
        public bool ContactWindowOpen
        {
            get { return CurrentProfile != null && !CurrentProfile.ranged && !CurrentProfile.scripted && (Phase == SwingPhase.Strike || Phase == SwingPhase.FollowThrough); }
        }
        public float CurrentLunge { get { return (CurrentProfile != null && Phase == SwingPhase.Strike) ? CurrentProfile.lungeSpeed : 0f; } }

        /// <summary>Прогресс текущей фазы удара 0…1 (для процедурной анимации тела).</summary>
        public float PhaseProgress { get { return Phase == SwingPhase.Idle ? 0f : Mathf.Clamp01(phaseTime / Mathf.Max(0.0001f, PhaseDuration(Phase))); } }

        /// <summary>Визуальный выпад корпуса вперёд, м (из профиля и фазы). Руки с клинком едут вместе с телом, иначе на ударе кисти вжимаются в грудь.</summary>
        public float VisualLunge { get; private set; }
        /// <summary>Текущие углы позы катаны без нормализации Unity (localEulerAngles за ±90° по pitch переворачивает yaw/roll и ломает расчёт тела).</summary>
        public Vector3 CurrentEuler { get; private set; }
        /// <summary>Локальная позиция pivot без поправки «досягаемости рук» (HeroRigDriver подтягивает меч к плечам, если рукоять дальше, чем достают руки).</summary>
        public Vector3 PureLocalPosition { get; private set; }
        /// <summary>Где должны быть руки сейчас: смещение от середины плеч в системе корпуса. Модель (HeroRigDriver) ставит pivot по нему; без модели работает старое смещение pivotOffset.</summary>
        public Vector3 HandOffset { get; private set; }

        /// <summary>Поза всего тела сейчас (каналы PoseCh, уже зеркалены по yawSign): руки, клинок, таз, грудь, стопы. Её ставит на скелет HeroRigDriver.</summary>
        public readonly float[] Pose = new float[PoseCh.N];
        /// <summary>Текущий удар задан ключевыми позами всего тела (таз/грудь/стопы берутся из Pose), а не старой парой поз клинка.</summary>
        public bool Keyed { get { return Phase != SwingPhase.Idle && CurrentProfile != null && CurrentProfile.track != null; } }
        /// <summary>Время от начала удара, с (сквозь все фазы).</summary>
        public float SwingTime
        {
            get
            {
                if (Phase == SwingPhase.Idle || CurrentProfile == null) return 0f;
                float s = phaseTime;
                if (Phase >= SwingPhase.Strike) s += CurrentProfile.windupTime;
                if (Phase >= SwingPhase.FollowThrough) s += CurrentProfile.strikeTime;
                if (Phase >= SwingPhase.Recovery) s += CurrentProfile.followTime;
                return s;
            }
        }
        /// <summary>Скорость передней / задней стопы вдоль шага, м/с (нога в шаге приподнимается).</summary>
        public float FootSpeedF { get; private set; }
        public float FootSpeedR { get; private set; }
        const float BlendTime = 0.12f;
        readonly float[] blendFrom = new float[PoseCh.N];
        readonly float[] sample = new float[PoseCh.N];
        float blendLeft;
        /// <summary>Поза прервалась скачком (новый удар из восстановления, сброс замаха): плавно перетекаем из прежней позы, а не телепортируем клинок.</summary>
        void SnapshotForBlend() { System.Array.Copy(Pose, blendFrom, PoseCh.N); blendLeft = BlendTime; }

        public event Action<WeaponController> ContactWindowOpened;
        public event Action<WeaponController> ContactWindowClosed;
        /// <summary>Дальняя атака: момент выпуска снаряда.</summary>
        public event Action<WeaponController> Released;

        float phaseTime;
        Vector3 pivotBasePos;

        void Awake()
        {
            if (pivot != null) pivotBasePos = pivot.localPosition;
            float[] r = SwingProfile.ReadyPose;   // стойка одна для всего: и для ключевых ударов, и для старых
            restHand = new Vector3(r[PoseCh.HX], r[PoseCh.HY], r[PoseCh.HZ]);
            restEuler = new Vector3(r[PoseCh.EP], r[PoseCh.EY], r[PoseCh.ER]);
            ApplyRestPose();
        }

        /// <summary>Опасная атака (危): блок её не держит; замах дольше — время среагировать (рывок навстречу выпаду, прыжок от подсечки).</summary>
        public bool Perilous { get; private set; }
        public float perilousWindupRate = 0.7f;

        public bool TryStartSwing(AttackType type, bool perilous = false)
        {
            if (Phase != SwingPhase.Idle) return false;
            Perilous = perilous;
            CurrentProfile = SwingProfile.Get(type);
            SwingId++;
            SnapshotForBlend();
            Phase = SwingPhase.Windup;
            phaseTime = 0f;
            return true;
        }

        /// <summary>
        /// Серия: из восстановления после удара сразу в замах следующего (поза смешивается из текущей).
        /// Раньше доли minRec01 восстановления нельзя — у каждого удара остаётся «хвост», серия читается по ритму.
        /// </summary>
        public bool TryChain(AttackType type, float minRec01 = 0.3f)
        {
            if (Phase == SwingPhase.Idle) return TryStartSwing(type);
            if (Phase != SwingPhase.Recovery || PhaseProgress < minRec01) return false;
            Perilous = false;
            CurrentProfile = SwingProfile.Get(type);
            SwingId++;
            SnapshotForBlend();
            Phase = SwingPhase.Windup;
            phaseTime = 0f;
            return true;
        }

        public void ResetState()
        {
            bool wasOpen = ContactWindowOpen;
            if (Phase != SwingPhase.Idle) SnapshotForBlend();
            Phase = SwingPhase.Idle; Perilous = false;
            phaseTime = 0f;
            ApplyRestPose();
            if (wasOpen && ContactWindowClosed != null) ContactWindowClosed(this);
        }

        /// <summary>
        /// Сразу в стойку, без перетекания из прежней позы. Для редактора (пробы и кадры CI): там Time.deltaTime не идёт,
        /// и после ResetState() смешивание навсегда застревает в позе прерванного удара (клинок остриём в землю).
        /// </summary>
        public void SnapRest() { ResetState(); blendLeft = 0f; blockK = 0f; stagK = 0f; pinUntil = 0f; downUntil = 0f; downK = 0f; stanceK1 = stance == 1 ? 1f : 0f; stanceK2 = stance == 2 ? 1f : 0f; ApplyRestPose(); }

        float pinUntil, downUntil, downK;
        /// <summary>На колено (добит, но поднимается / пропустил сокрушающий удар): корпус осел, клинок упёрт остриём в землю.</summary>
        public void Down(float seconds) { downUntil = Time.time + seconds; }

        float deflectAt = -10f, deflectStrength;
        float idleT;   // сколько стоит в стойке без движения — для «живых» мелочей (перехват рукояти, покачивание острия)
        /// <summary>Отражение (парирование): клинок резко «бьёт» навстречу чужому и возвращается в блок — видимый щелчок, как в Sekiro.</summary>
        public void Deflect(float strength = 1f) { deflectAt = Time.time; deflectStrength = strength; }
        /// <summary>Клинок прижат к земле (микири: на него наступили) — поза как при сломе равновесия, остриём вниз.</summary>
        public void Pin(float seconds) { pinUntil = Time.time + seconds; stagK = Mathf.Max(stagK, 0.6f); }

        // ---- блок по направлению удара: клинок встаёт на пути чужого клинка, а не всегда вертикально перед лицом
        Vector3 gE, gH; bool gInit;
        int threatSwing = -1, threatKind = -1; float threatSide = 1f;

        /// <summary>
        /// Поза блока под угрозу. Сторону и высоту читаем по замаху противника (где его руки и клинок) и держим до конца удара —
        /// иначе блок «прыгает» за клинком через центр. Сверху — клинок поперёк над головой; рез — вертикально с той стороны;
        /// по ногам — остриём вниз у колена; укол — отвод вверх-вбок.
        /// </summary>
        void GuardFor(out Vector3 e, out Vector3 h)
        {
            e = new Vector3(guardEuler.x, guardEuler.y * yawSign, guardEuler.z);
            h = new Vector3(guardHand.x * yawSign, guardHand.y, guardHand.z);
            FighterController op = owner != null ? owner.opponent : null;
            WeaponController ow = op != null ? op.weapon : null;
            if (ow == null || ow.bladeRoot == null || ow.pivot == null || ow.CurrentProfile == null || ow.CurrentProfile.ranged
                || ow.Phase == SwingPhase.Idle || ow.Phase == SwingPhase.Recovery) { threatSwing = -1; threatKind = -1; return; }
            if (ow.Phase == SwingPhase.Windup || threatSwing != ow.SwingId)
            {
                threatSwing = ow.SwingId;
                Transform ft = owner.transform;
                Vector3 mid = ft.InverseTransformPoint(ow.bladeRoot.position + ow.bladeRoot.forward * ow.bladeLength * 0.6f);
                Vector3 hl = ft.InverseTransformPoint(ow.pivot.position);
                threatSide = Mathf.Sign(hl.x + mid.x * 0.5f + 1e-4f);
                AttackType t = ow.CurrentProfile.type;
                if (t == AttackType.Thrust) threatKind = 3;
                else if (t == AttackType.LegSweep) threatKind = 2;
                else if (t == AttackType.Overhead || t == AttackType.JumpStrike) threatKind = 0;
                else threatKind = mid.y > 1.75f ? 0 : 1;
            }
            float s = threatSide;
            switch (threatKind)
            {
                case 0: h = new Vector3(0.08f * yawSign, 0.22f, 0.30f); e = new Vector3(-14f, -80f * yawSign, 0f); break;   // над головой поперёк, остриё в сторону
                case 1: h = new Vector3(0.15f * s, -0.02f, 0.32f); e = new Vector3(-78f, 16f * s, 0f); break;              // вертикально с той стороны
                case 2: h = new Vector3(0.14f * s, -0.42f, 0.30f); e = new Vector3(72f, 18f * s, 0f); break;               // остриём вниз у колена
                case 3: h = new Vector3(0.06f * yawSign, -0.3f, 0.42f); e = new Vector3(8f, 28f * yawSign, 0f); break;    // укол: клинок вперёд-вниз наискось поперёк линии укола (на уровне живота)
            }
        }

        /// <summary>Удар принят в блок: клинок отскакивает от чужого клинка в восстановление (поза перетекает из текущей).</summary>
        public void Rebound()
        {
            if (Phase != SwingPhase.Strike && Phase != SwingPhase.FollowThrough) return;
            bool wasOpen = ContactWindowOpen;
            SnapshotForBlend();
            Phase = SwingPhase.Recovery;
            phaseTime = CurrentProfile != null ? CurrentProfile.recoveryTime * 0.3f : 0f;
            ApplyPose();
            if (wasOpen && ContactWindowClosed != null) ContactWindowClosed(this);
        }

        void ApplyRestPose()
        {
            if (pivot == null) return;
            float bk = Smooth(blockK);
            Vector3 ge, gh; GuardFor(out ge, out gh);
            if (!gInit) { gE = ge; gH = gh; gInit = true; }
            float gk = Time.deltaTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / 0.05f) : 1f;   // в редакторе время стоит — сразу
            if (bk < 0.01f) gk = 1f;   // блок не поднят — поза блока сразу под угрозу
            gE = Vector3.Lerp(gE, ge, gk); gH = Vector3.Lerp(gH, gh, gk);
            // стойка (камаэ): тюдан / хассо / вакигамаэ — плавно, блок поднимается из любой
            float k1 = Smooth(stanceK1), k2 = Smooth(stanceK2);
            Vector3 sE = restEuler, sH = new Vector3(restHand.x * yawSign, restHand.y, restHand.z);
            if (k1 > 0.001f) { sE = Vector3.Lerp(sE, new Vector3(-97f, -16f * yawSign, 0f), k1); sH = Vector3.Lerp(sH, new Vector3(0.27f * yawSign, -0.05f, 0.1f), k1); }    // хассо: руки у правого плеча, клинок вверх и чуть назад за голову
            if (k2 > 0.001f) { sE = Vector3.Lerp(sE, new Vector3(24f, 150f * yawSign, 0f), k2); sH = Vector3.Lerp(sH, new Vector3(0.3f * yawSign, -0.48f, -0.02f), k2); }   // вакигамаэ: руки у бедра, остриё назад-вниз, клинок за телом
            Vector3 re = Vector3.Lerp(sE, gE, bk);
            Vector3 rh = Vector3.Lerp(sH, gH, bk);
            for (int c = PoseCh.PF; c < PoseCh.N; c++) Pose[c] = SwingProfile.ReadyPose[c];
            Pose[PoseCh.HYAW] = (Pose[PoseCh.HYAW] - openHips) * yawSign; Pose[PoseCh.CYAW] = (Pose[PoseCh.CYAW] - openChest) * yawSign;
            float nb = 1f - bk;
            Pose[PoseCh.HYAW] += (6f * k1 + 16f * k2) * yawSign * nb; Pose[PoseCh.CYAW] += (6f * k1 + 8f * k2) * yawSign * nb;
            Pose[PoseCh.PD] += (0.01f * k1 + 0.04f * k2) * nb; Pose[PoseCh.CPIT] += (-2f * k1 + 4f * k2) * nb;
            Pose[PoseCh.PF] -= 0.04f * bk; Pose[PoseCh.PD] += 0.03f * bk; Pose[PoseCh.CPIT] -= 3f * bk;   // блок: вес чуть назад, колени ниже
            float sk = Smooth(stagK);
            if (sk > 0.001f)
            {
                // «поплыл»: клинок повис, колени подогнулись, корпус осел и покачивается
                re = Vector3.Lerp(re, new Vector3(staggerEuler.x, staggerEuler.y * yawSign, staggerEuler.z * yawSign), sk);
                rh = Vector3.Lerp(rh, new Vector3(staggerHand.x * yawSign, staggerHand.y, staggerHand.z), sk);
                float wob = Mathf.Sin(Time.time * 4.5f);
                Pose[PoseCh.PD] += 0.15f * sk; Pose[PoseCh.PF] -= 0.08f * sk;
                Pose[PoseCh.CPIT] += (24f + 3f * wob) * sk; Pose[PoseCh.HPIT] += 10f * sk;
                Pose[PoseCh.CYAW] += 6f * wob * sk * yawSign;
            }
            // стойка без движения: остриё чуть дышит, раз в 7 с — перехват рукояти (кисти проворачиваются, клинок на миг опускается)
            if (idleT > 2.5f)
            {
                float cyc = (idleT - 2.5f) % 7f;
                if (cyc < 0.9f)
                {
                    float k = Mathf.Sin(Mathf.PI * cyc / 0.9f);
                    rh += new Vector3(0.015f * yawSign, -0.025f, -0.03f) * k;
                    re += new Vector3(7f, 3f * yawSign, -5f * yawSign) * k;
                }
                re.x += Mathf.Sin(idleT * 1.3f) * 1.2f * Mathf.Clamp01(idleT - 2.5f);
            }
            // щелчок отражения: руки на миг вперёд-вверх, клинок наклоняется к удару и возвращается (0,2 с)
            float da = Time.time - deflectAt;
            if (da >= 0f && da < 0.2f)
            {
                float k = Mathf.Sin(Mathf.PI * da / 0.2f) * deflectStrength * Mathf.Max(bk, 0.5f);
                rh += new Vector3(0f, 0.05f, 0.11f) * k;
                re += new Vector3(-14f, 0f, 0f) * k;
                Pose[PoseCh.CPIT] += 6f * k; Pose[PoseCh.PF] += 0.04f * k;
            }
            float dk = Smooth(downK);
            if (dk > 0.001f)
            {
                // на колене: таз низко, корпус согнут, клинок упёрт в землю перед собой
                re = Vector3.Lerp(re, new Vector3(68f, 12f * yawSign, 0f), dk);
                rh = Vector3.Lerp(rh, new Vector3(0.12f * yawSign, -0.66f, 0.34f), dk);
                Pose[PoseCh.PD] += 0.3f * dk; Pose[PoseCh.PF] -= 0.04f * dk;
                Pose[PoseCh.CPIT] += 26f * dk; Pose[PoseCh.HPIT] += 14f * dk;
            }
            SetHandEuler(rh, re);
            FootSpeedF = FootSpeedR = 0f;
            ApplyBlend();
            pivot.localRotation = Quaternion.Euler(CurrentEuler);
            pivot.localPosition = pivotBasePos;
            VisualLunge = 0f;
            PureLocalPosition = pivotBasePos;
        }

        void Update()
        {
            float bt = (owner != null && owner.Blocking) ? 1f : 0f;
            blockK = Mathf.MoveTowards(blockK, bt, Time.deltaTime / 0.12f);
            bool stag = (owner != null && owner.PostureBroken) || Time.time < pinUntil;
            downK = Mathf.MoveTowards(downK, Time.time < downUntil ? 1f : 0f, Time.deltaTime / (Time.time < downUntil ? 0.18f : 0.5f));
            stagK = Mathf.MoveTowards(stagK, stag ? 1f : 0f, Time.deltaTime / (stag ? 0.15f : 0.4f));
            stanceK1 = Mathf.MoveTowards(stanceK1, stance == 1 ? 1f : 0f, Time.deltaTime / 0.4f);
            stanceK2 = Mathf.MoveTowards(stanceK2, stance == 2 ? 1f : 0f, Time.deltaTime / 0.4f);
            blendLeft = Mathf.Max(0f, blendLeft - Time.deltaTime);
            bool still = Phase == SwingPhase.Idle && blockK < 0.01f && stagK < 0.01f && owner != null && Mathf.Abs(owner.MoveAxis) < 0.1f && !owner.Dead;
            idleT = still ? idleT + Time.deltaTime : 0f;
            if (Phase == SwingPhase.Idle && pivot != null) { ApplyRestPose(); return; }
            if (Phase == SwingPhase.Idle || pivot == null || CurrentProfile == null) return;

            phaseTime += Time.deltaTime * (Perilous && Phase == SwingPhase.Windup ? perilousWindupRate : 1f);
            // перенос остатка времени между фазами (длинный кадр не «съедает» фазу)
            int guard = 8;
            while (Phase != SwingPhase.Idle && guard-- > 0)
            {
                float dur = Mathf.Max(0.0001f, PhaseDuration(Phase));
                if (phaseTime < dur) break;
                phaseTime -= dur;
                AdvancePhase();
            }
            ApplyPose();
        }

        float PhaseDuration(SwingPhase p)
        {
            switch (p)
            {
                case SwingPhase.Windup: return CurrentProfile.windupTime;
                case SwingPhase.Strike: return CurrentProfile.strikeTime;
                case SwingPhase.FollowThrough: return CurrentProfile.followTime;
                case SwingPhase.Recovery: return CurrentProfile.recoveryTime;
                default: return 0f;
            }
        }

        void AdvancePhase()
        {
            switch (Phase)
            {
                case SwingPhase.Windup:
                    Phase = SwingPhase.Strike;
                    if (CurrentProfile.ranged) { if (Released != null) Released(this); }
                    else if (!CurrentProfile.scripted && ContactWindowOpened != null) ContactWindowOpened(this);
                    break;
                case SwingPhase.Strike:
                    Phase = SwingPhase.FollowThrough;
                    break;
                case SwingPhase.FollowThrough:
                    Phase = SwingPhase.Recovery;
                    if (!CurrentProfile.ranged && !CurrentProfile.scripted && ContactWindowClosed != null) ContactWindowClosed(this);
                    break;
                case SwingPhase.Recovery:
                    Phase = SwingPhase.Idle; Perilous = false;
                    break;
            }
        }

        Vector3 Mirror(Vector3 e) { return new Vector3(e.x, e.y * yawSign, e.z); }

        void ApplyPose()
        {
            if (Phase == SwingPhase.Idle) { ApplyRestPose(); return; }

            SwingProfile p = CurrentProfile;
            if (p.track != null) { ApplyKeyed(p); return; }
            float t = Mathf.Clamp01(phaseTime / Mathf.Max(0.0001f, PhaseDuration(Phase)));
            Vector3 rest = restEuler;
            Vector3 wind = Mirror(p.windupEuler), end = Mirror(p.strikeEndEuler), follow = Mirror(p.followEuler);
            Vector3 e;
            Vector3 off0 = new Vector3(p.pivotOffset.x * yawSign, p.pivotOffset.y, p.pivotOffset.z);
            Vector3 off1 = p.hasOffsetEnd ? new Vector3(p.pivotOffsetEnd.x * yawSign, p.pivotOffsetEnd.y, p.pivotOffsetEnd.z) : off0;
            Vector3 off;
            switch (Phase)
            {
                case SwingPhase.Windup:
                    e = Vector3.Lerp(rest, wind, Smooth(t));
                    off = off0 * Smooth(t);
                    break;
                case SwingPhase.Strike:
                    // клинок (и руки, если есть pivotOffsetEnd) разгоняются к концу удара
                    float u = Mathf.Pow(t, 1.5f);
                    e = Vector3.Lerp(wind, end, u);
                    off = Vector3.Lerp(off0, off1, u) + new Vector3(0f, 0f, p.arcBulge * Mathf.Sin(Mathf.PI * u));   // руки идут по дуге
                    break;
                case SwingPhase.FollowThrough:
                    e = Vector3.Lerp(end, follow, 1f - (1f - t) * (1f - t));
                    off = off1;
                    break;
                default: // Recovery
                    e = Vector3.Lerp(follow, rest, Smooth(t));
                    off = off1 * (1f - Smooth(t));
                    break;
            }
            // положение рук относительно плеч (то же время/форма, что и у pivotOffset)
            Vector3 hr = new Vector3(restHand.x * yawSign, restHand.y, restHand.z);
            Vector3 h0 = p.hasHand ? new Vector3(p.hand0.x * yawSign, p.hand0.y, p.hand0.z) : hr;
            Vector3 h1 = p.hasHand ? new Vector3(p.hand1.x * yawSign, p.hand1.y, p.hand1.z) : h0;
            Vector3 hand;
            switch (Phase)
            {
                case SwingPhase.Windup: hand = Vector3.Lerp(hr, h0, Smooth(t)); break;
                case SwingPhase.Strike:
                    { float uu = Mathf.Pow(t, 1.5f); hand = Vector3.Lerp(h0, h1, uu) + new Vector3(0f, 0f, p.arcBulge * Mathf.Sin(Mathf.PI * uu)); break; }
                case SwingPhase.FollowThrough: hand = h1; break;
                default: hand = Vector3.Lerp(h1, hr, Smooth(t)); break;
            }
            // Euler-интерполяция выбрана осознанно: слерп кватернионов при развороте ~180° неоднозначен.
            float b;   // поза тела: −1 замах … +1 конец удара (как в HeroRigDriver)
            switch (Phase)
            {
                case SwingPhase.Windup: b = -Smooth(t); break;
                case SwingPhase.Strike: b = Mathf.Lerp(-1f, 1f, t); break;
                case SwingPhase.FollowThrough: b = 1f; break;
                default: b = 1f - Smooth(t); break;
            }
            VisualLunge = Mathf.Max(0f, b) * p.bodyStep - Mathf.Max(0f, -b) * 0.05f;
            for (int c = PoseCh.PF; c < PoseCh.N; c++) Pose[c] = SwingProfile.ReadyPose[c];   // тело для старых профилей достраивает HeroRigDriver
            Pose[PoseCh.HYAW] = (Pose[PoseCh.HYAW] - openHips) * yawSign; Pose[PoseCh.CYAW] = (Pose[PoseCh.CYAW] - openChest) * yawSign;
            FootSpeedF = FootSpeedR = 0f;
            SetHandEuler(hand, e);
            ApplyBlend();
            e = CurrentEuler;
            pivot.localRotation = Quaternion.Euler(e);
            pivot.localPosition = pivotBasePos + off + new Vector3(0f, 0f, VisualLunge);
            PureLocalPosition = pivot.localPosition;
        }

        void SetHandEuler(Vector3 hand, Vector3 e)
        {
            Pose[PoseCh.HX] = hand.x; Pose[PoseCh.HY] = hand.y; Pose[PoseCh.HZ] = hand.z;
            Pose[PoseCh.EP] = e.x; Pose[PoseCh.EY] = e.y; Pose[PoseCh.ER] = e.z;
        }

        void ApplyBlend()
        {
            if (blendLeft > 0f)
            {
                float k = Smooth(1f - blendLeft / BlendTime);
                for (int c = 0; c < PoseCh.N; c++) Pose[c] = Mathf.Lerp(blendFrom[c], Pose[c], k);
            }
            HandOffset = new Vector3(Pose[PoseCh.HX], Pose[PoseCh.HY], Pose[PoseCh.HZ]);
            CurrentEuler = new Vector3(Pose[PoseCh.EP], Pose[PoseCh.EY], Pose[PoseCh.ER]);
        }

        /// <summary>
        /// Удар по ключевым позам. Кинетическая цепь — сдвигом времени по каналам: стопы и таз проходят ключи раньше рук,
        /// грудь чуть раньше, клинок чуть позже кистей (хлёст). Окно sin(π·τ/T) гасит сдвиг к началу и концу — стойка не дёргается.
        /// </summary>
        void ApplyKeyed(SwingProfile p)
        {
            PoseTrack tr = p.track;
            float tau = SwingTime, T = Mathf.Max(0.01f, tr.Duration);
            float w = Mathf.Sin(Mathf.PI * Mathf.Clamp01(tau / T));
            tr.Sample(tau, sample, PoseCh.HX, PoseCh.HZ + 1);
            tr.Sample(tau - p.bladeLag * w, sample, PoseCh.EP, PoseCh.ER + 1);
            tr.Sample(tau + p.leadPelvis * w, sample, PoseCh.PF, PoseCh.HPIT + 1);
            tr.Sample(tau + p.leadChest * w, sample, PoseCh.CYAW, PoseCh.CPIT + 1);
            tr.Sample(tau + p.leadFeet * w, sample, PoseCh.FF, PoseCh.HEEL + 1);
            System.Array.Copy(sample, Pose, PoseCh.N);
            // скорость передней стопы (для подъёма ноги в шаге): та же кривая чуть раньше/позже
            float tf = tau + p.leadFeet * w;
            tr.Sample(tf + 0.01f, sample, PoseCh.FF, PoseCh.RF + 1); float f1 = sample[PoseCh.FF], r1 = sample[PoseCh.RF];
            tr.Sample(tf - 0.01f, sample, PoseCh.FF, PoseCh.RF + 1);
            FootSpeedF = (f1 - sample[PoseCh.FF]) / 0.02f; FootSpeedR = (r1 - sample[PoseCh.RF]) / 0.02f;
            Pose[PoseCh.HX] *= yawSign; Pose[PoseCh.EY] *= yawSign; Pose[PoseCh.ER] *= yawSign;
            float ok = 1f - w;   // у концов удара (стойка) — поправка разворота целиком, в середине — авторская поза
            Pose[PoseCh.HYAW] = (Pose[PoseCh.HYAW] - openHips * ok) * yawSign; Pose[PoseCh.CYAW] = (Pose[PoseCh.CYAW] - openChest * ok) * yawSign;
            ApplyBlend();
            VisualLunge = Pose[PoseCh.PF];
            pivot.localRotation = Quaternion.Euler(CurrentEuler);
            pivot.localPosition = pivotBasePos + new Vector3(0f, 0f, VisualLunge);
            PureLocalPosition = pivot.localPosition;
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }
    }
}
