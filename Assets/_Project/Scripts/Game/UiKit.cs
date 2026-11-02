using UnityEngine;

namespace BladePath
{
    /// <summary>Мелкие помощники рисования IMGUI в «виртуальных» единицах (высота экрана = 540): диски, кольца, линии, текст с тенью.</summary>
    public static class UiKit
    {
        public const float VirtualHeight = 540f;
        static Texture2D white, disc, ring, ringThin;
        static GUIStyle label;
        // One mutable IMGUI content object replaces thousands of per-label
        // GUIContent allocations across map, inventory and battle HUD.
        static readonly GUIContent sharedTextContent = new GUIContent();

        public static float Scale { get { return Mathf.Max(0.01f, Screen.height / VirtualHeight); } }
        public static float Width { get { return Screen.width / Scale; } }

        static Texture2D Make(int n, float inner)
        {
            Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            float c = (n - 1) * 0.5f, R = n * 0.5f - 1.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(R - d + 0.5f);
                    if (inner > 0f) a = Mathf.Min(a, Mathf.Clamp01(d - R * inner + 0.5f));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static void Init()
        {
            if (white != null) return;
            white = Texture2D.whiteTexture;
            disc = Make(128, 0f);
            ring = Make(128, 0.93f);
            ringThin = Make(128, 0.97f);
            label = new GUIStyle(GUI.skin.label);
            label.alignment = TextAnchor.MiddleCenter;
            label.fontStyle = FontStyle.Bold;
            label.wordWrap = false;
            label.clipping = TextClipping.Overflow;
        }

        static readonly System.Collections.Generic.Dictionary<string, Texture2D> texCache = new System.Collections.Generic.Dictionary<string, Texture2D>();
        /// <summary>Текстура интерфейса из Resources (UI/…, FX/…); null, если её нет.</summary>
        public static Texture2D Tex(string path)
        {
            Texture2D t;
            if (!texCache.TryGetValue(path, out t)) { t = Resources.Load<Texture2D>(path); texCache[path] = t; }
            return t;
        }

        /// <summary>Нарисовать текстуру по центру с тоном.</summary>
        public static void Image(Vector2 center, float radius, Texture2D t, Color tint)
        {
            if (t == null) return;
            Color o = GUI.color; GUI.color = tint;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), t);
            GUI.color = o;
        }

        static Texture2D paperTex; static bool paperTried;
        /// <summary>Панель из старой рисовой бумаги (Resources/UI/paper_ui), тонированная; без текстуры — обычный прямоугольник.</summary>
        public static void Paper(Rect r, Color tint)
        {
            Init();
            if (!paperTried) { paperTried = true; paperTex = Resources.Load<Texture2D>("UI/paper_ui"); }
            Color o = GUI.color; GUI.color = tint;
            GUI.DrawTexture(r, paperTex != null ? paperTex : white, ScaleMode.StretchToFill);
            GUI.color = o;
        }

