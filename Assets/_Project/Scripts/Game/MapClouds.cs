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
        struct Cloud { public float x0, y, speed, size; public int sky; }   // sky: 1 дождь, 2 гроза
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
                    size = Random.Range(0.15f, 0.22f), sky = i % 2 == 0 ? 1 : 2,
                };
            Random.state = st;
        }

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
                float rx = c.size * 0.5f, ry = c.size * 0.34f;   // эллипс тени на карте (её высота — 0,45 ширины облака, карта 810×540)
                if ((d.x * d.x) / (rx * rx) + (d.y * d.y) / (ry * ry) <= 1f) sky = Mathf.Max(sky, c.sky);
            }
            return sky;
        }

        public static string SkyName(int sky) { return sky == 2 ? "гроза" : sky == 1 ? "дождь" : "ясно"; }

        /// <summary>Нарисовать облака поверх карты (после мест и героя): тень на земле, само облако, дождь и молнии.</summary>
        public static void Draw(int r, Rect view, float u0, float uw)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ensure(r);
            Texture2D cloud = UiKit.Tex("FX/cloud"), bolt = UiKit.Tex("FX/lightning"), shade = SoftSprites.Fog;
            Color oc = GUI.color;
            float t = Time.realtimeSinceStartup;
            for (int i = 0; i < clouds.Length; i++)
            {
                Cloud c = clouds[i];
                Vector2 cp = Center(c);
                float sx = view.x + (cp.x - u0) / uw * view.width, sy = cp.y * view.height;
                float w = c.size * view.width / uw, h = w * 0.5f;
                if (sx + w < view.x || sx - w > view.xMax) continue;
                bool storm = c.sky == 2;
                // тень облака на земле — она и показывает, где пойдёт дождь
                if (shade != null)
                {
                    GUI.color = new Color(0.02f, 0.02f, 0.06f, storm ? 0.42f : 0.3f);
                    GUI.DrawTexture(new Rect(sx - w * 0.5f, sy - h * 0.35f, w, h * 0.9f), shade);
                }
                // косые полосы дождя под облаком
                GUI.color = new Color(0.7f, 0.78f, 0.95f, storm ? 0.5f : 0.38f);
                for (int k = 0; k < 14; k++)
                {
                    float fx = sx + (k / 13f - 0.5f) * w * 0.7f + 6f * Mathf.Sin(k * 3.1f);
                    float ph = Mathf.Repeat(t * 1.6f + k * 0.37f, 1f);
                    float fy = sy - h * 0.1f + ph * h * 0.55f;
                    UiKit.Line(new Vector2(fx, fy), new Vector2(fx - 4f, fy + 12f), 1.2f, GUI.color);
                }
                if (cloud != null)
                {
                    GUI.color = storm ? new Color(0.36f, 0.32f, 0.46f, 0.85f) : new Color(0.72f, 0.74f, 0.82f, 0.78f);
                    GUI.DrawTexture(new Rect(sx - w * 0.5f, sy - h * 1.05f, w, h), cloud);
                }
                // молния у грозового облака: короткая вспышка раз в несколько секунд
                if (storm && bolt != null)
                {
                    float cyc = Mathf.Repeat(t + i * 1.7f, 4.5f);
                    if (cyc < 0.18f)
                    {
                        float a = cyc < 0.06f || (cyc > 0.1f && cyc < 0.14f) ? 1f : 0.35f;
                        GUI.color = new Color(0.85f, 0.9f, 1f, 0.9f * a);
                        GUI.DrawTexture(new Rect(sx - w * 0.08f + (i % 2 == 0 ? -w * 0.12f : w * 0.1f), sy - h * 0.55f, w * 0.16f, h * 0.8f), bolt);
                        GUI.color = new Color(0.8f, 0.85f, 1f, 0.18f * a);
                        GUI.DrawTexture(new Rect(sx - w * 0.5f, sy - h * 1.05f, w, h), cloud);
                    }
                }
            }
            GUI.color = oc;
        }
    }
}
