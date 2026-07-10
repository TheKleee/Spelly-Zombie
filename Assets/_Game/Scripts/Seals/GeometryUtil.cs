using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Pure geometry helpers for seal shape analysis:
    /// plane fitting, projection, RDP corner counting, circle test, containment.
    public static class GeometryUtil
    {
        /// True if segments p1p2 and p3p4 properly cross, with the crossing's
        /// parameters along each (t on p1p2, u on p3p4) in (0,1).
        public static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4,
                                             out float t, out float u)
        {
            t = 0f; u = 0f;
            Vector2 r = p2 - p1;
            Vector2 s = p4 - p3;
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 1e-9f) return false; // parallel / collinear
            Vector2 qp = p3 - p1;
            t = (qp.x * s.y - qp.y * s.x) / denom;
            u = (qp.x * r.y - qp.y * r.x) / denom;
            return t > 1e-4f && t < 1f - 1e-4f && u > 1e-4f && u < 1f - 1e-4f;
        }

        /// Newell's method — robust polygon normal for a (possibly wobbly, non-planar) loop.
        public static Vector3 NewellNormal(IReadOnlyList<Vector3> pts)
        {
            Vector3 n = Vector3.zero;
            int count = pts.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 c = pts[i];
                Vector3 x = pts[(i + 1) % count];
                n.x += (c.y - x.y) * (c.z + x.z);
                n.y += (c.z - x.z) * (c.x + x.x);
                n.z += (c.x - x.x) * (c.y + x.y);
            }
            return n;
        }

        public static void PlaneBasis(Vector3 normal, out Vector3 u, out Vector3 v)
        {
            u = Vector3.Cross(normal, Vector3.up);
            if (u.sqrMagnitude < 1e-6f)
                u = Vector3.Cross(normal, Vector3.right);
            u.Normalize();
            v = Vector3.Cross(normal, u).normalized;
        }

        public static List<Vector2> ProjectToPlane(IReadOnlyList<Vector3> pts, Vector3 origin, Vector3 u, Vector3 v)
        {
            var result = new List<Vector2>(pts.Count);
            foreach (var p in pts)
            {
                Vector3 d = p - origin;
                result.Add(new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)));
            }
            return result;
        }

        public static Vector2 ProjectPoint(Vector3 p, Vector3 origin, Vector3 u, Vector3 v)
        {
            Vector3 d = p - origin;
            return new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v));
        }

        public static float Perimeter(IReadOnlyList<Vector2> pts, bool closed)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
            if (closed && pts.Count > 2) len += Vector2.Distance(pts[pts.Count - 1], pts[0]);
            return len;
        }

        /// Shoelace formula, absolute area of the projected loop.
        public static float PolygonArea(IReadOnlyList<Vector2> pts)
        {
            float a = 0f;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
                a += (pts[j].x + pts[i].x) * (pts[j].y - pts[i].y);
            return Mathf.Abs(a * 0.5f);
        }

        public static Vector2 Centroid2D(IReadOnlyList<Vector2> pts)
        {
            Vector2 sum = Vector2.zero;
            foreach (var p in pts) sum += p;
            return pts.Count > 0 ? sum / pts.Count : Vector2.zero;
        }

        public static Vector2 BoundingSize(IReadOnlyList<Vector2> pts)
        {
            if (pts.Count == 0) return Vector2.zero;
            Vector2 min = pts[0], max = pts[0];
            foreach (var p in pts)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return max - min;
        }

        /// Coefficient of variation of point distances from the centroid.
        /// Near-zero = a circle. A square lands around 0.10, a triangle higher.
        public static float RadialVariation(IReadOnlyList<Vector2> pts)
        {
            if (pts.Count < 3) return 1f;
            Vector2 c = Centroid2D(pts);
            float mean = 0f;
            foreach (var p in pts) mean += Vector2.Distance(p, c);
            mean /= pts.Count;
            if (mean < 1e-5f) return 1f;
            float var = 0f;
            foreach (var p in pts)
            {
                float d = Vector2.Distance(p, c) - mean;
                var += d * d;
            }
            return Mathf.Sqrt(var / pts.Count) / mean;
        }

        static float PerpDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-6f) return Vector2.Distance(p, a);
            float cross = Mathf.Abs(ab.x * (p.y - a.y) - ab.y * (p.x - a.x));
            return cross / len;
        }

        static void Rdp(IReadOnlyList<Vector2> pts, int i0, int i1, float eps, List<int> keep)
        {
            float maxD = -1f;
            int idx = -1;
            for (int i = i0 + 1; i < i1; i++)
            {
                float d = PerpDistance(pts[i], pts[i0], pts[i1]);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (idx >= 0 && maxD > eps)
            {
                Rdp(pts, i0, idx, eps, keep);
                keep.Add(idx);
                Rdp(pts, idx, i1, eps, keep);
            }
        }

        /// Corner count of a closed loop: split the ring at the point farthest from
        /// the start, simplify both halves with Ramer-Douglas-Peucker, then drop
        /// "corners" whose direction change is too shallow to count as a real edge.
        public static int ClosedLoopCorners(IReadOnlyList<Vector2> pts)
        {
            int n = pts.Count;
            if (n < 3) return 3;

            Vector2 bs = BoundingSize(pts);
            float eps = Mathf.Max(1e-4f, bs.magnitude * DrawingConfig.RdpEpsilonFactor);

            int m = 0;
            float best = -1f;
            for (int i = 1; i < n; i++)
            {
                float d = Vector2.Distance(pts[i], pts[0]);
                if (d > best) { best = d; m = i; }
            }
            if (m == 0 || m == n - 1) m = n / 2;

            var cornerIdx = new List<int> { 0 };
            Rdp(pts, 0, m, eps, cornerIdx);
            cornerIdx.Add(m);
            var second = new List<int>();
            // second half wraps back to index 0; run RDP on a stitched copy
            var half = new List<Vector2>();
            var map = new List<int>();
            for (int i = m; i < n; i++) { half.Add(pts[i]); map.Add(i); }
            half.Add(pts[0]);
            map.Add(0);
            var keep = new List<int>();
            Rdp(half, 0, half.Count - 1, eps, keep);
            foreach (var k in keep) second.Add(map[k]);
            cornerIdx.AddRange(second);

            cornerIdx.Sort();
            // dedupe
            var ring = new List<int>();
            foreach (var i in cornerIdx)
                if (ring.Count == 0 || ring[ring.Count - 1] != i) ring.Add(i);
            if (ring.Count < 3) return 3;

            // keep only corners with a real direction change
            int corners = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 prev = pts[ring[(i - 1 + ring.Count) % ring.Count]];
                Vector2 cur = pts[ring[i]];
                Vector2 next = pts[ring[(i + 1) % ring.Count]];
                Vector2 a = (cur - prev).normalized;
                Vector2 b = (next - cur).normalized;
                if (a == Vector2.zero || b == Vector2.zero) continue;
                float turn = Vector2.Angle(a, b);
                if (turn >= DrawingConfig.MinCornerAngle) corners++;
            }
            return Mathf.Max(3, corners);
        }

        /// Even-odd point-in-polygon test in the seal's 2D plane space.
        public static bool PointInPolygon(Vector2 pt, IReadOnlyList<Vector2> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if ((poly[i].y > pt.y) != (poly[j].y > pt.y) &&
                    pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
