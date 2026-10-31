using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Погода матча: ясная ночь или ливень с грозой (выбирается случайно на каждый матч).
    /// Дождь — частицы-штрихи вокруг камеры + брызги на земле, шум дождя; молния — вспышка лунного света и неба, гром с задержкой.
    /// </summary>
    public class Weather : MonoBehaviour
    {
        public static Weather Instance { get; private set; }
        [Range(0f, 1f)] public float rainChance = 0.4f, snowChance = 0.25f;
        public bool Raining { get; private set; }
        /// <summary>Первый снег: хлопья, побелевший камень, снег на плечах и шлемах, пар дыхания.</summary>
        public bool Snowing { get; private set; }
        /// <summary>Солнечный день (иногда, раз на матч): дневной свет и небо (DayNight), днём не бывает дождя и снега.</summary>
        public bool Daytime { get; private set; }
        [Range(0f, 1f)] public float dayChance = 0.45f;
        /// <summary>Рассвет в тумане (часть «дневных» матчей): низкое солнце, розоватая дымка, на рассвете поют хигураси.</summary>
        public bool Dawn { get; private set; }
        /// <summary>Закат (из дневных матчей): солнце за храмом, контровой свет, хигураси.</summary>
        public bool Dusk { get; private set; }
        [Range(0f, 1f)] public float duskShare = 0.4f;   // доля закатов среди не-рассветных дней
        [Range(0f, 1f)] public float dawnShare = 0.35f;
        float fogDensBase = 0.05f;
        ParticleSystem snow;
        readonly System.Collections.Generic.List<ParticleSystem> breaths = new System.Collections.Generic.List<ParticleSystem>();
        float snowK, nextBreathAt;
        static readonly int SnowId = Shader.PropertyToID("_BP_Snow");

        ParticleSystem rain, splash, ripples;
        AudioSource rainAudio, thunderAudio;
        AudioClip thunder;
        Light moon;
        float moonBase, ambBaseK = 1f;
        Color ambBase, fogBase;
        float nextFlashAt, flashT = -10f, thunderAt = -1f;
        Material skyMat;
        float skyExpBase;

        void Awake() { Instance = this; }

        void Start()
        {
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { moon = l; break; }
            CaptureBase();
            skyMat = RenderSettings.skybox;
            Roll();
        }

        /// <summary>База света, тумана и неба (после смены времени суток — заново): от неё считают дождь, снег и молнии.</summary>
        void CaptureBase()
        {
            if (moon != null) moonBase = moon.intensity;
            ambBase = RenderSettings.ambientLight;
            fogBase = RenderSettings.fogColor;
            fogDensBase = RenderSettings.fogDensity;
            skyMat = RenderSettings.skybox;
            if (skyMat != null && skyMat.HasProperty("_Exposure")) skyExpBase = skyMat.GetFloat("_Exposure");
        }

        /// <summary>Новый матч — новая погода.</summary>
        public void Roll()
        {
            // время суток: сперва свет и небо, потом — база для погоды
            if (Raining) StopRain();
            if (Snowing) StopSnow();
            Raining = false; Snowing = false;
            Daytime = DayNight.Instance != null && Random.value < dayChance;
            Dawn = Daytime && Random.value < dawnShare;
            Dusk = Daytime && !Dawn && Random.value < duskShare;
            if (DayNight.Instance != null) DayNight.Instance.Apply(Daytime, Dawn, Dusk);
            CaptureBase();
            float r = Random.value;
            Raining = !Daytime && r < rainChance;
            Snowing = !Daytime && !Raining && r < rainChance + snowChance;
            if (Raining) StartRain(); else StopRain();
            if (Snowing) StartSnow(); else StopSnow();
            if (Dawn) EnsureBreaths();   // утро холодное — изо рта пар
            Critters(!Raining && !Snowing && !Daytime);
            DayCritters(Daytime);
            nextFlashAt = Time.time + Random.Range(5f, 12f);
        }

        /// <summary>Капель с карниза храма (EnvironmentBuilder.AddEaveDrips) — только в дождь.</summary>
        void Eaves(bool on)
        {
            GameObject g = GameObject.Find("EaveDrips");
            ParticleSystem ps = g != null ? g.GetComponent<ParticleSystem>() : null;
            if (ps == null) return;
            var em = ps.emission; em.rateOverTime = on ? (DuelManager.Gfx == 1 ? 90f : 45f) : 0f;
            if (on && !ps.isPlaying) ps.Play();
        }

        /// <summary>Стрекозы — только ясным днём.</summary>
        void DayCritters(bool on)
        {
            GameObject g = GameObject.Find("Dragonflies");
            ParticleSystem ps = g != null ? g.GetComponent<ParticleSystem>() : null;
            if (ps == null) return;
            var em = ps.emission; em.enabled = on;
            if (!on) ps.Clear(); else if (!ps.isPlaying) ps.Play();
        }

        /// <summary>Светлячки и мотыльки у фонарей — только в ясную ночь (в дождь и снег прячутся).</summary>
        void Critters(bool on)
        {
            foreach (ParticleSystem ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.name != "Fireflies" && ps.name != "Moths") continue;
                var em = ps.emission; em.enabled = on;
                if (!on) ps.Clear();
                else if (!ps.isPlaying) ps.Play();
            }
        }

        void StartRain()
        {
            if (rain == null) Build();
            var re = rain.emission; re.rateOverTime = DuelManager.Gfx == 1 ? 700f : 300f;
            rain.Play(); splash.Play(); if (ripples != null) ripples.Play();
            if (rainAudio != null && !rainAudio.isPlaying) rainAudio.Play();
            RenderSettings.fogDensity = fogDensBase * 1.3f;
            Eaves(true);
            Wet(true);
            RenderSettings.fogColor = fogBase * 0.85f;
        }

        void StopRain()
        {
            if (rain != null) { rain.Stop(); splash.Stop(); }
            if (ripples != null) ripples.Stop();
            if (rainAudio != null) rainAudio.Stop();
            RenderSettings.fogDensity = fogDensBase;
            Eaves(false);
            Wet(false);
            RenderSettings.fogColor = fogBase;
        }

        void StartSnow()
        {
            if (snow == null) BuildSnow();
            var em = snow.emission; em.rateOverTime = DuelManager.Gfx == 1 ? 140f : 70f;
            snow.Play();
            RenderSettings.fogDensity = fogDensBase * 1.2f;
            RenderSettings.fogColor = Color.Lerp(fogBase, new Color(0.42f, 0.46f, 0.56f), 0.6f);
            Snowy(true);
            EnsureBreaths();
        }

        /// <summary>Пар дыхания у голов бойцов (в снег и холодным туманным рассветом).</summary>
        void EnsureBreaths()
        {
            foreach (HeroRigDriver d in FindObjectsByType<HeroRigDriver>(FindObjectsSortMode.None))
            {
                if (d.head == null) continue;
                bool has = false;
                foreach (ParticleSystem b in breaths) if (b != null && b.transform.parent == d.head) has = true;
                if (!has) breaths.Add(BuildBreath(d.head, d.fighter != null ? d.fighter.transform : d.transform));
            }
        }

        void StopSnow()
        {
            if (snow != null) snow.Stop();
            Snowy(false);
            if (!Raining) { RenderSettings.fogDensity = fogDensBase; RenderSettings.fogColor = fogBase; }
        }

        Color floorColor0 = Color.white;
        /// <summary>Камень припорошён: светлее и холоднее, матовый.</summary>
        void Snowy(bool on)
        {
            if (floorMat == null) { Wet(Raining); }
            if (floorMat == null) return;
            if (on)
            {
                // сам снег рисует шейдер окружения (BladePath/Env по _BP_Snow) — здесь только холоднее и светлее под ним
                floorMat.color = new Color(floorColor0.r * 1.12f, floorColor0.g * 1.15f, floorColor0.b * 1.24f, 1f);
                if (floorMat.HasProperty("_Glossiness")) floorMat.SetFloat("_Glossiness", 0.25f);
                if (floorMat.IsKeywordEnabled("_METALLICGLOSSMAP") && floorMat.HasProperty("_GlossMapScale")) floorMat.SetFloat("_GlossMapScale", 0.35f);
            }
            else if (!Raining) Wet(false);
        }

        void BuildSnow()
        {
            Shader sh = Shader.Find("Sprites/Default");
            Material m = new Material(sh); m.mainTexture = SoftSprites.Disc;
            GameObject g = new GameObject("Snow");
            g.transform.position = new Vector3(0f, 7f, 1f);
            snow = g.AddComponent<ParticleSystem>();
            snow.Stop();
            var main = snow.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 9f; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new Color(0.92f, 0.94f, 1f, 0.85f);
            main.gravityModifier = 0.07f; main.maxParticles = 1400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shp = snow.shape; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = new Vector3(16f, 0.5f, 12f);
            var noise = snow.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.25f; noise.scrollSpeed = 0.15f;
            var vel = snow.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(0.25f); vel.y = new ParticleSystem.MinMaxCurve(-0.35f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var col = snow.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes; col.SetPlane(0, MakeFloorPlane()); col.lifetimeLoss = 1f; col.bounce = 0f;
            var r = g.GetComponent<ParticleSystemRenderer>(); r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        Transform floorPlane;
        Transform MakeFloorPlane()
        {
            if (floorPlane == null) { floorPlane = new GameObject("SnowFloor").transform; floorPlane.position = new Vector3(0f, 0.01f, 0f); }
            return floorPlane;
        }

        static ParticleSystem BuildBreath(Transform head, Transform fighter)
        {
            GameObject g = new GameObject("Breath");
            g.transform.SetParent(head, false);
            g.transform.position = head.position + fighter.forward * 0.12f - Vector3.up * 0.06f;
            g.transform.rotation = Quaternion.LookRotation(fighter.forward + Vector3.down * 0.2f);
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = 0.4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f); main.startColor = new Color(0.85f, 0.88f, 0.95f, 0.12f);
            main.maxParticles = 20; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });
            var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Cone; shp.angle = 14f; shp.radius = 0.01f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 3.5f)));
            var colm = ps.colorOverLifetime; colm.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colm.color = gr;
            var r = g.GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Sprites/Default");
            Material m = new Material(sh); m.mainTexture = SoftSprites.Disc; r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        Material floorMat; float floorGloss, floorGlossMap = -1f;
        /// <summary>Мокрый камень арены: гладкость растёт, появляются блики от огня и молний.</summary>
        void Wet(bool on)
        {
            if (floorMat == null)
            {
                GameObject f = GameObject.Find("ArenaFloor");
                if (f == null) return;
                Renderer r = f.GetComponent<Renderer>();
                if (r == null) return;
                floorMat = r.material;
                floorGloss = floorMat.HasProperty("_Glossiness") ? floorMat.GetFloat("_Glossiness") : 0.35f;
                floorColor0 = floorMat.color;
            }
            if (floorMat.HasProperty("_Glossiness")) floorMat.SetFloat("_Glossiness", on ? 0.82f : floorGloss);
            // цвет — исходный оттенок мостовой (раньше сухой пол становился белым, на телефоне ярче задуманного);
            // мокрое потемнение делает шейдер окружения по _BP_Wetness
            floorMat.color = floorColor0;
            // пол с картой гладкости (мостовая): блеск задаёт масштаб карты, а не _Glossiness
            if (floorMat.IsKeywordEnabled("_METALLICGLOSSMAP") && floorMat.HasProperty("_GlossMapScale"))
            {
                if (floorGlossMap < 0f) floorGlossMap = floorMat.GetFloat("_GlossMapScale");
                floorMat.SetFloat("_GlossMapScale", on ? 1f : floorGlossMap);
            }
        }

        void Build()
        {
            Shader sh = Shader.Find("Sprites/Default");
            Material m = new Material(sh); m.mainTexture = SoftSprites.Disc;

            GameObject g = new GameObject("Rain");
            rain = g.AddComponent<ParticleSystem>();
            rain.Stop();
            var main = rain.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 0.9f;
            main.startSpeed = 11f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
            main.startColor = new Color(0.75f, 0.8f, 0.95f, 0.35f);
            main.maxParticles = 900;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = rain.emission; em.rateOverTime = 700f;
            var shp = rain.shape; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = new Vector3(14f, 12f, 0.1f);   // после поворота на 90° по X: локальная Y лежит горизонтально
            g.transform.rotation = Quaternion.Euler(95f, 0f, 0f);   // струи вниз, чуть наискось
            g.transform.position = new Vector3(0f, 8f, 1f);
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.material = m; r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.045f; r.lengthScale = 1f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            GameObject s = new GameObject("RainSplash");
            s.transform.position = new Vector3(0f, 0.02f, 0.5f);
            s.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            splash = s.AddComponent<ParticleSystem>();
            splash.Stop();
            var sm = splash.main;
            sm.loop = true; sm.startLifetime = 0.25f; sm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            sm.startColor = new Color(0.7f, 0.7f, 0.72f, 0.35f);
            sm.gravityModifier = 1f; sm.maxParticles = 400;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var se = splash.emission; se.rateOverTime = 260f;
            var ss = splash.shape; ss.shapeType = ParticleSystemShapeType.Box; ss.scale = new Vector3(10f, 7f, 0.01f);
            var sr = s.GetComponent<ParticleSystemRenderer>(); sr.material = m;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // круги от капель на мокрых плитах: кольца лежат на земле, расходятся и тают
            GameObject rp = new GameObject("RainRipples");
            rp.transform.position = new Vector3(0f, 0.012f, 0f);
            rp.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ripples = rp.AddComponent<ParticleSystem>();
            ripples.Stop();
            var rm = ripples.main;
            rm.loop = true; rm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f); rm.startSpeed = 0f;
            rm.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            rm.startColor = new Color(0.75f, 0.8f, 0.9f, 0.32f);
            rm.maxParticles = 160; rm.simulationSpace = ParticleSystemSimulationSpace.World;
            var re = ripples.emission; re.rateOverTime = 95f;
            var rs = ripples.shape; rs.shapeType = ParticleSystemShapeType.Box; rs.scale = new Vector3(7f, 5.2f, 0.01f);
            var rsz = ripples.sizeOverLifetime; rsz.enabled = true;
            rsz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.1f, 0f, 2.5f), new Keyframe(1f, 1f, 0f, 0f)));
            var rc = ripples.colorOverLifetime; rc.enabled = true;
            Gradient rg = new Gradient();
            rg.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new GradientAlphaKey[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            rc.color = rg;
            var rr = rp.GetComponent<ParticleSystemRenderer>();
            Material rmat = new Material(sh); rmat.mainTexture = SoftSprites.Ring;
            rr.material = rmat; rr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;

            rainAudio = gameObject.AddComponent<AudioSource>();
            rainAudio.clip = RainClip(); rainAudio.loop = true; rainAudio.volume = 0.35f; rainAudio.spatialBlend = 0f;
            thunderAudio = gameObject.AddComponent<AudioSource>();
            thunder = ThunderClip(); thunderAudio.spatialBlend = 0f;
        }

        float wetK;
        static readonly int WetId = Shader.PropertyToID("_BP_Wetness");

        void Update()
        {
            // бойцы промокают под дождём за несколько секунд (шейдер персонажей: ткань темнеет, доспех и кожа блестят)
            wetK = Mathf.MoveTowards(wetK, Raining ? 1f : 0f, Time.deltaTime / (Raining ? 5f : 1f));
            Shader.SetGlobalFloat(WetId, wetK);
            // снег оседает на плечах и шлемах за полминуты боя; пар дыхания раз в пару секунд
            snowK = Mathf.MoveTowards(snowK, Snowing ? 0.85f : 0f, Time.deltaTime / (Snowing ? 30f : 1f));
            Shader.SetGlobalFloat(SnowId, snowK);
            if ((Snowing || Dawn) && Time.time >= nextBreathAt)
            {
                nextBreathAt = Time.time + Random.Range(1.6f, 2.6f);
                foreach (ParticleSystem b in breaths) if (b != null && b.transform.parent != null && b.transform.parent.gameObject.activeInHierarchy) b.Play();
            }
            if (!Raining) { Restore(); return; }

            // молния: двойная вспышка, гром через 0,3…1,8 с
            if (Time.time >= nextFlashAt)
            {
                flashT = Time.unscaledTime;
                nextFlashAt = Time.time + Random.Range(7f, 18f);
                thunderAt = Time.unscaledTime + Random.Range(0.3f, 1.8f);
            }
            if (thunderAt > 0f && Time.unscaledTime >= thunderAt)
            {
                thunderAt = -1f;
                if (thunderAudio != null) { thunderAudio.pitch = Random.Range(0.8f, 1.1f); thunderAudio.PlayOneShot(thunder, 0.9f); }
                Haptics.Pulse(30);
            }
            float t = Time.unscaledTime - flashT;
            float f = 0f;
            if (t < 0.6f) f = Mathf.Max(Spike(t, 0f, 0.07f), 0.7f * Spike(t, 0.16f, 0.1f), 0.45f * Spike(t, 0.32f, 0.25f));
            if (moon != null) moon.intensity = moonBase * (0.75f + 3.5f * f);
            RenderSettings.ambientLight = ambBase * (0.85f + 2.5f * f);
            if (skyMat != null && skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", skyExpBase * (0.8f + 4f * f));
        }

        static float Spike(float t, float at, float len) { float u = (t - at) / len; return u < 0f || u > 1f ? 0f : (1f - u) * (1f - u); }

        void Restore()
        {
            if (moon != null) moon.intensity = moonBase;
            RenderSettings.ambientLight = ambBase;
            if (skyMat != null && skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", skyExpBase);
        }

        void OnDestroy() { Restore(); if (Instance == this) Instance = null; }

        const int Rate = 22050;
        static AudioClip RainClip()
        {
            int n = Rate * 4, m = Rate / 3;
            float[] raw = new float[n + m];
            System.Random rnd = new System.Random(9);
            float lp = 0f, hp = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += 0.35f * (x - lp);
                hp += 0.02f * (lp - hp);
                float drop = rnd.NextDouble() < 0.002 ? (float)(rnd.NextDouble() * 0.6) : 0f;
                raw[i] = (lp - hp) * 0.55f + drop;
            }
            float[] d = new float[n];
            for (int i = 0; i < n; i++) d[i] = raw[i];
            for (int i = 0; i < m; i++) { float k = i / (float)m; d[i] = raw[i] * k + raw[n + i] * (1f - k); }
            AudioClip c = AudioClip.Create("rain", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        static AudioClip ThunderClip()
        {
            int n = (int)(Rate * 3.5f);
            float[] d = new float[n];
            System.Random rnd = new System.Random(4);
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp1 += 0.03f * (x - lp1);
                lp2 += 0.008f * (x - lp2);
                float env = Mathf.Min(1f, t * 8f) * Mathf.Exp(-t * 1.1f) * (0.7f + 0.3f * Mathf.Sin(t * 9f + Mathf.Sin(t * 3f) * 2f));
                float crack = t < 0.25f ? lp1 * 2f * (1f - t / 0.25f) : 0f;
                d[i] = Mathf.Clamp((lp2 * 9f + lp1 * 3f) * env + crack, -1f, 1f);
            }
            AudioClip c = AudioClip.Create("thunder", n, 1, Rate, false); c.SetData(d, 0); return c;
        }
    }
}
