using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Рябь пруда: карта нормалей медленно плывёт (смещение текстуры — у Standard общее для всех карт), на порыве ветра
    /// и в дождь вода рябит сильнее, в снег — почти гладкая, как подёрнутая ледком.
    /// </summary>
    public class WaterRipple : MonoBehaviour
    {
        public Vector2 drift = new Vector2(0.012f, 0.007f);
        public float calm = 0.3f;
        Material mat;
        Vector2 off;
        static readonly int BumpId = Shader.PropertyToID("_BumpScale");

        void OnDestroy()
        {
            // Renderer.material creates a private material instance; release it with the pond.
            if (mat != null)
            {
                if (Application.isPlaying) Destroy(mat);
                else DestroyImmediate(mat);
            }
        }

        void Start()
        {
            Renderer r = GetComponent<Renderer>();
            if (r != null)
            {
                mat = r.material;   // свой экземпляр: смещение и сила ряби только у пруда
                // Preserve the level artist's original texture offset rather
                // than snapping the painted water surface to (0,0) at startup.
                if (mat != null) off = mat.mainTextureOffset;
            }
        }

        void Update()
        {
            if (mat == null) return;
            Weather w = Weather.Instance;
            bool rain = w != null && w.Raining, snow = w != null && w.Snowing;
            float wind = WindGusts.Strength;
            float dt = Mathf.Min(0.05f, Time.deltaTime);
            off += drift * (1f + 2.5f * wind) * dt;
            // Keep offsets bounded during multi-hour sessions to prevent precision jitter.
            off.x = Mathf.Repeat(off.x, 1f);
            off.y = Mathf.Repeat(off.y, 1f);
            mat.mainTextureOffset = off;
            float target = snow ? 0.08f : calm + 0.35f * wind + (rain ? 0.5f : 0f);
            if (mat.HasProperty(BumpId))
                mat.SetFloat(BumpId,
                    Mathf.MoveTowards(mat.GetFloat(BumpId), target, dt * 0.5f));
        }
    }
}
