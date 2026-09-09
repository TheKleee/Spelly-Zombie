using UnityEngine;

namespace SpellyZombie
{
    /// Acolytes start with NO runes and earn all four by doing the thing the
    /// rune is for - his ladder, ruled Aug 25:
    ///   scan an object             -> Solid  (melee zombie)
    ///   turn back to your own body -> Liquid (ranged zombie)
    ///   summon at least one        -> Arrow  (march order)
    ///   2+ of yours alive at once  -> Y      (scatter order)
    /// Callers just report the deed; this decides what it grants.
    public static class AcolyteDeeds
    {
        /// The mischief deeds, numbered 1..8. Each unlocks one existing glyph.
        public enum Deed
        {
            Decoy = 1, Reveal, DeathNeedle, LifeNeedle,
            Evaporation, Transformation, Aggressive, Spreading
        }

        /// owner, rune, world position of the deed. Fires only on a fresh grant.
        public static event System.Action<int, RuneType, Vector3> Granted;

        static bool IsAcolyte(int owner) => Sides.Of(owner) == Side.Acolyte;

        static bool Grant(int owner, RuneType rune, Vector3 at)
        {
            if (!IsAcolyte(owner) || Grimoire.HasRune(owner, rune)) return false;
            Granted?.Invoke(owner, rune, at); // the placed tell first, then the book
            Grimoire.UnlockRune(owner, rune);  // the toast fires from in there
            return true;
        }

        public static RuneType RuneFor(Deed d)
        {
            switch (d)
            {
                case Deed.Decoy: return RuneType.HeatUp;
                case Deed.Reveal: return RuneType.HeatDown;
                case Deed.DeathNeedle: return RuneType.StickyUp;
                case Deed.LifeNeedle: return RuneType.StickyDown;
                case Deed.Evaporation: return RuneType.LuminanceUp;
                case Deed.Transformation: return RuneType.LuminanceDown;
                case Deed.Aggressive: return RuneType.DensityUp;
                case Deed.Spreading: return RuneType.DensityDown;
                default: return RuneType.None;
            }
        }

        /// The rune granted, or None when the owner is no acolyte or owns it already.
        public static RuneType Grant(int owner, Deed deed, Vector3 at = default)
        {
            var rune = RuneFor(deed);
            return Grant(owner, rune, at) ? rune : RuneType.None;
        }

        public static void Scanned(int owner, Vector3 at) => Grant(owner, RuneType.StateSolid, at);

        public static void RevertedToSelf(int owner, Vector3 at) => Grant(owner, RuneType.StateLiquid, at);

        /// One call per summoning seal.
        public static void Summoned(int owner, int count, Vector3 at)
        {
            if (count > 0) Grant(owner, RuneType.Attract, at);
        }

        /// ★ THE Y DEED: at least 2 of your zombies alive at the same time -
        /// across seals, not per seal. Checked after every summon lands.
        public static void ZombiesAlive(int owner, Vector3 at)
        {
            int alive = 0;
            foreach (var z in Zombie.All)
                if (z != null && z.OwnerId == owner) alive++;
            if (alive > 1) Grant(owner, RuneType.Repel, at);
        }
    }
}
