using System;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// Maps shown as picture cards, three to a row from the top left and as far
    /// down as they go, beside the picked one's big picture. The Maps screen and
    /// the lobby's map picker both use these.
    public static class MapCards
    {
        public const int Columns = 3;
        const float Gap = 12f, NameH = 44f, Margin = 40f;

        /// The dimmed screen and a panel filling most of it, stacked. `innerW`
        /// is the width its rows get, `innerH` the height they share.
        public static RectTransform Panel(RectTransform screen, out float innerW, out float innerH)
        {
            var skin = UISkin.I;
            var dim = UIKit.Panel(screen, null, new Color(0f, 0f, 0f, 0.62f));
            UIKit.Stretch(dim.rectTransform);
            dim.raycastTarget = true; // what is underneath stays out of reach

            Vector2 size = UIKit.Root.rect.size;
            var panel = UIKit.Panel(screen, skin != null ? skin.PanelBrown : null,
                skin != null ? (Color?)null : new Color(0.93f, 0.88f, 0.76f, 0.98f));
            panel.raycastTarget = true;
            var rt = panel.rectTransform;
            UIKit.Place(rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Mathf.Max(940f, size.x - Margin * 2f), 0f));
            UIKit.Stack(rt, 20, 16, 10f);
            innerW = Mathf.Max(900f, size.x - Margin * 2f - 40f);
            innerH = Mathf.Max(500f, size.y - Margin * 2f - 32f);
            return rt;
        }

        /// Two scrolling columns side by side: the cards, and the picked map. Returns the cards' column.
        public static RectTransform Split(RectTransform panel, float innerW, float height,
            out RectTransform detail, out float cardsW, out float detailW)
        {
            var split = UIKit.Group(panel, "Split");
            UIKit.Row(split, innerW, height);
            var row = split.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 24f;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            float left = Mathf.Round(innerW * 0.62f);
            float right = innerW - left - 24f;
            var cards = UIKit.Scroll(split, "Cards", left, height, 10f);
            detail = UIKit.Scroll(split, "Detail", right, height, 8f);
            cardsW = left - 12f;   // the scrollbar keeps its lane
            detailW = right - 12f;
            return cards;
        }

        /// The cards: a picture over a name, the lit one picked. With `create`,
        /// the first card is a blank one with a plus that makes a new map.
        public static void Grid(RectTransform column, float width, int count, Func<int, string> name,
            Func<int, Texture2D> picture, Func<int, bool> lit, Action<int> picked,
            string createLabel = null, Action create = null)
        {
            var grid = Cards(column, width, count + (create != null ? 1 : 0), out float nameFrac);
            if (create != null) Mark(Card(grid, createLabel, null, nameFrac, create, false), nameFrac, "+");
            for (int i = 0; i < count; i++)
            {
                int at = i;
                Card(grid, name(at), picture(at), nameFrac, () => picked(at), lit(at));
            }
        }

        /// An empty grid with room for this many cards; `nameFrac` is the share of a card its name takes.
        public static RectTransform Cards(RectTransform column, float width, int cards, out float nameFrac)
        {
            float cellW = (width - Gap * (Columns - 1)) / Columns;
            var cell = new Vector2(cellW, cellW * 9f / 16f + NameH);
            int rows = Mathf.Max(1, Mathf.CeilToInt(cards / (float)Columns));
            var grid = UIKit.Row(UIKit.Grid(column, "CardGrid", cell, Gap, Columns), width, rows * (cell.y + Gap));
            var lay = grid.GetComponent<GridLayoutGroup>();
            if (lay != null) lay.childAlignment = TextAnchor.UpperLeft;
            nameFrac = NameH / cell.y;
            return grid;
        }

        /// A big sign over a card's picture (the plus on a card that makes something new).
        public static void Mark(RectTransform card, float nameFrac, string sign)
        {
            var mark = UIKit.Label(card, sign, 72, UIKit.Parchment, TextAnchor.MiddleCenter, true);
            mark.resizeTextForBestFit = false;
            var mrt = mark.rectTransform;
            mrt.anchorMin = new Vector2(0f, nameFrac);
            mrt.anchorMax = Vector2.one;
            mrt.offsetMin = mrt.offsetMax = Vector2.zero;
        }

        /// `tile` false: no picture part, for a card that draws its own look over its name.
        public static RectTransform Card(RectTransform grid, string name, Texture2D picture, float nameFrac, Action click, bool lit,
            bool tile = true)
        {
            var card = UIKit.Button(grid, "", click, CreatorUI.Pick(lit), 13);
            var crt = (RectTransform)card.transform;
            if (tile) Picture(crt, picture, new Vector2(0f, nameFrac), Vector2.one, 8f);
            var label = UIKit.Label(crt, name, 16, UIKit.Ink, TextAnchor.MiddleCenter, true);
            var nrt = label.rectTransform;
            nrt.anchorMin = Vector2.zero;
            nrt.anchorMax = new Vector2(1f, nameFrac);
            nrt.offsetMin = new Vector2(8f, 2f);
            nrt.offsetMax = new Vector2(-8f, -2f);
            return crt;
        }

        /// A title on the left and one button pinned to the right of the same row.
        public static void TitleBar(RectTransform panel, float width, string title, string button, Action act, Sprite sprite)
        {
            var bar = UIKit.Group(panel, "TitleBar");
            UIKit.Row(bar, width, 44f);
            var hl = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            var t = UIKit.Label(bar, title, 30, UIKit.Ink, TextAnchor.MiddleLeft, true);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var b = UIKit.Button(bar, button, act, sprite, 16);
            var le = b.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 240f;
        }

        /// A picture filling part of a card or a pane, or a dark tile when there is none.
        public static void Picture(RectTransform parent, Texture2D tex, Vector2 min, Vector2 max, float inset)
        {
            var go = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var raw = go.GetComponent<RawImage>();
            raw.texture = tex;
            raw.color = tex != null ? Color.white : new Color(0.2f, 0.17f, 0.14f, 0.85f);
            raw.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = new Vector2(inset, inset * 0.5f);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        /// The picked map's picture, as wide as its column.
        public static void Big(RectTransform column, float width, Texture2D tex)
        {
            var frame = UIKit.Group(column, "Big");
            UIKit.Row(frame, width, width * 9f / 16f);
            Picture(frame, tex, Vector2.zero, Vector2.one, 0f);
        }

        public static Text Line(RectTransform column, float width, string text, int size, bool bold = false, float height = 24f) =>
            UIKit.Row(UIKit.Label(column, text, size, UIKit.Ink, TextAnchor.MiddleLeft, bold), width, height);

        /// Empties a column for a rebuild; retired rows are renamed so the same
        /// frame's new rows never take their place.
        public static void Empty(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) UIKit.Retire((RectTransform)rt.GetChild(i));
        }
    }
}
