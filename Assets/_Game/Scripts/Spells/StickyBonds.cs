using UnityEngine;

namespace SpellyZombie
{
    /// THE STICKINESS LAW (Marko's system): stickiness IS bond strength —
    /// one scalar for grabbing, gluing, and building alike. The ladder is
    /// absolute:
    ///
    ///   hands < Sticky rune < Sticky lvl2 (ultimate glue) << lvl3 = TIME
    ///
    /// Lvl2 is the strongest glue that exists as a THING: it never moves on
    /// its own and anything that touches it is stuck (except the player
    /// carrying it). Lvl3 sticky is NOT a particle — it's the TIME ZONE
    /// (TimeFreezeField): absolute grip, stops anything nearby. Everything
    /// the ENVIRONMENT offers sits below that, so the strongest hold is
    /// always one the players made. SLICK is the same axis with the sign
    /// flipped: it subtracts from stickiness — bonds refuse to form, grips
    /// tear early, cargo slides off carts.
    public static class StickyBonds
    {
        // the ladder (matches Matter's stickiness range of -1..1)
        // tunable through sz_tuning.json (AXIOM: no knob locked in code)
        public static readonly float HandHold = DrawingConfig.Overlay("StickyHandHold", 0.35f);
        public static readonly float Sticky1 = DrawingConfig.Overlay("Sticky1", 0.55f);
        public static readonly float Sticky2 = DrawingConfig.Overlay("Sticky2", 0.85f);
        /// STAYS const — it is the ±1 clamp CAP, not a feel knob. Tuning it
        /// below Sticky2 would make lvl2 glue permanently unbreakable.
        public const float Sticky3 = 1.0f;
        public static readonly float EnvironmentMax = DrawingConfig.Overlay("StickyEnvironmentMax", 0.8f);

        /// Two surfaces meet: the stickier side sets the bond; any SLICK on
        /// the other side subtracts. At or below zero, no bond forms.
        public static float Effective(float a, float b)
        {
            float sticky = Mathf.Max(a, b);
            float slick = Mathf.Min(0f, Mathf.Min(a, b));
            return sticky + slick;
        }

        /// Bond strength → joint break force. Quadratic so the ladder FEELS
        /// like a ladder: hands tear on a snag, lvl2 holds a struggling
        /// zombie, lvl3 is effectively unbreakable.
        public static float BreakForce(float stickiness)
        {
            if (stickiness <= 0.05f) return 0f;
            if (stickiness >= Sticky3) return float.PositiveInfinity;
            float t = Mathf.Clamp01(stickiness);
            return Mathf.Lerp(90f, 4200f, t * t);
        }

        /// Stick two bodies together at the given effective stickiness.
        /// Null when the stickiness can't bond or they're already joined.
        public static FixedJoint Bond(Rigidbody a, Rigidbody b, float stickiness)
        {
            if (a == null || b == null || a == b) return null;
            float force = BreakForce(stickiness);
            if (force <= 0f) return null;
            foreach (var j in a.GetComponents<FixedJoint>())
                if (j.connectedBody == b) return j; // already bonded
            var joint = a.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = b;
            joint.breakForce = force;
            joint.breakTorque = force;
            return joint;
        }
    }
}
