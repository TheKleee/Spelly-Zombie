using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Cached active-scene name - GetActiveScene().name allocates a fresh string per access.
    public static class ActiveScene
    {
        public static string Name { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            Name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged +=
                (_, next) => Name = next.name;
        }
    }

    /// ESC = pause: Resume / Restart / Options (persisted) / Wishlist / Quit - zero scene setup needed.
    public class GameMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        const string WishlistUrl = "https://store.steampowered.com/"; // real page URL once it exists

        bool _options;
        float _sens, _volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("GameMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<GameMenu>();
        }

        void Awake()
        {
            _sens = PlayerPrefs.GetFloat("sz_look_sens", 0.12f);
            _volume = PlayerPrefs.GetFloat("sz_volume", 1f);
            AudioListener.volume = _volume;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || PoseStudio.IsOpen) return;
            // the MAIN MENU owns its screen - no pause menu on top of it
            if (ActiveScene.Name == "Menu")
            {
                if (IsOpen) Close();
                return;
            }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else Open();
            }
        }

        RectTransform _ui;

        void Open()
        {
            IsOpen = true;
            _options = false;
            // never pause a connected game: the world runs on while you read
            Time.timeScale = NetGame.Connected ? 1f : 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            BuildUI();
        }

        void Close()
        {
            IsOpen = false;
            Time.timeScale = 1f;
            PlayerPrefs.SetFloat("sz_look_sens", _sens);
            PlayerPrefs.SetFloat("sz_volume", _volume);
            PlayerPrefs.Save();
            if (_ui != null) Destroy(_ui.gameObject);
            _ui = null;
            // cursor re-locks on the next left-click (controller's existing rule)
        }

        void BuildUI()
        {
            UIKit.Retire(_ui); // never re-adopt the dying menu same-frame
            var skin = UISkin.I;
            _ui = UIKit.Group(UIKit.Root, "PauseMenu");
            UIKit.Stretch(_ui);

            // dim the world (and swallow clicks behind the menu)
            var dim = UIKit.Panel(_ui, null, new Color(0f, 0f, 0f, 0.62f));
            dim.raycastTarget = true;
            UIKit.Stretch((RectTransform)dim.transform);

            var title = UIKit.Label(_ui, "SPELLY ZOMBIE", 44, UIKit.Parchment, TextAnchor.MiddleCenter, true);
            UIKit.Place((RectTransform)title.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(800f, 60f));

            var panel = UIKit.Panel(_ui, skin != null ? skin.PanelBrown : null,
                skin != null ? Color.white : new Color(0.22f, 0.17f, 0.12f, 0.95f));
            var pr = (RectTransform)panel.transform;
            UIKit.Place(pr, new Vector2(0.5f, 0.5f), new Vector2(0f, -30f),
                new Vector2(380f, _options ? 390f : 380f));

            if (_options)
            {
                var sensLabel = UIKit.Label(pr, Loc.F("opt.sens", _sens.ToString("0.00")), 17, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Place((RectTransform)sensLabel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(300f, 24f));
                var sens = UIKit.Slider(pr, 0.02f, 0.4f, _sens, v =>
                {
                    _sens = v;
                    sensLabel.text = Loc.F("opt.sens", _sens.ToString("0.00"));
                    foreach (var p in SimpleFPSController.All)
                        if (p != null) p.LookSensitivity = _sens;
                });
                UIKit.Place((RectTransform)sens.transform, new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(300f, 26f));

                var volLabel = UIKit.Label(pr, Loc.F("opt.volume", (_volume * 100f).ToString("0")), 17, UIKit.Ink, TextAnchor.MiddleLeft, true);
                UIKit.Place((RectTransform)volLabel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(300f, 24f));
                var vol = UIKit.Slider(pr, 0f, 1f, _volume, v =>
                {
                    _volume = v;
                    volLabel.text = Loc.F("opt.volume", (_volume * 100f).ToString("0"));
                    AudioListener.volume = _volume;
                });
                UIKit.Place((RectTransform)vol.transform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(300f, 26f));

                // one button cycles every language; the menu rebuilds in it immediately
                OptionButton(Loc.F("opt.language", Loc.NativeName(Loc.LanguageCode)), -198f,
                    () => { Loc.SetLanguage(Loc.NextLanguage()); BuildUI(); });

                // immersive mode: the whole HUD off
                OptionButton(Loc.T(UIKit.Immersive ? "opt.immersive.on" : "opt.immersive.off"), -248f,
                    () => { UIKit.Immersive = !UIKit.Immersive; BuildUI(); });

                var hint = UIKit.Label(pr, Loc.T("opt.immersive.hint"),
                    13, new Color(0.35f, 0.28f, 0.2f), TextAnchor.MiddleCenter, true);
                UIKit.Place((RectTransform)hint.transform, new Vector2(0.5f, 1f), new Vector2(0f, -282f), new Vector2(320f, 20f));

                var back = UIKit.Button(pr, Loc.T("menu.back"), () => { _options = false; BuildUI(); },
                    skin != null ? skin.ButtonGrey : null);
                UIKit.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(300f, 48f));
                return;

                void OptionButton(string label, float y, System.Action act)
                {
                    var b = UIKit.Button(pr, label, act, skin != null ? skin.ButtonGrey : null);
                    UIKit.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(300f, 44f));
                }
            }

            void MenuButton(string label, float y, System.Action act, Sprite sprite = null)
            {
                var b = UIKit.Button(pr, label, act, sprite ?? (skin != null ? skin.ButtonBrown : null));
                UIKit.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(300f, 50f));
            }

            float y = -36f;
            MenuButton(Loc.T("menu.resume"), y, Close); y -= 60f;

            // a connected game cannot be scene-restarted; the lobby verbs
            // take that slot instead
            if (!NetGame.Connected)
            {
                MenuButton(Loc.T("menu.restart"), y, () =>
                {
                    Close();
                    LoadingHints.Show();
                    UnityEngine.SceneManagement.SceneManager.LoadScene(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                });
                y -= 60f;
            }
            MenuButton(Loc.T("menu.options"), y, () => { _options = true; BuildUI(); }); y -= 60f;
            if (NetGame.Connected && !NetGame.IsHost)
            {
                MenuButton(Loc.T("menu.leave"), y, () =>
                {
                    FishNet.InstanceFinder.ClientManager.StopConnection();
                    Close();
                }, skin != null ? skin.ButtonGrey : null);
                y -= 60f;
            }
            if (NetGame.Connected && NetGame.IsHost)
            {
                MenuButton(Loc.T("menu.delete"), y, () =>
                {
                    SteamLobby.DeleteLobby();
                    Close();
                }, skin != null ? skin.ButtonGrey : null);
                y -= 60f;
            }
            MenuButton(Loc.T("menu.wishlist"), y, () => Application.OpenURL(WishlistUrl),
                skin != null ? skin.ButtonGrey : null);
            MenuButton(Loc.T("menu.quit"), y - 60f, QuitGame, skin != null ? skin.ButtonRed : null);
        }

        /// Editor-aware quit - the one copy (MainMenu's Quit button calls it too).
        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
