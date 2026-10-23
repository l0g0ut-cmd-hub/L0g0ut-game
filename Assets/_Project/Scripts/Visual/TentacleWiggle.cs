using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Живая анимация щупальца врага: цепочка сегментов (сфер) колеблется волной вдоль заданного направления.
    /// ТОЛЬКО ВИЗУАЛ: у щупалец нет зон поражения (появятся, когда решим, как они участвуют в бою).
    /// Позиции задаются в локальных координатах корня щупальца, поэтому оно едет вместе с бойцом.
    /// </summary>
    public class TentacleWiggle : MonoBehaviour
    {
        public Transform[] segments;
        public Vector3 direction = Vector3.right;   // куда растёт щупальце (локально, относительно бойца)
        public float length = 0.9f;
        public float amplitude = 0.12f;             // размах волны
        public float speed = 2.2f;
        public float phase;
        public float curl = 0.25f;                  // общий изгиб вниз к кончику

        void Update()
        {
            if (segments == null || segments.Length < 2) return;
            int n = segments.Length;
            Vector3 dir = direction.normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir);
            float t0 = Time.time * speed + phase;

            for (int i = 0; i < n; i++)
            {
                if (segments[i] == null) continue;
                float t = i / (float)(n - 1);
                Vector3 p = dir * (length * t)
                          + Vector3.down * (curl * t * t)
                          + side * (Mathf.Sin(t0 + t * 5f) * amplitude * t)
                          + up * (Mathf.Cos(t0 * 0.8f + t * 4f) * amplitude * 0.6f * t);
                segments[i].localPosition = p;
            }
        }
    }
}
