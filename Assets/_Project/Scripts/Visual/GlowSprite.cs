using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Ореол света (имитация bloom без постобработки): аддитивный квад всегда смотрит в камеру, слегка мерцает.
    /// </summary>
    public class GlowSprite : MonoBehaviour
    {
        public float flicker = 0.12f;
        public float speed = 9f;
        Vector3 baseScale;
        float seed;

        void Awake() { baseScale = transform.localScale; seed = Random.value * 100f; }

        void LateUpdate()
        {
            Camera c = Camera.main;
            if (c == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position, c.transform.up);
            if (flicker > 0f)
            {
                float t = Time.time * speed + seed;
                float k = 1f + flicker * (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f;
                transform.localScale = baseScale * k;
            }
        }
    }
}
