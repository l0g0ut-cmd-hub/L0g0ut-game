using UnityEngine;
using UnityEngine.Video;

namespace BladePath
{
    /// <summary>
    /// Живой трактир: зацикленное видео (StreamingAssets/tavern_loop.mp4 — Seedance, движущиеся части поверх чёткой картинки UI/tavern,
    /// та же раскадровка 1:1). Играет, только пока трактир на экране; не готово или не грузится — трактир показывается картинкой.
    /// </summary>
    public class TavernVideo : MonoBehaviour
    {
        static TavernVideo inst;
        static bool failed;
        VideoPlayer vp;
        RenderTexture rt;
        float lastAsk = -10f;

        /// <summary>Кадр видео для отрисовки (null — показывать картинку). Звать каждый кадр, пока трактир открыт.</summary>
        public static Texture Frame()
        {
            if (failed) return null;
            if (inst == null)
            {
                GameObject g = new GameObject("TavernVideo");
                DontDestroyOnLoad(g);
                inst = g.AddComponent<TavernVideo>();
                inst.Init();
            }
            inst.lastAsk = Time.unscaledTime;
            if (!inst.vp.isPlaying) inst.vp.Play();
            return inst.vp.isPrepared && inst.vp.frame > 0 ? inst.rt : null;
        }

        // файл лежит в StreamingAssets как есть: Unity под Linux (сборка на CI) видео не импортирует — VideoClip не собирался
        void Init()
        {
            rt = new RenderTexture(1920, 816, 0, RenderTextureFormat.ARGB32) { name = "TavernVideoRT" };
            vp = gameObject.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.source = VideoSource.Url;
            vp.url = System.IO.Path.Combine(Application.streamingAssetsPath, "tavern_loop.mp4");
            vp.isLooping = true;
            vp.skipOnDrop = true;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rt;
            vp.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            vp.errorReceived += (p, msg) => { failed = true; Debug.LogWarning("TavernVideo: " + msg); };
            vp.Prepare();
        }

        void Update()
        {
            // трактир закрыт — видео на паузе (батарея и декодер)
            if (vp != null && vp.isPlaying && Time.unscaledTime - lastAsk > 0.5f) vp.Pause();
        }

        void OnDestroy() { if (rt != null) rt.Release(); if (inst == this) inst = null; }
    }
}
