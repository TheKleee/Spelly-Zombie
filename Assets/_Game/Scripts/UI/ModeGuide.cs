using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Crossroads chips via UIPrompt.Offer. Priority: until the first
    /// grimoire open only the G lesson shows; a scan offer owns the screen
    /// alone; an open book's chips outrank these; any Show() hides every chip.
    [DefaultExecutionOrder(300)] // after every mode's own prompts
    public class ModeGuide : MonoBehaviour
    {
        SimpleFPSController _pilot;
        EmotePlayer _emotes;
        GhostState _ghost;
        static bool _ghostUpTaught, _ghostDownTaught; // the two flying keys retire once each was used

        void Update()
        {
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
            if (_pilot == null || !_pilot.IsLocalViewer) return;
            if (GameMenu.IsOpen || PoseStudio.IsOpen || LobbyStand.PanelOpen
                || HatPillar.PanelOpen) return;
            // the book, the body paint and the view keys are the living's: a body on the ground or a ghost has none of them
            if (GhostState.LocalIsGhost) { GhostKeys(); return; }
            if (_pilot.IsDowned || _pilot.IsDead) return;

            // an open mode's own prompts take priority; the guide only covers
            // the crossroads. A held pose counts as a mode.
            if (_emotes == null) _emotes = GetComponent<EmotePlayer>();
            if (SelfPaint.IsActive || PoseGrab.IsOpen || HeldWeapon.DrawMode
                || ZombieWatch.IsOpen || ShapeShift.PoseOpen || HandGrab.LocalHolding
                || (_emotes != null && _emotes.IsPosing))
                return;

            // while the pen is active the row belongs to the pen hints
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
                    UIPrompt.Offer("TAB", Loc.T("chip.third")); // third of the max 3
                }
                return;
            }

            // acolyte crossroads: third person is the disguise
            if (ShapeShift.LocalIsShaped) return; // ShapeShift's own line covers it

            bool dead = OwnsAZombie();
            if (!ShapeShift.HasStoredShape)
            {
                UIPrompt.Offer("G", Loc.T("chip.grimoire"));
                if (dead) UIPrompt.Offer("R", Loc.T("chip.watch"));
            }
            else
            {
                UIPrompt.Offer("TAB", Loc.T("chip.become"));
                if (dead) UIPrompt.Offer("R", Loc.T("chip.watch"));
                else UIPrompt.Offer("G", Loc.T("chip.grimoire"));
            }
        }

        /// A ghost's row: how to let go of what it rides, shown only while it rides something;
        /// flying free, the two keys nobody guesses (up and down), each until it was used once.
        void GhostKeys()
        {
            if (_ghost == null) _ghost = GetComponent<GhostState>();
            if (_ghost != null && _ghost.Driving)
            {
                UIPrompt.Offer("F", Loc.T("chip.release"));
                return;
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.isPressed) _ghostUpTaught = true;
                if (kb.leftCtrlKey.isPressed) _ghostDownTaught = true;
            }
            if (!_ghostUpTaught) UIPrompt.Offer("SPACE", Loc.T("chip.up"));
            if (!_ghostDownTaught) UIPrompt.Offer("CTRL", Loc.T("chip.down"));
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
