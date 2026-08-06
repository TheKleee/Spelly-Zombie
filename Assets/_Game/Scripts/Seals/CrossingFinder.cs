using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Finds seals formed by ink MEETING ink — the general rule the design wants:
    /// "if the ink encloses an area, it's a seal", regardless of endpoints.
    ///
    /// Fully general: every place where any ink meets any ink (including a
    /// stroke meeting itself) becomes a graph vertex; the arcs of ink between
    /// consecutive vertices become edges. Any cycle in that graph encloses a
    /// region — so a heptagon drawn as seven overshooting segments, a scribble
    /// that loops once, or two arcs meeting in a lens all resolve the same way.
    /// The LARGEST enclosing cycle wins (Marko's rule — a small sub-loop must
    /// never steal the intended boundary). The caller splits the involved strokes
    /// at the vertices; the enclosed arcs become boundary, the tails stay ink.
    ///
    /// TWO KINDS OF JUNCTION, because TOUCHING IS TOUCHING WHEREVER IT HAPPENS
    /// (Marko, Aug 1: "everything must touch exactly"):
    ///   · a CROSSING — the two lines properly cross (needs an overshoot);
    ///   · a TOUCH — one line's END lands ON another line, anywhere along it.
    /// The touch half was missing, and that was the whole of his multi-stroke
    /// bug. GeometryUtil.SegmentsIntersect refuses t==1 by construction, so a
    /// stroke that STOPS on the line it meets registered nothing — and stopping
    /// exactly on the line is what a careful drawer does. A square drawn as four
    /// strokes, each ending on the previous one's mid-span, produced ZERO
    /// vertices: the better he drew, the worse it worked. Endpoint-to-endpoint
    /// chaining (SealDetector) could not see it either — its model is "a stroke
    /// is an edge between its two ENDS", i.e. exactly two attachment points per
    /// stroke, so a line landing on another line's MIDDLE is invisible to it.
    /// The rune path already had this fix (RuneGraph.SplitTJunctions); this is
    /// the seal half of it.
    public static class CrossingFinder
    {
        /// Why an ALMOST-junction was refused — surfaced by the caller so a
        /// near-touch never fails in silence (his standing rule).
        public static string LastNearMiss;

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
            public int Id;                    // WELDED vertex id — see WeldVertices
            public int SA; public float PosA; // stroke index + (segIndex + t)
            public int SB; public float PosB;
            public Vector3 P;                 // where in the world the two lines meet
            public bool Crossing;             // true: the lines properly CROSS; false: one line's END lands on the other
        }

        struct Appearance { public int XingId; public float Pos; }

        class Edge
        {
            public int U, V;   // crossing ids: U at the Lo end, V at the Hi end
            public Stroke Stroke;
            public int Lo, Hi; // inclusive node range between the two crossings
            public float Length;
            public readonly List<Vector3> Pts = new List<Vector3>(); // world positions Lo..Hi (for area guards)
        }

        const int MaxCrossings = 400;
        const int MaxNearMisses = 256;

        // ---- pooled scratch (Find runs at 8 Hz on the main thread, never
        // reentrant — it was the biggest single allocator in the seal path;
        // nothing pooled here ever escapes: Result carries only Arc structs) ----
        static List<Vector3>[] _pts = new List<Vector3>[16];
        static Vector3[] _norms = new Vector3[16];
        static float[][] _arc = new float[16][];
        static Bounds[] _bounds = new Bounds[16];
        static List<Appearance>[] _appear = new List<Appearance>[16];
        static readonly List<Xing> _xings = new List<Xing>();
        static readonly List<Xing> _xingPool = new List<Xing>();
        static int _xingUsed;
        static readonly List<Edge> _edges = new List<Edge>();
        static readonly List<Edge> _edgePool = new List<Edge>();
        static int _edgeUsed;
        static readonly List<Vector3> _projAll = new List<Vector3>();
        static readonly List<Vector2> _projA = new List<Vector2>();
        static readonly List<Vector2> _projB = new List<Vector2>();
        static readonly Dictionary<int, float> _spDist = new Dictionary<int, float>();
        static readonly Dictionary<int, int> _spPrev = new Dictionary<int, int>();
        static readonly HashSet<int> _spVisited = new HashSet<int>();
        static readonly List<int> _spFrontier = new List<int>();

        static void EnsureScratch(int n)
        {
            if (_pts.Length < n)
            {
                int cap = Mathf.NextPowerOfTwo(n);
                System.Array.Resize(ref _pts, cap);
                System.Array.Resize(ref _norms, cap);
                System.Array.Resize(ref _arc, cap);
                System.Array.Resize(ref _bounds, cap);
                System.Array.Resize(ref _appear, cap);
            }
            for (int i = 0; i < n; i++)
            {
                if (_pts[i] == null) _pts[i] = new List<Vector3>();
                if (_appear[i] == null) _appear[i] = new List<Appearance>();
                _appear[i].Clear();
            }
        }

        /// Drop last scan's stroke references and reset the object pools.
        static void ReleaseScratch()
        {
            foreach (var e in _edges) { e.Stroke = null; e.Pts.Clear(); }
            _edges.Clear();
            _edgeUsed = 0;
            _xings.Clear();
            _xingUsed = 0;
        }

        static Xing TakeXing()
        {
            Xing x;
            if (_xingUsed < _xingPool.Count) x = _xingPool[_xingUsed];
            else { x = new Xing(); _xingPool.Add(x); }
            _xingUsed++;
            return x;
        }

        static Edge TakeEdge()
        {
            Edge e;
            if (_edgeUsed < _edgePool.Count) e = _edgePool[_edgeUsed];
            else { e = new Edge(); _edgePool.Add(e); }
            _edgeUsed++;
            e.Pts.Clear();
            return e;
        }

        /// Every endpoint that came close to ink without touching it, WITH the
        /// two strokes involved — the strokes matter, see SelectNearMiss.
        static readonly List<(float Dist, int SA, int SB)> _nearMisses =
            new List<(float, int, int)>();

        /// The closest QUALIFYING near miss this scan (see SelectNearMiss).
        static float _nearMissDist;

        /// Why a ring that was actually FOUND got thrown away. Outranks a near
        /// miss: "your loop is too small" beats "some line stops short", because
        /// the ink really did touch all the way round.
        static string _cycleReject;

        public static Result Find(IReadOnlyList<Stroke> eligible)
        {
            LastNearMiss = null;
            _nearMissDist = float.MaxValue;
            _cycleReject = null;
            _nearMisses.Clear();

            var result = FindCycle(eligible);
            ReleaseScratch(); // nothing pooled escapes via Result (Arc structs only)
            if (result.Valid) return result;

            // NEVER SILENTLY REFUSE (his standing rule). One message, and only
            // when nothing sealed. A ring that was found and then refused by a
            // size guard is the more useful complaint, so it wins; otherwise the
            // nearest gap that was plausibly MEANT to be a join gets named.
            float r = DrawingConfig.InkTouchDistance;
            if (_cycleReject != null)
                LastNearMiss = _cycleReject;
            else if (_nearMissDist < r * 3f)
                LastNearMiss = $"a line stops {_nearMissDist * 100f:0.0}cm short of the ink it should meet. nudge it ONTO the line (touching is {r * 100f:0.0}cm)";
            return result;
        }

        static Result FindCycle(IReadOnlyList<Stroke> eligible)
        {
            int n = eligible.Count;
            if (n == 0) return default;

            EnsureScratch(n);
            var pts = _pts;          // pooled per-stroke world points
            var norms = _norms;      // averaged surface normal — the plane-fit fallback
            var arc = _arc;          // running length, for the self-touch loop-size gate
            var bounds = _bounds;
            for (int i = 0; i < n; i++)
            {
                WorldPoints(eligible[i], pts[i], out norms[i]);
                arc[i] = ArcLengths(pts[i], arc[i]);
                bounds[i] = BoundsOf(pts[i]);
                // Bounds.Expand grows the SIZE by the amount, i.e. each face by
                // half of it — so this pushes each face out by one full
                // InkTouchDistance, and with BOTH boxes expanded the cull keeps
                // every pair that could possibly touch. Derived, never a literal:
                // raising InkTouchDistance in sz_tuning.json used to leave this
                // cull behind at 2cm and the knob quietly stopped working on
                // cross-stroke pairs while self-touches kept obeying it.
                bounds[i].Expand(DrawingConfig.InkTouchDistance * 2f);
            }

            // 1) every junction: ink CROSSING ink AND ink ENDING ON ink (pairwise
            //    + self). Both are "the ink meets" — his one law, one graph.
            var xings = _xings; // pooled, released after Find
            for (int i = 0; i < n && xings.Count < MaxCrossings; i++)
            {
                CollectCrossings(pts, norms, i, i, xings);
                CollectTouches(pts, arc, i, i, xings);
                for (int j = i + 1; j < n && xings.Count < MaxCrossings; j++)
                {
                    if (!bounds[i].Intersects(bounds[j])) continue;
                    CollectCrossings(pts, norms, i, j, xings);
                    CollectTouches(pts, arc, i, j, xings);
                }
            }
            if (xings.Count == 0) return default; // nothing meets: no ring, and no gap worth naming

            // 1b) decide WHICH near miss (if any) is worth telling him about,
            //     now that we know what actually touches what.
            SelectNearMiss(n, xings);

            // 2) WELD junctions that are the same physical meeting point.
            //    Two strokes whose ends meet generate TWO touches — A's end onto
            //    B's ink, and B's end onto A's ink — a hair apart. They are one
            //    corner of the drawing and must be one vertex, or each stroke
            //    gets two vertices at the same spot, the arc between them is
            //    empty, and the ring never forms.
            WeldVertices(xings);

            // 3) per-stroke sorted vertex appearances (pooled, cleared in EnsureScratch)
            var appear = _appear;
            foreach (var x in xings)
            {
                appear[x.SA].Add(new Appearance { XingId = x.Id, Pos = x.PosA });
                appear[x.SB].Add(new Appearance { XingId = x.Id, Pos = x.PosB });
            }
            for (int i = 0; i < n; i++) appear[i].Sort((a, b) => a.Pos.CompareTo(b.Pos));

            // 4) edges: the arc of ink between consecutive vertices on a stroke
            var edges = _edges; // pooled, released after Find
            for (int i = 0; i < n; i++)
            {
                var ap = appear[i];
                for (int k = 0; k + 1 < ap.Count; k++)
                {
                    int lo = Mathf.FloorToInt(ap[k].Pos) + 1;
                    int hi = Mathf.FloorToInt(ap[k + 1].Pos);
                    if (hi < lo) continue; // both crossings on one segment: no arc between
                    var ed = TakeEdge();
                    ed.U = ap[k].XingId; ed.V = ap[k + 1].XingId;
                    ed.Stroke = eligible[i]; ed.Lo = lo; ed.Hi = hi;
                    for (int p = lo; p <= hi && p < pts[i].Count; p++) ed.Pts.Add(pts[i][p]);
                    ed.Length = PolyLength(ed.Pts);
                    edges.Add(ed);
                }
            }
            if (edges.Count == 0) return default;

            // a stroke that crosses ITSELF two or more times is a complex
            // GLYPH being drawn (a five-point star self-crosses five times),
            // not a lasso — its internal cells must never self-seal and eat
            // the drawing (Marko's star: "Seal resolved - ink consumed" left
            // a grey star inside an empty gold circle). ONE self-crossing
            // stays a seal: that's the overshoot-closed circle, the most
            // common seal in the game.
            //
            // COUNT ONLY REAL SELF-*CROSSINGS*, never self-TOUCHES. A star
            // CROSSES itself; a closure TOUCHES itself. One overshoot-closed
            // circle emits up to three self-junctions — the crossing itself,
            // plus start-onto-tail and tail-tip-onto-head — and those sit an
            // OVERSHOOT apart, which beats the 1.4cm weld radius the moment the
            // overshoot is a centimetre and a half. Counting them made an
            // ordinary 9cm circle read as "self-crosses twice" = a star, and
            // AllFromOneGlyph then refused it: SIZE deciding shape, in silence,
            // over the whole palm-sized range config calls legal. Crossings are
            // welded too, so a star's five stay five.
            var selfVerts = new Dictionary<Stroke, HashSet<int>>();
            foreach (var x in xings)
                if (x.SA == x.SB && x.Crossing)
                {
                    var s = eligible[x.SA];
                    if (!selfVerts.TryGetValue(s, out var set)) { set = new HashSet<int>(); selfVerts[s] = set; }
                    set.Add(x.Id);
                }
            var glyphish = new HashSet<Stroke>();
            foreach (var kv in selfVerts)
                if (kv.Value.Count >= 2) glyphish.Add(kv.Key);

            // 5) largest enclosing cycle
            return FindLargestCycle(edges, glyphish);
        }

        /// A GAP IS ONLY A COMPLAINT IF IT WAS MEANT TO BE A JOIN.
        ///
        /// This scan runs eight times a second over ALL open ink, and "nothing
        /// sealed" is the normal state while he draws runes. Reporting the
        /// nearest approach anywhere in the world meant two runes drawn 3cm
        /// apart — deliberately apart, exactly as his law demands — were nagged
        /// forever with "nudge it ONTO the line", which is the OPPOSITE of the
        /// law it quotes. A lone open arc accused itself the same way.
        ///
        /// So: a near miss counts only when both strokes involved are already
        /// JOINED to ink, and joined to EACH OTHER through that ink. That is
        /// precisely "a chain that almost comes back around" — the square whose
        /// fourth corner stops 2cm short — and nothing else. Ink standing on its
        /// own says nothing here; a single stroke that nearly closes on itself
        /// is SealDetector's message to make, and it already makes it.
        static void SelectNearMiss(int n, List<Xing> xings)
        {
            _nearMissDist = float.MaxValue;
            if (_nearMisses.Count == 0) return;

            var parent = new int[n];
            var joined = new bool[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Root(int v)
            {
                while (parent[v] != v) { parent[v] = parent[parent[v]]; v = parent[v]; }
                return v;
            }
            foreach (var x in xings)
            {
                joined[x.SA] = true;
                joined[x.SB] = true;
                int ra = Root(x.SA), rb = Root(x.SB);
                if (ra != rb) parent[ra] = rb;
            }

            foreach (var (dist, sa, sb) in _nearMisses)
            {
                if (sa < 0 || sb < 0 || sa >= n || sb >= n) continue;
                if (!joined[sa] || !joined[sb]) continue;
                if (Root(sa) != Root(sb)) continue;
                if (dist < _nearMissDist) _nearMissDist = dist;
            }
        }

        /// One vertex per PLACE, not per detection. Weld radius is the touch law
        /// itself: two junctions closer together than the ink is wide are the
        /// same corner of the drawing.
        static void WeldVertices(List<Xing> xings)
        {
            float weld2 = DrawingConfig.InkTouchDistance * DrawingConfig.InkTouchDistance;
            var verts = new List<Vector3>();
            foreach (var x in xings)
            {
                int id = -1;
                for (int v = 0; v < verts.Count; v++)
                    if ((verts[v] - x.P).sqrMagnitude <= weld2) { id = v; break; }
                if (id < 0) { verts.Add(x.P); id = verts.Count - 1; }
                x.Id = id;
            }
        }

        // ---- crossings between stroke i and stroke j (i==j => self) ----
        static void CollectCrossings(List<Vector3>[] pts, Vector3[] norms, int i, int j, List<Xing> xings)
        {
            var a = pts[i];
            var b = pts[j];
            if (a.Count < 2 || b.Count < 2) return;

            // fit a plane from the involved points and work in 2D
            Project(a, b, i == j, norms[i] + norms[j], out var pa, out var pb);

            // ...but the answer has to be true in the WORLD too (below).
            // Generous by one extra ink width, because ink following a rough or
            // curved surface doesn't sit exactly in the fitted plane.
            float maxSep = DrawingConfig.InkTouchDistance * 2f;
            float maxSep2 = maxSep * maxSep;

            for (int s = 0; s < pa.Count - 1; s++)
            {
                int tStart = (i == j) ? s + 2 : 0; // self: skip adjacent segments
                for (int t = tStart; t < pb.Count - 1; t++)
                {
                    if (i == j && s == 0 && t == pb.Count - 2) continue; // the two endpoints: endpoint-closure, not a cross
                    if (GeometryUtil.SegmentsIntersect(pa[s], pa[s + 1], pb[t], pb[t + 1], out float ts, out float tt))
                    {
                        // the meeting point in the WORLD, for welding: the two
                        // lines pass within ink width of each other in 3D even
                        // when the 2D projection says they cross exactly
                        Vector3 pA = a[s] + (a[s + 1] - a[s]) * ts;
                        Vector3 pB = b[t] + (b[t + 1] - b[t]) * tt;

                        // TOUCHING IS TOUCHING IN THE WORLD, NOT IN A PROJECTION.
                        // The test above is done in ONE fitted plane. Ink wrapped
                        // around a barrel, a tree or a limb has a deep bounding
                        // box on every axis, so two strokes on OPPOSITE sides of
                        // the object project onto each other and "cross" through
                        // a hand's width of solid wood. A visible gap must never
                        // close — and a whole log is the most visible gap there
                        // is. Both points are already here, so this is free.
                        if ((pA - pB).sqrMagnitude > maxSep2) continue;

                        var x = TakeXing();
                        x.Id = 0;
                        x.SA = i; x.PosA = s + ts;
                        x.SB = j; x.PosB = t + tt;
                        x.P = (pA + pB) * 0.5f;
                        x.Crossing = true;
                        xings.Add(x);
                        if (xings.Count >= MaxCrossings) return;
                    }
                }
            }
        }

        /// A STROKE THAT STOPS ON ANOTHER STROKE IS TOUCHING IT. Emit a vertex
        /// wherever an ENDPOINT lands on ink — measured point-to-SEGMENT, so a
        /// line landing between two of the other line's samples counts exactly
        /// as much as one landing on a sample. This runs in 3D on purpose: no
        /// plane fit, no projection, nothing that can collapse (see Project).
        ///
        /// Stroke-count independence, which is the whole point: the same square
        /// drawn in one sweep (self-touch at the start) and in four strokes
        /// (four end-on-ink touches) now produces the same ring.
        static void CollectTouches(List<Vector3>[] pts, float[][] arc, int i, int j, List<Xing> xings)
        {
            float r = DrawingConfig.InkTouchDistance;
            EndOnInk(pts, arc, i, 0, j, r, xings);
            EndOnInk(pts, arc, i, pts[i].Count - 1, j, r, xings);
            if (i == j) return;                       // self: both ends already tested against itself
            EndOnInk(pts, arc, j, 0, i, r, xings);
            EndOnInk(pts, arc, j, pts[j].Count - 1, i, r, xings);
        }

        /// Endpoint `endIdx` of stroke `si` against the whole polyline of `sj`.
        static void EndOnInk(List<Vector3>[] pts, float[][] arc, int si, int endIdx, int sj,
                             float r, List<Xing> xings)
        {
            var a = pts[si];
            var b = pts[sj];
            if (endIdx < 0 || endIdx >= a.Count || b.Count < 2) return;

            Vector3 p = a[endIdx];
            float bestDist = r;
            float bestPos = -1f;
            Vector3 bestPt = default;
            float nearMiss = float.MaxValue; // closest REFUSED approach, for the log

            for (int t = 0; t + 1 < b.Count; t++)
            {
                Vector3 q0 = b[t];
                Vector3 d = b[t + 1] - q0;
                float l2 = d.sqrMagnitude;
                float u = l2 > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - q0, d) / l2) : 0f;
                Vector3 c = q0 + d * u;
                float dist = Vector3.Distance(p, c);

                // A LINE IS ALWAYS TOUCHING ITSELF right where it is. Only a
                // return far enough around to enclose something is a closure —
                // and "far enough" is the same MinLoopPerimeter every other
                // closure path uses, so a stroke can't seal a 2cm nub of itself.
                if (si == sj)
                {
                    float alongTouch = arc[sj][t] + u * (arc[sj][t + 1] - arc[sj][t]);
                    if (Mathf.Abs(alongTouch - arc[si][endIdx]) < DrawingConfig.MinLoopPerimeter) continue;
                }

                if (dist < bestDist) { bestDist = dist; bestPos = t + u; bestPt = c; }
                else if (dist > r && dist < nearMiss) nearMiss = dist;
            }

            if (bestPos < 0f)
            {
                // remember the refusal AND who it was between — SelectNearMiss
                // decides afterwards whether this gap was ever meant to be a
                // join, which it can only know once the whole graph is built
                if (nearMiss < float.MaxValue && _nearMisses.Count < MaxNearMisses)
                    _nearMisses.Add((nearMiss, si, sj));
                return;
            }

            var x = TakeXing();
            x.Id = 0;
            x.SA = si; x.PosA = endIdx;
            x.SB = sj; x.PosB = bestPos;
            x.P = (p + bestPt) * 0.5f;
            x.Crossing = false;   // an END landing ON ink: a touch, never a self-crossing
            xings.Add(x);
        }

        static void Project(List<Vector3> a, List<Vector3> b, bool self, Vector3 surfaceNormal,
                            out List<Vector2> pa, out List<Vector2> pb)
        {
            var all = _projAll; // pooled — this ran per candidate pair per scan
            all.Clear();
            all.AddRange(a);
            if (!self) all.AddRange(b);
            Vector3 origin = Vector3.zero;
            foreach (var p in all) origin += p;
            origin /= all.Count;

            // `all` is stroke A's points CONCATENATED with stroke B's — a garbage
            // "polygon" whose signed area happily cancels to zero for two
            // near-straight strokes. When it does, falling back to Vector3.up
            // flattens a WALL drawing to a line and every crossing test on that
            // pair becomes noise — silently, per pair, "sometimes". Fall back to
            // the ink's OWN surface normal instead, exactly as Seal.cs does.
            Vector3 normal = GeometryUtil.NewellNormal(all);
            if (normal.sqrMagnitude < 1e-8f) normal = surfaceNormal;
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.up;
            normal.Normalize();
            GeometryUtil.PlaneBasis(normal, out var u, out var v);

            GeometryUtil.ProjectToPlane(a, origin, u, v, _projA);
            pa = _projA;
            if (self) pb = _projA;
            else
            {
                GeometryUtil.ProjectToPlane(b, origin, u, v, _projB);
                pb = _projB;
            }
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
            // A RING THAT WAS FOUND AND THEN THROWN AWAY IS STILL A REFUSAL, and
            // his rule is that a refusal is never silent. Every rejection below
            // used to leave LastNearMiss null — the ink touched all the way
            // round, every junction was found, and the HUD said nothing at all.
            // Keep the reason belonging to the BIGGEST ring we had to drop: that
            // is the boundary he meant.
            string reject = null;
            float rejectPerim = -1f;
            void Refuse(float ringPerim, string reason)
            {
                if (ringPerim <= rejectPerim) return;
                rejectPerim = ringPerim;
                reject = reason;
            }

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
                if (AllFromOneGlyph(edges, cycle, glyphish))
                {
                    // stars stay drawings
                    Refuse(perim, "that ring is a cell inside one self-crossing drawing, and it's smaller than a seal. draw the boundary bigger, or as its own line");
                    continue;
                }
                if (!Encloses(edges, cycle, out string why))
                {
                    if (why != null) Refuse(perim, why);
                    continue;
                }
                bestPerim = perim;
                best = cycle;
            }

            if (best == null)
            {
                _cycleReject = reject;
                return default;
            }
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
            // pooled scratch — this ran per seed edge per 8 Hz scan and its
            // three fresh Dictionaries + frontier were steady garbage
            _spDist.Clear();
            _spPrev.Clear();
            _spVisited.Clear();
            _spFrontier.Clear();
            _spDist[start] = 0f;
            _spFrontier.Add(start);

            while (_spFrontier.Count > 0)
            {
                // pop nearest
                int bi = 0;
                for (int k = 1; k < _spFrontier.Count; k++)
                    if (_spDist[_spFrontier[k]] < _spDist[_spFrontier[bi]]) bi = k;
                int cur = _spFrontier[bi];
                _spFrontier.RemoveAt(bi);
                if (!_spVisited.Add(cur)) continue;
                if (cur == goal) break;

                if (!adj.TryGetValue(cur, out var incident)) continue;
                foreach (var e in incident)
                {
                    if (e == excludeEdge) continue;
                    var ed = edges[e];
                    int other = ed.U == cur ? ed.V : ed.U;
                    if (_spVisited.Contains(other)) continue;
                    float nd = _spDist[cur] + ed.Length;
                    if (!_spDist.TryGetValue(other, out var od) || nd < od)
                    {
                        _spDist[other] = nd;
                        _spPrev[other] = e;
                        _spFrontier.Add(other);
                    }
                }
            }

            if (!_spPrev.ContainsKey(goal)) return null;
            var path = new List<int>();
            int node = goal;
            int guard = 0;
            while (node != start && guard++ < edges.Count + 2)
            {
                int e = _spPrev[node];
                path.Add(e);
                var ed = edges[e];
                node = ed.U == node ? ed.V : ed.U;
            }
            return node == start ? path : null;
        }

        /// One arc of the ring, in the direction the ring travels through it.
        struct Step { public int Edge; public bool Reversed; }

        /// Walk the cycle's arcs end to end, so the ring comes out in RING ORDER.
        /// Null = the arcs don't chain into one clean cycle.
        ///
        /// This must be shared with the area guard, not just the result builder.
        /// An Edge's points are always stored Lo→Hi (node order), which is the
        /// order it was DRAWN in, not the direction the ring runs — so simply
        /// concatenating arcs gives a zig-zag whose signed area partly cancels.
        /// Newell's normal then collapses toward zero and Encloses refuses an
        /// honest seal, at random, depending on which way each stroke happened to
        /// be drawn. That is a "sometimes", and multi-stroke seals — Marko's bug
        /// (A) — are precisely the case with the most arcs to disagree.
        static List<Step> OrderCycle(List<Edge> edges, List<int> cycle)
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
            var steps = new List<Step>(cycle.Count);
            int current = edges[cycle[0]].U;
            for (int step = 0; step < cycle.Count; step++)
            {
                int pick = -1;
                if (localAdj.TryGetValue(current, out var incident))
                    foreach (var k in incident)
                        if (!used[k]) { pick = k; break; }
                if (pick < 0) return null; // not a clean single cycle

                used[pick] = true;
                var ed = edges[cycle[pick]];
                bool reversed = ed.U != current;
                steps.Add(new Step { Edge = cycle[pick], Reversed = reversed });
                current = reversed ? ed.U : ed.V;
            }
            return steps;
        }

        /// True = this ring is a seal. False = it isn't, and `why` says so in his
        /// words (null only for the internal "these arcs don't chain" case, which
        /// is not something a player can act on).
        static bool Encloses(List<Edge> edges, List<int> cycle, out string why)
        {
            why = null;
            var steps = OrderCycle(edges, cycle);
            // reject here, so a BETTER cycle still gets its turn
            if (steps == null) return false;

            var ring = new List<Vector3>();
            int nodeCount = 0;
            float perim = 0f;
            foreach (var st in steps)
            {
                var ed = edges[st.Edge];
                if (st.Reversed)
                    for (int i = ed.Pts.Count - 1; i >= 0; i--) ring.Add(ed.Pts[i]);
                else
                    ring.AddRange(ed.Pts);
                nodeCount += ed.Pts.Count;
                perim += ed.Length;
            }
            if (nodeCount < DrawingConfig.MinLoopNodes || perim < DrawingConfig.MinLoopPerimeter)
            {
                why = $"the ink closes, but that loop is only {perim * 100f:0}cm around. a seal needs {DrawingConfig.MinLoopPerimeter * 100f:0}cm";
                return false;
            }
            if (ring.Count < 3)
            {
                why = "the ink closes, but that loop is too short to enclose anything";
                return false;
            }

            // reject thin slivers: the ring must span in two directions
            Vector3 c = Vector3.zero;
            foreach (var p in ring) c += p;
            c /= ring.Count;
            Vector3 normal = GeometryUtil.NewellNormal(ring);
            if (normal.sqrMagnitude < 1e-8f)
            {
                why = "the ink closes, but that loop is flat and encloses no area";
                return false;
            }
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
            if ((maxU - minU) < DrawingConfig.MinLoopBulge || (maxV - minV) < DrawingConfig.MinLoopBulge)
            {
                why = $"the ink closes, but that loop is a sliver and has to bulge {DrawingConfig.MinLoopBulge * 100f:0}cm across to hold anything";
                return false;
            }
            return true;
        }

        static Result BuildResult(List<Edge> edges, List<int> cycle)
        {
            var steps = OrderCycle(edges, cycle);
            if (steps == null) return default; // not a clean single cycle

            var arcs = new List<Arc>(steps.Count);
            foreach (var st in steps)
            {
                var ed = edges[st.Edge];
                arcs.Add(new Arc { Stroke = ed.Stroke, Lo = ed.Lo, Hi = ed.Hi, Reversed = st.Reversed });
            }
            float perim = 0f;
            foreach (var ei in cycle) perim += edges[ei].Length;
            return new Result { Cycle = arcs, Valid = arcs.Count > 0, Perimeter = perim };
        }

        // ---- small helpers ----
        /// World positions AND the ink's averaged surface normal, into a pooled
        /// buffer. The normal used to be thrown away here, which is why the
        /// plane fit had nothing to fall back on but Vector3.up.
        static void WorldPoints(Stroke s, List<Vector3> into, out Vector3 surfaceNormal)
        {
            into.Clear();
            surfaceNormal = Vector3.zero;
            foreach (var n in s.Nodes)
                if (n != null)
                {
                    into.Add(n.transform.position);
                    surfaceNormal += n.SurfaceNormal;
                }
            if (surfaceNormal.sqrMagnitude > 1e-8f) surfaceNormal.Normalize();
        }

        /// Running length along a polyline, index-aligned with its points —
        /// reuses the caller's buffer when it is big enough.
        static float[] ArcLengths(List<Vector3> pts, float[] buf)
        {
            int need = Mathf.Max(pts.Count, 1);
            if (buf == null || buf.Length < need) buf = new float[Mathf.Max(need, 32)];
            buf[0] = 0f;
            for (int i = 1; i < pts.Count; i++)
                buf[i] = buf[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            return buf;
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
