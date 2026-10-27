using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Боец: перемещение вдоль оси дуэли (мировая X), прыжок, границы арены, дистанция до противника,
    /// запуск атак. Логика попаданий здесь не живёт.
    /// Двигаться можно во время удара (скорость умножается на attackMoveMultiplier).
    /// </summary>
    public class FighterController : MonoBehaviour
    {
        public string displayName = "Fighter";
        public FighterController opponent;
        public WeaponController weapon;
        public BodyZone[] zones;

        [Header("Movement")]
        public float moveSpeed = 1.6f;
        [Tooltip("Множитель скорости, пока идёт любая фаза удара. 1 = без штрафа.")]
        [Range(0f, 1f)] public float attackMoveMultiplier = 0.8f;
        public float minSeparation = 0.7f;      // ближе этого бойцы не сходятся (корпуса)
        public float arenaHalfWidth = 2.4f;
        [Tooltip("+1: смотрит в +X, -1: смотрит в -X")]
        public int facing = 1;

        [Header("Jump")]
        public float jumpVelocity = 4.2f;       // м/с, вершина ≈ 0,73 м
        public float gravity = 12f;

        [Header("Energy ball")]
        public Material projectileMaterial;
        public float energySpeed = 7f;

        public bool Grounded { get; private set; }
        /// <summary>Ось хода в этом кадре (-1 назад .. +1 вперёд относительно взгляда) — для аниматора.</summary>
        public float MoveAxis { get; private set; }

        float forwardInput;      // -1 назад .. +1 вперёд (относительно facing)
        float vy;
        Vector3 startPosition;

        void Awake() { startPosition = transform.position; Grounded = true; }
        void OnEnable() { if (weapon != null) weapon.Released += OnRelease; }
        void OnDisable() { if (weapon != null) weapon.Released -= OnRelease; }

        /// <summary>Задать ось движения на этот кадр (+1 вперёд, -1 назад).</summary>
        public void SetMoveInput(float axis) { forwardInput = Mathf.Clamp(axis, -1f, 1f); }

        public bool Jump()
        {
            if (!Grounded) return false;
            vy = jumpVelocity;
            Grounded = false;
            return true;
        }

        /// <summary>
        /// Начать атаку. В воздухе рубящие (Overhead/Rising) превращаются в удар в прыжке; по ногам из воздуха нельзя.
        /// </summary>
        public bool Attack(AttackType type)
        {
            if (weapon == null) return false;
            AttackType t = type;
            if (!Grounded)
            {
                if (type == AttackType.LegSweep || type == AttackType.Thrust || type == AttackType.Slash || type == AttackType.SlashBack) return false;
                if (type == AttackType.Overhead || type == AttackType.Rising) t = AttackType.JumpStrike;
            }
            else if (type == AttackType.JumpStrike) t = AttackType.Overhead;
            return weapon.TryStartSwing(t);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (!Grounded)
            {
                vy -= gravity * dt;
                Vector3 p = transform.position;
                p.y += vy * dt;
                if (p.y <= 0f) { p.y = 0f; vy = 0f; Grounded = true; }
                transform.position = p;
            }

            float speed = moveSpeed;
            if (weapon != null && weapon.Phase != SwingPhase.Idle) speed *= attackMoveMultiplier;
            MoveAxis = forwardInput;
            float dx = forwardInput * facing * speed * dt;
            if (weapon != null) dx += facing * weapon.CurrentLunge * dt;   // бросок вперёд в ударе из прыжка
            if (Mathf.Abs(dx) > 1e-6f) Move(dx);
            forwardInput = 0f;
        }

        void Move(float dx)
        {
            Vector3 pos = transform.position;
            float newX = Mathf.Clamp(pos.x + dx, -arenaHalfWidth, arenaHalfWidth);

            if (opponent != null)
            {
                float ox = opponent.transform.position.x;
                float oldDist = Mathf.Abs(pos.x - ox);
                float newDist = Mathf.Abs(newX - ox);
                // нельзя сближаться за минимальную дистанцию и проходить сквозь противника
                bool closing = newDist < oldDist;
                bool crossing = Mathf.Sign(newX - ox) != Mathf.Sign(pos.x - ox);
                if (closing && (newDist < minSeparation || crossing)) return;
            }
            pos.x = newX;
            transform.position = pos;
        }

        void OnRelease(WeaponController w)
        {
            Vector3 dir = transform.forward;
            dir.y = 0f;
            dir = dir.normalized;
            Vector3 origin = w.pivot.position + dir * 0.55f;
            EnergyProjectile.Spawn(this, origin, dir * energySpeed, projectileMaterial);
        }

        public void ResetFighter()
        {
            transform.position = startPosition;
            vy = 0f;
            Grounded = true;
            forwardInput = 0f;
            if (weapon != null) weapon.ResetState();
            if (zones != null) for (int i = 0; i < zones.Length; i++) if (zones[i] != null) zones[i].ClearFlash();
            EnergyProjectile.DestroyAll();
        }
    }
}
