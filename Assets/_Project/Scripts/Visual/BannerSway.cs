using UnityEngine;

namespace BladePath
{
    /// <summary>Знамя колышется на ветру: поворот вокруг точки подвеса (объект с этим компонентом — подвес).</summary>
    public class BannerSway : MonoBehaviour
    {
        public float amp = 6f, speed = 1.3f;
        Quaternion baseRot;
        float seed, phase;
        void Awake() { baseRot = transform.localRotation; seed = Random.value * 10f; }
        void Update()
        {
            float g = WindGusts.Strength;
            phase += Time.deltaTime * speed * (1f + 0.9f * g);   // в порыв — чаще и шире
            float t = phase + seed;
            float rain = (Weather.Instance != null && Weather.Instance.Raining ? 1.6f : 1f) * (1f + 1.4f * g);
            float a = (Mathf.Sin(t) * 0.6f + Mathf.Sin(t * 2.3f + 1f) * 0.4f) * amp * rain;
            float b = Mathf.Sin(t * 0.7f + 2f) * amp * 0.5f * rain;
            transform.localRotation = baseRot * Quaternion.Euler(a, 0f, b);
        }
    }
}
