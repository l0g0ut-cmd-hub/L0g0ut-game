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
        float slowUntil;

        void Awake() { Instance = this; }
        void Start() { ResetDuel(); }

        void OnDestroy() { Time.timeScale = 1f; if (resolver != null) resolver.HitRegistered -= OnHit; if (Instance == this) Instance = null; }

        public float Hp(FighterController f) { float v; return hp.TryGetValue(f, out v) ? v : maxHp; }
        public float Hp01(FighterController f) { return Mathf.Clamp01(Hp(f) / maxHp); }
        public float Trail01(FighterController f) { float v; return hpTrail.TryGetValue(f, out v) ? Mathf.Clamp01(v / maxHp) : 1f; }
        public float SinceHit(FighterController f) { float v; return lastHitTime.TryGetValue(f, out v) ? Time.unscaledTime - v : 99f; }

        public void ResetDuel()
        {
            Time.timeScale = 1f;
            Paused = false; Slowmo = false;
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

        public void SetPaused(bool p)
        {
            Paused = p;
            Time.timeScale = p ? 0f : (Slowmo ? 0.3f : 1f);
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
            if (Slowmo && Time.unscaledTime >= slowUntil) { Slowmo = false; if (!Paused) Time.timeScale = 1f; }

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
            ApplyDamage(r.target, r.attacker, dmg, r.contact.point, r.zone == BodyZoneId.Head);
        }

        void ApplyDamage(FighterController target, FighterController attacker, float dmg, Vector3 point, bool crit)
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

            // оглушение: рубящие выбивают из замаха сильнее
            if (target.weapon != null && target.weapon.Phase == SwingPhase.Windup) target.weapon.ResetState();
            target.Stun(dmg >= 20f ? 0.55f : 0.38f);

            if (Hp(target) <= 0f)
            {
                target.Dead = true;
                if (target.weapon != null) target.weapon.ResetState();
                SetState(target == player ? DuelState.PlayerLost : DuelState.PlayerWon);
                if (attacker != null) attacker.Frozen = true;
                Slowmo = true; slowUntil = Time.unscaledTime + 1.1f;
                if (!Paused) Time.timeScale = 0.3f;
            }
        }
    }
}
