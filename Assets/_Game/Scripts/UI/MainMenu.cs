using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// The main menu, per the design sketch (layout copied from Meccha Chameleon):
    /// game name top-left, server buttons on the left, your poseable character on
    /// a pedestal right-of-center (PoseStudio in AlwaysOpen mode handles that).
    ///
    /// Create Server starts a local match until EOS lands; Find Server is the
    /// future server browser. Poses are saved HERE — drawing on your character
    /// happens pre-match in the lobby, never in the menu.
    public class MainMenu : MonoBehaviour
    {
        bool _settingsOpen;
        float _sensitivity;
        string _status = "";

        RectTransform _ui, _settingsUi;
        UnityEngine.UI.Text _statusLabel;

        void Awake()
        {
            _sensitivity = PlayerPrefs.GetFloat("sz_look_sens", 0.12f);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Start() => BuildUI();

        void OnDestroy()
        {
            // the canvas persists across scenes — the menu HIDES (never
            // destroys: prefab-adopted UI must survive Menu→Lobby→Menu)
            if (_ui != null) _ui.gameObject.SetActive(false);
        }

        void BuildUI()
        {
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "MainMenu");
            UIKit.Stretch(_ui);

            // title top-left, button column on the left — Marko's Meccha layout
            var title = UIKit.Label(_ui, "Spelly Zombie", 58, UIKit.Parchment, TextAnchor.MiddleLeft, true);
            UIKit.Place((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(46f, -66f), new Vector2(700f, 80f));
            var tag = UIKit.Label(_ui, "draw fast. die funny.", 20, UIKit.Gold, TextAnchor.MiddleLeft);
            UIKit.Place((RectTransform)tag.transform, new Vector2(0f, 1f), new Vector2(50f, -116f), new Vector2(500f, 26f));

            float y = -20f;
            void MenuButton(string label, System.Action act, Sprite sprite = null)
            {
                var b = UIKit.Button(_ui, label, act, sprite ?? (skin != null ? skin.ButtonBrown : null), 19);
                UIKit.Place((RectTransform)b.transform, new Vector2(0f, 0.5f), new Vector2(46f, y), new Vector2(250f, 50f));
                ((RectTransform)b.transform).pivot = new Vector2(0f, 0.5f);
                y -= 60f;
            }

            // menu → LOBBY (pick team, ready up) — Marko's two modes:
            // Create Server = host a FRIENDS lobby (code + Steam invites);
            // Find Server = join by code / Quick Join a public lobby.
            // No Steam running = plain offline village (LAN panel still works).
            MenuButton("Create Server", () =>
            {
                if (SteamLobby.SteamReady) SteamLobby.HostFriends();
                else SceneManager.LoadScene("Lobby");
            });
            MenuButton("Find Server", ToggleJoin);
            MenuButton("Settings", ToggleSettings, skin != null ? skin.ButtonGrey : null);
            MenuButton("Quit Game", GameMenu.QuitGame, skin != null ? skin.ButtonRed : null);

            _statusLabel = UIKit.Label(_ui, _status, 15, UIKit.Parchment, TextAnchor.UpperLeft);
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIKit.Place((RectTransform)_statusLabel.transform, new Vector2(0f, 0.5f), new Vector2(46f, y - 40f), new Vector2(300f, 110f));
        }

        void Update()
        {
            // the Steam layer narrates itself (lobby code, search progress,
            // "Steam not running") — the status corner is its mouth
            if (_statusLabel != null && !string.IsNullOrEmpty(SteamLobby.Status))
                _statusLabel.text = SteamLobby.Status;
        }

        RectTransform _joinUi;
        string _code = "";

        /// Marko's join flow: type a friend's CODE, or Quick Join a public
        /// lobby (none open = you host one and randoms find you).
        void ToggleJoin()
        {
            if (_joinUi != null)
            {
                Destroy(_joinUi.gameObject);
                _joinUi = null;
                return;
            }
            if (!SteamLobby.SteamReady)
            {
                _statusLabel.text = "Steam isn't running. Start Steam to host or join online. (Offline: Create Server opens the village solo.)";
                return;
            }

            var skin = UISkin.I;
            _joinUi = UIKit.Group(_ui, "JoinPanel");
            UIKit.Place(_joinUi, new Vector2(0f, 0.5f), new Vector2(330f, -80f), new Vector2(320f, 168f));
            var back = UIKit.Panel(_joinUi, skin != null ? skin.PanelBrown : null,
                skin != null ? Color.white : new Color(0.22f, 0.17f, 0.12f, 0.95f));
            UIKit.Stretch((RectTransform)back.transform);

            var label = UIKit.Label(_joinUi, "Friend's lobby code:", 16, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place((RectTransform)label.transform, new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(270f, 22f));

            var codeField = UIKit.Input(_joinUi, _code, v => _code = v);
            UIKit.Place((RectTransform)codeField.transform, new Vector2(0.5f, 1f), new Vector2(-40f, -52f), new Vector2(180f, 30f));

            var joinBtn = UIKit.Button(_joinUi, "JOIN", () => SteamLobby.JoinByCode(_code),
                skin != null ? skin.ButtonBrown : null, 15);
            UIKit.Place((RectTransform)joinBtn.transform, new Vector2(0.5f, 1f), new Vector2(100f, -52f), new Vector2(76f, 30f));

            var quick = UIKit.Button(_joinUi, "QUICK JOIN A PUBLIC LOBBY", () => SteamLobby.QuickJoin(),
                skin != null ? skin.ButtonGrey : null, 15);
            UIKit.Place((RectTransform)quick.transform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(270f, 36f));
        }

        void ToggleSettings()
        {
            _settingsOpen = !_settingsOpen;
            if (!_settingsOpen)
            {
                if (_settingsUi != null) Destroy(_settingsUi.gameObject);
                _settingsUi = null;
                return;
            }

            var skin = UISkin.I;
            _settingsUi = UIKit.Group(_ui, "Settings");
            UIKit.Place(_settingsUi, new Vector2(0f, 0.5f), new Vector2(330f, -20f), new Vector2(320f, 150f));
            var back = UIKit.Panel(_settingsUi, skin != null ? skin.PanelBrown : null,
                skin != null ? Color.white : new Color(0.22f, 0.17f, 0.12f, 0.95f));
            UIKit.Stretch((RectTransform)back.transform);

            var label = UIKit.Label(_settingsUi, $"Mouse sensitivity: {_sensitivity:0.00}",
                16, UIKit.Ink, TextAnchor.MiddleLeft, true);
            UIKit.Place((RectTransform)label.transform, new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(270f, 22f));

            var slider = UIKit.Slider(_settingsUi, 0.03f, 0.30f, _sensitivity, v =>
            {
                _sensitivity = v;
                label.text = $"Mouse sensitivity: {_sensitivity:0.00}";
                PlayerPrefs.SetFloat("sz_look_sens", _sensitivity);
            });
            UIKit.Place((RectTransform)slider.transform, new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(270f, 26f));

            var close = UIKit.Button(_settingsUi, "Close", ToggleSettings,
                skin != null ? skin.ButtonGrey : null, 16);
            UIKit.Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(270f, 36f));
        }

    }
}
