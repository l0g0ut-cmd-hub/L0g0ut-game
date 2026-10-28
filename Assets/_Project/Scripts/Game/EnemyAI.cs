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
        float punishAt;
        float strafeDir = 1f;
        bool hooked;
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
            if (!aiEnabled || self == null || target == null || !self.CanAct) return;
            if (DuelManager.Instance != null && DuelManager.Instance.State != DuelState.Fighting) return;

            float agg = Mathf.Clamp01(aggression - 0.2f + 0.2f * Difficulty + 0.12f * Mathf.Max(0, Level) + Ladder.Current.aggr);
            Vector3 to = target.transform.position - self.transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 1e-3f ? to / dist : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            bool busy = self.weapon != null && self.weapon.Phase != SwingPhase.Idle;

            if (Time.time > strafeUntil) { strafeUntil = Time.time + Random.Range(0.8f, 1.8f); strafeDir = Random.value < 0.5f ? -1f : 1f; }

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
                if (self.Attack(t))
                {
                    nextAttackAt = Time.time + Mathf.Lerp(2.4f, 0.8f, agg) + Random.Range(0f, 0.7f);
                    if (Random.value < 0.45f - 0.08f * Level) retreatUntil = Time.time + 0.9f;
                }
            }
            if (!busy && dist < 1.0f && Time.time > dashAt && Random.value < 0.01f) { self.Dash(XZ(-dir + side * strafeDir)); dashAt = Time.time + 2f; }
        }
    }
}
