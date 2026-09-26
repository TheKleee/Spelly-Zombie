using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
            SteamLobby.ShowIdle();
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
            Button MenuButton(string label, System.Action act, Sprite sprite = null)
                => UIKit.Row(UIKit.Button(col, label, act, sprite ?? (skin != null ? skin.ButtonBrown : null), 19), 250f, 50f);

            // a build before this one boxed two buttons: back into the column they go, in order, so the
            // prefab's buttons are found in a row again (they are adopted by name, in sibling order)
            var oldBox = col.Find(BoxName);
            if (oldBox != null)
            {
                int at = oldBox.GetSiblingIndex() + 1;
                for (int i = 0; i < oldBox.childCount; i++)
                {
                    var c = oldBox.GetChild(i);
                    if (!c.name.StartsWith("Button")) continue;
                    c.SetParent(col, false);
                    c.SetSiblingIndex(at++);
                    i--;
                }
            }

            // the prefab's column authors five buttons. A sixth of its own, second from the top, is the
            // Extras header; until it has one, the header is a copy of the Map Creator button
            int authored = 0;
            foreach (Transform c in col) if (c.name.StartsWith("Button")) authored++;

            // one door to play: the lobby is the sandbox AND the control room.
            // The creators are their own doors, apart from play, folded under Extras.
            MenuButton(Loc.T("menu.play"), () =>
            {
                LoadEgg.Travel("Lobby");
            });
            Button extras = authored == 0 || authored >= 6
                ? MenuButton(Loc.T("menu.extras"), ToggleExtras, skin != null ? skin.ButtonGrey : null) : null;
            _mapButton = MenuButton(Loc.T("menu.mapcreator"), MapsScreen.Open, skin != null ? skin.ButtonGrey : null);
            _photoButton = MenuButton(Loc.T("menu.photobooth"), PhotosScreen.Open, skin != null ? skin.ButtonGrey : null);
            if (extras == null) extras = CopyAsExtras(col, _mapButton);
            BoxExtras(col, extras);
            ShowExtras();
            MenuButton(Loc.T("menu.options"), GameMenu.OpenOptions, skin != null ? skin.ButtonGrey : null);
            MenuButton(Loc.T("menu.quit"), GameMenu.OpenQuitCheck, skin != null ? skin.ButtonRed : null);

            _statusLabel = UIKit.Row(UIKit.Label(col, _status, 15, UIKit.Parchment, TextAnchor.UpperLeft), 250f, 60f);
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        // ---- Extras: the Map Creator and the Photo Booth fold away under one button ----
        // A box in the column: its header carries an arrow, and opening it shows a frame with the two
        // buttons inside; folded, only the header is there.
        const string BoxName = "ExtrasBox", HeaderName = "ExtrasButton";
        const float FramePad = 12f; // how far the frame reaches past the buttons it holds
        static bool _extrasOpen;    // kept while the game runs, so a language change or a trip back keeps it as it was left
        Button _mapButton, _photoButton;
        Image _frame;
        RectTransform _arrow, _box, _column;

        void ToggleExtras()
        {
            _extrasOpen = !_extrasOpen;
            ShowExtras();
        }

        void ShowExtras()
        {
            if (_mapButton != null) _mapButton.gameObject.SetActive(_extrasOpen);
            if (_photoButton != null) _photoButton.gameObject.SetActive(_extrasOpen);
            if (_frame != null) _frame.enabled = _extrasOpen;
            // the arrow's art points up: up while open (click to fold), down while folded
            if (_arrow != null) _arrow.localRotation = Quaternion.Euler(0f, 0f, _extrasOpen ? 0f : 180f);
            SizeBox();
        }

        /// The box is as tall as the rows it shows. It is sized HERE and the column laid out again at
        /// once: a column that leaves its rows their own size (the prefab's does) reads the box's size
        /// before a size fitter on the box would have set it, so with a fitter the column was always one
        /// step behind: a gap under the folded box, Options and Quit on top of the two buttons when open.
        void SizeBox()
        {
            if (_box == null || _column == null) return;
            var colRows = _column.GetComponent<VerticalLayoutGroup>();
            if (colRows == null || !colRows.childControlHeight)
            {
                var rows = _box.GetComponent<VerticalLayoutGroup>();
                float wide = 0f, tall = 0f;
                int shown = 0;
                foreach (RectTransform row in _box)
                {
                    if (!row.gameObject.activeSelf) continue;
                    var le = row.GetComponent<LayoutElement>();
                    if (le != null && le.ignoreLayout) continue; // the frame
                    wide = Mathf.Max(wide, row.rect.width);
                    tall += row.rect.height;
                    shown++;
                }
                if (shown > 1 && rows != null) tall += rows.spacing * (shown - 1);
                _box.sizeDelta = new Vector2(wide, tall);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_box);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_column);
        }

        /// The header and the two buttons move into a box of their own in the column. The box lays its
        /// rows out the way the column does, so the buttons keep their size and spacing; its frame is
        /// a panel behind them that reaches a little past them and shows only while the box is open.
        void BoxExtras(RectTransform col, Button header)
        {
            if (header == null || _mapButton == null || _photoButton == null) return;
            var had = col.Find(BoxName);
            RectTransform box;
            if (had != null) box = (RectTransform)had;
            else
            {
                box = new GameObject(BoxName, typeof(RectTransform)).GetComponent<RectTransform>();
                box.SetParent(col, false);
                box.pivot = new Vector2(0f, 1f);
                var rows = box.gameObject.AddComponent<VerticalLayoutGroup>();
                var like = col.GetComponent<VerticalLayoutGroup>();
                rows.childAlignment = like != null ? like.childAlignment : TextAnchor.UpperLeft;
                rows.spacing = like != null ? like.spacing : 10f;
                rows.childControlWidth = like != null && like.childControlWidth;
                rows.childControlHeight = like != null && like.childControlHeight;
                rows.childForceExpandWidth = false;
                rows.childForceExpandHeight = false;

                var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                frameGo.transform.SetParent(box, false);
                frameGo.GetComponent<LayoutElement>().ignoreLayout = true;
                var frt = (RectTransform)frameGo.transform;
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = Vector2.one;
                frt.offsetMin = new Vector2(-FramePad, -FramePad);
                frt.offsetMax = new Vector2(FramePad, FramePad);
                var img = frameGo.GetComponent<Image>();
                var skin = UISkin.I;
                img.sprite = skin != null ? skin.PanelBrown : null;
                img.type = img.sprite != null && img.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                img.color = img.sprite != null ? Color.white : new Color(0.25f, 0.18f, 0.12f, 0.85f);
                img.raycastTarget = false;
            }
            if (header.transform.parent != box) box.SetSiblingIndex(header.transform.GetSiblingIndex());
            header.transform.SetParent(box, false);
            _mapButton.transform.SetParent(box, false);
            _photoButton.transform.SetParent(box, false);
            var frame = box.Find("Frame");
            if (frame != null) { frame.SetAsFirstSibling(); _frame = frame.GetComponent<Image>(); }
            _arrow = ArrowOn(header);
            _box = box;
            _column = col;
        }

        /// The arrow on the header, at its left edge, in the ink of the caption.
        RectTransform ArrowOn(Button header)
        {
            var had = header.transform.Find("Arrow");
            if (had != null) return (RectTransform)had;
            var go = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(header.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(26f, 0f);
            rt.sizeDelta = new Vector2(18f, 18f);
            var img = go.GetComponent<Image>();
            var skin = UISkin.I;
            img.sprite = skin != null && skin.Arrow != null ? skin.Arrow : TriangleSprite();
            img.color = UIKit.Ink;
            img.raycastTarget = false;
            return rt;
        }

        static Sprite _triangle;

        /// A plain triangle pointing up, for a skin that has no arrow.
        static Sprite TriangleSprite()
        {
            if (_triangle != null) return _triangle;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // inside while closer to the middle column than half the width of this row, and rows narrow to the top
                    float half = (n - 1 - y) * 0.5f * (n / (float)(n - 1));
                    float edge = half - Mathf.Abs(x + 0.5f - n * 0.5f);
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(edge + 0.5f) * (y >= 3 && y <= n - 4 ? 1f : 0f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return _triangle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        /// The prefab has no button of its own for the header: it gets a copy of the one below it, so it
        /// looks like the column it sits in. Named apart from "Button", or the next build would adopt it as one of the five.
        Button CopyAsExtras(RectTransform col, Button like)
        {
            if (like == null) return null;
            var had = col.Find(HeaderName);
            if (had == null && col.Find(BoxName) != null) had = col.Find(BoxName).Find(HeaderName);
            GameObject go = had != null ? had.gameObject : Instantiate(like.gameObject, col);
            if (had == null)
            {
                go.name = HeaderName;
                go.transform.SetSiblingIndex(like.transform.GetSiblingIndex());
            }
            go.SetActive(true);
            var b = go.GetComponent<Button>();
            if (b == null) return null;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => ButtonJuice.Press(b, ToggleExtras));
            var legacy = go.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (legacy != null) legacy.text = Loc.T("menu.extras");
            else
            {
                var tmp = go.GetComponentInChildren<TMPro.TMP_Text>(true);
                if (tmp != null) tmp.text = Loc.T("menu.extras");
            }
            return b;
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
