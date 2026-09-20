using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ A FLOATING WINDOW for the creators: drag it by its title bar, hide it
    /// with the cross, click it to bring it forward. The body scrolls, and a
    /// window rebuilds its body whenever what it shows changes.
    public class CreatorWindow : MonoBehaviour, IPointerDownHandler
    {
        public RectTransform Body { get; private set; }
        public Text Title { get; private set; }
        /// The width rows in the body are laid out at.
        public float Width { get; private set; }
        /// The window hid itself (its cross).
        public Action Closed;

        public static CreatorWindow Create(RectTransform parent, string name, string title, Vector2 at,
            float width, float bodyHeight)
        {
            var skin = UISkin.I;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = skin != null ? skin.PanelBrown : null;
            img.type = img.sprite != null && img.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = skin != null ? Color.white : new Color(0.93f, 0.88f, 0.76f, 0.97f);
            img.raycastTarget = true; // a click on a window never reaches the map
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = at;
            UIKit.Stack(rt, 14, 12, 6);

            var w = go.AddComponent<CreatorWindow>();
            w.Width = width - 28f;

            var bar = new GameObject("TitleBar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(rt, false);
            bar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // catches the drag, shows nothing
            var row = bar.GetComponent<HorizontalLayoutGroup>();
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            row.spacing = 6f;
            var barRt = (RectTransform)bar.transform;
            UIKit.Row(barRt, w.Width, 30f);
            bar.AddComponent<WindowDrag>().Target = rt;

            w.Title = UIKit.Label(barRt, title, 19, UIKit.Ink, TextAnchor.MiddleLeft, true);
            w.Title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var close = UIKit.Button(barRt, "X", w.Hide, skin != null ? skin.ButtonRed : null, 15);
            var le = close.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 30f;

            w.Body = UIKit.Scroll(rt, "Body", w.Width, bodyHeight, 5f);
            return w;
        }

        public bool Visible => gameObject.activeSelf;

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        public void Toggle() { if (Visible) Hide(); else Show(); }

        /// Empties the body for a rebuild; retired rows are renamed so the same
        /// frame's new rows never take their place.
        public void Clear()
        {
            for (int i = Body.childCount - 1; i >= 0; i--)
                UIKit.Retire((RectTransform)Body.GetChild(i));
        }

        public void OnPointerDown(PointerEventData e) => transform.SetAsLastSibling();
    }

    /// A title bar's drag: the window follows the pointer and its bar stays on screen.
    public class WindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public RectTransform Target;

        public void OnBeginDrag(PointerEventData e)
        {
            if (Target != null) Target.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData e)
        {
            if (Target == null) return;
            var canvas = Target.GetComponentInParent<Canvas>();
            float k = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            var p = Target.anchoredPosition + e.delta / k;
            if (Target.parent is RectTransform parent)
            {
                Vector2 size = parent.rect.size;
                p.x = Mathf.Clamp(p.x, 80f - Target.rect.width, size.x - 80f);
                p.y = Mathf.Clamp(p.y, 40f - size.y, 0f);
            }
            Target.anchoredPosition = p;
        }
    }

    /// The creator windows' rows: headings, notes, labelled sliders, switches,
    /// choice grids, fields. Every choice shows at once; the lit one is chosen.
    public static class CreatorUI
    {
        public static readonly Color HeadInk = new Color(0.35f, 0.28f, 0.2f);
        static Sprite Lit => UISkin.I != null ? UISkin.I.ButtonBrown : null;
        static Sprite Dim => UISkin.I != null ? UISkin.I.ButtonGrey : null;
        public static Sprite Red => UISkin.I != null ? UISkin.I.ButtonRed : null;
        public static Sprite Pick(bool lit) => lit ? Lit : Dim;

        public static Text Head(RectTransform body, float width, string text) =>
            UIKit.Row(UIKit.Label(body, text, 15, HeadInk, TextAnchor.LowerLeft, true), width, 26f);

        public static Text Note(RectTransform body, float width, string text, float height = 34f)
        {
            var t = UIKit.Row(UIKit.Label(body, text, 12, UIKit.Ink, TextAnchor.UpperLeft), width, height);
            t.resizeTextMinSize = 9;
            return t;
        }

        /// "Name: value" over a slider. `say` writes the value (null = the number).
        public static Slider Number(RectTransform body, float width, string name, float min, float max, float value,
            bool whole, Action<float> changed, Func<float, string> say = null) =>
            Number(body, width, name, min, max, value, whole, changed, say, out _);

        /// The same, handing back the line over the slider (to move both from code).
        public static Slider Number(RectTransform body, float width, string name, float min, float max, float value,
            bool whole, Action<float> changed, Func<float, string> say, out Text line)
        {
            string Say(float v) => say != null ? say(v)
                : name + ": " + (whole ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##"));
            var label = UIKit.Row(UIKit.Label(body, Say(value), 13, UIKit.Ink, TextAnchor.MiddleLeft), width, 18f);
            var s = UIKit.Row(UIKit.Slider(body, min, max, value, v =>
            {
                if (whole) v = Mathf.Round(v);
                label.text = Say(v);
                changed?.Invoke(v);
            }), width, 22f);
            s.wholeNumbers = whole;
            line = label;
            return s;
        }

        /// A row of choices side by side, the current one lit.
        public static void Switch(RectTransform body, float width, string name, IReadOnlyList<string> options, int current,
            Action<int> picked)
        {
            if (!string.IsNullOrEmpty(name))
                UIKit.Row(UIKit.Label(body, name, 13, UIKit.Ink, TextAnchor.MiddleLeft), width, 18f);
            var seg = UIKit.Segments(body, width, 30f, 4f);
            for (int i = 0; i < options.Count; i++)
            {
                int at = i;
                UIKit.Button(seg, options[i], () => picked?.Invoke(at), Pick(i == current), 13);
            }
        }

        /// A grid of named buttons; `lit` says which show as chosen.
        public static RectTransform Grid(RectTransform body, float width, int columns, IReadOnlyList<string> labels,
            Func<int, bool> lit, Action<int> picked, float cellHeight = 30f, int fontSize = 13)
        {
            columns = Mathf.Max(1, columns);
            float cellW = (width - (columns - 1) * 4f) / columns;
            int rows = Mathf.Max(1, Mathf.CeilToInt(labels.Count / (float)columns));
            var g = UIKit.Row(UIKit.Grid(body, "Grid", new Vector2(cellW, cellHeight), 4f, columns),
                width, rows * (cellHeight + 4f));
            for (int i = 0; i < labels.Count; i++)
            {
                int at = i;
                UIKit.Button(g, labels[i], () => picked?.Invoke(at), Pick(lit != null && lit(at)), fontSize);
            }
            return g;
        }

        public static InputField Field(RectTransform body, float width, string name, string value, Action<string> changed)
        {
            if (!string.IsNullOrEmpty(name))
                UIKit.Row(UIKit.Label(body, name, 13, UIKit.Ink, TextAnchor.MiddleLeft), width, 18f);
            return UIKit.Row(UIKit.Input(body, value, changed), width, 32f);
        }

        /// One wide button.
        public static Button Do(RectTransform body, float width, string label, Action act, Sprite sprite = null,
            float height = 34f) =>
            UIKit.Row(UIKit.Button(body, label, act, sprite ?? Dim, 15), width, height);

        /// A heading that opens and closes what is under it. `start` is how it first
        /// shows; `open` remembers a click away from that, by key.
        public static bool Section(RectTransform b, float W, HashSet<string> open, string key, string title,
            bool start, Action rebuild)
        {
            bool shown = start != open.Contains(key);
            Do(b, W, title + (shown ? "  -" : "  +"), () =>
            {
                if (!open.Remove(key)) open.Add(key);
                rebuild();
            }, Pick(shown), 30f);
            return shown;
        }

        /// A DROP DOWN: its name and current pick on one button, every choice
        /// under it only while it is open. A pick closes it, unless it stays open.
        public static void Dropdown(RectTransform b, float W, HashSet<string> open, string key, string name,
            string current, IReadOnlyList<string> options, Func<int, bool> lit, Action<int> picked, Action rebuild,
            bool stayOpen = false)
        {
            bool shown = open.Contains(key);
            string label = string.IsNullOrEmpty(current) ? name : name + ": " + current;
            Do(b, W, label + (shown ? "  -" : "  +"), () =>
            {
                if (!open.Remove(key)) open.Add(key);
                rebuild();
            }, Pick(shown), 30f);
            if (!shown) return;
            Grid(b, W, 2, options, lit, i =>
            {
                if (!stayOpen) open.Remove(key);
                picked(i);
                rebuild();
            }, 26f, 12);
        }

        /// Buttons side by side: add them to the returned row.
        public static RectTransform Row(RectTransform body, float width, float height = 32f) =>
            UIKit.Segments(body, width, height, 6f);

        /// Emoji and rune icons need TextMeshPro: its sprite asset draws them.
        public static TMPro.TextMeshProUGUI Emoji(RectTransform parent, string text, float size)
        {
            var go = new GameObject("Emoji", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.raycastTarget = false;
            UIKit.Stretch((RectTransform)go.transform);
            return t;
        }
    }
}
