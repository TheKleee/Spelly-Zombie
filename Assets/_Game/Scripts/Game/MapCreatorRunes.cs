using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE RUNES CREATOR, over the whole screen: the Rune Crafter in the
    /// game, on the map's own book. The list; the rune's drawings or its
    /// grimoire page; the name, the side and its emoji; what the rune pushes.
    /// Drawings kept here teach this map to read the rune; pages painted here
    /// are the map's own and are saved with it.
    public partial class MapCreator
    {
        int _rune = -1;
        bool _runeAcolyte;
        int _runeTab;                 // 0 the drawings, 1 the page
        int _keptPick = -1;           // the kept drawing on the pad, -1 = a new one
        RuneType _padRune = RuneType.None;
        bool _sureRune;
        SketchPad _pad;
        PagePainter _page;
        string _pageName;
        float _pageBrush = 6f, _stampSize = 0.45f, _stampTurn;
        bool _stamping;
        Text _padReads, _padNote;
        RectTransform _runeTabs, _drawView, _padTools, _keptView, _pageView, _pageTools;
        readonly List<Texture2D> _thumbs = new List<Texture2D>();

        static readonly Color32 ThumbInk = new Color32(250, 244, 230, 255), ThumbGround = new Color32(40, 33, 27, 255);
        static readonly Rect PadBox = new Rect(SketchPad.Pixels * 0.15f, SketchPad.Pixels * 0.15f,
            SketchPad.Pixels * 0.7f, SketchPad.Pixels * 0.7f);

        RuneDef CurrentRune
        {
            get
            {
                var book = SpellBook.Live;
                return _rune >= 0 && _rune < book.runes.Count ? book.runes[_rune] : null;
            }
        }

        /// Columns: the list | the drawings or the page | name, side, emoji | pushes.
        void BuildRunesScreen()
        {
            var s = _screen;
            var mid = s.Columns[1];
            float W = s.Widths[1] - 12f;
            _runeTabs = CreatorScreen.Section(mid, "Tabs");

            _drawView = CreatorScreen.Section(mid, "Drawings");
            _padNote = CreatorUI.Note(_drawView, W, Loc.T("mc.pad.note"), 18f);
            _pad = SketchPad.Create(_drawView, Mathf.Min(W, 460f));
            _pad.Changed = () => { ReadPad(); CanvasStep(_pad); };
            _padReads = UIKit.Row(UIKit.Label(_drawView, "", 15, UIKit.Ink, TextAnchor.MiddleLeft, true), W, 24f);
            _padTools = CreatorScreen.Section(_drawView, "PadTools");
            _keptView = CreatorScreen.Section(_drawView, "Kept");

            _pageView = CreatorScreen.Section(mid, "Page");
            _page = null;
            _pageName = null;
            _padRune = RuneType.None;
            _keptPick = -1;
            if (_rune < 0 && SpellBook.Live.runes.Count > 0) _rune = 0;
            RefreshRunes();
        }

        void RefreshRunes()
        {
            if (_screen == null || _screenKind != ScreenKind.Runes) return;
            var book = SpellBook.Live;
            if (_rune >= book.runes.Count) _rune = book.runes.Count - 1;
            var rune = CurrentRune;
            RuneList(_screen.Columns[0], _screen.Widths[0] - 12f, book);
            RuneMiddle(rune);
            RuneFields(_screen.Columns[2], _screen.Widths[2] - 12f, rune, book);
            RunePushes(_screen.Columns[3], _screen.Widths[3] - 12f, rune);
        }

        // ------------------------------------------------------------- list --
        void RuneList(RectTransform col, float W, SpellBook book) => KeepScroll(col, () =>
        {
            CreatorUI.Note(col, W, Loc.T("mc.runes.note"), 46f);
            var row = CreatorUI.Row(col, W);
            UIKit.Button(row, Loc.T("mc.rune.new"), () =>
            {
                var r = book.NewRune();
                r.Name = Loc.T("mc.rune.new");
                PickRune(book.runes.Count - 1);
            }, CreatorUI.Pick(true), 14);
            var cur = CurrentRune;
            if (cur != null && !cur.BuiltIn)
                UIKit.Button(row, _sureRune ? Loc.T("maps.delete.sure") : Loc.T("creator.delete"), () =>
                {
                    if (!_sureRune) { _sureRune = true; RefreshRunes(); return; }
                    DeleteRune(cur, book);
                }, CreatorUI.Red, 14);

            var names = new List<string>();
            foreach (var r in book.runes) names.Add(r.Name);
            var grid = CreatorUI.Grid(col, W, 1, names, i => i == _rune, PickRune, 34f, 13);
            // each row wears its emoji at the left, for the side being edited
            for (int i = 0; i < grid.childCount && i < book.runes.Count; i++)
            {
                string emoji = book.runes[i].EmojiFor(_runeAcolyte);
                if (string.IsNullOrEmpty(emoji)) continue;
                var t = CreatorUI.Emoji((RectTransform)grid.GetChild(i), emoji, 22f);
                var rt = t.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(34f, 0f);
                rt.anchoredPosition = new Vector2(6f, 0f);
            }
        });

        void PickRune(int i)
        {
            _rune = i;
            _sureRune = false;
            RefreshRunes();
        }

        /// A made rune goes with its kept drawings, and no spell summons with it any more.
        void DeleteRune(RuneDef gone, SpellBook book)
        {
            book.runes.Remove(gone);
            Editing.RuneSamples.RemoveAll(s => s.Rune == gone.Id);
            foreach (var s in book.spells) s.Runes.RemoveAll(r => r == gone.Type);
            RuneLibrary.UseMapSamples(Editing.SampleList());
            _sureRune = false;
            PickRune(Mathf.Min(_rune, book.runes.Count - 1));
        }

        // ------------------------------------------------- drawings and page --
        void RuneMiddle(RuneDef rune)
        {
            float W = _screen.Widths[1] - 12f;
            CreatorScreen.Clear(_runeTabs);
            if (rune != null)
                CreatorUI.Switch(_runeTabs, W, null, new[] { Loc.T("mc.drawing"), Loc.T("mc.page") }, _runeTab,
                    i => { _runeTab = i; RefreshRunes(); });
            _drawView.gameObject.SetActive(rune != null && _runeTab == 0);
            _pageView.gameObject.SetActive(rune != null && _runeTab == 1);
            if (rune == null) return;

            if (rune.Type != _padRune)
            {
                _padRune = rune.Type;
                _keptPick = -1;
                Quiet(() => _pad.Reset());
            }
            if (_keptPick >= Editing.RuneSamples.Count || (_keptPick >= 0 && Editing.RuneSamples[_keptPick].Rune != rune.Id))
                _keptPick = -1;
            _pad.Guide = _keptPick < 0 ? RuneLibrary.RecordedStrokes(rune.Type) : null; // a new drawing gets the rune faint behind it
            _pad.Repaint();
            ReadPad();

            CreatorScreen.Clear(_padTools);
            var tools = CreatorUI.Row(_padTools, W);
            UIKit.Button(tools, Loc.T("mc.undo"), () => Quiet(() => _pad?.Undo()), CreatorUI.Pick(false), 13);
            UIKit.Button(tools, Loc.T("mc.redo"), () => Quiet(() => _pad?.Redo()), CreatorUI.Pick(false), 13);
            UIKit.Button(tools, Loc.T("mc.clear"), () => _pad?.Clear(), CreatorUI.Pick(false), 13);
            CreatorUI.Do(_padTools, W, _keptPick < 0 ? Loc.T("mc.keep") : Loc.T("mc.keep.update"),
                () => KeepPad(rune), CreatorUI.Pick(true), 32f);

            KeptDrawings(rune, W);
            if (_runeTab == 1) PageView(rune, W);
        }

        /// The map's kept drawings of this rune as tiles, and a blank tile with a
        /// plus at the end: pick a tile to work on that drawing, the plus for a new one.
        void KeptDrawings(RuneDef rune, float W)
        {
            CreatorScreen.Clear(_keptView);
            foreach (var t in _thumbs) if (t != null) Destroy(t);
            _thumbs.Clear();
            CreatorUI.Head(_keptView, W, Loc.T("mc.kept"));
            var mine = new List<int>();
            for (int i = 0; i < Editing.RuneSamples.Count; i++)
                if (Editing.RuneSamples[i].Rune == rune.Id) mine.Add(i);

            const float cell = 80f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((W + 4f) / (cell + 4f)));
            int rows = Mathf.CeilToInt((mine.Count + 1) / (float)cols);
            var grid = UIKit.Row(UIKit.Grid(_keptView, "KeptGrid", new Vector2(cell, cell), 4f, cols), W, rows * (cell + 4f));
            var lay = grid.GetComponent<GridLayoutGroup>();
            if (lay != null) lay.childAlignment = TextAnchor.UpperLeft;
            foreach (int at in mine)
            {
                int index = at;
                var tex = RuneSketch.Thumbnail(SampleOf(Editing.RuneSamples[index]), 64, ThumbInk, ThumbGround);
                _thumbs.Add(tex);
                var btn = UIKit.Button(grid, "", () => PickKept(index), CreatorUI.Pick(_keptPick == index), 11);
                var pic = new GameObject("Drawing", typeof(RectTransform), typeof(RawImage));
                pic.transform.SetParent(btn.transform, false);
                var raw = pic.GetComponent<RawImage>();
                raw.texture = tex;
                raw.raycastTarget = false;
                var prt = (RectTransform)pic.transform;
                UIKit.Stretch(prt);
                prt.offsetMin = new Vector2(6f, 6f);
                prt.offsetMax = new Vector2(-6f, -6f);

                var x = UIKit.Button((RectTransform)btn.transform, "X", () =>
                {
                    Editing.RuneSamples.RemoveAt(index);
                    RuneLibrary.UseMapSamples(Editing.SampleList());
                    if (_keptPick == index) { _keptPick = -1; Quiet(() => _pad.Reset()); }
                    else if (_keptPick > index) _keptPick--;
                    RefreshRunes();
                }, CreatorUI.Red, 11);
                var xrt = (RectTransform)x.transform;
                xrt.anchorMin = xrt.anchorMax = xrt.pivot = new Vector2(1f, 1f);
                xrt.sizeDelta = new Vector2(22f, 22f);
                xrt.anchoredPosition = Vector2.zero;
            }
            var plus = UIKit.Button(grid, "", () => PickKept(-1), CreatorUI.Pick(_keptPick < 0), 11);
            var mark = UIKit.Label((RectTransform)plus.transform, "+", 44, UIKit.Ink, TextAnchor.MiddleCenter, true);
            mark.resizeTextForBestFit = false;
            UIKit.Stretch(mark.rectTransform);
        }

        /// A kept drawing comes onto the pad to be worked on; the plus gives a blank pad.
        void PickKept(int index)
        {
            _keptPick = index;
            Quiet(() =>
            {
                if (index < 0 || index >= Editing.RuneSamples.Count) _pad.Reset();
                else _pad.Load(RuneSketch.Fit(SampleOf(Editing.RuneSamples[index]), PadBox, 0f));
            });
            RefreshRunes();
        }

        void ReadPad()
        {
            if (_padReads == null) return;
            if (_pad == null || _pad.Points < 12) { _padReads.text = ""; return; }
            bool was = RuneLibrary.AllRunesUnlockedForTesting;
            RuneLibrary.AllRunesUnlockedForTesting = true;
            var (type, score) = RuneLibrary.Classify(Grimoire.LocalPlayerId, _pad.Sample());
            RuneLibrary.AllRunesUnlockedForTesting = was;
            var def = SpellBook.Live.Rune(type);
            _padReads.text = type == RuneType.None || score < DrawingConfig.MinRuneScore
                ? Loc.T("mc.reads.none")
                : Loc.F("mc.reads", def != null ? def.Name : type.ToString());
        }

        /// A new drawing joins the rune's kept ones (the pad clears for the next);
        /// a picked one is replaced by what the pad holds now.
        void KeepPad(RuneDef rune)
        {
            if (_pad == null || _pad.Points < 12) return;
            var sample = _pad.Sample();
            var def = MapDef.ToSampleDef(rune.Type, sample);
            if (_keptPick >= 0 && _keptPick < Editing.RuneSamples.Count && Editing.RuneSamples[_keptPick].Rune == rune.Id)
            {
                Editing.RuneSamples[_keptPick] = def;
                RuneLibrary.UseMapSamples(Editing.SampleList());
            }
            else
            {
                Editing.RuneSamples.Add(def);
                RuneLibrary.AddMapSample(rune.Type, sample);
                _pad.Clear();
            }
            RefreshRunes();
        }

        static List<List<Vector2>> SampleOf(MapDef.RuneSampleDef d)
        {
            var strokes = new List<List<Vector2>>();
            foreach (var s in d.Strokes)
                if (s?.Points != null && s.Points.Count >= 2) strokes.Add(new List<Vector2>(s.Points));
            return strokes;
        }

        /// ★ The page this rune shows in the grimoire for the side being edited,
        /// to paint on. Every stroke lands on the map's own copy of the page.
        void PageView(RuneDef rune, float W)
        {
            string key = _runeAcolyte ? GrimoirePages.AcolytePageKey(rune.Type) + "_Acolyte" : GrimoirePages.PageKey(rune.Type);
            string pageName = "GrimoirePage_" + key;
            if (_page == null || pageName != _pageName)
            {
                CreatorScreen.Clear(_pageView);
                _pageName = pageName;
                CreatorUI.Note(_pageView, W, Loc.T("mc.page.note"), 34f);
                var tex = GrimoirePages.PageImage(pageName);
                int w = tex != null ? tex.width : 1024, h = tex != null ? tex.height : 742;
                var px = tex != null ? PagePainter.Read(tex) : PagePainter.BlankPage(w, h);
                _page = PagePainter.Create(_pageView, W, px, w, h);
                string name = pageName;
                var painter = _page;
                _page.Changed = () =>
                {
                    MapPages.Put(name, painter.Pixels, painter.Width, painter.Height);
                    CanvasStep(painter);
                };
                _pageTools = CreatorScreen.Section(_pageView, "PageTools");
            }
            _page.Brush = _pageBrush;
            _page.Stamping = _stamping;
            _page.StampSize = _stampSize;
            _page.StampTurn = _stampTurn;
            _page.Glyph = RuneLibrary.RecordedStrokes(rune.Type);

            CreatorScreen.Clear(_pageTools);
            var t = _pageTools;
            CreatorUI.Number(t, W, Loc.T("mc.page.brush"), 2f, 80f, _pageBrush, true, v => { _pageBrush = v; if (_page != null) _page.Brush = v; });
            CreatorUI.Switch(t, W, Loc.T("mc.page.stamp"), new[] { Loc.T("opt.off"), Loc.T("opt.on") }, _stamping ? 1 : 0,
                i => { _stamping = i == 1; RefreshRunes(); });
            if (_stamping)
            {
                if (_page.Glyph == null) CreatorUI.Note(t, W, Loc.T("mc.page.noglyph"), 18f);
                CreatorUI.Number(t, W, Loc.T("mc.page.stampsize"), 0.1f, 1f, _stampSize, false, v => { _stampSize = v; if (_page != null) _page.StampSize = v; });
                CreatorUI.Number(t, W, Loc.T("mc.page.stampturn"), -180f, 180f, _stampTurn, true, v => { _stampTurn = v; if (_page != null) _page.StampTurn = v; });
            }
            var row = CreatorUI.Row(t, W);
            UIKit.Button(row, Loc.T("mc.undo"), () => Quiet(() => _page?.Undo()), CreatorUI.Pick(false), 13);
            UIKit.Button(row, Loc.T("mc.redo"), () => Quiet(() => _page?.Redo()), CreatorUI.Pick(false), 13);
            UIKit.Button(row, Loc.T("mc.clear"), () => _page?.Clear(), CreatorUI.Pick(false), 13);
        }

        // ---------------------------------------------------- name and emoji --
        void RuneFields(RectTransform col, float W, RuneDef rune, SpellBook book) => KeepScroll(col, () =>
        {
            if (rune == null) { CreatorUI.Note(col, W, Loc.T("mc.rune.none"), 30f); return; }
            CreatorUI.Field(col, W, Loc.T("mc.name"), rune.Name, v =>
            {
                rune.Name = v;
                RuneList(_screen.Columns[0], _screen.Widths[0] - 12f, book);
            });
            CreatorUI.Switch(col, W, Loc.T("mc.side"), new[] { Loc.T("mc.side.wizard"), Loc.T("mc.side.acolyte") },
                _runeAcolyte ? 1 : 0, i => { _runeAcolyte = i == 1; RefreshRunes(); });
            CreatorUI.Note(col, W, Loc.T("mc.side.note"), 34f);

            CreatorUI.Head(col, W, Loc.T("mc.emoji"));
            string own = _runeAcolyte ? rune.AcolyteEmoji : rune.Emoji;
            var shown = UIKit.Row(UIKit.Label(col, "", 13, UIKit.Ink, TextAnchor.MiddleCenter), W, 60f);
            CreatorUI.Emoji((RectTransform)shown.transform, string.IsNullOrEmpty(own) ? Loc.T("mc.none") : own, 48f)
                .color = new Color(0.13f, 0.1f, 0.07f);
            var row = CreatorUI.Row(col, W, 32f);
            bool picking = _open.Contains("rune:emoji");
            UIKit.Button(row, Loc.T("mc.emoji.pick") + (picking ? "  -" : "  +"), () =>
            {
                if (!_open.Remove("rune:emoji")) _open.Add("rune:emoji");
                RefreshRunes();
            }, CreatorUI.Pick(picking), 13);
            if (!string.IsNullOrEmpty(own))
                UIKit.Button(row, Loc.T("mc.clear"), () => { SetEmoji(rune, ""); RefreshRunes(); }, CreatorUI.Pick(false), 13);
            if (picking) EmojiGrid(col, W, book, rune);
        });

        void SetEmoji(RuneDef rune, string e)
        {
            if (_runeAcolyte) rune.AcolyteEmoji = e;
            else rune.Emoji = e;
        }

        /// Every emoji the game can draw; one already on another rune of the side is taken.
        void EmojiGrid(RectTransform b, float W, SpellBook book, RuneDef rune)
        {
            var sprites = TMPro.TMP_Settings.defaultSpriteAsset;
            if (sprites == null) return;
            var choices = new List<string>();
            foreach (var ch in sprites.spriteCharacterTable)
                if (ch != null && ch.unicode != 0 && ch.unicode != 0xFFFE)
                    choices.Add(char.ConvertFromUtf32((int)ch.unicode));
            const float cell = 48f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((W + 4f) / (cell + 4f)));
            int rows = Mathf.CeilToInt(choices.Count / (float)cols);
            var grid = UIKit.Row(UIKit.Grid(b, "Emoji", new Vector2(cell, cell), 4f, cols), W, rows * (cell + 4f));
            string own = _runeAcolyte ? rune.AcolyteEmoji : rune.Emoji;
            foreach (var e in choices)
            {
                string emoji = e;
                bool taken = false;
                foreach (var other in book.runes)
                    if (other != rune && (_runeAcolyte ? other.AcolyteEmoji : other.Emoji) == emoji) { taken = true; break; }
                var btn = UIKit.Button(grid, "", () =>
                {
                    SetEmoji(rune, emoji);
                    _open.Remove("rune:emoji");
                    RefreshRunes();
                }, CreatorUI.Pick(emoji == own), 11);
                btn.interactable = !taken;
                var t = CreatorUI.Emoji((RectTransform)btn.transform, emoji, 30f);
                if (taken) t.alpha = 0.35f;
            }
        }

        // ----------------------------------------------------------- pushes --
        void RunePushes(RectTransform col, float W, RuneDef rune) => KeepScroll(col, () =>
        {
            if (rune == null) return;
            CreatorUI.Head(col, W, Loc.T("mc.pushes"));
            CreatorUI.Note(col, W, Loc.T("mc.pushes.note"), 34f);
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                int axis = i;
                SpellPayload.SpellRange(axis, out int lo, out int hi);
                CreatorUI.Number(col, W, Loc.T("axis." + axis), lo, hi, rune.Axis[axis], true, v => rune.Axis[axis] = Mathf.RoundToInt(v),
                    v => Loc.T("axis." + axis) + ": " + Mathf.RoundToInt(v) + SpellPayload.UnitName(axis));
            }
        });
    }
}
