using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Small hysteretic mobile quality governor. Never touches the player's chosen
    /// graphics setting; it only reduces optional particles and bloom while frame
    /// time remains poor, and recovers slowly after sustained healthy frame times.
    /// </summary>
    public static class MobileVfxBudget
    {
        static float averageFrameTime = 1f / 60f;
        static float overloadTime;
        static float recoveryTime;
        static int degradation;
        static bool lastHigh = true;
        public static int Level { get { return degradation; } }
        public static float ParticleScale { get { return degradation == 0 ? 1f : degradation == 1 ? 0.72f : 0.48f; } }
        public static int BloomPenalty { get { return degradation; } }
        public static bool AllowDepthOfField { get { return degradation == 0; } }

        public static void Tick(float deltaTime, bool highGraphics)
        {
            if (!Application.isPlaying) return;
            if (lastHigh != highGraphics)
            {
                lastHigh = highGraphics;
                degradation = 0; overloadTime = 0f; recoveryTime = 0f;
                averageFrameTime = 1f / 60f;
            }
            if (!highGraphics)
            {
                degradation = 2;
                return;
            }
            float dt = Mathf.Clamp(deltaTime, 0.001f, 0.15f);
            // Slow moving average, so isolated hit-stops or app pauses do not trigger.
            averageFrameTime = Mathf.Lerp(averageFrameTime, dt, 0.035f);
            if (averageFrameTime > 0.044f)
            {
                overloadTime += dt;
                recoveryTime = 0f;
                if (overloadTime >= 4f && degradation < 2)
                {
                    degradation++;
                    overloadTime = 0f;
                }
            }
            else if (averageFrameTime < 0.028f)
            {
                recoveryTime += dt;
                overloadTime = 0f;
                if (recoveryTime >= 9f && degradation > 0)
                {
                    degradation--;
                    recoveryTime = 0f;
                }
            }
            else
            {
                overloadTime = Mathf.Max(0f, overloadTime - dt * 0.4f);
                recoveryTime = Mathf.Max(0f, recoveryTime - dt * 0.4f);
            }
        }
    }
}
