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
        readonly List<Drift> icicles = new List<Drift>();   // сосульки со свеса крыши храма и колпаков стен
        static Mesh cone;
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
            // сосульки нарастают позже — когда снег уже лёг и подтаивает у тёплой крыши
            float ic = Mathf.SmoothStep(0f, 1f, (k - 0.45f) / 0.55f);
            for (int i = 0; i < icicles.Count; i++)
            {
                Vector3 f = icicles[i].full;
                icicles[i].t.gameObject.SetActive(ic > 0.02f);
                icicles[i].t.localScale = new Vector3(f.x * (0.6f + 0.4f * ic), Mathf.Max(0.001f, f.y * ic), f.z * (0.6f + 0.4f * ic));
            }
        }

        /// <summary>Конус-сосулька: основание вверху (y = 0), остриё внизу (y = −1), 6 граней.</summary>
        static Mesh Cone()
        {
            const int sides = 6;
            Vector3[] v = new Vector3[sides + 2]; int[] t = new int[sides * 6];
            for (int i = 0; i < sides; i++) { float a = i * Mathf.PI * 2f / sides; v[i] = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f); }
            v[sides] = new Vector3(0f, -1f, 0f); v[sides + 1] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                t[i * 6] = i; t[i * 6 + 1] = j; t[i * 6 + 2] = sides;            // бок
                t[i * 6 + 3] = j; t[i * 6 + 4] = i; t[i * 6 + 5] = sides + 1;    // крышка
            }
            Mesh m = new Mesh { name = "Icicle", vertices = v, triangles = t };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        void Icicle(Material m, Vector3 top, float len, float thick)
        {
            GameObject g = new GameObject("Icicle");
            g.transform.SetParent(root, false); g.transform.position = top;
            g.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 60f), 0f);
            g.AddComponent<MeshFilter>().sharedMesh = cone;
            MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            icicles.Add(new Drift { t = g.transform, full = new Vector3(thick, len, thick) });
        }

        void Build()
        {
            root = new GameObject("SnowDrifts").transform;
            Material m = new Material(Shader.Find("Standard")) { name = "SnowDrift", color = new Color(0.66f, 0.7f, 0.78f) };   // тон снега Env под луной (белее — «подушки»)
            m.SetFloat("_Glossiness", 0.2f);
            System.Random r = new System.Random(19);
            System.Func<float> R = () => (float)r.NextDouble();
            // вдоль боковых стен (внутренняя грань цоколя x = ±6,21), от ворот до храма
            for (int side = -1; side <= 1; side += 2)
                for (float z = -3.2f; z < 4.3f; z += 0.75f + 0.35f * R())
                    Add(m, new Vector3(side * (6.18f - 0.05f * R()), z), 0.9f + 0.6f * R(), 0.07f + 0.05f * R(), 0.34f + 0.16f * R(), 0f);
            // вдоль передней стены по обе стороны ворот, гуще в углах
            for (int side = -1; side <= 1; side += 2)
                for (float x = 1.7f; x < 6.1f; x += 0.7f + 0.35f * R())
                    Add(m, new Vector3(side * x, -7.34f + 0.04f * R()), 0.85f + 0.5f * R(), 0.07f + 0.05f * R() + (x > 5.3f ? 0.04f : 0f), 0.34f + 0.14f * R(), 90f);
            // сосульки: свес крыши храма (кромка y ≈ 2,44, z ≈ 3,92; углы загнуты — только прямой участок) и колпаки боковых стен
            if (cone == null) cone = Cone();
            Material ice = new Material(Shader.Find("Standard")) { name = "Ice", color = new Color(0.72f, 0.8f, 0.9f) };
            ice.SetFloat("_Glossiness", 0.9f);
            for (float x = -4.5f; x < 2.9f; x += 0.16f + 0.3f * R())
                if (R() < 0.75f) Icicle(ice, new Vector3(x, 2.45f, 3.93f), 0.08f + 0.32f * R() * R() + (R() < 0.12f ? 0.25f : 0f), 0.025f + 0.025f * R());
            for (int side = -1; side <= 1; side += 2)
                for (float z = -3.2f; z < 8.2f; z += 0.25f + 0.4f * R())
                    if (R() < 0.6f) Icicle(ice, new Vector3(side * (6.4f - 0.29f), 1.1f, z), 0.05f + 0.16f * R() * R(), 0.02f + 0.015f * R());
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
