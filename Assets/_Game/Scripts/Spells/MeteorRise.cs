using UnityEngine;

namespace SpellyZombie
{
    /// THE METEOR RISES BEFORE IT FALLS (Marko Aug 10):
    /// "meteor is not instantly created but rather heat particle is created
    /// first and then meteor falls... as solid is popping up it should hit the
    /// heat and go up in the air growing a lot and falling down (or wherever
    /// it's supposed to fall due to arrows, pull or grab/toss) => this way we
    /// have a lot more visual clarity of what's going on."
    ///
    /// The old Meteorite spawned a rock 12 metres OVERHEAD and dropped it. Two
    /// things wrong with that. It broke his Aug 9 ruling that solid no longer
    /// falls from the sky — everything erupts from the ground now. And it hid
    /// the cause: you never saw the stone meet the fire, a boulder simply
    /// appeared above you with no story.
    ///
    /// So the stone is born AT THE SEAL where you drew it, is thrown upward,
    /// SWELLS as it climbs, and then comes down under plain gravity. The whole
    /// chain is legible from the ground: draw, erupt, ignite, rise, fall.
    ///
    /// It never steers itself. Once it is airborne it is an ordinary heavy
    /// rigidbody, so an arrow shoves it, a Y pulls it back, and a grab-and-toss
    /// throws it — exactly the redirection he asked to keep working, and none
    /// of it needed writing because it is just physics.
    public class MeteorRise : MonoBehaviour
    {
        /// Final size as a multiple of the size it was born at.
        public float Grow = 3.5f;

        Vector3 _birth;
        float _t;
        bool _done;

        void Start() => _birth = transform.localScale;

        void FixedUpdate()
        {
            if (_done) return;
            if (!TryGetComponent<Rigidbody>(out var rb)) { _done = true; return; }

            // it swells only on the way UP. The apex is the moment it stops
            // being a conjure and starts being a falling rock, so that is where
            // growth ends and physics takes over completely.
            if (rb.linearVelocity.y <= 0.05f)
            {
                _done = true;
                return;
            }

            _t = Mathf.Min(1f, _t + Time.fixedDeltaTime / Mathf.Max(0.05f,
                DrawingConfig.MeteorGrowSeconds));
            transform.localScale = _birth * Mathf.Lerp(1f, Mathf.Max(1f, Grow), _t);
        }
    }
}
