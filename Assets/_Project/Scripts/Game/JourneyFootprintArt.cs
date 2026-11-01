using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Tiny paired bootprints for the adventure map, generated once at runtime.
    /// White alpha-mask lets IMGUI tint travelled, reachable and exhausted steps
    /// without extra imported textures or materials.
    /// </summary>
    public static class JourneyFootprintArt
    {
        static Texture2D footsteps;

        public static Texture2D Footsteps
        {
            get
            {
                if (footsteps == null) footsteps = Create();
                return footsteps;
            }
        }

        static float Oval(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return Mathf.Clamp01((1.2f - dx * dx - dy * dy) * 3f);
        }

        static Texture2D Create()
        {
            const int n = 40;
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "Map_Journey_Footsteps",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            Color32[] px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float ux = (x + 0.5f) / n * 2f - 1f;
                float uy = (y + 0.5f) / n * 2f - 1f;
                // Toe and heel for each foot. Slightly alternating placement
                // reads like a short marching trail rather than a circular dot.
                float left = Mathf.Max(
                    Oval(ux, uy, -0.30f, 0.34f, 0.22f, 0.34f),
                    Oval(ux, uy, -0.34f, -0.07f, 0.18f, 0.20f));
                float right = Mathf.Max(
                    Oval(ux, uy, 0.30f, -0.11f, 0.22f, 0.34f),
                    Oval(ux, uy, 0.34f, -0.49f, 0.18f, 0.20f));
                float a = Mathf.Clamp01(Mathf.Max(left, right));
                px[y * n + x] = new Color32(255, 250, 228, (byte)Mathf.RoundToInt(a * 255f));
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
