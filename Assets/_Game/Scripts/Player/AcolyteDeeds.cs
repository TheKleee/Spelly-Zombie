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
        static bool IsAcolyte(int owner) => Sides.Of(owner) == Side.Acolyte;

        static void Grant(int owner, RuneType rune)
        {
            if (!IsAcolyte(owner) || Grimoire.HasRune(owner, rune)) return;
            Grimoire.UnlockRune(owner, rune); // the toast fires from in there
        }

        public static void Scanned(int owner) => Grant(owner, RuneType.StateSolid);

        public static void RevertedToSelf(int owner) => Grant(owner, RuneType.StateLiquid);

        /// One call per summoning seal.
        public static void Summoned(int owner, int count)
        {
            if (count > 0) Grant(owner, RuneType.DirectionAway);
        }

        /// ★ THE Y DEED: at least 2 of your zombies alive at the same time -
        /// across seals, not per seal. Checked after every summon lands.
        public static void ZombiesAlive(int owner)
        {
            int alive = 0;
            foreach (var z in Zombie.All)
                if (z != null && z.OwnerId == owner) alive++;
            if (alive > 1) Grant(owner, RuneType.DirectionToward);
        }
    }
}
