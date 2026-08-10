using System.Collections.Generic;

namespace SpellyZombie
{
    /// WHICH SIDE ARE YOU ON. Two classes, both of them wizards, both of them
    /// drawing: the WIZARD casts spells, the ACOLYTE summons the dead and hides
    /// inside the furniture.
    ///
    /// This is not a fixed team. In the acolyte mode you change sides mid-round
    /// by being corrupted, so every system that cares has to ask rather than
    /// cache. In the LOBBY you just pick, and switch as often as you like, which
    /// is how anybody learns the other half of the game.
    public enum Side
    {
        Wizard = 0,
        Acolyte = 1
    }

    /// Per-player side, keyed by owner id exactly like Grimoire's rune tables, so
    /// there is one identity scheme in the project rather than two.
    ///
    /// DELIBERATELY NOT ON SimpleFPSController: that file is Marko's and off
    /// limits. Keeping the side in its own registry also means anything can ask
    /// about any player without holding a reference to them, which is what the
    /// zombies, the cauldron and the HUD all need.
    public static class Sides
    {
        /// Same id Grimoire uses, so a player's runes and their side always agree.
        public static int LocalPlayerId => Grimoire.LocalPlayerId;

        static readonly Dictionary<int, Side> _byOwner = new Dictionary<int, Side>();

        /// Unknown players are WIZARDS. That keeps the co-op mode and every
        /// existing scene behaving exactly as they do today: nothing has to opt
        /// out of being an acolyte, it has to opt in.
        public static Side Of(int owner) =>
            _byOwner.TryGetValue(owner, out var s) ? s : Side.Wizard;

        public static bool IsAcolyte(int owner) => Of(owner) == Side.Acolyte;
        public static bool IsWizard(int owner) => Of(owner) == Side.Wizard;

        public static Side Local => Of(LocalPlayerId);
        public static bool LocalIsAcolyte => IsAcolyte(LocalPlayerId);

        /// Fired AFTER the change lands, with (owner, newSide). The wand colour,
        /// the held grimoire, shape mode and the HUD all listen; nothing polls.
        public static event System.Action<int, Side> Changed;

        public static void Set(int owner, Side side)
        {
            if (Of(owner) == side) return;   // no event storm on a no-op
            _byOwner[owner] = side;
            Changed?.Invoke(owner, side);
        }

        /// Lobby convenience: flip between the two so one key can switch you.
        public static void Toggle(int owner) =>
            Set(owner, IsAcolyte(owner) ? Side.Wizard : Side.Acolyte);

        /// WHICH SIDE IS THAT BODY ON. Zombies need this to know who to leave
        /// alone, and they only have a Transform to go on.
        ///
        /// Only the local player can be resolved today: the owner id lives in
        /// Grimoire and nothing exposes a per-controller id, and SimpleFPSController
        /// is Marko's file. Anyone unresolved counts as a WIZARD, which is the safe
        /// default: a zombie still hunts them, so the worst case is the old
        /// behaviour rather than a zombie standing around ignoring an enemy.
        /// When multiplayer gets tested this is the one seam to fill in.
        public static bool IsAcolytePlayer(SimpleFPSController p)
        {
            if (p == null) return false;
            if (!p.IsLocalViewer) return false;   // remote: unknown, treat as wizard
            return LocalIsAcolyte;
        }

        /// Wipe on round start / scene change. Sides are per round, never saved:
        /// the whole point of the mode is that you might not end on the side you
        /// began on.
        public static void ResetAll() => _byOwner.Clear();

        /// Everyone currently on a side. Used by the win checks later, and by the
        /// lobby to show who picked what.
        public static int CountOn(Side side)
        {
            int n = 0;
            foreach (var kv in _byOwner) if (kv.Value == side) n++;
            return n;
        }
    }
}
