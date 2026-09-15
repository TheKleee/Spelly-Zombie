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

    /// ESC = pause: Resume / Restart / Options (persisted) / Wishlist / Quit (asks first) - zero scene setup needed.
    public class GameMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        const string WishlistUrl = "https://store.steampowered.com/"; // real page URL once it exists

        bool _options, _langPick, _micPick, _resPick, _quitCheck;
        bool _fromMainMenu;   // opened by the main menu's Options: no pause, Back closes
        int _tab; // 0 game, 1 video, 2 audio
        float _sens;
        static GameMenu _i;

        /// The main menu's Options: the same page as in play.
        public static void OpenOptions()
        {
            if (_i == null || IsOpen) return;
            _i.Open();
            Time.timeScale = 1f;
            _i._fromMainMenu = true;
            _i._options = true;
            _i.BuildUI();
        }

        /// The main menu's Quit: the same check as in play.
        public static void OpenQuitCheck()
        {
            if (_i == null || IsOpen) return;
            _i.Open();
            Time.timeScale = 1f;
            _i._fromMainMenu = true;
            _i._quitCheck = true;
            _i.BuildUI();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("GameMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<GameMenu>();
        }

        void Awake()
        {
            _i = this;
            _sens = PlayerPrefs.GetFloat("sz_look_sens", 0.12f);
            AudioListener.volume = AudioOptions.Master;
        }

        Image _meter;
        RectTransform _meterRect;

        void LateUpdate()
        {
            // the mic meter breathes with your own voice while the menu is open
            if (_meterRect != null)
                _meterRect.sizeDelta = new Vector2(288f * VoiceChat.LocalLevel, 0f);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || PoseStudio.IsOpen) return;
            // the MAIN MENU owns its screen - no pause menu on top of it
            if (ActiveScene.Name == "Menu")
            {
                // only its own Options page may sit on top of the main menu
                if (IsOpen && (!_fromMainMenu || kb.escapeKey.wasPressedThisFrame)) Close();
                return;
            }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else Open();
            }
        }

        RectTransform _ui;

        bool _wasLocked;

        void Open()
        {
            IsOpen = true;
            _options = false;
            _langPick = false;
            _micPick = false;
            _resPick = false;
            _quitCheck = false;
            // never pause a connected game: the world runs on while you read
            Time.timeScale = NetGame.Connected ? 1f : 0f;
            _wasLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            BuildUI();
        }

        void Close()
        {
            IsOpen = false;
            _fromMainMenu = false;
            Time.timeScale = 1f;
            PlayerPrefs.SetFloat("sz_look_sens", _sens);
            PlayerPrefs.Save();
            if (_ui != null) Destroy(_ui.gameObject);
            _ui = null;
            // the lock the menu interrupted comes back with the game - a ghost
            // or a driven zombie has no click-to-relock rule to fall back on
            if (_wasLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
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

            float top = 250f;
            if (!_options && !_quitCheck)
            {
                var title = UIKit.Label(_ui, "SPELLY ZOMBIE", 44, UIKit.Parchment, TextAnchor.MiddleCenter, true);
                UIKit.Place((RectTransform)title.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(800f, 60f));
                top = 190f;
            }

            // one vertical stack that sizes itself to its rows, hung from a
            // point under the title so a long list grows downward
            var panel = UIKit.Panel(_ui, skin != null ? skin.PanelBrown : null,
                skin != null ? Color.white : new Color(0.22f, 0.17f, 0.12f, 0.95f));
            var pr = (RectTransform)panel.transform;
            UIKit.Place(pr, new Vector2(0.5f, 0.5f), new Vector2(0f, top), new Vector2(380f, 100f));
            if (!UIKit.WasAdopted(pr)) pr.pivot = new Vector2(0.5f, 1f);
            UIKit.Stack(pr, 40, 18, 8);
            Sprite grey = skin != null ? skin.ButtonGrey : null;

            if (_quitCheck)
            {
                // the question names where Quit takes you from here
                string here = ActiveScene.Name;
                string ask = here == "Menu" ? "menu.quit.game" : here == "Lobby" ? "menu.quit.lobby" : "menu.quit.match";
                var q = UIKit.Label(pr, Loc.T(ask), 20, UIKit.Ink, TextAnchor.MiddleCenter, true);
                q.resizeTextForBestFit = false;
                UIKit.Row(q, 300f, -1f);
                if (here != "Menu" && NetGame.IsHost && NetSync.RemoteCount > 0)
                {
                    var host = UIKit.Label(pr, Loc.T("menu.quit.host"), 14, new Color(0.35f, 0.28f, 0.2f), TextAnchor.MiddleCenter, true);
                    host.resizeTextForBestFit = false;
                    UIKit.Row(host, 300f, -1f);
                }
                UIKit.Gap(pr, 6f);
                UIKit.Row(UIKit.Button(pr, Loc.T("menu.quit"), QuitHere, skin != null ? skin.ButtonRed : null), 300f, 50f);
                UIKit.Row(UIKit.Button(pr, Loc.T("menu.cancel"),
                    () => { if (_fromMainMenu) Close(); else { _quitCheck = false; BuildUI(); } }, grey), 300f, 44f);
                return;
            }

            if (_options && _micPick)
            {
                // every microphone at once; picking one relabels and stays, Back returns
                UIKit.Row(UIKit.Label(pr, Loc.T("opt.mic.title"), 20, UIKit.Ink, TextAnchor.MiddleCenter, true), 300f, 28f);
                var devices = Microphone.devices;
                bool none = string.IsNullOrEmpty(VoiceChat.Device);
                UIKit.Row(UIKit.Button(pr, Loc.T("opt.mic.default"), () => { VoiceChat.Device = ""; BuildUI(); },
                    skin != null ? (none ? skin.ButtonBrown : skin.ButtonGrey) : null, 16), 300f, 44f);
                if (devices != null)
                    foreach (var d in devices)
                    {
                        string dev = d;
                        bool on = dev == VoiceChat.Device;
                        UIKit.Row(UIKit.Button(pr, dev, () => { VoiceChat.Device = dev; BuildUI(); },
                            skin != null ? (on ? skin.ButtonBrown : skin.ButtonGrey) : null, 16), 300f, 44f);
                    }
                UIKit.Gap(pr, 6f);
                UIKit.Row(UIKit.Button(pr, Loc.T("menu.back"), () => { _micPick = false; BuildUI(); }, grey), 300f, 48f);
                return;
            }

            if (_options && _langPick)
            {
                // three columns of native names, each drawn in its own script.
                // picking one relabels the picker and stays; Back returns to the options
                UIKit.Row(UIKit.Label(pr, Loc.T("stand.langs"), 20, UIKit.Ink, TextAnchor.MiddleCenter, true), 300f, 28f);
                var grid = UIKit.Grid(pr, "LangGrid", new Vector2(150f, 44f), 10f, 3);
                foreach (var lang in Loc.Languages)
                {
                    string code = lang.Code;
                    bool on = code == Loc.LanguageCode;
                    var b = UIKit.Button(grid, lang.Native, () => { Loc.SetLanguage(code); BuildUI(); },
                        skin != null ? (on ? skin.ButtonBrown : skin.ButtonGrey) : null);
                    var bt = b.GetComponentInChildren<Text>();
                    if (bt != null) bt.font = LocFonts.LegacyFor(code);
                }
                UIKit.Gap(pr, 6f);
                UIKit.Row(UIKit.Button(pr, Loc.T("menu.back"), () => { _langPick = false; BuildUI(); }, grey), 300f, 48f);
                return;
            }

            if (_options && _resPick)
            {
                // every size at once; picking one relabels and stays, Back returns
                UIKit.Row(UIKit.Label(pr, Loc.T("opt.resolution.title"), 20, UIKit.Ink, TextAnchor.MiddleCenter, true), 300f, 28f);
                var grid = UIKit.Grid(pr, "ResGrid", new Vector2(150f, 44f), 10f, 2);
                foreach (var r in VideoOptions.Resolutions())
                {
                    int w = r.x, h = r.y;
                    bool on = w == VideoOptions.CurrentWidth && h == VideoOptions.CurrentHeight;
                    UIKit.Button(grid, w + " x " + h, () => { VideoOptions.SetResolution(w, h); BuildUI(); },
                        skin != null ? (on ? skin.ButtonBrown : skin.ButtonGrey) : null, 16);
                }
                UIKit.Gap(pr, 6f);
                UIKit.Row(UIKit.Button(pr, Loc.T("menu.back"), () => { _resPick = false; BuildUI(); }, grey), 300f, 48f);
                return;
            }

            if (_options)
            {
                // three tabs, all visible, the open one lit; the rows below them
                // scroll inside a fixed window so no tab can run off the screen
                var tabs = UIKit.Segments(pr, 300f, 36f, 6f);
                TabButton(tabs, 0, "opt.tab.game");
                TabButton(tabs, 1, "opt.tab.video");
                TabButton(tabs, 2, "opt.tab.audio");
                UIKit.Gap(pr, 6f);
                var at = UIKit.Scroll(pr, "OptionsScroll", 300f, 420f, 5f);
                const float W = 288f;

                if (_tab == 0)
                {
                    var sensLabel = UIKit.Row(UIKit.Label(at, Loc.F("opt.sens", _sens.ToString("0.00")), 15, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 22f);
                    UIKit.Row(UIKit.Slider(at, 0.02f, 0.4f, _sens, v =>
                    {
                        _sens = v;
                        sensLabel.text = Loc.F("opt.sens", _sens.ToString("0.00"));
                        foreach (var p in SimpleFPSController.All)
                            if (p != null) p.LookSensitivity = _sens;
                    }), W, 26f);
                    UIKit.Gap(at, 4f);

                    var uiLabel = UIKit.Row(UIKit.Label(at, Loc.F("opt.uiscale", Mathf.RoundToInt(UIKit.UiScale * 100f)), 15, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 22f);
                    UIKit.Row(UIKit.Slider(at, 0.6f, 1.4f, UIKit.UiScale, v =>
                    {
                        UIKit.UiScale = Mathf.Round(v * 20f) / 20f; // 5 percent steps
                        uiLabel.text = Loc.F("opt.uiscale", Mathf.RoundToInt(UIKit.UiScale * 100f));
                    }), W, 26f);
                    UIKit.Gap(at, 4f);

                    // the language button opens the picker: every language at once
                    OptionButton(Loc.F("opt.language", Loc.NativeName(Loc.LanguageCode)),
                        () => { _langPick = true; BuildUI(); });

                    // immersive mode: the whole HUD off
                    OptionButton(Loc.T(UIKit.Immersive ? "opt.immersive.on" : "opt.immersive.off"),
                        () => { UIKit.Immersive = !UIKit.Immersive; BuildUI(); });
                    var hint = UIKit.Label(at, Loc.T("opt.immersive.hint"),
                        12, new Color(0.35f, 0.28f, 0.2f), TextAnchor.MiddleCenter, true);
                    hint.resizeTextForBestFit = false;
                    UIKit.Row(hint, W, -1f); // as tall as its lines
                }
                else if (_tab == 1)
                {
                    OptionButton(Loc.F("opt.resolution", VideoOptions.CurrentWidth + " x " + VideoOptions.CurrentHeight),
                        () => { _resPick = true; BuildUI(); });
                    Choice("opt.display",
                        new[] { Loc.T("opt.display.full"), Loc.T("opt.display.borderless"), Loc.T("opt.display.windowed") },
                        (int)VideoOptions.Mode, i => VideoOptions.SetDisplay((VideoOptions.Display)i));
                    Choice("opt.quality", Levels(), VideoOptions.Preset, VideoOptions.SetPreset);
                    Choice("opt.textures", Levels(), (int)VideoOptions.Textures, i => VideoOptions.SetTextures((VideoOptions.Level)i));
                    Choice("opt.shadows", new[] { Loc.T("opt.off"), Loc.T("opt.low"), Loc.T("opt.high") },
                        (int)VideoOptions.Shadows, i => VideoOptions.SetShadows((VideoOptions.ShadowLevel)i));
                    Choice("opt.effects", OffOn(), VideoOptions.Effects ? 1 : 0, i => VideoOptions.SetEffects(i == 1));
                    Choice("opt.motionblur", OffOn(), VideoOptions.MotionBlur ? 1 : 0, i => VideoOptions.SetMotionBlur(i == 1));
                    Choice("opt.aa", new[] { Loc.T("opt.off"), "FXAA", "SMAA", "TAA", "MSAA" },
                        (int)VideoOptions.Antialias, i => VideoOptions.SetAntialias((VideoOptions.Aa)i));
                    Choice("opt.fps", new[] { Loc.T("opt.off"), "60", "120", "144", "240" }, VideoOptions.FpsIndex, VideoOptions.SetFps);
                    Choice("opt.vsync", OffOn(), VideoOptions.VSync ? 1 : 0, i => VideoOptions.SetVSync(i == 1));
                }
                else
                {
                    VolumeRow("opt.volume", AudioOptions.Master, AudioOptions.SetMaster);
                    VolumeRow("opt.music", AudioOptions.Music, AudioOptions.SetMusic);
                    VolumeRow("opt.sfx", AudioOptions.Sfx, AudioOptions.SetSfx);

                    // ---- voice: how the mic opens, which mic, and a live level so
                    // the pick is seen to work; then one mute button per other player ----
                    UIKit.Row(UIKit.Label(at, Loc.T("opt.mic.title"), 15, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 22f);
                    var modes = UIKit.Segments(at, W, 36f, 6f);
                    ModeButton(modes, VoiceChat.MicMode.Open, "opt.mic.open");
                    ModeButton(modes, VoiceChat.MicMode.PushToTalk, "opt.mic.ptt");
                    ModeButton(modes, VoiceChat.MicMode.Off, "opt.mic.off");
                    var devs = Microphone.devices;
                    string micName = string.IsNullOrEmpty(VoiceChat.Device) ? Loc.T("opt.mic.default") : VoiceChat.Device;
                    if (devs != null && devs.Length > 1)
                        OptionButton(Loc.F("opt.mic", micName), () => { _micPick = true; BuildUI(); });
                    else
                        UIKit.Row(UIKit.Label(at, Loc.F("opt.mic", micName), 14, UIKit.Ink, TextAnchor.MiddleCenter, true), W, 22f);
                    var meterBack = UIKit.Row(UIKit.Panel(at, null, new Color(0f, 0f, 0f, 0.35f)), W, 8f);
                    _meter = UIKit.Panel((RectTransform)meterBack.transform, null, new Color(0.45f, 1f, 0.55f, 0.9f));
                    _meterRect = (RectTransform)_meter.transform;
                    _meterRect.anchorMin = Vector2.zero;
                    _meterRect.anchorMax = new Vector2(0f, 1f);
                    _meterRect.pivot = new Vector2(0f, 0.5f);
                    _meterRect.anchoredPosition = Vector2.zero;
                    _meterRect.sizeDelta = Vector2.zero;
                    UIKit.Gap(at, 4f);

                    if (NetAvatar.All.Count == 0)
                        UIKit.Row(UIKit.Label(at, Loc.T("opt.nobody"), 14, UIKit.Ink, TextAnchor.MiddleCenter, true), W, 22f);
                    foreach (var av in NetAvatar.All)
                    {
                        if (av == null) continue;
                        int owner = NetSync.OwnerIdOf(av.Id);
                        string who = NetSync.IdentityOf(av.Id, out var name, out _) && !string.IsNullOrEmpty(name)
                            ? name : "#" + owner;
                        bool muted = VoiceChat.IsMuted(owner);
                        OptionButton(Loc.F(muted ? "opt.unmute" : "opt.mute", who),
                            () => { VoiceChat.SetMuted(owner, !muted); BuildUI(); });
                    }
                }

                UIKit.Gap(pr, 6f);
                UIKit.Row(UIKit.Button(pr, Loc.T("menu.back"),
                    () => { if (_fromMainMenu) Close(); else { _options = false; BuildUI(); } }, grey), 300f, 44f);
                return;

                void OptionButton(string label, System.Action act)
                    => UIKit.Row(UIKit.Button(at, label, act, grey, 16), W, 36f);

                void ModeButton(RectTransform row, VoiceChat.MicMode mode, string key)
                {
                    bool on = VoiceChat.Mode == mode;
                    UIKit.Row(UIKit.Button(row, Loc.T(key), () => { VoiceChat.Mode = mode; BuildUI(); },
                        skin != null ? (on ? skin.ButtonBrown : skin.ButtonGrey) : null, 15), 90f, 36f);
                }

                void TabButton(RectTransform row, int tab, string key)
                {
                    bool on = _tab == tab;
                    UIKit.Row(UIKit.Button(row, Loc.T(key), () => { _tab = tab; BuildUI(); },
                        skin != null ? (on ? skin.ButtonBrown : skin.ButtonGrey) : null, 16), 90f, 36f);
                }

                // one setting: its name, then every value at once with the current one lit
                void Choice(string titleKey, string[] labels, int current, System.Action<int> pick)
                {
                    UIKit.Row(UIKit.Label(at, Loc.T(titleKey), 14, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 18f);
                    var row = UIKit.Segments(at, W, 28f, 4f);
                    float w = (W - 4f * (labels.Length - 1)) / labels.Length;
                    for (int i = 0; i < labels.Length; i++)
                    {
                        int v = i;
                        UIKit.Row(UIKit.Button(row, labels[i], () => { pick(v); BuildUI(); },
                            skin != null ? (current == v ? skin.ButtonBrown : skin.ButtonGrey) : null, 13), w, 28f);
                    }
                    UIKit.Gap(at, 2f);
                }

                void VolumeRow(string key, float value, System.Action<float> set)
                {
                    var label = UIKit.Row(UIKit.Label(at, Loc.F(key, (value * 100f).ToString("0")), 15, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 22f);
                    UIKit.Row(UIKit.Slider(at, 0f, 1f, value, v =>
                    {
                        set(v);
                        label.text = Loc.F(key, (v * 100f).ToString("0"));
                    }), W, 26f);
                    UIKit.Gap(at, 4f);
                }

                string[] Levels() => new[] { Loc.T("opt.low"), Loc.T("opt.medium"), Loc.T("opt.high") };
                string[] OffOn() => new[] { Loc.T("opt.off"), Loc.T("opt.on") };
            }

            void MenuButton(string label, System.Action act, Sprite sprite = null)
                => UIKit.Row(UIKit.Button(pr, label, act, sprite ?? (skin != null ? skin.ButtonBrown : null)), 300f, 50f);

            MenuButton(Loc.T("menu.resume"), Close);

            // a connected game cannot be scene-restarted; the lobby verbs
            // take that slot instead
            if (!NetGame.Connected)
                MenuButton(Loc.T("menu.restart"), () =>
                {
                    Close();
                    LoadEgg.Travel(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                });
            MenuButton(Loc.T("menu.options"), () => { _options = true; BuildUI(); });
            if (NetGame.Connected && !NetGame.IsHost)
                MenuButton(Loc.T("menu.leave"), () =>
                {
                    FishNet.InstanceFinder.ClientManager.StopConnection();
                    Close();
                }, grey);
            if (NetGame.Connected && NetGame.IsHost)
                MenuButton(Loc.T("menu.delete"), () =>
                {
                    SteamLobby.DeleteLobby();
                    Close();
                }, grey);
            MenuButton(Loc.T("menu.wishlist"), () => Application.OpenURL(WishlistUrl), grey);
            MenuButton(Loc.T("menu.quit"), () => { _quitCheck = true; BuildUI(); }, skin != null ? skin.ButtonRed : null);
        }

        /// Quit from where you are: the main menu closes the game, the lobby
        /// goes to the main menu, a match to an empty lobby of your own.
        void QuitHere()
        {
            string here = ActiveScene.Name;
            if (here == "Menu") { QuitGame(); return; }
            if (here != "Lobby") RoundDirector.Abandon(); // dropped, not decided
            NetSync.LeaveSession();
            Close();
            LoadEgg.Travel(here == "Lobby" ? "Menu" : "Lobby");
        }

        /// Editor-aware quit - the one copy.
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
