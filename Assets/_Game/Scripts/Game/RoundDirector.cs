using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// The game: round-based survival. Z starts a run. Each round spends a
    /// spawn budget of escalating zombie mixes; clear it to earn a 20s
    /// intermission (ink trickles back); all players down = wipe; survive
    /// round 10 (demo cap) = victory. Kills feed the shared ink economy.
    /// Self-bootstraps into any scene; also owns the survival HUD.
    public class RoundDirector : MonoBehaviour
    {
        enum Phase { Idle, Wave, Intermission, GameOver, Victory }

        public static RoundDirector Instance { get; private set; }

        /// True while a run is on — gates ink costs and real player downs.
        public static bool RunActive =>
            Instance != null && (Instance._phase == Phase.Wave || Instance._phase == Phase.Intermission);

        /// True before a run starts — the MatchLobby (pillars/ready-up) lives here.
        public static bool InLobby => Instance != null && Instance._phase == Phase.Idle;

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

            // B4: only the HOST runs the round machine — clients render the
            // host's zombies (NetZombieProxy) and read round state off the wire
            if (NetGame.Connected && !NetGame.IsHost) return;

            if (NetGame.IsHost)
            {
                _netPush -= Time.deltaTime;
                if (_netPush <= 0f)
                {
                    _netPush = 0.5f;
                    NetSync.PushRoundState((byte)_phase, _round,
                        _toSpawn + Zombie.All.Count, _phaseTimer, _kills);
                }
            }

            switch (_phase)
            {
                case Phase.Idle:
                    if (kb.zKey.wasPressedThisFrame
                        || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame))
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

        void StartRun()
        {
            _kills = 0;
            Wallet.Riches = 0;
            _runStart = Time.time;
            PlayerInk.RefillAll();
            Powerups.ResetRun(); // fresh build every run
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

        void TickWave()
        {
            int alive = Zombie.All.Count;

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
            if (_round >= DrawingConfig.MaxRounds)
            {
                _phase = Phase.Victory;
                ComboBanner.Show("YOU SURVIVED THE DEMO!", new Color(0.5f, 1f, 0.6f));
                RunStats.Log("victory", _round, _kills, 0, Wallet.Riches, Time.time - _runStart);
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
            RunStats.Log("wipe", _round, _kills, 0, Wallet.Riches, Time.time - _runStart);
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
            var entries = FindObjectsByType<ZombieEntryPoint>(FindObjectsSortMode.None);
            if (entries.Length > 0)
                return entries[Random.Range(0, entries.Length)].transform.position;

            Vector3 center = _players.Count > 0 && _players[0] != null
                ? _players[0].transform.position : Vector3.zero;
            float ang = Random.value * Mathf.PI * 2f;
            Vector3 pos = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 16f;
            pos.y = center.y;
            return pos;
        }

        // --------------------------------------------------------------- HUD --
        void OnGUI()
        {
            var player = _players.Count > 0 ? _players[0] : null;

            // top line: run state (clients read the HOST's round off the wire)
            string top;
            if (NetGame.Connected && !NetGame.IsHost)
            {
                top = !NetSync.HasRound ? "co-op: waiting for the host…"
                    : NetSync.NetPhase switch
                {
                    1 => $"ROUND {NetSync.NetRound} — {NetSync.NetLeft} zombies left",
                    2 => $"INTERMISSION — round {NetSync.NetRound + 1} in {NetSync.NetTimer:0}s (draw!)",
                    3 => $"WIPED on round {NetSync.NetRound} — {NetSync.NetKills} kills. Host restarts",
                    4 => $"VICTORY — {NetSync.NetKills} kills!",
                    _ => "host presses Z to start the run",
                };
            }
            else top = _phase switch
            {
                Phase.Idle => "Z = start the run",
                Phase.Wave => $"ROUND {_round} — {_toSpawn + Zombie.All.Count} zombies left",
                Phase.Intermission => $"INTERMISSION — round {_round + 1} in {_phaseTimer:0}s (draw!)",
                Phase.GameOver => $"WIPED on round {_round} — {_kills} kills, {Wallet.Riches} riches. ENTER = again",
                _ => $"VICTORY — {_kills} kills, {(Time.time - _runStart) / 60f:0.0} min. ENTER = again",
            };
            GUI.Label(new Rect(12, Screen.height - 64, 900, 20), top);

            // seal gallery: the round's spellwork on display between rounds
            if (_phase == Phase.Intermission || _phase == Phase.GameOver || _phase == Phase.Victory)
                SealGallery.DrawGrid(Screen.width / 2f, Screen.height * 0.68f);

            // bars: health + ink
            if (player != null)
            {
                DrawBar(new Rect(12, Screen.height - 44, 180, 12),
                    player.Health / 100f, new Color(0.85f, 0.25f, 0.25f));
                var ink = player.GetComponent<PlayerInk>();
                if (ink != null)
                    DrawBar(new Rect(12, Screen.height - 28, 180, 12),
                        ink.Fraction, new Color(0.25f, 0.55f, 0.95f));
                GUI.Label(new Rect(200, Screen.height - 46, 300, 20), "HP");
                GUI.Label(new Rect(200, Screen.height - 30, 300, 20), $"INK   Riches: {Wallet.Riches}");

                if (player.IsDowned)
                {
                    var style = new GUIStyle(GUI.skin.label)
                    { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                    style.normal.textColor = player.IsDead ? Color.red : new Color(1f, 0.5f, 0.4f);
                    string msg = player.IsDead ? "DEAD"
                        : player.ReviveProgress > 0f ? $"REVIVING… {player.ReviveProgress * 100f:0}%"
                        : $"DOWNED — bleeding out {player.BleedOut:0}s (teammate: hold E)";
                    GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40), msg, style);
                }
            }
        }

        static void DrawBar(Rect r, float t, Color c)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, (r.width - 2) * Mathf.Clamp01(t), r.height - 2),
                Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
