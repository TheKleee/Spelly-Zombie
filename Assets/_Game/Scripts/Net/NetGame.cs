using FishNet;
using UnityEngine;

namespace SpellyZombie
{
    /// Connection front door (Phase B1). Uses whatever transport the scene's
    /// NetworkManager has active — Tugboat for localhost/LAN testing now,
    /// FishySteamworks once we flip to Steam lobbies. Pure OnGUI, zero scene
    /// dependencies beyond the NetworkManager the user already created.
    public class NetGame : MonoBehaviour
    {
        public static bool Connected =>
            InstanceFinder.NetworkManager != null &&
            (InstanceFinder.ServerManager.Started || InstanceFinder.ClientManager.Started);

        public static bool IsHost =>
            InstanceFinder.NetworkManager != null && InstanceFinder.ServerManager.Started;

        string _address = "127.0.0.1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("NetGame");
            DontDestroyOnLoad(go);
            go.AddComponent<NetGame>();
            go.AddComponent<NetSync>();
        }

        RectTransform _ui;
        bool _uiConnected;
        UnityEngine.UI.Text _status;
        UnityEngine.UI.InputField _addrField;

        void LateUpdate()
        {
            bool show = InstanceFinder.NetworkManager != null
                && !GameMenu.IsOpen && !PoseStudio.IsOpen
                // the MAIN MENU has its own Create/Find Server buttons — the
                // keyboard panel belongs to the lobby and the game
                && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Menu";
            if (!show)
            {
                // hide, never destroy — prefab-adopted UI survives cycles
                if (_ui != null && _ui.gameObject.activeSelf) _ui.gameObject.SetActive(false);
                return;
            }
            if (_ui == null || _uiConnected != Connected) BuildUI();
            else if (!_ui.gameObject.activeSelf) _ui.gameObject.SetActive(true);
            if (Connected && _status != null)
                _status.text = $"● {(IsHost ? "HOSTING" : "CONNECTED")} — {NetSync.RemoteCount + 1} player(s)"
                    + (string.IsNullOrEmpty(SteamLobby.CurrentCode) ? "" : $" · CODE {SteamLobby.CurrentCode}");

            // the cursor is locked in first person, so the panel is KEYBOARD
            // FIRST: H hosts, J joins, I edits the address (Enter commits)
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

        void Host()
        {
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
        }

        void Join() => InstanceFinder.ClientManager.StartConnection(_address);

        void BuildUI()
        {
            UIKit.Retire(_ui); // rebuild is same-frame — never adopt the corpse
            _uiConnected = Connected;
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "NetPanel");

            if (Connected)
            {
                UIKit.Place(_ui, new Vector2(1f, 1f), new Vector2(-14f, -12f), new Vector2(260f, 28f));
                _status = UIKit.Label(_ui, "", 15, new Color(0.5f, 1f, 0.6f), TextAnchor.MiddleRight);
                UIKit.Stretch((RectTransform)_status.transform);
                return;
            }

            // NOTE: Place() pivots at the anchor corner — these coordinates are
            // each element's TOP-LEFT, everything fits inside 266×132
            UIKit.Place(_ui, new Vector2(1f, 1f), new Vector2(-14f, -12f), new Vector2(266f, 132f));
            var back = UIKit.Panel(_ui, skin != null ? skin.PanelGreyDark : null,
                skin != null ? Color.white : new Color(0.12f, 0.12f, 0.15f, 0.85f));
            UIKit.Stretch((RectTransform)back.transform);

            var title = UIKit.Label(_ui, "CO-OP (LAN test)", 15, UIKit.Parchment, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)title.transform, new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(230f, 20f));

            var capH = UIKit.Keycap(_ui, "H", 26f);
            UIKit.Place(capH, new Vector2(0f, 1f), new Vector2(14f, -38f), capH.sizeDelta);
            var host = UIKit.Button(_ui, "HOST", Host, skin != null ? skin.ButtonBrown : null, 15);
            UIKit.Place((RectTransform)host.transform, new Vector2(0f, 1f), new Vector2(52f, -38f), new Vector2(74f, 28f));

            var capJ = UIKit.Keycap(_ui, "J", 26f);
            UIKit.Place(capJ, new Vector2(0f, 1f), new Vector2(138f, -38f), capJ.sizeDelta);
            var join = UIKit.Button(_ui, "JOIN", Join, skin != null ? skin.ButtonGrey : null, 15);
            UIKit.Place((RectTransform)join.transform, new Vector2(0f, 1f), new Vector2(176f, -38f), new Vector2(74f, 28f));

            var capI = UIKit.Keycap(_ui, "I", 26f);
            UIKit.Place(capI, new Vector2(0f, 1f), new Vector2(14f, -74f), capI.sizeDelta);
            _addrField = UIKit.Input(_ui, _address, v => _address = v);
            UIKit.Place((RectTransform)_addrField.transform, new Vector2(0f, 1f), new Vector2(52f, -74f), new Vector2(198f, 28f));

            var hint = UIKit.Label(_ui, "I = edit address · Enter = done", 12,
                new Color(0.7f, 0.7f, 0.75f), TextAnchor.MiddleCenter);
            UIKit.Place((RectTransform)hint.transform, new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(230f, 16f));
        }
    }
}
