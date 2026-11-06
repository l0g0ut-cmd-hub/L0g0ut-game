using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Эффекты и звуки интерфейса (IMGUI): покадровые листы CGHEVEN во всех кадрах исходного видео (25 к/с),
    /// соседние кадры перетекают друг в друга. Листы — UI/fx_ui_* (прозрачность из яркости, без чёрного фона):
    ///   flash_gold / flash_white / flash_red — вспышка духа (energy burst 11/12/09), 36 кадров 6×6;
    ///   ring — расходящееся кольцо (magical shockwave 01, обесцвечено — красится), 40 кадров 8×5;
    ///   aura_blue / aura_red / aura_violet — пламя духа (magic aura 01/04/05), петли 133 / 104 / 107 кадров.
    /// Звуки — UI/Audio ui_* (Kenney CC0 и синтез): щелчок, клинок из ножен, ткань, кожа, металл, свиток, колокольчик, тайко.
    /// </summary>
    public static class UiFx
    {
        public struct Sheet
        {
            public string tex; public int cols, rows, frames; public float fps; public bool loop;
            public Sheet(string t, int c, int r, int f, bool l) { tex = t; cols = c; rows = r; frames = f; fps = 25f; loop = l; }
            public float Length { get { return frames / fps; } }
        }
        public static readonly Sheet FlashGold = new Sheet("UI/fx_ui_flash_gold", 6, 6, 36, false);
        public static readonly Sheet FlashWhite = new Sheet("UI/fx_ui_flash_white", 6, 6, 36, false);
        public static readonly Sheet FlashRed = new Sheet("UI/fx_ui_flash_red", 6, 6, 36, false);
        public static readonly Sheet Ring = new Sheet("UI/fx_ui_ring", 8, 5, 40, false);
        public static readonly Sheet AuraBlue = new Sheet("UI/fx_ui_aura_blue", 12, 12, 133, true);
        public static readonly Sheet AuraRed = new Sheet("UI/fx_ui_aura_red", 12, 9, 104, true);
        public static readonly Sheet AuraViolet = new Sheet("UI/fx_ui_aura_violet", 12, 9, 107, true);

        /// <summary>
        /// Кадр эффекта через age секунд после начала. Вспышка к концу дополнительно гаснет (fadeOut — доля длины).
        /// Возвращает false, когда одноразовый эффект закончился.
        /// </summary>
        public static bool Draw(Rect r, Sheet s, float age, Color tint, float fadeOut = 0.35f, bool additive = true)
        {
            if (age < 0f) return true;
            if (!s.loop && age >= s.Length) return false;
            if (Event.current == null || Event.current.type != EventType.Repaint) return true;
            Texture2D tex = UiKit.Tex(s.tex);
            if (tex == null) return true;
            float f = age * s.fps;
            int a = Mathf.FloorToInt(f), b = a + 1;
            float k = f - a;
            if (s.loop) { a %= s.frames; b %= s.frames; }
            else { a = Mathf.Min(a, s.frames - 1); b = Mathf.Min(b, s.frames - 1); }
            float fade = s.loop ? 1f : Mathf.Clamp01((s.Length - age) / Mathf.Max(0.05f, s.Length * fadeOut));
            Material add = additive ? AddMat() : null;
            if (add != null)
            {
                // свет складывается с картинкой (как огонь и сияние), а не закрывает её: кадры a и b — с весами 1−k и k
                Color ca = tint * 0.5f; ca.a = tint.a * fade * 0.5f * (1f - k);
                Graphics.DrawTexture(r, tex, Cell(s, a), 0, 0, 0, 0, ca, add);
                if (k > 0.01f && b != a) { Color cb = tint * 0.5f; cb.a = tint.a * fade * 0.5f * k; Graphics.DrawTexture(r, tex, Cell(s, b), 0, 0, 0, 0, cb, add); }
                return true;
            }
            Color oc = GUI.color;
            GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * fade);
            GUI.DrawTextureWithTexCoords(r, tex, Cell(s, a));
            if (k > 0.01f && b != a)
            {
                GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * fade * k);
                GUI.DrawTextureWithTexCoords(r, tex, Cell(s, b));
            }
            GUI.color = oc;
            return true;
        }

        static Material addMat; static bool addTried;
        /// <summary>Сложение света (тот же шейдер, что у боевых эффектов FxSprite); _TintColor = 1 — яркость задаёт цвет вершин (×0,5).</summary>
        static Material AddMat()
        {
            if (addMat != null || addTried) return addMat;
            addTried = true;
            Shader sh = Shader.Find("Legacy Shaders/Particles/Additive");
            if (sh == null) return null;
            addMat = new Material(sh) { name = "UiFxAdditive", hideFlags = HideFlags.DontSave };
            if (addMat.HasProperty("_TintColor")) addMat.SetColor("_TintColor", Color.white);
            return addMat;
        }
        static Rect Cell(Sheet s, int k)
        {
            int c = k % s.cols, rw = k / s.cols;
            return new Rect(c / (float)s.cols, 1f - (rw + 1) / (float)s.rows, 1f / s.cols, 1f / s.rows);
        }
        /// <summary>Квадрат с центром c и стороной size.</summary>
        public static Rect Around(Vector2 c, float size) { return new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size); }

        /// <summary>«Отскок» при выборе: 1 → 1+amp → 1 за ~0,35 с (затухающая пружина).</summary>
        public static float Pop(float age, float amp = 0.12f)
        {
            if (age < 0f || age > 0.6f) return 1f;
            return 1f + amp * Mathf.Sin(age * 18f) * Mathf.Exp(-age * 7f);
        }

        // ---------------------------------------------------------------- вспышка нажатой кнопки

        struct Press { public Rect r; public float at; }
        static readonly List<Press> presses = new List<Press>();
        static int pressFrame = -1;

        /// <summary>Кнопка нажата: щелчок и золотая вспышка по ней (рисуется DrawPress в следующих кадрах).</summary>
        public static void Pressed(Rect r, bool sound = true)
        {
            presses.Add(new Press { r = r, at = Time.unscaledTime });
            if (presses.Count > 12) presses.RemoveAt(0);
            if (sound) Click();
        }
        /// <summary>Золотое свечение по нажатой кнопке (0,35 с); вызывается после её отрисовки.</summary>
        public static void DrawPress(Rect r)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (Time.frameCount != pressFrame)
            {
                pressFrame = Time.frameCount;
                presses.RemoveAll(p => Time.unscaledTime - p.at > 0.5f);
            }
            for (int i = 0; i < presses.Count; i++)
            {
                Press p = presses[i];
                if (Mathf.Abs(p.r.x - r.x) > 0.5f || Mathf.Abs(p.r.y - r.y) > 0.5f || Mathf.Abs(p.r.width - r.width) > 0.5f) continue;
                float age = Time.unscaledTime - p.at, k = 1f - age / 0.4f;
                if (k <= 0f) continue;
                Texture2D glow = UiKit.Tex("FX/glow_pool");
                Color oc = GUI.color;
                if (glow != null)
                {
                    float grow = 1f + 0.25f * (1f - k);
                    Rect g = new Rect(r.center.x - r.width * 0.62f * grow, r.center.y - r.height * 0.95f * grow, r.width * 1.24f * grow, r.height * 1.9f * grow);
                    GUI.color = new Color(1f, 0.78f, 0.38f, 0.55f * k * k);
                    GUI.DrawTexture(g, glow);
                }
                // тонкая кромка света вдоль кнопки
                GUI.color = new Color(1f, 0.9f, 0.6f, 0.7f * k);
                GUI.DrawTexture(new Rect(r.x + r.width * 0.08f, r.y + 1f, r.width * 0.84f, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x + r.width * 0.08f, r.yMax - 3f, r.width * 0.84f, 2f), Texture2D.whiteTexture);
                GUI.color = oc;
            }
        }

        // ---------------------------------------------------------------- звуки

        static AudioSource src;
        static readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        static AudioClip[] Set(string name)
        {
            AudioClip[] c;
            if (clips.TryGetValue(name, out c)) return c;
            List<AudioClip> l = new List<AudioClip>();
            AudioClip one = Resources.Load<AudioClip>("Audio/" + name);
            if (one != null) l.Add(one);
            for (int i = 1; i <= 6; i++) { AudioClip v = Resources.Load<AudioClip>("Audio/" + name + "_" + i); if (v != null) l.Add(v); }
            c = l.ToArray(); clips[name] = c; return c;
        }
        public static void Play(string name, float vol = 0.8f, float pitchJitter = 0.06f)
        {
            AudioClip[] set = Set(name);
            if (set.Length == 0) return;
            if (src == null)
            {
                GameObject g = new GameObject("UiSfx");
                Object.DontDestroyOnLoad(g);
                src = g.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 0f; src.reverbZoneMix = 0f;
            }
            src.mute = !SoundSettings.GameOn;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.PlayOneShot(set[Random.Range(0, set.Length)], vol);
        }
        public static void Click() { Play("ui_click", 0.55f, 0.08f); }
        /// <summary>Звук надевания по ячейке: клинок — из ножен, цуба и тыква — металл, облачение — ткань, маска и амулет — кожа, свиток — шелест.</summary>
        public static void EquipSound(int slot)
        {
            switch (slot)
            {
                case (int)Slot.Blade: Play("ui_draw", 0.9f, 0.04f); break;
                case (int)Slot.Guard: Play("ui_metal", 0.85f); break;
                case (int)Slot.Garb: Play("ui_cloth", 0.9f); break;
                case (int)Slot.Mask: case (int)Slot.Charm: Play("ui_leather", 0.9f); break;
                case (int)Slot.Gourd: Play("ui_metal", 0.6f, 0.15f); break;
                default: Play("ui_scroll", 0.8f); break;
            }
        }
    }
}
