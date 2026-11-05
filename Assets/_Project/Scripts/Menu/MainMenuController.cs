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
        int newJourneyStep;    // 0: classic video menu, 1: difficulty, 2: traveller
        int chosenDifficulty = 1, chosenHero;
        string toast = "";
        float toastUntil;
        GUIStyle hitStyle, boxStyle, labelStyle;

        void Start()
        {
            useGUILayout = false;   // GUILayout нигде не используется: без лишнего прохода Layout IMGUI вдвое дешевле
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
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (newJourneyStep > 0) newJourneyStep--;
                else if (settingsOpen) settingsOpen = false;
                else QuitGame();
            }
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

        // BP-051: a restrained CINEMATIC side extension, not bulky battle panels.
        // Texture is procedurally rendered once at native pillar resolution.
        // No changes to video dimensions, video pixels or button hitboxes.
        static Texture2D menuPillarTexture;
        static Texture2D MenuPillarTexture()
        {
            if (menuPillarTexture != null) return menuPillarTexture;
            const int width = 192, height = 1024;
            Texture2D t = new Texture2D(width, height, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            Color32[] p = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / (width - 1);
                    float v = (float)y / (height - 1);
                    float grain = Mathf.PerlinNoise(x * 0.07f + 19f, y * 0.012f) * 0.065f;
                    float cloud = Mathf.PerlinNoise(x * 0.017f + 51f, y * 0.004f + 13f) * 0.085f;
                    float edge = Mathf.Pow(u, 3f) * 0.12f;
                    float accent = Mathf.Exp(-Mathf.Pow((u - 0.975f) * 180f, 2f));
                    float inlay = Mathf.Exp(-Mathf.Pow((u - 0.915f) * 130f, 2f));
                    Color baseColor = new Color(0.035f + grain + cloud * 0.5f,
                        0.031f + grain * 0.65f + cloud * 0.39f,
                        0.061f + grain * 0.76f + cloud * 0.65f, 1f);
                    baseColor = Color.Lerp(baseColor, new Color(0.23f, 0.11f, 0.09f), edge);
                    baseColor = Color.Lerp(baseColor, new Color(0.67f, 0.40f, 0.19f), inlay * 0.56f);
                    baseColor = Color.Lerp(baseColor, new Color(0.92f, 0.63f, 0.30f), accent * 0.7f);
                    p[y * width + x] = baseColor;
                }
            t.SetPixels32(p);
            t.Apply(false, true);
            menuPillarTexture = t;
            return t;
        }

        static void DrawSideBorders(Rect video)
        {
            float left = Mathf.Max(0f, video.x);
            float right = Mathf.Max(0f, Screen.width - video.xMax);
            if (left < 1f && right < 1f) return;
            Texture2D panel = MenuPillarTexture();
            Texture2D blossom = UiKit.Tex("UI/sakura_branch");
            Color saved = GUI.color;
            GUI.color = Color.white;
            if (left > 0f) DrawMenuPillar(new Rect(0f, 0f, left, Screen.height), panel, blossom, false);
            if (right > 0f) DrawMenuPillar(new Rect(video.xMax, 0f, right, Screen.height), panel, blossom, true);
            GUI.color = saved;
        }

        static void DrawMenuPillar(Rect area, Texture2D panel, Texture2D blossom, bool mirror)
        {
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(area, panel,
                mirror ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f));
            // Sparse petals from the existing game art; never obscure the video.
            if (blossom != null && area.width > 45f)
            {
                GUI.color = new Color(1f, 0.8f, 0.78f, 0.17f);
                float bw = area.width * 0.78f;
                float bh = Mathf.Min(area.height * 0.27f, bw * 2.0f);
                GUI.DrawTextureWithTexCoords(new Rect(area.x + (area.width-bw)*0.5f,
                    area.y + area.height*0.075f, bw, bh), blossom,
                    mirror ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f));
            }
            GUI.color = Color.white;
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
            // Add decorative artwork ONLY inside the left/right letterbox bars.
            // The original 852x480 video, painted menu and click targets remain
            // pixel-perfect, untouched and in their original positions.
            DrawSideBorders(vr);

            if (loading && DrawLoading()) return;
            if (crashLog != null) { DrawCrash(); return; }
            if (newJourneyStep > 0) { DrawJourneySelection(); return; }
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
                case 0:
                    chosenDifficulty = EnemyAI.Difficulty;
                    chosenHero = JourneyChoice.Hero;
                    newJourneyStep = 1;
                    break;
                case 1:
                    if (JourneyChoice.CanContinue) StartCoroutine(LoadBattle());
                    else Say("Сохранённого пути нет — начните новую игру");
                    break;
                case 2: settingsOpen = true; break;
                default: QuitGame(); break;
            }
        }

        // Экраны выбора сложности и путника — готовые картины 1916x821 (рамки, надписи и кнопки
        // нарисованы прямо в них) + невидимые зоны нажатия, как в главном меню. Картина заполняет
        // экран целиком (лишнее по краям обрезается: карточки и кнопки лежат в средних 60% ширины).
        // Выбранная карточка подсвечивается золотым свечением card_glow.
        const float SelW = 1916f, SelH = 821f;
        static Rect SelRect(float x0, float y0, float x1, float y1) { return new Rect(x0, y0, x1 - x0, y1 - y0); }
        static readonly Rect[] DifficultyCards =
        {
            SelRect(428f, 118f, 768f, 660f), SelRect(786f, 118f, 1132f, 660f), SelRect(1152f, 118f, 1492f, 660f)
        };
        // New difficulty art is authored at 1824x862 (the traveller keeps 1916x821).
        const float DifficultyW = 1824f, DifficultyH = 862f;
        static readonly Rect[] RedesignDifficultyCards =
        {
            SelRect(355f, 149f, 704f, 654f), SelRect(735f, 149f, 1085f, 654f),
            SelRect(1119f, 149f, 1471f, 654f)
        };
        static readonly Rect DifficultyConfirm = SelRect(424f, 682f, 910f, 778f);
        static readonly Rect DifficultyBack = SelRect(937f, 682f, 1327f, 778f);
        static readonly Rect DifficultyFog = SelRect(1485f, 683f, 1754f, 801f);
        static readonly Rect[] TravellerCards =
        {
            SelRect(385f, 97f, 749f, 670f), SelRect(764f, 97f, 1152f, 670f), SelRect(1167f, 97f, 1531f, 670f)
        };
        static readonly Rect TravellerStart = SelRect(573f, 698f, 945f, 778f);
        static readonly Rect TravellerBack = SelRect(988f, 698f, 1310f, 775f);

        void DrawJourneySelection()
        {
            bool difficulty = newJourneyStep == 1;
            Texture2D screen = UiKit.Tex(difficulty
                ? "UI/selection/screen_difficulty_redesign" : "UI/selection/screen_traveller");
            if (screen == null) { DrawJourneySelectionLegacy(); return; }

            float artworkW = difficulty ? DifficultyW : SelW;
            float artworkH = difficulty ? DifficultyH : SelH;
            float s = Mathf.Max(Screen.width / artworkW, Screen.height / artworkH);
            float ox = (Screen.width - artworkW * s) * 0.5f, oy = (Screen.height - artworkH * s) * 0.5f;
            GUI.DrawTexture(new Rect(ox, oy, artworkW * s, artworkH * s), screen, ScaleMode.StretchToFill, false);
            // BP-053: original painting contains a rectangular BLACK matte
            // outside the carved fog plaque. Correct ONLY those black pixels;
            // all scenery, cards and the painted gold plaque remain unchanged.
            if (difficulty)
            {
                Texture2D matteFix = UiKit.Tex("UI/selection/difficulty_plaque_matte_fix");
                if (matteFix != null)
                {
                    Color previousTint = GUI.color;
                    GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(ox + 1467f * s, oy + 669f * s,
                        305f * s, 147f * s), matteFix, ScaleMode.StretchToFill, true);
                    GUI.color = previousTint;
                }
            }

            Rect[] cards = difficulty ? RedesignDifficultyCards : TravellerCards;
            int chosen = difficulty ? chosenDifficulty : chosenHero;
            // Difficulty art is neutral. The prior separate gold PNG had an
            // inner ornate frame smaller than the card (cropped text/icons).
            // Draw ONLY a thin luminous rim aligned to the painted card edges.
            // Traveller keeps its existing glow and geometry untouched.
            Texture2D glow = difficulty ? null : UiKit.Tex("UI/selection/card_glow");

            for (int i = 0; i < cards.Length; i++)
            {
                Rect r = new Rect(ox + cards[i].x * s, oy + cards[i].y * s, cards[i].width * s, cards[i].height * s);
                if (i == chosen)
                {
                    if (difficulty)
                        DrawDifficultySelectionGlow(r, s);
                    else if (glow != null)
                    {
                        Color prev = GUI.color;
                        GUI.color = new Color(1f, 1f, 1f,
                            0.94f + 0.06f * Mathf.Sin(Time.unscaledTime * 3f));
                        float pad = r.width * 0.09f;
                        GUI.DrawTexture(new Rect(r.x-pad, r.y-pad,
                            r.width+pad*2f, r.height+pad*2f), glow);
                        GUI.color = prev;
                    }
                }
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    if (difficulty) chosenDifficulty = i;
                    else chosenHero = i;
                    Haptics.Pulse(12);
                }
            }

            if (difficulty)
            {
                Rect fr = DifficultyFog;
                DrawFogToggleArt(new Rect(ox + fr.x * s, oy + fr.y * s, fr.width * s, fr.height * s), s);
            }

            Rect ok = difficulty ? DifficultyConfirm : TravellerStart;
            Rect back = difficulty ? DifficultyBack : TravellerBack;
            // The approved screen textures already contain the COMPLETE ornamental
            // buttons (frames, fills and labels). Draw no second button on top:
            // an overlay produces offset double borders and mismatched text.
            bool pressedBack = SelectionHitbox(back, s, ox, oy);
            bool pressedOk = SelectionHitbox(ok, s, ox, oy);
            if (pressedBack) newJourneyStep--;
            else if (pressedOk)
            {
                if (difficulty) newJourneyStep = 2;
                else if (!loading)
                {
                    JourneyChoice.BeginNew(chosenDifficulty, chosenHero);
                    StartCoroutine(LoadBattle());
                }
            }
        }

        // BP-051: a single translucent ornament-shaped halo matched to the
        // actual card edges; no second square frame, text clipping or baked glow.
        static Texture2D difficultySelectionHalo;
        static Texture2D DifficultySelectionHalo()
        {
            if (difficultySelectionHalo != null) return difficultySelectionHalo;
            const int w=180, h=264;
            Vector2[] path = {
                new Vector2(0.075f,0.042f), new Vector2(0.38f,0.042f),
                new Vector2(0.50f,0.010f), new Vector2(0.62f,0.042f),
                new Vector2(0.925f,0.042f), new Vector2(0.975f,0.075f),
                new Vector2(0.975f,0.920f), new Vector2(0.925f,0.955f),
                new Vector2(0.62f,0.955f), new Vector2(0.50f,0.993f),
                new Vector2(0.38f,0.955f), new Vector2(0.075f,0.955f),
                new Vector2(0.025f,0.920f), new Vector2(0.025f,0.075f)
            };
            Texture2D t = new Texture2D(w,h,TextureFormat.RGBA32,false);
            t.wrapMode=TextureWrapMode.Clamp;
            t.filterMode=FilterMode.Bilinear;
            Color[] pixels=new Color[w*h];
            for(int y=0;y<h;y++) for(int x=0;x<w;x++)
            {
                Vector2 p=new Vector2(x,y);
                float nearest=100000f;
                for(int j=0;j<path.Length;j++)
                {
                    Vector2 a=new Vector2(path[j].x*(w-1),path[j].y*(h-1));
                    Vector2 b=new Vector2(path[(j+1)%path.Length].x*(w-1),
                        path[(j+1)%path.Length].y*(h-1));
                    Vector2 delta=b-a;
                    float amount=Mathf.Clamp01(Vector2.Dot(p-a,delta) /
                        Mathf.Max(0.0001f,delta.sqrMagnitude));
                    nearest=Mathf.Min(nearest,Vector2.Distance(p,a+amount*delta));
                }
                float soft=Mathf.Exp(-nearest*nearest/36f)*0.42f;
                float rim=Mathf.Exp(-nearest*nearest/1.8f)*0.58f;
                pixels[y*w+x]=new Color(1f,0.78f+0.17f*rim,0.36f+0.26f*rim,
                    Mathf.Clamp01(soft+rim));
            }
            t.SetPixels(pixels);
            t.Apply(false,true);
            difficultySelectionHalo=t;
            return t;
        }
        static void DrawDifficultySelectionGlow(Rect card, float s)
        {
            Texture2D halo=DifficultySelectionHalo();
            Color old=GUI.color;
            GUI.color=new Color(1f,1f,1f,
                0.92f+0.08f*Mathf.Sin(Time.unscaledTime*3f));
            float margin=6f*s;
            GUI.DrawTexture(new Rect(card.x-margin,card.y-margin,
                card.width+margin*2f,card.height+margin*2f),
                halo,ScaleMode.StretchToFill,true);
            GUI.color=old;
        }

        /// <summary>Clickable area aligned with a button painted into the screen art.
        /// GUIStyle.none draws no pixels in any button state.</summary>
        static bool SelectionHitbox(Rect original, float scale, float ox, float oy)
        {
            Rect r = new Rect(ox + original.x * scale, oy + original.y * scale,
                              original.width * scale, original.height * scale);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // BP-053: approved fog option #1, without the background JPEG matte.
        // Both transparent PNGs have an identical 325x152 canvas and exactly
        // the same ornamental perimeter. Only the cloud and slider INSIDES
        // change. The original gold/wood plaque is painted once in the scene.
        void DrawFogToggleArt(Rect screenRect, float selectionScale)
        {
            bool on = JourneyChoice.FogEnabled;
            Texture2D art = UiKit.Tex(on
                ? "UI/selection/fog_toggle_inner_on"
                : "UI/selection/fog_toggle_inner_off");
            if (art != null)
            {
                Color oldTint = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(screenRect, art, ScaleMode.StretchToFill, true);
                GUI.color = oldTint;
            }
            if (GUI.Button(screenRect, GUIContent.none, GUIStyle.none))
            {
                JourneyChoice.FogEnabled = !on;
                Haptics.Pulse(12);
            }
        }

        /// <summary>
        /// Переключатель «Туман войны» на экране сложности (rect — в пикселях экрана). Выключено по умолчанию:
        /// тогда вся карта похода открыта, без смотровых башен и меток «?».
        /// </summary>
        void DrawFogToggle(Rect screenRect)
        {
            Matrix4x4 old = UiKit.Begin();
            float k = UiKit.Scale;
            Rect r = new Rect(screenRect.x / k, screenRect.y / k, screenRect.width / k, screenRect.height / k);
            bool on = JourneyChoice.FogEnabled;
            string label = on ? "ТУМАН ВКЛ" : "ТУМАН ВЫКЛ";
            if (VisualIdentity.KitButton(r, label, true, false, on)) { JourneyChoice.FogEnabled = !on; Haptics.Pulse(12); }
            GUI.matrix = old;
        }

        /// <summary>Прежний экран выбора (рисуется интерфейсом) — если картины экранов не загрузились.</summary>
        void DrawJourneySelectionLegacy()
        {
            Matrix4x4 old = UiKit.Begin();
            float W = UiKit.Width;
            const float H = 540f;
            Texture2D landscape = UiKit.Tex("UI/map_0");
            if (landscape != null)
                GUI.DrawTexture(new Rect(0f, 0f, W, H), landscape, ScaleMode.ScaleAndCrop);
            UiKit.Rect(new Rect(0f, 0f, W, H), new Color(0.015f, 0.013f, 0.027f, 0.66f));
            float boardW = Mathf.Min(W - 24f, 1020f);
            float boardX = (W - boardW) * 0.5f;
            Rect board = new Rect(boardX, 16f, boardW, 508f);
            VisualIdentity.Surface(board, 0.98f);
            string title = newJourneyStep == 1 ? "ВЫБОР СЛОЖНОСТИ" : "ВЫБОР ПУТНИКА";
            VisualIdentity.Heading(new Rect(board.x + 30f, board.y + 7f, board.width - 60f, 42f), title);
            UiKit.Text(new Rect(board.x + 30f, board.y + 51f, board.width - 60f, 21f),
                newJourneyStep == 1
                    ? JourneyChoice.DifficultyDesc[chosenDifficulty]
                    : JourneyChoice.Biographies[chosenHero],
                13f, VisualIdentity.Subdued, TextAnchor.MiddleCenter, false);

            const float gap = 13f;
            float side = 22f;
            float cw = (board.width - side * 2f - 2f * gap) / 3f;
            float cardY = board.y + 83f;
            float cardH = 339f;
            for (int i = 0; i < 3; i++)
            {
                Rect card = new Rect(board.x + side + i * (cw + gap), cardY, cw, cardH);
                bool selected = newJourneyStep == 1 ? chosenDifficulty == i : chosenHero == i;
                Color accent = newJourneyStep == 1
                    ? i == 0 ? VisualIdentity.Jade :
                      i == 1 ? VisualIdentity.BrassLight : VisualIdentity.Vermilion
                    : JourneyChoice.Accent(i);

                UiKit.Rect(new Rect(card.x - 2f, card.y - 2f,
                    card.width + 4f, card.height + 4f),
                    new Color(accent.r, accent.g, accent.b,
                        selected ? 1f : 0.33f));
                VisualIdentity.Surface(card, 1f, false);
                float picH = card.height - 93f;
                Rect artRect = new Rect(card.x + 6f, card.y + 6f, card.width - 12f, picH);
                string art = newJourneyStep == 1
                    ? JourneyChoice.DifficultyArt(i) : JourneyChoice.Portrait(i);
                // The experimental six-panel atlas is currently a 6 KB thumbnail.
                // Render the established high-resolution illustrated portraits
                // until a verified production atlas is actually checked in.
                // Never substitute unrelated encounter/NPC portraits for the
                // player-authored hero and difficulty art.
                if (UiKit.Tex(art) != null)
                    VisualIdentity.Portrait(artRect, art, null, true);
                else
                {
                    UiKit.Rect(artRect, new Color(0.10f, 0.07f, 0.06f, 1f));
                    UiKit.Text(artRect, "ИЛЛЮСТРАЦИЯ НЕ ЗАГРУЖЕНА", 11f,
                        VisualIdentity.Subdued, TextAnchor.MiddleCenter, false);
                }

                if (selected)
                {
                    Color glow = new Color(accent.r, accent.g, accent.b,
                        0.18f + 0.12f * Mathf.Sin(Time.unscaledTime * 3.8f));
                    UiKit.Rect(new Rect(card.x + 8f, artRect.yMax - 5f,
                        card.width - 16f, 3f), glow);
                }
                string name = newJourneyStep == 1
                    ? JourneyChoice.DifficultyNames[i] : JourneyChoice.Names[i];
                UiKit.Text(new Rect(card.x + 5f, card.yMax - 87f,
                        card.width - 10f, 34f), name, 22f, accent);
                UiKit.Text(new Rect(card.x + 7f, card.yMax - 55f,
                        card.width - 14f, 20f),
                    newJourneyStep == 1
                        ? i == 0 ? "ЩАДЯЩИЙ ПУТЬ" : i == 1 ? "ПУТЬ ВОИНА" : "ПУТЬ МАСТЕРА"
                        : JourneyChoice.Specialties[i],
                    12f, VisualIdentity.Parchment);
                if (newJourneyStep == 2)
                    UiKit.Text(new Rect(card.x + 7f, card.yMax - 34f,
                        card.width - 14f, 20f),
                        JourneyChoice.Titles[i], 10f, VisualIdentity.Subdued);
                if (GUI.Button(card, GUIContent.none, GUIStyle.none))
                {
                    if (newJourneyStep == 1) chosenDifficulty = i;
                    else chosenHero = i;
                    Haptics.Pulse(12);
                }
            }

            if (newJourneyStep == 1)
            {
                GUI.matrix = old;
                float k = UiKit.Scale;
                DrawFogToggle(new Rect((board.x + 280f) * k, (board.yMax - 65f) * k, 210f * k, 44f * k));
                UiKit.Begin();
            }
            Rect back = new Rect(board.x + 36f, board.yMax - 65f, 205f, 44f);
            Rect proceed = new Rect(board.xMax - 296f, board.yMax - 65f, 260f, 44f);
            if (VisualIdentity.Button(back, "НАЗАД")) newJourneyStep--;
            if (VisualIdentity.Button(proceed,
                newJourneyStep == 1 ? "ВЫБРАТЬ" : "НАЧАТЬ ПУТЬ", true, true))
            {
                if (newJourneyStep == 1)
                    newJourneyStep = 2;
                else if (!loading)
                {
                    JourneyChoice.BeginNew(chosenDifficulty, chosenHero);
                    StartCoroutine(LoadBattle());
                }
            }
            GUI.matrix = old;
        }

        // Экран загрузки: путник идёт к замку (UI/loading, 1914x822), снизу — название и совет.
        // Рисуется в кадрах между остановкой видео и загрузкой сцены боя.
        static readonly string[] LoadingTips =
        {
            "Удар по ногам проходит под высокой защитой.",
            "Сломленного врага добивают — смотри на красную метку.",
            "Кость пути даёт от одного до трёх шагов. Бой сжигает остаток.",
            "К кузнецу, торговцу и травнице можно вернуться по пройденной дороге.",
            "Серия из десяти ударов — это уже буря стали.",
        };
        int loadingTip = -1;
        bool DrawLoading()
        {
            Texture2D art = UiKit.Tex("UI/loading");
            if (art == null) return false;
            if (loadingTip < 0) loadingTip = Random.Range(0, LoadingTips.Length);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), art, ScaleMode.ScaleAndCrop);
            Matrix4x4 old = UiKit.Begin();
            float W = UiKit.Width;
            UiKit.Text(new Rect(0f, 430f, W, 40f), "ПУТЬ КЛИНКА", 30f, VisualIdentity.BrassLight);
            UiKit.Text(new Rect(0f, 470f, W, 22f), LoadingTips[loadingTip], 14f, VisualIdentity.Parchment);
            float dots = Time.unscaledTime * 3f % 3f;
            UiKit.Text(new Rect(0f, 500f, W, 20f), "загрузка" + (dots < 1f ? "." : dots < 2f ? ".." : "..."), 12f, VisualIdentity.Subdued);
            GUI.matrix = old;
            return true;
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
            Color gold = VisualIdentity.Brass;
            VisualIdentity.Surface(box, 1f);
            UiKit.Paper(new Rect(box.x + 8f, box.y + 7f,
                box.width - 16f, box.height - 14f),
                new Color(0.18f, 0.13f, 0.08f, 0.18f));
            Texture2D br = UiKit.Tex("UI/sakura_branch");
            if (br != null) { Color oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.95f); GUI.DrawTextureWithTexCoords(new Rect(box.xMax - 150f, box.y - 34f, 190f, 190f), br, new Rect(1f, 0f, -1f, 1f)); GUI.color = oc; }   // ветка сакуры с правого угла
            UiKit.Text(new Rect(box.x, box.y + 10f, box.width, 44f),
                "НАСТРОЙКИ", 30f, gold);
            VisualIdentity.Divider(new Rect(box.x + 32f, box.y + 56f,
                box.width - 64f, 1f), 0.48f);

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
                bool selected = i == 3 && PlayerPrefs.GetInt("haptics", 0) == 1;
                bool clicked = VisualIdentity.Button(r, labels[i], true,
                    i == labels.Length - 1, selected);
                if (clicked)
                {
                    switch (i)
                    {
                        case 0: SetAnimated(!animated); break;
                        case 1: EnemyAI.Difficulty = (EnemyAI.Difficulty + 1) % 3; break;
                        case 2: DuelManager.Gfx = 1 - DuelManager.Gfx; break;
                        case 3:
                            Haptics.Enabled = PlayerPrefs.GetInt("haptics", 0) != 1;
                            PlayerPrefs.SetInt("haptics", Haptics.Enabled ? 1 : 0);
                            PlayerPrefs.Save();
                            if (Haptics.Enabled) Haptics.Pulse(40); // opt-in feedback only
                            break;
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
