using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE LOBBY'S MAP PICKER, beside the book stand panel and in its look:
    /// every map the host can play at once as picture cards, three to a row
    /// from the top left. A click picks it and the side shows what the picked
    /// map is. The stand's map row opens and closes it.
    public class MapPicker : MonoBehaviour
    {
        static MapPicker _live;
        public static bool IsOpen => _live != null;

        // the stand panel's own place: centre offset and size (NetGame)
        const float StandX = -215f, StandW = 600f, StandH = 620f, StandY = 10f, Gap = 12f;
        static readonly Color Back = new Color(0.05f, 0.06f, 0.09f, 0.86f);
        static readonly Color Light = new Color(0.85f, 0.85f, 0.88f);

        Action _changed;
        RectTransform _cards, _detail;
        float _cardsW, _detailW;
        readonly Dictionary<string, MapDef> _defs = new Dictionary<string, MapDef>();

        /// Opens beside the stand, or closes when it is open. `changed` hears
        /// each pick, so the stand redraws its map row.
        public static void Open(Action changed)
        {
            if (_live != null) { Close(); return; }
            if (NetGame.Connected && !NetGame.IsHost) return; // host-only pick
            MatchLobby.ForgetMaps(); // maps saved since are in the list
            var ui = UIKit.Group(UIKit.Root, "MapPicker");
            _live = ui.gameObject.AddComponent<MapPicker>();
            _live._changed = changed;
            _live.Build(ui);
        }

        public static void Close()
        {
            if (_live == null) return;
            var rt = (RectTransform)_live.transform;
            _live = null;
            UIKit.Retire(rt);
        }

        void OnDestroy()
        {
            if (_live == this) _live = null;
        }

        void Build(RectTransform ui)
        {
            // to the right of the stand panel, as tall as it
            float left = StandX + StandW * 0.5f + Gap;
            float width = Mathf.Clamp(UIKit.Root.rect.width * 0.5f - left - 16f, 420f, 820f);
            UIKit.Place(ui, new Vector2(0.5f, 0.5f), new Vector2(left + width * 0.5f, StandY), new Vector2(width, StandH));
            var back = UIKit.Panel(ui, null, Back);
            back.raycastTarget = true;
            UIKit.Stretch(back.rectTransform);

            var col = UIKit.Group(ui, "Column");
            UIKit.Stretch(col);
            col.offsetMin = new Vector2(16f, 16f);
            col.offsetMax = new Vector2(-16f, -12f);
            var stack = col.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = 10f;
            stack.childAlignment = TextAnchor.UpperLeft;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = stack.childForceExpandHeight = false;
            float inner = width - 32f;

            var bar = UIKit.Group(col, "TitleBar");
            UIKit.Row(bar, inner, 30f);
            var hl = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8f;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = true;
            var title = UIKit.Label(bar, Loc.T("maps.title"), 19, LobbyBrowserUI.HeadInk, TextAnchor.MiddleLeft, true);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var close = ChipButton(bar, Loc.T("menu.close"), Close, false, 14);
            var le = close.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 120f;

            float splitH = StandH - 28f - 30f - 10f;
            var split = UIKit.Group(col, "Split");
            UIKit.Row(split, inner, splitH);
            var row = split.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            float cardsW = Mathf.Round(inner * 0.6f);
            float detailW = inner - cardsW - 16f;
            _cards = UIKit.Scroll(split, "Cards", cardsW, splitH, 8f);
            _detail = UIKit.Scroll(split, "Detail", detailW, splitH, 6f);
            _cardsW = cardsW - 10f;
            _detailW = detailW - 10f;
            Rebuild();
        }

        /// A button in the stand's chip look.
        static Button ChipButton(RectTransform parent, string label, Action click, bool on, int size)
        {
            var b = UIKit.Button(parent, label, click, null, size, on ? Color.white : Light);
            var img = b.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = null;
                img.color = on ? LobbyBrowserUI.ChipOn : LobbyBrowserUI.ChipOff;
            }
            return b;
        }

        void Rebuild()
        {
            var maps = MatchLobby.Maps;
            MapCards.Empty(_cards);
            const int cols = 3;
            const float gap = 8f, nameH = 28f;
            float cellW = (_cardsW - gap * (cols - 1)) / cols;
            var cell = new Vector2(cellW, cellW * 9f / 16f + nameH);
            int rows = Mathf.Max(1, Mathf.CeilToInt(maps.Count / (float)cols));
            var grid = UIKit.Row(UIKit.Grid(_cards, "CardGrid", cell, gap, cols), _cardsW, rows * (cell.y + gap));
            var lay = grid.GetComponent<GridLayoutGroup>();
            if (lay != null) lay.childAlignment = TextAnchor.UpperLeft;
            float nameFrac = nameH / cell.y;
            for (int i = 0; i < maps.Count; i++)
            {
                string map = maps[i];
                bool picked = map == MatchLobby.SelectedMap;
                var card = ChipButton(grid, "", () =>
                {
                    MatchLobby.PickMap(map);
                    _changed?.Invoke();
                    Rebuild();
                }, picked, 12);
                var crt = (RectTransform)card.transform;
                MapCards.Picture(crt, PictureOf(map), new Vector2(0f, nameFrac), Vector2.one, 4f);
                var name = UIKit.Label(crt, map, 13, picked ? Color.white : Light, TextAnchor.MiddleCenter, true);
                var nrt = name.rectTransform;
                nrt.anchorMin = Vector2.zero;
                nrt.anchorMax = new Vector2(1f, nameFrac);
                nrt.offsetMin = new Vector2(4f, 1f);
                nrt.offsetMax = new Vector2(-4f, -1f);
            }

            MapCards.Empty(_detail);
            string current = MatchLobby.SelectedMap;
            float W = _detailW;
            MapCards.Big(_detail, W, PictureOf(current));
            UIKit.Row(UIKit.Label(_detail, current, 18, Color.white, TextAnchor.MiddleLeft, true), W, 28f);
            var def = DefOf(current);
            if (def == null) return;
            Line(W, !string.IsNullOrEmpty(def.Author) ? Loc.F("maps.by", def.Author) : null);
            Line(W, Loc.F("maps.info", def.Biomes.Count, def.Spawns.Count, def.Props.Count));
            Line(W, def.TimerMinutes <= 0 ? Loc.T("creator.noclock") : Loc.F("creator.timer", def.TimerMinutes));
        }

        void Line(float W, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            UIKit.Row(UIKit.Label(_detail, text, 13, Light, TextAnchor.MiddleLeft), W, 20f);
        }

        /// A saved map, read once; null for a scene.
        MapDef DefOf(string name)
        {
            if (!_defs.TryGetValue(name, out var def))
                _defs[name] = def = MapLibrary.Exists(name) ? MapLibrary.Load(name) : null;
            return def;
        }

        Texture2D PictureOf(string name) => MapLibrary.PictureOf(name, DefOf(name));
    }
}
