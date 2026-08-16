using UnityEngine;

namespace SpellyZombie
{
    /// THE TOUR GUIDE, FINAL FORM (, ruled across four
    /// corrections): TEXT chips through UIPrompt.Offer - one key and one
    /// fact per block, never more ("you can't put more information than one
    /// in one block"), few of them ("more than 1 important info... max 3 -
    /// but this is the extreme maximum"). Emoji chips were tried and retired
    /// the same day — too small at prompt size, "the field feels empty";
    /// the emoji grid lives on in the grimoire where icons render big.
    ///
    /// the sequencing laws, all enforced here or by the tier system:
    ///  · until the first grimoire open, the G lesson is the ONLY voice -
    ///    after it, G remains and R appears
    ///  · the flying F's scan moment owns the screen alone
    ///  · an open book's own chips outrank the crossroads
    ///  · any mode's own instructions (a Show) hide every chip
    [DefaultExecutionOrder(300)] // after every mode's own prompts
    public class ModeGuide : MonoBehaviour
    {
        SimpleFPSController _pilot;
        EmotePlayer _emotes;

        void Update()
        {
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
            if (_pilot == null || !_pilot.IsLocalViewer) return;
            if (GameMenu.IsOpen || PoseStudio.IsOpen || LobbyStand.PanelOpen
                || HatPillar.PanelOpen) return;

            // any real mode open = its own prompts own the screen; the guide
            // only describes the CROSSROADS, never the room you are in.
            // A HELD POSE counts: EmotePlayer's row speaks there, and both
            // voices at once printed "TAB first person" twice
            if (_emotes == null) _emotes = GetComponent<EmotePlayer>();
            if (SelfPaint.IsActive || PoseGrab.IsOpen || HeldWeapon.DrawMode
                || ZombieWatch.IsOpen || ShapeShift.PoseOpen || HandGrab.LocalHolding
                || (_emotes != null && _emotes.IsPosing))
                return;

            // PEN DOWN = a room of its own: while ink flows the crossroads is
            // noise, and the row belongs to the pen lessons (ALT precision,
            // RMB erase) until each is learned - then drawing stays silent
            if (SurfaceDrawer.IsPenActive) return;

            if (AimBadge.ScanOfferLive) return;      // the flying F's moment
            if (!GrimoirePages.TaughtOpen) return;   // first lesson stands alone
            if (GrimoirePages.BookOpen) return;      // the book speaks for itself

            bool third = SimpleFPSController.ThirdPersonActive;

            if (!Sides.IsAcolytePlayer(_pilot))
            {
                if (third)
                {
                    UIPrompt.Offer("TAB", Loc.T("chip.first"));
                    UIPrompt.Offer("R", Loc.T("chip.pose"));
                }
                else
                {
                    UIPrompt.Offer("G", Loc.T("chip.grimoire"));
                    UIPrompt.Offer("R", Loc.T("chip.paint"));
                    UIPrompt.Offer("TAB", Loc.T("chip.third")); // the max-3, used in full
                }
                return;
            }

            // THE ACOLYTE'S CROSSROADS - their third person IS the disguise
            if (ShapeShift.LocalIsShaped) return; // ShapeShift's own line covers it

            string shape = ShapeShift.StoredShapeName;
            bool dead = OwnsAZombie();
            if (shape == null)
            {
                UIPrompt.Offer("G", Loc.T("chip.grimoire"));
                if (dead) UIPrompt.Offer("R", Loc.T("chip.watch"));
            }
            else
            {
                UIPrompt.Offer("TAB", Loc.F("chip.become", shape));
                if (dead) UIPrompt.Offer("R", Loc.T("chip.watch"));
                else UIPrompt.Offer("G", Loc.T("chip.grimoire"));
            }
        }

        bool OwnsAZombie()
        {
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                var mine = z.GetComponent<SummonedZombie>();
                if (mine != null && mine.SummonedBy == Grimoire.LocalPlayerId) return true;
            }
            return false;
        }
    }
}
