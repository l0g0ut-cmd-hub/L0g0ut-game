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

        static int Shore(int region, Vector2 p)
        {
            // Authored map checkpoints can sit ON the painted bridge itself.
            // The old two-bank classification incorrectly snapped these nodes
            // to a far-bank road hub, forcing the horse to travel past the
            // checkpoint and double back across the river/lava. Attach those
            // markers to a bridge hub (shore 2), never to a bank.
            // Bounds include the +/-0.012 procedural node jitter.
            if (region == 0 && p.x >= 0.415f && p.x <= 0.525f &&
                p.y >= 0.625f && p.y <= 0.715f) return 2;
            if (region == 1 && p.x >= 0.492f && p.x <= 0.675f &&
                p.y >= 0.460f && p.y <= 0.550f) return 2;
            if (region == 2 && p.x >= 0.395f && p.x <= 0.545f &&
                p.y >= 0.570f && p.y <= 0.665f) return 2;
            if (region == 0) return p.x < (p.y > 0.58f ? 0.385f : 0.365f) ? 0 : 1;
            if (region == 1) return p.x < 0.49f ? 0 : 1;
            return p.x < (p.y > 0.52f ? 0.425f : 0.43f) ? 0 : 1;
        }

        static int Nearest(Graph graph, Vector2 pos, int region)
        {
            int shore = Shore(region, pos), best = -1;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < graph.hubs.Count; i++)
            {
                Hub hub = graph.hubs[i];
                float d = Length(pos - hub.pos);
                // Attach to the road on the SAME bank. A nearby bridge waypoint
                // must not become an off-road shortcut across water.
                if (hub.shore != shore) continue;
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

        static void MakeGraphs()
        {
            graphs = new Graph[3];
            // FOREST OF SHADOWS: riverside rice fields -> actual wooden bridge
            // at x=.408..523, y=.68, then torii clearing and castle approach.
            Graph g = graphs[0] = new Graph();
            int w0=g.Add(.27f,.66f,0), w1=g.Add(.23f,.48f,0), w2=g.Add(.30f,.77f,0);
            int w3=g.Add(.34f,.63f,0), w4=g.Add(.375f,.645f,0), w5=g.Add(.406f,.678f,0);
            int b0=g.Add(.429f,.684f,2), b1=g.Add(.460f,.679f,2);
            int b2=g.Add(.494f,.672f,2), b3=g.Add(.522f,.663f,2);
            int e0=g.Add(.55f,.646f,1), e1=g.Add(.62f,.64f,1), e2=g.Add(.68f,.54f,1);
            int e3=g.Add(.68f,.47f,1), e4=g.Add(.61f,.46f,1), e5=g.Add(.55f,.48f,1);
            int e6=g.Add(.58f,.38f,1), e7=g.Add(.65f,.36f,1), e8=g.Add(.74f,.40f,1);
            int e9=g.Add(.81f,.35f,1), e10=g.Add(.83f,.25f,1), e11=g.Add(.86f,.17f,1);
            g.Link(w0,w1);g.Link(w0,w2);g.Link(w0,w3);g.Link(w2,w3);g.Link(w1,w3);
            g.Link(w3,w4);g.Link(w4,w5);
            g.Link(w5,b0);g.Link(b0,b1);g.Link(b1,b2);g.Link(b2,b3);g.Link(b3,e0);
            g.Link(e0,e1);g.Link(e1,e2);g.Link(e0,e5);g.Link(e5,e4);
            g.Link(e4,e3);g.Link(e4,e6);g.Link(e6,e7);g.Link(e7,e8);
            g.Link(e3,e8);g.Link(e2,e3);g.Link(e8,e9);g.Link(e9,e10);
            g.Link(e10,e11);g.Link(e7,e10);

            // ONI MOUNTAINS: village stairways -> suspension bridge over lava
            // x=.48..67 at y=.50 -> volcanic plateau -> demon gate.
            g = graphs[1] = new Graph();
            w0=g.Add(.22f,.72f,0);w1=g.Add(.34f,.68f,0);w2=g.Add(.38f,.58f,0);
            w3=g.Add(.29f,.48f,0);w4=g.Add(.20f,.33f,0);w5=g.Add(.35f,.39f,0);
            int w6=g.Add(.43f,.45f,0), w7=g.Add(.476f,.504f,0);
            b0=g.Add(.505f,.509f,2);b1=g.Add(.55f,.521f,2);
            b2=g.Add(.605f,.512f,2);b3=g.Add(.658f,.494f,2);
            e0=g.Add(.705f,.481f,1);e1=g.Add(.80f,.47f,1);e2=g.Add(.75f,.35f,1);
            e3=g.Add(.85f,.32f,1);e4=g.Add(.83f,.25f,1);e5=g.Add(.81f,.17f,1);
            e6=g.Add(.91f,.36f,1);e7=g.Add(.62f,.36f,1);
            g.Link(w0,w1);g.Link(w1,w2);g.Link(w2,w3);g.Link(w3,w4);
            g.Link(w3,w5);g.Link(w4,w5);g.Link(w5,w6);g.Link(w2,w6);g.Link(w6,w7);
            g.Link(w7,b0);g.Link(b0,b1);g.Link(b1,b2);g.Link(b2,b3);g.Link(b3,e0);
            g.Link(e0,e1);g.Link(e0,e7);g.Link(e1,e2);g.Link(e7,e2);
            g.Link(e2,e3);g.Link(e3,e4);g.Link(e4,e5);g.Link(e3,e6);

            // SHOGUNATE: castle-town / tea garden -> stone arched bridge
            // x=.38..54 at y=.61 -> pagoda / cemetery -> final fortress.
            g = graphs[2] = new Graph();
            w0=g.Add(.27f,.62f,0);w1=g.Add(.36f,.47f,0);w2=g.Add(.29f,.36f,0);
            w3=g.Add(.38f,.75f,0);w4=g.Add(.34f,.58f,0);w5=g.Add(.378f,.617f,0);
            b0=g.Add(.405f,.625f,2);b1=g.Add(.447f,.617f,2);
            b2=g.Add(.490f,.612f,2);b3=g.Add(.530f,.612f,2);
            e0=g.Add(.57f,.60f,1);e1=g.Add(.64f,.60f,1);e2=g.Add(.65f,.49f,1);
            e3=g.Add(.56f,.39f,1);e4=g.Add(.70f,.45f,1);e5=g.Add(.79f,.58f,1);
            e6=g.Add(.75f,.36f,1);e7=g.Add(.86f,.40f,1);e8=g.Add(.80f,.23f,1);
            e9=g.Add(.82f,.15f,1);
            g.Link(w0,w1);g.Link(w0,w3);g.Link(w0,w4);g.Link(w1,w2);
            g.Link(w1,w4);g.Link(w4,w5);g.Link(w3,w5);
            g.Link(w5,b0);g.Link(b0,b1);g.Link(b1,b2);g.Link(b2,b3);g.Link(b3,e0);
            g.Link(e0,e1);g.Link(e1,e2);g.Link(e2,e3);g.Link(e2,e4);
            g.Link(e3,e4);g.Link(e4,e5);g.Link(e4,e6);g.Link(e5,e7);
            g.Link(e6,e7);g.Link(e6,e8);g.Link(e8,e9);
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
