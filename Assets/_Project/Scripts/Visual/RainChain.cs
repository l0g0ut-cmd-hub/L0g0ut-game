using UnityEngine;

namespace BladePath
{
    /// <summary>Цепь-водосток кусари-тои: в дождь по звеньям сбегает вода (частицы вдоль цепи) и плещет в каменную чашу; цепь чуть качается на ветру.</summary>
    public class RainChain : MonoBehaviour
    {
        public ParticleSystem water, splash;
        public Transform chain;
        bool on;

        void Update()
        {
            bool rain = Weather.Instance != null && Weather.Instance.Raining;
            if (rain != on)
            {
                on = rain;
                if (water != null) { var e = water.emission; e.rateOverTime = rain ? 70f : 0f; }
                if (splash != null) { var e = splash.emission; e.rateOverTime = rain ? 30f : 0f; }
            }
            if (chain != null)
            {
                float w = WindGusts.Strength;
                chain.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * (0.6f + 3f * w), 0f, Mathf.Sin(Time.time * 0.9f + 1f) * (0.4f + 2.5f * w) * WindGusts.Dir.x);
            }
        }
    }
}
