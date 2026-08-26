using UnityEngine;

namespace SpellyZombie
{
    /// ★ ONE BEAT FOR THE WHOLE WORLD. Every recurring effect hangs off this
    /// instead of counting down on its own, so everything manifests under the
    /// same law rather than each thing keeping private time.
    ///
    /// It runs ALWAYS - in the lobby too, where the match timer simply is not
    /// shown. The match clock cannot be the source: it only advances during
    /// the live phase, so anything hung on it would stop existing in the lobby.
    ///
    /// Why it matters beyond tidiness: a hundred flames each running their own
    /// countdown fired at a hundred different moments, drifting apart from the
    /// first frame, and no two machines ever agreed on when a beat landed.
    /// A period plus a phase read off the same clock is the same answer
    /// everywhere, computed rather than remembered.
    public static class WorldClock
    {
        /// Seconds since this scene loaded. Always advancing, no driver
        /// object, nothing to keep in sync.
        public static double Now => Time.timeSinceLevelLoadAsDouble;

        /// Which beat of `period` we are in right now, for a thing whose phase
        /// is `key`.
        ///
        /// The phase is DERIVED from the key rather than from whenever the
        /// thing happened to be born. Two consequences, both wanted: a
        /// thousand particles on one period do not all sweep the world on the
        /// same frame, and the spread is computed, so it is identical on every
        /// machine instead of depending on spawn order.
        public static long BeatOf(float period, int key)
        {
            if (period <= 0.0001f) return (long)(Now * 10000.0);
            double phase = (key & 1023) / 1024.0 * period;
            return (long)((Now + phase) / period);
        }

        /// True once per period, on the frame it comes round. `last` is this
        /// thing's own memory of the beat it acted on - pass a field.
        public static bool IsBeat(float period, int key, ref long last)
        {
            long n = BeatOf(period, key);
            if (n == last) return false;
            last = n;
            return true;
        }

        /// How long one beat of `period` actually was for this caller. Effects
        /// that scale with time use this instead of Time.deltaTime, so a beat
        /// delivers the same amount however the frame rate wandered.
        public static float Span(float period) => period;
    }
}
