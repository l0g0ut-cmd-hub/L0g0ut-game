using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Сугробы в снегопад: ветер наметает снег к подножию стен, в угол у ворот и к ногам жаровен. Пологие валики
    /// (сплюснутые сферы, наполовину в земле) растут вместе с глобальным _BP_Snow — сначала тонкая кромка, к концу
    /// боя — пухлые заносы; растаивают, когда снег уходит. Строятся лениво при первом снеге (в остальные погоды их нет).
    /// </summary>
    public class SnowDrifts : MonoBehaviour
    {
        struct Drift { public Transform t; public Vector3 full; }
        readonly List<Drift> drifts = new List<Drift>();
        Transform root;
        float shown = -1f;
        static readonly int SnowId = Shader.PropertyToID("_BP_Snow");

        void Update()
        {
            float k = Mathf.Clamp01(Shader.GetGlobalFloat(SnowId) / 0.85f);
            if (k < 0.01f && root == null) return;
            if (root == null) Build();
            if (Mathf.Abs(k - shown) < 0.004f) return;
            shown = k;
            root.gameObject.SetActive(k > 0.01f);
            // по высоте заносы растут позже ширины: сперва «кромка», потом валик
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f)), h = Mathf.SmoothStep(0f, 1f, k);
            for (int i = 0; i < drifts.Count; i++)
            {
                Vector3 f = drifts[i].full;
                drifts[i].t.localScale = new Vector3(f.x * (0.5f + 0.5f * w), Mathf.Max(0.001f, f.y * h), f.z * (0.4f + 0.6f * w));
            }
        }

        void Build()
        {
            root = new GameObject("SnowDrifts").transform;
            Material m = new Material(Shader.Find("Standard")) { name = "SnowDrift", color = new Color(0.78f, 0.81f, 0.87f) };
            m.SetFloat("_Glossiness", 0.28f);
            System.Random r = new System.Random(19);
            System.Func<float> R = () => (float)r.NextDouble();
            // вдоль боковых стен (внутренняя грань цоколя x = ±6,21), от ворот до храма
            for (int side = -1; side <= 1; side += 2)
                for (float z = -3.2f; z < 4.3f; z += 0.75f + 0.35f * R())
                    Add(m, new Vector3(side * (6.16f - 0.08f * R()), z), 0.9f + 0.6f * R(), 0.16f + 0.08f * R(), 0.5f + 0.25f * R(), 0f);
            // вдоль передней стены по обе стороны ворот, гуще в углах
            for (int side = -1; side <= 1; side += 2)
                for (float x = 1.7f; x < 6.1f; x += 0.7f + 0.35f * R())
                    Add(m, new Vector3(side * x, -7.32f + 0.06f * R()), 0.85f + 0.5f * R(), 0.14f + 0.08f * R() + (x > 5.3f ? 0.06f : 0f), 0.5f + 0.2f * R(), 90f);
            // к ногам жаровен ветер наметает с наветренной стороны
            foreach (float bx in new[] { -3.2f, 3.2f })
                for (int i = 0; i < 3; i++)
                {
                    float a = (bx < 0f ? 160f : 20f) + (i - 1) * 50f + 12f * R();
                    Vector3 o = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.36f;
                    Add(m, new Vector3(bx + o.x, 2.2f + o.z), 0.34f + 0.12f * R(), 0.09f + 0.04f * R(), 0.24f, a);
                }
        }

        /// <summary>Сугроб в точке xz: длина вдоль стены, высота, ширина от стены; yaw — разворот длинной оси.</summary>
        void Add(Material m, Vector2 xz, float len, float h, float wid, float yaw)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider c = g.GetComponent<Collider>(); if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            g.name = "Drift";
            g.transform.SetParent(root, false);
            g.transform.position = new Vector3(xz.x, -0.07f, xz.y);   // центр на уровне гравия — над землёй только верхняя половина
            g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Renderer rr = g.GetComponent<Renderer>(); rr.sharedMaterial = m;
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Vector3 full = new Vector3(wid, h * 2f, len);   // сфера: длинная ось — локальная z (вдоль стены)
            drifts.Add(new Drift { t = g.transform, full = full });
        }
    }
}
