using UnityEngine;

namespace SpellyZombie
{
    /// The rune layer of the threshold engine (Spells V2). A rune is a push
    /// on the shared payload axes (SpellPayload.Of) - there are no roles, no
    /// paradox pairs and no exotic tables any more: what combinations become
    /// is decided by SpellTable's threshold regions, never by rune identity.
    /// What survives here: lineage bits (the demon ritual counts all 12) and
    /// the demon itself.
    public static class RuneGrammar
    {
        /// All core runes' lineage bits - a chain carrying every one becomes
        /// the Demon.
        public static ulong CoreMask { get; private set; }

        static readonly ulong[] _bits = new ulong[32];

        static RuneGrammar()
        {
            int next = 0;
            foreach (RuneType r in System.Enum.GetValues(typeof(RuneType)))
            {
                if (r == RuneType.None) continue;
                int i = (int)r;
                if (i < 0 || i >= _bits.Length) continue;
                _bits[i] = 1UL << next++;
                CoreMask |= _bits[i];
            }
        }

        /// Lineage bit for a rune - stamped on every particle its zone emits.
        public static ulong Bit(RuneType r)
        {
            int i = (int)r;
            return i > 0 && i < _bits.Length ? _bits[i] : 0UL;
        }

        // ------------------------------------------------------------- demon --
        static float _lastDemon = -999f;

        /// THE TEAM RITUAL: all twelve runes in one chain summons the Demon.
        /// Multiple wizards feed it - one wizard's ink cannot reach it - and
        /// the completed-grimoire wizard closes the seal. Never told to players.
        public static bool TryDemon(ulong lineage, Vector3 at, float srcSize)
        {
            if ((lineage & CoreMask) != CoreMask) return false;
            if (Time.time - _lastDemon < DrawingConfig.DemonCooldown) return false;
            if (Object.FindAnyObjectByType<Demon>() != null) return false;
            _lastDemon = Time.time;
            DrawingWorld.Instance?.LogEvent("ALL TWELVE COMBINED. THE DEMON COMES");
            Juice.Boom(at, 1.5f);
            Demon.SummonGrand(at + Vector3.up * 0.5f, Mathf.Max(1.5f, srcSize));
            return true;
        }
    }
}
