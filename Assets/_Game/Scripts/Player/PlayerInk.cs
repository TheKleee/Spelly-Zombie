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

        void Awake() => All.Add(this);
        void OnDestroy() => All.Remove(this);

        // slow passive regen — kills are the FAST refill, but you can never be
        // stranded with an empty well and no way to fight for more
        void Update()
        {
            Award(DrawingConfig.InkRegenPerSec * Time.deltaTime); // same rules in the lobby
        }

        // the Drawing perk deepens the well (Perks.InkMax) — no perk = old value
        public float Fraction => Ink / Perks.InkMax;

        /// Spend ink for drawn line length. THE LOBBY IS NOT A FREE RIDE any
        /// more (Marko: "the game should not be played differently than normal
        /// just cause it's lobby — same rules should apply"). Ink used to cost
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
