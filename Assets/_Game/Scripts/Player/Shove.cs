using UnityEngine;

namespace SpellyZombie
{
    /// A BLOW BIG ENOUGH BREAKS YOUR CONCENTRATION ("when the
    /// force is strong enough it should push you out of the draw mode into the
    /// first person mode").
    ///
    /// the controller drops `_shove` to zero while a draw or easel mode is open
    /// - the easel is an anchor, or the body slides out from under a detached
    /// camera. That is right for the gentle stuff, and it is why a detonation in
    /// your face moved you nowhere: you detonate BY drawing, so the impulse was
    /// deleted the frame it landed.
    ///
    /// the answer is better than removing the anchor. A light knock leaves you
    /// drawing; a real blast throws you OUT of the mode first, and then the
    /// shove lands on a body that is free to move. the controller already does
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

            // EVERY MODE, NOT JUST DRAWING ("whenever you're in
            // any mode like posing or shapeshift mode... you drop into either
            // first person or third person mode so that your body can ragdoll").
            // Each of these pins the body, detaches the camera, or eats the
            // shove - so a hit big enough has to END them before the impulse can
            // mean anything. One list here rather than each mode inventing its
            // own "am I being blown up" test.
            if (impulse.magnitude < DrawingConfig.ShoveBreaksDrawing)
            {
                player.TakeHit(impulse, damage, cause);
                return;
            }

            // WHERE YOU LAND WHEN A BLAST RAGDOLLS YOU - the table, Aug 10.
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

            // ASKED FOR BY NAME, never toggled - a toggle here would land the
            // blast victim in whichever mode they were NOT in.
            if (wasPose)
            {
                // body pose keeps him looking at himself while is thrown
                player.EnterThirdPerson();
            }
            else if (wasAcolyteMode || wasPaint)
            {
                // BACK INTO YOUR OWN EYES, wand and grimoire and all. For a
                // SHAPED acolyte this also strips the disguise, and that is
                // deliberate (confirmed it): losing first person while
                // wearing something is already ShapeShift's own rule — "Tab
                // brought us back to first person = back to yourself" — so its
                // Update unwears on the next frame without a second copy of that
                // law living here. A blast exposes a hider.
                player.EnterFirstPerson();
            }
            // nothing forced a camera  leave the mode exactly as it was

            if (wasInAMode)
                DrawingWorld.Instance?.LogEvent("the blast throws you out of it");

            player.TakeHit(impulse, damage, cause);
        }

        static readonly Collider[] _blastHits = new Collider[48];

        /// A radial blast: players shoved with distance falloff (acolytes
        /// shoved but never damaged - poison is corruption, blasts are
        /// physics), loose props thrown. One implementation for the zombie
        /// detonation and the acolyte death burst.
        public static void Blast(Vector3 at, float radius, float power,
            float baseDamage, string cause, Rigidbody except = null)
        {
            Juice.Thud(at);

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDowned) continue;
                Vector3 away = p.transform.position - at;
                away.y = 0f;
                float dist = away.magnitude;
                if (dist > radius) continue;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;

                float t = 1f - Mathf.Clamp01(dist / Mathf.Max(0.1f, radius));
                bool corrupted = Sides.IsAcolytePlayer(p);
                Hit(p,
                    away.normalized * power * t + Vector3.up * power * 0.3f * t,
                    corrupted ? 0f : baseDamage * t,
                    cause);
            }

            int n = Physics.OverlapSphereNonAlloc(at, radius, _blastHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_blastHits[i] == null) continue;
                var rb = _blastHits[i].attachedRigidbody;
                if (rb != null && !rb.isKinematic && rb != except)
                    rb.AddExplosionForce(power * 18f, at, radius, 0.4f, ForceMode.Impulse);
            }
        }
    }
}
