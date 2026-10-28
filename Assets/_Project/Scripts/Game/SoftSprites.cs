using UnityEngine;

namespace BladePath
{
    /// <summary>Процедурные мягкие спрайты для частиц и пятен (без внешних текстур).</summary>
    public static class SoftSprites
    {
        static Texture2D disc, petal, splat;

        public static Texture2D Disc { get { if (disc == null) disc = Make(64, 1f, 0.35f, 0f); return disc; } }
        public static Texture2D Petal { get { if (petal == null) petal = Make(64, 0.55f, 0.15f, 0f); return petal; } }
        /// <summary>Неровная клякса: диск с «брызгами» по краю.</summary>
        public static Texture2D Splat { get { if (splat == null) splat = Make(96, 1f, 0.2f, 0.35f); return splat; } }

        // aspect: 1 = круг, <1 = эллипс по X; soft — доля радиуса под сглаживание края; wobble — неровность края
        static Texture2D Make(int n, float aspect, float soft, float wobble)
        {
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            float c = (n - 1) * 0.5f;
            Color[] px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - c) / (c * aspect), dy = (y - c) / c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 1f - wobble * (0.5f + 0.5f * Mathf.Sin(ang * 7f + 1.3f) * Mathf.Sin(ang * 3f + 0.4f));
                    float a = Mathf.Clamp01((edge - d) / soft);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }

    /// <summary>Вешается на систему частиц: подменяет материал копией с мягкой текстурой (лепестки сакуры вместо квадратов).</summary>
    public class PetalTexture : MonoBehaviour
    {
        public bool disc;   // круглый мягкий спрайт (дымка, светлячки) вместо лепестка
        void Awake()
        {
            ParticleSystemRenderer r = GetComponent<ParticleSystemRenderer>();
            if (r == null || r.sharedMaterial == null) return;
            Material m = new Material(r.sharedMaterial);
            m.mainTexture = disc ? SoftSprites.Disc : SoftSprites.Petal;
            r.material = m;
        }
    }
}
