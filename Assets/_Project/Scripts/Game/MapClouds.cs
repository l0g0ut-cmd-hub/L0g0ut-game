using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Погода над картой похода: несколько облаков (дождевые и грозовые) медленно плывут по области.
    /// Облако над местом боя в момент выхода на бой задаёт погоду арены (Weather.MapSky):
    /// нет облака — ясно, дождевое — дождь, грозовое — гроза с молниями. Время суток не меняется.
    /// Положение облаков — функция реального времени, поэтому после боя они продолжают путь.
    /// </summary>
    public static class MapClouds
    {
        struct Cloud { public float x0, y, speed, size, period, phase; public int sky; }   // sky: 1 дождевое, 2 грозовое (по нраву)
        static Cloud[] clouds;
        static int region = -1;

        static void Ensure(int r)
        {
            if (clouds != null && region == r) return;
            region = r;
            Random.State st = Random.state;
            Random.InitState(9173 + r * 131);
            // карты расширены (MapGeom): облаков больше, размер и скорость — в долях широкой карты, на глаз как прежде
            float u = MapGeom.OldU(r);
            clouds = new Cloud[Mathf.Max(4, Mathf.RoundToInt(4f / u))];
            for (int i = 0; i < clouds.Length; i++)
                clouds[i] = new Cloud
                {
                    x0 = Random.value * 1.4f, y = Random.Range(0.18f, 0.82f),
                    speed = Random.Range(0.006f, 0.011f) * u * (Random.value < 0.5f ? 1f : -1f),   // 1,5–3 мин на старую ширину карты
                    size = Random.Range(0.2f, 0.28f) * u, sky = i % 2 == 0 ? 1 : 2,
                    period = Random.Range(110f, 220f), phase = Random.value * 6.283f,
                };
            Random.state = st;
        }

        /// <summary>
        /// Насколько облако сейчас грозовое (0..1). Облака живут: дождевое время от времени темнеет и набухает
        /// грозой, грозовое — иногда выдыхается до дождя. Погода в точке берётся отсюда же, поэтому картинка и бой совпадают.
        /// </summary>
        static float Storminess(Cloud c, float t)
        {
            float s = 0.5f + 0.5f * Mathf.Sin(t * 6.283f / c.period + c.phase);
            return c.sky == 2 ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.38f, s))
                              : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.74f, 0.92f, s));
        }

        /// <summary>Дыхание облака: размер плавно гуляет ±15%, форма тянется то вширь, то ввысь.</summary>
        static float Swell(Cloud c, float t) { return 1f + 0.15f * Mathf.Sin(t * 0.07f + c.phase * 1.3f); }

        static Vector2 Center(Cloud c)
        {
            float t = Time.realtimeSinceStartup;
            float x = Mathf.Repeat(c.x0 + c.speed * t, 1.6f) - 0.3f;   // уходит за край и возвращается с другой стороны
            return new Vector2(x, c.y + 0.03f * Mathf.Sin(t * 0.05f + c.x0 * 7f));
        }

        /// <summary>Погода в точке карты (доли 0..1): 0 ясно, 1 дождь, 2 гроза (гроза сильнее, если облака перекрываются).</summary>
        public static int SkyAt(int r, Vector2 p)
        {
            if (Weather.NoRain) return 0;   // дождь и гроза выключены в меню разработчика
            Ensure(r);
            int sky = 0;
            foreach (Cloud c in clouds)
            {
                Vector2 d = Center(c) - p;
                float t = Time.realtimeSinceStartup, k = Swell(c, t);
                float rx = c.size * 0.46f * k, ry = c.size * 0.46f * k * MapGeom.W(r) / 540f;   // облако квадратное: по высоте в долях оно больше во столько, во сколько карта шире
                // облако висит над тенью (рисуется выше на 0,2 своей ширины): место под дождём — и под тенью, и под самим облаком,
                // иначе игрок видит тучу над местом, а в бою ясно
                Vector2 body = d - new Vector2(0f, c.size * k * 0.3f);   // центр облака выше тени на карте
                bool under = (d.x * d.x) / (rx * rx) + (d.y * d.y) / (ry * ry) <= 1f || (body.x * body.x) / (rx * rx) + (body.y * body.y) / (ry * ry) <= 1f;
                if (under) sky = Mathf.Max(sky, Storminess(c, t) >= 0.5f ? 2 : 1);
            }
            return sky;
        }

        public static string SkyName(int sky) { return sky == 2 ? "гроза" : sky == 1 ? "дождь" : "ясно"; }

        static Rect Cell(int row, int col, bool flip)
        {
            // UI/map_clouds: сверху дождевые, снизу грозовые; flip — зеркально (больше разных форм из четырёх клеток)
            return flip ? new Rect(col * 0.5f + 0.5f, row == 0 ? 0.5f : 0f, -0.5f, 0.5f) : new Rect(col * 0.5f, row == 0 ? 0.5f : 0f, 0.5f, 0.5f);
        }

        static float Hash(float a) { float x = Mathf.Sin(a * 12.9898f) * 43758.5453f; return x - Mathf.Floor(x); }

        const int Puffs = 7;   // клубов в облаке: ядро + 6 живых клубов

        /// <summary>
        /// Клуб облака k в момент t: смещение от центра (в ширинах облака), размер, прозрачность, клетка и зеркальность.
        /// Каждый клуб живёт 16–28 с: рождается прозрачным и маленьким, набухает и дрейфует наружу, тает —
        /// и рождается снова в другом месте облака. Жизни клубов сдвинуты, поэтому облако всегда плотное,
        /// а его силуэт всё время медленно клубится и перестраивается.
        /// </summary>
        static void Puff(int ci, int k, float t, out Vector2 off, out float size, out float alpha, out int col, out bool flip)
        {
            if (k == 0)
            {   // ядро: держит форму облака
                off = new Vector2(0.03f * Mathf.Sin(t * 0.05f + ci), 0.02f * Mathf.Cos(t * 0.04f + ci));
                size = 0.78f + 0.05f * Mathf.Sin(t * 0.06f + ci * 2f); alpha = 0.9f; col = ci % 2; flip = ci % 3 == 0; return;
            }
            float life = 16f + 12f * Hash(ci * 7.1f + k * 1.3f);
            float tt = t / life + k / (float)(Puffs - 1) + Hash(ci * 3.3f + k);
            float cycle = Mathf.Floor(tt), ph = tt - cycle;
            float seed = ci * 31.7f + k * 5.9f + cycle * 1.618f;
            float ang = Hash(seed) * 6.2832f, rad = 0.12f + 0.2f * Hash(seed + 1.7f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.62f);
            off = dir * (rad + 0.09f * ph);                          // дрейф наружу за жизнь
            size = (0.42f + 0.22f * Hash(seed + 2.9f)) * (0.78f + 0.34f * ph);   // набухает
            alpha = Mathf.Sin(ph * Mathf.PI); alpha = alpha * alpha * (3f - 2f * alpha) * 0.85f;
            col = Hash(seed + 4.1f) < 0.5f ? 0 : 1; flip = Hash(seed + 5.3f) < 0.5f;
        }

        /// <summary>Нарисовать облака поверх карты (после мест и героя): тень на земле, дождь, клубящееся облако и молнии.</summary>
        public static void Draw(int r, Rect view, float u0, float uw)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (Weather.NoRain) return;   // без дождя и грозы — и тучи с карты убраны
            Ensure(r);
            Texture2D puffs = UiKit.Tex("UI/map_cloud_puffs"), bolts = UiKit.Tex("FX/map_lightning");
            Texture2D atlas = UiKit.Tex("UI/map_clouds"), old = UiKit.Tex("FX/cloud"), bolt = UiKit.Tex("FX/lightning"),
                glow = UiKit.Tex("FX/glow_pool"), shade = SoftSprites.Fog;
            Color oc = GUI.color;
            float t = Time.realtimeSinceStartup;
            for (int i = 0; i < clouds.Length; i++)
            {
                Cloud c = clouds[i];
                Vector2 cp = Center(c);
                float k = Swell(c, t), storm = Storminess(c, t);
                float sx = view.x + (cp.x - u0) / uw * view.width, sy = cp.y * view.height;
                float w = c.size * view.width / uw * k;
                if (sx + w < view.x || sx - w > view.xMax) continue;
                Vector2 cc = new Vector2(sx, sy - w * 0.2f);   // облако висит над своей тенью
                // тень на земле — мягкая, смещена от солнца; по ней видно, где пойдёт дождь
                if (shade != null)
                {
                    GUI.color = new Color(0.02f, 0.02f, 0.06f, Mathf.Lerp(0.26f, 0.42f, storm));
                    GUI.DrawTexture(new Rect(sx - w * 0.48f + w * 0.04f, sy - w * 0.14f, w * 0.96f, w * 0.62f), shade);
                }
                // дождь: косые струи разной длины и яркости, падают из-под облака до тени, гуще в грозу
                int drops = Mathf.RoundToInt(Mathf.Lerp(22f, 40f, storm));
                for (int q = 0; q < drops; q++)
                {
                    float hq = Hash(i * 13.1f + q * 0.77f);
                    float speed = 1.1f + 0.6f * Hash(q * 3.7f + i);
                    float ph = Mathf.Repeat(t * speed + hq * 5f, 1f);
                    float fx = sx + (hq - 0.5f) * w * 0.72f;
                    // капли падают на разную «глубину» под тучей (не все на одну линию) и у земли разбиваются брызгами
                    float y0 = cc.y + w * 0.12f, y1 = sy + w * Mathf.Lerp(-0.02f, 0.22f, Hash(q * 2.3f + i * 0.9f));
                    float fy = Mathf.Lerp(y0, y1, Mathf.Min(1f, ph / 0.88f));
                    float len = 9f + 9f * Hash(q * 1.9f + i * 0.3f);
                    if (ph < 0.88f)
                    {
                        float a = Mathf.Lerp(0.28f, 0.5f, storm) * Mathf.Sin(ph / 0.88f * Mathf.PI * 0.85f + 0.15f) * (0.6f + 0.4f * Hash(q + i * 2f));
                        UiKit.Line(new Vector2(fx + len * 0.3f, fy - len), new Vector2(fx, fy), 1.1f, new Color(0.78f, 0.84f, 0.98f, a));
                    }
                    else if (glow != null)
                    {
                        // брызги: маленькое светлое пятнышко расходится вширь и гаснет
                        float sp = (ph - 0.88f) / 0.12f, bw = 3f + 5f * sp;
                        GUI.color = new Color(0.82f, 0.88f, 1f, Mathf.Lerp(0.35f, 0.55f, storm) * (1f - sp));
                        GUI.DrawTexture(new Rect(fx - bw, y1 - bw * 0.3f, bw * 2f, bw * 0.6f), glow);
                    }
                }
                if (atlas != null || puffs != null)
                {
                    // клубы: сначала дальние (тёмные, ниже), сверху — освещённые
                    for (int pass = 0; pass < 2; pass++)
                        for (int pk = 0; pk < Puffs; pk++)
                        {
                            Puff(i, pk, t, out Vector2 off, out float ps, out float pa, out int col, out bool flip);
                            if (pa <= 0.02f) continue;
                            Vector2 pc = cc + new Vector2(off.x * w, off.y * w + (pass == 0 ? w * 0.04f : 0f));
                            float pw = w * ps * (pass == 0 ? 1.04f : 1f), ph2 = pw * 0.86f;
                            float shadeK = pass == 0 ? 0.55f : 1f;
                            for (int row = 0; row < 2; row++)
                            {
                                float rowA = row == 1 ? storm : 1f - storm;   // дождевое ↔ грозовое — плавно
                                if (rowA <= 0.01f) continue;
                                GUI.color = new Color(shadeK, shadeK, shadeK * 1.05f, pa * rowA * (pass == 0 ? 0.55f : 0.9f));
                                if (puffs != null && (pk > 0 || atlas == null))   // ядро — целое облако из UI/map_clouds, клубы — из атласа клубов
                                {
                                    // UI/map_cloud_puffs: 4×4, два верхних ряда — 8 дождевых клубов, два нижних — 8 грозовых
                                    int idx = (int)(Hash(i * 5.3f + pk * 2.7f + col * 11.1f + (flip ? 3f : 0f)) * 8f) % 8;
                                    float cx = (idx % 4) * 0.25f, cy = 1f - (idx / 4 + (row == 1 ? 2 : 0) + 1) * 0.25f;
                                    float bw = pw * 1.15f;
                                    GUI.DrawTextureWithTexCoords(new Rect(pc.x - bw * 0.5f, pc.y - bw * 0.5f, bw, bw), puffs,
                                        flip ? new Rect(cx + 0.25f, cy, -0.25f, 0.25f) : new Rect(cx, cy, 0.25f, 0.25f));
                                }
                                else
                                    GUI.DrawTextureWithTexCoords(new Rect(pc.x - pw * 0.5f, pc.y - ph2 * 0.5f, pw, ph2), atlas, Cell(row, col, flip));
                            }
                        }
                }
                else if (old != null)
                {
                    GUI.color = Color.Lerp(new Color(0.72f, 0.74f, 0.82f, 0.78f), new Color(0.36f, 0.32f, 0.46f, 0.85f), storm);
                    GUI.DrawTexture(new Rect(cc.x - w * 0.5f, cc.y - w * 0.25f, w, w * 0.5f), old);
                }
                // гроза: вспышка внутри облака (подсвечивает клубы изнутри), иногда — разряд до земли и отблеск на ней
                if (storm > 0.5f)
                {
                    float period = 3.2f + 2.4f * Hash(i * 9.1f);
                    float cyc = Mathf.Repeat(t + i * 1.7f, period);
                    int strike = Mathf.FloorToInt((t + i * 1.7f) / period);
                    if (cyc < 0.32f)
                    {
                        float a = cyc < 0.05f ? 1f : cyc < 0.1f ? 0.25f : cyc < 0.16f ? 0.85f : 0.85f * (1f - (cyc - 0.16f) / 0.16f);
                        Vector2 fp = cc + new Vector2((Hash(strike * 1.3f + i) - 0.5f) * w * 0.5f, (Hash(strike * 2.1f + i) - 0.5f) * w * 0.2f);
                        if (glow != null)
                        {
                            GUI.color = new Color(0.6f, 0.75f, 1f, 0.55f * a);
                            GUI.DrawTexture(new Rect(fp.x - w * 0.32f, fp.y - w * 0.22f, w * 0.64f, w * 0.44f), glow);
                        }
                        if ((bolts != null || bolt != null) && Hash(strike * 4.7f + i) < 0.6f)   // не каждая вспышка бьёт в землю
                        {
                            float bh = (sy + w * 0.2f) - fp.y, bw = bh * 0.5f;
                            GUI.color = new Color(0.9f, 0.94f, 1f, 0.95f * a);
                            bool mirror = Hash(strike * 6.1f) < 0.5f;
                            if (bolts != null)
                            {   // FX/map_lightning: 4 разные молнии в ряд
                                float bx = (int)(Hash(strike * 3.9f + i) * 4f) % 4 * 0.25f;
                                GUI.DrawTextureWithTexCoords(new Rect(fp.x - bw * 0.5f, fp.y, bw, bh), bolts,
                                    mirror ? new Rect(bx + 0.25f, 0f, -0.25f, 1f) : new Rect(bx, 0f, 0.25f, 1f));
                            }
                            else
                                GUI.DrawTextureWithTexCoords(new Rect(fp.x - bw * 0.5f, fp.y, bw * 0.76f, bh), bolt,
                                    mirror ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f));
                            if (glow != null)
                            {
                                GUI.color = new Color(0.7f, 0.82f, 1f, 0.35f * a);
                                GUI.DrawTexture(new Rect(fp.x - w * 0.2f, sy + w * 0.2f - w * 0.08f, w * 0.4f, w * 0.16f), glow);
                            }
                        }
                    }
                }
            }
            GUI.color = oc;
        }
    }
}
