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

        [Header("Dash")]
        public float dashSpeed = 6.5f;
        public float dashTime = 0.18f;
        public float dashCooldown = 0.9f;

        [Header("Ki (дух)")]
        public float kiMax = 100f;
        public float energyCost = 35f;      // шар духа тратит ки
        public float kiRegen = 2.5f;        // ед/с
        public float kiStart = 25f;
        /// <summary>Шкала духа: копится от попаданий и блоков, тратится на шар.</summary>
        public float Ki { get; private set; }
        public float Ki01 { get { return Mathf.Clamp01(Ki / Mathf.Max(1f, kiMax)); } }
        public bool CanEnergy { get { return Ki >= energyCost; } }
        Vector3 knockVel;
        /// <summary>Отбрасывание от удара: скорость гасится за ~0,3 с, стены арены и препятствия учитываются.</summary>
        public void Knockback(Vector3 dir, float speed)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = -transform.forward;
            knockVel = dir.normalized * speed;
        }
        public bool KiFull { get { return Ki >= kiMax - 0.5f; } }
        public void DrainKi() { Ki = 0f; }

        /// <summary>Мгновенно переставить бойца (супер-удар «сквозь» противника), с учётом границ арены.</summary>
        public void Teleport(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, -arenaHalfWidth, arenaHalfWidth);
            p.z = Mathf.Clamp(p.z, -arenaHalfDepth, arenaHalfDepth);
            p.y = transform.position.y;
            transform.position = p;
            if (opponent != null)
            {
                Vector3 to = opponent.transform.position - p; to.y = 0f;
                if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }
            knockVel = Vector3.zero; dashLeft = 0f;
        }

        public void AddKi(float v) { Ki = Mathf.Clamp(Ki + v, 0f, kiMax); }

        public bool Grounded { get; private set; }
        /// <summary>Боец повержен: не двигается и не бьёт, тело падает (HeroRigDriver).</summary>
        public bool Dead { get; set; }
        /// <summary>Заморожен менеджером дуэли (до начала боя / после победы / пауза).</summary>
        public bool Frozen { get; set; }
        /// <summary>Оглушён после попадания (сек. осталось).</summary>
        public float StunLeft { get { return Mathf.Max(0f, stunUntil - Time.time); } }
        public bool Stunned { get { return Time.time < stunUntil; } }
        public bool CanAct { get { return !Dead && !Frozen && !Stunned; } }
        /// <summary>Держит блок: клинок вертикально перед собой, урон сильно режется. Недоступно во время удара, оглушения и смерти.</summary>
        public bool Blocking { get; private set; }
        /// <summary>Сколько секунд подряд держится блок (для окна парирования).</summary>
        public float BlockTime { get; private set; }
        /// <summary>Блок «выдохся» — какое-то время нельзя поднять снова.</summary>
        public bool GuardBroken { get { return Time.time < guardBrokenUntil; } }
        public bool Dashing { get { return dashLeft > 0f; } }
        public float DashReady01 { get { return Mathf.Clamp01(1f - (dashReadyAt - Time.time) / dashCooldown); } }
        /// <summary>Скорость хода в этом кадре 0…1; знак минус, если идёт спиной к направлению взгляда — для аниматора.</summary>
        public float MoveAxis { get; private set; }

        float stunUntil, dashLeft, dashReadyAt, guardBrokenUntil;
        bool blockWanted;
        Vector3 dashDir;
        Vector2 moveInput;       // желаемое движение в мировых осях X/Z, длина 0…1
        float vy;
        Vector3 startPosition;
        Quaternion startRotation;

        void Awake() { startPosition = transform.position; startRotation = transform.rotation; Grounded = true; Ki = kiStart; }
        void OnEnable() { if (weapon != null) weapon.Released += OnRelease; }
        void OnDisable() { if (weapon != null) weapon.Released -= OnRelease; }

        /// <summary>Задать движение на этот кадр: вектор в мировых осях (x → X, y → Z), длина обрезается до 1.</summary>
        public void SetMoveVector(Vector2 worldXZ) { moveInput = Vector2.ClampMagnitude(worldXZ, 1f); }

        /// <summary>Хочет ли боец держать блок в этом кадре (вызывать каждый кадр).</summary>
        public void SetBlock(bool want) { blockWanted = want; }

        public void Stun(float seconds) { stunUntil = Mathf.Max(stunUntil, Time.time + seconds); }

        /// <summary>Рывок: в сторону вектора (мировой XZ); без вектора — шаг назад от противника.</summary>
        public bool Dash(Vector2 worldXZ)
        {
            if (!CanAct || Time.time < dashReadyAt || !Grounded) return false;
            Vector3 d = new Vector3(worldXZ.x, 0f, worldXZ.y);
            if (d.sqrMagnitude < 0.04f)
            {
                d = opponent != null ? transform.position - opponent.transform.position : -transform.forward;
                d.y = 0f;
            }
            if (d.sqrMagnitude < 1e-6f) d = -transform.forward;
            dashDir = d.normalized;
            dashLeft = dashTime;
            dashReadyAt = Time.time + dashCooldown;
            if (Juice.Instance != null) { Juice.Instance.DashSound(); Juice.Instance.Dust(transform.position + Vector3.up * 0.05f, -dashDir, 14); }
            return true;
        }

        public bool Jump()
        {
            if (!CanAct) return false;
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
            if (weapon == null || !CanAct) return false;
            AttackType t = type;
            if (!Grounded)
            {
                if (type == AttackType.LegSweep || type == AttackType.Thrust || type == AttackType.Slash || type == AttackType.SlashBack) return false;
                if (type == AttackType.Overhead || type == AttackType.Rising) t = AttackType.JumpStrike;
            }
            else if (type == AttackType.JumpStrike) t = AttackType.Overhead;
            if (t == AttackType.Energy && !CanEnergy) return false;
            bool ok = weapon.TryStartSwing(t);
            if (ok && t == AttackType.Energy) Ki -= energyCost;
            return ok;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!Dead && !Frozen) AddKi(kiRegen * dt);

            bool canBlock = CanAct && Grounded && weapon != null && weapon.Phase == SwingPhase.Idle && !GuardBroken;
            bool wasBlocking = Blocking;
            Blocking = canBlock && blockWanted;
            if (Blocking) { BlockTime += dt; if (BlockTime > 2.5f) { Blocking = false; guardBrokenUntil = Time.time + 1.2f; BlockTime = 0f; } }
            else BlockTime = 0f;
            blockWanted = false;

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
            if (Blocking) speed *= 0.45f;
            Vector3 mv = CanAct ? new Vector3(moveInput.x, 0f, moveInput.y) : Vector3.zero;
            float mag = Mathf.Min(1f, mv.magnitude);
            float sign = 1f;
            if (mag > 0.01f) { mv /= mv.magnitude; if (Vector3.Dot(mv, fwd) < -0.3f) sign = -1f; }
            MoveAxis = mag * sign;

            Vector3 delta = mv * (mag * speed * dt);
            if (dashLeft > 0f)
            {
                float dd = Mathf.Min(dt, dashLeft);
                delta += dashDir * (dashSpeed * dd);
                dashLeft -= dt;
            }
            if (knockVel.sqrMagnitude > 1e-4f)
            {
                delta += knockVel * dt;
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 10f * dt);
            }
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
            Ki = kiStart; knockVel = Vector3.zero;
            Dead = false; Frozen = false; Blocking = false; BlockTime = 0f; guardBrokenUntil = 0f; stunUntil = 0f; dashLeft = 0f; dashReadyAt = 0f;
            if (weapon != null) weapon.ResetState();
            if (zones != null) for (int i = 0; i < zones.Length; i++) if (zones[i] != null) zones[i].ClearFlash();
            EnergyProjectile.DestroyAll();
        }
    }
}
