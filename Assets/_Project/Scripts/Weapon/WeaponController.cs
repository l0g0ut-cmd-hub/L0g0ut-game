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
            Phase = SwingPhase.Windup;
            phaseTime = 0f;
            return true;
        }

        public void ResetState()
        {
            bool wasOpen = ContactWindowOpen;
            Phase = SwingPhase.Idle;
            phaseTime = 0f;
            ApplyRestPose();
            if (wasOpen && ContactWindowClosed != null) ContactWindowClosed(this);
        }

        void ApplyRestPose()
        {
            if (pivot == null) return;
            pivot.localRotation = Quaternion.Euler(restEuler);
            pivot.localPosition = pivotBasePos;
        }

        void Update()
        {
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
            float t = Mathf.Clamp01(phaseTime / Mathf.Max(0.0001f, PhaseDuration(Phase)));
            Vector3 rest = restEuler;
            Vector3 wind = Mirror(p.windupEuler), end = Mirror(p.strikeEndEuler), follow = Mirror(p.followEuler);
            Vector3 e;
            float offsetK;
            switch (Phase)
            {
                case SwingPhase.Windup:
                    e = Vector3.Lerp(rest, wind, Smooth(t));
                    offsetK = Smooth(t);
                    break;
                case SwingPhase.Strike:
                    // клинок разгоняется к концу удара
                    e = Vector3.Lerp(wind, end, Mathf.Pow(t, 1.5f));
                    offsetK = 1f;
                    break;
                case SwingPhase.FollowThrough:
                    e = Vector3.Lerp(end, follow, 1f - (1f - t) * (1f - t));
                    offsetK = 1f;
                    break;
                default: // Recovery
                    e = Vector3.Lerp(follow, rest, Smooth(t));
                    offsetK = 1f - Smooth(t);
                    break;
            }
            // Euler-интерполяция выбрана осознанно: слерп кватернионов при развороте ~180° неоднозначен.
            pivot.localRotation = Quaternion.Euler(e);
            Vector3 off = new Vector3(p.pivotOffset.x * yawSign, p.pivotOffset.y, p.pivotOffset.z);
            pivot.localPosition = pivotBasePos + off * offsetK;
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }
    }
}
