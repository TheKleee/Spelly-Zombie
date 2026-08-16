using UnityEngine;

namespace SpellyZombie
{
    public enum WorldEventKind { Ink, Spell, Explosion, Sparkle, Death }

    /// Tiny perception bus: anything noteworthy reports itself here, and googly
    /// eyes / zombie brains READ it instead of having wires into every system.
    /// This is what makes eyes follow fresh ink and zombies gawk at explosions -
    /// and because zombies perceive through it, drawing ink somewhere IS a decoy.
    public static class WorldEvents
    {
        public struct Evt
        {
            public WorldEventKind Kind;
            public Vector3 Pos;
            public float Intensity;   // ~1 spell, ~2 death, 3+ explosion
            public float Time;
        }

        const int Cap = 24;
        static readonly Evt[] _ring = new Evt[Cap];
        static int _count, _head;

        public static Vector3 LatestInkPos { get; private set; }
        public static float LatestInkTime { get; private set; } = -999f;

        public static void Report(WorldEventKind kind, Vector3 pos, float intensity)
        {
            if (kind == WorldEventKind.Ink)
            {
                LatestInkPos = pos;
                LatestInkTime = Time.time;
                return; // ink is continuous - tracked separately, not ring-buffered
            }
            _ring[_head] = new Evt { Kind = kind, Pos = pos, Intensity = intensity, Time = Time.time };
            _head = (_head + 1) % Cap;
            if (_count < Cap) _count++;
        }

        /// Loudest event in the last `window` seconds (default weighting favors
        /// recent + loud). Returns false when the world is quiet.
        public static bool TryGetLoudest(float window, out Evt evt)
        {
            evt = default;
            float best = 0f;
            float now = Time.time;
            for (int i = 0; i < _count; i++)
            {
                float age = now - _ring[i].Time;
                if (age > window) continue;
                float score = _ring[i].Intensity * (1f - age / window);
                if (score > best) { best = score; evt = _ring[i]; }
            }
            return best > 0f;
        }

        public static bool InkIsFresh => Time.time - LatestInkTime < 1.2f;
    }
}
