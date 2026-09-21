using FishNet;
using UnityEngine;

namespace SpellyZombie
{
    /// Connection front door: uses whatever transport the scene's NetworkManager
    /// has active (Tugboat LAN or FishySteamworks). The control panel exists only
    /// while the local player stands at a LobbyStand, with the cursor freed;
    /// the top-right status line is an always-on readout, not a control.
    public class NetGame : MonoBehaviour
    {
        /// Asks FishNet's list directly: InstanceFinder logs on every call while
        /// no NetworkManager is open (a map played straight from the editor).
        public static bool HasManager => FishNet.Managing.NetworkManager.Instances.Count > 0;

        public static bool Connected =>
            HasManager &&
            (InstanceFinder.ServerManager.Started || InstanceFinder.ClientManager.Started);

        public static bool IsHost =>
            HasManager && InstanceFinder.ServerManager.Started;

        /// Host-authoritative law: solo and the host simulate; clients ship intents (netcode §0).
        public static bool IsAuthority => !Connected || IsHost;

        /// A client connection was asked for and has not reported back yet.
        /// Multipass errors when a client that never started is stopped.
        public static bool ClientStarting;

        /// The password this lobby demands (host side; empty = open lobby) and
        /// the one we typed to get in (client side) - NetSync's join handshake reads both.
        public static string HostPassword = "";
        public static string JoinPassword = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("NetGame");
            DontDestroyOnLoad(go);
            go.AddComponent<NetGame>();
            go.AddComponent<NetSync>();
        }

        RectTransform _ui;       // the stand panel (control)
        RectTransform _uiStatus; // the top-right readout (always-on while connected)
        bool _uiConnected;
        bool _uiNear;
        UnityEngine.UI.Text _status, _statusCorner, _mapLabel;

        // status-line cache - rebuild the string only when a shown value changes, not per frame
        int _shownPlayers = -1;
        bool _shownHost;
        string _shownCode, _shownMap;
        int _shownLikes = -1;
        int _rosterStamp = -1;
        string _codeEdit = "";

        static int ReadyCountStamp()
        {
            int n = 0;
            foreach (var id in NetSync.RemoteIds)
                if (MatchLobby.IsReady(id)) n++;
            return n;
        }

        void LateUpdate()
        {
            RefreshNotice();
            bool sceneOk = HasManager
                && !GameMenu.IsOpen && !PoseStudio.IsOpen
                // the MAIN MENU has its own Create/Find Server buttons - this panel belongs to lobby + game
                && ActiveScene.Name != "Menu";
            // only the host interacts with the book stand; clients use ESC
            bool near = sceneOk && LobbyStand.NearLocal && (!Connected || IsHost);

            // ---- the always-on readout (top-right, connected only) ----
            bool corner = sceneOk && Connected;
            if (_uiStatus != null && _uiStatus.gameObject.activeSelf != corner)
                _uiStatus.gameObject.SetActive(corner);
            if (corner && _uiStatus == null) BuildCorner();
            if (corner && _statusCorner != null) RefreshStatus(_statusCorner);

            // ---- the stand panel (control - exists only AT the stand) ----
            if (!near)
            {
                MapPicker.Close(); // it belongs to the stand
                LanLobby.Listen = false;
                if (_ui != null && _ui.gameObject.activeSelf) _ui.gameObject.SetActive(false);
                LobbyStand.HoldPanel(false);
                _uiNear = false; // next arrival rebuilds and refreshes the list
                return;
            }
            if (_ui == null || _uiConnected != Connected || !_uiNear)
            {
                if (!Connected) SteamLobby.RefreshList(); // arriving at the stand refreshes the list
                BuildUI();
            }
            else if (!_ui.gameObject.activeSelf) _ui.gameObject.SetActive(true);
            _uiNear = true;
            LobbyStand.HoldPanel(true); // keeps the cursor free for the mouse
            LanLobby.Listen = !Connected && !LobbyBrowserUI.TabHost; // the Join list hears lobbies on this network

            if (Connected)
            {
                if (_status != null) RefreshStatus(_status);
                RefreshMapRow();

                // the roster redraws when players, readiness or bans change
                int stamp = NetSync.RemoteCount * 100
                    + (MatchLobby.LocalReady ? 1 : 0) + ReadyCountStamp() * 7
                    + BanList.All.Count * 1000;
                if (stamp != _rosterStamp) { _rosterStamp = stamp; BuildUI(); }
            }
            else
            {
                // the browser redraws when the list or the search changes
                if (LobbyBrowserUI.Stamp != _rosterStamp)
                {
                    _rosterStamp = LobbyBrowserUI.Stamp;
                    BuildUI();
                }
            }
        }

