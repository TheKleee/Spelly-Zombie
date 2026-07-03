using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum StrokeState
    {
        Drawing, // pen is still down
        Open,    // completed, available as a rune or as future seal boundary
        InSeal,  // currently boundary or payload of an active seal
        Spent,   // persistent ink whose spell resolved — re-arms when the loop opens
        Burned   // consumed by a resolved spell (or discarded); ink is gone
    }

    /// One continuous drawn line: an ordered chain of DrawNodes plus a flattened
    /// 2D shape used for rune recognition. A stroke is always an open path —
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

        public StrokeState State = StrokeState.Drawing;
        public RuneType Rune = RuneType.None;
        public float RuneScore;

        /// Every node sits on a character/weapon — ink survives spell resolution.
        public bool Persistent { get; private set; }

        /// Spans more than one surface, so the middle can move while the
        /// endpoints stand still — its line must refresh every frame.
        public bool MultiSurface { get; private set; }

        LineRenderer _line;
        GameObject _lineGo;
        bool _dirty = true; // set when nodes are added/destroyed
        Vector3 _lastFirstPos, _lastLastPos;

        public DrawNode First => Nodes.Count > 0 ? Nodes[0] : null;
        public DrawNode Last => Nodes.Count > 0 ? Nodes[Nodes.Count - 1] : null;
        public bool Alive => State != StrokeState.Burned;

        public static readonly Color InkColor = new Color(0.08f, 0.08f, 0.10f);
        public static readonly Color SealColor = new Color(1f, 0.80f, 0.25f);
        public static readonly Color RuneColor = new Color(0.30f, 0.90f, 1f);
        public static readonly Color FizzleColor = new Color(0.5f, 0.5f, 0.5f);
        public static readonly Color SpentColor = new Color(0.55f, 0.45f, 0.22f);

        public void AddNode(DrawNode node)
        {
            Nodes.Add(node);
            _dirty = true;
        }

        public void MarkDirty() => _dirty = true;

        /// Call once the node list is final (stroke completed or closed into a seal).
        public void CachePersistence()
        {
            Persistent = Nodes.Count > 0;
            MultiSurface = false;
            Transform firstParent = null;
            foreach (var n in Nodes)
            {
                if (n == null) continue;
                if (!n.OnPersistentSurface) Persistent = false;
                var parent = n.transform.parent;
                if (firstParent == null) firstParent = parent;
                else if (parent != firstParent) MultiSurface = true;
            }
        }

        /// True when every node still exists and no adjacent pair has been pulled apart.
        /// A stroke with a hole in it is dead ink: it can never participate in a seal.
        public bool ChainIntact()
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i] == null) return false;
                if (i > 0 && Vector3.Distance(Nodes[i - 1].transform.position, Nodes[i].transform.position) > DrawingConfig.BreakDistance)
                    return false;
            }
            return Nodes.Count > 0;
        }

        public float PathLength()
        {
            float len = 0f;
            for (int i = 1; i < Nodes.Count; i++)
            {
                if (Nodes[i - 1] == null || Nodes[i] == null) continue;
                len += Vector3.Distance(Nodes[i - 1].transform.position, Nodes[i].transform.position);
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
        public void ComputeRawShape()
        {
            RawShape.Clear();
            if (First == null) return;
            Vector3 origin = First.transform.position;
            foreach (var n in Nodes)
            {
                if (n == null) continue;
                Vector3 d = n.transform.position - origin;
                RawShape.Add(new Vector2(Vector3.Dot(d, BasisRight), Vector3.Dot(d, BasisUp)));
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
            SetColor(InkColor);
        }

        public void SetColor(Color c)
        {
            if (_line == null) return;
            _line.startColor = c;
            _line.endColor = c;
        }

        /// Nodes ride moving surfaces, so the line refreshes from live positions —
        /// but the vast majority of ink sits on static surfaces, so skip the mesh
        /// rebuild unless something actually moved or changed.
        public void UpdateLine()
        {
            if (_line == null) return;

            if (!_dirty && State != StrokeState.Drawing && !MultiSurface)
            {
                var f = First;
                var l = Last;
                if (f != null && l != null)
                {
                    Vector3 fp = f.transform.position;
                    Vector3 lp = l.transform.position;
                    if ((fp - _lastFirstPos).sqrMagnitude < 1e-8f && (lp - _lastLastPos).sqrMagnitude < 1e-8f)
                        return; // nothing moved — keep last frame's line
                    _lastFirstPos = fp;
                    _lastLastPos = lp;
                }
            }
            else
            {
                if (First != null) _lastFirstPos = First.transform.position;
                if (Last != null) _lastLastPos = Last.transform.position;
            }
            _dirty = false;

            int alive = 0;
            for (int i = 0; i < Nodes.Count; i++)
                if (Nodes[i] != null) alive++;
            _line.positionCount = alive;
            int p = 0;
            for (int i = 0; i < Nodes.Count; i++)
                if (Nodes[i] != null) _line.SetPosition(p++, Nodes[i].transform.position);
        }

        /// Destroy all ink belonging to this stroke.
        public void Burn()
        {
            State = StrokeState.Burned;
            foreach (var n in Nodes)
                if (n != null) Object.Destroy(n.gameObject);
            if (_lineGo != null) Object.Destroy(_lineGo);
            _line = null;
            _lineGo = null;
        }
    }
}
