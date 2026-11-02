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
            clouds = new Cloud[4];
            for (int i = 0; i < clouds.Length; i++)
                clouds[i] = new Cloud
                {
                    x0 = Random.value * 1.4f, y = Random.Range(0.18f, 0.82f),
                    speed = Random.Range(0.006f, 0.011f) * (Random.value < 0.5f ? 1f : -1f),   // доля карты в секунду: пересекает за 1,5–3 мин
                    size = Random.Range(0.2f, 0.28f), sky = i % 2 == 0 ? 1 : 2,
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
            Ensure(r);
            int sky = 0;
            foreach (Cloud c in clouds)
            {
                Vector2 d = Center(c) - p;
                float t = Time.realtimeSinceStartup, k = Swell(c, t);
                float rx = c.size * 0.42f * k, ry = c.size * 0.42f * k * 1.5f;   // облако квадратное, карта 810×540 — по высоте в долях оно в 1,5 раза больше
                if ((d.x * d.x) / (rx * rx) + (d.y * d.y) / (ry * ry) <= 1f) sky = Mathf.Max(sky, Storminess(c, t) >= 0.5f ? 2 : 1);
            }
            return sky;
        }

        public static string SkyName(int sky) { return sky == 2 ? "гроза" : sky == 1 ? "дождь" : "ясно"; }

        static Rect Cell(int row, int col) { return new Rect(col * 0.5f, row == 0 ? 0.5f : 0f, 0.5f, 0.5f); }   // UI/map_clouds: сверху дождевые, снизу грозовые

        /// <summary>
        /// Один слой облака: клетка атласа, повёрнутая и растянутая по-своему. Облако — это два слоя двух разных форм,
        /// которые медленно вращаются навстречу и перетекают друг в друга, поэтому силуэт всё время меняется.
        /// </summary>
        static void Layer(Texture2D tex, Rect cell, Vector2 c, float w, float h, float angle, Color col)
        {
            if (col.a <= 0.01f) return;
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, c * UiKit.Scale);
            GUI.color = col;
            GUI.DrawTextureWithTexCoords(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h), tex, cell);
            GUI.matrix = m;
        }

        /// <summary>Нарисовать облака поверх карты (после мест и героя): тень на земле, дождь, само облако и молнии.</summary>
        public static void Draw(int r, Rect view, float u0, float uw)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ensure(r);
            Texture2D atlas = UiKit.Tex("UI/map_clouds"), old = UiKit.Tex("FX/cloud"), bolt = UiKit.Tex("FX/lightning"), shade = SoftSprites.Fog;
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
                // тень облака на земле — она и показывает, где пойдёт дождь
                if (shade != null)
                {
                    GUI.color = new Color(0.02f, 0.02f, 0.06f, Mathf.Lerp(0.3f, 0.48f, storm));
                    GUI.DrawTexture(new Rect(sx - w * 0.45f, sy - w * 0.1f, w * 0.9f, w * 0.6f), shade);
                }
                // косые полосы дождя под облаком, гуще в грозу
                Color rain = new Color(0.7f, 0.78f, 0.95f, Mathf.Lerp(0.36f, 0.55f, storm));
                int drops = storm > 0.5f ? 20 : 14;
                for (int q = 0; q < drops; q++)
                {
                    float fx = sx + (q / (drops - 1f) - 0.5f) * w * 0.6f + 6f * Mathf.Sin(q * 3.1f);
                    float ph = Mathf.Repeat(t * 1.6f + q * 0.37f, 1f);
                    float fy = sy - w * 0.05f + ph * w * 0.32f;
                    UiKit.Line(new Vector2(fx, fy), new Vector2(fx - 4f, fy + 12f), 1.2f, rain);
                }
                Vector2 cc = new Vector2(sx, sy - w * 0.18f);
                if (atlas != null)
                {
                    // две формы перетекают друг в друга (период ~40–60 с), каждая медленно вращается и дышит по-своему
                    float morph = 0.5f + 0.5f * Mathf.Sin(t * (0.11f + 0.02f * i) + c.phase);
                    float a1 = t * (1.6f + 0.4f * i) + c.phase * 30f, a2 = -t * (1.3f + 0.3f * i) + c.phase * 50f;
                    float st1 = 1f + 0.08f * Mathf.Sin(t * 0.09f + i), st2 = 1f + 0.08f * Mathf.Cos(t * 0.08f + i * 2f);
                    for (int row = 0; row < 2; row++)
                    {
                        float rowA = row == 1 ? storm : 1f - storm;   // дождевое ↔ грозовое — тоже плавно
                        if (rowA <= 0.01f) continue;
                        Layer(atlas, Cell(row, i % 2), cc, w * st1, w * 0.86f / st1, a1, new Color(1f, 1f, 1f, 0.92f * rowA * (1f - 0.55f * morph)));
                        Layer(atlas, Cell(row, 1 - i % 2), cc + new Vector2(w * 0.05f * Mathf.Sin(t * 0.05f + i), 0f), w * 0.95f * st2, w * 0.82f / st2, a2,
                            new Color(1f, 1f, 1f, 0.92f * rowA * (0.45f + 0.55f * morph)));
                    }
                }
                else if (old != null)
                {
                    GUI.color = Color.Lerp(new Color(0.72f, 0.74f, 0.82f, 0.78f), new Color(0.36f, 0.32f, 0.46f, 0.85f), storm);
                    GUI.DrawTexture(new Rect(cc.x - w * 0.5f, cc.y - w * 0.25f, w, w * 0.5f), old);
                }
                // молния у грозового облака: короткая вспышка раз в несколько секунд
                if (storm > 0.5f)
                {
                    float cyc = Mathf.Repeat(t + i * 1.7f, 4.5f);
                    if (cyc < 0.18f)
                    {
                        float a = cyc < 0.06f || (cyc > 0.1f && cyc < 0.14f) ? 1f : 0.35f;
                        if (bolt != null)
                        {
                            GUI.color = new Color(0.85f, 0.9f, 1f, 0.9f * a);
                            GUI.DrawTexture(new Rect(sx - w * 0.06f + (i % 2 == 0 ? -w * 0.1f : w * 0.08f), sy - w * 0.12f, w * 0.12f, w * 0.34f), bolt);
                        }
                        if (atlas != null) Layer(atlas, Cell(1, i % 2), cc, w, w * 0.86f, t * (1.6f + 0.4f * i) + c.phase * 30f, new Color(0.75f, 0.85f, 1f, 0.35f * a));
                    }
                }
            }
            GUI.color = oc;
        }
    }
}
