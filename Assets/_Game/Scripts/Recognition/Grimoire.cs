using System.Collections.Generic;

namespace SpellyZombie
{
    /// Who knows which runes, keyed by owner id (players and zombies alike;
    /// instance id of their GameObject). A seal is owned by whoever drew on it
    /// last and casts with that owner's cards.
    public static class Grimoire
    {
        /// The local player's owner id (set by SimpleFPSController on Awake).
        public static int LocalPlayerId;

        /// The coarse family record ("do they know any Heat?"). Never granted
        /// directly any more - it rides along under UnlockRune.
        static readonly Dictionary<int, HashSet<RuneCardType>> _byOwner =
            new Dictionary<int, HashSet<RuneCardType>>();

        /// Per-rune unlocks: absorbing a flame teaches HeatUp, not its opposite.
        static readonly Dictionary<int, HashSet<RuneType>> _runesByOwner =
            new Dictionary<int, HashSet<RuneType>>();

        /// ★ THE ONE GATE. Unlock ONE rune: records it, stamps its family,
        /// seeds the writing meter, replicates, toasts once.
        public static void UnlockRune(int owner, RuneType rune)
        {
            if (!_runesByOwner.TryGetValue(owner, out var set))
                _runesByOwner[owner] = set = new HashSet<RuneType>();
            bool fresh = set.Add(rune); // re-announces must not re-celebrate
            if (!_byOwner.TryGetValue(owner, out var cards))
                _byOwner[owner] = cards = new HashSet<RuneCardType>();
            cards.Add(RuneLibrary.CardOf(rune));
            if (owner == LocalPlayerId)
            {
                SeedWriting(owner, rune);
                NetSync.PushUnlock(owner, -1, (int)rune);
                if (fresh)
                {
                    RuneToast.Show(rune);
                    Achievements.RuneLearned(RuneCount(owner));
                }
            }
            // a HOST-side grant for a remote owner (summon deeds run in host
            // code): relay it, or the earner never learns what they earned
            else if (fresh) NetSync.PushUnlockFor(owner, (int)rune);
        }

        /// A remote machine's unlock arriving over the wire (netcode §1).
        public static void UnlockRemote(int owner, int card, int rune)
        {
            if (rune >= 0)
            {
                if (!_runesByOwner.TryGetValue(owner, out var set))
                    _runesByOwner[owner] = set = new HashSet<RuneType>();
                if (set.Add((RuneType)rune) && owner == LocalPlayerId) Achievements.RuneLearned(RuneCount(owner));
                // the family record rides along, same as a local unlock
                if (!_byOwner.TryGetValue(owner, out var fams))
                    _byOwner[owner] = fams = new HashSet<RuneCardType>();
                fams.Add(RuneLibrary.CardOf((RuneType)rune));
            }
            if (card >= 0)
            {
                if (!_byOwner.TryGetValue(owner, out var cards))
                    _byOwner[owner] = cards = new HashSet<RuneCardType>();
                cards.Add((RuneCardType)card);
            }
        }

        /// Connecting swaps the machine-local instance id for the stable FishNet
        /// ClientId - carry everything already learned across (netcode §0).
        public static void Rekey(int oldId, int newId)
        {
            if (oldId == newId) return;
            if (_byOwner.TryGetValue(oldId, out var cards))
            {
                _byOwner.Remove(oldId);
                _byOwner[newId] = cards;
            }
            if (_runesByOwner.TryGetValue(oldId, out var runes))
            {
                _runesByOwner.Remove(oldId);
                _runesByOwner[newId] = runes;
            }
            _dropScratch.Clear();
            foreach (var key in _writing.Keys)
                if (key.owner == oldId) _dropScratch.Add(key);
            foreach (var key in _dropScratch)
            {
                float v = _writing[key];
                _writing.Remove(key);
                _writing[(newId, key.rune)] = v;
            }
            _dropScratch.Clear();
        }

        // ---- writing level: 0..1 per rune, this run, local player only.
        // Moves only on a correction at the grimoire (the seal's ramp lives on
        // RuneType.None); recognition help only, never touches rune power.
        static readonly Dictionary<(int owner, RuneType rune), float> _writing =
            new Dictionary<(int, RuneType), float>();

        /// Bumped on every real change - the book page redraws its bar off it.
        public static int WritingVersion { get; private set; }

        /// 0 fresh … 1 fully corrected-in. 0 when no ramp exists yet - this
        /// is a display meter, nothing reads it for gameplay.
        public static float WritingLevelOf(int owner, RuneType rune) =>
            _writing.TryGetValue((owner, rune), out var v) ? v : 0f;

        /// True when a ramp exists (the page shows its bar).
        public static bool WritingTracked(int owner, RuneType rune) =>
            _writing.ContainsKey((owner, rune));

        static void SeedWriting(int owner, RuneType rune)
        {
            if (_writing.ContainsKey((owner, rune))) return;
            _writing[(owner, rune)] = 0f;
            WritingVersion++;
        }

        /// A correction is the only thing that moves the meter. Creates the
        /// ramp on first use, so a correction always counts.
        public static void BumpWriting(int owner, RuneType rune, float amount)
        {
            SeedWriting(owner, rune);
            float v = _writing[(owner, rune)];
            if (v >= 1f) return;
            _writing[(owner, rune)] = System.Math.Min(1f, v + amount);
            WritingVersion++;
        }

        public static bool Has(int owner, RuneCardType card) =>
            _byOwner.TryGetValue(owner, out var set) && set.Contains(card);

        /// One question, one set. The wholesale card bridge is gone - a card
        /// grant writes real runes now, so knowing a rune MEANS it is in here.
        public static bool HasRune(int owner, RuneType rune) =>
            _runesByOwner.TryGetValue(owner, out var runes) && runes.Contains(rune);

        public static IReadOnlyCollection<RuneType> RunesOf(int owner) =>
            _runesByOwner.TryGetValue(owner, out var set)
                ? (IReadOnlyCollection<RuneType>)set : System.Array.Empty<RuneType>();

        public static int RuneCount(int owner)
        {
            if (!_runesByOwner.TryGetValue(owner, out var set)) return 0;
            int n = 0;
            foreach (var r in set) if (r != RuneType.None) n++;
            return n;
        }

        public static bool HasAny(int owner) =>
            _byOwner.TryGetValue(owner, out var set) && set.Count > 0;

        public static IReadOnlyCollection<RuneCardType> CardsOf(int owner) =>
            _byOwner.TryGetValue(owner, out var set) ? (IReadOnlyCollection<RuneCardType>)set : System.Array.Empty<RuneCardType>();

        /// Rebuild the owner's book to exactly this kit (side switch).
        /// Deliberately not Drop(): the writing meters survive - handwriting
        /// belongs to the player, not the side.
        public static void SetKit(int owner, params RuneType[] runes)
        {
            _byOwner.Remove(owner);
            _runesByOwner.Remove(owner);
            if (runes != null)
                foreach (var r in runes) UnlockRune(owner, r);
        }

        public static void Drop(int owner)
        {
            _byOwner.Remove(owner);
            _runesByOwner.Remove(owner);
            _dropScratch.Clear();
            foreach (var key in _writing.Keys)
                if (key.owner == owner) _dropScratch.Add(key);
            if (_dropScratch.Count == 0) return;
            foreach (var key in _dropScratch) _writing.Remove(key);
            _dropScratch.Clear();
            WritingVersion++;
        }

        /// Reused by Drop - a dictionary can't be edited while it is enumerated.
        static readonly List<(int owner, RuneType rune)> _dropScratch =
            new List<(int owner, RuneType rune)>();
    }
}
