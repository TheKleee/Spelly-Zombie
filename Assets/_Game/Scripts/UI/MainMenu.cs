using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// The main menu, per the design sketch (layout copied from Meccha Chameleon):
    /// game name top-left, server buttons on the left, your poseable character on
    /// a pedestal right-of-center (PoseStudio in AlwaysOpen mode handles that).
    ///
    /// Create Server starts a local match until EOS lands; Find Server is the
    /// future server browser. Poses are saved HERE - drawing on your character
    /// happens pre-match in the lobby, never in the menu.
    public class MainMenu : MonoBehaviour
    {
        string _status = "";

        RectTransform _ui;
        UnityEngine.UI.Text _statusLabel;

        void Awake()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Start() => BuildUI();

        void OnDestroy()
        {
            // the canvas persists across scenes - the menu HIDES (never
            // destroys: prefab-adopted UI must survive MenuLobbyMenu)
            if (_ui != null) _ui.gameObject.SetActive(false);
        }

        void BuildUI()
        {
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "MainMenu");
            UIKit.Stretch(_ui);

            // title top-left, button column on the left - the Meccha layout
            var title = UIKit.Label(_ui, "Spelly Zombie", 58, UIKit.Parchment, TextAnchor.MiddleLeft, true);
            UIKit.Place((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(46f, -66f), new Vector2(700f, 80f));
            var tag = UIKit.Label(_ui, Loc.T("menu.tagline"), 20, UIKit.Gold, TextAnchor.MiddleLeft);
            UIKit.Place((RectTransform)tag.transform, new Vector2(0f, 1f), new Vector2(50f, -116f), new Vector2(500f, 26f));

            float y = -20f;
            void MenuButton(string label, System.Action act, Sprite sprite = null)
            {
                var b = UIKit.Button(_ui, label, act, sprite ?? (skin != null ? skin.ButtonBrown : null), 19);
                UIKit.Place((RectTransform)b.transform, new Vector2(0f, 0.5f), new Vector2(46f, y), new Vector2(250f, 50f));
                ((RectTransform)b.transform).pivot = new Vector2(0f, 0.5f);
                y -= 60f;
            }

            // one door: the lobby is the sandbox AND the control room, so the
            // menu needs exactly one verb
            MenuButton(Loc.T("menu.play"), () =>
            {
                LoadEgg.Travel("Lobby");
            });
            MenuButton(Loc.T("menu.options"), GameMenu.OpenOptions, skin != null ? skin.ButtonGrey : null);
            MenuButton(Loc.T("menu.quit"), GameMenu.QuitGame, skin != null ? skin.ButtonRed : null);

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


    }
}
