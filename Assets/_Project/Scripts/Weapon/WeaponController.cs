using System;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Управляет движением катаны: конечный автомат удара (замах → удар → проводка → восстановление).
    /// Анимация здесь — это только программное движение pivot; попадания определяет BladeTrajectorySampler.
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

        [Header("Swing poses (Euler pivot, локально бойцу; X = pitch, + вниз; Y = yaw)")]
        public Vector3 restEuler = new Vector3(-30f, 0f, 0f);
        public Vector3 windupEuler = new Vector3(-120f, 25f, 0f);   // yaw задаёт диагональ: сверху «справа» → вниз «налево»
        public Vector3 strikeEndEuler = new Vector3(55f, -25f, 0f);
        public Vector3 followEuler = new Vector3(85f, -30f, 0f);

        [Header("Swing timing, c")]
        public float windupTime = 0.28f;
        public float strikeTime = 0.16f;
        public float followTime = 0.08f;
        public float recoveryTime = 0.40f;

        public SwingPhase Phase { get; private set; }
        public int SwingId { get; private set; }
        public bool ContactWindowOpen { get { return Phase == SwingPhase.Strike || Phase == SwingPhase.FollowThrough; } }

        public event Action<WeaponController> ContactWindowOpened;
        public event Action<WeaponController> ContactWindowClosed;

        float phaseTime;

        void Awake() { ApplyRestPose(); }

        public bool TryStartSwing()
        {
            if (Phase != SwingPhase.Idle) return false;
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
            if (pivot != null) pivot.localRotation = Quaternion.Euler(restEuler);
        }

        void Update()
        {
            if (Phase == SwingPhase.Idle || pivot == null) return;

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
                case SwingPhase.Windup: return windupTime;
                case SwingPhase.Strike: return strikeTime;
                case SwingPhase.FollowThrough: return followTime;
                case SwingPhase.Recovery: return recoveryTime;
                default: return 0f;
            }
        }

        void AdvancePhase()
        {
            switch (Phase)
            {
                case SwingPhase.Windup:
                    Phase = SwingPhase.Strike;
                    if (ContactWindowOpened != null) ContactWindowOpened(this);
                    break;
                case SwingPhase.Strike:
                    Phase = SwingPhase.FollowThrough;
                    break;
                case SwingPhase.FollowThrough:
                    Phase = SwingPhase.Recovery;
                    if (ContactWindowClosed != null) ContactWindowClosed(this);
                    break;
                case SwingPhase.Recovery:
                    Phase = SwingPhase.Idle;
                    break;
            }
        }

        void ApplyPose()
        {
            if (Phase == SwingPhase.Idle) { ApplyRestPose(); return; }

            float t = Mathf.Clamp01(phaseTime / Mathf.Max(0.0001f, PhaseDuration(Phase)));
            Vector3 e;
            switch (Phase)
            {
                case SwingPhase.Windup:
                    e = Vector3.Lerp(restEuler, windupEuler, Smooth(t));
                    break;
                case SwingPhase.Strike:
                    // клинок разгоняется к концу удара
                    e = Vector3.Lerp(windupEuler, strikeEndEuler, Mathf.Pow(t, 1.5f));
                    break;
                case SwingPhase.FollowThrough:
                    e = Vector3.Lerp(strikeEndEuler, followEuler, 1f - (1f - t) * (1f - t));
                    break;
                default: // Recovery
                    e = Vector3.Lerp(followEuler, restEuler, Smooth(t));
                    break;
            }
            // Euler-интерполяция выбрана осознанно: слерп кватернионов при развороте ровно на ~180° неоднозначен.
            pivot.localRotation = Quaternion.Euler(e);
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }
    }
}
