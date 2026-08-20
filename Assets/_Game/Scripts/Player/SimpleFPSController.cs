using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Graybox first-person controller: WASD + mouse look + jump, pushes rigidbodies
    /// (needed to shove crates together and close proximity seals). Holding LeftAlt
    /// frees the cursor for precision drawing and freezes the view.
    [RequireComponent(typeof(CharacterController))]
    public class SimpleFPSController : MonoBehaviour
    {
        public Transform CameraPivot;
        /// Third-person framing anchor. Put an empty on the ROOT at chest
        /// height - NEVER on a bone, or a held pose drags it and the camera
        /// floats off the body again. Empty = falls back to root + 1.15m.
        public Transform CameraTarget;
        public float MoveSpeed = 4.5f;
        public float SprintSpeed = 7f;
        public float JumpSpeed = 4.6f;
        public float Gravity = -14f;
        public float LookSensitivity = 0.12f;
        public float PushStrength = 1.6f;

        /// Live player registry - replaces FindObjectsByType in per-frame paths.
        public static readonly System.Collections.Generic.List<SimpleFPSController> All
            = new System.Collections.Generic.List<SimpleFPSController>();

        CharacterController _cc;
        float _pitch;
        float _verticalVelocity;
        bool _wasGrounded = true; // fall-damage edge detection
        float _airTime;           // continuous seconds off the ground
        float _tumbleRecover;     // the flop-on-the-floor beat after a tumble

        /// Airborne past AirTumbleSeconds: the body ragdolls for the rest of
        /// the arc (jumps never trigger it). Ends a short flop after touchdown.
        public bool IsAirTumbling { get; private set; }

        /// Camera pitch in degrees (+ = looking down) - the rig bends the
        /// head/neck to match, and NetSync ships it. Zero in third person.
        public float LookPitch => ThirdPersonActive ? 0f : _pitch;

        BodyState _body;          // the slider board - speed/jump/gravity all read it
        Rigidbody _ragdollFollow; // CharacterRig hands us the hips while ragdolling

        /// While ragdolling the capsule chases the doll, so the camera follows
        /// the body and the get-up happens where it actually landed.
        public void SetRagdollFollow(Rigidbody hips) => _ragdollFollow = hips;
        bool _wasPrecision;
        Vector3 _shove; // external impulse (zombie swipes, explosions) - decays fast

        // ---- crouch: hold LeftCtrl (gamepad East). Crouch-jump springs a
        // bit higher - enough to clear a window sill.
        public bool IsCrouched { get; private set; }
        float _crouchHeight = 1.15f; // refit by CharacterRig to the real model
        float _standHeight, _camStandY;
        float _camY;        // current eye height (crouch moves it)
        Vector3 _eyeAnchorLocal; // head-bone-tracked eye point (player-local), fed by CharacterRig
        bool _eyeAnchorFresh;    // consumed each frame - stale anchors (ragdoll) fall back to static
        float _camForward;  // eye offset in FRONT of the body axis - rotates
                            // with pitch so your own head/torso never clip in
        Vector3 _standCenter;

        // ---- TAB = third person: camera above and behind. 1-9 emote,
        // R paints the body, B opens the pose editor. No world drawing there.
        public static bool ThirdPersonActive { get; private set; }
        Camera _cam;
        Vector3 _camDefaultLocal;
        int _revivePctShown = int.MinValue; // revive prompt cache - no per-frame string build
        string _revivePrompt;
        GooglyEyes _eyes;
        WeaponSlots _slots;
        EmotePlayer _emotes;

        // sustained spell forces (Direction/Density zones) - this is FLIGHT:
        // a force seal drawn on your own feet feeds this every frame and you fly
        Vector3 _spellVel;
        float _spellFedAt = -999f; // last moment a zone actually pushed
        float _fallFor;            // seconds tumbling in air without gaining height
        float _prevY;

        /// Called by spell zones each frame they act on the player.
        public void AddSpellForce(Vector3 accel, float dt)
        {
            _spellFedAt = Time.time; // the engine is still burning
            _spellVel += accel * dt;
            // terminal velocity for spell flight
            _spellVel = Vector3.ClampMagnitude(_spellVel, 34f);
        }

        public float Health = 100f;

        /// Real motion this frame (the CC's own measurement) - liquids read it
        /// to apply viscous drag against the direction you're actually moving.
        public Vector3 Velocity => _cc != null ? _cc.velocity : Vector3.zero;

        /// Raw ground contact - the animation rig smooths its own airborne
        /// signal from this (slope flicker is the caller's problem).
        public bool IsGrounded => _cc != null && _cc.isGrounded;

        /// E takes what you are looking at within reach. Returns how centred
        /// the point is (1 = dead centre), or -1 when out of range, outside
        /// the cone, or behind something. Every E interaction should ask this.
        public float AimScore(Vector3 point, float range, float cone = 0.78f, Transform self = null)
        {
            Vector3 eye = CameraPivot != null ? CameraPivot.position : transform.position + Vector3.up * 1.4f;
            Vector3 look = CameraPivot != null ? CameraPivot.forward : transform.forward;

            Vector3 to = point - eye;
            float dist = to.magnitude;
            if (dist > range || dist < 1e-3f) return -1f;

            float aim = Vector3.Dot(look, to / dist);
            if (aim < cone) return -1f;

            // no reaching through walls
            if (Physics.Raycast(eye, to / dist, out var hit, dist,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (self == null) return -1f;
                if (hit.transform != self && !hit.transform.IsChildOf(self)
                    && !self.IsChildOf(hit.transform)) return -1f;
            }
            return aim;
        }

        /// Shift held and actually moving: no shift = walk look, shift = run
        /// look, regardless of true ground speed.
        public bool IsSprinting { get; private set; }

        // ---- knockdown sprawl: camera keels over, control cuts out, momentum
        // slides you, then you stagger back up. Never while properly downed.
        float _knockLeft;
        public bool IsSprawled => _knockLeft > 0f;

        public void KnockDown(float seconds)
        {
            if (IsDowned) return;
            _knockLeft = Mathf.Max(_knockLeft, seconds);
        }

        // ---- glued boots: lvl2 grip and the time zone hold you in place -
        // no walking or jumping. Re-applied on a beat while you remain in it.
        float _feetStuckUntil;
        public bool FeetStuck => Time.time < _feetStuckUntil;
        public void StickFeet(float seconds) =>
            _feetStuckUntil = Mathf.Max(_feetStuckUntil, Time.time + seconds);
        public void UnstickFeet() => _feetStuckUntil = 0f;

        // ---- downed / revive ----
        public bool IsDowned { get; private set; }
        public bool IsDead => IsDowned && _bleedOut <= 0f;
        public float BleedOut => _bleedOut;
        public float ReviveProgress { get; private set; }
        float _bleedOut;
        GameObject _heartFx; // broken heart over the downed body
        bool _soulFled;      // Souls Escape fired once for this death

        void OnDeathCrossed()
        {
            if (_soulFled || !IsDead) return;
            _soulFled = true;
            if (_heartFx != null) { Destroy(_heartFx); _heartFx = null; }
            if (FxLibrary.I != null) // the soul leaves the body
                FxLibrary.Spawn(FxLibrary.I.SoulsOut, transform.position + Vector3.up * 1.2f);

            // a corrupt body gasses off like a detonated zombie, shove and
            // all - poison damages only wizards, the push throws everyone.
            if (Sides.IsAcolytePlayer(this))
            {
                Vector3 at = transform.position + Vector3.up * 0.9f;
                PoisonField.Open(at, DrawingConfig.PoisonDeathRadius,
                    DrawingConfig.PoisonDeathSeconds);
                Shove.Blast(at, DrawingConfig.PoisonDeathRadius,
                    DrawingConfig.DetonateShove, 0f, "a dying acolyte burst");
            }
        }

        /// Physical hits shove the player and chip health. At 0 HP the ghost
        /// rises at once; revival is the ghost flying home, with a teammate
        /// meeting it there in matches.
        // every hit has a name; small chips roll up into one console line
        // every couple of seconds: "hurt: standing in fire −6.2"
        static readonly System.Collections.Generic.Dictionary<string, float> _dmgLog =
            new System.Collections.Generic.Dictionary<string, float>();
        static float _dmgFlushAt;

        static void NoteDamage(string cause, float amount)
        {
            _dmgLog.TryGetValue(cause, out float sum);
            _dmgLog[cause] = sum + amount;
            if (Time.time < _dmgFlushAt) return;
            _dmgFlushAt = Time.time + 2f;
            var parts = new System.Text.StringBuilder("hurt: ");
            bool first = true;
            foreach (var kv in _dmgLog)
            {
                if (!first) parts.Append(" · ");
                parts.Append($"{kv.Key} −{kv.Value:0.0}");
                first = false;
            }
            _dmgLog.Clear();
            DrawingWorld.Instance?.LogEvent(parts.ToString());
        }

        public void TakeHit(Vector3 impulse, float damage, string cause = null)
        {
            if (IsDowned)
            {
                _bleedOut -= 1.5f; // hits while downed accelerate the bleed-out
                OnDeathCrossed();
                return;
            }
            _shove += impulse;
            Health -= damage;
            _lastHurt = Time.time;
            if (damage > 0f) NoteDamage(cause ?? "hit", damage);
            // DoT ticks show on the hurt vignette only; the camera shakes for
            // real blows, on a cooldown, gentler while the pen is down
            if (damage >= 5f && Time.time - _lastFeelShake > 0.6f)
            {
                _lastFeelShake = Time.time;
                Juice.Thud(transform.position);
                Juice.Shake(SurfaceDrawer.IsPenActive ? 0.18f : 0.35f, 0.25f);
                Debug.Log($"[SpellyZombie] Player hit! {Mathf.Max(0, Health):0} hp");
            }
            if (damage >= 15f) KnockDown(1.1f); // big hits floor you
            if (Health <= 0f)
            {
                // the lobby kills too, so death and ghosts are testable there
                if (RoundDirector.RunActive || ActiveScene.Name == "Lobby") GoDown();
                else
                {
                    Health = Sides.MaxHealthFor(Grimoire.LocalPlayerId); // sandbox mercy respawn-in-place
                    Debug.Log("[SpellyZombie] Player DOWN - shaking it off (sandbox)");
                }
            }
        }
        float _lastHurt;
        float _lastFeelShake; // camera-shake cooldown - DoT ticks must not machine-gun the view

        /// FallCatcher arrival: floored, revivable, never kills outright.
        public void DropDowned()
        {
            if (!IsDowned && RoundDirector.RunActive) GoDown();
        }

        /// Straight to dead - skips the sandbox mercy and the bleed-out.
        public void DieOutright()
        {
            if (!IsDowned) GoDown();
            _bleedOut = 0f;
            OnDeathCrossed();
        }

        /// Teleports (fall catch) wipe all carried motion - otherwise the
        /// fall's speed lands WITH you and the next frame deals its damage.
        public void CancelMomentum()
        {
            _verticalVelocity = -1f;
            _spellVel = Vector3.zero;
            _shove = Vector3.zero;
            _airTime = 0f;
        }

        void GoDown()
        {
            IsDowned = true;
            Health = 1f;
            // no bleed-out anywhere: death goes straight to ghost, and the
            // ghost flying home is the revive flow allies join in on
            _bleedOut = 0f;
            OnDeathCrossed();
            ReviveProgress = 0f;
            WorldEvents.Report(WorldEventKind.Death, transform.position, 2f); // the horde celebrates
            Juice.Sting(transform.position);
            Juice.Shake(0.8f, 0.5f);
            Juice.HitStop(0.2f, 0.25f);
            // a broken heart floats over the crawling body - readable at range, no HUD
            _soulFled = false;
            if (_heartFx == null && FxLibrary.I != null)
                _heartFx = FxLibrary.Spawn(FxLibrary.I.BrokenHeart, transform.position + Vector3.up * 2.1f, transform);
            Debug.Log("[SpellyZombie] Player DOWNED - teammate hold E to revive");
        }

        /// A teammate is holding E over this body.
        public void AddRevive(float dt)
        {
            if (!IsDowned || IsDead) return;
            ReviveProgress += dt / DrawingConfig.ReviveSeconds;
            if (ReviveProgress >= 1f) Revive();
        }

        public void Revive()
        {
            IsDowned = false;
            Health = 50f;
            ReviveProgress = 0f;
            _soulFled = false;
            if (_heartFx != null) { Destroy(_heartFx); _heartFx = null; } // mended
            _lastHurt = Time.time;
            // gravity kept integrating into _verticalVelocity while down -
            // wipe it or the first Move slams the fresh revive back to 1 hp
            CancelMomentum();
            Debug.Log("[SpellyZombie] Player revived!");
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            // liquid Matter lives on the Water layer (4): the capsule never
            // collides with it - you WADE through puddles, never bump them
            // (the LiquidVolume shell applies the slow/current instead)
            _cc.excludeLayers |= 1 << 4;
            _standHeight = _cc.height;
            _standCenter = _cc.center;
            _camStandY = CameraPivot != null ? CameraPivot.localPosition.y : 1.6f;
            _camY = _camStandY;
            LookSensitivity = PlayerPrefs.GetFloat("sz_look_sens", LookSensitivity);
            LockCursor();

            // every caster has an identity; seals are owned by whoever completes them.
            // Connected, the stable ClientId-derived owner id IS the identity -
            // machine-local instance ids meant nothing on other machines (netcode §0).
            Grimoire.LocalPlayerId = NetGame.Connected && NetSync.LocalOwnerId >= 0
                ? NetSync.LocalOwnerId : gameObject.GetInstanceID();

            // body ink never expires: the player class carries its own marker
            // so no scene setup can forget it
            if (GetComponent<PersistentInkSurface>() == null)
                gameObject.AddComponent<PersistentInkSurface>();

            // the player is googly too - other players and clips see the eyes react
            _eyes = GooglyEyes.Attach(transform, 1.55f, 1.4f);
            if (CameraPivot != null)
            {
                _cam = CameraPivot.GetComponentInChildren<Camera>();
                if (_cam != null) _camDefaultLocal = _cam.transform.localPosition;
            }
            // eyes stay visible in every mode: the camera rides in front of
            // the face, so they are always behind the lens
            _eyes.SetVisible(true);
            ThirdPersonActive = false;

            if (GetComponent<PlayerInk>() == null) gameObject.AddComponent<PlayerInk>();
            _slots = GetComponent<WeaponSlots>();
            if (_slots == null) _slots = gameObject.AddComponent<WeaponSlots>();
            if (GetComponent<SelfPaint>() == null) gameObject.AddComponent<SelfPaint>();
            if (GetComponent<HandGrab>() == null) gameObject.AddComponent<HandGrab>();
            if (GetComponent<GrimoireAbsorb>() == null) gameObject.AddComponent<GrimoireAbsorb>();
            _body = GetComponent<BodyState>();
            if (_body == null) _body = gameObject.AddComponent<BodyState>(); // the slider board
            if (GetComponent<CharacterRig>() == null) gameObject.AddComponent<CharacterRig>();
            if (GetComponent<PoseGrab>() == null) gameObject.AddComponent<PoseGrab>();

            // spell physics speaks Damageable - bridge it into the controller's
            // health. The bridge never dies itself; the controller owns downs.
            var dmg = GetComponent<Damageable>();
            if (dmg == null) dmg = gameObject.AddComponent<Damageable>();
            dmg.Health = float.MaxValue;
            dmg.Destructible = false;
            dmg.OnDamaged = (amount, cause) => TakeHit(Vector3.zero, amount);

            All.Add(this);
        }

        void OnDestroy() => All.Remove(this);

        /// Where the static first-person eye point sits right now (world) -
        /// CharacterRig calibrates its head-bone camera anchor against this.
        public Vector3 EyeCenterWorld => transform.TransformPoint(new Vector3(0f, _camY, 0f));

        /// This body is the local viewer (its camera is live). Cached _cam.
        public bool IsLocalViewer => _cam != null && _cam.isActiveAndEnabled;

        /// CharacterRig feeds the head-bone eye point every frame so the
        /// camera moves with the face. Must be re-fed each frame - a stale
        /// anchor falls back to the static eye height.
        public void SetEyeAnchor(Vector3 world)
        {
            _eyeAnchorLocal = transform.InverseTransformPoint(world);
            _eyeAnchorFresh = true;

            // runs in LateUpdate (post-animation) - snap hard here so the
            // face can't outrun the lens on a sprint start
            if (_cam != null && !ThirdPersonActive && !SelfPaint.IsActive
                && !PoseGrab.IsOpen && CameraPivot != null && _camForward > 0f)
            {
                Vector3 fpTarget = _eyeAnchorLocal
                    + CameraPivot.localRotation * new Vector3(0f, 0f, _camForward);
                _cam.transform.localPosition = Vector3.Lerp(
                    _cam.transform.localPosition, fpTarget, Time.deltaTime * 30f);
            }
        }

        /// Fits capsule height/radius (feet stay planted), eye height, crouch
        /// proportion. eyeForward pushes the camera just in front of the face.
        public void FitBody(float height, float radius, float eyeLocalY, float eyeForward = 0f)
        {
            if (_cc == null) return;
            float bottom = _cc.center.y - _cc.height * 0.5f;
            _cc.height = height;
            _cc.radius = radius;
            _cc.center = new Vector3(0f, bottom + height * 0.5f, 0f);
            _standHeight = height;
            _standCenter = _cc.center;
            _crouchHeight = height * 0.62f; // still clears window sills on a crouch-jump
            _camStandY = eyeLocalY;
            _camY = eyeLocalY;
            _camForward = eyeForward;
            if (CameraPivot != null)
                CameraPivot.localPosition = new Vector3(0f, eyeLocalY, eyeForward);
        }

        /// CharacterRig moved the googly eyes onto the head bone - adopt them.
        public void ReplaceEyes(GooglyEyes fresh)
        {
            if (_eyes != null) Destroy(_eyes.gameObject);
            _eyes = fresh;
            if (_eyes != null) _eyes.SetVisible(true); // behind the lens, never in it
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // frozen while posing in the studio, paused, or typing into a UI
            // field (the IP box must not steer the wizard); they own the input
            if (PoseStudio.IsOpen || GameMenu.IsOpen || UIKit.Typing) return;

            // ---- cursor handling ----
            // TAB flips the camera; either direction lands on the plain idle
            if (kb.tabKey.wasPressedThisFrame && !IsDowned && !SelfPaint.IsActive
                && ShapeShift.ThirdPersonAllowed) // acolyte with no stored shape: refused
                ToggleThirdPerson();
            if (_cam != null && !SelfPaint.IsActive && !PoseGrab.IsOpen) // easel modes own the camera
            {
                if (ThirdPersonActive)
                {
                    // boom up and back, camera looks at the body. Boom in ROOT
                    // space, never pivot space - the pivot rides the head bone
                    // and a held pose would drag the boom with it
                    Vector3 boom = transform.position + Vector3.up * 1.15f
                        - Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Vector3.forward * 3.6f;
                    _cam.transform.position = Vector3.Lerp(_cam.transform.position,
                        boom, Time.deltaTime * 8f);
                    var look = Quaternion.LookRotation(
                        transform.position + Vector3.up * 0.35f - _cam.transform.position,
                        Vector3.up);
                    _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, look,
                        Time.deltaTime * 8f);
                }
                else
                {
                    // rotation belongs to the look block in first person. The
                    // eye-forward offset pitches with the view; the eye point
                    // rides the head bone when the rig feeds one.
                    Vector3 eyeLocal = _eyeAnchorFresh ? _eyeAnchorLocal : new Vector3(0f, _camY, 0f);
                    _eyeAnchorFresh = false;
                    Vector3 fpTarget = _camForward > 0f
                        ? eyeLocal + CameraPivot.localRotation * new Vector3(0f, 0f, _camForward)
                        : _camDefaultLocal;
                    _cam.transform.localPosition = Vector3.Lerp(_cam.transform.localPosition,
                        fpTarget, Time.deltaTime * 8f);
                }
            }

            // Alt precision belongs to the wand (slot 1) in FIRST person; the
            // draw modes and R pose mode free the cursor themselves.
            bool altPrecision = !ThirdPersonActive && kb.leftAltKey.isPressed
                && (_slots == null || _slots.PenSelected);
            // teach the precision pen once; retire only when Alt is used while inking
            if (!ThirdPersonActive && (_slots == null || _slots.PenSelected))
            {
                if (altPrecision && SurfaceDrawer.IsPenActive) Hints.Retire(Hints.Id.FreeHand);
                else if (SurfaceDrawer.IsPenActive) Hints.Offer(Hints.Id.FreeHand);
            }
            bool precision = altPrecision || HeldWeapon.DrawMode || SelfPaint.IsActive
            || PoseGrab.IsOpen || LobbyStand.PanelOpen // book stand menu = real mouse
                || HatPillar.PanelOpen                      // hat color sliders = same law as the stand
                || ShapeShift.PoseOpen;                     // acolyte shape posing = same precision cursor
            if (precision)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (!_wasPrecision) PrecisionCursor.Apply(); // brush circle, not an arrow
            }
            else if (_wasPrecision)
            {
                PrecisionCursor.Clear();
                LockCursor();
            }
            else if (kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
            {
                LockCursor();
            }
            _wasPrecision = precision;

            // draw modes: idling, not frozen - no move/jump/crouch (WASD steers
            // the view), but gravity, damage and revives keep running. Getting
            // floored kicks you out of the mode.
            bool drawingMode = SelfPaint.IsActive || HeldWeapon.DrawMode || PoseGrab.IsOpen;
            if ((IsDowned || IsSprawled || IsAirTumbling) && HeldWeapon.DrawMode)
                HeldWeapon.CancelDrawMode();

            // ---- look (only while the cursor is captured) ----
            var gp = Gamepad.current;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                // steady the hand: the camera slows way down while ink is flowing
                float sens = SurfaceDrawer.IsPenActive
                    ? LookSensitivity * DrawingConfig.DrawLookSensitivityScale
                    : LookSensitivity;
                Vector2 d = mouse.delta.ReadValue() * sens;
                if (gp != null) // right stick - same draw-slowdown applies
                    d += gp.rightStick.ReadValue() * sens * 1400f * Time.deltaTime;
                transform.Rotate(0f, d.x, 0f); // yaw spins the body in both modes
                if (!ThirdPersonActive)
                {
                    // pitch aims the pivot in FIRST person only - in third the
                    // camera block owns rotation (it must keep facing the bean)
                    _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
                    if (CameraPivot != null)
                        CameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);
                }
            }

            // ---- downed ----
            if (IsDowned)
            {
                if (!IsDead)
                {
                    _bleedOut -= Time.deltaTime;
                    ReviveProgress = Mathf.Max(0f, ReviveProgress - Time.deltaTime * 0.15f); // rescuer let go
                    OnDeathCrossed(); // fires exactly once, the frame the bleed-out runs dry
                }
                // camera keels over - the world from the floor
                if (CameraPivot != null)
                {
                    var e = CameraPivot.localEulerAngles;
                    CameraPivot.localEulerAngles = new Vector3(e.x, e.y,
                        Mathf.LerpAngle(e.z, 55f, Time.deltaTime * 3f));
                }
            }
            else
            {
                // sprawled: knocked clean off your feet - camera keels hard,
                // then staggers back upright as the timer runs out
                if (_knockLeft > 0f) _knockLeft -= Time.deltaTime;
                float targetRoll = _knockLeft > 0f ? 75f : 0f;
                if (CameraPivot != null)
                {
                    var e = CameraPivot.localEulerAngles;
                    if (Mathf.Abs(Mathf.DeltaAngle(e.z, targetRoll)) > 0.5f)
                        CameraPivot.localEulerAngles = new Vector3(e.x, e.y,
                            Mathf.LerpAngle(e.z, targetRoll, Time.deltaTime * (targetRoll > 0f ? 8f : 4f)));
                }

                // rescue: a downed friend in range announces itself - HOLD E
                // (gamepad X) to pick them up, the prompt counts the progress
                SimpleFPSController fallen = null;
                foreach (var other in All)
                {
                    if (other == this || !other.IsDowned || other.IsDead) continue;
                    if ((other.transform.position - transform.position).sqrMagnitude
                        > DrawingConfig.ReviveRange * DrawingConfig.ReviveRange) continue;
                    fallen = other;
                    break;
                }
                if (fallen != null)
                {
                    // rebuild the prompt string only when the percent changes
                    int pct = fallen.ReviveProgress > 0f
                        ? Mathf.RoundToInt(fallen.ReviveProgress * 100f) : -1;
                    if (pct != _revivePctShown || _revivePrompt == null)
                    {
                        _revivePctShown = pct;
                        _revivePrompt = pct >= 0
                            ? Loc.F("revive.pct", pct) : Loc.T("revive.friend");
                    }
                    UIPrompt.Show("E", _revivePrompt, new Color(0.55f, 1f, 0.6f));
                    if (kb.eKey.isPressed || (gp != null && gp.buttonWest.isPressed))
                        fallen.AddRevive(Time.deltaTime);
                }
            }

            // ---- crouch ----
            bool wantCrouch = (kb.leftCtrlKey.isPressed || (gp != null && gp.buttonEast.isPressed))
                && !IsDowned && !drawingMode;
            if (!wantCrouch && IsCrouched)
            {
                // no standing up under something solid (ink canvases excluded)
                float curTop = transform.position.y + _cc.center.y + _cc.height * 0.5f;
                int mask = Physics.DefaultRaycastLayers & ~(1 << InkCanvasLayer.Layer);
                if (Physics.SphereCast(
                        new Ray(new Vector3(transform.position.x, curTop + 0.05f, transform.position.z), Vector3.up),
                        _cc.radius * 0.9f, _standHeight - _cc.height + 0.1f, mask,
                        QueryTriggerInteraction.Ignore))
                    wantCrouch = true;
            }
            // load past the walk limit folds you whether you chose it or not
            if (_body != null && _body.CrawlOnly) wantCrouch = true;
            IsCrouched = wantCrouch;
            float targetHeight = IsCrouched ? _crouchHeight : _standHeight;
            if (!Mathf.Approximately(_cc.height, targetHeight))
            {
                _cc.height = Mathf.MoveTowards(_cc.height, targetHeight, 8f * Time.deltaTime);
                _cc.center = new Vector3(_standCenter.x,
                    _standCenter.y - (_standHeight - _cc.height) * 0.5f, _standCenter.z);
                _camY = _camStandY - (_standHeight - _cc.height) * 0.85f;
            }

            // ---- move ----
            Vector2 mv = Vector2.zero;
            if (kb.wKey.isPressed) mv.y += 1f;
            if (kb.sKey.isPressed) mv.y -= 1f;
            if (kb.dKey.isPressed) mv.x += 1f;
            if (kb.aKey.isPressed) mv.x -= 1f;
            if (gp != null) mv += gp.leftStick.ReadValue();
            if (mv.sqrMagnitude > 1f) mv.Normalize();
            // in draw modes WASD belongs to the view (orbit), not to walking
            if (drawingMode) mv = Vector2.zero;
            // a Y owns you: inputs walk you the OTHER way
            if (_body != null) mv *= _body.InputSign;

            bool sprint = kb.leftShiftKey.isPressed || (gp != null && gp.leftStickButton.isPressed);
            if (_body != null && !_body.CanSprint) sprint = false; // too heavy to run
            IsSprinting = sprint && !IsDowned && !IsCrouched && mv.sqrMagnitude > 0.01f;

            // inside a liquid volume: no drowning (no mouths), just slow
            var swimIn = LiquidBiome.At(transform.position + Vector3.up * 0.9f);
            float speed = IsDead ? 0f
                : IsDowned ? MoveSpeed * 0.25f // crawl
                : IsSprawled ? 0f              // flat on your face - momentum owns you
                : IsAirTumbling ? 0f           // ragdolls don't steer - the launch owns you
                : FeetStuck ? 0f               // glued boots - the grip won't let go
                : sprint ? SprintSpeed : MoveSpeed;
            if (IsCrouched) speed *= 0.5f;
            if (_body != null)
            {
                speed *= _body.SpeedMul; // grip, frost, arrows and Ys all live here
                if (_body.CrawlOnly && !IsCrouched)
                    speed = Mathf.Min(speed, MoveSpeed * 0.5f); // too heavy: crouch pace is all you have
            }
            if (swimIn != null) speed *= 0.6f; // water wastes your time, never your air
            Vector3 planar = (transform.right * mv.x + transform.forward * mv.y) * speed;

            if (_cc.isGrounded)
            {
                bool justLanded = !_wasGrounded;

                // fall damage never finishes you by itself: the worst cliff
                // leaves you at 1 hp, floored
                float landing = -_verticalVelocity;
                if (justLanded && landing > DrawingConfig.SafeFallSpeed && !IsDowned)
                {
                    float dmg = (landing - DrawingConfig.SafeFallSpeed) * DrawingConfig.FallDamagePerSpeed
                        * (_body != null ? _body.TotalWeight : 1f); // WEIGHT × SPEED - carried rocks count too
                    dmg = Mathf.Min(dmg, Mathf.Max(0f, Health - 1f));
                    if (dmg > 0f) TakeHit(Vector3.zero, dmg);
                    else KnockDown(1.1f); // already scraping 1 hp: just the pratfall
                    Juice.Thud(transform.position);
                    Juice.Shake(Mathf.Min(0.5f, landing * 0.02f));
                }
                _wasGrounded = true;
                _airTime = 0f;

                // every touchdown re-arms the full flop, so a one-frame
                // rooftop skim can't pre-spend it
                if (IsAirTumbling)
                {
                    if (justLanded) _tumbleRecover = DrawingConfig.AirTumbleRecover;
                    _tumbleRecover -= Time.deltaTime;
                    if (_tumbleRecover <= 0f) IsAirTumbling = false;
                }

                _verticalVelocity = -1f;
                bool jump = kb.spaceKey.wasPressedThisFrame
                    || (gp != null && gp.buttonSouth.wasPressedThisFrame);
                if (jump && !IsDowned && !IsSprawled && !IsAirTumbling && !drawingMode && !FeetStuck)
                {
                    // JumpMul is 0 when weight beats strength - the feet simply
                    // do not leave the ground, and it is worth saying why once.
                    if (_body != null && !_body.CanJump)
                        DrawingWorld.Instance?.LogEvent("too heavy to jump");
                    else
                        _verticalVelocity = (IsCrouched ? JumpSpeed * 1.15f : JumpSpeed)
                            * (_body != null ? _body.JumpMul : 1f); // light wizards spring higher
                }
            }
            else if (swimIn != null)
            {
                // swimming: sink slowly (buoyancy eats most of gravity), every
                // jump press is a stroke. No drowning, no tumble clock.
                _wasGrounded = false;
                _airTime = 0f;
                float sink = Gravity * (1f - Mathf.Clamp01(swimIn.Buoyancy));
                _verticalVelocity = Mathf.Max(_verticalVelocity + sink * Time.deltaTime, -1.6f);
                bool stroke = kb.spaceKey.wasPressedThisFrame
                    || (gp != null && gp.buttonSouth.wasPressedThisFrame);
                if (stroke && !IsDowned && !IsSprawled && !drawingMode)
                    _verticalVelocity = JumpSpeed * 0.65f
                        * (_body != null ? _body.JumpMul : 1f);
            }
            else
            {
                _wasGrounded = false;
                // light bodies fall soft (gradual - jump higher, fall
                // slower, and only the REALLY light float outright)
                // GravityMul goes negative once the medium is denser than you,
                // so rising is the same line that makes you fall - no float
                // flag, no swim mode. Drag scales with how much the medium
                // holds you, which is what stops a drift becoming a rocket.
                float gscale = _body != null ? _body.GravityMul : 1f;
                _verticalVelocity += Gravity * gscale * Time.deltaTime;
                float hold = _body != null ? _body.MediumControl : 0f;
                if (hold > 0.01f)
                    _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, 0f,
                        DrawingConfig.FloatDrag * hold * 20f * Time.deltaTime);

                // airborne too long = launched. Spell flight is exempt - the
                // clock only runs on ballistic air; the body ragdolls for the
                // rest of the arc.
                if (_spellVel.y > 0.5f) _airTime = 0f;
                else _airTime += Time.deltaTime;
                if (!IsAirTumbling && _airTime >= DrawingConfig.AirTumbleSeconds
                    && !IsDowned && !IsSprawled)
                {
                    IsAirTumbling = true;
                    _tumbleRecover = DrawingConfig.AirTumbleRecover;
                    // carry the momentum you had into the tumble - the capsule
                    // keeps sailing the arc (decaying) instead of stopping
                    // dead mid-air while the ragdoll flies on without it
                    _spellVel.x += planar.x;
                    _spellVel.z += planar.z;
                    Juice.Whoosh(transform.position); // the "uh oh" cue
                }
            }

            // external shoves decay quickly but land with punch
            _shove = Vector3.MoveTowards(_shove, Vector3.zero, 18f * Time.deltaTime);

            // flight lasts as long as the spell feeds it; unfed it decays
            // fast (the lift below overwrites gravity outright)
            bool burning = Time.time - _spellFedAt < 0.15f;
            _spellVel = Vector3.MoveTowards(_spellVel, Vector3.zero,
                (burning ? 5f : 26f) * Time.deltaTime);

            // while a pose/paint/draw mode is open, shoves and spell forces
            // are dropped outright. Gravity stays; leaving re-arms the forces.
            if (SelfPaint.IsActive || PoseGrab.IsOpen || HeldWeapon.DrawMode)
            {
                _shove = Vector3.zero;
                _spellVel = Vector3.zero;
            }

            if (_spellVel.y > 0.5f) _verticalVelocity = _spellVel.y;
            Vector3 spellPlanar = new Vector3(_spellVel.x, 0f, _spellVel.z);

            // slow mending once out of danger, never while down and never
            // past the side's own ceiling. Acolytes mend faster - the healing
            // SPELL is a wizard tool, so this is their only way back.
            if (!IsDowned && IsLocalViewer && Time.time - _lastHurt > DrawingConfig.RegenCalmSeconds)
            {
                int me = Grimoire.LocalPlayerId;
                float cap = Sides.MaxHealthFor(me);
                if (Health < cap)
                    Health = Mathf.MoveTowards(Health, cap,
                        Sides.RegenPerSecFor(me) * Time.deltaTime);
            }

            if (_ragdollFollow != null)
            {
                // the doll leads, the capsule (and camera) follows. Vertical
                // velocity mirrors the doll's worst fall so landing damage
                // stays honest.
                _verticalVelocity = Mathf.Min(_verticalVelocity, _ragdollFollow.linearVelocity.y);
                if (IsDowned && planar.sqrMagnitude > 0.01f)
                    _ragdollFollow.AddForce(planar * 6f, ForceMode.Acceleration);
                Vector3 gap = _ragdollFollow.worldCenterOfMass - (transform.position + _cc.center);
                // the doll is parented to this capsule, so Moving to catch it
                // also drags it - chase only a doll that is travelling; a
                // settled body stays where it lies. Fast dolls track hard,
                // crawls and slow settles get the gentle spring.
                float dollSpeed = _ragdollFollow.linearVelocity.magnitude;
                if (planar.sqrMagnitude > 0.01f || dollSpeed > 0.2f)
                {
                    float k = dollSpeed > 4f ? 60f : 12f;
                    _cc.Move(Vector3.ClampMagnitude(gap * k, 250f) * Time.deltaTime);
                }
            }
            else
            {
                _cc.Move((planar + _shove + spellPlanar + Vector3.up * _verticalVelocity) * Time.deltaTime);
            }

            // the world's absolute floor, whatever the map's own slab covers
            if (transform.position.y < FallCatcher.KillY) FallCatcher.Catch(this);

            // ragdolled in open air and not gaining height for too long = dead.
            // Gaining height refreshes the clock, so there is always a chance
            // to fly out of it; near the ground the landing flop handles it.
            if (IsAirTumbling && !IsDead)
            {
                bool nearGround = Physics.Raycast(transform.position, Vector3.down, 3f,
                    Physics.DefaultRaycastLayers & ~(1 << InkCanvasLayer.Layer),
                    QueryTriggerInteraction.Ignore);
                bool rising = transform.position.y - _prevY > 0.05f * Time.deltaTime;
                if (nearGround || rising) _fallFor = 0f;
                else _fallFor += Time.deltaTime;
                if (_fallFor >= DrawingConfig.FallDeathSeconds)
                {
                    _fallFor = 0f;
                    GetComponent<BodyState>()?.ClearSpellEffects();
                    DieOutright();
                    DrawingWorld.Instance?.LogEvent("the fall outlasted you");
                }
            }
            else _fallFor = 0f;
            _prevY = transform.position.y;
        }

        /// Tab's toggle only. Anything that wants a specific mode calls
        /// EnterFirstPerson / EnterThirdPerson instead.
        public void ToggleThirdPerson()
        {
            if (ThirdPersonActive) EnterFirstPerson();
            else EnterThirdPerson();
        }

        /// Both modes are idempotent full mode changes: everything downstream
        /// keys off ThirdPersonActive, which hands the wand and grimoire back
        /// on the way into first person.
        public void EnterThirdPerson()
        {
            if (ThirdPersonActive) return;
            ThirdPersonActive = true;
            if (_emotes == null) _emotes = GetComponent<EmotePlayer>();
            _emotes?.StopToRest();
            XRayGlow.Show(gameObject);
        }

        public void EnterFirstPerson()
        {
            if (!ThirdPersonActive) return;
            ThirdPersonActive = false;
            if (_emotes == null) _emotes = GetComponent<EmotePlayer>();
            _emotes?.StopToRest();
            XRayGlow.Hide(gameObject);
            // aim owns the pivot again, immediately
            if (CameraPivot != null)
                CameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // shove crates around so half-drawn arcs on two objects can meet and seal
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic) return;
            if (hit.moveDirection.y < -0.3f) return; // don't push things we stand on

            Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);

            body.linearVelocity = new Vector3(dir.x * PushStrength, body.linearVelocity.y, dir.z * PushStrength);
        }
    }
}
