using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Where a flying spell or a thrown thing is drawn to: the nearest enemy
    /// in range that is still ahead of it. A hidden acolyte is a prop here too.
    public static class LockOn
    {
        static readonly Dictionary<int, float> _lift = new Dictionary<int, float>();
        static float _liftClearAt;

        /// The nearest enemy of `ownerId` within `range` of `from` that lies
        /// ahead along `vel` (nothing pulls a mote back to what it passed),
        /// and where on it to aim. Null when there is none.
        public static Transform Nearest(Vector3 from, Vector3 vel, int ownerId, float range, out float lift)
        {
            Team mine = Teams.OfOwner(ownerId);
            float bestSqr = range * range;
            Transform best = null;
            lift = 0.9f;

            void Consider(Transform t, Team theirs)
            {
                if (t == null || theirs == mine) return; // three teams, all against each other
                Vector3 to = t.position - from;
                float d = to.sqrMagnitude;
                if (d >= bestSqr) return;
                if (vel.sqrMagnitude > 0.01f && Vector3.Dot(to, vel) < 0f) return;
                bestSqr = d;
                best = t;
            }

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned || ShapeShift.Disguised(p)) continue;
                Consider(p.transform, Teams.Of(p));
            }
            foreach (var a in NetAvatar.All)
                if (a != null && !a.Downed && !a.Disguised) Consider(a.transform, Teams.OfOwner(NetSync.OwnerIdOf(a.Id)));
            foreach (var z in Zombie.All) if (z != null) Consider(z.transform, Team.Acolyte);
            foreach (var g in Golem.All) if (g != null && g.Alive) Consider(g.transform, Teams.OfOwner(g.OwnerId));
            if (best != null) lift = AimHeight(best);
            return best;
        }

        /// How far above its root a body's middle sits: the middle of its solid
        /// colliders, else of what it renders, else chest height. Remembered per
        /// body, so a flying spell never walks a hierarchy every frame.
        public static float AimHeight(Transform t)
        {
            if (Time.time >= _liftClearAt) { _lift.Clear(); _liftClearAt = Time.time + 30f; }
            int id = t.GetInstanceID();
            if (_lift.TryGetValue(id, out float h)) return h;
            bool any = false;
            var b = new Bounds();
            foreach (var c in t.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger) continue;
                if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
            }
            if (!any)
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || r is TrailRenderer || r is LineRenderer || r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
            h = any ? Mathf.Max(0.05f, b.center.y - t.position.y) : 0.9f;
            _lift[id] = h;
            return h;
        }
    }
}
