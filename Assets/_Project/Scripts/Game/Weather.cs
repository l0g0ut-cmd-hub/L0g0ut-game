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
        [Range(0f, 1f)] public float dayChance = 0.62f;
        /// <summary>Рассвет в тумане (часть «дневных» матчей): низкое солнце, розоватая дымка, на рассвете поют хигураси.</summary>
        public bool Dawn { get; private set; }
        /// <summary>Закат (из дневных матчей): солнце за храмом, контровой свет, хигураси.</summary>
        public bool Dusk { get; private set; }
        [Range(0f, 1f)] public float duskShare = 0.42f;   // доля закатов среди не-рассветных дней
        /// <summary>Ночной туман (из ясных ночей): густая холодная дымка, огни в ореолах, по земле стелется молоко.</summary>
        public bool Foggy { get; private set; }
        /// <summary>Снегопад сильный (крупно и густо, листья не летят) или слабый (редкий снег, иногда кружат последние листья).</summary>
        public bool HeavySnow { get; private set; }
        /// <summary>Ветреность боя: 0,35 — тихо (редкие слабые порывы), 1 — обычно, 1,6 — ветрено (WindGusts).</summary>
        public float Windiness { get; private set; } = 1f;
        /// <summary>Что падает с деревьев: 0 — ничего, 1 — листья клёна, 2 — лепестки сакуры, 3 — редкие листья клёна.</summary>
        public static int LeafMode { get; private set; } = 1;
        /// <summary>Счётчик бросков погоды (новый раунд/бой) — чтобы декор мог заново выбрать своё (монахи за сёдзи и т.п.).</summary>
        public static int RollId { get; private set; }
        /// <summary>Изредка ясной ночью — кровавая луна: диск и лунный свет багровые, туман и тени тёплые.</summary>
        public bool BloodMoon { get; private set; }
        /// <summary>Множитель лунного света (облако на луне — MoonClouds).</summary>
        public static float MoonDim = 1f;
        [Range(0f, 1f)] public float fogChance = 0.3f;
        ParticleSystem mistPs; float mistRate0 = -1f; Color mistCol0;
        /// <summary>Погода меняется по ходу боя: ясной ночью иногда через полминуты-минуту накрывает дождь (с громом) или начинается снег.</summary>
        [Range(0f, 1f)] public float changeChance = 0.3f;
        float changeAt = -1f; bool changeToSnow, rumbled;
        [Range(0f, 1f)] public float dawnShare = 0.3f;
        float fogDensBase = 0.05f;
        ParticleSystem snow;
        readonly System.Collections.Generic.List<ParticleSystem> breaths = new System.Collections.Generic.List<ParticleSystem>();
        readonly System.Collections.Generic.List<ParticleSystem> steams = new System.Collections.Generic.List<ParticleSystem>();   // пар от разгорячённых тел на морозе
        float snowK, nextBreathAt;
        static readonly int SnowId = Shader.PropertyToID("_BP_Snow");

        ParticleSystem rain, splash, ripples, crowns;
        Transform bolt; Material boltMat;
        AudioSource rainAudio, thunderAudio;
        AudioClip thunder;
        Light moon;
        float moonBase, ambBaseK = 1f;
        Color ambBase, fogBase;
        float nextFlashAt, flashT = -10f, thunderAt = -1f;
        Material skyMat;
        float skyExpBase;

        void Awake() { Instance = this; GlowSprite.Haze = 1f; if (GetComponent<SnowDrifts>() == null) gameObject.AddComponent<SnowDrifts>(); if (Application.isPlaying && GetComponent<Butterflies>() == null) gameObject.AddComponent<Butterflies>(); }   // сугробы растут по _BP_Snow

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
        public static bool ForceNight;
        /// <summary>Погода с карты похода (MapClouds): −1 — как раньше, случайно; 0 — ясно; 1 — дождевое облако; 2 — грозовое.</summary>
        public static int MapSky = -1;
        /// <summary>Время суток с карты похода (DayCycle): −1 — случайно, как раньше; 0 утро, 1 день, 2 вечер, 3 ночь.</summary>
        public static int MapTime = -1;
        /// <summary>Дождь и гроза выключены (меню разработчика): ни в бою, ни на карте.</summary>
        public static bool NoRain
        {
            get { if (noRain < 0) noRain = PlayerPrefs.GetInt("wx_norain", 0); return noRain == 1; }
            set { noRain = value ? 1 : 0; PlayerPrefs.SetInt("wx_norain", noRain); PlayerPrefs.Save(); }
        }
        static int noRain = -1;
        /// <summary>Гроза: дождь с молниями и громом. Дождь от обычного облака — без молний.</summary>
        public bool Storm { get; private set; }
        public void Roll()
        {
            // время суток: сперва свет и небо, потом — база для погоды
            if (Raining) StopRain();
            if (Snowing) StopSnow();
            if (Foggy) StopFog();
            Raining = false; Snowing = false; Foggy = false;
            // ночь не больше двух боёв подряд; ясный (не рассвет/закат) день — не реже раза в четыре боя
            Daytime = DayNight.Instance != null && (nightStreak >= 2 || Random.value < dayChance) && !ForceNight;   // засада на карте — всегда ночью
            Dawn = Daytime && Random.value < dawnShare;
            Dusk = Daytime && !Dawn && Random.value < duskShare;
            if (Daytime && noClearDay >= 4) { Dawn = false; Dusk = false; }
            // время суток с карты (или заданное в меню разработчика): утро — рассвет, день — ясный день, вечер — закат, ночь — ночь
            int tod = DayCycle.FightTime(MapTime);
            if (tod >= 0 && DayNight.Instance != null && !(ForceNight && tod != DayCycle.Night && DayCycle.Mode == DayCycle.Auto))
            {
                Daytime = tod != DayCycle.Night; Dawn = tod == DayCycle.Morning; Dusk = tod == DayCycle.Evening;
            }
            nightStreak = Daytime ? 0 : nightStreak + 1;
            noClearDay = Daytime && !Dawn && !Dusk ? 0 : noClearDay + 1;
            if (DayNight.Instance != null) DayNight.Instance.Apply(Daytime, Dawn, Dusk);
            CaptureBase();
            float r = Random.value;
            Raining = !Daytime && r < rainChance;
            Snowing = !Daytime && !Raining && r < rainChance + snowChance;
            Storm = Raining;
            if (MapSky >= 0)
            {
                // погоду определило облако над местом боя на карте (время суток — как раньше)
                Raining = MapSky >= 1; Storm = MapSky == 2; Snowing = false;
            }
            if (NoRain) { Raining = false; Storm = false; }
            if (Raining) StartRain(); else StopRain();
            if (Snowing) StartSnow(); else StopSnow();
            Foggy = MapSky < 0 && !Daytime && !Raining && !Snowing && Random.value < fogChance;
            if (Foggy) StartFog(); else StopFog();
            changeAt = MapSky < 0 && !Daytime && !Raining && !Snowing && Random.value < changeChance ? Time.time + Random.Range(25f, 55f) : -1f;
            changeToSnow = NoRain || Random.value < 0.4f; rumbled = false;   // без дождя — смена погоды только на снег
            if (Dawn) EnsureBreaths();   // утро холодное — изо рта пар
            Critters(!Raining && !Snowing && !Daytime);
            DayCritters(Daytime);
            nextFlashAt = Time.time + Random.Range(5f, 12f);
            // ветер — не каждый бой: тихо, обычно или ветрено (в сильный снег — метёт)
            float wr = Random.value;
            Windiness = Snowing && HeavySnow ? 1.6f : wr < 0.4f ? 0.35f : wr < 0.8f ? 1f : 1.6f;
            // листопад: ясным днём (с рассветом и закатом) иногда сакура; в снег — смотря по силе снега
            // сакура днём — 50 %; два дневных боя подряд без неё — в третьем точно
            bool canSakura = Daytime && !Raining && !Snowing;
            // рассвет/закат: сакура 80 %, а если в прошлый рассвет/закат её не было — точно
            bool edge = Dawn || Dusk;
            sakuraRoll = canSakura && (noSakura >= 2 || (edge && (!lastEdgeSakura || Random.value < 0.8f)) || Random.value < 0.5f);
            if (canSakura) noSakura = sakuraRoll ? 0 : noSakura + 1;
            if (edge) lastEdgeSakura = sakuraRoll;
            lightSnowLeaves = Random.value < 0.5f;
            Leaves();
            // рассвет с сакурой — ясный: без утренней дымки (обычный рассвет остаётся туманным)
            if (Dawn && LeafMode == 2) { RenderSettings.fogDensity = Mathf.Min(RenderSettings.fogDensity, 0.024f); fogDensBase = RenderSettings.fogDensity; }
            BloodMoon = !Daytime && !Raining && !Snowing && !Foggy && changeAt < 0f && Random.value < 0.12f;
            Blood(BloodMoon);
            RollId++;
        }

        bool sakuraRoll, lightSnowLeaves;
        static int nightStreak, noClearDay, noSakura;   // счётчики серий между боями (на всю сессию)
        static bool lastEdgeSakura;   // была ли сакура в прошлый рассвет/закат
        Renderer[] moonRends; Color[] moonCols;
        void Blood(bool on)
        {
            if (!Application.isPlaying) return;
            if (moonRends == null)
            {
                var list = new System.Collections.Generic.List<Renderer>();
                foreach (string nm in new[] { "Moon", "MoonHalo" }) { GameObject g = GameObject.Find(nm); if (g != null) list.AddRange(g.GetComponentsInChildren<Renderer>()); }
                moonRends = list.ToArray(); moonCols = new Color[moonRends.Length];
                for (int i = 0; i < moonRends.Length; i++) { Material m = moonRends[i].material; moonCols[i] = m.HasProperty("_TintColor") ? m.GetColor("_TintColor") : m.HasProperty("_Color") ? m.color : Color.white; }
            }
            Color blood = new Color(1f, 0.36f, 0.24f);
            for (int i = 0; i < moonRends.Length; i++)
            {
                if (moonRends[i] == null) continue;
                Material m = moonRends[i].material;
                Color c = on ? new Color(moonCols[i].r * blood.r, moonCols[i].g * blood.g, moonCols[i].b * blood.b, moonCols[i].a) : moonCols[i];
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c); else if (m.HasProperty("_Color")) m.color = c;
            }
            if (!on) return;
            // лунный свет и воздух — в багровый (база для Restore и смены погоды тоже)
            if (moon != null) moon.color = Color.Lerp(moon.color, new Color(1f, 0.42f, 0.32f), 0.65f);
            ambBase = new Color(ambBase.r * 1.25f, ambBase.g * 0.78f, ambBase.b * 0.78f);
            fogBase = new Color(fogBase.r * 1.35f, fogBase.g * 0.72f, fogBase.b * 0.72f);
            RenderSettings.fogColor = fogBase; RenderSettings.ambientLight = ambBase;
        }
        Transform sakuraGround;
        Material crownMat; Texture crownTex0; Color crownEm0; Texture sakuraTex; bool crownInit;
        /// <summary>
        /// Весна: когда падает сакура, кроны клёнов во дворе цветут — карточки крон получают текстуру соцветий сакуры
        /// (Resources/FX/sakura_cluster, tools/texgen/sakura_cluster.py) и розоватое свечение; иначе — красный клён.
        /// </summary>
        void Crowns(bool sakura)
        {
            if (!crownInit)
            {
                crownInit = true;
                sakuraTex = Resources.Load<Texture2D>("FX/sakura_cluster");
                foreach (Renderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("Env_MapleCrown")) { crownMat = r.sharedMaterial; break; }
                if (crownMat != null) { crownTex0 = crownMat.mainTexture; crownEm0 = crownMat.HasProperty("_EmissionColor") ? crownMat.GetColor("_EmissionColor") : Color.black; }
            }
            if (crownMat == null || sakuraTex == null) return;
            crownMat.mainTexture = sakura ? sakuraTex : crownTex0;
            if (crownMat.HasProperty("_EmissionColor")) crownMat.SetColor("_EmissionColor", sakura ? new Color(0.06f, 0.04f, 0.045f) : crownEm0);
        }

        void RestoreCrowns() { if (crownMat != null && crownTex0 != null) { crownMat.mainTexture = crownTex0; if (crownMat.HasProperty("_EmissionColor")) crownMat.SetColor("_EmissionColor", crownEm0); } }   // в редакторе ассет не должен остаться розовым
        ParticleSystem petalsPs; Texture mapleTex; bool petalsInit;
        static readonly Color MapleA = new Color(0.75f, 0.55f, 0.5f, 0.95f), MapleB = new Color(1f, 0.85f, 0.8f, 0.95f);
        /// <summary>
        /// Что падает с деревьев сейчас: сильный снег — ничего; слабый — то редкие листья клёна, то ничего; ясный день —
        /// иногда лепестки сакуры (мельче, розовые, кружат дольше); иначе — листья клёна.
        /// </summary>
        Texture2D petals8, leaves8;
        void Leaves()
        {
            int mode = 1;
            if (Snowing) mode = HeavySnow ? 0 : (lightSnowLeaves ? 3 : 0);
            else if (Daytime && !Raining && sakuraRoll) mode = 2;
            else if (Raining) mode = 3;   // в ливень мокрые листья почти не летят — редкие
            LeafMode = mode;
            if (!Application.isPlaying) return;   // кадры CI в редакторе: не трогать ассет материала и частицы сцены
            if (!petalsInit)
            {
                petalsInit = true;
                GameObject g = GameObject.Find("Petals");
                if (g != null) petalsPs = g.GetComponent<ParticleSystem>();
                if (petalsPs != null) { Renderer pr = petalsPs.GetComponent<Renderer>(); if (pr != null && pr.sharedMaterial != null) mapleTex = pr.sharedMaterial.mainTexture; }
            }
            Crowns(mode == 2);
            if (sakuraGround == null) { GameObject sg = GameObject.Find("SakuraGround"); if (sg != null) sakuraGround = sg.transform; }
            if (sakuraGround != null) foreach (Transform ch in sakuraGround) ch.gameObject.SetActive(mode == 2);   // опавшие лепестки на камне и гравии
            if (petalsPs == null) return;
            var main = petalsPs.main; var em = petalsPs.emission; var noise = petalsPs.noise;
            Renderer r = petalsPs.GetComponent<Renderer>();
            bool sakura = mode == 2;
            // атлас 38: восемь лепестков и восемь листьев (лист 4×2, у каждой частицы — свой кадр); без него — прежние текстуры
            if (petals8 == null) petals8 = Resources.Load<Texture2D>("FX/petals8");
            if (leaves8 == null) leaves8 = Resources.Load<Texture2D>("FX/leaves8");
            Texture tex = sakura ? (petals8 != null ? (Texture)petals8 : SoftSprites.Petal) : (leaves8 != null ? (Texture)leaves8 : mapleTex);
            bool sheet = tex != null && (tex == petals8 || tex == leaves8);
            if (r != null && r.sharedMaterial != null) r.sharedMaterial.mainTexture = tex;
            var ts = petalsPs.textureSheetAnimation;
            ts.enabled = sheet;
            if (sheet) { ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 4; ts.numTilesY = 2; ts.animation = ParticleSystemAnimationType.WholeSheet; ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f); ts.cycleCount = 1; }
            // цветные картинки не подкрашиваем (только разброс яркости), процедурные — как раньше
            main.startColor = sheet ? new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.82f, 0.8f, 0.8f, 0.95f))
                            : sakura ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.78f, 0.86f, 0.95f), new Color(1f, 0.93f, 0.96f, 0.95f))
                                     : new ParticleSystem.MinMaxGradient(MapleA, MapleB);
            float ks = sheet ? 1.3f : 1f;   // на картинке лист/лепесток занимает ~70 % кадра
            main.startSize = sakura ? new ParticleSystem.MinMaxCurve(0.035f * ks, 0.065f * ks) : new ParticleSystem.MinMaxCurve(0.07f * ks, 0.13f * ks);
            main.gravityModifier = sakura ? 0.03f : 0.05f;
            noise.strength = sakura ? 0.38f : 0.25f;
            em.rateOverTime = mode == 0 ? 0f : mode == 2 ? 24f : mode == 3 ? 3.5f : 14f;
            if (mode == 0) petalsPs.Clear();
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
            var re = rain.emission; re.rateOverTime = DuelManager.Gfx == 1 ? 520f : 220f;
            rain.Play(); splash.Play(); if (ripples != null) ripples.Play(); if (crowns != null) crowns.Play();
            if (rainAudio != null && !rainAudio.isPlaying) rainAudio.Play();
            RenderSettings.fogDensity = fogDensBase * 1.3f;
            Eaves(true);
            Wet(true);
            RenderSettings.fogColor = fogBase * 0.85f;
            if (!Foggy) Dome(0.35f);
        }

        void StopRain()
        {
            if (rain != null) rain.Stop();
            if (splash != null) splash.Stop();
            if (ripples != null) ripples.Stop();
            if (crowns != null) crowns.Stop();
            if (bolt != null) bolt.gameObject.SetActive(false);
            if (rainAudio != null) rainAudio.Stop();
            RenderSettings.fogDensity = fogDensBase;
            Eaves(false);
            Wet(false);
            RenderSettings.fogColor = fogBase;
            Dome(Foggy ? 0.9f : Snowing ? 0.45f : 0f);
        }

        void StartSnow()
        {
            if (snow == null) BuildSnow();
            HeavySnow = Random.value < 0.5f;
            var em = snow.emission; em.rateOverTime = (DuelManager.Gfx == 1 ? 110f : 48f) * (HeavySnow ? 1.3f : 0.45f);
            // сильный — хлопья крупнее и падают быстрее, слабый — мелкие редкие снежинки медленно кружат
            var sm = snow.main;
            sm.startSize = HeavySnow ? new ParticleSystem.MinMaxCurve(0.035f, 0.075f) : new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
            sm.gravityModifier = HeavySnow ? 0.1f : 0.05f;
            snow.Play();
            RenderSettings.fogDensity = fogDensBase * (HeavySnow ? 1.45f : 1.1f);
            RenderSettings.fogColor = Color.Lerp(fogBase, new Color(0.42f, 0.46f, 0.56f), 0.6f);
            Snowy(true);
            EnsureBreaths();
            if (!Foggy) Dome(0.45f);
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
                if (d.chest != null && !has) steams.Add(BuildSteam(d.chest));
            }
        }

        void Mist(float rateK, float alphaK)
        {
            if (mistPs == null) { GameObject g = GameObject.Find("GroundMist"); if (g != null) mistPs = g.GetComponent<ParticleSystem>(); }
            if (mistPs == null) return;
            var main = mistPs.main; var em = mistPs.emission;
            if (mistRate0 < 0f) { mistRate0 = em.rateOverTime.constant; mistCol0 = main.startColor.color; }
            em.rateOverTime = mistRate0 * rateK;
            main.maxParticles = Mathf.Max(main.maxParticles, Mathf.CeilToInt(40 * rateK));
            Color c = mistCol0; c.a = Mathf.Min(1f, mistCol0.a * alphaK); main.startColor = c;
        }

        void StartFog()
        {
            RenderSettings.fogDensity = fogDensBase * 3.2f;
            RenderSettings.fogColor = Color.Lerp(fogBase, new Color(0.26f, 0.29f, 0.35f), 0.7f);
            Mist(3.5f, 2.6f);
            Dome(0.9f);
        }

        void StopFog()
        {
            if (!Raining && !Snowing) { RenderSettings.fogDensity = fogDensBase; RenderSettings.fogColor = fogBase; }
            Mist(1f, 1f);
            Dome(Raining ? 0.35f : Snowing ? 0.45f : 0f);
        }

        void StopSnow()
        {
            if (snow != null) snow.Stop();
            Snowy(false);
            if (!Raining) { RenderSettings.fogDensity = fogDensBase; RenderSettings.fogColor = fogBase; }
            Dome(Foggy ? 0.9f : Raining ? 0.35f : 0f);
        }

        Renderer dome; Material domeMat;
        /// <summary>
        /// Туманный купол: туман Unity не трогает небо, и в густом тумане тёмное небо зияло дырами между светлыми от тумана
        /// кронами. Большая сфера вокруг двора цвета тумана (полупрозрачная) прячет небо: в тумане почти целиком, в дождь и
        /// снегопад — слегка размывает. a = 0 — купол убран.
        /// </summary>
        void Dome(float a)
        {
            if (a <= 0.001f) { if (dome != null) dome.enabled = false; return; }
            if (dome == null)
            {
                GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                g.name = "FogDome";
                Collider c = g.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
                g.transform.SetParent(transform, false);
                g.transform.position = new Vector3(0f, 0f, 2f); g.transform.localScale = Vector3.one * 90f;
                dome = g.GetComponent<Renderer>();
                domeMat = new Material(Shader.Find("Sprites/Default")) { name = "FogDome", renderQueue = 2502 };   // Cull Off — видно изнутри; сразу после неба, до прозрачных частиц (иначе вуалит искры и лепестки)
                dome.sharedMaterial = domeMat;
                dome.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; dome.receiveShadows = false;
            }
            Color fc = RenderSettings.fogColor; fc.a = a;
            domeMat.color = fc;
            dome.enabled = true;
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

        /// <summary>
        /// Пар от тела на морозе: над плечами и спиной разгорячённого бойца медленно поднимаются едва заметные струйки
        /// (эмиссия включается только в снег — см. Update).
        /// </summary>
        static ParticleSystem BuildSteam(Transform chest)
        {
            GameObject g = new GameObject("BodySteam");
            g.transform.SetParent(chest, false);
            g.transform.localPosition = Vector3.zero;
            ParticleSystem ps = g.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main; main.loop = true; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f); main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f); main.startColor = new Color(0.85f, 0.88f, 0.95f, 0.05f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.maxParticles = 16; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 4f; em.enabled = false;
            var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Sphere; shp.radius = 0.2f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f); vel.y = new ParticleSystem.MinMaxCurve(0.14f, 0.26f); vel.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.08f; noise.frequency = 0.8f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.6f)));
            var colm = ps.colorOverLifetime; colm.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            colm.color = gr;
            var r = g.GetComponent<ParticleSystemRenderer>();
            Material m = new Material(Shader.Find("Sprites/Default")); m.mainTexture = SoftSprites.Disc; r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
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
            Texture2D rpt = Resources.Load<Texture2D>("FX/ripple");   // несколько расходящихся колец (атлас), а не одно ровное
            Material rmat = new Material(sh); rmat.mainTexture = rpt != null ? rpt : SoftSprites.Ring;
            rr.material = rmat; rr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rr.receiveShadows = false;

            // «коронки» брызг: капля бьёт в лужу и на миг встаёт венчик (стоячий спрайт из атласа)
            Texture2D crt = Resources.Load<Texture2D>("FX/splash_crown");
            if (crt != null)
            {
                GameObject cg = new GameObject("RainCrowns");
                cg.transform.position = new Vector3(0f, 0.045f, 0.3f);
                cg.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                crowns = cg.AddComponent<ParticleSystem>();
                crowns.Stop();
                var cm = crowns.main;
                cm.loop = true; cm.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.2f); cm.startSpeed = 0f;
                cm.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
                cm.startColor = new Color(0.78f, 0.82f, 0.9f, 0.55f);
                cm.maxParticles = 80; cm.simulationSpace = ParticleSystemSimulationSpace.World;
                var ce = crowns.emission; ce.rateOverTime = 110f;
                var cs = crowns.shape; cs.shapeType = ParticleSystemShapeType.Box; cs.scale = new Vector3(8f, 6f, 0.01f);
                var csz = crowns.sizeOverLifetime; csz.enabled = true;
                csz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.1f)));
                var cc = crowns.colorOverLifetime; cc.enabled = true;
                Gradient cgr = new Gradient();
                cgr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
                cc.color = cgr;
                var cr = cg.GetComponent<ParticleSystemRenderer>();
                Material cmat = new Material(sh); cmat.mainTexture = crt;
                cr.material = cmat; cr.renderMode = ParticleSystemRenderMode.VerticalBillboard;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cr.receiveShadows = false;
            }
            // молния в небе: ветвистый разряд (атлас) на миг вспыхивает над лесом вместе со светом
            Texture2D bt = Resources.Load<Texture2D>("FX/lightning");
            if (bt != null)
            {
                GameObject bg = new GameObject("LightningBolt");
                bg.AddComponent<MeshFilter>().sharedMesh = BoltQuad();
                boltMat = new Material(sh) { name = "Lightning", mainTexture = bt, renderQueue = 2600 };
                MeshRenderer br = bg.AddComponent<MeshRenderer>(); br.sharedMaterial = boltMat;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; br.receiveShadows = false;
                bolt = bg.transform; bg.SetActive(false);
            }

            rainAudio = gameObject.AddComponent<AudioSource>();
            AudioClip rec = Resources.Load<AudioClip>("Audio/rain_loop");   // запись дождя в лесу (CC BY 4.0 Oculus), иначе синтез
            rainAudio.clip = rec != null ? rec : RainClip(); rainAudio.loop = true; rainAudio.volume = 0.35f; rainAudio.spatialBlend = 0f;
            thunderAudio = gameObject.AddComponent<AudioSource>();
            thunder = ThunderClip(); thunderAudio.spatialBlend = 0f;
        }

        float wetK;
        float nextParticleBudgetAt;
        static readonly int WetId = Shader.PropertyToID("_BP_Wetness");

        void Update()
        {
            // Retune existing systems at low frequency: keep cinematic rain and snow,
            // but reduce transparent overdraw when sustained frame time degrades.
            if (Time.unscaledTime >= nextParticleBudgetAt)
            {
                nextParticleBudgetAt = Time.unscaledTime + 1.25f;
                float scale = MobileVfxBudget.ParticleScale;
                if (Raining && rain != null)
                {
                    var emission = rain.emission;
                    emission.rateOverTime = (DuelManager.Gfx == 1 ? 520f : 220f) * scale;
                }
                if (Snowing && snow != null)
                {
                    var emission = snow.emission;
                    emission.rateOverTime = (DuelManager.Gfx == 1 ? 110f : 48f) *
                        (HeavySnow ? 1.3f : 0.45f) * scale;
                }
            }
            // бойцы промокают под дождём за несколько секунд (шейдер персонажей: ткань темнеет, доспех и кожа блестят)
            wetK = Mathf.MoveTowards(wetK, Raining ? 1f : 0f, Time.deltaTime / (Raining ? 5f : 1f));
            Shader.SetGlobalFloat(WetId, wetK);
            // снег оседает на плечах и шлемах за полминуты боя; пар дыхания раз в пару секунд
            snowK = Mathf.MoveTowards(snowK, Snowing ? (HeavySnow ? 0.85f : 0.55f) : 0f, Time.deltaTime / (Snowing ? (HeavySnow ? 26f : 45f) : 1f));   // слабый снег ложится тоньше и дольше
            Shader.SetGlobalFloat(SnowId, snowK);
            // за ~9 с до ливня — далёкий раскат: гроза идёт
            if (changeAt > 0f && !changeToSnow && !rumbled && Time.time >= changeAt - 9f)
            {
                rumbled = true;
                if (thunderAudio == null) { thunderAudio = gameObject.AddComponent<AudioSource>(); thunderAudio.spatialBlend = 0f; }
                if (thunder == null) thunder = ThunderClip();
                thunderAudio.pitch = 0.7f; thunderAudio.PlayOneShot(thunder, 0.35f);
            }
            if (changeAt > 0f && Time.time >= changeAt && !Raining && !Snowing && !Daytime)
            {
                // смена погоды посреди боя: туман рассеивается, светлячки прячутся, дальше — дождь с громом или первый снег
                changeAt = -1f;
                if (Foggy) { Foggy = false; StopFog(); }
                Critters(false);
                if (changeToSnow) { Snowing = true; StartSnow(); Leaves(); if (HeavySnow) Windiness = 1.6f; }
                else { Raining = true; Storm = true; StartRain(); Leaves(); nextFlashAt = Time.time + Random.Range(0.5f, 2f); }
            }
            if ((Snowing || Dawn) && Time.time >= nextBreathAt)
            {
                nextBreathAt = Time.time + Random.Range(1.6f, 2.6f);
                foreach (ParticleSystem b in breaths) if (b != null && b.transform.parent != null && b.transform.parent.gameObject.activeInHierarchy) b.Play();
            }
            float haze = Foggy ? 1.55f : Raining ? 1.3f : Snowing ? 1.2f : 1f;
            GlowSprite.Haze = Mathf.MoveTowards(GlowSprite.Haze, haze, Time.deltaTime * 0.2f);
            for (int i = 0; i < steams.Count; i++)
                if (steams[i] != null) { var em = steams[i].emission; bool on = Snowing && snowK > 0.25f; if (em.enabled != on) em.enabled = on; }
            if (!Raining) { Restore(); return; }

            // молния: двойная вспышка, гром через 0,3…1,8 с
            if (Storm && Time.time >= nextFlashAt)
            {
                flashT = Time.unscaledTime;
                nextFlashAt = Time.time + Random.Range(7f, 18f);
                PlaceBolt();
                thunderAt = Time.unscaledTime + Random.Range(0.3f, 1.8f);
            }
            if (thunderAt > 0f && Time.unscaledTime >= thunderAt)
            {
                thunderAt = -1f;
                if (thunderAudio != null) { thunderAudio.pitch = Random.Range(0.8f, 1.1f); thunderAudio.PlayOneShot(thunder, 0.9f); Birds.Startle(); Ambience.Extend(3f); }
                Haptics.Pulse(30);
            }
            float t = Time.unscaledTime - flashT;
            float f = 0f;
            if (t < 0.6f) f = Mathf.Max(Spike(t, 0f, 0.07f), 0.7f * Spike(t, 0.16f, 0.1f), 0.45f * Spike(t, 0.32f, 0.25f));
            if (moon != null) moon.intensity = moonBase * (0.85f + 3.5f * f);
            if (bolt != null)
            {
                bool on = t < 0.6f && f > 0.02f;
                if (bolt.gameObject.activeSelf != on) bolt.gameObject.SetActive(on);
                if (on && boltMat != null) boltMat.color = new Color(0.85f, 0.9f, 1f, Mathf.Clamp01(f * 1.6f));
                if (on) { Camera cm = Camera.main; if (cm != null) { Vector3 to = cm.transform.position - bolt.position; to.y = 0f; if (to.sqrMagnitude > 1e-3f) bolt.rotation = Quaternion.LookRotation(-to.normalized, Vector3.up); } }
            }
            RenderSettings.ambientLight = ambBase * (0.95f + 2.5f * f);
            if (skyMat != null && skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", skyExpBase * (0.8f + 4f * f));
        }

        /// <summary>Молния — далеко за стенами, по ту сторону двора от камеры, в пределах её обзора (±35°), низ — за кронами.</summary>
        void PlaceBolt()
        {
            if (bolt == null) return;
            Camera cm = Camera.main;
            Vector3 fwd = cm != null ? cm.transform.forward : Vector3.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward;
            fwd = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * fwd.normalized;
            Vector3 c = cm != null ? cm.transform.position : Vector3.zero; c.y = 0f;
            float s = Random.Range(0.85f, 1.2f);
            bolt.position = c + fwd * Random.Range(34f, 44f) + Vector3.up * 5f;
            bolt.localScale = new Vector3(13f * s * (Random.value < 0.5f ? 1f : -1f), 26f * s, 1f);
        }

        /// <summary>Квад молнии: низ в начале координат (растёт вверх), лицом к −Z.</summary>
        static Mesh BoltQuad()
        {
            Mesh m = new Mesh { name = "BoltQuad" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static float Spike(float t, float at, float len) { float u = (t - at) / len; return u < 0f || u > 1f ? 0f : (1f - u) * (1f - u); }

        void Restore()
        {
            if (moon != null) moon.intensity = moonBase * (Daytime ? 1f : MoonDim);
            RenderSettings.ambientLight = ambBase;
            if (skyMat != null && skyMat.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", skyExpBase);
        }

        void OnDestroy() { Restore(); RestoreCrowns(); if (Instance == this) Instance = null; }

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
