using System;
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
        public float moveSpeed = 1.2f;   // размеренный шаг в стойке
        [Tooltip("Боком (приставным шагом) и спиной вперёд боец ходит медленнее, чем на противника.")]
        [Range(0.3f, 1f)] public float strafeSpeedMul = 0.6f, backSpeedMul = 0.85f;
        [Tooltip("Множитель скорости, пока идёт любая фаза удара. 1 = без штрафа.")]
        [Range(0f, 1f)] public float attackMoveMultiplier = 0.8f;
        public float minSeparation = 0.7f;      // ближе этого бойцы не сходятся (корпуса)
        /// <summary>Чужая пара на арене (соратник и его противник): сквозь них не проходим, держим avoidRadius.</summary>
        [NonSerialized] public FighterController[] avoid;
        /// <summary>Ждёт своей очереди и смотрит на чужой бой: разворачивается к нему, а не к своему противнику.</summary>
        [NonSerialized] public FighterController lookTarget;
        public float avoidRadius = 1.1f;
        public float arenaHalfWidth = 3.1f;     // половина площадки по X
        public float arenaHalfDepth = 2.3f;     // половина площадки по Z
        [Tooltip("Дальний край площадки (к ступеням храма), м: ближе, чем передний — иначе бойцы будто наступают на ступени.")]
        public float arenaBackDepth = 1.9f;
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
        float bumpAt, slideDustAt, edgeAt;
        float lastDashAt = -10f;
        /// <summary>Секунд с начала последнего рывка (удар из рывка — «иай»).</summary>
        public float SinceDash { get { return Time.time - lastDashAt; } }
        /// <summary>Отбрасывание от удара: скорость гасится за ~0,3 с, стены арены и препятствия учитываются.</summary>
        /// <summary>Забрать скорость отброса (её подхватывает физическое падение тела) — корень бойца дальше не скользит.</summary>
        /// <summary>Постановка (добивание с прыжка): боец несётся по заданной траектории; grounded — приземлился.</summary>
        public void Carry(Vector3 pos, bool grounded) { transform.position = pos; vy = 0f; Grounded = grounded; }

        public Vector3 TakeKnockback() { Vector3 v = knockVel; knockVel = Vector3.zero; return v; }

        public void Knockback(Vector3 dir, float speed)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = -transform.forward;
            knockVel = dir.normalized * speed;
        }
        /// <summary>Множитель накопления духа: «последний рубеж» при низком здоровье, особенности противника «Пути».</summary>
        public float KiRegenMul
        {
            get
            {
                float m = 1f;
                DuelManager dm = DuelManager.Instance;
                if (dm != null)
                {
                    if (dm.Hp01(this) < 0.25f) m *= 2.2f;
                    if (this == dm.enemy) m *= Ladder.Current.kiMul;
                }
                return m;
            }
        }
        public bool KiFull { get { return Ki >= kiMax - 0.5f; } }
        public void DrainKi() { Ki = 0f; }

        /// <summary>Мгновенно переставить бойца (супер-удар «сквозь» противника), с учётом границ арены.</summary>
        public void Teleport(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, -arenaHalfWidth, arenaHalfWidth);
            p.z = Mathf.Clamp(p.z, -arenaHalfDepth, Mathf.Min(arenaHalfDepth, arenaBackDepth));
            p.y = transform.position.y;
            transform.position = p;
            if (opponent != null)
            {
                Vector3 to = opponent.transform.position - p; to.y = 0f;
                if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }
            knockVel = Vector3.zero; dashLeft = 0f;
        }

        public void AddKi(float v) { if (v > 0f && IsPlayer) v *= Profile.B.kiGainMul; Ki = Mathf.Clamp(Ki + v, 0f, kiMax); }
        /// <summary>Это герой игрока (на него действуют снаряжение, характеристики и приёмы из Profile).</summary>
        public bool IsPlayer { get { DuelManager dm = DuelManager.Instance; return dm != null && dm.player == this; } }
        float DashCd { get { return dashCooldown * (IsPlayer ? Profile.B.dashCdMul : 1f); } }

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
        /// <summary>Окно отражения (парирования) от начала блока: 0,25 с, при частых нажатиях — уже.</summary>
        public float ParryWindow { get { return 0.25f / (1f + 0.8f * blockSpam) * (IsPlayer ? Profile.B.parryMul : 1f); } }
        int blockSpam; float lastBlockStart = -10f;
        /// <summary>Сколько секунд подряд держится блок (для окна парирования).</summary>
        public float BlockTime { get; private set; }
        /// <summary>Блок «выдохся» — какое-то время нельзя поднять снова.</summary>
        public bool GuardBroken { get { return Time.time < guardBrokenUntil || PostureBroken; } }

        [Header("Равновесие (posture, как в Sekiro)")]
        public float maxPosture = 100f;
        public float postureBreakTime = 2.0f;   // сколько боец «поплыл» после слома равновесия (окно добивания)
        /// <summary>Равновесие 0…maxPosture: растёт от принятых в блок ударов, отражённых противником атак и пропущенных ударов; полное — слом.</summary>
        public float Posture { get; private set; }
        public float Posture01 { get { return maxPosture > 0f ? Mathf.Clamp01(Posture / maxPosture) : 0f; } }
        public bool PostureBroken { get { return Time.time < postureBrokenUntil; } }
        public float SincePostureHit { get { return Time.time - postureHitAt; } }
        public event Action<FighterController> PostureBreak;
        float postureBrokenUntil, postureHitAt = -10f;

        public void AddPosture(float v)
        {
            if (Dead || PostureBroken || v <= 0f) return;
            Posture = Mathf.Min(maxPosture, Posture + v);
            postureHitAt = Time.time;
            if (Posture >= maxPosture)
            {
                postureBrokenUntil = Time.time + postureBreakTime;
                Blocking = false; BlockTime = 0f;
                Stun(postureBreakTime);
                if (weapon != null) weapon.ResetState();
                if (PostureBreak != null) PostureBreak(this);
            }
        }

        /// <summary>Здоровье в ноль: равновесие ломается само (открыто добивание), на seconds.</summary>
        public void ForceBreak(float seconds)
        {
            if (Dead) return;
            Posture = maxPosture;
            postureBrokenUntil = Time.time + seconds;
            Blocking = false; BlockTime = 0f;
            Stun(seconds);
            if (weapon != null) weapon.ResetState();
            if (PostureBreak != null) PostureBreak(this);
        }

        /// <summary>Равновесие снова целое (поднялся после добивания / сокрушающего удара).</summary>
        public void ResetPosture() { Posture = 0f; postureBrokenUntil = 0f; }

        /// <summary>Продлить слом равновесия (добивание идёт — противник не приходит в себя посреди приёма).</summary>
        public void HoldPostureBreak(float seconds)
        {
            if (!PostureBroken) return;
            postureBrokenUntil = Mathf.Max(postureBrokenUntil, Time.time + seconds);
            Stun(seconds);
        }

        /// <summary>Восстановление равновесия (скорость считает DuelManager: зависит от здоровья и блока).</summary>
        public void RecoverPosture(float v)
        {
            if (PostureBroken) return;
            Posture = Mathf.Max(0f, Posture - v);
        }
        public bool Dashing { get { return dashLeft > 0f; } }
        /// <summary>Накапливает удар (ChargeSystem): стоит на месте, атаки и рывки — только отпусканием.</summary>
        public bool Charging { get; set; }

        // ---- тыква-горлянка (как в Sekiro): глоток лечит, но ~1 с боец уязвим — клинок опущен, ход медленный; удар прерывает
        [Header("Тыква-горлянка")]
        public int gourdMax = 2;
        public int Gourds { get; set; }
        public const float DrinkTime = 1.15f, DrinkHealAt = 0.68f;
        float drinkStart = -10f, drinkUntil;
        public bool Drinking { get { return Time.time < drinkUntil; } }
        /// <summary>Сколько секунд идёт глоток (номер глотка — DrinkId, чтобы лечить один раз).</summary>
        public float DrinkT { get { return Time.time - drinkStart; } }
        public int DrinkId { get; private set; }
        public bool CanDrink { get { return CanAct && Grounded && !Drinking && Gourds > 0 && !Dashing && (weapon == null || weapon.Phase == SwingPhase.Idle); } }
        public bool TryDrink()
        {
            if (!CanDrink) return false;
            Gourds--; DrinkId++;
            drinkStart = Time.time; drinkUntil = Time.time + DrinkTime;
            Blocking = false; BlockTime = 0f;
            return true;
        }
        public void CancelDrink() { drinkUntil = 0f; }
        public float DashReady01 { get { return Mathf.Clamp01(1f - (dashReadyAt - Time.time) / DashCd); } }
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
            if (!CanAct || Drinking || Time.time < dashReadyAt || !Grounded) return false;
            Vector3 d = new Vector3(worldXZ.x, 0f, worldXZ.y);
            if (d.sqrMagnitude < 0.04f)
            {
                d = opponent != null ? transform.position - opponent.transform.position : -transform.forward;
                d.y = 0f;
            }
            if (d.sqrMagnitude < 1e-6f) d = -transform.forward;
            dashDir = d.normalized;
            dashLeft = dashTime;
            dashReadyAt = Time.time + DashCd;
            if (Dashed != null) Dashed(this, dashDir);
            lastDashAt = Time.time;
            if (DuelManager.Instance != null && DuelManager.Instance.player == this) PostFX.SpeedLines(0.55f);   // рывок героя — линии скорости
            if (Juice.Instance != null) { Juice.Instance.DashSound(); Juice.Instance.Dust(transform.position + Vector3.up * 0.05f, -dashDir, 14); Juice.Instance.LeafKick(transform.position, dashDir); Juice.Instance.DashFx(transform.position, dashDir); }
            return true;
        }

        public bool Jump()
        {
            if (!CanAct || Drinking) return false;
            if (!Grounded) return false;
            vy = jumpVelocity;
            Grounded = false;
            return true;
        }

        /// <summary>Идёт серия ударов (ставит ИИ): отражение посреди серии не оглушает — следующий удар уже в пути.</summary>
        public bool InString { get; set; }

        /// <summary>Рывок начался (направление в мире) — для микири.</summary>
        public event Action<FighterController, Vector3> Dashed;
        public void StopDash() { dashLeft = 0f; }

        /// <summary>Следующий удар серии прямо из восстановления после предыдущего (на земле, без шара духа).</summary>
        public bool Chain(AttackType type)
        {
            if (weapon == null || !CanAct || Drinking || !Grounded || type == AttackType.Energy || type == AttackType.JumpStrike) return false;
            if (weapon.Phase == SwingPhase.Idle) return Attack(type);
            return weapon.TryChain(type);
        }

        /// <summary>Удар сейчас, а если идёт восстановление после прошлого — продолжение серии.</summary>
        public bool AttackOrChain(AttackType type)
        {
            if (weapon != null && weapon.Phase == SwingPhase.Recovery && Grounded) return Chain(type);
            return Attack(type);
        }

        /// <summary>
        /// Начать атаку. В воздухе рубящие (Overhead/Rising) превращаются в удар в прыжке; по ногам из воздуха нельзя.
        /// </summary>
        public bool Attack(AttackType type, bool perilous = false)
        {
            if (weapon == null || !CanAct || Drinking) return false;
            AttackType t = type;
            if (!Grounded)
            {
                if (type == AttackType.LegSweep || type == AttackType.Thrust || type == AttackType.Slash || type == AttackType.SlashBack || type == AttackType.Charge) return false;
                if (type == AttackType.Overhead || type == AttackType.Rising) t = AttackType.JumpStrike;
            }
            else if (type == AttackType.JumpStrike) t = AttackType.Overhead;
            if (t == AttackType.Energy && !CanEnergy) return false;
            bool ok = weapon.TryStartSwing(t, perilous && (t == AttackType.Thrust || t == AttackType.LegSweep || t == AttackType.Charge));
            if (ok && t == AttackType.Energy) Ki -= energyCost;
            return ok;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!Dead && !Frozen) AddKi(kiRegen * KiRegenMul * dt * (IsPlayer ? Profile.B.kiRegenMul / Profile.B.kiGainMul : 1f));   // AddKi ещё умножит на kiGainMul

            bool canBlock = CanAct && Grounded && !Drinking && weapon != null && weapon.Phase == SwingPhase.Idle && !GuardBroken;
            bool wasBlocking = Blocking;
            Blocking = canBlock && blockWanted;
            if (Blocking && !wasBlocking)
            {
                // частые нажатия блока сужают окно отражения (как в Sekiro: «спам» не парирует)
                blockSpam = Time.time - lastBlockStart < 0.45f ? Mathf.Min(blockSpam + 1, 4) : 0;
                lastBlockStart = Time.time;
            }
            if (Blocking) BlockTime += dt;   // блок держится сколько угодно — ломается равновесием, а не временем
            else BlockTime = 0f;
            // равновесие после слома: снова в стойке, шкала наполовину
            if (postureBrokenUntil > 0f && !PostureBroken) { postureBrokenUntil = 0f; Posture = maxPosture * 0.4f; }
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
            FighterController faceT = lookTarget != null && lookTarget.isActiveAndEnabled ? lookTarget : opponent;
            if (faceT != null)
            {
                Vector3 to = faceT.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    fwd = to.normalized;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(fwd, Vector3.up), turnSpeed * dt);
                }
            }
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();

            float speed = moveSpeed * (IsPlayer ? Profile.B.moveMul : 1f);
            if (weapon != null && weapon.Phase != SwingPhase.Idle) speed *= attackMoveMultiplier;
            if (Blocking) speed *= 0.45f;
            if (Drinking) speed *= 0.35f;   // пьёт — еле переступает
            if (Charging) speed = 0f;        // накапливает удар — стоит на месте
            Vector3 mv = CanAct ? new Vector3(moveInput.x, 0f, moveInput.y) : Vector3.zero;
            float mag = Mathf.Min(1f, mv.magnitude);
            float sign = 1f;
            if (mag > 0.01f)
            {
                mv /= mv.magnitude;
                float fd = Vector3.Dot(mv, fwd);
                if (fd < -0.3f) sign = -1f;
                speed *= Mathf.Lerp(strafeSpeedMul, fd >= 0f ? 1f : backSpeedMul, fd * fd);
            }
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
                float ks = knockVel.magnitude;
                // тяжёлый отброс — боец скользит по камням дольше (торможение мягче), из-под ног летит пыль
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, (ks > 2.2f ? 6.5f : 10f) * dt);
                if (ks > 1.6f && Time.time > slideDustAt && Juice.Instance != null && Grounded)
                {
                    slideDustAt = Time.time + 0.06f;
                    Juice.Instance.Dust(transform.position + Vector3.up * 0.04f - knockVel.normalized * 0.15f, -knockVel, 2);
                }
            }
            if (weapon != null) delta += fwd * (weapon.CurrentLunge * dt);   // бросок вперёд в ударе из прыжка
            if (delta.sqrMagnitude > 1e-10f) Move(delta);
            moveInput = Vector2.zero;
        }

        void Move(Vector3 delta)
        {
            Vector3 pos = transform.position;
            Vector3 np = pos + delta;
            Vector3 raw = np;
            np.x = Mathf.Clamp(np.x, -arenaHalfWidth, arenaHalfWidth);
            np.z = Mathf.Clamp(np.z, -arenaHalfDepth, Mathf.Min(arenaHalfDepth, arenaBackDepth));
            // отброшенный тяжёлым ударом долетел до края двора: удар, пыль, отскок назад и короткая потеря опоры
            if (knockVel.sqrMagnitude > 4f && (raw.x != np.x || raw.z != np.z) && Time.time > edgeAt && !Dead)
            {
                edgeAt = Time.time + 0.8f;
                if (Juice.Instance != null) Juice.Instance.EdgeSlam(np, knockVel);
                Vector3 n = new Vector3(raw.x != np.x ? -Mathf.Sign(raw.x) : 0f, 0f, raw.z != np.z ? -Mathf.Sign(raw.z) : 0f).normalized;
                knockVel = Vector3.Reflect(knockVel, n) * 0.3f;
                Stun(0.35f);
            }

            // препятствия (жаровни и т.п.): выталкиваем наружу, получается скольжение вдоль
            for (int i = 0; i < ArenaObstacle.All.Count; i++)
            {
                ArenaObstacle o = ArenaObstacle.All[i];
                if (o == null) continue;
                Vector3 d = np - o.transform.position; d.y = 0f;
                float r = o.radius + 0.25f;
                float m = d.magnitude;
                // тяжёлый отброс сносит жаровню, даже если боец лишь задел её краем (зона шире корпуса)
                if (m < r + 0.45f && knockVel.sqrMagnitude > 4.84f && Time.time > bumpAt && Juice.Instance != null)
                { bumpAt = Time.time + 0.7f; Juice.Instance.ObstacleBump(o, knockVel); if (DuelManager.Instance != null) DuelManager.Instance.Burn(this, o.transform.position); }
                if (m < r)
                {
                    np = o.transform.position + (m > 1e-4f ? d / m : Vector3.right) * r + Vector3.up * np.y;
                    // отброшенный ударом врезался в жаровню: она качается, из чаши взлетает сноп искр
                    if (knockVel.sqrMagnitude > 4f && Time.time > bumpAt && Juice.Instance != null) { bumpAt = Time.time + 0.7f; Juice.Instance.ObstacleBump(o, knockVel); if (DuelManager.Instance != null) DuelManager.Instance.Burn(this, o.transform.position); }
                }
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
            if (avoid != null)
                for (int i = 0; i < avoid.Length; i++)
                {
                    FighterController o = avoid[i];
                    if (o == null || o == this || o == opponent || !o.isActiveAndEnabled) continue;
                    Vector3 op = o.transform.position;
                    Vector3 d = np - op; d.y = 0f;
                    Vector3 old = pos - op; old.y = 0f;
                    float nd = d.magnitude;
                    if (nd < avoidRadius && nd < old.magnitude + 1e-4f)
                        np = new Vector3(op.x, np.y, op.z) + (nd > 1e-4f ? d / nd : (old.sqrMagnitude > 1e-8f ? old.normalized : Vector3.right)) * avoidRadius;
                }
            np.x = Mathf.Clamp(np.x, -arenaHalfWidth, arenaHalfWidth);
            np.z = Mathf.Clamp(np.z, -arenaHalfDepth, Mathf.Min(arenaHalfDepth, arenaBackDepth));
            transform.position = np;
        }

        /// <summary>Место и разворот, куда боец встаёт в начале раунда (ResetFighter).</summary>
        public void SetHome(Vector3 p, Quaternion r) { startPosition = p; startRotation = r; }

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
            Posture = 0f; postureBrokenUntil = 0f; postureHitAt = -10f; blockSpam = 0; lastBlockStart = -10f; drinkUntil = 0f;
            if (weapon != null) weapon.ResetState();
            if (zones != null) for (int i = 0; i < zones.Length; i++) if (zones[i] != null) zones[i].ClearFlash();
            EnergyProjectile.DestroyAll();
        }
    }
}
