using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Замер производительности на телефоне (меню разработчика). Владелец: «на тестовой арене производительность заметно выше,
    /// но первая арена намного красивее» — чтобы облегчать первую арену прицельно, а не наугад, нужно знать, что именно её тяжелит.
    ///
    /// «ЗАМЕР»: в углу экрана — кадры в секунду (средние за секунду), время кадра и худший кадр за секунду.
    /// «ОТКЛ»: по очереди выключает одну часть картинки, чтобы по разнице кадров увидеть её цену:
    /// тени → точечный свет (жаровни, зал) → частицы (листья, лепестки, дождь, искры) → пост-эффекты (свечение, цвет) →
    /// декор вокруг площадки → разрешение 75%. Всё возвращается, когда переключаешь дальше или выходишь из боя.
    /// Ничего не сохраняется — только для проверки.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        public static bool Show;
        public static int Cut;
        public static readonly string[] CutNames = { "НИЧЕГО", "ТЕНИ", "ТОЧЕЧНЫЙ СВЕТ", "ЧАСТИЦЫ", "ПОСТ-ЭФФЕКТЫ", "ДЕКОР ВОКРУГ", "РАЗРЕШЕНИЕ 75%" };
        static PerfProbe instance;

        float acc, worst, shownFps, shownMs, shownWorst; int frames;
        int applied;
        ShadowQuality savedShadows;
        readonly List<Behaviour> offB = new List<Behaviour>();
        readonly List<Renderer> offR = new List<Renderer>();
        int savedW, savedH;

        public static void Ensure()
        {
            if (instance != null) return;
            GameObject g = new GameObject("PerfProbe");
            instance = g.AddComponent<PerfProbe>();
        }

        public static void NextCut() { Cut = (Cut + 1) % CutNames.Length; }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            acc += dt; frames++; worst = Mathf.Max(worst, dt);
            if (acc >= 1f)
            {
                shownFps = frames / acc; shownMs = acc / frames * 1000f; shownWorst = worst * 1000f;
                acc = 0f; frames = 0; worst = 0f;
            }
            if (applied != Cut) { Restore(); ApplyCut(Cut); applied = Cut; }
        }

        void ApplyCut(int c)
        {
            switch (c)
            {
                case 1:
                    savedShadows = QualitySettings.shadows;
                    QualitySettings.shadows = ShadowQuality.Disable;
                    break;
                case 2:
                    foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.enabled && l.type != LightType.Directional) { l.enabled = false; offB.Add(l); }
                    break;
                case 3:
                    foreach (ParticleSystemRenderer r in FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None))
                        if (r.enabled) { r.enabled = false; offR.Add(r); }
                    break;
                case 4:
                    foreach (PostFX p in FindObjectsByType<PostFX>(FindObjectsSortMode.None))
                        if (p.enabled) { p.enabled = false; offB.Add(p); }
                    break;
                case 5:
                    // всё неподвижное дальше ~5 м от центра площадки (здания, деревья, сад, ограда)
                    if (ArenaVariants.Instance != null)
                        foreach (GameObject g in ArenaVariants.Instance.temple)
                        {
                            if (g == null || !g.activeInHierarchy) continue;
                            foreach (Renderer r in g.GetComponentsInChildren<Renderer>())
                            {
                                if (!r.enabled || r is ParticleSystemRenderer) continue;
                                Vector3 cpos = r.bounds.center; cpos.y = 0f;
                                if (cpos.magnitude > 5f) { r.enabled = false; offR.Add(r); }
                            }
                        }
                    break;
                case 6:
                    savedW = Screen.width; savedH = Screen.height;
                    Screen.SetResolution(Mathf.RoundToInt(savedW * 0.75f), Mathf.RoundToInt(savedH * 0.75f), true);
                    break;
            }
        }

        void Restore()
        {
            if (applied == 1) QualitySettings.shadows = savedShadows;
            if (applied == 6 && savedW > 0) Screen.SetResolution(savedW, savedH, true);
            foreach (Behaviour b in offB) if (b != null) b.enabled = true;
            foreach (Renderer r in offR) if (r != null) r.enabled = true;
            offB.Clear(); offR.Clear();
            applied = 0;
        }

        void OnDisable() { Restore(); }
        void OnDestroy() { Restore(); if (instance == this) instance = null; }

        void OnGUI()
        {
            if (!Show || Event.current.type != EventType.Repaint) return;
            Matrix4x4 old = UiKit.Begin();
            Rect r = new Rect(176f, 186f, 340f, 62f);
            UiKit.Rect(r, new Color(0f, 0f, 0f, 0.55f));
            Color c = shownFps >= 55f ? new Color(0.6f, 1f, 0.6f) : shownFps >= 40f ? new Color(1f, 0.9f, 0.5f) : new Color(1f, 0.5f, 0.45f);
            UiKit.Text(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 20f),
                Mathf.RoundToInt(shownFps) + " к/с  ·  " + shownMs.ToString("0.0") + " мс  ·  худший " + Mathf.RoundToInt(shownWorst) + " мс", 13f, c, TextAnchor.MiddleLeft, false);
            UiKit.Text(new Rect(r.x + 8f, r.y + 21f, r.width - 16f, 19f), "ВЫКЛ: " + CutNames[Cut], 12f, new Color(0.9f, 0.85f, 0.75f), TextAnchor.MiddleLeft, false);
            UiKit.Text(new Rect(r.x + 8f, r.y + 40f, r.width - 16f, 19f),
                "арена: " + ArenaVariants.Names[Mathf.Clamp(ArenaVariants.Current, 0, ArenaVariants.Names.Length - 1)], 12f, new Color(0.9f, 0.85f, 0.75f), TextAnchor.MiddleLeft, false);
            GUI.matrix = old;
        }
    }
}
