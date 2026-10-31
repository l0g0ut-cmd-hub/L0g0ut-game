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

        static Texture2D star, ring, dragonfly;

        /// <summary>
        /// Стрекоза (тонбо — «насекомое победы» самураев) сверху: тонкое тёмное брюшко, голова-шарик, четыре прозрачных крыла
        /// с размытой кромкой (крылья бьются — в кадре видна дымка, а не перепонки). Брюшко вдоль оси X.
        /// </summary>
        public static Texture2D Dragonfly
        {
            get
            {
                if (dragonfly != null) return dragonfly;
                const int n = 64;
                dragonfly = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "dragonfly" };
                Color[] px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        float a = 0f; Color c = new Color(1f, 1f, 1f, 0f);
                        // крылья: две пары эллипсов поперёк тела (передние чуть впереди груди)
                        for (int w = 0; w < 4; w++)
                        {
                            float cx = w < 2 ? 0.28f : 0.08f, sgn = (w % 2 == 0) ? 1f : -1f;
                            float ex = (u - cx) / 0.13f, ey = (v - sgn * 0.42f) / 0.42f;
                            float e = ex * ex + ey * ey;
                            if (e < 1f) a = Mathf.Max(a, (1f - e) * 0.45f);
                        }
                        Color wing = new Color(0.85f, 0.9f, 0.95f, a);
                        // брюшко (длинное, к хвосту тоньше), грудь, голова
                        float body = 0f;
                        if (u > -0.92f && u < 0.2f) { float th = Mathf.Lerp(0.025f, 0.06f, (u + 0.92f) / 1.12f); if (Mathf.Abs(v) < th) body = 1f; }
                        float tx = (u - 0.3f) / 0.11f, ty = v / 0.08f; if (tx * tx + ty * ty < 1f) body = 1f;
                        float hx = (u - 0.45f) / 0.07f, hy = v / 0.075f; if (hx * hx + hy * hy < 1f) body = 1f;
                        c = body > 0f ? new Color(0.12f, 0.16f, 0.2f, 1f) : wing;
                        px[y * n + x] = c;
                    }
                dragonfly.SetPixels(px); dragonfly.Apply(true);
                return dragonfly;
            }
        }

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
