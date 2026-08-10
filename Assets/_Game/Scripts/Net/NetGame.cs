using FishNet;
using UnityEngine;

namespace SpellyZombie
{
    /// Connection front door (B1): whatever transport the scene's NetworkManager has active — Tugboat for LAN now, FishySteamworks later.
    ///
    /// THE BOOK STAND RULE (Marko Aug 8): "I don't want multiplayer to be
    /// controlled via a floating permanent ui in the lobby but rather by
    /// walking towards the book stand" — the control panel exists ONLY while
    /// the local player stands at a LobbyStand he placed, with the cursor
    /// freed for real mouse use. Create/join with an optional PASSWORD, the
    /// HOST picks the map (and one day the mode); everyone else leaves a LIKE
    /// on the map they want. The tiny top-right status line stays always —
    /// it is a readout, not a control.
    public class NetGame : MonoBehaviour
    {
        public static bool Connected =>
            InstanceFinder.NetworkManager != null &&
            (InstanceFinder.ServerManager.Started || InstanceFinder.ClientManager.Started);

        public static bool IsHost =>
            InstanceFinder.NetworkManager != null && InstanceFinder.ServerManager.Started;

        /// Host-authoritative law: solo and the host simulate; clients ship intents (netcode §0).
        public static bool IsAuthority => !Connected || IsHost;

        /// The password this lobby demands (host side; empty = open lobby) and
        /// the one we typed to get in (client side) — NetSync's join handshake
        /// reads both at the book stand's word.
        public static string HostPassword = "";
        public static string JoinPassword = "";

        string _address = "127.0.0.1";
        string _password = "";

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
        UnityEngine.UI.InputField _addrField, _passField;

        // status-line cache — rebuild the string only when a shown value changes, not per frame
        int _shownPlayers = -1;
        bool _shownHost;
        string _shownCode, _shownMap;
        int _shownLikes = -1;

        void LateUpdate()
        {
            bool sceneOk = InstanceFinder.NetworkManager != null
                && !GameMenu.IsOpen && !PoseStudio.IsOpen
                // the MAIN MENU has its own Create/Find Server buttons — this panel belongs to lobby + game
                && ActiveScene.Name != "Menu";
            bool near = sceneOk && LobbyStand.NearLocal;

            // ---- the always-on readout (top-right, connected only) ----
            bool corner = sceneOk && Connected;
            if (_uiStatus != null && _uiStatus.gameObject.activeSelf != corner)
                _uiStatus.gameObject.SetActive(corner);
            if (corner && _uiStatus == null) BuildCorner();
            if (corner && _statusCorner != null) RefreshStatus(_statusCorner);

            // ---- the stand panel (control — exists only AT the stand) ----
            if (!near)
            {
                if (_ui != null && _ui.gameObject.activeSelf) _ui.gameObject.SetActive(false);
                LobbyStand.HoldPanel(false);
                return;
            }
            if (_ui == null || _uiConnected != Connected || !_uiNear) BuildUI();
            else if (!_ui.gameObject.activeSelf) _ui.gameObject.SetActive(true);
            _uiNear = true;
            LobbyStand.HoldPanel(true); // keeps the cursor free for the mouse

            if (Connected)
            {
                if (_status != null) RefreshStatus(_status);
                RefreshMapRow();
            }

            // keyboard stays first-class at the stand: H hosts, J joins,
            // I edits the address (Enter commits)
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || Connected || UIKit.Typing) return;
            if (kb.hKey.wasPressedThisFrame) Host();
            else if (kb.jKey.wasPressedThisFrame) Join();
            else if (kb.iKey.wasPressedThisFrame && _addrField != null)
            {
                UnityEngine.EventSystems.EventSystem.current?
                    .SetSelectedGameObject(_addrField.gameObject);
                _addrField.ActivateInputField();
            }
        }

        void RefreshStatus(UnityEngine.UI.Text into)
        {
            int players = NetSync.RemoteCount + 1;
            string code = SteamLobby.CurrentCode;
            if (players == _shownPlayers && IsHost == _shownHost && code == _shownCode) return;
            _shownPlayers = players; _shownHost = IsHost; _shownCode = code;
            string line = $"● {(IsHost ? "HOSTING" : "CONNECTED")}, {players} player(s)"
                + (string.IsNullOrEmpty(code) ? "" : $" · CODE {code}");
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
            _mapLabel.text = likes > 0 ? $"MAP: {map} · ♥{likes}" : $"MAP: {map}";
        }

        void Host()
        {
            HostPassword = _password ?? "";
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
        }

        void Join()
        {
            JoinPassword = _password ?? "";
            InstanceFinder.ClientManager.StartConnection(_address);
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
            UIKit.Retire(_ui); // rebuild is same-frame — never adopt the corpse
            _uiConnected = Connected;
            _shownPlayers = -1; // fresh labels — invalidate the caches
            _shownMap = null; _shownLikes = -1;
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "BookStandPanel");

