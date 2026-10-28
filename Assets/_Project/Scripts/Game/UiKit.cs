using UnityEngine;

namespace BladePath
{
    /// <summary>Мелкие помощники рисования IMGUI в «виртуальных» единицах (высота экрана = 540): диски, кольца, линии, текст с тенью.</summary>
    public static class UiKit
    {
        public const float VirtualHeight = 540f;
        static Texture2D white, disc, ring, ringThin;
        static GUIStyle label;

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

        public static void Text(Rect r, string s, float size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool shadow = true)
        {
            Init();
            label.fontSize = Mathf.Max(6, Mathf.RoundToInt(size));
            label.alignment = anchor;
            if (shadow)
            {
                label.normal.textColor = new Color(0f, 0f, 0f, 0.85f * c.a);
                Rect sr = r; sr.x += 1.5f; sr.y += 1.5f;
                GUI.Label(sr, s, label);
            }
            label.normal.textColor = c;
            GUI.Label(r, s, label);
        }

        public static Matrix4x4 Begin()
        {
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            return old;
        }
    }
}
