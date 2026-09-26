using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ WHICH BOOK EACH PLAYER IS HOLDING. Defaults to their side's book;
    /// the acolyte's Life curse swaps a wizard onto an acolyte book for a
    /// while and this is the one place that is recorded, so every spell
    /// lookup in the game reads the swap for free.
    ///
    /// His ruling: "the spell list is team based not meshed" - and more
    /// precisely grimoire based, because the curse changes the book without
    /// changing the side. A cursed book shows the acolyte pages and casts the
    /// acolyte spells with the runes the WIZARD has learned; the dead it
    /// raises are the curser's (his double edge).
    public static class Grimoires
    {
        static readonly Dictionary<int, BookKind> _held = new Dictionary<int, BookKind>();
        static readonly Dictionary<int, float> _until = new Dictionary<int, float>();
        static readonly Dictionary<int, int> _curser = new Dictionary<int, int>();

        /// The book in this player's hands right now.
        public static BookKind HeldBy(int owner)
        {
            if (_held.TryGetValue(owner, out var g))
            {
                if (_until.TryGetValue(owner, out var t) && Time.time >= t)
                {
                    Restore(owner); // the curse wore off
                    if (owner == Grimoire.LocalPlayerId) Juice.Sound2D(Sfx.BookClose, 1f, 1.15f); // his own book back
                }
                else return g;
            }
            return Sides.IsAcolyte(owner) ? BookKind.Acolyte : BookKind.Wizard;
        }

        /// Hand someone a different book for a while.
        public static void Swap(int owner, BookKind book, float seconds)
        {
            _held[owner] = book;
            _until[owner] = Time.time + seconds;
        }

        /// The Life curse: `by` hands `owner` their acolyte book for a while.
        public static void Curse(int owner, int by, float seconds)
        {
            // the same acolyte's second dart only lengthens it
            if (CurserOf(owner) == by && _until.TryGetValue(owner, out var end)) seconds = Mathf.Max(seconds, end - Time.time);
            Swap(owner, BookKind.Acolyte, seconds);
            _curser[owner] = by;
        }

        /// The acolyte whose book this player holds, or -1 when it is their own.
        public static int CurserOf(int owner) =>
            HeldBy(owner) == BookKind.Acolyte && _curser.TryGetValue(owner, out var by) ? by : -1;

        /// Who a summon raised by this player belongs to: the curser while cursed.
        public static int SummonerFor(int owner)
        {
            int by = CurserOf(owner);
            return by >= 0 ? by : owner;
        }

        public static void Restore(int owner) { _held.Remove(owner); _until.Remove(owner); _curser.Remove(owner); }
        public static void ResetAll() { _held.Clear(); _until.Clear(); _curser.Clear(); }
    }
}
