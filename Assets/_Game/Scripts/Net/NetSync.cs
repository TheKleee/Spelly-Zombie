using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

namespace SpellyZombie
{
    /// Broadcast-based sync: live avatars + replicated world strokes (each client detects/casts), plus host-authoritative zombies/rounds.
    public class NetSync : MonoBehaviour
    {
        // ------------------------------------------------------------ types --
        public struct PlayerState : IBroadcast
        {
            public int Id;
            public Vector3 Pos;
            public float Yaw;
            public float Pitch; // look pitch (+down) - remote heads follow it
            public byte Flags; // 1 = downed, 2 = sprawled, 4 = GHOST is flying
            public byte Team;  // MatchLobby team color index
            // ghost position; the corpse stays at Pos so friends can revive it
            public Vector3 GhostPos;
            public float GhostYaw;
            public bool Acolyte; // ghost tint: green acolyte / wizard ink
        }

        public struct ReadyMsg : IBroadcast // client  host: lobby ready toggle
        {
            public bool Ready;
        }

        public struct LobbyMsg : IBroadcast // host  clients: lobby state
        {
            public byte Ready;
            public byte Total;
            public float Countdown;
            public string Map; // the host's pick - everyone sees it
            public int Seed;
            public byte AcolytePct;
            public byte DurationMin;
        }

        public struct ReadyCallMsg : IBroadcast { } // host asks everyone: B yes, C no

        public struct SideAssignMsg : IBroadcast // host  all: who plays acolyte this match
        {
            public int[] AcolyteOwners;
        }

        public struct StandMsg : IBroadcast // the host's book: open while their menu is
        {
            public bool Open;
        }

        public struct StrokeMsg : IBroadcast
        {
            public int Owner;
            public string SurfacePath;
            public Vector3 Normal;
            public Vector3[] Points;
            public int DeclaredRune;
            public int StrokeId;       // (Owner, StrokeId) names this ink everywhere (netcode §0)
            public int ReadRune;       // the OWNER's pen-up verdict for its touching cluster -
            public float ReadScore;    // the host primes its cache, never re-reads (netcode §1)
            public int[] ClusterOwners;
            public int[] ClusterIds;
            // Non-empty = body ink: Points and Normal are in that bone's local
            // space, and the receiver mounts them on the same-named bone of the
            // owner's avatar. Empty = world ink.
            public string BoneName;
        }

        /// Body ink drunk/burned by its owner (I key) - the copies on every
        /// other machine must die too. (Owner, id) pairs, same naming as
        /// StrokeMsg (netcode §0).
        public struct InkBurnMsg : IBroadcast
        {
            public int Owner;
            public int[] Ids;
        }

        // ---- the BOOK STAND lobby ----
        /// A joiner announces the password it typed at the stand; the host
        /// kicks mismatches. No password set = open lobby, nothing checked.
        public struct JoinAuthMsg : IBroadcast
        {
            public string Password;
        }

        /// A player likes the map they want (client  host). One like per
        /// player - liking again just moves it.
        public struct MapLikeMsg : IBroadcast
        {
            public string Map;
        }

        /// Host  all: the current like tally, shown at the book stand.
        public struct MapLikesMsg : IBroadcast
        {
            public string[] Maps;
            public int[] Counts;
        }

        /// Host  all, 2 Hz: the pot's truth (one ink pool, host law).
        public struct PotMsg : IBroadcast
        {
            public float Fill01;
            public bool Corrupt;
            public float Prep;
        }

        /// Client → host: how much the wand drank from the pot.
        public struct PotDrinkMsg : IBroadcast
        {
            public float Amount;
        }

        public struct PlayerLeft : IBroadcast
        {
            public int Id;
        }

        public struct OutfitMsg : IBroadcast // any  host  all: outfit choices
        {
            public int Id;
            public string Code; // SocketManager wire format ("2,0,1"), slot order = catalog order
        }

        // ---- host-authoritative zombies & rounds ----
        public struct ZombieSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            public Vector3[] Pos;
            public float[] Yaw;
            public byte[] Kinds;
        }

        public struct ZombieHit : IBroadcast // client  host: my magic hurt your zombie
        {
            public int Id;
            public float Amount;
        }

        public struct KillFeed : IBroadcast // host  clients: shared ink for the kill
        {
            public Vector3 Pos;
        }

        public struct RoundState : IBroadcast // host  clients, 2 Hz
        {
            public byte Phase; // RoundDirector.Phase ordinal
            public int Round;
            public int Left;
            public float Timer;
            public int Kills;
        }

        // ---- host-authoritative seals/matter/particles/lifting (netcode §1-§4) ----
        public struct UnlockMsg : IBroadcast // any  host  all: grimoire truth
        {
            public int Owner;
            public int Card; // -1 = none
            public int Rune; // -1 = none
        }

        public struct DeclareRuneMsg : IBroadcast // any → host → others: declare a rune for strokes
        {
            public int Owner;
            public int[] Owners;
            public int[] Ids;
            public int Rune;
        }

        public struct DeclareSealIntent : IBroadcast // client  host: book seal-page F
        {
            public int[] Owners;
            public int[] Ids;
        }

        public struct BodySealFire : IBroadcast // client  host: a body seal fired (its ink never replicates)
        {
            public Vector3 Origin;
            public Vector3 Normal;
            public int Edges;
            public float Duration;
            public int[] Runes;
            public float[] Strengths;
            public Vector3[] Centers;
            public Vector3[] PushDirs;
            public float[] Sizes;
        }

        public struct SealMsg : IBroadcast // host  clients: gold ring, display only
        {
            public int SealId;
            public Vector3[] Loop;
            public float Duration;
        }

        public struct SealEndMsg : IBroadcast // host  clients: ring down; resolved burns the ink
        {
            public int SealId;
            public bool Resolved;
            public int[] BurnOwners;
            public int[] BurnIds;
        }

        public struct EraseMsg : IBroadcast // any  host  others: ink graphs must not drift
        {
            public int Owner;
            public Vector3 From;
            public Vector3 To;
            public float Radius;
        }

