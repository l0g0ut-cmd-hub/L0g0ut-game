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
        public Material[] glowMats = new Material[0];   // ореолы, огни фонарей, бумажные фонари, сёдзи — днём тусклее
        public Vector3 sunDir = new Vector3(-0.55f, 0.8f, 0.25f);   // на солнце (совпадает с tools/gen_sky_day.py)
        public bool Day { get; private set; }

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
        void OnDestroy() { if (Day) Apply(false); if (Instance == this) Instance = null; }

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

        /// <summary>Переключить время суток (вызывает Weather до того, как запомнит свою базу света и тумана).</summary>
        public void Apply(bool day)
        {
            Capture();
            Day = day;
            if (sun != null)
            {
                sun.transform.rotation = day ? Quaternion.LookRotation(-sunDir.normalized, Vector3.up) : moonRot;
                sun.color = day ? new Color(1f, 0.95f, 0.86f) : moonCol;
                sun.intensity = day ? 1.3f : moonI;
                sun.shadowStrength = day ? 0.72f : moonShadow;
            }
            RenderSettings.ambientLight = day ? new Color(0.5f, 0.52f, 0.58f) : amb;
            RenderSettings.fogColor = day ? new Color(0.64f, 0.71f, 0.8f) : fogC;
            RenderSettings.fogDensity = day ? 0.02f : fogD;
            if (daySky != null) RenderSettings.skybox = day ? daySky : nightSky;
            if (moonObj != null) moonObj.SetActive(!day);
            if (moonHalo != null) moonHalo.SetActive(!day);
            foreach (LightBase b in lights)
            {
                if (b.l == null) continue;
                float k = day ? 0.3f : 1f;
                if (b.fl != null) { if (!flBase.ContainsKey(b.fl)) flBase[b.fl] = b.fl.baseIntensity; b.fl.baseIntensity = flBase[b.fl] * k; }
                else b.l.intensity = b.i * k;
            }
            foreach (MatBase mb in mats)
            {
                if (mb.m == null) continue;
                Color c = mb.c;
                if (day) { if (mb.prop == ColorId) c = Color.Lerp(c, new Color(0.85f, 0.8f, 0.7f, c.a), 0.5f); else { c.r *= 0.25f; c.g *= 0.25f; c.b *= 0.25f; } }
                mb.m.SetColor(mb.prop, c);
            }
            for (int i = 0; i < refl.Count; i++) if (refl[i] != null) refl[i].strength = reflS[i] * (day ? 0.2f : 1f);
            for (int i = 0; i < shafts.Count; i++)
                if (shafts[i] != null) { shafts[i].color = day ? new Color(1f, 0.86f, 0.62f) : shaftC[i]; shafts[i].strength = shaftS[i] * (day ? 0.8f : 1f); }
            if (post != null)
            {
                post.exposure = day ? 1.0f : pExp;
                post.threshold = day ? 1.15f : pThr;
                post.intensity = day ? 0.38f : pInt;
                post.vignette = day ? 0.26f : pVig;
                post.contrast = day ? 1.06f : pCon;
                post.saturation = day ? 1.08f : pSat;
                post.shadows = day ? new Color(0.97f, 0.99f, 1.04f) : pSh;
                post.highlights = day ? new Color(1.04f, 1.0f, 0.95f) : pHi;
            }
            DynamicGI.UpdateEnvironment();
        }
        readonly Dictionary<FlickerLight, float> flBase = new Dictionary<FlickerLight, float>();
    }
}
