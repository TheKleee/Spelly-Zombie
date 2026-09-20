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

        /// Registers every marker before a tool looks them up by name.
        public static void Touch() { }
    }
}
