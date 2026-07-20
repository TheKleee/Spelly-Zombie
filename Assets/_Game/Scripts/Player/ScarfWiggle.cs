using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE WIGGLE ENGINE (Marko's scarf design: the collar stands rigid, the
    /// tail trails and wiggles behind you — clipping allowed, cloth banned).
    ///
    /// HIS MODELING CONTRACT — works on ANY costume piece, player or zombie:
    ///   name a child transform "Wiggle" (or "Wiggle_1" → "Wiggle_2" → …
    ///   nested as a chain, pivot at each joint) and that part comes alive:
    ///   it lags behind movement, swings on turns, droops with gravity, and
    ///   settles when you stand still. Everything not named Wiggle stays
    ///   exactly where he modeled it.
    ///
    /// One component sits on each chain's TOP segment and updates the whole
    /// chain top-down (per-segment components would update in undefined
    /// order and jitter). Springs are simple tip-mass physics — deterministic
    /// enough, zero network cost, impossible to wrap around a neck.
    public class ScarfWiggle : MonoBehaviour
    {
        [Tooltip("Spring pull toward the modeled pose")]
        public float Stiffness = 70f;
        [Tooltip("Velocity damping — higher settles faster")]
        public float Damping = 4.5f;
        [Tooltip("How much the tail droops")]
        public float Gravity = 5f;
        [Tooltip("Max swing away from the modeled pose (degrees)")]
        public float MaxSwing = 70f;

        class Segment
        {
            public Transform T;
            public Quaternion RestLocal;
            public Vector3 AxisLocal;   // the direction the tail EXTENDS, in local space
            public float Length;
            public Vector3 Tip;         // world-space virtual tip (the springy mass)
            public Vector3 Vel;
        }

        readonly List<Segment> _chain = new List<Segment>();

        /// Scan a dressed body and put one wiggle engine on every chain top.
        public static void AttachAll(GameObject root)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Wiggle")) continue;
                bool chainTop = t.parent == null || !t.parent.name.StartsWith("Wiggle");
                if (chainTop && t.GetComponent<ScarfWiggle>() == null)
                    t.gameObject.AddComponent<ScarfWiggle>();
            }
        }

        void Start()
        {
            // the chain: this node, then each Wiggle child in nesting order
            var walk = transform;
            while (walk != null)
            {
                var seg = new Segment { T = walk, RestLocal = walk.localRotation };

                // the tail extends toward the next chain link when there is
                // one; a leaf segment extends along its renderer's long axis
                Transform next = null;
                foreach (Transform child in walk)
                    if (child.name.StartsWith("Wiggle")) { next = child; break; }
                if (next != null)
                {
                    Vector3 local = next.localPosition;
                    seg.AxisLocal = local.sqrMagnitude > 1e-8f ? local.normalized : Vector3.down;
                    seg.Length = Mathf.Max(local.magnitude, 0.03f);
                }
                else
                {
                    var rend = walk.GetComponentInChildren<Renderer>();
                    Vector3 size = rend != null ? rend.bounds.size : Vector3.one * 0.15f;
                    seg.Length = Mathf.Max(Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.8f, 0.05f);
                    seg.AxisLocal = Vector3.down; // convention: a lone tail hangs
                }
                seg.Tip = walk.position + walk.TransformDirection(seg.AxisLocal) * seg.Length;
                _chain.Add(seg);
                walk = next;
            }
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.033f); // spring stability on hitches
            if (dt <= 0f) return;

            foreach (var seg in _chain)
            {
                if (seg.T == null || seg.T.parent == null) continue;

                // where the modeler put it, as posed by the body this frame
                Quaternion restRot = seg.T.parent.rotation * seg.RestLocal;
                Vector3 restDir = restRot * seg.AxisLocal;
                Vector3 targetTip = seg.T.position + restDir * seg.Length;

                // tip-mass spring: chase the modeled pose, droop, settle
                seg.Vel += (targetTip - seg.Tip) * (Stiffness * dt);
                seg.Vel += Vector3.down * (Gravity * dt);
                seg.Vel *= Mathf.Clamp01(1f - Damping * dt);
                seg.Tip += seg.Vel * dt;

                // keep the tip on its sphere and inside the swing cone
                Vector3 dir = seg.Tip - seg.T.position;
                if (dir.sqrMagnitude < 1e-8f) dir = restDir;
                dir = dir.normalized;
                float angle = Vector3.Angle(restDir, dir);
                if (angle > MaxSwing)
                    dir = Vector3.Slerp(restDir, dir, MaxSwing / angle).normalized;
                seg.Tip = seg.T.position + dir * seg.Length;

                seg.T.rotation = Quaternion.FromToRotation(restDir, dir) * restRot;
            }
        }
    }
}
