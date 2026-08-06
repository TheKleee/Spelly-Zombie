using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Pre-match lobby: PURE CO-OP, NO PILLARS (Marko: "remove the team pillars from code"); color = join order, ENTER readies, NO auto-start (his call), host picks the map with M.
    /// (TeamNames/TeamColors/LocalTeam keep their names — they're the player-COLOR api and wire byte now, renaming would churn net + saves.)
    public class MatchLobby : MonoBehaviour
    {
        public static MatchLobby Instance { get; private set; }

        public const float AllReadyStart = 10f;

        /// The scene the ready-up loads — host's pick ("Game" until more maps
        /// exist; any non-Menu/Lobby scene in Build Settings is offered).
        public static string SelectedMap { get; private set; } = "Game";

        /// The map a CLIENT should ride along to when the host starts — the
        /// host's own pick locally, the mirrored net value when joined.
        public static string HostMap =>
            Instance != null && !string.IsNullOrEmpty(Instance._netMap)
                ? Instance._netMap : SelectedMap;

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
            if (_mapCache.Count == 0) _mapCache.Add("Game");
            return _mapCache;
        }

        /// Local player's COLOR (index into TeamColors), assigned by join
        /// order — never picked. Host/offline = RED.
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

        float _countdown = -1f; // ticks once everyone is ready
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

        public static void NetLobby(byte ready, byte total, float countdown, string map)
        {
            if (Instance == null) return;
            Instance._netReady = ready;
            Instance._netTotal = total;
            Instance._netCountdown = countdown;
            Instance._netMap = map;
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

            // COLOR = JOIN ORDER, automatically (no pillars, no picking):
            // host/offline RED; a client locks in by how many players were
            // already here when it arrived
            if (!NetGame.Connected) { LocalTeam = 0; _colorSet = false; }
            else if (NetGame.IsHost) LocalTeam = 0;
            else if (!_colorSet)
            {
                _colorSet = true;
                LocalTeam = (byte)Mathf.Clamp(NetSync.RemoteCount, 1, TeamColors.Length - 1);
            }

            if (player != null && kb != null && !GameMenu.IsOpen && !PoseStudio.IsOpen
                && !UIKit.Typing) // ENTER while typing an IP must not ready you up
            {
                // ENTER = ready toggle
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                {
                    _readyLocal = !_readyLocal;
                    if (client) NetSync.SendReady(_readyLocal);
                }

                // M = the HOST cycles the map (Among Us style — host picks,
                // everyone sees the pick on the board)
                if (!client && kb.mKey.wasPressedThisFrame)
                {
                    var maps = MapList();
                    int i = Mathf.Max(0, maps.IndexOf(SelectedMap));
                    SelectedMap = maps[(i + 1) % maps.Count];
                    Juice.Chime(player.transform.position);
                    DrawingWorld.Instance?.LogEvent($"map: {SelectedMap}");
                }
            }

            if (client) return; // the host owns the clocks

            int total = 1 + NetSync.RemoteCount;
            int ready = (_readyLocal ? 1 : 0) + _remoteReady.Count;
            bool all = ready >= total && ready > 0;

            if (all)
            {
                if (_countdown < 0f) _countdown = AllReadyStart;
                _countdown -= Time.deltaTime;
            }
            else _countdown = -1f;

            if (NetGame.IsHost)
            {
                _pushTimer -= Time.deltaTime;
                if (_pushTimer <= 0f)
                {
                    _pushTimer = 0.5f;
                    NetSync.PushLobby((byte)ready, (byte)total, _countdown, SelectedMap);
                }
            }

            // no auto-start timer — the lobby waits until everyone readies up
            if (all && _countdown <= 0f)
            {
                RoundDirector.ForceStart();
                if (RoundDirector.InLobby) _countdown = -1f; // no map scene yet — retry
            }
        }

        // ------------------------------------------------------ lobby state --
        void Build() => _built = true;

        void Teardown()
        {
            _built = false;
            _remoteReady.Clear();
            _readyLocal = false;
            _countdown = -1f;
            _netCountdown = -1f;
        }

        // -------------------------------------------------------------- HUD --
        RectTransform _ui, _uiBoard;
        UnityEngine.UI.Text _uiStatus, _uiTeam, _uiReady, _uiMap;

        // label caches — rebuild the strings only when a shown value changes, not per frame
        int _shownCountdown = int.MinValue, _shownReady = -1, _shownTotal = -1;
        byte _shownTeam = 255;
        bool _shownReadyLocal, _shownClient;
        string _shownMap;

        void LateUpdate()
        {
            // InLobby (phase Idle) is also true on the MENU screen — the
            // ribbon and ready-board belong to the Lobby SCENE alone
            bool show = RoundDirector.InLobby && !GameMenu.IsOpen && !PoseStudio.IsOpen
                && ActiveScene.Name == "Lobby";
            if (!show)
            {
                // HIDE, never destroy — prefab-adopted UI must survive cycles
                if (_ui != null && _ui.gameObject.activeSelf) _ui.gameObject.SetActive(false);
                if (_uiBoard != null && _uiBoard.gameObject.activeSelf) _uiBoard.gameObject.SetActive(false);
                return;
            }
            if (_ui != null && !_ui.gameObject.activeSelf) _ui.gameObject.SetActive(true);
            if (_uiBoard != null && !_uiBoard.gameObject.activeSelf) _uiBoard.gameObject.SetActive(true);
            if (_ui == null) BuildLobbyUI();

            bool client = NetGame.Connected && !NetGame.IsHost;
            float countdown = client ? _netCountdown : _countdown;
            int ready = client ? _netReady : (_readyLocal ? 1 : 0) + _remoteReady.Count;
            int total = client ? Mathf.Max(1, _netTotal) : 1 + NetSync.RemoteCount;
            string map = client && !string.IsNullOrEmpty(_netMap) ? _netMap : SelectedMap;

            int cd = countdown >= 0f ? Mathf.CeilToInt(Mathf.Max(0f, countdown)) : -1;
            if (cd != _shownCountdown)
            {
                _shownCountdown = cd;
                _uiStatus.text = cd >= 0 ? $"STARTING IN {cd}…" : "READY UP TO START";
            }
            if (LocalTeam != _shownTeam)
            {
                _shownTeam = LocalTeam;
                _uiTeam.text = $"■ {TeamNames[LocalTeam]}"; // your color, not a team
                _uiTeam.color = TeamColors[Mathf.Min(LocalTeam, (byte)(TeamColors.Length - 1))];
            }
            if (ready != _shownReady || total != _shownTotal || _readyLocal != _shownReadyLocal)
            {
                _shownReady = ready; _shownTotal = total; _shownReadyLocal = _readyLocal;
                _uiReady.text = $"READY {ready}/{total}, ENTER to ready up"
                    + (_readyLocal ? "  (you are READY)" : "");
            }
            if (map != _shownMap || client != _shownClient)
            {
                _shownMap = map; _shownClient = client;
                _uiMap.text = client
                    ? $"MAP: {map} (the host picks)"
                    : $"MAP: {map} (M to change)";
            }
        }

        void BuildLobbyUI()
        {
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "LobbyBanner");
            UIKit.Place(_ui, new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(560f, 190f));

            // the curtain ribbon is a ONE-LINE title sprite — headline only,
            // near its native proportions so the border art doesn't smear
            var ribbon = UIKit.Group(_ui, "Ribbon");
            UIKit.Place(ribbon, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(560f, 76f));
            var cloth = UIKit.Panel(ribbon, skin != null ? skin.BannerCurtain : null,
                skin != null ? Color.white : new Color(0f, 0f, 0f, 0.6f));
            UIKit.Stretch((RectTransform)cloth.transform);
            _uiStatus = UIKit.Label(ribbon, "", 22, UIKit.Ink, TextAnchor.MiddleCenter, true);
            var sr = (RectTransform)_uiStatus.transform;
            UIKit.Stretch(sr);
            sr.offsetMin = new Vector2(70f, 16f);  // keep off the tails and trim
            sr.offsetMax = new Vector2(-70f, -14f);

            // the details live on a plain panel in the BOTTOM-RIGHT corner —
            // Marko: "on top of your head is really annoying"
            var board = UIKit.Group(UIKit.Root, "LobbyBoard");
            _uiBoard = board;
            UIKit.Place(board, new Vector2(1f, 0f), new Vector2(-14f, 14f), new Vector2(470f, 128f));
            var back = UIKit.Panel(board, skin != null ? skin.PanelBrown : null,
                skin != null ? Color.white : new Color(0.22f, 0.17f, 0.12f, 0.92f));
            UIKit.Stretch((RectTransform)back.transform);

            _uiTeam = UIKit.Label(board, "", 16, Color.white, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)_uiTeam.transform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(430f, 20f));
            _uiReady = UIKit.Label(board, "", 16, UIKit.Ink, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)_uiReady.transform, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(430f, 20f));
            _uiMap = UIKit.Label(board, "", 16, UIKit.Ink, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)_uiMap.transform, new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(430f, 20f));

            // fresh labels start empty — invalidate the caches so they repopulate
            _shownCountdown = int.MinValue; _shownTeam = 255;
            _shownReady = -1; _shownTotal = -1; _shownMap = null;
        }

        void OnDestroy()
        {
            if (_ui != null) Destroy(_ui.gameObject);
            if (_uiBoard != null) Destroy(_uiBoard.gameObject);
        }
    }
}
