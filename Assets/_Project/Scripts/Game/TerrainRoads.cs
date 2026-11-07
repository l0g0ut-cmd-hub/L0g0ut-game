using System;
using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Authored roads on the three painted region maps (extended east on 2026-11-07, see MapGeom).
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
                return new Vector2(d.x * MapGeom.W(activeRegion), d.y * 540f).normalized;
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
            return new Vector2(uv.x * MapGeom.W(activeRegion < 0 ? 0 : activeRegion), uv.y * 540f).magnitude;
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
            float kw = MapGeom.W(region);
            for (int i = 0; i < graph.hubs.Count; i++)
            {
                Vector2 dv = pos - graph.hubs[i].pos;
                float d = new Vector2(dv.x * kw, dv.y * 540f).magnitude;
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
            // лес теней: 92 узлов дорог, 95 отрезков; мост — BW, A2, BE, MB1, MB2, MB3, LB1, LB2
            graphs[0] = Make(new float[] {
                0.127f, 0.722f, 0, 0.132f, 0.585f, 0, 0.177f, 0.885f, 0, 0.274f, 0.66f, 2, 0.338f, 0.615f, 0, 0.306f, 0.52f, 0,
                0.394f, 0.55f, 0, 0.356f, 0.462f, 0, 0.421f, 0.445f, 0, 0.462f, 0.395f, 0, 0.515f, 0.3f, 0, 0.505f, 0.16f, 0,
                0.63f, 0.533f, 0, 0.703f, 0.6f, 0, 0.752f, 0.477f, 0, 0.723f, 0.781f, 0, 0.784f, 0.578f, 0, 0.725f, 0.91f, 0,
                0.838f, 0.59f, 0, 0.869f, 0.717f, 0, 0.946f, 0.408f, 0, 0.171f, 0.615f, 0, 0.242f, 0.672f, 2, 0.306f, 0.645f, 2,
                0.863f, 0.597f, 2, 0.893f, 0.599f, 2, 0.923f, 0.572f, 2, 0.94f, 0.544f, 0, 0.898f, 0.701f, 2, 0.927f, 0.677f, 2,
                0.932f, 0.615f, 0, 0.956f, 0.5f, 0, 0.147f, 0.712f, 0, 0.16f, 0.68f, 0, 0.17f, 0.645f, 0, 0.152f, 0.597f, 0,
                0.194f, 0.636f, 0, 0.218f, 0.655f, 0, 0.234f, 0.668f, 0, 0.324f, 0.625f, 0, 0.125f, 0.76f, 0, 0.127f, 0.8f, 0,
                0.125f, 0.835f, 0, 0.138f, 0.868f, 0, 0.158f, 0.882f, 0, 0.312f, 0.6f, 0, 0.306f, 0.56f, 0, 0.324f, 0.58f, 0,
                0.365f, 0.58f, 0, 0.324f, 0.48f, 0, 0.344f, 0.47f, 0, 0.383f, 0.5f, 0, 0.371f, 0.47f, 0, 0.412f, 0.5f, 0,
                0.383f, 0.455f, 0, 0.406f, 0.45f, 0, 0.441f, 0.425f, 0, 0.483f, 0.36f, 0, 0.5f, 0.33f, 0, 0.521f, 0.265f, 0,
                0.517f, 0.205f, 0, 0.51f, 0.18f, 0, 0.538f, 0.328f, 0, 0.562f, 0.375f, 0, 0.587f, 0.422f, 0, 0.611f, 0.48f, 0,
                0.653f, 0.57f, 0, 0.677f, 0.586f, 0, 0.719f, 0.576f, 0, 0.733f, 0.555f, 0, 0.742f, 0.535f, 0, 0.748f, 0.506f, 0,
                0.717f, 0.623f, 0, 0.73f, 0.609f, 0, 0.745f, 0.6f, 0, 0.76f, 0.59f, 0, 0.774f, 0.582f, 0, 0.722f, 0.637f, 0,
                0.736f, 0.68f, 0, 0.743f, 0.727f, 0, 0.733f, 0.768f, 0, 0.713f, 0.828f, 0, 0.718f, 0.869f, 0, 0.793f, 0.562f, 0,
                0.806f, 0.553f, 0, 0.816f, 0.566f, 0, 0.828f, 0.584f, 0, 0.955f, 0.457f, 0, 0.752f, 0.807f, 0, 0.782f, 0.816f, 0,
                0.812f, 0.791f, 0, 0.843f, 0.754f, 0,
            }, new int[] {
                0, 32, 32, 33, 33, 34, 34, 21, 1, 35, 35, 21, 21, 36, 36, 37, 37, 38, 38, 22,
                22, 3, 3, 23, 23, 39, 39, 4, 0, 40, 40, 41, 41, 42, 42, 43, 43, 44, 44, 2,
                23, 45, 45, 46, 46, 5, 4, 47, 47, 5, 4, 48, 48, 6, 5, 49, 49, 50, 50, 7,
                6, 51, 51, 52, 52, 7, 6, 53, 53, 8, 7, 54, 54, 55, 55, 8, 8, 56, 56, 9,
                9, 57, 57, 58, 58, 10, 10, 59, 59, 60, 60, 61, 61, 11, 10, 62, 62, 63, 63, 64,
                64, 65, 65, 12, 12, 66, 66, 67, 67, 13, 13, 68, 68, 69, 69, 70, 70, 71, 71, 14,
                13, 72, 72, 73, 73, 74, 74, 75, 75, 76, 76, 16, 13, 77, 77, 78, 78, 79, 79, 80,
                80, 15, 15, 81, 81, 82, 82, 17, 16, 83, 83, 84, 84, 85, 85, 86, 86, 18, 18, 24,
                24, 25, 25, 26, 26, 27, 27, 31, 31, 87, 87, 20, 15, 88, 88, 89, 89, 90, 90, 91,
                91, 19, 19, 28, 28, 29, 29, 30, 30, 27,
            });
            // горы они: 87 узлов дорог, 88 отрезков; мост — BW, B3, BE, LP1, LP2, LP3, LP4, LP5, LP6
            graphs[1] = Make(new float[] {
                0.112f, 0.67f, 0, 0.16f, 0.555f, 0, 0.203f, 0.805f, 0, 0.115f, 0.415f, 0, 0.218f, 0.445f, 0, 0.262f, 0.47f, 0,
                0.341f, 0.535f, 2, 0.388f, 0.37f, 0, 0.43f, 0.465f, 0, 0.447f, 0.35f, 0, 0.524f, 0.43f, 0, 0.474f, 0.235f, 0,
                0.613f, 0.502f, 0, 0.719f, 0.635f, 0, 0.764f, 0.504f, 0, 0.762f, 0.914f, 0, 0.711f, 0.365f, 0, 0.831f, 0.435f, 0,
                0.815f, 0.748f, 0, 0.897f, 0.262f, 0, 0.177f, 0.442f, 0, 0.287f, 0.508f, 2, 0.403f, 0.478f, 2, 0.433f, 0.395f, 0,
                0.656f, 0.734f, 2, 0.65f, 0.777f, 2, 0.66f, 0.82f, 2, 0.683f, 0.846f, 2, 0.71f, 0.865f, 2, 0.736f, 0.898f, 2,
                0.137f, 0.64f, 0, 0.154f, 0.605f, 0, 0.171f, 0.505f, 0, 0.177f, 0.47f, 0, 0.147f, 0.43f, 0, 0.197f, 0.44f, 0,
                0.138f, 0.675f, 0, 0.162f, 0.69f, 0, 0.182f, 0.735f, 0, 0.179f, 0.775f, 0, 0.188f, 0.8f, 0, 0.241f, 0.452f, 0,
                0.275f, 0.488f, 0, 0.312f, 0.53f, 0, 0.371f, 0.515f, 0, 0.421f, 0.476f, 0, 0.43f, 0.42f, 0, 0.412f, 0.385f, 0,
                0.465f, 0.4f, 0, 0.494f, 0.405f, 0, 0.465f, 0.28f, 0, 0.471f, 0.258f, 0, 0.494f, 0.38f, 0, 0.471f, 0.36f, 0,
                0.544f, 0.457f, 0, 0.567f, 0.471f, 0, 0.592f, 0.492f, 0, 0.632f, 0.508f, 0, 0.652f, 0.519f, 0, 0.663f, 0.568f, 0,
                0.675f, 0.613f, 0, 0.69f, 0.643f, 0, 0.733f, 0.602f, 0, 0.747f, 0.57f, 0, 0.758f, 0.539f, 0, 0.756f, 0.465f, 0,
                0.743f, 0.422f, 0, 0.728f, 0.389f, 0, 0.785f, 0.477f, 0, 0.805f, 0.451f, 0, 0.854f, 0.43f, 0, 0.88f, 0.404f, 0,
                0.89f, 0.367f, 0, 0.895f, 0.312f, 0, 0.687f, 0.674f, 0, 0.665f, 0.701f, 0, 0.751f, 0.91f, 0, 0.728f, 0.684f, 0,
                0.757f, 0.711f, 0, 0.786f, 0.731f, 0, 0.832f, 0.754f, 0, 0.844f, 0.779f, 0, 0.848f, 0.816f, 0, 0.839f, 0.842f, 0,
                0.813f, 0.859f, 0, 0.786f, 0.879f, 0, 0.77f, 0.891f, 0,
            }, new int[] {
                0, 30, 30, 31, 31, 1, 1, 32, 32, 33, 33, 20, 20, 34, 34, 3, 20, 35, 35, 4,
                0, 36, 36, 37, 37, 38, 38, 39, 39, 40, 40, 2, 4, 41, 41, 5, 5, 42, 42, 21,
                21, 43, 43, 6, 6, 44, 44, 22, 22, 45, 45, 8, 8, 46, 46, 23, 23, 47, 47, 7,
                23, 9, 23, 48, 48, 49, 49, 10, 9, 50, 50, 51, 51, 11, 10, 52, 52, 53, 53, 9,
                10, 54, 54, 55, 55, 56, 56, 12, 12, 57, 57, 58, 58, 59, 59, 60, 60, 61, 61, 13,
                13, 62, 62, 63, 63, 64, 64, 14, 14, 65, 65, 66, 66, 67, 67, 16, 14, 68, 68, 69,
                69, 17, 17, 70, 70, 71, 71, 72, 72, 73, 73, 19, 13, 74, 74, 75, 75, 24, 24, 25,
                25, 26, 26, 27, 27, 28, 28, 29, 29, 76, 76, 15, 13, 77, 77, 78, 78, 79, 79, 18,
                18, 80, 80, 81, 81, 82, 82, 83, 83, 84, 84, 85, 85, 86, 86, 15,
            });
            // сёгунат: 79 узлов дорог, 81 отрезков; мост — BW, B2, BE, CB1, CB2
            graphs[2] = Make(new float[] {
                0.183f, 0.665f, 0, 0.071f, 0.46f, 0, 0.205f, 0.79f, 0, 0.104f, 0.378f, 0, 0.254f, 0.612f, 2, 0.321f, 0.455f, 0,
                0.344f, 0.58f, 0, 0.393f, 0.42f, 0, 0.426f, 0.6f, 0, 0.491f, 0.468f, 0, 0.469f, 0.585f, 0, 0.475f, 0.19f, 0,
                0.584f, 0.646f, 0, 0.676f, 0.609f, 0, 0.707f, 0.771f, 0, 0.75f, 0.547f, 0, 0.783f, 0.695f, 0, 0.75f, 0.395f, 0,
                0.807f, 0.451f, 0, 0.859f, 0.721f, 0, 0.905f, 0.328f, 0, 0.212f, 0.628f, 2, 0.299f, 0.6f, 2, 0.846f, 0.411f, 2,
                0.877f, 0.378f, 2, 0.082f, 0.415f, 0, 0.319f, 0.592f, 0, 0.336f, 0.53f, 0, 0.327f, 0.49f, 0, 0.344f, 0.462f, 0,
                0.371f, 0.44f, 0, 0.363f, 0.52f, 0, 0.382f, 0.47f, 0, 0.382f, 0.588f, 0, 0.436f, 0.432f, 0, 0.469f, 0.45f, 0,
                0.447f, 0.595f, 0, 0.486f, 0.505f, 0, 0.499f, 0.4f, 0, 0.496f, 0.33f, 0, 0.488f, 0.27f, 0, 0.48f, 0.22f, 0,
                0.188f, 0.635f, 0, 0.175f, 0.595f, 0, 0.153f, 0.575f, 0, 0.131f, 0.545f, 0, 0.109f, 0.525f, 0, 0.085f, 0.5f, 0,
                0.199f, 0.648f, 0, 0.191f, 0.7f, 0, 0.198f, 0.75f, 0, 0.51f, 0.508f, 0, 0.526f, 0.553f, 0, 0.543f, 0.596f, 0,
                0.565f, 0.631f, 0, 0.605f, 0.625f, 0, 0.636f, 0.613f, 0, 0.657f, 0.609f, 0, 0.693f, 0.586f, 0, 0.714f, 0.571f, 0,
                0.735f, 0.559f, 0, 0.757f, 0.508f, 0, 0.761f, 0.447f, 0, 0.754f, 0.416f, 0, 0.771f, 0.488f, 0, 0.789f, 0.465f, 0,
                0.895f, 0.352f, 0, 0.579f, 0.684f, 0, 0.586f, 0.728f, 0, 0.604f, 0.757f, 0, 0.632f, 0.769f, 0, 0.668f, 0.773f, 0,
                0.735f, 0.776f, 0, 0.764f, 0.769f, 0, 0.781f, 0.734f, 0, 0.76f, 0.625f, 0, 0.75f, 0.586f, 0, 0.799f, 0.734f, 0,
                0.828f, 0.742f, 0,
            }, new int[] {
                1, 25, 25, 3, 21, 4, 4, 22, 22, 26, 26, 6, 6, 27, 27, 28, 28, 5, 5, 29,
                29, 30, 30, 7, 6, 31, 31, 32, 32, 7, 6, 33, 33, 8, 7, 34, 34, 35, 35, 9,
                8, 36, 36, 10, 10, 37, 37, 9, 9, 38, 38, 39, 39, 40, 40, 41, 41, 11, 0, 42,
                42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47, 1, 0, 48, 48, 21, 0, 49, 49, 50,
                50, 2, 9, 51, 51, 52, 52, 53, 53, 54, 54, 12, 12, 55, 55, 56, 56, 57, 57, 13,
                13, 58, 58, 59, 59, 60, 60, 15, 15, 61, 61, 62, 62, 63, 63, 17, 15, 64, 64, 65,
                65, 18, 18, 23, 23, 24, 24, 66, 66, 20, 12, 67, 67, 68, 68, 69, 69, 70, 70, 71,
                71, 14, 14, 72, 72, 73, 73, 74, 74, 16, 16, 75, 75, 76, 76, 15, 16, 77, 77, 78,
                78, 19,
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

        /// <summary>Место стоит прямо на мосту (узел дороги с пометкой «мост»): там не ставят домов и стоянок.</summary>
        public static bool OnBridge(int region, Vector2 pos)
        {
            if (graphs == null) MakeGraphs();
            Graph g = graphs[Mathf.Clamp(region, 0, 2)];
            int h = Nearest(g, pos, region);
            if (h < 0) return false;
            Vector2 dv = g.hubs[h].pos - pos;
            return g.hubs[h].shore == 2 && new Vector2(dv.x * MapGeom.W(region), dv.y * 540f).magnitude < 12f;
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
