using System.Collections.Generic;
using UnityEngine;

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

        /// A fresh rune of the LOCAL player - fired after the toast.
        public static event System.Action<int, RuneType> Unlocked;

        /// ★ THE ONE GATE. Unlock ONE rune: records it, stamps its family,
        /// seeds the writing meter, replicates, toasts once.
        /// at: where the deed happened; every other machine poofs there.
        /// quiet: a starting kit, no toast and no book flip. Every machine
        /// sets kits itself, so a quiet grant is never relayed.
        /// The lobby cheat filled this player's book (SideBootstrap.UnlockWholeBook): no achievement
        /// counts runes until the match start resets the books.
        public static bool Cheated;

        public static void UnlockRune(int owner, RuneType rune, Vector3? at = null, bool quiet = false)
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
                NetSync.PushUnlock(owner, -1, (int)rune, fresh ? at : null);
                if (fresh)
                {
                    if (!quiet) RuneToast.Show(rune);
                    if (!Cheated) Achievements.RuneLearned(RuneCount(owner));
                    if (!quiet) Unlocked?.Invoke(owner, rune);
                }
            }
            // a HOST-side grant for a remote owner (summon deeds run in host
            // code): relay it, or the earner never learns what they earned
            else if (fresh && !quiet)
            {
                NetSync.PushUnlockFor(owner, (int)rune, at);
                if (at.HasValue) UnlockMark.PoofAt(at.Value); // this machine is a bystander here
            }
        }

        /// A remote machine's unlock arriving over the wire (netcode §1), with the side it was
        /// earned on: one for the book they are not holding here goes on its shelf.
        public static void UnlockRemote(int owner, int card, int rune, Side side)
        {
            if (side != Sides.Of(owner))
            {
                if (rune >= 0) Shelf(owner, side).Add((RuneType)rune);
                return;
            }
            UnlockRemote(owner, card, rune);
        }

        public static void UnlockRemote(int owner, int card, int rune)
        {
            if (rune >= 0)
            {
                if (!_runesByOwner.TryGetValue(owner, out var set))
                    _runesByOwner[owner] = set = new HashSet<RuneType>();
                if (set.Add((RuneType)rune) && owner == LocalPlayerId && !Cheated) Achievements.RuneLearned(RuneCount(owner));
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
            foreach (Side side in System.Enum.GetValues(typeof(Side)))
                if (_shelf.TryGetValue((oldId, side), out var kept))
                {
                    _shelf.Remove((oldId, side));
                    _shelf[(newId, side)] = kept;
                }
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

        /// Rebuild the owner's book to exactly this kit.
        /// Deliberately not Drop(): the writing meters survive - handwriting
        /// belongs to the player, not the side.
        public static void SetKit(int owner, params RuneType[] runes)
        {
            // on the record when it costs the local player something: who asked for the book to start over
            // (a client's book was seen starting over after a revive, and nothing in the log said why)
            if (owner == LocalPlayerId && RuneCount(owner) > (runes != null ? runes.Length : 0))
                Debug.Log($"[SpellyZombie] your book starts over: {RuneCount(owner)} runes down to the kit of {(runes != null ? runes.Length : 0)}. Asked by:\n"
                    + new System.Diagnostics.StackTrace(1, false));
            Replace(owner, runes);
        }

        static void Replace(int owner, IEnumerable<RuneType> runes)
        {
            _byOwner.Remove(owner);
            _runesByOwner.Remove(owner);
            if (runes != null)
                foreach (var r in runes) UnlockRune(owner, r, quiet: true);
        }

        // ---- the lobby keeps both books: what was learned on a side waits while the other is played
        static readonly Dictionary<(int owner, Side side), HashSet<RuneType>> _shelf =
            new Dictionary<(int, Side), HashSet<RuneType>>();

        static HashSet<RuneType> Shelf(int owner, Side side)
        {
            if (!_shelf.TryGetValue((owner, side), out var kept))
                _shelf[(owner, side)] = kept = new HashSet<RuneType>();
            return kept;
        }

        /// Every rune this machine knows of, with whose it is and the side it was earned on:
        /// the books in hand and the ones on the shelf (the host's welcome to a joiner).
        public static void EachRune(System.Action<int, Side, RuneType> visit)
        {
            foreach (var kv in _runesByOwner)
            {
                var side = Sides.Of(kv.Key);
                foreach (var r in kv.Value) if (r != RuneType.None) visit(kv.Key, side, r);
            }
            foreach (var kv in _shelf)
                foreach (var r in kv.Value) if (r != RuneType.None) visit(kv.Key.owner, kv.Key.side, r);
        }

        /// A side switch in the lobby: the book being left goes on its shelf as it is, and the
        /// other side's comes down as it was left, on top of that side's kit.
        public static void SwitchBook(int owner, Side from, Side to, RuneType[] kit)
        {
            var leaving = Shelf(owner, from);
            leaving.Clear();
            foreach (var r in RunesOf(owner)) leaving.Add(r);

            var book = new List<RuneType>();
            if (kit != null) book.AddRange(kit);
            if (_shelf.TryGetValue((owner, to), out var kept))
            {
                foreach (var r in kept)
                    if (r != RuneType.None && !book.Contains(r)) book.Add(r);
                _shelf.Remove((owner, to));
            }
            Replace(owner, book);
        }

        /// A match starts every player's book over: each keeps only the kit
        /// their side opens with, and the writing meters start fresh. The
        /// local owner announces theirs; the others are set silently, the
        /// wire fills them in as they earn.
        public static void ResetForMatch(IEnumerable<int> owners, System.Func<int, RuneType[]> kitOf)
        {
            _shelf.Clear(); // the book left on the shelf starts over too
            Cheated = false;
            _writing.Clear();
            WritingVersion++;
            foreach (var owner in new List<int>(owners))
            {
                if (owner == LocalPlayerId) { SetKit(owner, kitOf(owner)); continue; }
                _byOwner.Remove(owner);
                _runesByOwner.Remove(owner);
                foreach (var r in kitOf(owner)) UnlockRemote(owner, -1, (int)r);
            }
        }

        public static void Drop(int owner)
        {
            _byOwner.Remove(owner);
            _runesByOwner.Remove(owner);
            foreach (Side side in System.Enum.GetValues(typeof(Side))) _shelf.Remove((owner, side));
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
