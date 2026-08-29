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
        /// MP gap (flagged): a remote acolyte's credit needs a NetSync push.
        public static void CreditWand(int ownerId, float amount)
        {
            if (ownerId < 0 || amount <= 0f || !Sides.IsAcolyte(ownerId)) return;
            if (ownerId != Grimoire.LocalPlayerId) return;
            foreach (var ink in All)
            {
                var pilot = ink.GetComponent<SimpleFPSController>();
                if (pilot == null || !pilot.IsLocalViewer) continue;
                ink.Store(Mathf.Min(amount, DrawingConfig.InkMax * 0.15f));
                return;
            }
        }

        /// No passive regen. Wizards: the pot is the only well
        /// (CauldronEconomy.LocalWandTick). Acolytes: ink evaporates, except
        /// while worn - a disguise has no wand to dry out.
        void Update()
        {
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
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
