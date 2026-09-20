using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE RUNE CREATOR. Runes are their own thing: a glyph you draw and
    /// keep, an emoji per side picked from the atlas, a page png per side, and
    /// what drawing it pushes. What the numbers become is the Spell Creator's
    /// business. New runes count on from 13 and flow through the game as ids.
    /// Rune Studio recorded on walls before this.
    public class RuneCreator : EditorWindow
    {
        [MenuItem("Spelly Zombie/Spells/Rune Creator")]
        static void Open() => GetWindow<RuneCreator>("Rune Creator").minSize = new Vector2(1040, 640);

        const string PagesFolder = "Assets/_Game/Art/2D/Book Pages";
        const string EmojiFolder = "Assets/_Game/Fonts/sz-emoji";
        static readonly Color Paper = new Color(0.96f, 0.92f, 0.80f);
        static readonly Color32 Ink = new Color32(0, 0, 0, 255);
        static readonly Color PadGround = new Color(0.13f, 0.12f, 0.15f);

        SpellBook _book;
        int _picked;
        bool _acolyte;              // the side the emoji and page belong to
        int _tab;                   // 0 glyph, 1 page
        Vector2 _list, _right, _thumbs, _emojiScroll;
        bool _pickEmoji;
        RuneCanvas _glyph, _page;
        RuneType _canvasRune = RuneType.None;
        string _reads = "";
        int _reference = -1;        // a saved drawing shown faint on the pad
        Color32[] _pageBase, _pagePx;
        Texture2D _pageTex;
        int _pageW = 1024, _pageH = 742; // the size his pages are
        bool _pageSide;
        float _brush = 6f, _stampSize = 0.45f, _stampAngle;
        bool _erase, _stamp;
        readonly List<(string emoji, Texture2D png, string stem)> _atlas = new List<(string, Texture2D, string)>();

        RuneDef Current => _book != null && _picked >= 0 && _picked < _book.runes.Count ? _book.runes[_picked] : null;

        void OnEnable()
        {
            _book = SpellBook.Load();
            _book.EnsureRunes();
            _picked = _book.runes.Count > 0 ? Mathf.Clamp(_picked, 0, _book.runes.Count - 1) : -1;
            wantsMouseMove = true;
            LoadAtlas();
        }

        void OnDisable() { if (_pageTex != null) DestroyImmediate(_pageTex); }

        void OnGUI()
        {
            if (_book == null) OnEnable();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawCanvas();
                DrawFields();
            }
        }

        // ---------------------------------------------------------- the list
        void DrawList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(190)))
            {
                EditorGUILayout.LabelField("RUNES", EditorStyles.boldLabel);
                _list = EditorGUILayout.BeginScrollView(_list);
                for (int i = 0; i < _book.runes.Count; i++)
                {
                    var r = _book.runes[i];
                    string emoji = r.EmojiFor(_acolyte);
                    bool on = i == _picked;
                    if (GUILayout.Toggle(on, (string.IsNullOrEmpty(emoji) ? "·" : emoji) + "  " + r.Name, "Button") && !on)
                        _picked = i;
                }
                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("+ New rune"))
                {
                    _book.NewRune();
                    _picked = _book.runes.Count - 1;
                }
                var cur = Current;
                using (new EditorGUI.DisabledScope(cur == null || cur.BuiltIn))
                    if (GUILayout.Button("Delete rune") && cur != null
                        && EditorUtility.DisplayDialog("Delete rune",
                            $"Delete {cur.Name} and its saved drawings?", "Delete", "Cancel"))
                    {
                        RuneLibrary.ReplaceSamples(cur.Type, new List<List<List<Vector2>>>(), allowClear: true);
                        _book.runes.RemoveAt(_picked);
                        _picked = Mathf.Min(_picked, _book.runes.Count - 1);
                    }
                EditorGUILayout.Space();
                if (GUILayout.Button("SAVE", GUILayout.Height(28)))
                {
                    _book.Save();
                    ShowNotification(new GUIContent("Saved"));
                }
                if (GUILayout.Button("Reload")) OnEnable();
            }
        }

        // -------------------------------------------------------- the fields
        void DrawFields()
        {
            var r = Current;
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(380)))
            {
                if (r == null) { EditorGUILayout.LabelField("Make a rune."); return; }
                _right = EditorGUILayout.BeginScrollView(_right);
                r.Name = EditorGUILayout.TextField("Name", r.Name);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Side");
                    _acolyte = GUILayout.Toolbar(_acolyte ? 1 : 0, new[] { "Wizard", "Acolyte" }) == 1;
                }
                EditorGUILayout.LabelField("The emoji and the page below belong to this side. An acolyte with none of " +
                                           "its own shows the wizard's.", EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("EMOJI", EditorStyles.boldLabel);
                string own = _acolyte ? r.AcolyteEmoji : r.Emoji;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(own) ? "none" : own, big, GUILayout.Width(70), GUILayout.Height(32));
                    _pickEmoji = GUILayout.Toggle(_pickEmoji, "Pick", "Button", GUILayout.Height(32));
                    if (GUILayout.Button("Clear", GUILayout.Height(32))) SetEmoji(r, "");
                }
                if (_pickEmoji) DrawEmojiPicker(r);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("PUSHES", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("What drawing this rune adds to the seal's numbers, one axis or several. " +
                                           "The spells decide what that becomes.", EditorStyles.wordWrappedMiniLabel);
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                {
                    SpellPayload.SpellRange(i, out int lo, out int hi);
                    r.Axis[i] = EditorGUILayout.IntSlider(
                        new GUIContent(SpellCreator.Names[i] + SpellPayload.UnitName(i), SpellCreator.Poles[i]), r.Axis[i], lo, hi);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        void SetEmoji(RuneDef r, string e) { if (_acolyte) r.AcolyteEmoji = e; else r.Emoji = e; }

        /// The atlas folder: every Noto png is one emoji the game can show.
        void LoadAtlas()
        {
            _atlas.Clear();
            if (!Directory.Exists(EmojiFolder)) return;
            var files = Directory.GetFiles(EmojiFolder, "emoji_u*.png");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var f in files)
            {
                string stem = Path.GetFileNameWithoutExtension(f);
                var sb = new System.Text.StringBuilder();
                foreach (var part in stem.Substring("emoji_u".Length).Split('_'))
                    if (int.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out int cp))
                        sb.Append(char.ConvertFromUtf32(cp));
                var png = AssetDatabase.LoadAssetAtPath<Texture2D>(f.Replace('\\', '/'));
                _atlas.Add((sb.ToString(), png, stem));
            }
        }

        /// Every emoji in the atlas; one another rune already wears is greyed with its name.
        void DrawEmojiPicker(RuneDef r)
        {
            if (_atlas.Count == 0)
            {
                EditorGUILayout.HelpBox($"No emoji pngs in {EmojiFolder}. Add Noto pngs named emoji_u<hex>.png there.", MessageType.Info);
                return;
            }
            var taken = new Dictionary<string, string>();
            foreach (var o in _book.runes)
            {
                if (o == r) continue;
                if (!string.IsNullOrEmpty(o.Emoji) && !taken.ContainsKey(o.Emoji)) taken[o.Emoji] = o.Name;
                if (!string.IsNullOrEmpty(o.AcolyteEmoji) && !taken.ContainsKey(o.AcolyteEmoji)) taken[o.AcolyteEmoji] = o.Name + " (acolyte)";
            }
            _emojiScroll = EditorGUILayout.BeginScrollView(_emojiScroll, GUILayout.Height(170));
            const int perRow = 7;
            for (int i = 0; i < _atlas.Count; i += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                    for (int j = i; j < Mathf.Min(i + perRow, _atlas.Count); j++)
                    {
                        var e = _atlas[j];
                        bool isTaken = taken.TryGetValue(e.emoji, out var by);
                        string tip = isTaken ? "used by " + by : e.stem;
                        var content = e.png != null ? new GUIContent(e.png, tip) : new GUIContent(e.emoji, tip);
                        using (new EditorGUI.DisabledScope(isTaken))
                            if (GUILayout.Button(content, GUILayout.Width(44), GUILayout.Height(44)))
                            {
                                SetEmoji(r, e.emoji);
                                _pickEmoji = false;
                            }
                    }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField($"{_atlas.Count} in the atlas. More: drop Noto pngs into {EmojiFolder}.",
                                       EditorStyles.wordWrappedMiniLabel);
        }

        // -------------------------------------------------------- the canvas
        void DrawCanvas()
        {
            var r = Current;
            float width = Mathf.Clamp(position.width - 190f - 380f - 30f, 340f, 900f);
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(width)))
            {
                if (r == null) return;
                if (_glyph == null) { _glyph = new RuneCanvas(this); _glyph.Changed = ReadGlyph; }
                if (_page == null) { _page = new RuneCanvas(this); _page.Changed = BakePage; _page.OnClick = StampAt; }
                if (r.Type != _canvasRune)
                {
                    _canvasRune = r.Type;
                    _glyph.Reset(); _reads = ""; _reference = -1;
                    _page.Reset(); _pageBase = null;
                }
                int tab = GUILayout.Toolbar(_tab, new[] { "GLYPH", "PAGE" });
                if (tab != _tab) { _tab = tab; _page.Reset(); _pageBase = null; }
                if (_tab == 0) DrawGlyphPad(r, width); else DrawPagePainter(r, width);
            }
        }

        /// ★ THE GLYPH PAD. Draw the rune; plus keeps the drawing as one of its
        /// saved drawings (the recognizer's samples). Saved drawings sit below
        /// as thumbnails, one at a time as a faint reference when clicked.
        void DrawGlyphPad(RuneDef r, float width)
        {
            var samples = RuneLibrary.AllSamples(r.Type);
            EditorGUILayout.LabelField("Left mouse draws, right mouse erases. Plus keeps the drawing.", EditorStyles.wordWrappedMiniLabel);
            float side = Mathf.Min(width, 460f);
            var rect = _glyph.Frame(side, 1f, PadGround);
            if (_reference >= 0 && _reference < samples.Count)
            {
                var box = new Rect(rect.width * 0.1f, rect.height * 0.1f, rect.width * 0.8f, rect.height * 0.8f);
                RuneCanvas.DrawStrokes(rect, RuneCanvas.Fit(samples[_reference], box, 0f), new Color(1f, 1f, 1f, 0.25f), 2f);
            }
            RuneCanvas.DrawStrokes(rect, _glyph.Strokes, Color.white, 4f);
            if (_glyph.Live != null) RuneCanvas.DrawStrokes(rect, new[] { _glyph.Live }, Color.white, 4f);
            _glyph.DrawEraser(rect);

            EditorGUILayout.LabelField(string.IsNullOrEmpty(_reads) ? " " : _reads, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_glyph.CanUndo))
                    if (GUILayout.Button("Undo")) { _glyph.Undo(); ReadGlyph(); }
                using (new EditorGUI.DisabledScope(!_glyph.CanRedo))
                    if (GUILayout.Button("Redo")) { _glyph.Redo(); ReadGlyph(); }
                if (GUILayout.Button("Clear")) { _glyph.Clear(); _reads = ""; }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"SAVED DRAWINGS  ({samples.Count})", EditorStyles.boldLabel);
            _thumbs = EditorGUILayout.BeginScrollView(_thumbs, GUILayout.Height(100));
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    var cell = GUILayoutUtility.GetRect(76, 76, GUILayout.ExpandWidth(false));
                    bool on = _reference == i;
                    EditorGUI.DrawRect(cell, on ? new Color(0.25f, 0.35f, 0.5f) : PadGround);
                    RuneCanvas.DrawStrokes(cell, RuneCanvas.Fit(samples[i], new Rect(10, 10, 56, 56), 0f), Color.white, 1.5f);
                    // the small x first, so its click is its own
                    var x = new Rect(cell.xMax - 17, cell.y + 1, 16, 16);
                    if (GUI.Button(x, "×", EditorStyles.miniButton)
                        && EditorUtility.DisplayDialog("Delete drawing", $"Delete saved drawing {i + 1} of {r.Name}?", "Delete", "Cancel"))
                    {
                        samples.RemoveAt(i);
                        RuneLibrary.ReplaceSamples(r.Type, samples, allowClear: true);
                        _reference = -1;
                        break;
                    }
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && cell.Contains(Event.current.mousePosition))
                    {
                        _reference = on ? -1 : i;
                        Event.current.Use();
                        Repaint();
                    }
                }
                using (new EditorGUI.DisabledScope(_glyph.Points < 12))
                    if (GUILayout.Button(new GUIContent("+", "Keep the pad's drawing"), GUILayout.Width(76), GUILayout.Height(76)))
                    {
                        var sample = RuneCanvas.ToSample(_glyph.Strokes, _glyph.Size.x, _glyph.Size.y);
                        if (RuneLibrary.AddSample(r.Type, sample))
                        {
                            _glyph.Clear(); _reads = "";
                            ShowNotification(new GUIContent("Drawing kept"));
                        }
                        else ShowNotification(new GUIContent("Too small to teach - draw larger"));
                    }
            }
            EditorGUILayout.EndScrollView();
        }

        /// What the recognizer makes of the pad right now, every rune allowed.
        void ReadGlyph()
        {
            if (_glyph.Points < 12) { _reads = ""; return; }
            var sample = RuneCanvas.ToSample(_glyph.Strokes, _glyph.Size.x, _glyph.Size.y);
            bool prev = RuneLibrary.AllRunesUnlockedForTesting;
            RuneLibrary.AllRunesUnlockedForTesting = true;
            var (t, s) = RuneLibrary.Classify(0, sample);
            RuneLibrary.AllRunesUnlockedForTesting = prev;
            _reads = t == RuneType.None ? "reads as: nothing yet"
                : $"reads as: {RuneLibrary.Icon(t)} {RuneLibrary.ShortName(t)}  ({s:0.00})";
        }

        /// ★ THE PAGE PAINTER. The side's grimoire page png: the one that
        /// exists, or blank paper. Black ink, an eraser, the kept glyph
        /// stamped where you click at the size and turn you set. SAVE PNG
        /// writes into Book Pages and joins the Collection Manager's list.
        void DrawPagePainter(RuneDef r, float width)
        {
            string key = _acolyte ? GrimoirePages.AcolytePageKey(r.Type) + "_Acolyte" : GrimoirePages.PageKey(r.Type);
            string path = $"{PagesFolder}/GrimoirePage_{key}.png";
            if (_pageBase == null || _pageSide != _acolyte)
            {
                _pageSide = _acolyte;
                _page.Reset();
                LoadPageBase(path);
            }
            EditorGUILayout.LabelField((File.Exists(path) ? "Editing " : "New page ") + Path.GetFileName(path)
                                       + $"  ({_pageW}x{_pageH}). Left mouse paints, right mouse erases.", EditorStyles.wordWrappedMiniLabel);

            _page.CurrentStyle = (_brush * 0.5f, _erase ? (Color32)Paper : Ink);
            var rect = _page.Frame(width, (float)_pageW / _pageH, Color.clear);
            if (_pageTex != null) GUI.DrawTexture(rect, _pageTex, ScaleMode.StretchToFill);
            float k = _pageW / Mathf.Max(1f, rect.width);
            _page.EraseRadius = Mathf.Max(6f, _brush / k);
            if (_page.Live != null)
                RuneCanvas.DrawStrokes(rect, new[] { _page.Live }, _erase ? Paper : Color.black, Mathf.Max(1f, _brush / k));
            _page.DrawEraser(rect);
            if (_stamp && _page.Mouse.x >= 0f)
            {
                var recorded = RuneLibrary.RecordedStrokes(r.Type);
                if (recorded != null)
                {
                    float size = rect.height * _stampSize;
                    var box = new Rect(_page.Mouse.x - size * 0.5f, _page.Mouse.y - size * 0.5f, size, size);
                    RuneCanvas.DrawStrokes(rect, RuneCanvas.Fit(recorded, box, _stampAngle), new Color(0f, 0f, 0f, 0.35f), Mathf.Max(1f, _brush / k));
                }
            }

            _brush = EditorGUILayout.Slider(new GUIContent("Brush (px)", "Round brush width in image pixels"), _brush, 2f, 80f);
            using (new EditorGUILayout.HorizontalScope())
            {
                _erase = GUILayout.Toggle(_erase, "Eraser (paper)", "Button");
                _stamp = GUILayout.Toggle(_stamp, "Stamp the kept glyph on click", "Button");
            }
            if (_stamp)
            {
                _stampSize = EditorGUILayout.Slider(new GUIContent("Stamp size", "Of the page height"), _stampSize, 0.1f, 1f);
                _stampAngle = EditorGUILayout.Slider(new GUIContent("Stamp turn", "Degrees"), _stampAngle, -180f, 180f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!_page.CanUndo))
                    if (GUILayout.Button("Undo")) { _page.Undo(); BakePage(); }
                using (new EditorGUI.DisabledScope(!_page.CanRedo))
                    if (GUILayout.Button("Redo")) { _page.Redo(); BakePage(); }
                if (GUILayout.Button("Clear strokes")) { _page.Clear(); BakePage(); }
                if (GUILayout.Button("Reload page")) { _page.Clear(); _pageBase = null; }
            }
            if (GUILayout.Button("SAVE PNG", GUILayout.Height(28)))
            {
                var tex = new Texture2D(_pageW, _pageH, TextureFormat.RGBA32, false);
                tex.SetPixels32(_pagePx);
                tex.Apply();
                Directory.CreateDirectory(PagesFolder);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                CollectionListTools.AddPages(); // the list the game reads; warns when no Collection Manager is open
                _page.Reset(); _pageBase = null; // the strokes are paint now
                ShowNotification(new GUIContent("Saved " + Path.GetFileName(path)));
            }
        }

        void LoadPageBase(string path)
        {
            if (File.Exists(path))
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(path));
                _pageW = t.width; _pageH = t.height;
                _pageBase = t.GetPixels32();
                DestroyImmediate(t);
            }
            else
            {
                _pageW = 1024; _pageH = 742;
                _pageBase = new Color32[_pageW * _pageH];
                Color32 paper = Paper;
                for (int i = 0; i < _pageBase.Length; i++) _pageBase[i] = paper;
            }
            BakePage();
        }

        /// Every stroke over the base, into the preview texture.
        void BakePage()
        {
            if (_pageBase == null) return;
            _pagePx = (Color32[])_pageBase.Clone();
            float k = _pageW / Mathf.Max(1f, _page.Size.x);
            for (int i = 0; i < _page.Strokes.Count; i++)
            {
                var style = i < _page.Styles.Count ? _page.Styles[i] : _page.CurrentStyle;
                RuneCanvas.Paint(_pagePx, _pageW, _pageH, _page.Strokes[i], k, style.radius, style.ink);
            }
            if (_pageTex == null || _pageTex.width != _pageW || _pageTex.height != _pageH)
            {
                if (_pageTex != null) DestroyImmediate(_pageTex);
                _pageTex = new Texture2D(_pageW, _pageH, TextureFormat.RGBA32, false);
            }
            _pageTex.SetPixels32(_pagePx);
            _pageTex.Apply();
            Repaint();
        }

        /// Stamp mode: the kept glyph lands where you click, as strokes you can undo like any other.
        bool StampAt(Vector2 at)
        {
            if (!_stamp || _canvasRune == RuneType.None) return false;
            var recorded = RuneLibrary.RecordedStrokes(_canvasRune);
            if (recorded == null) { ShowNotification(new GUIContent("No kept drawing to stamp")); return true; }
            float size = _page.Size.y * _stampSize;
            var box = new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size);
            _page.AddStrokes(RuneCanvas.Fit(recorded, box, _stampAngle));
            BakePage();
            return true;
        }
    }
}
