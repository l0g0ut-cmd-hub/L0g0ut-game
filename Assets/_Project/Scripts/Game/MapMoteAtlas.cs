using UnityEngine;

namespace BladePath
{
    // A generated 4x4 sparkle atlas for map landmarks. No binary texture import
    // and no per-frame texture allocations. Each frame fits inside 32x32 pixels.
    internal static class MapMoteAtlas
    {
        static Texture2D atlas;
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        const int CellSize = 32;

        public static Texture2D Texture
        {
            get
            {
                if (atlas == null) Build();
                return atlas;
            }
        }

        static void Build()
        {
            var pixels = new Color32[128 * 128];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Clear;
            for (int frame = 0; frame < 16; frame++)
            {
                float phase = frame * Mathf.PI * 2f / 16f;
                float pulse = 0.6f + 0.4f * Mathf.Sin(phase) * Mathf.Sin(phase);
                float rotation = phase * 0.25f;
                float cs = Mathf.Cos(rotation), sn = Mathf.Sin(rotation);
                int ox = (frame % 4) * CellSize, oy = (3 - frame / 4) * CellSize;
                for (int y = 0; y < CellSize; y++)
                for (int x = 0; x < CellSize; x++)
                {
                    float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                    float u = dx * cs - dy * sn, v = dx * sn + dy * cs;
                    float core = Mathf.Exp(-((u*u + v*v) * 48f));
                    float armH = Mathf.Exp(-(v*v * 260f + u*u * 8f));
                    float armV = Mathf.Exp(-(u*u * 260f + v*v * 8f));
                    float halo = Mathf.Exp(-((u*u + v*v) * 9f));
                    float a = Mathf.Clamp01((core * 0.85f + (armH + armV) * 0.34f
                        + halo * 0.16f) * pulse);
                    pixels[(oy + y) * 128 + ox + x] =
                        new Color32(255, 238, 182, (byte)(a * 255f));
                }
            }
            atlas = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            atlas.name = "MapMotes_16_Procedural";
            atlas.filterMode = FilterMode.Bilinear;
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.SetPixels32(pixels);
            atlas.Apply(false, true);
        }

        public static Rect Uv(int frame)
        {
            frame = ((frame % 16) + 16) % 16;
            return new Rect((frame % 4) * 0.25f, (3 - frame / 4) * 0.25f,
                0.25f, 0.25f);
        }
    }
}
