using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Размеры карт областей. С 2026-11-07 карты расширены на восток: к старой картине (1536×1024) дорисовано продолжение
    /// (tools/map_extend: ref-холст → ChatGPT → сшивка по совпадающему краю). Старые координаты (доли старой картины)
    /// переводятся в доли новой умножением x на OldU; высота не менялась. Всё, что считало карту «810 точек шириной»,
    /// берёт ширину отсюда (точки карты при высоте 540).
    /// </summary>
    public static class MapGeom
    {
        public static readonly int[] TexW = { 4096, 2610, 2815 };   // UI/map_wide_0..2
        // высота картины: 1024 — карта ровно в экран; больше — карта листается и вверх-вниз (2026-11-20: Лес теней 4096×1608)
        public static readonly int[] TexHt = { 1608, 1024, 1024 };
        public const float TexH = 1024f, OldTexW = 1536f, H = 540f, OldW = 810f;

        static int R(int region) { return Mathf.Clamp(region, 0, TexW.Length - 1); }
        /// <summary>Ширина карты области в точках (высота — 540).</summary>
        public static float W(int region) { return H * TexWidth(region) / TexH; }
        /// <summary>Высота карты области в точках (540 — ровно экран; масштаб — как у прежних карт: 1024 px = 540 точек).</summary>
        public static float Hgt(int region) { return H * TexHeight(region) / TexH; }
        public static float TexHeight(int region)
        {
            if (MapStore.IsCustom(region)) { MapData m = MapStore.Get(region); if (m != null && m.height > 0) return m.height; }
            return TexHt[R(region)];
        }
        /// <summary>Высота карты текущей области в точках.</summary>
        public static float Hc { get { return Hgt(Campaign.Region); } }
        /// <summary>Ширина картины области в пикселях (своя область из редактора карт — своя ширина).</summary>
        public static float TexWidth(int region)
        {
            if (MapStore.IsCustom(region)) { MapData m = MapStore.Get(region); if (m != null && m.width > 0) return m.width; }
            return TexW[R(region)];
        }
        /// <summary>Ширина карты текущей области в точках.</summary>
        public static float Wc { get { return W(Campaign.Region); } }
        /// <summary>Доля старой картины в новой: x_new = x_old × OldU.</summary>
        public static float OldU(int region) { return OldTexW / TexWidth(region); }
        /// <summary>Перевод координаты старой картины (доли) в доли новой.</summary>
        public static Vector2 FromOld(int region, Vector2 uv) { return new Vector2(uv.x * OldU(region), uv.y); }
        /// <summary>Точки карты на долю (по x — ширина области, по y — 540).</summary>
        public static Vector2 Pts(int region) { return new Vector2(W(region), Hgt(region)); }
        public static string Texture(int region)
        {
            if (MapStore.IsCustom(region)) { MapData m = MapStore.Get(region); if (m != null && !string.IsNullOrEmpty(m.texture)) return m.texture; }
            return "UI/map_wide_" + R(region);
        }
    }
}
