using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Runtime-generated additive VFX atlas. No external PNG is required.
    /// Generates six sequential frames once and caches the resulting texture.
    /// Intended as a real in-build fallback for the separately generated art packs.
    /// </summary>
    public static class EmbeddedFxArt
    {
        const int FrameSize = 96;
        const int Frames = 6;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static readonly string[] supported = { "slash_red", "sparks_gold", "blood_spray", "smoke", "petals", "sigil_red", "spirit_wolf", "flame_red" };

        public static Texture2D Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D tex;
            if (cache.TryGetValue(name, out tex)) return tex;
            bool valid = false;
            foreach (string id in supported) if (id == name) { valid = true; break; }
            if (!valid) return null;
            tex = Make(name);
            cache[name] = tex;
            return tex;
        }

        static float Hash(float x, float y, float seed)
        {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f + seed * 74.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        static float Ring(float radius, float center, float width)
        {
            float d = (radius - center) / Mathf.Max(0.001f, width);
            return Mathf.Exp(-d * d * 3f);
        }

        static Texture2D Make(string name)
        {
            int w = FrameSize * Frames;
            Texture2D tex = new Texture2D(w, FrameSize, TextureFormat.RGBA32, false)
            {
                name = "Embedded_" + name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            Color32[] pixels = new Color32[w * FrameSize];
            for (int frame = 0; frame < Frames; frame++)
            {
                float t = frame / (float)(Frames - 1);
                for (int y = 0; y < FrameSize; y++)
                for (int x = 0; x < FrameSize; x++)
                {
                    float u = (x + 0.5f) / FrameSize * 2f - 1f;
                    float v = (y + 0.5f) / FrameSize * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    float noise = Hash(x / 7f, y / 7f, frame + 1f);
                    float a = 0f;
                    Color color = Color.white;
                    if (name == "slash_red")
                    {
                        float sweep = -1.65f + t * 2.4f;
                        float diff = Mathf.Atan2(Mathf.Sin(ang - sweep), Mathf.Cos(ang - sweep));
                        a = Ring(r, 0.66f, 0.095f) * Mathf.Exp(-diff * diff * 0.7f);
                        a *= Mathf.Clamp01((u + 0.98f) * 3f) * (1f - 0.5f * t);
                        color = Color.Lerp(new Color(1f, 0.07f, 0.02f), Color.white, Mathf.Clamp01(a * 1.6f));
                    }
                    else if (name == "sigil_red")
                    {
                        float spikes = Mathf.Abs(Mathf.Sin(ang * 8f + t * 0.8f));
                        a = Ring(r, 0.65f + spikes * 0.045f, 0.028f) + Ring(r, 0.4f, 0.024f);
                        a += Ring(r, 0.2f, 0.035f) * 0.6f;
                        color = new Color(1f, 0.12f + a * 0.35f, 0.07f);
                    }
                    else if (name == "flame_red")
                    {
                        float rise = (v + 1f) * 0.5f;
                        float sway = Mathf.Sin(v * 12f + t * 6f) * 0.12f;
                        float width = (1f - rise) * 0.53f + 0.03f;
                        a = Mathf.Clamp01((width - Mathf.Abs(u + sway)) * 8f) * Mathf.Clamp01((1f - rise) * 2f);
                        a *= 0.7f + 0.3f * noise;
                        color = Color.Lerp(new Color(0.8f, 0.015f, 0.01f), new Color(1f, 0.8f, 0.22f), Mathf.Clamp01(1f - rise));
                    }
                    else if (name == "smoke")
                    {
                        float cloud = Mathf.Exp(-r * r * 3.3f);
                        a = cloud * (0.3f + noise * 0.35f) * (1f - t * 0.65f);
                        color = new Color(0.46f, 0.47f, 0.5f);
                    }
                    else if (name == "sparks_gold")
                    {
                        float rays = Mathf.Exp(-Mathf.Abs(Mathf.Sin(ang * 9f + t)) * 65f);
                        a = rays * Mathf.Exp(-r * (2.5f + t * 2f)) + Mathf.Exp(-r * r * 65f);
                        color = Color.Lerp(new Color(1f, 0.35f, 0.04f), Color.white, Mathf.Clamp01(a));
                    }
                    else if (name == "blood_spray")
                    {
                        float spray = Mathf.Abs(Mathf.Sin(ang * 13f + t * 3f));
                        a = Mathf.Clamp01((spray - 0.86f) * 7f) * Mathf.Exp(-Mathf.Pow(r - t * 0.65f, 2f) * 14f);
                        color = new Color(0.75f, 0.025f, 0.02f);
                    }
                    else if (name == "petals")
                    {
                        float spots = Mathf.Sin(u * 19f + frame * 2.3f) * Mathf.Cos(v * 21f - frame * 1.8f);
                        a = Mathf.Clamp01((spots - 0.87f) * 6f) * Mathf.Clamp01(1f - r * 0.5f);
                        color = new Color(1f, 0.36f, 0.54f);
                    }
                    else if (name == "spirit_wolf")
                    {
                        float head = Mathf.Exp(-((u - 0.2f) * (u - 0.2f) * 18f + (v + 0.03f) * (v + 0.03f) * 24f));
                        float body = Mathf.Exp(-((u + 0.25f) * (u + 0.25f) * 8f + (v + 0.12f) * (v + 0.12f) * 28f));
                        float tail = Mathf.Exp(-((u + 0.67f) * (u + 0.67f) * 24f + (v - 0.23f) * (v - 0.23f) * 18f));
                        a = (head + body + tail) * (0.7f + 0.3f * Mathf.Sin(t * 6.28f));
                        color = new Color(0.48f, 0.7f, 1f);
                    }
                    a = Mathf.Clamp01(a);
                    int ix = frame * FrameSize + x;
                    pixels[y * w + ix] = new Color(color.r, color.g, color.b, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
