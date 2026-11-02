using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// One-time generated, hand-worn ivory travel dice with three distinct faces.
    /// The generated atlas is 3x1, so it can be drawn by IMGUI without imported PNGs.
    /// </summary>
    public static class JourneyDiceArt
    {
        const int Face = 96;
        static Texture2D atlas;

        public static Texture2D Atlas
        {
            get { if (atlas == null) atlas = Create(); return atlas; }
        }

        static float RoundBox(float x, float y)
        {
            Vector2 q = new Vector2(Mathf.Abs(x), Mathf.Abs(y)) - new Vector2(0.62f, 0.62f);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude +
                Mathf.Min(Mathf.Max(q.x, q.y), 0f) - 0.19f;
        }

        static float Dot(float x, float y, float px, float py)
        {
            float radius = Mathf.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            return 1f - Mathf.SmoothStep(0.12f, 0.16f, radius);
        }

        static Texture2D Create()
        {
            const int w = Face * 3, h = Face;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "Journey_Weathered_Ivory_Dice",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            Color32[] px = new Color32[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int face = x / Face + 1;
                float ux = ((x % Face + 0.5f) / Face - 0.5f) * 2f;
                float uy = ((y + 0.5f) / Face - 0.5f) * 2f;
                float d = RoundBox(ux, uy);
                float a = 1f - Mathf.SmoothStep(0.012f, 0.04f, d);
                if (a <= 0.005f) continue;

                float grain = Mathf.Sin(x * 1.73f + y * 4.19f) *
                              Mathf.Cos(y * 1.51f - x * 3.07f) * 0.014f;
                float light = Mathf.Clamp01(0.93f + (uy - ux) * 0.10f + grain);
                Color body = new Color(0.91f, 0.83f, 0.66f) * light;
                float brassRim = 1f - Mathf.SmoothStep(-0.09f, -0.025f, d);
                if (brassRim < 0.9f)
                {
                    float rim = Mathf.Clamp01((d + 0.092f) * 20f) *
                                (1f - Mathf.SmoothStep(-0.018f, 0.008f, d));
                    body = Color.Lerp(body, new Color(0.55f, 0.32f, 0.13f), rim * 0.9f);
                }

                float pip = 0f;
                if (face == 1 || face == 3)
                    pip = Mathf.Max(pip, Dot(ux, uy, 0f, 0f));
                if (face >= 2)
                {
                    pip = Mathf.Max(pip, Dot(ux, uy, -0.38f, 0.36f));
                    pip = Mathf.Max(pip, Dot(ux, uy, 0.38f, -0.36f));
                }
                float raised = Mathf.Clamp01(pip);
                body = Color.Lerp(body, new Color(0.42f, 0.025f, 0.025f), raised);
                float shine = raised * (1f - Mathf.SmoothStep(-0.05f, 0.12f, uy - ux));
                body = Color.Lerp(body, new Color(0.77f, 0.12f, 0.09f), shine * 0.23f);
                px[y * w + x] = new Color(body.r, body.g, body.b, a);
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
