using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Полы плаща (кости Coat.* от пояса к подолу): маятник на пружине поверх аниматора и IK. Конец кости догоняет своё
    /// место в позе с запаздыванием (инерция ткани при рывке, развороте, ударе), тянется вниз весом и не входит в бёдра —
    /// ноги в выпаде раздвигают полы. Только визуал. Работает после HeroRigDriver (порядок исполнения).
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class CoatSpring : MonoBehaviour
    {
        [System.Serializable]
        public class Panel
        {
            public Transform bone;
            [System.NonSerialized] public Quaternion restLocal;
            [System.NonSerialized] public Vector3 tipLocal;           // конец кости в покое (в системе кости)
            [System.NonSerialized] public Vector3 pos, vel;           // симулируемый конец (мир)
            [System.NonSerialized] public bool init;
        }

        public List<Panel> panels = new List<Panel>();
        public Transform[] thighs = new Transform[0];   // UpperLeg.L/R: капсулы бедро→колено
        public Transform[] knees = new Transform[0];    // LowerLeg.L/R
        public float stiffness = 60f, damping = 9f, gravity = 3f;
        public float legRadius = 0.13f, maxAngle = 55f, length = 0.45f;

        public void Collect(Transform root)
        {
            panels.Clear();
            List<Transform> th = new List<Transform>(), kn = new List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Coat")) panels.Add(new Panel { bone = t });
                if (t.name == "UpperLeg.L" || t.name == "UpperLeg.R") th.Add(t);
                if (t.name == "LowerLeg.L" || t.name == "LowerLeg.R") kn.Add(t);
            }
            thighs = th.ToArray(); knees = kn.ToArray();
        }

        bool ready;

        void Start() { Init(); }

        void Init()
        {
            ready = true;
            foreach (Panel p in panels)
            {
                if (p.bone == null) continue;
                p.restLocal = p.bone.localRotation;
                // пола в покое висит вниз от пояса: конец — в системе кости (оси FBX не важны)
                p.tipLocal = p.bone.InverseTransformPoint(p.bone.position + Vector3.down * length);
                p.init = false;
            }
        }

        void LateUpdate()
        {
            if (!ready) Init();   // в редакторе (кадры CI) Start не вызывается
            float dt = Mathf.Clamp(HeroRigDriver.DtOverride >= 0f ? HeroRigDriver.DtOverride : Time.deltaTime, 0f, 1f / 30f);
            if (dt <= 0f) return;
            foreach (Panel p in panels)
            {
                if (p.bone == null) continue;
                p.bone.localRotation = p.restLocal;                       // целевая поза: как пошит (за тазом)
                Vector3 root = p.bone.position;
                Vector3 target = p.bone.TransformPoint(p.tipLocal);
                if (!p.init) { p.pos = target; p.vel = Vector3.zero; p.init = true; }
                // пружина к целевой точке + вес ткани
                Vector3 acc = (target - p.pos) * stiffness - p.vel * damping + Vector3.down * gravity;
                p.vel += acc * dt;
                p.pos += p.vel * dt;
                // длина полы постоянна
                Vector3 d = p.pos - root;
                float L = Vector3.Distance(target, root);
                if (d.sqrMagnitude < 1e-8f) d = target - root;
                p.pos = root + d.normalized * L;
                // бёдра раздвигают полы: точку выталкивает из капсулы бедра
                for (int i = 0; i < thighs.Length && i < knees.Length; i++)
                {
                    if (thighs[i] == null || knees[i] == null) continue;
                    Vector3 a = thighs[i].position, b = knees[i].position;
                    Vector3 ab = b - a;
                    float t = Mathf.Clamp01(Vector3.Dot(p.pos - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    Vector3 c = a + ab * t;
                    Vector3 off = p.pos - c;
                    float r = legRadius;
                    if (off.sqrMagnitude < r * r)
                    {
                        Vector3 n = off.sqrMagnitude > 1e-8f ? off.normalized : (p.pos - root).normalized;
                        p.pos = c + n * r;
                        p.vel -= n * Mathf.Min(0f, Vector3.Dot(p.vel, n));
                        d = p.pos - root; p.pos = root + d.normalized * L;
                    }
                }
                // ограничение угла от покоя
                Vector3 restDir = (target - root).normalized, curDir = (p.pos - root).normalized;
                float ang = Vector3.Angle(restDir, curDir);
                if (ang > maxAngle) { curDir = Vector3.Slerp(restDir, curDir, maxAngle / ang); p.pos = root + curDir * L; }
                p.bone.rotation = Quaternion.FromToRotation(restDir, curDir) * p.bone.rotation;
            }
        }
    }
}
