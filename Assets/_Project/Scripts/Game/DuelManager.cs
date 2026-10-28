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

        void Awake() { Instance = this; }
        void Start()
        {
            Juice j = GetComponent<Juice>();
            if (j == null) j = gameObject.AddComponent<Juice>();
            if (player != null) j.Track(player.weapon);
            if (enemy != null) j.Track(enemy.weapon);
            ResetDuel();
        }

        void OnDestroy() { Time.timeScale = 1f; if (resolver != null) resolver.HitRegistered -= OnHit; if (Instance == this) Instance = null; }

        public float Hp(FighterController f) { float v; return hp.TryGetValue(f, out v) ? v : maxHp; }
        public float Hp01(FighterController f) { return Mathf.Clamp01(Hp(f) / maxHp); }
        public float Trail01(FighterController f) { float v; return hpTrail.TryGetValue(f, out v) ? Mathf.Clamp01(v / maxHp) : 1f; }
        public float SinceHit(FighterController f) { float v; return lastHitTime.TryGetValue(f, out v) ? Time.unscaledTime - v : 99f; }

        public void ResetDuel()
        {
            Time.timeScale = 1f;
            Paused = false; Slowmo = false; hitStopUntil = 0f;
            if (player != null) { player.ResetFighter(); hp[player] = maxHp; hpTrail[player] = maxHp; lastSwing[player] = -1; }
            if (enemy != null) { enemy.ResetFighter(); hp[enemy] = maxHp; hpTrail[enemy] = maxHp; lastSwing[enemy] = -1; }
            Popups.Clear();
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
                if (tr > cur && SinceHit(f) > 0.45f) tr = Mathf.MoveTowards(tr, cur, maxHp * 0.35f * dt);
                else if (tr < cur) tr = cur;
                hpTrail[f] = tr;
            }

            if (State == DuelState.Intro && StateTime >= introSeconds) { SetState(DuelState.Fighting); Freeze(false); }
            if (Slowmo && Time.unscaledTime >= slowUntil) Slowmo = false;
            ApplyTimeScale();

            for (int i = Popups.Count - 1; i >= 0; i--) if (Time.unscaledTime - Popups[i].born > 1.0f) Popups.RemoveAt(i);
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
            float dmg = BaseDamage(type) * ZoneMul(r.zone) * (r.glancing ? 0.5f : 1f);
            dmg = Mathf.Round(dmg);
            if (r.target.Blocking)
            {
                bool parry = r.target.BlockTime < 0.25f;
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

        void ApplyDamage(FighterController target, FighterController attacker, float dmg, Vector3 point, bool crit, bool chip = false, Vector3 normal = default(Vector3), Vector3 hitDir = default(Vector3), Transform zoneT = null)
        {
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
                SetState(target == player ? DuelState.PlayerLost : DuelState.PlayerWon);
                if (attacker != null) attacker.Frozen = true;
                Slowmo = true; slowUntil = Time.unscaledTime + 1.1f;
            }
        }
    }
}
