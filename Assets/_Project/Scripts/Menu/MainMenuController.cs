using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace BladePath
{
    /// <summary>
    /// Главное меню = готовый ролик 852x480 (панель с пунктами запечена в видео) + невидимые зоны нажатия
    /// поверх пунктов. Кадр вписывается целиком (без обрезки), по бокам чёрные поля.
    /// Пока видео готовится, показывается первый кадр; после конца ролика — последний.
    /// Видео лежит в StreamingAssets (на Android читается напрямую из APK).
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        public Texture2D posterFirst;
        public Texture2D posterLast;
        public string videoFileName = "menu_bg.mp4";

        const float CW = 852f, CH = 480f;                       // кадр ролика
        const float HitX = 50f, HitW = 172f, HitH = 38f;        // зоны пунктов (в координатах кадра)
        static readonly float[] HitCy = new float[] { 225f, 269f, 313f, 356f };   // New Game, Continue, Settings, Exit

        VideoPlayer player;
        RenderTexture rt;
        bool hasFrame;           // видео уже отдало хотя бы один кадр в RenderTexture
        bool videoFailed;        // видео не удалось воспроизвести — тогда показываем постер
        bool videoEnded;
        bool animated = true;
        bool settingsOpen;
        string toast = "";
        float toastUntil;
        GUIStyle hitStyle, boxStyle, labelStyle;

        void Start()
        {
            rt = new RenderTexture(852, 480, 0, RenderTextureFormat.ARGB32);
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.url = Application.streamingAssetsPath + "/" + videoFileName;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = rt;
            player.isLooping = false;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.prepareCompleted += OnPrepared;
            player.loopPointReached += OnEnded;
            player.errorReceived += OnError;
            player.sendFrameReadyEvents = true;
            player.frameReady += OnFrameReady;
            player.Prepare();
        }

        void OnPrepared(VideoPlayer vp) { if (animated) vp.Play(); }
        void OnFrameReady(VideoPlayer vp, long frameIdx) { hasFrame = true; }
        // После конца ролика НИЧЕГО не подменяем: RenderTexture остаётся на последнем кадре видео.
        // (Раньше здесь подставлялась отдельная JPEG-картинка — она отличалась от реального кадра и при подмене «прыгала».)
        void OnEnded(VideoPlayer vp) { videoEnded = true; }
        void OnError(VideoPlayer vp, string message) { Debug.LogWarning("Меню: видео не воспроизводится: " + message); videoFailed = true; }

        void OnDestroy() { if (rt != null) rt.Release(); }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) QuitGame();
#endif
        }

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void SetAnimated(bool on)
        {
            animated = on;
            if (player == null) return;
            if (on)
            {
                if (player.isPrepared)
                {
                    if (videoEnded) { player.time = 0.0; videoEnded = false; }
                    player.Play();
                }
            }
            else { player.Pause(); }   // кадр замирает там, где был: подмены на постер нет
        }

        void Say(string msg) { toast = msg; toastUntil = Time.time + 2.5f; }

        Rect VideoRect()
        {
            float s = Mathf.Min(Screen.width / CW, Screen.height / CH);
            float w = CW * s, h = CH * s;
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        void OnGUI()
        {
            if (hitStyle == null)
            {
                hitStyle = new GUIStyle();
                hitStyle.active.background = Texture2D.whiteTexture;   // отклик при нажатии (тонируется GUI.backgroundColor)
                boxStyle = new GUIStyle(GUI.skin.box);
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.alignment = TextAnchor.MiddleCenter;
                labelStyle.normal.textColor = Color.white;
            }

            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.blackTexture);

            Rect vr = VideoRect();
            // Видеокадр, как только он есть, показывается всегда (и во время ролика, и после, и на паузе).
            // Постер — только пока видео ещё не отдало первый кадр или если оно не запустилось.
            Texture tex;
            if (hasFrame && !videoFailed) tex = rt;
            else if ((videoFailed || !animated) && posterLast != null) tex = posterLast;
            else tex = posterFirst != null ? (Texture)posterFirst : Texture2D.blackTexture;
            GUI.DrawTexture(vr, tex, ScaleMode.StretchToFill, false);

            if (settingsOpen) { DrawSettings(); return; }

            float k = vr.width / CW;
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 1f, 1f, 0.22f);
            for (int i = 0; i < HitCy.Length; i++)
            {
                Rect r = new Rect(vr.x + HitX * k, vr.y + (HitCy[i] - HitH * 0.5f) * k, HitW * k, HitH * k);
                if (GUI.Button(r, GUIContent.none, hitStyle)) OnItem(i);
            }
            GUI.backgroundColor = oldBg;

            if (Time.time < toastUntil)
            {
                labelStyle.fontSize = Mathf.RoundToInt(Mathf.Max(14f, Screen.height * 0.035f));
                GUI.Box(new Rect(Screen.width * 0.5f - 260f, Screen.height - 90f, 520f, 56f), toast, boxStyle);
            }
        }

        void OnItem(int index)
        {
            switch (index)
            {
                case 0: SceneManager.LoadScene(SceneNames.Prototype); break;
                case 1: Say("Continue: сохранений на этапе 1 нет"); break;
                case 2: settingsOpen = true; break;
                default: QuitGame(); break;
            }
        }

        void DrawSettings()
        {
            float w = Mathf.Min(Screen.width * 0.7f, 520f), h = 180f;
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(box, "Settings", boxStyle);
            bool on = GUI.Toggle(new Rect(box.x + 24f, box.y + 48f, w - 48f, 40f), animated, "  Анимированный фон");
            if (on != animated) SetAnimated(on);
            if (GUI.Button(new Rect(box.x + w * 0.5f - 70f, box.y + h - 56f, 140f, 40f), "OK")) settingsOpen = false;
        }
    }
}
