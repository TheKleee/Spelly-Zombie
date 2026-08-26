using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Death mode: the body stays where it fell and the player flies off it
    /// as a ghost. Revival needs a living teammate at the body and the ghost
    /// hovering over it; in the lobby the ghost revives itself.
    [DefaultExecutionOrder(500)] // runs after the controller and rig write the camera
    public class GhostState : MonoBehaviour
    {
        public static bool LocalIsGhost { get; private set; }

        static readonly System.Collections.Generic.List<GhostState> All =
            new System.Collections.Generic.List<GhostState>();

        [Tooltip("Your ghost prefab. Assign it here in the Inspector.")]
        public GameObject GhostPrefab;

        [Tooltip("How close the ghost must hover to its own body, meters.")]
        public float HomeRange = 2.5f;
        [Tooltip("Lobby only: seconds of hovering over your own body to stand back up.")]
        public float LobbySeconds = 3f;

        [Tooltip("Flight speed.")]
        public float GhostSpeed = 6f;

        [Tooltip("Leave OFF for free flight. ON restricts movement to forward plus trim.")]
        public bool ForwardOnly;

        public int OwnerId { get; private set; }

        /// NetAvatar instantiates this for remote ghosts.
        public static GameObject SharedPrefab { get; private set; }

        /// Streamed by NetSync so remote clients can show this ghost.
        public Vector3 SpiritAt => _ghost != null ? _ghost.position : transform.position;
        public float SpiritYaw => _ghost != null ? _ghost.eulerAngles.y : 0f;

        SimpleFPSController _pilot;
        Camera _bodyCam;
        Transform _ghost;
        Transform _visual;
        Camera _ghostCam;
        bool _third;               // set while driving a spell or zombie
        Vector3 _camOffset;        // glides between views instead of snapping
        SpellParticle _ridden;
        Rigidbody _rbRidden;   // conjured spell matter: liquids, solids, combos
        Zombie _zombie;
        float _noGrabUntil;
        float _yaw, _pitch;
        float _homeTime;           // lobby self-revive clock
        float _revive;             // match revive progress, 0..1

        public bool IsGhost { get; private set; }
        /// Live corpse position, not where the death happened.
        public Vector3 BodyAt => transform.position;
        public bool AtHome => IsGhost
            && (_ghost.position - BodyAt).sqrMagnitude <= HomeRange * HomeRange;

        void Awake()
        {
            _pilot = GetComponent<SimpleFPSController>();
            All.Add(this);
            if (GhostPrefab != null) SharedPrefab = GhostPrefab;
        }
        void OnDestroy() { All.Remove(this); if (IsGhost) Land(); }

        void Update()
        {
            if (_pilot == null) return;

            // the controller is disabled while ghosting, so this owns the state
            if (IsGhost && !_pilot.IsDowned) { Land(); return; }

            if (!IsGhost)
            {
                // K = die on purpose, lobby and matches for now
                if (_pilot.IsLocalViewer && !_pilot.IsDowned)
                {
                    var kb0 = Keyboard.current;
                    if (kb0 != null && kb0.kKey.wasPressedThisFrame && !UIKit.Typing && !GameMenu.IsOpen)
                        _pilot.DieOutright(); // bypasses the sandbox mercy heal
                }
                if (_pilot.IsDead) Rise();
                else if (_pilot.IsLocalViewer) OfferRescue();
                return;
            }

            FlyGhost();
            TickPossession();
            TickRevive();
        }

        /// Touching a spell (or a zombie, for acolytes) takes control of it.
        /// Own team only. A dormant spell wakes on contact.
        void TickPossession()
        {
            if (_ghostCam == null) return; // only the owner drives

            bool acolyte = Sides.Of(OwnerId) == Side.Acolyte;

            // ReferenceEquals: a destroyed zombie compares equal to null
            // through Unity's overload
            if (!ReferenceEquals(_zombie, null))
            {
                // dead counts as gone: the corpse object can linger a while
                var dmg = _zombie == null ? null : _zombie.GetComponent<Element>();
                if (_zombie == null || !_zombie.isActiveAndEnabled
                    || (dmg != null && dmg.Health <= 0f))
                {
                    LeaveZombie();
                    return;
                }
                DriveZombie();
                return;
            }

            if (_ridden != null && _ridden.Dead)
            {
                _ridden = null;
                _third = false;
            }
            if (_ridden != null)
            {
                var kbD = Keyboard.current;
                if (kbD != null && !GameMenu.IsOpen && !UIKit.Typing)
                {
                    if (kbD.fKey.wasPressedThisFrame)
                    {
                        _ridden = null;
                        _third = false;
                        _noGrabUntil = Time.time + 0.8f;
                        Juice.Chime(_ghost.position);
                        DrawingWorld.Instance?.LogEvent("you release the spell");
                        return;
                    }
                    Vector3 drive = Vector3.zero;
                    if (kbD.wKey.isPressed) drive += _ghost.forward;
                    if (kbD.sKey.isPressed) drive -= _ghost.forward;
                    if (kbD.dKey.isPressed) drive += _ghost.right;
                    if (kbD.aKey.isPressed) drive -= _ghost.right;
                    if (kbD.spaceKey.isPressed) drive += Vector3.up;
                    if (kbD.leftCtrlKey.isPressed) drive -= Vector3.up;
                    if (drive.sqrMagnitude > 0.001f)
                        _ridden.Steer(drive.normalized, kbD.leftShiftKey.isPressed ? 14f : 7f);
                }
                _ghost.position = _ridden.transform.position;
                return;
            }

            if (!ReferenceEquals(_rbRidden, null))
            {
                if (_rbRidden == null) // the chemistry ate it
                {
                    _rbRidden = null;
                    _third = false;
                }
                else
                {
                    DriveMatter();
                    return;
                }
            }

            if (Time.time < _noGrabUntil) return; // just released: no instant regrab

            foreach (var h in Physics.OverlapSphere(_ghost.position, 0.6f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                if (acolyte)
                {
                    var z = h.GetComponentInParent<Zombie>();
                    if (z == null || z.Possessed) continue;
                    var mine = z.GetComponent<SummonedZombie>();
                    if (mine == null || Sides.Of(mine.SummonedBy) != Side.Acolyte) continue;
                    _zombie = z;
                    z.PossessBy(true);
                    _third = true;
                    Juice.Chime(_ghost.position);
                    DrawingWorld.Instance?.LogEvent("you take the zombie. LMB uses what it is");
                    return;
                }

                // conjured matter in spell form: liquid, solid, and what
                // combinations made of them - all of it rides like a spell
                var strike = h.GetComponentInParent<MatterStrike>();
                if (strike != null && strike.SpellForm && strike.OwnerId != 0
                    && Sides.Of(strike.OwnerId) == Sides.Of(OwnerId)
                    && strike.GetComponent<Rigidbody>() != null)
                {
                    _rbRidden = strike.GetComponent<Rigidbody>();
                    _third = true;
                    Juice.Chime(_ghost.position);
                    DrawingWorld.Instance?.LogEvent("you combine with the spell");
                    break;
                }

                var p = h.GetComponentInParent<SpellParticle>();
                if (p == null || p.Dead) continue;
                // unowned ink (id 0) belongs to nobody and cannot be driven
                if (p.OwnerId == 0 || Sides.Of(p.OwnerId) != Sides.Of(OwnerId)) continue;
                _ridden = p;
                if (p.Dormant) p.Wake();
                _third = true;
                Juice.Chime(_ghost.position);
                DrawingWorld.Instance?.LogEvent("you combine with the spell");
                break;
            }
        }

        /// Writes the zombie's brain inputs so it walks with its own physics.
        /// Full WASD relative to the look, and the camera rides in its head.
        void DriveZombie()
        {
            // ★ A FALLEN BODY THROWS ITS RIDER (his rule): knocked down or
            // ragdolled - Slipping/GettingUp, the actual fall states - the
            // ghost slips out, same as pressing F. NOT on Stuck/Frozen: a
            // rooted body keeps its rider (keying this on CanMove ejected the
            // ghost every time its own goo puddle gripped its feet).
            var body = _zombie != null ? _zombie.GetComponent<Creature>() : null;
            if (_zombie == null || (body != null && (body.Slipping || body.GettingUp)))
            {
                _noGrabUntil = Time.time + 0.8f;
                DrawingWorld.Instance?.LogEvent("the body tumbles away from you");
                LeaveZombie();
                return;
            }

            var brain = _zombie.GetComponent<ZombieBrain>();
            var kb = Keyboard.current;
            if (brain == null || kb == null) return;

            bool uiBusy = GameMenu.IsOpen || UIKit.Typing;
            if (!uiBusy && kb.fKey.wasPressedThisFrame)
            {
                _noGrabUntil = Time.time + 0.8f;
                Juice.Chime(_ghost.position);
                DrawingWorld.Instance?.LogEvent("you release the zombie");
                LeaveZombie();
                return;
            }

            Vector3 fwd = _ghost.forward; fwd.y = 0f;
            Vector3 right = _ghost.right; right.y = 0f;
            Vector3 move = Vector3.zero;
            if (!uiBusy)
            {
                if (kb.wKey.isPressed) move += fwd;
                if (kb.sKey.isPressed) move -= fwd;
                if (kb.dKey.isPressed) move += right;
                if (kb.aKey.isPressed) move -= right;
            }
            bool moving = move.sqrMagnitude > 0.01f;
            brain.MoveDir = moving ? move.normalized : Vector3.zero;
            brain.SpeedScale = moving ? 1f : 0f;
            if (fwd.sqrMagnitude > 0.01f) _zombie.PossessedFace = fwd.normalized;

            _ghost.position = _zombie.HeadAt;

            var mouse = Mouse.current;
            if (!uiBusy && mouse != null && (mouse.leftButton.wasPressedThisFrame
                || mouse.rightButton.wasPressedThisFrame))
                _zombie.GhostAbility(_ghost.forward);   // the FULL look: casts pitch too
        }

        /// Conjured matter rides like a spell: eased velocity, F lets go.
        void DriveMatter()
        {
            var kb = Keyboard.current;
            if (kb != null && !GameMenu.IsOpen && !UIKit.Typing)
            {
                if (kb.fKey.wasPressedThisFrame)
                {
                    _rbRidden = null;
                    _third = false;
                    _noGrabUntil = Time.time + 0.8f;
                    Juice.Chime(_ghost.position);
                    DrawingWorld.Instance?.LogEvent("you release the spell");
                    return;
                }
                Vector3 drive = Vector3.zero;
                if (kb.wKey.isPressed) drive += _ghost.forward;
                if (kb.sKey.isPressed) drive -= _ghost.forward;
                if (kb.dKey.isPressed) drive += _ghost.right;
                if (kb.aKey.isPressed) drive -= _ghost.right;
                if (kb.spaceKey.isPressed) drive += Vector3.up;
                if (kb.leftCtrlKey.isPressed) drive -= Vector3.up;
                if (drive.sqrMagnitude > 0.001f)
                    _rbRidden.linearVelocity = Vector3.MoveTowards(_rbRidden.linearVelocity,
                        drive.normalized * (kb.leftShiftKey.isPressed ? 14f : 7f),
                        26f * Time.deltaTime);
            }
            _ghost.position = _rbRidden.transform.position;
        }

        void LeaveZombie()
        {
            if (_zombie != null && !_zombie.Equals(null)) _zombie.PossessBy(false);
            _zombie = null;
            _third = false;
        }

        /// Where the local player actually LOOKS from - the ghost camera while
        /// ghosting. Distance culls measure from here, or effects around a
        /// driven zombie are culled against the corpse's parked camera.
        public static Vector3? LocalViewPoint
        {
            get
            {
                foreach (var g in All)
                    if (g != null && g.IsGhost && g._ghostCam != null)
                        return g._ghostCam.transform.position;
                return null;
            }
        }

        /// Match start: the local lobby ghost stands back up on the spot.
        public static void ReviveLocalNow()
        {
            foreach (var g in All)
                if (g != null && g.IsGhost && g._ghostCam != null)
                {
                    g._pilot.Revive();
                    g.Land();
                    return;
                }
        }

        /// A living player holding E over a teammate's body whose ghost is home.
        void OfferRescue()
        {
            if (ActiveScene.Name == "Lobby") return;  // lobby ghosts revive themselves
            // only the LIVING lend a hand: no ghost, no corpse
            if (IsGhost || _pilot.IsDowned || _pilot.IsDead) return;
            var target = BodyNear(transform.position, Sides.Of(Grimoire.LocalPlayerId), 2.5f);
            if (target == null || target == this) return;

            var kb = Keyboard.current;
            if (kb != null && kb.eKey.isPressed) target.AddGhostRevive(_pilot, Time.deltaTime);
        }

        void Rise()
        {
            IsGhost = true;
            OwnerId = _pilot.IsLocalViewer ? Grimoire.LocalPlayerId : OwnerId;
            _homeTime = 0f;
            _revive = 0f;

            bool local = _pilot.IsLocalViewer;
            _ghost = BuildSpirit(local);
            if (_ghost == null) { IsGhost = false; return; }
            if (local)
            {
                LocalIsGhost = true;
                // the player's camera flies the ghost. It is NOT reparented:
                // SimpleFPSController and CharacterRig both rewrite its local
                // position every frame, so it is driven in world space from
                // LateUpdate instead.
                _bodyCam = GetComponentInChildren<Camera>(true);
                _ghostCam = _bodyCam;
                var eye = _pilot.CameraPivot != null ? _pilot.CameraPivot : transform;
                _yaw = eye.eulerAngles.y;
                _pitch = 10f;

                // re-lock: the disabled controller cannot, and precise-draw death leaves it free
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                // stops the downed body reading the same WASD the ghost flies with
                _pilot.enabled = false;
                XRayGlow.Show(gameObject); // the corpse stays findable through walls
            }
            DrawingWorld.Instance?.LogEvent("you are a ghost. fly home to your body to be revived");
        }

        /// Spawns the assigned prefab and tints it for the side.
        Transform BuildSpirit(bool local)
        {
            if (GhostPrefab == null)
            {
                Debug.LogError("[SpellyZombie] GhostState.GhostPrefab is EMPTY on " + name
                    + ". Assign your Ghost prefab in the Inspector.", this);
                return null;
            }

            var go = Object.Instantiate(GhostPrefab,
                BodyAt + Vector3.up * 1.5f, Quaternion.identity);
            go.name = "Ghost";
            _visual = go.transform;

            // the player's camera is the only camera, and a ghost is not matter
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (var a in go.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (Named(r.transform, "eye")) continue;
                PillarBeam.Tint(r, Named(r.transform, "hat")
                    ? (HatColor.Saved() ?? SideColor()) : SideColor());
            }

            _third = false;
            _camOffset = new Vector3(0f, 0.05f, 0.45f);
            RefreshLook();
            return go.transform;
        }

        public static readonly Color WizardGhost = new Color(0.94f, 0.95f, 0.86f);

        /// Ghost side tints: pale and thin, never the full ink colors.
        public static Color GhostSideColor(bool acolyte)
        {
            Color c = acolyte
                ? Color.Lerp(DrawingConfig.CorruptInkColor, Color.white, 0.5f)
                : WizardGhost;
            c.a = 0.32f;
            return c;
        }

        Color SideColor() => GhostSideColor(Sides.Of(OwnerId) == Side.Acolyte);

        public static bool Named(Transform t, string word)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// The ghost is never hidden, in any view or possession state.
        void RefreshLook()
        {
            if (_visual != null)
                foreach (var r in _visual.GetComponentsInChildren<Renderer>(true))
                    r.enabled = true;
        }

        void FlyGhost()
        {
            if (_ghostCam == null) return; // remote ghost: driven by its owner
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || GameMenu.IsOpen || UIKit.Typing) return;

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue() * 0.08f;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
            }
            _ghost.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            if (_ridden != null || !ReferenceEquals(_rbRidden, null)
                || _zombie != null) return; // driving something else

            Vector3 move = Vector3.zero;
            bool w = kb.wKey.isPressed;
            if (w) move += _ghost.forward;
            if (!ForwardOnly || w)
            {
                float trim = ForwardOnly ? 0.7f : 1f;
                if (!ForwardOnly && kb.sKey.isPressed) move -= _ghost.forward;
                if (kb.dKey.isPressed) move += _ghost.right * trim;
                if (kb.aKey.isPressed) move -= _ghost.right * trim;
                if (kb.spaceKey.isPressed) move += Vector3.up * trim;
                if (kb.leftCtrlKey.isPressed) move -= Vector3.up * trim;
            }
            if (move.sqrMagnitude > 0.001f)
                _ghost.position += move.normalized * (GhostSpeed * Time.deltaTime);
        }

        void TickRevive()
        {
            bool home = AtHome;

            // lobby: hover over your own body, no teammate needed
            if (ActiveScene.Name == "Lobby")
            {
                if (home)
                {
                    _homeTime += Time.deltaTime;
                    Shine();
                    if (_homeTime >= LobbySeconds) { _pilot.Revive(); Land(); }
                }
                else _homeTime = 0f;
                return;
            }

            // match: needs the ghost home AND a teammate holding E on the body
            if (!home) { _revive = 0f; return; }
            if (_revive >= 1f) { _pilot.Revive(); Land(); }
        }

        /// The rescuer must be ALIVE - a corpse or another ghost cannot
        /// revive anyone, checked here so every caller inherits the rule.
        public void AddGhostRevive(SimpleFPSController by, float dt)
        {
            if (by == null || by.IsDowned || by.IsDead) return;
            if (!IsGhost || !AtHome) return;
            _revive += dt / Mathf.Max(0.5f, DrawingConfig.ReviveSeconds);
            Shine();
        }

        float _shineUntil;

        /// The revive has no UI. This VFX off the body is the only signal.
        void Shine()
        {
            if (Time.time < _shineUntil || FxLibrary.I == null) return;
            _shineUntil = Time.time + 0.9f;
            FxLibrary.Spawn(FxLibrary.I.HealShine, BodyAt + Vector3.up * 0.35f,
                transform, 1.1f);
        }

        void Land()
        {
            IsGhost = false;
            if (_pilot != null) _pilot.enabled = true; // the body takes orders again
            _homeTime = 0f;
            _revive = 0f;
            _ridden = null;
            _rbRidden = null;
            LeaveZombie();
            _visual = null;
            _third = false;
            if (!SimpleFPSController.ThirdPersonActive) XRayGlow.Hide(gameObject);
            if (_ghostCam != null) LocalIsGhost = false;
            // the camera needs no restoring: it never left its parent, and the
            // controller's eye lerp takes it back once this stops writing it
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
            _ghostCam = null;
            _bodyCam = null;
        }

        /// Writes the ghost view last, after the controller and rig.
        /// World space, because the prefab is scaled and a local offset
        /// would shrink the third person distance with it.
        void LateUpdate()
        {
            if (!IsGhost || _ghostCam == null || _ghost == null) return;
            Quaternion look = Quaternion.Euler(_pitch, _yaw, 0f);

            // the offset glides so the view never snaps between the views;
            // driving a zombie puts the camera inside its head
            bool inZombie = !ReferenceEquals(_zombie, null) && _zombie != null;
            Vector3 want = inZombie
                ? new Vector3(0f, 0.02f, 0.18f)
                : _third
                    ? new Vector3(0f, 0.6f, -2.8f)
                    : new Vector3(0f, 0.05f, 0.45f);
            _camOffset = Vector3.MoveTowards(_camOffset, want, 11f * Time.deltaTime);

            _ghostCam.transform.SetPositionAndRotation(_ghost.position + look * _camOffset, look);
        }

        // ------------------------------------------- the teammate's side --
        /// The nearest revivable body for a living player: dead, same side,
        /// and its ghost is hovering at home.
        public static GhostState BodyNear(Vector3 at, Side side, float range)
        {
            GhostState best = null;
            float bestSqr = range * range;
            foreach (var g in All)
            {
                if (g == null || !g.AtHome) continue;
                if (Sides.Of(g.OwnerId) != side) continue;
                float d = (g.BodyAt - at).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = g; }
            }
            return best;
        }
    }
}
