using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Pre-match lobby: color = join order, ready toggle, host picks the map.
    /// (TeamNames/TeamColors/LocalTeam are the player-color api and net wire byte; renaming churns net + saves.)
    public class MatchLobby : MonoBehaviour
    {
        public static MatchLobby Instance { get; private set; }

        /// Host-set match settings, mirrored to clients through LobbyMsg.
        public static int Seed;
        public static int AcolytePercent = 50; // minimum share, 10..90
        /// Match length in minutes. ZERO = NO TIMER: the same rules, played
        /// for as long as everyone wants, and the clock can never be the thing
        /// that decides a winner. Same as the lobby, but it is a real match.
        public static int DurationMin = 10;
        public static bool Endless => DurationMin <= 0;

        /// What the bookstand shows. A bare dash for no timer: nobody has to
        /// read the word, and it means the same thing in every language a
        /// player might be reading in - the timer is simply not there.
        public static string DurationLabel => Endless ? "-" : DurationMin.ToString();

        /// True while a ready call is on screen (B = ready, C = not ready).
        public static bool CallActive => Instance != null && Time.unscaledTime < Instance._callUntil;
        float _callUntil = -1f;

        /// The scene the ready-up loads — the host's pick. Any non-Menu/Lobby
        /// scene in Build Settings is offered.
        public static string SelectedMap { get; private set; } = "Spelly Island";

        /// The map a CLIENT should ride along to when the host starts - the
        /// host's own pick locally, the mirrored net value when joined.
        public static string HostMap =>
            Instance != null && !string.IsNullOrEmpty(Instance._netMap)
                ? Instance._netMap : SelectedMap;

        /// The game mode; only Wizards vs Acolytes exists today.
        public static string SelectedMode { get; private set; } = "Acolytes";

        /// Host cycles the map; public so the book stand menu reuses this action.
        public static void CycleMap() => CycleMap(1);

        public static void CycleMap(int dir)
        {
            if (NetGame.Connected && !NetGame.IsHost) return; // host-only pick
            var maps = MapList();
            int i = Mathf.Max(0, maps.IndexOf(SelectedMap));
            SelectedMap = maps[(i + dir + maps.Count) % maps.Count];
            DrawingWorld.Instance?.LogEvent($"map: {SelectedMap}");
        }

        static List<string> _mapCache;

        static List<string> MapList()
        {
            if (_mapCache != null) return _mapCache;
            _mapCache = new List<string>();
            int n = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < n; i++)
            {
                string path = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (sceneName != "Menu" && sceneName != "Lobby") _mapCache.Add(sceneName);
            }
            if (_mapCache.Count == 0) _mapCache.Add("Spelly Island");
            return _mapCache;
        }

        /// Local player's COLOR (index into TeamColors), assigned by join
        /// order - never picked. Host/offline = RED.
        public static byte LocalTeam;
        bool _colorSet; // client color locks once, at connect

        public static readonly string[] TeamNames = { "RED", "BLUE", "GREEN", "YELLOW" };
        public static readonly Color[] TeamColors =
        {
            new Color(0.84f, 0.24f, 0.10f), // vermillion
            new Color(0.00f, 0.45f, 0.70f), // strong blue
            new Color(0.00f, 0.62f, 0.45f), // bluish green
            new Color(0.94f, 0.89f, 0.26f), // warm yellow
        };

        float _pushTimer;
        bool _readyLocal, _built;
        readonly HashSet<int> _remoteReady = new HashSet<int>();

        // client-side mirror of the host's lobby state
        float _netCountdown = -1f;
        int _netReady, _netTotal;
        string _netMap;

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

        public static void NetLobby(byte ready, byte total, float countdown, string map,
            int seed, byte acolytePct, byte durationMin)
        {
            if (Instance == null) return;
            Instance._netReady = ready;
            Instance._netTotal = total;
            Instance._netCountdown = countdown;
            Instance._netMap = map;
            Seed = seed;
            if (acolytePct >= 10) AcolytePercent = acolytePct;
            // 255 is "the host did not say"; 0 is a REAL choice now - no
            // timer - so it must not be swallowed as an unset value or a host
            // picking endless would leave every client on their own old length
            if (durationMin != 255) DurationMin = durationMin;
        }

        /// The host asked everyone to ready up.
        public static void OnReadyCall()
        {
            if (Instance != null) Instance._callUntil = Time.unscaledTime + 20f;
        }

        public static bool IsReady(int clientId) =>
            Instance != null && Instance._remoteReady.Contains(clientId);

        public static bool LocalReady => Instance != null && Instance._readyLocal;

        /// Start gate: everyone ready and at least 2 players.
        public static bool CanStart
        {
            get
            {
                if (Instance == null || !NetGame.Connected) return false;
                int total = 1 + NetSync.RemoteCount;
                int ready = (Instance._readyLocal ? 1 : 0) + Instance._remoteReady.Count;
                return total >= 2 && ready >= total;
            }
        }

        /// Host (or solo, vs the bot) starts the match. Fills the acolyte
        /// quota first: pillar volunteers keep their side, the rest is drawn
        /// at random, always at least 1 acolyte and 1 wizard.
        public static void StartMatch()
        {
            if (NetGame.Connected)
            {
                if (!NetGame.IsHost || !CanStart) return;
                AssignSides();
                SteamLobby.SetInGame(true); // browser rows show "game in progress"
            }
            else
            {
                BotPlayer.QueueForNextMatch();
            }
            RoundDirector.ForceStart();
        }

        static void AssignSides()
        {
            var owners = new List<int> { NetSync.LocalOwnerId >= 0 ? NetSync.LocalOwnerId : Grimoire.LocalPlayerId };
            foreach (var id in NetSync.RemoteIds) owners.Add(NetSync.OwnerIdOf(id));

            int total = owners.Count;
            int want = Mathf.Clamp(Mathf.RoundToInt(total * AcolytePercent / 100f), 1, total - 1);

            var acolytes = new List<int>();
            foreach (var o in owners) if (Sides.IsAcolyte(o)) acolytes.Add(o);

            // volunteers stay (the share is a minimum), but never ALL acolytes
            while (acolytes.Count >= total && acolytes.Count > 1)
                acolytes.RemoveAt(Random.Range(0, acolytes.Count));

            var pool = new List<int>();
            foreach (var o in owners) if (!acolytes.Contains(o)) pool.Add(o);
            while (acolytes.Count < want && pool.Count > 1)
            {
                int pick = Random.Range(0, pool.Count);
                acolytes.Add(pool[pick]);
                pool.RemoveAt(pick);
            }

            ApplySideAssign(acolytes.ToArray());
            NetSync.PushSideAssign(acolytes.ToArray());
        }

        public static void ApplySideAssign(int[] acolyteOwners)
        {
            var set = new HashSet<int>(acolyteOwners);
            int me = Grimoire.LocalPlayerId;
            Sides.Set(me, set.Contains(me) ? Side.Acolyte : Side.Wizard);
            foreach (var id in NetSync.RemoteIds)
            {
                int o = NetSync.OwnerIdOf(id);
                Sides.Set(o, set.Contains(o) ? Side.Acolyte : Side.Wizard);
            }
        }

        // ------------------------------------------------------------ tick --
        void Update()
        {
            // the match lobby lives ONLY in the Lobby scene (not menu, not game)
            if (!RoundDirector.InLobby || ActiveScene.Name != "Lobby")
            {
                if (_built) Teardown();
                return;
            }
            if (!_built) Build();

            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            var kb = Keyboard.current;
            bool client = NetGame.Connected && !NetGame.IsHost;

            // color = join order: host/offline RED; a client locks in by
            // how many players were already here when it arrived
            if (!NetGame.Connected) { LocalTeam = 0; _colorSet = false; }
            else if (NetGame.IsHost) LocalTeam = 0;
            else if (!_colorSet)
            {
                _colorSet = true;
                LocalTeam = (byte)Mathf.Clamp(NetSync.RemoteCount, 1, TeamColors.Length - 1);
            }

            if (player != null && kb != null && !GameMenu.IsOpen && !PoseStudio.IsOpen
                && !UIKit.Typing)
            {
                // B toggles ready, hosted lobbies only (alone = sandbox).
                // During a ready call C answers "not ready".
                if (NetGame.Connected)
                {
                    if (kb.bKey.wasPressedThisFrame)
                    {
                        _readyLocal = !_readyLocal;
                        if (client) NetSync.SendReady(_readyLocal);
                        Juice.Chime(player.transform.position);
                    }
                    if (CallActive && kb.cKey.wasPressedThisFrame)
                    {
                        _readyLocal = false;
                        if (client) NetSync.SendReady(false);
                        _callUntil = -1f;
                    }
                    if (CallActive && _readyLocal) _callUntil = -1f; // answered
                }

            }

            if (CallActive)
                UIPrompt.Show("B", Loc.T("lobby.readycall"), new Color(0.6f, 1f, 0.65f));

            if (client) return;

            if (NetGame.IsHost)
            {
                _pushTimer -= Time.deltaTime;
                if (_pushTimer <= 0f)
                {
                    _pushTimer = 0.5f;
                    int total = 1 + NetSync.RemoteCount;
                    int ready = (_readyLocal ? 1 : 0) + _remoteReady.Count;
                    NetSync.PushLobby((byte)ready, (byte)total, -1f, SelectedMap,
                        Seed, (byte)AcolytePercent, (byte)DurationMin);
                }
            }
            // no clocks: the host presses START at the book stand
        }

        // ------------------------------------------------------ lobby state --
        void Build()
        {
            _built = true;
            SteamLobby.SetInGame(false); // back in the lobby: joinable again

            // roll the match seed from the DAILY POOL - authority only;
            // clients mirror the host's roll through PushLobby (no clock skew)
            if (!NetGame.Connected || NetGame.IsHost) Seed = DailyRoll();
        }

        /// Pool base = the UTC date hashed, one of DailySeedPool rolls on top.
        /// The date includes the year, so pools never repeat annually.
        static int DailyRoll()
        {
            var d = System.DateTime.UtcNow;
            int day = d.Year * 10000 + d.Month * 100 + d.Day;
            unchecked
            {
                int poolBase = (int)((uint)day * 2654435761u) ^ 0x5A113E;
                return poolBase + Random.Range(0, Mathf.Max(1, (int)DrawingConfig.DailySeedPool));
            }
        }

        void Teardown()
        {
            _built = false;
            _remoteReady.Clear();
            _readyLocal = false;
            _netCountdown = -1f;
        }

        // -------------------------------------------------------------- HUD --
        RectTransform _ui, _uiBoard;
        UnityEngine.UI.Text _uiReady;

        // label caches - rebuild the strings only when a shown value changes, not per frame
        int _shownReady = -1, _shownTotal = -1;
        bool _shownReadyLocal;

        void LateUpdate()
        {
            // InLobby (phase Idle) is also true on the MENU screen - the
            // ribbon and ready-board belong to the Lobby SCENE alone
            bool show = RoundDirector.InLobby && !GameMenu.IsOpen && !PoseStudio.IsOpen
                && ActiveScene.Name == "Lobby";
            if (!show)
            {
                // HIDE, never destroy - prefab-adopted UI must survive cycles
                if (_ui != null && _ui.gameObject.activeSelf) _ui.gameObject.SetActive(false);
                if (_uiBoard != null && _uiBoard.gameObject.activeSelf) _uiBoard.gameObject.SetActive(false);
                return;
            }
            if (_ui != null && !_ui.gameObject.activeSelf) _ui.gameObject.SetActive(true);
            if (_uiBoard != null && !_uiBoard.gameObject.activeSelf) _uiBoard.gameObject.SetActive(true);
            if (_ui == null) BuildLobbyUI();

            bool client = NetGame.Connected && !NetGame.IsHost;
            int ready = client ? _netReady : (_readyLocal ? 1 : 0) + _remoteReady.Count;
            int total = client ? Mathf.Max(1, _netTotal) : 1 + NetSync.RemoteCount;

            if (ready != _shownReady || total != _shownTotal || _readyLocal != _shownReadyLocal)
            {
                _shownReady = ready; _shownTotal = total; _shownReadyLocal = _readyLocal;
                // the ready marker exists only in a hosted lobby; alone this
                // is a sandbox and stays quiet
                _uiReady.text = !NetGame.Connected ? ""
                    : _readyLocal
                        ? Loc.F("lobby.ready.on", ready, total)
                        : Loc.F("lobby.ready.off", ready, total);
            }
            // (no map line: the book stand is the one map picker)
        }

        // floating lines bottom-right, no ribbon and no panel box
        void BuildLobbyUI()
        {
            var board = UIKit.Group(UIKit.Root, "LobbyBoard");
            _ui = board;
            _uiBoard = board;
            UIKit.Place(board, new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(470f, 100f));

            _uiReady = UIKit.Label(board, "", 18, UIKit.Parchment, TextAnchor.MiddleRight, true);
            UIKit.Place((RectTransform)_uiReady.transform, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(430f, 22f));

            _shownReady = -1; _shownTotal = -1;
        }

        void OnDestroy()
        {
            if (_ui != null) Destroy(_ui.gameObject);
            if (_uiBoard != null) Destroy(_uiBoard.gameObject);
        }
    }
}
