using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// День и ночь во дворе храма. Сцена собрана ночной (луна, фонари, жаровни); днём (Weather бросает раз на матч):
    /// направленный свет становится солнцем (тёплый, выше, чёткие тени), дневное небо-панорама, светлая голубая дымка,
    /// ярче окружающий свет; луна убрана, огни фонарей и жаровен, ореолы и отражения огней в камне — тусклые,
    /// лучи сквозь кроны — тёплые солнечные; пост-обработка — с высоким порогом свечения (иначе светится всё небо).
    /// Ночные значения запоминаются при первом переключении и возвращаются как были.
    /// </summary>
    public class DayNight : MonoBehaviour
    {
        public static DayNight Instance { get; private set; }
        public Material daySky;
        public Cubemap nightRefl, dayRefl, dawnRefl, duskRefl;   // отражения неба (EnvironmentBuilder.SkyCube)
        public Material duskSky;   // закатная панорама (tools/gen_sky_dusk.py)
        /// <summary>Закат: солнце низко за храмом — бойцы в контровом оранжевом свете, лиловая дымка, фонари уже зажгли.</summary>
        public bool Dusk { get; private set; }
        public Vector3 duskSunDir = new Vector3(0.42f, 0.07f, 0.9f);   // на солнце (совпадает с tools/gen_sky_dusk.py)
        public Material[] glowMats = new Material[0];   // ореолы, огни фонарей, бумажные фонари, сёдзи — днём тусклее
        public Vector3 sunDir = new Vector3(-0.55f, 0.8f, 0.25f);   // на солнце (совпадает с tools/gen_sky_day.py)
        public bool Day { get; private set; }
        /// <summary>Рассвет в тумане: солнце низко, свет тёплый, густая розоватая дымка, огни ещё горят вполсилы.</summary>
        public bool Dawn { get; private set; }
        public Vector3 dawnSunDir = new Vector3(-0.8f, 0.2f, 0.55f);   // низко слева впереди — контровой свет, длинные тени к камере
        Color skyTint; float skyExp; bool skyCaptured;

        bool captured;
        Material nightSky;
        Light sun; Quaternion moonRot; Color moonCol; float moonI, moonShadow;
        Color amb, fogC; float fogD;
        GameObject moonObj, moonHalo;
        struct LightBase { public Light l; public FlickerLight fl; public float i; }
        readonly List<LightBase> lights = new List<LightBase>();
        struct MatBase { public Material m; public int prop; public Color c; }
        readonly List<MatBase> mats = new List<MatBase>();
        readonly List<FloorReflection> refl = new List<FloorReflection>(); readonly List<float> reflS = new List<float>();
        readonly List<LightShaft> shafts = new List<LightShaft>(); readonly List<Color> shaftC = new List<Color>(); readonly List<float> shaftS = new List<float>();
        PostFX post; float pExp, pThr, pInt, pVig, pCon, pSat; Color pSh, pHi;

        static readonly int EmisId = Shader.PropertyToID("_EmissionColor"), TintId = Shader.PropertyToID("_TintColor"), ColorId = Shader.PropertyToID("_Color");

        void Awake() { Instance = this; }

        // свечи за сёдзи: свет бумаги чуть дышит и вздрагивает (огонёк лампады за ней)
        Material shojiMat; Color shojiC;
        void Update()
        {
            if (shojiMat == null) return;
            float t = Time.time;
            float k = 1f + 0.05f * (Mathf.PerlinNoise(t * 1.7f, 0.3f) - 0.5f) * 2f + 0.025f * (Mathf.PerlinNoise(t * 7f, 5.1f) - 0.5f) * 2f;
            shojiMat.SetColor(EmisId, shojiC * k);
        }
        void OnDestroy() { if (Day) Apply(false); if (Instance == this) Instance = null; }   // вернуть ночь (в редакторе — и материалы-ассеты)

        void Capture()
        {
            if (captured) return;
            captured = true;
            nightSky = RenderSettings.skybox;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { if (sun == null) sun = l; continue; }
                lights.Add(new LightBase { l = l, fl = l.GetComponent<FlickerLight>(), i = l.intensity });
            }
            if (sun != null) { moonRot = sun.transform.rotation; moonCol = sun.color; moonI = sun.intensity; moonShadow = sun.shadowStrength; }
            amb = RenderSettings.ambientLight; fogC = RenderSettings.fogColor; fogD = RenderSettings.fogDensity;
            moonObj = GameObject.Find("Moon"); moonHalo = GameObject.Find("MoonHalo");
            foreach (Material m in glowMats)
            {
                if (m == null) continue;
                int prop = m.HasProperty(EmisId) && m.IsKeywordEnabled("_EMISSION") ? EmisId : m.HasProperty(TintId) ? TintId : m.HasProperty(ColorId) ? ColorId : 0;
                if (prop != 0) mats.Add(new MatBase { m = m, prop = prop, c = m.GetColor(prop) });
            }
            foreach (FloorReflection r in FindObjectsByType<FloorReflection>(FindObjectsSortMode.None)) { refl.Add(r); reflS.Add(r.strength); }
            foreach (LightShaft s in FindObjectsByType<LightShaft>(FindObjectsSortMode.None)) { shafts.Add(s); shaftC.Add(s.color); shaftS.Add(s.strength); }
            post = FindFirstObjectByType<PostFX>();
            if (post != null) { pExp = post.exposure; pThr = post.threshold; pInt = post.intensity; pVig = post.vignette; pCon = post.contrast; pSat = post.saturation; pSh = post.shadows; pHi = post.highlights; }
        }

        /// <summary>Переключить время суток (вызывает Weather до того, как запомнит свою базу света и тумана). dawn — рассвет (при day).</summary>
        public void Apply(bool day, bool dawn = false, bool dusk = false)
        {
            Capture();
            Day = day; Dawn = day && dawn; Dusk = day && !Dawn && dusk;
            bool K = Dusk;
            if (daySky != null && !skyCaptured) { skyCaptured = true; skyTint = daySky.HasProperty("_Tint") ? daySky.GetColor("_Tint") : Color.gray; skyExp = daySky.HasProperty("_Exposure") ? daySky.GetFloat("_Exposure") : 1f; }
            if (daySky != null)
            {
                // рассвет: то же небо, но в тёплой дымке (скайбокс туман не берёт — тонируем сам)
                if (daySky.HasProperty("_Tint")) daySky.SetColor("_Tint", Dawn ? new Color(0.62f, 0.5f, 0.48f, 1f) : skyTint);
                if (daySky.HasProperty("_Exposure")) daySky.SetFloat("_Exposure", Dawn ? skyExp * 0.82f : skyExp);
            }
            if (sun != null)
            {
                sun.transform.rotation = day ? Quaternion.LookRotation(-(Dawn ? dawnSunDir : K ? duskSunDir : sunDir).normalized, Vector3.up) : moonRot;
                sun.color = Dawn ? new Color(1f, 0.68f, 0.45f) : K ? new Color(1f, 0.52f, 0.26f) : day ? new Color(1f, 0.95f, 0.86f) : moonCol;
                sun.intensity = Dawn ? 1.05f : K ? 1.25f : day ? 1.3f : moonI;
                sun.shadowStrength = Dawn ? 0.6f : K ? 0.75f : day ? 0.72f : moonShadow;
            }
            RenderSettings.ambientLight = Dawn ? new Color(0.38f, 0.37f, 0.45f) : K ? new Color(0.34f, 0.26f, 0.34f) : day ? new Color(0.5f, 0.52f, 0.58f) : amb;
            RenderSettings.fogColor = Dawn ? new Color(0.68f, 0.6f, 0.62f) : K ? new Color(0.58f, 0.38f, 0.36f) : day ? new Color(0.64f, 0.71f, 0.8f) : fogC;
            RenderSettings.fogDensity = Dawn ? 0.045f : K ? 0.03f : day ? 0.02f : fogD;
            if (daySky != null) RenderSettings.skybox = day ? (K && duskSky != null ? duskSky : daySky) : nightSky;
            if (moonObj != null) moonObj.SetActive(!day);
            if (moonHalo != null) moonHalo.SetActive(!day);
            foreach (LightBase b in lights)
            {
                if (b.l == null) continue;
                float k = Dawn ? 0.55f : K ? 0.7f : day ? 0.3f : 1f;   // на закате фонари уже зажгли
                if (b.fl != null) { if (!flBase.ContainsKey(b.fl)) flBase[b.fl] = b.fl.baseIntensity; b.fl.baseIntensity = flBase[b.fl] * k; }
                else b.l.intensity = b.i * k;
            }
            foreach (MatBase mb in mats)
            {
                if (mb.m == null) continue;
                Color c = mb.c;
                if (day)
                {
                    float gk = Dawn ? 0.55f : K ? 0.75f : 0.25f;
                    if (mb.prop == ColorId) c = Color.Lerp(c, new Color(0.85f, 0.8f, 0.7f, c.a), Dawn || K ? 0.25f : 0.5f); else { c.r *= gk; c.g *= gk; c.b *= gk; }
                }
                mb.m.SetColor(mb.prop, c);
                if (mb.m.name.Contains("Shoji") && mb.prop == EmisId) { shojiMat = mb.m; shojiC = c; }
            }
            for (int i = 0; i < refl.Count; i++) if (refl[i] != null) refl[i].strength = reflS[i] * (K ? 0.6f : day ? 0.2f : 1f);
            for (int i = 0; i < shafts.Count; i++)
                if (shafts[i] != null) { shafts[i].color = Dawn ? new Color(1f, 0.74f, 0.58f) : K ? new Color(1f, 0.55f, 0.3f) : day ? new Color(1f, 0.86f, 0.62f) : shaftC[i]; shafts[i].strength = shaftS[i] * (Dawn ? 1.4f : K ? 1.6f : day ? 0.8f : 1f); }
            if (post != null)
            {
                post.exposure = Dawn ? 1.02f : K ? 1.04f : day ? 1.0f : pExp;
                post.threshold = Dawn ? 0.95f : K ? 1.0f : day ? 1.15f : pThr;
                post.intensity = Dawn ? 0.55f : K ? 0.62f : day ? 0.38f : pInt;
                post.vignette = Dawn ? 0.34f : K ? 0.38f : day ? 0.26f : pVig;
                post.contrast = Dawn ? 1.04f : K ? 1.08f : day ? 1.06f : pCon;
                post.saturation = Dawn ? 0.98f : K ? 1.12f : day ? 1.08f : pSat;
                post.shadows = Dawn ? new Color(0.94f, 0.96f, 1.06f) : K ? new Color(0.92f, 0.9f, 1.08f) : day ? new Color(0.97f, 0.99f, 1.04f) : pSh;
                post.highlights = Dawn ? new Color(1.08f, 0.98f, 0.9f) : K ? new Color(1.12f, 0.96f, 0.84f) : day ? new Color(1.04f, 1.0f, 0.95f) : pHi;
            }
            Shader.SetGlobalFloat("_BP_Day", day ? (Dawn ? 0.6f : K ? 0.5f : 1f) : 0f);
            Cubemap cube = Dawn ? dawnRefl : K ? (duskRefl != null ? duskRefl : dawnRefl) : day ? dayRefl : nightRefl;
            if (cube != null) SetReflection(cube);
            DynamicGI.UpdateEnvironment();
        }
        readonly Dictionary<FlickerLight, float> flBase = new Dictionary<FlickerLight, float>();

        /// <summary>Своя кубкарта отражений окружения (свойство называется по-разному в версиях Unity — ищем отражением).</summary>
        public static void SetReflection(Texture t)
        {
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            System.Type rs = typeof(RenderSettings);
            System.Reflection.PropertyInfo p = rs.GetProperty("customReflectionTexture") ?? rs.GetProperty("customReflection");
            if (p != null && p.PropertyType.IsAssignableFrom(t.GetType())) p.SetValue(null, t, null);
        }
    }
}
