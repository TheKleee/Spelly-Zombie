using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE MAPS SCREEN, from the main menu's Map Creator button. Shared maps
    /// from the Workshop and your own, each with its picture: preview any of
    /// them, save a shared one (it joins your maps and the lobby list), edit or
    /// share your own, or start a blank map with only the sky.
    public class MapsScreen : MonoBehaviour
    {
        /// The creator came back to the menu: the maps show again.
        public static bool OpenOnMenu;
        static MapsScreen _live;
        public static bool IsOpen => _live != null;
        static bool _sharedTab; // the tab open last this session; your own maps first

        bool _shared = _sharedTab;
        int _pick = -1;
        bool _confirmDelete;
        string _note = "";
        int _stamp = -1;
        float _cardsW, _detailW;
        readonly List<MapDef> _mine = new List<MapDef>();
        readonly Dictionary<string, Texture2D> _pictures = new Dictionary<string, Texture2D>();
        RectTransform _tabs, _cards, _detail;
        Text _status;

        public static void Open()
        {
            if (_live != null) return;
            var ui = UIKit.Group(UIKit.Root, "MapsScreen");
            UIKit.Stretch(ui);
            ui.SetAsLastSibling();
            _live = ui.gameObject.AddComponent<MapsScreen>();
            _live.Build();
        }

        public static void Close()
        {
            if (_live == null) return;
            var rt = (RectTransform)_live.transform;
            _live.FreePictures();
            _live = null;
            UIKit.Retire(rt);
        }

        void OnDestroy()
        {
            FreePictures();
            if (_live == this) _live = null;
        }

        void FreePictures()
        {
            foreach (var t in _pictures.Values) if (t != null) Destroy(t);
            _pictures.Clear();
        }

        void Update()
        {
            if (_shared && MapWorkshop.Stamp != _stamp) { _stamp = MapWorkshop.Stamp; Rebuild(); }
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !UIKit.Typing) Close();
        }

        void Build()
        {
            var rt = MapCards.Panel((RectTransform)transform, out float innerW, out float innerH);
            MapCards.TitleBar(rt, innerW, Loc.T("maps.title"), Loc.T("maps.back"), Close, CreatorUI.Red);
            _tabs = UIKit.Segments(rt, innerW, 44f, 8f);
            float splitH = innerH - 44f - 44f - 22f - 30f;
            _cards = MapCards.Split(rt, innerW, splitH, out _detail, out _cardsW, out _detailW);
            _status = UIKit.Row(UIKit.Label(rt, "", 14, UIKit.Ink, TextAnchor.MiddleLeft), innerW, 22f);
            if (_shared) MapWorkshop.Refresh();
            Rebuild();
        }

        // -------------------------------------------------------------- build --
        void Rebuild()
        {
            if (_tabs == null) return;
            MapCards.Empty(_tabs);
            UIKit.Button(_tabs, Loc.T("maps.tab.mine"), () => Tab(false), CreatorUI.Pick(!_shared), 16);
            UIKit.Button(_tabs, Loc.T("maps.tab.shared"), () => Tab(true), CreatorUI.Pick(_shared), 16);

            if (!_shared)
            {
                _mine.Clear();
                _mine.AddRange(MapLibrary.All());
            }
            BuildCards();
            BuildDetail();
            string status = _shared ? MapWorkshop.Status : "";
            if (!string.IsNullOrEmpty(_note)) status = _note;
            _status.text = status;
        }

        void Tab(bool shared)
        {
            if (_shared == shared) return;
            _shared = _sharedTab = shared;
            _pick = -1;
            _confirmDelete = false;
            _note = "";
            if (shared) MapWorkshop.Refresh();
            Rebuild();
        }

        int Count => _shared ? MapWorkshop.Items.Count : _mine.Count;
        string NameAt(int i) => _shared ? MapWorkshop.Items[i].Title : _mine[i].Name;

        Texture2D PictureAt(int i)
        {
            if (_shared) return MapWorkshop.Items[i].Picture;
            string name = _mine[i].Name;
            if (!_pictures.TryGetValue(name, out var tex))
                _pictures[name] = tex = MapLibrary.LoadPicture(name);
            return tex;
        }

        void BuildCards()
        {
            MapCards.Empty(_cards);
            if (_shared && Count == 0)
            {
                // where the maps would be, the reason there are none
                MapCards.Line(_cards, _cardsW, MapWorkshop.Status, 17, false, 60f);
                return;
            }
            // your own maps start with a blank card: a new map
            MapCards.Grid(_cards, _cardsW, Count, NameAt, PictureAt, i => i == _pick,
                i => { _pick = i; _confirmDelete = false; _note = ""; Rebuild(); },
                _shared ? null : Loc.T("maps.create"),
                _shared ? (System.Action)null : () => { Close(); MapCreator.Open(null); });
        }

        void BuildDetail()
        {
            MapCards.Empty(_detail);
            float W = _detailW;
            if (_pick < 0 || _pick >= Count)
            {
                UIKit.Row(UIKit.Label(_detail, Loc.T("maps.pick"), 17, UIKit.Ink, TextAnchor.MiddleCenter), W, 60f);
                return;
            }
            MapCards.Big(_detail, W, PictureAt(_pick));
            MapCards.Line(_detail, W, NameAt(_pick), 26, true, 38f);

            if (_shared)
            {
                var it = MapWorkshop.Items[_pick];
                string author = MapWorkshop.AuthorOf(it);
                if (!string.IsNullOrEmpty(author)) MapCards.Line(_detail, W, Loc.F("maps.by", author), 15);
                if (!string.IsNullOrEmpty(it.Description))
                    UIKit.Row(UIKit.Label(_detail, it.Description, 14, UIKit.Ink, TextAnchor.UpperLeft), W, 80f);
                bool kept = MapLibrary.Exists(it.Title) || (it.Map != null && MapLibrary.Exists(it.Map.Name));
                var row = UIKit.Segments(_detail, W, 44f, 8f);
                UIKit.Button(row, Loc.T("maps.preview"), () => Preview(it), CreatorUI.Pick(false), 16);
                UIKit.Button(row, kept ? Loc.T("maps.kept") : Loc.T("maps.keep"), () => { if (!kept) Keep(it); },
                    CreatorUI.Pick(!kept), 16);
                UIKit.Row(UIKit.Button(_detail, Loc.T("maps.page"), () => MapWorkshop.ShowItem(it.Id), CreatorUI.Pick(false), 15), W, 40f);
            }
            else
            {
                var map = _mine[_pick];
                if (!string.IsNullOrEmpty(map.Author)) MapCards.Line(_detail, W, Loc.F("maps.by", map.Author), 15);
                MapCards.Line(_detail, W, Loc.F("maps.info", map.Biomes.Count, map.Spawns.Count, map.Props.Count), 15);
                MapCards.Line(_detail, W, map.TimerMinutes <= 0 ? Loc.T("creator.noclock") : Loc.F("creator.timer", map.TimerMinutes), 15);
                var row = UIKit.Segments(_detail, W, 44f, 8f);
                UIKit.Button(row, Loc.T("maps.edit"), () => { Close(); MapCreator.Open(map); }, CreatorUI.Pick(true), 16);
                UIKit.Button(row, Loc.T("maps.preview"), () => { Close(); MapCreator.Open(map, true, null); }, CreatorUI.Pick(false), 16);
                // a map that comes with the game is shared already and never deleted
                if (MapLibrary.Locked(map.Name)) { MapCards.Line(_detail, W, Loc.T("maps.official"), 15); return; }
                var row2 = UIKit.Segments(_detail, W, 44f, 8f);
                UIKit.Button(row2, Loc.T("maps.share"), () =>
                {
                    _note = Loc.T("maps.sharing");
                    MapWorkshop.Share(map, (ok, msg) => { _note = msg; if (_live == this) Rebuild(); });
                    Rebuild();
                }, CreatorUI.Pick(false), 16);
                UIKit.Button(row2, _confirmDelete ? Loc.T("maps.delete.sure") : Loc.T("creator.delete"), () =>
                {
                    if (!_confirmDelete) { _confirmDelete = true; Rebuild(); return; }
                    MapLibrary.Delete(map.Name);
                    if (_pictures.TryGetValue(map.Name, out var tex) && tex != null) Destroy(tex);
                    _pictures.Remove(map.Name);
                    _pick = -1;
                    _confirmDelete = false;
                    Rebuild();
                }, CreatorUI.Red, 16);
            }
        }

        void Preview(MapWorkshop.Item it)
        {
            _note = Loc.T("maps.downloading");
            Rebuild();
            MapWorkshop.Fetch(it, map =>
            {
                if (map == null) { _note = Loc.F("maps.failed", "download"); if (_live == this) Rebuild(); return; }
                Close();
                MapCreator.Open(map, true, it);
            });
        }

        void Keep(MapWorkshop.Item it)
        {
            _note = Loc.T("maps.downloading");
            Rebuild();
            MapWorkshop.Fetch(it, map =>
            {
                _note = map == null ? Loc.F("maps.failed", "download")
                    : MapWorkshop.Keep(it) ? Loc.F("creator.saved", map.Name) : Loc.T("mc.needbiome");
                if (_live == this) Rebuild();
            });
        }
    }
}
