using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Cached active-scene name - GetActiveScene().name allocates a fresh string per access.
    public static class ActiveScene
    {
        public static string Name { get; private set; } = "";
        /// Its build index, as world messages carry it.
        public static byte Index { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            var now = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Name = now.name;
            Index = (byte)now.buildIndex;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += (_, next) =>
            {
                Name = next.name;
                Index = (byte)next.buildIndex;
            };
        }
    }

    /// ESC = pause: Resume / Restart / Options (persisted) / Share with friends / Quit (asks first) - zero scene setup needed.
    public class GameMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        const string StoreUrl = "https://store.steampowered.com/app/5050950/"; // the store page, by app id

        bool _options, _langPick, _micPick, _resPick, _quitCheck;
        bool _fromMainMenu;   // opened by the main menu's Options: no pause, Back closes
        int _tab; // 0 game, 1 video, 2 audio, 3 controls
        Act _bindAct; // the binding waiting for its key (Controls tab)
        bool _bindPad, _binding;
        float _sens, _stickSens;
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
            _stickSens = Keys.StickSensitivity;
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
            if (PoseStudio.IsOpen) return;
            // the MAIN MENU owns its screen - no pause menu on top of it
            if (ActiveScene.Name == "Menu")
            {
                // only its own Options page may sit on top of the main menu
                if (IsOpen && (!_fromMainMenu || MenuKey())) Close();
                return;
            }
            if (MenuKey())
            {
                if (MapPicker.IsOpen) MapPicker.Close(); // Esc closes the picker first
                else if (!IsOpen && PhotoBooth.Escape()) { } // the booth's posing, ink or placing ends first
                else if (IsOpen) Close();
                else if (!LoadEgg.Leaving) Open(); // the egg has the screen: no menu nobody can see
            }
        }

        /// The menu key or button; on a controller B closes an open menu or map picker too. Not
        /// while a binding waits for its key: Escape belongs to that then.
        bool MenuKey() => !Keys.Listening && (Keys.Down(Act.Menu) || ((IsOpen || MapPicker.IsOpen) && Keys.BackDown));

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

        /// What the Controls tab calls each action.
        static string ActName(Act a)
        {
            switch (a)
            {
                case Act.Forward: return Loc.T("keys.act.forward");
                case Act.Back: return Loc.T("keys.act.back");
                case Act.Left: return Loc.T("keys.act.left");
                case Act.Right: return Loc.T("keys.act.right");
                case Act.Jump: return Loc.T("keys.act.jump");
                case Act.Crouch: return Loc.T("keys.act.crouch");
                case Act.Sprint: return Loc.T("keys.act.sprint");
                case Act.Draw: return Loc.T("keys.act.draw");
                case Act.Erase: return Loc.T("keys.act.erase");
                case Act.Precise: return Loc.T("keys.act.precise");
                case Act.Use: return Loc.T("keys.act.use");
                case Act.Drop: return Loc.T("keys.act.drop");
                case Act.Book: return Loc.T("chip.grimoire");
                case Act.Body: return Loc.T("keys.act.body");
                case Act.View: return Loc.T("chip.third");
                case Act.Menu: return Loc.T("keys.act.menu");
                case Act.Ready: return Loc.T("keys.act.ready");
                case Act.NotReady: return Loc.T("keys.act.notready");
                case Act.Talk: return Loc.T("keys.act.talk");
                case Act.Prev: return Loc.T("keys.act.prev");
                case Act.Next: return Loc.T("keys.act.next");
                case Act.Video: return Loc.T("opt.key.clip");
                case Act.Photo: return Loc.T("opt.key.photo");
                default: return Loc.T("opt.key.folder");
            }
        }

        /// A trip takes the screen: an open menu closes (the egg hides it, and it came back on the
        /// other side over a locked cursor).
        public static void CloseForTrip()
        {
            if (_i != null && IsOpen) _i.Close();
        }

        void Close()
        {
            Keys.Cancel();
            _binding = false;
            IsOpen = false;
            _fromMainMenu = false;
            Time.timeScale = 1f;
            PlayerPrefs.SetFloat("sz_look_sens", _sens);
            PlayerPrefs.SetFloat(Keys.StickSensPref, _stickSens);
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

        RectTransform _scrollAt; // the options list, for its place across a rebuild

        void BuildUI()
        {
            float keepScroll = _scrollAt != null ? _scrollAt.anchoredPosition.y : 0f;
            _scrollAt = null;
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
                string ask = here == "Menu" ? "menu.quit.game"
                    : here == "Lobby" || MapCreator.Active || PhotoBooth.Active ? "menu.quit.lobby" : "menu.quit.match";
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
                TabButton(tabs, 0, Loc.T("opt.tab.game"));
                TabButton(tabs, 1, Loc.T("opt.tab.video"));
                TabButton(tabs, 2, Loc.T("opt.tab.audio"));
                TabButton(tabs, 3, Loc.T("opt.tab.keys"));
                UIKit.Gap(pr, 6f);
                var at = UIKit.Scroll(pr, "OptionsScroll", 300f, 420f, 5f);
                _scrollAt = at;
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

                    // the right stick has its own: 1.0 turns as it always did
                    var stickLabel = UIKit.Row(UIKit.Label(at, Loc.F("opt.stick", _stickSens.ToString("0.0")), 15, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 22f);
                    UIKit.Row(UIKit.Slider(at, 0.3f, 3f, _stickSens, v =>
                    {
                        _stickSens = Mathf.Round(v * 10f) / 10f; // tenths
                        stickLabel.text = Loc.F("opt.stick", _stickSens.ToString("0.0"));
                        Keys.StickSensitivity = _stickSens;
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

                    // the ghost hand: the lobby lessons and the floating F's drawn previews
                    Choice("opt.ghost", OffOn(), GhostHand.Enabled ? 1 : 0, i => GhostHand.Enabled = i == 1);

                    // the recorder's keys (ClipRecorder): nothing else in the game names them
                    UIKit.Gap(at, 8f);
                    UIKit.Row(UIKit.Label(at, Loc.T("opt.keys.title"), 15, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 22f);
                    KeyRow(at, W, Keys.KeyLabel(Act.Video), Loc.T("opt.key.clip"));
                    KeyRow(at, W, Keys.KeyLabel(Act.Photo), Loc.T("opt.key.photo"));
                    KeyRow(at, W, Keys.KeyLabel(Act.Folder), Loc.T("opt.key.folder"));
                    var keysNote = UIKit.Label(at, Loc.T("opt.keys.note"),
                        12, new Color(0.35f, 0.28f, 0.2f), TextAnchor.MiddleCenter, true);
                    keysNote.resizeTextForBestFit = false;
                    UIKit.Row(keysNote, W, -1f);
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
                else if (_tab == 2)
                {
                    VolumeRow("opt.volume", AudioOptions.Master, AudioOptions.SetMaster);
                    VolumeRow("opt.music", AudioOptions.Music, AudioOptions.SetMusic);
                    VolumeRow("opt.sfx", AudioOptions.Sfx, AudioOptions.SetSfx);

                    // ---- voice: how the mic opens, which mic, and a live level so
                    // the pick is seen to work; then one mute button per other player ----
                    UIKit.Row(UIKit.Label(at, Loc.T("opt.mic.title"), 15, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 22f);
                    var modes = UIKit.Segments(at, W, 36f, 6f);
                    ModeButton(modes, VoiceChat.MicMode.Open, Loc.T("opt.mic.open"));
                    ModeButton(modes, VoiceChat.MicMode.PushToTalk, Loc.F("opt.mic.ptt", Keys.Label(Act.Talk)));
                    ModeButton(modes, VoiceChat.MicMode.Off, Loc.T("opt.mic.off"));
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

                else if (_tab == 3)
                {
                    // every action: its name, its key, its controller button. A click waits for the new one.
                    var note = UIKit.Label(at, _binding ? Loc.T("keys.press") : Loc.T("keys.note"),
                        12, new Color(0.35f, 0.28f, 0.2f), TextAnchor.MiddleCenter, true);
                    note.resizeTextForBestFit = false;
                    UIKit.Row(note, W, -1f);
                    BindRow(Loc.T("keys.action"), Loc.T("keys.keyboard"), Loc.T("keys.pad"), null, null);
                    foreach (var act in Keys.Listed)
                    {
                        var a = act;
                        bool waitKey = _binding && _bindAct == a && !_bindPad, waitPad = _binding && _bindAct == a && _bindPad;
                        BindRow(ActName(a),
                            waitKey ? "..." : Dash(Keys.KeyLabel(a)), waitPad ? "..." : Dash(Keys.PadLabel(a)),
                            () => Bind(a, false), () => Bind(a, true));
                    }
                    UIKit.Gap(at, 4f);
                    OptionButton(Loc.T("keys.reset"), () => { Keys.Cancel(); _binding = false; Keys.ResetAll(); BuildUI(); });
                }

                UIKit.Gap(pr, 6f);
                UIKit.Row(UIKit.Button(pr, Loc.T("menu.back"),
                    () => { Keys.Cancel(); _binding = false; if (_fromMainMenu) Close(); else { _options = false; BuildUI(); } }, grey), 300f, 44f);
                // a click rebuilds the page: the list stays where it was scrolled to
                at.anchoredPosition = new Vector2(at.anchoredPosition.x, keepScroll);
                return;

                string Dash(string label) => string.IsNullOrEmpty(label) ? "-" : label;

                void Bind(Act a, bool pad)
                {
                    _bindAct = a; _bindPad = pad; _binding = true;
                    Keys.Rebind(a, pad, () => { _binding = false; BuildUI(); });
                    BuildUI();
                }

                // a name and two buttons on one line; without actions it is the heading of the list
                void BindRow(string name, string key, string pad, System.Action onKey, System.Action onPad)
                {
                    var row = UIKit.Group(at, "BindRow");
                    UIKit.Row(row, W, 30f);
                    if (UIKit.WasAdopted(row)) return;
                    var lay = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    lay.spacing = 4f;
                    lay.childAlignment = TextAnchor.MiddleLeft;
                    lay.childControlWidth = true;
                    lay.childControlHeight = true;
                    lay.childForceExpandWidth = false;
                    lay.childForceExpandHeight = true;
                    var label = UIKit.Label(row, name, 13, UIKit.Ink, TextAnchor.MiddleLeft, true);
                    UIKit.Row(label, 118f, 30f);
                    if (onKey == null)
                    {
                        UIKit.Row(UIKit.Label(row, key, 12, UIKit.Ink, TextAnchor.MiddleCenter, true), 81f, 30f);
                        UIKit.Row(UIKit.Label(row, pad, 12, UIKit.Ink, TextAnchor.MiddleCenter, true), 81f, 30f);
                        return;
                    }
                    UIKit.Row(UIKit.Button(row, key, onKey, grey, 13), 81f, 30f);
                    UIKit.Row(UIKit.Button(row, pad, onPad, grey, 13), 81f, 30f);
                }

                // one key and what it does; the line grows when a language needs two lines
                void KeyRow(RectTransform into, float width, string key, string does)
                {
                    var row = UIKit.Group(into, "KeyRow");
                    UIKit.Row(row, width, -1f);
                    if (UIKit.WasAdopted(row)) return;
                    var lay = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    lay.spacing = 10f;
                    lay.childAlignment = TextAnchor.MiddleLeft;
                    lay.childControlWidth = true;
                    lay.childControlHeight = true;
                    lay.childForceExpandWidth = false;
                    lay.childForceExpandHeight = false;
                    var cap = UIKit.Keycap(row, key, 26f);
                    UIKit.Row(cap, cap.sizeDelta.x, cap.sizeDelta.y);
                    var label = UIKit.Label(row, does, 15, UIKit.Ink, TextAnchor.MiddleLeft, true);
                    label.resizeTextForBestFit = false;
                    var room = label.gameObject.AddComponent<LayoutElement>();
                    room.flexibleWidth = 1f;
                }

                void OptionButton(string label, System.Action act)
                    => UIKit.Row(UIKit.Button(at, label, act, grey, 16), W, 36f);

                void ModeButton(RectTransform row, VoiceChat.MicMode mode, string label)
                {
                    bool on = VoiceChat.Mode == mode;
                    UIKit.Row(UIKit.Button(row, label, () => { VoiceChat.Mode = mode; BuildUI(); },
                        skin != null ? (on ? skin.ButtonBrown : skin.ButtonGrey) : null, 15), 90f, 36f);
                }

                void TabButton(RectTransform row, int tab, string label)
                {
                    bool on = _tab == tab;
                    // four tabs share the 300 of the row (three gaps of 6): at 90 each the fourth hung off the panel
                    UIKit.Row(UIKit.Button(row, label, () => { Keys.Cancel(); _binding = false; _tab = tab; _scrollAt = null; BuildUI(); },
                        skin != null ? (on ? skin.ButtonBrown : skin.ButtonGrey) : null, 16), 70.5f, 36f);
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
            if (!NetGame.Connected && !MapCreator.Active && !PhotoBooth.Active)
                MenuButton(Loc.T("menu.restart"), () =>
                {
                    Close();
                    string at = ActiveScene.Name;
                    if (at != "Lobby" && at != "Menu") RoundDirector.RestartOnLoad(); // a map restarts its match
                    LoadEgg.Travel(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                });
            MenuButton(Loc.T("menu.options"), () => { _options = true; BuildUI(); });
            if (NetGame.Connected && !NetGame.IsHost)
                MenuButton(Loc.T("menu.leave"), LeaveHere, grey);
            if (NetGame.Connected && NetGame.IsHost)
                MenuButton(Loc.T("menu.delete"), () =>
                {
                    SteamLobby.DeleteLobby();
                    Close();
                }, grey);
            // share: the store link goes on the clipboard and the button says so
            Text shareLabel = null;
            shareLabel = UIKit.Row(UIKit.Button(pr, Loc.T("menu.share"), () =>
            {
                GUIUtility.systemCopyBuffer = StoreUrl;
                if (shareLabel != null) shareLabel.text = Loc.T("menu.sharecopied");
            }, grey), 300f, 50f).GetComponentInChildren<Text>();
            MenuButton(Loc.T("menu.quit"), () => { _quitCheck = true; BuildUI(); }, skin != null ? skin.ButtonRed : null);
        }

        /// Quit from where you are: the main menu closes the game, the lobby
        /// goes to the main menu, a match to an empty lobby of your own.
        void QuitHere()
        {
            string here = ActiveScene.Name;
            if (here == "Menu") { QuitGame(); return; }
            if (MapCreator.Active) { Close(); MapCreator.ExitToMenu(); return; } // the creator goes back to the maps
            if (PhotoBooth.Active) { Close(); PhotoBooth.ExitToMenu(); return; }  // the booth goes back to the photos
            if (here != "Lobby") RoundDirector.Abandon(); // dropped, not decided
            NetSync.WalkingOut();
            Close();
            // the world stays in view and in play while the shell closes; the session is left as the scene changes
            LoadEgg.Travel(here == "Lobby" ? "Menu" : "Lobby", NetSync.LeaveSession);
        }

        /// A guest walks out of the session: off the Steam lobby and the connection, never the
        /// "host left" road. In the lobby they stay; a match goes home as Quit does.
        void LeaveHere()
        {
            if (ActiveScene.Name != "Lobby") { QuitHere(); return; }
            NetSync.LeaveSession();
            Close();
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
