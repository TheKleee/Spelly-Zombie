using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The per-player ink pool: strokes drain it by drawn length, awards
    /// refill it.
    public class PlayerInk : MonoBehaviour
    {
        public float Ink = DrawingConfig.InkMax;

        public static readonly List<PlayerInk> All = new List<PlayerInk>();

        void Awake()
        {
            All.Add(this);
            // Player.prefab serializes Ink, so the field initializer never
            // runs there - set in Awake, as a fraction of the Perks ceiling
            Ink = DrawingConfig.InkMax * Mathf.Clamp01(DrawingConfig.StartInkFraction);
        }
        void OnDestroy() => All.Remove(this);

        SimpleFPSController _pilot;

        /// What a scan gave you that has not become wand yet. Invisible: it
        /// shows only as the wand slowly growing back once you are yourself.
        public float Reserve { get; private set; }

        /// A scan fills the RESERVE, not the wand. The wand you had stays the
        /// wand you have - the shape you wore has no wand to feed.
        public void Store(float amount) => Reserve += Mathf.Max(0f, amount);

        /// ★ THE SPELLS FEED THE WAND BACK (his rule): an acolyte's spell
        /// expiring, dealing damage, or their zombies wrecking things returns
        /// a bit of wand - counterplay against running dry. Feeds the RESERVE
        /// so the existing trickle grows the wand back, capped at full.
        /// A remote acolyte's credit travels to their machine (NetSync).
        public static void CreditWand(int ownerId, float amount)
        {
            if (ownerId < 0 || amount <= 0f || !Sides.IsAcolyte(ownerId)) return;
            if (ownerId != Grimoire.LocalPlayerId) { NetSync.SendWandCredit(ownerId, amount); return; }
            foreach (var ink in All)
            {
                var pilot = ink.GetComponent<SimpleFPSController>();
                if (pilot == null || !pilot.IsLocalViewer) continue;
                ink.Store(Mathf.Min(amount, DrawingConfig.InkMax * 0.15f));
                return;
            }
        }

        /// Acolytes drink wizard ink: each living acolyte near a living wizard
        /// takes StealRate(distance) from that wizard's wand. The wizard sees it
        /// only on the wand, thinning or refilling slower. Each machine runs its
        /// own side from the positions it already has.
        void StealTick(float dt)
        {
            if (DrawingConfig.AcolyteInkStealPerSec <= 0f || _pilot.IsDowned || GhostState.LocalIsGhost) return;
            var mine = Sides.Of(Grimoire.LocalPlayerId);
            if (mine != Side.Wizard && mine != Side.Acolyte) return;
            float r2 = DrawingConfig.AcolyteInkStealRange * DrawingConfig.AcolyteInkStealRange;
            Vector3 at = _pilot.transform.position;
            float total = 0f;
            foreach (var a in NetAvatar.All)
            {
                if (a == null || a.Downed) continue;
                var side = Sides.Of(NetSync.OwnerIdOf(a.Id));
                float d2 = (a.transform.position - at).sqrMagnitude;
                if (side == mine || d2 > r2) continue;
                if (mine == Side.Acolyte && (side != Side.Wizard || a.Wandless || a.InkFraction <= 0.01f)) continue;
                if (mine == Side.Wizard && side != Side.Acolyte) continue;
                total += StealRate(Mathf.Sqrt(d2));
            }
            if (total <= 0f) return;
            if (mine == Side.Wizard)
            {
                if (!WandState.Armed(_pilot)) return;
                Ink = Mathf.Max(0f, Ink - total * dt);
            }
            else Store(total * dt); // the reserve: in as soon as you are yourself
        }

        /// The steal at this distance: full inside AcolyteInkStealFullRange, easing
        /// down to AcolyteInkStealEdgePerSec at AcolyteInkStealRange, none beyond.
        static float StealRate(float d)
        {
            float outer = DrawingConfig.AcolyteInkStealRange;
            float inner = Mathf.Min(DrawingConfig.AcolyteInkStealFullRange, outer);
            if (d > outer) return 0f;
            if (d <= inner) return DrawingConfig.AcolyteInkStealPerSec;
            return Mathf.Lerp(DrawingConfig.AcolyteInkStealPerSec, DrawingConfig.AcolyteInkStealEdgePerSec,
                (d - inner) / Mathf.Max(0.01f, outer - inner));
        }

        /// No passive regen. Wizards: the pot is the only well
        /// (CauldronEconomy.LocalWandTick). Acolytes: ink evaporates, except
        /// while worn - a disguise has no wand to dry out.
        void Update()
        {
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
            if (_pilot != null && _pilot.IsLocalViewer) StealTick(Time.deltaTime);
            if (!Sides.IsAcolytePlayer(_pilot)) return;   // wizards: the pot, or nothing

            float dt = Time.deltaTime;
            bool shaped = ShapeShift.LocalIsShaped;

            // WHILE WORN: nothing moves. No evaporation, no refill - you are
            // an object, and objects do not hold wands.
            if (shaped) return;

            // BACK IN YOUR BODY: the reserve bleeds into the wand until the
            // wand is full, and the moment it IS full whatever is left is
            // thrown away - a scan you did not spend is a scan you wasted.
            if (Reserve > 0f)
            {
                if (Ink >= DrawingConfig.InkMax - 0.01f) Reserve = 0f;
                else
                {
                    float move = Mathf.Min(Reserve,
                        DrawingConfig.ReserveFlowPerSec * dt,
                        DrawingConfig.InkMax - Ink);
                    Ink += move;
                    Reserve -= move;
                    return;   // refilling holds evaporation off
                }
            }

            Ink = Mathf.Max(0f, Ink - DrawingConfig.AcolyteInkEvaporatePerSec * dt);
        }

        public float Fraction => Ink / DrawingConfig.InkMax;

        /// The share of a drawing charge this wand pays, and gets back when the ink returns.
        public float DrawRate
        {
            get
            {
                if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
                return Sides.IsAcolytePlayer(_pilot) ? DrawingConfig.AcolyteDrawCostMul : 1f;
            }
        }

        /// Rune Studio is the practice hall: drawing there never costs ink
        /// (his ask, Aug 26) and the wand stays full.
        public static bool Bottomless => RuneLibrary.PracticeHall;

        /// Spend ink for drawn line length - the lobby pays too (its cauldron
        /// refills itself forever).
        public bool TrySpend(float amount)
        {
            if (Bottomless) { Ink = DrawingConfig.InkMax; return true; }
            if (Ink < amount) return false;
            Ink -= amount;
            return true;
        }

        public void Award(float amount) => Ink = Mathf.Min(DrawingConfig.InkMax, Ink + amount);

        /// The evaporation ink dried some of the wand.
        public void Evaporate(float amount) => Ink = Mathf.Max(0f, Ink - Mathf.Max(0f, amount));

        public static void AwardAll(float amount)
        {
            foreach (var p in All) p.Award(amount);
        }

        public static void RefillAll()
        {
            foreach (var p in All) p.Ink = DrawingConfig.InkMax;
        }
    }
}
