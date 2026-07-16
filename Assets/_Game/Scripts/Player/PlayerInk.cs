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
            if (RoundDirector.RunActive) Award(DrawingConfig.InkRegenPerSec * Time.deltaTime);
        }

        // the Drawing perk deepens the well (Perks.InkMax) — no perk = old value
        public float Fraction => Ink / Perks.InkMax;

        /// Spend ink for drawn line length. Free when no run is active.
        public bool TrySpend(float amount)
        {
            if (!RoundDirector.RunActive) return true;
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
