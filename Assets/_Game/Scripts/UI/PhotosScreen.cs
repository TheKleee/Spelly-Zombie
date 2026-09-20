using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE PHOTOS SCREEN, from the main menu's Photo Booth button: every saved
    /// photo setup as a picture card, grouped in folders so many thumbnails stay
    /// easy to find. Open a folder, start a new photo or folder, and edit, copy,
    /// move or delete the picked one.
    public class PhotosScreen : MonoBehaviour
    {
        /// The booth came back to the menu: the photos show again.
        public static bool OpenOnMenu;
        static PhotosScreen _live;
        public static bool IsOpen => _live != null;
        static string _shownFolder = ""; // the folder open last this session

        string _folder = _shownFolder;
        int _pick = -1;                  // folders first (only outside any folder), then photos
        bool _confirmDelete;
        string _note = "";
        string _rename = "";
        float _cardsW, _detailW;
        readonly List<string> _folders = new List<string>();
        readonly List<PhotoDef> _photos = new List<PhotoDef>();
        readonly Dictionary<string, Texture2D> _pictures = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _open = new HashSet<string>();
        RectTransform _panel, _cards, _detail;
        Text _status;

        public static void Open()
        {
            if (_live != null) return;
            var ui = UIKit.Group(UIKit.Root, "PhotosScreen");
            UIKit.Stretch(ui);
            ui.SetAsLastSibling();
            _live = ui.gameObject.AddComponent<PhotosScreen>();
            if (!string.IsNullOrEmpty(_live._folder) && !PhotoLibrary.FolderExists(_live._folder)) _live._folder = "";
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
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame || UIKit.Typing) return;
            Back();
        }

        /// Out of a folder, else off the screen.
        void Back()
        {
            if (!string.IsNullOrEmpty(_folder)) { Enter(""); return; }
            Close();
        }

        void Enter(string folder)
        {
            _folder = _shownFolder = folder ?? "";
            _pick = -1;
            _confirmDelete = false;
            _note = "";
            FreePictures();
            Build();
        }

        void Build()
        {
            if (_panel != null) UIKit.Retire(_panel);
            var screen = (RectTransform)transform;
            MapCards.Empty(screen);
            _panel = MapCards.Panel(screen, out float innerW, out float innerH);
            string title = string.IsNullOrEmpty(_folder) ? Loc.T("photos.title") : Loc.T("photos.title") + ": " + _folder;
            MapCards.TitleBar(_panel, innerW, title, Loc.T("maps.back"), Back, CreatorUI.Red);
            float splitH = innerH - 44f - 22f - 30f;
            _cards = MapCards.Split(_panel, innerW, splitH, out _detail, out _cardsW, out _detailW);
            _status = UIKit.Row(UIKit.Label(_panel, "", 14, UIKit.Ink, TextAnchor.MiddleLeft), innerW, 22f);
            Rebuild();
        }

        void Rebuild()
        {
            if (_cards == null) return;
            _folders.Clear();
            if (string.IsNullOrEmpty(_folder)) _folders.AddRange(PhotoLibrary.Folders());
            _photos.Clear();
            _photos.AddRange(PhotoLibrary.In(_folder));
            if (_pick >= _folders.Count + _photos.Count) _pick = -1;
            BuildCards();
            BuildDetail();
            _status.text = _note;
        }

        bool PickedFolder => _pick >= 0 && _pick < _folders.Count;
        PhotoDef PickedPhoto => _pick >= _folders.Count && _pick < _folders.Count + _photos.Count ? _photos[_pick - _folders.Count] : null;

        Texture2D PictureOf(string key, System.Func<Texture2D> load)
        {
            if (!_pictures.TryGetValue(key, out var tex)) _pictures[key] = tex = load();
            return tex;
        }

        Texture2D FolderPicture(string folder) => PictureOf("folder:" + folder, () => PhotoLibrary.Cover(folder));
        Texture2D PhotoPicture(PhotoDef p) => PictureOf("photo:" + p.Folder + "/" + p.Name, () => PhotoLibrary.Picture(p.Folder, p.Name));

        void BuildCards()
        {
            MapCards.Empty(_cards);
            bool top = string.IsNullOrEmpty(_folder);
            int count = 1 + (top ? 1 : 0) + _folders.Count + _photos.Count;
            var grid = MapCards.Cards(_cards, _cardsW, count, out float nameFrac);
            MapCards.Mark(MapCards.Card(grid, Loc.T("photos.newphoto"), null, nameFrac, NewPhoto, false), nameFrac, "+");
            if (top)
                MapCards.Mark(MapCards.Card(grid, Loc.T("photos.newfolder"), null, nameFrac, NewFolder, false), nameFrac, "+");
            for (int i = 0; i < _folders.Count; i++)
            {
                int at = i;
                string f = _folders[i];
                // a folder reads as a group: its name and how many photos it holds, over its newest picture
                MapCards.Card(grid, f + "  (" + PhotoLibrary.CountIn(f) + ")", FolderPicture(f), nameFrac, () => Pick(at), _pick == at);
            }
            for (int i = 0; i < _photos.Count; i++)
            {
                int at = _folders.Count + i;
                MapCards.Card(grid, _photos[i].Name, PhotoPicture(_photos[i]), nameFrac, () => Pick(at), _pick == at);
            }
        }

        void Pick(int at)
        {
            // a second click on a folder opens it
            if (at == _pick && at < _folders.Count) { Enter(_folders[at]); return; }
            _pick = at;
            _confirmDelete = false;
            _note = "";
            _rename = PickedFolder ? _folders[at] : "";
            Rebuild();
        }

        void BuildDetail()
        {
            MapCards.Empty(_detail);
            float W = _detailW;
            if (PickedFolder) FolderDetail(W);
            else if (PickedPhoto != null) PhotoDetail(PickedPhoto, W);
            else
            {
                UIKit.Row(UIKit.Label(_detail, Loc.T("photos.pick"), 17, UIKit.Ink, TextAnchor.MiddleCenter), W, 60f);
                if (!string.IsNullOrEmpty(_folder)) FolderTools(_folder, W);
            }
            CreatorUI.Do(_detail, W, Loc.T("photos.files"),
                () => PhotoLibrary.Reveal(PhotoLibrary.PhotosFolder(_folder)), null, 34f);
        }

        void FolderDetail(float W)
        {
            string f = _folders[_pick];
            MapCards.Big(_detail, W, FolderPicture(f));
            MapCards.Line(_detail, W, f, 26, true, 38f);
            MapCards.Line(_detail, W, Loc.F("photos.count", PhotoLibrary.CountIn(f)), 15);
            CreatorUI.Do(_detail, W, Loc.T("photos.open"), () => Enter(f), CreatorUI.Pick(true), 40f);
            FolderTools(f, W);
        }

        /// Rename a folder, or delete it once it is empty.
        void FolderTools(string f, float W)
        {
            if (string.IsNullOrEmpty(_rename)) _rename = f;
            CreatorUI.Field(_detail, W, Loc.T("photos.folder.name"), _rename, v => _rename = v);
            var row = CreatorUI.Row(_detail, W, 40f);
            UIKit.Button(row, Loc.T("photos.rename"), () =>
            {
                string to = _rename.Trim();
                if (to.Length == 0 || to == f) return;
                if (!PhotoLibrary.RenameFolder(f, to))
                {
                    _note = Loc.T("photos.taken");
                    Juice.Sound2D(Sfx.UiError);
                    Rebuild();
                    return;
                }
                FreePictures();
                if (_folder == f) { Enter(to); return; }
                _note = "";
                Rebuild();
            }, CreatorUI.Pick(false), 15);
            bool empty = PhotoLibrary.CountIn(f) == 0;
            UIKit.Button(row, Loc.T("photos.folder.delete"), () =>
            {
                if (!PhotoLibrary.DeleteFolder(f))
                {
                    _note = Loc.T("photos.folder.full");
                    Juice.Sound2D(Sfx.UiError);
                    Rebuild();
                    return;
                }
                _pick = -1;
                _note = "";
                if (_folder == f) { Enter(""); return; }
                Rebuild();
            }, CreatorUI.Red, 15).interactable = empty;
            if (!empty) CreatorUI.Note(_detail, W, Loc.T("photos.folder.full"), 24f);
        }

        void PhotoDetail(PhotoDef p, float W)
        {
            MapCards.Big(_detail, W, PhotoPicture(p));
            MapCards.Line(_detail, W, p.Name, 26, true, 38f);
            MapCards.Line(_detail, W, p.Width + "x" + p.Height, 15);
            var row = CreatorUI.Row(_detail, W, 44f);
            UIKit.Button(row, Loc.T("maps.edit"), () => { Close(); PhotoBooth.Open(p); }, CreatorUI.Pick(true), 16);
            UIKit.Button(row, Loc.T("mc.duplicate"), () =>
            {
                var copy = PhotoLibrary.Copy(p);
                _pick = -1;
                Rebuild();
                if (copy != null)
                {
                    int at = _photos.FindIndex(x => x.Name == copy.Name);
                    if (at >= 0) Pick(_folders.Count + at);
                }
            }, CreatorUI.Pick(false), 16);

            var folders = PhotoLibrary.Folders();
            var names = new List<string> { Loc.T("photos.nofolder") };
            names.AddRange(folders);
            CreatorUI.Dropdown(_detail, W, _open, "move", Loc.T("photos.folder"),
                string.IsNullOrEmpty(p.Folder) ? Loc.T("photos.nofolder") : p.Folder, names,
                i => i == 0 ? string.IsNullOrEmpty(p.Folder) : string.Equals(folders[i - 1], p.Folder, System.StringComparison.OrdinalIgnoreCase),
                i =>
                {
                    PhotoLibrary.Move(p, i == 0 ? "" : folders[i - 1]);
                    FreePictures();
                    _pick = -1;
                }, Rebuild);

            CreatorUI.Do(_detail, W, _confirmDelete ? Loc.T("maps.delete.sure") : Loc.T("creator.delete"), () =>
            {
                if (!_confirmDelete) { _confirmDelete = true; Rebuild(); return; }
                PhotoLibrary.Delete(p.Folder, p.Name);
                FreePictures();
                _pick = -1;
                _confirmDelete = false;
                Rebuild();
            }, CreatorUI.Red, 36f);
        }

        void NewPhoto()
        {
            string folder = _folder;
            Close();
            PhotoBooth.Open(new PhotoDef { Folder = folder, Name = PhotoLibrary.FreeName(folder, Loc.T("photos.newphoto")) });
        }

        void NewFolder()
        {
            string made = PhotoLibrary.MakeFolder(Loc.T("photos.newfolder"));
            _note = "";
            Rebuild();
            int at = _folders.IndexOf(made);
            if (at >= 0) Pick(at);
        }
    }
}
