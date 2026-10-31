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
        [Range(0f, 1f)] public float aggression = 0.5f;

        float nextAttackAt, strafeUntil, retreatUntil, dashAt, blockUntil, energyAt;
        int lastSeenSwing = -1, parrySwing = -1;
        float punishAt, ballCheckAt, stanceAt;
        int lastBall;
        float strafeDir = 1f;
        bool hooked;
        // серия (как у мечников Sekiro): 2–4 удара подряд из восстановления, каждый надо отбить; парирование её обрывает
        int feintSwing = -1;   // финт: замах обрывается на середине — ловит ранний блок/парирование, следом настоящий удар
        int stringLeft; AttackType stringLast; bool stringDelay; float stringPauseUntil, stringIdleAt = -1f;
        static readonly AttackType[] StringNext = { AttackType.Slash, AttackType.SlashBack, AttackType.Overhead, AttackType.Thrust };
        static readonly AttackType[] Pool = { AttackType.Overhead, AttackType.Overhead, AttackType.Slash, AttackType.SlashBack, AttackType.Thrust, AttackType.Rising, AttackType.LegSweep };
        static readonly AttackType[] Followups = { AttackType.Slash, AttackType.SlashBack, AttackType.Thrust, AttackType.Overhead };

        void Start() { nextAttackAt = Time.time + 2.2f; }

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

        int Level { get { int r = DuelManager.Instance != null ? DuelManager.Instance.Round - 1 : 0; return Mathf.Clamp(r + Difficulty - 1, 0, 4); } }

        static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        void Update()
        {
            if (!hooked && DuelManager.Instance != null) { DuelManager.Instance.Damaged += OnDamaged; hooked = true; }
            if (self != null && (self.Stunned || self.Dead)) stringLeft = 0;   // оглушён (сломлено равновесие, микири) — серия сорвана
            if (self != null) self.InString = stringLeft > 0;
            if (!aiEnabled || self == null || target == null || !self.CanAct) return;
            if (DuelManager.Instance != null && DuelManager.Instance.State != DuelState.Fighting) return;

            float agg = Mathf.Clamp01(aggression - 0.2f + 0.2f * Difficulty + 0.12f * Mathf.Max(0, Level) + Ladder.Current.aggr);
            Vector3 to = target.transform.position - self.transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 1e-3f ? to / dist : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            bool busy = self.weapon != null && self.weapon.Phase != SwingPhase.Idle;

            if (Time.time > strafeUntil) { strafeUntil = Time.time + Random.Range(0.8f, 1.8f); strafeDir = Random.value < 0.5f ? -1f : 1f; }

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
                mv += side * strafeDir * 0.35f;
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
            if (!busy && dist > 2.2f && self.CanEnergy && Time.time >= energyAt && Time.time >= blockUntil)
            {
                energyAt = Time.time + Random.Range(2.5f, 5f);
                if (Random.value < 0.6f && self.Attack(AttackType.Energy)) return;
            }

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
                if (self.Attack(t, peril))
                {
                    if (self.weapon != null && Random.value < 0.65f) { self.weapon.stance = 0; stanceAt = Time.time + Random.Range(2f, 5f); }   // после удара — обычно назад в тюдан
                    nextAttackAt = Time.time + Mathf.Lerp(2.4f, 0.8f, agg) + Random.Range(0f, 0.7f);
                    // серия: с уровня «воин», не после опасной атаки и не шаром
                    if (!peril && !quick && Level >= 1 && t != AttackType.Energy && Random.value < 0.18f + 0.08f * Level)
                    {
                        stringLeft = 1 + Random.Range(0, Level >= 2 ? 3 : 2);
                        stringLast = t; stringDelay = Level >= 3 && Random.value < 0.35f; stringPauseUntil = 0f; self.InString = true;
                    }
                    else if (!peril && !quick && Level >= 2 && t != AttackType.Energy && Random.value < 0.08f * (Level - 1)) feintSwing = self.weapon.SwingId;
                    else if (Random.value < 0.45f - 0.08f * Level) retreatUntil = Time.time + 0.9f;
                }
            }
            if (!busy && dist < 1.0f && Time.time > dashAt && Random.value < 0.01f) { self.Dash(XZ(-dir + side * strafeDir)); dashAt = Time.time + 2f; }
        }
    }
}
