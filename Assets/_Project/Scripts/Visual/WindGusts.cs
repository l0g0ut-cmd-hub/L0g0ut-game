using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Порывы ветра над двором храма: раз в 12–25 с ветер нарастает за ~1 с и стихает за 3–4 с. Во время порыва
    /// знамёна и бумажная полоска фурина рвутся сильнее (BannerSway/WindChime читают Strength), листья клёна в воздухе
    /// сносит вбок, по камню арены пробегает горсть опавших листьев, снег закручивает; слышен глухой свист (синтез).
    /// Направление — поперёк двора (то слева, то справа), чтобы с камеры боя было видно движение.
    /// </summary>
    public class WindGusts : MonoBehaviour
    {
        public static float Strength { get; private set; }
        public static Vector3 Dir { get; private set; } = Vector3.right;
        public float minGap = 12f, maxGap = 25f;
        float nextAt, t = -1f, rise = 1f, fall = 3.5f, peak = 1f;
        ParticleSystem petals, snow, skitter, whirl;
        ParticleSystem[] fireBits = new ParticleSystem[0];   // искры и дым жаровен — сносит порывом
        AudioSource src, rustle;
        static AudioClip whoosh, rustleClip;
        bool fired;
        float snowLookAt;

        void Start()
        {
            nextAt = Time.time + Random.Range(5f, 10f);
            GameObject p = GameObject.Find("Petals");
            if (p != null) petals = p.GetComponent<ParticleSystem>();
            src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0.22f;
            if (whoosh == null) whoosh = Whoosh();
            // шелест листвы клёнов и хвои: петля, громкость идёт за силой порыва (в снег и без листвы — тише)
            if (rustleClip == null) rustleClip = Rustle();
            rustle = gameObject.AddComponent<AudioSource>(); rustle.playOnAwake = false; rustle.spatialBlend = 0f; rustle.loop = true;
            rustle.clip = rustleClip; rustle.volume = 0f; rustle.Play();
            skitter = BuildSkitter();
            var bits = new System.Collections.Generic.List<ParticleSystem>();
            foreach (ParticleSystem ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.name == "Embers" || ps.name == "Smoke" || ps.name == "IncenseSmoke") bits.Add(ps);
            fireBits = bits.ToArray();
        }

        static readonly int WindId = Shader.PropertyToID("_BP_Wind"), WindDirId = Shader.PropertyToID("_BP_WindDir");
        void OnDisable() { Strength = 0f; Shader.SetGlobalFloat(WindId, 0f); }

        void Update()
        {
            if (t < 0f && Time.time >= nextAt)
            {
                t = 0f; fired = false;
                rise = Random.Range(0.8f, 1.3f); fall = Random.Range(2.8f, 4.2f); peak = Random.Range(0.7f, 1f);
                Dir = (Random.value < 0.5f ? Vector3.right : Vector3.left) + new Vector3(0f, 0f, Random.Range(-0.3f, 0.3f));
                Dir = Dir.normalized;
                if (src != null && whoosh != null) { src.pitch = Random.Range(0.85f, 1.1f); src.PlayOneShot(whoosh, 0.6f + 0.4f * peak); }
            }
            float s = 0f;
            if (t >= 0f)
            {
                t += Time.deltaTime;
                s = t < rise ? Mathf.SmoothStep(0f, 1f, t / rise) : 1f - Mathf.SmoothStep(0f, 1f, (t - rise) / fall);
                s *= peak;
                if (!fired && t > rise * 0.6f) { fired = true; Skitter(); if (peak > 0.82f && Random.value < 0.45f) Whirl(); }
                if (t > rise + fall) { t = -1f; s = 0f; nextAt = Time.time + Random.Range(minGap, maxGap); }
            }
            Strength = s;
            if (rustle != null)
            {
                bool snowy = Weather.Instance != null && Weather.Instance.Snowing;
                float target = (0.03f + 0.3f * s) * (snowy ? 0.5f : 1f);   // лёгкий шорох всегда, на порыве — шумит роща
                rustle.volume = Mathf.MoveTowards(rustle.volume, target, Time.deltaTime * 0.6f);
                rustle.panStereo = Mathf.Clamp(Dir.x * 0.4f * s, -0.4f, 0.4f);
            }
            // трава и листва (шейдер BladePath/Foliage) гнутся по ветру
            Shader.SetGlobalFloat(WindId, s);
            Shader.SetGlobalVector(WindDirId, new Vector4(Dir.x, 0f, Dir.z, 0f));
            // листья в воздухе и снег сносит по ветру
            Push(petals, 2.2f);
            if (snow == null && Time.time >= snowLookAt) { snowLookAt = Time.time + 2f; GameObject g = GameObject.Find("Snow"); if (g != null) snow = g.GetComponent<ParticleSystem>(); }   // снег появляется с погодой — ищем редко
            Push(snow, 3.5f);
            for (int i = 0; i < fireBits.Length; i++) Push(fireBits[i], fireBits[i].name == "Embers" ? 2.6f : 1.4f);
        }

        void Push(ParticleSystem ps, float k)
        {
            if (ps == null) return;
            var f = ps.forceOverLifetime;
            f.enabled = Strength > 0.01f;
            f.space = ParticleSystemSimulationSpace.World;
            f.x = new ParticleSystem.MinMaxCurve(Dir.x * Strength * k);
            f.y = new ParticleSystem.MinMaxCurve(Strength * 0.25f);
            f.z = new ParticleSystem.MinMaxCurve(Dir.z * Strength * k);
        }

        /// <summary>Горсть опавших листьев срывается с камня с наветренной стороны арены и пробегает по ней, кувыркаясь.</summary>
        void Skitter()
        {
            if (skitter == null) return;
            bool snowy = Weather.Instance != null && Weather.Instance.Snowing;
            int n = Random.Range(6, 11) / (snowy ? 2 : 1);
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                e.position = new Vector3(-Dir.x * Random.Range(3.4f, 4.6f), Random.Range(0.02f, 0.12f), Random.Range(-2.4f, 2.4f));
                e.velocity = Dir * Random.Range(2.2f, 3.6f) * peak + Vector3.up * Random.Range(0.2f, 1.1f);
                e.startSize = Random.Range(0.07f, 0.12f);
                e.startLifetime = Random.Range(2.6f, 3.6f);
                e.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                e.angularVelocity3D = new Vector3(Random.Range(-500f, 500f), Random.Range(-200f, 200f), Random.Range(-500f, 500f));
                skitter.Emit(e, 1);
            }
        }

        /// <summary>
        /// Вихрь листьев: на сильном порыве у края двора (на гравии, сбоку от арены) горсть опавших листьев закручивается
        /// столбиком — кружит, поднимается до пояса и рассыпается. В дождь мокрые листья не летят, в снег — нет листвы.
        /// </summary>
        void Whirl()
        {
            Weather w = Weather.Instance;
            if (w != null && (w.Raining || w.Snowing)) return;
            if (whirl == null) whirl = BuildWhirl();
            if (whirl == null) return;
            float side = Random.value < 0.5f ? -1f : 1f;
            whirl.transform.position = new Vector3(side * Random.Range(4.3f, 5.4f), 0.02f, Random.Range(-2.5f, 3f));
            ParticleSystem.EmitParams e = new ParticleSystem.EmitParams();
            int n = Random.Range(12, 18);
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(0.15f, 0.55f);
                e.position = new Vector3(Mathf.Cos(a) * r, Random.Range(0f, 0.15f), Mathf.Sin(a) * r);
                e.velocity = new Vector3(0f, Random.Range(0.5f, 1.2f), 0f);
                e.startSize = Random.Range(0.06f, 0.11f);
                e.startLifetime = Random.Range(2.2f, 3.4f);
                e.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                e.angularVelocity3D = new Vector3(Random.Range(-400f, 400f), Random.Range(-300f, 300f), Random.Range(-400f, 400f));
                whirl.Emit(e, 1);
            }
        }

        ParticleSystem BuildWhirl()
        {
            GameObject g = new GameObject("LeafWhirl");
            g.transform.SetParent(transform, false);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.startSpeed = 0f; main.maxParticles = 30;
            main.gravityModifier = 0.12f; main.startRotation3D = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.5f, 0.45f, 1f), new Color(1f, 0.82f, 0.75f, 1f));
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            // кружение вокруг оси вихря: орбита слабеет к концу жизни, лёгкий разлёт наружу — вихрь распадается
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            AnimationCurve orb = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0.1f));
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, orb); vel.orbitalY = new ParticleSystem.MinMaxCurve(7f, orb); vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, orb);   // все три — в одном режиме
            AnimationCurve rad = new AnimationCurve(new Keyframe(0f, -0.2f), new Keyframe(0.55f, 0f), new Keyframe(1f, 1f));
            vel.radial = new ParticleSystem.MinMaxCurve(1.2f, rad);
            vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.5f; noise.frequency = 1.4f;
            var colLife = ps.colorOverLifetime; colLife.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colLife.color = gr;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer pr = skitter != null ? skitter.GetComponent<ParticleSystemRenderer>() : null;
            if (pr != null && pr.sharedMaterial != null) r.sharedMaterial = pr.sharedMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.World;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        ParticleSystem BuildSkitter()
        {
            GameObject g = new GameObject("LeafSkitter");
            g.transform.SetParent(transform, false);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true; main.playOnAwake = false;
            main.startSpeed = 0f; main.maxParticles = 40;
            main.gravityModifier = 0.35f;
            main.startRotation3D = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.5f, 0.45f, 1f), new Color(1f, 0.82f, 0.75f, 1f));
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            // скользят по камню: у земли сильное трение (лимит скорости с затуханием), подпрыгивают
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = new ParticleSystem.MinMaxCurve(3.2f); lim.dampen = 0.08f;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 1.1f;
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes; col.dampen = 0.5f; col.bounce = 0.25f; col.lifetimeLoss = 0f;
            GameObject floor = new GameObject("SkitterFloor"); floor.transform.SetParent(g.transform, false); floor.transform.position = new Vector3(0f, 0.01f, 0f);
            col.SetPlane(0, floor.transform);
            var colLife = ps.colorOverLifetime; colLife.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            colLife.color = gr;
            ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer pr = petals != null ? petals.GetComponent<ParticleSystemRenderer>() : null;
            if (pr != null && pr.sharedMaterial != null) r.sharedMaterial = pr.sharedMaterial;   // тот же лист клёна
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.World;   // плоский лист кувыркается в 3D (бывает и ребром)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>Порыв: шум, отфильтрованный полосой, которая медленно уходит вверх и вниз; нарастание ~1 с, хвост ~3 с.</summary>
        /// <summary>
        /// Шелест листвы (петля 6 с без шва): шум в полосе 2–6 кГц с медленной «дышащей» огибающей и россыпью
        /// коротких сухих тиков — листья задевают друг друга. Начало и конец сшиты перекрёстным затуханием.
        /// </summary>
        static AudioClip Rustle()
        {
            const int rate = 22050;
            int n = (int)(rate * 6f), fade = rate / 2;
            float[] d = new float[n + fade];
            System.Random r = new System.Random(23);
            float lp = 0f, hp = 0f, prev = 0f, tick = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float tt = i / (float)rate;
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                hp = 0.86f * (hp + x - prev); prev = x;   // срез низов
                lp += 0.55f * (hp - lp);                   // и верхов: полоса ~2–6 кГц
                if (r.NextDouble() < 0.0016) tick = 0.6f + 0.6f * (float)r.NextDouble();   // сухой тик листа
                tick *= 0.985f;
                float breathe = 0.55f + 0.45f * Mathf.Sin(tt * 1.7f + Mathf.Sin(tt * 0.63f) * 2f);
                d[i] = lp * breathe + x * tick * 0.5f;
            }
            float[] o = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = d[i];
                if (i < fade) { float k = i / (float)fade; v = d[i] * k + d[n + i] * (1f - k); }   // шов петли
                o[i] = v;
            }
            float peak = 1e-4f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(o[i]));
            for (int i = 0; i < n; i++) o[i] *= 0.8f / peak;
            AudioClip c = AudioClip.Create("leaf_rustle", n, 1, rate, false);
            c.SetData(o, 0);
            return c;
        }

        static AudioClip Whoosh()
        {
            const int rate = 22050;
            int n = (int)(rate * 4.2f);
            float[] d = new float[n];
            System.Random r = new System.Random(17);
            float lp1 = 0f, lp2 = 0f, bp = 0f, peak = 1e-4f;
            for (int i = 0; i < n; i++)
            {
                float tt = i / (float)rate;
                float env = tt < 1.1f ? Mathf.SmoothStep(0f, 1f, tt / 1.1f) : Mathf.Exp(-(tt - 1.1f) * 1.1f);
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                // полоса 250–900 Гц, «дышит» с порывом
                float fc = 300f + 500f * env + 120f * Mathf.Sin(tt * 3.1f);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * fc / rate);
                lp1 += a * (x - lp1); lp2 += a * 0.5f * (lp1 - lp2);
                bp = lp1 - lp2;
                float v = bp * env * (0.8f + 0.2f * Mathf.Sin(tt * 7.3f + Mathf.Sin(tt * 2.1f)));
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.7f / peak;
            AudioClip c = AudioClip.Create("gust", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
