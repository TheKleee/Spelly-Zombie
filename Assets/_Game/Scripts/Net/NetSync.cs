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
            public byte Flags; // 1 = downed, 2 = sprawled
            public float Health;
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
        float _sendTimer, _zombieTimer;
        bool _registered;

        void Awake() => _instance = this;

        void Update()
        {
            if (InstanceFinder.NetworkManager == null) return;
            RegisterOnce();

            if (!NetGame.Connected) return;

            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer <= 0f)
            {
                _sendTimer = 0.1f; // 10 Hz presence
                SendLocalState();
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
            if (player.IsSprawled) flags |= 2;

            InstanceFinder.ClientManager.Broadcast(new PlayerState
            {
                Id = LocalId,
                Pos = player.transform.position,
                Yaw = player.transform.eulerAngles.y,
                Flags = flags,
                Health = player.Health
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

        void OnRemoteConnection(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            RemoveAvatar(conn.ClientId);
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
            avatar.Target(msg.Pos, msg.Yaw, msg.Flags, msg.Health);
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
        float _targetYaw;
        byte _flags;

        public bool Downed => (_flags & 1) != 0;

        public static NetAvatar Build(int id)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"NetPlayer_{id}";
            go.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            go.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.35f, 0.55f, 0.9f), MoteShade.Opaque); // friendly blue
            Object.Destroy(go.GetComponent<Collider>()); // presence, not physics (yet)

            var eyes = GooglyEyes.Attach(go.transform, 0.6f, 1.4f);
            eyes.SetVisible(true);

            var a = go.AddComponent<NetAvatar>();
            a._targetPos = go.transform.position;
            return a;
        }

        public void Target(Vector3 pos, float yaw, byte flags, float health)
        {
            _targetPos = pos;
            _targetYaw = yaw;
            _flags = flags;
        }

        void Update()
        {
            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * 12f);
            float roll = (_flags & 1) != 0 ? 80f : (_flags & 2) != 0 ? 70f : 0f;
            var target = Quaternion.Euler(0f, _targetYaw, roll);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * 10f);
        }
    }
}
