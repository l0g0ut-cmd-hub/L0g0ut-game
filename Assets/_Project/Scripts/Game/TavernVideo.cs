using UnityEngine;
using UnityEngine.Video;

namespace BladePath
{
    /// <summary>
    /// Живой трактир: зацикленное видео (StreamingAssets/tavern_loop.mp4 — Seedance, движущиеся части поверх чёткой картинки UI/tavern,
    /// та же раскадровка 1:1). Файл лежит как есть: Unity под Linux (сборка на CI) видео не импортирует.
    ///
    /// Без рывков на стыке: встроенный повтор VideoPlayer на Android перематывает декодер в начало — раз в несколько кругов это
    /// давало заминку и разрыв. Здесь два проигрывателя по очереди: пока играет один, второй уже подготовлен и стоит на первом
    /// кадре; первый дошёл до конца (последний кадр остаётся в его текстуре) — сразу пускается второй, а первый готовится заново.
    /// Пока видео готовится (первый вход), показывается та же картинка без эффектов — она совпадает с первым кадром, подмены не видно.
    /// </summary>
    public class TavernVideo : MonoBehaviour
    {
        static TavernVideo inst;
        static bool failed;
        readonly VideoPlayer[] vp = new VideoPlayer[2];
        readonly RenderTexture[] rt = new RenderTexture[2];
        readonly bool[] started = new bool[2];
        readonly bool[] frameDelivered = new bool[2];
        bool standbyPrepared;
        bool initialVideoVisible;
        float firstFrameAt = -1f;
        float initialVisibleAt = -1f;
        // First launch waits for a real decoded frame; never switch on frame index alone.
        const float FirstFrameSettle = 0.12f;
        int cur = -1;   // чью текстуру показываем
        bool shown;     // первый кадр уже был — дальше текстура всегда с кадром
        float lastAsk = -10f;

        /// <summary>Видео не будет (нет файла / ошибка декодера) — трактир рисуется картинкой с огнём и паром кодом.</summary>
        public static bool Failed { get { return failed; } }

        /// <summary>Заранее подготовить (вход в трактир), чтобы первый кадр был сразу.</summary>
        public static void Preload()
        {
            if (failed || inst != null) return;
            GameObject g = new GameObject("TavernVideo");
            DontDestroyOnLoad(g);
            inst = g.AddComponent<TavernVideo>();
            inst.Init();
        }

        /// <summary>Кадр видео для отрисовки (null — пока показывать картинку). Звать каждый кадр, пока трактир открыт.</summary>
        public static Texture Frame()
        {
            if (failed) return null;
            Preload();
            inst.lastAsk = Time.unscaledTime;
            return inst.Tick();
        }

        void Init()
        {
            string url = System.IO.Path.Combine(Application.streamingAssetsPath, "tavern_loop.mp4");
            for (int i = 0; i < 2; i++)
            {
                rt[i] = new RenderTexture(1920, 816, 0, RenderTextureFormat.ARGB32) { name = "TavernVideoRT" + i };
                VideoPlayer p = gameObject.AddComponent<VideoPlayer>();
                p.playOnAwake = false;
                p.source = VideoSource.Url;
                p.url = url;
                p.isLooping = false;
                p.skipOnDrop = false;   // кадры не выбрасываются — иначе на загруженном телефоне «прыжки»
                p.waitForFirstFrame = true;
                p.audioOutputMode = VideoAudioOutputMode.None;
                p.renderMode = VideoRenderMode.RenderTexture;
                p.targetTexture = rt[i];
                p.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
                int index = i;
                p.sendFrameReadyEvents = true;
                p.frameReady += (player, frameIndex) =>
                {
                    frameDelivered[index] = true;
                    if (index == 0 && firstFrameAt < 0f)
                        firstFrameAt = Time.unscaledTime;
                };
                p.errorReceived += (v, msg) => { failed = true; Debug.LogWarning("TavernVideo: " + msg); };
                vp[i] = p;
                // Start one Android decoder first. Preparing both simultaneously
                // caused an avoidable first-visit GPU/codec load spike.
                if (i == 0) p.Prepare();
            }
        }

        bool Ended(int i)
        {
            VideoPlayer p = vp[i];
            if (!started[i]) return false;
            if (!p.isPlaying && !p.isPaused) return true;
            ulong n = p.frameCount;
            return n > 0 && p.frame >= (long)n - 1;
        }

        Texture Tick()
        {
            if (cur < 0)
            {
                if (!vp[0].isPrepared) return null;
                vp[0].Play(); started[0] = true; cur = 0;
            }
            // Wait for the decoder to output a real frame and settle before
            // replacing the static picture. Index zero can mean no pixels yet.
            if (!initialVideoVisible)
            {
                if (!frameDelivered[0] || firstFrameAt < 0f || Time.unscaledTime - firstFrameAt < FirstFrameSettle)
                    return null;
                initialVideoVisible = true;
                initialVisibleAt = Time.unscaledTime;
                shown = true;
            }
            // Let Android finish the first decoder handoff before bringing
            // up the standby codec. This avoids a second start-up spike
            // during the first visible moment of the tavern.
            if (!standbyPrepared && Time.unscaledTime - initialVisibleAt >= 0.7f)
            {
                standbyPrepared = true;
                vp[1].Prepare();
            }
            VideoPlayer p = vp[cur];
            if (p.isPaused) p.Play();   // вернулись в трактир
            int nxt = 1 - cur;
            if (Ended(cur) && vp[nxt].isPrepared)
            {
                if (!started[nxt]) { frameDelivered[nxt] = false; vp[nxt].Play(); started[nxt] = true; }
                if (frameDelivered[nxt])
                {
                    // второй уже выдал кадр — переключаемся, первый готовим к следующему кругу
                    vp[cur].Stop(); started[cur] = false; frameDelivered[cur] = false; vp[cur].Prepare();
                    cur = nxt;
                }
            }
            return shown && frameDelivered[cur] ? rt[cur] : null;   // never display an unrendered RT
        }

        void Update()
        {
            // трактир закрыт — видео на паузе (батарея и декодер)
            if (cur >= 0 && Time.unscaledTime - lastAsk > 0.5f && vp[cur].isPlaying) vp[cur].Pause();
        }

        void OnDestroy()
        {
            for (int i = 0; i < 2; i++) if (rt[i] != null) rt[i].Release();
            if (inst == this) inst = null;
        }
    }
}
