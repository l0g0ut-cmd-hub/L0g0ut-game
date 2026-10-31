using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Карпы кои в пруду: 4 рыбы медленно кружат под полупрозрачной тёмной водой, каждая — по своему эллипсу со своей
    /// скоростью, плавно меняя направление; хвост виляет (рыскание), иногда рыба замирает и снова трогается.
    /// В снег — уходят на дно (невидимы).
    /// </summary>
    public class KoiPond : MonoBehaviour
    {
        public Transform[] fish;
        public Vector2 radii = new Vector2(1.15f, 0.62f);
        float[] ang, spd, wob, pause;

        void Start()
        {
            int n = fish != null ? fish.Length : 0;
            ang = new float[n]; spd = new float[n]; wob = new float[n]; pause = new float[n];
            for (int i = 0; i < n; i++) { ang[i] = Random.value * 6.28f; spd[i] = Random.Range(0.12f, 0.22f) * (Random.value < 0.5f ? 1f : -1f); wob[i] = Random.value * 6.28f; }
        }

        void Update()
        {
            if (fish == null) return;
            bool hide = Weather.Instance != null && Weather.Instance.Snowing;
            float dt = Time.deltaTime, t = Time.time;
            for (int i = 0; i < fish.Length; i++)
            {
                Transform f = fish[i];
                if (f == null) continue;
                if (f.gameObject.activeSelf == hide) f.gameObject.SetActive(!hide);
                if (hide) continue;
                // иногда замирает
                if (pause[i] > 0f) pause[i] -= dt; else if (Random.value < dt * 0.05f) pause[i] = Random.Range(1.5f, 4f);
                float s = pause[i] > 0f ? 0.15f : 1f;
                ang[i] += spd[i] * dt * s;
                float k = 0.55f + 0.35f * Mathf.Sin(t * 0.13f + i * 1.7f);   // радиус орбиты «дышит» — не по рельсам
                Vector3 p = new Vector3(Mathf.Cos(ang[i]) * radii.x * k, 0f, Mathf.Sin(ang[i]) * radii.y * k);
                Vector3 tangent = new Vector3(-Mathf.Sin(ang[i]) * radii.x, 0f, Mathf.Cos(ang[i]) * radii.y) * Mathf.Sign(spd[i]);
                wob[i] += dt * (s > 0.5f ? 7f : 2.5f);
                float yaw = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg + Mathf.Sin(wob[i]) * 9f;
                f.localPosition = new Vector3(p.x, f.localPosition.y, p.z);
                f.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }
    }
}
