using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// $1 Unistroke Recognizer (Wobbrock, Wilson, Li 2007), tuned for runes:
    ///  - UNIFORM scaling instead of square-fit, so tall vs. flat glyphs stay distinct
    ///    (HeatUp is a tall zigzag, HeatDown a squat one).
    ///  - Orientation-sensitive: no indicative-angle normalization, only a small
    ///    +/-15 degree search — an up arrow and a down Y must NOT match each other.
    public static class DollarRecognizer
    {
        public const int NumPoints = 64;
        const float Size = 250f;
        const float AngleRange = 15f;     // degrees searched either side
        const float AnglePrecision = 2f;  // golden section search tolerance
        static readonly float HalfDiagonal = 0.5f * Mathf.Sqrt(2f) * Size;
        static readonly float Phi = 0.5f * (Mathf.Sqrt(5f) - 1f);

        /// Resample + uniform scale + center. Returns null when the stroke is degenerate.
        public static Vector2[] Normalize(IReadOnlyList<Vector2> raw)
        {
            if (raw == null || raw.Count < 2) return null;
            if (PathLength(raw) < 1e-4f) return null;
            var pts = Resample(raw, NumPoints);
            ScaleUniform(pts);
            TranslateToOrigin(pts);
            return pts;
        }

        /// Best (templateIndex, score in 0..1) over the given normalized templates.
        public static (int index, float score) Recognize(Vector2[] candidate, IReadOnlyList<Vector2[]> templates)
        {
            int bestIdx = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < templates.Count; i++)
            {
                float d = DistanceAtBestAngle(candidate, templates[i], -AngleRange, AngleRange, AnglePrecision);
                if (d < bestDist) { bestDist = d; bestIdx = i; }
            }
            float score = bestIdx < 0 ? 0f : 1f - bestDist / HalfDiagonal;
            return (bestIdx, Mathf.Clamp01(score));
        }

        static float PathLength(IReadOnlyList<Vector2> pts)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
            return len;
        }

        static Vector2[] Resample(IReadOnlyList<Vector2> raw, int n)
        {
            var src = new List<Vector2>(raw);
            float interval = PathLength(src) / (n - 1);
            float accumulated = 0f;
            var dst = new List<Vector2>(n) { src[0] };

            for (int i = 1; i < src.Count; i++)
            {
                float d = Vector2.Distance(src[i - 1], src[i]);
                if (accumulated + d >= interval && d > 1e-6f)
                {
                    float t = (interval - accumulated) / d;
                    Vector2 q = Vector2.Lerp(src[i - 1], src[i], t);
                    dst.Add(q);
                    src.Insert(i, q); // continue walking from the inserted point
                    accumulated = 0f;
                }
                else
                {
                    accumulated += d;
                }
            }
            while (dst.Count < n) dst.Add(src[src.Count - 1]); // float drift safety
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
            if (extent < 1e-5f) return;
            float s = Size / extent;
            for (int i = 0; i < pts.Length; i++) pts[i] *= s;
        }

        static void TranslateToOrigin(Vector2[] pts)
        {
            Vector2 c = Vector2.zero;
            foreach (var p in pts) c += p;
            c /= pts.Length;
            for (int i = 0; i < pts.Length; i++) pts[i] -= c;
        }

        static float DistanceAtBestAngle(Vector2[] pts, Vector2[] tmpl, float a, float b, float tol)
        {
            float x1 = Phi * a + (1f - Phi) * b;
            float f1 = DistanceAtAngle(pts, tmpl, x1);
            float x2 = (1f - Phi) * a + Phi * b;
            float f2 = DistanceAtAngle(pts, tmpl, x2);
            while (Mathf.Abs(b - a) > tol)
            {
                if (f1 < f2)
                {
                    b = x2; x2 = x1; f2 = f1;
                    x1 = Phi * a + (1f - Phi) * b;
                    f1 = DistanceAtAngle(pts, tmpl, x1);
                }
                else
                {
                    a = x1; x1 = x2; f1 = f2;
                    x2 = (1f - Phi) * a + Phi * b;
                    f2 = DistanceAtAngle(pts, tmpl, x2);
                }
            }
            return Mathf.Min(f1, f2);
        }

        static float DistanceAtAngle(Vector2[] pts, Vector2[] tmpl, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float sum = 0f;
            int n = Mathf.Min(pts.Length, tmpl.Length);
            for (int i = 0; i < n; i++)
            {
                // points are already centered on the origin, rotate in place
                float rx = pts[i].x * cos - pts[i].y * sin;
                float ry = pts[i].x * sin + pts[i].y * cos;
                float dx = rx - tmpl[i].x;
                float dy = ry - tmpl[i].y;
                sum += Mathf.Sqrt(dx * dx + dy * dy);
            }
            return sum / n;
        }
    }
}
