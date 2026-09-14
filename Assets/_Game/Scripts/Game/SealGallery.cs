using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Per-round record of every seal cast (projected ink points + label);
    /// the SealAutopsy replay reads it.
    public static class SealGallery
    {
        public class Entry
        {
            public string Label;
            public float Time;                       // when it activated
            public List<List<Vector2>> BoundaryPts;  // kept for the autopsy replay
            public List<List<Vector2>> RunePts;
        }

        public static readonly List<Entry> Round = new List<Entry>();
        const int MaxEntries = 12;

        public static void Clear() => Round.Clear();

        /// Snapshot a seal the moment it activates (node positions are live now;
        /// they may be consumed later).
        public static void Capture(Seal seal, string comboName)
        {
            // gather boundary + payload points, projected into the seal plane
            PlaneAxes(seal.PlaneNormal, out var right, out var up);
            Vector3 origin = seal.PlaneOrigin;

            var boundary = new List<List<Vector2>>();
            foreach (var e in seal.Boundary)
                boundary.Add(ProjectStroke(e.Stroke, origin, right, up));

            var runes = new List<List<Vector2>>();
            int recognized = 0, fizzles = 0;
            foreach (var g in seal.Runes)
            {
                if (g.Rune != RuneType.None) recognized++; else fizzles++;
                foreach (var m in g.Members)
                    runes.Add(ProjectStroke(m, origin, right, up));
            }

            Add(boundary, runes, comboName, recognized, fizzles);
        }

        /// A seal that closed on another machine, from this machine's copies of
        /// its ink (SealLookMsg): boundary centroid and averaged node normal
        /// stand in for the seal plane; `read` = 1 per payload stroke read as a rune.
        public static void CaptureCopy(List<Stroke> boundaryStrokes, List<Stroke> payload, List<byte> read)
        {
            Vector3 origin = Vector3.zero, n = Vector3.zero;
            int count = 0;
            foreach (var s in boundaryStrokes)
            {
                if (s == null) continue;
                foreach (var node in s.Nodes)
                {
                    if (node == null) continue;
                    origin += node.transform.position;
                    n += node.SurfaceNormal;
                    count++;
                }
            }
            if (count == 0) return;
            origin /= count;
            n = n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up;
            PlaneAxes(n, out var right, out var up);

            var boundary = new List<List<Vector2>>();
            foreach (var s in boundaryStrokes)
                boundary.Add(ProjectStroke(s, origin, right, up));

            var runes = new List<List<Vector2>>();
            int recognized = 0, fizzles = 0;
            for (int i = 0; i < payload.Count; i++)
            {
                if (i < read.Count && read[i] == 1) recognized++; else fizzles++;
                runes.Add(ProjectStroke(payload[i], origin, right, up));
            }

            Add(boundary, runes, null, recognized, fizzles);
        }

        static void PlaneAxes(Vector3 n, out Vector3 right, out Vector3 up)
        {
            right = Vector3.ProjectOnPlane(
                Camera.main != null ? Camera.main.transform.right : Vector3.right, n);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.ProjectOnPlane(Vector3.forward, n);
            right.Normalize();
            up = Vector3.Cross(right, n).normalized;
        }

        static void Add(List<List<Vector2>> boundary, List<List<Vector2>> runes, string comboName,
            int recognized, int fizzles)
        {
            // degenerate (no points): skip
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            void Grow(List<List<Vector2>> set)
            {
                foreach (var line in set)
                    foreach (var p in line)
                    {
                        min = Vector2.Min(min, p);
                        max = Vector2.Max(max, p);
                    }
            }
            Grow(boundary); Grow(runes);
            if (min.x > max.x) return;

            string label = comboName ?? (recognized > 0
                ? $"{recognized} rune{(recognized > 1 ? "s" : "")}{(fizzles > 0 ? " +fizzle" : "")}"
                : "fizzle");

            Round.Add(new Entry
            {
                Label = label, Time = UnityEngine.Time.time,
                BoundaryPts = boundary, RunePts = runes
            });
            while (Round.Count > MaxEntries)
                Round.RemoveAt(0);
        }

        static List<Vector2> ProjectStroke(Stroke s, Vector3 origin, Vector3 right, Vector3 up)
        {
            var pts = new List<Vector2>();
            if (s == null) return pts;
            foreach (var node in s.Nodes)
            {
                if (node == null) continue;
                Vector3 d = node.transform.position - origin;
                pts.Add(new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)));
            }
            return pts;
        }
    }
}
