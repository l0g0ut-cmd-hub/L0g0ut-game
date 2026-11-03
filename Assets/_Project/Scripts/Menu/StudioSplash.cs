using UnityEngine;
using UnityEngine.SceneManagement;

namespace BladePath
{
    /// <summary>
    /// Заставка студии «HELLFIRE DOJO presents» (UI/splash_studio) — один раз при запуске игры, сразу после заставки Unity:
    /// чёрный экран → картина проявляется → держится ~2,3 с (огонь медальона и разрез под надписью чуть дышат) → плавно гаснет.
    /// Внизу мелко — версия и номер сборки (Resources/build_number, пишет сборка в облаке).
    /// Пока висит заставка, главное меню выключено и его ролик с самураем не начинается — он стартует с начала после неё.
    /// Касание экрана — пропустить. Главное меню при этом не меняется.
    /// </summary>
    public class StudioSplash : MonoBehaviour
    {
        const float FadeIn = 0.6f, Hold = 2.3f, FadeOut = 0.8f;
        static bool shown;
        Texture2D art;
        float start;
        Behaviour menu;
        string version;
        GUIStyle verStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (shown || SceneManager.GetActiveScene().name != SceneNames.MainMenu) return;
            Texture2D tex = Resources.Load<Texture2D>("UI/splash_studio");
            if (tex == null) return;
            shown = true;
            GameObject go = new GameObject("StudioSplash");
            StudioSplash s = go.AddComponent<StudioSplash>();
            s.art = tex;
            // меню ждёт: Start ещё не вызывался (AfterSceneLoad — после Awake, до Start), ролик начнётся после заставки
            MainMenuController mm = FindFirstObjectByType<MainMenuController>();
            if (mm != null) { mm.enabled = false; s.menu = mm; }
        }

        void Start()
        {
            useGUILayout = false;
            start = Time.unscaledTime;
            TextAsset bn = Resources.Load<TextAsset>("build_number");
            string num = bn != null ? bn.text.Trim() : "";
            version = "ПУТЬ КЛИНКА  ·  версия " + Application.version + (num.Length > 0 ? "  ·  сборка #" + num : "");
        }

        void Update()
        {
            float t = Time.unscaledTime - start;
#if ENABLE_LEGACY_INPUT_MANAGER
            bool skip = t > 0.4f && (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began || Input.GetMouseButtonDown(0));
            if (skip && t < FadeIn + Hold) { start = Time.unscaledTime - (FadeIn + Hold); t = FadeIn + Hold; }
#endif
            // меню включается в начале угасания: ролик успевает подготовиться и проступает сквозь гаснущую заставку
            if (t >= FadeIn + Hold && menu != null && !menu.enabled) menu.enabled = true;
            if (t >= FadeIn + Hold + FadeOut) Finish();
        }

        void Finish()
        {
            if (menu != null) menu.enabled = true;
            Destroy(gameObject);
        }

        void OnGUI()
        {
            GUI.depth = -1000;
            if (Event.current.type != EventType.Repaint) return;
            float t = Time.unscaledTime - start;
            float a = t < FadeIn ? Mathf.SmoothStep(0f, 1f, t / FadeIn) : t < FadeIn + Hold ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - FadeIn - Hold) / FadeOut);
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            Color oc = GUI.color;
            // фон держится чёрным до конца проявления меню: гаснет только картина, затем — сам чёрный экран
            GUI.color = new Color(0f, 0f, 0f, t < FadeIn + Hold ? 1f : a);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            if (art != null)
            {
                float s = Mathf.Min(Screen.width / (float)art.width, Screen.height / (float)art.height);
                float zoom = 1f + 0.025f * Mathf.Clamp01(t / (FadeIn + Hold + FadeOut));   // медленный наезд
                float w = art.width * s * zoom, h = art.height * s * zoom;
                Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(r, art, ScaleMode.StretchToFill);
                // жар: огонь медальона и разрез чуть вспыхивают
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.2f) * Mathf.Sin(t * 1.3f + 0.7f);
                GUI.color = new Color(1f, 0.55f, 0.2f, 0.07f * pulse * a);
                GUI.DrawTexture(r, art, ScaleMode.StretchToFill);
            }
            if (verStyle == null)
            {
                verStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(11, Screen.height / 48) };
                verStyle.normal.textColor = new Color(0.7f, 0.62f, 0.55f, 1f);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.75f * a);
            GUI.Label(new Rect(0f, Screen.height * 0.92f, Screen.width, Screen.height * 0.06f), version, verStyle);
            GUI.color = oc;
        }
    }
}
