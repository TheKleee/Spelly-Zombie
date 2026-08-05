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
        static readonly float TurnSensitivity = DrawingConfig.Overlay("GrabTurnSensitivity", 0.6f);
        static readonly float LiftRangeMax = DrawingConfig.Overlay("LiftRangeMax", 9f);

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

        /// IT STAYS WHERE YOU GRABBED IT (Marko): nothing is yanked into your
        /// face — the cargo keeps the distance it had when you took it, and
        /// simply follows your movement and your aim from there.
        float _holdDist = 0.92f;
        /// THE WHEEL FLIPS IT, NO MODIFIER (Marko): wheel UP tips it up,
        /// wheel DOWN rolls it to the right. Shift stays free for RUNNING —
        /// he wants to sprint around with a bench and hit things with it.
        Quaternion _spinRot = Quaternion.identity;

        Vector3 HandPoint()
        {
            var piv = _pilot != null ? _pilot.CameraPivot : null;
            if (piv == null) return transform.position + transform.forward * _holdDist;
            return piv.position + piv.forward * _holdDist;
        }

        Quaternion YawRot() => Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        /// Where the cargo should be facing: your heading, then your flips.
        Quaternion HoldRot() => YawRot() * _spinRot * _grabRelRot;

        /// Physics-rate cargo tracking. Kinematic holds move the BODY itself —
        /// it is simply in your hands. Joint holds (heavier than you) move the
        /// anchor and let the bond give.
        void FixedUpdate()
        {
            if (_heldBody == null) return;
            LevitateTick();
        }

        // ===================================== INK IS LIFTING POWER =======
        // Marko Jul 30: "the actual problem is that the freaking bench is too
        // heavy — make lifting power per ink 5x stronger." Ink buys five times
        // the lift it used to, so a scribble on a bench carries it.
        static float LiftInkPerKg => DrawingConfig.LiftInkPerKg;

        /// 0 = you can't shift it, 1 = it obeys completely. This ONE number
        /// cancels gravity AND caps acceleration, so "heavy things resist and
        /// you overcome them slowly" needs no separate code — and a partial
        /// hold naturally just SLOWS a falling object instead of lifting it.
        float AuthorityOver(Rigidbody rb, out float share)
        {
            share = 1f;
            if (rb == null) return 0f;

            // THE WHOLE THING, not one transform — the same law AuthorityIn
            // states: strokes land on whichever collider they hit, often a
            // CHILD of the body, so the ledgers live scattered through the
            // subtree. Reading only the root here meant a just-torn compound
            // prop saw ink 0 on its very first hold tick and dropped with
            // "your ink is gone" — the tear gate had just counted that ink.
            float mine = 0f, all = 0f;
            foreach (var mark in rb.GetComponentsInChildren<InkMark>(true))
            {
                mine += mark.Authority(Grimoire.LocalPlayerId);
                // everyone's pull, so the share is honest when two of you lift one thing
                foreach (var kv in mark.Stakes) all += kv.Value;
                if (mark.FreeForAll || mark.BornOf >= 0) all += Perks.InkMax;
            }
            if (mine <= 0f) return 0f;
            share = all > 0f ? Mathf.Clamp01(mine / all) : 1f;

            return Mathf.Clamp01(mine / Mathf.Max(0.01f, rb.mass * LiftInkPerKg));
        }

        /// The hold, at physics rate. It keeps the distance you grabbed it at
        /// and follows your movement and aim — no mouse steering.
        void LevitateTick()
        {
            float auth = AuthorityOver(_heldBody, out float share);
            if (auth <= 0f)
            {
                ClearBodyHold();
                DrawingWorld.Instance?.LogEvent("your ink is gone — it drops");
                return;
            }

            Vector3 delta = HandPoint() - _heldBody.position;
            float accel = Mathf.Lerp(4f, 90f, auth) * share;
            Vector3 target = Vector3.ClampMagnitude(delta * 8f, Mathf.Lerp(2.5f, 14f, auth));
            _heldBody.linearVelocity = Vector3.MoveTowards(
                _heldBody.linearVelocity, target, accel * Time.fixedDeltaTime);

            // gravity's grip loosens exactly as far as you own the thing
            _heldBody.useGravity = false;
            _heldBody.AddForce(Physics.gravity * (1f - auth), ForceMode.Acceleration);

            // IF YOU CAN'T LIFT IT, YOU CAN'T TURN IT (Marko, twice). This used
            // to run at ANY authority, so merely holding a too-heavy bench let
            // your own turning swing it around — nonsense. Below a full lift
            // its rotation is left completely alone.
            if (auth < 1f) return;

            // heavy things still turn grudgingly once you CAN lift them
            float turn = Mathf.Lerp(2f, 12f, auth) * Mathf.Clamp01(10f / Mathf.Max(1f, _heldBody.mass));
            _heldBody.MoveRotation(Quaternion.Slerp(_heldBody.rotation, HoldRot(),
                turn * Time.fixedDeltaTime));
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _pilot == null) return;

            // A HELD BLOB CAN DIE IN YOUR HAND: a nearby puddle proximity-
            // merges it away, or it ages out or burns. That destruction never
            // went through ClearBodyHold, so CarriedWeight stayed on your
            // back and the ball just silently vanished — a merge is loud
            // everywhere else, so it is loud here too.
            if (!ReferenceEquals(_heldBody, null) && _heldBody == null)
            {
                _heldBody = null;
                _joint = null;
                _kinematicHold = false;
                var board = GetComponent<BodyState>();
                if (board != null) board.CarriedWeight = 0f; // arms free again
                DrawingWorld.Instance?.LogEvent("what you held is gone — merged or spent");
            }

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

            // HOLD ALT AND TURN IT WITH THE MOUSE (Marko): alt already frees
            // the cursor, so the whole hand is available for turning — no
            // wheel, no modifier gymnastics.
            //
            // AND YOU CAN ONLY TURN WHAT YOU CAN LIFT (his rule): a hold that
            // is merely slowing a falling bench has no business spinning it.
            var mouse = Mouse.current;
            bool canTurn = _heldParticle != null
                || (_heldBody != null && AuthorityOver(_heldBody, out _) >= 1f);
            // ALT frees the cursor, then HOLD LEFT-MOUSE AND DRAG to turn it —
            // so moving the free cursor around doesn't spin your cargo by
            // accident. Axes follow the drag: pull right and it turns right,
            // pull up and it tips up (both were inverted).
            if (mouse != null && canTurn
                && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed)
                && mouse.leftButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue() * TurnSensitivity;
                if (d.sqrMagnitude > 0.0001f)
                    _spinRot = Quaternion.AngleAxis(-d.x, Vector3.up)
                             * Quaternion.AngleAxis(d.y, Vector3.right)
                             * _spinRot;
            }

            // F PUTS IT DOWN, E THROWS IT (his split)
            if (kb.fKey.wasPressedThisFrame)
            {
                DropHeld(Vector3.zero);
                return;
            }

            // (body cargo tracks in FixedUpdate — physics-rate, no swimming)
            if (_heldParticle != null)
            {
                if (_heldParticle.Dead) { _heldParticle = null; return; } // it burned out in your hand
                _heldParticle.transform.position = Vector3.Lerp(
                    _heldParticle.transform.position, HandPoint(), HandLerp * Time.deltaTime);
            }

            // (nothing tears loose any more — it floats on ink, not on a joint)

            // wand out (slot 1) or third person: CHANGING MODE RELEASES IT
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
                _spinRot = Quaternion.identity;
                var pv0 = _pilot.CameraPivot;
                if (pv0 != null) _holdDist = Mathf.Clamp(
                    Vector3.Distance(pv0.position, bestP.transform.position), 0.7f, GrabRange);
                DrawingWorld.Instance?.LogEvent($"grabbed the {bestP.Kind} — E throws it");
                return;
            }

            // WHAT YOU ARE AIMING AT, LITERALLY (his rule: E takes whatever
            // the raycast hits first — you point at the thing you mean).
            //
            // AND THE RAY MUST SEE THE BLOB (Marko: "Grabbing the liquid ball
            // made me fall through the ground indefinitely"). A liquid's big
            // collider is its walk-through trigger SHELL and its core is
            // shrunk deep inside the visible skin, so the old ignore-triggers
            // ray sailed straight THROUGH the ball and hit the floor behind
            // it — and the floor is what tore loose. The ray reads triggers
            // now: the nearest MATTER along the aim line is the thing you
            // meant, while trigger zones that aren't matter still never block
            // a grab (the first solid hit behaves exactly as before).
            Rigidbody bestB = null;
            var piv0 = _pilot.CameraPivot;
            if (piv0 == null) return;

            // E IS THE ORE'S KEY WHILE YOU CARRY ONE: InkRuneStone consumes
            // this very press to feed or drop, and the carried stone's own
            // collider is off — so the grab ray reached PAST it and could
            // tear up whatever stood behind the cauldron mid-feed.
            if (InkRuneStone.Carried != null) return;

            // NOT ~0: layer 2 is your OWN body ("the pen ignores our own
            // body") — a sprint-crossing forearm was winning the aim — and
            // layer 30 is the ink CANVASES, invisible planes floating outside
            // every facade that soak up wall ink by design; aiming at any
            // house met the canvas first and refused with world-scale spam.
            // Every world-purpose cast here masks them out; so does the grab.
            int mask = Physics.DefaultRaycastLayers & ~(1 << InkCanvasLayer.Layer);
            var along = Physics.RaycastAll(piv0.position, piv0.forward, LiftRangeMax, mask,
                QueryTriggerInteraction.Collide);
            if (along.Length == 0) return;
            System.Array.Sort(along, (h1, h2) => h1.distance.CompareTo(h2.distance));
            RaycastHit aimed = default;
            bool foundHit = false;
            Matter stateRefused = null;
            foreach (var h in along)
            {
                var blob = h.collider.GetComponentInParent<Matter>();
                if (blob != null)
                {
                    // STATE RULE (Marko): any state grabs while it's still a
                    // SPELL; once touched, only SOLID can be picked up again.
                    // A refused blob never blocks the ray — like the old
                    // scan, the grab simply looks past it.
                    if (blob.Touched && blob.Phase != MatterPhase.Solid)
                    { stateRefused = blob; continue; }
                    aimed = h; foundHit = true; // the blob IS what you aimed at
                    break;
                }
                // ink ores answer to their own E (take/feed/drop, aim-bid in
                // InkRuneStone) — the grab never competes for that press
                if (h.collider.GetComponentInParent<InkRuneStone>() != null) return;
                if (h.collider.isTrigger) continue; // invisible zones don't block aim
                aimed = h; foundHit = true;         // the solid hit the old ray saw
                break;
            }
            if (!foundHit)
            {
                if (stateRefused != null) // the law, out loud — no silently eaten press
                    DrawingWorld.Instance?.LogEvent(
                        $"the {stateRefused.Material} has been handled — only a SOLID grabs again");
                return;
            }

            var hitRb = aimed.collider.attachedRigidbody;

            // NEVER a wizard, a creature, a held weapon or your own hand
            // anchor — and the refusal must come BEFORE any physics change:
            // the old order made an excluded kinematic body dynamic first and
            // said "you can't lift it" after, leaving it fallen loose forever.
            if (aimed.collider.GetComponentInParent<SimpleFPSController>() != null
                || aimed.collider.GetComponentInParent<Creature>() != null
                || aimed.collider.GetComponentInParent<HeldWeapon>() != null
                || hitRb == _anchor)
            {
                DrawingWorld.Instance?.LogEvent($"you can't lift {aimed.collider.name}");
                return;
            }

            // A KINEMATIC BODY CANNOT BE MOVED BY VELOCITY. The old grab
            // skipped these; my raycast version didn't, so they were "grabbed"
            // and then every physics tick failed with "Setting linear velocity
            // of a kinematic body is not supported" while the object sat
            // there. If you have the ink, it becomes dynamic instead.
            if (hitRb != null && hitRb.isKinematic)
            {
                // THE WORLD ITSELF REFUSES (Marko's fall-through, Aug 4) —
                // kinematic world machinery must never become a free body,
                // no matter how much ink is on it. Same cap as tear-loose.
                if (Liftable.WorldScale(hitRb.transform, out var kd))
                {
                    DrawingWorld.Instance?.LogEvent(
                        $"the world itself refuses — {hitRb.name} is {kd.x:0.#}×{kd.y:0.#}×{kd.z:0.#}m of world, not a prop");
                    return;
                }
                if (InkMark.AuthorityIn(hitRb.transform, Grimoire.LocalPlayerId) <= 0f)
                {
                    DrawingWorld.Instance?.LogEvent($"no ink on {hitRb.name} — draw on it to lift it");
                    return;
                }
                Liftable.MakePhysicsLegal(hitRb.transform);
                hitRb.isKinematic = false;
                var lf = hitRb.GetComponent<Liftable>();
                if (lf != null) lf.Rooted = false;
            }

            if (hitRb != null)
            {
                if (AuthorityOver(hitRb, out _) <= 0f)
                {
                    float have = InkMark.AuthorityIn(hitRb.transform, Grimoire.LocalPlayerId);
                    DrawingWorld.Instance?.LogEvent(
                        $"{hitRb.name}: your ink {have:0}, needs {hitRb.mass * LiftInkPerKg:0} — draw more on it");
                    return;
                }
                bestB = hitRb;
            }
            else
            {
                // TEAR IT OUT OF THE GROUND: rooted scenery must have its
                // anchor overpowered first, then it's a real object forever.
                var host = InkMark.Host(aimed.collider.transform);
                var lift = host.GetComponentInParent<Liftable>();
                if (lift != null) host = lift.transform;

                // BUT THE WORLD ITSELF REFUSES (Marko, Aug 4: "I guess I
                // lifted the ground collider just a little bit and now the
                // character is probably spawning inside of it"). He draws on
                // the floor CONSTANTLY, so the floor out-inked its own anchor
                // and this path handed the GROUND a rigidbody and a convex
                // hull — a bowl he fell through forever. Size decides world
                // vs prop and ink never overrules it, so this runs BEFORE the
                // ink math: "draw more on it" must never be the advice here.
                if (Liftable.WorldScale(host, out var wd))
                {
                    DrawingWorld.Instance?.LogEvent(
                        $"the world itself refuses — {host.name} is {wd.x:0.#}×{wd.y:0.#}×{wd.z:0.#}m of world, not a prop");
                    return;
                }

                float mine = InkMark.AuthorityIn(host, Grimoire.LocalPlayerId);
                float hold = lift != null ? lift.HoldStrength : InkMark.AnchorHold(host);
                if (mine < hold)
                {
                    // SAY THE NUMBERS. "It won't budge" is useless; knowing
                    // you're 9 ink short tells you to keep drawing.
                    DrawingWorld.Instance?.LogEvent(
                        $"rooted — your ink on it: {mine:0} of {hold:0} needed ({host.name})");
                    return;
                }

                // it becomes a physics object HERE, at the moment it's freed
                if (lift != null) bestB = lift.TearLoose();
                else
                {
                    // same legality pass as Liftable: a concave mesh collider
                    // would make the freed prop fall through the world
                    Liftable.MakePhysicsLegal(host);
                    bestB = host.gameObject.AddComponent<Rigidbody>();
                    bestB.mass = Mathf.Max(0.2f, InkMark.EstimateMass(host));
                    bestB.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    bestB.interpolation = RigidbodyInterpolation.Interpolate;
                }
                DrawingWorld.Instance?.LogEvent("it tears free of the ground");
            }
            if (bestB == null) return;

            _heldBody = bestB;
            _slotAtGrab = _slots != null ? _slots.Current : 1;
            _spinRot = Quaternion.identity;
            var pv1 = _pilot.CameraPivot;
            // clamp to the RAY's reach, not GrabRange — the ray grabs out to
            // LiftRangeMax, and "it stays where you grabbed it" (his rule
            // above) forbids yanking a 9m grab in to arm's length
            if (pv1 != null) _holdDist = Mathf.Clamp(
                Vector3.Distance(pv1.position, bestB.worldCenterOfMass), 0.7f, LiftRangeMax);
            _heldHadGravity = bestB.useGravity;
            _prevInterp = bestB.interpolation;
            _prevAngDamp = bestB.angularDamping;
            _prevLinDamp = bestB.linearDamping;
            _grabRelRot = Quaternion.Inverse(YawRot()) * bestB.rotation;
            bestB.interpolation = RigidbodyInterpolation.Interpolate;
            bestB.angularDamping = 4f;
            _kinematicHold = false;   // it FLOATS on your ink — never kinematic

            // THE CARGO IS FLOATING, NOT SHOULDERED — it rides on your ink, so
            // it barely weighs on you and you can still RUN with it (Marko
            // wants to sprint around holding a bench and hit things with it).
            var board = _pilot != null ? _pilot.GetComponent<BodyState>() : null;
            if (board != null) board.CarriedWeight = bestB.mass / 420f;

            float auth0 = AuthorityOver(bestB, out _);
            float needInk = bestB.mass * LiftInkPerKg;
            float haveInk = InkMark.AuthorityIn(bestB.transform, Grimoire.LocalPlayerId);
            DrawingWorld.Instance?.LogEvent(auth0 >= 1f
                ? "it lifts free — alt + left-drag turns it · F drops · E throws"
                : $"only slowing it — ink {haveInk:0} of {needInk:0} ({bestB.mass:0.#}kg): draw more, no turning yet");
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
                // IT MUST NOT ROCKET OFF WHEN YOU LET GO (Marko: "lifted
                // objects suddenly start flying all over the place after I
                // release them"). The hold drives velocity directly, so if
                // you spin on the spot or the cargo is fighting a wall it can
                // be carrying a huge speed at the moment of release. Hand it
                // back to physics calm.
                if (!_heldBody.isKinematic)
                {
                    _heldBody.linearVelocity = Vector3.ClampMagnitude(_heldBody.linearVelocity, 4f);
                    _heldBody.angularVelocity = Vector3.ClampMagnitude(_heldBody.angularVelocity, 4f);
                }
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
