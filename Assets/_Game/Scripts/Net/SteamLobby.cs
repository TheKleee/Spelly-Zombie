using FishNet;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// STEAM LOBBIES — Marko's two modes:
    ///   FRIENDS: host gets a short CODE to read out loud; friends type it in
    ///   (the Steam overlay invite + friends-list "Join Game" also work).
    ///   PUBLIC: Quick Join drops you into any open lobby with a free seat —
    ///   none found means YOU become the host and wait for randoms.
    ///
    /// All lobbies are Steam-typed PUBLIC so code lookup works between
    /// strangers; friend lobbies carry sz_private=1 so Quick Join skips them.
    /// Transport: FishySteamworks is swapped onto the NetworkManager at
    /// connect time — the H/J LAN panel keeps Tugboat for dev testing.
    ///
    /// The NetworkManager lives in the LOBBY scene, so menu-started flows
    /// run deferred: make/join the Steam lobby → load "Lobby" → connect the
    /// moment the NetworkManager exists.
    public class SteamLobby : MonoBehaviour
    {
        public const int MaxPlayers = 4; // ours to tune (Marko's rule)

        public static SteamLobby I { get; private set; }
        public static bool SteamReady { get; private set; }
        /// The short join code while hosting/joined — panels display it.
        public static string CurrentCode { get; private set; } = "";
        /// One-line human status for menu/status labels.
        public static string Status { get; private set; } = "";

        enum Pending { None, Host, Client }

        CSteamID _lobby;
        bool _isPrivate;
        Pending _pending = Pending.None;
        string _hostAddress = "";
        bool _transportReady;

        Callback<LobbyCreated_t> _cbCreated;
        Callback<LobbyEnter_t> _cbEnter;
        Callback<GameLobbyJoinRequested_t> _cbJoinRequested;
        CallResult<LobbyMatchList_t> _crList;
        bool _quickJoinSearch;
        string _pendingCode = "";

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
            // pumps callbacks — touching Initialized bootstraps it.
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
            _crList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
            Status = $"Steam ready: {SteamFriends.GetPersonaName()}";
        }

        // ------------------------------------------------------ public API --
        public static void HostFriends() => I?.CreateLobby(friendsPrivate: true);
        public static void HostPublic() => I?.CreateLobby(friendsPrivate: false);
        public static void JoinByCode(string code) => I?.FindByCode(code);
        public static void QuickJoin() => I?.FindPublic();

        /// Steam's own invite dialog for the current lobby (host presses this).
        public static void OpenInviteOverlay()
        {
            if (I != null && I._lobby.IsValid())
                SteamFriends.ActivateGameOverlayInviteDialog(I._lobby);
        }

        // ---------------------------------------------------------- hosting --
        void CreateLobby(bool friendsPrivate)
        {
            if (!SteamReady) { Status = "Steam not running"; return; }
            if (NetGame.Connected || _pending != Pending.None) return;
            _isPrivate = friendsPrivate;
            Status = "creating lobby…";
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, MaxPlayers);
        }

        void OnLobbyCreated(LobbyCreated_t r)
        {
            if (r.m_eResult != EResult.k_EResultOK)
            {
                Status = $"lobby failed: {r.m_eResult}";
                return;
            }
            _lobby = new CSteamID(r.m_ulSteamIDLobby);
            CurrentCode = MakeCode();
            SteamMatchmaking.SetLobbyData(_lobby, "sz_game", "spellyzombie");
            SteamMatchmaking.SetLobbyData(_lobby, "sz_code", CurrentCode);
            SteamMatchmaking.SetLobbyData(_lobby, "sz_private", _isPrivate ? "1" : "0");
            SteamMatchmaking.SetLobbyData(_lobby, "sz_host",
                SteamUser.GetSteamID().m_SteamID.ToString());

            _pending = Pending.Host;
            Status = $"LOBBY {CurrentCode}, {(_isPrivate ? "friends" : "public")}";
            Debug.Log($"[SpellyZombie] Steam lobby up: CODE {CurrentCode}. Overlay invites work too.");
            EnterVillage();
        }

        // ---------------------------------------------------------- joining --
        void FindByCode(string code)
        {
            if (!SteamReady || string.IsNullOrWhiteSpace(code)) return;
            _pendingCode = code.Trim().ToUpperInvariant();
            _quickJoinSearch = false;
            Status = $"searching for {_pendingCode}…";
            SteamMatchmaking.AddRequestLobbyListStringFilter("sz_game", "spellyzombie", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter("sz_code", _pendingCode, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            _crList.Set(SteamMatchmaking.RequestLobbyList());
        }

        void FindPublic()
        {
            if (!SteamReady) return;
            _quickJoinSearch = true;
            Status = "looking for open lobbies…";
            SteamMatchmaking.AddRequestLobbyListStringFilter("sz_game", "spellyzombie", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter("sz_private", "0", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            _crList.Set(SteamMatchmaking.RequestLobbyList());
        }

        void OnLobbyList(LobbyMatchList_t r, bool ioFailure)
        {
            if (ioFailure || r.m_nLobbiesMatching == 0)
            {
                if (_quickJoinSearch)
                {
                    // his rule: nobody out there = you host and randoms find YOU
                    Status = "no open lobbies, hosting a public one";
                    CreateLobby(friendsPrivate: false);
                }
                else
                {
                    Status = $"code {_pendingCode} not found";
                }
                return;
            }
            Status = "joining…";
            SteamMatchmaking.JoinLobby(SteamMatchmaking.GetLobbyByIndex(0));
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
            CurrentCode = SteamMatchmaking.GetLobbyData(_lobby, "sz_code");
            _pending = Pending.Client;
            Status = $"joined {CurrentCode}, connecting…";
            EnterVillage();
        }

        // ------------------------------------------- deferred FishNet start --
        void EnterVillage()
        {
            if (SceneManager.GetActiveScene().name != "Lobby")
                SceneManager.LoadScene("Lobby"); // the NetworkManager lives there
        }

        void Update()
        {
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

        /// 5 characters, no look-alikes — readable over voice chat.
        static string MakeCode()
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var sb = new System.Text.StringBuilder(5);
            for (int i = 0; i < 5; i++)
                sb.Append(alphabet[Random.Range(0, alphabet.Length)]);
            return sb.ToString();
        }
    }
}
