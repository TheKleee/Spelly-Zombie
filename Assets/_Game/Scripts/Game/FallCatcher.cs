using UnityEngine;

namespace SpellyZombie
{
    /// The safety net under the world. The trigger slab (placed by the map
    /// builders) catches falls over the map itself; the controller ALSO
    /// checks KillY every frame, so a player flung past the slab's edges is
    /// caught anyway — nobody falls forever, no matter how far they went.
    ///
    /// The catch has a price mid-run (Marko's rule): back at the map middle
    /// but DOWNED — a teammate's E picks you up, the bleed-out or the horde
    /// finishes you. In the lobby it's a free ride home.
    public class FallCatcher : MonoBehaviour
    {
        /// The world's absolute floor — below this you're caught, always.
        public const float KillY = -12f;

        public Vector3 RespawnPoint = new Vector3(0f, 2f, 5f);

        static FallCatcher _placed; // the map builder's slab (its point wins)
        void OnEnable() => _placed = this;
        void OnDisable() { if (_placed == this) _placed = null; }

        /// Bring a fallen player home: middle of the map, on real ground,
        /// momentum wiped. During a run they arrive FLOORED (revivable).
        public static void Catch(SimpleFPSController pilot)
        {
            if (pilot == null) return;
            Vector3 home = _placed != null ? _placed.RespawnPoint : new Vector3(0f, 2f, 0f);
            // drop onto the ACTUAL ground — terrain maps aren't flat
            if (Physics.Raycast(home + Vector3.up * 40f, Vector3.down, out var hit, 100f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                home = hit.point + Vector3.up * 1.5f;

            var cc = pilot.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false; // CharacterController fights teleports
            pilot.transform.position = home;
            if (cc != null) cc.enabled = true;
            pilot.CancelMomentum(); // the fall's speed must not land WITH you

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
            // PARENT lookup, not GetComponent: every player BONE carries a
            // rigidbody+collider (ragdoll/ink), and a falling body's bones
            // enter the net before the capsule does — the junk branch below
            // was DELETING the player's skeleton ("my body vanished").
            var pilot = other.GetComponentInParent<SimpleFPSController>();
            if (pilot != null)
            {
                Catch(pilot);
                return;
            }

            // creatures die PROPERLY in the void (drops + kill credit fire —
            // the silent Destroy used to eat the death, so a shoved zombie
            // dropped nothing and made no sound). Matter and junk still just
            // vanish quietly.
            if (other.attachedRigidbody != null)
            {
                var creature = other.attachedRigidbody.GetComponentInParent<Creature>();
                var victim = creature != null ? creature.GetComponent<Damageable>() : null;
                if (victim != null)
                {
                    victim.TakeDamage(99999f, "the void");
                    return;
                }
                Destroy(other.attachedRigidbody.gameObject);
            }
        }
    }

    /// Layer 30 = INK CANVASES: flat drawable planes spanning whole facades so
    /// strokes never split at wall-module seams. Nothing collides with them
    /// (players walk through the doorways they span; particles fly through to
    /// hit the real walls) — but raycasts still hit them, which is the point:
    /// the pen sees one perfect surface.
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
