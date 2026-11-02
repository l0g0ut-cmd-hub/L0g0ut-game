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
            string crash = Breadcrumbs.TakeReport();
            if (crash != null)
            {
                crashLog = crash + "\n\n--- logcat ---\n" + Breadcrumbs.ReadLogcat();
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.persistentDataPath, "crash_report.txt"), crashLog); } catch { }
            }
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

        // Видео декодируется в своём потоке и пишет в RenderTexture. Освобождать её, пока плеер работает, нельзя —
        // на Android это роняет приложение при переходе в бой. Сначала стоп и отвязка, потом освобождение.
        void StopVideo()
        {
            if (player != null)
            {
                player.prepareCompleted -= OnPrepared;
                player.loopPointReached -= OnEnded;
                player.errorReceived -= OnError;
                player.frameReady -= OnFrameReady;
                player.sendFrameReadyEvents = false;
                player.Stop();
                player.targetTexture = null;
            }
        }

        void OnDestroy()
        {
            StopVideo();
            if (rt != null) { rt.Release(); rt = null; }
        }

        bool loading;
        /// <summary>Переход в бой: остановить видео, дать кадр на то, чтобы декодер отпустил текстуру, затем грузить сцену.</summary>
        System.Collections.IEnumerator LoadBattle()
        {
            if (loading) yield break;
            loading = true;
            Breadcrumbs.Begin("меню: остановка видео");
            StopVideo();
            yield return null;
            yield return null;
            Breadcrumbs.Mark("загрузка сцены боя");
            SceneManager.LoadScene(SceneNames.Prototype);
        }

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

            if (crashLog != null) { DrawCrash(); return; }
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
                boxStyle.wordWrap = true; boxStyle.fontSize = Mathf.RoundToInt(Mathf.Max(12f, Screen.height * 0.025f));
                GUI.Box(new Rect(Screen.width * 0.5f - Screen.width * 0.4f, Screen.height - Screen.height * 0.3f, Screen.width * 0.8f, Screen.height * 0.25f), toast, boxStyle);
            }
        }

        void OnItem(int index)
        {
            switch (index)
            {
                case 0: Ladder.Index = 0; StartCoroutine(LoadBattle()); break;   // новый «Путь» с первого противника
                case 1:
                    if (Ladder.Index > 0 || Records.Wins > 0 || Records.Losses > 0) StartCoroutine(LoadBattle());   // продолжить с текущей ступени
                    else Say("Сохранённого пути нет — начните новую игру");
                    break;
                case 2: settingsOpen = true; break;
                default: QuitGame(); break;
            }
        }

        string crashLog;
        Vector2 crashScroll;
        bool copied;

        /// <summary>Экран отчёта о вылете: текст лога с прокруткой, кнопки «Скопировать» и «Закрыть».</summary>
        void DrawCrash()
        {
            float W = Screen.width, H = Screen.height;
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.DrawTexture(new Rect(0f, 0f, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUIStyle st = new GUIStyle(GUI.skin.label);
            st.fontSize = Mathf.RoundToInt(Mathf.Max(11f, H * 0.022f));
            st.wordWrap = true;
            st.normal.textColor = new Color(0.9f, 0.9f, 0.85f);
            float bh = H * 0.1f;
            Rect view = new Rect(W * 0.03f, H * 0.03f, W * 0.94f, H * 0.94f - bh - 10f);
            float ch = st.CalcHeight(new GUIContent(crashLog), view.width - 30f);
            crashScroll = GUI.BeginScrollView(view, crashScroll, new Rect(0f, 0f, view.width - 30f, ch));
            GUI.Label(new Rect(0f, 0f, view.width - 30f, ch), crashLog, st);
            GUI.EndScrollView();
            GUIStyle bs = new GUIStyle(GUI.skin.button);
            bs.fontSize = Mathf.RoundToInt(H * 0.035f);
            if (GUI.Button(new Rect(W * 0.03f, H - bh - H * 0.02f, W * 0.45f, bh), copied ? "СКОПИРОВАНО" : "СКОПИРОВАТЬ ЛОГ", bs)) { GUIUtility.systemCopyBuffer = crashLog; copied = true; }
            if (GUI.Button(new Rect(W * 0.52f, H - bh - H * 0.02f, W * 0.45f, bh), "ЗАКРЫТЬ", bs)) crashLog = null;
            // прокрутка пальцем
            if (Event.current.type == EventType.MouseDrag) { crashScroll.y -= Event.current.delta.y; Event.current.Use(); }
        }

        void DrawSettings()
        {
            Matrix4x4 old = UiKit.Begin();
            float W = UiKit.Width, cx = W * 0.5f;
            UiKit.Rect(new Rect(0f, 0f, W, 540f), new Color(0f, 0f, 0f, 0.6f));
            Rect box = new Rect(cx - 220f, 46f, 440f, 452f);
            Color gold = new Color(0.86f, 0.70f, 0.38f, 1f);
            UiKit.Rect(new Rect(box.x - 2f, box.y - 2f, box.width + 4f, box.height + 4f), gold * 0.8f);
            UiKit.Rect(box, new Color(0.06f, 0.05f, 0.05f, 0.97f));
            UiKit.Paper(box, new Color(0.2f, 0.16f, 0.12f, 0.55f));   // фактура старой бумаги поверх тёмной подложки
            UiKit.Corners(box, 58f);   // резные уголки чёрного лака с золотом
            Texture2D br = UiKit.Tex("UI/sakura_branch");
            if (br != null) { Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.95f); GUI.DrawTextureWithTexCoords(new Rect(box.xMax - 150f, box.y - 34f, 190f, 190f), br, new Rect(1f, 0f, -1f, 1f)); GUI.color = oc; }   // ветка сакуры с правого угла
            UiKit.Text(new Rect(box.x, box.y + 10f, box.width, 44f), "НАСТРОЙКИ", 32f, gold);

            string[] labels =
            {
                "АНИМИРОВАННЫЙ ФОН: " + (animated ? "ВКЛ" : "ВЫКЛ"),
                "СЛОЖНОСТЬ: " + EnemyAI.DifficultyName,
                "ГРАФИКА: " + (DuelManager.Gfx == 1 ? "ВЫСОКАЯ" : "ЭКОНОМ"),
                "ВИБРАЦИЯ: " + (PlayerPrefs.GetInt("haptics", 0) == 1 ? "ВКЛ" : "ВЫКЛ"),
                "КАМЕРА: " + (CameraOrbit.FreeMode ? "СВОБОДНАЯ" : "БОЕВАЯ"),
                "ТАЙМЕР БОЯ: " + (DuelManager.TimerOn ? "ВКЛ" : "ВЫКЛ"),
                "ГОТОВО"
            };
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 1f, 1f, 0.2f);
            for (int i = 0; i < labels.Length; i++)
            {
                Rect r = new Rect(box.x + 40f, box.y + 62f + i * 54f, box.width - 80f, 44f);
                UiKit.Rect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), gold * 0.7f);
                UiKit.Rect(r, i == labels.Length - 1 ? new Color(0.35f, 0.08f, 0.06f, 1f) : new Color(0.1f, 0.08f, 0.08f, 1f));
                UiKit.Text(r, labels[i], 18f, new Color(0.95f, 0.9f, 0.78f));
                if (GUI.Button(r, GUIContent.none, hitStyle))
                {
                    switch (i)
                    {
                        case 0: SetAnimated(!animated); break;
                        case 1: EnemyAI.Difficulty = (EnemyAI.Difficulty + 1) % 3; break;
                        case 2: DuelManager.Gfx = 1 - DuelManager.Gfx; break;
                        case 3: PlayerPrefs.SetInt("haptics", PlayerPrefs.GetInt("haptics", 0) == 1 ? 0 : 1); Haptics.Enabled = PlayerPrefs.GetInt("haptics", 0) == 1; Haptics.Pulse(40); break;
                        case 4: CameraOrbit.FreeMode = !CameraOrbit.FreeMode; break;
                        case 5: DuelManager.TimerOn = !DuelManager.TimerOn; break;
                        default: settingsOpen = false; break;
                    }
                }
            }
            GUI.backgroundColor = oldBg;
            UiKit.Text(new Rect(box.x, box.yMax + 8f, box.width, 24f),
                "ПУТЬ " + (Ladder.Index + 1) + "/" + Ladder.Count + "  ·  ПОБЕД " + Records.Wins + "  ·  ПОРАЖЕНИЙ " + Records.Losses, 14f, new Color(0.85f, 0.8f, 0.7f, 0.9f));
            GUI.matrix = old;
        }
    }
}
