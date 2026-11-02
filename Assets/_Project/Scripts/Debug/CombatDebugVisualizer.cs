using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Отладочная визуализация (ТЗ, раздел 15): коллайдеры зон, клинок и его контрольные точки,
    /// пройденные отрезки проверки (траектория), найденные контакты и журнал событий.
    ///
    /// Линии рисуются мешем через Graphics.DrawMesh (видны и в редакторе без Gizmos, и в APK).
    /// Текстовый журнал рисуется через OnGUI. F1 включает/выключает.
    /// В релизных сборках (не Development) всё отключено, если не включён forceEnable.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class CombatDebugVisualizer : MonoBehaviour
    {
        public BladeTrajectorySampler[] samplers;
        public FighterController[] fighters;
        public CombatResolver resolver;
        [Tooltip("Материал с поддержкой цвета вершин (Sprites/Default). Задаётся сборщиком сцены.")]
        public Material lineMaterial;

        public bool visible = true;
        public bool forceEnableInRelease = false;
        public bool showColliders = true;
        public float segmentFadeSeconds = 2.5f;
        public float contactFadeSeconds = 5f;

        struct ContactMark { public Vector3 point, normal, dir; public float time; public bool glancing; }
        readonly List<ContactMark> marks = new List<ContactMark>();
        GUIStyle style;
        readonly DebugLineBatch lines = new DebugLineBatch();

        bool Active { get { return visible && (Debug.isDebugBuild || forceEnableInRelease); } }

        void OnEnable() { useGUILayout = false; if (resolver != null) resolver.HitRegistered += OnHit; }
        void OnDisable() { if (resolver != null) resolver.HitRegistered -= OnHit; }

        void OnHit(HitResult r)
        {
            ContactMark m = new ContactMark();
            m.point = r.contact.point;
            m.normal = r.contact.normal;
            m.dir = r.contact.bladeVelocity.normalized;
            m.time = Time.time;
            m.glancing = r.glancing;
            marks.Add(m);
            if (marks.Count > 64) marks.RemoveAt(0);
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F1)) visible = !visible;
#endif
        }

        void LateUpdate()
        {
            if (!Active || lineMaterial == null) return;
            lines.Clear();

            if (showColliders && fighters != null)
                for (int f = 0; f < fighters.Length; f++)
                    if (fighters[f] != null && fighters[f].zones != null)
                        for (int z = 0; z < fighters[f].zones.Length; z++)
                            DrawZone(fighters[f].zones[z]);

            if (samplers != null)
                for (int s = 0; s < samplers.Length; s++) DrawSampler(samplers[s]);

            for (int i = 0; i < marks.Count; i++)
            {
                ContactMark m = marks[i];
                float age = Time.time - m.time;
                if (age > contactFadeSeconds) continue;
                float a = 1f - age / contactFadeSeconds;
                lines.WireSphere(m.point, 0.04f, new Color(1f, 0.1f, 0.1f, a));
                lines.Line(m.point, m.point + m.normal * 0.2f, new Color(1f, 1f, 0.2f, a));                 // нормаль поверхности
                lines.Line(m.point - m.dir * 0.25f, m.point,                                               // направление удара
                    m.glancing ? new Color(0.2f, 1f, 0.4f, a) : new Color(1f, 0.2f, 1f, a));
            }

            lines.Draw(lineMaterial);
        }

        void DrawZone(BodyZone zone)
        {
            if (zone == null) return;
            Collider col = zone.GetComponent<Collider>();
            Color c = new Color(0.2f, 1f, 0.3f, 0.8f);

            SphereCollider sc = col as SphereCollider;
            if (sc != null)
            {
                Vector3 ls = sc.transform.lossyScale;
                float r = sc.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                lines.WireSphere(sc.transform.TransformPoint(sc.center), r, c);
                return;
            }
            CapsuleCollider cc = col as CapsuleCollider;
            if (cc != null)
            {
                Transform t = cc.transform;
                Vector3 ls = t.lossyScale;
                Vector3 axisLocal = cc.direction == 0 ? Vector3.right : (cc.direction == 1 ? Vector3.up : Vector3.forward);
                float axisScale = cc.direction == 0 ? ls.x : (cc.direction == 1 ? ls.y : ls.z);
                float radScale = cc.direction == 0 ? Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z))
                               : (cc.direction == 1 ? Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z)) : Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y)));
                float r = cc.radius * radScale;
                float h = Mathf.Max(cc.height * Mathf.Abs(axisScale), 2f * r);
                Vector3 center = t.TransformPoint(cc.center);
                Vector3 ax = t.TransformDirection(axisLocal).normalized;
                float half = h * 0.5f - r;
                lines.WireCapsule(center + ax * half, center - ax * half, r, c);
            }
        }

        void DrawSampler(BladeTrajectorySampler s)
        {
            if (s == null || s.Weapon == null || s.Weapon.bladeRoot == null) return;
            WeaponController w = s.Weapon;

            // клинок и контрольные точки
            Vector3 b0 = w.bladeRoot.position;
            Vector3 b1 = b0 + w.bladeRoot.forward * w.bladeLength;
            Color bladeColor = w.ContactWindowOpen ? new Color(1f, 0.5f, 0f) : Color.white;
            lines.Line(b0, b1, bladeColor);
            for (int k = 1; k <= s.samplePoints; k++)
                lines.WireSphere(Vector3.Lerp(b0, b1, k / (float)s.samplePoints), w.bladeRadius, bladeColor);

            // пройденные отрезки проверки: кончик — оранжевый, остальное — циановое
            for (int i = 0; i < s.DebugCount; i++)
            {
                BladeTrajectorySampler.DebugSegment seg = s.GetDebugSegment(i);
                float age = Time.time - seg.time;
                if (age > segmentFadeSeconds) continue;
                float a = 1f - age / segmentFadeSeconds;
                bool tip = seg.bladeT > 0.999f;
                lines.Line(seg.a, seg.b, tip ? new Color(1f, 0.55f, 0f, a) : new Color(0f, 0.9f, 1f, a * 0.35f));
            }
        }

        void OnGUI()
        {
            if (!Active) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.richText = false;
                style.normal.textColor = Color.white;
            }
            float scale = Mathf.Max(1f, Screen.height / 540f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            style.fontSize = 12;
            float y = 6f;
            if (samplers != null)
            {
                for (int i = 0; i < samplers.Length; i++)
                {
                    BladeTrajectorySampler s = samplers[i];
                    if (s == null) continue;
                    string warn = s.UndersampledLastFrame ? "  !! НЕДОСТАТОЧНО ПОДШАГОВ (возможны пропуски)" : "";
                    GUI.Label(new Rect(8f, y, 640f, 18f),
                        s.Weapon.owner.displayName + ": " + s.Weapon.AttackLabel + "  фаза=" + s.Weapon.Phase + "  подшагов/кадр=" + s.SubstepsLastFrame + "/" + s.maxSubsteps + warn, style);
                    y += 16f;
                }
            }
            if (resolver != null)
            {
                int from = Mathf.Max(0, resolver.Log.Count - 8);
                for (int i = from; i < resolver.Log.Count; i++)
                {
                    GUI.Label(new Rect(8f, y, 900f, 18f), resolver.Log[i], style);
                    y += 16f;
                }
            }
            GUI.matrix = old;
        }
    }
}
