using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Пакет линий, рисуемый одним мешем (MeshTopology.Lines) через Graphics.DrawMesh.
    /// В отличие от Gizmos работает и в APK, и в любом render pipeline. Материал должен учитывать цвет вершин
    /// (подходит Sprites/Default).
    /// </summary>
    public class DebugLineBatch
    {
        readonly List<Vector3> verts = new List<Vector3>(4096);
        readonly List<Color> cols = new List<Color>(4096);
        readonly List<int> idx = new List<int>(4096);
        Mesh mesh;

        public void Clear() { verts.Clear(); cols.Clear(); idx.Clear(); }

        public void Line(Vector3 a, Vector3 b, Color c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b);
            cols.Add(c); cols.Add(c);
            idx.Add(i); idx.Add(i + 1);
        }

        public void Circle(Vector3 center, Vector3 u, Vector3 v, float r, Color c, int seg)
        {
            Vector3 prev = center + u * r;
            for (int k = 1; k <= seg; k++)
            {
                float a = k * (Mathf.PI * 2f / seg);
                Vector3 p = center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
                Line(prev, p, c);
                prev = p;
            }
        }

        public void WireSphere(Vector3 center, float r, Color c)
        {
            Circle(center, Vector3.right, Vector3.up, r, c, 14);
            Circle(center, Vector3.right, Vector3.forward, r, c, 14);
            Circle(center, Vector3.up, Vector3.forward, r, c, 14);
        }

        public void WireCapsule(Vector3 p1, Vector3 p2, float r, Color c)
        {
            WireSphere(p1, r, c);
            WireSphere(p2, r, c);
            Vector3 ax = (p2 - p1);
            if (ax.sqrMagnitude < 1e-8f) return;
            ax = ax.normalized;
            Vector3 s1 = Vector3.Cross(ax, Vector3.forward);
            if (s1.sqrMagnitude < 1e-4f) s1 = Vector3.Cross(ax, Vector3.right);
            s1 = s1.normalized * r;
            Vector3 s2 = Vector3.Cross(ax, s1).normalized * r;
            Line(p1 + s1, p2 + s1, c); Line(p1 - s1, p2 - s1, c);
            Line(p1 + s2, p2 + s2, c); Line(p1 - s2, p2 - s2, c);
        }

        /// <summary>Отправить накопленное на отрисовку в этом кадре.</summary>
        public void Draw(Material material)
        {
            if (material == null || verts.Count == 0) return;
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "DebugLineBatch";
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.MarkDynamic();
            }
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetIndices(idx, MeshTopology.Lines, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(200f, 200f, 200f)); // не отсекать по камере
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0);
        }
    }
}
