using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// E grabs the aimed object; the cargo's weight lands on the body, so the
    /// movement gates are the strength limit. Spell particles are all grabbable;
    /// holding blocks drawing, E again throws, wand/third person drops it.
    public class HandGrab : MonoBehaviour
    {
        const float GrabRange = 2.8f;
        const float AimCone = 0.78f;   // same cone as every other E interaction
        const float TurnBallShare = 0.2f; // the drag ball's radius, at most this share of its distance from the eye
        // particle push speed, aimed down the cursor (host reuses - netcode §4);
        // overridable via sz_tuning.json
        public static float ThrowSpeed => DrawingConfig.ThrowSpeed * LocalStrength();
        /// The same law with a remote friend's strength (the host throws for them).
        public static float ThrowSpeedFor(int ownerId) => DrawingConfig.ThrowSpeed * NetSync.StrengthOf(ownerId);

        /// STRENGTH IS HEALTH: what you can lift and how hard you throw scale
        /// with how much of your ceiling you still have. Wounded = weaker, so
        /// healing is what buys back your carrying power.
        /// Never reaches 0 - a dying wizard still shoves, just feebly.
        public static float LocalStrength()
        {
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || !p.IsLocalViewer) continue;
                float f = Sides.StrengthFraction(Grimoire.LocalPlayerId, p.Health);
                return Mathf.Lerp(DrawingConfig.StrengthFloorMul, 1f, f);
            }
            return 1f;
        }
        public const float ThrowImpulse = 14f; // rigidbodies: velocity change
        const float HandLerp = 14f;
        static readonly float TurnSensitivity = DrawingConfig.Overlay("GrabTurnSensitivity", 0.6f);
        static readonly float LiftRangeMax = DrawingConfig.Overlay("LiftRangeMax", 9f);

        /// The local player is holding something - the hand is occupied
        /// (drawing is blocked while true).
        public static bool LocalHolding { get; private set; }
        /// What the local hands hold (HandIK puts the wizard's hands ON it).
        public static Rigidbody LocalHeldBody { get; private set; }
        public static SpellParticle LocalHeldMote { get; private set; }

        /// CLIENT: the proxy this hand holds through the host, and where the
        /// hand is - the proxy rides it so the cargo does not lag a round trip.
        public static Transform PredictedCargo { get; private set; }
        public static Vector3 PredictedHand { get; private set; }

        /// The hand point for a proxy this machine holds; false = not held here.
        public static bool PredictCargo(Transform t, out Vector3 hand)
        {
            hand = PredictedHand;
            return PredictedCargo != null && PredictedCargo == t;
        }

        /// The wire name of the local cargo, for the presence (0 = none).
        public static void LocalHeldId(out byte kind, out int id)
        {
            var g = _localGrab;
            NetSync.HeldIdOf(g != null ? g._heldBody : null, g != null ? g._heldParticle : null,
                g != null ? g._remoteCargo : null, out kind, out id);
        }

        SimpleFPSController _pilot;
        WeaponSlots _slots;
        SpellParticle _heldParticle;
        Rigidbody _heldBody;
        InkMark[] _heldMarks;        // the held subtree's ledgers, cached at grab (no per-frame scan)
        int _slotAtGrab;
        bool _heldHadGravity = true; // restored on release
        Quaternion _grabRelRot = Quaternion.identity; // cargo pose relative to facing
        RigidbodyInterpolation _prevInterp;
        float _prevAngDamp, _prevLinDamp, _prevMaxSpin;

        void Awake()
        {
            _pilot = GetComponent<SimpleFPSController>();
            _slots = GetComponent<WeaponSlots>();
            _localGrab = this; // GrabAck refusals find the hand (netcode §4)
        }

        void OnDisable() { if (LocalHolding) DropHeld(Vector3.zero); LocalHolding = false; }

        /// The cargo keeps the distance it had when grabbed.
        float _holdDist = 0.92f;
        /// Accumulated turn applied on top of the facing.
        Quaternion _spinRot = Quaternion.identity;
        // the shared arcball drag state (same feel as the shape pose mode)
        Vector3 _turnGrabLocal;   // the grabbed direction, in the frame of the turn being given
        Vector3 _turnCenterLocal; // the middle of the cargo, in its own frame
        float _turnRadius;
        bool _turning;

        /// The middle the drag ball sits on: a real body's centre of mass, else the middle of what is drawn.
        Vector3 TurnCenter(Transform held)
            => _heldBody != null ? _heldBody.worldCenterOfMass : held.TransformPoint(_turnCenterLocal);

        Vector3 HandPoint()
        {
            var piv = _pilot != null ? _pilot.CameraPivot : null;
            if (piv == null) return transform.position + transform.forward * _holdDist;
            return piv.position + piv.forward * _holdDist;
        }

        Quaternion YawRot() => Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        /// Where the cargo should be facing: your heading, then your flips.
        Quaternion HoldRot() => YawRot() * _spinRot * _grabRelRot;

        /// Physics-rate cargo tracking - the hold floats on ink; no joints,
        /// no kinematic holds.
        void FixedUpdate()
        {
            if (_heldBody == null) return;
            LevitateTick();
        }

        /// 0 = you can't shift it, 1 = it obeys completely. One number cancels
        /// gravity and caps acceleration; a partial hold slows a falling object.
        /// One-off callers (TryGrab) scan fresh; the hold passes _heldMarks.
        float AuthorityOver(Rigidbody rb, out float share)
            => AuthorityOver(rb,
                rb != null ? rb.GetComponentsInChildren<InkMark>(true) : null, out share);

        float AuthorityOver(Rigidbody rb, InkMark[] marks, out float share)
            => AuthorityFor(rb, marks, Grimoire.LocalPlayerId, out share);

        /// Owner-parameterized: the HOST drives remote friends' holds with THEIR ink (netcode §4).
        public static float AuthorityFor(Rigidbody rb, InkMark[] marks, int ownerId, out float share)
        {
            share = 1f;
            if (rb == null) return 0f;

            // spell-form matter lifts free (no ink, weight ignored); once
            // thrown, the ink law rules again
            var spellMatter = rb.GetComponent<MatterStrike>();
            if (spellMatter != null && spellMatter.SpellForm && spellMatter.OwnerId == ownerId)
                return 1f;

            // your own golem obeys completely - no ink needed (his rule)
            var golem = rb.GetComponentInParent<Golem>();
            if (golem != null && golem.OwnerId == ownerId) return 1f;

            if (OwnPot(rb.transform, ownerId)) return 1f;

            if (marks == null) return 0f;

            // the WHOLE subtree, not one transform - ledgers live on whichever
            // collider the strokes hit (same law as InkMark.AuthorityIn)
            float mine = InkMark.AuthorityIn(marks, ownerId);
            float all = 0f;
            foreach (var mark in marks)
            {
                if (mark == null) continue;
                // everyone's pull, so the share is honest when two of you lift one thing
                foreach (var kv in mark.Stakes) all += kv.Value;
                if (mark.FreeForAll || mark.BornOf >= 0) all += DrawingConfig.InkMax;
            }
            if (mine <= 0f) return 0f;
            share = all > 0f ? Mathf.Clamp01(mine / all) : 1f;

            // a wounded lifter needs more ink for the same mass
            float need = rb.mass * DrawingConfig.LiftInkPerKg / Mathf.Max(0.05f, NetSync.StrengthOf(ownerId));
            return Mathf.Clamp01(mine / Mathf.Max(0.01f, need));
        }

        /// The hold, at physics rate. It keeps the distance you grabbed it at
        /// and follows your movement and aim - no mouse steering.
        void LevitateTick()
        {
            float auth = AuthorityOver(_heldBody, _heldMarks, out float share);
            if (auth <= 0f)
            {
                ClearBodyHold();
                DrawingWorld.Instance?.LogEvent("your ink is gone, it drops");
                return;
            }

            // the middle of the thing rides the hand, never its pivot
            Vector3 delta = HandPoint() - _heldBody.worldCenterOfMass;
            float accel = Mathf.Lerp(4f, 90f, auth) * share;
            Vector3 target = Vector3.ClampMagnitude(delta * 8f, Mathf.Lerp(2.5f, 14f, auth));
            _heldBody.linearVelocity = Vector3.MoveTowards(
                _heldBody.linearVelocity, target, accel * Time.fixedDeltaTime);

            // gravity's grip loosens exactly as far as you own the thing
            _heldBody.useGravity = false;
            _heldBody.AddForce(Physics.gravity * (1f - auth), ForceMode.Acceleration);

            // below a full lift, rotation is left alone
            if (auth < 1f) return;

            TurnAboutCenter(_heldBody, HoldRot(), TurnRate);
        }

        /// How fast held cargo closes on the turn you gave it, as a share of the gap per second:
        /// the pace an acolyte's disguise turns at (ArcballDrag's own), whatever the cargo weighs.
        public const float TurnRate = 30f;
        /// The engine stops a body's spin at 7 rad/s, which caps a quick turn at about a quarter
        /// turn in a fifth of a second. Held cargo may spin as fast as the turn asks.
        public const float HeldMaxSpin = 60f;

        /// Turns a held body toward 'want' by angular velocity, which physics
        /// applies about the center of mass, so it spins in place instead of
        /// swinging around its pivot. 'rate' = share of the gap closed per second.
        public static void TurnAboutCenter(Rigidbody rb, Quaternion want, float rate)
        {
            (want * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float deg, out Vector3 axis);
            if (deg > 180f) deg -= 360f;
            rb.angularVelocity = Mathf.Abs(deg) < 0.05f ? Vector3.zero
                : axis.normalized * (deg * Mathf.Deg2Rad * rate);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || _pilot == null) return;

            // the held body can be destroyed externally (merge/age/burn) -
            // clear the hold state and CarriedWeight here
            if (!ReferenceEquals(_heldBody, null) && _heldBody == null)
            {
                _heldBody = null;
                _heldMarks = null;
                var board = GetComponent<BodyState>();
                if (board != null) board.CarriedWeight = 0f; // arms free again
                DrawingWorld.Instance?.LogEvent("what you held is gone, merged or spent");
            }
            // the lobby hides a broken prop instead of destroying it: the hand opens all the same
            else if (_heldBody != null && LobbyRespawn.IsHidden(_heldBody))
            {
                ClearBodyHold();
                DrawingWorld.Instance?.LogEvent("what you held is gone, merged or spent");
            }

            // CLIENT: what the hand holds lives on the host. Gone there (eaten, merged, burned out),
            // its stand-in goes too and nothing told the hand: full hands kept the book at the belt for good
            if (_remoteHolding && _remoteCargo == null)
            {
                _cargoGoneFor += Time.deltaTime;
                if (_cargoGoneFor > 1f)
                {
                    _cargoGoneFor = 0f;
                    DropHeld(Vector3.zero);
                    DrawingWorld.Instance?.LogEvent("what you held is gone, merged or spent");
                }
            }
            else _cargoGoneFor = 0f;

            bool holding = _heldParticle != null || _heldBody != null || _remoteHolding;
            LocalHolding = holding;
            LocalHeldBody = _heldBody;
            LocalHeldMote = _heldParticle;
            // the proxy in hand rides the hand; the host's snapshots only correct it
            PredictedCargo = _remoteHolding ? _remoteCargo : null;
            PredictedHand = HandPoint();

            // remote hold: stream the hand point so the HOST can drive (netcode §4)
            if (_remoteHolding)
            {
                _aimStream -= Time.deltaTime;
                if (_aimStream <= 0f)
                {
                    // a turn in progress goes out three times as often: at the hold's pace the
                    // host's body would reach each word at once and turn in ten steps a second
                    _aimStream = _turning ? 0.033f : 0.1f;
                    NetSync.SendLiftAim(HandPoint(), HoldRot());
                }
            }
            if (!holding)
            {
                // third person: E belongs to poses; no grabbing while downed
                if (SimpleFPSController.ThirdPersonActive || _pilot.IsDowned) return;
                // the floating key goes on the spell the grab itself would take: its cone is far
                // wider than the badge's ray, and a client's stand-ins have no collider for a ray at all
                var seen = NetGame.IsAuthority ? BestMote()?.transform : BestProxy()?.transform;
                if (seen != null) AimBadge.OfferSpell(seen);
                if (Keys.Down(Act.Use))
                {
                    // a closed chest under the aim takes E: it opens and stays open
                    if (AimBadge.ChestTarget != null) AimBadge.ChestTarget.OpenByPlayer();
                    else TryGrab();
                }
                return;
            }

            if (_pilot.IsDowned) { DropHeld(Vector3.zero); return; }

            // Alt + left-drag turns the cargo; only a full lift can turn it
            var mouse = Mouse.current;
            bool canTurn = _heldParticle != null || _remoteHolding // host clamps by real authority
                || (_heldBody != null && AuthorityOver(_heldBody, _heldMarks, out _) >= 1f);
            // ArcballDrag, shared with ShapeShift: grab a point on the cargo,
            // drag, and it turns so that point follows the hand
            if (mouse != null && canTurn
                && (Keys.Held(Act.Precise) || kb.rightAltKey.isPressed))
            {
                // a client turns the host's cargo by its stand-in; the turn travels with the hand point
                var held = _heldBody != null ? _heldBody.transform
                    : _heldParticle != null ? _heldParticle.transform : _remoteCargo;
                var cam = Camera.main;
                if (held != null && cam != null)
                {
                    Vector2 screen = mouse.position.ReadValue();
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        var box = ShapeShift.FindObjectBounds(held);
                        _turnCenterLocal = held.InverseTransformPoint(box.center);
                        // the drag ball is the cargo's own size, but never bigger on screen than a prop in
                        // an acolyte's hands: a long beam's ball would need the cursor across the whole
                        // screen for a quarter turn. Capped, a quarter turn is a fifth of the screen
                        float dist = Vector3.Distance(cam.transform.position, TurnCenter(held));
                        _turnRadius = Mathf.Clamp(box.extents.magnitude, 0.12f, dist * TurnBallShare);
                        // the grabbed point is kept on the TURN you are giving, not on the body that
                        // chases it: the same drag, to the number, an acolyte's disguise turns by
                        _turnGrabLocal = Quaternion.Inverse(HoldRot())
                            * ArcballDrag.Grab(cam, screen, TurnCenter(held), _turnRadius);
                        _turning = true;
                    }
                    else if (!mouse.leftButton.isPressed) _turning = false;
                    else if (_turning)
                    {
                        // the turn is applied to the spin, so the hold keeps following the heading
                        Quaternion before = HoldRot();
                        Quaternion after = ArcballDrag.Turn(cam, screen, TurnCenter(held), _turnRadius,
                            before * _turnGrabLocal, before, Time.deltaTime);
                        _spinRot = Quaternion.Inverse(YawRot()) * after
                                 * Quaternion.Inverse(_grabRelRot);
                    }
                }
            }
            else _turning = false;

            // ★ F WAKES THE RUNE IN PLACE (his rework: waking looked better
            // than exploding) - the dormant ghost becomes the real spell
            // right there: auras on, areas raised, a meteor's sky answered.
            // E still throws it to detonate on impact.
            if (Keys.Down(Act.Drop))
            {
                if (_heldParticle != null && !_remoteHolding)
                {
                    var p = _heldParticle;
                    _heldParticle = null;
                    p.ReleaseHeld(Vector3.zero);
                    p.Wake();
                    SpellKick.Apply(p, Vector3.zero, _pilot.transform, Grimoire.LocalPlayerId); // it wakes in your hand: it pushes you off
                }
                else DropHeld(Vector3.zero, wake: true); // a friend's hand: the host wakes it there
                return;
            }

            // (body cargo tracks in FixedUpdate - physics-rate, no swimming)
            if (_heldParticle != null)
            {
                if (_heldParticle.Dead) // it died in your hand (the particle's own log names the cause)
                {
                    _heldParticle = null;
                    DrawingWorld.Instance?.LogEvent("the spell in your hand is gone");
                    return;
                }
                _heldParticle.transform.position = Vector3.Lerp(
                    _heldParticle.transform.position, HandPoint(), HandLerp * Time.deltaTime);
            }

            // wand out (slot 1) or third person: changing mode releases it
            if ((_slots != null && _slots.Current == 1 && _slotAtGrab != 1)
                || SimpleFPSController.ThirdPersonActive)
            {
                DrawingWorld.Instance?.LogEvent("the hand opens: the wand came out, or third person");
                DropHeld(Vector3.zero);
                return;
            }

            if (Keys.Down(Act.Use)) Throw();
        }

        // ------------------------------------------------------- grabbing --
        /// CLIENT: the spell stand-in the aim is on, by the grab's own rule. Null when none.
        NetMoteProxy BestProxy()
        {
            NetMoteProxy best = null;
            float bestAim = 0f;
            foreach (var mp in NetMoteProxy.Living)
            {
                if (mp == null || mp.Claimed) continue;
                float a = _pilot.AimScore(mp.transform.position, GrabRange, AimCone, mp.transform);
                if (a > bestAim) { bestAim = a; best = mp; }
            }
            return best;
        }

        /// The spell the aim is on, by the grab's own rule. Null when none.
        SpellParticle BestMote()
        {
            SpellParticle best = null;
            float bestAim = 0f;
            foreach (var p in SpellParticle.Living)
            {
                if (p == null || p.Dead || p.Claimed) continue;
                float a = _pilot.AimScore(p.transform.position, GrabRange, AimCone, p.transform);
                if (a > bestAim) { bestAim = a; best = p; }
            }
            return best;
        }

        void TryGrab()
        {
            // CLIENT: live particles exist only on the host - aim at the mote
            // PROXIES and ship a claim intent instead (netcode §4)
            if (!NetGame.IsAuthority)
            {
                var bestM = BestProxy();
                if (bestM != null)
                {
                    NetSync.SendClaimIntent(bestM.HostId);
                    var pvm = _pilot.CameraPivot;
                    BeginRemoteHold(pvm != null
                        ? Mathf.Clamp(Vector3.Distance(pvm.position, bestM.transform.position), 0.7f, GrabRange)
                        : 0.92f, bestM.transform);
                    return;
                }
            }

            // spell particles first - ALL of them are grabbable
            SpellParticle bestP = BestMote();
            if (bestP != null)
            {
                bestP.Claim(transform);
                _heldParticle = bestP;
                _slotAtGrab = _slots != null ? _slots.Current : 1;
                _spinRot = Quaternion.identity;
                var pv0 = _pilot.CameraPivot;
                if (pv0 != null) _holdDist = Mathf.Clamp(
                    Vector3.Distance(pv0.position, bestP.transform.position), 0.7f, GrabRange);
                // name what the BOOK says it is - the raw kind reads "Push"
                // for anything without a dominant axis, which looks like a
                // particle nobody ever drew
                string held = bestP.Fusions.Count > 0
                    ? string.Join(" + ", bestP.Fusions.ConvertAll(f => f.Name))
                    : bestP.Kind.ToString();
                DrawingWorld.Instance?.LogEvent($"grabbed the {held}. E throws it");
                return;
            }

            // the ray reads triggers: the nearest Matter along the aim line
            // wins (liquid cores hide inside trigger shells); non-matter
            // triggers never block a grab
            Rigidbody bestB = null;
            var piv0 = _pilot.CameraPivot;
            if (piv0 == null) return;

            // while carrying an ink ore, E belongs to InkRuneStone (feed/drop)
            if (InkRuneStone.Carried != null) return;

            // mask out layer 2 (own body) and the ink-canvas layer, like every
            // world-purpose cast
            int mask = Physics.DefaultRaycastLayers & ~(1 << InkCanvasLayer.Layer)
                & ~(1 << VesselShell.Layer); // the true-bowl follower is a kinematic ghost - grabbing must reach the POT behind it
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
                    // any state grabs while still a spell; once touched, only
                    // SOLID grabs again. A refused blob never blocks the ray.
                    if (blob.Touched && blob.Phase != MatterPhase.Solid)
                    { stateRefused = blob; continue; }
                    aimed = h; foundHit = true; // the blob IS what you aimed at
                    break;
                }
                // ink ores answer to their own E (take/feed/drop, aim-bid in
                // InkRuneStone) - the grab never competes for that press
                if (h.collider.GetComponentInParent<InkRuneStone>() != null) return;
                if (h.collider.isTrigger) continue; // invisible zones don't block aim
                aimed = h; foundHit = true;
                break;
            }
            if (!foundHit)
            {
                if (stateRefused != null)
                    DrawingWorld.Instance?.LogEvent(
                        $"the {stateRefused.Material} has been handled. only a SOLID grabs again");
                return;
            }

            // CLIENT: the host owns physics - ship the grab as an intent and
            // hold remotely; PropSnap/MatterSnap bring the motion back (netcode §4)
            if (!NetGame.IsAuthority)
            {
                var proxyBlob = aimed.collider.GetComponentInParent<NetMatterProxy>();
                // a creature stand-in has no scene path the host knows: its snapshot id names it
                var zp = aimed.collider.GetComponentInParent<NetZombieProxy>();
                var gp = aimed.collider.GetComponentInParent<NetGolemProxy>();
                int creature = zp != null ? zp.Id : gp != null ? gp.Id : 0;
                // a prop is read by the host's own rule first: a lift the host would refuse never
                // starts here (the table rose and fell on this screen alone while the host said no)
                if (proxyBlob == null && creature == 0
                    && !MayLift(aimed.collider, Grimoire.LocalPlayerId, out _, out var why, out _))
                {
                    DrawingWorld.Instance?.LogEvent(why);
                    return;
                }
                NetSync.SendGrabIntent(proxyBlob != null ? proxyBlob.HostId : 0,
                    proxyBlob != null || creature != 0 ? "" : NetSync.PathOf(aimed.collider.transform),
                    aimed.distance, creature, (byte)(zp != null ? 1 : gp != null ? 2 : 0));
                var cargoRb = aimed.collider.attachedRigidbody;
                BeginRemoteHold(Mathf.Clamp(aimed.distance, 0.7f, LiftRangeMax),
                    cargoRb != null ? cargoRb.transform : aimed.collider.transform);
                return;
            }

            bestB = AcquireBody(aimed.collider, Grimoire.LocalPlayerId);
            if (bestB == null) return;

            _heldBody = bestB;
            _heldBody.GetComponentInParent<Golem>()?.BeCarried(); // no resisting, no hopping
            NetSync.TrackProp(bestB); // clients follow the lifted prop (netcode §4)
            // cache the ledgers ONCE - the subtree doesn't change mid-hold
            _heldMarks = bestB.GetComponentsInChildren<InkMark>(true);
            _slotAtGrab = _slots != null ? _slots.Current : 1;
            _spinRot = Quaternion.identity;
            var pv1 = _pilot.CameraPivot;
            // a body with no enabled colliders reports its pivot as center of
            // mass - re-teach the true middle from bounds
            bool hasBody = false;
            foreach (var c in bestB.GetComponentsInChildren<Collider>(true))
                if (c != null && c.enabled && !c.isTrigger) { hasBody = true; break; }
            if (!hasBody)
                bestB.centerOfMass = bestB.transform.InverseTransformPoint(
                    ShapeShift.FindObjectBounds(bestB.transform).center);

            // clamp to LiftRangeMax, not GrabRange - the hold keeps the grab distance
            if (pv1 != null) _holdDist = Mathf.Clamp(
                Vector3.Distance(pv1.position, bestB.worldCenterOfMass), 0.7f, LiftRangeMax);
            _heldHadGravity = bestB.useGravity;
            _prevInterp = bestB.interpolation;
            _prevAngDamp = bestB.angularDamping;
            _prevLinDamp = bestB.linearDamping;
            _grabRelRot = Quaternion.Inverse(YawRot()) * bestB.rotation;
            _prevMaxSpin = bestB.maxAngularVelocity;
            bestB.interpolation = RigidbodyInterpolation.Interpolate;
            bestB.angularDamping = 4f;
            bestB.maxAngularVelocity = HeldMaxSpin;

            // floating cargo barely weighs on the carrier
            var board = _pilot != null ? _pilot.GetComponent<BodyState>() : null;
            if (board != null) board.CarriedWeight = CarryWeight(bestB.mass);

            float auth0 = AuthorityOver(bestB, _heldMarks, out _);
            float needInk = bestB.mass * DrawingConfig.LiftInkPerKg;
            float haveInk = InkMark.AuthorityIn(_heldMarks, Grimoire.LocalPlayerId);
            DrawingWorld.Instance?.LogEvent(auth0 >= 1f
                ? "it lifts free. alt + left-drag turns it · F drops · E throws"
                : $"too heavy to lift. your ink is {haveInk:0} of {needInk:0}. draw more on it");
        }

        /// One acquire law for the local grab and the host's GrabIntent (netcode §4).
        /// CanAcquire = AcquireBody with mutations and logs stripped; AcquireBody
        /// returns the freed body or null. Pot ink only pours - the pot is stealable.
        static bool IsPotInk(Collider c)
            => c != null && CauldronEconomy.IsInk(c.transform);

        /// The pot holding the ink lifts for its own side with no ink on it:
        /// clean for wizards, green for acolytes. A client cannot tell which
        /// pot holds the ink, so it predicts for any pot; the host decides.
        static bool OwnPot(Transform t, int ownerId)
        {
            if (t == null || ownerId < 0) return false;
            var pot = t.GetComponentInParent<CauldronEconomy>();
            if (pot == null || (NetGame.IsAuthority && pot != CauldronEconomy.Active)) return false;
            return Teams.OfOwner(ownerId) == (CauldronEconomy.IsCorrupt ? Team.Acolyte : Team.Wizard);
        }

        /// The lift rule, read only: false when the aim would be refused. The badge asks it,
        /// a client's hand asks it before the host is asked, AcquireBody acts on it.
        public static bool CanAcquire(Collider aimedCollider, int ownerId)
            => MayLift(aimedCollider, ownerId, out _, out _, out _);

        /// Why the last AcquireBody said no: the host's answer to a friend's ask.
        public static string LastRefusal { get; private set; }
        public static void ForgetRefusal() => LastRefusal = null;

        /// The one lift rule. hitRb = the body the aim would take, null = rooted scenery to tear
        /// out; why = the refusal in the player's words. freesKinematic: a rooted body carrying any
        /// of the owner's ink goes dynamic even when the lift itself is refused (a little ink makes
        /// it a real object, enough ink lifts it).
        public static bool MayLift(Collider aimedCollider, int ownerId, out Rigidbody hitRb, out string why,
            out bool freesKinematic)
        {
            hitRb = null; why = ""; freesKinematic = false;
            if (aimedCollider == null) { why = "nothing to lift"; return false; }
            if (IsPotInk(aimedCollider)) { why = "the ink only pours. drink it at the pot"; return false; }
            // a disabled collider (a build boxes an unreadable mesh and switches the mesh collider off)
            // reports no body at all: ask the hierarchy, as physics would for an enabled one
            hitRb = aimedCollider.attachedRigidbody;
            if (hitRb == null) hitRb = aimedCollider.GetComponentInParent<Rigidbody>();

            // never a wizard, creature or held weapon. Zombies are liftable (draw on one, lift it
            // like a barrel); demons are exempt.
            var zomb = aimedCollider.GetComponentInParent<Zombie>();
            var glm = aimedCollider.GetComponentInParent<Golem>();
            bool liftableCreature = (zomb != null && !zomb.IsDemon)
                || (glm != null && glm.OwnerId == ownerId); // YOUR OWN golem lifts easily
            if (aimedCollider.GetComponentInParent<SimpleFPSController>() != null
                || (!liftableCreature && (aimedCollider.GetComponentInParent<Creature>() != null || glm != null)) // a living object is a golem too
                || aimedCollider.GetComponentInParent<BossMark>() != null // a boss is never carried (his call)
                || aimedCollider.GetComponentInParent<HeldWeapon>() != null)
            {
                why = $"you can't lift {aimedCollider.name}";
                return false;
            }
            // one pair of hands at a time: the first keeps it, a second lifter is refused
            if (hitRb != null && HeldByAnother(hitRb))
            {
                why = $"{hitRb.name} is in someone's hands";
                return false;
            }

            if (hitRb != null)
            {
                // a kinematic body cannot be moved by velocity - with enough ink it becomes dynamic instead
                if (hitRb.isKinematic)
                {
                    // world-scale machinery never becomes a free body, whatever the ink. Same cap as tear-loose.
                    if (Liftable.WorldScale(hitRb.transform, out var kd))
                    {
                        why = $"the world itself refuses: {hitRb.name} is {kd.x:0.#}×{kd.y:0.#}×{kd.z:0.#}m of world, not a prop";
                        return false;
                    }
                    if (!OwnPot(hitRb.transform, ownerId) && InkMark.AuthorityIn(hitRb.transform, ownerId) <= 0f)
                    {
                        why = $"no ink on {hitRb.name}, draw on it to lift it";
                        return false;
                    }
                    freesKinematic = true;
                }
                if (AuthorityFor(hitRb, hitRb.GetComponentsInChildren<InkMark>(true), ownerId, out _) <= 0f)
                {
                    float have = InkMark.AuthorityIn(hitRb.transform, ownerId);
                    why = $"{hitRb.name}: your ink {have:0}, needs {hitRb.mass * DrawingConfig.LiftInkPerKg:0}. draw more on it";
                    return false;
                }
                return true;
            }

            // rooted scenery: size decides world vs prop, ink never overrules it, then the anchor
            var host = InkMark.Host(aimedCollider.transform);
            var lift = host.GetComponentInParent<Liftable>();
            if (lift != null) host = lift.transform;
            if (Liftable.WorldScale(host, out var wd))
            {
                why = $"the world itself refuses: {host.name} is {wd.x:0.#}×{wd.y:0.#}×{wd.z:0.#}m of world, not a prop";
                return false;
            }
            float mine = InkMark.AuthorityIn(host, ownerId);
            float hold = lift != null ? lift.HoldStrength : InkMark.AnchorHold(host);
            if (mine < hold && !OwnPot(aimedCollider.transform, ownerId))
            {
                why = $"it is rooted. your ink is {mine:0} of {hold:0} needed";
                return false;
            }
            return true;
        }

        /// In a hand that is not this one: the host's own, a friend's through the host, or a
        /// friend's as their presence tells it on a client.
        public static bool HeldByAnother(Rigidbody rb)
        {
            if (rb == null) return false;
            if (LocalHeldBody == rb) return true;
            if (NetGame.IsHost) return NetSync.IsRemoteHeld(rb);
            foreach (var a in NetAvatar.All)
            {
                if (a == null) continue;
                var h = a.HeldTransform;
                if (h == null) continue;
                if (h == rb.transform || h.IsChildOf(rb.transform) || rb.transform.IsChildOf(h)) return true;
            }
            return false;
        }

        public static Rigidbody AcquireBody(Collider aimedCollider, int ownerId)
        {
            bool may = MayLift(aimedCollider, ownerId, out var hitRb, out var why, out bool frees);
            LastRefusal = may ? null : why;
            // a rooted body with any of the owner's ink becomes a physics object here, lift or no lift
            if (frees && hitRb != null)
            {
                Liftable.MakePhysicsLegal(hitRb.transform);
                hitRb.isKinematic = false;
                var lf = hitRb.GetComponent<Liftable>();
                if (lf != null) lf.Rooted = false;
            }
            if (!may)
            {
                // the local hand hears why; a friend's ask is answered through the host's Ack
                if (ownerId == Grimoire.LocalPlayerId) DrawingWorld.Instance?.LogEvent(why);
                return null;
            }
            if (hitRb != null)
            {
                WakeRiders(hitRb);
                return hitRb;
            }

            // TEAR IT OUT OF THE GROUND: rooted scenery must have its
            // anchor overpowered first, then it's a real object forever.
            var host = InkMark.Host(aimedCollider.transform);
            var lift = host.GetComponentInParent<Liftable>();
            if (lift != null) host = lift.transform;

            // it becomes a physics object HERE, at the moment it's freed
            Rigidbody freed;
            if (lift != null) freed = lift.TearLoose();
            else
            {
                // same legality pass as Liftable: a concave mesh collider
                // would make the freed prop fall through the world
                Liftable.MakePhysicsLegal(host);
                // a body it already has is the body: adding a second one returns nothing
                freed = host.GetComponent<Rigidbody>();
                if (freed == null)
                {
                    freed = host.gameObject.AddComponent<Rigidbody>();
                    freed.mass = Mathf.Max(0.2f, InkMark.EstimateMass(host));
                }
                freed.isKinematic = false;
                freed.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                freed.interpolation = RigidbodyInterpolation.Interpolate;
            }
            if (freed == null) return null;
            DrawingWorld.Instance?.LogEvent("it tears free of the ground");
            WakeRiders(freed);
            return freed;
        }

        /// Whatever rests on a body that goes free goes free with it: a kinematic
        /// book would hang where the table was and block the lift. Runs where the
        /// body is acquired, so the host does it for everyone; what wakes rides
        /// PropSnap like the lifted thing itself.
        public static void WakeRiders(Rigidbody body, int depth = 0)
        {
            if (body == null || depth > 3) return;
            Bounds b = ShapeShift.FindObjectBounds(body.transform);
            if (b.size.sqrMagnitude < 1e-6f) return;
            b.Expand(new Vector3(0.1f, 0f, 0.1f));
            b.Encapsulate(b.max + Vector3.up * 0.35f);
            var hits = Physics.OverlapBox(b.center, b.extents, Quaternion.identity,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var c in hits)
            {
                var rb = c.attachedRigidbody;
                if (rb == null || rb == body || !rb.isKinematic) continue;
                if (rb.GetComponent<Element>() == null && rb.GetComponent<Liftable>() == null) continue;
                if (rb.GetComponentInParent<SimpleFPSController>() != null
                    || rb.GetComponentInParent<CharacterRig>() != null
                    || rb.GetComponentInParent<Creature>() != null
                    || rb.GetComponentInParent<HeldWeapon>() != null
                    || rb.GetComponentInParent<CauldronEconomy>() != null
                    || rb.GetComponentInParent<VesselShell>() != null) continue;
                if (Liftable.WorldScale(rb.transform, out _)) continue;
                Liftable.MakePhysicsLegal(rb.transform);
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.WakeUp();
                var lf = rb.GetComponent<Liftable>();
                if (lf != null) lf.Rooted = false;
                NetSync.TrackProp(rb);
                WakeRiders(rb, depth + 1);
            }
        }

        // ---------------------------------------------- remote hold (client) --
        bool _remoteHolding;   // this machine holds via host intents (netcode §4)
        float _aimStream;      // 10 Hz LiftAim throttle
        Transform _remoteCargo; // the proxy/prop aimed at: predicted into the hand, named in the presence

        void BeginRemoteHold(float dist, Transform cargo)
        {
            _remoteHolding = true;
            _remoteCargo = cargo;
            _holdDist = dist;
            _slotAtGrab = _slots != null ? _slots.Current : 1;
            _spinRot = Quaternion.identity;
            _grabRelRot = Quaternion.identity;
            DrawingWorld.Instance?.LogEvent("held through the host. F drops · E throws");
        }

        static HandGrab _localGrab;
        float _cargoGoneFor; // CLIENT: how long the held stand-in has been missing

        /// The host said no (no ink, world-scale, gone) - open the hand again.
        public static void RemoteHoldRefused(string why)
        {
            if (_localGrab == null || !_localGrab._remoteHolding) return;
            _localGrab._remoteHolding = false;
            _localGrab._remoteCargo = null;
            _localGrab.CarryOnBody(0f);
            DrawingWorld.Instance?.LogEvent(string.IsNullOrEmpty(why) ? "the host refused the grab" : why);
        }

        /// The host took the grab: the cargo weighs on this body as a local lift does.
        public static void RemoteHoldTaken(float mass)
        {
            if (_localGrab == null || !_localGrab._remoteHolding) return;
            _localGrab.CarryOnBody(CarryWeight(mass));
        }

        /// Floating cargo barely weighs on the carrier.
        static float CarryWeight(float mass) => mass / 420f;

        void CarryOnBody(float weight)
        {
            var board = _pilot != null ? _pilot.GetComponent<BodyState>() : null;
            if (board != null) board.CarriedWeight = weight;
        }

        // ------------------------------------------------- throwing/dropping --
        void Throw()
        {
            var piv = _pilot.CameraPivot;
            Vector3 dir = piv != null ? piv.forward : transform.forward;
            if (_remoteHolding)
            {
                _remoteHolding = false;
                _remoteCargo = null;
                CarryOnBody(0f);
                NetSync.SendThrowIntent(dir, HandPoint()); // the host does the physics (netcode §4)
                return;
            }
            // in the air it is drawn to the nearest enemy ahead of it (SpellParticle.Pull, Homing)
            if (_heldParticle != null)
            {
                var p = _heldParticle;
                _heldParticle = null;
                DrawingWorld.Instance?.LogEvent($"threw the {p.name}");
                p.ReleaseHeld(dir * ThrowSpeed); // the push ability, down your own cursor
                p.PrimeToBlow(transform); // detonates on impact; the thrower is briefly immune
                // E wakes it for real (his rule): the awakening gust pushes you
                // off, then it flies where you threw it, alive and dangerous
                SpellKick.Apply(p, dir, _pilot.transform, Grimoire.LocalPlayerId);
                p.Wake();
            }
            else if (_heldBody != null)
            {
                var b = _heldBody;
                ClearBodyHold();
                // a conjured rock flies faster than a prop (his law)
                var sm = b.GetComponent<Matter>();
                float mul = sm != null && sm.SpellBorn ? DrawingConfig.SpellThrowMul : 1f;
                b.AddForce(dir * ThrowImpulse * mul, ForceMode.VelocityChange);
                Homing.Arm(b, Grimoire.LocalPlayerId);
            }
        }

        void DropHeld(Vector3 extra, bool wake = false)
        {
            if (_remoteHolding)
            {
                _remoteHolding = false;
                _remoteCargo = null;
                CarryOnBody(0f);
                NetSync.SendDropIntent(wake); // the host lets go, or wakes it in the hand on F (netcode §4)
                return;
            }
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
            if (_heldBody != null)
            {
                _heldBody.GetComponentInParent<Golem>()?.BeReleased(); // wakes back up on release (as ReleaseHeldBody)
                // it must not rocket off on release - the hold drives
                // velocity directly, so hand it back to physics calm
                if (!_heldBody.isKinematic)
                {
                    _heldBody.linearVelocity = Vector3.ClampMagnitude(_heldBody.linearVelocity, 4f);
                    _heldBody.angularVelocity = Vector3.ClampMagnitude(_heldBody.angularVelocity, 4f);
                }
                _heldBody.useGravity = _heldHadGravity;
                _heldBody.interpolation = _prevInterp;
                _heldBody.maxAngularVelocity = _prevMaxSpin;
                _heldBody.angularDamping = _prevAngDamp;
                _heldBody.linearDamping = _prevLinDamp;
                var m = _heldBody.GetComponent<Matter>();
                if (m != null) m.Touched = true; // TOUCH = WORLD - it's an object now
            }
            var board = _pilot != null ? _pilot.GetComponent<BodyState>() : null;
            if (board != null) board.CarriedWeight = 0f; // arms free again
            _heldBody = null;
            _heldMarks = null;
        }
    }
}
