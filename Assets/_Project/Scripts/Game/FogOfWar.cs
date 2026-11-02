using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Туман войны на карте похода, как в «Героях»: неразведанное закрыто рисованным туманом
    /// (UI/fog_tile_bytes — бесшовная плитка), край тумана — мягкий и клубится (UI/fog_puffs — 16 клубов 4×4).
    /// Разведано: известные места (MapScreen.Known), путь героя и сам герой. Маска считается на CPU
    /// в текстуру 162×108 только при изменении разведки — в кадре один DrawTexture плюс ~40 клубов.
    /// </summary>
    public static class FogOfWar
    {
        const int MW = 162, MH = 108;            // маска: 5 точек карты (810×540) на пиксель
        const float RevealR = 92f, Soft = 46f;   // радиус разведки вокруг места и ширина мягкого края, в точках карты 810×540
        static Texture2D mask, tile;
        static Color32[] tilePx; static int tileW, tileH;
        static Color32[] px;
        static string key;
        static readonly List<Vector3> puffs = new List<Vector3>(64);   // x, y (0..1 карты), фаза

        static void LoadTile()
        {
            if (tilePx != null) return;
            TextAsset ta = Resources.Load<TextAsset>("UI/fog_tile_bytes");
            tile = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (ta == null || !tile.LoadImage(ta.bytes)) { tilePx = new Color32[] { new Color32(34, 26, 48, 255) }; tileW = tileH = 1; return; }
            tilePx = tile.GetPixels32(); tileW = tile.width; tileH = tile.height;
            Object.Destroy(tile); tile = null;
        }

        /// <summary>Пересчитать маску, если разведка изменилась. points — центры разведки в долях карты.</summary>
        static void Rebuild(List<Vector2> points, List<Vector2> soft)
        {
            LoadTile();
            if (mask == null)
            {
                mask = new Texture2D(MW, MH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                px = new Color32[MW * MH];
            }
            puffs.Clear();
            float sx = 810f / MW, sy = 540f / MH;
            for (int y = 0; y < MH; y++)
            {
                for (int x = 0; x < MW; x++)
                {
                    float mx = (x + 0.5f) * sx, my = (MH - 1 - y + 0.5f) * sy;   // текстура снизу вверх
                    float best = 1e9f;
                    foreach (Vector2 p in points)
                    {
                        float dx = p.x * 810f - mx, dy = p.y * 540f - my;
                        float d = dx * dx + dy * dy; if (d < best) best = d;
                    }
                    float d1 = Mathf.Sqrt(best) - RevealR;
                    float bestS = 1e9f;
                    foreach (Vector2 p in soft)   // следы пройденных дорог: узкая полоса
                    {
                        float dx = p.x * 810f - mx, dy = p.y * 540f - my;
                        float d = dx * dx + dy * dy; if (d < bestS) bestS = d;
                    }
                    float d2 = Mathf.Sqrt(bestS) - RevealR * 0.45f;
                    float t = Mathf.Clamp01(Mathf.Min(d1, d2) / Soft);
                    t = t * t * (3f - 2f * t);
                    // плитка тумана повторяется дважды по ширине карты
                    Color32 c = tilePx[((y * 2 * tileH / MH) % tileH) * tileW + (x * 3 * tileW / MW) % tileW];
                    px[y * MW + x] = new Color32(c.r, c.g, c.b, (byte)(t * 240f));
                    if (t > 0.25f && t < 0.6f && ((x * 7 + y * 13) % 23) == 0 && puffs.Count < 48)
                        puffs.Add(new Vector3(mx / 810f, my / 540f, (x * 0.37f + y * 0.71f) % 6.28f));
                }
            }
            mask.SetPixels32(px); mask.Apply(false);
        }

        /// <summary>Нарисовать туман поверх карты. Вызывать после дорог и до мест на карте.</summary>
        public static void Draw(Rect view, float u0, float uw, List<Vector2> points, List<Vector2> soft, string stateKey)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (mask == null || stateKey != key) { key = stateKey; Rebuild(points, soft); }
            GUI.DrawTextureWithTexCoords(view, mask, new Rect(u0, 0f, uw, 1f));
            // клубы на краю: медленно дышат и дрейфуют
            Texture2D pf = UiKit.Tex("UI/fog_puffs");
            if (pf == null) return;
            Color oc = GUI.color;
            float t = Time.unscaledTime;
            for (int i = 0; i < puffs.Count; i++)
            {
                Vector3 p = puffs[i];
                float sx = view.x + (p.x - u0) / uw * view.width, sy = p.y * view.height;
                if (sx < view.x - 60f || sx > view.xMax + 60f) continue;
                float br = 0.5f + 0.5f * Mathf.Sin(t * 0.35f + p.z);
                float size = 86f + 22f * br;
                sx += 9f * Mathf.Sin(t * 0.13f + p.z * 1.7f);
                sy += 5f * Mathf.Cos(t * 0.11f + p.z);
                int cell = i % 16;
                GUI.color = new Color(0.62f, 0.55f, 0.78f, 0.28f + 0.16f * br);
                GUI.DrawTextureWithTexCoords(new Rect(sx - size * 0.5f, sy - size * 0.38f, size, size * 0.76f), pf,
                    new Rect((cell % 4) * 0.25f, 1f - (cell / 4 + 1) * 0.25f, 0.25f, 0.25f));
            }
            GUI.color = oc;
        }
    }
}
