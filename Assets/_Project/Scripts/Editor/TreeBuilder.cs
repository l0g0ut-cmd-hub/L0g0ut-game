#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Процедурное дерево японского клёна (момидзи): ствол и ветви — трубки с сужением, изгибом и ветвлением (4 уровня),
    /// крона — карточки гроздей листьев с альфа-срезом (двусторонние, нормали «от центра кроны» — объёмный свет без
    /// чёрных изнанок), гуще на концах веток. Один меш дерева + один меш кроны: 2 вызова отрисовки на дерево.
    /// </summary>
    public static class TreeBuilder
    {
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
            public Mesh Build(string name)
            {
                Mesh m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        struct Tip { public Vector3 p, dir; public int level; }

        const int Sides = 7;

        static float R(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }
        static Vector3 RandUnit(System.Random r) { Vector3 v; do { v = new Vector3(R(r, -1, 1), R(r, -1, 1), R(r, -1, 1)); } while (v.sqrMagnitude > 1f || v.sqrMagnitude < 1e-3f); return v.normalized; }

        public static GameObject Maple(Transform parent, string name, Vector3 pos, float s, int seed, Material bark, Material leaves)
        {
            System.Random r = new System.Random(seed * 7919 + 101);
            MB wood = new MB(), crown = new MB();
            List<Tip> tips = new List<Tip>();
            Vector3 lean = new Vector3(R(r, -0.12f, 0.12f), 1f, R(r, -0.12f, 0.12f)).normalized;
            Branch(wood, tips, r, Vector3.zero, lean, 1.35f * s, 0.16f * s, 0, s, 0f);

            // центр кроны — по концам веток
            Vector3 cc = Vector3.zero; foreach (Tip tp in tips) cc += tp.p; cc /= Mathf.Max(1, tips.Count); cc.y -= 0.25f * s;
            foreach (Tip tp in tips)
            {
                int cards = tp.level >= 4 ? 3 : (tp.level == 3 ? 2 : 1);
                for (int k = 0; k < cards; k++)
                {
                    Vector3 c = tp.p + tp.dir * R(r, -0.15f, 0.25f) * s + RandUnit(r) * 0.22f * s;
                    float size = R(0.75f, 1.15f, r) * s * (tp.level >= 4 ? 1f : 0.85f);
                    Card(crown, r, c, size, cc);
                }
            }

            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            GameObject w = new GameObject("Wood");
            w.transform.SetParent(root.transform, false);
            w.AddComponent<MeshFilter>().sharedMesh = wood.Build(name + "_wood");
            w.AddComponent<MeshRenderer>().sharedMaterial = bark;
            GameObject l = new GameObject("Crown");
            l.transform.SetParent(root.transform, false);
            l.AddComponent<MeshFilter>().sharedMesh = crown.Build(name + "_crown");
            MeshRenderer lr = l.AddComponent<MeshRenderer>();
            lr.sharedMaterial = leaves;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;   // кружевная тень кроны на камне
            return root;
        }

        static float R(float a, float b, System.Random r) { return R(r, a, b); }

        /// <summary>Пучок карточек листвы вокруг точки (бамбук, кусты): count карточек размером size в сфере radius.</summary>
        public static GameObject Foliage(Transform parent, string name, Vector3 localCenter, float radius, int count, float size, Material mat, int seed)
        {
            System.Random r = new System.Random(seed);
            MB m = new MB();
            Vector3 cc = localCenter - Vector3.up * radius * 0.6f;
            for (int i = 0; i < count; i++) Card(m, r, localCenter + RandUnit(r) * radius, size * R(r, 0.8f, 1.2f), cc);
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m.Build(name);
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        // ветвь: 4 подотрезка с изгибом; дети разлетаются в стороны, клён растёт «зонтиком» — к краю ветви почти горизонтальны
        static void Branch(MB m, List<Tip> tips, System.Random r, Vector3 p, Vector3 dir, float len, float rad, int level, float s, float v0)
        {
            const int sub = 4;
            float radEnd = rad * (level == 0 ? 0.72f : 0.55f);
            Vector3 cur = p; float v = v0;
            Vector3 d = dir;
            for (int i = 0; i < sub; i++)
            {
                float k0 = i / (float)sub, k1 = (i + 1) / (float)sub;
                Vector3 bend = RandUnit(r) * (level == 0 ? 0.12f : 0.28f);
                Vector3 tend = level == 0 ? Vector3.up * 0.15f : (new Vector3(d.x, 0f, d.z).normalized * 0.12f + Vector3.down * 0.05f * level * k1);
                d = (d + bend + tend).normalized;
                Vector3 nxt = cur + d * (len / sub);
                float r0 = Mathf.Lerp(rad, radEnd, k0), r1 = Mathf.Lerp(rad, radEnd, k1);
                Tube(m, cur, nxt, r0, r1, ref v);
                // боковая веточка с середины (кроме ствола и последнего уровня)
                if (level >= 1 && level <= 3 && i == 1 && r.NextDouble() < 0.7)
                    Child(m, tips, r, nxt, d, len * 0.55f, r1 * 0.6f, level + 1, s, v);
                cur = nxt;
            }
            if (level >= 4 || radEnd < 0.004f * s)
            {
                tips.Add(new Tip { p = cur, dir = d, level = level });
                return;
            }
            if (level >= 2) tips.Add(new Tip { p = cur, dir = d, level = level });
            int kids = level == 0 ? 3 + (r.NextDouble() < 0.4 ? 1 : 0) : (level == 1 ? 3 : 2);
            float az0 = R(r, 0f, 360f);
            for (int c = 0; c < kids; c++)
            {
                float az = az0 + 360f / kids * c + R(r, -25f, 25f);
                float spread = level == 0 ? R(r, 42f, 62f) : R(r, 22f, 42f);
                Vector3 axis = Vector3.Cross(d, Quaternion.AngleAxis(az, d) * Perp(d)).normalized;
                Vector3 cd = Quaternion.AngleAxis(spread, axis) * d;
                Child(m, tips, r, cur, cd, len * (level == 0 ? 0.95f : 0.7f) * R(r, 0.85f, 1.1f), radEnd * (level == 0 ? 0.68f : 0.72f), level + 1, s, v);
            }
        }

        static void Child(MB m, List<Tip> tips, System.Random r, Vector3 p, Vector3 d, float len, float rad, int level, float s, float v)
        {
            Branch(m, tips, r, p - d * rad * 0.5f, d, len, rad, level, s, v);   // чуть утоплена в родителя — без щели в развилке
        }

        static Vector3 Perp(Vector3 d)
        {
            Vector3 a = Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(d, a).normalized;
        }

        static void Tube(MB m, Vector3 a, Vector3 b, float ra, float rb, ref float v)
        {
            Vector3 ax = b - a; float L = ax.magnitude; if (L < 1e-5f) return;
            Vector3 dir = ax / L;
            Vector3 x = Perp(dir), y = Vector3.Cross(dir, x);
            int i0 = m.v.Count;
            float vb = v + L / 0.9f;   // кора: ~0,9 м на повтор текстуры по длине
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3 c = ring == 0 ? a : b; float rr = ring == 0 ? ra : rb;
                for (int k = 0; k <= Sides; k++)
                {
                    float ang = k / (float)Sides * Mathf.PI * 2f;
                    Vector3 nrm = x * Mathf.Cos(ang) + y * Mathf.Sin(ang);
                    m.v.Add(c + nrm * rr);
                    m.n.Add(nrm);
                    m.uv.Add(new Vector2(k / (float)Sides, ring == 0 ? v : vb));
                }
            }
            int stride = Sides + 1;
            for (int k = 0; k < Sides; k++)
            {
                int a0 = i0 + k, a1 = i0 + k + 1, b0 = i0 + stride + k, b1 = i0 + stride + k + 1;
                m.t.Add(a0); m.t.Add(b0); m.t.Add(a1);
                m.t.Add(a1); m.t.Add(b0); m.t.Add(b1);
            }
            v = vb;
        }

        // двусторонняя карточка грозди: случайный поворот в плоскости, плоскость смотрит наружу кроны; нормали — наружу кроны
        static void Card(MB m, System.Random r, Vector3 c, float size, Vector3 cc)
        {
            Vector3 outw = c - cc; if (outw.sqrMagnitude < 1e-4f) outw = Vector3.up; outw.Normalize();
            Vector3 nrm = (outw * 0.6f + RandUnit(r) * 0.55f + Vector3.up * 0.25f).normalized;
            Vector3 x = Perp(nrm), y = Vector3.Cross(nrm, x);
            float rot = R(r, 0f, Mathf.PI * 2f);
            Vector3 ex = (x * Mathf.Cos(rot) + y * Mathf.Sin(rot)) * size * 0.5f, ey = Vector3.Cross(nrm, ex);
            Vector3 ln = (outw * 0.8f + Vector3.up * 0.2f).normalized;   // свет как у объёма кроны, не как у плоской карточки
            Vector3[] q = { c - ex - ey, c + ex - ey, c + ex + ey, c - ex + ey };
            Vector2[] uv = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            for (int side = 0; side < 2; side++)
            {
                int i0 = m.v.Count;
                for (int k = 0; k < 4; k++) { m.v.Add(q[k]); m.n.Add(ln); m.uv.Add(uv[k]); }
                if (side == 0) { m.t.Add(i0); m.t.Add(i0 + 2); m.t.Add(i0 + 1); m.t.Add(i0); m.t.Add(i0 + 3); m.t.Add(i0 + 2); }
                else { m.t.Add(i0); m.t.Add(i0 + 1); m.t.Add(i0 + 2); m.t.Add(i0); m.t.Add(i0 + 2); m.t.Add(i0 + 3); }
            }
        }
    }
}
#endif