        // ---- the version notice: one line on top of the screen, in the menu and the lobby ----
        RectTransform _uiNotice;
        UnityEngine.UI.Text _noticeLabel;
        string _noticeShown;

        void RefreshNotice()
        {
            string text = NetVersion.Notice;
            bool show = text.Length > 0 && (ActiveScene.Name == "Menu" || ActiveScene.Name == "Lobby");
            if (_uiNotice == null)
            {
                if (!show) return;
                _uiNotice = UIKit.Group(UIKit.Root, "VersionNotice");
                UIKit.Place(_uiNotice, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(980f, 34f));
                var back = UIKit.Panel(_uiNotice, null, new Color(0.05f, 0.06f, 0.09f, 0.86f));
                UIKit.Stretch((RectTransform)back.transform);
                _noticeLabel = UIKit.Label(_uiNotice, "", 17, new Color(1f, 0.78f, 0.4f), TextAnchor.MiddleCenter, true);
                UIKit.Stretch((RectTransform)_noticeLabel.transform);
                _noticeShown = null;
            }
            if (_uiNotice.gameObject.activeSelf != show) _uiNotice.gameObject.SetActive(show);
            if (show && text != _noticeShown) { _noticeShown = text; _noticeLabel.text = text; }
        }

        void RefreshStatus(UnityEngine.UI.Text into)
        {
            int players = NetSync.RemoteCount + 1;
            if (players == _shownPlayers && IsHost == _shownHost) return;
            _shownPlayers = players; _shownHost = IsHost;
            string line = Loc.F(IsHost ? "net.hosting" : "net.connected", players);
            if (_status != null) _status.text = line;
            if (_statusCorner != null) _statusCorner.text = line;
        }

        void RefreshMapRow()
        {
            if (_mapLabel == null) return;
            string map = MatchLobby.HostMap;
            int likes = NetSync.LikeCount(map);
            if (map == _shownMap && likes == _shownLikes) return;
            _shownMap = map; _shownLikes = likes;
            _mapLabel.text = likes > 0 ? Loc.F("net.maplikes", map, likes) : Loc.F("net.map", map);
        }

        /// LAN rides Tugboat, one road of the Multipass on the NetworkManager.
        static void UseLan()
        {
            var nm = InstanceFinder.NetworkManager;
            var mp = nm != null ? nm.GetComponent<FishNet.Transporting.Multipass.Multipass>() : null;
            if (mp != null) mp.SetClientTransport<FishNet.Transporting.Tugboat.Tugboat>();
        }

        /// ★ A lobby on this PC through Tugboat, for when Steam is not there.
        /// A listed one calls out on the local network (LanLobby).
        public static void HostLocal(string password, bool listed)
        {
            if (!HasManager || Connected || ClientStarting) return;
            HostPassword = password ?? "";
            UseLan();
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
            ClientStarting = true;
            LanLobby.Call(listed);
        }

        /// Joins a lobby heard on this network.
        public static void JoinLocal(string address, string password)
        {
            if (!HasManager || Connected || ClientStarting) return;
            JoinPassword = password ?? "";
            UseLan();
            InstanceFinder.ClientManager.StartConnection(address);
            ClientStarting = true;
        }

        void BuildCorner()
        {
            UIKit.Retire(_uiStatus);
            _uiStatus = UIKit.Group(UIKit.Root, "NetStatus");
            UIKit.Place(_uiStatus, new Vector2(1f, 1f), new Vector2(-14f, -12f), new Vector2(260f, 28f));
            _statusCorner = UIKit.Label(_uiStatus, "", 15, new Color(0.5f, 1f, 0.6f), TextAnchor.MiddleRight);
            UIKit.Stretch((RectTransform)_statusCorner.transform);
            _shownPlayers = -1;
        }

        void BuildUI()
        {
            UIKit.Retire(_ui); // rebuild is same-frame - never adopt the corpse
            _uiConnected = Connected;
            _shownPlayers = -1; // fresh labels - invalidate the caches
            _shownMap = null; _shownLikes = -1;
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "BookStandPanel");

