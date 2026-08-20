using UnityEngine;

namespace SpellyZombie
{
    /// Stickiness is bond strength: one scalar for grabbing, gluing, building.
    /// Ladder: hands < Sticky rune < Sticky lvl2 << lvl3 (the TimeFreezeField).
    /// Slick is the same axis negative: bonds refuse to form, grips tear early.
    public static class StickyBonds
    {
        // the ladder (matches Matter's stickiness range of -1..1)
        // tunable through sz_tuning.json
        public static readonly float Sticky1 = DrawingConfig.Overlay("Sticky1", 0.55f);
        public static readonly float Sticky2 = DrawingConfig.Overlay("Sticky2", 0.85f);
        /// Const on purpose: the ±1 clamp cap, not a feel knob. Below Sticky2
        /// it would make lvl2 glue permanently unbreakable.
        public const float Sticky3 = 1.0f;

        /// Bond strength -> joint break force; quadratic. Lvl3 is unbreakable.
        public static float BreakForce(float stickiness)
        {
            if (stickiness <= 0.05f) return 0f;
            if (stickiness >= Sticky3) return float.PositiveInfinity;
            float t = Mathf.Clamp01(stickiness);
            return Mathf.Lerp(90f, 4200f, t * t);
        }
    }
}
