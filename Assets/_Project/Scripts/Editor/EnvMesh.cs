#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Меши окружения с UV в мировом масштабе (одна текстура = tile метров), чтобы кирпич/доски не растягивались на боксах разного размера.
    /// Меши сохраняются ассетами в Assets/_Project/Models/Env (Editor-время, CreatePrimitive/коллайдеры в рантайме не нужны).
    /// </summary>
    public static class EnvMesh
    {
        const string Folder = "Assets/_Project/Models/Env";
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();
        static int counter;

        class MB
        {
            public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public List<Vector2> uv = new List<Vector2>();
            public List<int> t = new List<int>();

            /// <summary>Полигон; нормаль разворачивается наружу относительно centroid. stretch: квад a,b,c,d с UV 0..1.</summary>
            public void Poly(Vector3[] p, float tile, Vector3 centroid, bool stretch, bool swap)
            {
                Vector3 nrm = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
                Vector3 ua = (p[1] - p[0]).normalized;
                Vector3 va = Vector3.Cross(nrm, ua);
                Vector2[] uvs = new Vector2[p.Length];
                Vector2[] quad = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                for (int i = 0; i < p.Length; i++)
                {
                    if (stretch && p.Length == 4) uvs[i] = quad[i];
                    else uvs[i] = new Vector2(Vector3.Dot(p[i] - p[0], ua), Vector3.Dot(p[i] - p[0], va)) / Mathf.Max(0.01f, tile);
                    if (swap) uvs[i] = new Vector2(uvs[i].y, uvs[i].x);
                }
                Vector3 mid = Vector3.zero;
                for (int i = 0; i < p.Length; i++) mid += p[i];
                mid /= p.Length;
                if (Vector3.Dot(nrm, mid - centroid) < 0f)
                {
                    System.Array.Reverse(p);
                    System.Array.Reverse(uvs);
                    nrm = -nrm;
                }
                int b = v.Count;
                for (int i = 0; i < p.Length; i++) { v.Add(p[i]); n.Add(nrm); uv.Add(uvs[i]); }
                for (int i = 1; i < p.Length - 1; i++) { t.Add(b); t.Add(b + i); t.Add(b + i + 1); }
            }

            public Mesh Build(string name)
            {
                Mesh m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        static Mesh Save(string key, MB mb)
        {
            Mesh m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Models", "Env");
            m = mb.Build("env_" + (counter));
            AssetDatabase.CreateAsset(m, Folder + "/env_" + (counter++) + ".asset");
            cache[key] = m;
            return m;
        }

        static Mesh BoxMesh(Vector3 s, float tile, bool swap, bool stretch)
        {
            string key = string.Format("box{0:F3},{1:F3},{2:F3},{3},{4},{5}", s.x, s.y, s.z, tile, swap, stretch);
            if (cache.ContainsKey(key) && cache[key] != null) return cache[key];
            MB mb = new MB();
            Vector3 h = s * 0.5f;
            Vector3 c = Vector3.zero;
            // +z / -z: u = x, v = y
            mb.Poly(new[] { new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z) }, tile, c, stretch, swap);
            mb.Poly(new[] { new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z) }, tile, c, stretch, swap);
            // +x / -x: u = z, v = y
            mb.Poly(new[] { new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z) }, tile, c, stretch, swap);
            mb.Poly(new[] { new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z) }, tile, c, stretch, swap);
            // +y / -y: u = x, v = z
            mb.Poly(new[] { new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z) }, tile, c, stretch, swap);
            mb.Poly(new[] { new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z) }, tile, c, stretch, swap);
            return Save(key, mb);
        }

        /// <summary>Скаты двускатной крыши вдоль X: основание y=0, коньок y=height.</summary>
        static Mesh GableMesh(float length, float depth, float height, float tile)
        {
            string key = string.Format("gable{0:F3},{1:F3},{2:F3},{3}", length, depth, height, tile);
            if (cache.ContainsKey(key) && cache[key] != null) return cache[key];
            MB mb = new MB();
            float l = length * 0.5f, d = depth * 0.5f;
            Vector3 c = new Vector3(0f, height * 0.33f, 0f);
            mb.Poly(new[] { new Vector3(-l, 0, -d), new Vector3(l, 0, -d), new Vector3(l, height, 0), new Vector3(-l, height, 0) }, tile, c, false, false);
            mb.Poly(new[] { new Vector3(-l, 0, d), new Vector3(l, 0, d), new Vector3(l, height, 0), new Vector3(-l, height, 0) }, tile, c, false, false);
            return Save(key, mb);
        }

        /// <summary>Торцевые треугольники фронтона.</summary>
        static Mesh GableEndsMesh(float length, float depth, float height, float tile)
        {
            string key = string.Format("gend{0:F3},{1:F3},{2:F3},{3}", length, depth, height, tile);
            if (cache.ContainsKey(key) && cache[key] != null) return cache[key];
            MB mb = new MB();
            float l = length * 0.5f, d = depth * 0.5f;
            Vector3 c = new Vector3(0f, height * 0.33f, 0f);
            mb.Poly(new[] { new Vector3(l, 0, -d), new Vector3(l, 0, d), new Vector3(l, height, 0) }, tile, c, false, false);
            mb.Poly(new[] { new Vector3(-l, 0, -d), new Vector3(-l, 0, d), new Vector3(-l, height, 0) }, tile, c, false, false);
            return Save(key, mb);
        }

        static Mesh PyramidMesh(float half, float height, float tile)
        {
            string key = string.Format("pyr{0:F3},{1:F3},{2}", half, height, tile);
            if (cache.ContainsKey(key) && cache[key] != null) return cache[key];
            MB mb = new MB();
            Vector3 c = new Vector3(0f, height * 0.33f, 0f);
            Vector3 top = new Vector3(0f, height, 0f);
            Vector3 a = new Vector3(-half, 0, -half), b = new Vector3(half, 0, -half), cc = new Vector3(half, 0, half), dd = new Vector3(-half, 0, half);
            mb.Poly(new[] { a, b, top }, tile, c, false, false);
            mb.Poly(new[] { b, cc, top }, tile, c, false, false);
            mb.Poly(new[] { cc, dd, top }, tile, c, false, false);
            mb.Poly(new[] { dd, a, top }, tile, c, false, false);
            mb.Poly(new[] { a, b, cc, dd }, tile, c, false, false);
            return Save(key, mb);
        }

        static GameObject Make(Transform parent, string name, Mesh m, Vector3 pos, Vector3 euler, Material mat)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localEulerAngles = euler;
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        /// <summary>Бокс размера size с UV в мировом масштабе. tile — метров на повтор текстуры (0 = растянуть на грань). swap — доски вдоль длинной стороны.</summary>
        public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 size, Material mat, float tile, bool swap = false)
        {
            return Make(parent, name, BoxMesh(size, tile <= 0f ? 1f : tile, swap, tile <= 0f), pos, euler, mat);
        }

        public static GameObject Gable(Transform parent, string name, Vector3 pos, float length, float depth, float height, Material roof, Material ends, float tile)
        {
            GameObject g = Make(parent, name, GableMesh(length, depth, height, tile), pos, Vector3.zero, roof);
            Make(g.transform, name + "Ends", GableEndsMesh(length, depth, height, 1.5f), Vector3.zero, Vector3.zero, ends);
            return g;
        }

        /// <summary>Ореол-биллборд: аддитивный квад с мягким диском, разворачивается к камере (GlowSprite).</summary>
        public static GameObject Glow(Transform parent, string name, Vector3 pos, float size, Material mat, float flicker = 0.12f)
        {
            string key = "glowquad";
            Mesh m;
            if (!cache.TryGetValue(key, out m) || m == null)
            {
                MB mb = new MB();
                mb.Poly(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) }, 1f, new Vector3(0f, 0f, -1f), true, false);
                m = Save(key, mb);
            }
            GameObject g = Make(parent, name, m, pos, Vector3.zero, mat);
            g.transform.localScale = Vector3.one * size;
            MeshRenderer r = g.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            g.AddComponent<BladePath.GlowSprite>().flicker = flicker;
            return g;
        }

        public static GameObject Pyramid(Transform parent, string name, Vector3 pos, float half, float height, Material mat, float tile)
        {
            return Make(parent, name, PyramidMesh(half, height, tile), pos, Vector3.zero, mat);
        }
    }
}
#endif
