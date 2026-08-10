using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// THE WORLD TEACHES (Marko Jul 25): you don't find floating runes — you
    /// find a FLAME, a PUDDLE, a LOG, and absorb it into the grimoire. The
    /// thing you absorb IS the lesson: fire teaches Heat, water teaches Liquid,
    /// a log teaches Solid.
    ///
    /// ABSORBING CONSUMES THE SOURCE (his ruling): the torch goes out, so that
    /// one torch teaches exactly ONE player. Everyone else must find their own
    /// fire — which is what keeps kits different and forces you to combine
    /// spells instead of all owning the same ones.
    ///
    /// SPELL-BORN THINGS CAN NEVER BE ANALYZED — not "isn't currently a spell"
    /// but PROVENANCE: anything a spell created is permanently ineligible, even
    /// after the touch law turns it into an object. Otherwise conjure → touch →
    /// analyze is a free rune printer.
    ///
    /// AXIOM: everything here is yours. Put this on your torch/puddle/log
    /// prefab, pick the card it teaches, and supply your own absorb effect.
    /// Nothing about your object is altered except what you asked for.
    public class Analyzable : MonoBehaviour
    {
        [Header("WHAT THIS TEACHES")]
        [Tooltip("The ONE rune the grimoire learns from this object. One page = one rune " +
                 "(all 12 collected individually). A flame teaches HeatUp, not its opposite.")]
        public RuneType Teaches = RuneType.HeatUp;

        [Header("WHAT HAPPENS WHEN IT'S ABSORBED (your call)")]
        [Tooltip("Your effect at the moment of absorbing. Empty = the default poof.")]
        public GameObject AbsorbFx;
        [Tooltip("What's left behind: a spent torch, a dry basin. Empty = see Consume below.")]
        public GameObject Remains;
        [Tooltip("ON (his ruling): the source is used up, so only ONE player can learn from it. " +
                 "Turn OFF for a teaching object you want everyone to be able to read.")]
        public bool Consume = true;
        [Tooltip("With Consume on and no Remains prefab: destroy the whole object, or just " +
                 "switch off these renderers/lights (a torch that goes out but stays standing).")]
        public bool DestroyOnAbsorb = false;
        [Tooltip("Objects switched off instead of destroyed. Empty = this object's own renderers + lights.")]
        public GameObject[] Extinguish;

        [Header("Reach")]
        public float Range = 2.6f;

        /// Set by the spell systems on anything they create — such a thing can
        /// NEVER teach a rune, however many times it changes hands.
        [System.NonSerialized] public bool SpellBorn;

        public bool Spent { get; private set; }

        static readonly List<Analyzable> All = new List<Analyzable>();
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// The one live registry the grimoire scans — no per-object Update.
        public static IReadOnlyList<Analyzable> Living => All;

        public bool CanTeach => !Spent && !SpellBorn;

        /// Absorb into `owner`'s grimoire. Returns false when there was
        /// nothing to learn (already known, spent, or spell-born).
        public bool AbsorbInto(int ownerId)
        {
            if (!CanTeach) return false;
            if (Grimoire.HasRune(ownerId, Teaches)) return false;   // already in the book

            Grimoire.UnlockRune(ownerId, Teaches);
            Spent = true;

            if (AbsorbFx != null) Instantiate(AbsorbFx, transform.position, Quaternion.identity);
            else if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, transform.position);
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent($"the grimoire absorbs it. {Teaches} is yours");

            if (!Consume) return true;

            if (Remains != null)
            {
                Instantiate(Remains, transform.position, transform.rotation, transform.parent);
                Destroy(gameObject);
                return true;
            }
            if (DestroyOnAbsorb) { Destroy(gameObject); return true; }

            // it goes OUT but stays standing — the spent torch
            if (Extinguish != null && Extinguish.Length > 0)
            {
                foreach (var go in Extinguish) if (go != null) go.SetActive(false);
            }
            else
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r is ParticleSystemRenderer) r.gameObject.SetActive(false);
                foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = false;
            }
            return true;
        }
    }

}
