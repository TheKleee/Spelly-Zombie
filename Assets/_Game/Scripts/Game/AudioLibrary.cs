using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE SOUND OF A PROPERTY CHANGING, NOT THE SOUND OF A SPELL (
    /// "each increase in something can have its own sound => we don't need a
    /// sound for all spells as they all just change some parameters and those
    /// parameters can create sounds").
    ///
    /// This is the audio half of the master law - state is DERIVED from
    /// properties, so audio is too. Twelve clips cover the entire grammar
    /// instead of one per spell, and combinations layer themselves for free:
    /// Steam plays heat-up and heat-down together because that is exactly what
    /// Steam IS. A new rune pair invented next year needs no new audio.
    ///
    /// the CLIPS WIN, ALWAYS. Every slot is empty by default and the game keeps
    /// using Juice's synthesised placeholders until drops a file in - nothing
    /// ever goes silent waiting for art. Drop this component on one object in
    /// the scene (or let PrefabVault load an "AudioLibrary" prefab) and fill in
    /// whatever you have.
    ///
    /// USE `Spelly Zombie  Audio  What Needs A Sound` to print the checklist
    /// of every slot still empty — that is the "find what we're missing" tool.
    public class AudioLibrary : MonoBehaviour
    {
        public static AudioLibrary I { get; private set; }

        // ---- THE PROPERTY LADDER: one per direction, and that is the whole
        // spell system covered ----------------------------------------------
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

        // ---- THE MOMENTS THAT ARE NOT PROPERTY CHANGES ---------------------
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
        /// placeholder answer. ONE lookup covers every spell that rune appears
        /// in, including combinations nobody has invented yet.
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
                case RuneType.DirectionAway: return Push;
                case RuneType.DirectionToward: return Pull;
            }
            return null;
        }

        /// EVERY SLOT STILL EMPTY, by name. Reflection rather than a hand-kept
        /// list, so a slot added later cannot be forgotten off the checklist.
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
