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
        /// <summary>Первый раунд матча — дольше: карточка противника «Пути».</summary>
        public float IntroLength { get { return Round <= 1 ? introSeconds + 1.3f : introSeconds; } }
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
        /// <summary>Босс в ярости (вторая фаза): быстрее, аура ярче.</summary>
        public bool Enraged { get; private set; }

        public DuelState State { get; private set; }
        public bool Paused { get; private set; }
        public bool Slowmo { get; private set; }
        public float StateTime { get { return Time.unscaledTime - stateStart; } }

        public struct Popup { public Vector3 world; public string text; public Color color; public float born; public float size; }
        public readonly List<Popup> Popups = new List<Popup>();
        public event Action<FighterController, float> Damaged;

        /// <summary>Крупные объявления по центру экрана: «ПЕРВАЯ КРОВЬ», «10 СЕКУНД!» и т.п.</summary>
        public string AnnounceText { get; private set; }
        public Color AnnounceColor { get; private set; }
        public float AnnounceAge { get { return Time.unscaledTime - announceAt; } }
        float announceAt = -10f;
        bool firstBlood, lastStandShown, tenSecShown;
        public void Announce(string text, Color c) { AnnounceText = text; AnnounceColor = c; announceAt = Time.unscaledTime; }

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
        int frames;

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
        readonly Dictionary<FighterController, float> counterUntil = new Dictionary<FighterController, float>();
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
            // арена ~10 м: короткая дистанция теней + высокое разрешение — чёткие контактные тени бойцов и кружево клёнов
            QualitySettings.shadowResolution = hi ? ShadowResolution.High : ShadowResolution.Low;
            QualitySettings.shadowDistance = hi ? 12f : 9f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.antiAliasing = hi ? 2 : 0;
            QualitySettings.anisotropicFiltering = hi ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable;
            QualitySettings.lodBias = hi ? 1.5f : 0.8f;
            QualitySettings.realtimeReflectionProbes = false;
            // скиннинг до 4 костей на вершину: по умолчанию на Android 2 — у плеч/шеи/таза веса обрезаются и сетку перекручивает
            QualitySettings.skinWeights = SkinWeights.FourBones;
        }

        void Awake()
        {
            Breadcrumbs.Mark("бой: Awake");
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
            Breadcrumbs.Mark("бой: эффекты");
            Juice j = GetComponent<Juice>();
            if (j == null) j = gameObject.AddComponent<Juice>();
            if (player != null) j.Track(player.weapon);
            if (enemy != null) j.Track(enemy.weapon);
            Breadcrumbs.Mark("бой: погода");
            if (GetComponent<Weather>() == null) gameObject.AddComponent<Weather>();
            Breadcrumbs.Mark("бой: музыка");
            if (GetComponent<Music>() == null) gameObject.AddComponent<Music>();
            Breadcrumbs.Mark("бой: ступень пути");
            ApplyStage();
            ResetDuel();
            Breadcrumbs.Mark("бой: первый кадр");
        }

        // свернули приложение / пришёл звонок — бой встаёт на паузу
        void OnApplicationPause(bool p) { if (p && (State == DuelState.Fighting || State == DuelState.Intro)) SetPaused(true); }
        void OnApplicationFocus(bool f) { if (!f && State == DuelState.Fighting) SetPaused(true); }

        void OnDestroy() { Time.timeScale = 1f; if (resolver != null) resolver.HitRegistered -= OnHit; if (Instance == this) Instance = null; }

        public float MaxHp(FighterController f) { return f != null && f == enemy ? maxHp * Ladder.Current.hpMul : maxHp; }
        public float Hp(FighterController f) { float v; return hp.TryGetValue(f, out v) ? v : MaxHp(f); }
        public float Hp01(FighterController f) { return Mathf.Clamp01(Hp(f) / MaxHp(f)); }
        public float Trail01(FighterController f) { float v; return hpTrail.TryGetValue(f, out v) ? Mathf.Clamp01(v / MaxHp(f)) : 1f; }

        /// <summary>Материалы тела бойца (без оружия) и их исходные цвета — для окраса ступени и «окровавленности».</summary>
        class BodyMats { public List<Material> mats = new List<Material>(); public List<Color> baseCol = new List<Color>(); public List<Color> baseEm = new List<Color>(); public Color tint = Color.white; public float blood; public Color applied = Color.white; public float flashAt = -10f, flashK; }
        readonly Dictionary<FighterController, BodyMats> bodyMats = new Dictionary<FighterController, BodyMats>();
        float enemyBaseSpeed = -1f;

        BodyMats MatsOf(FighterController f)
        {
            BodyMats b;
            if (bodyMats.TryGetValue(f, out b)) return b;
            b = new BodyMats();
            HashSet<Renderer> flash = new HashSet<Renderer>();   // подсветки зон попадания не трогаем — у них своя логика цвета
            foreach (BodyZone z in f.GetComponentsInChildren<BodyZone>(true)) if (z.flashRenderer != null) flash.Add(z.flashRenderer);
            foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
            {
                if (flash.Contains(r)) continue;
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                if (f.weapon != null && f.weapon.bladeRoot != null && r.transform.IsChildOf(f.weapon.bladeRoot)) continue;
                if (r.GetComponent<WeaponTrail>() != null) continue;
                foreach (Material m in r.materials)
                    if (m != null && m.HasProperty("_Color")) { b.mats.Add(m); b.baseCol.Add(m.color); b.baseEm.Add(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black); }
            }
            bodyMats[f] = b;
            return b;
        }

        /// <summary>Чем меньше здоровья, тем темнее и краснее тело — следы ран.</summary>
        void UpdateBodyTint(FighterController f)
        {
            if (f == null) return;
            BodyMats b = MatsOf(f);
            float target = (1f - Hp01(f)) * 0.45f;
            b.blood = Mathf.MoveTowards(b.blood, target, Time.unscaledDeltaTime * 0.8f);
            Color k = BloodCol(b.blood) * b.tint;
            // вспышка попадания по самой модели: красно-белое свечение на 0,15 с
            float fl = Mathf.Clamp01(1f - (Time.unscaledTime - b.flashAt) / 0.15f);
            if (fl > 0f) k = Color.Lerp(k, new Color(1f, 0.75f, 0.7f), fl * 0.6f);
            if (k == b.applied && fl == b.flashK) return;
            b.applied = k;
            Color em = new Color(0.9f, 0.08f, 0.04f) * (fl * 1.4f);
            bool emisChanged = fl > 0f || b.flashK > 0f;
            b.flashK = fl;
            for (int i = 0; i < b.mats.Count; i++)
            {
                b.mats[i].color = b.baseCol[i] * k;
                if (emisChanged && b.mats[i].HasProperty("_EmissionColor")) b.mats[i].SetColor("_EmissionColor", b.baseEm[i] + em);
            }
        }

        static Color BloodCol(float k) { return Color.Lerp(Color.white, new Color(0.62f, 0.2f, 0.18f), k); }

        /// <summary>Применить ступень «Пути»: скорость, окрас и аура противника.</summary>
        public void ApplyStage()
        {
            if (enemy == null) return;
            Ladder.Stage s = Ladder.Current;
            if (enemyBaseSpeed < 0f) enemyBaseSpeed = enemy.moveSpeed;
            enemy.moveSpeed = enemyBaseSpeed * s.speedMul;
            MatsOf(enemy).tint = s.tint;
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
            Resurrections = 1;   // 回生 — раз за матч
            firstBlood = false;
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
            combo.Clear(); comboTime.Clear(); counterUntil.Clear(); PerilAt.Clear(); perilSwing.Clear(); jumpedSweep.Clear(); DeathblowAt = -10f;
            Enraged = false;
            ApplyStage();   // скорость врага к базовой ступени (сброс ярости)
            SuperBy = null; DeathblowBy = null; DeathblowTarget = null; executing = false; ResurrectOffered = false;
            marks.Clear(); exhaustedUntil.Clear(); reviveUntil.Clear();
            if (player != null) marks[player] = 1;
            if (enemy != null) marks[enemy] = MaxMarks(enemy);
            if (Juice.Instance != null) Juice.Instance.CleanBlades();
            lastStandShown = false; tenSecShown = false;
            TimeLeft = roundSeconds + 30f * (MaxMarks(enemy) - 1); TimedOut = false;   // больше жизней у врага — больше времени LastDraw = false; Perfect = false;
            if (Juice.Instance != null)
            {
                Juice.Instance.Taiko(0.9f, 0.95f);
                if (Round <= 1) Juice.Instance.Growl(1f / Mathf.Sqrt(Ladder.Current.hpMul));   // чем крепче враг, тем ниже рык
            }
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

            Breadcrumbs.Frame();
            if (State == DuelState.Intro && StateTime >= IntroLength)
            {
                SetState(DuelState.Fighting); Freeze(false);
                if (Juice.Instance != null) Juice.Instance.Taiko(1f, 1.1f);
                Haptics.Pulse(40);
            }
            if (!postureHooked && player != null && enemy != null)
            {
                player.PostureBreak += OnPostureBreak; enemy.PostureBreak += OnPostureBreak;
                player.Dashed += OnDashed; enemy.Dashed += OnDashed;
                postureHooked = true;
            }
            WatchPeril(player); WatchPeril(enemy);
            if (State == DuelState.Fighting && !Paused)
            {
                // равновесие восстанавливается, когда по бойцу не бьют: медленнее при низком здоровье, быстрее в блоке
                RegenPosture(player); RegenPosture(enemy);
                UpdateLives(Time.unscaledDeltaTime);
                if (!SuperActive && !DeathblowActive && !ResurrectOffered) TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
                if (TimeLeft <= 0f) TimeUp();
                if (!tenSecShown && TimeLeft <= 10f) { tenSecShown = true; Announce("10 СЕКУНД!", new Color(1f, 0.4f, 0.25f)); }
                if (!lastStandShown && player != null && Hp01(player) < 0.25f && Hp(player) > 0f) { lastStandShown = true; Announce("ПОСЛЕДНИЙ РУБЕЖ", new Color(0.5f, 0.85f, 1f)); }
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
            UpdateCharge(player); UpdateCharge(enemy);
            UpdateSuper();
            UpdateDeathblow();
            UpdateResurrectOffer();
            UpdateBodyTint(player);
            UpdateBodyTint(enemy);
            Flourish();

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
            if (State != DuelState.Fighting || SuperActive || DeathblowActive || f == null || f.opponent == null) return false;
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
                if (State == DuelState.Fighting && !ResurrectOffered) Freeze(false);
            }
        }

        // ---------------------------------------------------------------- добивание (忍殺, как в Sekiro)
        /// <summary>
        /// Постановка добивания: приём, дистанция между бойцами и моменты по времени удара (с): контакт клинка с телом,
        /// развязка (смерть или падение на колено, если у врага остались метки жизни), тибури.
        /// </summary>
        struct DbKind { public AttackType type; public float distance, contact, outcome, flick; public bool cut, neck; }
        static readonly DbKind[] DbKinds =
        {
            new DbKind { type = AttackType.Deathblow, distance = 1.38f, contact = 0.44f, outcome = 1.10f, flick = 1.36f },                       // укол в грудь
            new DbKind { type = AttackType.DeathblowKesa, distance = 1.22f, contact = 0.45f, outcome = 0.72f, flick = 1.22f, cut = true },       // кэса-гири
            new DbKind { type = AttackType.DeathblowNeck, distance = 1.20f, contact = 0.46f, outcome = 0.52f, flick = 1.22f, cut = true, neck = true },   // по шее
            new DbKind { type = AttackType.DeathblowRising, distance = 1.22f, contact = 0.45f, outcome = 0.72f, flick = 1.18f, cut = true },     // снизу вверх
        };
        public FighterController DeathblowBy { get; private set; }
        public FighterController DeathblowTarget { get; private set; }
        public bool DeathblowActive { get { return DeathblowBy != null; } }
        /// <summary>Идущее добивание последнее (снимает последнюю метку) — жертва падает замертво.</summary>
        public bool DeathblowLethal { get; private set; }
        float dbStart; int dbStage, lastDbKind = -1; DbKind db;
        bool executing;   // урон приёма: здоровье в ноль убивает, а не открывает новое добивание

        // метки жизни (как у боссов Sekiro): добивание снимает одну, пока метки есть — враг поднимается с полным здоровьем
        readonly Dictionary<FighterController, int> marks = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, float> exhaustedUntil = new Dictionary<FighterController, float>();
        readonly Dictionary<FighterController, float> reviveUntil = new Dictionary<FighterController, float>();
        public int Marks(FighterController f) { int m; return f != null && marks.TryGetValue(f, out m) ? m : 1; }
        public int MaxMarks(FighterController f) { return f != null && f == enemy ? Mathf.Max(1, Ladder.Current.marks) : 1; }
        /// <summary>Поднимается после добивания (здоровье наполняется, удары не проходят).</summary>
        public bool Reviving(FighterController f) { float u; return f != null && reviveUntil.TryGetValue(f, out u) && Time.unscaledTime < u; }
        /// <summary>Доля здоровья игрока, которую снимает сокрушающий удар по сломленному равновесию (казнь — только если это добьёт).</summary>
        const float CrushFrac = 0.35f;

        /// <summary>
        /// Удар по сломленному равновесию — постановка: оба замирают, атакующий подшагивает на дистанцию и проводит один из приёмов
        /// (укол, кэса-гири, по шее, снизу; подряд один и тот же не повторяется). Последняя метка — смерть (по шее — голова с плеч),
        /// иначе враг падает на колено и поднимается с полным здоровьем.
        /// </summary>
        bool TryDeathblow(FighterController a, FighterController t)
        {
            if (DeathblowActive || SuperActive || a == null || t == null || a.weapon == null) return false;
            if (!a.Grounded || !t.Grounded || a.Dead || t.Dead || Reviving(t)) return false;
            DeathblowLethal = Marks(t) <= 1;
            // выбор приёма: голову рубим только последним добиванием
            int pick = -1;
            for (int tries = 0; tries < 8; tries++)
            {
                int k = UnityEngine.Random.Range(0, DbKinds.Length);
                if (DbKinds[k].neck && !DeathblowLethal) continue;
                if (k == lastDbKind && tries < 7) continue;
                pick = k; break;
            }
            if (pick < 0) pick = 0;
            lastDbKind = pick; db = DbKinds[pick];
            DeathblowBy = a; DeathblowTarget = t; dbStart = Time.unscaledTime; dbStage = 0;
            Freeze(true);
            a.TakeKnockback(); t.TakeKnockback(); a.StopDash(); t.StopDash();
            t.HoldPostureBreak(3.5f);
            EnergyProjectile.DestroyAll();
            if (t.weapon != null) t.weapon.ResetState();
            a.weapon.ResetState();
            a.weapon.TryStartSwing(db.type);
            lastSwing[a] = a.weapon.SwingId;
            if (DeathblowLethal) flourishDone = true;   // тибури — часть приёма
            Announce(DeathblowLethal ? "СМЕРТЕЛЬНЫЙ УДАР" : "ДОБИВАНИЕ", new Color(0.95f, 0.08f, 0.06f));
            if (Juice.Instance != null) Juice.Instance.Taiko(1f, 0.72f);
            hitStopUntil = Time.unscaledTime + 0.07f;
            Haptics.Pulse(50);
            return true;
        }

        static Transform TorsoOf(FighterController f)
        {
            if (f.zones != null) foreach (BodyZone z in f.zones) if (z != null && z.zoneId == BodyZoneId.Torso) return z.transform;
            return f.transform;
        }
        static Transform HeadOf(FighterController f)
        {
            if (f.zones != null) foreach (BodyZone z in f.zones) if (z != null && z.zoneId == BodyZoneId.Head) return z.transform;
            return null;
        }

        void UpdateDeathblow()
        {
            if (!DeathblowActive) return;
            FighterController a = DeathblowBy, t = DeathblowTarget;
            WeaponController w = a.weapon;
            bool on = w != null && w.Phase != SwingPhase.Idle && w.CurrentProfile != null && w.CurrentProfile.type == db.type;
            float st = on ? w.SwingTime : 99f;
            Vector3 dir = t.transform.position - a.transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : a.transform.forward;
            if (on && st < db.contact && !t.Dead)
            {
                // подшаг на дистанцию приёма, лицом к лицу (до контакта успевает: экспонента ~0,1 с)
                Vector3 want = t.transform.position - dir * db.distance; want.y = a.transform.position.y;
                a.transform.position = Vector3.Lerp(a.transform.position, want, 1f - Mathf.Exp(-14f * Time.deltaTime));
                a.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                t.transform.rotation = Quaternion.RotateTowards(t.transform.rotation, Quaternion.LookRotation(-dir, Vector3.up), 360f * Time.deltaTime);
            }
            Transform torso = TorsoOf(t), head = HeadOf(t);
            Vector3 aim = db.neck && head != null ? head.position - Vector3.up * 0.14f : torso.position;
            Vector3 hit = aim;
            if (w != null && w.bladeRoot != null)
            {
                // точка контакта — на клинке напротив груди (шеи)
                Vector3 b0 = w.bladeRoot.position, ax = w.bladeRoot.forward;
                hit = b0 + ax * Mathf.Clamp(Vector3.Dot(aim - b0, ax), 0f, w.bladeLength);
            }
            HeroRigDriver rig = t.GetComponentInChildren<HeroRigDriver>();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (dbStage == 0 && st >= db.contact)
            {
                dbStage = 1;
                DeathblowAt = Time.unscaledTime; DeathblowPos = hit;
                if (Juice.Instance != null)
                {
                    if (!db.cut)
                    {
                        Juice.Instance.OnHit(hit + dir * 0.16f, dir, dir, 0.85f, false, false, null);   // кровь из спины по ходу клинка
                        Juice.Instance.StabSound();
                    }
                    else
                    {
                        // рез: веер крови по ходу клинка (рез справа налево — влево от атакующего)
                        Vector3 cutDir = (-side * 0.8f + (db.type == AttackType.DeathblowRising ? Vector3.up * 0.5f : Vector3.down * (db.neck ? 0.05f : 0.5f))).normalized;
                        Juice.Instance.OnHit(hit, -dir, cutDir * 6f, 1.5f, false, false, db.neck ? null : torso);
                    }
                    Juice.Instance.BloodyBlade(w, 0.7f);
                }
                if (rig != null) rig.Recoil(db.cut ? (dir - side * 0.6f).normalized : dir, hit, 1.15f, db.neck ? BodyZoneId.Head : BodyZoneId.Torso);
                if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Shake(0.06f); CameraOrbit.Instance.Punch(4f); }
                PostFX.Kick(0.014f);
                hitStopUntil = Time.unscaledTime + (db.cut ? 0.12f : 0.14f);
                Haptics.Pulse(t == player ? 120 : 80);
            }
            if (dbStage == 1 && st >= db.outcome)
            {
                dbStage = 2;
                if (Camera.main != null && Vector3.Dot(side, Camera.main.transform.forward) < 0f) side = -side;
                if (DeathblowLethal)
                {
                    // падение: прочь от атакующего и вбок — от камеры, чтобы тело не легло между ней и победителем
                    Vector3 fall = (dir * 0.7f + side * 0.6f).normalized;
                    lastHitHead = db.neck;   // по шее — голова с плеч (Decapitate в ApplyDamage)
                    marks[t] = 0;
                    executing = true;
                    if (!t.Dead) ApplyDamage(t, a, Mathf.Max(1f, Hp(t)), hit, true, false, -dir, db.cut ? -side * 3f : -dir * 3f, torso != t.transform ? torso : null);
                    executing = false;
                    flourishDone = true;   // тибури победителя — уже часть приёма
                    t.TakeKnockback(); t.Knockback(fall, 1.1f);
                    if (rig != null) rig.Recoil(fall, hit, 0.7f);
                    if (Juice.Instance != null) { if (!db.cut) Juice.Instance.SquelchSound(); Juice.Instance.Dust(t.transform.position + Vector3.up * 0.05f, fall, 8); }
                }
                else
                {
                    // метка снята, но враг жив: отшатнулся, рухнул на колено, поднимается с полным здоровьем
                    marks[t] = Marks(t) - 1;
                    t.ResetPosture();
                    t.TakeKnockback(); t.Knockback(dir, 1.3f);
                    t.Stun(1.7f);
                    if (t.weapon != null) { t.weapon.ResetState(); t.weapon.Down(1.5f); }
                    reviveUntil[t] = Time.unscaledTime + 1.7f;
                    exhaustedUntil.Remove(t);
                    if (rig != null) rig.Recoil(dir, hit, 0.9f);
                    if (Juice.Instance != null)
                    {
                        if (!db.cut) Juice.Instance.SquelchSound();
                        Juice.Instance.OnHit(hit, -dir, -dir * 3f, 1.0f, false, false, torso != t.transform ? torso : null);
                        Juice.Instance.Growl(0.8f);
                        Juice.Instance.Dust(t.transform.position + Vector3.up * 0.05f, dir, 12);
                    }
                    StatsOf(a).hits++;
                    int left = Marks(t);
                    Announce(left > 1 ? "ВРАГ ПОДНИМАЕТСЯ — ЕЩЁ " + left + " ДОБИВАНИЯ" : "ВРАГ ПОДНИМАЕТСЯ — ПОСЛЕДНЯЯ ЖИЗНЬ", new Color(1f, 0.5f, 0.2f));
                    if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.05f);
                    // с каждой снятой меткой враг злее
                    if (t == enemy) { enemy.moveSpeed *= 1.06f; enemy.AddKi(30f); }
                }
            }
            if (dbStage == 2 && st >= db.flick)
            {
                dbStage = 3;
                if (Juice.Instance != null && w != null)
                {
                    Juice.Instance.ShakeBlood(w);
                    Juice.Instance.DashSound();
                    if (w.bladeRoot != null) Juice.Instance.Drip(w.bladeRoot.position + w.bladeRoot.forward * w.bladeLength * 0.7f);
                }
            }
            if (!on || Time.unscaledTime - dbStart > 8f)
            {
                DeathblowBy = null; DeathblowTarget = null;
                if (State == DuelState.Fighting && !ResurrectOffered) Freeze(false);
            }
        }

        // ---------------------------------------------------------------- натиск (тай-атари): бьёт телом, блок не держит
        readonly Dictionary<FighterController, int> chargeDone = new Dictionary<FighterController, int>();
        void UpdateCharge(FighterController a)
        {
            if (a == null || State != DuelState.Fighting || DeathblowActive) return;
            WeaponController w = a.weapon;
            if (w == null || w.CurrentProfile == null || w.CurrentProfile.type != AttackType.Charge || w.Phase != SwingPhase.Strike) return;
            int done;
            if (chargeDone.TryGetValue(a, out done) && done == w.SwingId) return;
            FighterController t = a.opponent;
            if (t == null || t.Dead || Reviving(t)) return;
            Vector3 d = t.transform.position - a.transform.position; d.y = 0f;
            if (d.magnitude > 0.95f) return;
            chargeDone[a] = w.SwingId;
            if (t.Dashing || t.SinceDash < 0.25f)
            {
                // ушёл рывком: натиск проскочил, атакующий открыт
                a.Stun(0.7f); a.AddPosture(25f); t.AddKi(12f);
                Popup up = new Popup(); up.world = t.transform.position + Vector3.up * 1.9f; up.text = "УКЛОНЕНИЕ!"; up.color = new Color(0.5f, 0.95f, 1f); up.born = Time.unscaledTime; up.size = 28f; Popups.Add(up);
                Slowmo = true; slowUntil = Time.unscaledTime + 0.3f;
                return;
            }
            // сбит с ног: урон, равновесие, на колено
            float dmg = Mathf.Round(18f * (a == enemy ? Ladder.Current.dmgMul * Records.Assist : 1f));
            ApplyDamage(t, a, dmg, t.transform.position + Vector3.up * 1.1f, true, false, -d.normalized, d.normalized * 4f, null);
            if (!t.Dead)
            {
                t.AddPosture(35f);
                t.Knockback(d, 3.6f);
                t.Stun(0.9f);
                if (t.weapon != null) { t.weapon.ResetState(); t.weapon.Down(0.8f); }
            }
            if (Juice.Instance != null) { Juice.Instance.Dust(t.transform.position + Vector3.up * 0.05f, d, 18); Juice.Instance.Taiko(0.8f, 1.2f); }
            if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Shake(0.1f); CameraOrbit.Instance.Punch(3f); }
            PostFX.Kick(0.008f);
            Haptics.Pulse(t == player ? 110 : 40);
            a.StopDash();
        }

        /// <summary>Подъём после добивания (здоровье наполняется за 1,2 с) и выход из «пустого» здоровья без добивания.</summary>
        void UpdateLives(float dt)
        {
            foreach (FighterController f in new[] { player, enemy })
            {
                if (f == null || f.Dead) continue;
                if (Reviving(f)) { hp[f] = Mathf.MoveTowards(Hp(f), MaxHp(f), MaxHp(f) * dt / 1.2f); hpTrail[f] = Mathf.Max(hpTrail[f], hp[f]); }
                float eu;
                if (exhaustedUntil.TryGetValue(f, out eu) && Time.time > eu && !DeathblowActive)
                {
                    // не успел добить — враг пришёл в себя с частью здоровья
                    exhaustedUntil.Remove(f);
                    if (Hp(f) <= 0f) { hp[f] = Mathf.Round(MaxHp(f) * 0.3f); hpTrail[f] = hp[f]; }
                    f.ResetPosture();
                    Announce("ВРАГ ОПРАВИЛСЯ", new Color(1f, 0.6f, 0.3f));
                }
            }
        }

        /// <summary>Шар отбит клинком: дух за мастерство, надпись, стоп-кадр.</summary>
        public void OnDeflect(Vector3 pos, FighterController by)
        {
            by.AddKi(15f);
            hitStopUntil = Time.unscaledTime + 0.08f;
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.04f);
            if (by == player) Haptics.Pulse(35);
            Popup p = new Popup();
            p.world = pos + Vector3.up * 0.3f; p.text = "ОТРАЖЕНО!"; p.color = new Color(0.6f, 0.95f, 1f);
            p.born = Time.unscaledTime; p.size = 28f;
            Popups.Add(p);
        }

        bool lastHitHead;
        bool flourishDone;
        /// <summary>Тибури: победитель стряхивает кровь с клинка резким взмахом.</summary>
        void Flourish()
        {
            if (State == DuelState.Fighting || State == DuelState.Intro) { flourishDone = false; return; }
            if (flourishDone || TimedOut || LastDraw || StateTime < 0.9f || DeathblowActive) return;
            flourishDone = true;
            FighterController w = State == DuelState.PlayerWon ? player : enemy;
            if (w == null || w.Dead || w.weapon == null) return;
            w.weapon.ResetState();
            w.weapon.TryStartSwing(AttackType.SlashBack);
            lastSwing[w] = w.weapon.SwingId;
            if (Juice.Instance != null)
            {
                Juice.Instance.ShakeBlood(w.weapon);
                if (w.weapon.bladeRoot != null) Juice.Instance.Drip(w.weapon.bladeRoot.position + w.weapon.bladeRoot.forward * w.weapon.bladeLength * 0.7f);
            }
        }

        int lastClashA = -1, lastClashB = -1;

        /// <summary>Сшибка: оба бьют одновременно и клинки пересеклись — удары гасятся, бойцов отбрасывает.</summary>
        void CheckClash()
        {
            if (State != DuelState.Fighting || player == null || enemy == null || DeathblowActive) return;
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

        /// <summary>Урон по равновесию от удара, принятого в блок (отражённый удар бьёт по равновесию атакующего).</summary>
        static float PostureDamage(AttackType t)
        {
            switch (t)
            {
                case AttackType.Overhead: return 30f;
                case AttackType.JumpStrike: return 36f;
                case AttackType.Thrust: return 26f;
                case AttackType.Rising: return 24f;
                case AttackType.LegSweep: return 20f;
                case AttackType.Energy: return 18f;
                default: return 22f;
            }
        }

        bool postureHooked;
        /// <summary>Добивание (для HUD: знак 斬 на месте удара).</summary>
        public float DeathblowAt { get; private set; } = -10f;
        public Vector3 DeathblowPos { get; private set; }
        /// <summary>Опасная атака (危): когда показан знак над атакующим (для HUD).</summary>
        public readonly Dictionary<FighterController, float> PerilAt = new Dictionary<FighterController, float>();
        readonly Dictionary<FighterController, int> perilSwing = new Dictionary<FighterController, int>();
        readonly Dictionary<FighterController, int> jumpedSweep = new Dictionary<FighterController, int>();
        static int perilTips;

        void WatchPeril(FighterController f)
        {
            if (f == null || f.weapon == null || !f.weapon.Perilous) return;
            int last;
            if (perilSwing.TryGetValue(f, out last) && last == f.weapon.SwingId) return;
            perilSwing[f] = f.weapon.SwingId;
            PerilAt[f] = Time.unscaledTime;
            if (Juice.Instance != null) Juice.Instance.Danger();
            if (f == enemy && perilTips < 2 && PlayerPrefs.GetInt("peril_tip", 0) < 3)
            {
                perilTips++; PlayerPrefs.SetInt("peril_tip", PlayerPrefs.GetInt("peril_tip", 0) + 1);
                AttackType pt = f.weapon.CurrentProfile != null ? f.weapon.CurrentProfile.type : AttackType.Thrust;
                Announce(pt == AttackType.LegSweep ? "ОПАСНО: ПОДСЕЧКА — ПРЫГАЙ!" : pt == AttackType.Charge ? "ОПАСНО: НАТИСК — РЫВОК ВБОК!" : "ОПАСНО: ВЫПАД — РЫВОК НАВСТРЕЧУ!", new Color(1f, 0.25f, 0.15f));
            }
        }

        /// <summary>Микири: рывок навстречу опасному выпаду — клинок противника прижат к земле, он открыт и теряет равновесие.</summary>
        void OnDashed(FighterController f, Vector3 dir)
        {
            if (State != DuelState.Fighting) return;
            FighterController o = f == player ? enemy : player;
            if (o == null || o.weapon == null || !o.weapon.Perilous || o.weapon.CurrentProfile == null || o.weapon.CurrentProfile.type != AttackType.Thrust) return;
            SwingPhase ph = o.weapon.Phase;
            bool window = (ph == SwingPhase.Windup && o.weapon.PhaseProgress > 0.35f) || ph == SwingPhase.Strike;
            Vector3 to = o.transform.position - f.transform.position; to.y = 0f;
            if (!window || to.magnitude > 2.6f || Vector3.Dot(dir.normalized, to.normalized) < 0.35f) return;
            f.StopDash();
            o.weapon.ResetState();
            o.weapon.Pin(1.1f);   // клинок врага прижат к земле
            if (f.weapon != null) { f.weapon.ResetState(); f.weapon.TryStartSwing(AttackType.Mikiri); lastSwing[f] = f.weapon.SwingId; }   // наступил на клинок
            if (Juice.Instance != null) { Juice.Instance.Footstep(f.transform.position + f.transform.forward * 0.45f, 1.6f); Juice.Instance.Dust(f.transform.position + f.transform.forward * 0.5f + Vector3.up * 0.05f, Vector3.up, 14); }
            o.Stun(1.3f);
            o.AddPosture(45f);
            f.AddKi(15f);
            Popup mp = new Popup();
            mp.world = o.transform.position + Vector3.up * 1.6f; mp.text = "МИКИРИ!"; mp.color = new Color(0.5f, 0.95f, 1f);
            mp.born = Time.unscaledTime; mp.size = 32f;
            Popups.Add(mp);
            if (Juice.Instance != null) Juice.Instance.Clash(o.transform.position + Vector3.up * 0.25f + o.transform.forward * 0.4f);
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.06f);
            Haptics.Pulse(60);
            hitStopUntil = Time.unscaledTime + 0.12f;
            Slowmo = true; slowUntil = Time.unscaledTime + 0.5f;
            counterUntil[f] = Time.unscaledTime + 1.0f;
        }
        void RegenPosture(FighterController f)
        {
            if (f != null && f.SincePostureHit > 1.0f)
                f.RecoverPosture(Time.deltaTime * 14f * (0.35f + 0.65f * Hp01(f)) * (f.Blocking ? 1.6f : 1f));
        }
        void OnPostureBreak(FighterController f)
        {
            if (State != DuelState.Fighting) return;
            bool isPlayer = f == player;
            Announce(isPlayer ? "РАВНОВЕСИЕ СЛОМЛЕНО!" : "РАВНОВЕСИЕ СЛОМЛЕНО — ДОБЕЙ!", isPlayer ? new Color(1f, 0.45f, 0.2f) : new Color(1f, 0.2f, 0.12f));
            if (Juice.Instance != null) Juice.Instance.Clash(f.transform.position + Vector3.up * 1.2f + f.transform.forward * 0.3f);
            if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.07f);
            Haptics.Pulse(isPlayer ? 80 : 50);
            hitStopUntil = Time.unscaledTime + 0.1f;
            Slowmo = true; slowUntil = Time.unscaledTime + 0.45f;
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
            if (r.attacker.Dead || r.target.Dead || DeathblowActive) return;
            int id = r.contact.swingId;
            int last;
            if (lastSwing.TryGetValue(r.attacker, out last) && last == id) return;   // один удар — одно попадание

            AttackType type = r.attacker.weapon != null && r.attacker.weapon.CurrentProfile != null ? r.attacker.weapon.CurrentProfile.type : AttackType.Overhead;
            // подсечку перепрыгивают: пока цель в воздухе, клинок проходит под ногами (удар ещё может попасть, если она приземлится)
            if (type == AttackType.LegSweep && !r.target.Grounded && r.target.transform.position.y > 0.12f)
            {
                int j;
                if (!jumpedSweep.TryGetValue(r.target, out j) || j != id)
                {
                    jumpedSweep[r.target] = id;
                    bool pr = r.attacker.weapon != null && r.attacker.weapon.Perilous;
                    Popup jp = new Popup();
                    jp.world = r.target.transform.position + Vector3.up * 1.9f; jp.text = pr ? "ПЕРЕПРЫГНУЛ!" : "МИМО"; jp.color = new Color(0.5f, 0.95f, 1f);
                    jp.born = Time.unscaledTime; jp.size = pr ? 28f : 20f;
                    Popups.Add(jp);
                    if (pr) { r.attacker.AddPosture(25f); r.target.AddKi(10f); }
                }
                return;
            }
            lastSwing[r.attacker] = id;

            if (Reviving(r.target)) return;   // поднимается после добивания — клинок проходит мимо

            // сломленное равновесие (как в Sekiro): по врагу — добивание (снимает метку жизни);
            // по игроку — сокрушающий удар, казнь только если он добьёт
            if (r.target.PostureBroken)
            {
                bool rangedHit = r.attacker.weapon != null && r.attacker.weapon.CurrentProfile != null && r.attacker.weapon.CurrentProfile.ranged;
                if (!rangedHit)
                {
                    bool lethalCrush = r.target == player && Hp(player) <= MaxHp(player) * CrushFrac + 0.5f;
                    if ((r.target == enemy || lethalCrush) && TryDeathblow(r.attacker, r.target)) return;
                    if (r.target == player)
                    {
                        float cd = Mathf.Round(MaxHp(player) * CrushFrac);
                        Announce("СОКРУШАЮЩИЙ УДАР", new Color(1f, 0.3f, 0.15f));
                        if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Punch(4f); CameraOrbit.Instance.Shake(0.09f); }
                        if (Juice.Instance != null) Juice.Instance.Taiko(0.9f, 0.85f);
                        Haptics.Pulse(110);
                        ApplyDamage(player, r.attacker, cd, r.contact.point, true, false, r.contact.normal, r.contact.bladeVelocity, r.contact.zone != null ? r.contact.zone.transform : null);
                        if (!player.Dead)
                        {
                            player.ResetPosture();   // слом исчерпан ударом — не два раза подряд
                            if (player.weapon != null) player.weapon.Down(0.8f);   // сбит на колено
                            player.Stun(0.8f);
                        }
                        hitStopUntil = Time.unscaledTime + 0.14f;
                        return;
                    }
                }
            }
            float dmg = BaseDamage(type) * ZoneMul(r.zone) * (r.glancing ? 0.5f : 1f) * (r.attacker == enemy ? Ladder.Current.dmgMul * Records.Assist : 1f);
            bool iai = r.attacker.SinceDash < 0.6f;   // удар прямо из рывка
            if (iai) dmg *= 1.25f;
            dmg = Mathf.Round(dmg);
            if (iai && !r.target.Blocking)
            {
                Popup ip = new Popup();
                ip.world = r.contact.point + Vector3.up * 0.55f; ip.text = "ИАЙ!"; ip.color = new Color(0.6f, 0.9f, 1f);
                ip.born = Time.unscaledTime; ip.size = 26f;
                Popups.Add(ip);
            }
            bool perilous = r.attacker.weapon != null && r.attacker.weapon.Perilous;
            if (r.target.Blocking && perilous)
            {
                // опасную атаку блок не держит
                Popup ub = new Popup();
                ub.world = r.contact.point + Vector3.up * 0.3f; ub.text = "БЛОК ПРОБИТ"; ub.color = new Color(1f, 0.35f, 0.2f);
                ub.born = Time.unscaledTime; ub.size = 24f;
                Popups.Add(ub);
                r.target.AddPosture(20f);
            }
            if (r.target.Blocking && !perilous)
            {
                // искры там, где клинки встретились (блок встаёт на пути удара), а не в точке попадания по телу
                Vector3 clashAt = r.contact.point;
                WeaponController dw = r.target.weapon, aw = r.attacker.weapon;
                if (dw != null && aw != null && dw.bladeRoot != null && aw.bladeRoot != null)
                {
                    Vector3 qa, qb;
                    Vector3 a0 = aw.bladeRoot.position, b0 = dw.bladeRoot.position;
                    if (SegDist(a0, a0 + aw.bladeRoot.forward * aw.bladeLength, b0, b0 + dw.bladeRoot.forward * dw.bladeLength, out qa, out qb) < 0.45f) clashAt = (qa + qb) * 0.5f;
                }
                bool parry = r.target.BlockTime < r.target.ParryWindow;
                r.target.AddKi(parry ? 20f : 6f);
                if (parry) StatsOf(r.target).parries++; else StatsOf(r.target).blocks++;
                if (!parry) r.target.Knockback(r.target.transform.position - r.attacker.transform.position, 1.0f);
                else r.attacker.Knockback(r.attacker.transform.position - r.target.transform.position, 1.6f);
                if (r.target == player) Haptics.Pulse(parry ? 35 : 15);
                Popup bp = new Popup();
                bp.world = clashAt + Vector3.up * 0.2f;
                bp.text = parry ? "ПАРИРОВАНИЕ!" : "БЛОК";
                bp.color = parry ? new Color(0.4f, 0.9f, 1f) : new Color(0.75f, 0.85f, 1f);
                bp.born = Time.unscaledTime; bp.size = parry ? 30f : 24f;
                Popups.Add(bp);
                if (Juice.Instance != null)
                {
                    if (parry) { Juice.Instance.Parry(clashAt, r.contact.normal); if (r.target.weapon != null) r.target.weapon.Deflect(1f); PostFX.Kick(0.006f); }
                    else { Juice.Instance.OnHit(clashAt, r.contact.normal, r.contact.bladeVelocity, 0.5f, true, false, null); if (r.target.weapon != null) r.target.weapon.Deflect(-0.6f); }   // блок: клинок отдаёт к телу
                }
                if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Shake(parry ? 0.05f : 0.025f); if (parry) CameraOrbit.Instance.Punch(2.5f); }
                // равновесие: блок копит урон защитнику, отражение (парирование) — атакующему
                if (parry) { r.target.AddPosture(4f); r.attacker.AddPosture(PostureDamage(type) * 0.9f); }
                else r.target.AddPosture(PostureDamage(type));
                if (r.target.PostureBroken) return;
                if (parry)
                {
                    if (r.attacker.weapon != null) r.attacker.weapon.ResetState();   // клинок отброшен назад
                    if (r.attacker.InString)
                    {
                        // посреди серии отражение не останавливает противника: следующий удар уже идёт — держи ритм
                        hitStopUntil = Time.unscaledTime + 0.05f;
                    }
                    else
                    {
                        r.attacker.Stun(0.8f);
                        hitStopUntil = Time.unscaledTime + 0.09f;
                        counterUntil[r.target] = Time.unscaledTime + 1.0f;     // окно контратаки
                        Slowmo = true; slowUntil = Time.unscaledTime + 0.35f;
                    }
                }
                else
                {
                    if (r.attacker.weapon != null) r.attacker.weapon.Rebound();   // клинок отскочил от блока
                    ApplyDamage(r.target, r.attacker, Mathf.Max(1f, Mathf.Round(dmg * 0.15f)), r.contact.point, false, true);
                }
                return;
            }
            lastHitHead = r.zone == BodyZoneId.Head;
            ApplyDamage(r.target, r.attacker, dmg, r.contact.point, r.zone == BodyZoneId.Head, false, r.contact.normal, r.contact.bladeVelocity, r.contact.zone != null ? r.contact.zone.transform : null);
            if (!r.target.Dead) r.target.AddPosture(dmg * 0.6f);   // пропущенный удар тоже сбивает равновесие
        }

        /// <summary>Время раунда вышло: побеждает тот, у кого больше здоровья; при равенстве — переигровка.</summary>
        void TimeUp()
        {
            TimedOut = true;
            // по времени сравниваем «запас жизни»: оставшиеся метки врага считаются полными полосами здоровья (в долях)
            float hpP = Hp01(player), hpE = Hp01(enemy) + (Marks(enemy) - 1);
            Freeze(true);
            if (player.weapon != null) player.weapon.ResetState();
            if (enemy.weapon != null) enemy.weapon.ResetState();
            nextRoundAt = Time.unscaledTime + nextRoundDelay;
            if (Mathf.Approximately(hpP, hpE)) { LastDraw = true; SetState(DuelState.PlayerLost); return; }
            bool playerLost = hpP < hpE;
            SetState(playerLost ? DuelState.PlayerLost : DuelState.PlayerWon);
            if (playerLost) EnemyWins++; else PlayerWins++;
            Perfect = playerLost ? hpE >= MaxMarks(enemy) - 0.001f : hpP >= 0.999f;
            MatchOver = PlayerWins >= winsToMatch || EnemyWins >= winsToMatch;
            if (MatchOver) Records.OnMatch(PlayerWins >= winsToMatch, Ladder.Index);
            if (Juice.Instance != null) Juice.Instance.Taiko(1f, 0.8f);
        }

        void ApplyDamage(FighterController target, FighterController attacker, float dmg, Vector3 point, bool crit, bool chip = false, Vector3 normal = default(Vector3), Vector3 hitDir = default(Vector3), Transform zoneT = null)
        {
            if (!chip && attacker != null)
            {
                // контратака после парирования: ×1,5
                float cu;
                if (counterUntil.TryGetValue(attacker, out cu) && Time.unscaledTime < cu)
                {
                    counterUntil[attacker] = 0f;
                    dmg = Mathf.Round(dmg * 1.5f);
                    crit = true;
                    Popup cp = new Popup();
                    cp.world = point + Vector3.up * 0.45f; cp.text = "КОНТРАТАКА!"; cp.color = new Color(1f, 0.55f, 0.2f);
                    cp.born = Time.unscaledTime; cp.size = 28f;
                    Popups.Add(cp);
                }
                // серия: попадания подряд, пока соперник не ответил; каждое следующее чуть сильнее
                int c = ComboAge(attacker) < 1.6f ? Combo(attacker) + 1 : 1;
                combo[attacker] = c; comboTime[attacker] = Time.unscaledTime;
                combo[target] = 0;
                dmg = Mathf.Round(dmg * Mathf.Min(1.4f, 1f + 0.1f * (c - 1)));
                if (!firstBlood) { firstBlood = true; Announce("ПЕРВАЯ КРОВЬ", new Color(0.95f, 0.15f, 0.1f)); }
                if (c == 5) Announce(attacker == player ? "НЕУДЕРЖИМ!" : "БЕЗЖАЛОСТНЫЙ НАТИСК", new Color(1f, 0.8f, 0.25f));
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
            float before01 = Hp01(target);
            hp[target] = Mathf.Max(0f, Hp(target) - dmg);
            // финальный противник «Пути» на половине здоровья впадает в ярость
            if (target == enemy && Ladder.IsLast && !Enraged && Marks(enemy) <= 1 && before01 > 0.5f && Hp01(target) <= 0.5f && Hp(target) > 0f)
            {
                Enraged = true;
                enemy.moveSpeed *= 1.18f;
                enemy.AddKi(60f);
                Announce("ЯРОСТЬ СЁГУНА", new Color(1f, 0.75f, 0.2f));
                if (Juice.Instance != null) { Juice.Instance.Growl(0.75f); Juice.Instance.Taiko(1f, 0.6f); }
                if (CameraOrbit.Instance != null) CameraOrbit.Instance.Shake(0.06f);
            }
            if (!chip) MatsOf(target).flashAt = Time.unscaledTime;
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
                if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Shake(kill ? 0.12f : (dmg >= 20f ? 0.07f : 0.04f)); CameraOrbit.Instance.Punch(kill ? 5f : (dmg >= 20f ? 2.5f : 1f)); }
                hitStopUntil = Time.unscaledTime + (kill ? 0.16f : (dmg >= 20f ? 0.08f : 0.05f));
                if (kill || dmg >= 20f) PostFX.Kick(kill ? 0.012f : 0.006f);
            }

            // оглушение: рубящие выбивают из замаха сильнее
            if (!chip && target.weapon != null && target.weapon.Phase == SwingPhase.Windup) target.weapon.ResetState();
            if (!chip) target.Stun(dmg >= 20f ? 0.55f : 0.38f);

            // здоровье врага в ноль: он не падает сам — равновесие ломается, открыто добивание (как в Sekiro)
            if (Hp(target) <= 0f && target == enemy && !executing && !target.Dead)
            {
                hp[target] = 0f;
                target.ForceBreak(3.2f);
                exhaustedUntil[target] = Time.time + 3.2f;
                return;
            }
            if (Hp(target) <= 0f)
            {
                target.Dead = true;
                if (lastHitHead && !chip)
                {
                    HeroRigDriver rig = target.GetComponentInChildren<HeroRigDriver>();
                    if (rig != null) rig.Decapitate();
                    Announce("ОБЕЗГЛАВЛЕН", new Color(0.9f, 0.1f, 0.08f));
                }
                if (target.weapon != null) target.weapon.ResetState();
                Haptics.Pulse(120);
                // 回生 (как в Sekiro): павший игрок может один раз за матч подняться — пока решает, бой замер
                bool decap = lastHitHead && !chip;
                if (target == player && Resurrections > 0 && !decap)
                {
                    ResurrectOffered = true; offerAt = Time.unscaledTime; offerKiller = attacker;
                    Freeze(true); if (attacker != null) attacker.Frozen = true;
                    Slowmo = true; slowUntil = Time.unscaledTime + 1.1f;
                    if (Juice.Instance != null) Juice.Instance.Heartbeat();
                    return;
                }
                EndRound(target, attacker);
            }
        }

        // ---------------------------------------------------------------- 回生: воскрешение игрока (раз за матч)
        public const float ResurrectWindow = 5f;
        public int Resurrections { get; private set; } = 1;
        public bool ResurrectOffered { get; private set; }
        public float ResurrectOfferAge { get { return Time.unscaledTime - offerAt; } }
        public float ResurrectedAt { get; private set; } = -10f;
        float offerAt; FighterController offerKiller;

        /// <summary>Боец пал: раунд за его противником.</summary>
        void EndRound(FighterController target, FighterController attacker)
        {
            bool playerLost = target == player;
            Perfect = attacker != null && Hp(attacker) >= MaxHp(attacker);
            SetState(playerLost ? DuelState.PlayerLost : DuelState.PlayerWon);
            if (playerLost) EnemyWins++; else PlayerWins++;
            MatchOver = PlayerWins >= winsToMatch || EnemyWins >= winsToMatch;
            if (MatchOver) Records.OnMatch(PlayerWins >= winsToMatch, Ladder.Index);
            nextRoundAt = Time.unscaledTime + nextRoundDelay;
            if (attacker != null) attacker.Frozen = true;
            Slowmo = true; slowUntil = Time.unscaledTime + 1.1f;
        }

        /// <summary>Игрок встаёт (кнопка 回生): половина здоровья, равновесие целое, миг неуязвимости; враг отпрянул от неожиданности.</summary>
        public void Resurrect()
        {
            if (!ResurrectOffered || player == null) return;
            ResurrectOffered = false; Resurrections--;
            ResurrectedAt = Time.unscaledTime;
            player.Dead = false;
            hp[player] = Mathf.Round(MaxHp(player) * 0.5f); hpTrail[player] = hp[player];
            player.ResetPosture();
            reviveUntil[player] = Time.unscaledTime + 1.2f;
            Freeze(false);
            if (enemy != null)
            {
                Vector3 away = enemy.transform.position - player.transform.position; away.y = 0f;
                enemy.Knockback(away, 2.6f); enemy.Stun(1.0f);
                if (enemy.weapon != null) enemy.weapon.ResetState();
            }
            if (Juice.Instance != null)
            {
                Juice.Instance.Taiko(1f, 1.15f);
                Juice.Instance.Dust(player.transform.position + Vector3.up * 0.05f, Vector3.up, 30);
                Juice.Instance.PetalBurst(40);
            }
            if (CameraOrbit.Instance != null) { CameraOrbit.Instance.Shake(0.08f); CameraOrbit.Instance.Punch(4f); }
            Haptics.Pulse(80);
            hitStopUntil = 0f; Slowmo = false;
            Announce("ВОСКРЕШЕНИЕ", new Color(0.75f, 0.9f, 1f));
        }

        void UpdateResurrectOffer()
        {
            PostFX.Desaturate(ResurrectOffered ? 0.85f : (State == DuelState.PlayerLost && !TimedOut ? 0.6f : 0f));
            if (!ResurrectOffered) return;
            if (ResurrectOfferAge > ResurrectWindow) { ResurrectOffered = false; EndRound(player, offerKiller); }
        }
    }
}
