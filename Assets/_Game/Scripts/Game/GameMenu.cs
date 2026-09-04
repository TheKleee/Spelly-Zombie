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

        bool _options, _langPick, _micPick;
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

        Image _meter;
        RectTransform _meterRect;

        void LateUpdate()
        {
            // the mic meter breathes with your own voice while the menu is open
            if (_meterRect != null)
                _meterRect.sizeDelta = new Vector2(300f * VoiceChat.LocalLevel, 0f);
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

        bool _wasLocked;

        void Open()
        {
            IsOpen = true;
            _options = false;
            _langPick = false;
            _micPick = false;
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
            Time.timeScale = 1f;
            PlayerPrefs.SetFloat("sz_look_sens", _sens);
            PlayerPrefs.SetFloat("sz_volume", _volume);
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
            if (!_options)
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

            if (_options)
            {
                var sensLabel = UIKit.Row(UIKit.Label(pr, Loc.F("opt.sens", _sens.ToString("0.00")), 17, UIKit.Ink, TextAnchor.MiddleLeft, true), 300f, 24f);
                UIKit.Row(UIKit.Slider(pr, 0.02f, 0.4f, _sens, v =>
                {
                    _sens = v;
                    sensLabel.text = Loc.F("opt.sens", _sens.ToString("0.00"));
                    foreach (var p in SimpleFPSController.All)
                        if (p != null) p.LookSensitivity = _sens;
                }), 300f, 26f);
                UIKit.Gap(pr, 6f);

                var volLabel = UIKit.Row(UIKit.Label(pr, Loc.F("opt.volume", (_volume * 100f).ToString("0")), 17, UIKit.Ink, TextAnchor.MiddleLeft, true), 300f, 24f);
                UIKit.Row(UIKit.Slider(pr, 0f, 1f, _volume, v =>
                {
                    _volume = v;
                    volLabel.text = Loc.F("opt.volume", (_volume * 100f).ToString("0"));
                    AudioListener.volume = _volume;
                }), 300f, 26f);
                UIKit.Gap(pr, 6f);

                // the language button opens the picker: every language at once
                OptionButton(Loc.F("opt.language", Loc.NativeName(Loc.LanguageCode)),
                    () => { _langPick = true; BuildUI(); });

                // immersive mode: the whole HUD off
                OptionButton(Loc.T(UIKit.Immersive ? "opt.immersive.on" : "opt.immersive.off"),
                    () => { UIKit.Immersive = !UIKit.Immersive; BuildUI(); });
                var hint = UIKit.Label(pr, Loc.T("opt.immersive.hint"),
                    13, new Color(0.35f, 0.28f, 0.2f), TextAnchor.MiddleCenter, true);
                hint.resizeTextForBestFit = false;
                UIKit.Row(hint, 300f, -1f); // as tall as its lines
                UIKit.Gap(pr, 6f);

                // ---- voice: how the mic opens, which mic, and a live level so
                // the pick is seen to work; then one mute button per other player ----
                UIKit.Row(UIKit.Label(pr, Loc.T("opt.mic.title"), 17, UIKit.Ink, TextAnchor.MiddleLeft, true), 300f, 24f);
                var modes = UIKit.Segments(pr, 300f, 40f, 6f);
                ModeButton(modes, VoiceChat.MicMode.Open, "opt.mic.open");
                ModeButton(modes, VoiceChat.MicMode.PushToTalk, "opt.mic.ptt");
                ModeButton(modes, VoiceChat.MicMode.Off, "opt.mic.off");
                var devs = Microphone.devices;
                string micName = string.IsNullOrEmpty(VoiceChat.Device) ? Loc.T("opt.mic.default") : VoiceChat.Device;
                if (devs != null && devs.Length > 1)
                    OptionButton(Loc.F("opt.mic", micName), () => { _micPick = true; BuildUI(); });
                else
                    UIKit.Row(UIKit.Label(pr, Loc.F("opt.mic", micName), 15, UIKit.Ink, TextAnchor.MiddleCenter, true), 300f, 24f);
                var meterBack = UIKit.Row(UIKit.Panel(pr, null, new Color(0f, 0f, 0f, 0.35f)), 300f, 8f);
                _meter = UIKit.Panel((RectTransform)meterBack.transform, null, new Color(0.45f, 1f, 0.55f, 0.9f));
                _meterRect = (RectTransform)_meter.transform;
                _meterRect.anchorMin = Vector2.zero;
                _meterRect.anchorMax = new Vector2(0f, 1f);
                _meterRect.pivot = new Vector2(0f, 0.5f);
                _meterRect.anchoredPosition = Vector2.zero;
                _meterRect.sizeDelta = Vector2.zero;
                UIKit.Gap(pr, 6f);

                if (NetAvatar.All.Count == 0)
                    UIKit.Row(UIKit.Label(pr, Loc.T("opt.nobody"), 15, UIKit.Ink, TextAnchor.MiddleCenter, true), 300f, 24f);
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

                UIKit.Gap(pr, 6f);
                UIKit.Row(UIKit.Button(pr, Loc.T("menu.back"), () => { _options = false; BuildUI(); }, grey), 300f, 48f);
                return;

                void OptionButton(string label, System.Action act)
                    => UIKit.Row(UIKit.Button(pr, label, act, grey), 300f, 44f);

                void ModeButton(RectTransform row, VoiceChat.MicMode mode, string key)
                {
                    bool on = VoiceChat.Mode == mode;
                    UIKit.Row(UIKit.Button(row, Loc.T(key), () => { VoiceChat.Mode = mode; BuildUI(); },
                        skin != null ? (on ? skin.ButtonBrown : skin.ButtonGrey) : null, 16), 90f, 40f);
                }
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
                    LoadingHints.Show();
                    UnityEngine.SceneManagement.SceneManager.LoadScene(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
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
            MenuButton(Loc.T("menu.quit"), QuitGame, skin != null ? skin.ButtonRed : null);
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
