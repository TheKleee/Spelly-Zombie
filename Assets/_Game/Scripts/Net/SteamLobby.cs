using System.Collections.Generic;
using FishNet;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// Steam lobbies: private (invite-only) or public (browser-listed, optional
    /// password, join-time ping gate). FishySteamworks is swapped onto the
    /// NetworkManager at connect time (lobbies without Steam keep Tugboat); the
    /// NetworkManager lives in the Lobby scene, so menu-started flows connect deferred.
    public class SteamLobby : MonoBehaviour
    {
        /// Steam's own ceiling for a lobby. The real size is the host's pick,
        /// PendingSize; at the ceiling the stand reads "no cap".
        public const int MaxPlayers = 250;
        /// The size a fresh stand offers, and the crowd past which the stand
        /// says the host's connection carries everyone.
        public const int DefaultSize = 12, ComfortPlayers = 32;
        static readonly int[] SizeLadder = { 20, 24, 32, 48, 64, 100, MaxPlayers };

        /// The next size the stand's arrows offer: by one up to 16, then the ladder.
        public static int StepSize(int size, int dir)
        {
            if (dir > 0)
            {
                if (size < 16) return size + 1;
                foreach (int rung in SizeLadder) if (rung > size) return rung;
                return MaxPlayers;
            }
            if (size <= 16) return Mathf.Max(2, size - 1);
            int prev = 16;
            foreach (int rung in SizeLadder) { if (rung >= size) break; prev = rung; }
            return prev;
        }

        /// The size row's text: a number, or no cap at Steam's ceiling.
        public static string SizeLabel(int size) =>
            size >= MaxPlayers ? Loc.T("stand.nocap") : Loc.F("stand.size", size);

        public static SteamLobby I { get; private set; }
        public static bool SteamReady { get; private set; }
        /// One-line human status for menu/status labels.
        public static string Status { get; private set; } = "";

        /// Back in the main menu: the last word of a lobby that is gone ("Lobby deleted")
        /// gives way to the plain line again.
        public static void ShowIdle()
        {
            if (NetGame.Connected) return;
            Status = SteamReady ? Loc.F("steam.ready", SteamFriends.GetPersonaName()) : Loc.T("steam.offline");
        }

        enum Pending { None, Host, Client }

        CSteamID _lobby;
        bool _isPrivate;
        Pending _pending = Pending.None;
        string _hostAddress = "";
        bool _creating; // one Steam lobby per click, the callback is async
        string _hostPassword = "";
        float _plocRefresh; // ping location goes stale; republished while hosting

        Callback<LobbyCreated_t> _cbCreated;
        Callback<LobbyEnter_t> _cbEnter;
        Callback<GameLobbyJoinRequested_t> _cbJoinRequested;
        CallResult<LobbyMatchList_t> _crBrowse;

        // ---- the public lobby browser ----
        public struct PublicLobby
        {
            public CSteamID Id;
            public string Name;
            public int Players, Max, Ping; // Ping -1 = unknown
            public bool Locked;
            public string Region; // code from Regions
            public string Lang;   // code from Loc.Languages
            public int Tags;      // bitmask into TagKeys
            public bool InGame;   // a match is running right now
            public float UpStamp; // unscaled time the lobby went up, as this machine counts; < 0 = unknown
        }

        /// The host has not touched the controls for HostAfkMinutes: a public lobby is off
        /// the lists (friends can still come) until they move again.
        public static bool HostAway { get; private set; }

        /// The host flips this when a match starts/ends; rows show it.
        public static void SetInGame(bool on)
        {
            LanLobby.InGame = on;
            if (I == null || !I._lobby.IsValid() || !NetGame.IsHost) return;
            SteamMatchmaking.SetLobbyData(I._lobby, "sz_ingame", on ? "1" : "0");
        }

        /// Region codes; display names live in Loc. Mandatory when hosting public.
        public static readonly string[] Regions = { "eu", "na", "sa", "asia", "oce", "mea" };

        /// Server tag Loc keys, bitmask order.
        public static readonly string[] TagKeys =
            { "tag.welcome", "tag.beginners", "tag.casual", "tag.tryhard", "tag.mic", "tag.quiet" };

        /// Set by the host UI before creating a public lobby.
        public static string PendingRegion = "eu";
        public static string PendingLang = "";  // "" = adopt the player's game language
        public static int PendingTags;
        public static string PendingName = "";
        /// Host-picked lobby size, 2..MaxPlayers (MaxPlayers = no cap).
        public static int PendingSize = DefaultSize;

        public static readonly List<PublicLobby> Lobbies = new List<PublicLobby>();
        /// Bumps every time Lobbies is refilled - UI rebuilds on change.
        public static int ListStamp { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("SteamLobby");
            DontDestroyOnLoad(go);
            go.AddComponent<SteamLobby>();
        }

        void Awake()
        {
            I = this;
            // SteamManager (Steamworks.NET's helper) owns init/shutdown and
            // pumps callbacks - touching Initialized bootstraps it.
            try
            {
                SteamReady = SteamManager.Initialized;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] Steam unavailable ({e.Message}). Lobbies stay on this PC and its local network.");
                SteamReady = false;
            }
            if (!SteamReady)
            {
                Status = Loc.T("steam.offline");
                return;
            }
            _cbCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _cbEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(
                r => SteamMatchmaking.JoinLobby(r.m_steamIDLobby)); // overlay "Join Game"
            // an invite accepted while the game was closed: Steam starts it with +connect_lobby <id>
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong invited))
                {
                    SteamMatchmaking.JoinLobby(new CSteamID(invited));
                    break;
                }
            _crBrowse = CallResult<LobbyMatchList_t>.Create(OnBrowseList);
            // measure our network coordinates now so ping estimates exist by
            // the time anyone browses a lobby
            SteamNetworkingUtils.InitRelayNetworkAccess();
            Status = Loc.F("steam.ready", SteamFriends.GetPersonaName());
        }

        // ------------------------------------------------------ public API --
        public static void HostFriends() => I?.CreateLobby(friendsPrivate: true, "");
        public static void HostPublic(string password = "")
            => I?.CreateLobby(friendsPrivate: false, password);

        /// True while this machine hosts a live lobby (Steam or LAN).
        public static bool Hosting => NetGame.IsHost;

        /// A Steam join is on its way: the lobby is entered, the connection not started yet.
        public static bool JoinPending => I != null && I._pending == Pending.Client;

        /// A listed lobby its host's game has not stamped for this long is left over.
        const long StaleSeconds = 120;

        /// Refill the public lobby list. Worldwide on purpose: the list shows
        /// each row's estimated ping and the player picks.
        public static void RefreshList()
        {
            if (I == null || !SteamReady) return;
            SteamMatchmaking.AddRequestLobbyListStringFilter("sz_game", "spellyzombie", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter("sz_private", "0", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            I._crBrowse.Set(SteamMatchmaking.RequestLobbyList());
        }

        void OnBrowseList(LobbyMatchList_t r, bool ioFailure)
        {
            Lobbies.Clear();
            if (!ioFailure)
                for (int i = 0; i < r.m_nLobbiesMatching; i++)
                {
                    var id = SteamMatchmaking.GetLobbyByIndex(i);
                    string name = SteamMatchmaking.GetLobbyData(id, "sz_name");
                    int.TryParse(SteamMatchmaking.GetLobbyData(id, "sz_tags"), out int tags);
                    if (SteamMatchmaking.GetLobbyData(id, "sz_afk") == "1") continue; // the host is away from the keys
                    // a lobby its host's game no longer stamps is left over (a game that closed badly)
                    uint.TryParse(SteamMatchmaking.GetLobbyData(id, "sz_beat"), out uint beat);
                    if (beat == 0 || (long)SteamUtils.GetServerRealTime() - beat > StaleSeconds) continue;
                    if (SteamMatchmaking.GetLobbyData(id, "sz_owner") == SteamUser.GetSteamID().m_SteamID.ToString())
                        continue; // your own: nothing to join
                    // how long it has stood: Steam's clock on both ends
                    uint.TryParse(SteamMatchmaking.GetLobbyData(id, "sz_up"), out uint up);
                    float upStamp = up == 0 ? -1f
                        : Time.unscaledTime - Mathf.Max(0f, (float)((long)SteamUtils.GetServerRealTime() - up));
                    Lobbies.Add(new PublicLobby
                    {
                        Id = id,
                        Name = string.IsNullOrEmpty(name) ? "lobby" : name,
                        Players = SteamMatchmaking.GetNumLobbyMembers(id),
                        Max = SteamMatchmaking.GetLobbyMemberLimit(id),
                        Ping = EstimatePingTo(id),
                        Locked = SteamMatchmaking.GetLobbyData(id, "sz_pw") == "1",
                        Region = SteamMatchmaking.GetLobbyData(id, "sz_region"),
                        Lang = SteamMatchmaking.GetLobbyData(id, "sz_lang"),
                        Tags = tags,
                        InGame = SteamMatchmaking.GetLobbyData(id, "sz_ingame") == "1",
                        UpStamp = upStamp,
                    });
                }
            // best connection first, unknown ping last
            Lobbies.Sort((a, b) => (a.Ping < 0 ? 9999 : a.Ping).CompareTo(b.Ping < 0 ? 9999 : b.Ping));
            ListStamp++;
        }

        /// Join the exact lobby the player clicked. Password and ping gate
        /// both answer with the reason when they refuse.
        public static void JoinListed(CSteamID lobby, string password)
        {
            if (I == null) return;
            if (NetGame.Connected) { Status = Loc.T("steam.leavefirst"); return; }
            if (!Fits(lobby)) return;
            if (SteamMatchmaking.GetLobbyData(lobby, "sz_pw") == "1"
                && Hash(password ?? "") != SteamMatchmaking.GetLobbyData(lobby, "sz_pwh"))
            {
                Status = Loc.T("browse.needpw");
                return;
            }
            if (I.PingTooHigh(lobby, out int ms, out int cap))
            {
                Status = Loc.F("steam.ping", ms, cap);
                return;
            }
            Status = Loc.T("steam.joining");
            SteamMatchmaking.JoinLobby(lobby);
        }

        /// The version gate, for a lobby whose data Steam has handed over.
        static bool Fits(CSteamID lobby)
        {
            int.TryParse(SteamMatchmaking.GetLobbyData(lobby, "sz_build"), out int build);
            if (NetVersion.Admit(SteamMatchmaking.GetLobbyData(lobby, "sz_proto"), build)) return true;
            Status = NetVersion.Notice;
            return false;
        }

        static int EstimatePingTo(CSteamID lob)
        {
            string s = SteamMatchmaking.GetLobbyData(lob, "sz_ploc");
            if (string.IsNullOrEmpty(s)
                || !SteamNetworkingUtils.ParsePingLocationString(s, out var loc)) return -1;
            return SteamNetworkingUtils.EstimatePingTimeFromLocalHost(ref loc);
        }

        /// Change or clear the running public lobby's password.
        public static void SetPassword(string pw)
        {
            if (I == null || !I._lobby.IsValid() || !NetGame.IsHost || I._isPrivate) return;
            pw = (pw ?? "").Trim();
            I._hostPassword = pw;
            SteamMatchmaking.SetLobbyData(I._lobby, "sz_pw", string.IsNullOrEmpty(pw) ? "0" : "1");
            SteamMatchmaking.SetLobbyData(I._lobby, "sz_pwh", string.IsNullOrEmpty(pw) ? "" : Hash(pw));
            Status = string.IsNullOrEmpty(pw) ? "password removed" : "password set";
        }

        /// Tear the hosted lobby down: everyone is disconnected and the Steam
        /// lobby closes. Required before this player can join anything else.
        public static void DeleteLobby()
        {
            if (I != null && I._lobby.IsValid())
            {
                SteamMatchmaking.LeaveLobby(I._lobby);
                I._lobby = default;
            }
            if (InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Started)
                InstanceFinder.ServerManager.StopConnection(true);
            if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started)
                InstanceFinder.ClientManager.StopConnection();
            Status = Loc.T("steam.deleted");
        }

        /// A guest walks out: off the Steam lobby and the connection, quietly.
        public static void LeaveJoined()
        {
            if (I != null && I._lobby.IsValid())
            {
                SteamMatchmaking.LeaveLobby(I._lobby);
                I._lobby = default;
            }
            // a client still connecting stops too; one that never started is left
            // alone (Multipass errors when stopping a transport nobody set)
            if (NetGame.HasManager && InstanceFinder.ClientManager != null
                && (InstanceFinder.ClientManager.Started || NetGame.ClientStarting))
                InstanceFinder.ClientManager.StopConnection();
            NetGame.ClientStarting = false;
            Status = "";
        }

        /// Steam's own invite dialog for the current lobby (host presses this).
        public static void OpenInviteOverlay()
        {
            if (I != null && I._lobby.IsValid())
                SteamFriends.ActivateGameOverlayInviteDialog(I._lobby);
        }

        // ---------------------------------------------------------- hosting --
        void CreateLobby(bool friendsPrivate, string password)
        {
            if (!SteamReady)
            {
                // no Steam: the lobby stands on this PC instead; a public one is heard on the local network
                Status = Loc.T("steam.offline");
                NetGame.HostLocal(friendsPrivate ? "" : (password ?? "").Trim(), listed: !friendsPrivate);
                return;
            }
            if (NetGame.Connected || _pending != Pending.None || _creating) return;
            // a lobby still held from an earlier session goes first: one lobby per host
            if (_lobby.IsValid()) { SteamMatchmaking.LeaveLobby(_lobby); _lobby = default; }
            _creating = true;
            _isPrivate = friendsPrivate;
            _hostPassword = friendsPrivate ? "" : (password ?? "").Trim();
            Status = Loc.T("steam.creating");
            // private = invite only through Steam, invisible to every search
            SteamMatchmaking.CreateLobby(
                friendsPrivate ? ELobbyType.k_ELobbyTypePrivate : ELobbyType.k_ELobbyTypePublic,
                Mathf.Clamp(PendingSize, 2, MaxPlayers));
        }

        void OnLobbyCreated(LobbyCreated_t r)
        {
            _creating = false;
            if (r.m_eResult != EResult.k_EResultOK)
            {
                Status = Loc.F("steam.failed", r.m_eResult);
                return;
            }
            _lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_lobby, "sz_game", "spellyzombie");
            SteamMatchmaking.SetLobbyData(_lobby, "sz_private", _isPrivate ? "1" : "0");
            // the messages this host speaks (NetVersion): a joiner that speaks others stays out
            SteamMatchmaking.SetLobbyData(_lobby, "sz_proto", NetVersion.Proto);
            SteamMatchmaking.SetLobbyData(_lobby, "sz_build", NetVersion.Build.ToString());
            if (!_isPrivate)
            {
                string lobbyName = string.IsNullOrEmpty(PendingName)
                    ? SteamFriends.GetPersonaName() + "'s lobby" : PendingName;
                SteamMatchmaking.SetLobbyData(_lobby, "sz_name", lobbyName);
                SteamMatchmaking.SetLobbyData(_lobby, "sz_region",
                    string.IsNullOrEmpty(PendingRegion) ? "eu" : PendingRegion);
                SteamMatchmaking.SetLobbyData(_lobby, "sz_lang",
                    string.IsNullOrEmpty(PendingLang) ? Loc.LanguageCode : PendingLang);
                SteamMatchmaking.SetLobbyData(_lobby, "sz_tags", PendingTags.ToString());
            }
            SteamMatchmaking.SetLobbyData(_lobby, "sz_host",
                SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(_lobby, "sz_pw",
                string.IsNullOrEmpty(_hostPassword) ? "0" : "1");
            if (!string.IsNullOrEmpty(_hostPassword))
                SteamMatchmaking.SetLobbyData(_lobby, "sz_pwh", Hash(_hostPassword));
            SteamMatchmaking.SetLobbyData(_lobby, "sz_maxping",
                DrawingConfig.LobbyMaxPingMs.ToString("0"));
            SteamMatchmaking.SetLobbyData(_lobby, "sz_up", SteamUtils.GetServerRealTime().ToString()); // the lists count from here
            SteamMatchmaking.SetLobbyData(_lobby, "sz_afk", "0");
            SteamMatchmaking.SetLobbyData(_lobby, "sz_owner", SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(_lobby, "sz_beat", SteamUtils.GetServerRealTime().ToString());
            PublishPingLocation();

            _pending = Pending.Host;
            Status = Loc.T(_isPrivate ? "steam.private" : "steam.public");
            Debug.Log($"[SpellyZombie] Steam lobby up: {Status}");
            EnterVillage();
        }

        /// The join-time ping gate: the host publishes its Valve network
        /// coordinates in lobby data, so anyone can estimate their ping to it
        /// without connecting. No limit set or no estimate = never refused.
        bool PingTooHigh(CSteamID lob, out int ms, out int cap)
        {
            ms = -1;
            int.TryParse(SteamMatchmaking.GetLobbyData(lob, "sz_maxping"), out cap);
            string s = SteamMatchmaking.GetLobbyData(lob, "sz_ploc");
            if (!string.IsNullOrEmpty(s)
                && SteamNetworkingUtils.ParsePingLocationString(s, out var loc))
                ms = SteamNetworkingUtils.EstimatePingTimeFromLocalHost(ref loc);
            return cap > 0 && ms >= 0 && ms > cap;
        }

        void PublishPingLocation()
        {
            if (!_lobby.IsValid()) return;
            if (SteamNetworkingUtils.GetLocalPingLocation(out var loc) < 0f) return; // not measured yet, retried by Update
            SteamNetworkingUtils.ConvertPingLocationToString(ref loc, out string s, 1024);
            if (!string.IsNullOrEmpty(s)) SteamMatchmaking.SetLobbyData(_lobby, "sz_ploc", s);
        }

        static string Hash(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var b = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("sz:" + pw.Trim()));
                var sb = new System.Text.StringBuilder(b.Length * 2);
                foreach (var x in b) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        void OnLobbyEnter(LobbyEnter_t r)
        {
            if (_pending == Pending.Host || NetGame.IsHost)
                return; // the host's own lobby entry (no manager yet in the menu: nobody hosts there)
            if (r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Status = Loc.T("steam.noenter");
                return;
            }
            _lobby = new CSteamID(r.m_ulSteamIDLobby);
            // an invite or the overlay's Join Game lands here without passing the list
            if (!Fits(_lobby))
            {
                SteamMatchmaking.LeaveLobby(_lobby);
                _lobby = default;
                return;
            }
            _hostAddress = SteamMatchmaking.GetLobbyData(_lobby, "sz_host");
            if (string.IsNullOrEmpty(_hostAddress))
            {
                Status = Loc.T("steam.nohost");
                return;
            }
            _pending = Pending.Client;
            Status = Loc.T("steam.connecting");
            EnterVillage();
        }

        /// Seconds of silence before the LAN road gives a connection up for gone.
        const float LanTimeoutSeconds = 20f;

        // ------------------------------------------- deferred FishNet start --
        void EnterVillage()
        {
            if (SceneManager.GetActiveScene().name != "Lobby")
            {
                LoadEgg.Travel("Lobby"); // the NetworkManager lives there
            }
        }

        void Update()
        {
            // a host away from the keys: the public lobby steps out of the lists until they move again
            bool away = NetGame.IsHost && NetGame.Connected
                && Keys.IdleSeconds > DrawingConfig.HostAfkMinutes * 60f;
            if (away != HostAway)
            {
                HostAway = away;
                if (SteamReady && _lobby.IsValid() && !_isPrivate
                    && SteamMatchmaking.GetLobbyOwner(_lobby) == SteamUser.GetSteamID())
                {
                    // friends-only: gone from every search, friends and invites still get in
                    SteamMatchmaking.SetLobbyType(_lobby,
                        away ? ELobbyType.k_ELobbyTypeFriendsOnly : ELobbyType.k_ELobbyTypePublic);
                    SteamMatchmaking.SetLobbyData(_lobby, "sz_afk", away ? "1" : "0");
                }
                Debug.Log(away ? "[SpellyZombie] the host is away from the keys: the lobby is off the lists until they move"
                    : "[SpellyZombie] the host is back: the lobby is listed again");
            }

            // ping coordinates drift; a hosting lobby republishes every 30s
            // (also covers the first publish when Steam hadn't measured yet)
            if (SteamReady && _lobby.IsValid() && Time.unscaledTime >= _plocRefresh
                && SteamMatchmaking.GetLobbyOwner(_lobby) == SteamUser.GetSteamID())
            {
                _plocRefresh = Time.unscaledTime + 30f;
                PublishPingLocation();
                // still hosting it: the lists keep it (StaleSeconds)
                if (NetGame.IsHost && NetGame.Connected)
                    SteamMatchmaking.SetLobbyData(_lobby, "sz_beat", SteamUtils.GetServerRealTime().ToString());
            }

            if (_pending == Pending.None) return;
            // still riding into the Lobby scene: the manager can outlive it, and a road opened
            // anywhere else takes the lobby's welcome in a scene that does not hold it
            if (!NetGame.HasManager || SceneManager.GetActiveScene().name != "Lobby") return;
            var nm = InstanceFinder.NetworkManager;

            UseSteamTransport(nm);
            if (_pending == Pending.Host)
            {
                InstanceFinder.ServerManager.StartConnection();
                InstanceFinder.ClientManager.StartConnection();
            }
            else
            {
                InstanceFinder.ClientManager.StartConnection(_hostAddress);
            }
            NetGame.ClientStarting = true;
            _pending = Pending.None;
        }

        /// The NetworkManager's Multipass holds Tugboat (LAN) and FishySteamworks,
        /// both bound at startup; the server listens on both, the client picks its road.
        void UseSteamTransport(FishNet.Managing.NetworkManager nm)
        {
            var mp = nm.GetComponent<FishNet.Transporting.Multipass.Multipass>();
            // the LAN road waits half an hour of silence before it drops a connection: a client
            // that crashed kept its puppet standing on the host that long
            var lan = nm.GetComponent<FishNet.Transporting.Tugboat.Tugboat>();
            if (lan != null && lan.GetTimeout(true) > LanTimeoutSeconds)
            {
                lan.SetTimeout(LanTimeoutSeconds, true);
                lan.SetTimeout(LanTimeoutSeconds, false);
            }
            if (mp == null)
            {
                Debug.LogError("[SpellyZombie] The Lobby's NetworkManager needs a Multipass transport holding Tugboat and FishySteamworks.", nm);
                return;
            }
            mp.SetClientTransport<global::FishySteamworks.FishySteamworks>();
        }

        void OnDestroy()
        {
            if (SteamReady && _lobby.IsValid())
                SteamMatchmaking.LeaveLobby(_lobby);
        }

    }
}
