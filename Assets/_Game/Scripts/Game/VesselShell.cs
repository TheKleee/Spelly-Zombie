using UnityEngine;

namespace SpellyZombie
{
    /// Kinematic follower carrying a vessel's concave bowl collider (dynamic
    /// bodies must be convex, losing the hollow). Layer 29 collides with
    /// Default only; the pen raycast sees it, the grab ray masks it out.
    public class VesselShell : MonoBehaviour
    {
        public const int Layer = 29; // name it "VesselShell" in Tags & Layers (cosmetic)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            // collide with Default only - the ink ball's world - pass everything else
            for (int i = 0; i < 32; i++)
                Physics.IgnoreLayerCollision(Layer, i, i != 0);
        }

        Transform _follow;
        Rigidbody _rb;

        /// Build the follower for a vessel. `cargo` is the subtree that must
        /// keep colliding with the shell (the ink ball); the ignore loop
        /// below exempts it.
        public static VesselShell Attach(Transform followPose, Mesh bowlMesh, Transform vesselRoot,
            Transform cargo = null)
        {
            if (followPose == null || bowlMesh == null) return null;
            var go = new GameObject(vesselRoot.name + "_Bowl");
            go.layer = Layer;
            go.transform.SetPositionAndRotation(followPose.position, followPose.rotation);
            go.transform.localScale = followPose.lossyScale;

            var s = go.AddComponent<VesselShell>();
            s._follow = followPose;
            s._rb = go.AddComponent<Rigidbody>();
            s._rb.isKinematic = true; // kinematic + concave = legal, and it can cup a dynamic ball

            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = bowlMesh;
            col.convex = false;

            // ignore the vessel's own colliders (same space); cargo keeps colliding
            foreach (var own in vesselRoot.GetComponentsInChildren<Collider>(true))
            {
                if (own == null) continue;
                if (cargo != null && own.transform.IsChildOf(cargo)) continue;
                Physics.IgnoreCollision(col, own, true);
            }

            return s;
        }

        void FixedUpdate()
        {
            if (_follow == null) { Destroy(gameObject); return; } // vessel died, shell follows
            _rb.MovePosition(_follow.position);
            _rb.MoveRotation(_follow.rotation);
        }
    }
}
