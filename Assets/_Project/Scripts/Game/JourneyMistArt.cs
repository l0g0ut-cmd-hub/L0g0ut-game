using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Soft painted mist texture for the world map. Generates one small atlas
    /// at startup instead of layering large particle systems over the map.
    /// Subtle paper-grain wisps preserve readability of location badges.
    /// </summary>
    public static class JourneyMistArt
    {
        static Texture2D texture;

        public static Texture2D Texture
        {
            get
            {
                if (texture == null) texture = Create();
                return texture;
            }
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        static Texture2D Create()
        {
            const int w = 256, h = 128;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "Journey_Map_Painted_Mist",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            Color32[] pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float v = (y + 0.5f) / h;
                float longWave = Mathf.PerlinNoise(u * 3.3f + 8.5f, v * 1.1f + 2.7f);
                float fineWave = Mathf.PerlinNoise(u * 9.7f + 5.8f, v * 3.0f + 11.2f);
                float flow = 0.5f + 0.16f * Mathf.Sin(u * 10.5f + longWave * 2.2f);
                float ridge = Smooth(1f - Mathf.Abs(v - flow) * 4.5f);
                float wisps = Smooth((longWave * 0.7f + fineWave * 0.3f - 0.34f) * 2.2f);
                float ends = Smooth(u * 6f) * Smooth((1f - u) * 6f);
                float alpha = Mathf.Clamp01(ridge * wisps * ends * 0.6f);
                // Alpha mask only; tint is applied per-region through GUI.color.
                pixels[y * w + x] = new Color32(247, 247, 242,
                    (byte)Mathf.RoundToInt(255f * alpha));
            }
            t.SetPixels32(pixels);
            t.Apply(false, true);
            return t;
        }
    }
}
