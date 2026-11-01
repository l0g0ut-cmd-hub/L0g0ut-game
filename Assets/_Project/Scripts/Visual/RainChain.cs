using UnityEngine;

namespace BladePath
{
    /// <summary>Цепь-водосток кусари-тои: в дождь по звеньям сбегает вода (частицы вдоль цепи) и плещет в каменную чашу; цепь чуть качается на ветру.</summary>
    public class RainChain : MonoBehaviour
    {
        public ParticleSystem water, splash;
        public Transform chain;
        bool on;
        float nextQualityCheck;

        void Update()
        {
            bool rain = Weather.Instance != null && Weather.Instance.Raining;
            if (rain != on || (rain && Time.unscaledTime >= nextQualityCheck))
            {
                on = rain;
                nextQualityCheck = Time.unscaledTime + 1.5f;
                float quality = (DuelManager.Gfx == 1 ? 1f : 0.5f) * MobileVfxBudget.ParticleScale;
                if (water != null)
                {
                    var e = water.emission; e.rateOverTime = rain ? 70f * quality : 0f;
                    if (rain && !water.isPlaying) water.Play();
                    else if (!rain && water.isPlaying) water.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                if (splash != null)
                {
                    var e = splash.emission; e.rateOverTime = rain ? 30f * quality : 0f;
                    if (rain && !splash.isPlaying) splash.Play();
                    else if (!rain && splash.isPlaying) splash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            if (chain != null)
            {
                float w = WindGusts.Strength;
                chain.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * (0.6f + 3f * w), 0f, Mathf.Sin(Time.time * 0.9f + 1f) * (0.4f + 2.5f * w) * WindGusts.Dir.x);
            }
        }
    }
}
