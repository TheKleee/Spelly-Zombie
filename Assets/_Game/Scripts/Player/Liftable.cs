using UnityEngine;

namespace SpellyZombie
{
    /// How big a thing is, as a step rather than a measurement — it multiplies
    /// the material's base weight (Marko: "size can be 1 = small, 2 = medium,
    /// 3 = large, 4 = xL, 5 = xxL").
    public enum PropSize { Small = 1, Medium = 2, Large = 3, XL = 4, XXL = 5 }

    /// YOU DECIDE WHAT CAN BE PICKED UP. Drop this on anything — a bench, a
    /// chair, a crate, a log — and it becomes liftable. No material tag, no
    /// guessing by code, no rules about what "counts".
    ///
    /// NO RIGIDBODY UNTIL IT EARNS ONE (Marko): a rooted prop stays plain
    /// static geometry — free for physics, no wobble, no cost — and only
    /// becomes a real body at the moment your ink overpowers what holds it
    /// down. From then on it stays free. Adding the body up front (which this
    /// used to do) made the game skip the anchor check entirely.
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

        /// What it takes to rip this out of the world. By default that's
        /// exactly what it costs to LIFT it — freeing something you could
        /// already carry shouldn't be a second, harder puzzle.
        public float HoldStrength => AnchorHold > 0f ? AnchorHold
            : BodyMass * DrawingConfig.LiftInkPerKg;

        /// Its weight once free — yours if you set one, else from its size.
        public float BodyMass => Mass > 0f ? Mass : InkMark.EstimateMass(transform);

        void Awake()
        {
            // a collider is the ONLY thing it needs while rooted
            if (GetComponent<Collider>() == null && GetComponentInChildren<Collider>() == null)
                gameObject.AddComponent<BoxCollider>();

            if (!Rooted && GetComponent<Rigidbody>() == null)
                ApplyBody(gameObject.AddComponent<Rigidbody>());
        }

        /// Wake a rooted prop into a real physics object — once, forever.
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
            // thrown things are FAST and props are THIN — without this they
            // tunnel straight through the floor between physics steps
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            MakePhysicsLegal(rb.transform);
            // if the legality pass REFUSED (world-scale meshes stay concave),
            // a dynamic body here would simply stop colliding and fall out of
            // the map. An over-cap body stays kinematic — loudly, because a
            // Liftable authored un-Rooted at this size is an authoring mistake.
            if (WorldScale(rb.transform, out var dim))
            {
                rb.isKinematic = true;
                Debug.LogWarning($"[SpellyZombie] {rb.name} measures " +
                    $"{dim.x:0.#}×{dim.y:0.#}×{dim.z:0.#}m, over the " +
                    $"{DrawingConfig.LiftMaxDimension}m world cap, so it stays " +
                    "kinematic instead of falling through the world", rb);
            }
        }

        /// THE WORLD-OR-PROP LINE (Marko's fall-through, Aug 4: "Grabbing the
        /// liquid ball made me fall through the ground indefinitely... I guess
        /// I lifted the ground collider just a little bit"). His aim ray
        /// missed the blob, hit the FLOOR, and the floor — carrying every
        /// stroke he had ever drawn on it — out-inked its own anchor and tore
        /// loose. Anything whose solid collider bounds exceed
        /// DrawingConfig.LiftMaxDimension in ANY dimension is the WORLD:
        /// never liftable, never given a rigidbody, never made convex. Size
        /// decides; ink never overrules it. Benches, chairs and crates all
        /// measure under the cap, so props stay liftable exactly as ruled.
        public static bool WorldScale(Transform root, out Vector3 size)
        {
            size = Vector3.zero;
            if (root == null) return false;
            bool any = false;
            var b = new Bounds();
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue; // shells and zones aren't the body
                // a DISABLED collider reports empty bounds AT THE WORLD ORIGIN
                // — encapsulating that stretches the box from the prop to 0,0,0
                // and a legit bench "measures" 48m of world. Only live colliders
                // are the body. (This can only shrink the measurement of real
                // props, never the floor's — the world guarantee stands.)
                if (!c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!any) return false;
            size = b.size;
            float cap = DrawingConfig.LiftMaxDimension;
            return size.x > cap || size.y > cap || size.z > cap;
        }

        /// UNITY WILL NOT SIMULATE A CONCAVE MESH COLLIDER ON A MOVING BODY —
        /// it logs "Concave Mesh Colliders are not supported when used with
        /// dynamic Rigidbody GameObjects" and the collider simply stops
        /// working, which is why freed props fall through the world. Scenery
        /// meshes are concave almost by definition, so the instant something
        /// becomes dynamic its mesh colliders must go convex.
        ///
        /// EXCEPT THE WORLD ITSELF: a convex hull of the ground is a BOWL
        /// (Marko's fall-through — convexing the village floor bridged its
        /// concavities and he stood INSIDE the hull, falling forever). A mesh
        /// bigger than the world cap is never convexed, and never silently:
        /// the log names the mesh and the numbers, because a refusal here
        /// means some caller upstream should not have made this thing dynamic.
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
