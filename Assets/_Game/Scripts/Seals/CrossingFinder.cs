using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Finds seals formed by ink CROSSING ink — the general rule the design wants:
    /// "if the ink encloses an area, it's a seal", regardless of endpoints.
    ///
    /// Fully general: every place where any ink crosses any ink (including a
    /// stroke crossing itself) becomes a graph vertex; the arcs of ink between
    /// consecutive crossings become edges. Any cycle in that graph encloses a
    /// region — so a heptagon drawn as seven overshooting segments, a scribble
    /// that loops once, or two arcs meeting in a lens all resolve the same way.
    /// The LARGEST enclosing cycle wins (Marko's rule — a small sub-loop must
    /// never steal the intended boundary). The caller splits the crossed strokes
    /// at the crossings; the enclosed arcs become boundary, the tails stay ink.
    public static class CrossingFinder
    {
        /// One arc of the boundary: an inclusive node range on a stroke, walked
        /// Lo->Hi unless Reversed (so the whole ring stays continuous).
        public struct Arc
        {
            public Stroke Stroke;
            public int Lo, Hi;
            public bool Reversed;
        }

        public struct Result
        {
            public List<Arc> Cycle;
            public bool Valid;
            public float Perimeter; // for "largest seal wins" comparison
        }

        class Xing
        {
            public int Id;
            public int SA; public float PosA; // stroke index + (segIndex + t)
            public int SB; public float PosB;
        }

        struct Appearance { public int XingId; public float Pos; }

        class Edge
        {
            public int U, V;   // crossing ids: U at the Lo end, V at the Hi end
            public Stroke Stroke;
            public int Lo, Hi; // inclusive node range between the two crossings
            public float Length;
            public List<Vector3> Pts; // world positions Lo..Hi (for area guards)
        }

        const int MaxCrossings = 400;

        public static Result Find(IReadOnlyList<Stroke> eligible)
        {
            int n = eligible.Count;
            if (n == 0) return default;

            var pts = new List<Vector3>[n];
            var bounds = new Bounds[n];
            for (int i = 0; i < n; i++)
            {
                pts[i] = WorldPoints(eligible[i]);
                bounds[i] = BoundsOf(pts[i]);
                bounds[i].Expand(0.02f);
            }

            // 1) all crossings (pairwise + self), deduped as shared vertices
            var xings = new List<Xing>();
            for (int i = 0; i < n && xings.Count < MaxCrossings; i++)
            {
                CollectCrossings(pts, i, i, xings);
                for (int j = i + 1; j < n && xings.Count < MaxCrossings; j++)
                {
                    if (!bounds[i].Intersects(bounds[j])) continue;
                    CollectCrossings(pts, i, j, xings);
                }
            }
            if (xings.Count == 0) return default;

            // 2) per-stroke sorted crossing appearances
            var appear = new List<Appearance>[n];
            for (int i = 0; i < n; i++) appear[i] = new List<Appearance>();
            foreach (var x in xings)
            {
                appear[x.SA].Add(new Appearance { XingId = x.Id, Pos = x.PosA });
                appear[x.SB].Add(new Appearance { XingId = x.Id, Pos = x.PosB });
            }
            for (int i = 0; i < n; i++) appear[i].Sort((a, b) => a.Pos.CompareTo(b.Pos));

            // 3) edges: the arc of ink between consecutive crossings on a stroke
            var edges = new List<Edge>();
            for (int i = 0; i < n; i++)
            {
                var ap = appear[i];
                for (int k = 0; k + 1 < ap.Count; k++)
                {
                    int lo = Mathf.FloorToInt(ap[k].Pos) + 1;
                    int hi = Mathf.FloorToInt(ap[k + 1].Pos);
                    if (hi < lo) continue; // both crossings on one segment: no arc between
                    var arcPts = new List<Vector3>();
                    for (int p = lo; p <= hi && p < pts[i].Count; p++) arcPts.Add(pts[i][p]);
                    edges.Add(new Edge
                    {
                        U = ap[k].XingId, V = ap[k + 1].XingId,
                        Stroke = eligible[i], Lo = lo, Hi = hi,
                        Length = PolyLength(arcPts), Pts = arcPts
                    });
                }
            }
            if (edges.Count == 0) return default;

            // a stroke that crosses ITSELF two or more times is a complex
            // GLYPH being drawn (a five-point star self-crosses five times),
            // not a lasso — its internal cells must never self-seal and eat
            // the drawing (Marko's star: "Seal resolved — ink consumed" left
            // a grey star inside an empty gold circle). ONE self-crossing
            // stays a seal: that's the overshoot-closed circle, the most
            // common seal in the game.
            var selfXings = new Dictionary<Stroke, int>();
            foreach (var x in xings)
                if (x.SA == x.SB)
                {
                    var s = eligible[x.SA];
                    selfXings[s] = selfXings.TryGetValue(s, out var cnt) ? cnt + 1 : 1;
                }
            var glyphish = new HashSet<Stroke>();
            foreach (var kv in selfXings)
                if (kv.Value >= 2) glyphish.Add(kv.Key);

            // 4) largest enclosing cycle
            return FindLargestCycle(edges, glyphish);
        }

        // ---- crossings between stroke i and stroke j (i==j => self) ----
        static void CollectCrossings(List<Vector3>[] pts, int i, int j, List<Xing> xings)
        {
            var a = pts[i];
            var b = pts[j];
            if (a.Count < 2 || b.Count < 2) return;

            // fit a plane from the involved points and work in 2D
            Project(a, b, i == j, out var pa, out var pb);

            for (int s = 0; s < pa.Count - 1; s++)
            {
                int tStart = (i == j) ? s + 2 : 0; // self: skip adjacent segments
                for (int t = tStart; t < pb.Count - 1; t++)
                {
                    if (i == j && s == 0 && t == pb.Count - 2) continue; // the two endpoints: endpoint-closure, not a cross
                    if (GeometryUtil.SegmentsIntersect(pa[s], pa[s + 1], pb[t], pb[t + 1], out float ts, out float tt))
                    {
                        xings.Add(new Xing
                        {
                            Id = xings.Count,
                            SA = i, PosA = s + ts,
                            SB = j, PosB = t + tt
                        });
                        if (xings.Count >= MaxCrossings) return;
                    }
                }
            }
        }

        static void Project(List<Vector3> a, List<Vector3> b, bool self,
                            out List<Vector2> pa, out List<Vector2> pb)
        {
            var all = new List<Vector3>(a);
            if (!self) all.AddRange(b);
            Vector3 origin = Vector3.zero;
            foreach (var p in all) origin += p;
            origin /= all.Count;

            Vector3 normal = GeometryUtil.NewellNormal(all);
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.up;
            normal.Normalize();
            GeometryUtil.PlaneBasis(normal, out var u, out var v);

            pa = GeometryUtil.ProjectToPlane(a, origin, u, v);
            pb = self ? pa : GeometryUtil.ProjectToPlane(b, origin, u, v);
        }

        // ---- LARGEST (by perimeter) valid cycle. Was shortest — which let the
        // tiny sliver wedge from overshoot-crossed circle ends "win", seal as a
        // 3-edge 0.3s blip, and CONSUME the shared ink, breaking the intended
        // big loop. When ink crossings enclose several regions, the big one is
        // the intent; the overshoot slivers are drawing debris. ----
        static Result FindLargestCycle(List<Edge> edges, HashSet<Stroke> glyphish)
        {
            var adj = new Dictionary<int, List<int>>();
            void AddAdj(int vert, int e)
            {
                if (!adj.TryGetValue(vert, out var l)) { l = new List<int>(); adj[vert] = l; }
                l.Add(e);
            }
            for (int e = 0; e < edges.Count; e++)
            {
                AddAdj(edges[e].U, e);
                if (edges[e].V != edges[e].U) AddAdj(edges[e].V, e);
            }

            List<int> best = null;
            float bestPerim = -1f;

            for (int e = 0; e < edges.Count; e++)
            {
                var ed = edges[e];
                List<int> cycle;
                if (ed.U == ed.V)
                {
                    cycle = new List<int> { e }; // self-loop is a cycle on its own
                }
                else
                {
                    var path = ShortestPath(edges, adj, ed.U, ed.V, e);
                    if (path == null) continue;
                    path.Add(e);
                    cycle = path;
                }

                float perim = 0f;
                foreach (var ei in cycle) perim += edges[ei].Length;
                if (perim <= bestPerim) continue;
                if (AllFromOneGlyph(edges, cycle, glyphish)) continue; // stars stay drawings
                if (!Encloses(edges, cycle)) continue;
                bestPerim = perim;
                best = cycle;
            }

            if (best == null) return default;
            return BuildResult(edges, best);
        }

        /// True when every arc of the cycle belongs to a single multi-self-
        /// crossing stroke — the cells inside a star/pentagram-style glyph.
        /// Mixed-stroke cycles (a lasso over other ink, a polygon of separate
        /// segments) are never suppressed.
        /// True only for a SMALL cell entirely inside one self-crossing stroke
        /// — a star's inner point, not a seal. Marko's rule: ANY real-sized
        /// loop closes no matter how many times it crosses itself, so we
        /// suppress only cells below rune scale; a big wobbly loop always seals.
        static bool AllFromOneGlyph(List<Edge> edges, List<int> cycle, HashSet<Stroke> glyphish)
        {
            if (cycle == null || cycle.Count == 0 || glyphish.Count == 0) return false;
            var first = edges[cycle[0]].Stroke;
            if (!glyphish.Contains(first)) return false;
            foreach (var ei in cycle)
                if (edges[ei].Stroke != first) return false;

            // size gate: only a small cell is a glyph-part; a large loop seals
            float minx = float.MaxValue, maxx = -float.MaxValue, miny = float.MaxValue, maxy = -float.MaxValue,
                  minz = float.MaxValue, maxz = -float.MaxValue;
            foreach (var ei in cycle)
                foreach (var p in edges[ei].Pts)
                {
                    minx = Mathf.Min(minx, p.x); maxx = Mathf.Max(maxx, p.x);
                    miny = Mathf.Min(miny, p.y); maxy = Mathf.Max(maxy, p.y);
                    minz = Mathf.Min(minz, p.z); maxz = Mathf.Max(maxz, p.z);
                }
            float diag = new Vector3(maxx - minx, maxy - miny, maxz - minz).magnitude;
            return diag < DrawingConfig.GlyphCellMax; // small = star cell; large = a seal
        }

        static List<int> ShortestPath(List<Edge> edges, Dictionary<int, List<int>> adj,
                                      int start, int goal, int excludeEdge)
        {
            var dist = new Dictionary<int, float> { [start] = 0f };
            var prevEdge = new Dictionary<int, int>();
            var visited = new HashSet<int>();
            var frontier = new List<int> { start };

            while (frontier.Count > 0)
            {
                // pop nearest
                int bi = 0;
                for (int k = 1; k < frontier.Count; k++)
                    if (dist[frontier[k]] < dist[frontier[bi]]) bi = k;
                int cur = frontier[bi];
                frontier.RemoveAt(bi);
                if (!visited.Add(cur)) continue;
                if (cur == goal) break;

                if (!adj.TryGetValue(cur, out var incident)) continue;
                foreach (var e in incident)
                {
                    if (e == excludeEdge) continue;
                    var ed = edges[e];
                    int other = ed.U == cur ? ed.V : ed.U;
                    if (visited.Contains(other)) continue;
                    float nd = dist[cur] + ed.Length;
                    if (!dist.TryGetValue(other, out var od) || nd < od)
                    {
                        dist[other] = nd;
                        prevEdge[other] = e;
                        frontier.Add(other);
                    }
                }
            }

            if (!prevEdge.ContainsKey(goal)) return null;
            var path = new List<int>();
            int node = goal;
            int guard = 0;
            while (node != start && guard++ < edges.Count + 2)
            {
                int e = prevEdge[node];
                path.Add(e);
                var ed = edges[e];
                node = ed.U == node ? ed.V : ed.U;
            }
            return node == start ? path : null;
        }

        static bool Encloses(List<Edge> edges, List<int> cycle)
        {
            var ring = new List<Vector3>();
            int nodeCount = 0;
            float perim = 0f;
            foreach (var ei in cycle)
            {
                ring.AddRange(edges[ei].Pts);
                nodeCount += edges[ei].Pts.Count;
                perim += edges[ei].Length;
            }
            if (nodeCount < DrawingConfig.MinLoopNodes) return false;
            if (perim < DrawingConfig.MinLoopPerimeter) return false;
            if (ring.Count < 3) return false;

            // reject thin slivers: the ring must span in two directions
            Vector3 c = Vector3.zero;
            foreach (var p in ring) c += p;
            c /= ring.Count;
            Vector3 normal = GeometryUtil.NewellNormal(ring);
            if (normal.sqrMagnitude < 1e-8f) return false;
            normal.Normalize();
            GeometryUtil.PlaneBasis(normal, out var u, out var v);
            float minU = float.MaxValue, maxU = -float.MaxValue, minV = float.MaxValue, maxV = -float.MaxValue;
            foreach (var p in ring)
            {
                float du = Vector3.Dot(p - c, u);
                float dv = Vector3.Dot(p - c, v);
                minU = Mathf.Min(minU, du); maxU = Mathf.Max(maxU, du);
                minV = Mathf.Min(minV, dv); maxV = Mathf.Max(maxV, dv);
            }
            return (maxU - minU) >= DrawingConfig.MinLoopBulge && (maxV - minV) >= DrawingConfig.MinLoopBulge;
        }

        static Result BuildResult(List<Edge> edges, List<int> cycle)
        {
            var localAdj = new Dictionary<int, List<int>>();
            void AddAdj(int vert, int idx)
            {
                if (!localAdj.TryGetValue(vert, out var l)) { l = new List<int>(); localAdj[vert] = l; }
                l.Add(idx);
            }
            for (int k = 0; k < cycle.Count; k++)
            {
                var ed = edges[cycle[k]];
                AddAdj(ed.U, k);
                if (ed.V != ed.U) AddAdj(ed.V, k);
            }

            var used = new bool[cycle.Count];
            var arcs = new List<Arc>();
            int current = edges[cycle[0]].U;
            for (int step = 0; step < cycle.Count; step++)
            {
                int pick = -1;
                if (localAdj.TryGetValue(current, out var incident))
                    foreach (var k in incident)
                        if (!used[k]) { pick = k; break; }
                if (pick < 0) return default; // not a clean single cycle

                used[pick] = true;
                var ed = edges[cycle[pick]];
                bool reversed = ed.U != current;
                arcs.Add(new Arc { Stroke = ed.Stroke, Lo = ed.Lo, Hi = ed.Hi, Reversed = reversed });
                current = reversed ? ed.U : ed.V;
            }
            float perim = 0f;
            foreach (var ei in cycle) perim += edges[ei].Length;
            return new Result { Cycle = arcs, Valid = arcs.Count > 0, Perimeter = perim };
        }

        // ---- small helpers ----
        static List<Vector3> WorldPoints(Stroke s)
        {
            var pts = new List<Vector3>(s.Nodes.Count);
            foreach (var n in s.Nodes)
                if (n != null) pts.Add(n.transform.position);
            return pts;
        }

        static Bounds BoundsOf(List<Vector3> pts)
        {
            var b = new Bounds();
            for (int i = 0; i < pts.Count; i++)
            {
                if (i == 0) b = new Bounds(pts[0], Vector3.zero);
                else b.Encapsulate(pts[i]);
            }
            return b;
        }

        static float PolyLength(List<Vector3> pts)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++) len += Vector3.Distance(pts[i - 1], pts[i]);
            return len;
        }
    }
}
