using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Пламя клинка (по мотивам «气焔» в Onimusha: Way of the Sword, но за серию попаданий, а не парирований):
    /// по клинку бегут языки огня (лист FX/brazier_fire, все 8 кадров), от него тёплый свет, след удара — огненный.
    /// Ignite/Extinguish зовёт DuelManager; в последние 1,5 с огонь опадает и гаснет сам.
    /// </summary>
    public class FlameBlade : MonoBehaviour
    {
        WeaponController weapon;
        float until, total;
        readonly List<Transform> anchors = new List<Transform>();
        readonly List<FxSprite> flames = new List<FxSprite>();
        Light glow;
        Color trailWas; string trailTexWas; bool trailSwapped;
        static readonly float[] Along = { 0.3f, 0.5f, 0.7f, 0.9f };

        public static void Ignite(WeaponController w, float seconds)
        {
            if (w == null || w.bladeRoot == null) return;
            FlameBlade fb = w.GetComponent<FlameBlade>();
            if (fb == null) fb = w.gameObject.AddComponent<FlameBlade>();
            fb.weapon = w; fb.total = seconds; fb.until = Time.unscaledTime + seconds;
            fb.Build();
        }

        public static void Extinguish(WeaponController w)
        {
            if (w == null) return;
            FlameBlade fb = w.GetComponent<FlameBlade>();
            if (fb != null) fb.Clear();
        }

        void Build()
        {
            if (flames.Count > 0) return;   // уже горит — продлили время
            for (int i = 0; i < Along.Length; i++)
            {
                Transform an = new GameObject("FlameAnchor" + i).transform;
                an.SetParent(weapon.bladeRoot, false);
                an.localPosition = Vector3.forward * (weapon.bladeLength * Along[i]) + Vector3.up * 0.03f;
                anchors.Add(an);
                FxSprite f = FxSprite.Spawn("brazier_fire", an.position, 0.2f + 0.04f * (i % 2), 4, 2, 8, 13f + i * 1.5f, true);
                if (f == null) continue;
                f.follow = an; f.unscaled = true;
                f.tint = new Color(1f, 0.7f, 0.4f, 0.8f);
                flames.Add(f);
            }
            GameObject lo = new GameObject("FlameBladeLight");
            lo.transform.SetParent(weapon.bladeRoot, false);
            lo.transform.localPosition = Vector3.forward * (weapon.bladeLength * 0.6f);
            glow = lo.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, 0.55f, 0.2f); glow.range = 2.6f; glow.intensity = 1.3f;
            WeaponTrail tr = WeaponTrail.Of(weapon);
            if (tr != null && !trailSwapped)
            {
                trailWas = tr.color; trailTexWas = tr.Tex; trailSwapped = true;
                tr.SetLook(new Color(1f, 0.55f, 0.2f), "trail_fire");
            }
        }

        void Clear()
        {
            for (int i = 0; i < flames.Count; i++) if (flames[i] != null) Destroy(flames[i].gameObject);
            flames.Clear();
            for (int i = 0; i < anchors.Count; i++) if (anchors[i] != null) Destroy(anchors[i].gameObject);
            anchors.Clear();
            if (glow != null) Destroy(glow.gameObject);
            glow = null;
            WeaponTrail tr = WeaponTrail.Of(weapon);
            if (tr != null && trailSwapped) tr.SetLook(trailWas, trailTexWas);
            trailSwapped = false;
            until = 0f;
        }

        void Update()
        {
            if (flames.Count == 0 && glow == null) return;
            float left = until - Time.unscaledTime;
            if (left <= 0f) { Clear(); return; }
            // огонь дышит; к концу опадает
            float k = Mathf.Clamp01(left / 1.5f);
            float breathe = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 9f);
            for (int i = 0; i < flames.Count; i++)
                if (flames[i] != null) { flames[i].tint = new Color(1f, 0.7f, 0.4f, 0.8f * k); flames[i].size = (0.2f + 0.04f * (i % 2)) * (0.55f + 0.45f * k) * breathe; }
            if (glow != null) glow.intensity = 1.3f * k * (0.8f + 0.2f * Mathf.PerlinNoise(Time.unscaledTime * 6f, 0.4f));
        }

        void OnDestroy() { Clear(); }
    }
}
