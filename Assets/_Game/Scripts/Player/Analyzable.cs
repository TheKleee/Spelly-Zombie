using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The four world sources and what they teach, in order, one per absorb.
    public enum AbsorbKind { Single, Flame, Puddle, Mud, Rock }

    /// A world object the grimoire absorbs to learn a rune. Absorbing
    /// consumes the source, so one source teaches exactly one player.
    /// Spell-born things are permanently ineligible (provenance, not current
    /// form) - otherwise conjure-then-analyze is a free rune printer.
    public class Analyzable : MonoBehaviour
    {
        [Header("WHAT THIS TEACHES")]
        [Tooltip("The ONE rune the grimoire learns from this object. One page = one rune " +
                 "(all 12 collected individually). A flame teaches HeatUp, not its opposite.")]
        public RuneType Teaches = RuneType.HeatUp;

        [Tooltip("Single teaches the one rune above. The four world sources teach three " +
                 "runes each in a fixed order, one per absorb: Flame = Heat, Light, Expand. " +
                 "Puddle = Liquid, Chill, Slick. Mud = Liquid, Sticky, Solid. " +
                 "Rock = Solid, Dark, Compress. In the lobby a source returns after 3s.")]
        public AbsorbKind Kind = AbsorbKind.Single;

        [Header("WHAT HAPPENS WHEN IT'S ABSORBED (your call)")]
        [Tooltip("Your effect at the moment of absorbing. Empty = the default poof.")]
        public GameObject AbsorbFx;
        [Tooltip("What's left behind: a spent torch, a dry basin. Empty = see Consume below.")]
        public GameObject Remains;
        [Tooltip("ON : the source is used up, so only ONE player can learn from it. " +
                 "Turn OFF for a teaching object you want everyone to be able to read.")]
        public bool Consume = true;
        [Tooltip("With Consume on and no Remains prefab: destroy the whole object, or just " +
                 "switch off these renderers/lights (a torch that goes out but stays standing).")]
        public bool DestroyOnAbsorb = false;
        [Tooltip("Objects switched off instead of destroyed. Empty = this object's own renderers + lights.")]
        public GameObject[] Extinguish;

        [Header("Reach")]
        public float Range = 2.6f;

        /// Set by the spell systems on anything they create - such a thing can
        /// NEVER teach a rune, however many times it changes hands.
        [System.NonSerialized] public bool SpellBorn;

        public bool Spent { get; private set; }

        static readonly List<Analyzable> All = new List<Analyzable>();
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// The one live registry the grimoire scans - no per-object Update.
        public static IReadOnlyList<Analyzable> Living => All;

        public bool CanTeach => !Spent && !SpellBorn;

        static readonly RuneType[] FlameOrder = { RuneType.HeatUp, RuneType.LuminanceUp, RuneType.DensityDown };
        static readonly RuneType[] PuddleOrder = { RuneType.StateLiquid, RuneType.HeatDown, RuneType.StickyDown };
        static readonly RuneType[] MudOrder = { RuneType.StateLiquid, RuneType.StickyUp, RuneType.StateSolid };
        static readonly RuneType[] RockOrder = { RuneType.StateSolid, RuneType.LuminanceDown, RuneType.DensityUp };

        RuneType[] Order()
        {
            switch (Kind)
            {
                case AbsorbKind.Flame: return FlameOrder;
                case AbsorbKind.Puddle: return PuddleOrder;
                case AbsorbKind.Mud: return MudOrder;
                case AbsorbKind.Rock: return RockOrder;
                default: return null;
            }
        }

        /// The rune this source would grant `owner` next; None = nothing left.
        public RuneType NextFor(int ownerId)
        {
            if (!CanTeach) return RuneType.None;
            var order = Order();
            if (order == null)
                return Grimoire.HasRune(ownerId, Teaches) ? RuneType.None : Teaches;
            foreach (var r in order)
                if (!Grimoire.HasRune(ownerId, r)) return r;
            return RuneType.None;
        }

        /// Absorb into `owner`'s grimoire. Returns false when there was
        /// nothing to learn (already known, spent, or spell-born).
        public bool AbsorbInto(int ownerId)
        {
            var rune = NextFor(ownerId);
            if (rune == RuneType.None) return false;

            Grimoire.UnlockRune(ownerId, rune);
            Spent = true;

            if (AbsorbFx != null) Instantiate(AbsorbFx, transform.position, Quaternion.identity);
            else if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, transform.position);
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent($"the grimoire absorbs it. {rune} is yours");
            NetSync.SendAbsorb(this);

            Depart();
            return true;
        }

        /// A remote player absorbed this: the world effect only, no rune here.
        public void VanishRemote()
        {
            if (Spent) return;
            Spent = true;
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, transform.position);
            Depart();
        }

        readonly List<GameObject> _hidObjects = new List<GameObject>();
        readonly List<Renderer> _hidRenderers = new List<Renderer>();
        readonly List<Light> _hidLights = new List<Light>();
        readonly List<Collider> _hidColliders = new List<Collider>();

        void Depart()
        {
            if (!Consume) { Spent = false; return; } // a teaching object everyone can read

            // the lobby is the training yard: sources always come back there,
            // so Remains/Destroy only apply on real maps
            bool lobby = ActiveScene.Name == "Lobby";
            if (!lobby && Remains != null)
            {
                Instantiate(Remains, transform.position, transform.rotation, transform.parent);
                Destroy(gameObject);
                return;
            }
            if (!lobby && DestroyOnAbsorb) { Destroy(gameObject); return; }

            Vanish();
            if (lobby) Invoke(nameof(Respawn), 3f);
        }

        void Vanish()
        {
            _hidObjects.Clear(); _hidRenderers.Clear(); _hidLights.Clear(); _hidColliders.Clear();

            if (Extinguish != null && Extinguish.Length > 0)
            {
                foreach (var go in Extinguish)
                    if (go != null && go.activeSelf) { go.SetActive(false); _hidObjects.Add(go); }
                return;
            }

            if (Kind == AbsorbKind.Single)
            {
                // it goes OUT but stays standing - the spent torch
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r is ParticleSystemRenderer && r.gameObject.activeSelf)
                    { r.gameObject.SetActive(false); _hidObjects.Add(r.gameObject); }
                foreach (var l in GetComponentsInChildren<Light>(true))
                    if (l.enabled) { l.enabled = false; _hidLights.Add(l); }
                return;
            }

            // a world source vanishes whole: look, light and collision
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; _hidRenderers.Add(r); }
            foreach (var l in GetComponentsInChildren<Light>(true))
                if (l.enabled) { l.enabled = false; _hidLights.Add(l); }
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (c.enabled) { c.enabled = false; _hidColliders.Add(c); }
        }

        void Respawn()
        {
            foreach (var go in _hidObjects) if (go != null) go.SetActive(true);
            foreach (var r in _hidRenderers) if (r != null) r.enabled = true;
            foreach (var l in _hidLights) if (l != null) l.enabled = true;
            foreach (var c in _hidColliders) if (c != null) c.enabled = true;
            _hidObjects.Clear(); _hidRenderers.Clear(); _hidLights.Clear(); _hidColliders.Clear();
            Spent = false;
        }
    }
}
