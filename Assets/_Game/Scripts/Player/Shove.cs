using UnityEngine;

namespace SpellyZombie
{
    /// A BLOW BIG ENOUGH BREAKS YOUR CONCENTRATION (Marko Aug 10: "when the
    /// force is strong enough it should push you out of the draw mode into the
    /// first person mode").
    ///
    /// His controller drops `_shove` to zero while a draw or easel mode is open
    /// — the easel is an anchor, or the body slides out from under a detached
    /// camera. That is right for the gentle stuff, and it is why a detonation in
    /// your face moved you nowhere: you detonate BY drawing, so the impulse was
    /// deleted the frame it landed.
    ///
    /// His answer is better than removing the anchor. A light knock leaves you
    /// drawing; a real blast throws you OUT of the mode first, and then the
    /// shove lands on a body that is free to move. His controller already does
    /// exactly this when you get floored ("getting floored kicks you out of the
    /// mode") — this is the same rule, keyed on force instead of on ragdolling.
    ///
    /// Every force source should deliver through here rather than calling
    /// TakeHit directly, so the rule holds for spells and charges too and does
    /// not have to be re-derived per caller.
    public static class Shove
    {
        /// Hit a player with an impulse and optional damage. Breaks drawing
        /// first when the impulse is over the threshold.
        public static void Hit(SimpleFPSController player, Vector3 impulse,
            float damage, string cause = null)
        {
            if (player == null) return;

            // EVERY MODE, NOT JUST DRAWING (Marko Aug 10: "whenever you're in
            // any mode like posing or shapeshift mode... you drop into either
            // first person or third person mode so that your body can ragdoll").
            // Each of these pins the body, detaches the camera, or eats the
            // shove — so a hit big enough has to END them before the impulse can
            // mean anything. One list here rather than each mode inventing its
            // own "am I being blown up" test.
            if (impulse.magnitude < DrawingConfig.ShoveBreaksDrawing)
            {
                player.TakeHit(impulse, damage, cause);
                return;
            }

            // WHERE YOU LAND WHEN A BLAST RAGDOLLS YOU — his table, Aug 10.
            // The shorthand is: YOUR CAMERA DOES NOT CHANGE UNLESS A MODE FORCED
            // IT, and body POSE is the single mode whose third-person view
            // survives, because that is the one where watching your own wizard
            // get thrown is the point.
            //
            //   FIRST person  · acolyte wearing a shape
            //                 · acolyte rotating that shape
            //                 · acolyte in zombie overwatch
            //                 · wizard in BODY PAINT
            //                 · anyone already in first person (draw mode too)
            //   THIRD person  · wizard in BODY POSE
            //                 · anyone already in third person
            bool wasShaped = ShapeShift.LocalIsShaped;
            bool wasAcolyteMode = wasShaped || ShapeShift.PoseOpen || ZombieWatch.IsOpen;
            bool wasPaint = SelfPaint.IsActive;
            bool wasPose = PoseGrab.IsOpen;
            bool wasInAMode = wasAcolyteMode || wasPaint || wasPose || HeldWeapon.DrawMode;

            HeldWeapon.CancelDrawMode();
            SelfPaint.Blown();
            PoseGrab.Blown();
            ShapeShift.Blown();
            ZombieWatch.Blown();

            // ASKED FOR BY NAME, never toggled — a toggle here would land the
            // blast victim in whichever mode they were NOT in.
            if (wasPose)
            {
                // body pose keeps him looking at himself while he is thrown
                player.EnterThirdPerson();
            }
            else if (wasAcolyteMode || wasPaint)
            {
                // BACK INTO YOUR OWN EYES, wand and grimoire and all. For a
                // SHAPED acolyte this also strips the disguise, and that is
                // deliberate (he confirmed it): losing first person while
                // wearing something is already ShapeShift's own rule — "Tab
                // brought us back to first person = back to yourself" — so its
                // Update unwears on the next frame without a second copy of that
                // law living here. A blast exposes a hider.
                player.EnterFirstPerson();
            }
            // nothing forced a camera → leave the mode exactly as it was

            if (wasInAMode)
                DrawingWorld.Instance?.LogEvent("the blast throws you out of it");

            player.TakeHit(impulse, damage, cause);
        }
    }
}
