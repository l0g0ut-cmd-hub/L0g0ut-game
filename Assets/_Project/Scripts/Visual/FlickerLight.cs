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

        void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * speed);
            float k = 1f - amount * 0.5f + amount * n;
            if (lightSource != null) lightSource.intensity = baseIntensity * k;
            if (flame != null) flame.localScale = flameBaseScale * (0.9f + 0.2f * n);
        }
    }
}
