using UnityEngine;

namespace SpellyZombie
{
    /// Size step - multiplies the material's base weight.
    public enum PropSize { Small = 1, Medium = 2, Large = 3, XL = 4, XXL = 5 }

    /// Marks a prop liftable. A Rooted prop stays static geometry until ink
    /// overpowers its anchor; it then gets a Rigidbody and stays free.
    [DisallowMultipleComponent]
    public class Liftable : MonoBehaviour
    {
        [Tooltip("Rooted = part of the scenery until someone tears it loose. " +
                 "Uncheck for things that should already be loose objects.")]
        public bool Rooted = true;

        [Tooltip("Ink needed to tear it out of the ground. 0 = work it out from " +
                 "the object's size (a bench yields, a house doesn't).")]
        public float AnchorHold = 0f;

        [Tooltip("How big it is. Weight = the material's base weight × this " +
                 "(Small ×1, Medium ×2, Large ×3, XL ×4, XXL ×5).")]
        public PropSize Size = PropSize.Medium;

        [Tooltip("Kilograms once it's free. 0 = material base weight × Size.")]
        public float Mass = 0f;

        /// Ink to rip this out of the world - by default the same as lifting it.
        public float HoldStrength => AnchorHold > 0f ? AnchorHold
            : BodyMass * DrawingConfig.LiftInkPerKg;

        /// Its weight once free - yours if you set one, else from its size.
        public float BodyMass => Mass > 0f ? Mass : InkMark.EstimateMass(transform);

        void Awake()
        {
            // a collider is the ONLY thing it needs while rooted
            if (GetComponent<Collider>() == null && GetComponentInChildren<Collider>() == null)
                gameObject.AddComponent<BoxCollider>();

            if (!Rooted && GetComponent<Rigidbody>() == null)
                ApplyBody(gameObject.AddComponent<Rigidbody>());
        }

        /// Wake a rooted prop into a real physics object - once, forever.
        public Rigidbody TearLoose()
        {
            Rooted = false;
            MakePhysicsLegal(transform);   // BEFORE the body exists, or Unity complains
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            ApplyBody(rb);
            return rb;
        }

        void ApplyBody(Rigidbody rb)
        {
            rb.mass = Mathf.Max(0.2f, BodyMass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // thrown things are FAST and props are THIN - without this they
            // tunnel straight through the floor between physics steps
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            MakePhysicsLegal(rb.transform);
            // over-cap meshes stay concave and would fall through the map as a
            // dynamic body - keep it kinematic, loudly
            if (WorldScale(rb.transform, out var dim))
            {
                rb.isKinematic = true;
                Debug.LogWarning($"[SpellyZombie] {rb.name} measures " +
                    $"{dim.x:0.#}×{dim.y:0.#}×{dim.z:0.#}m, over the " +
                    $"{DrawingConfig.LiftMaxDimension}m world cap, so it stays " +
                    "kinematic instead of falling through the world", rb);
            }
        }

        /// Anything whose solid collider bounds exceed LiftMaxDimension in any
        /// dimension is the WORLD: never liftable, never given a rigidbody,
        /// never made convex. Ink never overrules it.
        public static bool WorldScale(Transform root, out Vector3 size)
        {
            size = Vector3.zero;
            if (root == null) return false;
            bool any = false;
            var b = new Bounds();
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue; // shells and zones aren't the body
                // a disabled collider reports empty bounds at the world origin -
                // only live colliders are the body
                if (!c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!any) return false;
            size = b.size;
            float cap = DrawingConfig.LiftMaxDimension;
            return size.x > cap || size.y > cap || size.z > cap;
        }

        /// Unity does not simulate concave MeshColliders on dynamic bodies, so
        /// a freed prop's mesh colliders must go convex - except meshes over
        /// the world cap (a convex ground hull is a bowl); those refuse, loudly.
        public static void MakePhysicsLegal(Transform root)
        {
            float cap = DrawingConfig.LiftMaxDimension;
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc.convex) continue;
                var s = mc.bounds.size;
                if (s.x > cap || s.y > cap || s.z > cap)
                {
                    Debug.LogWarning($"[SpellyZombie] refusing to convex {mc.name} " +
                        $"({s.x:0.#}×{s.y:0.#}×{s.z:0.#}m exceeds the {cap}m world cap). " +
                        "That mesh is the world, not a prop; it stays static geometry", mc);
                    continue;
                }
                mc.convex = true;
            }
        }
    }
}