            // the stand menu sits mid-screen: it is a MOUSE surface now, not a
            // corner widget (Marko: "use the menu with their mouse normally")
            if (Connected)
            {
                UIKit.Place(_ui, new Vector2(0.5f, 0.5f), new Vector2(-160f, 90f), new Vector2(320f, 150f));
                var backC = UIKit.Panel(_ui, skin != null ? skin.PanelGreyDark : null,
                    skin != null ? Color.white : new Color(0.12f, 0.12f, 0.15f, 0.85f));
                UIKit.Stretch((RectTransform)backC.transform);

                var titleC = UIKit.Label(_ui, "BOOK STAND", 15, UIKit.Parchment, TextAnchor.MiddleCenter, true);
                UIKit.Place((RectTransform)titleC.transform, new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(280f, 20f));

                _status = UIKit.Label(_ui, "", 14, new Color(0.5f, 1f, 0.6f), TextAnchor.MiddleCenter);
                UIKit.Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(300f, 18f));

                var mode = UIKit.Label(_ui, $"MODE: {MatchLobby.SelectedMode}", 14, UIKit.Parchment, TextAnchor.MiddleLeft);
                UIKit.Place((RectTransform)mode.transform, new Vector2(0f, 1f), new Vector2(16f, -56f), new Vector2(200f, 18f));

                _mapLabel = UIKit.Label(_ui, "", 14, UIKit.Parchment, TextAnchor.MiddleLeft);
                UIKit.Place((RectTransform)_mapLabel.transform, new Vector2(0f, 1f), new Vector2(16f, -82f), new Vector2(200f, 18f));

                if (IsHost)
                {
                    // the host PICKS (Marko: "you have to be the host of the lobby to chose the map")
                    var change = UIKit.Button(_ui, "CHANGE", MatchLobby.CycleMap,
                        skin != null ? skin.ButtonBrown : null, 13);
                    UIKit.Place((RectTransform)change.transform, new Vector2(1f, 1f), new Vector2(-98f, -78f), new Vector2(84f, 26f));
                }
                else
                {
                    // everyone else LIKES the map they want to play
                    var like = UIKit.Button(_ui, "♥ LIKE",
                        () => NetSync.SendMapLike(MatchLobby.HostMap),
                        skin != null ? skin.ButtonGrey : null, 13);
                    UIKit.Place((RectTransform)like.transform, new Vector2(1f, 1f), new Vector2(-98f, -78f), new Vector2(84f, 26f));
                }

                var hintC = UIKit.Label(_ui, "ENTER = ready up · walk away to close", 12,
                    new Color(0.7f, 0.7f, 0.75f), TextAnchor.MiddleCenter);
                UIKit.Place((RectTransform)hintC.transform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(300f, 16f));
                return;
            }

            UIKit.Place(_ui, new Vector2(0.5f, 0.5f), new Vector2(-160f, 105f), new Vector2(320f, 210f));
            var back = UIKit.Panel(_ui, skin != null ? skin.PanelGreyDark : null,
                skin != null ? Color.white : new Color(0.12f, 0.12f, 0.15f, 0.85f));
            UIKit.Stretch((RectTransform)back.transform);

            var title = UIKit.Label(_ui, "BOOK STAND · CO-OP", 15, UIKit.Parchment, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)title.transform, new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(280f, 20f));

            var capH = UIKit.Keycap(_ui, "H", 26f);
            UIKit.Place(capH, new Vector2(0f, 1f), new Vector2(20f, -38f), capH.sizeDelta);
            var host = UIKit.Button(_ui, "HOST", Host, skin != null ? skin.ButtonBrown : null, 15);
            UIKit.Place((RectTransform)host.transform, new Vector2(0f, 1f), new Vector2(58f, -38f), new Vector2(90f, 28f));

            var capJ = UIKit.Keycap(_ui, "J", 26f);
            UIKit.Place(capJ, new Vector2(0f, 1f), new Vector2(162f, -38f), capJ.sizeDelta);
            var join = UIKit.Button(_ui, "JOIN", Join, skin != null ? skin.ButtonGrey : null, 15);
            UIKit.Place((RectTransform)join.transform, new Vector2(0f, 1f), new Vector2(200f, -38f), new Vector2(90f, 28f));

            var capI = UIKit.Keycap(_ui, "I", 26f);
            UIKit.Place(capI, new Vector2(0f, 1f), new Vector2(20f, -76f), capI.sizeDelta);
            _addrField = UIKit.Input(_ui, _address, v => _address = v);
            UIKit.Place((RectTransform)_addrField.transform, new Vector2(0f, 1f), new Vector2(58f, -76f), new Vector2(232f, 28f));

            var passLbl = UIKit.Label(_ui, "password (optional)", 12,
                new Color(0.7f, 0.7f, 0.75f), TextAnchor.MiddleLeft);
            UIKit.Place((RectTransform)passLbl.transform, new Vector2(0f, 1f), new Vector2(20f, -112f), new Vector2(160f, 16f));
            _passField = UIKit.Input(_ui, _password, v => _password = v);
            UIKit.Place((RectTransform)_passField.transform, new Vector2(0f, 1f), new Vector2(20f, -132f), new Vector2(270f, 28f));
            _passField.contentType = UnityEngine.UI.InputField.ContentType.Password;

            var hint = UIKit.Label(_ui, "host sets the password · joiners must match it", 12,
                new Color(0.7f, 0.7f, 0.75f), TextAnchor.MiddleCenter);
            UIKit.Place((RectTransform)hint.transform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(300f, 16f));
        }
    }
}