        public struct MatterSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            public Vector3[] Pos;
            public Quaternion[] Rot;
            public Vector3[] Scale;
            public byte[] Mats;
            public byte[] Phases;
            public byte[] Looks;
        }

        public struct ParticleSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            public byte[] Kinds;
            public Vector3[] Pos;
            public float[] Scale;
        }

        public struct PropReg : IBroadcast // host  clients: a scene prop went dynamic
        {
            public int Id;
            public string Path;
        }

        public struct PropSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            public Vector3[] Pos;
            public Quaternion[] Rot;
        }

        public struct GrabIntent : IBroadcast // client  host: E on a thing
        {
            public int MatterId; // host instance id of a matter blob, 0 = use Path
            public string Path;
            public float HoldDist;
        }

        public struct LiftAim : IBroadcast // client  host, 10 Hz unreliable: where the hand is
        {
            public Vector3 Hand;
            public Quaternion Rot;
        }

        public struct ThrowIntent : IBroadcast // client  host: E while holding
        {
            public Vector3 Dir;
        }

        public struct DropIntent : IBroadcast { public byte Pad; } // client  host: F while holding

        public struct ClaimIntent : IBroadcast // client  host: grab a spell particle
        {
            public int ParticleId;
        }

        public struct AbsorbMsg : IBroadcast // a world source was absorbed (scene path)
        {
            public string Path;
        }

        public struct IdentityMsg : IBroadcast // who a player id IS (persona + steam id)
        {
            public int Id;
            public string Name;
            public ulong SteamId;
        }

        public struct GrabAck : IBroadcast // host  the asking client: verdict on a grab
        {
            public bool Ok;
            public string Note;
        }

        // ------------------------------------------------------------ state --
        public static int RemoteCount => _instance != null ? _instance._avatars.Count : 0;

        /// True while a received stroke is being rebuilt - suppresses re-send.
        public static bool ApplyingRemote { get; private set; }

        /// Latest round state received from the host (client HUD reads these).
        public static bool HasRound;
        public static byte NetPhase;
        public static int NetRound, NetLeft, NetKills;
        public static float NetTimer;

        /// Client wizards' music: any zombie proxy near this point
        /// (hosts ask Zombie.All directly).
        public static bool AnyZombieNear(Vector3 at, float range)
        {
            if (_instance == null) return false;
            float sq = range * range;
            foreach (var kv in _instance._proxies)
            {
                var p = kv.Value;
                if (p != null && (p.transform.position - at).sqrMagnitude < sq) return true;
            }
            return false;
        }

        /// Host referee: any REMOTE player of this side still standing.
        public static bool AnySideAlive(Side side)
        {
            if (_instance == null) return false;
            foreach (var kv in _instance._avatars)
            {
                var a = kv.Value;
                if (a == null || a.Downed) continue;
                if (Sides.Of(OwnerIdOf(kv.Key)) == side) return true;
            }
            return false;
        }

        /// Host referee: any remote player of this side in the match at all.
        public static bool AnySidePresent(Side side)
        {
            if (_instance == null) return false;
            foreach (var kv in _instance._avatars)
                if (kv.Value != null && Sides.Of(OwnerIdOf(kv.Key)) == side) return true;
            return false;
        }

        /// True when every remote player reports downed (host wipe check).
        public static bool AllRemotesDown
        {
            get
            {
                if (_instance == null) return true;
                foreach (var kv in _instance._avatars)
                    if (kv.Value != null && !kv.Value.Downed) return false;
                return true;
            }
        }

        static NetSync _instance;

        readonly Dictionary<int, NetAvatar> _avatars = new Dictionary<int, NetAvatar>();
        readonly Dictionary<int, NetZombieProxy> _proxies = new Dictionary<int, NetZombieProxy>();
        readonly HashSet<int> _seen = new HashSet<int>();
        static readonly List<int> _gone = new List<int>();
        static readonly System.Collections.Generic.Dictionary<int, string> _outfits
            = new System.Collections.Generic.Dictionary<int, string>();
        static readonly Dictionary<int, IdentityMsg> _identities = new Dictionary<int, IdentityMsg>();
        float _sendTimer, _zombieTimer;
        bool _registered;
        bool _outfitSent;
        bool _identityAdopted; // stable ClientId adopted this connection (netcode §0)

        // ---- cross-machine stroke ledger: (owner, id)  the local copy (netcode §0) ----
        static readonly Dictionary<long, Stroke> _netStrokes = new Dictionary<long, Stroke>();
        static readonly List<long> _strokePrune = new List<long>();
        static readonly List<Stroke> _clusterBuf = new List<Stroke>();
        static int _nextStrokeId = 1;

        static long StrokeKey(int owner, int id) => ((long)(uint)owner << 32) | (uint)id;

        static void RegisterNetStroke(Stroke s)
        {
            if (s == null || s.NetId == 0) return;
            if (_netStrokes.Count > 1024)
            {
                _strokePrune.Clear();
                foreach (var kv in _netStrokes)
                    if (kv.Value == null || !kv.Value.Alive) _strokePrune.Add(kv.Key);
                foreach (var k in _strokePrune) _netStrokes.Remove(k);
            }
            _netStrokes[StrokeKey(s.OwnerId, s.NetId)] = s;
        }

        static Stroke FindNetStroke(int owner, int id)
            => _netStrokes.TryGetValue(StrokeKey(owner, id), out var s) ? s : null;

        // client-side proxies for host truth (netcode §2/§3)
        readonly Dictionary<int, NetMatterProxy> _matterProxies = new Dictionary<int, NetMatterProxy>();
        readonly Dictionary<int, NetMoteProxy> _moteProxies = new Dictionary<int, NetMoteProxy>();
        readonly Dictionary<int, NetSealRing> _rings = new Dictionary<int, NetSealRing>();
        readonly Dictionary<int, Transform> _propGhosts = new Dictionary<int, Transform>();

        // host-side: remote friends' holds + net-lifted props (netcode §4)
        class RemoteHold
        {
            public int Owner;
            public Rigidbody Body;
            public InkMark[] Marks;
            public SpellParticle Mote;
            public Vector3 Hand;
            public Quaternion Rot;
            public bool HasAim;
            public bool HadGravity;
        }
        readonly Dictionary<int, RemoteHold> _holds = new Dictionary<int, RemoteHold>();
        static readonly List<int> _holdGone = new List<int>();
        readonly Dictionary<int, float> _lastBodyFire = new Dictionary<int, float>();
        static readonly List<Rigidbody> _trackedProps = new List<Rigidbody>();
        static readonly List<int> _trackedPropIds = new List<int>();
        static int _nextPropId = 1;
        static readonly List<int> _burnOwnersBuf = new List<int>();
        static readonly List<int> _burnIdsBuf = new List<int>();

        /// The outfit code a remote player announced ("" = defaults).
        public static string OutfitOf(int id)
            => _outfits.TryGetValue(id, out var code) ? code : "";

        /// Announce the local player's outfit to everyone (once on connect;
        /// the lobby outfit picker calls this again after every change).
        public static void PushLocalOutfit()
        {
            if (!NetGame.Connected || _instance == null) return;
            InstanceFinder.ClientManager.Broadcast(new OutfitMsg
            {
                Id = _instance.LocalId,
                Code = SocketManager.LocalOutfitCode()
            });
        }

        void Awake() => _instance = this;

        void Update()
        {
            if (InstanceFinder.NetworkManager == null) return;
            RegisterOnce();

            if (!NetGame.Connected)
            {
                _outfitSent = false; // re-announce the look on the next session
                _identityAdopted = false;
                return;
            }

            // connected identity = FishNet ClientId, so strokes and grimoire
            // keys agree across machines (netcode §0)
            if (!_identityAdopted && LocalId >= 0)
            {
                _identityAdopted = true;
                int stable = OwnerIdOf(LocalId);
                int old = Grimoire.LocalPlayerId;
                if (old != 0 && old != stable) Grimoire.Rekey(old, stable);
                Grimoire.LocalPlayerId = stable;
                AnnounceUnlocks(); // the host answers IsUnlocked truthfully (netcode §1)
            }

            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = 0.1f; // 10 Hz presence
                SendLocalState();
            }

            // the outfit is static - announce it ONCE per connection (the
            // lobby outfit picker re-announces via PushLocalOutfit later)
            if (!_outfitSent)
            {
                _outfitSent = true;
                PushLocalOutfit();
                AnnounceIdentity();
            }

            // the host streams its zombies to everyone (10 Hz, unreliable -
            // an empty snapshot is meaningful too: it clears dead proxies)
            if (NetGame.IsHost)
            {
                _zombieTimer -= Time.unscaledDeltaTime;
                if (_zombieTimer <= 0f)
                {
                    _zombieTimer = 0.1f;
                    SendZombieSnap();
                    SendMatterSnap();   // host-simulated matter (netcode §3)
                    SendParticleSnap(); // host-simulated particles (netcode §3)
                    SendPropSnap();     // lifted/torn scene props (netcode §4)
                }
            }
        }

        void AnnounceUnlocks()
        {
            foreach (var c in Grimoire.CardsOf(Grimoire.LocalPlayerId))
                PushUnlock(Grimoire.LocalPlayerId, (int)c, -1);
            foreach (var r in Grimoire.RunesOf(Grimoire.LocalPlayerId))
                PushUnlock(Grimoire.LocalPlayerId, -1, (int)r);
        }

        // reusable snapshot buffers - resized only when the horde count changes, not 10 Hz garbage
        int[] _snapIds = System.Array.Empty<int>();
        Vector3[] _snapPos = System.Array.Empty<Vector3>();
        float[] _snapYaw = System.Array.Empty<float>();
        byte[] _snapKinds = System.Array.Empty<byte>();

        void SendZombieSnap()
        {
            int n = Zombie.All.Count;
            if (_snapIds.Length != n) // receivers read Ids.Length - size must match exactly
            {
                _snapIds = new int[n];
                _snapPos = new Vector3[n];
                _snapYaw = new float[n];
                _snapKinds = new byte[n];
            }
            for (int i = 0; i < n; i++)
            {
                var z = Zombie.All[i];
                if (z == null) // zero the slot - reused buffers would otherwise leak a stale zombie
                {
                    _snapIds[i] = 0; _snapPos[i] = default; _snapYaw[i] = 0f; _snapKinds[i] = 0;
                    continue;
                }
                _snapIds[i] = z.gameObject.GetInstanceID();
                _snapPos[i] = z.transform.position;
                _snapYaw[i] = z.transform.eulerAngles.y;
                _snapKinds[i] = (byte)z.Kind;
            }
            var snap = new ZombieSnap { Ids = _snapIds, Pos = _snapPos, Yaw = _snapYaw, Kinds = _snapKinds };
            InstanceFinder.ServerManager.Broadcast(snap, true, Channel.Unreliable);
        }

        // reusable matter/particle/prop snapshot buffers - same law as the zombie ones
        int[] _mIds = System.Array.Empty<int>();
        Vector3[] _mPos = System.Array.Empty<Vector3>();
        Quaternion[] _mRot = System.Array.Empty<Quaternion>();
        Vector3[] _mScale = System.Array.Empty<Vector3>();
        byte[] _mMats = System.Array.Empty<byte>();
        byte[] _mPhases = System.Array.Empty<byte>();
        byte[] _mLooks = System.Array.Empty<byte>();

        void SendMatterSnap()
        {
            var all = Matter.Living;
            int n = all.Count;
            if (_mIds.Length != n)
            {
                _mIds = new int[n];
                _mPos = new Vector3[n];
                _mRot = new Quaternion[n];
                _mScale = new Vector3[n];
                _mMats = new byte[n];
                _mPhases = new byte[n];
                _mLooks = new byte[n];
            }
            for (int i = 0; i < n; i++)
            {
                var m = all[i];
                if (m == null)
                {
                    _mIds[i] = 0;
                    continue;
                }
                _mIds[i] = m.gameObject.GetInstanceID();
                _mPos[i] = m.transform.position;
                _mRot[i] = m.transform.rotation;
                _mScale[i] = m.transform.localScale;
                _mMats[i] = (byte)m.Material;
                _mPhases[i] = (byte)m.Phase;
                _mLooks[i] = m.NetLook;
            }
            InstanceFinder.ServerManager.Broadcast(new MatterSnap
            {
                Ids = _mIds, Pos = _mPos, Rot = _mRot, Scale = _mScale,
                Mats = _mMats, Phases = _mPhases, Looks = _mLooks
            }, true, Channel.Unreliable);
        }

        int[] _pIds = System.Array.Empty<int>();
        byte[] _pKinds = System.Array.Empty<byte>();
        Vector3[] _pPos = System.Array.Empty<Vector3>();
        float[] _pScale = System.Array.Empty<float>();

        void SendParticleSnap()
        {
            var all = SpellParticle.Living;
            int n = all.Count;
            if (_pIds.Length != n)
            {
                _pIds = new int[n];
                _pKinds = new byte[n];
                _pPos = new Vector3[n];
                _pScale = new float[n];
            }
            for (int i = 0; i < n; i++)
            {
                var p = all[i];
                if (p == null)
                {
                    _pIds[i] = 0;
                    continue;
                }
                _pIds[i] = p.gameObject.GetInstanceID();
                _pKinds[i] = (byte)p.Kind;
                _pPos[i] = p.transform.position;
                _pScale[i] = p.transform.localScale.x;
            }
            InstanceFinder.ServerManager.Broadcast(new ParticleSnap
                { Ids = _pIds, Kinds = _pKinds, Pos = _pPos, Scale = _pScale }, true, Channel.Unreliable);
        }

        int[] _prIds = System.Array.Empty<int>();
        Vector3[] _prPos = System.Array.Empty<Vector3>();
        Quaternion[] _prRot = System.Array.Empty<Quaternion>();

        void SendPropSnap()
        {
            // drop dead/settled props - a sleeping prop stays where clients last saw it
            for (int i = _trackedProps.Count - 1; i >= 0; i--)
                if (_trackedProps[i] == null || _trackedProps[i].IsSleeping())
                {
                    _trackedProps.RemoveAt(i);
                    _trackedPropIds.RemoveAt(i);
                }
            int n = _trackedProps.Count;
            if (_prIds.Length != n)
            {
                _prIds = new int[n];
                _prPos = new Vector3[n];
                _prRot = new Quaternion[n];
            }
            for (int i = 0; i < n; i++)
            {
                _prIds[i] = _trackedPropIds[i];
                _prPos[i] = _trackedProps[i].transform.position;
                _prRot[i] = _trackedProps[i].transform.rotation;
            }
            InstanceFinder.ServerManager.Broadcast(new PropSnap
                { Ids = _prIds, Pos = _prPos, Rot = _prRot }, true, Channel.Unreliable);
        }

        /// A scene prop went dynamic under a wizard's ink - clients must see it move (netcode §4).
        public static void TrackProp(Rigidbody rb)
        {
            if (rb == null || !NetGame.IsHost || !NetGame.Connected) return;
            if (rb.GetComponent<Matter>() != null) return; // matter rides MatterSnap
            if (_trackedProps.Contains(rb)) return;
            int id = _nextPropId++;
            _trackedProps.Add(rb);
            _trackedPropIds.Add(id);
            InstanceFinder.ServerManager.Broadcast(new PropReg { Id = id, Path = FullPath(rb.transform) });
        }

        // ---- host physics drive for remote holds (netcode §4) ----
        void FixedUpdate()
        {
            if (!NetGame.IsHost || _holds.Count == 0) return;
            _holdGone.Clear();
            foreach (var kv in _holds)
            {
                var h = kv.Value;
                if (h.Mote != null)
                {
                    if (h.Mote.Dead || !h.Mote.Claimed)
                    {
                        _holdGone.Add(kv.Key);
                        NotifyHoldLost(kv.Key, "what you held is gone, merged or spent");
                        continue;
                    }
                    if (h.HasAim)
                        h.Mote.transform.position = Vector3.Lerp(
                            h.Mote.transform.position, h.Hand, 14f * Time.fixedDeltaTime);
                    continue;
                }
                if (h.Body == null)
                {
                    _holdGone.Add(kv.Key);
                    NotifyHoldLost(kv.Key, "what you held is gone, merged or spent");
                    continue;
                }
                if (!h.HasAim) continue;

                // the LevitateTick law with the REMOTE owner's ink (HandGrab mirrors this)
                float auth = HandGrab.AuthorityFor(h.Body, h.Marks, h.Owner, out float share);
                if (auth <= 0f)
                {
                    ReleaseHeldBody(h, Vector3.zero);
                    _holdGone.Add(kv.Key);
                    NotifyHoldLost(kv.Key, "your ink is gone, it drops");
                    continue;
                }
                Vector3 delta = h.Hand - h.Body.position;
                float accel = Mathf.Lerp(4f, 90f, auth) * share;
                Vector3 target = Vector3.ClampMagnitude(delta * 8f, Mathf.Lerp(2.5f, 14f, auth));
                h.Body.linearVelocity = Vector3.MoveTowards(
                    h.Body.linearVelocity, target, accel * Time.fixedDeltaTime);
                h.Body.useGravity = false;
                h.Body.AddForce(Physics.gravity * (1f - auth), ForceMode.Acceleration);
                if (auth < 1f) continue; // can't lift it = can't turn it
                float turn = Mathf.Lerp(2f, 12f, auth) * Mathf.Clamp01(10f / Mathf.Max(1f, h.Body.mass));
                h.Body.MoveRotation(Quaternion.Slerp(h.Body.rotation, h.Rot, turn * Time.fixedDeltaTime));
            }
            foreach (var k in _holdGone) _holds.Remove(k);
        }

        static void ReleaseHeldBody(RemoteHold h, Vector3 impulse)
        {
            if (h.Body == null) return;
            if (!h.Body.isKinematic)
            {
                h.Body.linearVelocity = Vector3.ClampMagnitude(h.Body.linearVelocity, 4f);
                h.Body.angularVelocity = Vector3.ClampMagnitude(h.Body.angularVelocity, 4f);
                if (impulse != Vector3.zero) h.Body.AddForce(impulse, ForceMode.VelocityChange);
            }
            h.Body.useGravity = h.HadGravity;
            var m = h.Body.GetComponent<Matter>();
            if (m != null) m.Touched = true; // TOUCH = WORLD
        }

        /// The host force-released a client's hold - tell their hand to open (netcode §4).
        void NotifyHoldLost(int clientId, string why)
        {
            if (InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out var conn))
                Ack(conn, false, why);
        }

        void ReleaseHold(int clientId, Vector3 impulse)
        {
            if (!_holds.TryGetValue(clientId, out var h)) return;
            _holds.Remove(clientId);
            if (h.Mote != null)
            {
                if (!h.Mote.Dead && h.Mote.Claimed) h.Mote.ReleaseHeld(impulse);
                return;
            }
            ReleaseHeldBody(h, impulse);
        }

        void RegisterOnce()
        {
            if (_registered) return;
            _registered = true;

            InstanceFinder.ServerManager.RegisterBroadcast<PlayerState>(OnPlayerStateServer);
            InstanceFinder.ClientManager.RegisterBroadcast<PlayerState>(OnPlayerStateClient);
            InstanceFinder.ServerManager.RegisterBroadcast<StrokeMsg>(OnStrokeServer);
            InstanceFinder.ClientManager.RegisterBroadcast<StrokeMsg>(OnStrokeClient);
            InstanceFinder.ClientManager.RegisterBroadcast<PlayerLeft>(OnPlayerLeftClient);
            InstanceFinder.ServerManager.RegisterBroadcast<ZombieHit>(OnZombieHitServer);
            InstanceFinder.ClientManager.RegisterBroadcast<ZombieSnap>(OnZombieSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<KillFeed>(OnKillFeedClient);
            InstanceFinder.ClientManager.RegisterBroadcast<RoundState>(OnRoundStateClient);
            InstanceFinder.ServerManager.RegisterBroadcast<ReadyMsg>(OnReadyServer);
            InstanceFinder.ClientManager.RegisterBroadcast<LobbyMsg>(OnLobbyClient);
            InstanceFinder.ServerManager.RegisterBroadcast<OutfitMsg>(OnOutfitServer);
            InstanceFinder.ClientManager.RegisterBroadcast<OutfitMsg>(OnOutfitClient);
            InstanceFinder.ServerManager.RegisterBroadcast<AbsorbMsg>(OnAbsorbServer);
            InstanceFinder.ClientManager.RegisterBroadcast<AbsorbMsg>(OnAbsorbClient);
            InstanceFinder.ServerManager.RegisterBroadcast<IdentityMsg>(OnIdentityServer);
            InstanceFinder.ClientManager.RegisterBroadcast<IdentityMsg>(OnIdentityClient);
            InstanceFinder.ClientManager.RegisterBroadcast<ReadyCallMsg>(OnReadyCallClient);
            InstanceFinder.ClientManager.RegisterBroadcast<SideAssignMsg>(OnSideAssignClient);
            InstanceFinder.ClientManager.RegisterBroadcast<StandMsg>(OnStandClient);

            // host-authoritative channels (netcode §1-§4)
            InstanceFinder.ServerManager.RegisterBroadcast<UnlockMsg>(OnUnlockServer);
            InstanceFinder.ClientManager.RegisterBroadcast<UnlockMsg>(OnUnlockClient);
            InstanceFinder.ServerManager.RegisterBroadcast<DeclareRuneMsg>(OnDeclareRuneServer);
            InstanceFinder.ClientManager.RegisterBroadcast<DeclareRuneMsg>(OnDeclareRuneClient);
            InstanceFinder.ServerManager.RegisterBroadcast<DeclareSealIntent>(OnDeclareSealServer);
            InstanceFinder.ServerManager.RegisterBroadcast<BodySealFire>(OnBodySealServer);
            InstanceFinder.ClientManager.RegisterBroadcast<SealMsg>(OnSealClient);
            InstanceFinder.ClientManager.RegisterBroadcast<SealEndMsg>(OnSealEndClient);
            InstanceFinder.ServerManager.RegisterBroadcast<EraseMsg>(OnEraseServer);
            InstanceFinder.ClientManager.RegisterBroadcast<EraseMsg>(OnEraseClient);
            InstanceFinder.ServerManager.RegisterBroadcast<InkBurnMsg>(OnInkBurnServer);
            InstanceFinder.ClientManager.RegisterBroadcast<InkBurnMsg>(OnInkBurnClient);

            // the book stand lobby: password gate + map likes
            InstanceFinder.ServerManager.RegisterBroadcast<JoinAuthMsg>(OnJoinAuthServer);
            InstanceFinder.ServerManager.RegisterBroadcast<MapLikeMsg>(OnMapLikeServer);
            InstanceFinder.ClientManager.RegisterBroadcast<MapLikesMsg>(OnMapLikesClient);
            InstanceFinder.ClientManager.OnClientConnectionState += OnLocalClientState;

            // the pot
            InstanceFinder.ClientManager.RegisterBroadcast<PotMsg>(OnPotClient);
            InstanceFinder.ServerManager.RegisterBroadcast<PotDrinkMsg>(OnPotDrinkServer);
            InstanceFinder.ClientManager.RegisterBroadcast<MatterSnap>(OnMatterSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<ParticleSnap>(OnParticleSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<PropReg>(OnPropRegClient);
            InstanceFinder.ClientManager.RegisterBroadcast<PropSnap>(OnPropSnapClient);
            InstanceFinder.ServerManager.RegisterBroadcast<GrabIntent>(OnGrabIntentServer);
            InstanceFinder.ServerManager.RegisterBroadcast<LiftAim>(OnLiftAimServer);
            InstanceFinder.ServerManager.RegisterBroadcast<ThrowIntent>(OnThrowIntentServer);
            InstanceFinder.ServerManager.RegisterBroadcast<DropIntent>(OnDropIntentServer);
            InstanceFinder.ServerManager.RegisterBroadcast<ClaimIntent>(OnClaimIntentServer);
            InstanceFinder.ClientManager.RegisterBroadcast<GrabAck>(OnGrabAckClient);

            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnection;
        }

        // ---------------------------------------------------------- outgoing --
        int LocalId => InstanceFinder.ClientManager.Connection != null
            ? InstanceFinder.ClientManager.Connection.ClientId : -1;

        /// The stable FishNet ClientId (-1 when unknown) - the co-op identity (netcode §0).
        public static int LocalClientId => _instance != null ? _instance.LocalId : -1;

        /// ClientId  owner id. Offset by 1: the host's ClientId is 0, and
        /// LocalPlayerId 0 is the codebase's "no player yet" sentinel (netcode §0).
        public static int OwnerIdOf(int clientId) => clientId + 1;

        /// The stable owner id for THIS machine (-1 when not connected/known).
        public static int LocalOwnerId => _instance != null && _instance.LocalId >= 0
            ? OwnerIdOf(_instance.LocalId) : -1;

        /// Scene-path naming for things without a net id (props, colliders).
        public static string PathOf(Transform t) => FullPath(t);

        void SendLocalState()
        {
            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player == null || LocalId < 0) return;

            byte flags = 0;
            if (player.IsDowned) flags |= 1;
            if (player.IsSprawled || player.IsAirTumbling) flags |= 2; // both read as "helpless ragdoll" remotely

            var ghost = player.GetComponent<GhostState>();
            bool flying = ghost != null && ghost.IsGhost;
            if (flying) flags |= 4;

            InstanceFinder.ClientManager.Broadcast(new PlayerState
            {
                Id = LocalId,
                Pos = player.transform.position,
                Yaw = player.transform.eulerAngles.y,
                Pitch = player.LookPitch,
                Flags = flags,
                Team = MatchLobby.LocalTeam,
                GhostPos = flying ? ghost.SpiritAt : player.transform.position,
                GhostYaw = flying ? ghost.SpiritYaw : 0f,
                Acolyte = Sides.Of(Grimoire.LocalPlayerId) == Side.Acolyte
            }, Channel.Unreliable);
        }

        /// DrawingWorld calls this whenever a local stroke finishes - replicate
        /// it if it lives on world geometry or the local player's body.
        public static void OnLocalStrokeFinished(Stroke s)
        {
            if (_instance == null || !NetGame.Connected || ApplyingRemote) return;
            if (s == null || !s.Alive) return;
            // your own pen - and, on the HOST, the zombie scribes too (their ink is host truth)
            if (s.OwnerId != Grimoire.LocalPlayerId && !NetGame.IsHost) return;
            if (s.Surface == null) return;

            // body strokes ride mixamorig bones that carry ragdoll Rigidbodies,
            // so this branch must be decided before the dynamic-surface skips below
            bool bodyInk = s.Persistent
                && s.Surface.name.StartsWith("mixamorig:")
                && s.Surface.GetComponentInParent<SimpleFPSController>() != null;

            // still local-only: weapon engravings (remote hands hold nothing yet)
            // and the PaintShell fallback (the shell never exists on a remote)
            if (s.Persistent && !bodyInk) return;
            if (!bodyInk && s.Surface.GetComponentInParent<Creature>() != null) return;  // dynamic: later
            if (!bodyInk && s.Surface.GetComponentInParent<Rigidbody>() != null) return; // dynamic: later

            var pts = new List<Vector3>();
            Vector3 normal = Vector3.up;
            foreach (var n in s.Nodes)
            {
                if (n == null) continue;
                // body ink travels in bone-local space - the only frame that
                // means the same thing on both machines
                pts.Add(bodyInk ? s.Surface.InverseTransformPoint(n.transform.position)
                                : n.transform.position);
                normal = n.SurfaceNormal;
            }
            if (pts.Count < 2) return;
            if (bodyInk) normal = s.Surface.InverseTransformDirection(normal);

            if (s.NetId == 0) s.NetId = _nextStrokeId++;
            RegisterNetStroke(s);

            // the OWNER's pen-up verdict rides along - the host primes, never re-reads (netcode §1)
            // (not for body ink: its casts ship whole via BodySealFire, netcode §2)
            int readRune = 0;
            float readScore = 0f;
            int[] clOwners = null, clIds = null;
            if (!bodyInk && s.DeclaredRune == RuneType.None && s.OwnerId == Grimoire.LocalPlayerId
                && DrawingWorld.Instance != null)
            {
                _clusterBuf.Clear();
                _clusterBuf.Add(s);
                RuneGlyph.GrowTouchingCluster(_clusterBuf, DrawingWorld.Instance.Strokes);
                if (RuneGlyph.CachedVerdict(_clusterBuf, s.OwnerId, out var vr, out var vs))
                {
                    readRune = (int)vr;
                    readScore = vs;
                    clOwners = new int[_clusterBuf.Count];
                    clIds = new int[_clusterBuf.Count];
                    for (int i = 0; i < _clusterBuf.Count; i++)
                    {
                        clOwners[i] = _clusterBuf[i].OwnerId;
                        clIds[i] = _clusterBuf[i].NetId;
                    }
                }
            }

            InstanceFinder.ClientManager.Broadcast(new StrokeMsg
            {
                Owner = s.OwnerId,
                SurfacePath = bodyInk ? "" : FullPath(s.Surface), // bone name IS the address
                Normal = normal,
                Points = pts.ToArray(),
                DeclaredRune = (int)s.DeclaredRune,
                StrokeId = s.NetId,
                ReadRune = readRune,
                ReadScore = readScore,
                ClusterOwners = clOwners,
                ClusterIds = clIds,
                BoneName = bodyInk ? s.Surface.name : ""
            });
        }

        static readonly List<int> _drinkBuf = new List<int>();

        /// The owner drank/burned their own body ink - tell everyone, so the
        /// copies riding their avatar die too. Strokes that never replicated
        /// (NetId 0: drawn offline, shell-fallback ink) are skipped.
        public static void OnLocalInkBurned(List<Stroke> burned)
        {
            if (_instance == null || !NetGame.Connected || burned == null) return;
            _drinkBuf.Clear();
            foreach (var s in burned)
                if (s != null && s.NetId != 0 && s.OwnerId == Grimoire.LocalPlayerId)
                    _drinkBuf.Add(s.NetId);
            if (_drinkBuf.Count == 0) return;
            InstanceFinder.ClientManager.Broadcast(new InkBurnMsg
                { Owner = Grimoire.LocalPlayerId, Ids = _drinkBuf.ToArray() });
        }

        static string FullPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return "/" + path;
        }

        // ---------------------------------------------- outgoing helpers --
        /// A proxy took damage on a client: tell the host (called by NetZombieProxy).
        public static void SendZombieHit(int id, float amount)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new ZombieHit { Id = id, Amount = amount });
        }

        /// Host RoundDirector streams round state to clients (2 Hz).
        public static void PushRoundState(byte phase, int round, int left, float timer, int kills)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new RoundState
                { Phase = phase, Round = round, Left = left, Timer = timer, Kills = kills });
        }

        /// Host announces a kill so clients share the ink economy.
        public static void PushKill(Vector3 pos)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new KillFeed { Pos = pos });
        }

        /// Client's lobby ready toggle, host-ward.
        public static void SendReady(bool ready)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new ReadyMsg { Ready = ready });
        }

        /// Host streams lobby state to clients (MatchLobby throttles).
        public static void PushLobby(byte ready, byte total, float countdown, string map,
            int seed, byte acolytePct, byte durationMin)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new LobbyMsg
            {
                Ready = ready, Total = total, Countdown = countdown, Map = map,
                Seed = seed, AcolytePct = acolytePct, DurationMin = durationMin
            });
        }

        public static void PushReadyCall()
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new ReadyCallMsg());
        }

        public static void PushSideAssign(int[] acolyteOwners)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new SideAssignMsg { AcolyteOwners = acolyteOwners });
        }

        public static void PushStandOpen(bool open)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new StandMsg { Open = open });
        }

        /// Host boots a client. The disconnect is the whole message.
        public static void Kick(int clientId)
        {
            if (!NetGame.IsHost) return;
            if (InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out var conn))
                conn.Disconnect(true);
        }

        /// Connected remote client ids, for rosters.
        public static IEnumerable<int> RemoteIds
        {
            get
            {
                if (_instance == null) yield break;
                foreach (var id in _instance._avatars.Keys) yield return id;
            }
        }

        // ------------------------------- outgoing: seals/ink/lifting (netcode §1-§4) --
        /// A local unlock - the host's IsUnlocked must answer truthfully (netcode §1).
        public static void PushUnlock(int owner, int card, int rune)
        {
            if (_instance == null || !NetGame.Connected) return;
            if (owner != Grimoire.LocalPlayerId) return;
            InstanceFinder.ClientManager.Broadcast(new UnlockMsg { Owner = owner, Card = card, Rune = rune });
        }

        /// Declare a rune: every machine stamps the same strokes (netcode §1).
        public static void PushDeclare(List<Stroke> members, RuneType rune)
        {
            if (_instance == null || !NetGame.Connected || members == null) return;
            int n = 0;
            foreach (var m in members)
                if (m != null && m.NetId != 0) n++;
            if (n == 0) return;
            var owners = new int[n];
            var ids = new int[n];
            int i = 0;
            foreach (var m in members)
                if (m != null && m.NetId != 0) { owners[i] = m.OwnerId; ids[i] = m.NetId; i++; }
            InstanceFinder.ClientManager.Broadcast(new DeclareRuneMsg
                { Owner = Grimoire.LocalPlayerId, Owners = owners, Ids = ids, Rune = (int)rune });
        }

        /// Client book seal-page F: the HOST runs the detectors on its copies (netcode §2).
        public static void SendDeclareSealIntent(List<Stroke> members)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost || members == null) return;
            int n = 0;
            foreach (var m in members)
                if (m != null && m.NetId != 0) n++;
            if (n == 0) return;
            var owners = new int[n];
            var ids = new int[n];
            int i = 0;
            foreach (var m in members)
                if (m != null && m.NetId != 0) { owners[i] = m.OwnerId; ids[i] = m.NetId; i++; }
            InstanceFinder.ClientManager.Broadcast(new DeclareSealIntent { Owners = owners, Ids = ids });
        }

        /// A client body seal fired - body ink never replicates, so the whole
        /// resolved payload ships and the HOST builds the spell (netcode §2).
        public static void SendBodySealFire(Seal seal)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost || seal == null) return;
            int n = 0;
            foreach (var g in seal.Runes)
                if (g.Rune != RuneType.None && g.Strength > 0.02f) n++;
            n = Mathf.Min(n, 12);
            if (n == 0) return;
            var runes = new int[n];
            var strengths = new float[n];
            var centers = new Vector3[n];
            var dirs = new Vector3[n];
            var sizes = new float[n];
            int i = 0;
            foreach (var g in seal.Runes)
            {
                if (g.Rune == RuneType.None || g.Strength <= 0.02f || i >= n) continue;
                runes[i] = (int)g.Rune;
                strengths[i] = g.Strength;
                centers[i] = g.Centroid();
                sizes[i] = g.WorldBounds().size.magnitude * 0.5f;
                dirs[i] = (g.Rune == RuneType.DirectionAway || g.Rune == RuneType.DirectionToward)
                    ? Spell.ArrowDirFor(g, seal.PlaneNormal, g.Rune)
                    : seal.PlaneNormal;
                i++;
            }
            InstanceFinder.ClientManager.Broadcast(new BodySealFire
            {
                Origin = seal.PlaneOrigin,
                Normal = seal.PlaneNormal,
                Edges = seal.Edges,
                Duration = seal.Duration,
                Runes = runes,
                Strengths = strengths,
                Centers = centers,
                PushDirs = dirs,
                Sizes = sizes
            });
        }

        /// Host seal activated - clients get the gold ring, display only (netcode §2).
        public static void PushSeal(Seal seal)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected || seal == null) return;
            var nodes = seal.LoopNodes;
            var pts = new Vector3[nodes.Count]; // one-shot per seal close, not per frame
            for (int i = 0; i < nodes.Count; i++)
                pts[i] = nodes[i] != null ? nodes[i].transform.position : seal.PlaneOrigin;
            InstanceFinder.ServerManager.Broadcast(new SealMsg
                { SealId = seal.Id, Loop = pts, Duration = seal.Duration });
        }

        /// Host seal ended - ring down; resolved also burns the matching client ink (netcode §2).
        public static void PushSealEnd(Seal seal, bool resolved)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected || seal == null) return;
            _burnOwnersBuf.Clear();
            _burnIdsBuf.Clear();
            if (resolved)
            {
                foreach (var e in seal.Boundary) CollectBurn(e.Stroke);
                foreach (var s in seal.Payload) CollectBurn(s);
            }
            InstanceFinder.ServerManager.Broadcast(new SealEndMsg
            {
                SealId = seal.Id,
                Resolved = resolved,
                BurnOwners = _burnOwnersBuf.ToArray(),
                BurnIds = _burnIdsBuf.ToArray()
            });
        }

        static void CollectBurn(Stroke s)
        {
            if (s == null || s.NetId == 0 || s.Persistent) return;
            for (int i = 0; i < _burnIdsBuf.Count; i++) // split pieces share the source id
                if (_burnIdsBuf[i] == s.NetId && _burnOwnersBuf[i] == s.OwnerId) return;
            _burnOwnersBuf.Add(s.OwnerId);
            _burnIdsBuf.Add(s.NetId);
        }

        /// True while a received erase is being replayed - suppresses re-send.
        public static bool ApplyingRemoteErase { get; private set; }

        /// Local erase/scoop - the host's ink graph must not drift (netcode §2).
        public static void OnLocalErase(Vector3 from, Vector3 to, float radius)
        {
            if (_instance == null || !NetGame.Connected || ApplyingRemoteErase) return;
            InstanceFinder.ClientManager.Broadcast(new EraseMsg
                { Owner = Grimoire.LocalPlayerId, From = from, To = to, Radius = radius });
        }

        // lifting intents (netcode §4)
        public static void SendGrabIntent(int matterId, string path, float holdDist)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new GrabIntent
                { MatterId = matterId, Path = path ?? "", HoldDist = holdDist });
        }

        public static void SendLiftAim(Vector3 hand, Quaternion rot)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new LiftAim { Hand = hand, Rot = rot }, Channel.Unreliable);
        }

        public static void SendThrowIntent(Vector3 dir)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new ThrowIntent { Dir = dir });
        }

        public static void SendDropIntent()
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new DropIntent());
        }

        public static void SendClaimIntent(int particleId)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new ClaimIntent { ParticleId = particleId });
        }

        // ------------------------------------------------- server relaying --
        void OnZombieHitServer(NetworkConnection conn, ZombieHit msg, Channel channel)
        {
            foreach (var z in Zombie.All)
            {
                if (z == null || z.gameObject.GetInstanceID() != msg.Id) continue;
                z.GetComponent<Damageable>()?.TakeDamage(
                    Mathf.Min(msg.Amount, 60f), "a friend's magic"); // per-hit cap: no one-packet nukes
                return;
            }
        }

        void OnPlayerStateServer(NetworkConnection conn, PlayerState msg, Channel channel)
        {
            msg.Id = conn.ClientId; // trust the connection, not the packet
            ApplyState(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg, true, Channel.Unreliable);
        }

        void OnStrokeServer(NetworkConnection conn, StrokeMsg msg, Channel channel)
        {
            ApplyStroke(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnReadyServer(NetworkConnection conn, ReadyMsg msg, Channel channel) =>
            MatchLobby.SetRemoteReady(conn.ClientId, msg.Ready);

        void OnOutfitServer(NetworkConnection conn, OutfitMsg msg, Channel channel)
        {
            _outfits[msg.Id] = msg.Code ?? "";
            InstanceFinder.ServerManager.Broadcast(msg); // relay: everyone sees the look
        }

        void AnnounceIdentity()
        {
            string name = SteamLobby.SteamReady
                ? Steamworks.SteamFriends.GetPersonaName() : System.Environment.UserName;
            ulong sid = SteamLobby.SteamReady
                ? Steamworks.SteamUser.GetSteamID().m_SteamID : 0UL;
            InstanceFinder.ClientManager.Broadcast(new IdentityMsg
            { Id = LocalId, Name = name, SteamId = sid });
        }

        void OnIdentityServer(NetworkConnection conn, IdentityMsg msg, Channel channel)
        {
            msg.Id = conn.ClientId; // trust the connection, not the packet

            if (BanList.Contains(msg.SteamId))
            {
                conn.Disconnect(true);
                return;
            }

            _identities[msg.Id] = msg;
            InstanceFinder.ServerManager.Broadcast(msg);
            // late joiners get everyone already known
            foreach (var kv in _identities)
                if (kv.Key != msg.Id)
                    InstanceFinder.ServerManager.Broadcast(conn, kv.Value);
        }

        void OnIdentityClient(IdentityMsg msg, Channel channel)
            => _identities[msg.Id] = msg;

        /// Who a player id is, for the lobby inspect popup and the host's
        /// kick list. False until their announcement arrived.
        public static bool IdentityOf(int clientId, out string name, out ulong steamId)
        {
            if (_identities.TryGetValue(clientId, out var m))
            { name = m.Name; steamId = m.SteamId; return true; }
            name = ""; steamId = 0UL;
            return false;
        }

        /// A player absorbed a world source: everyone's copy vanishes (and
        /// respawns on its own lobby timer). The rune grant itself is not in
        /// here; unlocks already replicate through UnlockMsg.
        public static void SendAbsorb(Analyzable a)
        {
            if (_instance == null || !NetGame.Connected || a == null) return;
            InstanceFinder.ClientManager.Broadcast(new AbsorbMsg { Path = PathOf(a.transform) });
        }

        void OnAbsorbServer(NetworkConnection conn, AbsorbMsg msg, Channel channel)
        {
            ApplyAbsorb(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg, true);
        }

        void OnAbsorbClient(AbsorbMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyAbsorb(msg);
        }

        static void ApplyAbsorb(AbsorbMsg msg)
        {
            if (string.IsNullOrEmpty(msg.Path)) return;
            var go = GameObject.Find(msg.Path);
            if (go != null) go.GetComponent<Analyzable>()?.VanishRemote();
        }

        void OnOutfitClient(OutfitMsg msg, Channel channel)
        {
            if (msg.Id == LocalId) return;
            _outfits[msg.Id] = msg.Code ?? "";
            // arrived after the avatar was built? re-dress it in place
            if (_avatars.TryGetValue(msg.Id, out var avatar) && avatar != null)
                avatar.ApplyOutfit(msg.Code);
        }

        void OnRemoteConnection(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            RemoveAvatar(conn.ClientId);
            ReleaseHold(conn.ClientId, Vector3.zero); // leavers let go (netcode §4)
            MatchLobby.SetRemoteReady(conn.ClientId, false); // leavers aren't ready
            InstanceFinder.ServerManager.Broadcast(new PlayerLeft { Id = conn.ClientId });
        }

        // ---------------------- server handlers: seals/ink/lifting (netcode §1-§4) --
        void OnUnlockServer(NetworkConnection conn, UnlockMsg msg, Channel channel)
        {
            msg.Owner = OwnerIdOf(conn.ClientId); // trust the connection, not the packet
            if (msg.Owner != Grimoire.LocalPlayerId)
                Grimoire.UnlockRemote(msg.Owner, msg.Card, msg.Rune);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnUnlockClient(UnlockMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Owner == Grimoire.LocalPlayerId) return;
            Grimoire.UnlockRemote(msg.Owner, msg.Card, msg.Rune);
        }

        void OnDeclareRuneServer(NetworkConnection conn, DeclareRuneMsg msg, Channel channel)
        {
            ApplyDeclare(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnDeclareRuneClient(DeclareRuneMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyDeclare(msg);
        }

        void ApplyDeclare(DeclareRuneMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId) return; // self-echo
            if (msg.Owners == null || msg.Ids == null || msg.Owners.Length != msg.Ids.Length) return;
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                var s = FindNetStroke(msg.Owners[i], msg.Ids[i]);
                if (s == null || !s.Alive) continue;
                s.DeclaredRune = (RuneType)msg.Rune;
                s.SetColor(Stroke.RuneColor);
                s.MarkDirty();
            }
        }

        void OnDeclareSealServer(NetworkConnection conn, DeclareSealIntent msg, Channel channel)
        {
            if (DrawingWorld.Instance == null) return;
            if (msg.Owners == null || msg.Ids == null || msg.Owners.Length != msg.Ids.Length) return;
            var cluster = new List<Stroke>(msg.Ids.Length);
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                var s = FindNetStroke(msg.Owners[i], msg.Ids[i]);
                if (s != null && s.Alive) cluster.Add(s);
            }
            if (cluster.Count == 0) return;
            DrawingWorld.Instance.TryDeclareSeal(cluster, allowMirror: false); // mirror costs THEIR ink - skipped (netcode §2)
        }

        void OnBodySealServer(NetworkConnection conn, BodySealFire msg, Channel channel)
        {
            // rate cap, like ZombieHit - no packet spam machine-gunning spells
            if (_lastBodyFire.TryGetValue(conn.ClientId, out var t)
                && Time.unscaledTime - t < 0.25f) return;
            _lastBodyFire[conn.ClientId] = Time.unscaledTime;
            if (msg.Runes == null || msg.Strengths == null || msg.Centers == null
                || msg.PushDirs == null || msg.Sizes == null) return;
            int n = msg.Runes.Length;
            if (n == 0 || msg.Strengths.Length != n || msg.Centers.Length != n
                || msg.PushDirs.Length != n || msg.Sizes.Length != n) return;
            Transform caster = _avatars.TryGetValue(conn.ClientId, out var av) && av != null
                ? av.transform : null;
            Spell.CreateRemote(OwnerIdOf(conn.ClientId), msg.Origin, msg.Normal,
                Mathf.Clamp(msg.Edges, 1, 10),
                Mathf.Clamp(msg.Duration, 0.5f, DrawingConfig.SealProduceSeconds),
                msg.Runes, msg.Strengths, msg.Centers, msg.PushDirs, msg.Sizes, caster);
        }

        void OnEraseServer(NetworkConnection conn, EraseMsg msg, Channel channel)
        {
            ApplyErase(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnEraseClient(EraseMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyErase(msg);
        }

        void ApplyErase(EraseMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId || DrawingWorld.Instance == null) return;
            ApplyingRemoteErase = true;
            try
            {
                DrawingWorld.Instance.EraseAlong(msg.From, msg.To, Mathf.Min(msg.Radius, 0.5f), null);
            }
            finally
            {
                ApplyingRemoteErase = false;
            }
        }

        void OnGrabIntentServer(NetworkConnection conn, GrabIntent msg, Channel channel)
        {
            ReleaseHold(conn.ClientId, Vector3.zero); // one hold per hand
            Rigidbody rb = null;
            if (msg.MatterId != 0)
            {
                Matter blob = null;
                foreach (var m in Matter.Living)
                    if (m != null && m.gameObject.GetInstanceID() == msg.MatterId) { blob = m; break; }
                if (blob == null)
                {
                    Ack(conn, false, "what you aimed at is gone");
                    return;
                }
                // once touched, only a solid grabs again
                if (blob.Touched && blob.Phase != MatterPhase.Solid)
                {
                    Ack(conn, false, $"the {blob.Material} has been handled, only a SOLID grabs again");
                    return;
                }
                if (blob.Core != null) rb = HandGrab.AcquireBody(blob.Core, OwnerIdOf(conn.ClientId));
            }
            else if (!string.IsNullOrEmpty(msg.Path))
            {
                var go = GameObject.Find(msg.Path);
                var col = go != null ? go.GetComponent<Collider>() : null;
                if (col == null && go != null) col = go.GetComponentInChildren<Collider>();
                if (col != null) rb = HandGrab.AcquireBody(col, OwnerIdOf(conn.ClientId));
            }
            if (rb == null)
            {
                Ack(conn, false, "the host refused: no ink, or the world itself");
                return;
            }
            _holds[conn.ClientId] = new RemoteHold
            {
                Owner = OwnerIdOf(conn.ClientId),
                Body = rb,
                Marks = rb.GetComponentsInChildren<InkMark>(true),
                HadGravity = rb.useGravity
            };
            if (rb.GetComponent<Matter>() == null) TrackProp(rb); // matter rides MatterSnap
            Ack(conn, true, "");
        }

        void OnLiftAimServer(NetworkConnection conn, LiftAim msg, Channel channel)
        {
            if (!_holds.TryGetValue(conn.ClientId, out var h)) return;
            h.Hand = msg.Hand;
            h.Rot = msg.Rot;
            h.HasAim = true;
        }

        void OnThrowIntentServer(NetworkConnection conn, ThrowIntent msg, Channel channel)
        {
            Vector3 dir = msg.Dir.sqrMagnitude > 0.01f ? msg.Dir.normalized : Vector3.forward;
            if (_holds.TryGetValue(conn.ClientId, out var h) && h.Mote != null)
                ReleaseHold(conn.ClientId, dir * HandGrab.ThrowSpeed);
            else
                ReleaseHold(conn.ClientId, dir * HandGrab.ThrowImpulse);
        }

        void OnDropIntentServer(NetworkConnection conn, DropIntent msg, Channel channel)
            => ReleaseHold(conn.ClientId, Vector3.zero);

        void OnClaimIntentServer(NetworkConnection conn, ClaimIntent msg, Channel channel)
        {
            ReleaseHold(conn.ClientId, Vector3.zero);
            SpellParticle mote = null;
            foreach (var p in SpellParticle.Living)
                if (p != null && p.gameObject.GetInstanceID() == msg.ParticleId) { mote = p; break; }
            if (mote == null || mote.Dead || mote.Claimed)
            {
                Ack(conn, false, "that spell is gone (or already claimed)");
                return;
            }
            Transform holder = _avatars.TryGetValue(conn.ClientId, out var av) && av != null
                ? av.transform : null;
                mote.Claim(holder); // claiming is harvesting - the rune re-emits
            _holds[conn.ClientId] = new RemoteHold { Owner = OwnerIdOf(conn.ClientId), Mote = mote };
            Ack(conn, true, "");
        }

        void Ack(NetworkConnection conn, bool ok, string note)
            => InstanceFinder.ServerManager.Broadcast(conn, new GrabAck { Ok = ok, Note = note });

        void OnGrabAckClient(GrabAck msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (!msg.Ok) HandGrab.RemoteHoldRefused(msg.Note);
        }

        // -------------------------------------------------- client applying --
        void OnPlayerStateClient(PlayerState msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host applied via server path
            ApplyState(msg);
        }

        void OnStrokeClient(StrokeMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ApplyStroke(msg);
        }

        void OnPlayerLeftClient(PlayerLeft msg, Channel channel) => RemoveAvatar(msg.Id);

        void OnZombieSnapClient(ZombieSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host has the real ones
            if (msg.Ids == null) return;

            _seen.Clear();
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                int id = msg.Ids[i];
                _seen.Add(id);
                if (!_proxies.TryGetValue(id, out var proxy) || proxy == null)
                {
                    proxy = NetZombieProxy.Build(id, (ZombieKind)msg.Kinds[i], msg.Pos[i]);
                    _proxies[id] = proxy;
                }
                proxy.Target(msg.Pos[i], msg.Yaw[i]);
            }

            // gone from the host's list = dead; remove the proxy
            _gone.Clear();
            foreach (var kv in _proxies)
                if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                if (_proxies[id] != null) _proxies[id].Vanish();
                _proxies.Remove(id);
            }
        }

        void OnKillFeedClient(KillFeed msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            PlayerInk.AwardAll(DrawingConfig.InkPerKill); // shared economy, client side
            SealAutopsy.OnKill();
            Powerups.OnKill(); // clients level off shared kills too
        }

        void OnRoundStateClient(RoundState msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            HasRound = true;
            NetPhase = msg.Phase;
            NetRound = msg.Round;
            NetLeft = msg.Left;
            NetTimer = msg.Timer;
            NetKills = msg.Kills;

            // phase 1 = the host's match went live: clients still in the lobby
            // follow to the host's map. Only the lobby auto-leaves.
            string here = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (msg.Phase == 1 && here == "Lobby")
            {
                string map = MatchLobby.HostMap;
                if (!string.IsNullOrEmpty(map) && Application.CanStreamedLevelBeLoaded(map))
                {
                    LoadEgg.Cover();
                    LoadingHints.Show(); // one random tip rides every load
                    UnityEngine.SceneManagement.SceneManager.LoadScene(map);
                }
            }
            // and home again: the match ended and the host stands in the lobby
            else if (msg.Phase == 0 && here != "Lobby" && here != "Menu")
            {
                LoadEgg.Cover();
                LoadingHints.Show();
                UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
            }
        }

        void OnLobbyClient(LobbyMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            MatchLobby.NetLobby(msg.Ready, msg.Total, msg.Countdown, msg.Map,
                msg.Seed, msg.AcolytePct, msg.DurationMin);
        }

        void OnReadyCallClient(ReadyCallMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            MatchLobby.OnReadyCall();
        }

        void OnSideAssignClient(SideAssignMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started || msg.AcolyteOwners == null) return;
            MatchLobby.ApplySideAssign(msg.AcolyteOwners);
        }

        void OnStandClient(StandMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            LobbyStand.HostMenuOpen = msg.Open;
        }

        // ------------------- client applying: seals/matter/particles (netcode §2/§3) --
        void OnSealClient(SealMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Loop == null || msg.Loop.Length < 3) return;
            if (_rings.TryGetValue(msg.SealId, out var old) && old != null) Destroy(old.gameObject);
            _rings[msg.SealId] = NetSealRing.Show(msg.Loop, msg.Duration);
        }

        void OnSealEndClient(SealEndMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (_rings.TryGetValue(msg.SealId, out var ring))
            {
                if (ring != null) Destroy(ring.gameObject);
                _rings.Remove(msg.SealId);
            }
            // resolved on the host = that environment ink is CONSUMED here too
            if (!msg.Resolved || msg.BurnIds == null || msg.BurnOwners == null
                || msg.BurnIds.Length != msg.BurnOwners.Length) return;
            for (int i = 0; i < msg.BurnIds.Length; i++)
            {
                var s = FindNetStroke(msg.BurnOwners[i], msg.BurnIds[i]);
                if (s != null && s.Alive && !s.Persistent) s.Burn();
            }
        }

        void OnMatterSnapClient(MatterSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Ids == null) return;

            _seen.Clear();
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                int id = msg.Ids[i];
                if (id == 0) continue;
                _seen.Add(id);
                var mat = (SurfaceMaterialType)msg.Mats[i];
                var phase = (MatterPhase)msg.Phases[i];
                if (_matterProxies.TryGetValue(id, out var proxy) && proxy != null
                    && (proxy.Mat != mat || proxy.Phase != phase))
                {
                    Destroy(proxy.gameObject); // phase/material changed - rebuild with the new shell
                    proxy = null;
                }
                if (proxy == null)
                {
                    proxy = NetMatterProxy.Build(id, mat, phase, msg.Pos[i], msg.Scale[i]);
                    _matterProxies[id] = proxy;
                }
                proxy.Target(msg.Pos[i], msg.Rot[i], msg.Scale[i], msg.Looks[i]);
            }

            _gone.Clear();
            foreach (var kv in _matterProxies)
                if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                if (_matterProxies[id] != null) Destroy(_matterProxies[id].gameObject);
                _matterProxies.Remove(id);
            }
        }

        void OnParticleSnapClient(ParticleSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Ids == null) return;

            _seen.Clear();
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                int id = msg.Ids[i];
                if (id == 0) continue;
                _seen.Add(id);
                var kind = (ParticleKind)msg.Kinds[i];
                if (_moteProxies.TryGetValue(id, out var proxy) && proxy != null && proxy.Kind != kind)
                {
                    Destroy(proxy.gameObject); // it combined into something else - rebuild the look
                    proxy = null;
                }
                if (proxy == null)
                {
                    proxy = NetMoteProxy.Build(id, kind, msg.Pos[i]);
                    _moteProxies[id] = proxy;
                }
                proxy.Target(msg.Pos[i], msg.Scale[i]);
            }

            _gone.Clear();
            foreach (var kv in _moteProxies)
                if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                if (_moteProxies[id] != null) Destroy(_moteProxies[id].gameObject);
                _moteProxies.Remove(id);
            }
        }

        void OnPropRegClient(PropReg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (string.IsNullOrEmpty(msg.Path)) return;
            var go = GameObject.Find(msg.Path);
            if (go == null) return; // scene mismatch - skip quietly
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true; // the HOST simulates; we follow
            if (go.GetComponent<NetPropGhost>() == null) go.AddComponent<NetPropGhost>();
            _propGhosts[msg.Id] = go.transform;
        }

        void OnPropSnapClient(PropSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Ids == null) return;
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                if (!_propGhosts.TryGetValue(msg.Ids[i], out var t) || t == null) continue;
                var ghost = t.GetComponent<NetPropGhost>();
                if (ghost != null) ghost.Target(msg.Pos[i], msg.Rot[i]);
            }
        }

        // ------------------------------------------------------------ apply --
        void ApplyState(PlayerState msg)
        {
            if (msg.Id == LocalId) return;
            if (!_avatars.TryGetValue(msg.Id, out var avatar) || avatar == null)
            {
                avatar = NetAvatar.Build(msg.Id);
                _avatars[msg.Id] = avatar;
            }
            avatar.Target(msg.Pos, msg.Yaw, msg.Flags, msg.Team, msg.Pitch);
            avatar.TargetGhost((msg.Flags & 4) != 0, msg.GhostPos, msg.GhostYaw, msg.Acolyte);
        }

        void ApplyStroke(StrokeMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId || DrawingWorld.Instance == null) return;

            // body ink names a BONE, not a scene path
            if (!string.IsNullOrEmpty(msg.BoneName)) { ApplyBodyStroke(msg); return; }

            Transform surface = null;
            var go = GameObject.Find(msg.SurfacePath);
            if (go != null) surface = go.transform;
            if (surface == null) return; // scene mismatch - skip quietly

            ApplyingRemote = true;
            try
            {
                ZombieScribe.PlaneBasis(msg.Normal, out var right, out var up);
                var s = new Stroke
                {
                    BasisRight = right,
                    BasisUp = up,
                    Surface = surface,
                    OwnerId = msg.Owner,
                    DeclaredRune = (RuneType)msg.DeclaredRune,
                    NetId = msg.StrokeId
                };
                RegisterNetStroke(s); // (owner, id)  this copy (netcode §0)
                DrawingWorld.Instance.Register(s);
                for (int i = 0; i < msg.Points.Length; i++)
                    s.AddNode(DrawNode.Create(s, i, msg.Points[i], msg.Normal, surface));
                DrawingWorld.Instance.CompleteStroke(s);

                // the OWNER's verdict primes the cache - never recomputed here (netcode §1)
                if (msg.ClusterIds != null && msg.ClusterOwners != null
                    && msg.ClusterIds.Length > 0 && msg.ClusterOwners.Length == msg.ClusterIds.Length)
                {
                    _clusterBuf.Clear();
                    bool complete = true;
                    for (int i = 0; i < msg.ClusterIds.Length; i++)
                    {
                        var member = FindNetStroke(msg.ClusterOwners[i], msg.ClusterIds[i]);
                        if (member == null || !member.Alive) { complete = false; break; }
                        _clusterBuf.Add(member);
                    }
                    if (complete)
                        RuneGlyph.Prime(_clusterBuf, msg.Owner, (RuneType)msg.ReadRune, msg.ReadScore);
                }

                DrawingWorld.Instance.RequestDetect(); // their circle seals here too
            }
            finally
            {
                ApplyingRemote = false;
            }
        }

        /// A friend's body ink: points arrive bone-local and mount on the same-named
        /// avatar bone. The copy is cosmetic and evaporation-exempt - no recognition,
        /// claim or closure (the owner's BodySealFire carries any cast, netcode §2).
        void ApplyBodyStroke(StrokeMsg msg)
        {
            // avatars key by CLIENT id; stroke owners are OwnerIdOf = client+1
            if (!_avatars.TryGetValue(msg.Owner - 1, out var avatar) || avatar == null) return;
            if (msg.Points == null || msg.Points.Length < 2) return;

            Transform bone = null;
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                if (t.name == msg.BoneName) { bone = t; break; }
            if (bone == null) return; // capsule-fallback avatar: no skeleton, skip quietly

            ApplyingRemote = true;
            try
            {
                Vector3 normal = bone.TransformDirection(msg.Normal);
                ZombieScribe.PlaneBasis(normal, out var right, out var up);
                var s = new Stroke
                {
                    BasisRight = right,
                    BasisUp = up,
                    Surface = bone,
                    OwnerId = msg.Owner,
                    DeclaredRune = (RuneType)msg.DeclaredRune,
                    NetId = msg.StrokeId
                };
                RegisterNetStroke(s); // (owner, id)  this copy, so drink-burns find it
                DrawingWorld.Instance.Register(s);
                for (int i = 0; i < msg.Points.Length; i++)
                    s.AddNode(DrawNode.Create(s, i,
                        bone.TransformPoint(msg.Points[i]), normal, bone));
                DrawingWorld.Instance.CompleteStroke(s,
                    allowCloseOntoInk: false, silent: true, preview: false);
            }
            finally
            {
                ApplyingRemote = false;
            }
        }

        // --------------------------------------------------- book stand lobby --
        static readonly Dictionary<int, string> _mapLikes = new Dictionary<int, string>();   // host: clientId  liked map
        static readonly Dictionary<string, int> _likeCounts = new Dictionary<string, int>(); // everyone: map  likes (stand UI reads)

        /// Likes for a map, as last announced by the host.
        public static int LikeCount(string map) =>
            !string.IsNullOrEmpty(map) && _likeCounts.TryGetValue(map, out var n) ? n : 0;

        /// Local player likes a map at the stand. One like per player; liking
        /// another map moves it.
        public static void SendMapLike(string map)
        {
            if (_instance == null || !NetGame.Connected || string.IsNullOrEmpty(map)) return;
            if (NetGame.IsHost) _instance.ApplyLike(-1, map); // the host's own like, id -1
            else InstanceFinder.ClientManager.Broadcast(new MapLikeMsg { Map = map });
        }

        void OnMapLikeServer(NetworkConnection conn, MapLikeMsg msg, Channel channel)
            => ApplyLike(conn.ClientId, msg.Map);

        void ApplyLike(int clientId, string map)
        {
            _mapLikes[clientId] = map ?? "";
            _likeCounts.Clear();
            foreach (var kv in _mapLikes)
                if (!string.IsNullOrEmpty(kv.Value))
                    _likeCounts[kv.Value] = (_likeCounts.TryGetValue(kv.Value, out var n) ? n : 0) + 1;
            var maps = new string[_likeCounts.Count];
            var counts = new int[_likeCounts.Count];
            int i = 0;
            foreach (var kv in _likeCounts) { maps[i] = kv.Key; counts[i] = kv.Value; i++; }
            InstanceFinder.ServerManager.Broadcast(new MapLikesMsg { Maps = maps, Counts = counts });
        }

        void OnMapLikesClient(MapLikesMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host tallied it itself
            _likeCounts.Clear();
            if (msg.Maps == null || msg.Counts == null) return;
            for (int i = 0; i < msg.Maps.Length && i < msg.Counts.Length; i++)
                _likeCounts[msg.Maps[i]] = msg.Counts[i];
        }

        /// The moment our CLIENT connection stands, announce the password we
        /// typed at the stand. The host ignores empty-password lobbies.
        void OnLocalClientState(FishNet.Transporting.ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != FishNet.Transporting.LocalConnectionState.Started) return;
            if (InstanceFinder.ServerManager.Started) return; // the host trusts itself
            InstanceFinder.ClientManager.Broadcast(new JoinAuthMsg
                { Password = NetGame.JoinPassword ?? "" });
        }

        /// Wrong password = disconnected on the spot. No password set = open
        /// lobby, everyone passes.
        void OnJoinAuthServer(NetworkConnection conn, JoinAuthMsg msg, Channel channel)
        {
            if (string.IsNullOrEmpty(NetGame.HostPassword)) return;
            if ((msg.Password ?? "") == NetGame.HostPassword) return;
            Debug.Log($"[SpellyZombie] Book stand: wrong lobby password from client {conn.ClientId} — kicked.");
            conn.Disconnect(true);
        }

        // ---------------------------------------------------------- the pot --
        /// Local player excluded - the sim already counts it directly.
        public static void EachRemotePlayer(System.Action<int, Vector3> visit)
        {
            if (_instance == null) return;
            foreach (var kv in _instance._avatars)
                if (kv.Value != null) visit(OwnerIdOf(kv.Key), kv.Value.transform.position);
        }

        public static void PushPot(float fill01, bool corrupt, float prep)
        {
            if (_instance == null || !NetGame.IsHost || !NetGame.Connected) return;
            InstanceFinder.ServerManager.Broadcast(new PotMsg
                { Fill01 = fill01, Corrupt = corrupt, Prep = prep });
        }

        public static void SendPotDrink(float amount)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new PotDrinkMsg { Amount = amount });
        }

        void OnPotClient(PotMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // the host IS the truth
            CauldronEconomy.ApplyNet(msg.Fill01, msg.Corrupt, msg.Prep);
        }

        void OnPotDrinkServer(NetworkConnection conn, PotDrinkMsg msg, Channel channel)
        {
            // sanity-capped: nobody drinks more than a second of near-rate per bill
            float amount = Mathf.Clamp(msg.Amount, 0f, DrawingConfig.PotRefillNearPerSec);
            CauldronEconomy.Active?.BillInk(amount);
        }

        void ApplyInkBurn(InkBurnMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId || msg.Ids == null) return;
            foreach (var id in msg.Ids)
            {
                var s = FindNetStroke(msg.Owner, id);
                if (s != null && s.Alive) s.Burn(); // the sweep culls the dead entry
            }
        }

        void OnInkBurnServer(NetworkConnection conn, InkBurnMsg msg, Channel channel)
        {
            ApplyInkBurn(msg);
            InstanceFinder.ServerManager.BroadcastExcept(conn, msg);
        }

        void OnInkBurnClient(InkBurnMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host applied via server path
            ApplyInkBurn(msg);
        }

        void RemoveAvatar(int id)
        {
            if (_avatars.TryGetValue(id, out var avatar) && avatar != null)
                Destroy(avatar.gameObject);
            _avatars.Remove(id);
        }
    }

    /// A remote player's avatar, lerped to broadcast state; tips over when
    /// their flags say downed/sprawled.
    public class NetAvatar : MonoBehaviour
    {
        public static readonly System.Collections.Generic.List<NetAvatar> All
            = new System.Collections.Generic.List<NetAvatar>();

        public int Id { get; private set; }
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        Vector3 _targetPos;
        float _targetYaw, _targetPitch;
        byte _flags;
        byte _team = 255;
        Transform _head;          // bends with their look pitch
        Quaternion _headBase;     // ...idempotently, from this base

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith(":Head")) { _head = t; _headBase = t.localRotation; break; }
        }

        public bool Downed => (_flags & 1) != 0;

        public static NetAvatar Build(int id)
        {
            GameObject go;
            System.Collections.Generic.List<GameObject> costume = null;
            SocketSet sockets = null;
            var prefab = CharacterLibrary.Model;
            if (prefab != null)
            {
                // no pose sync yet - the prefab's T-pose arms are eased down
                go = new GameObject($"NetPlayer_{id}");
                // makes their body-ink copies Persistent: evaporation-exempt and
                // never consumed by spells, same as on the owner's machine
                go.AddComponent<PersistentInkSurface>();
                var body = Object.Instantiate(prefab, go.transform);
                body.name = "Body";
                body.transform.localPosition = new Vector3(0f, -0.9f, 0f); // avatar anchor is mid-body
                // their grimoire stays fully visible; its page arrows do not
                foreach (var pages in body.GetComponentsInChildren<GrimoirePages>(true))
                    pages.HideForRemote();
                Transform armL = null, armR = null, handL = null, handR = null, head = null;
                Transform footL = null, toeL = null;
                foreach (var t in body.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.EndsWith("LeftArm")) armL = t;
                    else if (t.name.EndsWith("RightArm")) armR = t;
                    else if (t.name.EndsWith("LeftHand")) handL = t;
                    else if (t.name.EndsWith("RightHand")) handR = t;
                    else if (t.name.EndsWith("LeftToeBase")) toeL = t;
                    else if (t.name.EndsWith("LeftFoot")) footL = t;
                    else if (t.name.EndsWith(":Head")) head = t;
                }
                CharacterRig.FaceForward(body.transform, footL, toeL, go.transform.forward);
                LowerArm(armL, handL);
                LowerArm(armR, handR);
                // friends wear the team outfit too (retinted in Target) -
                // dressed with THEIR announced outfit, not ours
                sockets = SocketSet.Build(body, go.transform);
                costume = Wardrobe.DressPlayer(
                    sockets, new Color(0.35f, 0.55f, 0.9f), null,
                    outfitCode: NetSync.OutfitOf(id));
                var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    // the body stays SKIN - the hat/cloak carry the team color
                    smr.sharedMaterial = MatterFX.Get(new Color(0.93f, 0.87f, 0.72f), MoteShade.Opaque);
                    smr.updateWhenOffscreen = true;
                }
                var faceEyes = GooglyEyes.Attach(head != null ? head : go.transform,
                    head != null ? 0f : 0.6f, CharacterRig.EyeScale);
                if (head != null)
                {
                    // one knob for all eyes: CharacterRig.EyeLocalPos
                    faceEyes.transform.localPosition = CharacterRig.EyeLocalPos;
                    faceEyes.transform.localRotation = Quaternion.identity;
                    faceEyes.transform.localScale = Vector3.one * CharacterRig.EyeRigScale;
                }
                faceEyes.SetVisible(true);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"NetPlayer_{id}";
                go.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
                go.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.35f, 0.55f, 0.9f), MoteShade.Opaque); // friendly blue
                Object.Destroy(go.GetComponent<Collider>()); // presence, not physics (yet)
                var eyes = GooglyEyes.Attach(go.transform, 0.6f, 1.4f);
                eyes.SetVisible(true);
            }

            var a = go.AddComponent<NetAvatar>();
            a.Id = id;
            a._targetPos = go.transform.position;
            a._costume = costume;
            a._sockets = sockets;
            return a;
        }

        System.Collections.Generic.List<GameObject> _costume;
        SocketSet _sockets;

        // ---- their ghost ----
        Transform _ghost;
        Vector3 _ghostTarget;
        float _ghostYaw;

        /// Their spirit: shown while their flags say ghost, hidden the moment
        /// they are revived. The wisp is the ghost prefab, the same one the
        /// local player flies (GhostState.SharedPrefab) - nothing built here.
        public void TargetGhost(bool flying, Vector3 at, float yaw, bool acolyte)
        {
            _ghostTarget = at;
            _ghostYaw = yaw;

            if (!flying)
            {
                if (_ghost != null) { Destroy(_ghost.gameObject); _ghost = null; }
                return;
            }
            if (_ghost == null)
            {
                var prefab = GhostState.SharedPrefab;
                if (prefab == null) return; // the slot is empty - GhostState already said so
                var go = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f));
                go.name = "NetGhost";
                foreach (var c in go.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                foreach (var a in go.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
                Color side = GhostState.GhostSideColor(acolyte);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (GhostState.Named(r.transform, "eye")) continue;
                    PillarBeam.Tint(r, GhostState.Named(r.transform, "hat")
                        ? MatchLobby.TeamColors[Mathf.Min(_team, (byte)(MatchLobby.TeamColors.Length - 1))]
                        : side);
                }
                _ghost = go.transform;
            }
        }

        void OnDestroy() { if (_ghost != null) Destroy(_ghost.gameObject); }

        /// A (re)announced outfit arrived after this avatar was built -
        /// strip the old costume and wear the new one, keeping the team tint.
        public void ApplyOutfit(string code)
        {
            if (_sockets == null) return;
            if (_costume != null)
                foreach (var p in _costume)
                    if (p != null) Destroy(p);
            _costume = Wardrobe.DressPlayer(_sockets, new Color(0.35f, 0.55f, 0.9f), null,
                outfitCode: code);
            if (_team != 255)
                Wardrobe.Retint(_costume,
                    MatchLobby.TeamColors[Mathf.Min(_team, (byte)(MatchLobby.TeamColors.Length - 1))]);
        }

        /// Swing an arm from the T-pose toward hanging down (sign-proof).
        static void LowerArm(Transform upper, Transform hand)
        {
            if (upper == null || hand == null) return;
            Vector3 dir = hand.position - upper.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            Vector3 target = Vector3.Slerp(dir.normalized, Vector3.down, 0.75f);
            upper.rotation = Quaternion.FromToRotation(dir, target) * upper.rotation;
        }

        public void Target(Vector3 pos, float yaw, byte flags, byte team, float pitch = 0f)
        {
            _targetPos = pos;
            _targetYaw = yaw;
            _targetPitch = pitch;
            _flags = flags;
            if (team != _team) // friends wear their team color
            {
                _team = team;
                var teamColor = MatchLobby.TeamColors[
                    Mathf.Min(team, (byte)(MatchLobby.TeamColors.Length - 1))];
                // the OUTFIT carries the team color (the body stays skin) -
                // capsule fallback tints itself since it has no clothes
                if (_costume != null)
                    Wardrobe.Retint(_costume, teamColor);
                else
                {
                    var rend = GetComponent<Renderer>();
                    if (rend != null)
                        rend.sharedMaterial = MatterFX.Get(teamColor, MoteShade.Opaque);
                }
            }
        }

        void Update()
        {
            // their spirit glides on the same lerp their body uses
            if (_ghost != null)
            {
                _ghost.position = Vector3.Lerp(_ghost.position, _ghostTarget, Time.deltaTime * 12f);
                _ghost.rotation = Quaternion.Slerp(_ghost.rotation,
                    Quaternion.Euler(0f, _ghostYaw, 0f), Time.deltaTime * 10f);
            }

            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * 12f);
            float roll = (_flags & 1) != 0 ? 80f : (_flags & 2) != 0 ? 70f : 0f;
            var target = Quaternion.Euler(0f, _targetYaw, roll);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 10f);

            // their head follows their look pitch (rebuilt from base every frame)
            if (_head != null)
            {
                _head.localRotation = _headBase;
                _head.rotation = Quaternion.AngleAxis(_targetPitch * 0.8f, transform.right)
                    * _head.rotation;
            }
        }
    }
}
