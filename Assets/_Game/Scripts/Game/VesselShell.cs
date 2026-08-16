using UnityEngine;

namespace SpellyZombie
{
    /// THE TRUE INSIDE OF A LIFTED VESSEL : "the cauldron once
    /// drawn on changes the mesh collider to convex which ruins the inside
    /// detections" — tear-loose must make the pot convex (Unity forbids concave
    /// on a dynamic body), and a convex bowl has no hollow: the ink ball gets
    /// squeezed out of a hole that no longer exists.
    ///
    /// the split, built from the zombie's two proven parts: the DRESS pattern
    /// (a kinematic world-space follower may carry a concave collider legally)
    /// plus the CANVAS pattern (a dedicated layer whose collisions are curated).
    /// The follower holds the REAL bowl shape and follows the pot; the pot's own
    /// convex hull keeps doing world physics. Layer 29 collides with DEFAULT
    /// only - so the liquid ball (and props, and zombies) feel the true bowl,
    /// while the player capsule (layer 2) and everything curated passes by.
    /// The pen's raycast sees it, so drawings land on the true surface and ride
    /// the pot. The GRAB ray masks it out, so lifting still finds the pot.
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

        /// Build the follower for a vessel. `bowl` is the bowl mesh, dragged in
        /// - never searched for. `cargo` is the subtree the shell exists to CUP
        /// (the ink ball): it lives UNDER the vessel, so the fight-avoidance
        /// ignore below must not swallow it - without this exception the shell
        /// ignored the very ball it was built for.
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

            // never fight the vessel's own colliders - they occupy the same
            // space - but the CARGO keeps colliding: that is the whole point
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
