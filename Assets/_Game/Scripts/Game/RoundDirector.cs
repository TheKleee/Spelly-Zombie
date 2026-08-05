using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// Round-based survival: spawn budget per round, intermission between, wipe/victory; kills feed the shared ink economy. Self-bootstraps into any scene.
    public class RoundDirector : MonoBehaviour
    {
        enum Phase { Idle, Wave, Intermission, GameOver, Victory }

        public static RoundDirector Instance { get; private set; }

        /// True while a run is on — gates ink costs and real player downs.
        public static bool RunActive =>
            Instance != null && (Instance._phase == Phase.Wave || Instance._phase == Phase.Intermission);

        /// True before a run starts — the MatchLobby (pillars/ready-up) lives here.
        public static bool InLobby => Instance != null && Instance._phase == Phase.Idle;

        /// True only while a wave runs — the music director crossfades on this.
        public static bool WaveActive => Instance != null && Instance._phase == Phase.Wave;

        /// MatchLobby's start trigger (all ready, or the troll timer expired).
        public static void ForceStart()
        {
            if (Instance == null || Instance._phase != Phase.Idle) return;
            if (NetGame.Connected && !NetGame.IsHost) return; // host decides
            Instance.StartRun();
        }

        Phase _phase = Phase.Idle;
        int _round;
        int _toSpawn;          // budget left this round
        float _spawnTimer, _phaseTimer;
        int _kills;
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
        /// Zombie.OnDeath calls this: kills are the ink mine.
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

            // B4: only the HOST runs the round machine — clients read round state off the wire
            if (NetGame.Connected && !NetGame.IsHost)
                return;

            if (NetGame.IsHost)
            {
                _netPush -= Time.deltaTime;
                if (_netPush <= 0f)
                {
                    _netPush = 0.5f;
                    NetSync.PushRoundState((byte)_phase, _round,
                        _toSpawn + CountableZombies(), _phaseTimer, _kills);
                }
            }

            switch (_phase)
            {
                case Phase.Idle:
                    // Z-to-start REMOVED (Marko's ruling) — gamepad start stays as the couch equivalent
                    if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
                        StartRun();
                    break;

                case Phase.Wave:
                    TickWave();
                    if (AllPlayersDown()) Wipe();
                    break;

                case Phase.Intermission:
                    _phaseTimer -= Time.deltaTime;
                    PlayerInk.AwardAll(DrawingConfig.InkTricklePerSec * Time.deltaTime);
                    if (AllPlayersDown()) Wipe(); // stragglers can still get you
                    if (_phaseTimer <= 0f) StartRound(_round + 1);
                    break;

                case Phase.GameOver:
                case Phase.Victory:
                    if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                        || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame))
                        Restart();
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
            // back in the lobby after a wipe/victory: reset phase so no stale end screen haunts it
            if (scene.name == LobbySceneName
                && (_phase == Phase.GameOver || _phase == Phase.Victory))
            {
                _phase = Phase.Idle;
                _round = 0;
            }

            if (!_startOnLoad || scene.name != GameSceneName) return;
            _startOnLoad = false;
            StartRunNow();
        }

        /// THE LOBBY IS SAFE GROUND (Marko's rule) — runs load the game map and begin on arrival.
        void StartRun()
        {
            if (SceneManager.GetActiveScene().name == "Menu") return; // menus don't fight
            if (SceneManager.GetActiveScene().name == LobbySceneName)
            {
                if (Application.CanStreamedLevelBeLoaded(GameSceneName))
                {
                    _startOnLoad = true;
                    SceneManager.LoadScene(GameSceneName);
                    return;
                }
                ComboBanner.Show("THE LOBBY IS SAFE GROUND", new Color(0.7f, 0.9f, 1f));
                DrawingWorld.Instance?.LogEvent(
                    "runs happen on the game map — no 'Game' scene in Build Settings yet");
                return;
            }
            StartRunNow();
        }

        void StartRunNow()
        {
            _kills = 0;
            _runStart = Time.time;
            PlayerInk.RefillAll();
            Powerups.ResetRun(); // fresh build every run
            Perks.ResetRun();    // the brews wear off too
            StartRound(1);
        }

        void StartRound(int round)
        {
            _round = round;
            _phase = Phase.Wave;
            _toSpawn = 5 + round * 3;
            _spawnTimer = 1.5f; // a breath before the first shamble
            SealGallery.Clear(); // fresh gallery every round
            ComboBanner.Show($"ROUND {round}", new Color(1f, 0.85f, 0.4f));
            var p0 = _players.Count > 0 && _players[0] != null ? _players[0].transform.position : Vector3.zero;
            Juice.Drum(p0);
            DrawingWorld.Instance?.LogEvent($"ROUND {round} — {_toSpawn} incoming");
        }

        /// Zombies that count toward ending a round — DEMONS don't (an unkillable grand demon held the round hostage).
        static int CountableZombies()
        {
            int n = 0;
            foreach (var z in Zombie.All)
                if (z != null && !z.IsDemon) n++;
            return n;
        }

        void TickWave()
        {
            int alive = CountableZombies();

            if (_toSpawn <= 0)
            {
                if (alive == 0) EndRound();
                return;
            }

            int aliveCap = Mathf.Min(24, 12 + _round);
            if (alive >= aliveCap) return;

            _spawnTimer -= Time.deltaTime;
            if (_spawnTimer > 0f) return;
            _spawnTimer = Mathf.Max(0.6f, 2.2f - _round * 0.15f);

            float speedMul = 1f + _round * 0.03f;
            var kind = PickKind();
            if (kind == ZombieKind.Swarm)
            {
                // gremlins come as a family
                int pack = Mathf.Min(Random.Range(5, 8), _toSpawn);
                Vector3 at = PickSpawnPoint();
                for (int i = 0; i < pack; i++)
                    Zombie.Spawn(at + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)),
                        ZombieKind.Swarm, speedMul);
                _toSpawn -= pack;
            }
            else
            {
                Zombie.Spawn(PickSpawnPoint(), kind, speedMul);
                _toSpawn--;
            }
        }

        /// Escalating mix: walkers first, then runners, spitters (scribblers),
        /// brutes (chargers), and finally swarm packs.
        ZombieKind PickKind()
        {
            float w = 10f;                                   // walker
            float r = _round >= 2 ? 6f : 0f;                 // runner
            float sc = _round >= 3 ? 4f : 0f;                // scribbler/spitter
            float ch = _round >= 4 ? 3f : 0f;                // charger/brute
            float sw = _round >= 5 ? 3f : 0f;                // swarm pack
            float roll = Random.value * (w + r + sc + ch + sw);
            if ((roll -= w) < 0f) return ZombieKind.Walker;
            if ((roll -= r) < 0f) return ZombieKind.Runner;
            if ((roll -= sc) < 0f) return ZombieKind.Scribbler;
            if ((roll -= ch) < 0f) return ZombieKind.Charger;
            return ZombieKind.Swarm;
        }

        void EndRound()
        {
            // THE ROUND'S HANDWRITING IS FORGOTTEN (Marko: in memory "for that round and not all the rounds"); Rune Studio recordings untouched
            RuneLibrary.ForgetRoundLearning();

            // MARKO'S WIN: all gates open + round clear = demo victory; gateless maps keep the round cap
            bool gatesCleared = SpellLock.CountInScene > 0 && SpellLock.AllOpen;
            if (gatesCleared || _round >= DrawingConfig.MaxRounds)
            {
                _phase = Phase.Victory;
                ComboBanner.Show("YOU SURVIVED THE DEMO!", new Color(0.5f, 1f, 0.6f));
                RunStats.Log("victory", _round, _kills, Time.time - _runStart);
                return;
            }
            _phase = Phase.Intermission;
            _phaseTimer = DrawingConfig.IntermissionSeconds;
            ComboBanner.Show($"ROUND {_round} CLEARED", new Color(0.6f, 1f, 0.7f));
            var pc = _players.Count > 0 && _players[0] != null ? _players[0].transform.position : Vector3.zero;
            Juice.Chime(pc);
            // revive the fallen between rounds — co-op forgiveness beat
            foreach (var p in _players)
                if (p != null && p.IsDowned && !p.IsDead) p.Revive();
        }

        void Wipe()
        {
            _phase = Phase.GameOver;
            ComboBanner.Show("THE HORDE WINS", new Color(1f, 0.35f, 0.3f));
            var pw = _players.Count > 0 && _players[0] != null ? _players[0].transform.position : Vector3.zero;
            Juice.Sting(pw);
            SealAutopsy.OnWipe();
            RunStats.Log("wipe", _round, _kills, Time.time - _runStart);
        }

        void Restart()
        {
            _phase = Phase.Idle;
            _round = 0;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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

        bool AllPlayersDown()
        {
            if (_players.Count == 0) return false;
            foreach (var p in _players)
                if (p != null && !p.IsDowned) return false;
            // hosting: friends still standing keep the run alive
            if (NetGame.Connected && NetGame.IsHost && !NetSync.AllRemotesDown) return false;
            return true;
        }

        Vector3 PickSpawnPoint()
        {
            Vector3 pos;
            var entries = ZombieEntryPoint.All; // live registry — no per-spawn scene scan
            if (entries.Count > 0)
            {
                pos = entries[Random.Range(0, entries.Count)].transform.position;
            }
            else
            {
                Vector3 center = _players.Count > 0 && _players[0] != null
                    ? _players[0].transform.position : Vector3.zero;
                float ang = Random.value * Mathf.PI * 2f;
                pos = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 16f;
                pos.y = center.y;
            }

            // drop onto the ACTUAL ground — ring spawns at player height entombed zombies inside hills
            if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out var hit, 80f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            return pos;
        }

        // --------------------------------------------------------------- HUD --
        // Rendering moved to UI/HUD.cs (uGUI + Kenney skin); this just reports.

        /// The status line for the round banner (clients read the HOST's round off the wire).
        /// Cached — the HUD polls per frame, rebuild only when a displayed value changes.
        static string _hud = "";
        static (int, int, int, int, int) _hudKey = (int.MinValue, 0, 0, 0, 0);

        public static string HudStatus()
        {
            if (Instance == null) return "";
            bool remote = NetGame.Connected && !NetGame.IsHost;
            (int, int, int, int, int) key = remote
                ? (NetSync.HasRound ? NetSync.NetPhase : -1, NetSync.NetRound,
                    NetSync.NetLeft, Mathf.RoundToInt(NetSync.NetTimer), NetSync.NetKills)
                : (100 + (int)Instance._phase, Instance._round,
                    Instance._phase == Phase.Wave ? Instance._toSpawn + CountableZombies() : 0,
                    Instance._phase == Phase.Intermission ? Mathf.RoundToInt(Instance._phaseTimer)
                        : Instance._phase == Phase.Victory
                            ? Mathf.RoundToInt((Time.time - Instance._runStart) / 6f) : 0,
                    Instance._kills);
            if (key == _hudKey) return _hud;
            _hudKey = key;
            _hud = remote ? RemoteStatus() : LocalStatus();
            return _hud;
        }

        static string RemoteStatus() =>
            !NetSync.HasRound ? "co-op: waiting for the host…"
                : NetSync.NetPhase switch
            {
                1 => $"ROUND {NetSync.NetRound} — {NetSync.NetLeft} zombies left",
                2 => $"INTERMISSION — round {NetSync.NetRound + 1} in {NetSync.NetTimer:0}s (draw!)",
                3 => $"WIPED on round {NetSync.NetRound} — {NetSync.NetKills} kills. Host restarts",
                4 => $"VICTORY — {NetSync.NetKills} kills!",
                _ => "the host starts the run from the lobby",
            };

        static string LocalStatus() => Instance._phase switch
        {
            Phase.Idle => "", // runs start from the lobby ready-up; idle scenes stay quiet
            Phase.Wave => $"ROUND {Instance._round} — {Instance._toSpawn + CountableZombies()} zombies left",
            Phase.Intermission => $"INTERMISSION — round {Instance._round + 1} in {Instance._phaseTimer:0}s (draw!)",
            Phase.GameOver => $"WIPED on round {Instance._round} — {Instance._kills} kills. ENTER = again",
            _ => $"VICTORY — {Instance._kills} kills, {(Time.time - Instance._runStart) / 60f:0.0} min. ENTER = again",
        };
    }
}
