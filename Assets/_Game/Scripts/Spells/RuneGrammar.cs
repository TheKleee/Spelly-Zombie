using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// GRAMMAR v4 (SPELL_PARTICLES.md, the ruleset 2026-07-19).
    ///
    /// Every rune plays exactly one ROLE, and every law operates on ROLES -
    /// no law anywhere names a rune id. That's the extensibility contract:
    /// a post-release rune ships ONE registry entry (role, opposite, paradox)
    /// plus its payload, and leveling, synthesis, lineage and the fallback
    /// substrate all work for it on day one with zero engine changes.
    public enum RuneRole
    {
        Essence,  // WHAT the effect is (burn / freeze / judge / consume)
        Form,     // the SHAPE - identity of the other ingredient is preserved
        Behavior, // HOW it acts: anchor · chaos · manifest · multiply
        Vector    // pure motion, no effects of its own, ever
    }

    /// What two OPPOSITE particles synthesize into (annihilation is repealed -
    /// opposites make PARADOX objects now; each pair declares its own).
    public enum ParadoxKind
    {
        None,
        Steam,      // hot + cold - burning steam (lvl2×lvl2 = hot steam area)
        WhiteHole,  // light + dark - pushes everything away (black hole strength, opposite sign)
        ChaosGrip,  // grip + slip - sticks to everything, cannot be controlled
        Barrier     // compress + expand - two-way isolation, the system's insulator
    }

    public class RuneDef
    {
        public RuneType Rune;
        public RuneRole Role;
        public RuneType Opposite;     // pair partner (paradox synthesis)
        public ParadoxKind Paradox;   // what this pair synthesizes (same on both halves)
        public ParticleKind Emits;    // which particle carries it (None-ish runes: State emits Matter)
        public bool Core = true;      // counts toward the Demon's all-12 completeness
        public ulong Bit;             // lineage bit - assigned at registration, never hardcode
    }

    public static class RuneGrammar
    {
        static readonly Dictionary<RuneType, RuneDef> _defs = new Dictionary<RuneType, RuneDef>();
        // paradox pairs precomputed at registration, keyed by particle FAMILY -
        // ParadoxOf used to O(defs)-scan the registry on every particle collision
        static readonly Dictionary<(ParticleKind, ParticleKind), ParadoxKind> _paradoxes =
            new Dictionary<(ParticleKind, ParticleKind), ParadoxKind>();
        static int _nextBit;

        /// All core runes' lineage bits - a chain carrying every one becomes THE DEMON.
        public static ulong CoreMask { get; private set; }

        /// New runes (post-release) call this - the whole grammar lights up for them.
        public static void Register(RuneDef def)
        {
            def.Bit = 1UL << _nextBit++;
            _defs[def.Rune] = def;
            if (def.Core) CoreMask |= def.Bit;
            if (def.Paradox != ParadoxKind.None && def.Role != RuneRole.Vector
                && _defs.TryGetValue(def.Opposite, out var opp))
            {
                var a = Family(def.Emits); var b = Family(opp.Emits);
                if (a != b) _paradoxes[(int)a <= (int)b ? (a, b) : (b, a)] = def.Paradox;
            }
        }

        static RuneGrammar()
        {
            // the core 12 - every law reads THESE fields, never the ids
            void Pair(RuneType up, RuneType down, RuneRole role, ParadoxKind paradox,
                ParticleKind upEmits, ParticleKind downEmits)
            {
                Register(new RuneDef { Rune = up, Role = role, Opposite = down, Paradox = paradox, Emits = upEmits });
                Register(new RuneDef { Rune = down, Role = role, Opposite = up, Paradox = paradox, Emits = downEmits });
            }
            Pair(RuneType.HeatUp, RuneType.HeatDown, RuneRole.Essence, ParadoxKind.Steam,
                ParticleKind.Spark, ParticleKind.Frost);
            Pair(RuneType.LuminanceUp, RuneType.LuminanceDown, RuneRole.Essence, ParadoxKind.WhiteHole,
                ParticleKind.Light, ParticleKind.Dark);
            Pair(RuneType.StickyUp, RuneType.StickyDown, RuneRole.Behavior, ParadoxKind.ChaosGrip,
                ParticleKind.Glue, ParticleKind.Repel);
            Pair(RuneType.DensityUp, RuneType.DensityDown, RuneRole.Behavior, ParadoxKind.Barrier,
                ParticleKind.Dense, ParticleKind.Spread);
            // State emits MATTER, not particles (its leveling lives in Matter - P2)
            Pair(RuneType.StateSolid, RuneType.StateLiquid, RuneRole.Form, ParadoxKind.None,
                ParticleKind.Push /*unused*/, ParticleKind.Push /*unused*/);
            // vectors: pure movers; their paradox (Arrow+Y) is undefined on purpose
            Pair(RuneType.DirectionAway, RuneType.DirectionToward, RuneRole.Vector, ParadoxKind.None,
                ParticleKind.Push, ParticleKind.Push);

                // THE EXOTICS TABLE - the cross matrix, complete (P3 built):
            RegisterExotic(ParticleKind.Frost, ParticleKind.Light, ExoticKind.Healing);      // cold light = MERCY
            RegisterExotic(ParticleKind.Spark, ParticleKind.Light, ExoticKind.LightStrike);  // hot light = JUDGMENT
            RegisterExotic(ParticleKind.Spark, ParticleKind.Dark, ExoticKind.DarkFlames);    // hungry fire
            RegisterExotic(ParticleKind.Spark, ParticleKind.Glue, ExoticKind.StickyLava);
            RegisterExotic(ParticleKind.Spark, ParticleKind.Repel, ExoticKind.FireBolts);
            RegisterExotic(ParticleKind.Frost, ParticleKind.Repel, ExoticKind.IceBolts);
            // (Obsidian blade removed Jul 22 — "beats the purpose of drawing".)
            // Its slot refilled the same day: chill and heat share one logic
            // on glue - the burn patch has a freeze twin.
            RegisterExotic(ParticleKind.Frost, ParticleKind.Glue, ExoticKind.FrostGlue); // glue that freezes
            RegisterExotic(ParticleKind.Frost, ParticleKind.Dark, ExoticKind.AbsoluteZero);
            RegisterExotic(ParticleKind.Dark, ParticleKind.Dense, ExoticKind.DarkMatter);
            RegisterExotic(ParticleKind.Light, ParticleKind.Glue, ExoticKind.StickyLight);   // the moth lure
            RegisterExotic(ParticleKind.Light, ParticleKind.Repel, ExoticKind.SlickLight);
            RegisterExotic(ParticleKind.Light, ParticleKind.Dense, ExoticKind.Multiplication);
            RegisterExotic(ParticleKind.Repel, ParticleKind.Dense, ExoticKind.TeleportPair);
            // (Glue+Dense stays substrate: heavy-glue pooling + the weld IS "literal glue")
        }

        public static RuneDef Def(RuneType r) => _defs.TryGetValue(r, out var d) ? d : null;

        // ------------------------------------------------------------ exotics --
        /// The EXOTICS TABLE (SPELL_PARTICLES.md): sparse, data-registered
        /// overrides for specific cross-family pairs - Light Strike, Healing,
        /// Teleport… The grammar computes the default; an entry here WINS.
        /// This is data, not code branches: a new rune's exotics are one
        /// RegisterExotic call each.
        public enum ExoticKind
        {
            None, Healing,
            LightStrike, DarkFlames, StickyLava, FireBolts,   // HeatUp crosses
            IceBolts, FrostGlue, AbsoluteZero,                // HeatDown crosses
            DarkMatter, StickyLight, SlickLight,              // luminance crosses
            Multiplication, TeleportPair                      // the carrier exotics
        }

        static readonly Dictionary<(ParticleKind, ParticleKind), ExoticKind> _exotics =
            new Dictionary<(ParticleKind, ParticleKind), ExoticKind>();

        public static void RegisterExotic(ParticleKind a, ParticleKind b, ExoticKind kind)
        {
            a = Family(a); b = Family(b); // lookups probe family keys - register the same way
            var key = (int)a <= (int)b ? (a, b) : (b, a);
            _exotics[key] = kind;
        }

        public static ExoticKind ExoticOf(ParticleKind a, ParticleKind b)
        {
            var fa = Family(a); var fb = Family(b);
            var key = (int)fa <= (int)fb ? (fa, fb) : (fb, fa);
            return _exotics.TryGetValue(key, out var k) ? k : ExoticKind.None;
        }

        /// Lineage bit for a rune - stamped on every particle its zone emits.
        public static ulong Bit(RuneType r) => _defs.TryGetValue(r, out var d) ? d.Bit : 0UL;

        // ---------------------------------------------------------- families --
        /// A condensed state belongs to its base FAMILY for the leveling law:
        /// Lightning IS Light at level 2, a Black hole IS Darkness at level 2.
        public static ParticleKind Family(ParticleKind k)
        {
            switch (k)
            {
                case ParticleKind.Lightning: return ParticleKind.Light;
                case ParticleKind.BlackHole: return ParticleKind.Dark;
                case ParticleKind.Flame: return ParticleKind.Spark;
                default: return k;
            }
        }

        /// What an opposite pair synthesizes (None = fall back to the substrate).
        /// A registration-time lookup - the answer is fixed the moment a pair
        /// registers, exactly like _exotics.
        public static ParadoxKind ParadoxOf(ParticleKind a, ParticleKind b)
        {
            var fa = Family(a); var fb = Family(b);
            var key = (int)fa <= (int)fb ? (fa, fb) : (fb, fa);
            return _paradoxes.TryGetValue(key, out var p) ? p : ParadoxKind.None;
        }

        // ------------------------------------------------------------- demon --
        static float _lastDemon = -999f;

        /// ALL core runes combined into one chain, any order, any route -
        /// THE DEMON appears . One at a time, with a cooldown so
        /// a lingering complete chain can't machine-gun apocalypses.
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
