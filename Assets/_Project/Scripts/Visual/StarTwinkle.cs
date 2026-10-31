using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Мерцание звёзд ясной ночью: поверх нарисованного неба — несколько десятков ярких звёзд на сфере вокруг двора; каждая
    /// живёт 6–12 с и за это время несколько раз вспыхивает и притухает (атмосфера дрожит), на смену гаснущим проступают новые.
    /// Только в верхней части неба. Днём, в дождь, снег и туман — нет (в тумане их и так прячет туманный купол).
    /// </summary>
    public class StarTwinkle : MonoBehaviour
    {
        ParticleSystem ps;
        bool on;

        void Start()
        {
            GameObject g = new GameObject("StarTwinkle");
            g.transform.SetParent(transform, false);
            g.transform.position = new Vector3(0f, 0f, 2f);
            g.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // полусфера куполом вверх
            ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f); main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.3f); main.maxParticles = 60;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.82f, 1f, 1f), new Color(1f, 0.95f, 0.85f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 5f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 40f; sh.radiusThickness = 0f;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.35f, 0.25f), new GradientAlphaKey(1f, 0.4f),
                               new GradientAlphaKey(0.45f, 0.55f), new GradientAlphaKey(0.95f, 0.7f), new GradientAlphaKey(0.3f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var r = g.GetComponent<ParticleSystemRenderer>();
            Shader s = Shader.Find("Legacy Shaders/Particles/Additive");
            if (s != null) { Material m = new Material(s) { name = "Star" }; m.mainTexture = SoftSprites.Disc; m.SetColor("_TintColor", new Color(0.45f, 0.47f, 0.52f, 0.7f)); r.sharedMaterial = m; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        void Update()
        {
            if (ps == null) return;
            Weather w = Weather.Instance;
            DayNight dn = DayNight.Instance;
            bool want = (dn == null || !dn.Day) && !(w != null && (w.Raining || w.Snowing || w.Foggy));
            if (want == on) return;
            on = want;
            if (on) ps.Play(); else { ps.Stop(); ps.Clear(); }
        }
    }
}
