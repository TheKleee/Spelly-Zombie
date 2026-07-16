using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

namespace SpellyZombie
{
    /// Phase B2/B3 first pass, broadcast-based (no NetworkObject prefabs
    /// needed): players see each other as live bean avatars, and every stroke
    /// drawn on world geometry replicates — each client's own simulation then
    /// detects seals and casts. Visual/physics parity is approximate; true
    /// host-authoritative zombies/rounds are the next milestone (B4).
    public class NetSync : MonoBehaviour
    {
        // ------------------------------------------------------------ types --
        public struct PlayerState : IBroadcast
        {
            public int Id;
            public Vector3 Pos;
            public float Yaw;
            public float Pitch; // look pitch (+down) — remote heads follow it
            public byte Flags; // 1 = downed, 2 = sprawled
            public float Health;
            public byte Team;  // MatchLobby team color index
        }

        public struct ReadyMsg : IBroadcast // client → host: lobby ready toggle
        {
            public bool Ready;
        }

        public struct LobbyMsg : IBroadcast // host → clients: lobby state
        {
            public byte Ready;
            public byte Total;
            public float Countdown;
            public string Map; // the host's pick — everyone sees it
        }

        public struct StrokeMsg : IBroadcast
        {
            public int Owner;
            public string SurfacePath;
            public Vector3 Normal;
            public Vector3[] Points;
            public int DeclaredRune;
        }

        public struct PlayerLeft : IBroadcast
        {
            public int Id;
        }

        public struct OutfitMsg : IBroadcast // any → host → all: outfit choices
        {
            public int Id;
            public string Code; // SocketManager wire format ("2,0,1"), slot order = catalog order
        }

        // ---- B4: host-authoritative zombies & rounds ----
        public struct ZombieSnap : IBroadcast // host → clients, 10 Hz unreliable
        {
            public int[] Ids;
            public Vector3[] Pos;
            public float[] Yaw;
            public byte[] Kinds;
        }

        public struct ZombieHit : IBroadcast // client → host: my magic hurt your zombie
        {
            public int Id;
            public float Amount;
        }

        public struct KillFeed : IBroadcast // host → clients: shared ink for the kill
        {
            public Vector3 Pos;
        }

        public struct RoundState : IBroadcast // host → clients, 2 Hz
        {
            public byte Phase; // RoundDirector.Phase ordinal
            public int Round;
            public int Left;
            public float Timer;
            public int Kills;
        }

        // ------------------------------------------------------------ state --
        public static int RemoteCount => _instance != null ? _instance._avatars.Count : 0;

        /// True while a received stroke is being rebuilt — suppresses re-send.
        public static bool ApplyingRemote { get; private set; }