        /// <summary>Резные уголки рамки (чёрный лак с золотом, Resources/UI/corner) по четырём углам, чуть наружу.</summary>
        public static void Corners(Rect r, float size, float alpha = 1f)
        {
            Texture2D t = Tex("UI/corner");
            if (t == null) return;
            Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            float s = size, k = size * 0.16f;
            GUI.DrawTextureWithTexCoords(new Rect(r.x - k, r.y - k, s, s), t, new Rect(0f, 0f, 1f, 1f));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - s + k, r.y - k, s, s), t, new Rect(1f, 0f, -1f, 1f));
            GUI.DrawTextureWithTexCoords(new Rect(r.x - k, r.yMax - s + k, s, s), t, new Rect(0f, 1f, 1f, -1f));
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - s + k, r.yMax - s + k, s, s), t, new Rect(1f, 1f, -1f, -1f));
            GUI.color = o;
        }

        /// <summary>Знак тушью (белая маска, цвет — оттенком) высотой h по центру c; reveal — кисть проводит слева направо.</summary>
        public static void Ink(string tex, Vector2 c, float h, Color col, float reveal = 1f)
        {
            Texture2D t = Tex(tex);
            if (t == null || col.a <= 0.01f || reveal <= 0.001f) return;
            float w = h * t.width / Mathf.Max(1f, t.height);
            reveal = 1f - (1f - reveal) * (1f - reveal);
            Color o = GUI.color; GUI.color = col;
            GUI.DrawTextureWithTexCoords(new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w * reveal, h), t, new Rect(0f, 0f, reveal, 1f));
            GUI.color = o;
        }

        public static void Rect(Rect r, Color c) { Init(); Color o = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = o; }

        public static void Disc(Vector2 center, float radius, Color c)
        {
            Init(); Color o = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), disc);
            GUI.color = o;
        }

        public static void Ring(Vector2 center, float radius, Color c, bool thin = false)
        {
            Init(); Color o = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), thin ? ringThin : ring);
            GUI.color = o;
        }

        public static void Line(Vector2 a, Vector2 b, float thickness, Color c)
        {
            Init();
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 m = GUI.matrix;
            // поворот вокруг начала отрезка в локальных (виртуальных) координатах; RotateAroundPivot тут не годится: его точка задана в пикселях
            GUI.matrix = m * Matrix4x4.TRS(new Vector3(a.x, a.y, 0f), Quaternion.Euler(0f, 0f, ang), Vector3.one);
            Color o = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(0f, -thickness * 0.5f, len, thickness), white);
            GUI.color = o;
            GUI.matrix = m;
        }

        static Font display, body, digits;
        static bool fontsLoaded;
        static void LoadFonts()
        {
            if (fontsLoaded) return;
            fontsLoaded = true;
            display = Resources.Load<Font>("Fonts/YesevaOne-Regular");
            body = Resources.Load<Font>("Fonts/OldStandard-Bold");
            digits = Resources.Load<Font>("Fonts/RussoOne-Regular");
        }

        static bool IsNumeric(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) { char ch = s[i]; if (!(char.IsDigit(ch) || ch == ' ' || ch == ':' || ch == '/' || ch == '-' || ch == '+')) return false; }
            return true;
        }

        /// <summary>
        /// Текст в виртуальных единицах, но растеризуется в реальных пикселях (иначе шрифт мылится при масштабе GUI).
        /// Шрифты: крупные надписи — Yeseva One, мелкие — Old Standard, цифры — Russo One (все OFL, Resources/Fonts).
        /// </summary>
        /// <summary>
        /// Число кистью (UI/brush_digits: 0–9, «×», «!» в листе 4×3). anchor — по горизонтали: слева, по центру, справа от точки.
        /// Возвращает false, если листа нет или в строке есть другой знак (тогда пишем шрифтом).
        /// </summary>
        public static bool BrushNumber(Vector2 at, string s, float h, float alpha, TextAnchor anchor)
        {
            Texture2D t = Tex("UI/brush_digits");
            if (t == null || string.IsNullOrEmpty(s)) return false;
            float gw = h * 0.66f, step = h * 0.56f;   // мазки чуть заходят друг на друга, как у каллиграфа
            for (int i = 0; i < s.Length; i++) if (Glyph(s[i]) < 0) return false;
            float total = step * (s.Length - 1) + gw;
            float x = anchor == TextAnchor.MiddleLeft ? at.x : anchor == TextAnchor.MiddleRight ? at.x - total : at.x - total * 0.5f;
            Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            for (int i = 0; i < s.Length; i++)
            {
                int g = Glyph(s[i]);
                Rect uv = new Rect((g % 4) * 0.25f, 1f - (g / 4 + 1) / 3f, 0.25f, 1f / 3f);
                GUI.DrawTextureWithTexCoords(new Rect(x + i * step, at.y - h * 0.5f, gw, h), t, uv);
            }
            GUI.color = oc;
            return true;
        }
        static int Glyph(char ch) { return ch >= '0' && ch <= '9' ? ch - '0' : ch == '×' || ch == 'x' ? 10 : ch == '!' ? 11 : -1; }

        public static void Text(Rect r, string s, float size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool shadow = true)
        {
            Init();
            LoadFonts();
            Font f = IsNumeric(s) ? digits : (size >= 26f ? display : body);
            label.font = f;   // null → шрифт по умолчанию
            Matrix4x4 m = GUI.matrix;
            float k = Scale;
            GUI.matrix = m * Matrix4x4.Scale(new Vector3(1f / k, 1f / k, 1f));
            Rect pr = new Rect(r.x * k, r.y * k, r.width * k, r.height * k);
            label.fontSize = Mathf.Max(6, Mathf.RoundToInt(size * k));
            label.alignment = anchor;
            sharedTextContent.text = s ?? string.Empty;
            // не вылезать за рамку: длинная надпись ужимается по ширине (не мельче 55 %)
            if (pr.width > 24f && !string.IsNullOrEmpty(s))
            {
                float w = label.CalcSize(sharedTextContent).x, room = pr.width - 4f * k;
                if (w > room) label.fontSize = Mathf.Max(6, Mathf.RoundToInt(label.fontSize * Mathf.Max(0.55f, room / w)));
            }
            if (shadow)
            {
                label.normal.textColor = new Color(0f, 0f, 0f, 0.85f * c.a);
                Rect sr = pr; sr.x += 1.5f * k; sr.y += 1.5f * k;
                GUI.Label(sr, sharedTextContent, label);
            }
            label.normal.textColor = c;
            GUI.Label(pr, sharedTextContent, label);
            GUI.matrix = m;
        }

        public static Matrix4x4 Begin()
        {
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            return old;
        }
    }
}
