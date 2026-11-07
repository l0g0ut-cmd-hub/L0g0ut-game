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
            public Vector4[] smoke = new Vector4[0];    // дым из нарисованных печей и труб: x, y (устье), высота столба и снос вбок (точки карты; − влево) — по нарисованному дыму
            public Vector2[][] still = new Vector2[0][]; // стоячая вода и пропасти: без бликов, только чтобы не ставить домики в воду и над обрывом
            public Vector3[] wisps = new Vector3[0];    // x, y, размах
            public Vector4[] pulses = new Vector4[0];   // x, y, размер, оттенок (0 — тёплый, 1 — красный)
            public Vector3[] fog = new Vector3[0];      // полосы тумана поверх нарисованного: x, y, ширина (точки карты) — клубятся и медленно плывут
            public Vector2[][] flows = new Vector2[0][]; // туман, стекающий по руслу (ущелье под водопадом): клубы ползут по линии
            public Vector2[] lake = new Vector2[0];     // стоячая вода: места, где по очереди вспыхивает и гаснет мягкий блик (не рябь полосами)
            public Vector4[] petals = new Vector4[0];   // облетающая сакура: x, y, ширина, высота области (доли карты)
        }
        static Vector2 V(float x, float y) { return new Vector2(x / 768f, y / 512f); }
        static Vector3 L(float x, float y, float s) { return new Vector3(x / 768f, y / 512f, s); }

        static readonly Region[] regions =
        {
            new Region   // лес теней
            {
                // русло обведено по картине (каждые ~50 px, сверено наложением): прежняя ломаная срезала изгибы, блики ложились на камни и траву
                rivers = new[] { new[] { V(155, 100), V(160, 115), V(167.5f, 142.5f), V(185, 165), V(200, 190), V(207.5f, 200), V(220, 220), V(240, 245), V(260, 260), V(277.5f, 287.5f), V(295, 312.5f), V(320, 330), V(345, 350), V(370, 365), V(400, 390), V(425, 415), V(440, 440), V(457.5f, 465), V(495, 487.5f), V(515, 512) } },
                falls = new[] { new Vector4(148f / 768f, 30f / 512f, 70f / 512f, 26f / 768f), new Vector4(278f / 768f, 278f / 512f, 26f / 512f, 18f / 768f), new Vector4(200f / 768f, 185f / 512f, 25f / 512f, 14f / 768f) },
                lights = new[] { L(421, 129, 6), L(560, 211, 6), L(437, 224, 6), L(510, 223, 6), L(370, 281, 6), L(478, 329, 6), L(243, 406, 6), L(253, 406, 6), L(536, 402, 5), L(544, 402, 5), L(555, 453, 5), L(561, 453, 5), L(581, 461, 5), L(586, 461, 5), L(655, 336, 5), L(659, 335, 4), L(69, 329, 5), L(77, 332, 5), L(88, 334, 5), L(111, 329, 5), L(117, 328, 5), L(69, 312, 5), L(30, 349, 5), L(37, 348, 5), L(43, 347, 5), L(54, 344, 5), L(125, 346, 5), L(135, 347, 5), L(142, 348, 5), L(148, 350, 5), L(153, 351, 5), L(161, 350, 5), L(169, 336, 5), L(58, 443, 5), L(67, 442, 5), L(72, 441, 5), L(90, 435, 5), L(93, 435, 5), L(634, 33, 5), L(647, 35, 5), L(633, 52, 5), L(645, 53, 5), L(655, 54, 5), L(685, 54, 5), L(695, 54, 5), L(632, 71, 5), L(647, 72, 5), L(665, 74, 5), L(684, 73, 5), L(702, 74, 5) },   // окна и фонари — точно по нарисованным (сверено на увеличении 9–10×)
            },
            new Region   // горы Они
            {
                lava = true,
                rivers = new[] { new[] { V(470, 70), V(450, 140), V(438, 210), V(432, 262), V(450, 300), V(500, 345), V(560, 395), V(620, 440), V(700, 480), V(768, 500) },
                                 new[] { V(505, 0), V(492, 40), V(475, 80) },
                                 // центральное ущелье с подвесным мостом — отдельная трещина лавы, не задета первой веткой;
                                 // без неё DistToWater не знал про этот провал, и значки построек ставились прямо над пропастью
                                 new[] { V(476, 0), V(445, 51), V(407, 102), V(384, 154), V(376, 205), V(384, 241), V(399, 282), V(422, 333), V(445, 384), V(476, 435) } },
                lights = new[] { L(106, 193, 6), L(232, 204, 6), L(124, 180, 6), L(147, 180, 5), L(166, 296, 5), L(173, 297, 5), L(184, 297, 5), L(189, 295, 5), L(212, 322, 5), L(222, 323, 5), L(233, 323, 5), L(224, 355, 5), L(235, 354, 5), L(247, 355, 5), L(56, 376, 5), L(67, 376, 5), L(78, 377, 5), L(88, 380, 5), L(93, 373, 5), L(101, 372, 5), L(109, 376, 5), L(120, 367, 5), L(127, 367, 5), L(44, 398, 5), L(62, 401, 5), L(109, 422, 5), L(117, 423, 5), L(127, 424, 5), L(165, 427, 5), L(172, 426, 5), L(179, 425, 5), L(204, 391, 5), L(190, 387, 5) },   // окна и фонари — точно по нарисованным (сверено на увеличении 9–10×)
                steam = new[] { L(110, 300, 60), L(320, 368, 60), L(690, 205, 55), L(722, 230, 50), L(300, 330, 40) },
                pulses = new[] { new Vector4(636f / 768f, 112f / 512f, 120f, 1f), new Vector4(255f / 768f, 40f / 512f, 90f, 0f) },
            },
            new Region   // проклятый сёгунат
            {
                // русло от водопада под мостом к низу карты — по картине; второй рукав приходит слева и сливается под водопадом
                rivers = new[] { new[] { V(215, 160), V(210, 195), V(205, 225), V(210, 240), V(235, 260), V(265, 280), V(295, 300), V(325, 315), V(355, 335), V(370, 362.5f), V(400, 387.5f), V(430, 410), V(465, 427.5f), V(500, 445), V(540, 460), V(590, 480), V(630, 497.5f), V(660, 512) },
                                 new[] { V(150, 235), V(180, 242.5f), V(210, 240) } },
                falls = new[] { new Vector4(200f / 768f, 95f / 512f, 70f / 512f, 26f / 768f) },
                lights = new[] { L(99, 313, 5), L(76, 324, 5), L(92, 330, 5), L(100, 331, 5), L(107, 331, 5), L(120, 330, 5), L(127, 329, 5), L(34, 339, 5), L(26, 358, 5), L(45, 365, 5), L(61, 371, 5), L(70, 358, 5), L(82, 358, 5), L(87, 357, 5), L(92, 370, 5), L(80, 372, 5), L(111, 359, 5), L(167, 308, 5), L(149, 312, 5), L(136, 314, 5), L(132, 328, 5), L(164, 346, 5), L(147, 345, 5), L(222, 343, 5), L(215, 342, 5), L(235, 342, 5), L(228, 367, 5), L(216, 376, 5), L(206, 377, 5), L(190, 377, 5), L(146, 393, 5), L(124, 417, 5), L(134, 416, 5), L(152, 412, 5), L(158, 414, 5), L(143, 412, 5), L(102, 406, 5), L(114, 409, 5), L(209, 417, 5), L(215, 415, 5), L(222, 415, 5), L(231, 414, 5), L(279, 448, 5), L(287, 449, 5), L(295, 450, 5), L(313, 448, 5), L(321, 446, 5), L(289, 428, 4), L(312, 428, 4), L(332, 438, 4), L(145, 161, 5), L(155, 163, 5), L(165, 162, 5), L(413, 158, 4), L(417, 158, 4), L(422, 158, 4), L(428, 158, 4), L(435, 158, 4), L(410, 177, 4), L(415, 177, 4), L(420, 177, 4), L(428, 177, 4), L(435, 176, 4), L(408, 195, 4), L(412, 195, 4), L(420, 196, 4), L(427, 195, 4), L(437, 195, 4), L(402, 213, 4), L(406, 214, 4), L(413, 214, 4), L(420, 215, 4), L(427, 214, 4), L(436, 214, 4), L(636, 31, 4), L(641, 31, 4), L(656, 31, 4), L(661, 32, 4), L(630, 48, 4), L(644, 46, 4), L(654, 46, 4), L(639, 64, 4), L(626, 66, 4), L(621, 66, 4), L(596, 71, 4), L(591, 71, 4), L(669, 69, 4), L(599, 86, 4), L(607, 85, 4), L(616, 84, 4), L(633, 84, 4), L(657, 86, 4), L(670, 84, 4), L(677, 85, 4), L(693, 86, 4) },   // окна сёгуната — по нарисованным горящим окнам (сверено на увеличении 7–12×), без бликов на полу
                wisps = new[] { L(530, 185, 10), L(585, 214, 10), L(635, 236, 10), L(505, 303, 10), L(735, 296, 10) },
                pulses = new[] { new Vector4(585f / 768f, 26f / 512f, 110f, 1f) },
            },
        };

        // Карты расширены на восток (MapGeom): точки выше сняты со старых картин — x переводится в доли новой карты;
        // для новых частей точки — в пикселях широкой картины (TexW×1024).
        static MapLife()
        {
            for (int ri = 0; ri < regions.Length; ri++)
            {
                Region r = regions[ri]; float u = MapGeom.OldU(ri);
                foreach (Vector2[] pl in r.rivers) for (int i = 0; i < pl.Length; i++) pl[i].x *= u;
                for (int i = 0; i < r.falls.Length; i++) { r.falls[i].x *= u; r.falls[i].w *= u; }
                for (int i = 0; i < r.lights.Length; i++) r.lights[i].x *= u;
                for (int i = 0; i < r.steam.Length; i++) r.steam[i].x *= u;
                for (int i = 0; i < r.smoke.Length; i++) r.smoke[i].x *= u;
                foreach (Vector2[] pl in r.still) for (int i = 0; i < pl.Length; i++) pl[i].x *= u;
                for (int i = 0; i < r.wisps.Length; i++) r.wisps[i].x *= u;
                for (int i = 0; i < r.pulses.Length; i++) r.pulses[i].x *= u;
                if (ri < NewParts.Length) NewParts[ri](r, MapGeom.TexW[ri]);
            }
        }
        static Vector2 P(float x, float y, int w) { return new Vector2(x / w, y / 1024f); }
        static Vector3 Q(float x, float y, float s, int w) { return new Vector3(x / w, y / 1024f, s); }
        static T[] Join<T>(T[] a, T[] b) { T[] c = new T[a.Length + b.Length]; a.CopyTo(c, 0); b.CopyTo(c, a.Length); return c; }

        // новые (восточные) части карт: вода/лава, окна и фонари (на картине не горят — свет даёт игра), дым и пар, огни
        static readonly System.Action<Region, int>[] NewParts =
        {
            (r, w) =>   // лес теней — глушь: хижины углежогов, деревня синоби на утёсах, лодочный сарай, каменные фонари у кедра
            {
                // чёрное озеро и ущелье — без бликов (на озере рябь смотрелась полосами); только чтобы домики не вставали в воду и над пропастью
                r.still = Join(r.still, new[] {
                    new[] { P(2120, 200, w), P(2200, 230, w), P(2280, 270, w), P(2330, 300, w) },
                    new[] { P(2292, 320, w), P(2284, 440, w), P(2300, 560, w), P(2312, 650, w), P(2330, 760, w), P(2322, 880, w), P(2332, 1020, w) },
                    new[] { P(2230, 640, w), P(2300, 700, w), P(2380, 700, w) } });
                // печи углежогов: нарисованный дым — у левой поднимается и уходит влево (до ~1595,600 на картине — клубы тянутся на всю длину), у правой — вверх и чуть вправо
                r.smoke = Join(r.smoke, new[] { new Vector4(1708f / w, 736f / 1024f, 84f, -78f), new Vector4(1781f / w, 724f / 1024f, 46f, 14f) });
                r.lights = Join(r.lights, new[] {
                    Q(1876, 733, 5, w), Q(1897, 736, 5, w),                         // хижина углежогов: окно и дверь
                    Q(1957, 786, 5, w), Q(1988, 791, 5, w), Q(2010, 790, 5, w),     // вторая хижина
                    Q(2438, 397, 5, w), Q(2462, 400, 5, w), Q(2538, 397, 5, w),     // деревня синоби: дома на помостах
                    Q(2538, 323, 5, w), Q(2548, 492, 5, w), Q(2580, 281, 5, w), Q(2490, 123, 6, w),   // …верхний дом, нижний, крайний; дозорная вышка
                    Q(2348, 268, 5, w), Q(2368, 268, 5, w), Q(2386, 268, 5, w),     // лодочный сарай
                    Q(1625, 496, 5, w), Q(1655, 523, 5, w), Q(1940, 465, 5, w), Q(1962, 547, 5, w),   // каменные фонари у кедра и заставы
                });
                r.steam = Join(r.steam, new[] { Q(2262, 700, 60, w), Q(2300, 810, 55, w) });   // туман в ущелье
                // озеро сбрасывается в ущелье водопадом (на картине — белые струи у края озера), туман стекает по ущелью вниз
                r.falls = Join(r.falls, new[] { new Vector4(2295f / w, 360f / 1024f, 120f / 1024f, 36f / w) });
                r.flows = Join(r.flows, new[] { new[] { P(2290, 480, w), P(2300, 570, w), P(2320, 670, w), P(2330, 750, w), P(2350, 850, w), P(2370, 950, w), P(2380, 1024, w) } });
                r.lake = Join(r.lake, new[] { P(2080, 240, w), P(2110, 300, w), P(2200, 290, w), P(2260, 320, w), P(2280, 265, w), P(2370, 315, w) });
                // полосы тумана — на нарисованном тумане: у водопада, у замка, у руин, у бамбука, у кедра, за холмом святилища
                r.fog = Join(r.fog, new[] { Q(480, 160, 70, w), Q(700, 190, 60, w), Q(545, 430, 55, w), Q(280, 515, 60, w), Q(1090, 370, 65, w), Q(1380, 600, 60, w) });
            },
            (r, w) =>   // горы Они — пылающее сердце: лавопад у кратера, лавовое озеро, огонь в штольнях и за воротами крепости
            {
                r.rivers = Join(r.rivers, new[] {
                    new[] { P(2448, 292, w), P(2456, 344, w), P(2448, 400, w), P(2432, 460, w), P(2422, 520, w), P(2440, 580, w), P(2468, 640, w), P(2500, 700, w) },
                    new[] { P(1640, 762, w), P(1720, 792, w), P(1800, 818, w), P(1880, 832, w), P(1950, 852, w) },
                    new[] { P(1650, 880, w), P(1740, 902, w), P(1820, 922, w), P(1900, 942, w) } });
                r.lights = Join(r.lights, new[] { Q(1646, 440, 7, w), Q(1752, 465, 6, w), Q(2350, 232, 8, w) });   // штольни рудника; огонь за воротами
                r.steam = Join(r.steam, new[] { Q(1890, 800, 50, w), Q(1700, 650, 45, w), Q(2200, 900, 45, w) });
                r.pulses = Join(r.pulses, new[] { new Vector4(2372f / w, 108f / 1024f, 140f, 1f) });   // кратер дышит
            },
            (r, w) =>   // сёгунат — владения сёгуна: цитадель на острове, склады гавани, призрачная сакура
            {
                r.still = Join(r.still, new[] {   // озеро у цитадели и гавань — стоячая вода, без бликов
                    new[] { P(2250, 444, w), P(2400, 470, w), P(2550, 482, w), P(2700, 470, w), P(2812, 460, w) },
                    new[] { P(1950, 984, w), P(2100, 992, w), P(2300, 986, w), P(2500, 972, w) } });
                r.lights = Join(r.lights, new[] {
                    Q(2554, 149, 4, w), Q(2567, 149, 4, w), Q(2593, 147, 4, w), Q(2611, 146, 4, w),   // главная башня цитадели, по ярусам
                    Q(2605, 170, 4, w), Q(2524, 202, 4, w), Q(2534, 207, 4, w), Q(2594, 200, 4, w), Q(2614, 199, 4, w),
                    Q(2634, 223, 4, w), Q(2533, 257, 4, w), Q(2599, 257, 4, w),
                    Q(2337, 247, 4, w), Q(2347, 247, 4, w), Q(2357, 247, 4, w),     // угловая башня (запад)
                    Q(2755, 254, 4, w), Q(2775, 254, 4, w),                         // угловая башня (восток)
                    Q(1688, 870, 5, w), Q(1831, 846, 5, w), Q(1717, 915, 5, w), Q(1854, 903, 5, w),   // склады гавани: окна и ворота
                });
                r.lights = Join(r.lights, new[] { Q(2412, 722, 5, w) });   // каменный фонарь у призрачной рощи
                r.wisps = Join(r.wisps, new[] { Q(2350, 620, 10, w), Q(2500, 640, 10, w), Q(2610, 600, 10, w) });   // огоньки в призрачной роще
                r.petals = Join(r.petals, new[] { new Vector4(2130f / w, 520f / 1024f, 590f / w, 240f / 1024f) });   // призрачная сакура облетает
                r.lake = Join(r.lake, new[] { P(2330, 390, w), P(2480, 460, w), P(2560, 510, w), P(2620, 420, w), P(2700, 480, w), P(2760, 550, w), P(2640, 540, w), P(2440, 1000, w), P(2520, 1010, w) });
                r.fog = Join(r.fog, new[] { Q(555, 345, 70, w), Q(1000, 310, 70, w), Q(1275, 840, 65, w), Q(825, 665, 55, w), Q(2200, 500, 70, w), Q(1600, 380, 60, w), Q(660, 280, 60, w) });
            },
        };

        /// <summary>Расстояние (в точках карты) от точки до ближайшей воды или лавы этой области — чтобы ставить здания не на мост и не в реку.</summary>
        public static float DistToWater(int region, Vector2 uv)
        {
            Region r = regions[Mathf.Clamp(region, 0, regions.Length - 1)];
            float best = 1e9f; Vector2 k = MapGeom.Pts(region), q = Vector2.Scale(uv, k);
            foreach (Vector2[] pl in Join(r.rivers, r.still))
                for (int i = 1; i < pl.Length; i++)
                {
                    Vector2 a = Vector2.Scale(pl[i - 1], k), b = Vector2.Scale(pl[i], k), ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (a + ab * t - q).magnitude);
                }
            return best;
        }

        /// <summary>Расстояние (точки карты) до ближайшей пропасти или стоячей воды (still) — места на краю ущелья не держат домиков.</summary>
        public static float DistToStill(int region, Vector2 uv)
        {
            Region r = regions[Mathf.Clamp(region, 0, regions.Length - 1)];
            float best = 1e9f; Vector2 k = MapGeom.Pts(region), q = Vector2.Scale(uv, k);
            foreach (Vector2[] pl in r.still)
                for (int i = 1; i < pl.Length; i++)
                {
                    Vector2 a = Vector2.Scale(pl[i - 1], k), b = Vector2.Scale(pl[i], k), ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (a + ab * t - q).magnitude);
                }
            return best;
        }

        static float H(float a) { float x = Mathf.Sin(a * 12.9898f) * 43758.5453f; return x - Mathf.Floor(x); }

        /// <summary>Кадр листа (cols×rows, frames кадров туда-обратно) с наплывом в следующий — плавно и при малой частоте.</summary>
        static void FlipCell(Rect r, Texture2D tex, int cols, int rows, int frames, float fps, float phase, Color tint)
        {
            float f = (Time.unscaledTime + phase) * fps;
            int span = frames * 2 - 2;
            int a = (int)Mathf.Repeat(Mathf.Floor(f), span), b = (a + 1) % span;
            float kf = f - Mathf.Floor(f);
            if (a >= frames) a = span - a; if (b >= frames) b = span - b;
            // ровный наплыв (без скачка плотности на смене кадра — иначе туман мерцает), кадры сглажены по времени
            kf = kf * kf * (3f - 2f * kf);
            float ab = tint.a * kf;
            GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * (1f - kf) / Mathf.Max(0.001f, 1f - ab));
            GUI.DrawTextureWithTexCoords(r, tex, new Rect((a % cols) / (float)cols, 1f - (a / cols + 1) / (float)rows, 1f / cols, 1f / rows));
            GUI.color = new Color(tint.r, tint.g, tint.b, ab);
            GUI.DrawTextureWithTexCoords(r, tex, new Rect((b % cols) / (float)cols, 1f - (b / cols + 1) / (float)rows, 1f / cols, 1f / rows));
        }

        static Vector2 Along(Vector2[] pl, float u)
        {
            float total = 0f;
            for (int i = 1; i < pl.Length; i++) total += Vector2.Scale(pl[i] - pl[i - 1], new Vector2(1440f, 540f)).magnitude;
            float d = Mathf.Repeat(u, 1f) * total;
            for (int i = 1; i < pl.Length; i++)
            {
                float seg = Vector2.Scale(pl[i] - pl[i - 1], new Vector2(1440f, 540f)).magnitude;
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
            float t = Time.unscaledTime, k = view.width / uw / MapGeom.W(region);
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
            float k = view.width / uw / MapGeom.W(region);   // точек экрана на точку карты
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
                    Vector2 dir = Vector2.Scale(q - p, MapGeom.Pts(region)).normalized;
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
                        // мягкий блик по течению (мягкое пятно, вытянутое вдоль русла), а не жёсткая черта — тонкие линии читались «штрихами»
                        float len = (5f + 7f * H(seed + 4f)) * k;
                        UiKit.Streak(sp - dir * len, sp + dir * len, 2.6f * k, new Color(0.85f, 0.95f, 1f, 0.16f + 0.42f * tw * tw), glow);
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
            // туман, стекающий по ущелью: клубы ползут вниз по линии, растут и тают
            if (mist != null)
                foreach (Vector2[] fl in r.flows)
                    for (int i = 0; i < 9; i++)
                    {
                        float u = Mathf.Repeat(t * 0.012f + i / 9f, 1f);
                        Vector2 p = S(Along(fl, u));
                        if (!Vis(p)) continue;
                        float sz = (46f + 30f * H(i * 3.1f)) * k * (0.8f + 0.4f * u);
                        float a = Mathf.Sin(u * Mathf.PI) * 0.2f;
                        GUI.color = new Color(0.9f, 0.9f, 0.95f, a);
                        GUI.DrawTexture(new Rect(p.x - sz * 0.5f + Mathf.Sin(t * 0.3f + i) * 6f * k, p.y - sz * 0.3f, sz, sz * 0.6f), mist);
                    }
            // полосы тумана над нарисованным туманом: лист fx_mist (30 кадров туда-обратно) клубится, полоса дышит и плывёт
            Texture2D fogSheet = r.fog.Length > 0 ? UiKit.Tex("UI/fx_mist") : null;
            if (fogSheet != null)
                for (int i = 0; i < r.fog.Length; i++)
                {
                    Vector3 f = r.fog[i];
                    Vector2 p = S(f) + new Vector2(Mathf.Sin(t * 0.05f + i * 1.7f) * 14f * k, 0f);
                    if (!Vis(p)) continue;
                    float fw = f.z * k * 2f, fh = fw * 0.5f;
                    float a = 0.13f + 0.06f * Mathf.Sin(t * 0.21f + i * 2.3f);
                    FlipCell(new Rect(p.x - fw * 0.5f, p.y - fh * 0.5f, fw, fh), fogSheet, 6, 5, 30, 0.8f, i * 3.7f, new Color(0.92f, 0.92f, 0.96f, a));
                }
            // стоячая вода: в разных местах по очереди проступает мягкий блик и гаснет (3–5 с), едва смещаясь
            if (glow != null)
                for (int i = 0; i < r.lake.Length; i++)
                {
                    float per = 3f + 2f * H(i * 2.7f + 0.3f);
                    float ph = Mathf.Repeat(t / per + H(i * 5.1f), 1f);
                    float a = Mathf.Sin(ph * Mathf.PI); a *= a;
                    if (a < 0.02f) continue;
                    Vector2 p = S(r.lake[i]) + new Vector2((ph - 0.5f) * 6f * k, 0f);
                    if (!Vis(p)) continue;
                    float len = (7f + 6f * H(i * 1.3f)) * k;
                    UiKit.Streak(p - new Vector2(len, 0f), p + new Vector2(len, 0f), 2.2f * k, new Color(0.82f, 0.88f, 1f, 0.3f * a), glow);
                }
            // облетающая сакура: лепестки FX/petals8 (4×2) падают, раскачиваясь, и тают у земли
            Texture2D pet = r.petals.Length > 0 ? UiKit.Tex("FX/petals8") : null;
            if (pet != null)
                foreach (Vector4 ar in r.petals)
                    for (int i = 0; i < 22; i++)
                    {
                        float life = 7f + 4f * H(i * 1.9f);
                        float run = t / life + H(i * 4.3f), ph = run - Mathf.Floor(run);
                        float seed = i * 13.7f + Mathf.Floor(run) * 3.1f;
                        Vector2 uv = new Vector2(ar.x + ar.z * H(seed), ar.y + ar.w * (0.15f + 0.85f * ph));
                        Vector2 p = S(uv) + new Vector2(Mathf.Sin(t * 1.3f + seed) * 9f * k + ph * 14f * k, 0f);
                        if (!Vis(p)) continue;
                        float ps = (5.5f + 2.5f * H(seed + 1f)) * k;
                        float a = Mathf.Clamp01(ph / 0.1f) * Mathf.Clamp01((1f - ph) / 0.2f) * 0.9f;
                        int c = (int)(H(seed + 2f) * 8f) % 8;
                        bool fl = Mathf.Sin(t * 2.1f + seed) < 0f;   // лепесток переворачивается в полёте
                        Rect cell = new Rect((c % 4) * 0.25f, c < 4 ? 0.5f : 0f, 0.25f, 0.5f);
                        if (fl) cell = new Rect(cell.x + 0.25f, cell.y, -0.25f, 0.5f);
                        GUI.color = new Color(1f, 0.93f, 0.97f, a);   // бледные, призрачные
                        GUI.DrawTextureWithTexCoords(new Rect(p.x - ps, p.y - ps * 0.5f, ps * 2f, ps), pet, cell);
                    }
            // дым из нарисованных печей и труб: клубы поднимаются, растут, уходят по ветру и тают
            // клуб — покадровый объёмный дым 8×8 (Brackeys VFX Bundle, CC0): за жизнь клуба проигрываются все 64 кадра,
            // дым клубится внутри, а не плывёт неподвижной кляксой. Нет листа — прежние четыре клуба fx_puff
            Texture2D wisp = r.smoke.Length > 0 ? UiKit.Tex("UI/fx_smoke_wisp") : null;
            Texture2D puff = r.smoke.Length > 0 && wisp == null ? UiKit.Tex("UI/fx_puff") : null;
            if (puff != null || wisp != null)
                for (int si = 0; si < r.smoke.Length; si++)
                {
                    Vector4 sm = r.smoke[si];
                    Vector2 src = S(new Vector2(sm.x, sm.y));
                    if (!Vis(src)) continue;
                    float size = sm.z * k, side = sm.w * k;
                    // нарисованный дым с картины стёрт (tools/map_extend/desmoke_kilns.py) — шлейф целиком даёт этот дым:
                    // 16 плотных клубов, сплошной столб без просветов
                    for (int p = 0; p < 16; p++)
                    {
                        float run = t / 4.6f + p / 16f + si * 0.173f;
                        float u = run - Mathf.Floor(run);
                        int seed = Mathf.FloorToInt(run) * 7 + p * 13 + si * 31;
                        // сначала столбом вверх, потом сносит вбок — как нарисовано на карте
                        float rise = size * (1f - (1f - u) * (1f - u * 0.4f));
                        float drift = side * Mathf.Pow(u, 1.8f) + Mathf.Sin(u * 3f + seed) * 0.04f * size;
                        float sz = size * Mathf.Lerp(0.16f, 0.7f, Mathf.Sqrt(u));
                        // тает только в самом конце: клуб виден почти до верха столба — анимация тянется на всю длину нарисованного дыма
                        float al = Mathf.Clamp01(u / 0.12f) * (1f - Mathf.Pow(u, 2.5f)) * 0.78f;
                        GUI.color = new Color(0.9f, 0.88f, 0.92f, al);
                        Rect pr = new Rect(src.x + drift - sz * 0.5f, src.y - rise - sz * 0.5f, sz, sz);
                        if (wisp != null)
                        {
                            int fr = Mathf.Min(63, (int)(u * 64f));
                            float fu = (fr % 8) * 0.125f, fv = 1f - (fr / 8 + 1) * 0.125f;
                            // соседние клубы зеркалятся — столб не выглядит одной повторяющейся картинкой
                            GUI.DrawTextureWithTexCoords(pr, wisp, (seed & 1) == 0 ? new Rect(fu, fv, 0.125f, 0.125f) : new Rect(fu + 0.125f, fv, -0.125f, 0.125f));
                        }
                        else
                        {
                            int c = seed & 3;
                            GUI.DrawTextureWithTexCoords(pr, puff, new Rect((c % 2) * 0.5f, c < 2 ? 0.5f : 0f, 0.5f, 0.5f));
                        }
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
