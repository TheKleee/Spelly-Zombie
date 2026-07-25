using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// THE STICKY HAND (Marko's grabbing system): E grabs what you're
    /// AIMING at — grabbing IS stickiness. If you can lift it you carry it
    /// (no artificial cap): the cargo's weight lands on YOUR body, so the
    /// movement gates are the strength limit — sprint refuses, then you're
    /// crouch-hauling a boulder. A hard snag still tears the grip loose.
    ///
    /// Spell particles are ALL grabbable (claiming one harvests the seal —
    /// the rune re-emits); world objects only when they're raw tagged
    /// material that no other system owns. While holding: the hand is
    /// occupied, so no drawing. E again THROWS toward the aim point — the
    /// push ability fired down your own cursor. Switching to the wand
    /// (slot 1) or to third person simply drops it.
    public class HandGrab : MonoBehaviour
    {
        const float GrabRange = 2.8f;
        const float AimCone = 0.78f;   // same cone as every other E interaction
        const float ThrowSpeed = 11f;  // particles: push, aimed down the cursor
        const float ThrowImpulse = 7f; // rigidbodies: velocity change
        const float HandLerp = 14f;

        /// The local player is holding something — the hand is occupied
        /// (drawing is blocked while true).
        public static bool LocalHolding { get; private set; }
        /// What the local hands hold (HandIK puts the wizard's hands ON it).
        public static Rigidbody LocalHeldBody { get; private set; }
        public static SpellParticle LocalHeldMote { get; private set; }

        SimpleFPSController _pilot;
        WeaponSlots _slots;
        Rigidbody _anchor;             // kinematic hand-anchor bonds connect to
        SpellParticle _heldParticle;
        Rigidbody _heldBody;
        FixedJoint _joint;
        int _slotAtGrab;
        bool _heldHadGravity = true; // restored on release
        bool _kinematicHold;         // lighter than you = carried KINEMATIC (Marko: zero wobble)
        Quaternion _grabRelRot = Quaternion.identity; // cargo pose relative to facing
        RigidbodyInterpolation _prevInterp;
        float _prevAngDamp, _prevLinDamp;

        void Awake()
        {
            _pilot = GetComponent<SimpleFPSController>();
            _slots = GetComponent<WeaponSlots>();
            var go = new GameObject("HandAnchor");
            go.transform.SetParent(transform, false);
            _anchor = go.AddComponent<Rigidbody>();
            _anchor.isKinematic = true;
        }

        void OnDisable() { if (LocalHolding) DropHeld(Vector3.zero); LocalHolding = false; }

        Vector3 HandPoint()
        {
            var piv = _pilot != null ? _pilot.CameraPivot : null;
            if (piv == null) return transform.position + transform.forward * 0.9f;
            // slightly right and low — where HANDS live, not screen centre
            return piv.position + piv.forward * 0.92f + piv.right * 0.14f - piv.up * 0.16f;
        }

        Quaternion YawRot() => Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        /// Physics-rate cargo tracking. Kinematic holds move the BODY itself —
        /// it is simply in your hands. Joint holds (heavier than you) move the
        /// anchor and let the bond give.
        void FixedUpdate()
        {
            if (_heldBody == null || _anchor == null) return;
            if (_kinematicHold)
            {
                _heldBody.MovePosition(HandPoint());
                _heldBody.MoveRotation(YawRot() * _grabRelRot);
                return;
            }
            _anchor.MovePosition(HandPoint());
            _anchor.MoveRotation(YawRot() * _grabRelRot);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _pilot == null) return;

            bool holding = _heldParticle != null || _heldBody != null;
            LocalHolding = holding;
            LocalHeldBody = _heldBody;
            LocalHeldMote = _heldParticle;
            if (!holding)
            {
                // third person is for emoting (E belongs to poses there), and
                // a downed wizard's hands are busy dying
                if (SimpleFPSController.ThirdPersonActive || _pilot.IsDowned) return;
                if (kb.eKey.wasPressedThisFrame) TryGrab();
                return;
            }

            if (_pilot.IsDowned) { DropHeld(Vector3.zero); return; }

            // (body cargo tracks in FixedUpdate — physics-rate, no swimming)
            if (_heldParticle != null)
            {
                if (_heldParticle.Dead) { _heldParticle = null; return; } // it burned out in your hand
                _heldParticle.transform.position = Vector3.Lerp(
                    _heldParticle.transform.position, HandPoint(), HandLerp * Time.deltaTime);
            }

            // a JOINT hold whose bond broke = the grip tore loose (kinematic
            // holds cannot tear — they're simply in your hands)
            if (_heldBody != null && !_kinematicHold && _joint == null)
            {
                ClearBodyHold();
                DrawingWorld.Instance?.LogEvent("the grip tore loose");
                return;
            }

            // wand out (slot 1) or third person: the hands are needed — drop
            if ((_slots != null && _slots.Current == 1 && _slotAtGrab != 1)
                || SimpleFPSController.ThirdPersonActive)
            {
                DropHeld(Vector3.zero);
                return;
            }

            if (kb.eKey.wasPressedThisFrame) Throw();
        }

        // ------------------------------------------------------- grabbing --
        void TryGrab()
        {
            // spell particles first — ALL of them are grabbable (Marko's law)
            SpellParticle bestP = null;
            float best = 0f;
            foreach (var p in SpellParticle.Living)
            {
                if (p == null || p.Dead || p.Claimed) continue;
                float a = _pilot.AimScore(p.transform.position, GrabRange, AimCone, p.transform);
                if (a > best) { best = a; bestP = p; }
            }
            if (bestP != null)
            {
                bestP.Claim(transform);
                _heldParticle = bestP;
                _slotAtGrab = _slots != null ? _slots.Current : 1;
                DrawingWorld.Instance?.LogEvent($"grabbed the {bestP.Kind} — E throws it");
                return;
            }

            // world objects: raw tagged material that no other system owns
            Rigidbody bestB = null;
            best = 0f;
            foreach (var col in Physics.OverlapSphere(HandPoint(), GrabRange, ~0, QueryTriggerInteraction.Collide))
            {
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic || rb == _anchor) continue;
                var go = rb.gameObject;
                if (go.GetComponent<SurfaceMaterialTag>() == null && go.GetComponent<Matter>() == null) continue;
                if (go.GetComponent<HeldWeapon>() != null || go.GetComponent<InkRuneStone>() != null) continue;
                if (go.GetComponentInParent<Creature>() != null
                    || go.GetComponentInParent<SimpleFPSController>() != null) continue;
                // STATE RULE (Marko): any state grabs while it's still a SPELL;
                // once touched (an object), only SOLID can be picked up again
                var m2 = go.GetComponent<Matter>();
                if (m2 != null && m2.Touched && m2.Phase != MatterPhase.Solid) continue;
                float a = _pilot.AimScore(rb.worldCenterOfMass, GrabRange, AimCone, rb.transform);
                if (a > best) { best = a; bestB = rb; }
            }
            if (bestB == null) return;

            // the surface fights back (Marko's slick rule): a slicked object
            // subtracts from your grip — slick enough and it slides right out;
            // a glue-coated one welds to your hand HARDER than you meant
            var mm = bestB.GetComponent<Matter>();
            float grip = StickyBonds.Effective(StickyBonds.HandHold, mm != null ? mm.Stickiness : 0f);
            if (grip <= 0f)
            {
                DrawingWorld.Instance?.LogEvent("too slick — it slides out of your fingers");
                return;
            }

            _heldBody = bestB;
            _slotAtGrab = _slots != null ? _slots.Current : 1;
            _anchor.position = HandPoint();
            // IF YOU CAN LIFT IT, YOU CARRY IT (Marko: no artificial cap) —
            // the weight lands on YOUR body, and the movement gates are the
            // strength limit: sprint refuses, then you're crouch-hauling.
            _heldHadGravity = bestB.useGravity;
            _prevInterp = bestB.interpolation;
            _prevAngDamp = bestB.angularDamping;
            _prevLinDamp = bestB.linearDamping;
            _grabRelRot = Quaternion.Inverse(YawRot()) * bestB.rotation;
            bestB.interpolation = RigidbodyInterpolation.Interpolate;

            // LIGHTER THAN YOU = KINEMATIC while held (Marko: "grabbing is
            // terrible" — no joint, no physics, ZERO wobble; it's simply in
            // your hands). Heavier than you keeps the physical bond.
            _kinematicHold = bestB.mass <= 70f;
            if (_kinematicHold)
            {
                bestB.isKinematic = true;
            }
            else
            {
                bestB.useGravity = false;
                bestB.angularDamping = 8f;
                bestB.linearDamping = 2f;
                _anchor.rotation = YawRot() * _grabRelRot;
                _joint = StickyBonds.Bond(bestB, _anchor, grip);
            }
            var board = _pilot != null ? _pilot.GetComponent<BodyState>() : null;
            if (board != null) board.CarriedWeight = bestB.mass / 70f; // in wizard-weights
            DrawingWorld.Instance?.LogEvent(bestB.mass > 26f
                ? "heaved it up — you can feel it in your knees"
                : "grabbed it — E throws, walk to carry");
        }

        // ------------------------------------------------- throwing/dropping --
        void Throw()
        {
            var piv = _pilot.CameraPivot;
            Vector3 dir = piv != null ? piv.forward : transform.forward;
            if (_heldParticle != null)
            {
                var p = _heldParticle;
                _heldParticle = null;
                p.ReleaseHeld(dir * ThrowSpeed); // the push ability, down your own cursor
            }
            else if (_heldBody != null)
            {
                var b = _heldBody;
                ClearBodyHold();
                b.AddForce(dir * ThrowImpulse, ForceMode.VelocityChange);
            }
        }

        void DropHeld(Vector3 extra)
        {
            if (_heldParticle != null)
            {
                var p = _heldParticle;
                _heldParticle = null;
                p.ReleaseHeld(extra);
            }
            else if (_heldBody != null)
            {
                var b = _heldBody;
                ClearBodyHold();
                if (extra != Vector3.zero) b.AddForce(extra, ForceMode.VelocityChange);
            }
        }

        void ClearBodyHold()
        {
            if (_joint != null) Destroy(_joint);
            if (_heldBody != null)
            {
                if (_kinematicHold) _heldBody.isKinematic = false; // physics takes it back
                _heldBody.useGravity = _heldHadGravity;
                _heldBody.interpolation = _prevInterp;
                _heldBody.angularDamping = _prevAngDamp;
                _heldBody.linearDamping = _prevLinDamp;
                var m = _heldBody.GetComponent<Matter>();
                if (m != null) m.Touched = true; // TOUCH = WORLD — it's an object now
            }
            _kinematicHold = false;
            var board = _pilot != null ? _pilot.GetComponent<BodyState>() : null;
            if (board != null) board.CarriedWeight = 0f; // arms free again
            _joint = null;
            _heldBody = null;
        }
    }
}
