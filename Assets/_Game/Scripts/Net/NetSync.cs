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

        public struct SpawnAskMsg : IBroadcast // client → host: where do I stand?
        {
            public int Owner;
        }

        public struct VoiceMsg : IBroadcast // any → host → all: a slice of compressed voice
        {
            public int Owner;
            public bool Ghost;
            public byte[] Data;
        }

        public struct AbsorbAskMsg : IBroadcast // client → host: I pull at this source
        {
            public int Owner;
            public Vector3 At;
        }

        public struct AbsorbGiveMsg : IBroadcast // host → all: the mote flies to the winner
        {
            public int Owner;
            public Vector3 At;
        }

        public struct SpawnGiveMsg : IBroadcast // host → all: stand HERE
        {
            public int Owner;
            public Vector3 Point;
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
            public Vector3[] Scale;   // a summon's size is not in its kind
        }

        public struct GolemSnap : IBroadcast // host  clients, 10 Hz unreliable
        {
            public int[] Ids;
            public Vector3[] Pos;
            public float[] Yaw;
            public Vector3[] Scale;
            public Color[] Skin;      // the biome that raised it, not re-derived
            public int[] Owner;       // the player whose spell raised it, -1 for nature's own
        }

        public struct BiomeMsg : IBroadcast // host  clients: a lvl3 spell opened
        {
            public Vector3 At;
            public float Temp, Lum, Pressure, Balance, State;
            public float Affinity, Strength, Int, Courage, Clones;
            public float Radius;
            public float Seconds; // 0 = the lvl3 default; short = a terrain-burst linger
        }

        /// host -> all: something happened TO a body. ONE channel for every
        /// effect that lands on a player, because a remote player is only a
        /// puppet on the host - their real body, health and state live on their
        /// own machine, and nothing could reach it before this.
        public struct PlayerFxMsg : IBroadcast
        {
            public int Owner;     // whose body
            public byte Kind;     // 0 hurt, 1 heal, 2 buff, 3 phase, 4 blink, 5 trail, 6 fade
            public float Amount;  // damage / heal / buff / seconds
            public byte Phase;    // Kind 3 only: the MatterPhase to wear
            public Vector3 Point; // Kind 4: where to. Kind 0: the shove
        }

        /// client -> host: I want to hurt the thing with this id.
        /// The client does NOT subtract anything itself.
        public struct HurtIntent : IBroadcast
        {
            public int NetId;
            public float Amount;
        }

        /// host -> all: a mark was left on something. Curses read these, so
        /// every machine has to agree about who did what to whom.
        public struct MarkMsg : IBroadcast
        {
            public int Owner;
            public byte What;
            public int Value;
        }

        /// host -> all: this is what that thing's health IS. Applied verbatim,
        /// so one tree cannot end up on two different numbers.
        /// ★ WHAT THINGS IN THE WORLD CURRENTLY ARE. Health already crossed;
        /// nothing else did - so on a client a burning crate was not hot, a
        /// thing turning to gas never faded, and a frozen zombie looked fine.
        ///
        /// Only the axes you can SEE travel, and only for things that have
        /// actually left their natural, which is a small minority at any moment.
        /// Simulation stays entirely on the host; this is the picture of it.
        public struct StateMsg : IBroadcast
        {
            public int[] Ids;
            public short[] Temp;    // degrees, rounded - a degree is not visible
            public sbyte[] State;   // -100..100, so transparency and phase read
            public sbyte[] Lum;
            // ★ THE WHOLE BOARD RIDES (parity): without these a client could
            // not feel slick, sticky, weight, bravery or drunkenness at all
            public sbyte[] Press;   // -100..100 of AxisCap
            public sbyte[] Bal;
            public sbyte[] Aff;
            public sbyte[] Mind;
            public sbyte[] Cour;
        }

        public struct HealthMsg : IBroadcast
        {
            public int NetId;
            public float Health;
            public float Max;
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
            public byte Ending; // Achievements.Ending, set when Phase is Over
        }

        // ---- host-authoritative seals/matter/particles/lifting (netcode §1-§4) ----
        public struct UnlockMsg : IBroadcast // any  host  all: grimoire truth
        {
            public int Owner;
            public int Card; // -1 = none
            public int Rune; // -1 = none
        }

        /// ★ ONE BOOK FOR EVERYONE (his law): the host's spellbook is the
        /// match's spellbook, carried whole as JSON.
        public struct BookMsg : IBroadcast
        {
            public string Json;
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
            public int Owner;
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
            /// WHICH POSED BLOB, by its index in the authored shape list - the
            /// same list on every machine in a build, so a client wears what
            /// the host wears without a name per particle per snapshot.
            public byte[] Kinds;
            /// The colour its ten numbers came out as. A client cannot compute
            /// this: it never sees the payload.
            public Color32[] Tints;
            /// 1 · 2 · 3 - a lvl2 shows its ring, a lvl3 shows it is a place.
            public byte[] Levels;
            /// WHAT IT IS RIDING, by that thing's net id - 0 for a free mote.
            /// An attached particle moves with its host, so a client that does
            /// not know the host would show a hook hanging in mid air while the
            /// person it caught walks off.
            public int[] Rides;
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
                    SendGolemSnap();   // nature's own, same beat
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
        Vector3[] _snapScale = System.Array.Empty<Vector3>();
        int[] _gIds = System.Array.Empty<int>();
        Vector3[] _gPos = System.Array.Empty<Vector3>();
        float[] _gYaw = System.Array.Empty<float>();
        Vector3[] _gScale = System.Array.Empty<Vector3>();
        Color[] _gSkin = System.Array.Empty<Color>();
        int[] _gOwner = System.Array.Empty<int>();

        void SendZombieSnap()
        {
            int n = Zombie.All.Count;
            if (_snapIds.Length != n) // receivers read Ids.Length - size must match exactly
            {
                _snapIds = new int[n];
                _snapPos = new Vector3[n];
                _snapYaw = new float[n];
                _snapKinds = new byte[n];
                _snapScale = new Vector3[n];
            }
            for (int i = 0; i < n; i++)
            {
                var z = Zombie.All[i];
                if (z == null) // zero the slot - reused buffers would otherwise leak a stale zombie
                {
                    _snapIds[i] = 0; _snapPos[i] = default; _snapYaw[i] = 0f;
                    _snapKinds[i] = 0; _snapScale[i] = Vector3.one;
                    continue;
                }
                _snapIds[i] = z.gameObject.GetInstanceID();
                _snapPos[i] = z.transform.position;
                _snapYaw[i] = z.transform.eulerAngles.y;
                // one body, but MELEE VS RANGED still has to reach every
                // screen: it is the colour that tells a player which zombie
                // is about to throw something at them
                var sz = z.GetComponent<SummonedZombie>();
                _snapKinds[i] = (byte)(sz != null && sz.Ranged ? 1 : 0);
                _snapScale[i] = z.transform.localScale;
            }
            var snap = new ZombieSnap { Ids = _snapIds, Pos = _snapPos, Yaw = _snapYaw,
                Kinds = _snapKinds, Scale = _snapScale };
            InstanceFinder.ServerManager.Broadcast(snap, true, Channel.Unreliable);
        }

        void SendGolemSnap()
        {
            int n = Golem.All.Count;
            if (_gIds.Length != n) // receivers read Ids.Length - size must match exactly
            {
                _gIds = new int[n];
                _gPos = new Vector3[n];
                _gYaw = new float[n];
                _gScale = new Vector3[n];
                _gSkin = new Color[n];
                _gOwner = new int[n];
            }
            for (int i = 0; i < n; i++)
            {
                var g = Golem.All[i];
                if (g == null) // zero the slot - a reused buffer would leak a stale golem
                {
                    _gIds[i] = 0; _gPos[i] = default; _gYaw[i] = 0f;
                    _gScale[i] = Vector3.one; _gSkin[i] = Color.gray; _gOwner[i] = -1;
                    continue;
                }
                _gIds[i] = g.gameObject.GetInstanceID();
                _gPos[i] = g.transform.position;
                _gYaw[i] = g.transform.eulerAngles.y;
                _gScale[i] = g.transform.localScale;
                _gSkin[i] = g.Skin;
                _gOwner[i] = g.OwnerId;
            }
            InstanceFinder.ServerManager.Broadcast(new GolemSnap
            { Ids = _gIds, Pos = _gPos, Yaw = _gYaw, Scale = _gScale, Skin = _gSkin, Owner = _gOwner },
                true, Channel.Unreliable);
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
        Color32[] _pTints = System.Array.Empty<Color32>();
        byte[] _pLevels = System.Array.Empty<byte>();
        int[] _pRides = System.Array.Empty<int>();
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
                _pTints = new Color32[n];
                _pLevels = new byte[n];
                _pRides = new int[n];
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
                // ITS LOOK IS ITS NUMBERS, and the client has no numbers -
                // so the shape and the colour have to travel. Sending the KIND
                // was enough when a kind was all a particle was.
                _pKinds[i] = p.ShapeId;
                _pTints[i] = p.WireTint;
                _pLevels[i] = p.WireLevel;
                _pRides[i] = p.RidingId;
                _pPos[i] = p.transform.position;
                _pScale[i] = p.transform.localScale.x;
            }
            InstanceFinder.ServerManager.Broadcast(new ParticleSnap
                { Ids = _pIds, Kinds = _pKinds, Tints = _pTints, Levels = _pLevels,
                  Rides = _pRides, Pos = _pPos, Scale = _pScale }, true, Channel.Unreliable);
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
            h.Body.GetComponentInParent<Golem>()?.BeReleased(); // wakes back up on release (parity)
            if (!h.Body.isKinematic)
            {
                h.Body.linearVelocity = Vector3.ClampMagnitude(h.Body.linearVelocity, 4f);
                h.Body.angularVelocity = Vector3.ClampMagnitude(h.Body.angularVelocity, 4f);
                var sm = h.Body.GetComponent<Matter>();
                if (sm != null && sm.SpellBorn) impulse *= DrawingConfig.SpellThrowMul; // same law as the local hand
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
                if (!h.Mote.Dead && h.Mote.Claimed)
                {
                    // ★ SAME LAW AS THE LOCAL HAND (parity): a throw primes
                    // (detonate on impact, thrower briefly immune) and every
                    // release wakes INSTANTLY - a remote friend's rune must
                    // behave exactly like the host's own
                    h.Mote.ReleaseHeld(impulse);
                    if (impulse.sqrMagnitude > 1f)
                    {
                        Transform thrower = _avatars.TryGetValue(clientId, out var av)
                            && av != null ? av.transform : null;
                        h.Mote.PrimeToBlow(thrower);
                    }
                    h.Mote.Wake();
                }
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
            InstanceFinder.ClientManager.RegisterBroadcast<ZombieSnap>(OnZombieSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<KillFeed>(OnKillFeedClient);
            InstanceFinder.ClientManager.RegisterBroadcast<RoundState>(OnRoundStateClient);
            InstanceFinder.ServerManager.RegisterBroadcast<ReadyMsg>(OnReadyServer);
            InstanceFinder.ClientManager.RegisterBroadcast<LobbyMsg>(OnLobbyClient);
            InstanceFinder.ServerManager.RegisterBroadcast<OutfitMsg>(OnOutfitServer);
            InstanceFinder.ClientManager.RegisterBroadcast<OutfitMsg>(OnOutfitClient);
            InstanceFinder.ServerManager.RegisterBroadcast<VoiceMsg>(OnVoiceServer);
            InstanceFinder.ClientManager.RegisterBroadcast<VoiceMsg>(OnVoiceClient);
            VoiceChat.Touch();
            InstanceFinder.ServerManager.RegisterBroadcast<AbsorbMsg>(OnAbsorbServer);
            InstanceFinder.ClientManager.RegisterBroadcast<AbsorbMsg>(OnAbsorbClient);
            InstanceFinder.ServerManager.RegisterBroadcast<IdentityMsg>(OnIdentityServer);
            InstanceFinder.ClientManager.RegisterBroadcast<IdentityMsg>(OnIdentityClient);
            InstanceFinder.ClientManager.RegisterBroadcast<ReadyCallMsg>(OnReadyCallClient);
            InstanceFinder.ClientManager.RegisterBroadcast<SideAssignMsg>(OnSideAssignClient);
            InstanceFinder.ClientManager.RegisterBroadcast<StandMsg>(OnStandClient);
            InstanceFinder.ServerManager.RegisterBroadcast<SpawnAskMsg>(OnSpawnAskServer);
            InstanceFinder.ClientManager.RegisterBroadcast<SpawnGiveMsg>(OnSpawnGiveClient);
            InstanceFinder.ServerManager.RegisterBroadcast<AbsorbAskMsg>(OnAbsorbAskServer);
            InstanceFinder.ClientManager.RegisterBroadcast<AbsorbGiveMsg>(OnAbsorbGiveClient);
            InstanceFinder.ClientManager.RegisterBroadcast<GolemSnap>(OnGolemSnapClient);
            InstanceFinder.ClientManager.RegisterBroadcast<BiomeMsg>(OnBiomeClient);
            InstanceFinder.ClientManager.RegisterBroadcast<PlayerFxMsg>(OnPlayerFxClient);
            InstanceFinder.ServerManager.RegisterBroadcast<HurtIntent>(OnHurtServer);
            InstanceFinder.ClientManager.RegisterBroadcast<HealthMsg>(OnHealthClient);
            InstanceFinder.ClientManager.RegisterBroadcast<StateMsg>(OnElementStateClient);
            InstanceFinder.ClientManager.RegisterBroadcast<MarkMsg>(OnMarkClient);

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
            InstanceFinder.ClientManager.RegisterBroadcast<BookMsg>(OnBookClient);

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
        /// Host RoundDirector streams round state to clients (2 Hz).
        public static void PushRoundState(byte phase, int round, int left, float timer, int kills, byte ending)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new RoundState
                { Phase = phase, Round = round, Left = left, Timer = timer, Kills = kills, Ending = ending });
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

        /// Ask the host for a spawn point. Safe to call when offline - it just
        /// does nothing, and SpawnPlan picks locally.
        public static void AskSpawn(int owner)
        {
            if (_instance == null || !NetGame.Connected) return;
            InstanceFinder.ClientManager.Broadcast(new SpawnAskMsg { Owner = owner });
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

        /// ★ A HOST-SIDE GRANT FOR A REMOTE OWNER (summon deeds run in host
        /// code): relayed to everyone, so the earner celebrates and every
        /// mirror agrees.
        public static void PushUnlockFor(int owner, int rune)
        {
            if (_instance == null || !NetGame.Connected || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new UnlockMsg { Owner = owner, Card = -1, Rune = rune });
        }

        /// The host's book, to everyone - on join and on every save.
        public static void PushBook()
        {
            if (_instance == null || !NetGame.Connected || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new BookMsg { Json = SpellBook.LiveJson() });
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
                Owner = seal.OwnerId,
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

        void OnVoiceServer(NetworkConnection conn, VoiceMsg msg, Channel channel)
        {
            msg.Owner = OwnerIdOf(conn.ClientId); // the host names the speaker, never the packet
            InstanceFinder.ServerManager.Broadcast(msg, true, Channel.Unreliable);
        }

        void OnVoiceClient(VoiceMsg msg, Channel channel) => VoiceChat.Receive(msg.Owner, msg.Ghost, msg.Data);

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
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                // ★ the joiner plays by the HOST's book, from the first frame
                InstanceFinder.ServerManager.Broadcast(conn,
                    new BookMsg { Json = SpellBook.LiveJson() });
                return;
            }
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
            if (msg.Owner == Grimoire.LocalPlayerId)
            {
                // ★ A DEED THE HOST GRANTED ME (summon deeds run in host
                // code): taken the FULL way - toast, pages, recognition.
                // Self-echoes no-op inside UnlockRune's already-known check.
                if (msg.Rune >= 0 && !Grimoire.HasRune(msg.Owner, (RuneType)msg.Rune))
                    Grimoire.UnlockRune(msg.Owner, (RuneType)msg.Rune);
                return;
            }
            Grimoire.UnlockRemote(msg.Owner, msg.Card, msg.Rune);
        }

        void OnBookClient(BookMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            SpellBook.Adopt(msg.Json);
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
            // parity: a remote friend's golem goes limp in their hands too
            rb.GetComponentInParent<Golem>()?.BeCarried();
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
                    Vector3 scale = msg.Scale != null && i < msg.Scale.Length
                        ? msg.Scale[i] : Vector3.zero;   // zero = fall back to the kind table
                    proxy = NetZombieProxy.Build(id, msg.Pos[i], scale, msg.Kinds[i] == 1);
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

        readonly Dictionary<int, NetGolemProxy> _golems = new Dictionary<int, NetGolemProxy>();

        /// Client side: does a golem raised by this owner stand anywhere right now.
        public static bool AnyGolemOwnedBy(int owner)
        {
            if (_instance == null || owner < 0) return false;
            foreach (var kv in _instance._golems)
                if (kv.Value != null && kv.Value.OwnerId == owner) return true;
            return false;
        }

        void OnGolemSnapClient(GolemSnap msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return; // host has the real ones
            if (msg.Ids == null) return;

            _seen.Clear();
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                int id = msg.Ids[i];
                if (id == 0) continue;   // a zeroed slot is not a golem
                _seen.Add(id);
                if (!_golems.TryGetValue(id, out var proxy) || proxy == null)
                {
                    Vector3 scale = msg.Scale != null && i < msg.Scale.Length
                        ? msg.Scale[i] : Vector3.zero;
                    Color skin = msg.Skin != null && i < msg.Skin.Length
                        ? msg.Skin[i] : Color.gray;
                    proxy = NetGolemProxy.Build(id, msg.Pos[i], scale, skin);
                    if (proxy == null) continue;   // no prefab in the slot
                    _golems[id] = proxy;
                }
                proxy.Target(msg.Pos[i], msg.Yaw[i]);
                proxy.OwnerId = msg.Owner != null && i < msg.Owner.Length ? msg.Owner[i] : -1;
            }

            // gone from the host's list = it came apart; poof the proxy
            _gone.Clear();
            foreach (var kv in _golems)
                if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                if (_golems[id] != null) _golems[id].Vanish();
                _golems.Remove(id);
            }
        }

        /// A client's spell hurt a golem: only the host may actually wound it.
        /// A lvl3 spell opened a biome on the host. Clients open their own copy
        /// so body drift, strength caps and buoyancy agree on every machine -
        /// without this, only the caster was standing in their own spell.
        void OnBiomeClient(BiomeMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            ArtificialBiome.OpenLocal(msg.At, new SpellPayload
            {
                Temp = msg.Temp, Lum = msg.Lum, Pressure = msg.Pressure,
                Balance = msg.Balance, State = msg.State, Affinity = msg.Affinity,
                Strength = msg.Strength, Int = msg.Int,
                Courage = msg.Courage, Clones = msg.Clones,
            }, msg.Radius, msg.Seconds);
        }

        /// A client asked to hurt something. ONLY the host does the arithmetic,
        /// and it answers to everybody at once - that is what keeps the same
        /// tree on the same health on every machine.
        void OnHurtServer(NetworkConnection conn, HurtIntent msg, Channel channel)
        {
            var d = Element.ById(msg.NetId);
            if (d == null) return;              // not a thing we know; drop it
            // WHO asked comes from the CONNECTION, never the packet - the same
            // rule the identity handshake uses, so nobody can frame anybody.
            int by = Element.IdFor("player:" + OwnerIdOf(conn.ClientId));
            // per-hit cap the old zombie channel carried: no one-packet nukes
            d.TakeDamage(Mathf.Min(msg.Amount, DrawingConfig.NetHitCap), "a friend's magic", by);
        }

        void OnMarkClient(MarkMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            Marks.SetLocal(msg.Owner, (Mark)msg.What, msg.Value);
        }

        /// HOST: publish a mark so every machine can answer the same curse.
        public static void PushMark(int owner, Mark what, int value)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new MarkMsg
                { Owner = owner, What = (byte)what, Value = value });
        }

        void OnHealthClient(HealthMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            Element.ById(msg.NetId)?.TakeNetHealth(msg.Health, msg.Max);
        }

        /// CLIENT: ask the host to hurt something. Safe offline - it does
        /// nothing and the caller stays authoritative.
        public static void AskHurt(int netId, float amount)
        {
            if (_instance == null || !NetGame.Connected || NetGame.IsHost) return;
            InstanceFinder.ClientManager.Broadcast(new HurtIntent
                { NetId = netId, Amount = amount });
        }

        /// HOST: publish the truth after it changed something's health.
        static readonly List<int> _stIds = new List<int>();
        static readonly List<short> _stTemp = new List<short>();
        static readonly List<sbyte> _stState = new List<sbyte>();
        static readonly List<sbyte> _stLum = new List<sbyte>();
        static readonly List<sbyte> _stPress = new List<sbyte>();
        static readonly List<sbyte> _stBal = new List<sbyte>();
        static readonly List<sbyte> _stAff = new List<sbyte>();
        static readonly List<sbyte> _stMind = new List<sbyte>();
        static readonly List<sbyte> _stCour = new List<sbyte>();

        /// Called on the host beat. Sends only what has MOVED from its natural,
        /// so a quiet map sends nothing at all and a burning one sends the
        /// things that are burning.
        public static void PushElementState()
        {
            if (!NetGame.IsHost || !NetGame.Connected) return;
            _stIds.Clear(); _stTemp.Clear(); _stState.Clear(); _stLum.Clear();
            _stPress.Clear(); _stBal.Clear(); _stAff.Clear(); _stMind.Clear(); _stCour.Clear();

            sbyte Pack(float v) => (sbyte)Mathf.Clamp(
                Mathf.RoundToInt(v / DrawingConfig.AxisCap * 100f), -100, 100);

            foreach (var e in Element.Live)
            {
                if (e == null) continue;
                var d = e.Data; var n = e.Natural;
                float dT = d.Temp - n.Temp, dS = d.State - n.State, dL = d.Lum - n.Lum;
                float dP = d.Pressure - n.Pressure, dB = d.Balance - n.Balance;
                float dA = d.Affinity - n.Affinity, dM = d.Int - n.Int, dC = d.Courage - n.Courage;
                if (Mathf.Abs(dT) < 4f && Mathf.Abs(dS) < 0.08f && Mathf.Abs(dL) < 0.15f
                    && Mathf.Abs(dP) < 0.08f && Mathf.Abs(dB) < 0.08f && Mathf.Abs(dA) < 0.08f
                    && Mathf.Abs(dM) < 0.08f && Mathf.Abs(dC) < 0.08f) continue;

                _stIds.Add(e.NetId);
                _stTemp.Add((short)Mathf.Clamp(Mathf.RoundToInt(d.Temp), -30000, 30000));
                _stState.Add((sbyte)Mathf.Clamp(Mathf.RoundToInt(d.State * 100f), -100, 100));
                _stLum.Add((sbyte)Mathf.Clamp(Mathf.RoundToInt(d.Lum * 100f), -100, 100));
                _stPress.Add(Pack(d.Pressure));
                _stBal.Add(Pack(d.Balance));
                _stAff.Add(Pack(d.Affinity));
                _stMind.Add(Pack(d.Int));
                _stCour.Add(Pack(d.Courage));
                if (_stIds.Count >= 200) break;   // a snapshot, not a census
            }
            if (_stIds.Count == 0) return;

            InstanceFinder.ServerManager.Broadcast(new StateMsg
            {
                Ids = _stIds.ToArray(), Temp = _stTemp.ToArray(),
                State = _stState.ToArray(), Lum = _stLum.ToArray(),
                Press = _stPress.ToArray(), Bal = _stBal.ToArray(),
                Aff = _stAff.ToArray(), Mind = _stMind.ToArray(),
                Cour = _stCour.ToArray()
            }, true, Channel.Unreliable);
        }

        /// A client APPLIES the host's answer and never computes its own. Its
        /// element beat does not run, so these numbers are the only ones it has.
        void OnElementStateClient(StateMsg msg, Channel channel)
        {
            if (msg.Ids == null) return;
            for (int i = 0; i < msg.Ids.Length; i++)
            {
                var e = Element.ById(msg.Ids[i]);
                if (e == null) continue;
                var d = e.Data;
                d.Temp = msg.Temp[i];
                d.State = msg.State[i] / 100f;
                d.Lum = msg.Lum[i] / 100f;
                // the whole board (older hosts ship without these arrays)
                if (msg.Press != null && i < msg.Press.Length)
                {
                    float k = DrawingConfig.AxisCap / 100f;
                    d.Pressure = msg.Press[i] * k;
                    d.Balance = msg.Bal[i] * k;
                    d.Affinity = msg.Aff[i] * k;
                    d.Int = msg.Mind[i] * k;
                    d.Courage = msg.Cour[i] * k;
                }
                e.Data = d;
                e.ShowState();
            }
        }

        public static void PushHealth(int netId, float health, float max)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new HealthMsg
                { NetId = netId, Health = health, Max = max });
        }

        /// Which player a collider IS - the local one, or the owner behind a
        /// remote puppet. -1 when it is not a body at all.
        public static int OwnerOfBody(Collider c)
        {
            if (c == null) return -1;
            if (c.GetComponentInParent<SimpleFPSController>() != null)
                return Grimoire.LocalPlayerId;
            var av = c.GetComponentInParent<NetAvatar>();
            return av != null ? OwnerIdOf(av.Id) : -1;
        }

        /// HOST: land an effect on a body, wherever that body actually lives.
        /// True means it was shipped to a remote owner, so the caller must NOT
        /// also apply it to the puppet standing here.
        public static bool PushPlayerFx(int owner, byte kind, float amount,
            MatterPhase phase = MatterPhase.Solid, Vector3 point = default)
        {
            if (owner < 0 || owner == Grimoire.LocalPlayerId) return false;
            if (_instance == null || !NetGame.IsHost) return false;
            InstanceFinder.ServerManager.Broadcast(new PlayerFxMsg
            {
                Owner = owner, Kind = kind, Amount = amount,
                Phase = (byte)phase, Point = point,
            });
            return true;
        }

        void OnPlayerFxClient(PlayerFxMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            if (msg.Owner != Grimoire.LocalPlayerId) return;   // not my body

            SimpleFPSController me = null;
            foreach (var pl in SimpleFPSController.All)
                if (pl != null && pl.IsLocalViewer) { me = pl; break; }
            if (me == null) return;

            switch (msg.Kind)
            {
                case 0: me.TakeHit(msg.Point, msg.Amount, "magic"); break;
                case 1:
                    if (!me.IsDowned)
                        me.Health = Mathf.Min(Sides.MaxHealthFor(msg.Owner),
                            me.Health + msg.Amount);
                    break;
                case 2: Sides.AddBuff(msg.Owner, msg.Amount); break;
                case 3: BodyState.Of(me.transform)?.SetPhase((MatterPhase)msg.Phase, msg.Amount); break;
                case 4:
                    FallCatcher.Teleport(me, msg.Point + Vector3.up * 0.3f);
                    Juice.Chime(msg.Point);
                    break;
                case 5: TrailMark.Wear(me.transform, msg.Amount); break;
                case 6:
                    // Point.x carries how visible to become, Amount how long
                    me.GetComponentInChildren<StateView>()?.Fade(msg.Point.x, msg.Amount);
                    break;
            }
        }

        /// HOST: tell everyone a biome just opened.
        public static void PushBiome(Vector3 at, SpellPayload p, float radius, float seconds = 0f)
        {
            if (_instance == null || !NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new BiomeMsg
            {
                At = at, Radius = radius, Seconds = seconds,
                Temp = p.Temp, Lum = p.Lum, Pressure = p.Pressure,
                Balance = p.Balance, State = p.State, Affinity = p.Affinity,
                Strength = p.Strength, Int = p.Int,
                Courage = p.Courage, Clones = p.Clones,
            });
        }

        void OnKillFeedClient(KillFeed msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            PlayerInk.AwardAll(DrawingConfig.InkPerKill); // shared economy, client side
            SealAutopsy.OnKill();
            Powerups.OnKill(); // clients level off shared kills too
        }

        byte _lastPhase = 255;

        void OnRoundStateClient(RoundState msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            HasRound = true;
            NetPhase = msg.Phase;
            NetRound = msg.Round;
            NetLeft = msg.Left;
            NetTimer = msg.Timer;
            NetKills = msg.Kills;
            if (msg.Phase == 2 && _lastPhase != 2)
            {
                string at = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (at != "Lobby" && at != "Menu") Achievements.MatchEnded(msg.Round, (Achievements.Ending)msg.Ending);
            }
            _lastPhase = msg.Phase;

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

        /// Anybody may ask where to stand; the HOST decides, so no two players
        /// are handed the same tile. Its own copy is recorded here too - the
        /// host never hears its own broadcast come back.
        void OnSpawnAskServer(NetworkConnection conn, SpawnAskMsg msg, Channel channel)
        {
            if (!SpawnPlan.IssueFor(msg.Owner, out var at)) return; // asker picks its own
            SpawnPlan.TakeAssigned(msg.Owner, at);
            InstanceFinder.ServerManager.Broadcast(
                new SpawnGiveMsg { Owner = msg.Owner, Point = at });
        }

        void OnSpawnGiveClient(SpawnGiveMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            SpawnPlan.TakeAssigned(msg.Owner, msg.Point);
        }

        /// Route one absorb: offline grants now; the host grants and tells
        /// everyone; a client asks and waits for the give. Only one wizard
        /// wins a mote - a later ask finds the source not Ready and whiffs.
        public static void AbsorbCast(AbsorbSource src, int owner)
        {
            if (src == null) return;
            if (!NetGame.Connected) { src.Grant(owner); return; }
            if (InstanceFinder.ServerManager.Started)
            {
                src.Grant(owner);
                InstanceFinder.ServerManager.Broadcast(
                    new AbsorbGiveMsg { Owner = owner, At = src.transform.position });
            }
            else InstanceFinder.ClientManager.Broadcast(
                new AbsorbAskMsg { Owner = owner, At = src.transform.position });
        }

        void OnAbsorbAskServer(NetworkConnection conn, AbsorbAskMsg msg, Channel channel)
        {
            var src = AbsorbSource.Near(msg.At);
            if (src == null || !src.Ready) return;   // beaten to it
            src.Grant(msg.Owner);
            InstanceFinder.ServerManager.Broadcast(
                new AbsorbGiveMsg { Owner = msg.Owner, At = msg.At });
        }

        void OnAbsorbGiveClient(AbsorbGiveMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            AbsorbSource.Near(msg.At)?.Grant(msg.Owner);
        }

        /// The remote body standing in for this owner, or null.
        public static Transform AvatarTransformOf(int owner)
        {
            if (_instance == null) return null;
            foreach (var kv in _instance._avatars)
                if (OwnerIdOf(kv.Key) == owner && kv.Value != null)
                    return kv.Value.transform;
            return null;
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
            if (msg.Resolved && msg.Owner == Grimoire.LocalPlayerId) Achievements.Unlock(Achievements.FirstSpell);
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
                byte shape = msg.Kinds[i];
                var tint = msg.Tints != null && i < msg.Tints.Length ? msg.Tints[i] : (Color32)Color.white;
                byte level = msg.Levels != null && i < msg.Levels.Length ? msg.Levels[i] : (byte)1;

                if (_moteProxies.TryGetValue(id, out var proxy) && proxy != null && proxy.Shape != shape)
                {
                    Destroy(proxy.gameObject); // it became something else - rebuild the body
                    proxy = null;
                }
                if (proxy == null)
                {
                    proxy = NetMoteProxy.Build(id, shape, tint, msg.Pos[i]);
                    _moteProxies[id] = proxy;
                }
                proxy.Wear(tint, level);
                proxy.Ride(msg.Rides != null && i < msg.Rides.Length ? msg.Rides[i] : 0);
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
            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
            {
                HostGone();
                return;
            }
            if (args.ConnectionState != FishNet.Transporting.LocalConnectionState.Started) return;
            _wasClient = !InstanceFinder.ServerManager.Started;
            if (InstanceFinder.ServerManager.Started) return; // the host trusts itself
            InstanceFinder.ClientManager.Broadcast(new JoinAuthMsg
                { Password = NetGame.JoinPassword ?? "" });
        }

        bool _wasClient;   // we were a guest, not the host running its own client

        /// THE HOST WENT AWAY. Nothing used to catch this: every orphan kept
        /// its dead proxies standing, and `IsAuthority` (!Connected || IsHost)
        /// quietly promoted each of them to referee a match that no longer
        /// existed. Tear the borrowed world down and go home.
        void HostGone()
        {
            if (!_wasClient) return;   // the host stopping its own server is not this
            _wasClient = false;

            foreach (var kv in _proxies) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _proxies.Clear();
            foreach (var kv in _golems) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _golems.Clear();
            foreach (var kv in _avatars) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _avatars.Clear();
            foreach (var kv in _rings) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _rings.Clear();
            HasRound = false;

            DrawingWorld.Instance?.LogEvent("the host left. back to the lobby");
            ComboBanner.Show("THE HOST LEFT", new Color(1f, 0.6f, 0.5f));

            if (ActiveScene.Name == "Lobby") return;   // already home
            LoadEgg.Cover();
            LoadingHints.Show();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
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
            // the SAME body the local player wears - what exists in the game
            // exists for everyone, so a friend is never a different model
            var prefab = CollectionManager.PlayerBody;
            if (prefab != null)
            {
                // no pose sync yet - the prefab's T-pose arms are eased down
                go = new GameObject($"NetPlayer_{id}");
                // makes their body-ink copies Persistent: evaporation-exempt and
                // never consumed by spells, same as on the owner's machine
                go.AddComponent<PersistentInkSurface>();
                var body = Object.Instantiate(prefab, go.transform);
                body.name = "Body";
                body.transform.localPosition = Vector3.zero;   // set from its own height below
                // their grimoire stays fully visible; its page arrows do not
                foreach (var pages in body.GetComponentsInChildren<GrimoirePages>(true))
                    pages.HideForRemote();
                Transform armL = null, armR = null, handL = null, handR = null, head = null;
                Transform footL = null, toeL = null, headTop = null;
                foreach (var t in body.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.EndsWith("LeftArm")) armL = t;
                    else if (t.name.EndsWith("RightArm")) armR = t;
                    else if (t.name.EndsWith("LeftHand")) handL = t;
                    else if (t.name.EndsWith("RightHand")) handR = t;
                    else if (t.name.EndsWith("LeftToeBase")) toeL = t;
                    else if (t.name.EndsWith("LeftFoot")) footL = t;
                    else if (t.name.EndsWith(":Head")) head = t;
                    else if (t.name.Contains("HeadTop")) headTop = t;
                }

                // THE NETWORKED POINT IS MID-BODY, so the model hangs half its
                // own height below it. Measured off the bones the same way the
                // local rig measures - a hardcoded drop fits one body height
                // and sinks or floats every other.
                var crown = headTop != null ? headTop : head;
                if (footL != null && crown != null)
                {
                    float bodyH = Mathf.Clamp(crown.position.y - footL.position.y, 0.8f, 3f);
                    body.transform.localPosition = new Vector3(0f, -bodyH * 0.5f, 0f);
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
                // an authored body brings its OWN eyes; attaching a second
                // pair on top of them is the baked-prefab trap
                var faceEyes = body.GetComponentInChildren<GooglyEyes>(true);
                bool authoredEyes = faceEyes != null;
                if (!authoredEyes)
                    faceEyes = GooglyEyes.Attach(head != null ? head : go.transform,
                        head != null ? 0f : 0.6f, CharacterRig.EyeScale);

                // only a code-built pair gets placed by the shared knobs; a
                // pair that came with the body stays exactly where it was put
                if (!authoredEyes && head != null && faceEyes != null)
                {
                    // one knob for all eyes: CharacterRig.EyeLocalPos
                    faceEyes.transform.localPosition = CharacterRig.EyeLocalPos;
                    faceEyes.transform.localRotation = Quaternion.identity;
                    faceEyes.transform.localScale = Vector3.one * CharacterRig.EyeRigScale;
                }
                faceEyes?.SetVisible(true);
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

            // A REMOTE BODY WEARS THE SAME NAME ITS OWNER GAVE ITSELF, so a
            // spell that hits the puppet reaches the real person. Nothing has
            // to search for a network component: the id IS on the object.
            int owner = NetSync.OwnerIdOf(id);
            var hurtBox = go.GetComponent<Element>();
            if (hurtBox == null) hurtBox = go.AddComponent<Element>();
            hurtBox.Rename(Element.IdFor("player:" + owner));
            hurtBox.RemoveOnDeath = false;   // a puppet is never destroyed locally

            // ON THE HOST THIS IS THE REAL STORE for that player's strength -
            // it does the subtracting and answers with HealthMsg, and the owner
            // mirrors it. So it has to start at their true ceiling, not 100.
            hurtBox.MaxStrength = Sides.MaxHealthFor(owner);
            hurtBox.Health = hurtBox.MaxStrength;

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
            // the stand-in body mirrors the downed gate: a corpse's puppet
            // takes no damage and pays no wand credit on this machine either
            if (((flags ^ _flags) & 1) != 0)
            {
                var standIn = Element.ById(Element.IdFor("player:" + NetSync.OwnerIdOf(Id)));
                if (standIn != null) standIn.DeadStill = (flags & 1) != 0;
            }
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
