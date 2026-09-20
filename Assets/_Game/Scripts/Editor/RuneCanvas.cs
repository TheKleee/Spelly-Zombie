using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Mouse strokes on an editor rect, the Rune Creator's glyph pad and page
    /// painter share it. Left mouse draws, right mouse erases (the pen's own
    /// habit), undo and redo step through snapshots. Points are canvas pixels,
    /// y down; ToSample turns them into the library's frame (metres, y up,
    /// centred), the wall's own law, so mirror pairs stay right.
    public class RuneCanvas
    {
        public readonly List<List<Vector2>> Strokes = new List<List<Vector2>>();
        /// One per stroke: the brush it was drawn with (the page painter reads it).
        public readonly List<(float radius, Color32 ink)> Styles = new List<(float, Color32)>();
        /// The brush a new stroke wears.
        public (float radius, Color32 ink) CurrentStyle = (2f, new Color32(0, 0, 0, 255));
        public float EraseRadius = 14f;
        /// A stroke or an erase just ended.
        public System.Action Changed;
        /// A left click the owner wants for itself (a stamp): true = taken, no stroke starts.
        public System.Func<Vector2, bool> OnClick;
        /// The last framed size, and the mouse in canvas space (negative = outside).
        public Vector2 Size;
        public Vector2 Mouse = new Vector2(-1f, -1f);
        public IReadOnlyList<Vector2> Live => _live;
        public bool Erasing => _erasing;
        public bool CanUndo => _history.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        class Snapshot { public List<List<Vector2>> S; public List<(float, Color32)> St; }

        readonly EditorWindow _owner;
        readonly List<Snapshot> _history = new List<Snapshot>();
        readonly List<Snapshot> _redo = new List<Snapshot>();
        List<Vector2> _live;
        bool _erasing, _erasedAny;

        public RuneCanvas(EditorWindow owner) { _owner = owner; }

        /// Lays out the rect, paints its ground, takes the mouse. The caller draws on top.
        public Rect Frame(float width, float aspect, Color ground)
        {
            var rect = GUILayoutUtility.GetRect(width, width / aspect, GUILayout.ExpandWidth(false));
            Size = rect.size;
            if (ground.a > 0f) EditorGUI.DrawRect(rect, ground);
            HandleMouse(rect);
            return rect;
        }

        void HandleMouse(Rect rect)
        {
            var e = Event.current;
            Vector2 at = e.mousePosition - rect.position;
            bool inside = rect.Contains(e.mousePosition);
            switch (e.type)
            {
                case EventType.MouseMove:
                    Mouse = inside ? at : new Vector2(-1f, -1f);
                    if (inside) _owner.Repaint();
                    break;
                case EventType.MouseDown:
                    if (!inside) return;
                    Mouse = at;
                    if (e.button == 0)
                    {
                        if (OnClick != null && OnClick(at)) { e.Use(); _owner.Repaint(); return; }
                        Remember();
                        _live = new List<Vector2> { at };
                        Strokes.Add(_live);
                        Styles.Add(CurrentStyle);
                        e.Use();
                    }
                    else if (e.button == 1)
                    {
                        Remember();
                        _erasing = true; _erasedAny = false;
                        Erase(at);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    at.x = Mathf.Clamp(at.x, 0f, rect.width);
                    at.y = Mathf.Clamp(at.y, 0f, rect.height);
                    Mouse = at;
                    if (_live != null)
                    {
                        if ((at - _live[_live.Count - 1]).sqrMagnitude >= 2.25f) _live.Add(at);
                        e.Use(); _owner.Repaint();
                    }
                    else if (_erasing)
                    {
                        Erase(at);
                        e.Use(); _owner.Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (_live != null)
                    {
                        if (_live.Count < 2) { RemoveStroke(Strokes.Count - 1); Forget(); }
                        _live = null;
                        e.Use();
                        Changed?.Invoke();
                        _owner.Repaint();
                    }
                    else if (_erasing)
                    {
                        _erasing = false;
                        if (!_erasedAny) Forget();
                        e.Use();
                        Changed?.Invoke();
                        _owner.Repaint();
                    }
                    break;
            }
        }

        /// Rubs out every point within EraseRadius (RuneSketch.Erase); the styles follow the cut.
        void Erase(Vector2 at)
        {
            if (RuneSketch.Erase(Strokes, at, EraseRadius, (i, n) =>
            {
                var style = i < Styles.Count ? Styles[i] : CurrentStyle;
                if (i < Styles.Count) Styles.RemoveAt(i);
                for (int k = 0; k < n; k++) Styles.Insert(i, style);
            })) _erasedAny = true;
        }

        void RemoveStroke(int i)
        {
            if (i < 0 || i >= Strokes.Count) return;
            Strokes.RemoveAt(i);
            if (i < Styles.Count) Styles.RemoveAt(i);
        }

        // ---- history: a snapshot before every change
        void Remember()
        {
            _history.Add(Snap());
            if (_history.Count > 60) _history.RemoveAt(0);
            _redo.Clear();
        }
        void Forget() { if (_history.Count > 0) _history.RemoveAt(_history.Count - 1); }
        Snapshot Snap()
        {
            var s = new Snapshot { S = new List<List<Vector2>>(Strokes.Count), St = new List<(float, Color32)>(Styles) };
            foreach (var st in Strokes) s.S.Add(new List<Vector2>(st));
            return s;
        }
        void Restore(Snapshot s)
        {
            Strokes.Clear(); Styles.Clear();
            foreach (var st in s.S) Strokes.Add(new List<Vector2>(st));
            Styles.AddRange(s.St);
        }
        public void Undo()
        {
            if (_live != null || _erasing || _history.Count == 0) return;
            _redo.Add(Snap());
            Restore(_history[_history.Count - 1]);
            _history.RemoveAt(_history.Count - 1);
        }
        public void Redo()
        {
            if (_live != null || _erasing || _redo.Count == 0) return;
            _history.Add(Snap());
            Restore(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
        }
        public void Clear()
        {
            if (Strokes.Count > 0) Remember();
            Strokes.Clear(); Styles.Clear(); _live = null;
        }

        /// Strokes the owner places (a stamp): one undo step, the current brush.
        public void AddStrokes(IEnumerable<List<Vector2>> strokes)
        {
            Remember();
            foreach (var s in strokes)
            {
                if (s == null || s.Count < 2) continue;
                Strokes.Add(s);
                Styles.Add(CurrentStyle);
            }
        }

        /// Everything gone, history too: a new rune, tab or side.
        public void Reset()
        {
            Strokes.Clear(); Styles.Clear(); _history.Clear(); _redo.Clear();
            _live = null; _erasing = false;
        }
        public int Points { get { int n = 0; foreach (var s in Strokes) n += s.Count; return n; } }

        /// Strokes as lines over the rect.
        public static void DrawStrokes(Rect rect, IReadOnlyList<IReadOnlyList<Vector2>> strokes, Color color, float width)
        {
            if (strokes == null || Event.current.type != EventType.Repaint) return;
            Handles.color = color;
            foreach (var s in strokes)
            {
                if (s == null || s.Count < 2) continue;
                var pts = new Vector3[s.Count];
                for (int i = 0; i < s.Count; i++) pts[i] = new Vector3(rect.x + s[i].x, rect.y + s[i].y, 0f);
                Handles.DrawAAPolyLine(width, pts);
            }
        }

        /// The eraser's reach while the right button is down.
        public void DrawEraser(Rect rect)
        {
            if (!_erasing || Event.current.type != EventType.Repaint || Mouse.x < 0f) return;
            Handles.color = new Color(1f, 0.4f, 0.3f, 0.9f);
            Handles.DrawWireDisc(new Vector3(rect.x + Mouse.x, rect.y + Mouse.y, 0f), Vector3.forward, EraseRadius);
        }

        /// Canvas pixels to the sample frame (RuneSketch.ToSample).
        public static List<List<Vector2>> ToSample(IReadOnlyList<IReadOnlyList<Vector2>> strokes, float w, float h, float metres = 0.4f)
            => RuneSketch.ToSample(strokes, w, h, metres);

        /// A sample fitted into a box on the canvas (RuneSketch.Fit).
        public static List<List<Vector2>> Fit(IReadOnlyList<IReadOnlyList<Vector2>> sample, Rect box, float angle)
            => RuneSketch.Fit(sample, box, angle);

        /// A round brush into texture pixels (RuneSketch.Paint).
        public static void Paint(Color32[] px, int w, int h, IReadOnlyList<Vector2> stroke, float k, float radius, Color32 ink)
            => RuneSketch.Paint(px, w, h, stroke, k, radius, ink);
    }
}
