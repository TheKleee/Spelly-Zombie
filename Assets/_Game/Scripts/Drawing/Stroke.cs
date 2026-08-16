using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum StrokeState
    {
        Drawing, // pen is still down
        Open,    // completed, available as a rune or as future seal boundary
        InSeal,  // currently boundary or payload of an active seal
        Spent,   // persistent ink whose spell resolved - re-arms when the loop opens
        Burned   // consumed by a resolved spell (or discarded); ink is gone
    }

    /// One continuous drawn line: an ordered chain of DrawNodes plus a flattened
    /// 2D shape used for rune recognition. A stroke is always an open path -
    /// closure into a seal is detected by the SealDetector (a stroke whose two
    /// endpoints meet counts as a single-stroke loop).
    public class Stroke
    {
        static int _nextId;
        public int Id { get; } = _nextId++;

        public readonly List<DrawNode> Nodes = new List<DrawNode>();

        /// Node positions projected into the view plane captured at stroke start.
        /// This is "what the player visually drew" and is what recognition runs on.
        public readonly List<Vector2> RawShape = new List<Vector2>();

        public Vector3 BasisRight; // camera right at stroke start
        public Vector3 BasisUp;    // camera up at stroke start
        public Transform Surface;  // the collider this stroke lives on - one stroke, one surface

        /// Who drew this ink (player or zombie GameObject id). A seal belongs to
        /// the owner of the most recently drawn stroke in its loop.
        public int OwnerId;

        /// Time.time when ink last flowed into this stroke (drives seal ownership).
        public float LastInkTime;

        public StrokeState State = StrokeState.Drawing;

        // (the seal-stamped Rune/RuneScore fields are DELETED - write-only;
        // seals read glyphs, stroke logic reads DeclaredRune)

        /// The rune this stroke IS, declared at draw time (stamped by the
        /// player's choice or a zombie scribe). Seals trust this outright -
        /// no recognition, no guessing. None = plain ink.
        public RuneType DeclaredRune = RuneType.None;

        /// Cross-machine stroke identity: (OwnerId, NetId) names the same ink on
        /// every machine; 0 = never replicated. Split pieces inherit it (netcode §0).
        public int NetId;

        /// Every node sits on a character/weapon - ink survives spell resolution.
        public bool Persistent { get; private set; }

        /// Spans more than one surface, so the middle can move while the
        /// endpoints stand still - its line must refresh every frame.
        public bool MultiSurface { get; private set; }

        /// Leftover piece of a closing gesture (lead-in before the loop, hook
        /// tail after it, outer pieces of split ink). Still visible ink, but
        /// NEVER read as rune content inside a seal - a closing hook is not a
        /// glyph stroke, and reading it caused phantom-rune misfires.
        public bool SealResidue;

        /// When this ink appeared in the world - the evaporation clock
        /// (loose world ink thins out and vanishes after a minute; body ink
        /// and seal ink are exempt - see DrawingWorld's sweep).
        public float BornAt;

        LineRenderer _line;
        GameObject _lineGo;
        readonly List<LineRenderer> _extra = new List<LineRenderer>(); // runs after visual breaks
        bool _loop;
        Color _color = InkColor;
        bool _dirty = true; // set when nodes are added/destroyed
        // sampled node positions from the last line rebuild - the held-still
        // early-out below (first/last alone missed multi-limb middles, so
        // multi-surface body ink rebuilt EVERY frame even on a frozen body)
        readonly List<Vector3> _stillSnap = new List<Vector3>();
        readonly List<Vector3> _pts = new List<Vector3>();
        readonly List<int> _runStarts = new List<int>();

        public DrawNode First => Nodes.Count > 0 ? Nodes[0] : null;
        public DrawNode Last => Nodes.Count > 0 ? Nodes[Nodes.Count - 1] : null;
        public bool Alive => State != StrokeState.Burned;

        // deep luminous navy - reads near-black in lit scenes, but the unlit
        // line material lets it GLOW faintly in a pitch-dark cave, so ink
        // path-marks on walls work as wayfinding
        public static readonly Color InkColor = new Color(0.16f, 0.19f, 0.33f);

        /// WHAT COLOUR THIS HAND DRAWS IN. "when corrupted part of
        /// the wand is drawing the ink should be green not black", and "wands get
        /// corrupted or cured from the tip downwards".
        ///
        /// The tip is what writing consumes, and the green lives at the tip, so a
        /// corrupted wizard lays down green ink until they have burned through it.
        /// That makes corruption visible in the WORLD, not just on your hand:
        /// your friends start finding enemy-coloured drawings and cannot tell
        /// whether that was an acolyte or you.
        ///
        /// Today only the fully-corrupt case exists, which is an acolyte. When
        /// souls land, this becomes "is the drawing end of this wand green yet",
        /// and every call site here already asks the right question.
        public static Color InkColorFor(int owner) =>
            Sides.IsAcolyte(owner) ? DrawingConfig.CorruptInkColor : InkColor;
        public static readonly Color SealColor = new Color(1f, 0.80f, 0.25f);
        public static readonly Color RuneColor = new Color(0.30f, 0.90f, 1f);
        public static readonly Color FizzleColor = new Color(0.5f, 0.5f, 0.5f);
        public static readonly Color SpentColor = new Color(0.55f, 0.45f, 0.22f);

        // running length along the stroke, index-aligned with Nodes;
        // only trusted while drawing (all nodes alive, none erased yet)
        readonly List<float> _runningLength = new List<float>();

        public void AddNode(DrawNode node)
        {
            LastInkTime = Time.time;
            if (Nodes.Count == 0)
                _runningLength.Add(0f);
            else
                _runningLength.Add(_runningLength[_runningLength.Count - 1] +
                    Vector3.Distance(Nodes[Nodes.Count - 1].transform.position, node.transform.position));
            Nodes.Add(node);
            _dirty = true;
        }

        /// Path length between two node indices (drawing-time only).
        public float LengthBetween(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || toIndex >= _runningLength.Count || fromIndex > toIndex) return 0f;
            return _runningLength[toIndex] - _runningLength[fromIndex];
        }

        /// Lasso split: remove and return the nodes before `index` (the tail drawn
        /// before the crossing point); this stroke keeps the loop portion.
        public List<DrawNode> DetachNodesBefore(int index)
        {
            var removed = Nodes.GetRange(0, index);
            Nodes.RemoveRange(0, index);
            _runningLength.Clear();
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (i == 0) _runningLength.Add(0f);
                else _runningLength.Add(_runningLength[i - 1] +
                    Vector3.Distance(Nodes[i - 1].transform.position, Nodes[i].transform.position));
            }
            _dirty = true;
            return removed;
        }

        /// Render the line as a closed ring (used while this stroke is a whole seal).
        public void SetLoop(bool on)
        {
            _loop = on;
            _dirty = true; // the ring only closes visually when nothing is stretched apart
        }

        public void MarkDirty() => _dirty = true;

        /// Call once the node list is final (stroke completed or closed into a seal).
        /// PERSISTENCE IS A MAJORITY VOTE (body drawings "should never
        /// expire completely") — the old all-nodes rule let ONE stray node
        /// (pen clipping past a limb onto the floor for a frame) demote a
        /// whole body drawing to world ink, which the next cast then BURNED.
        public void CachePersistence()
        {
            MultiSurface = false;
            int total = 0, onBody = 0;
            Transform firstParent = null;
            foreach (var n in Nodes)
            {
                if (n == null) continue;
                total++;
                if (n.OnPersistentSurface) onBody++;
                var parent = n.transform.parent;
                if (firstParent == null) firstParent = parent;
                else if (parent != firstParent) MultiSurface = true;
            }
            Persistent = total > 0 && onBody * 2 > total;
        }

        /// Ink on a holstered surface (weapon stowed in third person, held in an
        /// unselected slot, ...) does NOT exist right now: it renders nothing,
        /// joins no seals, can't be erased, and zombies can't see it. It returns
        /// exactly as it was when the surface reactivates. MP-friendly by
        /// construction: derived from hierarchy visibility, so once weapon stow
        /// state replicates, ink visibility follows with zero extra messages.
        public bool Hidden()
        {
            var f = First;
            return f != null && !f.gameObject.activeInHierarchy;
        }

        /// True when the ink still exists - i.e. no node has been erased/destroyed.
        /// NOT a distance test: leftover pieces from a split, fast strokes with
        /// wide node spacing, and ink stretched across separating surfaces are all
        /// still valid ink that may join seals (active-seal breaking is handled
        /// separately by the seal's rest-gap check). A hole (erased node) is the
        /// only thing that makes ink dead; RepairErasedStrokes splits on it.
        public bool ChainIntact()
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i] == null) return false; // erased/destroyed ink
            }
            return Nodes.Count > 0;
        }

        /// Walks every node, so SealDetector calls it once per eligible stroke
        /// per tick AND again per candidate loop. It reads each node's position
        /// ONCE by carrying the previous one, rather than re-reading node i-1
        /// on the next step: half the transform calls and half the Unity null
        /// checks, same number to the last decimal.
        ///
        /// Not cached on purpose. Erasing destroys nodes from outside the
        /// stroke without telling it (DrawingWorld.EraseAlong), which is why
        /// _runningLength is documented as drawing-time only. A stale perimeter
        /// would resize seals, so this stays exact.
        public float PathLength()
        {
            float len = 0f;
            Vector3 prev = default;
            bool has = false;
            for (int i = 0; i < Nodes.Count; i++)
            {
                var n = Nodes[i];
                if (n == null) { has = false; continue; } // erased hole: no segment across it
                Vector3 p = n.transform.position;
                if (has) len += Vector3.Distance(prev, p);
                prev = p;
                has = true;
            }
            return len;
        }

        public Vector3 Centroid()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var n in Nodes)
            {
                if (n == null) continue;
                sum += n.transform.position;
                count++;
            }
            return count > 0 ? sum / count : Vector3.zero;
        }

        /// Flatten node positions into the stroke's start-of-draw view plane.
        /// The frame rides the surface (SurfaceDelta) so ink on moving carriers
        /// keeps the same 2D shape no matter how the carrier has turned since.
        public void ComputeRawShape()
        {
            RawShape.Clear();
            if (First == null) return;
            Vector3 origin = First.transform.position;
            var delta = First.SurfaceDelta;
            Vector3 right = delta * BasisRight;
            Vector3 up = delta * BasisUp;
            foreach (var n in Nodes)
            {
                if (n == null) continue;
                Vector3 d = n.transform.position - origin;
                RawShape.Add(new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)));
            }
        }

        public void EnsureLine(Material mat)
        {
            if (_line != null) return;
            _lineGo = new GameObject($"StrokeLine_{Id}");
            _line = _lineGo.AddComponent<LineRenderer>();
            _line.sharedMaterial = mat;
            _line.widthMultiplier = DrawingConfig.InkWidth;
            _line.useWorldSpace = true;
            _line.numCapVertices = 2;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(InkColorFor(OwnerId));   // green if this hand is corrupt
        }

        /// What colour this ink is showing right now - so a temporary highlight
        /// (the grimoire tinting the drawing you're aiming at) can put back
        /// exactly what it found instead of guessing at InkColor.
        public Color Color => _color;

        public void SetColor(Color c)
        {
            _color = c;
            if (_line != null)
            {
                _line.startColor = c;
                _line.endColor = c;
            }
            foreach (var l in _extra)
                if (l != null)
                {
                    l.startColor = c;
                    l.endColor = c;
                }
        }

        /// Nodes ride moving surfaces, so the line refreshes from live positions -
        /// but ink that held still (all world ink, and body ink whenever the
        /// pose is frozen: the easel, a held emote) skips the rebuild. The
        /// check samples up to seven nodes so a multi-limb middle is watched
        /// too - first/last alone let a middle bone move a line unseen, and
        /// guarding only single-surface ink made body strokes rebuild EVERY
        /// frame regardless .
        public void UpdateLine()
        {
            if (_line == null) return;

            // the line GO lives in world space, not under the surface - sync its
            // visibility by hand or stowed-weapon ink floats in mid-air
            bool hidden = Hidden();
            if (_lineGo != null && _lineGo.activeSelf == hidden) _lineGo.SetActive(!hidden);
            if (hidden) return;

            if (!_dirty && State != StrokeState.Drawing && SamplesHeldStill())
                return; // nothing moved - keep last frame's line
            SnapSamples();
            _dirty = false;

            // ---- split the polyline into visual RUNS: a segment that has
            // STRETCHED far past its drawn length (ink riding two separating
            // bones) is simply not rendered. The ink IS its nodes - runes and
            // seals compute from live positions ; the line is
            // cosmetics and must never rubber-band across the body.
            _pts.Clear();
            _runStarts.Clear();
            _runStarts.Add(0);
            int lastIdx = -1;
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i] == null) continue;
                Vector3 pos = Nodes[i].transform.position;
                if (lastIdx >= 0)
                {
                    float drawn = SegmentDrawnLength(lastIdx, i);
                    float breakLen = Mathf.Max(drawn * 2.5f, DrawingConfig.NodeSpacing * 3f);
                    if ((pos - _pts[_pts.Count - 1]).sqrMagnitude > breakLen * breakLen)
                        _runStarts.Add(_pts.Count); // stretched - start a new piece
                }
                _pts.Add(pos);
                lastIdx = i;
            }

            int runCount = _runStarts.Count;
            _line.loop = _loop && runCount == 1; // a torn ring doesn't close
            for (int r = 0; r < runCount; r++)
            {
                int start = _runStarts[r];
                int end = r + 1 < runCount ? _runStarts[r + 1] : _pts.Count;
                FillRun(r == 0 ? _line : ExtraLine(r - 1), start, end);
            }
            for (int r = runCount - 1; r < _extra.Count; r++) // park unused pieces
                if (r >= 0 && _extra[r] != null) _extra[r].positionCount = 0;
        }

        /// Half a millimetre: a line lagging its skin by less than ink width
        /// is invisible, while any real motion (sway, sculpt, pose) clears it.
        const float StillEps2 = 0.0005f * 0.0005f;

        int SampleStep() => Mathf.Max(1, Nodes.Count / 6);

        bool SamplesHeldStill()
        {
            int step = SampleStep();
            int k = 0;
            for (int i = 0; i < Nodes.Count; i += step)
            {
                if (k >= _stillSnap.Count) return false;
                var n = Nodes[i];
                Vector3 p = n != null ? n.transform.position : Vector3.zero;
                if ((p - _stillSnap[k++]).sqrMagnitude > StillEps2) return false;
            }
            if (k >= _stillSnap.Count) return false;
            var l = Last;
            Vector3 lp = l != null ? l.transform.position : Vector3.zero;
            if ((lp - _stillSnap[k++]).sqrMagnitude > StillEps2) return false;
            return k == _stillSnap.Count;
        }

        void SnapSamples()
        {
            _stillSnap.Clear();
            int step = SampleStep();
            for (int i = 0; i < Nodes.Count; i += step)
            {
                var n = Nodes[i];
                _stillSnap.Add(n != null ? n.transform.position : Vector3.zero);
            }
            var l = Last;
            _stillSnap.Add(l != null ? l.transform.position : Vector3.zero);
        }

        /// Drawing-time length of the segment between two node indices; falls
        /// back to the standard spacing when the length table is stale.
        float SegmentDrawnLength(int a, int b)
        {
            if (_runningLength.Count == Nodes.Count && a >= 0 && b < _runningLength.Count)
                return _runningLength[b] - _runningLength[a];
            return DrawingConfig.NodeSpacing;
        }

        void FillRun(LineRenderer lr, int start, int end)
        {
            int count = end - start;
            if (count == 1)
            {
                // a lone node still shows as an ink DOT, never vanishes
                Vector3 a = _pts[start];
                lr.positionCount = 2;
                lr.SetPosition(0, a);
                lr.SetPosition(1, a + Vector3.up * (DrawingConfig.InkWidth * 0.35f));
                return;
            }
            lr.positionCount = count;
            for (int i = 0; i < count; i++)
                lr.SetPosition(i, _pts[start + i]);
        }

        LineRenderer ExtraLine(int idx)
        {
            while (_extra.Count <= idx)
            {
                var go = new GameObject($"StrokeLine_{Id}_part{_extra.Count + 1}");
                go.transform.SetParent(_lineGo.transform, false); // hides/dies with the stroke
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _line.sharedMaterial;
                lr.widthMultiplier = _line.widthMultiplier;
                lr.useWorldSpace = true;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.startColor = _color;
                lr.endColor = _color;
                _extra.Add(lr);
            }
            return _extra[idx];
        }

        public bool HasDestroyedNodes()
        {
            foreach (var n in Nodes)
                if (n == null) return true;
            return false;
        }

        /// Evaporation: thin the drawn line toward nothing (k = 1 full … 0
        /// gone). Purely visual - Burn() does the actual removal.
        public void SetEvaporation(float k)
        {
            // _line + _extra already hold every renderer - no hierarchy walk
            // (no-GetComponentsInChildren law; this ran once/sec per fading stroke)
            float w = DrawingConfig.InkWidth * Mathf.Clamp01(k);
            if (_line != null) _line.widthMultiplier = w;
            foreach (var lr in _extra)
                if (lr != null) lr.widthMultiplier = w;
        }

        /// Destroy all ink belonging to this stroke.
        public void Burn()
        {
            State = StrokeState.Burned;
            foreach (var n in Nodes)
                if (n != null) Object.Destroy(n.gameObject);
            if (_lineGo != null) Object.Destroy(_lineGo); // parts are children - they go too
            _line = null;
            _lineGo = null;
            _extra.Clear();
        }

        /// Kill this stroke WITHOUT destroying its nodes - used when erasing
        /// splits it and the surviving nodes get adopted by new strokes.
        public void Retire()
        {
            State = StrokeState.Burned;
            Nodes.Clear();
            _runningLength.Clear();
            if (_lineGo != null) Object.Destroy(_lineGo);
            _line = null;
            _lineGo = null;
            _extra.Clear();
        }
    }
}
