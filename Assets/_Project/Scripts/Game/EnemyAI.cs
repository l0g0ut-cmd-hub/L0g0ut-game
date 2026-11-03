using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Противник: держит дистанцию, обходит по кругу, атакует случайным ударом, после удара отступает.
    /// С каждым раундом злее: чаще бьёт и чаще ставит блок. Издалека кидает шар духа, после попадания дожимает серией.
    /// Вся «логика ударов» — те же SwingProfile, что и у игрока.
    /// </summary>
    public class EnemyAI : MonoBehaviour
    {
        public FighterController self, target;
        public bool aiEnabled = true;
        /// <summary>ИИ ведёт игрока (отладочный автобой): без фирменных приёмов врагов (дым, волна, залпы).</summary>
        public bool autoPlayer;
        [Range(0f, 1f)] public float aggression = 0.5f;

        float nextAttackAt, strafeUntil, retreatUntil, dashAt, blockUntil, energyAt;
        int lastSeenSwing = -1, parrySwing = -1;
        float punishAt, ballCheckAt, stanceAt;
        int lastBall;
        float strafeDir = 1f, strafeK = 0.25f;
        bool hooked;
        // серия (как у мечников Sekiro): 2–4 удара подряд из восстановления, каждый надо отбить; парирование её обрывает
        int feintSwing = -1;   // финт: замах обрывается на середине — ловит ранний блок/парирование, следом настоящий удар
        int stringLeft; AttackType stringLast; bool stringDelay; float stringPauseUntil, stringIdleAt = -1f;
        static readonly AttackType[] StringNext = { AttackType.Slash, AttackType.SlashBack, AttackType.Overhead, AttackType.Thrust };
        static readonly AttackType[] Pool = { AttackType.Overhead, AttackType.Overhead, AttackType.Slash, AttackType.SlashBack, AttackType.Thrust, AttackType.Rising, AttackType.LegSweep };
        static readonly AttackType[] Followups = { AttackType.Slash, AttackType.SlashBack, AttackType.Thrust, AttackType.Overhead };

        void Start() { nextAttackAt = Time.time + 2.2f; }

        // ---------------- фирменные приёмы врагов «Пути» ----------------
        // Тень клана — уходит в дым и возникает за спиной; Кровавый они — бьёт по земле (волна, перепрыгнуть);
        // Проклятый сёгун — три фазы по меткам жизни: 2-я — залпы шаров духа, 3-я — ещё и уход в дым, быстрее и злее
        float vanishAt; int slamSwing = -1; bool slamDone; float volleyAt = -1f; int lastPhase = 1;
        int StageIdx { get { return Ladder.Index; } }
        int Phase { get { DuelManager dm = DuelManager.Instance; return dm == null ? 1 : 1 + Mathf.Max(0, dm.MaxMarks(self) - dm.Marks(self)); } }

        bool TryVanish(Vector3 dir)
        {
            if (Time.time < vanishAt || target == null) return false;
            vanishAt = Time.time + Random.Range(5f, 8f);
            if (Juice.Instance != null) Juice.Instance.SmokeVanish(self.transform.position);
            Vector3 back = target.transform.position - target.transform.forward * 0.95f;
            self.Teleport(back);
            if (Juice.Instance != null) Juice.Instance.SmokeVanish(self.transform.position);
            nextAttackAt = Time.time + Random.Range(0.18f, 0.32f);   // удар почти сразу — успей развернуться и закрыться
            blockUntil = 0f;
            return true;
        }

        void Signatures(float dist, Vector3 dir, bool busy)
        {
            DuelManager dm = DuelManager.Instance;
            // удар по земле: волна на ударе тяжёлого «сверху»
            if (slamSwing >= 0 && self.weapon != null)
            {
                if (self.weapon.SwingId != slamSwing) slamSwing = -1;
                else if (!slamDone && self.weapon.Phase == SwingPhase.Strike && self.weapon.PhaseProgress > 0.5f)
                {
                    slamDone = true;
                    Vector3 at = self.transform.position + self.transform.forward * 1.0f;
                    if (Juice.Instance != null) Juice.Instance.Slam(at);
                    Vector3 d = target.transform.position - at; d.y = 0f;
                    if (d.magnitude < 2.6f && dm != null)
                    {
                        if (target.Grounded) dm.ForceHit(target, self, 9f, 32f, at);
                        else { target.AddKi(10f); dm.Announce("ПЕРЕПРЫГНУЛ!", new Color(0.5f, 0.95f, 1f)); }
                    }
                }
            }
            // сёгун: смена фазы — объявление и злость
            int ph = StageIdx == 3 ? Phase : 1;
            if (ph != lastPhase)
            {
                if (ph > lastPhase && dm != null) dm.Announce(ph == 2 ? "СЁГУН ПРИЗЫВАЕТ ДУХОВ" : "СЁГУН В ЯРОСТИ", new Color(1f, 0.8f, 0.3f), ph == 2 ? "шары духа залпами — сближайся" : "уходит в дым и бьёт со спины");
                lastPhase = ph;
            }
            // залп второго шара
            if (volleyAt > 0f && Time.time >= volleyAt && !busy) { volleyAt = -1f; self.Attack(AttackType.Energy); }
        }

        // накопление удара (узор на полу): они, сёгун со 2-й фазы, на высоких уровнях — все. Пока копит — стоит; прерви ударом,
        // иначе рывок и сокрушительный удар (со 2-й ступени блок не держит, на 3-й — волна, перепрыгни)
        float chargeAt = 6f, chargeStart; int chargeGoal; bool chargeEarly; static bool chargeHinted;
        bool answering, answerChecked;   // копит навстречу накоплению игрока (встреча духа)
        bool Charge(float dist, bool busy)
        {
            ChargeSystem cs = ChargeSystem.Instance;
            if (cs == null) return false;
            if (cs.IsCharging(self))
            {
                int lv = cs.Level(self);
                float held = Time.time - chargeStart;
                // копил навстречу игроку: он отпустил — отпускаем сразу же (встреча духа)
                if (answering && lv >= 0 && target != null && !cs.IsCharging(target)) { answering = false; cs.Release(self, AttackType.Overhead); return true; }
                if (lv >= chargeGoal || (chargeEarly && lv >= 0 && dist < 1.3f) || (lv >= 0 && held > 3.0f)) { answering = false; cs.Release(self, AttackType.Overhead); }
                else if (held > 3.4f) { answering = false; cs.Cancel(self); }
                return true;
            }
            // заряд есть не у всех и не всегда: слабые — только сильным противником на карте, остальные — по силе и уровню
            bool strongNode = Campaign.InFight && (Campaign.FightType == NodeType.Elite || Campaign.FightType == NodeType.Boss);
            bool can = StageIdx >= 2 || (StageIdx == 1 && Level >= 1) || strongNode || Level >= 3;
            if (!can || busy || self.Ki < ChargeSystem.KiCost[0]) return false;
            // игрок копит — иногда копим навстречу (кто сильнее, тот и пробьёт)
            if (target != null && cs.IsCharging(target))
            {
                if (answerChecked) return false;
                answerChecked = true;
                if (dist > 4.5f || Random.value > 0.35f + 0.1f * Level) return false;
                int ag = Mathf.Clamp(Random.Range(0, 3), 0, ChargeSystem.Max);
                while (ag > 0 && self.Ki < ChargeSystem.KiCost[ag]) ag--;
                if (!cs.Begin(self)) return false;
                chargeStart = Time.time; chargeGoal = 9; chargeEarly = false; answering = true;
                return true;
            }
            answerChecked = false;
            if (Time.time < chargeAt || dist < 1.8f || dist > 4.2f) return false;
            if (Random.value > 0.015f + 0.005f * Level) return false;
            chargeAt = Time.time + Random.Range(6f, 10f) - Level * 0.6f;
            int goal = StageIdx == 2 ? (Random.value < 0.25f ? 3 : Random.value < 0.6f ? 2 : 1) : (StageIdx == 3 && Phase >= 3 ? 3 : StageIdx == 3 ? 2 : Random.Range(0, 2));   // ярость (IV) — они иногда, сёгун в 3-й фазе всегда
            while (goal > 0 && self.Ki < ChargeSystem.KiCost[goal]) goal--;
            if (!cs.Begin(self)) return false;
            chargeStart = Time.time; chargeGoal = goal; chargeEarly = Random.value < 0.5f;
            DuelManager dm = DuelManager.Instance;
            if (!chargeHinted && dm != null) { chargeHinted = true; dm.Announce("ВРАГ КОПИТ СИЛУ", Ladder.Current.aura, "прерви ударом — или перепрыгни волну"); }
            return true;
        }

        void OnDestroy() { if (hooked && DuelManager.Instance != null) DuelManager.Instance.Damaged -= OnDamaged; }

        void OnDamaged(FighterController victim, float dmg)
        {
            // попал — дожимает серией; пропустил — чаще уходит в блок/отступление
            if (victim == target && Random.value < 0.55f + 0.1f * Level) nextAttackAt = Time.time + Random.Range(0.18f, 0.35f);
            if (victim == self && Random.value < 0.4f) retreatUntil = Time.time + 0.6f;
        }

        /// <summary>0 — ученик, 1 — воин, 2 — мастер. Хранится между запусками.</summary>
        public static int Difficulty
        {
            get { return PlayerPrefs.GetInt("ai_diff", 1); }
            set { PlayerPrefs.SetInt("ai_diff", Mathf.Clamp(value, 0, 2)); }
        }
        public static string DifficultyName { get { int d = Difficulty; return d == 0 ? "УЧЕНИК" : (d == 1 ? "ВОИН" : "МАСТЕР"); } }

        /// <summary>Сложность этого ИИ: своя (отладочный автобой игрока) или общая сложность врага.</summary>
        public int difficultyOverride = -1;
        int Diff { get { return difficultyOverride >= 0 ? difficultyOverride : Difficulty; } }
        int Level { get { int r = DuelManager.Instance != null ? DuelManager.Instance.Round - 1 : 0; return Mathf.Clamp(r + Diff - 1, 0, 4); } }

        static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        void Update()
        {
            if (!hooked && DuelManager.Instance != null) { DuelManager.Instance.Damaged += OnDamaged; hooked = true; }
            if (self != null && (self.Stunned || self.Dead)) stringLeft = 0;   // оглушён (сломлено равновесие, микири) — серия сорвана
            if (self != null) self.InString = stringLeft > 0;
            if (!aiEnabled || self == null || target == null || !self.CanAct) return;
            if (DuelManager.Instance != null && (DuelManager.Instance.State != DuelState.Fighting || DuelManager.Instance.Paused)) return;   // под паузой ИИ не думает
            if (DuelManager.Instance != null && DuelManager.Instance.Round == 1 && lastPhase > 1 && Phase == 1) lastPhase = 1;   // новый матч

            float agg = Mathf.Clamp01(aggression - 0.2f + 0.2f * Diff + 0.12f * Mathf.Max(0, Level) + Ladder.Current.aggr
                + (DuelManager.Instance != null && DuelManager.Instance.EnemyEnraged ? 0.25f : 0f));   // ярость на последней трети здоровья
            Vector3 to = target.transform.position - self.transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 1e-3f ? to / dist : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            bool busy = self.weapon != null && self.weapon.Phase != SwingPhase.Idle;
            if (!autoPlayer) Signatures(dist, dir, busy);
            if (Charge(dist, busy)) return;
            bool canVanish = StageIdx == 1 || (StageIdx == 3 && Phase >= 3);

            // кружение — эпизодами, а не постоянно: чаще бойцы держат линию и сходятся (раньше вечно ходили по кругу)
            if (Time.time > strafeUntil)
            {
                strafeUntil = Time.time + Random.Range(0.8f, 1.8f);
                strafeDir = Random.value < 0.5f ? -1f : 1f;
                strafeK = Random.value < 0.4f ? Random.Range(0.18f, 0.3f) : 0f;
            }
            // уводит в глубину арены (к ступеням храма или к камере) — обходим в сторону центра
            {
                Vector3 sp = self.transform.position, sd = side * strafeDir;
                if (Mathf.Abs(sp.z) > 1.1f && sd.z * sp.z > 0f) strafeDir = -strafeDir;
            }

            // стойки (камаэ): между схватками враг перехватывает меч — хассо (рубит сверху и наискось), вакигамаэ (снизу, по ногам, укол);
            // Они любит хассо, Тень — вакигамаэ; ученик почти всегда в тюдан
            if (!busy && self.weapon != null && Time.time >= stanceAt && stringLeft <= 0)
            {
                stanceAt = Time.time + Random.Range(4f, 8f);
                float pH = 0.16f + Ladder.Current.heavyBias * 0.5f, pW = 0.12f + Ladder.Current.dashBias * 0.5f;
                if (Level < 1) { pH *= 0.4f; pW *= 0.4f; }
                float r = Random.value;
                int ns = r < pH ? 1 : (r < pH + pW ? 2 : 0);
                if (ns != self.weapon.stance && Juice.Instance != null) Juice.Instance.Regrip();
                self.weapon.stance = ns;
            }

            if (feintSwing >= 0 && self.weapon != null)
            {
                if (self.weapon.SwingId != feintSwing || self.weapon.Phase != SwingPhase.Windup) feintSwing = -1;
                else if (self.weapon.PhaseProgress > 0.55f)
                {
                    feintSwing = -1;
                    self.weapon.ResetState();
                    nextAttackAt = Time.time + Random.Range(0.22f, 0.4f);   // настоящий удар — почти сразу
                    return;
                }
            }

            // продолжение серии: рубящие чередуют сторону; последний удар мастер иногда придерживает (ломает ритм)
            if (stringLeft > 0 && self.weapon != null)
            {
                if (dist > 1.9f || target.Dead) stringLeft = 0;
                else
                {
                    bool last = stringLeft == 1;
                    if (last && stringDelay && self.weapon.Phase == SwingPhase.Recovery) { if (stringPauseUntil <= 0f) stringPauseUntil = Time.time + Random.Range(0.25f, 0.4f); }
                    bool ready = !(last && stringDelay) || (stringPauseUntil > 0f && Time.time >= stringPauseUntil);
                    // клинок отбит (сразу стойка) — короткая пауза, иначе следующий удар без зазора
                    bool idle = self.weapon.Phase == SwingPhase.Idle;
                    if (!idle) stringIdleAt = -1f; else if (stringIdleAt < 0f) stringIdleAt = Time.time;
                    if (idle && Time.time - stringIdleAt < 0.14f) ready = false;
                    if (ready && (self.weapon.Phase == SwingPhase.Recovery || self.weapon.Phase == SwingPhase.Idle))
                    {
                        AttackType t = stringLast == AttackType.Slash ? AttackType.SlashBack : stringLast == AttackType.SlashBack ? AttackType.Slash : StringNext[Random.Range(0, StringNext.Length)];
                        if (last && Random.value < 0.4f) t = Random.value < 0.5f ? AttackType.Overhead : AttackType.Thrust;   // тяжёлый финал
                        if (dist > 1.4f) self.SetMoveVector(new Vector2(dir.x, dir.z));   // догоняет отступающего
                        if (self.Chain(t))
                        {
                            stringLast = t; stringLeft--; stringPauseUntil = 0f; self.InString = stringLeft > 0;
                            nextAttackAt = Time.time + Mathf.Lerp(2.6f, 1.2f, agg) + Random.Range(0f, 0.6f);
                            return;
                        }
                    }
                    if (self.weapon.Phase != SwingPhase.Idle) { self.SetMoveVector(Vector2.zero); return; }
                }
            }

            // равновесие игрока сломлено — добить (опытный враг не упускает)
            if (target.PostureBroken && !busy && dist < 1.7f && Random.value < 0.25f + 0.2f * Level)
            {
                if (dist > 1.2f) self.SetMoveVector(new Vector2(dir.x, dir.z));
                else if (self.Attack(AttackType.Thrust)) { nextAttackAt = Time.time + 0.8f; return; }
            }
            // своё равновесие на исходе — не лезть под удар: больше блока и отхода
            if (self.Posture01 > 0.7f && Time.time > retreatUntil && Random.value < 0.02f) retreatUntil = Time.time + 0.8f;

            // реакция на замах игрока: блок или уход рывком
            WeaponController pw = target.weapon;
            if (pw != null && pw.Phase == SwingPhase.Windup && pw.SwingId != lastSeenSwing && dist < 1.8f && !busy)
            {
                lastSeenSwing = pw.SwingId;
                float r = Random.value;
                // Тень (и сёгун в 3-й фазе): вместо блока — в дым и за спину
                if (canVanish && Random.value < 0.3f + 0.08f * Level && TryVanish(dir)) return;
                if (r < 0.32f + 0.1f * Level)
                {
                    // мастер ставит блок в последний миг — это парирование
                    if (Level >= 2 && Random.value < 0.35f + 0.1f * Level) parrySwing = pw.SwingId;
                    else blockUntil = Time.time + Random.Range(0.45f, 0.75f);
                }
                else if (r < 0.5f + 0.08f * Level + Ladder.Current.dashBias && Time.time > dashAt) { self.Dash(XZ(-dir + side * strafeDir)); dashAt = Time.time + 1.5f; }
            }
            if (pw != null && parrySwing == pw.SwingId && pw.Phase == SwingPhase.Strike) { parrySwing = -1; blockUntil = Time.time + 0.35f; }
            if (Time.time < blockUntil && !busy) self.SetBlock(true);

            // игрок пьёт из тыквы — опытный враг наказывает: сближается и бьёт, пока клинок у того опущен
            if (target.Drinking && !busy && Time.time >= punishAt && Random.value < 0.25f + 0.15f * Level)
            {
                punishAt = Time.time + 0.9f;
                if (dist > 1.35f) self.SetMoveVector(new Vector2(dir.x, dir.z));
                else if (self.Attack(Random.value < 0.5f ? AttackType.Thrust : AttackType.Overhead)) { nextAttackAt = Time.time + 0.7f; return; }
            }
            // наказание за промах: игрок восстанавливается после удара рядом — бьём сразу
            if (pw != null && pw.Phase == SwingPhase.Recovery && dist < 1.4f && !busy && Time.time >= punishAt && Random.value < 0.35f + 0.1f * Level)
            {
                punishAt = Time.time + 1.2f;
                if (self.Attack(Random.value < 0.5f ? AttackType.Thrust : AttackType.Slash)) { nextAttackAt = Time.time + 0.6f; return; }
            }

            Vector3 mv = Vector3.zero;
            const float want = 1.15f;
            if (Time.time < retreatUntil) mv = -dir * 0.9f + side * strafeDir * 0.3f;
            else if (!busy)
            {
                if (dist > want + 0.35f) mv = dir;
                else if (dist < want - 0.35f) mv = -dir * 0.6f;
                mv += side * strafeDir * strafeK;
                // держимся ближе к центру площадки по глубине
                float z = self.transform.position.z;
                if (Mathf.Abs(z) > 0.9f) mv += new Vector3(0f, 0f, -Mathf.Sign(z) * Mathf.Clamp01((Mathf.Abs(z) - 0.9f) / 0.8f) * 0.45f);
            }
            self.SetMoveVector(new Vector2(mv.x, mv.z));

            // летит шар игрока: отбить клинком (опытный враг) или уйти рывком
            if (!busy && Time.time >= ballCheckAt)
            {
                ballCheckAt = Time.time + 0.08f;
                foreach (EnergyProjectile e in FindObjectsByType<EnergyProjectile>(FindObjectsSortMode.None))
                {
                    if (e.owner != target || e.GetInstanceID() == lastBall) continue;
                    Vector3 rel = self.transform.position + Vector3.up * 1.1f - e.transform.position;
                    float closing = Vector3.Dot(rel.normalized, e.velocity.normalized);
                    if (closing < 0.7f || rel.magnitude > 1.6f) continue;
                    lastBall = e.GetInstanceID();
                    float r = Random.value;
                    if (r < 0.15f + 0.15f * Level) { if (self.Attack(AttackType.Slash)) return; }
                    else if (r < 0.45f + 0.1f * Level && Time.time > dashAt) { self.Dash(XZ(side * strafeDir)); dashAt = Time.time + 1.2f; return; }
                    else blockUntil = Time.time + 0.4f;
                }
            }

            // супер-удар при полном духе
            if (!busy && self.KiFull && dist < 2.8f && Random.value < 0.02f + 0.01f * Level && DuelManager.Instance != null && DuelManager.Instance.TrySuper(self)) return;

            // шар духа издалека
            bool volley = StageIdx == 3 && Phase >= 2;
            if (!busy && dist > (volley ? 1.8f : 2.2f) && self.CanEnergy && Time.time >= energyAt && Time.time >= blockUntil)
            {
                energyAt = Time.time + (volley ? Random.Range(1.4f, 2.8f) : Random.Range(2.5f, 5f));
                if (Random.value < (volley ? 0.85f : 0.6f) && self.Attack(AttackType.Energy))
                {
                    if (volley && Random.value < 0.6f) volleyAt = Time.time + 0.55f;   // второй шар следом
                    return;
                }
            }
            // Тень издалека иногда сама уходит в дым к игроку
            if (canVanish && !busy && dist > 2.4f && Random.value < 0.006f && TryVanish(dir)) return;

            if (!busy && Time.time >= nextAttackAt && Time.time >= blockUntil && dist < 1.35f)
            {
                bool quick = Time.time - nextAttackAt < 0.4f && DuelManager.Instance != null && DuelManager.Instance.Combo(self) > 0 && DuelManager.Instance.ComboAge(self) < 1.2f;
                AttackType t = quick ? Followups[Random.Range(0, Followups.Length)] : Pool[Random.Range(0, Pool.Length)];
                if (!quick && Random.value < Ladder.Current.heavyBias) t = AttackType.Overhead;   // Они любит рубить сверху
                // из стойки — её удары (по стойке их можно прочитать заранее)
                int stc = self.weapon != null ? self.weapon.stance : 0;
                if (!quick && stc == 1 && Random.value < 0.75f) t = Random.value < 0.6f ? AttackType.Overhead : AttackType.Slash;
                else if (!quick && stc == 2 && Random.value < 0.75f) { float q = Random.value; t = q < 0.4f ? AttackType.Rising : (q < 0.7f ? AttackType.LegSweep : AttackType.Thrust); }
                // опасная атака (危): с уровня «воин», не в серии
                bool peril = !quick && Level >= 1 && Random.value < 0.10f + 0.04f * Level;
                if (peril) { float pr = Random.value; t = pr < 0.45f ? AttackType.Thrust : (pr < 0.75f || Level < 2 ? AttackType.LegSweep : AttackType.Charge); }   // натиск — с уровня 2
                // они: тяжёлое «сверху» — удар по земле с волной (危: перепрыгни)
                bool slam = StageIdx == 2 && !quick && !peril && t == AttackType.Overhead && Random.value < 0.5f;
                if (self.Attack(t, peril))
                {
                    if (slam && self.weapon != null && DuelManager.Instance != null)
                    {
                        slamSwing = self.weapon.SwingId; slamDone = false;
                        DuelManager.Instance.PerilAt[self] = Time.unscaledTime;   // знак 危 над головой
                    }
                    if (self.weapon != null && Random.value < 0.65f) { self.weapon.stance = 0; stanceAt = Time.time + Random.Range(2f, 5f); }   // после удара — обычно назад в тюдан
                    nextAttackAt = Time.time + Mathf.Lerp(2.4f, 0.8f, agg) + Random.Range(0f, 0.7f);
                    // серия: с уровня «воин», не после опасной атаки и не шаром
                    if (!peril && !quick && Level >= 1 && t != AttackType.Energy && Random.value < 0.18f + 0.08f * Level)
                    {
                        stringLeft = 1 + Random.Range(0, Level >= 2 ? 3 : 2);
                        stringLast = t; stringDelay = Level >= 3 && Random.value < 0.35f; stringPauseUntil = 0f; self.InString = true;
                    }
                    else if (!peril && !quick && t != AttackType.Energy && (StageIdx == 1 ? Level >= 1 && Random.value < 0.25f : Level >= 2 && Random.value < 0.08f * (Level - 1))) feintSwing = self.weapon.SwingId;   // Тень — мастер финтов
                    else if (Random.value < 0.45f - 0.08f * Level) retreatUntil = Time.time + 0.9f;
                }
            }
            if (!busy && dist < 1.0f && Time.time > dashAt && Random.value < 0.01f) { self.Dash(XZ(-dir + side * strafeDir)); dashAt = Time.time + 2f; }
        }
    }
}
