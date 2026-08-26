using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// What a mark REMEMBERS. Adding one is a single entry here - nothing else
    /// changes shape - which is what keeps this modular without being vague.
    public enum Mark
    {
        KilledBy,     // the owner that took its strength to 0
        DamagedBy,    // the owner that last hurt it, fatally or not
        Biome,        // the biome it was last standing in
    }

    /// THE WORLD'S SHORT MEMORY. One value per (owner, mark) - the LAST one,
    /// never a list: a curse asks "who finished me", and the same field answers
    /// "did I finish them" when read from the other side.
    ///
    /// A mark survives whatever left it. If the thing that killed you is long
    /// gone, you still carry who did it, because a mark is a note and not a
    /// live reference.
    ///
    /// Host-authoritative like every other number: the host writes, and clients
    /// are told (NetSync.PushMark).
    public static class Marks
    {
        struct Key
        {
            public int Owner;
            public Mark What;
        }

        class KeyCompare : IEqualityComparer<Key>
        {
            public bool Equals(Key a, Key b) => a.Owner == b.Owner && a.What == b.What;
            public int GetHashCode(Key k) => (k.Owner * 397) ^ (int)k.What;
        }

        static readonly Dictionary<Key, int> _marks = new Dictionary<Key, int>(new KeyCompare());

        /// Nothing is remembered at the start of a match.
        public static void Clear() => _marks.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            Clear();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => Clear();
        }

        /// Leave a note on `owner`. Overwrites whatever it said before, and
        /// tells everyone - a curse that only one machine agrees with is worse
        /// than no curse. Storing and telling are the same call on purpose.
        public static void Set(int owner, Mark what, int value)
        {
            if (owner < 0) return;
            SetLocal(owner, what, value);
            NetSync.PushMark(owner, what, value);
        }

        /// The host's note, applied on arrival. Does NOT tell anyone, or a
        /// received mark would echo straight back out.
        public static void SetLocal(int owner, Mark what, int value)
        {
            if (owner < 0) return;
            _marks[new Key { Owner = owner, What = what }] = value;
        }

        /// What that note says, or -1 when there is none. -1 IS the answer at
        /// match start: nobody has killed you yet.
        public static int Get(int owner, Mark what) =>
            _marks.TryGetValue(new Key { Owner = owner, What = what }, out var v) ? v : -1;

        /// "Did this one do that to me?" - the question every curse asks, in
        /// whichever direction it asks it.
        public static bool Is(int owner, Mark what, int who) =>
            who >= 0 && Get(owner, what) == who;
    }
}
