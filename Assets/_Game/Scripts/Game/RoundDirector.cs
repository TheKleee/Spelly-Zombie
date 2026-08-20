using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// The Wizards vs Acolytes referee: holds the match clock, watches the
    /// pot and both rosters, calls the win by the five endings, and carries
    /// everyone back to the lobby. Self-bootstraps into any scene.
    public class RoundDirector : MonoBehaviour
    {
        enum Phase { Idle, Live, Over }

        public static RoundDirector Instance { get; private set; }

        /// True while a match runs - gates ink costs and real player downs.
        public static bool RunActive => Instance != null && Instance._phase == Phase.Live;

        /// True before a match starts - the MatchLobby lives here.
        public static bool InLobby => Instance != null && Instance._phase == Phase.Idle;

        /// True while the match runs - the music director crossfades on this.
        public static bool WaveActive => RunActive;

        /// MatchLobby's start trigger.
        public static void ForceStart()
        {
            if (Instance == null || Instance._phase != Phase.Idle) return;
            if (NetGame.Connected && !NetGame.IsHost) return; // host decides
            Instance.StartRun();
        }

        Phase _phase = Phase.Idle;
        float _clock;        // seconds left on the match timer
        int _kills;
        int _winner;         // 0 none, 1 wizards, 2 acolytes
        float _overTimer;
        bool _hadWizards, _hadAcolytes;
        float _runStart;

        readonly List<SimpleFPSController> _players = new List<SimpleFPSController>();
        float _playerScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("RoundDirector");
            Instance = go.AddComponent<RoundDirector>();
            DontDestroyOnLoad(go);
        }

        // ------------------------------------------------------------ events --
        /// Zombie.OnDeath calls this: kills award ink.
        public static void NotifyKill(Zombie z)
        {
            if (Instance == null || !RunActive) return;
            Instance._kills++;
            PlayerInk.AwardAll(DrawingConfig.InkPerKill);
            SealAutopsy.OnKill(); // kill bursts near a seal trigger the replay
            Powerups.OnKill();    // kills feed the level-up track
            if (z != null) NetSync.PushKill(z.transform.position); // clients share the ink
        }

        // ------------------------------------------------------------- flow --
        float _netPush;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || PoseStudio.IsOpen || GameMenu.IsOpen) return;

            RefreshPlayers();

            // only the HOST referees - clients read match state off the wire
            if (NetGame.Connected && !NetGame.IsHost) return;

            if (NetGame.IsHost)
            {
                _netPush -= Time.deltaTime;
                if (_netPush <= 0f)
                {
                    _netPush = 0.5f;
                    NetSync.PushRoundState((byte)_phase, _winner, 0, _clock, _kills);
                }
            }

            switch (_phase)
            {
                case Phase.Idle:
                    if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
                        StartRun();
                    break;

                case Phase.Live:
                    TickMatch();
                    break;

                case Phase.Over:
                    _overTimer -= Time.deltaTime;
                    if (_overTimer <= 0f
                        || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                        ReturnToLobby();
                    break;
            }
        }

        const string LobbySceneName = "Lobby";
        static string GameSceneName => MatchLobby.SelectedMap; // the host's lobby pick
        static bool _startOnLoad;

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // back in the lobby: clear stale end-screen state
            if (scene.name == LobbySceneName && _phase == Phase.Over)
            {
                _phase = Phase.Idle;
                _winner = 0;
            }

            if (!_startOnLoad || scene.name != GameSceneName) return;
            _startOnLoad = false;
            BeginLive();
        }

        /// Matches load the game map and begin on arrival.
        void StartRun()
        {
            if (SceneManager.GetActiveScene().name == "Menu") return; // no match from the menu
            if (SceneManager.GetActiveScene().name == LobbySceneName)
            {
                if (Application.CanStreamedLevelBeLoaded(GameSceneName))
                {
                    _startOnLoad = true;
                    LoadEgg.Cover();     // ghosts revive, everyone travels dark
                    LoadingHints.Show(); // one random tip rides every load
                    SceneManager.LoadScene(GameSceneName);
                    return;
                }
                ComboBanner.Show("THE LOBBY IS SAFE GROUND", new Color(0.7f, 0.9f, 1f));
                DrawingWorld.Instance?.LogEvent(
                    "matches happen on the game map. no 'Game' scene in Build Settings yet");
                return;
            }
            BeginLive();
        }

        void BeginLive()
        {
            _kills = 0;
            _winner = 0;
            _runStart = Time.time;
            _clock = Mathf.Clamp(MatchLobby.DurationMin, 5, 15) * 60f;
            PlayerInk.RefillAll();
            Powerups.ResetRun(); // fresh build every match
            SealGallery.Clear();
            RuneLibrary.ForgetRoundLearning();

            // elimination endings only count sides that actually fielded
            // someone - a solo practice match runs on the clock alone
            _hadWizards = AnySide(Side.Wizard, aliveOnly: false);
            _hadAcolytes = AnySide(Side.Acolyte, aliveOnly: false);

            _phase = Phase.Live;
            ComboBanner.Show("WIZARDS vs ACOLYTES", new Color(1f, 0.85f, 0.4f));
            var p0 = _players.Count > 0 && _players[0] != null ? _players[0].transform.position : Vector3.zero;
            Juice.Drum(p0);
            DrawingWorld.Instance?.LogEvent($"the match is on. {Mathf.RoundToInt(_clock / 60f)} minutes");
        }

        /// The five endings, in GAME_MODE_ACOLYTES WINNING order:
        /// 1 pot empty -> acolytes, instantly, always
        /// 2 all wizards dead -> acolytes
        /// 3 timer out on a green pot -> acolytes, even from the grave
        /// 4 timer out on a clean pot -> wizards
        /// 5 all acolytes dead AND the pot clean -> wizards on the spot
        /// (acolytes dead + green pot + clock running = still racing)
        void TickMatch()
        {
            _clock -= Time.deltaTime;

            bool potReady = CauldronEconomy.Active != null
                && CauldronEconomy.PrepRemaining <= 0f
                && CauldronEconomy.VacuumRemaining < 0f; // mid-hop ink is not "empty"

            if (potReady && CauldronEconomy.Fill01 <= 0.002f)
            {
                Win(2, "the pot ran dry");
                return;
            }
            if (_hadWizards && !AnySide(Side.Wizard, aliveOnly: true))
            {
                Win(2, "no wizard stands");
                return;
            }
            if (_hadAcolytes && !AnySide(Side.Acolyte, aliveOnly: true)
                && !CauldronEconomy.IsCorrupt)
            {
                Win(1, "the acolytes are gone and the pot is clean");
                return;
            }
            if (_clock <= 0f)
            {
                if (CauldronEconomy.IsCorrupt) Win(2, "time ran out on a green pot");
                else Win(1, "time ran out on a clean pot");
            }
        }

        bool AnySide(Side side, bool aliveOnly)
        {
            foreach (var p in _players)
            {
                if (p == null) continue;
                if (aliveOnly && p.IsDead) continue;
                var s = Sides.IsAcolytePlayer(p) ? Side.Acolyte : Side.Wizard;
                if (s == side) return true;
            }
            if (NetGame.Connected && NetGame.IsHost)
                return aliveOnly ? NetSync.AnySideAlive(side) : NetSync.AnySidePresent(side);
            return false;
        }

        void Win(int winner, string how)
        {
            _phase = Phase.Over;
            _winner = winner;
            _overTimer = 7f;
            bool wizards = winner == 1;
            ComboBanner.Show(wizards ? "WIZARDS WIN" : "ACOLYTES WIN",
                wizards ? new Color(0.65f, 0.85f, 1f) : new Color(0.55f, 1f, 0.45f));
            DrawingWorld.Instance?.LogEvent(how);
            var at = _players.Count > 0 && _players[0] != null ? _players[0].transform.position : Vector3.zero;
            bool mine = wizards == (Sides.Of(Grimoire.LocalPlayerId) == Side.Wizard);
            if (mine) Juice.Chime(at); else Juice.Sting(at);
            RunStats.Log(wizards ? "wizards" : "acolytes", 0, _kills, Time.time - _runStart);
        }

        void ReturnToLobby()
        {
            if (SceneManager.GetActiveScene().name == LobbySceneName)
            {
                _phase = Phase.Idle;
                _winner = 0;
                return;
            }
            LoadEgg.Cover();
            LoadingHints.Show();
            SceneManager.LoadScene(LobbySceneName);
        }

        // ----------------------------------------------------------- helpers --
        void RefreshPlayers()
        {
            _playerScan -= Time.deltaTime;
            if (_playerScan > 0f) return;
            _playerScan = 1f;
            _players.Clear();
            _players.AddRange(SimpleFPSController.All);
        }

        // --------------------------------------------------------------- HUD --
        // rendering lives in UI/HUD.cs; this just reports
        static string _hud = "";
        static (int, int, int, int) _hudKey = (int.MinValue, 0, 0, 0);

        public static string HudStatus()
        {
            if (Instance == null) return "";
            bool remote = NetGame.Connected && !NetGame.IsHost;
            (int, int, int, int) key = remote
                ? (NetSync.HasRound ? NetSync.NetPhase : -1, NetSync.NetRound,
                    Mathf.RoundToInt(NetSync.NetTimer), NetSync.NetKills)
                : (100 + (int)Instance._phase, Instance._winner,
                    Mathf.RoundToInt(Instance._clock), Instance._kills);
            if (key == _hudKey) return _hud;
            _hudKey = key;
            _hud = remote ? RemoteStatus() : LocalStatus();
            return _hud;
        }

        static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        static string PotWord() =>
            CauldronEconomy.Active == null ? ""
            : CauldronEconomy.IsCorrupt ? " · the pot is GREEN"
            : $" · pot {Mathf.RoundToInt(CauldronEconomy.Fill01 * 100f)}%";

        static string LocalStatus() => Instance._phase switch
        {
            Phase.Live => $"{Clock(Instance._clock)}{PotWord()}",
            Phase.Over => Instance._winner == 1
                ? "WIZARDS WIN. back to the lobby"
                : "ACOLYTES WIN. back to the lobby",
            _ => "",
        };

        static string RemoteStatus() =>
            !NetSync.HasRound ? ""
                : NetSync.NetPhase switch
                {
                    1 => $"{Clock(NetSync.NetTimer)}{PotWord()}",
                    2 => NetSync.NetRound == 1
                        ? "WIZARDS WIN. back to the lobby"
                        : "ACOLYTES WIN. back to the lobby",
                    _ => "",
                };
    }
}
