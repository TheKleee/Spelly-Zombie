using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The two sides. Sides change mid-round (corruption), so systems must
    /// ask rather than cache; in the lobby you pick freely.
    public enum Side
    {
        Wizard = 0,
        Acolyte = 1
    }

    /// Per-player side registry, keyed by the same owner id as Grimoire.
    /// Its own registry so anything can ask about any player by id alone.
    public static class Sides
    {
        /// Same id Grimoire uses, so a player's runes and their side always agree.
        public static int LocalPlayerId => Grimoire.LocalPlayerId;

        // ---- STRENGTH IS HEALTH ----
        // One stat: the pool you lift, throw and slam with, and 0 = death.
        // Three layers: your OWN ceiling (side default + buffs), the BIOME's
        // ceiling, and the current value healing toward whichever is lower.

        /// A player's own ceiling before the world has a say - side default
        /// plus anything that raised it. Buffs raise this; that is what a buff IS.
        public static float OwnCapFor(int owner)
        {
            float b = Of(owner) == Side.Acolyte
                ? DrawingConfig.AcolyteMaxHealth
                : DrawingConfig.WizardMaxHealth;
            return b + BuffFor(owner);
        }

        static readonly Dictionary<int, float> _buff = new Dictionary<int, float>();

        /// Raise (or lower) a ceiling. A buff cast on an ENEMY is a real
        /// attack: a ceiling they cannot fill, and mending slows with it.
        public static void AddBuff(int owner, float amount)
        {
            _buff.TryGetValue(owner, out float had);
            _buff[owner] = had + amount;
        }

        public static float BuffFor(int owner) =>
            _buff.TryGetValue(owner, out float v) ? v : 0f;

        public static void ClearBuffs() => _buff.Clear();

        /// THE EFFECTIVE CEILING: the lower of your own and the ground's.
        /// A 90-cap acolyte in a 100 biome stays at 90 and feels strong; a
        /// 140-cap wizard there is dragged down to 100 and feels it. A biome
        /// with StrengthCap 0 has no opinion, so the natural world is unchanged.
        public static float MaxHealthFor(int owner) =>
            CapWithGround(OwnCapFor(owner), GroundCapFor(owner));

        public static float CapWithGround(float ownCap, float groundCap) =>
            groundCap > 0f ? Mathf.Min(ownCap, groundCap) : ownCap;

        /// The local biome's ceiling for this player, 0 where the ground has none.
        public static float GroundCapFor(int owner)
        {
            var b = BiomeUnder(owner);
            return b != null ? b.StrengthCap : 0f;
        }

        static Biome BiomeUnder(int owner)
        {
            if (owner != LocalPlayerId) return null; // remote bodies: host authority, see netcode
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer)
                    return SpellyMap.BiomeAt(p.transform.position);
            return null;
        }

        /// 0..1 of your effective ceiling - what every strength-scaled thing
        /// (lift, throw, slam) multiplies by. Wounded means weaker, always.
        public static float StrengthFraction(int owner, float current)
        {
            float max = MaxHealthFor(owner);
            return max <= 0f ? 0f : Mathf.Clamp01(current / max);
        }

        /// Mending speed is a FUNCTION OF THE CEILING, not of the side: the
        /// lower your max the faster you come back. So acolytes recover faster
        /// than wizards without a rule saying so, and raising a ceiling
        /// (buffing) buys power at the price of recovery.
        /// Derived from your OWN ceiling, not the ground's - buffing yourself
        /// must cost recovery even where a biome is capping you. The biome
        /// then scales the result: hostile ground mends you slower.
        public static float RegenPerSecFor(int owner)
        {
            var b = BiomeUnder(owner);
            float scale = b != null ? Mathf.Max(0f, b.RegenScale) : 1f;
            return RegenForMax(OwnCapFor(owner)) * scale;
        }

        public static float RegenForMax(float max)
        {
            float m = Mathf.Max(1f, max);
            return DrawingConfig.RegenAtRefMax
                * Mathf.Pow(DrawingConfig.RegenRefMax / m, DrawingConfig.RegenFalloff);
        }

        static readonly Dictionary<int, Side> _byOwner = new Dictionary<int, Side>();

        /// Unknown players default to Wizard.
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

        /// Side of a body. Only the local player resolves today; anyone
        /// unresolved counts as Wizard (a zombie still hunts them).
        /// THE ONE FRIEND-OR-FOE TEST. Anything with no side of its own -
        /// scenery, golems, a loose crate - is on NOBODY's team, so a spell
        /// that spares its own team still affects it. Zombies serve the
        /// acolytes, which is the only thing here that is not a player.
        public static Side? SideOfThing(GameObject go)
        {
            if (go == null) return null;
            var p = go.GetComponentInParent<SimpleFPSController>();
            if (p != null) return IsAcolytePlayer(p) ? Side.Acolyte : Side.Wizard;
            if (go.GetComponentInParent<Zombie>() != null) return Side.Acolyte;
            return null;
        }

        public static bool IsAcolytePlayer(SimpleFPSController p)
        {
            if (p == null) return false;
            if (!p.IsLocalViewer) return false;   // remote: unknown, treat as wizard
            return LocalIsAcolyte;
        }

        /// Wipe on round start / scene change - sides are per round, never saved.
        public static void ResetAll() { _byOwner.Clear(); _buff.Clear(); }

        /// How many players are on a side.
        public static int CountOn(Side side)
        {
            int n = 0;
            foreach (var kv in _byOwner) if (kv.Value == side) n++;
            return n;
        }
    }
}
