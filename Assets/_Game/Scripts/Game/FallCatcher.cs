using UnityEngine;

namespace SpellyZombie
{
    /// Safety net under the world: a trigger slab catches falls over the map,
    /// and the controller also checks KillY every frame. Mid-run the caught
    /// player returns to the map middle downed; in the lobby it is free.
    public class FallCatcher : MonoBehaviour
    {
        /// The world's absolute floor - below this you're caught, always.
        public const float KillY = -12f;

        public Vector3 RespawnPoint = new Vector3(0f, 2f, 5f);

        static FallCatcher _placed; // the map builder's slab (its point wins)
        void OnEnable() => _placed = this;
        void OnDisable() { if (_placed == this) _placed = null; }

        /// The scene's own anchor - where a body belongs when nothing else
        /// says otherwise. SpawnPlan scatters around this in the lobby.
        public static Vector3 Home =>
            _placed != null ? _placed.RespawnPoint : new Vector3(0f, 2f, 0f);

        /// Move a player without the CharacterController dragging them back.
        /// One recipe, so every teleport in the game behaves the same.
        public static void Teleport(SimpleFPSController pilot, Vector3 to)
        {
            if (pilot == null) return;
            var cc = pilot.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false; // CharacterController fights teleports
            pilot.transform.position = to;
            if (cc != null) cc.enabled = true;
            pilot.CancelMomentum();
        }

        /// Bring a fallen player home: middle of the map, on real ground,
        /// momentum wiped. During a run they arrive FLOORED (revivable).
        public static void Catch(SimpleFPSController pilot)
        {
            if (pilot == null) return;
            Vector3 home = Home;
            // drop onto the ACTUAL ground - terrain maps aren't flat
            if (Physics.Raycast(home + Vector3.up * 40f, Vector3.down, out var hit, 100f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                home = hit.point + Vector3.up * 1.5f;

            Teleport(pilot, home);

            if (RoundDirector.RunActive && !pilot.IsDowned)
            {
                pilot.DropDowned();
                DrawingWorld.Instance?.LogEvent("the void spat you back. hold E over them to help");
            }
            else
            {
                DrawingWorld.Instance?.LogEvent("the world caught you. watch that last step");
            }
        }

        void OnTriggerEnter(Collider other)
        {
            // parent lookup: player bones carry rigidbody+colliders and enter
            // the net before the capsule; the junk branch must not eat them
            var pilot = other.GetComponentInParent<SimpleFPSController>();
            if (pilot != null)
            {
                Catch(pilot);
                return;
            }

            // creatures die via TakeDamage so drops and kill credit fire;
            // matter and junk just vanish
            if (other.attachedRigidbody != null)
            {
                var creature = other.attachedRigidbody.GetComponentInParent<Creature>();
                var victim = creature != null ? creature.GetComponent<Element>() : null;
                if (victim != null)
                {
                    victim.TakeDamage(99999f, "the void");
                    return;
                }
                Destroy(other.attachedRigidbody.gameObject);
            }
        }
    }

    /// Layer 30 = ink canvases: drawable planes spanning whole facades so
    /// strokes never split at module seams. Nothing collides with them, but
    /// raycasts still hit them.
    public static class InkCanvasLayer
    {
        public const int Layer = 30;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            for (int i = 0; i < 32; i++)
                Physics.IgnoreLayerCollision(Layer, i, true);
        }
    }
}
