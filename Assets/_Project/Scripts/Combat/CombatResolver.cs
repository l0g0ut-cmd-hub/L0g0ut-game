using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    public struct HitResult
    {
        public BladeContact contact;
        public FighterController attacker;
        public FighterController target;
        public BodyZoneId zone;
        public float speed;          // м/с
        public float impactAngleDeg; // угол между направлением клинка и нормалью (0 = лоб в лоб, ~90 = касательно)
        public bool glancing;        // скользящий контакт
        public string attack;        // название атаки (СВЕРХУ, ШАР, ...)
    }

    /// <summary>
    /// Этап 1: превращает подтверждённый контакт в читаемый результат (попадание / промах) и
    /// сообщает об этом наружу. Урон, блоки и реакции — этапы 2-4, здесь их нет.
    /// Визуальная подсветка зоны — только отклик, решение «попал/нет» принято раньше, в сэмплере.
    /// </summary>
    public class CombatResolver : MonoBehaviour
    {
        public static CombatResolver Instance { get; private set; }

        public BladeTrajectorySampler[] samplers;
        public float glancingAngleDeg = 70f;
        public Color flashColor = new Color(0.9f, 0.05f, 0.05f);
        public float flashDuration = 0.35f;

        public event Action<HitResult> HitRegistered;
        public event Action<string> MessageLogged;

        public readonly List<string> Log = new List<string>();
        readonly Dictionary<WeaponController, int> hitsInSwing = new Dictionary<WeaponController, int>();

        bool subscribed;

        void OnEnable() { Instance = this; }

        // Подписка в Start: к этому моменту у всех объектов сцены уже выполнены Awake/OnEnable
        // (порядок Awake/OnEnable между объектами не гарантирован).
        void Start() { Subscribe(); }

        void Subscribe()
        {
            if (subscribed || samplers == null) return;
            for (int i = 0; i < samplers.Length; i++)
            {
                if (samplers[i] == null || samplers[i].Weapon == null) continue;
                samplers[i].ContactDetected += OnContact;
                samplers[i].Weapon.ContactWindowOpened += OnWindowOpened;
                samplers[i].Weapon.ContactWindowClosed += OnWindowClosed;
                samplers[i].Weapon.Released += OnReleased;
            }
            subscribed = true;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (!subscribed || samplers == null) return;
            for (int i = 0; i < samplers.Length; i++)
            {
                if (samplers[i] == null || samplers[i].Weapon == null) continue;
                samplers[i].ContactDetected -= OnContact;
                samplers[i].Weapon.ContactWindowOpened -= OnWindowOpened;
                samplers[i].Weapon.ContactWindowClosed -= OnWindowClosed;
                samplers[i].Weapon.Released -= OnReleased;
            }
            subscribed = false;
        }

        void OnWindowOpened(WeaponController w)
        {
            hitsInSwing[w] = 0;
            Say("[" + w.owner.displayName + " #" + w.SwingId + " " + w.AttackLabel + "] удар начат");
        }

        void OnWindowClosed(WeaponController w)
        {
            int n;
            hitsInSwing.TryGetValue(w, out n);
            string who = "[" + w.owner.displayName + " #" + w.SwingId + " " + w.AttackLabel + "] ";
            if (n == 0) Say(who + "ПРОМАХ: клинок не коснулся тела противника");
            else Say(who + "итог: попаданий " + n);
        }

        void OnContact(BladeContact c)
        {
            HitResult r = new HitResult();
            r.contact = c;
            r.attacker = c.weapon.owner;
            r.target = c.zone.owner;
            r.zone = c.zone.zoneId;
            r.speed = c.bladeVelocity.magnitude;
            r.attack = c.weapon.AttackLabel;

            Vector3 dir = r.speed > 1e-4f ? c.bladeVelocity / r.speed : Vector3.zero;
            float cosIn = Mathf.Clamp(Vector3.Dot(dir, -c.normal), -1f, 1f);
            r.impactAngleDeg = Mathf.Acos(cosIn) * Mathf.Rad2Deg;
            r.glancing = r.impactAngleDeg > glancingAngleDeg;

            int n;
            hitsInSwing.TryGetValue(c.weapon, out n);
            hitsInSwing[c.weapon] = n + 1;

            c.zone.Flash(flashColor, flashDuration);

            Say("[" + r.attacker.displayName + " #" + c.swingId + " " + r.attack + "] ПОПАДАНИЕ → " + r.target.displayName + "." + r.zone
                + "  v=" + r.speed.ToString("0.0") + " м/с, угол=" + r.impactAngleDeg.ToString("0") + "°, по клинку=" + c.bladeT.ToString("0.00")
                + (r.glancing ? "  (скользящее)" : "") + (c.fromOverlap ? "  (клинок уже внутри зоны)" : ""));

            if (HitRegistered != null) HitRegistered(r);
        }

        void OnReleased(WeaponController w)
        {
            Say("[" + w.owner.displayName + " #" + w.SwingId + " " + w.AttackLabel + "] шар выпущен");
        }

        /// <summary>Снаряд попал в чужую зону тела (вызывает EnergyProjectile).</summary>
        public void ReportProjectileHit(FighterController attacker, BodyZone zone, Vector3 point, Vector3 velocity)
        {
            BladeContact c = new BladeContact();
            c.weapon = attacker.weapon;
            c.zone = zone;
            c.point = point;
            c.normal = velocity.sqrMagnitude > 1e-8f ? -velocity.normalized : Vector3.up;
            c.bladeVelocity = velocity;
            c.bladeT = 0f;
            c.swingId = attacker.weapon != null ? attacker.weapon.SwingId : 0;

            HitResult r = new HitResult();
            r.contact = c;
            r.attacker = attacker;
            r.target = zone.owner;
            r.zone = zone.zoneId;
            r.speed = velocity.magnitude;
            r.impactAngleDeg = 0f;
            r.glancing = false;
            r.attack = attacker.weapon != null ? attacker.weapon.AttackLabel : "ШАР";

            zone.Flash(flashColor, flashDuration);
            Say("[" + attacker.displayName + " #" + c.swingId + " " + r.attack + "] ПОПАДАНИЕ → " + r.target.displayName + "." + r.zone
                + "  v=" + r.speed.ToString("0.0") + " м/с");
            if (HitRegistered != null) HitRegistered(r);
        }

        public void ReportProjectileMiss(FighterController attacker)
        {
            int id = attacker.weapon != null ? attacker.weapon.SwingId : 0;
            Say("[" + attacker.displayName + " #" + id + " ШАР] ПРОМАХ: улетел, никого не задел");
        }

        void Say(string msg)
        {
            Log.Add(msg);
            if (Log.Count > 60) Log.RemoveAt(0);
            if (MessageLogged != null) MessageLogged(msg);
            UnityEngine.Debug.Log(msg);
        }
    }
}
