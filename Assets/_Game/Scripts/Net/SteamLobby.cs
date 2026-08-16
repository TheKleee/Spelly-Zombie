using System.Collections.Generic;
using FishNet;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// STEAM LOBBIES - two modes:
    ///   PRIVATE: invite only through Steam (overlay or friends-list "Join
    ///   Game"). Steam-typed private, invisible to every search.
    ///   PUBLIC: listed in the in-game browser with name, size, region,
    ///   language, tags and estimated ping; optional password; a ping gate
    ///   refuses joiners over the lobby's limit before they ever connect.
    /// Transport: FishySteamworks is swapped onto the NetworkManager at
    /// connect time - the H/J LAN panel keeps Tugboat for dev testing.
    ///
    /// The NetworkManager lives in the LOBBY scene, so menu-started flows
    /// run deferred: make/join the Steam lobby, load "Lobby", connect the
    /// moment the NetworkManager exists.
    public class SteamLobby : MonoBehaviour
    {
        public const int MaxPlayers = 4; // demo cap

        public static SteamLobby I { get; private set; }
        public static bool SteamReady { get; private set; }
        /// One-line human status for menu/status labels.
        public static string Status { get; private set; } = "";

        enum Pending { None, Host, Client }

        CSteamID _lobby;
        bool _isPrivate;
        Pending _pending = Pending.None;
        string _hostAddress = "";
        bool _transportReady;
        string _hostPassword = "";
        float _plocRefresh; // ping location goes stale; republished while hosting

        Callback<LobbyCreated_t> _cbCreated;
        Callback<LobbyEnter_t> _cbEnter;
        Callback<GameLobbyJoinRequested_t> _cbJoinRequested;
        CallResult<LobbyMatchList_t> _crBrowse;

        // ---- the public lobby browser (Meccha style: pick a row, join it) ----
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
        }

        /// The host flips this when a match starts/ends; rows show it.
        public static void SetInGame(bool on)
        {
            if (I == null || !I._lobby.IsValid() || !NetGame.IsHost) return;
            SteamMatchmaking.SetLobbyData(I._lobby, "sz_ingame", on ? "1" : "0");
        }

        /// Region codes; display names live in Loc. Mandatory when hosting
        /// public .
        public static readonly string[] Regions = { "eu", "na", "sa", "asia", "oce", "mea" };

        /// Server tag Loc keys, bitmask order (Meccha-style vibe tags).
        public static readonly string[] TagKeys =
            { "tag.welcome", "tag.beginners", "tag.casual", "tag.tryhard", "tag.mic", "tag.quiet" };

        /// Set by the host UI before creating a public lobby.
        public static string PendingRegion = "eu";
        public static string PendingLang = "";  // "" = adopt the player's game language
        public static int PendingTags;
        public static string PendingName = "";
        /// Host-picked lobby size, 2..MaxPlayers (Meccha lets hosts size their rooms).
        public static int PendingSize = MaxPlayers;

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
                Debug.LogWarning($"[SpellyZombie] Steam unavailable ({e.Message}). LAN panel still works.");
                SteamReady = false;
            }
            if (!SteamReady)
            {
                Status = "Steam not running, offline & LAN only";
                return;
            }
            _cbCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _cbEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            _cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(
                r => SteamMatchmaking.JoinLobby(r.m_steamIDLobby)); // overlay "Join Game"
            _crBrowse = CallResult<LobbyMatchList_t>.Create(OnBrowseList);
            // measure our network coordinates now so ping estimates exist by
            // the time anyone browses a lobby
            SteamNetworkingUtils.InitRelayNetworkAccess();
            Status = $"Steam ready: {SteamFriends.GetPersonaName()}";
        }

        // ------------------------------------------------------ public API --
        public static void HostFriends() => I?.CreateLobby(friendsPrivate: true, "");
        public static void HostPublic(string password = "")
            => I?.CreateLobby(friendsPrivate: false, password);

        /// True while this machine hosts a live lobby (Steam or LAN).
        public static bool Hosting => NetGame.IsHost;

        /// Refill the public lobby list. Worldwide on purpose: the list shows
        /// each row's estimated ping and the player picks, WC3 style.
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
            if (NetGame.Connected) { Status = "leave your lobby first"; return; }
            if (SteamMatchmaking.GetLobbyData(lobby, "sz_pw") == "1"
                && Hash(password ?? "") != SteamMatchmaking.GetLobbyData(lobby, "sz_pwh"))
            {
                Status = "that lobby wants a password";
                return;
            }
            if (I.PingTooHigh(lobby, out int ms, out int cap))
            {
                Status = $"your ping to that host is {ms}ms, lobby allows {cap}";
                return;
            }
            Status = "joining…";
            SteamMatchmaking.JoinLobby(lobby);
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
            Status = "lobby deleted";
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
            if (!SteamReady) { Status = "Steam not running"; return; }
            if (NetGame.Connected || _pending != Pending.None) return;
            _isPrivate = friendsPrivate;
            _hostPassword = friendsPrivate ? "" : (password ?? "").Trim();
            Status = "creating lobby…";
            // private = invite only through Steam, invisible to every search
            SteamMatchmaking.CreateLobby(
                friendsPrivate ? ELobbyType.k_ELobbyTypePrivate : ELobbyType.k_ELobbyTypePublic,
                Mathf.Clamp(PendingSize, 2, MaxPlayers));
        }

        void OnLobbyCreated(LobbyCreated_t r)
        {
            if (r.m_eResult != EResult.k_EResultOK)
            {
                Status = $"lobby failed: {r.m_eResult}";
                return;
            }
            _lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_lobby, "sz_game", "spellyzombie");
            SteamMatchmaking.SetLobbyData(_lobby, "sz_private", _isPrivate ? "1" : "0");
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
            PublishPingLocation();

            _pending = Pending.Host;
            Status = _isPrivate ? "PRIVATE LOBBY, invite friends" : "PUBLIC LOBBY, listed";
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
            SteamNetworkingUtils.ConvertPingLocationToString(ref loc, out string s, 512);
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
            if (_pending == Pending.Host || InstanceFinder.ServerManager.Started)
                return; // the host's own lobby entry
            if (r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Status = "couldn't enter the lobby";
                return;
            }
            _lobby = new CSteamID(r.m_ulSteamIDLobby);
            _hostAddress = SteamMatchmaking.GetLobbyData(_lobby, "sz_host");
            if (string.IsNullOrEmpty(_hostAddress))
            {
                Status = "lobby has no host, try again";
                return;
            }
            _pending = Pending.Client;
            Status = "joined, connecting…";
            EnterVillage();
        }

        // ------------------------------------------- deferred FishNet start --
        void EnterVillage()
        {
            if (SceneManager.GetActiveScene().name != "Lobby")
            {
                LoadingHints.Show(); // one random tip rides every load
                SceneManager.LoadScene("Lobby"); // the NetworkManager lives there
            }
        }

        void Update()
        {
            // ping coordinates drift; a hosting lobby republishes every 30s
            // (also covers the first publish when Steam hadn't measured yet)
            if (SteamReady && _lobby.IsValid() && Time.unscaledTime >= _plocRefresh
                && SteamMatchmaking.GetLobbyOwner(_lobby) == SteamUser.GetSteamID())
            {
                _plocRefresh = Time.unscaledTime + 30f;
                PublishPingLocation();
            }

            if (_pending == Pending.None) return;
            var nm = InstanceFinder.NetworkManager;
            if (nm == null) return; // still riding into the Lobby scene

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
            _pending = Pending.None;
        }

        void UseSteamTransport(FishNet.Managing.NetworkManager nm)
        {
            var fs = nm.GetComponent<global::FishySteamworks.FishySteamworks>();
            if (fs == null)
            {
                fs = nm.gameObject.AddComponent<global::FishySteamworks.FishySteamworks>();
                _transportReady = false;
            }
            if (!_transportReady)
            {
                fs.Initialize(nm, 0); // runtime-added transports self-initialize
                _transportReady = true;
            }
            nm.TransportManager.Transport = fs;
        }

        void OnDestroy()
        {
            if (SteamReady && _lobby.IsValid())
                SteamMatchmaking.LeaveLobby(_lobby);
        }

    }
}
