using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Authored roads on the three ORIGINAL painted panorama maps.
    /// All coordinates are 0..1 image UV, with Y measured DOWN from the top
    /// (matching Campaign.Spots and MapScreen). Every west/east graph connects
    /// ONLY over the illustrated bridge. No node, reward, save or roll changes.
    ///
    /// One path is used by map lines, footprints, travelling horse, direction,
    /// camera follow and active-route glints. Cached per campaign node pair.
    /// </summary>
    public static class TerrainRoads
    {
        public sealed class Path
        {
            public readonly Vector2[] points;
            readonly float[] cumulative;
            public readonly float lengthPixels;

            public Path(List<Vector2> source)
            {
                List<Vector2> rounded = RoundCorners(source);
                if (rounded.Count < 2)
                {
                    Vector2 at = rounded.Count == 0 ? Vector2.zero : rounded[0];
                    rounded.Add(at);
                }
                points = rounded.ToArray();
                cumulative = new float[points.Length];
                float distance = 0f;
                for (int i = 1; i < points.Length; i++)
                {
                    distance += Length(points[i] - points[i - 1]);
                    cumulative[i] = distance;
                }
                lengthPixels = Mathf.Max(0.001f, distance);
            }

            public Vector2 Position(float progress)
            {
                float target = Mathf.Clamp01(progress) * lengthPixels;
                for (int i = 1; i < points.Length; i++)
                {
                    if (target > cumulative[i] && i < points.Length - 1) continue;
                    float segment = cumulative[i] - cumulative[i - 1];
                    float u = segment > 0.0001f ?
                        (target - cumulative[i - 1]) / segment : 1f;
                    return Vector2.LerpUnclamped(points[i - 1], points[i], Mathf.Clamp01(u));
                }
                return points[points.Length - 1];
            }

            public Vector2 Direction(float progress)
            {
                // Compare positions in screen-space, not UV: the panorama is 810x540.
                Vector2 d = Position(Mathf.Min(1f, progress + 0.018f)) -
                            Position(Mathf.Max(0f, progress - 0.018f));
                return new Vector2(d.x * 810f, d.y * 540f).normalized;
            }

            static List<Vector2> RoundCorners(List<Vector2> raw)
            {
                List<Vector2> smooth = new List<Vector2>(raw.Count * 5);
                if (raw.Count == 0) return smooth;
                smooth.Add(raw[0]);
                for (int i = 1; i < raw.Count - 1; i++)
                {
                    Vector2 prev = raw[i - 1], cur = raw[i], next = raw[i + 1];
                    float before = Vector2.Distance(prev, cur);
                    float after = Vector2.Distance(cur, next);
                    if (before < 0.002f || after < 0.002f) continue;
                    float r = Mathf.Min(0.009f, Mathf.Min(before, after) * 0.20f);
                    Vector2 entry = cur - (cur - prev).normalized * r;
                    Vector2 exit = cur + (next - cur).normalized * r;
                    smooth.Add(entry);
                    for (int k = 1; k <= 3; k++)
                    {
                        float t = k / 4f, v = 1f - t;
                        smooth.Add(v * v * entry + 2f * v * t * cur + t * t * exit);
                    }
                    smooth.Add(exit);
                }
                smooth.Add(raw[raw.Count - 1]);
                return smooth;
            }
        }

        sealed class Hub
        {
            public Vector2 pos;
            public int shore;  // 0 = starting bank, 1 = far bank, 2 = on bridge
            public readonly List<int> next = new List<int>(4);
        }

        sealed class Graph
        {
            public readonly List<Hub> hubs = new List<Hub>(32);
            public int Add(float x, float y, int shore)
            {
                hubs.Add(new Hub { pos = new Vector2(x, y), shore = shore });
                return hubs.Count - 1;
            }
            public void Link(int a, int b)
            {
                if (!hubs[a].next.Contains(b)) hubs[a].next.Add(b);
                if (!hubs[b].next.Contains(a)) hubs[b].next.Add(a);
            }
        }

        static Graph[] graphs;
        static int activeRegion = -1;
        static List<MapNode> activeNodes;
        // Route cache keys are node indices. A campaign can regenerate node
        // coordinates in the same List instance; reference equality alone
        // does not prove that cached geometry is still valid.
        static Vector2[] cachedNodePositions;
        static readonly Dictionary<long, Path> cache = new Dictionary<long, Path>(160);

        static float Length(Vector2 uv)
        {
            return new Vector2(uv.x * 810f, uv.y * 540f).magnitude;
        }

        public static void Bind(int region, List<MapNode> nodes)
        {
            if (graphs == null) MakeGraphs();
            int clampedRegion = Mathf.Clamp(region, 0, 2);
            bool changed = clampedRegion != activeRegion ||
                !ReferenceEquals(nodes, activeNodes) ||
                (nodes == null ? cachedNodePositions != null :
                    cachedNodePositions == null || cachedNodePositions.Length != nodes.Count);
            if (!changed && nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    Vector2 previous = cachedNodePositions[i];
                    if (previous.x != nodes[i].x || previous.y != nodes[i].y)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (changed)
            {
                cache.Clear();
                if (nodes == null) cachedNodePositions = null;
                else
                {
                    cachedNodePositions = new Vector2[nodes.Count];
                    for (int i = 0; i < nodes.Count; i++)
                        cachedNodePositions[i] = new Vector2(nodes[i].x, nodes[i].y);
                }
            }
            activeRegion = clampedRegion;
            activeNodes = nodes;
        }

        public static Path Route(int from, int to)
        {
            if (activeNodes == null || from < 0 || to < 0 ||
                from >= activeNodes.Count || to >= activeNodes.Count)
                return new Path(new List<Vector2> { Vector2.zero, Vector2.zero });
            long key = ((long)from << 32) | (uint)to;
            Path result;
            if (cache.TryGetValue(key, out result)) return result;
            MapNode a = activeNodes[from], b = activeNodes[to];
            result = Build(graphs[activeRegion],
                new Vector2(a.x, a.y), new Vector2(b.x, b.y), activeRegion);
            cache[key] = result;
            return result;
        }

        // Places on the map sit exactly on road hubs (tools/map_roads.json), so the nearest
        // hub is the place itself; old saves are moved onto the new places on load.
        static int Nearest(Graph graph, Vector2 pos, int region)
        {
            int best = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < graph.hubs.Count; i++)
            {
                float d = Length(pos - graph.hubs[i].pos);
                if (d < distance) { best = i; distance = d; }
            }
            return best;
        }

        static Path Build(Graph graph, Vector2 start, Vector2 end, int region)
        {
            int s = Nearest(graph, start, region), finish = Nearest(graph, end, region);
            if (s < 0 || finish < 0)
                return new Path(new List<Vector2> { start, end });

            int n = graph.hubs.Count;
            float[] dist = new float[n];
            int[] parents = new int[n];
            bool[] visited = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.PositiveInfinity; parents[i] = -1; }
            dist[s] = 0f;
            for (int iteration = 0; iteration < n; iteration++)
            {
                int u = -1;
                for (int i = 0; i < n; i++)
                    if (!visited[i] && (u < 0 || dist[i] < dist[u])) u = i;
                if (u < 0 || float.IsInfinity(dist[u])) break;
                visited[u] = true;
                if (u == finish) break;
                foreach (int v in graph.hubs[u].next)
                {
                    float d = dist[u] + Length(graph.hubs[u].pos - graph.hubs[v].pos);
                    if (d < dist[v]) { dist[v] = d; parents[v] = u; }
                }
            }

            List<Vector2> points = new List<Vector2>(24) { start };
            List<int> reverse = new List<int>(24);
            int current = finish;
            int guard = 0;
            while (current >= 0 && guard++ <= n)
            {
                reverse.Add(current);
                if (current == s) break;
                current = parents[current];
            }
            if (reverse.Count == 0 || reverse[reverse.Count - 1] != s)
                return new Path(new List<Vector2> { start, end });

            for (int i = reverse.Count - 1; i >= 0; i--)
            {
                Vector2 waypoint = graph.hubs[reverse[i]].pos;
                if ((waypoint - points[points.Count - 1]).sqrMagnitude > 0.000001f)
                    points.Add(waypoint);
            }
            if ((end - points[points.Count - 1]).sqrMagnitude > 0.000001f)
                points.Add(end);
            return new Path(points);
        }

        static Graph Make(float[] hubs, int[] links)
        {
            Graph g = new Graph();
            for (int i = 0; i + 2 < hubs.Length; i += 3) g.Add(hubs[i], hubs[i + 1], (int)hubs[i + 2]);
            for (int i = 0; i + 1 < links.Length; i += 2) g.Link(links[i], links[i + 1]);
            return g;
        }

        // ROADS-GRAPH BEGIN (сгенерировано tools/gen_map_roads.py из tools/map_roads.json — правьте JSON)
        static void MakeGraphs()
        {
            graphs = new Graph[3];
            // лес теней: 45 узлов дорог, 47 отрезков; мост — BW, A2, BE
            graphs[0] = Make(new float[] {
                0.215f, 0.722f, 0, 0.225f, 0.585f, 0, 0.3f, 0.885f, 0, 0.465f, 0.66f, 2, 0.575f, 0.615f, 0, 0.52f, 0.52f, 0,
                0.67f, 0.55f, 0, 0.605f, 0.462f, 0, 0.715f, 0.445f, 0, 0.785f, 0.395f, 0, 0.875f, 0.3f, 0, 0.858f, 0.16f, 0,
                0.29f, 0.615f, 0, 0.412f, 0.672f, 2, 0.52f, 0.645f, 2, 0.25f, 0.712f, 0, 0.272f, 0.68f, 0, 0.288f, 0.645f, 0,
                0.258f, 0.597f, 0, 0.33f, 0.636f, 0, 0.37f, 0.655f, 0, 0.398f, 0.668f, 0, 0.55f, 0.625f, 0, 0.212f, 0.76f, 0,
                0.215f, 0.8f, 0, 0.212f, 0.835f, 0, 0.235f, 0.868f, 0, 0.268f, 0.882f, 0, 0.53f, 0.6f, 0, 0.52f, 0.56f, 0,
                0.55f, 0.58f, 0, 0.62f, 0.58f, 0, 0.55f, 0.48f, 0, 0.585f, 0.47f, 0, 0.65f, 0.5f, 0, 0.63f, 0.47f, 0,
                0.7f, 0.5f, 0, 0.65f, 0.455f, 0, 0.69f, 0.45f, 0, 0.75f, 0.425f, 0, 0.82f, 0.36f, 0, 0.85f, 0.33f, 0,
                0.885f, 0.265f, 0, 0.878f, 0.205f, 0, 0.866f, 0.18f, 0,
            }, new int[] {
                0, 15, 15, 16, 16, 17, 17, 12, 1, 18, 18, 12, 12, 19, 19, 20, 20, 21, 21, 13,
                13, 3, 3, 14, 14, 22, 22, 4, 0, 23, 23, 24, 24, 25, 25, 26, 26, 27, 27, 2,
                14, 28, 28, 29, 29, 5, 4, 30, 30, 5, 4, 31, 31, 6, 5, 32, 32, 33, 33, 7,
                6, 34, 34, 35, 35, 7, 6, 36, 36, 8, 7, 37, 37, 38, 38, 8, 8, 39, 39, 9,
                9, 40, 40, 41, 41, 10, 10, 42, 42, 43, 43, 44, 44, 11,
            });
            // горы они: 40 узлов дорог, 40 отрезков; мост — BW, B3, BE
            graphs[1] = Make(new float[] {
                0.19f, 0.67f, 0, 0.272f, 0.555f, 0, 0.345f, 0.805f, 0, 0.195f, 0.415f, 0, 0.37f, 0.445f, 0, 0.445f, 0.47f, 0,
                0.58f, 0.535f, 2, 0.66f, 0.37f, 0, 0.73f, 0.465f, 0, 0.76f, 0.35f, 0, 0.89f, 0.43f, 0, 0.805f, 0.235f, 0,
                0.3f, 0.442f, 0, 0.488f, 0.508f, 2, 0.685f, 0.478f, 2, 0.735f, 0.395f, 0, 0.232f, 0.64f, 0, 0.262f, 0.605f, 0,
                0.29f, 0.505f, 0, 0.3f, 0.47f, 0, 0.25f, 0.43f, 0, 0.335f, 0.44f, 0, 0.235f, 0.675f, 0, 0.275f, 0.69f, 0,
                0.31f, 0.735f, 0, 0.305f, 0.775f, 0, 0.32f, 0.8f, 0, 0.41f, 0.452f, 0, 0.468f, 0.488f, 0, 0.53f, 0.53f, 0,
                0.63f, 0.515f, 0, 0.715f, 0.476f, 0, 0.73f, 0.42f, 0, 0.7f, 0.385f, 0, 0.79f, 0.4f, 0, 0.84f, 0.405f, 0,
                0.79f, 0.28f, 0, 0.8f, 0.258f, 0, 0.84f, 0.38f, 0, 0.8f, 0.36f, 0,
            }, new int[] {
                0, 16, 16, 17, 17, 1, 1, 18, 18, 19, 19, 12, 12, 20, 20, 3, 12, 21, 21, 4,
                0, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26, 2, 4, 27, 27, 5, 5, 28, 28, 13,
                13, 29, 29, 6, 6, 30, 30, 14, 14, 31, 31, 8, 8, 32, 32, 15, 15, 33, 33, 7,
                15, 9, 15, 34, 34, 35, 35, 10, 9, 36, 36, 37, 37, 11, 10, 38, 38, 39, 39, 9,
            });
            // сёгунат: 40 узлов дорог, 41 отрезков; мост — BW, B2, BE
            graphs[2] = Make(new float[] {
                0.335f, 0.665f, 0, 0.13f, 0.46f, 0, 0.375f, 0.79f, 0, 0.19f, 0.378f, 0, 0.465f, 0.612f, 2, 0.588f, 0.455f, 0,
                0.63f, 0.58f, 0, 0.72f, 0.42f, 0, 0.78f, 0.6f, 0, 0.9f, 0.468f, 0, 0.86f, 0.585f, 0, 0.87f, 0.19f, 0,
                0.388f, 0.628f, 2, 0.548f, 0.6f, 2, 0.15f, 0.415f, 0, 0.585f, 0.592f, 0, 0.615f, 0.53f, 0, 0.6f, 0.49f, 0,
                0.63f, 0.462f, 0, 0.68f, 0.44f, 0, 0.665f, 0.52f, 0, 0.7f, 0.47f, 0, 0.7f, 0.588f, 0, 0.8f, 0.432f, 0,
                0.86f, 0.45f, 0, 0.82f, 0.595f, 0, 0.89f, 0.505f, 0, 0.915f, 0.4f, 0, 0.91f, 0.33f, 0, 0.895f, 0.27f, 0,
                0.88f, 0.22f, 0, 0.345f, 0.635f, 0, 0.32f, 0.595f, 0, 0.28f, 0.575f, 0, 0.24f, 0.545f, 0, 0.2f, 0.525f, 0,
                0.155f, 0.5f, 0, 0.365f, 0.648f, 0, 0.35f, 0.7f, 0, 0.362f, 0.75f, 0,
            }, new int[] {
                1, 14, 14, 3, 12, 4, 4, 13, 13, 15, 15, 6, 6, 16, 16, 17, 17, 5, 5, 18,
                18, 19, 19, 7, 6, 20, 20, 21, 21, 7, 6, 22, 22, 8, 7, 23, 23, 24, 24, 9,
                8, 25, 25, 10, 10, 26, 26, 9, 9, 27, 27, 28, 28, 29, 29, 30, 30, 11, 0, 31,
                31, 32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 1, 0, 37, 37, 12, 0, 38, 38, 39,
                39, 2,
            });
        }
        // ROADS-GRAPH END

        /// <summary>
        /// Соседи мест по НАРИСОВАННЫМ дорогам: B — сосед A, если по дорогам карты от A до B можно доехать,
        /// не проезжая через другое место. Раньше дороги между местами придумывались по номерам (i-е место ряда → j-е
        /// следующего), и путь «соседей» на карте шёл через чужие места: всадник проезжал засаду, деревню, тайник.
        /// Убранное место (дорога сходится в одну точку) — просто развилка, через неё проезжают.
        /// </summary>
        public static List<int>[] PlaceLinks(int region, List<MapNode> nodes)
        {
            if (graphs == null) MakeGraphs();
            Graph g = graphs[Mathf.Clamp(region, 0, 2)];
            int n = nodes.Count;
            List<int>[] res = new List<int>[n];
            int[] hubOf = new int[n];
            Dictionary<int, int> owner = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                res[i] = new List<int>(4);
                hubOf[i] = Nearest(g, new Vector2(nodes[i].x, nodes[i].y), region);
                if (hubOf[i] >= 0 && !owner.ContainsKey(hubOf[i])) owner[hubOf[i]] = i;
            }
            bool[] seen = new bool[g.hubs.Count];
            Stack<int> stack = new Stack<int>(16);
            for (int i = 0; i < n; i++)
            {
                if (hubOf[i] < 0) continue;
                System.Array.Clear(seen, 0, seen.Length);
                stack.Clear();
                seen[hubOf[i]] = true; stack.Push(hubOf[i]);
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    foreach (int v in g.hubs[u].next)
                    {
                        if (seen[v]) continue;
                        seen[v] = true;
                        int o;
                        if (owner.TryGetValue(v, out o)) { if (o != i && !res[i].Contains(o)) res[i].Add(o); continue; }   // дальше этого места не едем
                        stack.Push(v);
                    }
                }
            }
            return res;
        }

        // Diagnostics for build-time and debug tests: road crossings MUST contain
        // an actual bridge waypoint. Public inspection never modifies route data.
        public static bool UsesBridge(int from, int to)
        {
            Path path = Route(from, to);
            Graph graph = graphs[Mathf.Clamp(activeRegion, 0, 2)];
            for (int i = 0; i < graph.hubs.Count; i++)
            {
                if (graph.hubs[i].shore != 2) continue;
                Vector2 bridge = graph.hubs[i].pos;
                foreach (Vector2 point in path.points)
                    if (Vector2.Distance(point, bridge) < 0.003f) return true;
            }
            return false;
        }
    }
}
