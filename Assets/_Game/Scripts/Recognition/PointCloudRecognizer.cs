using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// $P Point-Cloud Recognizer (Vatavu, Anthony, Wobbrock 2012), tuned for runes:
    ///  - multi-stroke by nature: an arrow drawn as shaft + barbs, in any order or
    ///    direction, matches the same template — no stroke rules for players to learn
    ///  - UNIFORM scaling (preserves aspect: tall HeatUp stays distinct from squat HeatDown)
    ///  - ROTATION-INVARIANT (CloudDistance searches orientations): a rune reads the
    ///    same drawn sideways or upside down. Reflection is NOT searched, so mirror
    ///    pairs (e.g. Sticky up/down) stay distinct; but pure 180° pairs (the square
    ///    brackets) become confusable and may need more distinct glyphs.
    public static class PointCloudRecognizer
    {
        public const int NumPoints = 32;

        /// Resample all strokes into one normalized 32-point cloud.
        /// Returns null for degenerate input.
        public static Vector2[] Normalize(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var clean = new List<List<Vector2>>();
            float totalLength = 0f;
            if (strokes != null)
            {
                foreach (var s in strokes)
                {
                    if (s == null || s.Count < 2) continue;
                    var copy = new List<Vector2>(s);
                    clean.Add(copy);
                    for (int i = 1; i < copy.Count; i++)
                        totalLength += Vector2.Distance(copy[i - 1], copy[i]);
                }
            }
            if (clean.Count == 0 || totalLength < 1e-5f) return null;

            var pts = Resample(clean, NumPoints, totalLength);
            ScaleUniform(pts);
            TranslateToCentroid(pts);
            return pts;
        }

        static Vector2[] Resample(List<List<Vector2>> strokes, int n, float totalLength)
        {
            float interval = totalLength / (n - 1);
            float accumulated = 0f;
            var dst = new List<Vector2>(n) { strokes[0][0] };

            foreach (var stroke in strokes)
            {
                for (int i = 1; i < stroke.Count; i++)
                {
                    float d = Vector2.Distance(stroke[i - 1], stroke[i]);
                    if (accumulated + d >= interval && d > 1e-6f)
                    {
                        var q = Vector2.Lerp(stroke[i - 1], stroke[i], (interval - accumulated) / d);
                        dst.Add(q);
                        stroke.Insert(i, q);
                        accumulated = 0f;
                    }
                    else
                    {
                        accumulated += d;
                    }
                }
                // stroke break: distance never accumulates across the pen-up gap
            }
            while (dst.Count < n) dst.Add(dst[dst.Count - 1]);
            if (dst.Count > n) dst.RemoveRange(n, dst.Count - n);
            return dst.ToArray();
        }

        static void ScaleUniform(Vector2[] pts)
        {
            Vector2 min = pts[0], max = pts[0];
            foreach (var p in pts)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);
            if (extent < 1e-6f) return;
            for (int i = 0; i < pts.Length; i++)
                pts[i] = (pts[i] - min) / extent;
        }

        static void TranslateToCentroid(Vector2[] pts)
        {
            Vector2 c = Vector2.zero;
            foreach (var p in pts) c += p;
            c /= pts.Length;
            for (int i = 0; i < pts.Length; i++) pts[i] -= c;
        }

        const int CoarseSteps = 7;        // samples across the limited window
        const float MaxRotation = 0.7f;   // ±40°: tolerate hand tilt, NOT flipped runes
        const float Phi = 0.6180339887f;

        [System.ThreadStatic] static Vector2[] _rotBuf;

        /// Cloud distance with LIMITED rotation tolerance (±40°). Full rotation
        /// invariance made 180°-pair glyphs (the State/Density brackets, the
        /// Heat zigzags) score as near-identical — the recognizer kept
        /// suggesting the flipped twin. A tilted hand still matches; an
        /// upside-down rune is a DIFFERENT rune.
        public static float CloudDistance(Vector2[] a, Vector2[] b)
        {
            int n = a.Length;
            if (_rotBuf == null || _rotBuf.Length != n) _rotBuf = new Vector2[n];

            // coarse sweep across the tolerance window
            float bestAng = 0f, bestDist = float.MaxValue;
            for (int r = 0; r < CoarseSteps; r++)
            {
                float ang = Mathf.Lerp(-MaxRotation, MaxRotation, r / (float)(CoarseSteps - 1));
                float d = DistAtAngle(a, b, ang);
                if (d < bestDist) { bestDist = d; bestAng = ang; }
            }

            // continuous refinement within ± one coarse step around the best sample
            float half = MaxRotation / (CoarseSteps - 1);
            float lo = bestAng - half, hi = bestAng + half;
            float x1 = hi - Phi * (hi - lo), x2 = lo + Phi * (hi - lo);
            float f1 = DistAtAngle(a, b, x1), f2 = DistAtAngle(a, b, x2);
            for (int iter = 0; iter < 8; iter++)
            {
                if (f1 < f2) { hi = x2; x2 = x1; f2 = f1; x1 = hi - Phi * (hi - lo); f1 = DistAtAngle(a, b, x1); }
                else { lo = x1; x1 = x2; f1 = f2; x2 = lo + Phi * (hi - lo); f2 = DistAtAngle(a, b, x2); }
            }
            return Mathf.Min(bestDist, Mathf.Min(f1, f2));
        }

        static float DistAtAngle(Vector2[] a, Vector2[] b, float ang)
        {
            int n = a.Length;
            float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
            for (int i = 0; i < n; i++)
            {
                float x = a[i].x, y = a[i].y;
                _rotBuf[i] = new Vector2(x * cos - y * sin, x * sin + y * cos);
            }
            return Mathf.Min(GreedyMatch(_rotBuf, b, 0), GreedyMatch(_rotBuf, b, n / 2));
        }

        static float GreedyMatch(Vector2[] a, Vector2[] b, int start)
        {
            int n = a.Length;
            var matched = new bool[n];
            float sum = 0f;
            int i = start;
            do
            {
                float min = float.MaxValue;
                int index = -1;
                for (int j = 0; j < n; j++)
                {
                    if (matched[j]) continue;
                    float d = Vector2.Distance(a[i], b[j]);
                    if (d < min) { min = d; index = j; }
                }
                matched[index] = true;
                float weight = 1f - ((i - start + n) % n) / (float)n;
                sum += weight * min;
                i = (i + 1) % n;
            } while (i != start);
            return sum;
        }

        /// 0..1 confidence from a cloud distance. Rescaled to match intuition:
        /// the paper's /2 mapping put a GOOD hand-drawn match at ~45%, which
        /// made the 40% gate reject honest drawings. Now a good match reads
        /// ~60-85%, a wrong rune ~20-35% — the 40% threshold means what
        /// players think it means.
        public static float Score(float distance) => Mathf.Clamp01((2f - distance) / 1.5f);
    }
}
