using Unity.Profiling;

namespace SpellyZombie
{
    /// Named timers around the game's own heavy loops. They show in Unity's
    /// Profiler, and the Frame Report and the Stress Test read them.
    public static class PerfMarkers
    {
        public static readonly ProfilerMarker ElementTurns = new ProfilerMarker("SZ Element Turns");
        public static readonly ProfilerMarker GolemBrains = new ProfilerMarker("SZ Golem Brains");
        public static readonly ProfilerMarker GolemBirths = new ProfilerMarker("SZ Golem Births");
        public static readonly ProfilerMarker Impacts = new ProfilerMarker("SZ Impacts");
        public static readonly ProfilerMarker Logs = new ProfilerMarker("SZ Log Lines");
        // the per-frame loops of the things an army is made of
        public static readonly ProfilerMarker UpdSpells = new ProfilerMarker("SZ Upd Spells");
        public static readonly ProfilerMarker UpdMatter = new ProfilerMarker("SZ Upd Matter");
        public static readonly ProfilerMarker UpdBlobs = new ProfilerMarker("SZ Upd Blobs");
        public static readonly ProfilerMarker UpdGolems = new ProfilerMarker("SZ Upd Golems");
        public static readonly ProfilerMarker UpdEyes = new ProfilerMarker("SZ Upd Eyes");
        public static readonly ProfilerMarker UpdBodies = new ProfilerMarker("SZ Upd Bodies");
        public static readonly ProfilerMarker UpdCreatures = new ProfilerMarker("SZ Upd Creatures");
        public static readonly ProfilerMarker UpdNet = new ProfilerMarker("SZ Upd Net");
        public static readonly ProfilerMarker UpdMotes = new ProfilerMarker("SZ Upd Motes");
        // what gets made new (the engine only says "Instantiate")
        public static readonly ProfilerMarker NewEffect = new ProfilerMarker("SZ New Effect");
        public static readonly ProfilerMarker NewMatter = new ProfilerMarker("SZ New Matter");
        public static readonly ProfilerMarker NewSpellLook = new ProfilerMarker("SZ New Spell Look");
        public static readonly ProfilerMarker NewAreaLook = new ProfilerMarker("SZ New Area Look");
        public static readonly ProfilerMarker NewFlames = new ProfilerMarker("SZ New Flames");
        public static readonly ProfilerMarker NewSplit = new ProfilerMarker("SZ New Split");
        public static readonly ProfilerMarker SpellStays = new ProfilerMarker("SZ Spell Stays");
        public static readonly ProfilerMarker SpShape = new ProfilerMarker("SZ Spell Shape");
        public static readonly ProfilerMarker SpDrift = new ProfilerMarker("SZ Spell Drift");
        public static readonly ProfilerMarker SpAura = new ProfilerMarker("SZ Spell Aura");
        public static readonly ProfilerMarker SpPull = new ProfilerMarker("SZ Spell Pull");
        public static readonly ProfilerMarker SpLure = new ProfilerMarker("SZ Spell Lure");
        // the parts of one element turn
        public static readonly ProfilerMarker TurnDrift = new ProfilerMarker("SZ Turn Drift");
        public static readonly ProfilerMarker TurnBody = new ProfilerMarker("SZ Turn Bear+Axes");
        public static readonly ProfilerMarker TurnInfluence = new ProfilerMarker("SZ Turn Influence");
        public static readonly ProfilerMarker TurnLook = new ProfilerMarker("SZ Turn Look");
        public static readonly ProfilerMarker TurnSpread = new ProfilerMarker("SZ Turn Spread");

        /// Registers every marker before a tool looks them up by name.
        public static void Touch() { }
    }
}
