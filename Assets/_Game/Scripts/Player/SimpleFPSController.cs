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

        /// Live player registry - replaces FindObjectsByType in per-frame paths
        /// (zombie perception ran one scene scan per zombie per frame).
        public static readonly System.Collections.Generic.List<SimpleFPSController> All
            = new System.Collections.Generic.List<SimpleFPSController>();

        CharacterController _cc;
        float _pitch;
        float _verticalVelocity;
        bool _wasGrounded = true; // fall-damage edge detection
        float _airTime;           // continuous seconds off the ground
        float _tumbleRecover;     // the flop-on-the-floor beat after a tumble

        /// Airborne past AirTumbleSeconds = you were SENT - the body ragdolls
        /// for the rest of the arc (the comedy rule; jumps never trigger
        /// it). Ends a short flop after touching down.
        public bool IsAirTumbling { get; private set; }

        /// Camera pitch in degrees (+ = looking down) - the rig bends the
        /// head/neck to match, and NetSync ships it so friends see it too.
        /// Zero in third person: the mouse stops driving _pitch there, and a
        /// wizard frozen mid-stare at the feet ruins every emote clip.
        public float LookPitch => ThirdPersonActive ? 0f : _pitch;

        BodyState _body;          // the slider board - speed/jump/gravity all read it
        Rigidbody _ragdollFollow; // CharacterRig hands us the hips while ragdolling

        /// While ragdolling, the CAPSULE chases the doll - not the other way
        /// around - so the camera follows the body wherever the launch sent
        /// it, and standing back up happens where the body actually landed.
        public void SetRagdollFollow(Rigidbody hips) => _ragdollFollow = hips;
        bool _wasPrecision;
        Vector3 _shove; // external impulse (zombie swipes, explosions) - decays fast

        // ---- crouch: hold LeftCtrl (gamepad East). Squeeze through broken
        // windows, duck behind barricades. Crouch-jump springs a bit higher -
        // exactly enough to clear a window sill.
        public bool IsCrouched { get; private set; }
        float _crouchHeight = 1.15f; // refit by CharacterRig to the real model
        float _standHeight, _camStandY;
        float _camY;        // current eye height (crouch moves it)
        Vector3 _eyeAnchorLocal; // head-bone-tracked eye point (player-local), fed by CharacterRig
        bool _eyeAnchorFresh;    // consumed each frame - stale anchors (ragdoll) fall back to static
        float _camForward;  // eye offset in FRONT of the body axis - rotates
                            // with pitch so your own head/torso never clip in
        Vector3 _standCenter;

        // ---- TAB = third person : camera above and behind,
        // whole bean in frame. 1-9 emote (and FREEZE you doll-still until F),
        // R paints your body, B opens the pose editor. No world drawing, no
        // cursor, no Alt - that's all first-person business.
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
            // terminal velocity raised for the mayhem pass (white hole
            // should YEET) - big fields now actually express their strength
            _spellVel = Vector3.ClampMagnitude(_spellVel, 34f);
        }

        public float Health = 100f;

        /// Real motion this frame (the CC's own measurement) - liquids read it
        /// to apply viscous drag against the direction you're actually moving.
        public Vector3 Velocity => _cc != null ? _cc.velocity : Vector3.zero;

        /// Raw ground contact - the animation rig smooths its own airborne
        /// signal from this (slope flicker is the caller's problem).
        public bool IsGrounded => _cc != null && _cc.isGrounded;

        /// THE AIM GATE : E takes what you are LOOKING AT within
        /// reach - never whichever thing happens to be nearest. Returns how
        /// CENTRED the point is (1 = dead centre, used to pick between several
        /// candidates), or -1 when it's out of range, outside the cone, or
        /// behind something. Every E interaction should ask this.
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

        /// Shift is held and we're actually moving - the ANIMATION rule
        /// : no shift = walk look, shift = run look, regardless of
        /// how fast the controller really covers ground.
        public bool IsSprinting { get; private set; }

        // ---- ragdoll-feel knockdown: hit hard enough and you SPRAWL - camera
        // keels over, control cuts out, momentum slides you, then you stagger
        // back up. Funny, brief, and never while properly downed.
        float _knockLeft;
        public bool IsSprawled => _knockLeft > 0f;

        public void KnockDown(float seconds)
        {
            if (IsDowned) return;
            _knockLeft = Mathf.Max(_knockLeft, seconds);
        }

        // ---- glued boots : the lvl2 grip and the
        // time zone hold you where you STAND - no walking, no jumping, until
        // the glue lets go. Re-applied on a beat while you remain in it.
        float _feetStuckUntil;
        public bool FeetStuck => Time.time < _feetStuckUntil;
        public void StickFeet(float seconds) =>
            _feetStuckUntil = Mathf.Max(_feetStuckUntil, Time.time + seconds);
        public void UnstickFeet() => _feetStuckUntil = 0f;

        // ---- downed / revive (the co-op moment) ----
        public bool IsDowned { get; private set; }
        public bool IsDead => IsDowned && _bleedOut <= 0f;
        public float BleedOut => _bleedOut;
        public float ReviveProgress { get; private set; }
        float _bleedOut;
        GameObject _heartFx; // broken heart over the downed body (ally-dying gap)
        bool _soulFled;      // Souls Escape fired once for this death

        void OnDeathCrossed()
        {
            if (_soulFled || !IsDead) return;
            _soulFled = true;
            if (_heartFx != null) { Destroy(_heartFx); _heartFx = null; }
            if (FxLibrary.I != null) // the soul leaves the body
                FxLibrary.Spawn(FxLibrary.I.SoulsOut, transform.position + Vector3.up * 1.2f);
        }

        /// Physical hits shove the player and chip health. At 0 HP the ghost
        /// rises at once; revival is the ghost flying home, with a teammate
        /// meeting it there in matches.
        // EVERY hit has a NAME .
        // Small chips don't shake the camera, but they all land in a rolled-up
        // console line every couple of seconds: "hurt: standing in fire −6.2"
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
            if (Barrier.Protects(this)) return; // isolated - NOTHING gets in
            if (IsDowned)
            {
                _bleedOut -= 1.5f; // kicking someone who's down. rude. effective.
                OnDeathCrossed();
                return;
            }
            _shove += impulse;
            Health -= damage;
            _lastHurt = Time.time;
            if (damage > 0f) NoteDamage(cause ?? "hit", damage);
            // FEEL belongs to REAL hits. Damage-over-time ticks (steam clouds,
            // ember auras, a stalking spark grazing you) arrive every fraction
            // of a second - shaking on each one meant permanent earthquake and
            // "drawing way too difficult" . Small ticks show on the
            // hurt vignette only; the camera reacts to blows, on a cooldown,
            // and gentler while the pen is down.
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
                    Health = Perks.MaxHealth; // sandbox mercy respawn-in-place
                    Debug.Log("[SpellyZombie] Player DOWN - shaking it off (sandbox)");
                }
            }
        }
        float _lastHurt;
        float _lastFeelShake; // camera-shake cooldown - DoT ticks must not machine-gun the view

        /// The void's price (FallCatcher): floored on arrival - revivable,
        /// bleeding out, and fair game for the horde. Never kills outright.
        public void DropDowned()
        {
            if (!IsDowned && RoundDirector.RunActive) GoDown();
        }

        /// Straight to DEAD - skips the sandbox mercy AND the bleedout (the
        /// ghost practice kill in the lobby; K used to hit the mercy heal
        /// and "I didn't remain dead").
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
            // the JMO layer answers the ally-dying gap: a broken heart floats
            // over the crawling body - readable across a courtyard, no HUD
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
            // while the corpse lay somewhere the capsule couldn't ground itself
            // (ledge, table, pressed to a wall) gravity kept integrating into
            // _verticalVelocity the whole bleed-out - wipe it, or the first Move
            // after standing slams the fresh revive straight back to 1 hp
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

                // BODY INK IS FOREVER, ON EVERY BODY IN EVERY SCENE (
            // drawings on the body "should never expire completely"). Only
            // the test-sandbox builder ever attached this marker - in the
            // hand-built Lobby the player had none, so body drawings counted
            // as WORLD ink and the seal CONSUMED them when the spell ended
            // ("disappeared as if it expired"). Structural now: the player
            // class carries its own marker, no scene setup can forget it.
            if (GetComponent<PersistentInkSurface>() == null)
                gameObject.AddComponent<PersistentInkSurface>();

            // the player is googly too - other players (and clips) see your eyes
            // dart to explosions and shrink in terror. Hidden from your own
            // first-person camera; third person and remote avatars re-enable them.
            _eyes = GooglyEyes.Attach(transform, 1.55f, 1.4f);
            if (CameraPivot != null)
            {
                _cam = CameraPivot.GetComponentInChildren<Camera>();
                if (_cam != null) _camDefaultLocal = _cam.transform.localPosition;
            }
            // eyes stay visible in EVERY mode: the camera rides in front of
            // the face now, so your own eyes are always behind the lens -
            // hiding them only produced eyeless shadows
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

            // spell physics (burn/freeze/crush) speaks Damageable - bridge it
            // into the controller's health so players are exactly as
            // FLAMMABLE as everything else (burning characters is the
            // fun). The bridge never dies itself; the controller owns downs.
            var dmg = GetComponent<Damageable>();
            if (dmg == null) dmg = gameObject.AddComponent<Damageable>();
            dmg.Health = float.MaxValue;
            dmg.Destructible = false;
            dmg.OnDamaged = (amount, cause) => TakeHit(Vector3.zero, amount);

            All.Add(this);
        }

        void OnDestroy() => All.Remove(this);

        /// CharacterRig fits the gameplay body to the imported model: capsule
        /// Where the static first-person eye point sits right now (world) -
        /// CharacterRig calibrates its head-bone camera anchor against this.
        public Vector3 EyeCenterWorld => transform.TransformPoint(new Vector3(0f, _camY, 0f));

        /// THIS body is the local viewer (its camera is live). Cached _cam -
        /// replaces the per-frame GetComponentInChildren&lt;Camera&gt; probes that
        /// WandState and BodyState each ran on their own.
        public bool IsLocalViewer => _cam != null && _cam.isActiveAndEnabled;

        /// CharacterRig feeds the eye point RIDING THE HEAD BONE every frame:
        /// sprint leans, bob and wobble move the camera WITH the face, so the
        /// hat and googly eyes can never end up in front of the lens. Must be
        /// re-fed each frame - a stale anchor (ragdoll, third person) simply
        /// falls back to the static eye height.
        public void SetEyeAnchor(Vector3 world)
        {
            _eyeAnchorLocal = transform.InverseTransformPoint(world);
            _eyeAnchorFresh = true;

            // RIGID GLUE, same frame: a sprint start lurches the head forward
            // faster than next frame's gentle chase - for a few frames the
            // face would outrun the lens and the googly eyes eat the screen.
            // This runs in LateUpdate (post-animation), so snap hard here.
            if (_cam != null && !ThirdPersonActive && !SelfPaint.IsActive
                && !PoseGrab.IsOpen && CameraPivot != null && _camForward > 0f)
            {
                Vector3 fpTarget = _eyeAnchorLocal
                    + CameraPivot.localRotation * new Vector3(0f, 0f, _camForward);
                _cam.transform.localPosition = Vector3.Lerp(
                    _cam.transform.localPosition, fpTarget, Time.deltaTime * 30f);
            }
        }

        /// height/radius (feet stay planted), eye height, crouch proportion.
        /// eyeForward pushes the camera just in front of the face so the whole
        /// head can stay rendered (no headless shadows) without blocking view.
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

            // THE EYES ARE NEVER HIDDEN. the tuned fit keeps them out of
            // the first-person view; hiding them was tried twice and hated
            // twice. Leave them alone.

            // ---- cursor handling ----
            // TAB flips the camera; third person = the emote stage. Either
            // direction lands on the plain idle - a saved emote pose only
            // replays when its key is pressed again .
            if (kb.tabKey.wasPressedThisFrame && !IsDowned && !SelfPaint.IsActive
                && ShapeShift.ThirdPersonAllowed) // acolyte with no stored shape: refused
                ToggleThirdPerson();
            if (_cam != null && !SelfPaint.IsActive && !PoseGrab.IsOpen) // easel modes own the camera
            {
                if (ThirdPersonActive)
                {
                    // Meccha framing: boom up and back off the pivot, camera
                    // LOOKS AT the bean - pitch orbits vertically, yaw spins
                    // around, the whole body stays in frame
                    // boom in ROOT space, never pivot space (pressing
                    // 1-9 made the camera "float outside the body" — the
                    // pivot rides the HEAD bone, and a held pose drags the
                    // head, so a pivot-local boom sailed off with it)
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
                    // rotation is the look block's business in first person -
                    // touching it here would drift the view during Alt precision.
                    // The eye-forward offset PITCHES with the view (orbits the
                    // eye point), so looking down never shows your own head.
                    // The eye point itself RIDES THE HEAD BONE when the rig
                    // feeds one (sprint leans move the camera WITH the face -
                    // hat and eyes can never swallow the screen).
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
                // teach the precision pen once ("users have no idea that by
            // clicking ALT they can draw more accurately"). Retire only when
            // Alt is used WHILE INKING - a stray Alt tap (or an alt-tab) used
            // to burn the one-shot lesson before it was ever seen.
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

            // draw modes (weapon engraving / body paint): you're IDLING, not
            // frozen - no move/jump/crouch commands (WASD steers the view
            // instead), but gravity, shoves, spell forces, damage and revives
            // all keep running. Getting floored kicks you out of the mode.
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

            // ---- downed: crawl, bleed, hope ----
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
                    // rebuild the prompt string only when the percent CHANGES
                    // (it was allocated every frame while standing by a friend)
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
            Vector3 planar = (transform.right * mv.x + transform.forward * mv.y) * speed;

            if (_cc.isGrounded)
            {
                bool justLanded = !_wasGrounded;

                // FALL DAMAGE (non-damage runes kill through physics
                // a FLOAT running out over a cliff must mean something).
                // But a fall NEVER finishes you by itself : the
                // worst cliff leaves you at 1 hp, floored and humiliated.
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

                // an air tumble ends with a short FLOP on the ground - half a
                // beat of limp wizard, then you're back up. EVERY touchdown
                // re-arms the full flop, so a one-frame rooftop skim can't
                // pre-spend it and hand control back mid-fall.
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
                    _verticalVelocity = (IsCrouched ? JumpSpeed * 1.15f : JumpSpeed)
                        * (_body != null ? _body.JumpMul : 1f); // light wizards spring higher
            }
            else
            {
                _wasGrounded = false;
                // light bodies fall soft (gradual - jump higher, fall
                // slower, and only the REALLY light float outright)
                float gscale = _body != null ? _body.GravityMul : 1f;
                _verticalVelocity += Gravity * gscale * Time.deltaTime;
                if (_body != null && _body.Floating)
                    _verticalVelocity = Mathf.MoveTowards(_verticalVelocity, 0f, 30f * Time.deltaTime);

                    // AIRBORNE TOO LONG = you were SENT :
                // no jump lasts this long - only launches and cliffs. SPELL
                // FLIGHT is exempt: a broom seal holding you up is a
                // mechanic, not a mishap - the clock only runs on ballistic
                // air. The body ragdolls for the rest of the arc; fall
                // damage and the flop are waiting at the bottom.
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

            // Flight lasts as long as the spell feeds it. Unfed it decays
            // fast, because the lift below overwrites gravity outright and a
            // slow afterburn left players hanging in the sky.
            bool burning = Time.time - _spellFedAt < 0.15f;
            _spellVel = Vector3.MoveTowards(_spellVel, Vector3.zero,
                (burning ? 5f : 26f) * Time.deltaTime);

                // THE EASEL IS AN ANCHOR (the pose-mode ruling: external
            // forces must not move the body while sculpting/painting - and
            // the body runes casting mid-pose were shoving the capsule
            // out from under the camera, then the get-up snapped the body
            // back to wherever it ended up): while a pose/paint/draw mode is
            // open, shoves and spell forces are dropped outright. Gravity
            // stays; leaving the mode re-arms the forces.
            if (SelfPaint.IsActive || PoseGrab.IsOpen || HeldWeapon.DrawMode)
            {
                _shove = Vector3.zero;
                _spellVel = Vector3.zero;
            }

            if (_spellVel.y > 0.5f) _verticalVelocity = _spellVel.y;
            Vector3 spellPlanar = new Vector3(_spellVel.x, 0f, _spellVel.z);

            // slow health regen once out of danger (never while down);
            // the Survival perk raises both the ceiling and the mend rate
            if (!IsDowned && Health < Perks.MaxHealth && Time.time - _lastHurt > 5f)
                Health = Mathf.MoveTowards(Health, Perks.MaxHealth, Perks.HealthRegenPerSec * Time.deltaTime);

            if (_ragdollFollow != null)
            {
                // the doll leads, the capsule (and camera) follows - downed
                // included: crawling now SCOOTS the doll along the floor and
                // the camera stays with your actual body (it used to crawl
                // away invisibly while the corpse stayed behind). Vertical
                // velocity mirrors the doll's WORST fall so the landing
                // damage stays honest (the capsule touches down a beat after
                // the body does, when the doll's own velocity is already 0).
                _verticalVelocity = Mathf.Min(_verticalVelocity, _ragdollFollow.linearVelocity.y);
                if (IsDowned && planar.sqrMagnitude > 0.01f)
                    _ragdollFollow.AddForce(planar * 6f, ForceMode.Acceleration);
                Vector3 gap = _ragdollFollow.worldCenterOfMass - (transform.position + _cc.center);
                // The doll is PARENTED to this capsule, so Moving to "catch" it
                // also drags it: a prone corpse's hips centre-of-mass sits low and
                // offset, leaving a gap our own Move never closes - capsule AND
                // body then skate forever (a transform teleport, which friction,
                // damping and the velocity settle can't fight). So chase only a
                // doll that is genuinely TRAVELLING - crawling (planar input) or
                // still in motion (a tumble/knockback arc). A settled body - a
                // corpse, a friend waiting for revive - we leave where it lies.
                // per-SECOND ceiling (a per-frame clamp let the doll outrun the
                // capsule to the leash on low-fps machines).
                // A FAST DOLL IS TRACKED 1:1 ("keeps falling out of
                // frame and then re-appearing") — the soft 12/s spring let a
                // free-fall open a gap until the lost-doll guard teleported
                // the body back into frame, over and over. Flying/falling
                // the camera rides the doll steadily until it hits something;
                // the gentle spring remains for crawls and slow settles.
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

        /// TAB'S toggle, and only Tab's - the one caller that genuinely means
        /// "the other mode, whichever that is". Everything that wants a SPECIFIC
        /// mode calls EnterFirstPerson / EnterThirdPerson below instead.
        public void ToggleThirdPerson()
        {
            if (ThirdPersonActive) EnterFirstPerson();
            else EnterThirdPerson();
        }

        /// ASK FOR THE MODE BY NAME ("this is why we put the third
        /// person aspects in one method so you would call it when we need the
        /// mode. The same should be done for the first person. No more confusion
        /// please").
        ///
        /// Callers that need a SPECIFIC mode must say so instead of flipping the
        /// toggle and hoping the current state was the one they assumed -
        /// getting that wrong lands you in the opposite mode. Both are
        /// idempotent, so asking twice is free, and BOTH ARE FULL MODE CHANGES:
        /// everything downstream keys off ThirdPersonActive, which is what hands
        /// the wand and grimoire back on the way into first person.
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
