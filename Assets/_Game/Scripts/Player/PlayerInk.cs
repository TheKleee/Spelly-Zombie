using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The round economy: ink is life. Strokes drain it by drawn length, kills
    /// refill it (shared — co-op, not competition), intermissions trickle.
    /// Outside an active run drawing is free (sandbox/testing).
    /// This is the draw-fast-vs-draw-well tension: a sloppy seal wastes ink AND
    /// casts worse.
    public class PlayerInk : MonoBehaviour
    {
        public float Ink = DrawingConfig.InkMax;

        public static readonly List<PlayerInk> All = new List<PlayerInk>();

        void Awake()
        {
            All.Add(this);
            // EVERYONE STARTS WANDLESS, LOBBY INCLUDED (Marko Aug 10: "lobby is
            // initial preparation for the gameplay so the rule should apply in
            // lobby as well" — the same law as his earlier "the game should not
            // be played differently just cause it's lobby").
            //
            // Assigned HERE rather than by editing his prefab: Player.prefab
            // serializes Ink: 100, so the field initializer above never runs for
            // the real player. Setting it in Awake beats the bake and keeps the
            // value a tunable knob instead of buried in an asset.
            Ink = DrawingConfig.StartInk;
        }
        void OnDestroy() => All.Remove(this);

        SimpleFPSController _pilot;

        /// ⛔ NO FREE INK FOR ANYONE (Marko Aug 10). This used to Award() every
        /// frame, unconditionally, to every player of both sides — a SECOND
        /// passive regen sitting on top of CauldronEconomy's distance-scaled
        /// one. WandState's own header has said since the day it was written
        /// that it "kills passive regen" once the real map lands; this is that.
        ///
        /// His rule, split by side:
        ///   WIZARD  — nothing passive at all. The pot is the only well, and
        ///             CauldronEconomy.LocalWandTick already pours it.
        ///   ACOLYTE — ink EVAPORATES. Corrupt ink does not keep, which is what
        ///             forces them to keep moving and keep scanning.
        void Update()
        {
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
            if (!Sides.IsAcolytePlayer(_pilot)) return;   // wizards: the pot, or nothing
            Ink = Mathf.Max(0f, Ink - DrawingConfig.AcolyteInkEvaporatePerSec * Time.deltaTime);
        }

        // the Drawing perk deepens the well (Perks.InkMax) — no perk = old value
        public float Fraction => Ink / Perks.InkMax;

        /// Spend ink for drawn line length. THE LOBBY IS NOT A FREE RIDE any
        /// more (Marko: "the game should not be played differently than normal
        /// just cause it's lobby - same rules should apply"). Ink used to cost
        /// nothing outside a run, which is why the wand never shrank there.
        /// The Lobby cauldron refills itself forever, so you can still test.
        public bool TrySpend(float amount)
        {
            if (Ink < amount) return false;
            Ink -= amount;
            return true;
        }

        public void Award(float amount) => Ink = Mathf.Min(Perks.InkMax, Ink + amount);

        public static void AwardAll(float amount)
        {
            foreach (var p in All) p.Award(amount);
        }

        public static void RefillAll()
        {
            foreach (var p in All) p.Ink = Perks.InkMax;
        }
    }
}
