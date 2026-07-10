using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// How zombies draw. Synthetic strokes go through the SAME pipeline as the
    /// player's pen — real DrawNodes, registered strokes, normal seal detection
    /// and recognition — so everything composes: a zombie's circle around your
    /// rune closes a real seal (owned by the ZOMBIE, cast with ITS cards), and a
    /// rune a zombie scrawls on a buddy is real ink you can circle yourself.
    public static class ZombieScribe
    {
        /// Draw a closed ring and seal it (the compulsion payoff). Every point is
        /// raycast-snapped onto the actual surface like the player's pen — points
        /// that miss (target moved, curved body, edge of a crate) simply don't
        /// land. Too many misses and the loop can't close: the zombie proudly
        /// produced a useless arc. Returns false on that glorious failure.
        public static bool DrawCircle(Vector3 center, Vector3 normal, float radius,
            Transform surface, int ownerId)
        {
            if (DrawingWorld.Instance == null || surface == null) return false;
            PlaneBasis(normal, out var right, out var up);

            var s = new Stroke { BasisRight = right, BasisUp = up, Surface = surface, OwnerId = ownerId };
            DrawingWorld.Instance.Register(s);

            const int steps = 22;
            for (int i = 0; i <= steps; i++) // first and last point meet — a loop
            {
                float a = i / (float)steps * Mathf.PI * 2f;
                // wobble: zombies do not own a compass
                float r = radius * (1f + Mathf.PerlinNoise(a * 2f, ownerId * 0.13f) * 0.15f);
                Vector3 p = center + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * r;
                if (Snap(p, normal, surface, out var onSurface, out var n))
                    s.AddNode(DrawNode.Create(s, s.Nodes.Count, onSurface, n, surface));
            }

            if (s.Nodes.Count >= DrawingConfig.MinLoopNodes)
            {
                DrawingWorld.Instance.CloseSingleStroke(s); // same path as the player pen
                return true;
            }
            DrawingWorld.Instance.CompleteStroke(s); // a sad little arc remains
            return false;
        }

        /// Stamp a rune glyph onto a surface — the ONE way runes enter the world
        /// now (player choose-and-stamp AND zombie scrawls). The stroke carries
        /// DeclaredRune, so seals read it with zero recognition. Direction runes
        /// pass alignTravel (the sketch's pen direction) so the stamp points the
        /// way the player drew. wobble > 0 adds the shaky zombie hand.
        public static void DrawGlyph(RuneType rune, Vector3 center, Vector3 normal,
            float size, Transform surface, int ownerId,
            Vector3 alignTravel = default, float wobble = 0.03f)
        {
            var poly = RuneLibrary.GlyphPolyline(rune);
            if (poly == null || poly.Count < 2 || DrawingWorld.Instance == null) return;

            // normalize template to [-0.5, 0.5]²
            Vector2 min = poly[0], max = poly[0];
            foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            Vector2 span = Vector2.Max(max - min, Vector2.one * 0.001f);
            float scale = size / Mathf.Max(span.x, span.y);

            PlaneBasis(normal, out var right, out var up);

            // rotate the stamp so the template's pen-travel matches the sketch's
            if (alignTravel != Vector3.zero)
            {
                Vector2 td = (poly[poly.Count - 1] - poly[0]).normalized;
                Vector3 mapped = right * td.x + up * td.y;
                Vector3 target = Vector3.ProjectOnPlane(alignTravel, normal);
                if (mapped.sqrMagnitude > 0.001f && target.sqrMagnitude > 0.001f)
                {
                    float angle = Vector3.SignedAngle(mapped, target.normalized, normal);
                    var rot = Quaternion.AngleAxis(angle, normal);
                    right = rot * right;
                    up = rot * up;
                }
            }

            var s = new Stroke { BasisRight = right, BasisUp = up, Surface = surface, OwnerId = ownerId, DeclaredRune = rune };
            DrawingWorld.Instance.Register(s);

            Vector2 prev = Vector2.zero;
            bool hasPrev = false;
            foreach (var p in poly)
            {
                // subdivide segments so stamped ink is dense enough to read
                if (hasPrev) PlacePoint(s, (prev + p) * 0.5f, min, max, scale, size, wobble, center, right, up, normal, surface);
                PlacePoint(s, p, min, max, scale, size, wobble, center, right, up, normal, surface);
                prev = p;
                hasPrev = true;
            }
            DrawingWorld.Instance.CompleteStroke(s); // too few landed points → it burns; oh well
        }

        static void PlacePoint(Stroke s, Vector2 p, Vector2 min, Vector2 max, float scale,
            float size, float wobble, Vector3 center, Vector3 right, Vector3 up, Vector3 normal, Transform surface)
        {
            Vector2 local = (p - (min + max) * 0.5f) * scale;
            if (wobble > 0f) local += Random.insideUnitCircle * size * wobble;
            Vector3 world = center + right * local.x + up * local.y;
            if (Snap(world, normal, surface, out var onSurface, out var n))
                s.AddNode(DrawNode.Create(s, s.Nodes.Count, onSurface, n, surface));
        }

        /// Sit a point ON the surface: cast from just above the intended spot
        /// back toward it, and accept only the intended surface. Misses are
        /// allowed — zombie drawings gap, wobble, and sometimes fail entirely.
        static bool Snap(Vector3 desired, Vector3 normal, Transform surface,
            out Vector3 pos, out Vector3 hitNormal)
        {
            if (Physics.Raycast(desired + normal * 0.6f, -normal, out var hit, 1.4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && (hit.transform == surface || hit.transform.IsChildOf(surface) || surface.IsChildOf(hit.transform)))
            {
                pos = hit.point;
                hitNormal = hit.normal;
                return true;
            }
            pos = default;
            hitNormal = default;
            return false;
        }

        /// Find the best nearby "unfinished doodle" — open, intact ink that is
        /// not the scribe's own. This is what a scribbler cannot walk past.
        public static Stroke FindDoodle(Vector3 from, float range, int selfId, Transform selfBody)
        {
            if (DrawingWorld.Instance == null) return null;
            Stroke best = null;
            float bestDist = range * range;
            foreach (var s in DrawingWorld.Instance.Strokes)
            {
                if (!s.Alive || s.State != StrokeState.Open) continue;
                if (s.OwnerId == selfId) continue;              // its own art is perfect already
                if (s.Nodes.Count < 4 || !s.ChainIntact()) continue;
                var first = s.First;
                if (first == null) continue;
                if (first.transform.IsChildOf(selfBody)) continue; // can't reach its own back
                float d = (s.Centroid() - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        /// Cluster geometry for circling a doodle: centroid, average surface
        /// normal, and the in-plane radius that encloses every node.
        public static void MeasureDoodle(Stroke s, out Vector3 center, out Vector3 normal, out float radius)
        {
            center = s.Centroid();
            normal = Vector3.zero;
            foreach (var n in s.Nodes)
                if (n != null) normal += n.SurfaceNormal;
            normal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;

            radius = 0.25f;
            foreach (var n in s.Nodes)
            {
                if (n == null) continue;
                Vector3 d = n.transform.position - center;
                d -= normal * Vector3.Dot(d, normal); // in-plane distance only
                radius = Mathf.Max(radius, d.magnitude);
            }
            radius += 0.3f; // circle AROUND it, not through it
        }

        public static void PlaneBasis(Vector3 normal, out Vector3 right, out Vector3 up)
        {
            right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.Cross(normal, Vector3.forward);
            right.Normalize();
            up = Vector3.Cross(right, normal).normalized;
        }
    }
}
