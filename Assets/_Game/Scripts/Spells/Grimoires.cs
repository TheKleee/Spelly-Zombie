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
    /// changing the side.
    public static class Grimoires
    {
        static readonly Dictionary<int, BookKind> _held = new Dictionary<int, BookKind>();
        static readonly Dictionary<int, float> _until = new Dictionary<int, float>();

        /// The book in this player's hands right now.
        public static BookKind HeldBy(int owner)
        {
            if (_held.TryGetValue(owner, out var g))
            {
                if (_until.TryGetValue(owner, out var t) && Time.time >= t)
                {
                    _held.Remove(owner); _until.Remove(owner);   // the curse wore off
                }
                else return g;
            }
            return Sides.IsAcolyte(owner) ? BookKind.Acolyte : BookKind.Wizard;
        }

        /// Hand someone a different book for a while. This is the curse.
        public static void Swap(int owner, BookKind book, float seconds)
        {
            _held[owner] = book;
            _until[owner] = Time.time + seconds;
        }

        public static void Restore(int owner) { _held.Remove(owner); _until.Remove(owner); }
        public static void ResetAll() { _held.Clear(); _until.Clear(); }
    }
}
