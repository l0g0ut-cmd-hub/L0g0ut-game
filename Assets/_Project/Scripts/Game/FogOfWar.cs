using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Туман войны на карте похода, как в «Героях 3»: туман расходится вокруг фигурки героя по мере её движения
    /// и больше не возвращается. Разведанное — набор точек, где побывал герой (Campaign.Seen), у каждой радиус обзора.
    /// Маска 162×108 считается на CPU и обновляется кусочно: новая точка пересчитывает только пиксели рядом с собой
    /// (альфа может только убывать), поэтому туман тает прямо на ходу без пересчёта всей карты.
    /// Рисунок тумана — бесшовная плитка UI/fog_tile_bytes, край клубится (UI/fog_puffs — 16 клубов 4×4).
    /// </summary>
    public static class FogOfWar
    {
        const int MW = 162, MH = 108;    // маска: 5 точек карты (810×540) на пиксель
        public const float Soft = 46f;   // ширина мягкого края тумана, в точках карты 810×540
        static Texture2D mask, tile;
        static Color32[] tilePx; static int tileW, tileH;
        static Color32[] px;
        static float[] fa;               // альфа тумана 0..1 по пикселям маски
        static string key; static int done;   // область/радиус и сколько точек уже учтено
        static bool dirty;
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

        static void Reset()
        {
            LoadTile();
            if (mask == null)
            {
                mask = new Texture2D(MW, MH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                px = new Color32[MW * MH]; fa = new float[MW * MH];
            }
            for (int y = 0; y < MH; y++)
                for (int x = 0; x < MW; x++)
                {
                    int i = y * MW + x;
                    px[i] = tilePx[((y * 2 * tileH / MH) % tileH) * tileW + (x * 3 * tileW / MW) % tileW];   // плитка: 3×2 по карте
                    fa[i] = 1f;
                }
            done = 0; dirty = true;
        }

        /// <summary>Открыть круг радиуса r (точки карты 810×540) вокруг p (доли карты): альфа только убывает.</summary>
        static void Open(Vector2 p, float r)
        {
            float sx = 810f / MW, sy = 540f / MH, cx = p.x * 810f, cy = p.y * 540f, reach = r + Soft;
            int x0 = Mathf.Max(0, (int)((cx - reach) / sx)), x1 = Mathf.Min(MW - 1, (int)((cx + reach) / sx) + 1);
            int yTop = Mathf.Max(0, (int)((cy - reach) / sy)), yBot = Mathf.Min(MH - 1, (int)((cy + reach) / sy) + 1);
            for (int my = yTop; my <= yBot; my++)
            {
                int y = MH - 1 - my;   // текстура снизу вверх
                float py = (my + 0.5f) * sy - cy;
                for (int x = x0; x <= x1; x++)
                {
                    float qx = (x + 0.5f) * sx - cx;
                    float t = Mathf.Clamp01((Mathf.Sqrt(qx * qx + py * py) - r) / Soft);
                    t = t * t * (3f - 2f * t);
                    int i = y * MW + x;
                    if (t < fa[i]) fa[i] = t;
                }
            }
            dirty = true;
        }

        static void Upload()
        {
            puffs.Clear();
            float sx = 810f / MW, sy = 540f / MH;
            for (int y = 0; y < MH; y++)
                for (int x = 0; x < MW; x++)
                {
                    int i = y * MW + x; float t = fa[i];
                    px[i].a = (byte)(t * 240f);
                    if (t > 0.25f && t < 0.6f && ((x * 7 + y * 13) % 23) == 0 && puffs.Count < 48)
                        puffs.Add(new Vector3((x + 0.5f) * sx / 810f, (MH - 1 - y + 0.5f) * sy / 540f, (x * 0.37f + y * 0.71f) % 6.28f));
                }
            mask.SetPixels32(px); mask.Apply(false);
            dirty = false;
        }

        /// <summary>Туман в точке (доли карты): 0 — открыто, 1 — густой туман. Пока маски нет — 1.</summary>
        public static float At(Vector2 p)
        {
            if (fa == null || key == null) return 1f;
            int x = Mathf.Clamp((int)(p.x * MW), 0, MW - 1), y = MH - 1 - Mathf.Clamp((int)(p.y * MH), 0, MH - 1);
            return fa[y * MW + x];
        }

        /// <summary>
        /// Нарисовать туман поверх карты (после дорог, до мест). seen — точки, где побывал герой, r — радиус обзора;
        /// stateKey меняется при смене области или радиуса (тогда маска считается заново).
        /// </summary>
        public static void Draw(Rect view, float u0, float uw, IList<Vector2> seen, float r, string stateKey)
        {
            if (mask == null || stateKey != key || seen.Count < done) { key = stateKey; Reset(); }
            for (; done < seen.Count; done++) Open(seen[done], r);
            if (Event.current.type != EventType.Repaint) return;
            if (dirty) Upload();
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
