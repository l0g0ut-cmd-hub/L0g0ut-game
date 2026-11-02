using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// 32 continuous frames (4x8) of fine jade and honey-gold healing light.
    /// Generated once into a 320x640 RGBA atlas: no missing PNG imports,
    /// no additional draw calls for each sparkle, no loading hiccups per drink.
    /// The phase goes upward with the gulp and dissolves, not an 8-frame loop.
    /// </summary>
    public static class HealingAuraArt
    {
        const int Cell = 80, Columns = 4, Rows = 8, Frames = Columns * Rows;
        static Texture2D atlas;
        public static Texture2D Atlas
        {
            get
            {
                if (atlas == null) atlas = Build();
                return atlas;
            }
        }

        static float Gauss(float x, float spread)
        {
            float n = x / Mathf.Max(0.0001f, spread);
            return Mathf.Exp(-n * n);
        }
        static float Ring(float x, float center, float width)
        {
            return Gauss(x - center, width);
        }

        static Texture2D Build()
        {
            int w = Cell * Columns, h = Cell * Rows;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "HealingAura_32_frames",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            Color32[] pixels = new Color32[w * h];
            // 32 unique frames, no duplicates; flow changes continuously.
            for (int frame = 0; frame < Frames; frame++)
            {
                float q = frame / (float)(Frames - 1);
                float rise = Mathf.SmoothStep(0f, 1f, q);
                float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(q * 7.0f));
                float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((q - 0.69f) / 0.31f));
                float life = fadeIn * fadeOut;
                int ox = (frame % Columns) * Cell;
                // Top-to-bottom image rows: consistent with FxSprite atlas UV.
                int oy = (Rows - 1 - frame / Columns) * Cell;
                for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    float u = (x + 0.5f) / Cell * 2f - 1f;
                    float v = (y + 0.5f) / Cell * 2f - 1f;
                    float radius = Mathf.Sqrt(u * u + v * v);
                    float angle = Mathf.Atan2(v, u);
                    // Air currents move actual strokes, not static glow brightness.
                    float swirl = angle - q * 5.4f - v * 0.65f;
                    float plume = 0f;
                    for (int lane = 0; lane < 3; lane++)
                    {
                        float phase = q * 7.3f + lane * 2.094f;
                        float cx = Mathf.Sin(phase + v * 2.8f) * (0.20f + 0.08f * lane);
                        float yy = -0.65f + Mathf.Repeat(q * 1.7f + lane / 3f, 1f) * 1.45f;
                        plume += Gauss(u - cx, 0.075f + lane * 0.008f) *
                                 Gauss(v - yy, 0.23f) * (0.48f - lane * 0.07f);
                    }

                    float spiral = Ring(radius, 0.39f + 0.07f * q, 0.032f) *
                                   Mathf.Pow(0.5f + 0.5f * Mathf.Sin(swirl * 5f), 5f);
                    float inner = Ring(radius, 0.18f + 0.09f * rise, 0.037f) *
                                  (0.36f + 0.25f * Mathf.Cos(angle * 4f - q * 3.1f));
                    float cloud = Gauss(radius, 0.30f + 0.15f * q) *
                                  (0.15f + 0.16f * Mathf.Sin(q * 4f + radius * 4f));
                    float motes = 0f;
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k * 2.39996f + q * (0.9f + k * 0.14f);
                        float mx = Mathf.Cos(a) * (0.23f + 0.07f * k);
                        float my = -0.7f + Mathf.Repeat(q * (0.65f + k * 0.08f) + k * 0.31f, 1f) * 1.46f;
                        motes += Gauss(u - mx, 0.021f) * Gauss(v - my, 0.031f);
                    }
                    float a0 = Mathf.Clamp01((plume + spiral * 0.47f + inner * 0.5f +
                                             cloud + motes * 0.72f) * life);
                    float golden = Mathf.Clamp01(radius * 0.6f + motes * 0.8f +
                        0.2f * Mathf.Sin(q * 5f + angle));
                    Color color = Color.Lerp(
                        new Color(0.31f, 0.85f, 0.59f),
                        new Color(1f, 0.84f, 0.43f), golden);
                    pixels[(oy + y) * w + ox + x] = new Color32(
                        (byte)Mathf.RoundToInt(color.r * 255f),
                        (byte)Mathf.RoundToInt(color.g * 255f),
                        (byte)Mathf.RoundToInt(color.b * 255f),
                        (byte)Mathf.RoundToInt(a0 * 255f));
                }
            }
            t.SetPixels32(pixels);
            t.Apply(false, true);
            return t;
        }
    }
}
