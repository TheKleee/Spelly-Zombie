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

        // a client's referee never runs: its phase is the host's, off the wire
        static bool Remote => NetGame.Connected && !NetGame.IsHost;
        static int NetPhase => NetSync.HasRound ? NetSync.NetPhase : 0;

        /// True while a match runs - gates ink costs and real player downs.
        public static bool RunActive => Remote ? NetPhase == 1
            : Instance != null && Instance._phase == Phase.Live;

        /// True before a match starts - the MatchLobby lives here.
        public static bool InLobby => Remote ? NetPhase == 0
            : Instance != null && Instance._phase == Phase.Idle;
        /// A real match on the map, running or just decided. Achievements gate on it.
        public static bool InMatch => Remote ? NetPhase != 0
            : Instance != null && Instance._phase != Phase.Idle;

        /// True while the match runs - the music director crossfades on this.
        public static bool WaveActive => RunActive;

        /// MatchLobby's start trigger.
        public static void ForceStart()
        {
            if (Instance == null || Instance._phase != Phase.Idle) return;
            if (NetGame.Connected && !NetGame.IsHost) return; // host decides
            Instance.StartRun();
        }

        /// The Quit button on a map: the match is dropped, not decided. No
        /// ending, no stats, and the lobby that loads next starts idle.
        public static void Abandon()
        {
            if (Instance == null) return;
            Instance._phase = Phase.Idle;
            Instance._winner = 0;
            Instance._ending = Achievements.Ending.None;
            _startOnLoad = false;
        }

        Phase _phase = Phase.Idle;
        float _clock;        // seconds left on the match timer
        int _kills;
        int _winner;         // 0 none, 1 wizards, 2 acolytes, 3 everyone together, 4 the environment, 5 nobody
        Achievements.Ending _ending;
        float _overTimer;
        bool _hadWizards, _hadAcolytes, _hadBoss;
        Vector3 _causeAt;    // where the ending camera looks (EndingShot)
        float _downFor;      // every player a ghost for this long
        const float WipeHoldSeconds = 1.5f;
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
            if (z != null)
            {
                var el = z.GetComponent<Element>();
                int killer = el != null ? Marks.Get(el.NetId, Mark.KilledBy) : -1;
                bool via = el != null && Marks.Get(el.NetId, Mark.KilledVia) == 1;
                NetSync.PushKill(z.transform.position, z.OwnerId, killer, via); // clients share the ink
                NetSync.RaiseZombieKilled(z.transform.position, z.OwnerId, killer, via);
            }
        }

        // ------------------------------------------------------------- flow --
        float _netPush;
        bool _bootChecked; // the first scene with players has been looked at

        void Update()
        {
            Achievements.Tick();
            var kb = Keyboard.current;
            // solo: the pause menu freezes time anyway. connected: the referee never stops
            if (kb == null || PoseStudio.IsOpen || (GameMenu.IsOpen && !NetGame.Connected)) return;

            RefreshPlayers();

            // only the HOST referees - clients read match state off the wire
            if (NetGame.Connected && !NetGame.IsHost) return;

            if (NetGame.IsHost)
            {
                _netPush -= Time.deltaTime;
                if (_netPush <= 0f)
                {
                    _netPush = 0.5f;
                    // over: the timer carries the countdown home instead of the match clock
                    BossMark.Summary(out float bossHp, out int bosses, out string bossName);
                    NetSync.PushRoundState((byte)_phase, _winner, 0,
                        _phase == Phase.Over ? _overTimer : _clock, _kills, (byte)_ending,
                        (byte)Mathf.RoundToInt(bossHp * 255f), (byte)Mathf.Min(bosses, 255), bossName, _causeAt);
                }
            }

            switch (_phase)
            {
                case Phase.Idle:
                    if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame)
                        StartRun();
                    else if (!_bootChecked && _players.Count > 0)
                    {
                        _bootChecked = true;
                        // a map played straight from the editor runs a real match;
                        // players only reach a map from the lobby, which starts it itself
                        if (FindFirstObjectByType<SpellyMap>() != null) StartRun();
                    }
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
        static string GameSceneName => MatchLobby.SelectedScene; // the host's lobby pick, a saved map's base scene
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
                _ending = Achievements.Ending.None;
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
                    LoadEgg.Travel(GameSceneName); // the shell forms, then everyone travels dark
                    return;
                }
                ComboBanner.Show(Loc.T("round.safe"), new Color(0.7f, 0.9f, 1f));
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
            _ending = Achievements.Ending.None;
            _runStart = Time.time;
            // negative IS the endless flag, and it crosses the wire on its
            // own - a client seeing a negative clock knows not to count down
            _clock = MatchLobby.Endless ? -1f
                : Mathf.Clamp(MatchLobby.DurationMin, 5, 15) * 60f;
            PlayerInk.RefillAll();
            BossMark.ResetMatch();
            _hadBoss = false;
            _causeAt = Vector3.zero;
            _downFor = 0f;
            SealGallery.Clear();
            RuneLibrary.ForgetRoundLearning();
            SideBootstrap.ResetBooksForMatch(); // lobby learning stays in the lobby

            // elimination endings only count sides that actually fielded
            // someone - a solo practice match runs on the clock alone
            _hadWizards = AnySide(Side.Wizard, aliveOnly: false);
            _hadAcolytes = AnySide(Side.Acolyte, aliveOnly: false);

            _phase = Phase.Live;
            // the start word follows the map's teams; asked of the scene itself, it may have just loaded
            ComboBanner.Show(MapRules.StartWords(SceneManager.GetActiveScene().name), new Color(1f, 0.85f, 0.4f));
            var p0 = _players.Count > 0 && _players[0] != null ? _players[0].transform.position : Vector3.zero;
            StartCue(p0);
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
            // NO TIMER: the rules are identical, the clock simply is not
            // one of the ways a match can end. Everything below still applies.
            // clamped at zero so the bell below rings; negative stays the endless flag
            if (_clock > 0f)
            {
                float was = _clock;
                _clock = Mathf.Max(0f, _clock - Time.deltaTime);
                if (was > WarnAt && _clock <= WarnAt) WarnCue();
            }

            // a side is fielded the moment anyone of it stands: bodies are
            // built after the scene arrives, so the start snapshot saw none
            _hadWizards |= AnySide(Side.Wizard, aliveOnly: false);
            _hadAcolytes |= AnySide(Side.Acolyte, aliveOnly: false);

            // a map with its own teams decides by them first; the five endings
            // below only while wizards and acolytes are two counting teams
            if (MapRules.Custom)
            {
                if (TickTeams()) return;
                if (!MapRules.ClassicEndings) return;
            }

            bool potReady = CauldronEconomy.Active != null
                && CauldronEconomy.PrepRemaining <= 0f
                && CauldronEconomy.VacuumRemaining < 0f; // mid-hop ink is not "empty"

            if (potReady && CauldronEconomy.Fill01 <= 0.002f)
            {
                Win(2, "the pot ran dry", Achievements.Ending.PotDry);
                return;
            }
            if (_hadWizards && !AnySide(Side.Wizard, aliveOnly: true))
            {
                Win(2, "no wizard stands", Achievements.Ending.NoWizards);
                return;
            }
            if (_hadAcolytes && !AnySide(Side.Acolyte, aliveOnly: true)
                && !CauldronEconomy.IsCorrupt)
            {
                Win(1, "the acolytes are gone and the pot is clean", Achievements.Ending.Sweep);
                return;
            }
            // the two timer endings, and the only two an endless match
            // cannot reach - it ends on the pot or on a side being wiped out
            if (_clock >= 0f && _clock <= 0.0001f)
            {
                if (CauldronEconomy.IsCorrupt) Win(2, "time ran out on a green pot", Achievements.Ending.GreenBell);
                else Win(1, "time ran out on a clean pot", Achievements.Ending.CleanBell);
            }
        }

        /// ★ THE MAP'S OWN TEAMS (MapRules, his WC3 model). Among the teams that
        /// count, the last one standing wins: a player team stands while anyone
        /// of it is not a ghost, the environment while a boss lives or has yet to
        /// stand up. When the last boss falls and no player team counts, the side
        /// that landed the last blow wins. The clock gives it to a standing
        /// environment that counts. True when this ended the match.
        bool TickTeams()
        {
            bool together = MapRules.Together;
            bool envCounts = MapRules.Counts(TeamWho.Environment);
            bool wizardsCount = !together && MapRules.Counts(TeamWho.Wizards);
            bool acolytesCount = !together && MapRules.Counts(TeamWho.Acolytes);

            bool pending = CreatureSpawn.BossPending;
            bool bossUp = BossMark.EnvironmentStands(out bool fielded);
            _hadBoss |= fielded;
            bool envUp = pending || bossUp;
            bool envFell = _hadBoss && !envUp;

            bool wizardsUp = AnySide(Side.Wizard, aliveOnly: true);
            bool acolytesUp = AnySide(Side.Acolyte, aliveOnly: true);
            // presence and revives arrive in slices: a wipe has to hold a moment
            _downFor = (_hadWizards || _hadAcolytes) && !wizardsUp && !acolytesUp ? _downFor + Time.deltaTime : 0f;
            bool wiped = _downFor >= WipeHoldSeconds;

            if (wiped)
            {
                if (envCounts && envUp) { Win(4, "every player is a ghost", Achievements.Ending.EveryoneDown); return true; }
                if (!MapRules.ClassicEndings) { Win(5, "every player is a ghost", Achievements.Ending.EveryoneDown); return true; }
            }

            if (envCounts && envFell)
            {
                if (together)
                {
                    Win(MapRules.Counts(TeamWho.Everyone) ? 3 : 5, "the last boss fell", Achievements.Ending.BossDown);
                    return true;
                }
                if (!wizardsCount && !acolytesCount)
                {
                    int side = BossMark.WinningSide(); // the race to the last boss
                    Win(side == 0 ? 5 : side, "the last boss fell", Achievements.Ending.BossDown);
                    return true;
                }
                if (wizardsCount != acolytesCount)
                {
                    bool up = wizardsCount ? wizardsUp : acolytesUp;
                    Win(up ? (wizardsCount ? 1 : 2) : 5, "the last boss fell", Achievements.Ending.BossDown);
                    return true;
                }
                // both sides count: their own race goes on
            }

            // the one counting side is gone while the environment stands
            if (envCounts && envUp && wizardsCount != acolytesCount)
            {
                bool had = wizardsCount ? _hadWizards : _hadAcolytes;
                bool up = wizardsCount ? wizardsUp : acolytesUp;
                if (had && !up) { Win(4, "the counting side is down", Achievements.Ending.EveryoneDown); return true; }
            }

            if (_clock >= 0f && _clock <= 0.0001f)
            {
                if (envCounts && envUp) { Win(4, "time ran out on the boss", Achievements.Ending.TimeUp); return true; }
                if (!MapRules.ClassicEndings) { Win(5, "time ran out", Achievements.Ending.TimeUp); return true; }
            }
            return false;
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

        void Win(int winner, string how, Achievements.Ending ending)
        {
            _phase = Phase.Over;
            _winner = winner;
            _ending = ending;
            _causeAt = CauseOf(ending);
            _overTimer = 7f;
            ComboBanner.Show(WinWords(winner), WinColor(winner));
            DrawingWorld.Instance?.LogEvent(how);
            var at = _players.Count > 0 && _players[0] != null ? _players[0].transform.position : Vector3.zero;
            EndCue(WonHere(winner), at);
            RunStats.Log(WinName(winner), 0, _kills, Time.time - _runStart);
            Achievements.MatchEnded(winner, ending);
        }

        /// The match is over here: who won, how, and where the ending camera looks.
        public static bool Ended(out int winner, out Achievements.Ending ending, out Vector3 causeAt)
        {
            if (Remote)
            {
                winner = NetSync.NetRound;
                ending = (Achievements.Ending)NetSync.NetEnding;
                causeAt = NetSync.NetCauseAt;
                return NetPhase == 2;
            }
            var d = Instance;
            winner = d != null ? d._winner : 0;
            ending = d != null ? d._ending : Achievements.Ending.None;
            causeAt = d != null ? d._causeAt : Vector3.zero;
            return d != null && d._phase == Phase.Over;
        }

        /// ★ WHAT THE ENDING CAMERA LOOKS AT (EndingShot): the pot for the pot
        /// endings, a fallen body of the side that went down, where the boss
        /// fell, the boss that outlasted the clock. Zero = no shot.
        Vector3 CauseOf(Achievements.Ending ending)
        {
            switch (ending)
            {
                case Achievements.Ending.NoWizards: return DownedAt(Side.Wizard, true);
                case Achievements.Ending.Sweep: return DownedAt(Side.Acolyte, true);
                case Achievements.Ending.EveryoneDown: return DownedAt(Side.Wizard, false);
                case Achievements.Ending.BossDown: return BossMark.FellAt;
                case Achievements.Ending.TimeUp:
                    foreach (var b in BossMark.All) if (b != null && b.Alive) return b.transform.position;
                    return PotAt();
                default: return PotAt();
            }
        }

        static Vector3 PotAt() => CauldronEconomy.Active != null ? CauldronEconomy.Active.transform.position : Vector3.zero;

        /// A fallen body: of that side when `ofSide`, anyone's otherwise.
        Vector3 DownedAt(Side side, bool ofSide)
        {
            foreach (var p in _players)
            {
                if (p == null || !p.IsDowned) continue;
                if (!ofSide || (Sides.IsAcolytePlayer(p) ? Side.Acolyte : Side.Wizard) == side) return p.transform.position;
            }
            foreach (var a in NetAvatar.All)
            {
                if (a == null || !a.Downed) continue;
                if (!ofSide || Sides.Of(NetSync.OwnerIdOf(a.Id)) == side) return a.transform.position;
            }
            return Vector3.zero;
        }

        /// The banner words for a winner code.
        public static string WinWords(int winner)
        {
            switch (winner)
            {
                case 1: return Loc.T("round.wizards");
                case 2: return Loc.T("round.acolytes");
                case 3: return Loc.T("round.everyone");
                case 4: return Loc.T("round.bosswins");
                default: return Loc.T("round.nobody");
            }
        }

        static string WinName(int winner) =>
            winner == 1 ? "wizards" : winner == 2 ? "acolytes" : winner == 3 ? "everyone" : winner == 4 ? "environment" : "nobody";

        static Color WinColor(int winner) =>
            winner == 1 ? new Color(0.65f, 0.85f, 1f)
            : winner == 2 ? new Color(0.55f, 1f, 0.45f)
            : winner == 3 ? new Color(1f, 0.85f, 0.4f)
            : winner == 4 ? new Color(1f, 0.45f, 0.35f)
            : new Color(0.8f, 0.8f, 0.8f);

        /// This machine's player is on the winning side.
        public static bool WonHere(int winner)
        {
            if (winner == 3) return true;
            if (winner != 1 && winner != 2) return false;
            return (winner == 1) == (Sides.Of(Grimoire.LocalPlayerId) == Side.Wizard);
        }

        /// The referee cues, on the machine that hears them: the start, and how it ended for this side.
        public static void StartCue(Vector3 at)
        {
            if (!Juice.Sound2D(Sfx.MatchStart)) Juice.Drum(at);
        }

        /// Seconds left when the warning sounds.
        public const float WarnAt = 60f;
        public static void WarnCue() => Juice.Sound2D(Sfx.Warning, 0.8f);

        public static void EndCue(bool won, Vector3 at)
        {
            if (Juice.Sound2D(won ? Sfx.YouWin : Sfx.YouLose)) return;
            if (won) Juice.Chime(at); else Juice.Sting(at);
        }

        void ReturnToLobby()
        {
            if (SceneManager.GetActiveScene().name == LobbySceneName)
            {
                _phase = Phase.Idle;
                _winner = 0;
                _ending = Achievements.Ending.None;
                return;
            }
            LoadEgg.Travel(LobbySceneName);
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
                    NetSync.NetPhase == 2 ? Mathf.CeilToInt(NetSync.NetTimer) : Mathf.RoundToInt(NetSync.NetTimer),
                    NetSync.NetKills)
                : (100 + (int)Instance._phase, Instance._winner,
                    Instance._phase == Phase.Over ? Mathf.CeilToInt(Instance._overTimer) : Mathf.RoundToInt(Instance._clock),
                    Instance._kills);
            if (key == _hudKey) return _hud;
            _hudKey = key;
            _hud = remote ? RemoteStatus() : LocalStatus();
            return _hud;
        }

        static string Clock(float seconds)
        {
            if (seconds < 0f) return "";   // no timer: nothing to count down
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        static string Victory(int winner, float secondsHome) =>
            Loc.F("round.home", WinWords(winner), Mathf.Max(1, Mathf.CeilToInt(secondsHome)));

        static string PotWord() =>
            !CauldronEconomy.HasInk ? ""
            : CauldronEconomy.VacuumRemaining >= 0f
                ? Loc.F("round.inkflight", Mathf.CeilToInt(CauldronEconomy.VacuumRemaining))
            : CauldronEconomy.PrepRemaining > 0f
                ? Loc.F("round.potopens", Mathf.CeilToInt(CauldronEconomy.PrepRemaining))
            : CauldronEconomy.IsCorrupt ? Loc.T("round.green")
            : Loc.F("round.pot", Mathf.RoundToInt(CauldronEconomy.Fill01 * 100f));

        static string LocalStatus() => Instance._phase switch
        {
            Phase.Live => $"{Clock(Instance._clock)}{PotWord()}",
            Phase.Over => Victory(Instance._winner, Instance._overTimer),
            _ => "",
        };

        static string RemoteStatus() =>
            !NetSync.HasRound ? ""
                : NetSync.NetPhase switch
                {
                    1 => $"{Clock(NetSync.NetTimer)}{PotWord()}",
                    2 => Victory(NetSync.NetRound, NetSync.NetTimer),
                    _ => "",
                };
    }
}
