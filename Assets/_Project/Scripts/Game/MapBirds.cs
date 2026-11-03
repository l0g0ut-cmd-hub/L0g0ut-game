using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Птицы над картой похода: время от времени через область пролетает ворон (один или парой)
    /// или стайка белых цапель (5–12, клином или россыпью). Каждая птица машет крыльями в своём ритме
    /// (атлас 4×2, 8 кадров взмаха: UI/map_bird_crow, UI/map_bird_white), стая слегка дышит строем,
    /// под птицами — бледная тень на земле. Чисто украшение: на игру не влияет.
    /// </summary>
    public static class MapBirds
    {
        struct Bird { public Vector2 off; public float phase, rate, size, wob; }
        class Flock
        {
            public bool crow; public Vector2 from, to; public float start, duration; public readonly List<Bird> birds = new List<Bird>();
        }
        static readonly List<Flock> flocks = new List<Flock>();
        static float nextAt = -1f;

        static void Spawn(float now)
        {
            Flock f = new Flock { crow = Random.value < 0.35f, start = now };
            // пролёт через карту по случайной хорде: с одного края на другой, слегка наискось
            bool leftToRight = Random.value < 0.5f;
            float y0 = Random.Range(0.12f, 0.88f), y1 = Mathf.Clamp(y0 + Random.Range(-0.35f, 0.35f), 0.08f, 0.92f);
            f.from = new Vector2(leftToRight ? -0.08f : 1.08f, y0); f.to = new Vector2(leftToRight ? 1.08f : -0.08f, y1);
            f.duration = f.crow ? Random.Range(16f, 24f) : Random.Range(20f, 30f);
            int n = f.crow ? (Random.value < 0.6f ? 1 : 2) : Random.Range(5, 13);
            bool vee = !f.crow && Random.value < 0.55f;
            for (int i = 0; i < n; i++)
            {
                Vector2 off;
                if (f.crow) off = i == 0 ? Vector2.zero : new Vector2(-0.035f, Random.Range(-0.03f, 0.03f));
                else if (vee)
                {   // клин: вожак впереди, две ветви назад
                    int k = (i + 1) / 2; float side = i % 2 == 0 ? 1f : -1f;
                    off = new Vector2(-0.022f * k, side * 0.026f * k);
                }
                else off = new Vector2(Random.Range(-0.07f, 0.03f), Random.Range(-0.05f, 0.05f));
                f.birds.Add(new Bird
                {
                    off = off, phase = Random.value * 8f, rate = Random.Range(8f, 11f) * (f.crow ? 0.8f : 1f),
                    size = f.crow ? Random.Range(24f, 30f) : Random.Range(13f, 18f), wob = Random.value * 6.28f,
                });
            }
            flocks.Add(f);
        }

        /// <summary>Нарисовать птиц поверх карты (view — окно карты, u0/uw — видимая доля ширины карты).</summary>
        public static void Draw(Rect view, float u0, float uw)
        {
            if (Event.current.type != EventType.Repaint) return;
            float now = Time.unscaledTime;
            if (nextAt < 0f) nextAt = now + Random.Range(4f, 10f);
            if (now >= nextAt && flocks.Count < 2) { Spawn(now); nextAt = now + Random.Range(18f, 40f); }
            Texture2D white = UiKit.Tex("UI/map_bird_white"), crow = UiKit.Tex("UI/map_bird_crow"), shade = SoftSprites.Fog;
            Color oc = GUI.color;
            Matrix4x4 om = GUI.matrix;
            for (int fi = flocks.Count - 1; fi >= 0; fi--)
            {
                Flock f = flocks[fi];
                float u = (now - f.start) / f.duration;
                if (u >= 1f) { flocks.RemoveAt(fi); continue; }
                Texture2D tex = f.crow ? crow : white;
                if (tex == null) continue;
                Vector2 dir = (f.to - f.from); dir.y *= 540f / 810f;
                float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f;   // спрайт смотрит вверх
                Vector2 c = Vector2.Lerp(f.from, f.to, u);
                c.y += 0.015f * Mathf.Sin(u * 9f);   // лёгкая волна пролёта
                Vector2 fwd = (f.to - f.from).normalized, side = new Vector2(-fwd.y, fwd.x);
                foreach (Bird b in f.birds)
                {
                    float breathe = 1f + 0.08f * Mathf.Sin(now * 0.7f + b.wob);
                    Vector2 p = c + fwd * b.off.x + side * b.off.y * breathe + new Vector2(0f, 0.004f * Mathf.Sin(now * 1.3f + b.wob));
                    float sx = view.x + (p.x - u0) / uw * view.width, sy = p.y * view.height;
                    if (sx < view.x - 40f || sx > view.xMax + 40f) continue;
                    float s = b.size;
                    // тень на земле: ниже и правее, бледная
                    if (shade != null)
                    {
                        GUI.color = new Color(0f, 0f, 0f, 0.16f);
                        GUI.DrawTexture(new Rect(sx + s * 0.9f - s * 0.35f, sy + s * 1.6f - s * 0.2f, s * 0.7f, s * 0.4f), shade);
                    }
                    int frame = (int)(now * b.rate + b.phase) % 8;
                    GUIUtility.RotateAroundPivot(ang, new Vector2(sx, sy) * UiKit.Scale);
                    GUI.color = Color.white;
                    GUI.DrawTextureWithTexCoords(new Rect(sx - s * 0.5f, sy - s * 0.5f, s, s), tex,
                        new Rect((frame % 4) * 0.25f, frame < 4 ? 0.5f : 0f, 0.25f, 0.5f));
                    GUI.matrix = om;
                }
            }
            GUI.color = oc;
        }
    }
}
