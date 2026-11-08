using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Пламя клинка — раскалённая сталь (переделано 2026-11-08: четыре нарисованных костра-спрайта на клинке смотрелись плохо).
    /// Теперь: сталь светится изнутри оранжевым (свечение шейдера BladePath/Blade, мерцает), с клинка срываются мелкие искры-угольки
    /// (частицы в мировом пространстве — на взмахе тянутся следом), тёплый неяркий свет, след удара — огненный (trail_fire).
    /// Ignite/Extinguish зовёт DuelManager; в последние 1,5 с накал спадает и гаснет сам.
    /// </summary>
    public class FlameBlade : MonoBehaviour
    {
        WeaponController weapon;
        float until;
        bool on;
        Renderer[] steel = new Renderer[0];
        MaterialPropertyBlock mpb;
        ParticleSystem embers;
        Light glow;
        Color trailWas; string trailTexWas; bool trailSwapped;
        static readonly Color Hot = new Color(1f, 0.36f, 0.08f);

        public static void Ignite(WeaponController w, float seconds)
        {
            if (w == null || w.bladeRoot == null) return;
            FlameBlade fb = w.GetComponent<FlameBlade>();
            if (fb == null) fb = w.gameObject.AddComponent<FlameBlade>();
            fb.weapon = w; fb.until = Time.unscaledTime + seconds;
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
            if (on) return;   // уже горит — продлили время
            on = true;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            // только стальное лезвие (как кровь на клинке в Juice): не рукоять, не цуба
            List<Renderer> list = new List<Renderer>();
            foreach (Renderer r in weapon.bladeRoot.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_EmissionColor") && r.sharedMaterial.name.Contains("Steel")) list.Add(r);
            steel = list.ToArray();
            embers = MakeEmbers();
            GameObject lo = new GameObject("FlameBladeLight");
            lo.transform.SetParent(weapon.bladeRoot, false);
            lo.transform.localPosition = Vector3.forward * (weapon.bladeLength * 0.6f);
            glow = lo.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, 0.5f, 0.2f); glow.range = 2f; glow.intensity = 0.8f;
            WeaponTrail tr = WeaponTrail.Of(weapon);
            if (tr != null && !trailSwapped)
            {
                trailWas = tr.color; trailTexWas = tr.Tex; trailSwapped = true;
                tr.SetLook(new Color(1f, 0.55f, 0.2f), "trail_fire");
            }
        }

        ParticleSystem MakeEmbers()
        {
            GameObject g = new GameObject("FlameBladeEmbers");
            g.transform.SetParent(weapon.bladeRoot, false);
            g.transform.localPosition = Vector3.forward * (weapon.bladeLength * 0.55f);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = 1f;
            main.useUnscaledTime = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.45f, 0.12f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // на взмахе искры остаются в воздухе следом
            main.maxParticles = 120;
            main.gravityModifier = -0.15f;   // тянутся вверх, как от жара
            var em = ps.emission; em.rateOverTime = 45f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.02f, 0.02f, weapon.bladeLength * 0.8f);
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.15f), 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.25f; noise.frequency = 1.6f;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            Shader add = Shader.Find("Legacy Shaders/Particles/Additive");
            Material m = new Material(add != null ? add : Shader.Find("Sprites/Default"));
            m.mainTexture = SoftSprites.Disc;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            r.material = m;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1.2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        void SetGlow(Color c)
        {
            for (int i = 0; i < steel.Length; i++)
            {
                Renderer r = steel[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);   // кровь на клинке (Juice) живёт в том же блоке — читаем и дописываем
                mpb.SetColor("_EmissionColor", c);
                r.SetPropertyBlock(mpb);
            }
        }

        void Clear()
        {
            if (!on) return;
            on = false;
            if (mpb != null) SetGlow(Color.black);
            steel = new Renderer[0];
            if (embers != null)
            {
                // искры не обрываются — дотлевают
                embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                embers.transform.SetParent(null, true);
                Destroy(embers.gameObject, 1f);
            }
            embers = null;
            if (glow != null) Destroy(glow.gameObject);
            glow = null;
            WeaponTrail tr = WeaponTrail.Of(weapon);
            if (tr != null && trailSwapped) tr.SetLook(trailWas, trailTexWas);
            trailSwapped = false;
            until = 0f;
        }

        void Update()
        {
            if (!on) return;
            float left = until - Time.unscaledTime;
            if (left <= 0f) { Clear(); return; }
            float k = Mathf.Clamp01(left / 1.5f);   // к концу накал спадает
            float t = Time.unscaledTime;
            float flick = 0.8f + 0.2f * Mathf.PerlinNoise(t * 7f, 0.3f) + 0.08f * Mathf.Sin(t * 23f);
            SetGlow(Hot * (1.25f * k * flick));
            if (embers != null) { var em = embers.emission; em.rateOverTime = 45f * k; }
            if (glow != null) glow.intensity = 0.8f * k * flick;
        }

        void OnDestroy() { Clear(); }
    }
}
