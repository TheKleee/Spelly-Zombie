using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace SpellyZombie
{
    /// Everything the player can do with a key or a button. Saved by name, so
    /// the order here is free to change.
    public enum Act
    {
        Forward, Back, Left, Right, Jump, Crouch, Sprint,
        Draw, Erase, Precise,
        Use, Drop, Book, Body, View, Menu,
        Ready, NotReady, Talk,
        Prev, Next,
        Video, Photo, Folder,
    }

    /// ★ THE BINDINGS. Every action has one keyboard or mouse control and one
    /// controller button; the game asks Keys.Down / Held / Up and never a key
    /// by name. The Options' Controls tab changes them (saved in PlayerPrefs);
    /// the key chips on screen show whatever is bound, for the device last used.
    public static class Keys
    {
        struct Default { public string Key, Pad; }

        // "k:" a keyboard key, "m:" a mouse button, by the Input System's own control names
        static readonly Dictionary<Act, Default> Defaults = new Dictionary<Act, Default>
        {
            [Act.Forward] = new Default { Key = "k:w", Pad = "" },
            [Act.Back] = new Default { Key = "k:s", Pad = "" },
            [Act.Left] = new Default { Key = "k:a", Pad = "" },
            [Act.Right] = new Default { Key = "k:d", Pad = "" },
            [Act.Jump] = new Default { Key = "k:space", Pad = "buttonSouth" },
            [Act.Crouch] = new Default { Key = "k:leftCtrl", Pad = "buttonEast" },
            [Act.Sprint] = new Default { Key = "k:leftShift", Pad = "leftStickPress" },
            [Act.Draw] = new Default { Key = "m:leftButton", Pad = "rightTrigger" },
            [Act.Erase] = new Default { Key = "m:rightButton", Pad = "leftTrigger" },
            [Act.Precise] = new Default { Key = "k:leftAlt", Pad = "dpad/up" },
            [Act.Use] = new Default { Key = "k:e", Pad = "buttonWest" },
            [Act.Drop] = new Default { Key = "k:f", Pad = "rightShoulder" },
            [Act.Book] = new Default { Key = "k:g", Pad = "buttonNorth" },
            [Act.Body] = new Default { Key = "k:r", Pad = "leftShoulder" },
            [Act.View] = new Default { Key = "k:tab", Pad = "rightStickPress" },
            [Act.Menu] = new Default { Key = "k:escape", Pad = "start" },
            [Act.Ready] = new Default { Key = "k:b", Pad = "select" },
            [Act.NotReady] = new Default { Key = "k:c", Pad = "" },
            [Act.Talk] = new Default { Key = "k:v", Pad = "" },
            [Act.Prev] = new Default { Key = "", Pad = "dpad/left" },
            [Act.Next] = new Default { Key = "", Pad = "dpad/right" },
            [Act.Video] = new Default { Key = "k:i", Pad = "" },
            [Act.Photo] = new Default { Key = "k:p", Pad = "" },
            [Act.Folder] = new Default { Key = "k:o", Pad = "" },
        };

        /// The actions in the order the Controls tab lists them.
        public static readonly Act[] Listed =
        {
            Act.Forward, Act.Back, Act.Left, Act.Right, Act.Jump, Act.Crouch, Act.Sprint,
            Act.Draw, Act.Erase, Act.Precise,
            Act.Use, Act.Drop, Act.Book, Act.Body, Act.View, Act.Menu,
            Act.Ready, Act.NotReady, Act.Talk, Act.Prev, Act.Next,
            Act.Video, Act.Photo, Act.Folder,
        };

        const string PrefsKey = "sz_keys";
        static readonly int Count = Enum.GetValues(typeof(Act)).Length;
        static readonly string[] _key = new string[Count];
        static readonly string[] _pad = new string[Count];
        static readonly ButtonControl[] _keyCtl = new ButtonControl[Count];
        static readonly ButtonControl[] _padCtl = new ButtonControl[Count];
        static bool _loaded, _resolved;
        static Keyboard _forKeyboard;
        static Mouse _forMouse;
        static Gamepad _forPad;

        /// Bumps whenever a binding or the device in use changes: what shows keys redraws on it.
        public static int Stamp { get; private set; }
        /// The controller was the last thing touched: chips show its buttons.
        public static bool PadActive { get; private set; }
        /// A binding is waiting for its new key or button.
        public static bool Listening { get; private set; }

        [Serializable] class Saved { public List<string> Acts = new List<string>(), Keys = new List<string>(), Pads = new List<string>(); }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            foreach (var kv in Defaults) { _key[(int)kv.Key] = kv.Value.Key; _pad[(int)kv.Key] = kv.Value.Pad; }
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var s = JsonUtility.FromJson<Saved>(json);
                for (int i = 0; s != null && i < s.Acts.Count && i < s.Keys.Count && i < s.Pads.Count; i++)
                    if (Enum.TryParse(s.Acts[i], out Act a)) { _key[(int)a] = s.Keys[i] ?? ""; _pad[(int)a] = s.Pads[i] ?? ""; }
            }
            catch (Exception) { }
        }

        static void Save()
        {
            var s = new Saved();
            for (int i = 0; i < Count; i++) { s.Acts.Add(((Act)i).ToString()); s.Keys.Add(_key[i]); s.Pads.Add(_pad[i]); }
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(s));
            PlayerPrefs.Save();
        }

        /// Every action back to the keys and buttons the game ships with.
        public static void ResetAll()
        {
            foreach (var kv in Defaults) { _key[(int)kv.Key] = kv.Value.Key; _pad[(int)kv.Key] = kv.Value.Pad; }
            _resolved = false;
            Save();
            Stamp++;
        }

        // ------------------------------------------------------------ reading --
        static void Resolve()
        {
            Load();
            var kb = Keyboard.current; var ms = Mouse.current; var gp = Gamepad.current;
            if (_resolved && kb == _forKeyboard && ms == _forMouse && gp == _forPad) return;
            _resolved = true; _forKeyboard = kb; _forMouse = ms; _forPad = gp;
            for (int i = 0; i < Count; i++)
            {
                _keyCtl[i] = Find(_key[i], kb, ms);
                _padCtl[i] = gp != null && !string.IsNullOrEmpty(_pad[i]) ? gp.TryGetChildControl<ButtonControl>(_pad[i]) : null;
            }
        }

        static ButtonControl Find(string bound, Keyboard kb, Mouse ms)
        {
            if (string.IsNullOrEmpty(bound) || bound.Length < 3) return null;
            string name = bound.Substring(2);
            if (bound[0] == 'k') return kb != null ? kb.TryGetChildControl<ButtonControl>(name) : null;
            return ms != null ? ms.TryGetChildControl<ButtonControl>(name) : null;
        }

        public static bool Down(Act a)
        {
            Resolve();
            if (Listening || Time.frameCount <= _swallowUntil) return false;
            var k = _keyCtl[(int)a]; var p = Pad(a);
            return (k != null && k.wasPressedThisFrame) || (p != null && p.wasPressedThisFrame);
        }

        public static bool Held(Act a)
        {
            Resolve();
            if (Listening) return false;
            var k = _keyCtl[(int)a]; var p = Pad(a);
            return (k != null && k.isPressed) || (p != null && p.isPressed);
        }

        public static bool Up(Act a)
        {
            Resolve();
            if (Listening) return false;
            var k = _keyCtl[(int)a]; var p = Pad(a);
            return (k != null && k.wasReleasedThisFrame) || (p != null && p.wasReleasedThisFrame);
        }

        /// The action's controller button; none while a panel has the controller and the button
        /// is one the panel uses (A, B, the D-pad), whatever is bound to it.
        static ButtonControl Pad(Act a)
        {
            var p = _padCtl[(int)a];
            if (p == null || !PadFocus.Engaged || _forPad == null) return p;
            var gp = _forPad;
            bool panels = p == gp.buttonSouth || p == gp.buttonEast
                || p == gp.dpad.up || p == gp.dpad.down || p == gp.dpad.left || p == gp.dpad.right;
            return panels ? null : p;
        }

        /// A menu's way back on a controller (B), whatever the bindings say.
        public static bool BackDown => !Swallowing && Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;

        /// A binding waits for its button, or has only just got it: that press is nobody else's.
        public static bool Swallowing => Listening || Time.frameCount <= _swallowUntil;

        /// This frame's press was a panel's (PadFocus taking the controller back): nobody else acts on it.
        internal static void Swallow() => _swallowUntil = Time.frameCount;

        /// Walking: the four move keys (the arrows always work too) and the left stick.
        public static Vector2 Move
        {
            get
            {
                var mv = Vector2.zero;
                if (Held(Act.Forward)) mv.y += 1f;
                if (Held(Act.Back)) mv.y -= 1f;
                if (Held(Act.Right)) mv.x += 1f;
                if (Held(Act.Left)) mv.x -= 1f;
                var kb = Keyboard.current;
                if (kb != null && !Listening)
                {
                    if (kb.upArrowKey.isPressed) mv.y += 1f;
                    if (kb.downArrowKey.isPressed) mv.y -= 1f;
                    if (kb.rightArrowKey.isPressed) mv.x += 1f;
                    if (kb.leftArrowKey.isPressed) mv.x -= 1f;
                }
                var gp = Gamepad.current;
                if (gp != null && !PadFocus.Engaged) // a panel that has the controller has its stick
                {
                    var stick = gp.leftStick.ReadValue();
                    if (stick.sqrMagnitude > 0.04f) mv += stick;
                }
                return Vector2.ClampMagnitude(mv, 1f);
            }
        }

        /// A full push of the right stick turns this many degrees a second at stick sensitivity 1:
        /// the turn it had while it rode the mouse's default look sensitivity (0.12 x 1400).
        public const float StickDegrees = 168f;

        /// The right stick's own look sensitivity (Options > Game), a multiple of its full-push turn.
        public static float StickSensitivity
        {
            get { if (_stickSens < 0f) _stickSens = PlayerPrefs.GetFloat(StickSensPref, 1f); return _stickSens; }
            set => _stickSens = value;
        }
        public const string StickSensPref = "sz_stick_sens";
        static float _stickSens = -1f;

        /// While the cursor is free (Alt on a controller) the right stick is the pointer, as the mouse is on
        /// a PC: the system cursor itself moves, so the pen and the menus follow it. A full push crosses the
        /// screen's height in StickCursorSeconds at stick sensitivity 1; a light push creeps.
        public static void StickCursor(float dt)
        {
            var mouse = Mouse.current;
            if (mouse == null || Gamepad.current == null) return;
            Vector2 s = LookStick;
            if (s == Vector2.zero) return;
            // on from last frame's move, else from wherever the pointer is now
            if (Time.frameCount - _pointerFrame > 1) _pointer = mouse.position.ReadValue();
            s *= s.magnitude; // squared: the pen tip holds steady on a light push
            _pointer += s * (Screen.height / StickCursorSeconds * StickSensitivity * dt);
            _pointer.x = Mathf.Clamp(_pointer.x, 0f, Screen.width - 1f);
            _pointer.y = Mathf.Clamp(_pointer.y, 0f, Screen.height - 1f);
            mouse.WarpCursorPosition(_pointer);
            _pointerFrame = Time.frameCount;
        }
        const float StickCursorSeconds = 1.2f;
        static Vector2 _pointer;
        static int _pointerFrame = -10;

        /// The right stick, for looking around; zero without a controller.
        public static Vector2 LookStick
        {
            get
            {
                var gp = Gamepad.current;
                if (gp == null) return Vector2.zero;
                var s = gp.rightStick.ReadValue();
                return s.sqrMagnitude > 0.0225f ? s : Vector2.zero;
            }
        }

        // ------------------------------------------------------------- labels --
        /// What a key chip shows for this action, on the device last used.
        public static string Label(Act a)
        {
            Load();
            if (PadActive && !string.IsNullOrEmpty(_pad[(int)a])) return PadLabel(a);
            string k = KeyLabel(a);
            return k.Length > 0 ? k : PadLabel(a);
        }

        public static string KeyLabel(Act a)
        {
            Resolve();
            string bound = _key[(int)a];
            if (string.IsNullOrEmpty(bound)) return "";
            string name = bound.Substring(2);
            switch (name)
            {
                case "leftButton": return "LMB";
                case "rightButton": return "RMB";
                case "middleButton": return "MMB";
                case "forwardButton": return "M5";
                case "backButton": return "M4";
                case "space": return "SPACE";
                case "leftCtrl": case "rightCtrl": return "CTRL";
                case "leftShift": case "rightShift": return "SHIFT";
                case "leftAlt": case "rightAlt": return "ALT";
                case "escape": return "ESC";
                case "tab": return "TAB";
                case "enter": case "numpadEnter": return "ENTER";
                case "backspace": return "BKSP";
            }
            var ctl = _keyCtl[(int)a];
            string shown = ctl != null && !string.IsNullOrEmpty(ctl.displayName) ? ctl.displayName : name;
            return shown.ToUpperInvariant();
        }

        public static string PadLabel(Act a)
        {
            Load();
            switch (_pad[(int)a])
            {
                case "buttonSouth": return "A";
                case "buttonEast": return "B";
                case "buttonWest": return "X";
                case "buttonNorth": return "Y";
                case "leftShoulder": return "LB";
                case "rightShoulder": return "RB";
                case "leftTrigger": return "LT";
                case "rightTrigger": return "RT";
                case "leftStickPress": return "L3";
                case "rightStickPress": return "R3";
                case "start": return "START";
                case "select": return "VIEW";
                case "dpad/left": return "D-LEFT";
                case "dpad/right": return "D-RIGHT";
                case "dpad/up": return "D-UP";
                case "dpad/down": return "D-DOWN";
                case "": case null: return "";
                default: return _pad[(int)a].ToUpperInvariant();
            }
        }

        // the letters the prompts were written with, and the action each one stands for
        static readonly Dictionary<string, Act> Written = new Dictionary<string, Act>
        {
            ["E"] = Act.Use, ["F"] = Act.Drop, ["G"] = Act.Book, ["R"] = Act.Body, ["TAB"] = Act.View,
            ["SPACE"] = Act.Jump, ["CTRL"] = Act.Crouch, ["ALT"] = Act.Precise, ["LMB"] = Act.Draw,
            ["RMB"] = Act.Erase, ["B"] = Act.Ready, ["C"] = Act.NotReady, ["ESC"] = Act.Menu,
            ["I"] = Act.Video, ["P"] = Act.Photo, ["O"] = Act.Folder, ["V"] = Act.Talk,
        };

        /// A chip written as "E" shows what Use is bound to now; anything else shows as written.
        public static string Shown(string written) =>
            written != null && Written.TryGetValue(written, out var a) ? Label(a) : written;

        // ---------------------------------------------------------- rebinding --
        static IDisposable _listen;

        /// Waits for the next key, mouse button (pad = false) or controller button (pad = true)
        /// and binds it. Escape leaves things as they were. Whoever had that control gets this
        /// action's old one, so nothing is ever left without a key.
        public static void Rebind(Act a, bool pad, Action done)
        {
            Load();
            Cancel();
            Listening = true;
            _listen = InputSystem.onAnyButtonPress.Call(control =>
            {
                if (control == null || control.name == "anyKey") return;
                var device = control.device;
                if (device is Keyboard && control.name == "escape") { Finish(done); return; }
                string child = control.path.Substring(device.path.Length + 1);
                if (child.StartsWith("leftStick/") || child.StartsWith("rightStick/")) return; // a stick leaning is no button
                string bound;
                if (pad) { if (!(device is Gamepad)) return; bound = child; }
                else if (device is Keyboard) bound = "k:" + child;
                else if (device is Mouse) bound = "m:" + child;
                else return;

                var table = pad ? _pad : _key;
                string old = table[(int)a];
                for (int i = 0; i < Count; i++)
                    if (i != (int)a && table[i] == bound) table[i] = old;
                table[(int)a] = bound;
                _resolved = false;
                Save();
                Finish(done);
            });
        }

        public static void Cancel()
        {
            _listen?.Dispose();
            _listen = null;
            Listening = false;
        }

        static void Finish(Action done)
        {
            Cancel();
            _swallowUntil = Time.frameCount + 1; // the press that bound it must not also act
            Stamp++;
            done?.Invoke();
        }

        static int _swallowUntil;

        // ------------------------------------------------- which device is in use --
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("SZ_Keys");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<KeysBeat>();
        }

        /// Unscaled time of the last press, stick push or mouse move on this machine.
        public static float LastInputAt { get; private set; }
        /// Seconds since anyone touched the controls here.
        public static float IdleSeconds => Time.unscaledTime - LastInputAt;

        internal static void Watch()
        {
            var gp = Gamepad.current;
            bool padNow = PadActive;
            bool any = false;
            if (gp != null && gp.wasUpdatedThisFrame)
            {
                bool touched = gp.leftStick.ReadValue().sqrMagnitude > 0.09f || gp.rightStick.ReadValue().sqrMagnitude > 0.09f
                    || gp.leftTrigger.ReadValue() > 0.3f || gp.rightTrigger.ReadValue() > 0.3f;
                if (!touched)
                    foreach (var c in gp.allControls)
                        if (c is ButtonControl b && !b.synthetic && b.wasPressedThisFrame) { touched = true; break; }
                if (touched) { padNow = true; any = true; }
            }
            var kb = Keyboard.current; var ms = Mouse.current;
            if ((kb != null && kb.anyKey.wasPressedThisFrame)
                || (ms != null && (ms.leftButton.wasPressedThisFrame || ms.rightButton.wasPressedThisFrame
                    || (ms.delta.ReadValue().sqrMagnitude > 25f && Time.frameCount > _pointerFrame + 2))))
            { padNow = false; any = true; }
            if (any) LastInputAt = Time.unscaledTime;
            if (gp == null) padNow = false;
            if (padNow == PadActive) return;
            PadActive = padNow;
            Stamp++;
        }
    }

    class KeysBeat : MonoBehaviour
    {
        void Update()
        {
            Keys.Watch();
            PadFocus.Tick();
        }
    }
}
