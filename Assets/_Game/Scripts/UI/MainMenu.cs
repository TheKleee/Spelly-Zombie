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

        void Start()
        {
            BuildUI();
            Loc.Changed += BuildUI; // the captions follow a language change
            if (MapsScreen.OpenOnMenu) { MapsScreen.OpenOnMenu = false; MapsScreen.Open(); } // back from the creator
            if (PhotosScreen.OpenOnMenu) { PhotosScreen.OpenOnMenu = false; PhotosScreen.Open(); } // back from the booth
        }

        void OnDestroy()
        {
            Loc.Changed -= BuildUI;
            // the canvas persists across scenes - the menu HIDES (never
            // destroys: prefab-adopted UI must survive MenuLobbyMenu)
            if (_ui != null) _ui.gameObject.SetActive(false);
        }

        void BuildUI()
        {
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "MainMenu");
            UIKit.Stretch(_ui);

            // the title and tagline: the prefab's "Name" group when it has one
            var head = UIKit.Group(_ui, "Name");
            if (!UIKit.WasAdopted(head))
            {
                UIKit.Place(head, new Vector2(0f, 1f), new Vector2(46f, -26f), new Vector2(700f, 0f));
                head.pivot = new Vector2(0f, 1f);
            }
            UIKit.Stack(head, 0, 0, 2f);
            UIKit.Row(UIKit.Label(head, "Spelly Zombie", 58, UIKit.Parchment, TextAnchor.MiddleLeft, true), 700f, 80f);
            UIKit.Row(UIKit.Label(head, Loc.T("menu.tagline"), 20, UIKit.Gold, TextAnchor.MiddleLeft), 700f, 26f);

            // the column: a layout group, buttons in hierarchy order (the prefab's
            // "Buttons" child when it has one: Play, Map Creator, Settings, Quit, status)
            var col = UIKit.Group(_ui, "Buttons");
            if (!UIKit.WasAdopted(col))
            {
                UIKit.Place(col, new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(250f, 0f));
                col.pivot = new Vector2(0f, 1f); // grows down from the middle of the screen
            }
            UIKit.Stack(col, 0, 0, 10f);
            void MenuButton(string label, System.Action act, Sprite sprite = null)
                => UIKit.Row(UIKit.Button(col, label, act, sprite ?? (skin != null ? skin.ButtonBrown : null), 19), 250f, 50f);

            // one door to play: the lobby is the sandbox AND the control room.
            // The Map Creator is its own door, apart from play.
            MenuButton(Loc.T("menu.play"), () =>
            {
                LoadEgg.Travel("Lobby");
            });
            MenuButton(Loc.T("menu.mapcreator"), MapsScreen.Open, skin != null ? skin.ButtonGrey : null);
            MenuButton(Loc.T("menu.photobooth"), PhotosScreen.Open, skin != null ? skin.ButtonGrey : null);
            MenuButton(Loc.T("menu.options"), GameMenu.OpenOptions, skin != null ? skin.ButtonGrey : null);
            MenuButton(Loc.T("menu.quit"), GameMenu.OpenQuitCheck, skin != null ? skin.ButtonRed : null);

            _statusLabel = UIKit.Row(UIKit.Label(col, _status, 15, UIKit.Parchment, TextAnchor.UpperLeft), 250f, 60f);
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
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
