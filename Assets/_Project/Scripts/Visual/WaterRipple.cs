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

        void Start()
        {
            Renderer r = GetComponent<Renderer>();
            if (r != null) mat = r.material;   // свой экземпляр: смещение и сила ряби только у пруда
        }

        void Update()
        {
            if (mat == null) return;
            Weather w = Weather.Instance;
            bool rain = w != null && w.Raining, snow = w != null && w.Snowing;
            float wind = WindGusts.Strength;
            off += drift * (1f + 2.5f * wind) * Time.deltaTime;
            mat.mainTextureOffset = off;
            float target = snow ? 0.08f : calm + 0.35f * wind + (rain ? 0.5f : 0f);
            mat.SetFloat(BumpId, Mathf.MoveTowards(mat.GetFloat(BumpId), target, Time.deltaTime * 0.5f));
        }
    }
}
