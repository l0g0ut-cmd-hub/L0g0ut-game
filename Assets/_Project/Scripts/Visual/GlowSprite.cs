using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Ореол света (имитация bloom без постобработки): аддитивный квад всегда смотрит в камеру, слегка мерцает.
    /// </summary>
    public class GlowSprite : MonoBehaviour
    {
        /// <summary>Влажность воздуха (Weather): в тумане, дождь и снегопад ореолы фонарей и огня шире — свет рассеивается.</summary>
        public static float Haze = 1f;
        public float flicker = 0.12f;
        public bool noHaze;   // диск луны и т.п. — не раздувать в тумане
        public float speed = 9f;
        Vector3 baseScale;
        float seed;

        void Awake() { baseScale = transform.localScale; seed = Random.value * 100f; }

        void LateUpdate()
        {
            Camera c = Camera.main;
            if (c == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position, c.transform.up);
            float k = 1f;
            if (flicker > 0f)
            {
                float t = Time.time * speed + seed;
                k = 1f + flicker * (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f;
            }
            float hz = noHaze ? 1f : Haze;
            if (flicker > 0f || hz != 1f) transform.localScale = baseScale * (k * hz);
        }
    }
}
