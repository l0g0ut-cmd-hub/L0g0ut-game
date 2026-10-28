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
        public Vector3 guardHand = new Vector3(0.03f, 0.02f, 0.30f);
        float blockK;

        public SwingProfile CurrentProfile { get; private set; }
        public SwingPhase Phase { get; private set; }
        public int SwingId { get; private set; }
        public string AttackLabel { get { return CurrentProfile != null ? CurrentProfile.label : "-"; } }

        /// <summary>Клинок «жив» для проверки попаданий. Для дальней атаки всегда false.</summary>
        public bool ContactWindowOpen
        {
            get { return CurrentProfile != null && !CurrentProfile.ranged && (Phase == SwingPhase.Strike || Phase == SwingPhase.FollowThrough); }
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
            ApplyRestPose();
        }

        public bool TryStartSwing(AttackType type)
        {
            if (Phase != SwingPhase.Idle) return false;
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
            Phase = SwingPhase.Idle;
            phaseTime = 0f;
            ApplyRestPose();
            if (wasOpen && ContactWindowClosed != null) ContactWindowClosed(this);
        }

        void ApplyRestPose()
        {
            if (pivot == null) return;
            float bk = Smooth(blockK);
            Vector3 re = Vector3.Lerp(restEuler, new Vector3(guardEuler.x, guardEuler.y * yawSign, guardEuler.z), bk);
            Vector3 rh = Vector3.Lerp(new Vector3(restHand.x * yawSign, restHand.y, restHand.z), new Vector3(guardHand.x * yawSign, guardHand.y, guardHand.z), bk);
            for (int c = PoseCh.PF; c < PoseCh.N; c++) Pose[c] = SwingProfile.ReadyPose[c];
            Pose[PoseCh.HYAW] *= yawSign; Pose[PoseCh.CYAW] *= yawSign;
            Pose[PoseCh.PF] -= 0.04f * bk; Pose[PoseCh.PD] += 0.03f * bk; Pose[PoseCh.CPIT] -= 3f * bk;   // блок: вес чуть назад, колени ниже
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
            blendLeft = Mathf.Max(0f, blendLeft - Time.deltaTime);
            if (Phase == SwingPhase.Idle && pivot != null) { ApplyRestPose(); return; }
            if (Phase == SwingPhase.Idle || pivot == null || CurrentProfile == null) return;

            phaseTime += Time.deltaTime;
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
                    else if (ContactWindowOpened != null) ContactWindowOpened(this);
                    break;
                case SwingPhase.Strike:
                    Phase = SwingPhase.FollowThrough;
                    break;
                case SwingPhase.FollowThrough:
                    Phase = SwingPhase.Recovery;
                    if (!CurrentProfile.ranged && ContactWindowClosed != null) ContactWindowClosed(this);
                    break;
                case SwingPhase.Recovery:
                    Phase = SwingPhase.Idle;
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
            Pose[PoseCh.HYAW] *= yawSign; Pose[PoseCh.CYAW] *= yawSign;
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
            Pose[PoseCh.HYAW] *= yawSign; Pose[PoseCh.CYAW] *= yawSign;
            ApplyBlend();
            VisualLunge = Pose[PoseCh.PF];
            pivot.localRotation = Quaternion.Euler(CurrentEuler);
            pivot.localPosition = pivotBasePos + new Vector3(0f, 0f, VisualLunge);
            PureLocalPosition = pivot.localPosition;
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }
    }
}
