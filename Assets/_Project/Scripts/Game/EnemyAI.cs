using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Простой противник: держит дистанцию, обходит игрока по кругу, периодически идёт в атаку случайным ударом,
    /// после удара отступает. Вся «логика ударов» — те же SwingProfile, что и у игрока.
    /// </summary>
    public class EnemyAI : MonoBehaviour
    {
        public FighterController self, target;
        public bool aiEnabled = true;
        [Range(0f, 1f)] public float aggression = 0.55f;

        float nextAttackAt, strafeUntil, retreatUntil, dashAt;
        float strafeDir = 1f;
        static readonly AttackType[] Pool = { AttackType.Overhead, AttackType.Overhead, AttackType.Slash, AttackType.SlashBack, AttackType.Thrust, AttackType.Rising, AttackType.LegSweep };

        void Start() { nextAttackAt = Time.time + 2.2f; }

        void Update()
        {
            if (!aiEnabled || self == null || target == null || !self.CanAct) return;
            if (DuelManager.Instance != null && DuelManager.Instance.State != DuelState.Fighting) return;

            Vector3 to = target.transform.position - self.transform.position; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 1e-3f ? to / dist : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            bool busy = self.weapon != null && self.weapon.Phase != SwingPhase.Idle;

            if (Time.time > strafeUntil) { strafeUntil = Time.time + Random.Range(0.8f, 1.8f); strafeDir = Random.value < 0.5f ? -1f : 1f; }

            Vector3 mv = Vector3.zero;
            const float want = 1.35f;
            if (Time.time < retreatUntil) mv = -dir * 0.9f + side * strafeDir * 0.3f;
            else if (!busy)
            {
                if (dist > want + 0.35f) mv = dir;
                else if (dist < want - 0.35f) mv = -dir * 0.6f;
                mv += side * strafeDir * 0.35f;
            }
            self.SetMoveVector(new Vector2(mv.x, mv.z));

            if (!busy && Time.time >= nextAttackAt && dist < 1.6f)
            {
                AttackType t = Pool[Random.Range(0, Pool.Length)];
                if (self.Attack(t))
                {
                    nextAttackAt = Time.time + Mathf.Lerp(2.4f, 0.9f, aggression) + Random.Range(0f, 0.8f);
                    if (Random.value < 0.45f) retreatUntil = Time.time + 0.9f;
                }
            }
            if (!busy && dist < 1.0f && Time.time > dashAt && Random.value < 0.01f) { self.Dash(-dir * 1f + side * strafeDir); dashAt = Time.time + 2f; }
        }
    }
}
