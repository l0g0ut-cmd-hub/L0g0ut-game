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
        Material fxMat, softMat, splatMat;
        const int MaxDecals = 220;
        readonly List<Decal> decals = new List<Decal>();
        int decalIdx;
        struct Decal { public Transform t; public Renderer r; public float born, life, size, grow; }
        MaterialPropertyBlock mpb;

        class Tracked { public WeaponController w; public SwingPhase last; public TrailRenderer trail; }
        readonly List<Tracked> tracked = new List<Tracked>();

        void Awake()
        {
            Instance = this;
            voices = new AudioSource[6];
            for (int i = 0; i < voices.Length; i++) { voices[i] = gameObject.AddComponent<AudioSource>(); voices[i].spatialBlend = 0f; voices[i].playOnAwake = false; }
            swish = Swish(); hit = Thump(0.22f, 140f, 55f, 0.55f); hitHeavy = Thump(0.34f, 100f, 38f, 0.9f); clang = Clang(); boom = Thump(1.0f, 70f, 28f, 0.8f);
            Shader sh = Shader.Find("Sprites/Default");
            if (sh != null)
            {
                fxMat = new Material(sh);
                softMat = new Material(sh); softMat.mainTexture = SoftSprites.Disc;
                splatMat = new Material(sh); splatMat.mainTexture = SoftSprites.Splat;
            }
            mpb = new MaterialPropertyBlock();
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

        /// <summary>Попадание. dir — направление удара (скорость клинка), strength ~0.4…2 (урон/крит/убийство).</summary>
        public void OnHit(Vector3 point, Vector3 normal, Vector3 dir, float strength, bool blocked, bool kill, Transform bleedAt)
        {
            if (blocked) { Play(clang, 0.8f, Random.Range(0.9f, 1.2f)); Sparks(point, normal, 16); return; }
            Play(strength > 1f ? hitHeavy : hit, 0.95f, Random.Range(0.92f, 1.08f));
            Blood(point, normal, dir, strength, kill);
            if (bleedAt != null) Bleed(bleedAt, Mathf.Lerp(1.2f, 3.5f, Mathf.Clamp01(strength - 0.4f)), strength);
            if (kill) Play(boom, 1f, 1f);
        }

        // ---------------------------------------------------------------- кровь

        void Blood(Vector3 pos, Vector3 normal, Vector3 dir, float strength, bool kill)
        {
            if (softMat == null) return;
            if (dir.sqrMagnitude < 1e-4f) dir = -normal;
            dir.Normalize();
            Vector3 cone = (dir * 0.75f + normal * 0.45f + Vector3.up * 0.35f).normalized;
            int count = Mathf.RoundToInt(Mathf.Lerp(50f, 150f, Mathf.Clamp01(strength - 0.3f)) * (kill ? 1.6f : 1f));

            // 1) струя брызг: вытянутые капли по ходу полёта
            Spray(pos, cone, count, 3.5f * Mathf.Lerp(0.9f, 1.4f, Mathf.Clamp01(strength - 0.5f)), 36f);
            // 2) веер вперёд по ходу клинка (длинные тонкие капли)
            Spray(pos, dir, Mathf.RoundToInt(count * 0.4f), 6.5f * Mathf.Lerp(0.9f, 1.3f, Mathf.Clamp01(strength - 0.5f)), 14f);
            // 3) кровавая взвесь
            Mist(pos, cone, 10 + Mathf.RoundToInt(10 * strength), 0.9f);
            // 4) пятна на земле там, куда долетели капли
            int splats = Mathf.RoundToInt(14 * strength) + (kill ? 18 : 0);
            for (int i = 0; i < splats; i++)
            {
                Vector3 v = (Quaternion.AngleAxis(Random.Range(-35f, 35f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-20f, 25f), Vector3.Cross(Vector3.up, cone)) * cone) * Random.Range(1.5f, 6.5f);
                float g = 9.81f, disc = v.y * v.y + 2f * g * Mathf.Max(0.05f, pos.y);
                float tf = (v.y + Mathf.Sqrt(disc)) / g;
                Vector3 land = pos + new Vector3(v.x, 0f, v.z) * tf;
                land.y = 0f;
                float sz = Random.Range(0.06f, 0.30f) * Mathf.Lerp(0.8f, 1.4f, Mathf.Clamp01(strength - 0.4f));
                AddDecal(land, sz, new Vector2(v.x, v.z), Random.Range(14f, 26f), tf * 0.9f);
            }
            if (kill) AddDecal(new Vector3(pos.x, 0f, pos.z), 0.25f, Vector2.zero, 40f, 0.9f, 1.15f);
        }

        void Spray(Vector3 pos, Vector3 dir, int count, float speed, float angle)
        {
            GameObject g = new GameObject("Blood");
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(dir);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.35f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.0f, 0.02f), new Color(0.85f, 0.04f, 0.05f));
            main.gravityModifier = 1.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = angle; sp.radius = 0.03f;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.4f)));
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.05f; rend.lengthScale = 2.4f;
            ps.Play();
        }

        void Mist(Vector3 pos, Vector3 dir, int count, float life)
        {
            GameObject g = new GameObject("BloodMist");
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(dir);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.5f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
            main.startColor = new Color(0.42f, 0.0f, 0.02f, 0.45f);
            main.gravityModifier = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 45f; sp.radius = 0.04f;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
            g.GetComponent<ParticleSystemRenderer>().material = softMat;
            ps.Play();
        }

        /// <summary>Рана кровоточит: капли стекают с места попадания несколько секунд (эмиттер едет вместе с телом).</summary>
        void Bleed(Transform at, float seconds, float strength)
        {
            if (softMat == null || at == null) return;
            GameObject g = new GameObject("Bleed");
            g.transform.SetParent(at, false);
            g.transform.localPosition = Vector3.zero;
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.duration = seconds;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.4f, 0f, 0.02f), new Color(0.8f, 0.03f, 0.04f));
            main.gravityModifier = 1.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = Mathf.Lerp(18f, 55f, Mathf.Clamp01(strength - 0.4f));
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Sphere; sp.radius = 0.08f;
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.04f; rend.lengthScale = 2f;
            ps.Play();
            StartCoroutine(StopBleed(ps, g, seconds));
        }

        System.Collections.IEnumerator StopBleed(ParticleSystem ps, GameObject g, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            yield return new WaitForSeconds(1.5f);
            if (g != null) Destroy(g);
        }

        void Sparks(Vector3 pos, Vector3 normal, int count)
        {
            if (softMat == null) return;
            GameObject g = new GameObject("Sparks");
            g.transform.position = pos;
            g.transform.rotation = Quaternion.LookRotation(normal.sqrMagnitude > 1e-4f ? normal : Vector3.up);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            main.startColor = new Color(1f, 0.85f, 0.45f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
            var sp = ps.shape; sp.shapeType = ParticleSystemShapeType.Cone; sp.angle = 50f; sp.radius = 0.02f;
            var rend = g.GetComponent<ParticleSystemRenderer>();
            rend.material = softMat; rend.renderMode = ParticleSystemRenderMode.Stretch; rend.velocityScale = 0.06f; rend.lengthScale = 2.5f;
            ps.Play();
        }

        static Mesh quad;
        static Mesh QuadMesh()
        {
            if (quad != null) return quad;
            quad = new Mesh();
            quad.vertices = new Vector3[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            quad.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.triangles = new int[] { 0, 2, 1, 0, 3, 2 };   // нормаль -Z, как у стандартного Quad
            quad.RecalculateNormals();
            return quad;
        }

        // пятна крови на земле (пул плоских квадов)
        void AddDecal(Vector3 pos, float size, Vector2 dir, float life, float delay, float grow = 0f)
        {
            if (splatMat == null) return;
            Decal d;
            if (decals.Count < MaxDecals)
            {
                // CreatePrimitive нельзя: он добавляет MeshCollider, а модуль физики для мешей в сборке вырезан
                GameObject q = new GameObject("BloodDecal");
                q.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                Renderer rr = q.AddComponent<MeshRenderer>();
                rr.sharedMaterial = splatMat;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rr.receiveShadows = false;
                d = new Decal(); d.t = q.transform; d.r = rr;
                decals.Add(d);
                decalIdx = decals.Count - 1;
            }
            else { decalIdx = (decalIdx + 1) % decals.Count; d = decals[decalIdx]; }
            float yaw = dir.sqrMagnitude > 1e-4f ? Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg : Random.Range(0f, 360f);
            d.t.position = new Vector3(pos.x, 0.012f + (decalIdx % 7) * 0.0006f, pos.z);
            d.t.rotation = Quaternion.Euler(90f, yaw, 0f);
            float stretch = dir.sqrMagnitude > 1e-4f ? Random.Range(1.0f, 1.8f) : 1f;
            d.size = size; d.grow = grow;
            d.t.localScale = new Vector3(size, size * stretch, 1f) * (grow > 0f ? 0.1f : 1f);
            d.born = Time.unscaledTime + delay; d.life = life;
            d.t.gameObject.SetActive(false);
            decals[decalIdx] = d;
        }

        void LateUpdate()
        {
            for (int i = 0; i < decals.Count; i++)
            {
                Decal d = decals[i];
                if (d.t == null) continue;
                float age = Time.unscaledTime - d.born;
                if (age < 0f) continue;
                if (!d.t.gameObject.activeSelf) d.t.gameObject.SetActive(true);
                float a = Mathf.Clamp01((d.life - age) / 6f);       // последние 6 с — растворяется
                if (d.grow > 0f)
                {
                    float k = Mathf.Clamp01(age / d.grow);
                    float sc = d.size * Mathf.Lerp(0.1f, 2.6f, 1f - (1f - k) * (1f - k));
                    d.t.localScale = new Vector3(sc, sc, 1f);
                }
                mpb.SetColor("_Color", new Color(0.30f, 0.0f, 0.015f, 0.88f * a));
                d.r.SetPropertyBlock(mpb);
                if (a <= 0f) d.t.gameObject.SetActive(false);
            }
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
