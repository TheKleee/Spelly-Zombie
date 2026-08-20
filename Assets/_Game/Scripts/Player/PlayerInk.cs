using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The per-player ink pool: strokes drain it by drawn length, awards
    /// refill it.
    public class PlayerInk : MonoBehaviour
    {
        public float Ink = DrawingConfig.InkMax;

        public static readonly List<PlayerInk> All = new List<PlayerInk>();

        void Awake()
        {
            All.Add(this);
            // Player.prefab serializes Ink, so the field initializer never
            // runs there - set in Awake, as a fraction of the Perks ceiling
            Ink = DrawingConfig.InkMax * Mathf.Clamp01(DrawingConfig.StartInkFraction);
        }
        void OnDestroy() => All.Remove(this);

        SimpleFPSController _pilot;

        /// No passive regen. Wizards: the pot is the only well
        /// (CauldronEconomy.LocalWandTick). Acolytes: ink evaporates.
        void Update()
        {
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
            if (!Sides.IsAcolytePlayer(_pilot)) return;   // wizards: the pot, or nothing
            Ink = Mathf.Max(0f, Ink - DrawingConfig.AcolyteInkEvaporatePerSec * Time.deltaTime);
        }

        public float Fraction => Ink / DrawingConfig.InkMax;

        /// Spend ink for drawn line length - the lobby pays too (its cauldron
        /// refills itself forever).
        public bool TrySpend(float amount)
        {
            if (Ink < amount) return false;
            Ink -= amount;
            return true;
        }

        public void Award(float amount) => Ink = Mathf.Min(DrawingConfig.InkMax, Ink + amount);

        public static void AwardAll(float amount)
        {
            foreach (var p in All) p.Award(amount);
        }

        public static void RefillAll()
        {
            foreach (var p in All) p.Ink = DrawingConfig.InkMax;
        }
    }
}