        /// Latest round state received from the host (client HUD reads these).
        public static bool HasRound;
        public static byte NetPhase;
        public static int NetRound, NetLeft, NetKills;
        public static float NetTimer;

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
        float _sendTimer, _zombieTimer;
        bool _registered;
        bool _outfitSent;

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
                return;
            }

            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = 0.1f; // 10 Hz presence
                SendLocalState();
            }

            // the outfit is static — announce it ONCE per connection (the
            // lobby outfit picker re-announces via PushLocalOutfit later)
            if (!_outfitSent)
            {
                _outfitSent = true;
                PushLocalOutfit();
            }

            // B4: the host streams its zombies to everyone (10 Hz, unreliable —
            // an empty snapshot is meaningful too: it clears dead proxies)
            if (NetGame.IsHost)
            {
                _zombieTimer -= Time.unscaledDeltaTime;
                if (_zombieTimer <= 0f)
                {
                    _zombieTimer = 0.1f;
                    SendZombieSnap();
                }
            }
        }

        void SendZombieSnap()
        {
            int n = Zombie.All.Count;
            var snap = new ZombieSnap
            {
                Ids = new int[n],
                Pos = new Vector3[n],
                Yaw = new float[n],
                Kinds = new byte[n]
            };
            for (int i = 0; i < n; i++)
            {
                var z = Zombie.All[i];
                if (z == null) continue;
                snap.Ids[i] = z.gameObject.GetInstanceID();
                snap.Pos[i] = z.transform.position;
                snap.Yaw[i] = z.transform.eulerAngles.y;
                snap.Kinds[i] = (byte)z.Kind;
            }
            InstanceFinder.ServerManager.Broadcast(snap, true, Channel.Unreliable);
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
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnection;
        }

        // ---------------------------------------------------------- outgoing --
        int LocalId => InstanceFinder.ClientManager.Connection != null
            ? InstanceFinder.ClientManager.Connection.ClientId : -1;

        void SendLocalState()
        {
            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player == null || LocalId < 0) return;

            byte flags = 0;
            if (player.IsDowned) flags |= 1;
            if (player.IsSprawled || player.IsAirTumbling) flags |= 2; // both read as "helpless ragdoll" remotely

            InstanceFinder.ClientManager.Broadcast(new PlayerState
            {
                Id = LocalId,
                Pos = player.transform.position,
                Yaw = player.transform.eulerAngles.y,
                Pitch = player.LookPitch,
                Flags = flags,
                Health = player.Health,
                Team = MatchLobby.LocalTeam
            }, Channel.Unreliable);
        }

        /// DrawingWorld calls this whenever a local stroke finishes — replicate
        /// it if it lives on world geometry (dynamic surfaces come with B4).
        public static void OnLocalStrokeFinished(Stroke s)
        {
            if (_instance == null || !NetGame.Connected || ApplyingRemote) return;
            if (s == null || !s.Alive || s.OwnerId != Grimoire.LocalPlayerId) return;
            if (s.Surface == null) return;
            if (s.Surface.GetComponentInParent<Creature>() != null) return;       // dynamic: later
            if (s.Surface.GetComponentInParent<Rigidbody>() != null) return;      // dynamic: later

            var pts = new List<Vector3>();
            Vector3 normal = Vector3.up;
            foreach (var n in s.Nodes)
            {
                if (n == null) continue;
                pts.Add(n.transform.position);
                normal = n.SurfaceNormal;
            }
            if (pts.Count < 2) return;

            InstanceFinder.ClientManager.Broadcast(new StrokeMsg
            {
                Owner = s.OwnerId,
                SurfacePath = FullPath(s.Surface),
                Normal = normal,
                Points = pts.ToArray(),
                DeclaredRune = (int)s.DeclaredRune
            });
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

        // ------------------------------------------- B4 outgoing helpers --
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
        public static void PushLobby(byte ready, byte total, float countdown, string map)
        {
            if (!NetGame.IsHost) return;
            InstanceFinder.ServerManager.Broadcast(new LobbyMsg
                { Ready = ready, Total = total, Countdown = countdown, Map = map });
        }

        // ------------------------------------------------- server relaying --
        void OnZombieHitServer(NetworkConnection conn, ZombieHit msg, Channel channel)
        {
            // find the real zombie this client's magic touched and hurt it
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
            MatchLobby.SetRemoteReady(conn.ClientId, false); // leavers aren't ready
            InstanceFinder.ServerManager.Broadcast(new PlayerLeft { Id = conn.ClientId });
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

            // whoever the host stopped listing is DEAD — poof the proxy
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

            // SCENE-FOLLOW: the host started the run (1 = Wave, 2 =
            // Intermission) — clients still standing in the lobby ride along
            // to the host's map. Only the lobby auto-leaves; nothing yanks a
            // player who is already somewhere else.
            if ((msg.Phase == 1 || msg.Phase == 2)
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Lobby")
            {
                string map = MatchLobby.HostMap;
                if (!string.IsNullOrEmpty(map) && Application.CanStreamedLevelBeLoaded(map))
                    UnityEngine.SceneManagement.SceneManager.LoadScene(map);
            }
        }

        void OnLobbyClient(LobbyMsg msg, Channel channel)
        {
            if (InstanceFinder.ServerManager.Started) return;
            MatchLobby.NetLobby(msg.Ready, msg.Total, msg.Countdown, msg.Map);
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
            avatar.Target(msg.Pos, msg.Yaw, msg.Flags, msg.Health, msg.Team, msg.Pitch);
        }

        void ApplyStroke(StrokeMsg msg)
        {
            if (msg.Owner == Grimoire.LocalPlayerId || DrawingWorld.Instance == null) return;

            Transform surface = null;
            var go = GameObject.Find(msg.SurfacePath);
            if (go != null) surface = go.transform;
            if (surface == null) return; // scene mismatch — skip quietly

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
                    DeclaredRune = (RuneType)msg.DeclaredRune
                };
                DrawingWorld.Instance.Register(s);
                for (int i = 0; i < msg.Points.Length; i++)
                    s.AddNode(DrawNode.Create(s, i, msg.Points[i], msg.Normal, surface));
                DrawingWorld.Instance.CompleteStroke(s);
                DrawingWorld.Instance.RequestDetect(); // their circle seals here too
            }
            finally
            {
                ApplyingRemote = false;
            }
        }

        void RemoveAvatar(int id)
        {
            if (_avatars.TryGetValue(id, out var avatar) && avatar != null)
                Destroy(avatar.gameObject);
            _avatars.Remove(id);
        }
    }

    /// A remote friend: bean, googly eyes, smooth-lerped presence. Keels over
    /// when their flags say downed/sprawled — your buddy's disasters are
    /// visible from across the map.
    public class NetAvatar : MonoBehaviour
    {
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
                // friends wear the real wizard (T-pose arms eased down until
                // pose sync lands in B4 — a gliding T-pose is a bit too cursed)
                go = new GameObject($"NetPlayer_{id}");
                var body = Object.Instantiate(prefab, go.transform);
                body.name = "Body";
                body.transform.localPosition = new Vector3(0f, -0.9f, 0f); // avatar anchor is mid-body
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
                // friends wear the team outfit too (retinted in Target) —
                // dressed with THEIR announced outfit, not ours
                sockets = SocketSet.Build(body, go.transform);
                costume = Wardrobe.DressPlayer(
                    sockets, new Color(0.35f, 0.55f, 0.9f), null,
                    outfitCode: NetSync.OutfitOf(id));
                var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null)
                {
                    // the body stays SKIN — the hat/cloak carry the team color
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
            a._targetPos = go.transform.position;
            a._costume = costume;
            a._sockets = sockets;
            return a;
        }

        System.Collections.Generic.List<GameObject> _costume;
        SocketSet _sockets;

        /// A (re)announced outfit arrived after this avatar was built —
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

        public void Target(Vector3 pos, float yaw, byte flags, float health, byte team,
            float pitch = 0f)
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
                // the OUTFIT carries the team color (the body stays skin) —
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
            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * 12f);
            float roll = (_flags & 1) != 0 ? 80f : (_flags & 2) != 0 ? 70f : 0f;
            var target = Quaternion.Euler(0f, _targetYaw, roll);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 10f);

            // their head follows their aim — you can SEE what a friend is
            // studying from across the yard (rebuilt from base every frame)
            if (_head != null)
            {
                _head.localRotation = _headBase;
                _head.rotation = Quaternion.AngleAxis(_targetPitch * 0.8f, transform.right)
                    * _head.rotation;
            }
        }
    }
}
