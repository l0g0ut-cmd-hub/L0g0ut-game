using UnityEngine;

namespace BladePath
{
    /// <summary>Процедурные мягкие спрайты для частиц и пятен (без внешних текстур).</summary>
    public static class SoftSprites
    {
        static Texture2D disc, petal, splat, fog;

        public static Texture2D Disc { get { if (disc == null) disc = Make(64, 1f, 0.35f, 0f); return disc; } }
        public static Texture2D Petal { get { if (petal == null) petal = Make(64, 0.55f, 0.15f, 0f); return petal; } }
        /// <summary>Гауссово пятно без видимого края — для дымки и пыли.</summary>
        public static Texture2D Fog
        {
            get
            {
                if (fog != null) return fog;
                const int n = 64;
                fog = new Texture2D(n, n, TextureFormat.RGBA32, false);
                fog.wrapMode = TextureWrapMode.Clamp;
                Color[] px = new Color[n * n];
                float c = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - c) / c, dy = (y - c) / c;
                        float d2 = dx * dx + dy * dy;
                        px[y * n + x] = new Color(1f, 1f, 1f, d2 >= 1f ? 0f : Mathf.Exp(-d2 * 4f) * (1f - d2));
                    }
                fog.SetPixels(px); fog.Apply();
                return fog;
            }
        }

        static Texture2D star, ring;

        /// <summary>Блик металла: яркое ядро и четыре тонких луча (крест чуть повёрнут), края в ноль.</summary>
        public static Texture2D Star
        {
            get
            {
                if (star != null) return star;
                const int n = 128;
                star = new Texture2D(n, n, TextureFormat.RGBA32, false);
                star.wrapMode = TextureWrapMode.Clamp; star.filterMode = FilterMode.Bilinear;
                Color[] px = new Color[n * n];
                float c = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - c) / c, dy = (y - c) / c;
                        float r2 = dx * dx + dy * dy;
                        float fade = Mathf.Clamp01(1f - Mathf.Sqrt(r2));
                        float core = Mathf.Exp(-r2 * 80f) + 0.12f * Mathf.Exp(-r2 * 14f);
                        // лучи: узкие по одной оси, длинные по другой; диагональные — слабее и короче
                        float rays = Mathf.Exp(-Mathf.Abs(dy) * 70f) * Mathf.Exp(-Mathf.Abs(dx) * 2.2f) + Mathf.Exp(-Mathf.Abs(dx) * 70f) * Mathf.Exp(-Mathf.Abs(dy) * 2.2f);
                        float u = (dx + dy) * 0.7071f, v = (dx - dy) * 0.7071f;
                        rays += 0.35f * (Mathf.Exp(-Mathf.Abs(v) * 90f) * Mathf.Exp(-Mathf.Abs(u) * 5f) + Mathf.Exp(-Mathf.Abs(u) * 90f) * Mathf.Exp(-Mathf.Abs(v) * 5f));
                        float a = Mathf.Clamp01((core + rays) * fade * fade);
                        px[y * n + x] = new Color(1f, 1f, 1f, a);
                    }
                star.SetPixels(px); star.Apply();
                return star;
            }
        }

        /// <summary>Тонкое мягкое кольцо — для ударной волны.</summary>
        public static Texture2D Ring
        {
            get
            {
                if (ring != null) return ring;
                const int n = 128;
                ring = new Texture2D(n, n, TextureFormat.RGBA32, false);
                ring.wrapMode = TextureWrapMode.Clamp; ring.filterMode = FilterMode.Bilinear;
                Color[] px = new Color[n * n];
                float c = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - c) / c, dy = (y - c) / c;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float k = (d - 0.82f) / 0.11f;
                        float a = d > 1f ? 0f : Mathf.Exp(-k * k) * (d < 0.82f ? 1f : 1f) + 0.12f * Mathf.Exp(-(d - 0.7f) * (d - 0.7f) * 30f);
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                    }
                ring.SetPixels(px); ring.Apply();
                return ring;
            }
        }

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
}
