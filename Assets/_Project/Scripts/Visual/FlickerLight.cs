using UnityEngine;

namespace BladePath
{
    /// <summary>Мерцание огня: плавно меняет яркость света и размер пламени шумом Перлина.</summary>
    public class FlickerLight : MonoBehaviour
    {
        public Light lightSource;
        public Transform flame;                 // необязательно: сфера пламени, дышит вместе со светом
        public float baseIntensity = 1.6f;
        public float amount = 0.55f;            // 0..1 — глубина мерцания
        public float speed = 1.8f;

        float seed;
        Vector3 flameBaseScale;

        void Awake()
        {
            seed = Random.value * 100f;
            if (lightSource == null) lightSource = GetComponent<Light>();
            if (flame != null) flameBaseScale = flame.localScale;
        }

        ParticleSystem[] fires;
        bool wasRain;
        bool firesFound;

        void Update()
        {
            // порыв ветра (WindGusts): огонь бьётся чаще и глубже, языки пламени и искры сносит по ветру
            float g = WindGusts.Strength;
            float n = Mathf.PerlinNoise(seed, Time.time * speed * (1f + 1.5f * g));
            float am = Mathf.Min(1f, amount * (1f + 0.8f * g));
            float k = 1f - am * 0.5f + am * n;
            if (lightSource != null) lightSource.intensity = baseIntensity * k;
            if (flame != null) flame.localScale = flameBaseScale * (0.9f + 0.2f * n);
            if (!firesFound)
            {
                firesFound = true;
                Transform root = transform.parent != null ? transform.parent : transform;
                fires = root.GetComponentsInChildren<ParticleSystem>(true);
            }
            // дождь: над огнём вместо дыма — светлый пар, гуще; огонь чуть никнет
            bool rain = Weather.Instance != null && Weather.Instance.Raining;
            if (rain != wasRain && fires != null)
            {
                wasRain = rain;
                foreach (ParticleSystem ps in fires)
                {
                    if (ps == null || ps.name != "Smoke") continue;
                    var m = ps.main; m.startColor = rain ? new Color(0.55f, 0.56f, 0.6f, 0.38f) : new Color(0.16f, 0.14f, 0.13f, 0.32f);
                    var e = ps.emission; e.rateOverTime = rain ? 9f : 3.5f;
                }
            }
            if (lightSource != null && rain) lightSource.intensity *= 0.85f;
            if (fires != null)
                foreach (ParticleSystem ps in fires)
                {
                    if (ps == null) continue;
                    var f = ps.forceOverLifetime;
                    if (g < 0.01f) { if (f.enabled) f.enabled = false; continue; }
                    f.enabled = true; f.space = ParticleSystemSimulationSpace.World;
                    Vector3 w = WindGusts.Dir * (g * 2.4f);
                    f.x = new ParticleSystem.MinMaxCurve(w.x); f.y = new ParticleSystem.MinMaxCurve(0f); f.z = new ParticleSystem.MinMaxCurve(w.z);
                }
        }
    }
}