            // floating elements over the world, no container box
            if (Connected)
            {
                BuildHostPanel(skin);
                return;
            }
            BuildOfflinePanel(skin);
        }

        // ---- the host controller: same visual system as the tab screen ----
        void BuildHostPanel(UISkin skin)
        {
            UIKit.Place(_ui, new Vector2(0.5f, 0.5f), new Vector2(-215f, 10f), new Vector2(600f, 620f));
            var back = UIKit.Panel(_ui, null, new Color(0.05f, 0.06f, 0.09f, 0.86f));
            UIKit.Stretch((RectTransform)back.transform);

            var title = UIKit.Label(_ui, Loc.T("stand.hosting"), 19,
                new Color(0.55f, 1f, 0.62f), TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)title.transform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(560f, 26f));
            _status = UIKit.Label(_ui, "", 13, new Color(0.5f, 1f, 0.6f), TextAnchor.MiddleCenter);
            UIKit.Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(560f, 18f));

            // left: who is here, ready ticks, kick and ban
            var ph = UIKit.Label(_ui, Loc.T("stand.players"), 15, new Color(1f, 0.92f, 0.75f), TextAnchor.MiddleLeft, true);
            UIKit.Place((RectTransform)ph.transform, new Vector2(0f, 1f), new Vector2(16f, -64f), new Vector2(200f, 20f));
            // the list scrolls: a lobby holds as many as the host allows
            var roster = UIKit.Scroll(_ui, "Roster", 284f, 430f, 0f);
            UIKit.Place((RectTransform)roster.parent, new Vector2(0f, 1f), new Vector2(0f, -88f), new Vector2(284f, 430f));
            int row = 0;
            RosterRow(roster, row++, SteamLobby.SteamReady ? Steamworks.SteamFriends.GetPersonaName() + " (you)" : "you",
                MatchLobby.LocalReady, -1, 0UL);
            foreach (var id in NetSync.RemoteIds)
            {
                NetSync.IdentityOf(id, out string pname, out ulong sid);
                RosterRow(roster, row++, string.IsNullOrEmpty(pname) ? $"player {id}" : pname,
                    MatchLobby.IsReady(id), id, sid);
            }
            UIKit.Gap(roster, 8f);
            foreach (var ban in BanList.All)
            {
                var line = UIKit.Row(UIKit.Group(roster, "Ban" + row++), 284f, 28f);
                var lbl = UIKit.Label(line, Loc.F("stand.banned", ban.Value), 12,
                    new Color(1f, 0.55f, 0.5f), TextAnchor.MiddleLeft);
                UIKit.Place((RectTransform)lbl.transform, new Vector2(0f, 1f), new Vector2(16f, -2f), new Vector2(170f, 18f));
                ulong sid = ban.Key;
                LobbyBrowserUI.Chip(line, 192f, 0f, 88f, Loc.T("stand.unban"), false,
                    () => { BanList.Unban(sid); BuildUI(); });
            }

            // right: the match settings and the big verbs
            float rx = 310f, ry = -64f;
            // the map opens the picker: every map at once, the picked one described
            LobbyBrowserUI.Chip(_ui, rx, ry, 270f, Loc.F("net.map", MatchLobby.SelectedMap), MapPicker.IsOpen,
                () => MapPicker.Open(BuildUI));
            ry -= 30f;
            LobbyBrowserUI.ArrowRow(_ui, rx, ry, 270f, Loc.F("stand.duration", MatchLobby.DurationLabel),
                () => { MatchLobby.DurationMin = Mathf.Max(0, MatchLobby.DurationMin - 5); BuildUI(); },
                () => { MatchLobby.DurationMin = Mathf.Min(15, MatchLobby.DurationMin + 5); BuildUI(); },
                MatchLobby.DurationMin > 0, MatchLobby.DurationMin < 15);
            ry -= 30f;
            LobbyBrowserUI.ArrowRow(_ui, rx, ry, 270f, Loc.F("stand.share", MatchLobby.AcolytePercent),
                () => { MatchLobby.AcolytePercent = Mathf.Max(10, MatchLobby.AcolytePercent - 10); BuildUI(); },
                () => { MatchLobby.AcolytePercent = Mathf.Min(90, MatchLobby.AcolytePercent + 10); BuildUI(); },
                MatchLobby.AcolytePercent > 10, MatchLobby.AcolytePercent < 90);
            ry -= 34f;

            // no seed control: seeds come from the daily pool, rolled by MatchLobby
            var pwField = UIKit.Input(_ui, _codeEdit, v => _codeEdit = v);
            pwField.contentType = UnityEngine.UI.InputField.ContentType.Password;
            UIKit.Place((RectTransform)pwField.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(120f, 26f));
            LobbyBrowserUI.Chip(_ui, rx + 128f, ry, 110f, Loc.T("stand.setpw"), false,
                () => SteamLobby.SetPassword(_codeEdit));
            ry -= 38f;

            LobbyBrowserUI.Chip(_ui, rx, ry, 130f, Loc.T("stand.readycall"), false, () =>
            {
                NetSync.PushReadyCall();
                MatchLobby.OnReadyCall();
            });
            LobbyBrowserUI.Chip(_ui, rx + 140f, ry, 130f, Loc.T("stand.invite"), false,
                SteamLobby.OpenInviteOverlay);
            ry -= 44f;

            bool can = MatchLobby.CanStart;
            var startBtn = UIKit.Button(_ui, can ? Loc.T("stand.start") : Loc.T("stand.waiting"),
                MatchLobby.StartMatch,
                skin != null ? (can ? skin.ButtonBrown : skin.ButtonGrey) : null, 16);
            UIKit.Place((RectTransform)startBtn.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(270f, 40f));
            ry -= 48f;
            var closeBtn = UIKit.Button(_ui, Loc.T("stand.delete"), SteamLobby.DeleteLobby,
                skin != null ? skin.ButtonRed : null, 14);
            UIKit.Place((RectTransform)closeBtn.transform, new Vector2(0f, 1f), new Vector2(rx, ry), new Vector2(270f, 36f));
            ry -= 44f;
            LobbyBrowserUI.MapPicture(_ui, rx, ry, 270f);
        }

        void RosterRow(RectTransform list, int index, string name, bool ready, int clientId, ulong steamId)
        {
            var line = UIKit.Row(UIKit.Group(list, "Roster" + index), 284f, 28f);
            var lbl = UIKit.Label(line, (ready ? "OK " : "-- ") + name, 14,
                ready ? new Color(0.6f, 1f, 0.65f) : new Color(0.95f, 0.93f, 0.85f), TextAnchor.MiddleLeft);
            UIKit.Place((RectTransform)lbl.transform, new Vector2(0f, 1f), new Vector2(16f, -2f), new Vector2(150f, 18f));
            if (clientId >= 0)
            {
                int cid = clientId;
                ulong sid = steamId;
                string pname = name;
                LobbyBrowserUI.Chip(line, 170f, 0f, 54f, Loc.T("stand.kick"), false, () => NetSync.Kick(cid));
                LobbyBrowserUI.Chip(line, 230f, 0f, 50f, Loc.T("stand.ban"), false, () =>
                {
                    BanList.Ban(sid, pname);
                    NetSync.Kick(cid);
                });
            }
        }

        // ---- alone at the stand: the two-tab screen ----
        void BuildOfflinePanel(UISkin skin)
        {
            UIKit.Place(_ui, new Vector2(0.5f, 0.5f), new Vector2(-215f, 10f), new Vector2(600f, 620f));
            var back = UIKit.Panel(_ui, null, new Color(0.05f, 0.06f, 0.09f, 0.86f));
            UIKit.Stretch((RectTransform)back.transform);
            LobbyBrowserUI.Changed = BuildUI;

            void Tab(float x, string key, bool active, System.Action click)
            {
                var b = UIKit.Button(_ui, Loc.T(key), click, null, 17,
                    active ? Color.white : new Color(0.6f, 0.6f, 0.66f));
                var img = b.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                    img.color = active ? new Color(0.72f, 0.48f, 0.18f, 0.96f)
                        : new Color(0.10f, 0.11f, 0.15f, 0.92f);
                UIKit.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(x, -10f), new Vector2(120f, 30f));
            }
            Tab(-64f, "stand.tab.host", LobbyBrowserUI.TabHost,
                () => { LobbyBrowserUI.TabHost = true; BuildUI(); });
            Tab(64f, "stand.tab.join", !LobbyBrowserUI.TabHost,
                () => { LobbyBrowserUI.TabHost = false; BuildUI(); });

            if (LobbyBrowserUI.TabHost)
                LobbyBrowserUI.BuildHostView(_ui, 20f, -56f, 560f);
            else
                LobbyBrowserUI.BuildJoinView(_ui, 20f, -56f, 560f, 6);
        }
    }
}
