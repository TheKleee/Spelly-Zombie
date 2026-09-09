using UnityEngine;

namespace SpellyZombie
{
    /// A throw locks onto the enemy nearest the aim, inside a cone and in
    /// view, and flies into it. A hidden acolyte is a prop to the hand too.
    public static class LockOn
    {
        static readonly RaycastHit[] _hits = new RaycastHit[8];

        /// The lock for a throw from 'from' along 'dir', or null. 'thrower' and
        /// 'thrown' are skipped by the sight check (the hand and what it holds).
        public static Transform Pick(Vector3 from, Vector3 dir, int ownerId, Transform thrower, Transform thrown)
        {
            if (dir.sqrMagnitude < 0.01f) return null;
            dir.Normalize();
            Team mine = Teams.OfOwner(ownerId);
            float range = DrawingConfig.ThrowLockRange;
            float bestDot = Mathf.Cos(DrawingConfig.ThrowLockCone * Mathf.Deg2Rad);
            Transform best = null;

            void Consider(Transform t, Component c)
            {
                if (t == null || !Teams.Enemies(mine, Teams.Of(c))) return;
                Vector3 to = t.position + Vector3.up * 0.9f - from;
                float d = to.magnitude;
                if (d < 0.5f || d > range) return;
                Vector3 n = to / d;
                float dot = Vector3.Dot(n, dir);
                if (dot <= bestDot) return;
                if (!Clear(from, n, d, t, thrower, thrown)) return;
                bestDot = dot;
                best = t;
            }

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned) continue;
                if (thrower != null && p.transform == thrower) continue;
                if (ShapeShift.Disguised(p)) continue;
                Consider(p.transform, p);
            }
            foreach (var z in Zombie.All) if (z != null) Consider(z.transform, z);
            foreach (var g in Golem.All) if (g != null && g.Alive) Consider(g.transform, g);
            return best;
        }

        static bool Clear(Vector3 from, Vector3 dir, float len, Transform target, Transform thrower, Transform thrown)
        {
            int n = Physics.RaycastNonAlloc(from, dir, _hits, len,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Collider blocker = null;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null) continue;
                var t = h.collider.transform;
                if (thrower != null && t.IsChildOf(thrower)) continue;
                if (thrown != null && t.IsChildOf(thrown)) continue;
                if (h.distance < nearest) { nearest = h.distance; blocker = h.collider; }
            }
            if (blocker == null || blocker.transform.IsChildOf(target)) return true;
            var shell = ZombieOwner.From(blocker); // a zombie's paint shell sits outside its hierarchy
            return shell != null && shell.transform == target;
        }
    }
}
