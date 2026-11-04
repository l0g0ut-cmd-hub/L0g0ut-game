using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Shared art direction for the entire game: weathered black lacquer,
    /// aged paper, warm brass, vermilion and subdued jade. This is presentation
    /// only; no game economy, input or combat decisions live here.
    ///
    /// UI drawings use the existing high-quality Resources/UI ornaments and a
    /// single tiny runtime-generated brushed-lacquer pattern.
    /// </summary>
    public static class VisualIdentity
    {
        public static readonly Color Lacquer = new Color(0.050f, 0.038f, 0.033f, 0.98f);
        public static readonly Color Raised = new Color(0.105f, 0.079f, 0.064f, 0.96f);
        public static readonly Color InsetColor = new Color(0.031f, 0.026f, 0.025f, 0.92f);
        public static readonly Color Brass = new Color(0.86f, 0.70f, 0.38f, 1f);
        public static readonly Color BrassLight = new Color(1f, 0.85f, 0.58f, 1f);
        public static readonly Color Parchment = new Color(0.95f, 0.90f, 0.80f, 1f);
        public static readonly Color Subdued = new Color(0.73f, 0.68f, 0.60f, 1f);
        public static readonly Color Vermilion = new Color(0.78f, 0.18f, 0.13f, 1f);
        public static readonly Color Jade = new Color(0.60f, 0.86f, 0.59f, 1f);
        public static readonly Color MistBlue = new Color(0.64f, 0.79f, 0.89f, 1f);
        static Texture2D lacquerGrain;

        static Texture2D Grain
        {
            get { if (lacquerGrain == null) lacquerGrain = MakeLacquerGrain(); return lacquerGrain; }
        }

        static Texture2D MakeLacquerGrain()
        {
            const int w = 96, h = 96;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "BP_Brushed_Lacquer",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };
            Color32[] pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fine = Mathf.PerlinNoise(x * 0.19f + 7f, y * 0.055f + 3f);
                float vein = Mathf.Sin(y * 0.39f + Mathf.Sin(x * 0.08f) * 1.9f);
                float n = Mathf.Clamp01(0.13f + fine * 0.12f + vein * 0.025f);
                pixels[y * w + x] = new Color32(
                    (byte)Mathf.RoundToInt(46f + n * 32f),
                    (byte)Mathf.RoundToInt(33f + n * 23f),
                    (byte)Mathf.RoundToInt(27f + n * 20f), 255);
            }
            t.SetPixels32(pixels);
            t.Apply(false, true);
            return t;
        }

        public static void Surface(Rect r, float alpha = 1f, bool ornaments = true)
        {
            if (r.width <= 3f || r.height <= 3f) return;
            UiKit.Rect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f),
                new Color(0.39f, 0.26f, 0.12f, alpha));
            UiKit.Rect(r, new Color(Lacquer.r, Lacquer.g, Lacquer.b, Lacquer.a * alpha));
            Color old = GUI.color;
            GUI.color = new Color(0.43f, 0.32f, 0.22f, 0.11f * alpha);
            GUI.DrawTextureWithTexCoords(r, Grain,
                new Rect(0f, 0f, r.width / 128f, r.height / 128f));
            GUI.color = old;
            UiKit.Rect(new Rect(r.x + 5f, r.y + 5f, r.width - 10f, 1f),
                new Color(BrassLight.r, BrassLight.g, BrassLight.b, 0.32f * alpha));
            UiKit.Rect(new Rect(r.x + 5f, r.yMax - 6f, r.width - 10f, 1f),
                new Color(Brass.r, Brass.g, Brass.b, 0.20f * alpha));
            if (ornaments && r.width >= 180f && r.height >= 110f)
                UiKit.Corners(r, Mathf.Min(43f, r.height * 0.25f), 0.65f * alpha);
        }

        public static void Inset(Rect r, Color? accent = null, float alpha = -1f)
        {
            Color rim = accent ?? Brass;
            UiKit.Rect(r, alpha < 0f ? InsetColor : new Color(InsetColor.r, InsetColor.g, InsetColor.b, alpha));
            UiKit.Rect(new Rect(r.x, r.y, r.width, 1.2f),
                new Color(rim.r, rim.g, rim.b, 0.34f));
            UiKit.Rect(new Rect(r.x, r.yMax - 1.2f, r.width, 1.2f),
                new Color(rim.r, rim.g, rim.b, 0.12f));
        }

        public static void Heading(Rect r, string label, Color? accent = null)
        {
            Color ink = accent ?? Brass;
            UiKit.Rect(new Rect(r.x, r.yMax - 3f, r.width, 1f),
                new Color(ink.r, ink.g, ink.b, 0.40f));
            UiKit.Rect(new Rect(r.x, r.yMax - 5f, 38f, 2f), ink);
            UiKit.Text(new Rect(r.x + 3f, r.y, r.width - 8f, r.height - 6f),
                label, 18f, ink, TextAnchor.MiddleLeft);
        }

        public static void Divider(Rect r, float strength = 0.38f)
        {
            UiKit.Rect(new Rect(r.x, r.y, r.width, 1f),
                new Color(Brass.r, Brass.g, Brass.b, strength));
            UiKit.Rect(new Rect(r.x, r.y + 2f, r.width, 1f),
                new Color(0f, 0f, 0f, strength * 0.6f));
        }

        public static void AccentBar(Rect r, Color color, float amount)
        {
            UiKit.Rect(r, InsetColor);
            float p = Mathf.Clamp01(amount);
            if (p > 0f) UiKit.Rect(new Rect(r.x, r.y, r.width * p, r.height), color);
            UiKit.Rect(new Rect(r.x, r.y, r.width, 1f),
                new Color(BrassLight.r, BrassLight.g, BrassLight.b, 0.20f));
        }

        public static bool Button(Rect r, string label, bool enabled = true,
                                  bool red = false, bool selected = false)
        {
            Texture2D plank = UiKit.Tex(red ? "UI/plank_red" : "UI/plank");
            Color accent = red ? Vermilion : Brass;
            if (plank != null)
            {
                Color old = GUI.color;
                GUI.color = enabled ? Color.white : new Color(0.48f, 0.47f, 0.43f, 0.82f);
                GUI.DrawTexture(r, plank, ScaleMode.StretchToFill);
                GUI.color = old;
            }
            else
            {
                Surface(r, enabled ? 1f : 0.6f, false);
                UiKit.Rect(new Rect(r.x + 5f, r.y + 5f, r.width - 10f, r.height - 10f),
                    red ? new Color(0.35f, 0.06f, 0.04f, 0.9f) : Raised);
            }
            float alpha = selected ? 0.85f + Mathf.Sin(Time.unscaledTime * 3f) * 0.1f : 0.40f;
            if (enabled)
                UiKit.Rect(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 2f),
                    new Color(accent.r, accent.g, accent.b, alpha));
            if (selected)
                UiKit.Rect(new Rect(r.x + 8f, r.yMax - 4f, r.width - 16f, 2f),
                    new Color(BrassLight.r, BrassLight.g, BrassLight.b, 0.70f));
            UiKit.Text(r, label, Mathf.Min(17f, r.height * 0.43f),
                enabled ? Parchment : new Color(0.60f, 0.57f, 0.51f), TextAnchor.MiddleCenter);
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ---------------- единый стиль (UI/Kit): чёрный лак, потёртое золото, кроваво-красный лак ----------------
        // Главное меню остаётся на прежних Button/Surface — его не трогаем.

        /// <summary>Кнопка в едином стиле: чёрная (или кроваво-красная — главная), золотая окантовка с шипами.</summary>
        public static bool KitButton(Rect r, string label, bool enabled = true, bool red = false, bool selected = false)
        {
            bool square = r.width < r.height * 1.7f;
            Texture2D t = UiKit.Tex(square ? "UI/Kit/ui_btn_square" : !enabled ? "UI/Kit/ui_btn_disabled" : red ? "UI/Kit/ui_btn_red" : "UI/Kit/ui_btn");
            if (t == null) return Button(r, label, enabled, red, selected);
            bool down = enabled && Event.current != null && r.Contains(Event.current.mousePosition) && GUIUtility.hotControl != 0;
            if (down && !square) { Texture2D pt = UiKit.Tex(red ? "UI/Kit/ui_btn_red_pressed" : "UI/Kit/ui_btn_pressed"); if (pt != null) t = pt; }
            // кнопка чуть выше своего прямоугольника: шипы и цветки по краям выходят за него, текст — в середине
            Rect vr = new Rect(r.x - r.height * 0.10f, r.y - r.height * 0.06f, r.width + r.height * 0.20f, r.height * 1.12f);   // шипы почти не выходят — соседние кнопки не наезжают
            Color tint = !enabled && square ? new Color(0.5f, 0.48f, 0.45f, 0.85f) : Color.white;
            if (square) GUI.DrawTexture(vr, t, ScaleMode.StretchToFill, true, 0f, tint, 0f, 0f);
            else UiKit.Slice(vr, t, vr.height / t.height, 0.14f, 0.12f, 0.5f, 0f, tint);
            if (selected) UiKit.Rect(new Rect(r.x + r.width * 0.2f, r.yMax - 3f, r.width * 0.6f, 2f), new Color(BrassLight.r, BrassLight.g, BrassLight.b, 0.6f + 0.3f * Mathf.Sin(Time.unscaledTime * 3f)));
            UiKit.Text(r, label, Mathf.Min(17f, r.height * 0.42f), enabled ? Parchment : new Color(0.60f, 0.57f, 0.51f), TextAnchor.MiddleCenter);
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>Панель в едином стиле: низкая — плашка-тост, высокая — окно с хищными углами.</summary>
        public static void KitSurface(Rect r, float alpha = 1f, bool ornaments = true)
        {
            bool low = r.height < 110f;
            Texture2D t = UiKit.Tex(low ? "UI/Kit/ui_toast" : "UI/Kit/ui_panel");
            if (t == null) { Surface(r, alpha, ornaments); return; }
            Color c = new Color(1f, 1f, 1f, alpha);
            if (low)
            {
                Rect vr = new Rect(r.x - 34f, r.y - 13f, r.width + 68f, r.height + 26f);   // концы и золотые кромки — за краями текста (по высоте кромка ~22%)
                UiKit.Slice(vr, t, Mathf.Min(1f, vr.height / t.height), 0.12f, 0.1f, 0.35f, 0f, c);
            }
            else
            {
                // рамка чуть шире прямоугольника: шипы углов наружу, содержимое — на тёмной середине
                // углы мельче (k ≤ 0,4) и рамка на 16 наружу — орнамент углов не заходит на заголовки карточек
                Rect vr = new Rect(r.x - 16f, r.y - 16f, r.width + 32f, r.height + 32f);
                float k = Mathf.Clamp(Mathf.Min(vr.width / t.width, vr.height / t.height) * 0.6f, 0.26f, 0.34f);
                // середина — цельная доска (без повторов: повтор лака давал полосатый узор), поверх — только рамка с прозрачной серединой
                Texture2D board = UiKit.Tex("UI/Kit/ui_board"), frame = UiKit.Tex("UI/Kit/ui_panel_frame");
                if (board != null && frame != null)
                {
                    float bd = 30f * k;
                    Color o = GUI.color; GUI.color = c;
                    GUI.DrawTexture(new Rect(vr.x + bd, vr.y + bd, vr.width - 2f * bd, vr.height - 2f * bd), board, ScaleMode.StretchToFill);
                    GUI.color = o;
                    UiKit.Slice(vr, frame, k, 0.2f, 0.1f, 0.3f, 0.12f, c);
                }
                else UiKit.Slice(vr, t, k, 0.2f, 0.1f, 0.3f, 0.12f, c);
            }
        }

        /// <summary>Фон экрана снаряжения: оружейная самурая в золотой рамке (UI/Kit/ui_bg_armory) — цельная картина, без нарезки.</summary>
        public static void KitArmory(Rect r, float alpha = 1f)
        {
            Texture2D t = UiKit.Tex("UI/Kit/ui_bg_armory");
            if (t == null) { KitSurface(r, alpha); return; }
            Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f), t, ScaleMode.StretchToFill);
            GUI.color = o;
        }

        /// <summary>Высокая боковая панель с гербом (карта похода).</summary>
        public static void KitSidePanel(Rect r, float alpha = 1f)
        {
            // высокая панель из атласа целиком, без нарезки (нарезка повторяла фактуру — выходил узор-«паттерн»).
            // Верх с гербом уходит за край экрана, чтобы не лечь на заголовок «ПОХОД»: видны боковые кромки, низ с шипами и цельная доска.
            Texture2D t = UiKit.Tex("UI/Kit/ui_sidepanel");
            if (t == null) { Surface(r, alpha, false); return; }
            float top = 0.41f;    // доля высоты текстуры с гербом, лентой и кистями (кисти ложились на текст панели)
            float w = r.width + 12f, h = (r.height + 4f) / (1f - top);
            Color o = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(r.x - 6f, r.yMax + 2f - h, w, h), t, ScaleMode.StretchToFill);
            GUI.color = o;
        }

        public static void Portrait(Rect r, string resourcePath, Color? tint = null,
            bool topCrop = false)
        {
            Texture2D t = UiKit.Tex(resourcePath);
            Inset(r, Brass);
            if (t != null)
            {
                Color old = GUI.color;
                GUI.color = tint ?? Color.white;
                Rect imageRect = new Rect(r.x + 3f, r.y + 3f,
                    r.width - 6f, r.height - 6f);
                if (topCrop)
                {
                    // Show the COMPLETE approved character/difficulty artwork.
                    // Cropping the tall paintings to a wide viewport used to
                    // cut off feet, weapons and decorative frames.
                    GUI.DrawTexture(imageRect, t, ScaleMode.ScaleToFit);
                }
                else GUI.DrawTexture(imageRect, t, ScaleMode.ScaleAndCrop);
                GUI.color = old;
            }
            UiKit.Rect(new Rect(r.x + 5f, r.y + 4f, r.width - 10f, 2f),
                new Color(BrassLight.r, BrassLight.g, BrassLight.b, 0.32f));
            UiKit.Rect(new Rect(r.xMax - 3f, r.y + 5f, 2f, r.height - 10f),
                new Color(Brass.r, Brass.g, Brass.b, 0.45f));
            UiKit.Rect(new Rect(r.x + 5f, r.yMax - 16f, r.width - 10f, 11f),
                new Color(0.03f, 0.02f, 0.02f, 0.38f));
        }
    }
}
