using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Runtime uGUI factory — the whole interface is BUILT FROM CODE on one
    /// overlay canvas (zero scene wiring; Marko's scenes stay his). Dressed by
    /// UISkin (Kenney Adventure); falls back to flat colors if the skin isn't
    /// built, so nothing ever breaks. Legacy Text now, font swaps via UISkin.
    public static class UIKit
    {
        public static readonly Color Ink = new Color(0.13f, 0.10f, 0.07f);       // dark ink on parchment
        public static readonly Color Parchment = new Color(0.98f, 0.94f, 0.82f); // light text on dark
        public static readonly Color Gold = new Color(1f, 0.85f, 0.4f);

        static Canvas _canvas;
        static Font _font;

        public static RectTransform Root
        {
            get
            {
                EnsureCanvas();
                return (RectTransform)_canvas.transform;
            }
        }

        public static Font Font
        {
            get
            {
                if (_font == null)
                    _font = UISkin.I != null && UISkin.I.TextFont != null
                        ? UISkin.I.TextFont
                        : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// True while a UI text field has keyboard focus — gameplay input
        /// (weapon slots, emotes, movement) must stand down.
        public static bool Typing =>
            EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject != null
            && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;

        // every group a system owns and shows/hides itself — one per manager
        static readonly HashSet<string> ManagedSurfaces = new HashSet<string>
        {
            "MainMenu", "Settings", "PauseMenu", "HUD", "Vitals", "RoundBanner",
            "Downed", "LobbyBanner", "LobbyBoard", "NetPanel", "PromptGroup", "PromptChips",
            "RuneChooser", "PowerupChooser", "Announcement", "SealGallery", "JoinPanel", "HatPanel",
        };

        static void EnsureCanvas()
        {
            if (_canvas != null) return;

            // MARKO'S PREFAB IS LAW: assign it on the UISkin asset (UI Prefab
            // slot) — or bake one to Resources/SZ_UI via the menu tool. The
            // canvas comes from it UNTOUCHED; factories adopt its children
            // (rebind clicks, feed live counters) and only build what the
            // prefab lacks. Nothing of his is ever restyled or reworded.
            var prefab = UISkin.I != null && UISkin.I.UIPrefab != null
                ? UISkin.I.UIPrefab
                : Resources.Load<GameObject>("SZ_UI");
            if (prefab != null)
            {
                var pgo = UnityEngine.Object.Instantiate(prefab);
                pgo.name = "SZ_UI";
                UnityEngine.Object.DontDestroyOnLoad(pgo);
                _canvas = pgo.GetComponent<Canvas>();
                if (_canvas != null)
                {
                    // Marko authors surface groups VISIBLE in the prefab
                    // (easier to edit) — but each belongs to a manager, and
                    // a scene without that manager must never show it (the
                    // main menu was greeting people mid-Lobby). Managed
                    // groups start ASLEEP; Group() wakes each one the moment
                    // its owner builds it. Anything unmanaged is left alone.
                    foreach (Transform child in _canvas.transform)
                        if (ManagedSurfaces.Contains(child.name))
                            child.gameObject.SetActive(false);
                    EnsureEventSystem();
                    Debug.Log("[SpellyZombie] UI: SZ_UI.prefab adopted. Edits in the prefab are law.");
                    return;
                }
                UnityEngine.Object.Destroy(pgo); // malformed — code takes over
            }

            var go = new GameObject("SZ_UI");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 80;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
        }

        // ------------------------------------------------ prefab adoption --
        // Factories claim prefab children by NAME, in sibling order, at most
        // one per frame-scoped pass (so two Labels under one parent map 1:1).
        // Adopted elements keep every value Marko set in the prefab — Place
        // and Stretch refuse to touch them; only behavior (text, clicks) and
        // brand-new elements come from code.
        static readonly HashSet<int> _adopted = new HashSet<int>();
        static int _claimFrame = -1;
        static readonly HashSet<int> _claimedNow = new HashSet<int>();

        public static bool WasAdopted(Component c)
            => c != null && _adopted.Contains(c.gameObject.GetInstanceID());

        static void RefreshClaims()
        {
            if (Time.frameCount == _claimFrame) return;
            _claimFrame = Time.frameCount;
            _claimedNow.Clear();
        }

        static void MarkNew(GameObject go)
        {
            RefreshClaims();
            _claimedNow.Add(go.GetInstanceID());
        }

        /// Take a live group out of service NOW. Unity defers Destroy to end
        /// of frame, so "destroy then rebuild" in one frame used to re-adopt
        /// the dying corpse and vanish with it (pause-menu Options, powerup
        /// rerolls). Renamed + claimed = invisible to same-frame Group().
        public static void Retire(RectTransform group)
        {
            if (group == null) return;
            RefreshClaims();
            _claimedNow.Add(group.gameObject.GetInstanceID());
            group.gameObject.name = "_retired";
            UnityEngine.Object.Destroy(group.gameObject);
        }

        static GameObject Adopt(RectTransform parent, string name, bool exact)
        {
            if (parent == null) return null;
            RefreshClaims();
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i).gameObject;
                int id = c.GetInstanceID();
                if (_claimedNow.Contains(id)) continue;
                if (!(exact ? c.name == name : c.name.StartsWith(name))) continue;
                _claimedNow.Add(id);
                _adopted.Add(id);
                return c;
            }
            return null;
        }

        static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("SZ_EventSystem");
            UnityEngine.Object.DontDestroyOnLoad(es);
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // ------------------------------------------------------- primitives --

        /// Empty anchored group — the container each system owns and hides.
        /// Resolution order: (1) adopt an existing child of that name,
        /// (2) instantiate MARKO'S PREFAB of that name from the UISkin
        /// registry — the whole subtree is his, untouchable, (3) code-build.
        public static RectTransform Group(RectTransform parent, string name)
        {
            var p = parent != null ? parent : Root;
            var found = Adopt(p, name, exact: true);
            if (found != null)
            {
                found.SetActive(true); // hidden groups wake when re-adopted
                return (RectTransform)found.transform;
            }

            var surface = UISkin.SurfacePrefab(name);
            if (surface != null)
            {
                var his = UnityEngine.Object.Instantiate(surface, p, false);
                his.name = name;
                MarkNew(his);
                foreach (var t in his.GetComponentsInChildren<Transform>(true))
                    _adopted.Add(t.gameObject.GetInstanceID()); // every value inside is HIS
                his.SetActive(true);
                return (RectTransform)his.transform;
            }

            var go = new GameObject(name, typeof(RectTransform));
            MarkNew(go);
            var rt = (RectTransform)go.transform;
            rt.SetParent(p, false);
            return rt;
        }

        /// Place with a single anchor point (Kenney sprites want fixed sizes).
        /// Adopted elements keep Marko's prefab values — hands off.
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            if (WasAdopted(rt)) return rt;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// Stretch across the whole parent (dim layers, fullscreen groups).
        public static RectTransform Stretch(RectTransform rt)
        {
            if (WasAdopted(rt)) return rt;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Panel(RectTransform parent, Sprite sprite, Color? tint = null)
        {
            var found = Adopt(parent, "Panel", exact: false);
            if (found != null) return found.GetComponent<Image>();
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            MarkNew(go);
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = tint ?? Color.white;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(RectTransform parent, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = false)
        {
            var found = Adopt(parent, "Label", exact: false);
            if (found != null)
            {
                var ft = found.GetComponent<Text>();
                if (ft != null) return ft; // Marko's text stays HIS — live
                                           // counters overwrite it themselves
            }
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            MarkNew(go);
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = anchor;
            t.text = text;
            t.raycastTarget = false;
            // letters NEVER overflow their element: best-fit shrinks long text
            // to its rect (size is the ceiling, not a promise)
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = size;
            t.resizeTextMinSize = Mathf.Max(8, size / 2);
            return t;
        }

        public static Button Button(RectTransform parent, string label, Action onClick,
            Sprite sprite = null, int fontSize = 20, Color? textColor = null)
        {
            var found = Adopt(parent, "Button", exact: false);
            if (found != null)
            {
                var fb = found.GetComponent<Button>();
                if (fb != null)
                {
                    fb.onClick.RemoveAllListeners(); // lambdas don't serialize — rebind
                    if (onClick != null) fb.onClick.AddListener(() => onClick());
                    return fb; // caption included: Marko's words, untouched
                }
            }
            var skin = UISkin.I;
            if (sprite == null && skin != null) sprite = skin.ButtonBrown;
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            MarkNew(go);
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = sprite != null ? Color.white : new Color(0.35f, 0.28f, 0.2f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var text = Label((RectTransform)go.transform, label, fontSize, textColor ?? Ink, TextAnchor.MiddleCenter, true);
            Stretch((RectTransform)text.transform);
            return btn;
        }

        /// A little keycap glyph — "[E]" done properly with the button sprite.
        public static RectTransform Keycap(RectTransform parent, string key, float size = 34f)
        {
            var cap = Group(parent, "Key_" + key);
            if (WasAdopted(cap)) return cap; // Marko's cap, children included
            cap.sizeDelta = new Vector2(size * 1.25f, size);
            var img = Panel(cap, UISkin.I != null ? UISkin.I.ButtonGrey : null);
            Stretch((RectTransform)img.transform);
            var t = Label(cap, key, Mathf.RoundToInt(size * 0.52f), Ink, TextAnchor.MiddleCenter, true);
            Stretch((RectTransform)t.transform);
            return cap;
        }

        // ------------------------------------------------------------- bars --

        public class UIBar
        {
            public RectTransform Rt;
            public Image Fill;

            public void Set(float t)
            {
                if (Fill == null) return;
                var rt = Fill.rectTransform;
                rt.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
            }
        }

        public static UIBar Bar(RectTransform parent, Sprite fill, Vector2 size, Color? fillTint = null)
        {
            var skin = UISkin.I;
            var group = Group(parent, "Bar");
            if (WasAdopted(group))
            {
                // Marko's bar: back = first Image child, fill = second
                var imgs = group.GetComponentsInChildren<Image>(true);
                return new UIBar { Rt = group, Fill = imgs.Length > 1 ? imgs[1] : (imgs.Length > 0 ? imgs[0] : null) };
            }
            group.sizeDelta = size;

            var back = Panel(group, skin != null ? skin.ProgressBack : null,
                skin != null ? Color.white : new Color(0f, 0f, 0f, 0.55f));
            Stretch((RectTransform)back.transform);

            var fillImg = Panel(group, fill, fillTint ?? Color.white);
            var fr = (RectTransform)fillImg.transform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(3f, 3f);
            fr.offsetMax = new Vector2(-3f, -3f);
            // fill by anchor: a nested rect that shrinks horizontally
            var bar = new UIBar { Rt = group, Fill = fillImg };
            return bar;
        }

        // ----------------------------------------------------------- slider --

        public static Slider Slider(RectTransform parent, float min, float max, float value,
            Action<float> onChange)
        {
            var foundSlider = Adopt(parent, "Slider", exact: false);
            if (foundSlider != null)
            {
                var fs = foundSlider.GetComponent<Slider>();
                if (fs != null)
                {
                    fs.minValue = min;
                    fs.maxValue = max;
                    fs.SetValueWithoutNotify(value);
                    fs.onValueChanged.RemoveAllListeners();
                    if (onChange != null) fs.onValueChanged.AddListener(v => onChange(v));
                    return fs;
                }
            }
            var skin = UISkin.I;
            var go = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            MarkNew(go);
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            var back = Panel(rt, skin != null ? skin.ProgressBack : null,
                skin != null ? Color.white : new Color(0f, 0f, 0f, 0.5f));
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin = new Vector2(0f, 0.35f);
            backRt.anchorMax = new Vector2(1f, 0.65f);
            backRt.offsetMin = backRt.offsetMax = Vector2.zero;

            var fillArea = Group(rt, "FillArea");
            fillArea.anchorMin = new Vector2(0f, 0.35f);
            fillArea.anchorMax = new Vector2(1f, 0.65f);
            fillArea.offsetMin = new Vector2(4f, 0f);
            fillArea.offsetMax = new Vector2(-4f, 0f);
            var fill = Panel(fillArea, skin != null ? skin.ProgressGreen : null,
                skin != null ? Color.white : new Color(0.4f, 0.7f, 0.3f));
            Stretch((RectTransform)fill.transform);

            var handleArea = Group(rt, "HandleArea");
            Stretch(handleArea);
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(handleArea, false);
            var handleImg = handleGo.GetComponent<Image>();
            handleImg.sprite = skin != null ? skin.RoundBrown : null;
            handleImg.color = skin != null ? Color.white : new Color(0.8f, 0.7f, 0.5f);
            ((RectTransform)handleGo.transform).sizeDelta = new Vector2(26f, 26f);

            var slider = go.GetComponent<Slider>();
            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handleGo.transform;
            slider.targetGraphic = handleImg;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (onChange != null) slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        // ------------------------------------------------------ input field --

        public static InputField Input(RectTransform parent, string value, Action<string> onChange)
        {
            var skin = UISkin.I;
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = skin != null ? skin.PanelGreyDark : null;
            img.type = Image.Type.Sliced;
            img.color = skin != null ? Color.white : new Color(0.15f, 0.15f, 0.18f);

            var text = Label((RectTransform)go.transform, "", 18, Parchment, TextAnchor.MiddleLeft);
            var tr = (RectTransform)text.transform;
            Stretch(tr);
            tr.offsetMin = new Vector2(10f, 2f);
            tr.offsetMax = new Vector2(-10f, -2f);
            text.raycastTarget = false;
            text.supportRichText = false;

            var field = go.GetComponent<InputField>();
            field.textComponent = text;
            field.text = value;
            if (onChange != null) field.onValueChanged.AddListener(v => onChange(v));
            return field;
        }
    }

    /// The one contextual prompt at the bottom of the screen: "[E] do a thing".
    /// Callers refresh it EVERY FRAME they want it visible (cauldrons, chest,
    /// doors later) — it hides itself the frame nobody asks for it.
    public class UIPrompt : MonoBehaviour
    {
        static UIPrompt _i;
        RectTransform _group;
        TMPro.TextMeshProUGUI _label; // TMP: prompts name runes by EMOJI (sprites)
        int _lastFrame = -1;

        public static void Show(string key, string text, Color? accent = null)
        {
            if (_i == null)
            {
                var go = new GameObject("UIPrompt");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<UIPrompt>();
                _i.BuildUI(key);
            }
            // record only — applied ONCE in LateUpdate. Applying here let two
            // same-frame callers with different keys destroy-and-rebuild the
            // keycap against each other until one adopted the other's dying
            // corpse: a destroyed RectTransform, thrown on every frame after.
            _i._wantKey = key;
            _i._wantText = text;
            _i._wantColor = accent ?? UIKit.Gold;
            _i._lastFrame = Time.frameCount;
        }

        RectTransform _cap;
        string _capKey;

        void BuildUI(string key)
        {
            _group = UIKit.Group(UIKit.Root, "PromptGroup");
            UIKit.Place(_group, new Vector2(0.5f, 0f), new Vector2(0f, 118f), new Vector2(560f, 46f));

            var back = UIKit.Panel(_group, UISkin.I != null ? UISkin.I.PanelBrownDark : null,
                UISkin.I != null ? Color.white : new Color(0f, 0f, 0f, 0.6f));
            UIKit.Stretch((RectTransform)back.transform);

            _cap = UIKit.Keycap(_group, key, 32f);
            UIKit.Place(_cap, new Vector2(0f, 0.5f), new Vector2(28f, 0f), _cap.sizeDelta);
            _capKey = key;

            var labelGo = new GameObject("PromptLabel", typeof(RectTransform));
            labelGo.transform.SetParent(_group, false);
            _label = labelGo.AddComponent<TMPro.TextMeshProUGUI>();
            _label.fontSize = 19f;
            _label.color = UIKit.Parchment;
            _label.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            _label.raycastTarget = false;
            var lr = (RectTransform)_label.transform;
            UIKit.Stretch(lr);
            lr.offsetMin = new Vector2(84f, 0f);
            lr.offsetMax = new Vector2(-14f, 0f);
        }

        string _wantKey, _wantText;
        Color _wantColor;

        // THE WEAK TIER IS A ROW OF CHIPS (Marko Aug 11, the block law: "you
        // can't put more information than one in one block... max 3 - but
        // this is the extreme maximum"). Each Offer this frame becomes its
        // own small pill — one key, one meaning — laid out by a layout
        // group. Any Show() (a mode's own instruction, an urgent state)
        // hides the whole row: focus beats navigation.
        struct Chip { public string Key, Text; public Color Tint; }
        readonly List<Chip> _chips = new List<Chip>();
        int _chipFrame = -1;
        RectTransform _chipRow;
        string _chipSig = "";

        /// One chip: a single fact behind a single key. Speaks only when no
        /// Show() lands this frame; the fourth Offer of a frame is refused —
        /// three blocks is Marko's extreme maximum, enforced here.
        public static void Offer(string key, string text, Color? accent = null)
        {
            if (_i == null) Show(key, text, accent); // first call builds the UI
            if (_i._chipFrame != Time.frameCount) { _i._chips.Clear(); _i._chipFrame = Time.frameCount; }
            if (_i._chips.Count >= 3) return;
            _i._chips.Add(new Chip { Key = key, Text = text, Tint = accent ?? UIKit.Parchment });
        }

        void LateUpdate()
        {
            if (_group == null) return;
            bool strong = Time.frameCount <= _lastFrame;   // someone claimed the slot
            bool chips = !strong && Time.frameCount <= _chipFrame + 1 && _chips.Count > 0;

            if (_group.gameObject.activeSelf != strong) _group.gameObject.SetActive(strong);
            if (strong)
            {
                if (_wantKey != _capKey || _cap == null) // == null: self-heal a dead cap
                {
                    UIKit.Retire(_cap); // corpse-proof — never re-adopted same frame
                    _cap = UIKit.Keycap(_group, _wantKey, 32f);
                    UIKit.Place(_cap, new Vector2(0f, 0.5f), new Vector2(28f, 0f), _cap.sizeDelta);
                    _capKey = _wantKey;
                }
                _label.text = _wantText;
                _label.color = _wantColor;
            }

            if (_chipRow == null && chips) BuildChipRow();
            if (_chipRow == null) return;
            if (_chipRow.gameObject.activeSelf != chips) _chipRow.gameObject.SetActive(chips);
            if (!chips) return;

            string sig = "";
            for (int i = 0; i < _chips.Count; i++)
                sig += _chips[i].Key + "|" + _chips[i].Text + "|";
            if (sig == _chipSig) return;
            // CHURN GUARD (Marko: "my ui is changing non stop" + lag): two
            // callers alternating chip sets frame-to-frame must not thrash
            // Retire/rebuild/layout every frame — a change lands at most
            // five times a second, invisible for honest changes, a wall
            // against fights.
            if (Time.unscaledTime < _chipRebuiltAt + 0.2f) return;
            _chipRebuiltAt = Time.unscaledTime;
            _chipSig = sig;
            FillChips();
        }

        float _chipRebuiltAt;

        void BuildChipRow()
        {
            _chipRow = UIKit.Group(UIKit.Root, "PromptChips");
            UIKit.Place(_chipRow, new Vector2(0.5f, 0f), new Vector2(0f, 118f), new Vector2(10f, 46f));
            if (UIKit.WasAdopted(_chipRow)) return; // Marko's row: his layout, his law
            var lay = _chipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            lay.spacing = 14f;
            lay.childAlignment = TextAnchor.MiddleCenter;
            lay.childControlWidth = true;
            lay.childControlHeight = true;
            lay.childForceExpandWidth = false;
            lay.childForceExpandHeight = false;
            var fit = _chipRow.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void FillChips()
        {
            for (int i = _chipRow.childCount - 1; i >= 0; i--)
                UIKit.Retire((RectTransform)_chipRow.GetChild(i));

            foreach (var c in _chips)
            {
                var pill = new GameObject("Chip", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
                pill.transform.SetParent(_chipRow, false);
                var img = pill.GetComponent<Image>();
                img.sprite = UISkin.I != null ? UISkin.I.PanelBrownDark : null;
                img.type = img.sprite != null && img.sprite.border != Vector4.zero
                    ? Image.Type.Sliced : Image.Type.Simple;
                img.color = img.sprite != null ? Color.white : new Color(0f, 0f, 0f, 0.6f);
                img.raycastTarget = false;
                var lay = pill.GetComponent<HorizontalLayoutGroup>();
                lay.padding = new RectOffset(8, 14, 6, 6);
                lay.spacing = 9;
                lay.childAlignment = TextAnchor.MiddleCenter;
                lay.childControlWidth = true;
                lay.childControlHeight = true;
                lay.childForceExpandWidth = false;
                lay.childForceExpandHeight = false;

                var cap = UIKit.Keycap((RectTransform)pill.transform, c.Key, 30f);
                var le = cap.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = cap.sizeDelta.x;
                le.preferredHeight = cap.sizeDelta.y;

                var label = UIKit.Label((RectTransform)pill.transform, c.Text, 18, c.Tint,
                    TextAnchor.MiddleCenter, true);
                label.resizeTextForBestFit = false;         // preferredWidth sizes the pill
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }
    }
}
