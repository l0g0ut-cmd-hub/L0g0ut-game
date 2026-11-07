using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Живая карта похода: поверх нарисованной карты — бегущая вода (блики плывут по руслу), пена водопадов,
    /// мерцающий свет в окнах и фонарях, пар горячих источников, течение и пульс лавы, блуждающие огни на кладбище,
    /// дыхание луны и пасти Они. Точки сняты с картин карт (доли ширины/высоты), всё рисуется под туманом и значками.
    /// </summary>
    public static class MapLife
    {
        class Region
        {
            public Vector2[][] rivers = new Vector2[0][]; public bool lava;
            public Vector4[] falls = new Vector4[0];    // x, y верх, высота, ширина (доли)
            public Vector3[] lights = new Vector3[0];   // x, y, размер (точки карты)
            public Vector3[] steam = new Vector3[0];    // x, y, размер
            public Vector3[] wisps = new Vector3[0];    // x, y, размах
            public Vector4[] pulses = new Vector4[0];   // x, y, размер, оттенок (0 — тёплый, 1 — красный)
        }
        static Vector2 V(float x, float y) { return new Vector2(x / 768f, y / 512f); }
        static Vector3 L(float x, float y, float s) { return new Vector3(x / 768f, y / 512f, s); }

        static readonly Region[] regions =
        {
            new Region   // лес теней
            {
                rivers = new[] { new[] { V(158, 95), V(185, 150), V(205, 200), V(240, 250), V(285, 300), V(330, 325), V(375, 345), V(400, 380), V(430, 415), V(465, 450), V(500, 485), V(540, 512) } },
                falls = new[] { new Vector4(148f / 768f, 30f / 512f, 70f / 512f, 26f / 768f), new Vector4(278f / 768f, 278f / 512f, 26f / 512f, 18f / 768f), new Vector4(200f / 768f, 185f / 512f, 25f / 512f, 14f / 768f) },
                lights = new[] { L(418, 130, 6), L(560, 211, 6), L(437, 224, 6), L(510, 223, 6), L(367, 282, 6), L(478, 327, 6), L(243, 405, 6), L(252, 404, 6), L(540, 400, 6), L(552, 447, 6), L(566, 450, 6), L(584, 452, 6), L(650, 333, 6), L(69, 329, 5), L(77, 332, 5), L(88, 334, 5), L(111, 329, 5), L(117, 328, 5), L(69, 312, 5), L(30, 349, 5), L(37, 348, 5), L(43, 347, 5), L(54, 344, 5), L(125, 346, 5), L(135, 347, 5), L(142, 348, 5), L(148, 350, 5), L(153, 351, 5), L(161, 350, 5), L(169, 336, 5), L(58, 443, 5), L(67, 442, 5), L(72, 441, 5), L(90, 435, 5), L(93, 435, 5), L(204, 428, 5), L(221, 423, 5), L(226, 422, 5), L(634, 33, 5), L(647, 35, 5), L(633, 52, 5), L(645, 53, 5), L(655, 54, 5), L(685, 54, 5), L(695, 54, 5), L(632, 71, 5), L(647, 72, 5), L(665, 74, 5), L(684, 73, 5), L(702, 74, 5) },   // окна и фонари — точно по нарисованным (сверено на увеличении 9–10×)
            },
            new Region   // горы Они
            {
                lava = true,
                rivers = new[] { new[] { V(470, 70), V(450, 140), V(438, 210), V(432, 262), V(450, 300), V(500, 345), V(560, 395), V(620, 440), V(700, 480), V(768, 500) },
                                 new[] { V(505, 0), V(492, 40), V(475, 80) },
                                 // центральное ущелье с подвесным мостом — отдельная трещина лавы, не задета первой веткой;
                                 // без неё DistToWater не знал про этот провал, и значки построек ставились прямо над пропастью
                                 new[] { V(476, 0), V(445, 51), V(407, 102), V(384, 154), V(376, 205), V(384, 241), V(399, 282), V(422, 333), V(445, 384), V(476, 435) } },
                lights = new[] { L(104, 192, 6), L(232, 204, 6), L(124, 180, 6), L(147, 180, 5), L(166, 296, 5), L(173, 297, 5), L(184, 297, 5), L(189, 295, 5), L(212, 322, 5), L(222, 323, 5), L(233, 323, 5), L(224, 355, 5), L(235, 354, 5), L(247, 355, 5), L(56, 376, 5), L(67, 376, 5), L(78, 377, 5), L(88, 380, 5), L(93, 373, 5), L(101, 372, 5), L(109, 376, 5), L(120, 367, 5), L(127, 367, 5), L(44, 398, 5), L(62, 401, 5), L(109, 422, 5), L(117, 423, 5), L(127, 424, 5), L(165, 427, 5), L(172, 426, 5), L(179, 425, 5), L(204, 391, 5), L(190, 387, 5) },   // окна и фонари — точно по нарисованным (сверено на увеличении 9–10×)
                steam = new[] { L(110, 300, 60), L(320, 368, 60), L(690, 205, 55), L(722, 230, 50), L(300, 330, 40) },
                pulses = new[] { new Vector4(636f / 768f, 112f / 512f, 120f, 1f), new Vector4(255f / 768f, 40f / 512f, 90f, 0f) },
            },
            new Region   // проклятый сёгунат
            {
                rivers = new[] { new[] { V(210, 160), V(222, 220), V(250, 268), V(300, 300), V(345, 335), V(385, 365), V(430, 410), V(480, 445), V(530, 472), V(600, 498), V(660, 512) } },
                falls = new[] { new Vector4(200f / 768f, 95f / 512f, 70f / 512f, 26f / 768f) },
                lights = new[] { L(330, 322, 6), L(99, 313, 5), L(76, 324, 5), L(92, 330, 5), L(100, 331, 5), L(107, 331, 5), L(120, 330, 5), L(127, 329, 5), L(34, 339, 5), L(26, 358, 5), L(45, 365, 5), L(61, 371, 5), L(70, 358, 5), L(82, 358, 5), L(87, 357, 5), L(92, 370, 5), L(80, 372, 5), L(111, 359, 5), L(167, 308, 5), L(149, 312, 5), L(136, 314, 5), L(132, 328, 5), L(164, 346, 5), L(147, 345, 5), L(222, 343, 5), L(215, 342, 5), L(235, 342, 5), L(228, 367, 5), L(216, 376, 5), L(206, 377, 5), L(190, 377, 5), L(146, 393, 5), L(124, 417, 5), L(134, 416, 5), L(152, 412, 5), L(158, 414, 5), L(143, 412, 5), L(102, 406, 5), L(114, 409, 5), L(209, 417, 5), L(215, 415, 5), L(222, 415, 5), L(231, 414, 5), L(279, 448, 5), L(287, 449, 5), L(295, 450, 5), L(313, 448, 5), L(321, 446, 5), L(289, 428, 4), L(312, 428, 4), L(332, 438, 4), L(145, 161, 5), L(155, 163, 5), L(165, 162, 5), L(413, 158, 4), L(417, 158, 4), L(422, 158, 4), L(428, 158, 4), L(435, 158, 4), L(410, 177, 4), L(415, 177, 4), L(420, 177, 4), L(428, 177, 4), L(435, 176, 4), L(408, 195, 4), L(412, 195, 4), L(420, 196, 4), L(427, 195, 4), L(437, 195, 4), L(402, 213, 4), L(406, 214, 4), L(413, 214, 4), L(420, 215, 4), L(427, 214, 4), L(436, 214, 4), L(636, 31, 4), L(641, 31, 4), L(656, 31, 4), L(661, 32, 4), L(630, 48, 4), L(644, 46, 4), L(654, 46, 4), L(639, 64, 4), L(626, 66, 4), L(621, 66, 4), L(596, 71, 4), L(591, 71, 4), L(669, 69, 4), L(599, 86, 4), L(607, 85, 4), L(616, 84, 4), L(633, 84, 4), L(657, 86, 4), L(670, 84, 4), L(677, 85, 4), L(693, 86, 4) },   // окна сёгуната — по нарисованным горящим окнам (сверено на увеличении 7–12×), без бликов на полу
                wisps = new[] { L(530, 185, 10), L(585, 214, 10), L(635, 236, 10), L(505, 303, 10), L(735, 296, 10) },
                pulses = new[] { new Vector4(585f / 768f, 26f / 512f, 110f, 1f) },
            },
        };

        /// <summary>Расстояние (в точках карты 810×540) от точки до ближайшей воды или лавы этой области — чтобы ставить здания не на мост и не в реку.</summary>
        public static float DistToWater(int region, Vector2 uv)
        {
            Region r = regions[Mathf.Clamp(region, 0, regions.Length - 1)];
            float best = 1e9f; Vector2 k = new Vector2(810f, 540f), q = Vector2.Scale(uv, k);
            foreach (Vector2[] pl in r.rivers)
                for (int i = 1; i < pl.Length; i++)
                {
                    Vector2 a = Vector2.Scale(pl[i - 1], k), b = Vector2.Scale(pl[i], k), ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (a + ab * t - q).magnitude);
                }
            return best;
        }

        static float H(float a) { float x = Mathf.Sin(a * 12.9898f) * 43758.5453f; return x - Mathf.Floor(x); }

        static Vector2 Along(Vector2[] pl, float u)
        {
            float total = 0f;
            for (int i = 1; i < pl.Length; i++) total += Vector2.Scale(pl[i] - pl[i - 1], new Vector2(810f, 540f)).magnitude;
            float d = Mathf.Repeat(u, 1f) * total;
            for (int i = 1; i < pl.Length; i++)
            {
                float seg = Vector2.Scale(pl[i] - pl[i - 1], new Vector2(810f, 540f)).magnitude;
                if (d <= seg) return Vector2.Lerp(pl[i - 1], pl[i], seg > 0f ? d / seg : 0f);
                d -= seg;
            }
            return pl[pl.Length - 1];
        }

        /// <summary>Рисовать сразу после картины карты (до дорог, тумана и значков).</summary>
        /// <summary>
        /// Окна и фонари нарисованной карты: тёплый свет, каждый в своём ритме, с лёгким колебанием.
        /// Рисуется поверх фильтра времени суток; light — сила (днём 0, ночью ~1,6).
        /// </summary>
        public static void DrawLights(int region, Rect view, float u0, float uw, float light)
        {
            if (Event.current.type != EventType.Repaint || DuelManager.Gfx == 0 || light <= 0.01f) return;
            Region r = regions[Mathf.Clamp(region, 0, regions.Length - 1)];
            Texture2D glow = UiKit.Tex("FX/glow_pool");
            if (glow == null) return;
            float t = Time.unscaledTime, k = view.width / uw / 810f;
            Color oc = GUI.color;
            foreach (Vector3 l in r.lights)
            {
                Vector2 p = new Vector2(view.x + (l.x - u0) / uw * view.width, l.y * view.height);
                if (p.x < view.x - 60f || p.x > view.xMax + 60f) continue;
                float fl = 0.86f + 0.1f * Mathf.PerlinNoise(t * 1.1f + l.x * 50f, l.y * 30f) + 0.04f * Mathf.Sin(t * 5.3f + l.x * 90f);
                float g = l.z * k * 1.6f;
                GUI.color = new Color(1f, 0.6f, 0.26f, Mathf.Clamp01(0.3f * light * fl));
                GUI.DrawTexture(new Rect(p.x - g, p.y - g, g * 2f, g * 2f), glow);
                float c = l.z * k * 0.5f;
                GUI.color = new Color(1f, 0.85f, 0.55f, Mathf.Clamp01(0.45f * light * fl));
                GUI.DrawTexture(new Rect(p.x - c, p.y - c, c * 2f, c * 2f), glow);
            }
            GUI.color = oc;
        }

        public static void Draw(int region, Rect view, float u0, float uw)
        {
            if (Event.current.type != EventType.Repaint || DuelManager.Gfx == 0) return;
            Region r = regions[Mathf.Clamp(region, 0, regions.Length - 1)];
            float t = Time.unscaledTime;
            Texture2D glow = UiKit.Tex("FX/glow_pool"), mist = SoftSprites.Fog;
            Color oc = GUI.color;
            float k = view.width / uw / 810f;   // точек экрана на точку карты 810
            System.Func<Vector2, Vector2> S = p => new Vector2(view.x + (p.x - u0) / uw * view.width, p.y * view.height);
            System.Func<Vector2, bool> Vis = p => p.x > view.x - 60f && p.x < view.xMax + 60f;

            // вода / лава: блики плывут по руслу, у лавы — ещё и медленные пятна жара
            for (int ri = 0; ri < r.rivers.Length; ri++)
            {
                Vector2[] pl = r.rivers[ri];
                int n = r.lava ? 26 : 40;
                for (int i = 0; i < n; i++)
                {
                    float seed = ri * 37.1f + i * 1.618f;
                    float u = H(seed) + t * (r.lava ? 0.008f : 0.02f) * (0.7f + 0.6f * H(seed + 3f));
                    Vector2 p = Along(pl, u);
                    Vector2 q = Along(pl, u + 0.004f);
                    Vector2 dir = Vector2.Scale(q - p, new Vector2(810f, 540f)).normalized;
                    Vector2 side = new Vector2(-dir.y, dir.x);
                    Vector2 sp = S(p) + side * (H(seed + 7f) - 0.5f) * 14f * k;
                    if (!Vis(sp)) continue;
                    float tw = 0.5f + 0.5f * Mathf.Sin(t * (2f + 3f * H(seed + 5f)) + seed * 9f);
                    if (r.lava)
                    {
                        if (glow == null) continue;
                        GUI.color = new Color(1f, 0.45f + 0.25f * tw, 0.1f, 0.22f + 0.2f * tw);
                        float g = (10f + 10f * H(seed + 2f)) * k;
                        GUI.DrawTexture(new Rect(sp.x - g, sp.y - g * 0.6f, g * 2f, g * 1.2f), glow);
                    }
                    else
                    {
                        float len = (4f + 6f * H(seed + 4f)) * k;
                        UiKit.Line(sp - dir * len, sp + dir * len, 1.3f, new Color(0.85f, 0.95f, 1f, 0.18f + 0.4f * tw * tw));
                    }
                }
            }
            // водопады: струи срываются вниз, у подножия — пена
            foreach (Vector4 f in r.falls)
            {
                Vector2 top = S(new Vector2(f.x, f.y));
                if (!Vis(top)) continue;
                float hgt = f.z * view.height, wid = f.w * view.width / uw;
                for (int i = 0; i < 9; i++)
                {
                    float seed = f.x * 100f + i;
                    float ph = Mathf.Repeat(t * (1.1f + 0.5f * H(seed)) + H(seed + 1f), 1f);
                    float x = top.x + (H(seed + 2f) - 0.5f) * wid;
                    float y = top.y + ph * hgt;
                    UiKit.Line(new Vector2(x, y), new Vector2(x, y + hgt * 0.18f), 1.4f, new Color(0.9f, 0.96f, 1f, 0.45f * Mathf.Sin(ph * Mathf.PI)));
                }
                if (mist != null)
                    for (int i = 0; i < 3; i++)
                    {
                        float ph = Mathf.Repeat(t * 0.3f + i / 3f, 1f), sz = wid * (1.2f + 1.4f * ph);
                        GUI.color = new Color(1f, 1f, 1f, 0.22f * Mathf.Sin(ph * Mathf.PI));
                        GUI.DrawTexture(new Rect(top.x - sz * 0.5f, top.y + hgt - sz * 0.25f - ph * 6f * k, sz, sz * 0.5f), mist);
                    }
            }
            // окна и фонари — отдельно, в DrawLights (поверх фильтра времени суток)
            // пар горячих источников и серных трещин
            if (mist != null)
                foreach (Vector3 st in r.steam)
                {
                    Vector2 p = S(st);
                    if (!Vis(p)) continue;
                    for (int i = 0; i < 4; i++)
                    {
                        float ph = Mathf.Repeat(t * 0.12f + i * 0.25f + st.x * 3f, 1f);
                        float sz = st.z * k * (0.6f + 0.9f * ph);
                        Vector2 q = p + new Vector2(10f * k * Mathf.Sin(t * 0.4f + i * 2f), -ph * 60f * k);
                        GUI.color = new Color(0.95f, 0.95f, 0.92f, 0.2f * Mathf.Sin(ph * Mathf.PI));
                        GUI.DrawTexture(new Rect(q.x - sz * 0.5f, q.y - sz * 0.35f, sz, sz * 0.7f), mist);
                    }
                }
            // блуждающие огни (кладбище сёгуната): парят, кружат и меркнут
            if (glow != null)
                foreach (Vector3 w in r.wisps)
                {
                    Vector2 p = S(w) + new Vector2(Mathf.Sin(t * 0.7f + w.x * 40f) * 8f, Mathf.Sin(t * 1.1f + w.y * 30f) * 6f - 4f) * k;
                    if (!Vis(p)) continue;
                    float a = 0.45f + 0.35f * Mathf.Sin(t * 1.7f + w.x * 90f);
                    float g = w.z * k * (1f + 0.3f * a);
                    GUI.color = new Color(0.35f, 1f, 0.95f, 0.55f * a);
                    GUI.DrawTexture(new Rect(p.x - g, p.y - g, g * 2f, g * 2f), glow);
                    GUI.color = new Color(0.85f, 1f, 1f, 0.8f * a);
                    GUI.DrawTexture(new Rect(p.x - g * 0.3f, p.y - g * 0.3f, g * 0.6f, g * 0.6f), glow);
                }
            // большие медленные пульсы: пасть Они, кровавая луна, закат
            if (glow != null)
                foreach (Vector4 pu in r.pulses)
                {
                    Vector2 p = S(new Vector2(pu.x, pu.y));
                    if (!Vis(p)) continue;
                    float a = 0.5f + 0.5f * Mathf.Sin(t * 0.9f + pu.x * 10f);
                    float g = pu.z * k * (0.9f + 0.15f * a);
                    GUI.color = pu.w > 0.5f ? new Color(1f, 0.2f, 0.1f, 0.18f + 0.18f * a) : new Color(1f, 0.6f, 0.25f, 0.14f + 0.12f * a);
                    GUI.DrawTexture(new Rect(p.x - g, p.y - g, g * 2f, g * 2f), glow);
                }
            GUI.color = oc;
        }
    }
}
