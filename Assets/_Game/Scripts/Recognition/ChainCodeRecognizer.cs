using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Chain-code recognizer: a drawing becomes a sentence of 8 direction
    /// letters (repeated letters collapse; matching is fuzzy edit distance;
    /// every shape also reads backwards; frames come from the drawing, not
    /// the world). Used by the compound-sigil parse.
    public static class ChainCodeRecognizer
    {
        const float MinStepFraction = 0.08f; // sampling gate: % of shape extent
        const float SegmentGroupAngle = 30f; // steps this aligned belong to one segment

        // ------------------------------------------------------------ encode --
        /// All readings of a shape: {pen-path walks} × {frames} × {fwd, bwd}.
        /// Multi-stroke shapes are re-walked from every endpoint (greedy
        /// nearest-endpoint chaining), so stroke order and direction never matter.
        public static List<byte[]> EncodeAll(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var readings = new List<byte[]>();
            foreach (var pts in AllWalks(strokes))
            {
                if (pts.Count < 2) continue;
                // both frames are read off the shape itself, never the
                // world/plane frame
                float a1 = LongestSegmentAngle(pts);
                float a2 = EndToEndAngle(pts);
                float[] frames = { a1, a2 };
                foreach (float frame in frames)
                {
                    var s = Encode(pts, frame);
                    if (s.Length < 1) continue;
                    AddUnique(readings, s);
                    AddUnique(readings, ReverseInvert(s));
                }
            }
            return readings;
        }

        /// Point sequences to try: the drawn order, plus (for 2-4 strokes) a
        /// greedy walk starting from EACH stroke end, always hopping to the
        /// nearest remaining endpoint - drawing order independence.
        static List<List<Vector2>> AllWalks(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var walks = new List<List<Vector2>> { Flatten(strokes) };

            var clean = new List<List<Vector2>>();
            if (strokes != null)
                foreach (var s in strokes)
                    if (s != null && s.Count >= 2) clean.Add(new List<Vector2>(s));
            if (clean.Count < 2 || clean.Count > 4) return walks;

            for (int start = 0; start < clean.Count; start++)
                for (int dir = 0; dir < 2; dir++)
                {
                    var remaining = new List<List<Vector2>>(clean);
                    var first = remaining[start];
                    remaining.RemoveAt(start);

                    var walk = new List<Vector2>();
                    AppendStroke(walk, first, dir == 1);
                    while (remaining.Count > 0)
                    {
                        Vector2 tip = walk[walk.Count - 1];
                        int bestIdx = 0; bool bestRev = false; float bestD = float.MaxValue;
                        for (int i = 0; i < remaining.Count; i++)
                        {
                            float dHead = (remaining[i][0] - tip).sqrMagnitude;
                            float dTail = (remaining[i][remaining[i].Count - 1] - tip).sqrMagnitude;
                            if (dHead < bestD) { bestD = dHead; bestIdx = i; bestRev = false; }
                            if (dTail < bestD) { bestD = dTail; bestIdx = i; bestRev = true; }
                        }
                        AppendStroke(walk, remaining[bestIdx], bestRev);
                        remaining.RemoveAt(bestIdx);
                    }
                    walks.Add(walk);
                }
            return walks;
        }

        static void AppendStroke(List<Vector2> walk, List<Vector2> stroke, bool reversed)
        {
            if (reversed)
                for (int i = stroke.Count - 1; i >= 0; i--) walk.Add(stroke[i]);
            else
                walk.AddRange(stroke);
        }

        /// Score a span of a sentence against a template's readings - the
        /// building block of compound parsing.
        public static float ScoreSpan(byte[] sentence, int start, int len, List<byte[]> templates)
        {
            if (templates == null || len < 1 || start + len > sentence.Length) return 0f;
            var span = new byte[len];
            System.Array.Copy(sentence, start, span, 0, len);
            float best = 0f;
            foreach (var t in templates)
            {
                float s = SentenceScore(span, t);
                if (s > best) best = s;
            }
            return best;
        }

        // -------------------------------------------------------- internals --
        static List<Vector2> Flatten(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var pts = new List<Vector2>();
            if (strokes == null) return pts;
            foreach (var s in strokes)
            {
                if (s == null) continue;
                foreach (var p in s) pts.Add(p);
            }
            return pts;
        }

        static byte[] Encode(List<Vector2> pts, float frameAngle)
        {
            // sampling gate scales with shape extent
            Vector2 min = pts[0], max = pts[0];
            foreach (var p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);
            if (extent < 1e-5f) return System.Array.Empty<byte>();
            float minStep = extent * MinStepFraction;

            var tokens = new List<byte>();
            Vector2 last = pts[0];
            int lastBucket = -1;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 d = pts[i] - last;
                if (d.magnitude < minStep) continue; // too close - jitter/speed immunity
                last = pts[i];

                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - frameAngle;
                int bucket = Mathf.RoundToInt(ang / 45f);
                bucket = ((bucket % 8) + 8) % 8;
                if (bucket != lastBucket) // same letter repeating = same line
                {
                    tokens.Add((byte)bucket);
                    lastBucket = bucket;
                }
            }
            return tokens.ToArray();
        }

        /// Same shape drawn from the other end: reverse the letters and point
        /// each one the opposite way.
        static byte[] ReverseInvert(byte[] s)
        {
            var r = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
                r[i] = (byte)((s[s.Length - 1 - i] + 4) % 8);
            return r;
        }

        static float SentenceScore(byte[] a, byte[] b)
        {
            if (a.Length == 0 || b.Length == 0) return 0f;
            int dist = Levenshtein(a, b);
            return Mathf.Clamp01(1f - dist / (float)Mathf.Max(a.Length, b.Length));
        }

        static int Levenshtein(byte[] a, byte[] b)
        {
            int n = a.Length, m = b.Length;
            var prev = new int[m + 1];
            var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int sub = prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    cur[j] = Mathf.Min(Mathf.Min(prev[j] + 1, cur[j - 1] + 1), sub);
                }
                (prev, cur) = (cur, prev);
            }
            return prev[m];
        }

        /// The drawing's own "forward": direction of its longest straight run.
        static float LongestSegmentAngle(List<Vector2> pts)
        {
            float bestLen = 0f, bestAngle = 0f;
            Vector2 runStart = pts[0], prev = pts[0];
            Vector2 runDir = Vector2.zero;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 step = pts[i] - prev;
                if (step.magnitude < 1e-6f) continue;
                Vector2 dir = step.normalized;
                if (runDir == Vector2.zero) runDir = dir;
                else if (Vector2.Angle(runDir, dir) > SegmentGroupAngle)
                {
                    Consider(runStart, prev, ref bestLen, ref bestAngle);
                    runStart = prev;
                    runDir = dir;
                }
                prev = pts[i];
            }
            Consider(runStart, prev, ref bestLen, ref bestAngle);
            return bestAngle;
        }

        static void Consider(Vector2 from, Vector2 to, ref float bestLen, ref float bestAngle)
        {
            float len = (to - from).magnitude;
            if (len > bestLen)
            {
                bestLen = len;
                bestAngle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
            }
        }

        static float EndToEndAngle(List<Vector2> pts)
        {
            Vector2 d = pts[pts.Count - 1] - pts[0];
            return d.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        static void AddUnique(List<byte[]> list, byte[] s)
        {
            foreach (var e in list)
            {
                if (e.Length != s.Length) continue;
                bool same = true;
                for (int i = 0; i < s.Length; i++)
                    if (e[i] != s[i]) { same = false; break; }
                if (same) return;
            }
            list.Add(s);
        }
    }
}
