using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Every sound the game asks for by name. The first WorldCount are made by
    /// the host's sim and replayed on clients (their place here is their wire
    /// id, so new ones go at the END of their group). The wire id is one byte
    /// and only 229..254 are free: the world group holds 26 and NOT ONE MORE
    /// (WireFits below stops the build if it grows); a sound every machine
    /// plays by itself belongs in the second group.
    public enum Sfx : byte
    {
        Explosion, MagicBurst, ThrownObjectHitting, CombineSpells,
        HeatImpact, ChillImpact, LightImpact, DarkImpact, CompressImpact, ExpandImpact,
        StickyImpact, SlickImpact, SolidImpact, LiquidImpact, AttractImpact, RepelImpact,
        BreakStone, BreakBone, BreakGlass,
        ZombieGrowl,
        GolemCharge, GolemHit, Lightning,
        GolemCroak, ZombieAttack, ZombieBite,
        // every machine works these out by itself
        BreakWood, BreakBottle, ZombieGroan,
        Wham, Door, Chest,
        BookClose, PageFlip, Idea,
        PotDiscovered, PotFromTheSky, PotTurningAcolyte, PotTurningWizard,
        Death, Revival, Egg, InkPop1, InkPop2, Jump, Land,
        JoinedLobby, LeftLobby, YouWin, YouLose, MatchStart,
        RuneComplete, SealComplete,
        StepRock, StepSnow, StepWood, StepGrass,
        UiHover, UiReady, UiError, UiClick, Whistle,
        GhostOut, GhostTake, Warning,
        AcolyteTransform, AcolyteBack,
        GolemStep, WandDry,
    }

    /// ★ HIS SOUNDS, one slot each (Assets/_Game/Sound/AudioLibrary, hooked from
    /// the FX Library's Sounds slot). An empty slot keeps the synthesised
    /// placeholder Juice had before. A spell sounds like its numbers: each axis
    /// has an impact and an area loop per direction.
    public class AudioLibrary : ScriptableObject
    {
        public const int WorldCount = 26;
        // does not compile when the last world sound would pass wire id 254 (255 means "nothing")
        const byte WireFits = (byte)(254 - (FxLibrary.SndClips + WorldCount - 1));

        public static AudioLibrary I => FxLibrary.I != null ? FxLibrary.I.Sounds : null;

        [Header("HITS AND BLASTS")]
        public AudioClip Explosion;
        public AudioClip MagicBurst;
        public AudioClip Wham;                 // with every WHAM and POW
        public AudioClip ThrownObjectHitting;
        public AudioClip CombineSpells;        // two things merging in the world

        [Header("BREAKING: by what the thing is made of")]
        public AudioClip BreakWood;
        public AudioClip BreakStone;            // stone, earth, metal; a golem crumbling, a zombie digging out
        public AudioClip BreakBone;             // bone and flesh; a zombie falling apart
        public AudioClip BreakGlass;            // ice and diamond; anything frozen shattering
        public AudioClip BreakBottle;           // no material of its own: for a prop's own Break Sound slot

        [Header("SPELLS: the hit")]
        public AudioClip HeatImpact;
        public AudioClip ChillImpact;
        public AudioClip LightImpact;
        public AudioClip DarkImpact;
        public AudioClip CompressImpact;
        public AudioClip ExpandImpact;
        public AudioClip StickyImpact;
        public AudioClip SlickImpact;
        public AudioClip SolidImpact;
        public AudioClip LiquidImpact;
        public AudioClip AttractImpact;
        public AudioClip RepelImpact;

        [Header("SPELLS: inside the area (loops)")]
        public AudioClip HeatArea;
        public AudioClip ChillArea;
        public AudioClip LightArea;
        public AudioClip DarkArea;
        public AudioClip CompressArea;
        public AudioClip ExpandArea;
        public AudioClip StickyArea;
        public AudioClip SlickArea;
        public AudioClip SolidArea;
        public AudioClip LiquidArea;
        public AudioClip AttractArea;
        public AudioClip RepelArea;

        [Header("DRAWING AND SEALS")]
        public AudioClip PencilLoop;
        public AudioClip ErasingLoop;
        public AudioClip RuneComplete;
        public AudioClip SealComplete;         // a seal closing, and a rune or spell unlocking
        public AudioClip InkPop1;              // ink added
        public AudioClip InkPop2;              // ink removed
        public AudioClip WandDry;              // the pen is pressed and there is no ink to draw with

        [Header("THE BOOK")]
        public AudioClip BookClose;
        public AudioClip PageFlip;
        public AudioClip Idea;                 // spare: an unlock sounds like Seal Complete, this only stands in while that slot is empty

        [Header("THE CAULDRON")]
        public AudioClip BubblesLoop;
        public AudioClip PotDiscovered;
        public AudioClip PotFromTheSky;
        public AudioClip PotTurningAcolyte;
        public AudioClip PotTurningWizard;

        [Header("BODIES (a step list holds any number of takes, one is picked per step)")]
        public AudioClip[] StepRock;            // the plain step: floors, stone, anything not below
        public AudioClip[] StepSnow;
        public AudioClip[] StepWood;
        public AudioClip[] StepGrass;
        public AudioClip Jump;
        public AudioClip Land;
        public AudioClip Death;
        public AudioClip Revival;
        public AudioClip Whistle;              // an acolyte's zombie popped; the owner tells on themselves

        [Header("ACOLYTES")]
        public AudioClip AcolyteTransform;      // the disguise goes on
        public AudioClip AcolyteBack;           // back to self, with the exit cloud

        [Header("GOLEMS AND THE SKY")]
        public AudioClip GolemCharge;           // the run of a golem's charge, as long as the run itself
        public AudioClip GolemHit;              // the charge lands
        public AudioClip GolemCroak;            // the tell before a charge: it is about to come; a boss croaks lowest
        public AudioClip GolemStep;             // one heavy step, by the body's size
        public AudioClip Lightning;             // a bolt strikes

        [Header("ZOMBIES AND GHOSTS")]
        public AudioClip[] ZombieGroans;        // one is picked for every speech bubble
        public AudioClip ZombieGrowl;           // climbing out of the ground
        public AudioClip ZombieAttack;          // winding up a charge
        public AudioClip ZombieBite;            // a swipe lands
        public AudioClip GhostOut;              // a soul leaves its body
        public AudioClip GhostTake;             // a ghost takes something over, or lets it go

        [Header("THE WORLD")]
        public AudioClip Door;
        public AudioClip Chest;
        public AudioClip Egg;

        [Header("MATCH AND LOBBY")]
        public AudioClip MatchStart;
        public AudioClip Warning;               // one minute left
        public AudioClip YouWin;
        public AudioClip YouLose;
        public AudioClip JoinedLobby;
        public AudioClip LeftLobby;

        [Header("UI")]
        public AudioClip UiHover;
        public AudioClip UiClick;
        public AudioClip UiReady;
        public AudioClip UiError;

        public AudioClip Clip(Sfx id)
        {
            switch (id)
            {
                case Sfx.Explosion: return Explosion;
                case Sfx.MagicBurst: return MagicBurst;
                case Sfx.Wham: return Wham;
                case Sfx.ThrownObjectHitting: return ThrownObjectHitting;
                case Sfx.Door: return Door;
                case Sfx.Chest: return Chest;
                case Sfx.CombineSpells: return CombineSpells;
                case Sfx.HeatImpact: return HeatImpact;
                case Sfx.ChillImpact: return ChillImpact;
                case Sfx.LightImpact: return LightImpact;
                case Sfx.DarkImpact: return DarkImpact;
                case Sfx.CompressImpact: return CompressImpact;
                case Sfx.ExpandImpact: return ExpandImpact;
                case Sfx.StickyImpact: return StickyImpact;
                case Sfx.SlickImpact: return SlickImpact;
                case Sfx.SolidImpact: return SolidImpact;
                case Sfx.LiquidImpact: return LiquidImpact;
                case Sfx.AttractImpact: return AttractImpact;
                case Sfx.RepelImpact: return RepelImpact;
                case Sfx.BreakWood: return BreakWood;
                case Sfx.BreakStone: return BreakStone;
                case Sfx.BreakBone: return BreakBone;
                case Sfx.BreakGlass: return BreakGlass;
                case Sfx.BreakBottle: return BreakBottle;
                case Sfx.ZombieGroan: return Pick(ZombieGroans);
                case Sfx.ZombieGrowl: return ZombieGrowl;
                case Sfx.GolemCharge: return GolemCharge;
                case Sfx.GolemHit: return GolemHit;
                case Sfx.Lightning: return Lightning;
                case Sfx.GolemCroak: return GolemCroak;
                case Sfx.ZombieAttack: return ZombieAttack;
                case Sfx.ZombieBite: return ZombieBite;
                case Sfx.GolemStep: return GolemStep;
                case Sfx.WandDry: return WandDry;
                case Sfx.BookClose: return BookClose;
                case Sfx.PageFlip: return PageFlip;
                case Sfx.Idea: return Idea;
                case Sfx.PotDiscovered: return PotDiscovered;
                case Sfx.PotFromTheSky: return PotFromTheSky;
                case Sfx.PotTurningAcolyte: return PotTurningAcolyte;
                case Sfx.PotTurningWizard: return PotTurningWizard;
                case Sfx.Death: return Death;
                case Sfx.Revival: return Revival;
                case Sfx.Egg: return Egg;
                case Sfx.InkPop1: return InkPop1;
                case Sfx.InkPop2: return InkPop2;
                case Sfx.Jump: return Jump;
                case Sfx.Land: return Land;
                case Sfx.JoinedLobby: return JoinedLobby;
                case Sfx.LeftLobby: return LeftLobby;
                case Sfx.YouWin: return YouWin;
                case Sfx.YouLose: return YouLose;
                case Sfx.MatchStart: return MatchStart;
                case Sfx.RuneComplete: return RuneComplete;
                case Sfx.SealComplete: return SealComplete;
                case Sfx.StepRock: return Pick(StepRock);
                case Sfx.StepSnow: return Pick(StepSnow);
                case Sfx.StepWood: return Pick(StepWood);
                case Sfx.StepGrass: return Pick(StepGrass);
                case Sfx.UiHover: return UiHover;
                case Sfx.UiReady: return UiReady;
                case Sfx.UiError: return UiError;
                case Sfx.UiClick: return UiClick;
                case Sfx.Whistle: return Whistle;
                case Sfx.GhostOut: return GhostOut;
                case Sfx.GhostTake: return GhostTake;
                case Sfx.Warning: return Warning;
                case Sfx.AcolyteTransform: return AcolyteTransform;
                case Sfx.AcolyteBack: return AcolyteBack;
            }
            return null;
        }

        /// One take out of a list, never the same one twice in a row.
        static AudioClip Pick(AudioClip[] takes)
        {
            if (takes == null || takes.Length == 0) return null;
            int i = Random.Range(0, takes.Length);
            if (takes.Length > 1 && takes[i] == _lastPick) i = (i + 1) % takes.Length;
            _lastPick = takes[i];
            return takes[i];
        }
        static AudioClip _lastPick;

        /// What breaking sounds like, by material.
        public static Sfx BreakOf(SurfaceMaterialType material)
        {
            switch (material)
            {
                case SurfaceMaterialType.Stone:
                case SurfaceMaterialType.Earth:
                case SurfaceMaterialType.Metal:
                case SurfaceMaterialType.Coal:
                case SurfaceMaterialType.Gold: return Sfx.BreakStone;
                case SurfaceMaterialType.Bone:
                case SurfaceMaterialType.Flesh: return Sfx.BreakBone;
                case SurfaceMaterialType.Water:     // solid water is ice
                case SurfaceMaterialType.Diamond: return Sfx.BreakGlass;
                case SurfaceMaterialType.Slime: return Sfx.StickyImpact;
                default: return Sfx.BreakWood;
            }
        }

        /// The six axes a spell can be heard on (SpellPayload's first six) and the
        /// sound of each direction: + first, - second.
        public const int SoundAxes = 6;

        public static Sfx ImpactOf(int axis, bool up)
        {
            switch (axis)
            {
                case 0: return up ? Sfx.HeatImpact : Sfx.ChillImpact;
                case 1: return up ? Sfx.LightImpact : Sfx.DarkImpact;
                case 2: return up ? Sfx.CompressImpact : Sfx.ExpandImpact;
                case 3: return up ? Sfx.StickyImpact : Sfx.SlickImpact;
                case 4: return up ? Sfx.SolidImpact : Sfx.LiquidImpact;
                default: return up ? Sfx.AttractImpact : Sfx.RepelImpact;
            }
        }

        public AudioClip AreaOf(int axis, bool up)
        {
            switch (axis)
            {
                case 0: return up ? HeatArea : ChillArea;
                case 1: return up ? LightArea : DarkArea;
                case 2: return up ? CompressArea : ExpandArea;
                case 3: return up ? StickyArea : SlickArea;
                case 4: return up ? SolidArea : LiquidArea;
                default: return up ? AttractArea : RepelArea;
            }
        }

        /// Every empty slot, by name. Reflection, so new slots are never missed.
        public List<string> Missing()
        {
            var missing = new List<string>();
            foreach (var f in typeof(AudioLibrary).GetFields())
            {
                if (f.FieldType == typeof(AudioClip)) { if (f.GetValue(this) == null) missing.Add(f.Name); }
                else if (f.FieldType == typeof(AudioClip[]))
                {
                    var takes = f.GetValue(this) as AudioClip[];
                    if (takes == null || takes.Length == 0 || System.Array.IndexOf(takes, null) >= 0) missing.Add(f.Name);
                }
            }
            return missing;
        }
    }
}
