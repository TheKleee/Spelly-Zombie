using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ A GRIMOIRE PAGE TO PAINT ON, the game's twin of the Rune Crafter's
    /// page painter: the left button lays black ink, the right button paints
    /// paper, and with the stamp on a click sets the rune's glyph down at the
    /// size and turn chosen. Strokes lie over the page they started from and
    /// undo one by one.
    public class PagePainter : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IUndoCanvas
    {
        public static readonly Color32 Paper = new Color32(245, 235, 204, 255);
        static readonly Color32 Ink = new Color32(0, 0, 0, 255);

        public int Width { get; private set; }
        public int Height { get; private set; }
        /// Brush width in page pixels.
        public float Brush = 6f;
        public bool Stamping;
        /// The stamp's height, as a share of the page's.
        public float StampSize = 0.45f;
        public float StampTurn;
        /// What a stamp sets down, in the sample frame.
        public IReadOnlyList<IReadOnlyList<Vector2>> Glyph;
        /// A stroke ended, a stamp landed, or strokes were undone, redone or cleared.
        public System.Action Changed;
        /// The page as it stands, rows bottom-up.
        public Color32[] Pixels => _px;

        struct Stroke { public List<Vector2> Points; public float Radius; public Color32 Ink; }

        RawImage _img;
        Texture2D _tex;
        Color32[] _base, _px;
        bool _dirty, _drawing;
        readonly List<Stroke> _strokes = new List<Stroke>();
        readonly List<List<Stroke>> _undo = new List<List<Stroke>>(), _redo = new List<List<Stroke>>();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public static PagePainter Create(RectTransform parent, float width, Color32[] page, int w, int h)
        {
            var go = new GameObject("Page", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<PagePainter>();
            p._img = go.GetComponent<RawImage>();
            p.Width = w;
            p.Height = h;
            p._base = page;
            p._px = (Color32[])page.Clone();
            p._tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            p._img.texture = p._tex;
            UIKit.Row(p._img, width, width * h / (float)w);
            p._dirty = true;
            return p;
        }

        /// A page as pixels, whether or not its texture can be read directly.
        public static Color32[] Read(Texture2D tex)
        {
            if (tex.isReadable)
            {
                try { return tex.GetPixels32(); }
                catch (UnityException) { } // compressed: read it back from the card below
            }
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply(false);
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            Destroy(copy);
            return px;
        }

        /// Blank paper the size of his pages.
        public static Color32[] BlankPage(int w, int h)
        {
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = Paper;
            return px;
        }

        void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }

        void LateUpdate()
        {
            if (!_dirty || _tex == null) return;
            _dirty = false;
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        bool ToPage(PointerEventData e, out Vector2 p)
        {
            p = default;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var lp))
                return false;
            var r = rt.rect;
            p = new Vector2((lp.x - r.xMin) / Mathf.Max(1f, r.width) * Width,
                            (r.yMax - lp.y) / Mathf.Max(1f, r.height) * Height);
            p.x = Mathf.Clamp(p.x, 0f, Width);
            p.y = Mathf.Clamp(p.y, 0f, Height);
            return true;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!ToPage(e, out var p)) return;
            bool left = e.button == PointerEventData.InputButton.Left;
            if (!left && e.button != PointerEventData.InputButton.Right) return;
            if (left && Stamping)
            {
                if (Glyph == null) return;
                Remember();
                float size = Height * StampSize;
                var box = new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size);
                foreach (var s in RuneSketch.Fit(Glyph, box, StampTurn))
                    _strokes.Add(new Stroke { Points = s, Radius = Brush * 0.5f, Ink = Ink });
                Bake();
                Changed?.Invoke();
                return;
            }
            Remember();
            var stroke = new Stroke { Points = new List<Vector2> { p }, Radius = Brush * 0.5f, Ink = left ? Ink : Paper };
            _strokes.Add(stroke);
            RuneSketch.Paint(_px, Width, Height, stroke.Points, 1f, stroke.Radius, stroke.Ink);
            _dirty = true;
            _drawing = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_drawing || _strokes.Count == 0 || !ToPage(e, out var p)) return;
            var s = _strokes[_strokes.Count - 1];
            var last = s.Points[s.Points.Count - 1];
            if ((p - last).sqrMagnitude < 2.25f) return;
            s.Points.Add(p);
            RuneSketch.Paint(_px, Width, Height, new[] { last, p }, 1f, s.Radius, s.Ink);
            _dirty = true;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_drawing) return;
            _drawing = false;
            Changed?.Invoke();
        }

        void Remember()
        {
            _undo.Add(new List<Stroke>(_strokes));
            if (_undo.Count > 60) _undo.RemoveAt(0);
            _redo.Clear();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Add(new List<Stroke>(_strokes));
            Put(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Add(new List<Stroke>(_strokes));
            Put(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
        }

        /// Every stroke of this sitting off the page.
        public void Clear()
        {
            if (_strokes.Count == 0) return;
            Remember();
            Put(new List<Stroke>());
        }

        void Put(List<Stroke> strokes)
        {
            _strokes.Clear();
            _strokes.AddRange(strokes);
            Bake();
            Changed?.Invoke();
        }

        void Bake()
        {
            _px = (Color32[])_base.Clone();
            foreach (var s in _strokes) RuneSketch.Paint(_px, Width, Height, s.Points, 1f, s.Radius, s.Ink);
            _dirty = true;
        }
    }
}
