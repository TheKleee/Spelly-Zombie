using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SpellyZombie
{
    /// An activated seal: a closed ring of DrawNodes plus every rune stroke that was
    /// inside it at the moment of closing.
    ///
    /// Lifecycle per design:
    ///  - activates the instant the loop closes (drawn closed OR objects/limbs
    ///    drifting together)
    ///  - BREAKS immediately if the ring opens: any node destroyed, or any adjacent
    ///    pair pulled beyond BreakDistance. Broken ink survives and can re-close.
    ///  - EXPIRES when duration (0.1s per edge, circle = 360 edges) runs out.
    ///    Environment ink is consumed; ink on characters/weapons survives as
    ///    "spent" and re-arms once the loop physically opens — a body seal fires
    ///    again every time the emote/pose closes it.
    public class Seal
    {
        static int _nextId;
        public int Id { get; } = _nextId++;

        public readonly List<SealDetector.LoopEntry> Boundary;
        public readonly List<Stroke> Payload = new List<Stroke>();

        readonly List<DrawNode> _loopNodes;
        readonly List<Vector2> _polygon2D;

        public Vector3 PlaneOrigin { get; private set; }
        public Vector3 PlaneNormal { get; private set; }
        Vector3 _u, _v;

        public bool IsCircle { get; private set; }
        public int Edges { get; private set; }
        public float Duration { get; private set; }
        public float Remaining { get; private set; }
        public float Area { get; private set; }

        Light _glow;
        GameObject _glowGo;

        public Seal(List<SealDetector.LoopEntry> boundary)
        {
            Boundary = boundary;
            _loopNodes = SealDetector.BuildLoopNodes(boundary);

            // ---- plane fit ----
            var worldPts = new List<Vector3>(_loopNodes.Count);
            Vector3 centroid = Vector3.zero;
            Vector3 avgSurfaceNormal = Vector3.zero;
            foreach (var n in _loopNodes)
            {
                worldPts.Add(n.transform.position);
                centroid += n.transform.position;
                avgSurfaceNormal += n.SurfaceNormal;
            }
            centroid /= worldPts.Count;
            PlaneOrigin = centroid;

            Vector3 normal = GeometryUtil.NewellNormal(worldPts);
            if (normal.sqrMagnitude < 1e-8f)
                normal = avgSurfaceNormal.sqrMagnitude > 1e-8f ? avgSurfaceNormal : Vector3.up;
            normal.Normalize();
            // orient the plane normal with the ink side of the surfaces
            if (avgSurfaceNormal.sqrMagnitude > 1e-8f && Vector3.Dot(normal, avgSurfaceNormal) < 0f)
                normal = -normal;
            PlaneNormal = normal;
            GeometryUtil.PlaneBasis(normal, out _u, out _v);

            _polygon2D = GeometryUtil.ProjectToPlane(worldPts, PlaneOrigin, _u, _v);

            // ---- shape -> edges -> duration ----
            float radialCv = GeometryUtil.RadialVariation(_polygon2D);
            int corners = GeometryUtil.ClosedLoopCorners(_polygon2D);
            IsCircle = radialCv <= DrawingConfig.CircleMaxVariance && corners >= DrawingConfig.CircleMinCorners;
            Edges = IsCircle ? DrawingConfig.CircleEdges : corners;
            Duration = Edges * DrawingConfig.DurationPerEdge;
            Remaining = Duration;
            Area = GeometryUtil.PolygonArea(_polygon2D);

            foreach (var e in Boundary)
            {
                e.Stroke.State = StrokeState.InSeal;
                e.Stroke.SetColor(Stroke.SealColor);
            }
        }

        /// Everything inside the ring at the moment of sealing participates.
        public void CapturePayload(IReadOnlyList<Stroke> allStrokes)
        {
            foreach (var s in allStrokes)
            {
                if (s.State != StrokeState.Open || !s.Alive || s.Nodes.Count < 3) continue;
                if (!s.ChainIntact()) continue;
                bool isBoundary = false;
                foreach (var e in Boundary)
                    if (e.Stroke == s) { isBoundary = true; break; }
                if (isBoundary) continue;

                Vector2 c = GeometryUtil.ProjectPoint(s.Centroid(), PlaneOrigin, _u, _v);
                if (!GeometryUtil.PointInPolygon(c, _polygon2D)) continue;

                Payload.Add(s);
                s.State = StrokeState.InSeal;
                s.SetColor(s.Rune != RuneType.None ? Stroke.RuneColor : Stroke.FizzleColor);
            }

            CreateGlow();
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(IsCircle ? "circle" : $"{Edges} edges");
            sb.Append($" → {Duration:0.0}s, area {Area:0.00}m²");
            if (Payload.Count == 0)
            {
                sb.Append(", no runes (empty seal)");
            }
            else
            {
                sb.Append(", runes: ");
                for (int i = 0; i < Payload.Count; i++)
                {
                    var s = Payload[i];
                    if (i > 0) sb.Append(", ");
                    sb.Append(s.Rune == RuneType.None
                        ? $"fizzle({s.RuneScore:0.00})"
                        : $"{s.Rune}({s.RuneScore:0.00})");
                }
            }
            return sb.ToString();
        }

        /// Advance the seal. Returns false when the seal ended this tick.
        public bool Tick(float dt)
        {
            // integrity: the ring must stay closed — every consecutive pair
            // (including stroke junctions and the wrap-around) within BreakDistance
            for (int i = 0; i < _loopNodes.Count; i++)
            {
                var a = _loopNodes[i];
                var b = _loopNodes[(i + 1) % _loopNodes.Count];
                if (a == null || b == null) { Break("ink destroyed"); return false; }
                if (Vector3.Distance(a.transform.position, b.transform.position) > DrawingConfig.BreakDistance)
                {
                    Break("seal opened");
                    return false;
                }
            }

            Remaining -= dt;
            if (Remaining <= 0f)
            {
                Expire();
                return false;
            }

            if (_glow != null)
                _glow.intensity = 1.6f + Mathf.Sin(Time.time * 6f) * 0.5f;
            return true;
        }

        /// The ring opened before the spell finished. Spell cancels, ink survives —
        /// if the loop closes again (objects pushed back together) it re-activates.
        void Break(string reason)
        {
            foreach (var e in Boundary) Release(e.Stroke);
            foreach (var s in Payload) Release(s);
            DestroyGlow();
            DrawingWorld.Instance?.OnSealEnded(this, $"Seal #{Id} broken ({reason}) with {Remaining:0.0}s left");
        }

        static void Release(Stroke s)
        {
            if (!s.Alive) return;
            s.State = StrokeState.Open;
            s.SetColor(Stroke.InkColor);
        }

        /// Full duration elapsed: the spell resolved. Environment ink is consumed;
        /// persistent (character/weapon) ink goes spent and re-arms when the loop opens.
        void Expire()
        {
            // junction pairs between consecutive boundary strokes — the points where
            // the loop can physically open again. Collected before anything burns.
            var pairs = new List<(DrawNode a, DrawNode b)>();
            for (int i = 0; i < Boundary.Count; i++)
            {
                var cur = Boundary[i];
                var next = Boundary[(i + 1) % Boundary.Count];
                if (!cur.Stroke.Persistent || !next.Stroke.Persistent) continue;
                var exit = cur.Forward ? cur.Stroke.Last : cur.Stroke.First;
                var entry = next.Forward ? next.Stroke.First : next.Stroke.Last;
                if (exit != null && entry != null)
                    pairs.Add((exit, entry));
            }

            var spent = new List<Stroke>();
            int burned = 0;
            foreach (var e in Boundary) SpendOrBurn(e.Stroke, spent, ref burned);
            foreach (var s in Payload) SpendOrBurn(s, spent, ref burned);
            DestroyGlow();

            if (spent.Count > 0)
                DrawingWorld.Instance?.RegisterSpentGroup(spent, pairs);

            string fate = spent.Count > 0
                ? burned > 0
                    ? $"{spent.Count} stroke(s) spent (open the loop to re-arm), {burned} consumed"
                    : $"{spent.Count} stroke(s) spent — open the loop to re-arm"
                : "ink consumed";
            DrawingWorld.Instance?.OnSealEnded(this, $"Seal #{Id} resolved after {Duration:0.0}s — {fate}");
        }

        static void SpendOrBurn(Stroke s, List<Stroke> spent, ref int burned)
        {
            if (!s.Alive) return;
            if (s.Persistent)
            {
                s.State = StrokeState.Spent;
                s.SetColor(Stroke.SpentColor);
                spent.Add(s);
            }
            else
            {
                s.Burn();
                burned++;
            }
        }

        void CreateGlow()
        {
            _glowGo = new GameObject($"SealGlow_{Id}");
            _glowGo.transform.position = PlaneOrigin + PlaneNormal * 0.15f;
            _glow = _glowGo.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.color = new Color(1f, 0.82f, 0.35f);
            _glow.range = Mathf.Max(1.5f, Mathf.Sqrt(Area) * 2.5f);
            _glow.intensity = 1.6f;
        }

        void DestroyGlow()
        {
            if (_glowGo != null) Object.Destroy(_glowGo);
            _glow = null;
            _glowGo = null;
        }
    }
}
