using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Боец: свободное перемещение по площадке (плоскость XZ), всегда лицом к противнику, прыжок, границы арены,
    /// дистанция до противника, запуск атак. Логика попаданий здесь не живёт.
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
        public float arenaHalfWidth = 3.1f;     // половина площадки по X
        public float arenaHalfDepth = 2.3f;     // половина площадки по Z
        [Tooltip("Скорость разворота к противнику, град/с.")]
        public float turnSpeed = 420f;
        [Tooltip("Стартовый взгляд: +1 в +X, -1 в -X (дальше боец сам следит за противником).")]
        public int facing = 1;

        [Header("Jump")]
        public float jumpVelocity = 4.2f;       // м/с, вершина ≈ 0,73 м
        public float gravity = 12f;

        [Header("Energy ball")]
        public Material projectileMaterial;
        public float energySpeed = 7f;

        public bool Grounded { get; private set; }
        /// <summary>Скорость хода в этом кадре 0…1; знак минус, если идёт спиной к направлению взгляда — для аниматора.</summary>
        public float MoveAxis { get; private set; }

        Vector2 moveInput;       // желаемое движение в мировых осях X/Z, длина 0…1
        float vy;
        Vector3 startPosition;
        Quaternion startRotation;

        void Awake() { startPosition = transform.position; startRotation = transform.rotation; Grounded = true; }
        void OnEnable() { if (weapon != null) weapon.Released += OnRelease; }
        void OnDisable() { if (weapon != null) weapon.Released -= OnRelease; }

        /// <summary>Задать движение на этот кадр: вектор в мировых осях (x → X, y → Z), длина обрезается до 1.</summary>
        public void SetMoveVector(Vector2 worldXZ) { moveInput = Vector2.ClampMagnitude(worldXZ, 1f); }

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

            // взгляд: всегда на противника (по горизонтали)
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (opponent != null)
            {
                Vector3 to = opponent.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    fwd = to.normalized;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(fwd, Vector3.up), turnSpeed * dt);
                }
            }
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            float speed = moveSpeed;
            if (weapon != null && weapon.Phase != SwingPhase.Idle) speed *= attackMoveMultiplier;
            Vector3 mv = new Vector3(moveInput.x, 0f, moveInput.y);
            float mag = Mathf.Min(1f, mv.magnitude);
            float sign = 1f;
            if (mag > 0.01f) { mv /= mv.magnitude; if (Vector3.Dot(mv, fwd) < -0.3f) sign = -1f; }
            MoveAxis = mag * sign;

            Vector3 delta = mv * (mag * speed * dt);
            if (weapon != null) delta += fwd * (weapon.CurrentLunge * dt);   // бросок вперёд в ударе из прыжка
            if (delta.sqrMagnitude > 1e-10f) Move(delta);
            moveInput = Vector2.zero;
        }

        void Move(Vector3 delta)
        {
            Vector3 pos = transform.position;
            Vector3 np = pos + delta;
            np.x = Mathf.Clamp(np.x, -arenaHalfWidth, arenaHalfWidth);
            np.z = Mathf.Clamp(np.z, -arenaHalfDepth, arenaHalfDepth);

            // препятствия (жаровни и т.п.): выталкиваем наружу, получается скольжение вдоль
            for (int i = 0; i < ArenaObstacle.All.Count; i++)
            {
                ArenaObstacle o = ArenaObstacle.All[i];
                if (o == null) continue;
                Vector3 d = np - o.transform.position; d.y = 0f;
                float r = o.radius + 0.25f;
                float m = d.magnitude;
                if (m < r) np = o.transform.position + (m > 1e-4f ? d / m : Vector3.right) * r + Vector3.up * np.y;
            }

            if (opponent != null)
            {
                Vector3 op = opponent.transform.position;
                Vector3 d = np - op; d.y = 0f;
                Vector3 old = pos - op; old.y = 0f;
                float nd = d.magnitude;
                // нельзя сближаться за минимальную дистанцию (корпуса) — упираемся и скользим вокруг
                if (nd < minSeparation && nd < old.magnitude)
                    np = new Vector3(op.x, np.y, op.z) + (nd > 1e-4f ? d / nd : old.normalized) * minSeparation;
            }
            np.x = Mathf.Clamp(np.x, -arenaHalfWidth, arenaHalfWidth);
            np.z = Mathf.Clamp(np.z, -arenaHalfDepth, arenaHalfDepth);
            transform.position = np;
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
            transform.rotation = startRotation;
            vy = 0f;
            Grounded = true;
            moveInput = Vector2.zero;
            if (weapon != null) weapon.ResetState();
            if (zones != null) for (int i = 0; i < zones.Length; i++) if (zones[i] != null) zones[i].ClearFlash();
            EnergyProjectile.DestroyAll();
        }
    }
}
