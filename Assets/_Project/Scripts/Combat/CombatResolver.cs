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
    }

    /// <summary>
    /// Этап 1: превращает подтверждённый контакт в читаемый результат (попадание / промах) и
    /// сообщает об этом наружу. Урон, блоки и реакции — этапы 2-4, здесь их нет.
    /// Визуальная подсветка зоны — только отклик, решение «попал/нет» принято раньше, в сэмплере.
    /// </summary>
    public class CombatResolver : MonoBehaviour
    {
        public BladeTrajectorySampler[] samplers;
        public float glancingAngleDeg = 70f;
        public Color flashColor = new Color(0.9f, 0.05f, 0.05f);
        public float flashDuration = 0.35f;

        public event Action<HitResult> HitRegistered;
        public event Action<string> MessageLogged;

        public readonly List<string> Log = new List<string>();
        readonly Dictionary<WeaponController, int> hitsInSwing = new Dictionary<WeaponController, int>();

        void OnEnable()
        {
            if (samplers == null) return;
            for (int i = 0; i < samplers.Length; i++)
            {
                if (samplers[i] == null) continue;
                samplers[i].ContactDetected += OnContact;
                samplers[i].Weapon.ContactWindowOpened += OnWindowOpened;
                samplers[i].Weapon.ContactWindowClosed += OnWindowClosed;
            }
        }

        void OnDisable()
        {
            if (samplers == null) return;
            for (int i = 0; i < samplers.Length; i++)
            {
                if (samplers[i] == null) continue;
                samplers[i].ContactDetected -= OnContact;
                samplers[i].Weapon.ContactWindowOpened -= OnWindowOpened;
                samplers[i].Weapon.ContactWindowClosed -= OnWindowClosed;
            }
        }

        void OnWindowOpened(WeaponController w)
        {
            hitsInSwing[w] = 0;
            Say("[" + w.owner.displayName + " #" + w.SwingId + "] удар начат");
        }

        void OnWindowClosed(WeaponController w)
        {
            int n;
            hitsInSwing.TryGetValue(w, out n);
            string who = "[" + w.owner.displayName + " #" + w.SwingId + "] ";
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

            Vector3 dir = r.speed > 1e-4f ? c.bladeVelocity / r.speed : Vector3.zero;
            float cosIn = Mathf.Clamp(Vector3.Dot(dir, -c.normal), -1f, 1f);
            r.impactAngleDeg = Mathf.Acos(cosIn) * Mathf.Rad2Deg;
            r.glancing = r.impactAngleDeg > glancingAngleDeg;

            int n;
            hitsInSwing.TryGetValue(c.weapon, out n);
            hitsInSwing[c.weapon] = n + 1;

            c.zone.Flash(flashColor, flashDuration);

            Say("[" + r.attacker.displayName + " #" + c.swingId + "] ПОПАДАНИЕ → " + r.target.displayName + "." + r.zone
                + "  v=" + r.speed.ToString("0.0") + " м/с, угол=" + r.impactAngleDeg.ToString("0") + "°, по клинку=" + c.bladeT.ToString("0.00")
                + (r.glancing ? "  (скользящее)" : "") + (c.fromOverlap ? "  (клинок уже внутри зоны)" : ""));

            if (HitRegistered != null) HitRegistered(r);
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
