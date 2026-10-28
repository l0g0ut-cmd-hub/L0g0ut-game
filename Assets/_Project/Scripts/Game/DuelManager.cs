using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    public enum DuelState { Intro, Fighting, PlayerWon, PlayerLost }

    /// <summary>
    /// Правила дуэли: здоровье, урон от попаданий (по типу атаки и зоне), оглушение, KO, пауза.
    /// Попадание определяет CombatResolver (геометрия клинка); здесь только последствия.
    /// Один удар = одно попадание по цели (повторные касания того же удара не добавляют урона).
    /// </summary>
    public class DuelManager : MonoBehaviour
    {
        public static DuelManager Instance { get; private set; }

        public FighterController player, enemy;
        public float maxHp = 100f;
        public float introSeconds = 1.4f;
        public int winsToMatch = 2;                       // матч до двух побед
        public float nextRoundDelay = 2.8f;
        public int PlayerWins { get; private set; }
        public int EnemyWins { get; private set; }
        public int Round { get; private set; }
        public bool MatchOver { get; private set; }
        public float roundSeconds = 60f;
        /// <summary>Сколько секунд осталось в раунде.</summary>
        public float TimeLeft { get; private set; }
        /// <summary>Как закончился последний раунд: по времени / ничья / без единой царапины.</summary>
        public bool TimedOut { get; private set; }
        public bool LastDraw { get; private set; }
        public bool Perfect { get; private set; }

        public DuelState State { get; private set; }
        public bool Paused { get; private set; }
        public bool Slowmo { get; private set; }
        public float StateTime { get { return Time.unscaledTime - stateStart; } }

        public struct Popup { public Vector3 world; public string text; public Color color; public float born; public float size; }
        public readonly List<Popup> Popups = new List<Popup>();
        public event Action<FighterController, float> Damaged;

        readonly Dictionary<FighterController, float> hp = new Dictionary<FighterController, float>();
        readonly Dictionary<FighterController, float> hpTrail = new Dictionary<FighterController, float>();
        readonly Dictionary<FighterController, int> lastSwing = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, float> lastHitTime = new Dictionary<FighterController, float>();
        CombatResolver resolver;
        float stateStart;
        float slowUntil, hitStopUntil;
        float nextRoundAt;
        /// <summary>Сколько секунд до следующего раунда (для затемнения на HUD); отрицательно — не ждём.</summary>
        public float NextRoundIn { get { return (!MatchOver && (State == DuelState.PlayerWon || State == DuelState.PlayerLost)) ? nextRoundAt - Time.unscaledTime : -1f; } }
        float nextHeartAt;

        /// <summary>Статистика за матч (для финального экрана).</summary>
        public class Stats { public int damage, hits, maxCombo, blocks, parries, supers; }
        readonly Dictionary<FighterController, Stats> stats = new Dictionary<FighterController, Stats>();
        public Stats StatsOf(FighterController f)
        {
            Stats s;
            if (f == null) return new Stats();
            if (!stats.TryGetValue(f, out s)) { s = new Stats(); stats[f] = s; }
            return s;
        }
        readonly Dictionary<FighterController, int> combo = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, float> comboTime = new Dictionary<FighterController, float>();
        public int Combo(FighterController f) { int v; return combo.TryGetValue(f, out v) ? v : 0; }
        public float ComboAge(FighterController f) { float v; return comboTime.TryGetValue(f, out v) ? Time.unscaledTime - v : 99f; }

        /// <summary>Графика: 1 — высокая (мягкие тени, MSAA, 4 источника света), 0 — экономная для слабых телефонов.</summary>
        public static int Gfx
        {
            get { return PlayerPrefs.GetInt("gfx", 1); }
            set { PlayerPrefs.SetInt("gfx", value); ApplyQuality(); }
        }

        public static void ApplyQuality()
        {
            bool hi = Gfx == 1;
            QualitySettings.pixelLightCount = hi ? 4 : 1;
            QualitySettings.shadows = hi ? ShadowQuality.All : ShadowQuality.HardOnly;
            QualitySettings.shadowResolution = hi ? ShadowResolution.Medium : ShadowResolution.Low;
            QualitySettings.shadowDistance = hi ? 16f : 9f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.antiAliasing = hi ? 2 : 0;
            QualitySettings.anisotropicFiltering = hi ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;
            QualitySettings.lodBias = hi ? 1.5f : 0.8f;
        }

        void Awake()
        {
            Instance = this;
            // Android по умолчанию держит 30 кадров/с — для файтинга нужно 60
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            // профиль качества по умолчанию на Android — «Medium»: 1 пиксельный источник света, жёсткие тени.
            // Нужны мягкие тени от луны и живой свет жаровен/фонарей храма.
            ApplyQuality();
        }
        void Start()
        {
            Juice j = GetComponent<Juice>();
            if (j == null) j = gameObject.AddComponent<Juice>();
            if (player != null) j.Track(player.weapon);
            if (enemy != null) j.Track(enemy.weapon);
            if (GetComponent<Weather>() == null) gameObject.AddComponent<Weather>();
            if (GetComponent<Music>() == null) gameObject.AddComponent<Music>();
            ApplyStage();
            ResetDuel();
        }

        void OnDestroy() { Time.timeScale = 1f; if (resolver != null) resolver.HitRegistered -= OnHit; if (Instance == this) Instance = null; }

        public float MaxHp(FighterController f) { return f != null && f == enemy ? maxHp * Ladder.Current.hpMul : maxHp; }
        public float Hp(FighterController f) { float v; return hp.TryGetValue(f, out v) ? v : MaxHp(f); }
        public float Hp01(FighterController f) { return Mathf.Clamp01(Hp(f) / MaxHp(f)); }
        public float Trail01(FighterController f) { float v; return hpTrail.TryGetValue(f, out v) ? Mathf.Clamp01(v / MaxHp(f)) : 1f; }

        /// <summary>Применить ступень «Пути»: скорость, окрас и аура противника.</summary>
        float enemyBaseSpeed = -1f;
        System.Collections.Generic.List<Material> enemyMats;
        System.Collections.Generic.List<Color> enemyBaseColors;
        public void ApplyStage()
        {
            if (enemy == null) return;
            Ladder.Stage s = Ladder.Current;
            if (enemyBaseSpeed < 0f) enemyBaseSpeed = enemy.moveSpeed;
            enemy.moveSpeed = enemyBaseSpeed * s.speedMul;
            if (enemyMats == null)
            {
                enemyMats = new System.Collections.Generic.List<Material>();
                enemyBaseColors = new System.Collections.Generic.List<Color>();
                foreach (Renderer r in enemy.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                    if (enemy.weapon != null && enemy.weapon.bladeRoot != null && r.transform.IsChildOf(enemy.weapon.bladeRoot)) continue;
                    if (r.GetComponentInParent<BodyZone>() != null && r.GetComponent<BodyZone>() == null && r.sharedMaterial != null && r.sharedMaterial.name.Contains("Flash")) continue;
                    foreach (Material m in r.materials)
                        if (m != null && m.HasProperty("_Color")) { enemyMats.Add(m); enemyBaseColors.Add(m.color); }
                }
            }
            for (int i = 0; i < enemyMats.Count; i++) enemyMats[i].color = enemyBaseColors[i] * s.tint;
            if (Juice.Instance != null) Juice.Instance.SetAuraColor(enemy, s.aura);
        }
        public float SinceHit(FighterController f) { float v; return lastHitTime.TryGetValue(f, out v) ? Time.unscaledTime - v : 99f; }

        /// <summary>Новый матч: счёт раундов обнуляется.</summary>
        /// <summary>Матч выигран — следующий противник «Пути» (после последнего — снова с первого).</summary>
        public void NextStage()
        {
            Ladder.Index = Ladder.IsLast ? 0 : Ladder.Index + 1;
            ResetMatch();
        }

        public bool PlayerWonMatch { get { return MatchOver && PlayerWins >= winsToMatch; } }

        public void ResetMatch()
        {
            PlayerWins = 0; EnemyWins = 0; Round = 1; MatchOver = false;
            stats.Clear();
            ApplyStage();
            if (Weather.Instance != null) Weather.Instance.Roll();
            ResetDuel();
        }

        /// <summary>Новый раунд: здоровье и позиции сбрасываются, счёт остаётся.</summary>
        public void ResetDuel()
        {
            if (Round < 1) Round = 1;
            Time.timeScale = 1f;
            Paused = false; Slowmo = false; hitStopUntil = 0f;
            if (player != null) { player.ResetFighter(); hp[player] = maxHp; hpTrail[player] = maxHp; lastSwing[player] = -1; }
            if (enemy != null) { enemy.ResetFighter(); hp[enemy] = MaxHp(enemy); hpTrail[enemy] = MaxHp(enemy); lastSwing[enemy] = -1; }
            Popups.Clear();
            combo.Clear(); comboTime.Clear();
            SuperBy = null;
            if (Juice.Instance != null) Juice.Instance.CleanBlades();
            TimeLeft = roundSeconds; TimedOut = false; LastDraw = false; Perfect = false;
            if (Juice.Instance != null) Juice.Instance.Taiko(0.9f, 0.95f);
            SetState(DuelState.Intro);
            Freeze(true);
        }

        void SetState(DuelState s) { State = s; stateStart = Time.unscaledTime; }

        void Freeze(bool on)
        {
            if (player != null) player.Frozen = on;
            if (enemy != null) enemy.Frozen = on;
        }

        public void SetPaused(bool p) { Paused = p; ApplyTimeScale(); }

        void ApplyTimeScale()
        {
            if (Paused) Time.timeScale = 0f;
            else if (Time.unscaledTime < hitStopUntil) Time.timeScale = 0.03f;
            else Time.timeScale = Slowmo ? 0.3f : 1f;
        }

        void Update()
        {
            if (resolver == null && CombatResolver.Instance != null)
            {
                resolver = CombatResolver.Instance;
                resolver.HitRegistered += OnHit;
            }

            // «шлейф» полоски здоровья догоняет текущее значение с задержкой
            float dt = Time.unscaledDeltaTime;
            foreach (FighterController f in new List<FighterController>(hpTrail.Keys))
            {
                float cur = hp[f], tr = hpTrail[f];
                if (tr > cur && SinceHit(f) > 0.45f) tr = Mathf.MoveTowards(tr, cur, MaxHp(f) * 0.35f * dt);
                else if (tr < cur) tr = cur;
                hpTrail[f] = tr;
            }

            if (State == DuelState.Intro && StateTime >= introSeconds)
            {
                SetState(DuelState.Fighting); Freeze(false);
                if (Juice.Instance != null) Juice.Instance.Taiko(1f, 1.1f);
                Haptics.Pulse(40);
            }
            if (State == DuelState.Fighting && !Paused)
            {
                if (!SuperActive) TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
                if (TimeLeft <= 0f) TimeUp();
                // низкое здоровье игрока: стук сердца
                if (player != null && Hp01(player) < 0.3f && Time.unscaledTime >= nextHeartAt)
                {
                    nextHeartAt = Time.unscaledTime + Mathf.Lerp(0.6f, 1.1f, Hp01(player) / 0.3f);
                    if (Juice.Instance != null) Juice.Instance.Heartbeat();
                }
            }
            if (Slowmo && Time.unscaledTime >= slowUntil) Slowmo = false;
            if (!MatchOver && !Paused && (State == DuelState.PlayerWon || State == DuelState.PlayerLost) && Time.unscaledTime >= nextRoundAt) { Round++; ResetDuel(); return; }
            ApplyTimeScale();

            CheckClash();
            UpdateSuper();

            for (int i = Popups.Count - 1; i >= 0; i--) if (Time.unscaledTime - Popups[i].born > 1.0f) Popups.RemoveAt(i);
        }

        // ---------------------------------------------------------------- супер-удар «УДАР ДУХА»
        public const float SuperWindup = 0.6f, SuperEnd = 1.05f;
        public FighterController SuperBy { get; private set; }
        public bool SuperActive { get { return SuperBy != null; } }
        public float SuperTime { get { return Time.unscaledTime - superStart; } }
        public bool SuperLanded { get; private set; }
        float superStart;
        bool superStruck;
        Vector3 superFrom;

        /// <summary>Супер-удар на полном духе: короткая заставка, рывок сквозь противника, сокрушительный разрез.</summary>
        public bool TrySuper(FighterController f)
        {
            if (State != DuelState.Fighting || SuperActive || f == null || f.opponent == null) return false;
            if (!f.KiFull || !f.CanAct || !f.Grounded || f.opponent.Dead) return false;
            if (f.weapon != null && f.weapon.Phase != SwingPhase.Idle) return false;
            f.DrainKi();
            SuperBy = f; superStart = Time.unscaledTime; superStruck = false; SuperLanded = false;
            superFrom = f.transform.position;
            Freeze(true);
            if (f.opponent.weapon != null) f.opponent.weapon.ResetState();
            if (Juice.Instance != null) { Juice.Instance.Taiko(1f, 0.7f); Juice.Instance.SuperCharge(f.transform.position); }
            Haptics.Pulse(50);
            return true;
        }

        void UpdateSuper()
        {
            if (!SuperActive) return;
            FighterController a = SuperBy, t = a.opponent;
            float st = SuperTime;
            if (!superStruck && st >= SuperWindup)
            {
                superStruck = true;
                Vector3 dir = t.transform.position - a.transform.position; dir.y = 0f;
                float d = dir.magnitude;
                dir = d > 1e-3f ? dir / d : a.transform.forward;
                SuperLanded = d < 3.2f && !t.Dead;
                Vector3 end = SuperLanded ? t.transform.position + dir * 1.0f : a.transform.position + dir * Mathf.Min(2.2f, d - 0.8f);
                a.Teleport(end);
                if (a.weapon != null)
                {
                    a.weapon.ResetState();
                    a.weapon.TryStartSwing(AttackType.Slash);
                    lastSwing[a] = a.weapon.SwingId;          // попадания этого замаха уже учтены здесь
                }
                if (Juice.Instance != null)
                {
                    Juice.Instance.Streak(superFrom + Vector3.up * 1.0f, a.transform.position + Vector3.up * 1.0f, a == player);
                    Juice.Instance.Dust(superFrom + Vector3.up * 0.05f, dir, 18);
                }
                if (SuperLanded)
                {
                    Vector3 hp = t.transform.position + Vector3.up * 1.15f;
                    Freeze(false); t.Frozen = true;   // урон проходит по «живому» бойцу, двигаться он ещё не может
                    ApplyDamage(t, a, 32f, hp, true, false, -dir, Vector3.Cross(Vector3.up, dir) + dir * 0.4f, null);
                    StatsOf(a).supers++;
                    hitStopUntil = Time.unscaledTime + 0.22f;
                    if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.14f);
                    Haptics.Pulse(140);
                }
            }
            if (st >= SuperEnd)
            {
                SuperBy = null;
                if (State == DuelState.Fighting) Freeze(false);
            }
        }

        int lastClashA = -1, lastClashB = -1;

        /// <summary>Сшибка: оба бьют одновременно и клинки пересеклись — удары гасятся, бойцов отбрасывает.</summary>
        void CheckClash()
        {
            if (State != DuelState.Fighting || player == null || enemy == null) return;
            WeaponController a = player.weapon, b = enemy.weapon;
            if (a == null || b == null || a.bladeRoot == null || b.bladeRoot == null) return;
            if (a.Phase != SwingPhase.Strike || b.Phase != SwingPhase.Strike) return;
            if (a.SwingId == lastClashA && b.SwingId == lastClashB) return;
            Vector3 a0 = a.bladeRoot.position, a1 = a0 + a.bladeRoot.forward * a.bladeLength;
            Vector3 b0 = b.bladeRoot.position, b1 = b0 + b.bladeRoot.forward * b.bladeLength;
            Vector3 pa, pb;
            if (SegDist(a0, a1, b0, b1, out pa, out pb) > 0.09f) return;
            lastClashA = a.SwingId; lastClashB = b.SwingId;
            Vector3 p = (pa + pb) * 0.5f;
            a.ResetState(); b.ResetState();
            player.Stun(0.3f); enemy.Stun(0.3f);
            player.Knockback(player.transform.position - enemy.transform.position, 2.2f);
            enemy.Knockback(enemy.transform.position - player.transform.position, 2.2f);
            player.AddKi(8f); enemy.AddKi(8f);
            if (Juice.Instance != null) Juice.Instance.Clash(p);
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.07f);
            hitStopUntil = Time.unscaledTime + 0.12f;
            Haptics.Pulse(60);
            Popup cp = new Popup();
            cp.world = p + Vector3.up * 0.25f; cp.text = "СШИБКА!"; cp.color = new Color(1f, 0.9f, 0.5f);
            cp.born = Time.unscaledTime; cp.size = 34f;
            Popups.Add(cp);
        }

        /// <summary>Кратчайшее расстояние между отрезками и ближайшие точки.</summary>
        static float SegDist(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a < 1e-6f && e < 1e-6f) { c1 = p1; c2 = p2; return (c1 - c2).magnitude; }
            if (a < 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den > 1e-6f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s; c2 = p2 + d2 * t;
            return (c1 - c2).magnitude;
        }

        static float BaseDamage(AttackType t)
        {
            switch (t)
            {
                case AttackType.Overhead: return 24f;
                case AttackType.JumpStrike: return 26f;
                case AttackType.Rising: return 17f;
                case AttackType.LegSweep: return 13f;
                case AttackType.Thrust: return 19f;
                case AttackType.Slash:
                case AttackType.SlashBack: return 16f;
                case AttackType.Energy: return 15f;
            }
            return 15f;
        }

        static float ZoneMul(BodyZoneId z)
        {
            switch (z)
            {
                case BodyZoneId.Head: return 1.6f;
                case BodyZoneId.Torso: return 1.0f;
                case BodyZoneId.ArmLeft:
                case BodyZoneId.ArmRight: return 0.7f;
                default: return 0.6f;
            }
        }

        void OnHit(HitResult r)
        {
            if (State != DuelState.Fighting || r.attacker == null || r.target == null) return;
            if (r.attacker.Dead || r.target.Dead) return;
            int id = r.contact.swingId;
            int last;
            if (lastSwing.TryGetValue(r.attacker, out last) && last == id) return;   // один удар — одно попадание
            lastSwing[r.attacker] = id;

            AttackType type = r.attacker.weapon != null && r.attacker.weapon.CurrentProfile != null ? r.attacker.weapon.CurrentProfile.type : AttackType.Overhead;
            float dmg = BaseDamage(type) * ZoneMul(r.zone) * (r.glancing ? 0.5f : 1f) * (r.attacker == enemy ? Ladder.Current.dmgMul : 1f);
            dmg = Mathf.Round(dmg);
            if (r.target.Blocking)
            {
                bool parry = r.target.BlockTime < 0.25f;
                r.target.AddKi(parry ? 20f : 6f);
                if (parry) StatsOf(r.target).parries++; else StatsOf(r.target).blocks++;
                if (!parry) r.target.Knockback(r.target.transform.position - r.attacker.transform.position, 1.0f);
                else r.attacker.Knockback(r.attacker.transform.position - r.target.transform.position, 1.6f);
                if (r.target == player) Haptics.Pulse(parry ? 35 : 15);
                Popup bp = new Popup();
                bp.world = r.contact.point + Vector3.up * 0.2f;
                bp.text = parry ? "ПАРИРОВАНИЕ!" : "БЛОК";
                bp.color = parry ? new Color(0.4f, 0.9f, 1f) : new Color(0.75f, 0.85f, 1f);
                bp.born = Time.unscaledTime; bp.size = parry ? 30f : 24f;
                Popups.Add(bp);
                if (Juice.Instance != null) Juice.Instance.OnHit(r.contact.point, r.contact.normal, r.contact.bladeVelocity, 0.5f, true, false, null);
                if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(parry ? 0.05f : 0.025f);
                if (parry)
                {
                    if (r.attacker.weapon != null) r.attacker.weapon.ResetState();
                    r.attacker.Stun(0.8f);
                    hitStopUntil = Time.unscaledTime + 0.09f;
                }
                else ApplyDamage(r.target, r.attacker, Mathf.Max(1f, Mathf.Round(dmg * 0.15f)), r.contact.point, false, true);
                return;
            }
            ApplyDamage(r.target, r.attacker, dmg, r.contact.point, r.zone == BodyZoneId.Head, false, r.contact.normal, r.contact.bladeVelocity, r.contact.zone != null ? r.contact.zone.transform : null);
        }

        /// <summary>Время раунда вышло: побеждает тот, у кого больше здоровья; при равенстве — переигровка.</summary>
        void TimeUp()
        {
            TimedOut = true;
            float hpP = Hp(player), hpE = Hp(enemy);
            Freeze(true);
            if (player.weapon != null) player.weapon.ResetState();
            if (enemy.weapon != null) enemy.weapon.ResetState();
            nextRoundAt = Time.unscaledTime + nextRoundDelay;
            if (Mathf.Approximately(hpP, hpE)) { LastDraw = true; SetState(DuelState.PlayerLost); return; }
            bool playerLost = hpP < hpE;
            SetState(playerLost ? DuelState.PlayerLost : DuelState.PlayerWon);
            if (playerLost) EnemyWins++; else PlayerWins++;
            Perfect = playerLost ? hpE >= MaxHp(enemy) : hpP >= MaxHp(player);
            MatchOver = PlayerWins >= winsToMatch || EnemyWins >= winsToMatch;
            if (Juice.Instance != null) Juice.Instance.Taiko(1f, 0.8f);
        }

        void ApplyDamage(FighterController target, FighterController attacker, float dmg, Vector3 point, bool crit, bool chip = false, Vector3 normal = default(Vector3), Vector3 hitDir = default(Vector3), Transform zoneT = null)
        {
            if (!chip && attacker != null)
            {
                // серия: попадания подряд, пока соперник не ответил; каждое следующее чуть сильнее
                int c = ComboAge(attacker) < 1.6f ? Combo(attacker) + 1 : 1;
                combo[attacker] = c; comboTime[attacker] = Time.unscaledTime;
                combo[target] = 0;
                dmg = Mathf.Round(dmg * Mathf.Min(1.4f, 1f + 0.1f * (c - 1)));
                Stats st = StatsOf(attacker);
                st.hits++; st.damage += (int)Mathf.Min(dmg, Hp(target)); st.maxCombo = Mathf.Max(st.maxCombo, c);
                bool heavy = dmg >= 20f || dmg >= Hp(target);
                target.Knockback(target.transform.position - attacker.transform.position, dmg >= Hp(target) ? 3.2f : (heavy ? 2.4f : 1.4f));
                if (heavy && Juice.Instance != null) Juice.Instance.Dust(target.transform.position + Vector3.up * 0.05f, target.transform.position - attacker.transform.position, 10);
                attacker.AddKi(10f);
                if (Juice.Instance != null && attacker.weapon != null) Juice.Instance.BloodyBlade(attacker.weapon, 0.22f);
                target.AddKi(7f);
                Haptics.Pulse(target == player ? (dmg >= 20f ? 70 : 40) : 18);
            }
            hp[target] = Mathf.Max(0f, Hp(target) - dmg);
            lastHitTime[target] = Time.unscaledTime;
            Popup p = new Popup();
            p.world = point + Vector3.up * 0.15f;
            p.text = ((int)dmg).ToString();
            p.color = crit ? new Color(1f, 0.85f, 0.2f) : Color.white;
            p.born = Time.unscaledTime;
            p.size = crit ? 34f : 26f;
            Popups.Add(p);
            if (Damaged != null) Damaged(target, dmg);
            bool kill = Hp(target) <= 0f;
            if (!chip)
            {
                if (Juice.Instance != null) Juice.Instance.OnHit(point, normal, hitDir, Mathf.Clamp(dmg / 16f + (crit ? 0.4f : 0f) + (kill ? 0.5f : 0f), 0.4f, 2.2f), false, kill, zoneT);
                if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(kill ? 0.12f : (dmg >= 20f ? 0.07f : 0.04f));
                hitStopUntil = Time.unscaledTime + (kill ? 0.16f : (dmg >= 20f ? 0.08f : 0.05f));
            }

            // оглушение: рубящие выбивают из замаха сильнее
            if (!chip && target.weapon != null && target.weapon.Phase == SwingPhase.Windup) target.weapon.ResetState();
            if (!chip) target.Stun(dmg >= 20f ? 0.55f : 0.38f);

            if (Hp(target) <= 0f)
            {
                target.Dead = true;
                if (target.weapon != null) target.weapon.ResetState();
                bool playerLost = target == player;
                Haptics.Pulse(120);
                Perfect = attacker != null && Hp(attacker) >= MaxHp(attacker);
                SetState(playerLost ? DuelState.PlayerLost : DuelState.PlayerWon);
                if (playerLost) EnemyWins++; else PlayerWins++;
                MatchOver = PlayerWins >= winsToMatch || EnemyWins >= winsToMatch;
                nextRoundAt = Time.unscaledTime + nextRoundDelay;
                if (attacker != null) attacker.Frozen = true;
                Slowmo = true; slowUntil = Time.unscaledTime + 1.1f;
            }
        }
    }
}
