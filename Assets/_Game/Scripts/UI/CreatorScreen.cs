using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ A CREATOR OVER THE WHOLE SCREEN: its own thing while it is open, the
    /// map dimmed behind it. Columns side by side, each a scrolling stack, so
    /// everything it edits is in view at once. One button takes you back.
    public class CreatorScreen : MonoBehaviour
    {
        public RectTransform[] Columns { get; private set; }
        /// The width rows in each column are laid out at.
        public float[] Widths { get; private set; }
        public Text Title { get; private set; }
        /// The screen is going away.
        public Action Closed;

        const float Margin = 20f, Pad = 16f, Gap = 16f;

        /// `widths`: one per column; a zero takes whatever the others leave.
        public static CreatorScreen Create(RectTransform parent, string name, string title, string closeLabel,
            params float[] widths)
        {
            var skin = UISkin.I;
            var root = UIKit.Group(parent, name);
            UIKit.Stretch(root);
            var s = root.gameObject.AddComponent<CreatorScreen>();

            var dim = UIKit.Panel(root, null, new Color(0f, 0f, 0f, 0.62f));
            dim.raycastTarget = true; // the map and its windows stay out of reach
            UIKit.Stretch(dim.rectTransform);

            var panel = UIKit.Panel(root, skin != null ? skin.PanelBrown : null,
                skin != null ? Color.white : new Color(0.93f, 0.88f, 0.76f, 0.98f));
            panel.raycastTarget = true;
            var rt = panel.rectTransform;
            UIKit.Stretch(rt);
            rt.offsetMin = new Vector2(Margin, Margin);
            rt.offsetMax = new Vector2(-Margin, -Margin);
            UIKit.Stack(rt, (int)Pad, 12, 8f);

            Vector2 size = parent.rect.size;
            float innerW = Mathf.Max(900f, size.x - Margin * 2f - Pad * 2f);
            float bodyH = Mathf.Max(400f, size.y - Margin * 2f - 24f - 44f);

            var bar = UIKit.Group(rt, "TitleBar");
            UIKit.Row(bar, innerW, 36f);
            var barRow = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barRow.spacing = 8f;
            barRow.childControlWidth = barRow.childControlHeight = true;
            barRow.childForceExpandWidth = false;
            barRow.childForceExpandHeight = true;
            s.Title = UIKit.Label(bar, title, 22, UIKit.Ink, TextAnchor.MiddleLeft, true);
            s.Title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var close = UIKit.Button(bar, closeLabel, s.Close, CreatorUI.Red, 15);
            var le = close.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 240f;

            var cols = UIKit.Group(rt, "Columns");
            UIKit.Row(cols, innerW, bodyH);
            var row = cols.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = Gap;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            float fixedW = 0f;
            int flexible = 0;
            foreach (var w in widths) { if (w > 0f) fixedW += w; else flexible++; }
            float rest = Mathf.Max(240f, (innerW - fixedW - Gap * (widths.Length - 1)) / Mathf.Max(1, flexible));
            s.Widths = new float[widths.Length];
            s.Columns = new RectTransform[widths.Length];
            for (int i = 0; i < widths.Length; i++)
            {
                s.Widths[i] = widths[i] > 0f ? widths[i] : rest;
                s.Columns[i] = UIKit.Scroll(cols, "Column" + i, s.Widths[i], bodyH, 6f);
            }
            return s;
        }

        public void Close()
        {
            var closed = Closed;
            Closed = null;
            UIKit.Retire((RectTransform)transform);
            closed?.Invoke();
        }

        /// A stack inside a column that can be emptied and filled again while
        /// its neighbours (a live preview, a drawing pad) stay as they are.
        public static RectTransform Section(RectTransform parent, string name, float spacing = 6f)
        {
            var g = UIKit.Group(parent, name);
            var lay = g.gameObject.AddComponent<VerticalLayoutGroup>();
            lay.spacing = spacing;
            lay.childAlignment = TextAnchor.UpperCenter;
            lay.childControlWidth = lay.childControlHeight = true;
            lay.childForceExpandWidth = lay.childForceExpandHeight = false;
            return g;
        }

        /// Empties a column or a section for a rebuild; retired rows are renamed
        /// so the same frame's new rows never take their place.
        public static void Clear(RectTransform stack)
        {
            if (stack == null) return;
            for (int i = stack.childCount - 1; i >= 0; i--)
                UIKit.Retire((RectTransform)stack.GetChild(i));
        }
    }
}
