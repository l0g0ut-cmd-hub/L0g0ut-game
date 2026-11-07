using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Время суток на карте похода: утро → день → вечер → ночь. Идёт шагами по дороге: каждые StepsPerPhase переходов —
    /// следующая четверть суток (хранится в PlayerPrefs, переживает смену области и выход из игры).
    /// Смена плавная (~6 с, с плавным разгоном и торможением): цветовой фильтр карты, затемнение по краям, сила света в окнах и фонарях.
    /// Огонь костров и горна горит всегда. Значок — медальон UI/map_daytime (ряд 1 — медальоны, ряд 2 — простые значки).
    /// Время суток в самом бою не меняется (там своё освещение).
    /// </summary>
    public static class DayCycle
    {
        public const int Morning = 0, Day = 1, Evening = 2, Night = 3;
        public const int StepsPerPhase = 2;
        public const string Atlas = "UI/map_daytime";
        public static readonly string[] Names = { "УТРО", "ДЕНЬ", "ВЕЧЕР", "НОЧЬ" };
        static readonly string[] Coming =
        {
            "Светает. Над дорогой стелется утренний туман.",
            "Солнце поднялось высоко — наступил день.",
            "Солнце клонится к закату. Вечереет.",
            "Опустилась ночь. В окнах и фонарях зажигается свет.",
        };

        static int step = -1;
        static float shown = -1f;        // показанная фаза, непрерывно 0…4 (плавный переход, ночь → утро через 4)
        static float lastTick = -1f;

        // ---- режим (меню разработчика на карте и в бою): по пути, одно время суток навсегда или выключено (всегда день)
        public const int Auto = -1, Off = 4;
        public static readonly string[] ModeNames = { "УТРО", "ДЕНЬ", "ВЕЧЕР", "НОЧЬ", "ВСЕГДА ДЕНЬ" };   // «всегда день» — смена выключена
        static int mode = -2;
        public static int Mode
        {
            get { if (mode == -2) mode = Mathf.Clamp(PlayerPrefs.GetInt("day_mode", Auto), Auto, Off); return mode; }
            set { mode = Mathf.Clamp(value, Auto, Off); PlayerPrefs.SetInt("day_mode", mode); PlayerPrefs.Save(); }
        }
        public static string ModeName(bool fight) { return Mode == Auto ? (!fight ? "ПО ПУТИ" : Weather.MapTime >= 0 ? "ПО КАРТЕ" : "СЛУЧАЙНО") : ModeNames[Mode]; }
        /// <summary>По кругу: по пути → утро → день → вечер → ночь → выкл → по пути.</summary>
        public static void NextMode() { Mode = Mode >= Off ? Auto : Mode + 1; Snap(); }
        /// <summary>Сразу показать выбранное время суток, без 6-секундного перетекания (меню разработчика).</summary>
        public static void Snap() { shown = Phase; lastTick = -1f; }
        public static bool Enabled { get { return Mode != Off; } }

        /// <summary>Время суток боя: −1 — случайно, как раньше (бой не с карты, режим «по пути»); иначе 0…3.</summary>
        public static int FightTime(int fromMap)
        {
            int m = Mode;
            if (m == Off) return Day;
            if (m >= 0) return m;
            return fromMap;
        }

        static void Load() { if (step < 0) step = Mathf.Max(0, PlayerPrefs.GetInt("day_step", 0)); }
        public static int Phase
        {
            get
            {
                int m = Mode;
                if (m == Off) return Day;
                if (m >= 0) return m;
                Load(); return (step / StepsPerPhase) % 4;
            }
        }
        /// <summary>Сколько шагов до следующей четверти суток.</summary>
        public static int StepsLeft { get { Load(); return StepsPerPhase - step % StepsPerPhase; } }

        /// <summary>Герой прошёл шаг по дороге. Если наступила новая четверть суток — текст для уведомления, иначе null.</summary>
        public static string Advance()
        {
            Load();
            int was = Phase;
            step++;
            PlayerPrefs.SetInt("day_step", step); PlayerPrefs.Save();
            return Mode == Auto && Phase != was ? Coming[Phase] : null;   // время задано вручную или выключено — без уведомлений
        }

        public static string Hint()
        {
            if (Mode == Off) return "Смена времени суток выключена: всегда день.";
            if (Mode >= 0) return Names[Phase] + " — время суток задано в меню разработчика.";
            int left = StepsLeft;
            return Names[Phase] + ". До " + (Phase == Morning ? "дня" : Phase == Day ? "вечера" : Phase == Evening ? "ночи" : "утра") + " — "
                   + left + (left == 1 ? " шаг" : " шага") + " пути.";
        }

        /// <summary>Плавное движение показанной фазы к настоящей (раз за кадр, из карты).</summary>
        public static void Tick()
        {
            Load();
            float now = Time.unscaledTime;
            if (lastTick == now) return;
            float dt = lastTick < 0f ? 0f : Mathf.Min(0.1f, now - lastTick);
            lastTick = now;
            float target = Phase;
            if (shown < 0f) { shown = target; return; }
            float d = Mathf.Repeat(target - shown, 4f);
            if (d > 2f) d -= 4f;
            shown = Mathf.Repeat(shown + Mathf.Clamp(d, -dt * 0.16f, dt * 0.16f), 4f);   // смена четверти суток — ~6 с плавного перетекания
        }

        // утро, день, вечер, ночь
        static readonly Color[] Tint = { new Color(1f, 0.74f, 0.68f), new Color(1f, 0.95f, 0.85f), new Color(1f, 0.48f, 0.16f), new Color(0.03f, 0.06f, 0.18f) };
        static readonly float[] TintA = { 0.12f, 0f, 0.17f, 0.5f };
        static readonly float[] Vig = { 0.16f, 0f, 0.32f, 0.6f };
        static readonly float[] Light = { 0f, 0f, 0.85f, 1.6f };   // окна и фонари зажигаются вечером и ночью, утром и днём погашены

        static void Mix(out int i0, out int i1, out float f)
        {
            if (shown < 0f) Tick();
            float s = Mathf.Max(0f, shown);
            i0 = Mathf.FloorToInt(s) % 4; i1 = (i0 + 1) % 4; f = s - Mathf.Floor(s);
            f = f * f * (3f - 2f * f);
        }

        /// <summary>Сила света окон и фонарей: утром и днём 0, вечером 0,85, ночью 1,6 (между фазами — плавно).</summary>
        public static float LightK { get { int a, b; float f; Mix(out a, out b, out f); return Mathf.Lerp(Light[a], Light[b], f); } }

        static Texture2D vignette;
        static Texture2D Vignette()
        {
            if (vignette != null) return vignette;
            const int n = 128;
            vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MapVignette" };
            Color32[] px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.Clamp01((d - 0.38f) / 0.62f); a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            vignette.SetPixels32(px); vignette.Apply();
            return vignette;
        }

        /// <summary>Фильтр поверх карты (под значками мест и под светом окон).</summary>
        public static void DrawFilter(Rect view)
        {
            if (Event.current.type != EventType.Repaint) return;
            int a, b; float f; Mix(out a, out b, out f);
            Color oc = GUI.color;
            Color tint = Color.Lerp(Tint[a], Tint[b], f);
            float ta = Mathf.Lerp(TintA[a], TintA[b], f), va = Mathf.Lerp(Vig[a], Vig[b], f);
            if (ta > 0.003f) { GUI.color = new Color(tint.r, tint.g, tint.b, ta); GUI.DrawTexture(view, Texture2D.whiteTexture); }
            if (va > 0.003f)
            {
                // края темнее: ночью — синяя тьма, вечером и утром — мягкая дымка тёплого/холодного тона
                Color vc = Color.Lerp(tint * 0.25f, new Color(0.01f, 0.015f, 0.05f), 0.6f);
                GUI.color = new Color(vc.r, vc.g, vc.b, va);
                GUI.DrawTexture(new Rect(view.x - view.width * 0.06f, view.y - view.height * 0.08f, view.width * 1.12f, view.height * 1.16f), Vignette());
            }
            GUI.color = oc;
        }

        /// <summary>Медальон текущего времени суток; при смене — наплыв со следующим.</summary>
        public static void DrawBadge(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            Texture2D tex = UiKit.Tex(Atlas);
            if (tex == null) return;
            int a, b; float f; Mix(out a, out b, out f);
            Color oc = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(r, tex, Cell(a));
            if (f > 0.002f) { GUI.color = new Color(1f, 1f, 1f, f); GUI.DrawTextureWithTexCoords(r, tex, Cell(b)); }
            GUI.color = oc;
        }
        static Rect Cell(int k) { return new Rect(k * 0.25f, 0.5f, 0.25f, 0.5f); }
    }
}
