using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Падающие звёзды ясной ночью: раз в 18–45 с высоко над рощей чиркает метеор — тонкий светящийся росчерк, вспыхивает и
    /// гаснет за полсекунды; изредка — два подряд. В дождь, снег, туман и днём их нет.
    /// </summary>
    public class ShootingStars : MonoBehaviour
    {
        ParticleSystem ps;
        float nextAt;

        void Start()
        {
            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.startLifetime = 0.65f; main.startSpeed = 0f;
            main.startSize = 0.35f; main.maxParticles = 4; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.92f, 1f), 0f), new GradientColorKey(new Color(1f, 0.9f, 0.75f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.18f; r.lengthScale = 2f;
            Shader s = Shader.Find("Legacy Shaders/Particles/Additive");
            if (s != null) { Material m = new Material(s) { name = "Meteor" }; m.mainTexture = SoftSprites.Disc; m.SetColor("_TintColor", new Color(0.6f, 0.62f, 0.7f, 0.8f)); r.sharedMaterial = m; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            nextAt = Time.time + Random.Range(8f, 20f);
        }

        void Update()
        {
            if (ps == null || Time.time < nextAt) return;
            nextAt = Time.time + Random.Range(18f, 45f);
            Weather w = Weather.Instance;
            bool clear = (DayNight.Instance == null || !DayNight.Instance.Day) && (w == null || (!w.Raining && !w.Snowing && !w.Foggy));
            if (!clear) return;
            Emit();
            if (Random.value < 0.2f) Invoke(nameof(Emit), Random.Range(0.6f, 1.6f));
        }

        void Emit()
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            var ep = new ParticleSystem.EmitParams();
            ep.position = new Vector3(Random.Range(-30f, 30f), Random.Range(26f, 40f), Random.Range(45f, 75f));
            ep.velocity = new Vector3(side * Random.Range(28f, 45f), -Random.Range(6f, 14f), Random.Range(-4f, 4f));
            ep.startLifetime = Random.Range(0.45f, 0.8f);
            ep.startSize = Random.Range(0.25f, 0.45f);
            ps.Emit(ep, 1);
        }
    }
}
