using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Runtime uGUI factory: builds the whole interface on one overlay canvas.
    /// Styled by UISkin, flat-color fallback without it. Legacy Text; font via UISkin.
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
                if (_font == null) _font = LocFonts.LegacyFor(Loc.LanguageCode);
                return _font;
            }
        }
        public static Font CachedFont => _font;
        public static void ForgetFont() => _font = null;

        /// True while a UI text field has keyboard focus - gameplay input
        /// (weapon slots, emotes, movement) must stand down.
        public static bool Typing =>
            EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject != null
            && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null;

        // ---- immersive mode: a canvas switch, not per-system flags ----
        public static bool Immersive
        {
            get => _immersive;
            set
            {
                _immersive = value;
                PlayerPrefs.SetInt("sz_immersive", value ? 1 : 0);
                PlayerPrefs.Save();
                TickImmersive();
            }
        }

        static bool _immersive = PlayerPrefs.GetInt("sz_immersive", 0) == 1;

        /// Screens the player opened; these outrank immersive.
        static bool ModalOpen =>
            GameMenu.IsOpen || UIKit.Typing || HatPillar.PanelOpen
            || LobbyStand.PanelOpen || PoseStudio.IsOpen || Powerups.IsChoosing
            || LobbyInspect.PanelOpen || ActiveScene.Name == "Menu";

        /// Ticked by SideBootstrap, which survives scene loads.
        public static void TickImmersive()
        {
            if (_canvas == null) return;
            bool show = !_immersive || ModalOpen;
            if (_canvas.enabled != show) _canvas.enabled = show;
        }

        /// Floating key badge - one style for all world-anchored key hints.
        public static RectTransform KeyBadge(RectTransform parent, string name,
            string letter, out Text letterText, out Image back)
        {
            var ui = Group(parent, name);
            ui.sizeDelta = new Vector2(44f, 44f);
            var skin = UISkin.I;
            back = Panel(ui, skin != null ? skin.RoundBrown : null,
                skin != null ? Color.white : new Color(0.95f, 0.93f, 0.85f, 0.9f));
            Stretch((RectTransform)back.transform);
            letterText = Label(ui, letter, 20, new Color(0.15f, 0.1f, 0.2f), TextAnchor.MiddleCenter, true);
            Stretch((RectTransform)letterText.transform);
            return ui;
        }

        // world-anchored floats (aim keys, offer badges) survive immersive mode
        static Canvas _floatCanvas;
        public static RectTransform FloatRoot
        {
            get
            {
                _ = Root; // the skin canvas first, so its scaler can be copied
                if (_floatCanvas == null)
                {
                    var go = new GameObject("SZ_FloatUI");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _floatCanvas = go.AddComponent<Canvas>();
                    _floatCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _floatCanvas.sortingOrder = -1;
                    var ms = _canvas != null ? _canvas.GetComponent<CanvasScaler>() : null;
                    if (ms != null)
                    {
                        var s = go.AddComponent<CanvasScaler>();
                        s.uiScaleMode = ms.uiScaleMode;
                        s.referenceResolution = ms.referenceResolution;
                        s.screenMatchMode = ms.screenMatchMode;
                        s.matchWidthOrHeight = ms.matchWidthOrHeight;
                    }
                }
                return (RectTransform)_floatCanvas.transform;
            }
        }

        // every group a system owns and shows/hides itself - one per manager
        static readonly HashSet<string> ManagedSurfaces = new HashSet<string>
        {
            "MainMenu", "Settings", "PauseMenu", "HUD", "Vitals", "RoundBanner",
            "Downed", "LobbyBanner", "LobbyBoard", "NetPanel", "PromptGroup", "PromptChips",
            "RuneChooser", "PowerupChooser", "Announcement", "SealGallery", "JoinPanel", "HatPanel",
        };

        static void EnsureCanvas()
        {
            if (_canvas != null) return;

            // Prefab source: UISkin's UI Prefab slot, else Resources/SZ_UI.
            // Factories adopt its children as-is and only build what it lacks.
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
                    // Managed groups start disabled; Group() re-enables each
                    // when its owner builds it. Unmanaged children are untouched.
                    foreach (Transform child in _canvas.transform)
                        if (ManagedSurfaces.Contains(child.name))
                            child.gameObject.SetActive(false);
                    EnsureEventSystem();
                    Debug.Log("[SpellyZombie] UI: SZ_UI.prefab adopted. Edits in the prefab are law.");
                    return;
                }
                UnityEngine.Object.Destroy(pgo); // malformed - code takes over
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
        // Factories claim prefab children by name, in sibling order, at most
        // one per frame-scoped pass. Adopted elements keep prefab values;
        // Place and Stretch skip them.
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

        /// Destroy is deferred to end of frame; renaming + claiming keeps
        /// same-frame Group() calls from re-adopting the dying object.
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

        /// Empty anchored group - the container each system owns and hides.
        /// Resolution order: adopt an existing child of that name, else
        /// instantiate the UISkin surface prefab, else code-build.
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
                var inst = UnityEngine.Object.Instantiate(surface, p, false);
                inst.name = name;
                MarkNew(inst);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    _adopted.Add(t.gameObject.GetInstanceID());
                inst.SetActive(true);
                return (RectTransform)inst.transform;
            }

            var go = new GameObject(name, typeof(RectTransform));
            MarkNew(go);
            var rt = (RectTransform)go.transform;
            rt.SetParent(p, false);
            return rt;
        }

        /// Place with a single anchor point. Adopted elements keep prefab values.
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
                if (ft != null) return ft; // adopted text is not overwritten here
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
            // best-fit shrinks long text to the rect; size is the max font size
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
                    fb.onClick.RemoveAllListeners(); // lambdas don't serialize - rebind
                    if (fb.GetComponent<ButtonJuice>() == null) fb.gameObject.AddComponent<ButtonJuice>();
                    if (onClick != null) fb.onClick.AddListener(() => ButtonJuice.Press(fb, onClick));
                    return fb; // adopted caption kept
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
            go.AddComponent<ButtonJuice>();
            if (onClick != null) btn.onClick.AddListener(() => ButtonJuice.Press(btn, onClick));

            var text = Label((RectTransform)go.transform, label, fontSize, textColor ?? Ink, TextAnchor.MiddleCenter, true);
            Stretch((RectTransform)text.transform);
            return btn;
        }

        /// A little keycap glyph — "[E]" done properly with the button sprite.
        public static RectTransform Keycap(RectTransform parent, string key, float size = 34f)
        {
            var cap = Group(parent, "Key_" + key);
            if (WasAdopted(cap)) return cap;
            cap.sizeDelta = new Vector2(size * 1.25f, size);
            var img = Panel(cap, UISkin.I != null ? UISkin.I.ButtonGrey : null);
            Stretch((RectTransform)img.transform);
            var t = Label(cap, key, Mathf.RoundToInt(size * 0.52f), Ink, TextAnchor.MiddleCenter, true);
            Stretch((RectTransform)t.transform);
            // a long key (Ctrl+1-9) widens the cap instead of spilling past its edge
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            cap.sizeDelta = new Vector2(Mathf.Max(size * 1.25f, t.preferredWidth + size * 0.6f), size);
            return cap;
        }

        // ------------------------------------------------------------ stacks --

        /// A vertical stack that sizes itself to its rows. Rows say their
        /// size with Row(); Gap() is empty space. Adopted rects keep the
        /// prefab's own layout.
        public static void Stack(RectTransform rt, int padX, int padY, float spacing)
        {
            if (WasAdopted(rt)) return;
            var lay = rt.GetComponent<VerticalLayoutGroup>();
            if (lay == null) lay = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            lay.padding = new RectOffset(padX, padX, padY, padY);
            lay.spacing = spacing;
            lay.childAlignment = TextAnchor.UpperCenter;
            lay.childControlWidth = true;
            lay.childControlHeight = true;
            lay.childForceExpandWidth = false;
            lay.childForceExpandHeight = false;
            var fit = rt.GetComponent<ContentSizeFitter>();
            if (fit == null) fit = rt.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// The size a row asks its stack for. Height below zero = as tall as
        /// the element says itself (wrapped text).
        public static T Row<T>(T c, float width, float height) where T : Component
        {
            if (c == null || WasAdopted(c)) return c;
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;
            return c;
        }

        /// Empty space inside a stack.
        public static RectTransform Gap(RectTransform parent, float height)
            => Row(Group(parent, "Gap"), 1f, height);

        /// A grid of fixed cells inside a stack; it reports its own size.
        public static RectTransform Grid(RectTransform parent, string name, Vector2 cell, float spacing, int columns)
        {
            var g = Group(parent, name);
            if (WasAdopted(g)) return g;
            var lay = g.gameObject.AddComponent<GridLayoutGroup>();
            lay.cellSize = cell;
            lay.spacing = new Vector2(spacing, spacing);
            lay.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            lay.constraintCount = columns;
            lay.childAlignment = TextAnchor.UpperCenter;
            return g;
        }

        /// A row of equal buttons inside a stack (a three-way switch).
        public static RectTransform Segments(RectTransform parent, float width, float height, float spacing)
        {
            var g = Group(parent, "Segments");
            Row(g, width, height);
            if (WasAdopted(g)) return g;
            var lay = g.gameObject.AddComponent<HorizontalLayoutGroup>();
            lay.spacing = spacing;
            lay.childAlignment = TextAnchor.MiddleCenter;
            lay.childControlWidth = true;
            lay.childControlHeight = true;
            lay.childForceExpandWidth = true;
            lay.childForceExpandHeight = false;
            return g;
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
                // back = first Image child, fill = second
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

            // the knob is round: the area is knob-high, so the slider's own
            // vertical stretch of the handle cannot make it taller than wide
            var handleArea = Group(rt, "HandleArea");
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.pivot = new Vector2(0.5f, 0.5f);
            handleArea.anchoredPosition = Vector2.zero;
            handleArea.sizeDelta = new Vector2(-20f, 26f);
            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(handleArea, false);
            var handleImg = handleGo.GetComponent<Image>();
            handleImg.sprite = skin != null ? skin.RoundBrown : null;
            handleImg.color = skin != null ? Color.white : new Color(0.8f, 0.7f, 0.5f);
            ((RectTransform)handleGo.transform).sizeDelta = new Vector2(26f, 0f);

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
    /// Callers refresh it every frame they want it visible; it hides itself
    /// the frame nobody asks for it.
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
            // record only - applied once in LateUpdate so same-frame callers
            // don't rebuild the keycap against each other
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

        // Weak tier: each Offer this frame becomes one chip, max 3 per frame.
        // Any Show() hides the whole row.
        struct Chip { public string Key, Text; public Color Tint; }
        readonly List<Chip> _chips = new List<Chip>();
        int _chipFrame = -1;
        RectTransform _chipRow;
        string _chipSig = "";

        /// One chip behind one key. Shown only when no Show() lands this
        /// frame; at most 3 offers per frame.
        public static void Offer(string key, string text, Color? accent = null)
        {
            if (_i == null) Show(key, text, accent); // first call builds the UI
            if (_i._chipFrame != Time.frameCount) { _i._chips.Clear(); _i._chipFrame = Time.frameCount; }
            // duplicate key+text offers collapse into one chip
            for (int i = 0; i < _i._chips.Count; i++)
                if (_i._chips[i].Key == key && _i._chips[i].Text == text) return;
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
                    UIKit.Retire(_cap); // never re-adopted same frame
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
            // rebuilds rate-limited to 5/s so alternating chip sets don't
            // thrash Retire/rebuild/layout every frame
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
            if (UIKit.WasAdopted(_chipRow)) return;
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
