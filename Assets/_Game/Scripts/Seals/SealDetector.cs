using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Finds closed loops in the live stroke graph.
    ///
    /// Model: every stroke is an edge between its two endpoints. Two endpoints
    /// within CloseThreshold of each other are linked — regardless of which
    /// surface or object either stroke lives on. A seal is a cycle: either a
    /// single stroke whose own endpoints meet, or a chain of strokes linked
    /// end-to-end that returns to its start.
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
        /// Marko's rule: seals take priority and the BIGGEST enclosing loop
        /// wins — a small sub-loop must never steal the intended boundary.
        public static List<LoopEntry> FindLoop(IReadOnlyList<Stroke> eligible)
        {
            _best = null;
            _bestPerim = -1f;

            // 1) single-stroke self closure. The threshold scales with the stroke's
            //    own size so a small rune's deliberate gap never counts as closed.
            foreach (var s in eligible)
            {
                if (s.State == StrokeState.Drawing) continue;
                if (s.Nodes.Count < DrawingConfig.MinLoopNodes) continue;
                float perimeter = s.PathLength();
                if (perimeter < DrawingConfig.MinLoopPerimeter) continue;
                float gap = Vector3.Distance(s.First.transform.position, s.Last.transform.position);
                float thr = DrawingConfig.SelfCloseThreshold(perimeter);
                if (gap <= thr)
                    Consider(new List<LoopEntry> { new LoopEntry(s, true) }, perimeter);
                else if (gap <= thr * 3f) // this path failed SILENTLY for months — never again
                    LastNearMiss = $"loop ends {gap * 100f:0.0}cm apart — {thr * 100f:0.0}cm allowed";
            }

            // 2) multi-stroke chains — explore ALL, keep the biggest
            var used = new HashSet<Stroke>();
            foreach (var s0 in eligible)
            {
                used.Clear();
                used.Add(s0);
                var path = new List<LoopEntry> { new LoopEntry(s0, true) };
                Dfs(eligible, path, used,
                    s0.First.transform.position,
                    s0.Last.transform.position, 0f);
            }
            return _best;
        }

        static List<LoopEntry> _best;
        static float _bestPerim;

        static void Consider(List<LoopEntry> path, float perimeter)
        {
            if (perimeter <= _bestPerim) return;
            _bestPerim = perimeter;
            _best = new List<LoopEntry>(path); // snapshot — the DFS reuses its list
        }

        /// PathLength returns approximate perimeter of a candidate loop.
        public static float LoopPerimeter(List<LoopEntry> path)
        {
            float p = 0f;
            foreach (var e in path) p += e.Stroke.PathLength();
            return p;
        }

        /// Set when a loop ALMOST closed (everything passed except one guard) —
        /// surfaced on the HUD so "why didn't it fire?!" answers itself.
        public static string LastNearMiss;

        /// gapSum tracks the total "air" in the chain. A loop is only real when
        /// the gaps are a small share of its perimeter — lines drawn NEAR each
        /// other are not TOUCHING each other. This is what stopped seals from
        /// closing "without touching".
        /// Explores every chain and records the LARGEST valid loop (does not
        /// short-circuit on the first — a small loop must not win over the big
        /// intended boundary).
        static void Dfs(IReadOnlyList<Stroke> all, List<LoopEntry> path, HashSet<Stroke> used,
                        Vector3 startPos, Vector3 exitPos, float gapSum)
        {
            float closeGap = Vector3.Distance(exitPos, startPos);
            if (path.Count >= 2 && closeGap <= DrawingConfig.CloseThreshold)
            {
                if (!LoopBigEnough(path))
                {
                    LastNearMiss = $"loop found but too small ({path.Count} strokes)";
                }
                else
                {
                    float perimeter = LoopPerimeter(path);
                    if (gapSum + closeGap <= perimeter * DrawingConfig.MaxLoopGapFraction)
                        Consider(path, perimeter); // keep exploring for a bigger one
                    else
                        LastNearMiss = $"loop found but gaps too wide ({(gapSum + closeGap) * 100f:0.0}cm air vs {perimeter * DrawingConfig.MaxLoopGapFraction * 100f:0.0}cm allowed)";
                }
            }

            if (path.Count >= DrawingConfig.MaxLoopStrokes) return;

            foreach (var t in all)
            {
                if (used.Contains(t)) continue;

                Vector3 a = t.First.transform.position;
                Vector3 b = t.Last.transform.position;

                float dA = Vector3.Distance(exitPos, a);
                if (dA <= DrawingConfig.CloseThreshold)
                {
                    path.Add(new LoopEntry(t, true));
                    used.Add(t);
                    Dfs(all, path, used, startPos, b, gapSum + dA);
                    path.RemoveAt(path.Count - 1);
                    used.Remove(t);
                }

                float dB = Vector3.Distance(exitPos, b);
                if (dB <= DrawingConfig.CloseThreshold)
                {
                    path.Add(new LoopEntry(t, false));
                    used.Add(t);
                    Dfs(all, path, used, startPos, a, gapSum + dB);
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
                length += e.Stroke.PathLength();
            }
            return nodes >= DrawingConfig.MinLoopNodes && length >= DrawingConfig.MinLoopPerimeter;
        }

        /// Concatenate the loop's strokes into one ordered ring of nodes.
        public static List<DrawNode> BuildLoopNodes(List<LoopEntry> loop)
        {
            var nodes = new List<DrawNode>();
            foreach (var e in loop)
            {
                if (e.Forward)
                {
                    foreach (var n in e.Stroke.Nodes)
                        if (n != null) nodes.Add(n);
                }
                else
                {
                    for (int i = e.Stroke.Nodes.Count - 1; i >= 0; i--)
                        if (e.Stroke.Nodes[i] != null) nodes.Add(e.Stroke.Nodes[i]);
                }
            }
            return nodes;
        }
    }
}
