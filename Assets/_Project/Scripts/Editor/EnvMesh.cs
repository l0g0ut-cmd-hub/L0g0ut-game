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

        /// <summary>
        /// Плоская «наклейка» на земле (AO у основания предметов): горизонтальный квад sx×sz, UV 0..1; v = 0 — у стены (край −z
        /// до поворота). yaw — поворот вокруг вертикали. Тень не отбрасывает и не принимает.
        /// </summary>
        public static GameObject Decal(Transform parent, string name, Vector3 pos, float yaw, float sx, float sz, Material mat)
        {
            const string key = "decalquad";
            Mesh m;
            if (!cache.TryGetValue(key, out m) || m == null)
            {
                MB mb = new MB();
                mb.Poly(new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) }, 1f, new Vector3(0f, -1f, 0f), true, false);
                m = Save(key, mb);
            }
            GameObject g = Make(parent, name, m, pos, new Vector3(0f, yaw, 0f), mat);
            g.transform.localScale = new Vector3(sx, 1f, sz);
            MeshRenderer r = g.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g;
        }

        /// <summary>Плоский многоугольник (звёздный относительно центра) на высоте pos.y: веер из центра, UV в метрах / tile.</summary>
        public static GameObject Flat(Transform parent, string name, Vector3 pos, Vector2[] outline, Material mat, float tile)
        {
            MB mb = new MB();
            Vector3 c = Vector3.zero;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
                mb.Poly(new[] { c, new Vector3(b.x, 0f, b.y), new Vector3(a.x, 0f, a.y) }, 1f, new Vector3(0f, -1f, 0f), false, false);
            }
            // UV — в мировых метрах (Poly считает их от первой вершины треугольника — перезаписываем)
            for (int i = 0; i < mb.uv.Count; i++) mb.uv[i] = new Vector2(mb.v[i].x, mb.v[i].z) / Mathf.Max(0.01f, tile);
            Mesh m = mb.Build("env_flat_" + name);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Models", "Env");
            AssetDatabase.CreateAsset(m, Folder + "/env_" + (counter++) + ".asset");
            return Make(parent, name, m, pos, Vector3.zero, mat);
        }

        /// <summary>
        /// Каменная плита неровного контура (outline — выпуклый многоугольник в метрах, xz): боковины от y = 0, скошенная
        /// кромка и чуть уменьшенный верх на высоте h — плоский природный камень, а не плитка.
        /// </summary>
        public static GameObject Slab(Transform parent, string name, Vector3 pos, float yaw, Vector2[] outline, float h, Material mat, float tile)
        {
            MB mb = new MB();
            Vector3 c = new Vector3(0f, h * 0.4f, 0f);
            int n = outline.Length;
            float hb = h * 0.62f, inset = 0.86f;
            Vector3[] top = new Vector3[n];
            for (int i = 0; i < n; i++) top[i] = new Vector3(outline[i].x * inset, h, outline[i].y * inset);
            mb.Poly((Vector3[])top.Clone(), tile, c, false, false);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 a = outline[i], b = outline[j];
                Vector3 a0 = new Vector3(a.x, 0f, a.y), b0 = new Vector3(b.x, 0f, b.y), a1 = new Vector3(a.x, hb, a.y), b1 = new Vector3(b.x, hb, b.y);
                mb.Poly(new[] { a0, b0, b1, a1 }, tile, c, false, false);
                mb.Poly(new[] { a1, b1, top[j], top[i] }, tile, c, false, false);
            }
            Mesh m = mb.Build("env_slab_" + name);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Models", "Env");
            AssetDatabase.CreateAsset(m, Folder + "/env_" + (counter++) + ".asset");
            return Make(parent, name, m, pos, new Vector3(0f, yaw, 0f), mat);
        }

        /// <summary>
        /// Валун: сфера 10×7 с бугристым радиусом (шум по направлению) и плоскими гранями — гранёный камень, а не гладкое «яйцо».
        /// size — полные размеры по осям; низ приплюснут (камень лежит). Меш на seed кэшируется.
        /// </summary>
        public static GameObject Boulder(Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 size, Material mat, int seed)
        {
            string key = "boulder" + seed;
            Mesh m;
            if (!cache.TryGetValue(key, out m) || m == null)
            {
                System.Random r = new System.Random(seed);
                const int seg = 10, ring = 7;
                Vector3[,] p = new Vector3[seg, ring + 1];
                float[] ph = new float[6];
                for (int i = 0; i < 6; i++) ph[i] = (float)r.NextDouble() * 6.28f;
                for (int j = 0; j <= ring; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        float th = Mathf.PI * j / ring, fi = Mathf.PI * 2f * i / seg;
                        Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(fi), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(fi));
                        float k = 1f + 0.16f * Mathf.Sin(d.x * 3.1f + ph[0]) * Mathf.Cos(d.z * 2.7f + ph[1]) + 0.1f * Mathf.Sin(d.y * 4.3f + d.x * 2f + ph[2])
                                + 0.06f * ((float)r.NextDouble() - 0.5f);
                        if (d.y < -0.2f) k *= Mathf.Lerp(1f, 0.75f, (-d.y - 0.2f) / 0.8f);   // плоское дно
                        p[i, j] = d * 0.5f * k;
                    }
                MB mb = new MB();
                Vector3 c = Vector3.zero;
                for (int j = 0; j < ring; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int i2 = (i + 1) % seg;
                        Vector3 a = p[i, j], b = p[i2, j], cc = p[i2, j + 1], dd = p[i, j + 1];
                        if (j == 0) mb.Poly(new[] { a, cc, dd }, 0.5f, c, false, false);
                        else if (j == ring - 1) mb.Poly(new[] { a, b, dd }, 0.5f, c, false, false);
                        else { mb.Poly(new[] { a, b, cc }, 0.5f, c, false, false); mb.Poly(new[] { a, cc, dd }, 0.5f, c, false, false); }
                    }
                m = Save(key, mb);
            }
            GameObject g = Make(parent, name, m, pos, euler, mat);
            g.transform.localScale = size;
            return g;
        }

        /// <summary>Неровный почти эллиптический контур камня: n точек, полуоси rx, rz, разброс радиуса jitter.</summary>
        public static Vector2[] StoneOutline(System.Random r, int n, float rx, float rz, float jitter)
        {
            Vector2[] o = new Vector2[n];
            float a0 = (float)r.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = a0 + (i + ((float)r.NextDouble() - 0.5f) * 0.5f) * Mathf.PI * 2f / n;
                float k = 1f + ((float)r.NextDouble() - 0.5f) * 2f * jitter;
                o[i] = new Vector2(Mathf.Cos(a) * rx * k, Mathf.Sin(a) * rz * k);
            }
            return o;
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

        /// <summary>
        /// Японская вальмовая крыша: вогнутые скаты и приподнятые углы карниза (ирими-мойя упрощённо).
        /// hx/hz — полуразмеры по свесу, height — подъём конька над краем, upturn — подъём углов.
        /// Две подсетки: 0 — черепица сверху, 1 — дерево снизу (свес виден снизу).
        /// </summary>
        public static GameObject CurvedHip(Transform parent, string name, Vector3 pos, float hx, float hz, float height, float upturn, Material roof, Material under, float tile)
        {
            string key = string.Format("hip{0:F3},{1:F3},{2:F3},{3:F3},{4}", hx, hz, height, upturn, tile);
            Mesh m;
            if (!cache.TryGetValue(key, out m) || m == null)
            {
                const int N = 28;
                List<Vector3> v = new List<Vector3>();
                List<Vector2> uv = new List<Vector2>();
                List<int> top = new List<int>(), bot = new List<int>();
                float ridgeHalf = Mathf.Max(0f, hx - hz);
                for (int pass = 0; pass < 2; pass++)
                {
                    int b = v.Count;
                    for (int j = 0; j <= N; j++)
                        for (int i = 0; i <= N; i++)
                        {
                            float x = Mathf.Lerp(-hx, hx, i / (float)N), z = Mathf.Lerp(-hz, hz, j / (float)N);
                            float tx = Mathf.Max(0f, Mathf.Abs(x) - ridgeHalf) / Mathf.Max(0.01f, hz);
                            float tz = Mathf.Abs(z) / Mathf.Max(0.01f, hz);
                            float t = Mathf.Clamp01(Mathf.Max(tx, tz));              // 0 на коньке, 1 на карнизе
                            float y = height * Mathf.Pow(1f - t, 1.25f);              // вогнутый скат
                            float cx = Mathf.Abs(x) / hx, cz = Mathf.Abs(z) / hz;
                            y += upturn * t * t * t * Mathf.Pow(Mathf.Min(cx, cz), 2.2f); // углы карниза загнуты вверх
                            y += upturn * 0.12f * t * t * t * t;                         // лёгкий загиб всей кромки
                            if (pass == 1) y -= 0.06f;
                            v.Add(new Vector3(x, y, z));
                            uv.Add(new Vector2(x / tile, z / tile));
                        }
                    for (int j = 0; j < N; j++)
                        for (int i = 0; i < N; i++)
                        {
                            int a = b + j * (N + 1) + i, c = a + N + 1;
                            if (pass == 0) { top.Add(a); top.Add(c); top.Add(a + 1); top.Add(a + 1); top.Add(c); top.Add(c + 1); }
                            else { bot.Add(a); bot.Add(a + 1); bot.Add(c); bot.Add(a + 1); bot.Add(c + 1); bot.Add(c); }
                        }
                }
                m = new Mesh { name = "hip" };
                m.SetVertices(v); m.SetUVs(0, uv);
                m.subMeshCount = 2;
                m.SetTriangles(top, 0); m.SetTriangles(bot, 1);
                m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Models", "Env");
                AssetDatabase.CreateAsset(m, Folder + "/env_" + (counter++) + ".asset");
                cache[key] = m;
            }
            GameObject g = Make(parent, name, m, pos, Vector3.zero, roof);
            g.GetComponent<MeshRenderer>().sharedMaterials = new Material[] { roof, under };
            return g;
        }

        public static GameObject Pyramid(Transform parent, string name, Vector3 pos, float half, float height, Material mat, float tile)
        {
            return Make(parent, name, PyramidMesh(half, height, tile), pos, Vector3.zero, mat);
        }
    }
}
#endif
