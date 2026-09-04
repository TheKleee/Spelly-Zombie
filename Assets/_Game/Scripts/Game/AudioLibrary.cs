using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// One clip per rune property change; twelve clips cover every spell and
    /// combination. Empty slots fall back to Juice's synthesised placeholders.
    /// Spelly Zombie -> Audio -> What Needs A Sound lists the empty slots.
    public class AudioLibrary : MonoBehaviour
    {
        public static AudioLibrary I { get; private set; }

        // ---- property ladder: one clip per direction ----
        [Header("HEAT")]
        public AudioClip HeatUp;          // spark, flame, lava, anything warming
        public AudioClip HeatDown;        // frost, glacier, absolute zero

        [Header("LUMINANCE")]
        public AudioClip LightUp;         // light, lightning, light strike
        public AudioClip LightDown;       // dark, black hole, blinding

        [Header("DENSITY")]
        public AudioClip DenseUp;         // heavier, compressed, sinking
        public AudioClip DenseDown;       // thinner, spreading, floating

        [Header("STICKINESS")]
        public AudioClip StickyUp;        // glue, tar, gripping
        public AudioClip StickyDown;      // slick, soap, slipping

        [Header("STATE")]
        public AudioClip Solidify;        // liquid or gas becoming solid
        public AudioClip Liquefy;         // solid becoming liquid, melting

        [Header("DIRECTION")]
        public AudioClip Push;            // the arrow
        public AudioClip Pull;            // the Y

        // ---- moments that are not property changes ----
        [Header("THE PEN")]
        public AudioClip PenDraw;         // looping while ink flows
        public AudioClip PenErase;
        public AudioClip InkOut;          // the wand runs dry
        public AudioClip WandGrow;
        public AudioClip WandShrink;

        [Header("SEALS")]
        public AudioClip SealClosed;      // the loop completes
        public AudioClip SealResolved;    // it casts
        public AudioClip SealBroken;

        [Header("ZOMBIES")]
        public AudioClip ZombieSpawn;
        public AudioClip ZombieDeath;
        public AudioClip ZombieAttack;
        public AudioClip ZombieDetonate;
        public AudioClip ZombieIdle;      // the mumble

        [Header("THE ACOLYTE")]
        public AudioClip Scan;            // becoming an object
        public AudioClip Unshape;         // back to yourself
        public AudioClip GasTick;         // choking in the corruption
        public AudioClip AcolyteWhistle;  // their zombie popped; the owner tells on themselves

        [Header("THE CAULDRON")]
        public AudioClip PotRefill;       // your wand drinking
        public AudioClip PotCorrupt;
        public AudioClip PotDefuse;

        [Header("BODIES")]
        public AudioClip PlayerHurt;
        public AudioClip PlayerDown;
        public AudioClip Shove;           // caught in a blast

        void OnEnable() { I = this; }
        void OnDisable() { if (I == this) I = null; }

        /// The clip for a rune's property change, or null to let the synth
        /// placeholder answer.
        public AudioClip ForRune(RuneType r)
        {
            switch (r)
            {
                case RuneType.HeatUp: return HeatUp;
                case RuneType.HeatDown: return HeatDown;
                case RuneType.LuminanceUp: return LightUp;
                case RuneType.LuminanceDown: return LightDown;
                case RuneType.DensityUp: return DenseUp;
                case RuneType.DensityDown: return DenseDown;
                case RuneType.StickyUp: return StickyUp;
                case RuneType.StickyDown: return StickyDown;
                case RuneType.StateSolid: return Solidify;
                case RuneType.StateLiquid: return Liquefy;
                case RuneType.Attract: return Push;
                case RuneType.Repel: return Pull;
            }
            return null;
        }

        /// Every empty slot, by name. Reflection, so new slots are never missed.
        public List<string> Missing()
        {
            var missing = new List<string>();
            foreach (var f in typeof(AudioLibrary).GetFields())
            {
                if (f.FieldType != typeof(AudioClip)) continue;
                if (f.GetValue(this) == null) missing.Add(f.Name);
            }
            return missing;
        }
    }
}
