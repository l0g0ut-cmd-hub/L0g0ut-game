using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// «Сок» боя без внешних ассетов: процедурные звуки (взмах, удар, лязг блока, гул смерти), искры/кровь при попадании,
    /// светящийся след клинка во время удара. Всё создаётся в рантайме из кода.
    /// </summary>
    public class Juice : MonoBehaviour
    {
        public static Juice Instance { get; private set; }

        AudioSource[] voices;
        int voiceIdx;
        AudioClip swish, hit, hitHeavy, clang, boom;
        Material fxMat;

        class Tracked { public WeaponController w; public SwingPhase last; public TrailRenderer trail; }
        readonly List<Tracked> tracked = new List<Tracked>();

        void Awake()
        {
            Instance = this;
            voices = new AudioSource[6];
            for (int i = 0; i < voices.Length; i++) { voices[i] = gameObject.AddComponent<AudioSource>(); voices[i].spatialBlend = 0f; voices[i].playOnAwake = false; }
            swish = Swish(); hit = Thump(0.22f, 140f, 55f, 0.55f); hitHeavy = Thump(0.34f, 100f, 38f, 0.9f); clang = Clang(); boom = Thump(1.0f, 70f, 28f, 0.8f);
            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null) fxMat = new Material(sh);
        }

        public void Track(WeaponController w)
        {
            if (w == null) return;
            Tracked t = new Tracked(); t.w = w; t.last = SwingPhase.Idle;
            if (fxMat != null && w.bladeRoot != null)
            {
                GameObject g = new GameObject("BladeTrail");
                g.transform.SetParent(w.bladeRoot, false);
                g.transform.localPosition = new Vector3(0f, 0f, w.bladeLength * 0.96f);
                TrailRenderer tr = g.AddComponent<TrailRenderer>();
                tr.material = fxMat;
                tr.time = 0.20f;
                tr.minVertexDistance = 0.02f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0f));
                Gradient gr = new Gradient();
                gr.SetKeys(new GradientColorKey[] { new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0f), new GradientColorKey(new Color(0.7f, 0.8f, 1f), 1f) },
                           new GradientAlphaKey[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
                tr.colorGradient = gr;
                tr.alignment = LineAlignment.View;
                tr.emitting = false;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t.trail = tr;
            }
            tracked.Add(t);
        }

        void Update()
        {
            for (int i = 0; i < tracked.Count; i++)
            {
                Tracked t = tracked[i];
                if (t.w == null) continue;
                SwingPhase ph = t.w.Phase;
                if (ph != t.last)
                {
                    if (ph == SwingPhase.Strike) { Play(swish, 0.55f, Random.Range(0.9f, 1.15f)); if (t.trail != null) { t.trail.Clear(); t.trail.emitting = true; } }
                    if ((ph == SwingPhase.Recovery || ph == SwingPhase.Idle) && t.trail != null) t.trail.emitting = false;
                    t.last = ph;
                }
            }
        }

        void Play(AudioClip c, float vol, float pitch)
        {
            if (c == null || voices == null) return;
            AudioSource a = voices[voiceIdx]; voiceIdx = (voiceIdx + 1) % voices.Length;
            a.pitch = pitch; a.volume = vol; a.clip = c; a.Play();
        }

        public void OnHit(Vector3 point, Vector3 normal, bool heavy, bool blocked, bool kill)
        {
            if (blocked) { Play(clang, 0.8f, Random.Range(0.9f, 1.2f)); Burst(point, normal, new Color(1f, 0.85f, 0.4f), 14, 0.04f, 3.5f); return; }
            Play(heavy ? hitHeavy : hit, 0.9f, Random.Range(0.92f, 1.08f));
            Burst(point, normal, new Color(0.75f, 0.02f, 0.02f), heavy ? 26 : 16, 0.035f, 3f);
            Burst(point, normal, new Color(1f, 0.9f, 0.7f), 6, 0.02f, 4.5f);
            if (kill) Play(boom, 1f, 1f);
        }

        void Burst(Vector3 pos, Vector3 normal, Color col, int count, float size, float speed)
        {
            if (fxMat == null) return;
            GameObject g = new GameObject("HitFx");
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 1e-4f ? normal : Vector3.up);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.4f);
            main.startColor = col;
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 38f; sp.radius = 0.02f;
            var col2 = ps.colorOverLifetime; col2.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col2.color = gr;
            var rend = g.GetComponent<ParticleSystemRenderer>(); rend.material = fxMat;
            ps.Play();
        }

        // ---------------------------------------------------------------- синтез

        const int Rate = 22050;

        static AudioClip Make(string name, float[] d)
        {
            AudioClip c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip Swish()
        {
            int n = (int)(Rate * 0.30f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(7);
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                float a = Mathf.Lerp(0.05f, 0.55f, t);
                lp1 += a * (noise - lp1);
                lp2 += (a * 0.35f) * (noise - lp2);
                float band = lp1 - lp2;
                float env = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f)), 1.5f);
                d[i] = band * env * 2.4f;
            }
            return Make("swish", d);
        }

        static AudioClip Thump(float len, float f0, float f1, float noiseAmt)
        {
            int n = (int)(Rate * len);
            float[] d = new float[n];
            System.Random rnd = new System.Random(11);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(f0, f1, Mathf.Clamp01(t / len));
                phase += 2f * Mathf.PI * f / Rate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * (len > 0.5f ? 3f : 16f));
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += 0.25f * (noise - lp);
                float crack = lp * Mathf.Exp(-t * 38f) * noiseAmt;
                d[i] = Mathf.Clamp(body * 0.9f + crack, -1f, 1f);
            }
            return Make("thump", d);
        }

        static AudioClip Clang()
        {
            int n = (int)(Rate * 0.7f);
            float[] d = new float[n];
            float[] fr = { 880f, 1320f, 1870f, 2650f, 3400f };
            float[] dc = { 6f, 8f, 10f, 13f, 18f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                for (int k = 0; k < fr.Length; k++) v += Mathf.Sin(2f * Mathf.PI * fr[k] * t) * Mathf.Exp(-t * dc[k]) / (1f + k * 0.4f);
                d[i] = v * 0.5f * Mathf.Min(1f, t * 400f);
            }
            return Make("clang", d);
        }
    }
}
