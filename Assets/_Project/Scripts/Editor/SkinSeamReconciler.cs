#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Fix cracks along UV-split vertices whose bone weights differ and thus
    /// separate as joints bend. Does not change vertices, UVs, polygons,
    /// normals, materials, bind poses, animations or 5+ influence meshes.
    /// </summary>
    internal static class SkinSeamReconciler
    {
        struct Key : IEquatable<Key>
        {
            readonly long x, y, z;
            public Key(Vector3 v, double scale)
            {
                x = (long)Math.Round(v.x * scale);
                y = (long)Math.Round(v.y * scale);
                z = (long)Math.Round(v.z * scale);
            }
            public bool Equals(Key k) { return x == k.x && y == k.y && z == k.z; }
            public override bool Equals(object obj) { return obj is Key && Equals((Key)obj); }
            public override int GetHashCode()
            {
                unchecked { return (x.GetHashCode() * 397 ^ y.GetHashCode()) * 397 ^ z.GetHashCode(); }
            }
        }

        static float Weight(BoneWeight b, int index)
        {
            float f = 0f;
            if (b.boneIndex0 == index) f += b.weight0;
            if (b.boneIndex1 == index) f += b.weight1;
            if (b.boneIndex2 == index) f += b.weight2;
            if (b.boneIndex3 == index) f += b.weight3;
            return f;
        }
        static float Overlap(BoneWeight a, BoneWeight b)
        {
            var ids = new HashSet<int>();
            if (a.weight0 > 0.0001f) ids.Add(a.boneIndex0);
            if (a.weight1 > 0.0001f) ids.Add(a.boneIndex1);
            if (a.weight2 > 0.0001f) ids.Add(a.boneIndex2);
            if (a.weight3 > 0.0001f) ids.Add(a.boneIndex3);
            if (b.weight0 > 0.0001f) ids.Add(b.boneIndex0);
            if (b.weight1 > 0.0001f) ids.Add(b.boneIndex1);
            if (b.weight2 > 0.0001f) ids.Add(b.boneIndex2);
            if (b.weight3 > 0.0001f) ids.Add(b.boneIndex3);
            float total = 0;
            foreach (int id in ids) total += Mathf.Min(Weight(a,id), Weight(b,id));
            return Mathf.Clamp01(total);
        }
        static int Root(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }
            return index;
        }
        static void Neighbor(HashSet<Key>[] graph, int index, Key point)
        {
            if (graph[index] == null) graph[index] = new HashSet<Key>();
            graph[index].Add(point);
        }
        static int Shared(HashSet<Key> a, HashSet<Key> b)
        {
            if (a == null || b == null) return 0;
            int count = 0;
            foreach (Key v in a) if (b.Contains(v)) count++;
            return count;
        }
        static void Add(Dictionary<int,float> dict, int bone, float weight)
        {
            if (weight <= 0.00001f) return;
            float old;
            dict.TryGetValue(bone, out old);
            dict[bone] = old + weight;
        }

        public static int Reconcile(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable || mesh.vertexCount < 6 || mesh.bindposes.Length == 0)
                return 0;
            Vector3[] positions = mesh.vertices, normals = mesh.normals;
            BoneWeight[] weights = mesh.boneWeights;
            int n = positions.Length;
            if (normals == null || normals.Length != n || weights == null || weights.Length != n)
                return 0;
            // Imported rigs with >4 influences are left untouched. Setting
            // Mesh.boneWeights would otherwise silently discard those bones.
            using (var counts = mesh.GetBonesPerVertex())
                for (int i = 0; i < counts.Length; i++)
                    if (counts[i] > 4) return 0;
            float extent = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
            if (extent < 1e-8f) return 0;
            double scale = 1.0 / Math.Max(extent * 0.000001, 0.00000001);
            var points = new Key[n];
            var buckets = new Dictionary<Key,List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                points[i] = new Key(positions[i], scale);
                List<int> group;
                if (!buckets.TryGetValue(points[i], out group))
                { group = new List<int>(2); buckets.Add(points[i], group); }
                group.Add(i);
            }
            var duplicates = new bool[n];
            int repeated = 0;
            foreach (List<int> group in buckets.Values)
                if (group.Count > 1)
                {
                    repeated++;
                    foreach (int v in group) duplicates[v] = true;
                }
            if (repeated == 0) return 0;

            // Adjacent vertices must actually share a geometric seam edge;
            // overlapping but separate armor components must not be joined.
            var graph = new HashSet<Key>[n];
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] tri = mesh.GetTriangles(sub);
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                    if (duplicates[a]) { Neighbor(graph,a,points[b]); Neighbor(graph,a,points[c]); }
                    if (duplicates[b]) { Neighbor(graph,b,points[a]); Neighbor(graph,b,points[c]); }
                    if (duplicates[c]) { Neighbor(graph,c,points[a]); Neighbor(graph,c,points[b]); }
                }
            }
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            foreach (List<int> group in buckets.Values)
            {
                if (group.Count < 2 || group.Count > 16) continue;
                for (int a = 0; a < group.Count; a++)
                    for (int b = a+1; b < group.Count; b++)
                    {
                        int i = group[a], j = group[b];
                        float facing = Vector3.Dot(normals[i].normalized, normals[j].normalized);
                        if (facing < 0.88f) continue;
                        int shared = Shared(graph[i],graph[j]);
                        if (shared == 0) continue;
                        float overlap = Overlap(weights[i],weights[j]);
                        if (overlap < 0.25f) continue;
                        if (overlap < 0.55f && (facing < 0.975f || shared < 2))
                            continue;
                        int ai = Root(parent,i), bj = Root(parent,j);
                        if (ai != bj) parent[bj] = ai;
                    }
            }
            var connected = new Dictionary<int,List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (!duplicates[i]) continue;
                int id = Root(parent,i);
                List<int> group;
                if (!connected.TryGetValue(id, out group))
                { group = new List<int>(); connected.Add(id, group); }
                group.Add(i);
            }
            int adjusted = 0;
            foreach (List<int> group in connected.Values)
            {
                if (group.Count < 2 || group.Count > 16) continue;
                float minOverlap = 1f;
                for (int a = 0; a < group.Count; a++)
                    for (int b = a+1; b < group.Count; b++)
                        minOverlap = Mathf.Min(minOverlap, Overlap(weights[group[a]],weights[group[b]]));
                if (minOverlap < 0.22f || minOverlap > 0.975f) continue;
                var sum = new Dictionary<int,float>();
                foreach (int vi in group)
                {
                    BoneWeight w = weights[vi];
                    Add(sum,w.boneIndex0,w.weight0); Add(sum,w.boneIndex1,w.weight1);
                    Add(sum,w.boneIndex2,w.weight2); Add(sum,w.boneIndex3,w.weight3);
                }
                var ranked = new List<KeyValuePair<int,float>>(sum);
                ranked.Sort((a,b) => b.Value.CompareTo(a.Value));
                if (ranked.Count > 4 && ranked[4].Value > group.Count * 0.045f)
                    continue;
                int take = Math.Min(4, ranked.Count);
                float norm = 0f;
                for (int i=0; i<take; i++) norm += ranked[i].Value;
                if (norm < 0.001f) continue;
                BoneWeight avg = new BoneWeight();
                if (take>0) { avg.boneIndex0=ranked[0].Key; avg.weight0=ranked[0].Value/norm; }
                if (take>1) { avg.boneIndex1=ranked[1].Key; avg.weight1=ranked[1].Value/norm; }
                if (take>2) { avg.boneIndex2=ranked[2].Key; avg.weight2=ranked[2].Value/norm; }
                if (take>3) { avg.boneIndex3=ranked[3].Key; avg.weight3=ranked[3].Value/norm; }
                foreach (int vi in group) { weights[vi]=avg; adjusted++; }
            }
            if (adjusted > 0) mesh.boneWeights = weights;
            return adjusted;
        }

        public static void SelfTest()
        {
            var mesh = new Mesh();
            try
            {
                mesh.vertices = new[] {
                    new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),
                    new Vector3(1,0,0),new Vector3(1,1,0),new Vector3(0,1,0)
                };
                mesh.normals = new[] {Vector3.forward,Vector3.forward,Vector3.forward,
                                     Vector3.forward,Vector3.forward,Vector3.forward};
                mesh.triangles = new[] {0,1,2,3,4,5};
                mesh.bindposes = new[] {Matrix4x4.identity,Matrix4x4.identity};
                BoneWeight a = new BoneWeight {boneIndex0=0,weight0=0.8f,boneIndex1=1,weight1=0.2f};
                BoneWeight b = new BoneWeight {boneIndex0=0,weight0=0.7f,boneIndex1=1,weight1=0.3f};
                mesh.boneWeights = new[] {a,a,a,b,b,b};
                int count = Reconcile(mesh);
                BoneWeight[] weights = mesh.boneWeights;
                if (count!=4 || Mathf.Abs(weights[1].weight0-weights[3].weight0)>0.0001f
                     || Mathf.Abs(weights[2].weight0-weights[5].weight0)>0.0001f)
                    throw new InvalidOperationException("Skin seam synthetic positive test failed: "+count);
                if (mesh.triangles.Length!=6 || mesh.vertexCount!=6)
                    throw new InvalidOperationException("Skin seam test changed mesh geometry");
                mesh.boneWeights = new[] {a,a,a,b,b,b};
                Vector3[] flipped = mesh.normals;
                flipped[3]=-Vector3.forward; flipped[5]=-Vector3.forward;
                mesh.normals=flipped;
                if (Reconcile(mesh)!=0)
                    throw new InvalidOperationException("Skin seam test joined opposite surfaces");
                Debug.Log("SkinSeamReconciler: positive + negative seam tests PASS");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
#endif
