using UnityEngine;

namespace SpellyZombie
{
    /// Arcball drag: grab a point on a ball around the object; as the cursor
    /// moves, rotate so the grabbed direction follows it, slerped. Shared by
    /// the shape pose mode and lifted cargo so they feel identical.
    public static class ArcballDrag
    {
        /// Where the cursor's ray meets the ball around the object. Off the
        /// ball, the nearest point on the ray is pushed onto the surface, so a
        /// drag never dies when your hand runs past the edge.
        public static Vector3 PointOnBall(Ray ray, Vector3 center, float radius)
        {
            Vector3 oc = ray.origin - center;
            float b = Vector3.Dot(oc, ray.direction);
            float c = Vector3.Dot(oc, oc) - radius * radius;
            float disc = b * b - c;
            if (disc >= 0f)
            {
                float t = -b - Mathf.Sqrt(disc);
                if (t > 0f) return ray.GetPoint(t);
            }
            Vector3 near = ray.origin + ray.direction * Mathf.Max(0f, -b);
            Vector3 d = near - center;
            return d.sqrMagnitude < 1e-6f
                ? center + ray.direction * radius
                : center + d.normalized * radius;
        }

        /// The direction from the centre to the grabbed point, at grab time.
        public static Vector3 Grab(Camera cam, Vector2 screen, Vector3 center, float radius)
            => (PointOnBall(cam.ScreenPointToRay(screen), center, radius) - center).normalized;

        /// Turn `current` so `fromDirWorld` lands where the cursor now points.
        public static Quaternion Turn(Camera cam, Vector2 screen, Vector3 center, float radius,
            Vector3 fromDirWorld, Quaternion current, float dt, float sharpness = 30f)
        {
            if (cam == null) return current;
            Vector3 to = (PointOnBall(cam.ScreenPointToRay(screen), center, radius) - center).normalized;
            if (fromDirWorld.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return current;
            Quaternion want = Quaternion.FromToRotation(fromDirWorld.normalized, to) * current;
            return Quaternion.Slerp(current, want, 1f - Mathf.Exp(-sharpness * dt));
        }
    }
}
