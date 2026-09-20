using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// A drawing surface with its own undo line (the rune pad, a page).
    public interface IUndoCanvas
    {
        bool CanUndo { get; }
        bool CanRedo { get; }
        void Undo();
        void Redo();
    }

    /// ★ THE GAME'S DRAWING PAD: the left button draws, the right button rubs
    /// out, undo and redo remember every change. Strokes are pad pixels with
    /// y down, the frame RuneSketch turns into a sample.
    public class SketchPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IUndoCanvas
    {
        public const int Pixels = 256;
        public readonly List<List<Vector2>> Strokes = new List<List<Vector2>>();
        /// A faint drawing under the ink (the rune's own), in the sample frame.
        public IReadOnlyList<IReadOnlyList<Vector2>> Guide;
        public System.Action Changed;
        public float EraseRadius = 12f;

        static readonly Color32 Ground = new Color32(40, 33, 27, 255);
        static readonly Color32 Ink = new Color32(250, 244, 230, 255);
        static readonly Color32 GuideInk = new Color32(105, 94, 80, 255);

        RawImage _img;
        Texture2D _tex;
        Color32[] _px;
        List<Vector2> _live;
        bool _erasing, _erasedAny;
        readonly List<List<List<Vector2>>> _undo = new List<List<List<Vector2>>>();
        readonly List<List<List<Vector2>>> _redo = new List<List<List<Vector2>>>();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public int Points { get { int n = 0; foreach (var s in Strokes) n += s.Count; return n; } }

        public static SketchPad Create(RectTransform parent, float size)
        {
            var go = new GameObject("SketchPad", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var pad = go.AddComponent<SketchPad>();
            pad._img = go.GetComponent<RawImage>();
            pad._tex = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            pad._px = new Color32[Pixels * Pixels];
            pad._img.texture = pad._tex;
            UIKit.Row(pad._img, size, size);
            pad.Repaint();
            return pad;
        }

        void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }

        bool ToPad(PointerEventData e, out Vector2 p)
        {
            p = default;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var lp))
                return false;
            var r = rt.rect;
            p = new Vector2((lp.x - r.xMin) / Mathf.Max(1f, r.width) * Pixels,
                            (r.yMax - lp.y) / Mathf.Max(1f, r.height) * Pixels);
            p.x = Mathf.Clamp(p.x, 0f, Pixels);
            p.y = Mathf.Clamp(p.y, 0f, Pixels);
            return true;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!ToPad(e, out var p)) return;
            if (e.button == PointerEventData.InputButton.Left)
            {
                Remember();
                _live = new List<Vector2> { p };
                Strokes.Add(_live);
            }
            else if (e.button == PointerEventData.InputButton.Right)
            {
                Remember();
                _erasing = true;
                _erasedAny = RuneSketch.Erase(Strokes, p, EraseRadius);
            }
            Repaint();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!ToPad(e, out var p)) return;
            if (_live != null)
            {
                if ((p - _live[_live.Count - 1]).sqrMagnitude < 2.25f) return;
                _live.Add(p);
                Repaint();
            }
            else if (_erasing && RuneSketch.Erase(Strokes, p, EraseRadius))
            {
                _erasedAny = true;
                Repaint();
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (_live != null)
            {
                if (_live.Count < 2) { Strokes.Remove(_live); Forget(); }
                _live = null;
            }
            else if (_erasing)
            {
                _erasing = false;
                if (!_erasedAny) Forget();
            }
            else return;
            Repaint();
            Changed?.Invoke();
        }

        void Remember()
        {
            _undo.Add(Copy(Strokes));
            if (_undo.Count > 60) _undo.RemoveAt(0);
            _redo.Clear();
        }

        void Forget() { if (_undo.Count > 0) _undo.RemoveAt(_undo.Count - 1); }

        static List<List<Vector2>> Copy(List<List<Vector2>> s)
        {
            var c = new List<List<Vector2>>(s.Count);
            foreach (var st in s) c.Add(new List<Vector2>(st));
            return c;
        }

        void Put(List<List<Vector2>> s)
        {
            Strokes.Clear();
            foreach (var st in s) Strokes.Add(new List<Vector2>(st));
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(Copy(Strokes));
            Put(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            Repaint();
            Changed?.Invoke();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(Copy(Strokes));
            Put(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            Repaint();
            Changed?.Invoke();
        }

        public void Clear()
        {
            if (Strokes.Count == 0) return;
            Remember();
            Strokes.Clear();
            Repaint();
            Changed?.Invoke();
        }

        /// These strokes on a fresh pad, with no history.
        public void Load(List<List<Vector2>> strokes)
        {
            Reset();
            if (strokes != null) foreach (var s in strokes) Strokes.Add(new List<Vector2>(s));
            Repaint();
        }

        /// A fresh pad: no strokes, no history.
        public void Reset()
        {
            Strokes.Clear();
            _undo.Clear();
            _redo.Clear();
            _live = null;
            _erasing = false;
            Repaint();
        }

        /// The drawing in the sample frame (RuneSketch).
        public List<List<Vector2>> Sample() => RuneSketch.ToSample(Strokes, Pixels, Pixels);

        public void Repaint()
        {
            if (_px == null) return;
            for (int i = 0; i < _px.Length; i++) _px[i] = Ground;
            if (Guide != null && Guide.Count > 0)
                foreach (var s in RuneSketch.Fit(Guide, new Rect(Pixels * 0.15f, Pixels * 0.15f, Pixels * 0.7f, Pixels * 0.7f), 0f))
                    RuneSketch.Paint(_px, Pixels, Pixels, s, 1f, 2.5f, GuideInk);
            foreach (var s in Strokes) RuneSketch.Paint(_px, Pixels, Pixels, s, 1f, 3f, Ink);
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }
    }
}
