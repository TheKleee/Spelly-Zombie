using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Finds closed loops in the live stroke graph.
    ///
    /// Model: every stroke is an edge between its two endpoints. Two endpoints
    /// within CloseThreshold of each other are linked - regardless of which
    /// surface or object either stroke lives on. A seal is a cycle: either a
    /// single stroke whose own endpoints meet, or a chain of strokes linked
    /// end-to-end that returns to its start.
    ///
    /// THE LIMIT OF THIS MODEL, STATED OUT LOUD: two attachment points per
    /// stroke means a line that ends on another line's MIDDLE is invisible here.
    /// That is not a bug to fix in this file - a T-junction needs the touched
    /// stroke SPLIT, which is CrossingFinder's job (it emits a vertex wherever
    /// ink meets ink and hands the split arcs back). Both detectors always run
    /// and the largest loop wins, so between them "touching is touching"
    /// holds however the strokes met. Do not widen CloseThreshold to chase a
    /// T-junction: that is how "the seal activates when lines don't even touch"
    /// came back last time.
    ///
    /// Because endpoints are read from live node transforms, loops can form in
    /// two ways with zero extra code: the player draws back to the start, or
    /// two objects carrying half-drawn arcs are pushed close enough together.
    public static class SealDetector
    {
        public struct LoopEntry
        {
            public Stroke Stroke;
            public bool Forward; // true: traverse First->Last

            public LoopEntry(Stroke s, bool forward)
            {
                Stroke = s;
                Forward = forward;
            }
        }

        /// Returns the LARGEST valid loop among the eligible strokes, or null.
        /// the rule: seals take priority and the BIGGEST enclosing loop
        /// wins - a small sub-loop must never steal the intended boundary.
        /// The returned list is a FRESH copy (Seal keeps it for its lifetime);
        /// everything else here runs on pooled scratch - this scans at 8 Hz
        /// whenever open ink exists, and it was the steadiest garbage source
        /// in the quadrant.
        public static List<LoopEntry> FindLoop(IReadOnlyList<Stroke> eligible)
        {
            _best = false;
            _bestPerim = -1f;
            _nearestOpen = float.MaxValue;
            _nearestChain = 0;
            _visits = 0;
            LastScanClipped = false;
            // OWN THE RESET, don't rely on the caller. The guard below is
            // `LastNearMiss == null`, so a message left over from a previous scan
            // silently suppresses this scan's real reason. It happens to work
            // today only because all three call sites null it first; the next one
            // added would break it quietly. CrossingFinder.Find already does this.
            LastNearMiss = null;

            // endpoints AND path lengths, read once per scan (ink cannot move
            // mid-scan - the same axiom the endpoint cache already stands on).
            // Every candidate loop used to WALK EVERY NODE of every stroke it
            // contained, per Consider, per LoopBigEnough: the single hottest
            // burn of a body covered in linked ink.
            CacheEnds(eligible);

            // 1) single-stroke self closure. The threshold scales with the stroke's
            //    own size so a small rune's deliberate gap never counts as closed.
            for (int i = 0; i < eligible.Count; i++)
            {
                var s = eligible[i];
                if (s.State == StrokeState.Drawing) continue;
                if (s.Nodes.Count < DrawingConfig.MinLoopNodes) continue;
                float perimeter = _len[i];
                if (perimeter < DrawingConfig.MinLoopPerimeter) continue;
                float gap = Vector3.Distance(_endA[i], _endB[i]);
                float thr = DrawingConfig.SelfCloseThreshold(perimeter);
                if (gap <= thr)
                {
                    _single.Clear();
                    _single.Add(new LoopEntry(s, true));
                    Consider(_single, perimeter);
                }
                else if (gap <= thr * 3f) // this path failed SILENTLY for months - never again
                    LastNearMiss = $"loop ends {gap * 100f:0.0}cm apart ({thr * 100f:0.0}cm allowed)";
            }

            // 2) multi-stroke chains - explore ALL, keep the biggest
            foreach (var s0 in eligible)
            {
                _used.Clear();
                _used.Add(s0);
                _path.Clear();
                _path.Add(new LoopEntry(s0, true));
                Dfs(eligible, _path, _used,
                    s0.First.transform.position,
                    s0.Last.transform.position);
            }

            // A CLIPPED SCAN IS A REFUSAL TOO (no silent caps): when the ink
            // graph is so tangled the work budget ran out before a loop was
            // found, say that instead of pretending nothing touched.
            if (!_best && LastScanClipped && LastNearMiss == null)
                LastNearMiss = "the ink tangle is too dense to scan fully — erase or simplify some of it";

            // NEVER SILENTLY REFUSE. Until now the ONLY near-miss this
            // file could report was for a loop that had already closed and then
            // failed a size guard - a chain that never linked at all said
            // nothing, which is the exact case hits and the exact reason it
            // felt random. Say which gap was the nearest to being a join.
            if (!_best && LastNearMiss == null && _nearestOpen < float.MaxValue)
                LastNearMiss = (_nearestChain > 0
                        ? $"a {_nearestChain}-stroke chain didn't come back around"
                        : "two line ends nearly meet")
                    + $", {_nearestOpen * 100f:0.0}cm apart and they must touch ({DrawingConfig.CloseThreshold * 100f:0.0}cm)";
            // the winner is copied out ONCE, at the end - the search itself
            // snapshots into a reused buffer instead of allocating per improvement
            return _best ? new List<LoopEntry>(_bestBuf) : null;
        }

        static bool _best;            // a valid loop was found this scan (in _bestBuf)
        static readonly List<LoopEntry> _bestBuf = new List<LoopEntry>(); // best loop so far (pooled)
        static readonly List<LoopEntry> _path = new List<LoopEntry>();   // DFS working path (pooled)
        static readonly List<LoopEntry> _single = new List<LoopEntry>(); // single-stroke candidate (pooled)
        static readonly HashSet<Stroke> _used = new HashSet<Stroke>();   // DFS visited set (pooled)
        static float _bestPerim;
        static float _nearestOpen;    // smallest gap that ALMOST linked this scan
        static int _nearestChain;     // 0 = two loose ends; >0 = a chain that didn't come back around

        /// Remember the closest thing to a join that wasn't one. Only gaps within
        /// a few thresholds count - ink on the far side of the room is not a near
        /// miss, it's a different drawing. Takes an int, not a message: this runs
        /// on every DFS node, and formatting a string there would allocate
        /// thousands of throwaways a second for text almost never shown.
        static void NoteOpenGap(float gap, float allowed, int chainLen)
        {
            if (gap <= allowed || gap > allowed * 3f) return;
            if (gap >= _nearestOpen) return;
            _nearestOpen = gap;
            _nearestChain = chainLen;
        }

        /// Squared-distance door to NoteOpenGap. The near-miss window is only
        /// [allowed, allowed x 3], and the DFS calls this for every stroke it
        /// rejects, so the sqrt is worth paying only inside that window. The
        /// gate below is the same test NoteOpenGap opens with, squared.
        static void NoteOpenGapSq(float gapSq, float allowed, int chainLen)
        {
            float hi = allowed * 3f;
            if (gapSq <= allowed * allowed || gapSq > hi * hi) return;
            NoteOpenGap(Mathf.Sqrt(gapSq), allowed, chainLen);
        }

        // Endpoint positions for the eligible set, index-aligned with the list
        // handed to Dfs. Filled once per FindLoop; grown, never reallocated per
        // call. Ink cannot move between the fill and the end of the scan.
        static Vector3[] _endA = new Vector3[64];
        static Vector3[] _endB = new Vector3[64];
        // per-scan path lengths: by index for the phase-1 sweep, by stroke for
        // LoopPerimeter/LoopBigEnough (a candidate loop only knows its strokes)
        static float[] _len = new float[64];
        static readonly Dictionary<Stroke, float> _lenOf = new Dictionary<Stroke, float>();

        // DFS work budget: a pose that sweeps many body-stroke ends through
        // link range at once makes the chain graph explode combinatorially
        // (every seed x every direction x depth 12), and one scan ate whole
        // frames. Honest loops close within a few hundred visits; the budget
        // only bites pathological tangles, and the refusal is spoken.
        const int VisitBudget = 40000;
        static int _visits;

        /// True when the last FindLoop ran out of budget before finishing.
        public static bool LastScanClipped;

        static void CacheEnds(IReadOnlyList<Stroke> all)
        {
            if (_endA.Length < all.Count)
            {
                int cap = Mathf.NextPowerOfTwo(all.Count);
                _endA = new Vector3[cap];
                _endB = new Vector3[cap];
                _len = new float[cap];
            }
            _lenOf.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                _endA[i] = all[i].First.transform.position;
                _endB[i] = all[i].Last.transform.position;
                _len[i] = all[i].PathLength();
                _lenOf[all[i]] = _len[i];
            }
        }

        static void Consider(List<LoopEntry> path, float perimeter)
        {
            if (perimeter <= _bestPerim) return;
            _bestPerim = perimeter;
            _bestBuf.Clear();           // snapshot into the pooled buffer -
            _bestBuf.AddRange(path);    // the DFS reuses its list
            _best = true;
        }

        /// PathLength returns approximate perimeter of a candidate loop.
        /// Reads the per-scan cache (filled by the FindLoop that produced the
        /// loop, same frame - ink cannot move in between); a stroke the cache
        /// never saw falls back to the honest walk.
        public static float LoopPerimeter(List<LoopEntry> path)
        {
            float p = 0f;
            foreach (var e in path)
                p += _lenOf.TryGetValue(e.Stroke, out float l) ? l : e.Stroke.PathLength();
            return p;
        }

        /// Set when a loop ALMOST closed (everything passed except one guard) -
        /// surfaced on the HUD so "why didn't it fire?!" answers itself.
        public static string LastNearMiss;

        /// EVERY JUNCTION IS ALREADY CAPPED AT CloseThreshold, one at a time, by
        /// the link tests below - and that absolute, ink-sized cap IS the touch
        /// law. There used to be a SECOND test here: total air ≤ perimeter ×
        /// MaxLoopGapFraction. It is deleted, because being relative it let SIZE
        /// and PEN-LIFT COUNT decide whether a drawing is a seal, and 's
        /// standing rules forbid both ("a seal drawn in 5 strokes must behave
        /// exactly like the same seal drawn in one sweep"; "size must never
        /// matter"). The arithmetic that did it: perimeter × 0.15 shared across K
        /// junctions, so a 30cm square got 1.1cm per corner drawn in 4 strokes
        /// and 0.56cm drawn in 8 - narrower than the ink is wide. Honest touches
        /// were refused for the crime of being drawn carefully, and the same seal
        /// drawn twice as large sailed through. The running `gapSum` that fed it
        /// went with it: nothing reads a TOTAL any more, only per-junction truth.
        ///
        /// Explores every chain and records the LARGEST valid loop (does not
        /// short-circuit on the first - a small loop must not win over the big
        /// intended boundary).
        static void Dfs(IReadOnlyList<Stroke> all, List<LoopEntry> path, HashSet<Stroke> used,
                        Vector3 startPos, Vector3 exitPos)
        {
            if (++_visits > VisitBudget) { LastScanClipped = true; return; }
            float closeGap = Vector3.Distance(exitPos, startPos);
            // ONE TOUCH LAW EVERYWHERE - body included (body
            // drawing kept "calling a seal that was never connected" — the
            // 5cm 'breathing' tolerance chained six untouching body strokes
            // into a phantom 0.01m² seal). Fresh loops close by touching
            // exactly; the forgiving ReCloseDistance belongs ONLY to re-arming
            // already-spent seals, which have earned it.
            float closeThr = DrawingConfig.CloseThreshold;
            if (path.Count >= 2)
            {
                if (closeGap <= closeThr)
                {
                    if (!LoopBigEnough(path))
                        LastNearMiss = $"loop found but too small ({path.Count} strokes)";
                    else
                        Consider(path, LoopPerimeter(path)); // keep exploring for a bigger one
                }
                else NoteOpenGap(closeGap, closeThr, path.Count);
            }

            if (path.Count >= DrawingConfig.MaxLoopStrokes) return;

            float link = DrawingConfig.CloseThreshold; // touching exactly - body and world alike
            float link2 = link * link;

            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (used.Contains(t)) continue;

                // ENDPOINTS COME FROM THE CACHE, NOT THE TRANSFORM. This loop
                // used to do t.First.transform.position and t.Last.transform.
                // position on every candidate on every DFS visit: four native
                // calls per stroke, and the search seeds from every stroke, so
                // it is quadratic. Ink cannot move mid-scan, so FindLoop reads
                // each endpoint exactly once into _endA/_endB.
                Vector3 a = _endA[i];
                Vector3 b = _endB[i];

                // Squared distance: the sqrt is only paid inside the near-miss
                // window, by NoteOpenGapSq. Same accept/reject decisions.
                float dA2 = (exitPos - a).sqrMagnitude;
                if (dA2 <= link2)
                {
                    path.Add(new LoopEntry(t, true));
                    used.Add(t);
                    Dfs(all, path, used, startPos, b);
                    path.RemoveAt(path.Count - 1);
                    used.Remove(t);
                }
                else NoteOpenGapSq(dA2, link, 0);

                float dB2 = (exitPos - b).sqrMagnitude;
                if (dB2 > link2) NoteOpenGapSq(dB2, link, 0);
                if (dB2 <= link2)
                {
                    path.Add(new LoopEntry(t, false));
                    used.Add(t);
                    Dfs(all, path, used, startPos, a);
                    path.RemoveAt(path.Count - 1);
                    used.Remove(t);
                }
            }
        }

        static bool LoopBigEnough(List<LoopEntry> path)
        {
            int nodes = 0;
            float length = 0f;
            foreach (var e in path)
            {
                nodes += e.Stroke.Nodes.Count;
                length += _lenOf.TryGetValue(e.Stroke, out float l) ? l : e.Stroke.PathLength();
            }
            return nodes >= DrawingConfig.MinLoopNodes && length >= DrawingConfig.MinLoopPerimeter;
        }

        /// Concatenate the loop's strokes into one ordered ring of nodes.
        public static List<DrawNode> BuildLoopNodes(List<LoopEntry> loop)
        {
            var nodes = new List<DrawNode>();
            BuildLoopNodes(loop, nodes);
            return nodes;
        }

        /// Pooled variant for the 8 Hz spent-group tick (DrawingWorld) -
        /// fills a caller-owned buffer instead of allocating per tick.
        public static void BuildLoopNodes(List<LoopEntry> loop, List<DrawNode> into)
        {
            into.Clear();
            foreach (var e in loop)
            {
                if (e.Forward)
                {
                    foreach (var n in e.Stroke.Nodes)
                        if (n != null) into.Add(n);
                }
                else
                {
                    for (int i = e.Stroke.Nodes.Count - 1; i >= 0; i--)
                        if (e.Stroke.Nodes[i] != null) into.Add(e.Stroke.Nodes[i]);
                }
            }
        }
    }
}
