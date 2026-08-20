namespace SpellyZombie
{
    /// Acolytes start with NO runes and earn all four by doing the thing the
    /// rune is for. Each deed is the natural next action of the one before it,
    /// so the ladder teaches the side without a tutorial.
    ///   scan an object          -> Solid  (melee zombie)
    ///   revert to first person  -> Liquid (ranged zombie)
    ///   summon any zombie       -> Arrow  (march order)
    ///   summon 2+ at once       -> Y      (scatter order)
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

        /// One call per summoning seal, with how many it raised at once.
        public static void Summoned(int owner, int count)
        {
            if (count <= 0) return;
            Grant(owner, RuneType.DirectionAway);
            if (count > 1) Grant(owner, RuneType.DirectionToward);
        }
    }
}
