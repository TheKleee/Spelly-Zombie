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

        /// A possession or release chime: the others hear it too (Chime is not a WorldSound).
        static void GhostChime(Vector3 at)
        {
            NetSync.PlayAndPushBodyFx(10, at);
        }

        SimpleFPSController _pilot;
        Camera _bodyCam;
        Transform _ghost;
        Transform _visual;
        Camera _ghostCam;
        bool _third;               // set while driving a spell or zombie
        Vector3 _camOffset;        // glides between views instead of snapping
        SpellParticle _ridden;
        Rigidbody _rbRidden;   // conjured spell matter: liquids, solids, combos
        int _riddenLife;       // the ridden spell's LifeId when taken
        Zombie _zombie;
        Golem _golem;
        float _noGrabUntil;
        float _yaw, _pitch;
        float _homeTime;           // lobby self-revive clock
        float _revive;             // match revive progress, 0..1
        int _lastRescuer = -1;     // who stood at us over the wire, for their achievement
        float _remoteHold;         // our time at a remote friend's body, shipped in slices
        float _remoteShine;
        // on a client the creatures are the host's stand-ins; the host drives
        Transform _proxy;
        byte _proxyKind;           // 1 zombie, 2 golem, 3 spell
        int _proxyId;
        float _askUntil;
        int _askedSpell, _skipSpell; // a spell asked for, and one the host kept quiet about
        float _skipUntil;

        /// A ghost takes a spell of its own side, or a teammate's where wizards and acolytes play as one
        /// team; unowned ink (id 0) belongs to nobody. The host judges a client's ghost by this same rule.
        public static bool CanRide(SpellParticle p, int ghostOwner)
            => p != null && !p.Dead && MayRideSpellOf(p.OwnerId, ghostOwner);

        /// The owner half of that rule; conjured matter flying as a spell rides by it too.
        public static bool MayRideSpellOf(int spellOwner, int ghostOwner)
            => spellOwner != 0 && (Sides.Of(spellOwner) == Sides.Of(ghostOwner)
                || !Teams.Enemies(Teams.OfOwner(spellOwner), Teams.OfOwner(ghostOwner)));

        /// How fast a ridden spell is steered, with the sprint key and without.
        public static float RideSpeed(bool fast) => fast ? 14f : 7f;

        /// HOST: one of this machine's own ghosts rides this spell.
        public static bool LocalRides(SpellParticle p)
        {
            if (p == null) return false;
            foreach (var g in All) if (g != null && g._ridden == p) return true;
            return false;
        }

        /// HOST: another ghost stole this spell (his call): whoever of ours rode it is thrown off.
        public static void DropSpell(SpellParticle p)
        {
            if (p == null) return;
            foreach (var g in All)
            {
                if (g == null || g._ridden != p) continue;
                g._ridden = null;
                g._third = false;
                g._noGrabUntil = Time.time + 0.8f;
                GhostChime(g._ghost.position);
                DrawingWorld.Instance?.LogEvent("a ghost stole your spell");
            }
        }

        /// Where a rider sits in what it rides, on every machine: a zombie's eyes, a golem's seat,
        /// a spell's or a lump of matter's heart.
        public static Vector3 SeatIn(Transform ridden)
        {
            if (ridden.TryGetComponent<NetZombieProxy>(out var zp)) return zp.HeadAt;
            if (ridden.TryGetComponent<NetGolemProxy>(out var gp)) return gp.SeatAt;
            if (ridden.TryGetComponent<Zombie>(out var z)) return z.HeadAt;
            if (ridden.TryGetComponent<Golem>(out var g)) return g.SeatAt;
            return ridden.position;
        }

        /// The reins of a ridden spell: where the ghost looks, sideways, up and down.
        Vector3 SpellReins()
        {
            Vector2 mv = Keys.Move; // the move keys and the left stick
            Vector3 drive = _ghost.forward * mv.y + _ghost.right * mv.x;
            if (Keys.Held(Act.Jump)) drive += Vector3.up;
            if (Keys.Held(Act.Crouch)) drive -= Vector3.up;
            return drive.sqrMagnitude > 0.001f ? drive.normalized : Vector3.zero;
        }

        public bool IsGhost { get; private set; }
        /// Riding something right now: a spell, conjured matter, a zombie, a golem, or the host's stand-in for one.
        public bool Driving => _ridden != null || _rbRidden != null || _zombie != null || _golem != null || _proxy != null;

        /// What it rides, for the presence: the ride kinds by the host's id (4 = matter), 0 = nothing.
        public byte RideOf(out int id)
        {
            id = 0;
            if (_proxyId != 0) { id = _proxyId; return _proxyKind; }
            if (_zombie != null) { id = _zombie.gameObject.GetInstanceID(); return 1; }
            if (_golem != null) { id = _golem.gameObject.GetInstanceID(); return 2; }
            if (_ridden != null && !_ridden.Dead) { id = _ridden.gameObject.GetInstanceID(); return 3; }
            var lump = _rbRidden != null ? _rbRidden.GetComponentInParent<Matter>() : null;
            if (lump != null) { id = lump.gameObject.GetInstanceID(); return 4; }
            return 0;
        }
        /// Live corpse position, not where the death happened: the body you can SEE. A downed body is
        /// a doll, and a crowd shoves it metres away from the root it fell from; home, a rescuer's
        /// reach and the spirit's start all mean the doll.
        public Vector3 BodyAt
        {
            get
            {
                if (_rig == null) _rig = GetComponentInChildren<CharacterRig>();
                return _rig != null ? _rig.BodyCenter : transform.position;
            }
        }
        CharacterRig _rig;
        public bool AtHome => IsGhost
            && (_ghost.position - BodyAt).sqrMagnitude <= HomeRange * HomeRange;

        void Awake()
        {
            _pilot = GetComponent<SimpleFPSController>();
            All.Add(this);
            if (GhostPrefab != null) SharedPrefab = GhostPrefab;
        }
        void OnDestroy() { All.Remove(this); HideEyes(null); if (IsGhost) Land(); }

        void Update()
        {
            if (_pilot == null) return;

            // the controller is disabled while ghosting, so this owns the state
            if (IsGhost && !_pilot.IsDowned) { Land(); return; }

            if (!IsGhost)
            {
                // K = die on purpose, lobby only
                if (_pilot.IsLocalViewer && !_pilot.IsDowned && ActiveScene.Name == "Lobby")
                {
                    var kb0 = Keyboard.current;
                    if (kb0 != null && kb0.kKey.wasPressedThisFrame && !UIKit.Typing && !GameMenu.IsOpen)
                        _pilot.DieOutright(); // bypasses the sandbox mercy heal
                    // F10 = every rune of the book you hold, to test spells without the deeds (lobby only)
                    if (kb0 != null && kb0.f10Key.wasPressedThisFrame && !UIKit.Typing && !GameMenu.IsOpen)
                    {
                        int added = SideBootstrap.UnlockWholeBook(Grimoire.LocalPlayerId);
                        DrawingWorld.Instance?.LogEvent(added > 0
                            ? $"lobby cheat: {added} runes unlocked" : "lobby cheat: this book is already full");
                        if (added > 0)
                        {
                            Juice.Sound2D(Sfx.RuneComplete);
                            GrimoirePages.RequestOpen(); // the new pages, in hand
                        }
                    }
                }
                if (_pilot.IsDead) Rise();
                else if (_pilot.IsLocalViewer) OfferRescue();
                return;
            }

            if (_ghostCam != null) _pilot.ChaseDoll(); // the pilot is switched off while we fly: its capsule still follows its body
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

            if (!ReferenceEquals(_golem, null))
            {
                if (_golem == null || !_golem.isActiveAndEnabled || !_golem.Alive)
                {
                    LeaveGolem();
                    return;
                }
                DriveGolem();
                return;
            }

            if (!ReferenceEquals(_proxy, null))
            {
                if (_proxy == null) _proxy = NetSync.RideProxy(_proxyKind, _proxyId); // a spell that changed shape got a new stand-in, same id
                if (_proxy == null) { LeaveProxy(false); return; } // the host's snapshot dropped it
                DriveProxy();
                return;
            }

            if (_ridden != null && (_ridden.Dead || _ridden.LifeId != _riddenLife)) // a pooled spell reborn is another one
            {
                // merged away: ride on in what it became (his call), unless a ghost has that one
                var into = _ridden.Dead && _ridden.LifeId == _riddenLife ? _ridden.BecameObj as SpellParticle : null;
                if (into != null && !into.Dead && CanRide(into, OwnerId) && !NetSync.SpellRidden(into) && !LocalRides(into))
                {
                    _ridden = into;
                    _riddenLife = into.LifeId;
                }
                else
                {
                    _ridden = null;
                    _third = false;
                }
            }
            if (_ridden != null)
            {
                if (!GameMenu.IsOpen && !UIKit.Typing)
                {
                    if (Keys.Down(Act.Drop))
                    {
                        _ridden.Coast(); // it flies on at the speed it was given, it does not glide to a stop
                        _ridden = null;
                        _third = false;
                        _noGrabUntil = Time.time + 0.8f;
                        GhostChime(_ghost.position);
                        DrawingWorld.Instance?.LogEvent("you release the spell");
                        return;
                    }
                    Vector3 drive = SpellReins();
                    if (drive.sqrMagnitude > 0.001f)
                        _ridden.Steer(drive, RideSpeed(Keys.Held(Act.Sprint)));
                }
                _ghost.position = SeatIn(_ridden.transform);
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

            // on a client a spell is the host's stand-in, a picture with no collider: the ghost
            // reaches it by distance, the host's own reach (0.6 m past the spell's round body)
            if (NetGame.Connected && !NetGame.IsAuthority && Time.time >= _askUntil)
            {
                // the host says nothing when it refuses: not that spell again for a while
                if (_askedSpell != 0) { _skipSpell = _askedSpell; _skipUntil = Time.time + 3f; _askedSpell = 0; }
                NetMoteProxy near = null;
                float nearSqr = float.MaxValue;
                foreach (var mp in NetMoteProxy.Living)
                {
                    if (mp == null || mp.OwnerId == 0 || Sides.Of(mp.OwnerId) != Sides.Of(OwnerId)) continue;
                    if (mp.HostId == _skipSpell && Time.time < _skipUntil) continue;
                    float reach = 0.6f + 0.5f * mp.transform.localScale.x;
                    float d = (mp.transform.position - _ghost.position).sqrMagnitude;
                    if (d <= reach * reach && d < nearSqr) { nearSqr = d; near = mp; }
                }
                if (near != null)
                {
                    NetSync.SendRideAsk(3, near.HostId, true);
                    _askedSpell = near.HostId;
                    _askUntil = Time.time + 0.6f;
                    return;
                }
            }

            foreach (var h in Physics.OverlapSphere(_ghost.position, 0.6f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                // on a client the creatures are the host's stand-ins: ask the host
                if (NetGame.Connected && !NetGame.IsAuthority)
                {
                    if (Time.time < _askUntil) break;
                    var gp = h.GetComponentInParent<NetGolemProxy>();
                    if (gp != null && gp.OwnerId >= 0 && Sides.Of(gp.OwnerId) == Sides.Of(OwnerId))
                    {
                        NetSync.SendRideAsk(2, gp.Id, true);
                        _askUntil = Time.time + 0.6f;
                        break;
                    }
                    var zp = h.GetComponentInParent<NetZombieProxy>();
                    if (acolyte && zp != null && zp.OwnerId >= 0 && Sides.Of(zp.OwnerId) == Side.Acolyte)
                    {
                        NetSync.SendRideAsk(1, zp.Id, true);
                        _askUntil = Time.time + 0.6f;
                        break;
                    }
                }

                // a golem of your own team, either side (his rule): wild ones
                // (no owner) belong to nobody and cannot be driven
                var g = h.GetComponentInParent<Golem>();
                if (g != null)
                {
                    if (g.Possessed || !g.Alive || g.OwnerId < 0
                        || Sides.Of(g.OwnerId) != Sides.Of(OwnerId)) continue;
                    _golem = g;
                    g.PossessBy(true);
                    if (_pilot.IsLocalViewer) Achievements.Unlock(Achievements.RideGolem);
                    _third = true;
                    GhostChime(_ghost.position);
                    DrawingWorld.Instance?.LogEvent("you take the golem. click to charge");
                    return;
                }

                if (acolyte)
                {
                    var z = h.GetComponentInParent<Zombie>();
                    if (z == null || z.Possessed) continue;
                    var mine = z.GetComponent<SummonedZombie>();
                    if (mine == null || Sides.Of(mine.SummonedBy) != Side.Acolyte) continue;
                    _zombie = z;
                    z.PossessBy(true);
                    if (_pilot.IsLocalViewer) Achievements.Unlock(Achievements.RideZombie);
                    _third = true;
                    GhostChime(_ghost.position);
                    DrawingWorld.Instance?.LogEvent("you take the zombie. LMB uses what it is");
                    return;
                }

                // conjured matter in spell form: liquid, solid, and what
                // combinations made of them - all of it rides like a spell
                var strike = h.GetComponentInParent<MatterStrike>();
                if (strike != null && strike.SpellForm && MayRideSpellOf(strike.OwnerId, OwnerId)
                    && strike.GetComponent<Rigidbody>() != null)
                {
                    _rbRidden = strike.GetComponent<Rigidbody>();
                    _third = true;
                    GhostChime(_ghost.position);
                    DrawingWorld.Instance?.LogEvent("you combine with the spell");
                    break;
                }

                var p = h.GetComponentInParent<SpellParticle>();
                if (!CanRide(p, OwnerId) || LocalRides(p)) continue;
                NetSync.StealSpell(p); // occupied is occupied, but a ghost can steal it (his call)
                _ridden = p;
                _riddenLife = p.LifeId;
                if (p.Dormant) p.Wake();
                _third = true;
                GhostChime(_ghost.position);
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
            if (brain == null) return;

            bool uiBusy = GameMenu.IsOpen || UIKit.Typing;
            if (!uiBusy && Keys.Down(Act.Drop))
            {
                _noGrabUntil = Time.time + 0.8f;
                GhostChime(_ghost.position);
                DrawingWorld.Instance?.LogEvent("you release the zombie");
                LeaveZombie();
                return;
            }

            Vector3 fwd = _ghost.forward; fwd.y = 0f;
            Vector3 right = _ghost.right; right.y = 0f;
            Vector3 move = Vector3.zero;
            if (!uiBusy)
            {
                Vector2 mv = Keys.Move; // the move keys and the left stick
                move += fwd * mv.y + right * mv.x;
            }
            bool moving = move.sqrMagnitude > 0.01f;
            brain.MoveDir = moving ? move.normalized : Vector3.zero;
            brain.SpeedScale = moving ? 1f : 0f;
            if (fwd.sqrMagnitude > 0.01f) _zombie.PossessedFace = fwd.normalized;

            _ghost.position = SeatIn(_zombie.transform);

            if (!uiBusy && (Keys.Down(Act.Draw) || Keys.Down(Act.Erase)))
                _zombie.GhostAbility(_ghost.forward);   // the FULL look: casts pitch too
        }

        /// Conjured matter rides like a spell: eased velocity, F lets go.
        void DriveMatter()
        {
            var kb = Keyboard.current;
            if (kb != null && !GameMenu.IsOpen && !UIKit.Typing)
            {
                if (Keys.Down(Act.Drop))
                {
                    _rbRidden = null;
                    _third = false;
                    _noGrabUntil = Time.time + 0.8f;
                    GhostChime(_ghost.position);
                    DrawingWorld.Instance?.LogEvent("you release the spell");
                    return;
                }
                Vector3 drive = Vector3.zero;
                Vector2 mv = Keys.Move; // the move keys and the left stick
                drive += _ghost.forward * mv.y + _ghost.right * mv.x;
                if (Keys.Held(Act.Jump)) drive += Vector3.up;
                if (Keys.Held(Act.Crouch)) drive -= Vector3.up;
                if (drive.sqrMagnitude > 0.001f)
                    _rbRidden.linearVelocity = Vector3.MoveTowards(_rbRidden.linearVelocity,
                        drive.normalized * (Keys.Held(Act.Sprint) ? 14f : 7f),
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

        /// Same reins as the zombie: WASD walks it, F lets go, a click charges.
        void DriveGolem()
        {
            bool uiBusy = GameMenu.IsOpen || UIKit.Typing;
            if (!uiBusy && Keys.Down(Act.Drop))
            {
                _noGrabUntil = Time.time + 0.8f;
                GhostChime(_ghost.position);
                DrawingWorld.Instance?.LogEvent("you release the golem");
                LeaveGolem();
                return;
            }

            Vector3 fwd = _ghost.forward; fwd.y = 0f;
            Vector3 right = _ghost.right; right.y = 0f;
            Vector3 move = Vector3.zero;
            if (!uiBusy)
            {
                Vector2 mv = Keys.Move; // the move keys and the left stick
                move += fwd * mv.y + right * mv.x;
            }
            _golem.PossessedMove = move.sqrMagnitude > 0.01f ? move.normalized : Vector3.zero;
            if (fwd.sqrMagnitude > 0.01f) _golem.PossessedFace = fwd.normalized;

            _ghost.position = SeatIn(_golem.transform); // inside the body, hat out the top; the view is from its eyes

            if (!uiBusy && (Keys.Down(Act.Draw) || Keys.Down(Act.Erase)))
                _golem.GhostAbility(_ghost.forward);
        }

        void LeaveGolem()
        {
            if (_golem != null && !_golem.Equals(null)) _golem.PossessBy(false);
            _golem = null;
            _third = false;
        }

        /// The host's verdict on a ride this ghost asked for, or the release
        /// it forced when the body died or tumbled.
        public static void OnRideGive(int owner, byte kind, int id, bool on)
        {
            if (owner != Grimoire.LocalPlayerId) return;
            foreach (var g in All)
            {
                if (g == null || !g.IsGhost || g._ghostCam == null) continue;
                if (!on)
                {
                    if (g._proxyId != id) return;
                    g.LeaveProxy(false);
                    g._noGrabUntil = Time.time + 0.8f; // thrown off (a steal, a death): no instant regrab
                    return;
                }
                var t = NetSync.RideProxy(kind, id);
                if (t == null) return;
                g._proxy = t;
                g._proxyKind = kind;
                g._proxyId = id;
                g._third = true;
                g._askedSpell = 0;
                GhostChime(g._ghost.position);
                if (kind == 3)
                {
                    DrawingWorld.Instance?.LogEvent("you combine with the spell");
                    return;
                }
                g.SetProxyEyes(false);
                Achievements.Unlock(kind == 1 ? Achievements.RideZombie : Achievements.RideGolem);
                DrawingWorld.Instance?.LogEvent(kind == 1
                    ? "you take the zombie. LMB uses what it is" : "you take the golem. click to charge");
                return;
            }
        }

        /// A client's reins: read here, applied on the host. The ghost rides
        /// the stand-in the snapshots move.
        void DriveProxy()
        {
            bool uiBusy = GameMenu.IsOpen || UIKit.Typing;
            if (!uiBusy && Keys.Down(Act.Drop))
            {
                _noGrabUntil = Time.time + 0.8f;
                GhostChime(_ghost.position);
                DrawingWorld.Instance?.LogEvent(_proxyKind == 3 ? "you release the spell"
                    : _proxyKind == 1 ? "you release the zombie" : "you release the golem");
                LeaveProxy(true);
                return;
            }

            // a spell: the same reins the host's own ghost holds, steered over there
            if (_proxyKind == 3)
            {
                NetSync.SendRideDrive(_proxyId, uiBusy ? Vector3.zero : SpellReins(), _ghost.forward, false,
                    !uiBusy && Keys.Held(Act.Sprint));
                _ghost.position = SeatIn(_proxy);
                return;
            }

            Vector3 fwd = _ghost.forward; fwd.y = 0f;
            Vector3 right = _ghost.right; right.y = 0f;
            Vector3 move = Vector3.zero;
            if (!uiBusy)
            {
                Vector2 mv = Keys.Move; // the move keys and the left stick
                move += fwd * mv.y + right * mv.x;
            }
            bool ability = !uiBusy && (Keys.Down(Act.Draw) || Keys.Down(Act.Erase));
            NetSync.SendRideDrive(_proxyId, move.sqrMagnitude > 0.01f ? move.normalized : Vector3.zero,
                _ghost.forward, ability);

            _ghost.position = SeatIn(_proxy);
        }

        void LeaveProxy(bool tellHost)
        {
            if (tellHost && _proxyId != 0) NetSync.SendRideAsk(_proxyKind, _proxyId, false);
            SetProxyEyes(true);
            _proxy = null;
            _proxyId = 0;
            _proxyKind = 0;
            _third = false;
        }

        void SetProxyEyes(bool on)
        {
            if (ReferenceEquals(_proxy, null) || _proxy == null) return;
            var zp = _proxy.GetComponent<NetZombieProxy>();
            if (zp != null) zp.ShowEyes(on);
            var gp = _proxy.GetComponent<NetGolemProxy>();
            if (gp != null) gp.ShowEyes(on);
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
                        return g._eyeAt;
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

        /// A living teammate standing at a body whose ghost is home revives
        /// it. No key: being there is the act.
        void OfferRescue()
        {
            if (ActiveScene.Name == "Lobby") return;  // lobby ghosts revive themselves
            // only the LIVING lend a hand: no ghost, no corpse
            if (IsGhost || _pilot.IsDowned || _pilot.IsDead) return;
            var side = Sides.Of(Grimoire.LocalPlayerId);

            var target = BodyNear(transform.position, side, 2.5f);
            if (target != null && target != this)
            {
                target.AddGhostRevive(_pilot, Time.deltaTime);
                return;
            }

            // a friend on another machine: their puppet is downed and their
            // ghost is home. Only their machine can stand them up, so our
            // presence travels to them in slices
            var av = NetAvatar.RevivableNear(transform.position, side == Side.Acolyte, 2.5f);
            if (av == null) { _remoteHold = 0f; return; }
            _remoteHold += Time.deltaTime;
            if (Time.time >= _remoteShine && FxLibrary.I != null)
            {
                _remoteShine = Time.time + 0.9f;
                // the target and the others shine it from the tick
                FxLibrary.Spawn(FxLibrary.I.HealShine, av.transform.position + Vector3.up * 0.35f,
                    av.transform, 1.1f, false);
            }
            if (_remoteHold >= 0.15f)
            {
                NetSync.SendReviveTick(NetSync.OwnerIdOf(av.Id), _remoteHold);
                _remoteHold = 0f;
            }
        }

        /// A friend's presence arriving over the wire. Only this machine can
        /// stand its own body up, and only with the ghost home.
        public static void ApplyRemoteRevive(float dt, int rescuer)
        {
            if (ActiveScene.Name == "Lobby") return;
            foreach (var g in All)
            {
                if (g == null || !g.IsGhost || g._ghostCam == null) continue;
                if (!g.AtHome) return;
                g._revive += dt / Mathf.Max(0.5f, DrawingConfig.ReviveSeconds);
                g._lastRescuer = rescuer;
                g.Shine(false); // the others shine its puppet from the tick
                return;
            }
        }

        void Rise()
        {
            IsGhost = true;
            OwnerId = _pilot.IsLocalViewer ? Grimoire.LocalPlayerId : OwnerId;
            _homeTime = 0f;
            _revive = 0f;
            _lastRescuer = -1;

            bool local = _pilot.IsLocalViewer;
            _ghost = BuildSpirit(local);
            if (_ghost == null) { IsGhost = false; return; }
            Juice.Sound(Sfx.GhostOut, _ghost.position); // the puppets sound theirs where their spirit appears
            if (local)
            {
                LocalIsGhost = true;
                // the player's camera flies the ghost. It is NOT reparented:
                // SimpleFPSController and CharacterRig both rewrite its local
                // position every frame, so it is driven in world space from
                // LateUpdate instead.
                _bodyCam = GetComponentInChildren<Camera>(true);
                _ghostCam = _bodyCam;
                _eyeAt = _ghostCam.transform.position;
                var eye = _pilot.CameraPivot != null ? _pilot.CameraPivot : transform;
                _yaw = eye.eulerAngles.y;
                _pitch = 10f;

                // re-lock: the disabled controller cannot, and precise-draw death leaves it free
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                // stops the downed body reading the same WASD the ghost flies with
                _pilot.enabled = false;
                XRayGlow.Hide(gameObject); // a ghost gets no see-through window on its body (third person may have left one on)
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
                    ? (HatColor.Worn() ?? SideColor()) : SideColor());
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
                Vector2 d = mouse.delta.ReadValue() * 0.08f + Keys.LookStick * (140f * Keys.StickSensitivity * Time.unscaledDeltaTime);
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
            }
            _ghost.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            if (_ridden != null || !ReferenceEquals(_rbRidden, null)
                || _zombie != null || !ReferenceEquals(_proxy, null)) return; // driving something else

            Vector3 move = Vector3.zero;
            Vector2 mv = Keys.Move; // the move keys and the left stick
            bool w = mv.y > 0.1f;
            if (w) move += _ghost.forward * mv.y;
            if (!ForwardOnly || w)
            {
                float trim = ForwardOnly ? 0.7f : 1f;
                if (!ForwardOnly && mv.y < 0f) move += _ghost.forward * mv.y;
                move += _ghost.right * (mv.x * trim);
                if (Keys.Held(Act.Jump)) move += Vector3.up * trim;
                if (Keys.Held(Act.Crouch)) move -= Vector3.up * trim;
            }
            if (move.sqrMagnitude > 0.001f)
                _ghost.position += move.normalized * (GhostSpeed * Time.deltaTime);
        }

        /// Back on your feet, heard where the body stands (the puppets play theirs off the downed flag).
        void ReviveHeard()
        {
            _pilot.Revive();
            Juice.Sound(Sfx.Revival, _pilot.transform.position);
        }

        static float _forceReviveAt = -1f;
        /// The death needle: dead now, back on your feet by yourself after `seconds`.
        public static void ReviveIn(float seconds) => _forceReviveAt = Mathf.Max(_forceReviveAt, Time.time + seconds); // a second needle only lengthens it

        void TickRevive()
        {
            if (_forceReviveAt > 0f && Time.time >= _forceReviveAt && _pilot != null && _pilot.IsLocalViewer)
            {
                _forceReviveAt = -1f;
                ReviveHeard();
                Land();
                return;
            }
            bool home = AtHome;

            // lobby: hover over your own body, no teammate needed
            if (ActiveScene.Name == "Lobby")
            {
                if (home)
                {
                    _homeTime += Time.deltaTime;
                    Shine();
                    if (_homeTime >= LobbySeconds) { ReviveHeard(); Land(); }
                }
                else _homeTime = 0f;
                return;
            }

            // match: needs the ghost home AND a living teammate at the body
            if (!home) { _revive = 0f; return; }
            if (_revive >= 1f)
            {
                // a rescuer on another machine earns it too
                if (_lastRescuer >= 0 && _lastRescuer != Grimoire.LocalPlayerId) NetSync.SendReviveDone(_lastRescuer);
                _lastRescuer = -1;
                ReviveHeard();
                Land();
            }
        }

        /// The rescuer must be ALIVE - a corpse or another ghost cannot
        /// revive anyone, checked here so every caller inherits the rule.
        public void AddGhostRevive(SimpleFPSController by, float dt)
        {
            if (by == null || by.IsDowned || by.IsDead) return;
            if (!IsGhost || !AtHome) return;
            bool below = _revive < 1f;
            _revive += dt / Mathf.Max(0.5f, DrawingConfig.ReviveSeconds);
            if (below && _revive >= 1f && by.IsLocalViewer) Achievements.Unlock(Achievements.ReviveFriend);
            Shine();
        }

        float _shineUntil;

        /// The revive has no UI. This VFX off the body is the only signal.
        /// relay: false where the others already shine it from the ReviveTickMsg.
        void Shine(bool relay = true)
        {
            if (Time.time < _shineUntil || FxLibrary.I == null) return;
            _shineUntil = Time.time + 0.9f;
            FxLibrary.Spawn(FxLibrary.I.HealShine, BodyAt + Vector3.up * 0.35f,
                transform, 1.1f, relay);
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
            LeaveGolem();
            LeaveProxy(true);
            _visual = null;
            _third = false;
            // back in the body: third person gets its window back, first person never had one
            if (_ghostCam != null)
            {
                if (SimpleFPSController.ThirdPersonActive) XRayGlow.Show(gameObject);
                else XRayGlow.Hide(gameObject);
            }
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
        // The rider looks out from between the golem's eyes. They sit beside the camera, and on a
        // big golem they are as big as the screen: for the rider alone they are not drawn.
        readonly System.Collections.Generic.List<Renderer> _eyesOff = new System.Collections.Generic.List<Renderer>();
        Transform _eyesOffFor;

        void HideEyes(Transform eyes)
        {
            if (eyes == _eyesOffFor) return;
            foreach (var r in _eyesOff) if (r != null) r.enabled = true;
            _eyesOff.Clear();
            _eyesOffFor = eyes;
            if (eyes == null) return;
            foreach (var r in eyes.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { r.enabled = false; _eyesOff.Add(r); }
        }

        void LateUpdate()
        {
            if (!IsGhost || _ghostCam == null || _ghost == null) { HideEyes(null); return; }
            Quaternion look = Quaternion.Euler(_pitch, _yaw, 0f);

            // the offset glides so the view never snaps between the views;
            // driving a zombie puts the camera inside its head
            bool inZombie = (!ReferenceEquals(_zombie, null) && _zombie != null)
                         || (!ReferenceEquals(_golem, null) && _golem != null)
                         || (_proxyKind != 3 && !ReferenceEquals(_proxy, null) && _proxy != null); // a ridden spell is seen from behind
            Vector3 want = inZombie
                ? new Vector3(0f, 0.02f, 0.18f)
                : _third
                    ? new Vector3(0f, 0.6f, -2.8f)
                    : new Vector3(0f, 0.05f, 0.45f);
            _camOffset = Vector3.MoveTowards(_camOffset, want, 11f * Time.deltaTime);

            // riding a golem the ghost sits in the body but SEES from the eyes
            bool inGolem = !ReferenceEquals(_golem, null) && _golem != null;
            Vector3 viewFrom = inGolem ? _golem.HeadAt : _ghost.position;
            Transform eyes = inGolem && _golem.Eyes != null ? _golem.Eyes.transform : null;
            if (_proxyKind == 2 && !ReferenceEquals(_proxy, null) && _proxy != null)
            {
                var gp = _proxy.GetComponent<NetGolemProxy>();
                if (gp != null)
                {
                    viewFrom = gp.HeadAt;
                    var gpEyes = gp.GetComponentInChildren<GooglyEyes>(true);
                    if (gpEyes != null) eyes = gpEyes.transform;
                }
            }
            HideEyes(eyes);
            _ghostCam.transform.SetPositionAndRotation(viewFrom + look * _camOffset, look);
            _eyeAt = _ghostCam.transform.position;
        }

        // where the ghost camera was put last: until this runs each frame, the body scripts have it parked at the body
        Vector3 _eyeAt;

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
                if (Sides.Of(g.OwnerId) != side && !MapRules.Together) continue; // a together map: any player
                float d = (g.BodyAt - at).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = g; }
            }
            return best;
        }
    }
}
