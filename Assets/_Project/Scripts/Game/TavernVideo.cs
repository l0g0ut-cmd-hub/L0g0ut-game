using UnityEngine;
using UnityEngine.Video;

namespace BladePath
{
    /// <summary>
    /// Живой трактир: зацикленное видео (Resources/Video/tavern_loop — Seedance, движущиеся части поверх чёткой картинки UI/tavern,
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
                VideoClip clip = Resources.Load<VideoClip>("Video/tavern_loop");
                if (clip == null) { failed = true; return null; }
                GameObject g = new GameObject("TavernVideo");
                DontDestroyOnLoad(g);
                inst = g.AddComponent<TavernVideo>();
                inst.Init(clip);
            }
            inst.lastAsk = Time.unscaledTime;
            if (!inst.vp.isPlaying) inst.vp.Play();
            return inst.vp.isPrepared && inst.vp.frame > 0 ? inst.rt : null;
        }

        void Init(VideoClip clip)
        {
            rt = new RenderTexture((int)clip.width, (int)clip.height, 0, RenderTextureFormat.ARGB32) { name = "TavernVideoRT" };
            vp = gameObject.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.source = VideoSource.VideoClip;
            vp.clip = clip;
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
