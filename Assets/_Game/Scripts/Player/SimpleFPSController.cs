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
        bool _wasPrecision;
        Vector3 _shove; // external impulse (zombie swipes, explosions) — decays fast

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
                    Health = 100f; // sandbox mercy respawn-in-place
                    Debug.Log("[SpellyZombie] Player DOWN — shaking it off (sandbox)");
                }
            }
        }
        float _lastHurt;

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
            LookSensitivity = PlayerPrefs.GetFloat("sz_look_sens", LookSensitivity);
            LockCursor();

            // every caster has an identity; seals are owned by whoever completes them
            Grimoire.LocalPlayerId = gameObject.GetInstanceID();

            // the player is googly too — other players (and clips) see your eyes
            // dart to explosions and shrink in terror. Hidden from your own
            // first-person camera; multiplayer remote avatars re-enable them.
            var eyes = GooglyEyes.Attach(transform, 1.55f, 1.4f);
            eyes.SetVisible(CameraPivot == null || !CameraPivot.GetComponentInChildren<Camera>());

            if (GetComponent<PlayerInk>() == null) gameObject.AddComponent<PlayerInk>();
            All.Add(this);
        }

        void OnDestroy() => All.Remove(this);

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // frozen while posing in the studio or paused; they own the cursor
            if (PoseStudio.IsOpen || GameMenu.IsOpen) return;

            // ---- cursor handling ----
            bool precision = kb.leftAltKey.isPressed;
            if (precision)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (_wasPrecision)
            {
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
                transform.Rotate(0f, d.x, 0f);
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
                if (CameraPivot != null)
                    CameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);
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

                // rescue: hold E (or gamepad X) over a downed friend
                if (kb.eKey.isPressed || (gp != null && gp.buttonWest.isPressed))
                {
                    foreach (var other in All)
                    {
                        if (other == this || !other.IsDowned || other.IsDead) continue;
                        if ((other.transform.position - transform.position).sqrMagnitude
                            > DrawingConfig.ReviveRange * DrawingConfig.ReviveRange) continue;
                        other.AddRevive(Time.deltaTime);
                        break;
                    }
                }
            }

            // ---- move ----
            Vector2 mv = Vector2.zero;
            if (kb.wKey.isPressed) mv.y += 1f;
            if (kb.sKey.isPressed) mv.y -= 1f;
            if (kb.dKey.isPressed) mv.x += 1f;
            if (kb.aKey.isPressed) mv.x -= 1f;
            if (gp != null) mv += gp.leftStick.ReadValue();
            if (mv.sqrMagnitude > 1f) mv.Normalize();

            bool sprint = kb.leftShiftKey.isPressed || (gp != null && gp.leftStickButton.isPressed);
            float speed = IsDead ? 0f
                : IsDowned ? MoveSpeed * 0.25f // crawl
                : IsSprawled ? 0f              // flat on your face — momentum owns you
                : sprint ? SprintSpeed : MoveSpeed;
            Vector3 planar = (transform.right * mv.x + transform.forward * mv.y) * speed;

            if (_cc.isGrounded)
            {
                _verticalVelocity = -1f;
                bool jump = kb.spaceKey.wasPressedThisFrame
                    || (gp != null && gp.buttonSouth.wasPressedThisFrame);
                if (jump && !IsDowned && !IsSprawled)
                    _verticalVelocity = JumpSpeed;
            }
            else
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }

            // external shoves decay quickly but land with punch
            _shove = Vector3.MoveTowards(_shove, Vector3.zero, 18f * Time.deltaTime);

            // spell forces decay gently — flight, launches, downdrafts.
            // While the spell pushes up, it owns your vertical (gravity waits).
            _spellVel = Vector3.MoveTowards(_spellVel, Vector3.zero, 5f * Time.deltaTime);
            if (_spellVel.y > 0.5f) _verticalVelocity = _spellVel.y;
            Vector3 spellPlanar = new Vector3(_spellVel.x, 0f, _spellVel.z);

            // slow health regen once out of danger (never while down)
            if (!IsDowned && Health < 100f && Time.time - _lastHurt > 5f)
                Health = Mathf.MoveTowards(Health, 100f, 8f * Time.deltaTime);

            _cc.Move((planar + _shove + spellPlanar + Vector3.up * _verticalVelocity) * Time.deltaTime);
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
