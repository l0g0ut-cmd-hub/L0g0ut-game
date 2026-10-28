#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Катана из кода (вместо бокса и цилиндров): изогнутый клинок с ребром синоги, остриём-киссаки и острой кромкой,
    /// хабаки, волнистая цуба, рукоять с оплёткой (текстура tsuka_albedo.png) и навершие. Ось клинка — +Z bladeRoot, длина 0,8 м.
    /// Кромка смотрит в −Y, изгиб (сори) — к обуху (+Y).
    /// </summary>
    public static class KatanaBuilder
    {
        const string Folder = "Assets/_Project/Models/Env";
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static void Build(Transform bladeRoot, Look look, Material tsukaMat, Material ironMat, Material brassMat)
        {
            Add(bladeRoot, "Blade", BladeMesh(), look.steel);
            Add(bladeRoot, "Habaki", BoxMesh(new Vector3(0.010f, 0.036f, 0.030f), new Vector3(0f, -0.016f, 0.017f)), brassMat);
            Add(bladeRoot, "Tsuba", TsubaMesh(), ironMat);
            Add(bladeRoot, "Tsuka", HandleMesh(), tsukaMat);
            Add(bladeRoot, "Kashira", BoxMesh(new Vector3(0.030f, 0.036f, 0.016f), new Vector3(0f, 0f, -0.262f)), ironMat);
        }

        static void Add(Transform parent, string name, Mesh m, Material mat)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
        }

        static Mesh Save(string key, Mesh m)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Models/Env")) AssetDatabase.CreateFolder("Assets/_Project/Models", "Env");
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, Folder + "/katana_" + key + ".asset");
            cache[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- клинок
        static Mesh BladeMesh()
        {
            Mesh c;
            if (cache.TryGetValue("blade", out c) && c != null) return c;
            const int N = 40;
            const float z0 = 0.032f, len = 0.80f, sori = 0.022f;
            List<Vector3> v = new List<Vector3>(); List<Vector3> nn = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> tri = new List<int>();
            // профиль сечения: обух (спина) сверху, ребро синоги, кромка снизу
            Vector3[][] rings = new Vector3[N + 1][];
            float[] centerY = new float[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float s = i / (float)N;
                float z = z0 + (len - z0) * s;
                float spineY = sori * s * s;                         // изгиб к обуху
                float w = Mathf.Lerp(0.031f, 0.025f, s);              // ширина клинка
                float t = Mathf.Lerp(0.0036f, 0.0020f, s);            // полутолщина у обуха
                // киссаки: последние 9% — кромка поднимается к обуху и сходится в точку
                float k = Mathf.Clamp01((s - 0.91f) / 0.09f);
                float edgeY = spineY - w * (1f - k * k * 0.98f);
                float ridgeY = spineY - w * 0.28f * (1f - k);
                float spineW = t * (1f - k * 0.9f);
                centerY[i] = (spineY + Mathf.Max(edgeY, ridgeY - 0.004f)) * 0.5f;
                rings[i] = new Vector3[]
                {
                    new Vector3(-spineW, spineY, z), new Vector3(spineW, spineY, z),
                    new Vector3(t, ridgeY, z), new Vector3(0f, edgeY, z), new Vector3(-t, ridgeY, z)
                };
            }
            for (int i = 0; i < N; i++)
                for (int f = 0; f < 5; f++)
                {
                    Vector3 a = rings[i][f], b = rings[i][(f + 1) % 5], c2 = rings[i + 1][(f + 1) % 5], d = rings[i + 1][f];
                    Quad(v, nn, uv, tri, a, b, c2, d, f, i / (float)N, (i + 1) / (float)N, new Vector3(0f, (centerY[i] + centerY[i + 1]) * 0.5f, (a.z + d.z) * 0.5f));
                }
            // торец у хабаки
            Vector3[] r0 = rings[0];
            int bi = v.Count;
            for (int f = 0; f < 5; f++) { v.Add(r0[f]); nn.Add(Vector3.back); uv.Add(Vector2.zero); }
            for (int f = 1; f < 4; f++)
            {
                Vector3 pa = r0[0], pb = r0[f], pc = r0[f + 1];
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), Vector3.back) > 0f) tri.AddRange(new[] { bi, bi + f, bi + f + 1 });
                else tri.AddRange(new[] { bi, bi + f + 1, bi + f });
            }
            Mesh m = new Mesh { name = "katana_blade" };
            m.SetVertices(v); m.SetNormals(nn); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateTangents();
            return Save("blade", m);
        }

        /// <summary>Плоская грань полосы: свои вершины (жёсткие рёбра), нормаль наружу.</summary>
        static void Quad(List<Vector3> v, List<Vector3> nn, List<Vector2> uv, List<int> tri, Vector3 a, Vector3 b, Vector3 c, Vector3 d, int face, float v0, float v1, Vector3 center)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            Vector3 mid = (a + b + c + d) * 0.25f;
            if (Vector3.Dot(n, mid - center) < 0f) n = -n;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++) nn.Add(n);
            uv.Add(new Vector2(face / 5f, v0)); uv.Add(new Vector2((face + 1) / 5f, v0)); uv.Add(new Vector2((face + 1) / 5f, v1)); uv.Add(new Vector2(face / 5f, v1));
            // обход так, чтобы лицевая сторона смотрела по нормали
            // Unity: треугольник лицевой, если Cross(b−a, c−a) смотрит по нормали
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0f) { tri.Add(i); tri.Add(i + 1); tri.Add(i + 2); tri.Add(i); tri.Add(i + 2); tri.Add(i + 3); }
            else { tri.Add(i); tri.Add(i + 2); tri.Add(i + 1); tri.Add(i); tri.Add(i + 3); tri.Add(i + 2); }
        }

        // ---------------------------------------------------------------- цуба (гарда)
        static Mesh TsubaMesh()
        {
            Mesh c;
            if (cache.TryGetValue("tsuba", out c) && c != null) return c;
            const int N = 40;
            const float th = 0.0035f, zc = 0.004f;
            List<Vector3> v = new List<Vector3>(); List<int> tri = new List<int>();
            v.Add(new Vector3(0f, 0f, zc + th)); v.Add(new Vector3(0f, 0f, zc - th));
            for (int i = 0; i < N; i++)
            {
                float a = i / (float)N * Mathf.PI * 2f;
                float r = 0.040f + 0.0035f * Mathf.Sin(a * 8f);          // волнистый край (цветок)
                float x = Mathf.Cos(a) * r * 0.92f, y = Mathf.Sin(a) * r;
                v.Add(new Vector3(x, y, zc + th)); v.Add(new Vector3(x, y, zc - th));
            }
            for (int i = 0; i < N; i++)
            {
                int a0 = 2 + i * 2, a1 = 2 + ((i + 1) % N) * 2;
                tri.AddRange(new[] { 0, a0, a1 });                 // передняя грань (+Z)
                tri.AddRange(new[] { 1, a1 + 1, a0 + 1 });         // задняя (−Z)
                tri.AddRange(new[] { a0, a1 + 1, a1, a0, a0 + 1, a1 + 1 });   // ребро (нормаль наружу)
            }
            Mesh m = new Mesh { name = "katana_tsuba" };
            m.SetVertices(v); m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            return Save("tsuba", m);
        }

        // ---------------------------------------------------------------- рукоять
        static Mesh HandleMesh()
        {
            Mesh c;
            if (cache.TryGetValue("tsuka", out c) && c != null) return c;
            const int N = 16, M = 12;
            const float z0 = -0.002f, z1 = -0.255f;
            List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> tri = new List<int>();
            for (int j = 0; j <= M; j++)
            {
                float s = j / (float)M;
                float z = Mathf.Lerp(z0, z1, s);
                float waist = 1f - 0.06f * Mathf.Sin(s * Mathf.PI);    // рукоять чуть тоньше в середине
                for (int i = 0; i <= N; i++)
                {
                    float a = i / (float)N * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * 0.0135f * waist, Mathf.Sin(a) * 0.0165f * waist, z));
                    uv.Add(new Vector2(i / (float)N, s));
                }
            }
            for (int j = 0; j < M; j++)
                for (int i = 0; i < N; i++)
                {
                    int a = j * (N + 1) + i, b = a + N + 1;
                    tri.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            Mesh m = new Mesh { name = "katana_tsuka" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            // нормали наружу: если обход дал внутрь — развернуть треугольники
            Vector3[] ns = m.normals;
            if (ns.Length > 0 && Vector3.Dot(ns[1], new Vector3(v[1].x, v[1].y, 0f)) < 0f)
            {
                for (int k = 0; k < tri.Count; k += 3) { int t = tri[k + 1]; tri[k + 1] = tri[k + 2]; tri[k + 2] = t; }
                m.SetTriangles(tri, 0);
                m.RecalculateNormals();
            }
            m.RecalculateTangents();
            return Save("tsuka", m);
        }

        static Mesh BoxMesh(Vector3 size, Vector3 center)
        {
            string key = string.Format("box{0:F3}{1:F3}{2:F3}{3:F3}", size.x, size.y, size.z, center.z);
            Mesh c;
            if (cache.TryGetValue(key, out c) && c != null) return c;
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh src = tmp.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vs = src.vertices;
            for (int i = 0; i < vs.Length; i++) vs[i] = Vector3.Scale(vs[i], size) + center;
            Mesh m = new Mesh { name = "katana_" + key };
            m.vertices = vs; m.normals = src.normals; m.uv = src.uv; m.triangles = src.triangles;
            Object.DestroyImmediate(tmp);
            return Save(key, m);
        }
    }
}
#endif
