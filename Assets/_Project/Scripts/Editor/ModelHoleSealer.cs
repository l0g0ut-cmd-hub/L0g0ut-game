#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Non-destructive repair of tiny, enclosed triangular-to-small-polygon
    /// pinholes in imported FBX and OBJ meshes. Never moves vertices, never
    /// changes UVs, normals, skin weights, bind poses, blend shapes or materials.
    ///
    /// IMPORTANT: an open edge is NOT automatically a hole: sleeves, collars,
    /// hat brims, leaves and banners have deliberate open rims. We only close
    /// small planar convex loops whose cap normal agrees with the surrounding
    /// surface. Larger/ambiguous openings are recorded by the Blender audit,
    /// not silently bridged here.
    /// </summary>
    public sealed class ModelHoleSealer : AssetPostprocessor
    {
        const string Folder = "Assets/_Project/Models/";
        const int MaxLoopEdges = 12;
        const int MaxPatchesPerMesh = 512;
        const float MaxSpanOfMesh = 0.045f;
        const float MaxAreaOfMesh = 0.00040f;

        // AssetPostprocessor dependency version: imported FBX/OBJ must be
        // reimported when the repair algorithm is updated.
        public override uint GetVersion() { return 3; }

        // FBX/OBJ imported with Read/Write disabled exposes no vertices during
        // OnPostprocessModel. APK 575 silently skipped EVERY mesh (23/23).
        // Switch it on BEFORE the FBX importer creates the mesh; a subsequent
        // edit to this setting during OnPostprocessModel would be too late.
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)
                || assetPath.Contains("/Arenas/")) return;
            ModelImporter importer = assetImporter as ModelImporter;
            if (importer != null && !importer.isReadable)
                importer.isReadable = true;
        }

        // A fresh CI checkout may restore a Library cache that contains meshes
        // imported before this postprocessor was introduced. Reimport once
        // explicitly before building scenes so all imported game models are
        // evaluated by this repair pass, regardless of cached import state.
        public static void ForceReimportAll()
        {
            int count = 0;
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) continue;
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                count++;
            }
            Debug.Log("ModelHoleSealer: forced reimport of " + count + " imported game FBX/OBJ models");
        }

        struct VertexKey : IEquatable<VertexKey>, IComparable<VertexKey>
        {
            public readonly long x, y, z;
            public VertexKey(Vector3 v, double scale)
            {
                x = (long)Math.Round(v.x * scale);
                y = (long)Math.Round(v.y * scale);
                z = (long)Math.Round(v.z * scale);
            }
            public bool Equals(VertexKey o) { return x == o.x && y == o.y && z == o.z; }
            public override bool Equals(object o) { return o is VertexKey && Equals((VertexKey)o); }
            public int CompareTo(VertexKey o)
            {
                int c = x.CompareTo(o.x); if (c != 0) return c;
                c = y.CompareTo(o.y); return c != 0 ? c : z.CompareTo(o.z);
            }
            public override int GetHashCode()
            {
                unchecked { int h = x.GetHashCode(); h = h * 397 ^ y.GetHashCode(); return h * 397 ^ z.GetHashCode(); }
            }
        }

        struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly VertexKey u, v;
            public EdgeKey(VertexKey a, VertexKey b)
            {
                if (a.CompareTo(b) <= 0) { u = a; v = b; }
                else { u = b; v = a; }
            }
            public bool Equals(EdgeKey o) { return u.Equals(o.u) && v.Equals(o.v); }
            public override bool Equals(object o) { return o is EdgeKey && Equals((EdgeKey)o); }
            public override int GetHashCode() { unchecked { return u.GetHashCode() * 397 ^ v.GetHashCode(); } }
        }

        sealed class Edge
        {
            public VertexKey from, to;
            public EdgeKey key;
            public int count, material;
            public VertexKey Other(VertexKey k) { return from.Equals(k) ? to : from; }
        }

        static void PutEdge(Dictionary<EdgeKey, Edge> all, VertexKey a, VertexKey b, int material)
        {
            if (a.Equals(b)) return;
            EdgeKey k = new EdgeKey(a, b);
            Edge e;
            if (all.TryGetValue(k, out e)) { e.count++; return; }
            all[k] = new Edge { from = a, to = b, key = k, count = 1, material = material };
        }

        static void AddNeighbor(Dictionary<VertexKey, List<Edge>> graph, VertexKey key, Edge e)
        {
            List<Edge> list;
            if (!graph.TryGetValue(key, out list)) { list = new List<Edge>(2); graph.Add(key, list); }
            list.Add(e);
        }

        void OnPostprocessModel(GameObject root)
        {
            if (root == null || !assetPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) return;
            if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                && !assetPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)) return;
            // ForestShrine contains intentionally one-sided foliage and cards;
            // capping their outline loops would destroy the approved silhouette.
            if (assetPath.Contains("/Arenas/")) return;

            var visited = new HashSet<Mesh>();
            int patched = 0, scanned = 0, unreadable = 0;
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMesh != null && visited.Add(r.sharedMesh))
                {
                    scanned++;
                    if (!r.sharedMesh.isReadable) unreadable++;
                    patched += Seal(r.sharedMesh);
                }
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && visited.Add(f.sharedMesh))
                {
                    scanned++;
                    if (!f.sharedMesh.isReadable) unreadable++;
                    patched += Seal(f.sharedMesh);
                }
            if (unreadable > 0)
                Debug.LogError("ModelHoleSealer: UNREADABLE imported mesh " + assetPath
                               + " (" + unreadable + "/" + scanned + "); repair was NOT performed");
            Debug.Log("ModelHoleSealer: " + assetPath + ", meshes=" + scanned
                      + ", unreadable=" + unreadable + ", tiny holes sealed=" + patched
                      + " (no vertex, UV, material or bone changes)");
        }

        static int Seal(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable || mesh.subMeshCount == 0 || mesh.vertexCount < 4)
                return 0;
            try { return SealReadable(mesh); }
            catch (Exception ex)
            {
                Debug.LogWarning("ModelHoleSealer: skipped " + mesh.name + " because " + ex.Message);
                return 0;
            }
        }

        static int SealReadable(Mesh mesh)
        {
            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int n = positions.Length, subCount = mesh.subMeshCount;
            float size = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
            if (size < 1e-8f) return 0;
            // Vertex seams can have different indices but the same 3D position.
            // Geometry is welded only for the TOPOLOGY SEARCH, never on the mesh.
            double quantScale = 1.0 / Math.Max(size * 0.000001, 0.00000001);
            var keys = new VertexKey[n];
            var reps = new Dictionary<VertexKey, int>(n);
            for (int i = 0; i < n; i++)
            {
                keys[i] = new VertexKey(positions[i], quantScale);
                if (!reps.ContainsKey(keys[i])) reps.Add(keys[i], i);
            }
            var triangles = new List<int>[subCount];
            var edges = new Dictionary<EdgeKey, Edge>(n * 2);
            for (int sub = 0; sub < subCount; sub++)
            {
                triangles[sub] = new List<int>(mesh.GetTriangles(sub));
                List<int> t = triangles[sub];
                for (int i = 0; i + 2 < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= n || b >= n || c >= n) continue;
                    PutEdge(edges, keys[a], keys[b], sub);
                    PutEdge(edges, keys[b], keys[c], sub);
                    PutEdge(edges, keys[c], keys[a], sub);
                }
            }

            var graph = new Dictionary<VertexKey, List<Edge>>();
            var boundary = new List<Edge>();
            foreach (Edge edge in edges.Values)
                if (edge.count == 1)
                {
                    boundary.Add(edge);
                    AddNeighbor(graph, edge.from, edge);
                    AddNeighbor(graph, edge.to, edge);
                }
            if (boundary.Count == 0) return 0;

            int filled = 0;
            var used = new HashSet<EdgeKey>();
            foreach (Edge first in boundary)
            {
                if (filled >= MaxPatchesPerMesh) break;
                if (used.Contains(first.key)) continue;
                List<Edge> st, en;
                if (!graph.TryGetValue(first.from, out st) || st.Count != 2
                    || !graph.TryGetValue(first.to, out en) || en.Count != 2) continue;

                var loop = new List<VertexKey>(MaxLoopEdges + 1) { first.from, first.to };
                var pathEdges = new List<Edge>(MaxLoopEdges) { first };
                VertexKey origin = first.from, cur = first.to;
                bool valid = true;
                while (!cur.Equals(origin))
                {
                    List<Edge> joined;
                    if (!graph.TryGetValue(cur, out joined) || joined.Count != 2 || pathEdges.Count >= MaxLoopEdges)
                    { valid = false; break; }
                    Edge next = joined[0].key.Equals(pathEdges[pathEdges.Count - 1].key)
                        ? joined[1] : joined[0];
                    if (pathEdges.Contains(next)) { valid = false; break; }
                    pathEdges.Add(next);
                    cur = next.Other(cur);
                    loop.Add(cur);
                }
                if (!valid || !cur.Equals(origin) || pathEdges.Count < 3) continue;
                // Mark a complete perimeter once, even if its cap is rejected.
                foreach (Edge e in pathEdges) used.Add(e.key);
                loop.RemoveAt(loop.Count - 1);
                int count = loop.Count;
                if (count < 3 || count > MaxLoopEdges) continue;

                var vi = new int[count];
                var p = new Vector3[count];
                Vector3 center = Vector3.zero, averageNormal = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    vi[i] = reps[loop[i]];
                    p[i] = positions[vi[i]];
                    center += p[i];
                    if (normals.Length == n) averageNormal += normals[vi[i]];
                }
                center /= count;
                Vector3 polygonNormal = Vector3.zero;
                float span = 0f, perimeter = 0f;
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    polygonNormal += Vector3.Cross(p[i] - center, p[j] - center);
                    perimeter += Vector3.Distance(p[i], p[j]);
                    for (int k = i + 1; k < count; k++)
                        span = Mathf.Max(span, Vector3.Distance(p[i], p[k]));
                }
                float area = 0.5f * polygonNormal.magnitude;
                if (area <= size * size * 0.00000002f || area > size * size * MaxAreaOfMesh
                    || span > size * MaxSpanOfMesh || perimeter > size * 0.20f)
                    continue;
                Vector3 normal = polygonNormal.normalized;
                for (int i = 0; i < count; i++)
                    if (Mathf.Abs(Vector3.Dot(p[i] - center, normal)) > size * 0.0015f)
                    { valid = false; break; }
                if (!valid) continue;

                // A missing surface is locally tangent to its neighbors. An
                // open cuff/tube end is normally perpendicular and stays open.
                if (averageNormal.sqrMagnitude < 0.00001f
                    || Mathf.Abs(Vector3.Dot(normal, averageNormal.normalized)) < 0.68f)
                    continue;

                // Only convex patches; never stretch a fan across a concave
                // gap or across garment pieces that merely happen to meet.
                for (int i = 0; i < count; i++)
                {
                    Vector3 a = p[(i + count - 1) % count] - p[i];
                    Vector3 b = p[(i + 1) % count] - p[i];
                    float signed = Vector3.Dot(Vector3.Cross(b, a), normal);
                    if (signed < -area * 0.0001f) { valid = false; break; }
                }
                if (!valid) continue;

                // Boundary winding of existing faces is opposite that of
                // the missing cap. Preserve manifold winding.
                int along = 0, sub = pathEdges[0].material;
                var votes = new int[subCount];
                for (int i = 0; i < count; i++)
                {
                    Edge e;
                    if (!edges.TryGetValue(new EdgeKey(loop[i], loop[(i + 1) % count]), out e))
                    { valid = false; break; }
                    along += e.from.Equals(loop[i]) ? 1 : -1;
                    votes[e.material]++;
                }
                if (!valid) continue;
                for (int i = 0; i < votes.Length; i++)
                    if (votes[i] > votes[sub]) sub = i;
                // The polygon was walked along original face boundary; flip
                // so its closing triangles point against that edge direction.
                if (along > 0)
                {
                    Array.Reverse(vi);
                    Array.Reverse(p);
                    normal = -normal;
                }
                if (Vector3.Dot(normal, averageNormal.normalized) < 0.6f) continue;
                // Fan triangulation is safe because convexity was checked.
                // All indices and source vertex attributes remain untouched.
                for (int i = 1; i < count - 1; i++)
                {
                    triangles[sub].Add(vi[0]);
                    triangles[sub].Add(vi[i]);
                    triangles[sub].Add(vi[i + 1]);
                }
                filled++;
            }

            if (filled > 0)
                for (int i = 0; i < subCount; i++)
                    mesh.SetTriangles(triangles[i], i, false);
            return filled;
        }
    }
}
#endif
