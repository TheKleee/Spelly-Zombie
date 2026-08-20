using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Finds closed loops in the live stroke graph: a stroke is an edge between
    /// its two endpoints, endpoints within CloseThreshold link, a seal is a cycle.
    /// T-junctions are CrossingFinder's job - do not widen CloseThreshold for them.
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

        /// Returns the largest valid loop among the eligible strokes, or null -
        /// a small sub-loop must never steal the intended boundary. The returned
        /// list is a fresh copy; everything else runs on pooled scratch.
        public static List<LoopEntry> FindLoop(IReadOnlyList<Stroke> eligible)
        {
            _best = false;
            _bestPerim = -1f;
            _nearestOpen = float.MaxValue;
            _nearestChain = 0;
            _visits = 0;
            LastScanClipped = false;
            // reset here, not in callers - a stale message would suppress this scan's reason
            LastNearMiss = null;

            // endpoints and path lengths read once per scan - ink cannot move mid-scan
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
                else if (gap <= thr * 3f)
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

            // a clipped scan is reported too - the budget refusal is never silent
            if (!_best && LastScanClipped && LastNearMiss == null)
                LastNearMiss = "the ink tangle is too dense to scan fully — erase or simplify some of it";

            // name the gap nearest to being a join when nothing sealed
            if (!_best && LastNearMiss == null && _nearestOpen < float.MaxValue)
                LastNearMiss = (_nearestChain > 0
                        ? $"a {_nearestChain}-stroke chain didn't come back around"
                        : "two line ends nearly meet")
                    + $", {_nearestOpen * 100f:0.0}cm apart and they must touch ({DrawingConfig.CloseThreshold * 100f:0.0}cm)";
            // the winner is copied out once; the search snapshots into a reused buffer
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

        /// Remember the closest thing to a join that wasn't one; only gaps within
        /// a few thresholds count. Takes an int, not a message - no string
        /// formatting on the DFS hot path.
        static void NoteOpenGap(float gap, float allowed, int chainLen)
        {
            if (gap <= allowed || gap > allowed * 3f) return;
            if (gap >= _nearestOpen) return;
            _nearestOpen = gap;
            _nearestChain = chainLen;
        }

        /// Squared-distance gate to NoteOpenGap - the sqrt is paid only inside the
        /// [allowed, allowed x 3] near-miss window.
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

        // DFS work budget: honest loops close within a few hundred visits; the
        // budget only bites pathological tangles, and the refusal is reported
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

        /// Approximate perimeter of a candidate loop, read from the per-scan cache;
        /// an uncached stroke falls back to the full walk.
        public static float LoopPerimeter(List<LoopEntry> path)
        {
            float p = 0f;
            foreach (var e in path)
                p += _lenOf.TryGetValue(e.Stroke, out float l) ? l : e.Stroke.PathLength();
            return p;
        }

        /// Set when a loop almost closed - surfaced so the refusal is never silent.
        public static string LastNearMiss;

        /// Explores every chain and records the largest valid loop (no
        /// short-circuit - a small loop must not win over the intended boundary).
        /// Junction gaps are capped per-junction at CloseThreshold only.
        static void Dfs(IReadOnlyList<Stroke> all, List<LoopEntry> path, HashSet<Stroke> used,
                        Vector3 startPos, Vector3 exitPos)
        {
            if (++_visits > VisitBudget) { LastScanClipped = true; return; }
            float closeGap = Vector3.Distance(exitPos, startPos);
            // fresh loops close at CloseThreshold everywhere, body included; the
            // forgiving ReCloseDistance applies only to re-arming spent seals
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

                // endpoints come from the per-scan cache - ink cannot move mid-scan
                Vector3 a = _endA[i];
                Vector3 b = _endB[i];

                // squared distance; the sqrt is paid only inside the near-miss window
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
