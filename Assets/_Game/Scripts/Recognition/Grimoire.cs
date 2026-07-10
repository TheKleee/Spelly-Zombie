using System.Collections.Generic;

namespace SpellyZombie
{
    /// Who knows which runes. EVERY caster has an entry — players and zombies
    /// alike — keyed by owner id (instance id of their GameObject). A seal is
    /// owned by whoever drew on it LAST (they "completed" it), and it casts with
    /// THAT owner's cards. This is what makes zombie-completed spells use the
    /// zombie's runes, and why baiting a zombie into circling your rune is a way
    /// to test what cards it holds before deciding to hunt it.
    public static class Grimoire
    {
        /// The local player's owner id (set by SimpleFPSController on Awake).
        public static int LocalPlayerId;

        static readonly Dictionary<int, HashSet<RuneCardType>> _byOwner =
            new Dictionary<int, HashSet<RuneCardType>>();

        public static void Unlock(int owner, RuneCardType card)
        {
            if (!_byOwner.TryGetValue(owner, out var set))
                _byOwner[owner] = set = new HashSet<RuneCardType>();
            set.Add(card);
        }

        public static bool Has(int owner, RuneCardType card) =>
            _byOwner.TryGetValue(owner, out var set) && set.Contains(card);

        public static bool HasAny(int owner) =>
            _byOwner.TryGetValue(owner, out var set) && set.Count > 0;

        public static IReadOnlyCollection<RuneCardType> CardsOf(int owner) =>
            _byOwner.TryGetValue(owner, out var set) ? (IReadOnlyCollection<RuneCardType>)set : System.Array.Empty<RuneCardType>();

        /// Owner is gone (zombie died) — its knowledge dies with it.
        public static void Drop(int owner) => _byOwner.Remove(owner);
    }
}
