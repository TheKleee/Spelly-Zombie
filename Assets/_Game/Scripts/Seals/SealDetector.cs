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

        /// Returns the first valid loop found among the eligible strokes, or null.
        public static List<LoopEntry> FindLoop(IReadOnlyList<Stroke> eligible)
        {
            // 1) single-stroke self closure. The threshold scales with the stroke's
            //    own size so a small rune's deliberate gap never counts as closed.
            foreach (var s in eligible)
            {
                if (s.State == StrokeState.Drawing) continue;
                if (s.Nodes.Count < DrawingConfig.MinLoopNodes) continue;
                float perimeter = s.PathLength();
                if (perimeter < DrawingConfig.MinLoopPerimeter) continue;
                if (Vector3.Distance(s.First.transform.position, s.Last.transform.position)
                    <= DrawingConfig.SelfCloseThreshold(perimeter))
                    return new List<LoopEntry> { new LoopEntry(s, true) };
            }

            // 2) multi-stroke chains
            var used = new HashSet<Stroke>();
            foreach (var s0 in eligible)
            {
                used.Clear();
                used.Add(s0);
                var path = new List<LoopEntry> { new LoopEntry(s0, true) };
                if (Dfs(eligible, path, used,
                        s0.First.transform.position,
                        s0.Last.transform.position))
                    return path;
            }
            return null;
        }

        static bool Dfs(IReadOnlyList<Stroke> all, List<LoopEntry> path, HashSet<Stroke> used,
                        Vector3 startPos, Vector3 exitPos)
        {
            if (path.Count >= 2 &&
                Vector3.Distance(exitPos, startPos) <= DrawingConfig.CloseThreshold &&
                LoopBigEnough(path))
                return true;

            if (path.Count >= DrawingConfig.MaxLoopStrokes) return false;

            foreach (var t in all)
            {
                if (used.Contains(t)) continue;

                Vector3 a = t.First.transform.position;
                Vector3 b = t.Last.transform.position;

                if (Vector3.Distance(exitPos, a) <= DrawingConfig.CloseThreshold)
                {
                    path.Add(new LoopEntry(t, true));
                    used.Add(t);
                    if (Dfs(all, path, used, startPos, b)) return true;
                    path.RemoveAt(path.Count - 1);
                    used.Remove(t);
                }

                if (Vector3.Distance(exitPos, b) <= DrawingConfig.CloseThreshold)
                {
                    path.Add(new LoopEntry(t, false));
                    used.Add(t);
                    if (Dfs(all, path, used, startPos, a)) return true;
                    path.RemoveAt(path.Count - 1);
                    used.Remove(t);
                }
            }
            return false;
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
