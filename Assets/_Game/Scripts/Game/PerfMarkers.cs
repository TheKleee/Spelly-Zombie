using Unity.Profiling;

namespace SpellyZombie
{
    /// Named timers around the game's own heavy loops. They show in Unity's
    /// Profiler, and the Frame Report and the Stress Test read them. Each one
    /// also tells the FreezeWatch where the main thread is, so a freeze can be
    /// placed afterwards.
    public static class PerfMarkers
    {
        /// A profiler marker that leaves a trail: `using (X.Auto())` as before.
        public readonly struct Mark
        {
            readonly ProfilerMarker _marker;
            readonly string _name;
            public Mark(string name) { _marker = new ProfilerMarker(name); _name = name; }
            public Scope Auto() => new Scope(_marker, _name);

            public readonly struct Scope : System.IDisposable
            {
                readonly ProfilerMarker _marker;
                internal Scope(ProfilerMarker marker, string name)
                {
                    _marker = marker;
                    marker.Begin();
                    FreezeWatch.Enter(name);
                }
                public void Dispose()
                {
                    FreezeWatch.Leave();
                    _marker.End();
                }
            }
        }

        public static readonly Mark ElementTurns = new Mark("SZ Element Turns");
        public static readonly Mark GolemBrains = new Mark("SZ Golem Brains");
        public static readonly Mark GolemBirths = new Mark("SZ Golem Births");
        public static readonly Mark Impacts = new Mark("SZ Impacts");
        public static readonly Mark Logs = new Mark("SZ Log Lines");
        // the per-frame loops of the things an army is made of
        public static readonly Mark UpdSpells = new Mark("SZ Upd Spells");
        public static readonly Mark UpdMatter = new Mark("SZ Upd Matter");
        public static readonly Mark UpdBlobs = new Mark("SZ Upd Blobs");
        public static readonly Mark UpdGolems = new Mark("SZ Upd Golems");
        public static readonly Mark UpdEyes = new Mark("SZ Upd Eyes");
        public static readonly Mark UpdBodies = new Mark("SZ Upd Bodies");
        public static readonly Mark UpdCreatures = new Mark("SZ Upd Creatures");
        public static readonly Mark UpdNet = new Mark("SZ Upd Net");
        public static readonly Mark UpdMotes = new Mark("SZ Upd Motes");
        // what gets made new (the engine only says "Instantiate")
        public static readonly Mark NewEffect = new Mark("SZ New Effect");
        public static readonly Mark NewMatter = new Mark("SZ New Matter");
        public static readonly Mark NewSpellLook = new Mark("SZ New Spell Look");
        public static readonly Mark NewAreaLook = new Mark("SZ New Area Look");
        public static readonly Mark NewFlames = new Mark("SZ New Flames");
        public static readonly Mark NewSplit = new Mark("SZ New Split");
        public static readonly Mark SpellStays = new Mark("SZ Spell Stays");
        public static readonly Mark SpShape = new Mark("SZ Spell Shape");
        public static readonly Mark SpDrift = new Mark("SZ Spell Drift");
        public static readonly Mark SpAura = new Mark("SZ Spell Aura");
        public static readonly Mark SpPull = new Mark("SZ Spell Pull");
        public static readonly Mark SpLure = new Mark("SZ Spell Lure");
        // the parts of one element turn
        public static readonly Mark TurnDrift = new Mark("SZ Turn Drift");
        public static readonly Mark TurnBody = new Mark("SZ Turn Bear+Axes");
        public static readonly Mark TurnInfluence = new Mark("SZ Turn Influence");
        public static readonly Mark TurnLook = new Mark("SZ Turn Look");
        public static readonly Mark TurnSpread = new Mark("SZ Turn Spread");

        /// Registers every marker before a tool looks them up by name.
        public static void Touch() { }
    }
}
