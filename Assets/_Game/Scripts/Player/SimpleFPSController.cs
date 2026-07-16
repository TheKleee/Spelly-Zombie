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
        public float MoveSpeed = 4.5f;
        public float SprintSpeed = 7f;
        public float JumpSpeed = 4.6f;
        public float Gravity = -14f;
        public float LookSensitivity = 0.12f;
        public float PushStrength = 1.6f;

        /// Live player registry — replaces FindObjectsByType in per-frame paths
        /// (zombie perception ran one scene scan per zombie per frame).
        public static readonly System.Collections.Generic.List<SimpleFPSController> All
            = new System.Collections.Generic.List<SimpleFPSController>();

        CharacterController _cc;
        float _pitch;
        float _verticalVelocity;
        bool _wasGrounded = true; // fall-damage edge detection
        float _airTime;           // continuous seconds off the ground
        float _tumbleRecover;     // the flop-on-the-floor beat after a tumble

        /// Airborne past AirTumbleSeconds = you were SENT — the body ragdolls
        /// for the rest of the arc (Marko's comedy rule; jumps never trigger
        /// it). Ends a short flop after touching down.
        public bool IsAirTumbling { get; private set; }

        /// Camera pitch in degrees (+ = looking down) — the rig bends the
        /// head/neck to match, and NetSync ships it so friends see it too.
        /// Zero in third person: the mouse stops driving _pitch there, and a
        /// wizard frozen mid-stare at his feet ruins every emote clip.
        public float LookPitch => ThirdPersonActive ? 0f : _pitch;

        Rigidbody _ragdollFollow; // CharacterRig hands us the hips while ragdolling

        /// While ragdolling, the CAPSULE chases the doll — not the other way
        /// around — so the camera follows the body wherever the launch sent
        /// it, and standing back up happens where the body actually landed.
        public void SetRagdollFollow(Rigidbody hips) => _ragdollFollow = hips;
        bool _wasPrecision;
        Vector3 _shove; // external impulse (zombie swipes, explosions) — decays fast

        // ---- crouch: hold LeftCtrl (gamepad East). Squeeze through broken
        // windows, duck behind barricades. Crouch-jump springs a bit higher —
        // exactly enough to clear a window sill.
        public bool IsCrouched { get; private set; }
        float _crouchHeight = 1.15f; // refit by CharacterRig to the real model
        float _standHeight, _camStandY;
        float _camY;        // current eye height (crouch moves it)
        Vector3 _eyeAnchorLocal; // head-bone-tracked eye point (player-local), fed by CharacterRig
        bool _eyeAnchorFresh;    // consumed each frame — stale anchors (ragdoll) fall back to static
        float _camForward;  // eye offset in FRONT of the body axis — rotates
                            // with pitch so your own head/torso never clip in
        Vector3 _standCenter;

        // ---- TAB = third person (Marko's spec): camera above and behind,
        // whole bean in frame. 1-9 emote (and FREEZE you doll-still until F),
        // R paints your body, B opens the pose editor. No world drawing, no
        // cursor, no Alt — that's all first-person business.
        public static bool ThirdPersonActive { get; private set; }
        Camera _cam;
        Vector3 _camDefaultLocal;
        GooglyEyes _eyes;
        WeaponSlots _slots;
        EmotePlayer _emotes;

        // sustained spell forces (Direction/Density zones) — this is FLIGHT:
        // a force seal drawn on your own feet feeds this every frame and you fly
        Vector3 _spellVel;

        /// Called by spell zones each frame they act on the player.
        public void AddSpellForce(Vector3 accel, float dt)
        {
            _spellVel += accel * dt;
            _spellVel = Vector3.ClampMagnitude(_spellVel, 16f); // terminal broom velocity
        }

        public float Health = 100f;

        // ---- ragdoll-feel knockdown: hit hard enough and you SPRAWL — camera
        // keels over, control cuts out, momentum slides you, then you stagger
        // back up. Funny, brief, and never while properly downed.
        float _knockLeft;
        public bool IsSprawled => _knockLeft > 0f;

        public void KnockDown(float seconds)
        {
            if (IsDowned) return;
            _knockLeft = Mathf.Max(_knockLeft, seconds);
        }

        // ---- downed / revive (the co-op moment) ----
        public bool IsDowned { get; private set; }
        public bool IsDead => IsDowned && _bleedOut <= 0f;
        public float BleedOut => _bleedOut;
        public float ReviveProgress { get; private set; }
        float _bleedOut;

        /// Physical hits shove the player and chip health. During a run, 0 HP
        /// means DOWNED — crawl, no drawing, bleed out unless a teammate holds E.
        /// Outside runs (sandbox) the old demo mercy applies.
        public void TakeHit(Vector3 impulse, float damage)
        {
            if (IsDowned)
            {
                _bleedOut -= 1.5f; // kicking someone who's down. rude. effective.
                return;
            }
            _shove += impulse;
            Health -= damage;
            _lastHurt = Time.time;
            Juice.Thud(transform.position);
            Juice.Shake(0.35f, 0.25f);
            if (damage >= 15f) KnockDown(1.1f); // big hits floor you
            Debug.Log($"[SpellyZombie] Player hit! {Mathf.Max(0, Health):0} hp");
            if (Health <= 0f)
            {
                if (RoundDirector.RunActive) GoDown();
                else
                {
                    Health = Perks.MaxHealth; // sandbox mercy respawn-in-place
                    Debug.Log("[SpellyZombie] Player DOWN — shaking it off (sandbox)");
                }
            }
        }
        float _lastHurt;

        /// The void's price (FallCatcher): floored on arrival — revivable,
        /// bleeding out, and fair game for the horde. Never kills outright.
        public void DropDowned()
        {
            if (!IsDowned && RoundDirector.RunActive) GoDown();
        }

        /// Teleports (fall catch) wipe all carried motion — otherwise the
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
            _bleedOut = DrawingConfig.BleedOutSeconds;
            ReviveProgress = 0f;
            WorldEvents.Report(WorldEventKind.Death, transform.position, 2f); // the horde celebrates
            Juice.Sting(transform.position);
            Juice.Shake(0.8f, 0.5f);
            Juice.HitStop(0.2f, 0.25f);
            Debug.Log("[SpellyZombie] Player DOWNED — teammate hold E to revive");
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
            _lastHurt = Time.time;
            Debug.Log("[SpellyZombie] Player revived!");
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _standHeight = _cc.height;
            _standCenter = _cc.center;
            _camStandY = CameraPivot != null ? CameraPivot.localPosition.y : 1.6f;
            _camY = _camStandY;
            LookSensitivity = PlayerPrefs.GetFloat("sz_look_sens", LookSensitivity);
            LockCursor();

            // every caster has an identity; seals are owned by whoever completes them
            Grimoire.LocalPlayerId = gameObject.GetInstanceID();

            // the player is googly too — other players (and clips) see your eyes
            // dart to explosions and shrink in terror. Hidden from your own
            // first-person camera; third person and remote avatars re-enable them.
            _eyes = GooglyEyes.Attach(transform, 1.55f, 1.4f);
            if (CameraPivot != null)
            {
                _cam = CameraPivot.GetComponentInChildren<Camera>();
                if (_cam != null) _camDefaultLocal = _cam.transform.localPosition;
            }
            // eyes stay visible in EVERY mode: the camera rides in front of
            // the face now, so your own eyes are always behind the lens —
            // hiding them only produced eyeless shadows (Marko's catch)
            _eyes.SetVisible(true);
            ThirdPersonActive = false;

            if (GetComponent<PlayerInk>() == null) gameObject.AddComponent<PlayerInk>();
            _slots = GetComponent<WeaponSlots>();
            if (_slots == null) _slots = gameObject.AddComponent<WeaponSlots>();
            if (GetComponent<SelfPaint>() == null) gameObject.AddComponent<SelfPaint>();
            if (GetComponent<CharacterRig>() == null) gameObject.AddComponent<CharacterRig>();
            if (GetComponent<PoseGrab>() == null) gameObject.AddComponent<PoseGrab>();

            // spell physics (burn/freeze/crush) speaks Damageable — bridge it
            // into the controller's health so players are exactly as
            // FLAMMABLE as everything else (Marko: burning characters is the
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
        /// Where the static first-person eye point sits right now (world) —
        /// CharacterRig calibrates its head-bone camera anchor against this.
        public Vector3 EyeCenterWorld => transform.TransformPoint(new Vector3(0f, _camY, 0f));

        /// CharacterRig feeds the eye point RIDING THE HEAD BONE every frame:
        /// sprint leans, bob and wobble move the camera WITH the face, so the
        /// hat and googly eyes can never end up in front of the lens. Must be
        /// re-fed each frame — a stale anchor (ragdoll, third person) simply
        /// falls back to the static eye height.
        public void SetEyeAnchor(Vector3 world)
        {
            _eyeAnchorLocal = transform.InverseTransformPoint(world);
            _eyeAnchorFresh = true;

            // RIGID GLUE, same frame: a sprint start lurches the head forward
            // faster than next frame's gentle chase — for a few frames the
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

        /// CharacterRig moved the googly eyes onto the head bone — adopt them.
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

            // THE EYES ARE NEVER HIDDEN. Marko's tuned fit keeps them out of
            // the first-person view; hiding them was tried twice and hated
            // twice. Leave them alone.

            // ---- cursor handling ----
            // TAB flips the camera; third person = the emote stage. Either
            // direction lands on the plain idle — a saved emote pose only
            // replays when its key is pressed again (Marko's spec).
            if (kb.tabKey.wasPressedThisFrame && !IsDowned && !SelfPaint.IsActive)
            {
                ThirdPersonActive = !ThirdPersonActive;
                if (_emotes == null) _emotes = GetComponent<EmotePlayer>();
                _emotes?.StopToRest();
                // back to first person: aim owns the pivot again, immediately
                if (!ThirdPersonActive && CameraPivot != null)
                    CameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
            if (_cam != null && !SelfPaint.IsActive && !PoseGrab.IsOpen) // easel modes own the camera
            {
                if (ThirdPersonActive)
                {
                    // Meccha framing: boom up and back off the pivot, camera
                    // LOOKS AT the bean — pitch orbits vertically, yaw spins
                    // around, the whole body stays in frame
                    _cam.transform.localPosition = Vector3.Lerp(_cam.transform.localPosition,
                        new Vector3(0f, 1.15f, -3.6f), Time.deltaTime * 8f);
                    var look = Quaternion.LookRotation(
                        transform.position + Vector3.up * 0.35f - _cam.transform.position,
                        Vector3.up);
                    _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, look,
                        Time.deltaTime * 8f);
                }
                else
                {
                    // rotation is the look block's business in first person —
                    // touching it here would drift the view during Alt precision.
                    // The eye-forward offset PITCHES with the view (orbits the
                    // eye point), so looking down never shows your own head.
                    // The eye point itself RIDES THE HEAD BONE when the rig
                    // feeds one (sprint leans move the camera WITH the face —
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
            bool precision = altPrecision || HeldWeapon.DrawMode || SelfPaint.IsActive
                || PoseGrab.IsOpen;
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
            // frozen — no move/jump/crouch commands (WASD steers the view
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
                if (gp != null) // right stick — same draw-slowdown applies
                    d += gp.rightStick.ReadValue() * sens * 1400f * Time.deltaTime;
                transform.Rotate(0f, d.x, 0f); // yaw spins the body in both modes
                if (!ThirdPersonActive)
                {
                    // pitch aims the pivot in FIRST person only — in third the
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
                }
                // camera keels over — the world from the floor
                if (CameraPivot != null)
                {
                    var e = CameraPivot.localEulerAngles;
                    CameraPivot.localEulerAngles = new Vector3(e.x, e.y,
                        Mathf.LerpAngle(e.z, 55f, Time.deltaTime * 3f));
                }
            }
            else
            {
                // sprawled: knocked clean off your feet — camera keels hard,
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

                // rescue: a downed friend in range announces itself — HOLD E
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
                    UIPrompt.Show("E", fallen.ReviveProgress > 0f
                        ? Loc.F("revive.pct", Mathf.RoundToInt(fallen.ReviveProgress * 100f))
                        : Loc.T("revive.friend"),
                        new Color(0.55f, 1f, 0.6f));
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

            bool sprint = kb.leftShiftKey.isPressed || (gp != null && gp.leftStickButton.isPressed);
            float speed = IsDead ? 0f
                : IsDowned ? MoveSpeed * 0.25f // crawl
                : IsSprawled ? 0f              // flat on your face — momentum owns you
                : IsAirTumbling ? 0f           // ragdolls don't steer — the launch owns you
                : sprint ? SprintSpeed : MoveSpeed;
            if (IsCrouched) speed *= 0.5f;
            Vector3 planar = (transform.right * mv.x + transform.forward * mv.y) * speed;

            if (_cc.isGrounded)
            {
                bool justLanded = !_wasGrounded;

                // FALL DAMAGE (Marko: non-damage runes kill through physics —
                // a FLOAT running out over a cliff must mean something).
                // But a fall NEVER finishes you by itself (Marko's rule): the
                // worst cliff leaves you at 1 hp, floored and humiliated.
                float landing = -_verticalVelocity;
                if (justLanded && landing > DrawingConfig.SafeFallSpeed && !IsDowned)
                {
                    float dmg = (landing - DrawingConfig.SafeFallSpeed) * DrawingConfig.FallDamagePerSpeed;
                    dmg = Mathf.Min(dmg, Mathf.Max(0f, Health - 1f));
                    if (dmg > 0f) TakeHit(Vector3.zero, dmg);
                    else KnockDown(1.1f); // already scraping 1 hp: just the pratfall
                    Juice.Thud(transform.position);
                    Juice.Shake(Mathf.Min(0.5f, landing * 0.02f));
                }
                _wasGrounded = true;
                _airTime = 0f;

                // an air tumble ends with a short FLOP on the ground — half a
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
                if (jump && !IsDowned && !IsSprawled && !IsAirTumbling && !drawingMode)
                    _verticalVelocity = IsCrouched ? JumpSpeed * 1.15f : JumpSpeed; // window-sill spring
            }
            else
            {
                _wasGrounded = false;
                _verticalVelocity += Gravity * Time.deltaTime;

                // AIRBORNE TOO LONG = you were SENT (Marko's comedy rule):
                // no jump lasts this long — only launches and cliffs. SPELL
                // FLIGHT is exempt: a broom seal holding you up is a
                // mechanic, not a mishap — the clock only runs on ballistic
                // air. The body ragdolls for the rest of the arc; fall
                // damage and the flop are waiting at the bottom.
                if (_spellVel.y > 0.5f) _airTime = 0f;
                else _airTime += Time.deltaTime;
                if (!IsAirTumbling && _airTime >= DrawingConfig.AirTumbleSeconds
                    && !IsDowned && !IsSprawled)
                {
                    IsAirTumbling = true;
                    _tumbleRecover = DrawingConfig.AirTumbleRecover;
                    // carry the momentum you had into the tumble — the capsule
                    // keeps sailing the arc (decaying) instead of stopping
                    // dead mid-air while the ragdoll flies on without it
                    _spellVel.x += planar.x;
                    _spellVel.z += planar.z;
                    Juice.Whoosh(transform.position); // the "uh oh" cue
                }
            }

            // external shoves decay quickly but land with punch
            _shove = Vector3.MoveTowards(_shove, Vector3.zero, 18f * Time.deltaTime);

            // spell forces decay gently — flight, launches, downdrafts.
            // While the spell pushes up, it owns your vertical (gravity waits).
            _spellVel = Vector3.MoveTowards(_spellVel, Vector3.zero, 5f * Time.deltaTime);
            if (_spellVel.y > 0.5f) _verticalVelocity = _spellVel.y;
            Vector3 spellPlanar = new Vector3(_spellVel.x, 0f, _spellVel.z);

            // slow health regen once out of danger (never while down);
            // the Survival perk raises both the ceiling and the mend rate
            if (!IsDowned && Health < Perks.MaxHealth && Time.time - _lastHurt > 5f)
                Health = Mathf.MoveTowards(Health, Perks.MaxHealth, Perks.HealthRegenPerSec * Time.deltaTime);

            if (_ragdollFollow != null)
            {
                // the doll leads, the capsule (and camera) follows — downed
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
                // per-SECOND ceiling (a per-frame clamp let the doll outrun
                // the capsule to the leash on low-fps machines)
                _cc.Move(Vector3.ClampMagnitude(gap * 12f, 120f) * Time.deltaTime);
            }
            else
            {
                _cc.Move((planar + _shove + spellPlanar + Vector3.up * _verticalVelocity) * Time.deltaTime);
            }

            // THE WORLD'S ABSOLUTE FLOOR: however far past the map (and its
            // FallCatcher slab) you were flung, -12y always brings you home —
            // mid-run at a price (floored, revivable), in the lobby for free.
            if (transform.position.y < FallCatcher.KillY) FallCatcher.Catch(this);
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // shove crates around so half-drawn arcs on two objects can meet and seal;
        // walking into conjured Gold/Diamond collects it
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var matter = hit.collider.GetComponent<Matter>();
            if (matter != null && matter.TreasureValue > 0)
            {
                Wallet.Riches += matter.TreasureValue;
                Debug.Log($"[SpellyZombie] Collected {matter.Material} (+{matter.TreasureValue}) — riches: {Wallet.Riches}");
                Destroy(matter.gameObject);
                return;
            }

            var body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic) return;
            if (hit.moveDirection.y < -0.3f) return; // don't push things we stand on

            Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            body.linearVelocity = new Vector3(dir.x * PushStrength, body.linearVelocity.y, dir.z * PushStrength);
        }
    }
}
