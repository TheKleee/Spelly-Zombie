using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Pre-match lobby (Marko's flow): four TEAM COLOR PILLARS stand in a ring
    /// at spawn — walk up and press E to wear that color. Nobody picks?
    /// Everyone is team RED (his decision). ENTER toggles ready; when EVERYONE
    /// is ready the match starts in ≤10 seconds — and a 300-second lobby timer
    /// force-starts it anyway, because somebody will troll. Team hues are
    /// colorblind-safe (Okabe-Ito): vermillion / strong blue / bluish-green /
    /// warm yellow stay distinct under deutan, protan, and tritan vision.
    public class MatchLobby : MonoBehaviour
    {
        public static MatchLobby Instance { get; private set; }

        public const float LobbySeconds = 300f;
        public const float AllReadyStart = 10f;

        /// Local player's team (index into TeamColors). Everyone starts RED.
        public static byte LocalTeam;

        public static readonly string[] TeamNames = { "RED", "BLUE", "GREEN", "YELLOW" };
        public static readonly Color[] TeamColors =
        {
            new Color(0.84f, 0.24f, 0.10f), // vermillion
            new Color(0.00f, 0.45f, 0.70f), // strong blue
            new Color(0.00f, 0.62f, 0.45f), // bluish green
            new Color(0.94f, 0.89f, 0.26f), // warm yellow
        };

        float _lobbyLeft = LobbySeconds;
        float _countdown = -1f; // ticks once everyone is ready
        float _pushTimer;
        bool _readyLocal, _built;
        readonly List<Transform> _pillars = new List<Transform>();
        readonly HashSet<int> _remoteReady = new HashSet<int>();

        // client-side mirror of the host's lobby clocks
        float _netCountdown = -1f, _netLobbyLeft = -1f;
        int _netReady, _netTotal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("MatchLobby");
            Instance = go.AddComponent<MatchLobby>();
            DontDestroyOnLoad(go);
        }

        // --------------------------------------------------- net plumbing --
        public static void SetRemoteReady(int id, bool ready)
        {
            if (Instance == null) return;
            if (ready) Instance._remoteReady.Add(id);
            else Instance._remoteReady.Remove(id);
        }

        public static void NetLobby(byte ready, byte total, float countdown, float left)
        {
            if (Instance == null) return;
            Instance._netReady = ready;
            Instance._netTotal = total;
            Instance._netCountdown = countdown;
            Instance._netLobbyLeft = left;
        }

        // ------------------------------------------------------------ tick --
        void Update()
        {
            if (!RoundDirector.InLobby)
            {
                if (_built) Teardown();
                return;
            }
            if (!_built) Build();

            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            var kb = Keyboard.current;
            bool client = NetGame.Connected && !NetGame.IsHost;

            if (player != null && kb != null && !GameMenu.IsOpen && !PoseStudio.IsOpen)
            {
                // E at a pillar = wear its color
                if (kb.eKey.wasPressedThisFrame)
                    for (int i = 0; i < _pillars.Count; i++)
                        if (_pillars[i] != null && Vector3.Distance(
                                player.transform.position, _pillars[i].position) < 2.2f)
                        {
                            LocalTeam = (byte)i;
                            Juice.Chime(player.transform.position);
                            DrawingWorld.Instance?.LogEvent($"you are TEAM {TeamNames[i]}");
                        }

                // ENTER = ready toggle
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                {
                    _readyLocal = !_readyLocal;
                    if (client) NetSync.SendReady(_readyLocal);
                }
            }

            if (client) return; // the host owns the clocks

            _lobbyLeft -= Time.deltaTime;

            int total = 1 + NetSync.RemoteCount;
            int ready = (_readyLocal ? 1 : 0) + _remoteReady.Count;
            bool all = ready >= total && ready > 0;

            if (all)
            {
                if (_countdown < 0f)
                    _countdown = Mathf.Min(AllReadyStart, Mathf.Max(1f, _lobbyLeft));
                _countdown -= Time.deltaTime;
            }
            else _countdown = -1f;

            if (NetGame.IsHost)
            {
                _pushTimer -= Time.deltaTime;
                if (_pushTimer <= 0f)
                {
                    _pushTimer = 0.5f;
                    NetSync.PushLobby((byte)ready, (byte)total, _countdown, _lobbyLeft);
                }
            }

            if (_lobbyLeft <= 0f || (all && _countdown <= 0f))
                RoundDirector.ForceStart(); // lobby dissolves next frame (InLobby flips)
        }

        // --------------------------------------------------------- pillars --
        void Build()
        {
            _built = true;
            Vector3 anchor = SimpleFPSController.All.Count > 0 && SimpleFPSController.All[0] != null
                ? SimpleFPSController.All[0].transform.position : Vector3.zero;
            anchor.y = 0f;

            for (int i = 0; i < TeamColors.Length; i++)
            {
                float a = (i / (float)TeamColors.Length) * Mathf.PI * 2f + Mathf.PI * 0.25f;
                Vector3 pos = anchor + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4.5f;

                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "TeamPillar_" + TeamNames[i];
                pillar.transform.position = pos + Vector3.up * 0.9f;
                pillar.transform.localScale = new Vector3(0.35f, 0.9f, 0.35f);
                pillar.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Particle(TeamColors[i], MoteShade.Additive, 0.03f, 0.8f);

                var glow = pillar.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = TeamColors[i];
                glow.range = 4.5f;
                glow.intensity = 2.2f;

                _pillars.Add(pillar.transform);
            }
        }

        void Teardown()
        {
            _built = false;
            foreach (var p in _pillars)
                if (p != null) Destroy(p.gameObject);
            _pillars.Clear();
            _remoteReady.Clear();
            _readyLocal = false;
            _lobbyLeft = LobbySeconds;
            _countdown = -1f;
            _netCountdown = _netLobbyLeft = -1f;
        }

        // -------------------------------------------------------------- HUD --
        void OnGUI()
        {
            if (!RoundDirector.InLobby || GameMenu.IsOpen || PoseStudio.IsOpen) return;

            bool client = NetGame.Connected && !NetGame.IsHost;
            float countdown = client ? _netCountdown : _countdown;
            float left = client ? (_netLobbyLeft >= 0f ? _netLobbyLeft : LobbySeconds) : _lobbyLeft;
            int ready = client ? _netReady : (_readyLocal ? 1 : 0) + _remoteReady.Count;
            int total = client ? Mathf.Max(1, _netTotal) : 1 + NetSync.RemoteCount;

            var r = new Rect((Screen.width - 520f) / 2f, 12f, 520f, 86f);
            GUI.Box(r, countdown >= 0f
                ? $"STARTING IN {Mathf.CeilToInt(Mathf.Max(0f, countdown))}…"
                : $"LOBBY — auto-start in {Mathf.CeilToInt(Mathf.Max(0f, left))}s");

            var prev = GUI.color;
            GUI.color = TeamColors[Mathf.Min(LocalTeam, (byte)(TeamColors.Length - 1))];
            GUI.Label(new Rect(r.x + 16f, r.y + 24f, 240f, 20f), $"■ TEAM {TeamNames[LocalTeam]}");
            GUI.color = prev;

            GUI.Label(new Rect(r.x + 16f, r.y + 44f, r.width - 32f, 20f),
                $"READY {ready}/{total} — ENTER to ready up" + (_readyLocal ? "  (you are READY)" : ""));
            GUI.Label(new Rect(r.x + 16f, r.y + 62f, r.width - 32f, 20f),
                "walk to a color pillar + E to pick a team (no pick = RED)");
        }
    }
}
