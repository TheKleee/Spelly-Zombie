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

        /// ONE PAGE = ONE RUNE (Marko's ruling: all 12 collected individually,
        /// no pairs). Absorbing a flame teaches HeatUp — NOT its opposite.
        /// The card set above stays as the coarse "family" record, so every
        /// older path (menu cauldron, zombie loot, powerups) keeps working:
        /// unlocking a CARD wholesale still grants both of its runes.
        static readonly Dictionary<int, HashSet<RuneType>> _runesByOwner =
            new Dictionary<int, HashSet<RuneType>>();

        public static void Unlock(int owner, RuneCardType card)
        {
            if (!_byOwner.TryGetValue(owner, out var set))
                _byOwner[owner] = set = new HashSet<RuneCardType>();
            set.Add(card);
            if (owner == LocalPlayerId)
            {
                foreach (RuneType r in System.Enum.GetValues(typeof(RuneType)))
                    if (r != RuneType.None && RuneLibrary.CardOf(r) == card)
                        SeedWriting(owner, r);
                NetSync.PushUnlock(owner, (int)card, -1); // host answers IsUnlocked truthfully (netcode §1)
            }
        }

        /// Unlock ONE rune. Its family is recorded too, so anything that asks
        /// the coarse question ("do they know any Heat?") still answers yes.
        public static void UnlockRune(int owner, RuneType rune)
        {
            if (!_runesByOwner.TryGetValue(owner, out var set))
                _runesByOwner[owner] = set = new HashSet<RuneType>();
            set.Add(rune);
            Unlock(owner, RuneLibrary.CardOf(rune));
            if (owner == LocalPlayerId)
                NetSync.PushUnlock(owner, -1, (int)rune);
        }

        /// A remote machine's unlock arriving over the wire (netcode §1).
        public static void UnlockRemote(int owner, int card, int rune)
        {
            if (rune >= 0)
            {
                if (!_runesByOwner.TryGetValue(owner, out var set))
                    _runesByOwner[owner] = set = new HashSet<RuneType>();
                set.Add((RuneType)rune);
            }
            if (card >= 0)
            {
                if (!_byOwner.TryGetValue(owner, out var cards))
                    _byOwner[owner] = cards = new HashSet<RuneCardType>();
                cards.Add((RuneCardType)card);
            }
        }

        /// Connecting swaps the machine-local instance id for the stable FishNet
        /// ClientId — carry everything already learned across (netcode §0).
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

        // ---- WRITING LEVEL — Marko's three laws, verified with him:
        //   1) it increases ONLY when you CORRECT a drawing at the grimoire
        //      (rune pages, and the seal page — the seal's ramp lives on
        //      RuneType.None),
        //   2) it represents ONLY how well the book has learned your hand —
        //      recognition help, nothing else,
        //   3) it NEVER touches the power of any rune or seal.
        // 0..1 per rune, this run, local player only. Purely a meter.
        static readonly Dictionary<(int owner, RuneType rune), float> _writing =
            new Dictionary<(int, RuneType), float>();

        /// Bumped on every real change — the book page redraws its bar off it.
        public static int WritingVersion { get; private set; }

        /// 0 fresh … 1 fully corrected-in. 0 when no ramp exists yet — this
        /// is a display meter, nothing reads it for gameplay.
        public static float WritingLevelOf(int owner, RuneType rune) =>
            _writing.TryGetValue((owner, rune), out var v) ? v : 0f;

        /// True when a ramp exists (the page shows its bar).
        public static bool WritingTracked(int owner, RuneType rune) =>
            _writing.ContainsKey((owner, rune));

        static void SeedWriting(int owner, RuneType rune)
        {
            if (_writing.ContainsKey((owner, rune))) return;
            _writing[(owner, rune)] = 0f; // minimum — the whole point
            WritingVersion++;
        }

        /// A CORRECTION happened — the only thing that moves the meter
        /// (Marko's law 1). Creates the ramp if this is the first one, so a
        /// correction always counts, free-play grounds included.
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

        /// Does this owner know THIS rune specifically?
        ///   • learned it directly            → yes
        ///   • learned its SIBLING directly   → no (that's the point of per-rune)
        ///   • has the card, learned neither  → yes (an older wholesale grant:
        ///                                      menu cauldron, zombie drop, tests)
        public static bool HasRune(int owner, RuneType rune)
        {
            var card = RuneLibrary.CardOf(rune);
            if (_runesByOwner.TryGetValue(owner, out var runes))
            {
                if (runes.Contains(rune)) return true;
                foreach (var r in runes)                    // sibling learned individually?
                    if (RuneLibrary.CardOf(r) == card) return false;
            }
            return Has(owner, card);                        // wholesale grant
        }

        public static IReadOnlyCollection<RuneType> RunesOf(int owner) =>
            _runesByOwner.TryGetValue(owner, out var set)
                ? (IReadOnlyCollection<RuneType>)set : System.Array.Empty<RuneType>();

        public static bool HasAny(int owner) =>
            _byOwner.TryGetValue(owner, out var set) && set.Count > 0;

        public static IReadOnlyCollection<RuneCardType> CardsOf(int owner) =>
            _byOwner.TryGetValue(owner, out var set) ? (IReadOnlyCollection<RuneCardType>)set : System.Array.Empty<RuneCardType>();

        /// Owner is gone (zombie died) — its knowledge dies with it.
        ///
        /// ALL THREE TABLES, not just the card set. Dropping only _byOwner left
        /// the per-rune grants behind, and HasRune answers from _runesByOwner
        /// FIRST — so a dead zombie still "knew" every rune it had been taught
        /// individually, and since RuneLibrary.IsUnlocked now asks HasRune, its
        /// knowledge did not die with it at all. The writing meters leaked the
        /// same way, one entry per (owner, rune) for the whole session.
        /// SWITCHING SIDES REBUILDS THE BOOK. Marko: converting hands you a
        /// "different grimoire mostly empty as when you start the game on that
        /// team". An acolyte's whole kit is Solid and Liquid; a wizard starts
        /// with nothing and earns everything.
        ///
        /// Deliberately NOT Drop(): that also wipes your HANDWRITING, and how you
        /// draw is yours, not your side's. Losing your hand every time you turned
        /// would be a punishment nobody asked for.
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

        /// Reused by Drop — a dictionary can't be edited while it is enumerated.
        static readonly List<(int owner, RuneType rune)> _dropScratch =
            new List<(int owner, RuneType rune)>();
    }
}
