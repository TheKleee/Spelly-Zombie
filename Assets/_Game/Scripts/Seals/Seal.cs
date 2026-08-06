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
    ///  - EXPIRES when duration (1s per side, cap 10s — circle counts as 10) runs out.
    ///    Environment ink is consumed; ink on characters/weapons survives as
    ///    "spent" and re-arms once the loop physically opens — a body seal fires
    ///    again every time the emote/pose closes it.
    public class Seal
    {
        static int _nextId;
        public int Id { get; } = _nextId++;

        public readonly List<SealDetector.LoopEntry> Boundary;
        public readonly List<Stroke> Payload = new List<Stroke>();

        /// The runes read out of the payload at the moment of closing — this is
        /// what the spell system consumes. Recognition happens HERE, never before.
        public readonly List<RuneGlyph> Runes = new List<RuneGlyph>();

        readonly List<DrawNode> _loopNodes;
        /// Ring-ordered boundary nodes — NetSync ships their positions to clients (netcode §2).
        public IReadOnlyList<DrawNode> LoopNodes => _loopNodes;
        readonly List<Vector2> _polygon2D;
        float[] _restGaps; // distance between each adjacent loop-node pair at activation

        public Vector3 PlaneOrigin { get; private set; }
        public Vector3 PlaneNormal { get; private set; }
        Vector3 _u, _v;

        /// The active effect this seal is driving (null until spell resolution runs).
        public Spell Spell { get; private set; }
        public void AttachSpell(Spell s) => Spell = s;

        public bool IsCircle { get; private set; }
        public int Edges { get; private set; }
        public float Duration { get; private set; }
        public float Remaining { get; private set; }
        public float Area { get; private set; }

        /// Whoever drew on the loop LAST completed it and owns the cast — the
        /// runes inside are read against THIS owner's grimoire. Close a zombie's
        /// half-drawn seal yourself and it casts with YOUR cards (and vice versa:
        /// a zombie circling your runes casts with the zombie's).
        public int OwnerId { get; private set; }

        public Seal(List<SealDetector.LoopEntry> boundary)
        {
            Boundary = boundary;
            _loopNodes = SealDetector.BuildLoopNodes(boundary);

            float newest = float.MinValue;
            foreach (var e in boundary)
                if (e.Stroke.LastInkTime > newest) { newest = e.Stroke.LastInkTime; OwnerId = e.Stroke.OwnerId; }

            // rest gaps: a seal breaks when ink is PULLED APART from how it was
            // drawn, not against a fixed distance — so a fast-drawn stroke with
            // wide node spacing is valid and only breaks if a gap actually grows.
            _restGaps = new float[_loopNodes.Count];
            for (int i = 0; i < _loopNodes.Count; i++)
            {
                var a = _loopNodes[i];
                var b = _loopNodes[(i + 1) % _loopNodes.Count];
                _restGaps[i] = (a != null && b != null)
                    ? Vector3.Distance(a.transform.position, b.transform.position)
                    : 0f;
            }

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
            Duration = Mathf.Min(DrawingConfig.SealMaxSeconds,
                Edges * DrawingConfig.DurationPerEdge);
            Remaining = Duration;
            Area = GeometryUtil.PolygonArea(_polygon2D);

            foreach (var e in Boundary)
            {
                e.Stroke.State = StrokeState.InSeal;
                e.Stroke.SetColor(Stroke.SealColor);
            }
            // a single-stroke seal renders as a closed ring — no floating gap
            if (Boundary.Count == 1)
                Boundary[0].Stroke.SetLoop(true);
        }

        /// Everything inside the ring at the moment of sealing participates.
        /// This is where runes come into existence: the enclosed strokes are
        /// clustered by pure spatial proximity, and each cluster is recognized as
        /// one rune. Ink drawn on top of itself clusters into mush and fizzles.
        static float _nextHandLearn; // silent handwriting-learning throttle

        public void CapturePayload(IReadOnlyList<Stroke> allStrokes)
        {
            // 1) gather enclosed, non-boundary ink
            var enclosed = new List<Stroke>();
            foreach (var s in allStrokes)
            {
                if (!Eligible(s)) continue;
                Vector2 c = GeometryUtil.ProjectPoint(s.Centroid(), PlaneOrigin, _u, _v);
                if (!GeometryUtil.PointInPolygon(c, _polygon2D)) continue;
                enclosed.Add(s);
            }

            // seal size, for scaling rune power by how big the rune is drawn
            float sealSize = Mathf.Max(0.01f, SealDiagonal());

            // 2) segment the enclosed ink into runes BY RECOGNITION — stacked/nested
            //    distinct runes stay separate, multi-line runes group as one
            foreach (var glyph in RuneGlyph.Segment(enclosed, OwnerId))
            {
                // strength = match quality × size. RECOGNIZED means MEANINGFUL:
                // match floors at 0.45 once past the gate (a readable rune is a
                // working rune), and size SATURATES at normal proportions — a
                // rune ~1/3 of its seal is correct drawing, not a weak spell.
                // (The old linear formulas gave "str 0.10": runes lit up but
                // their effects were homeopathic.)
                float match = Mathf.Lerp(0.45f, 1f,
                    Mathf.InverseLerp(DrawingConfig.MinRuneScore, DrawingConfig.GoodRuneScore, glyph.Score));
                float glyphSize = glyph.WorldBounds().size.magnitude;
                glyph.SizeRatio = Mathf.Clamp01(glyphSize / sealSize);
                float sizePower = Mathf.Lerp(DrawingConfig.MinSizePower, 1f, Mathf.Clamp01(glyph.SizeRatio * 3f));
                // (the WRITING LEVEL never touches Strength — Marko's rule:
                // "level doesn't improve the power of the rune or seal in any
                // way, shape or form"; it's purely the recognition meter and
                // only grimoire CORRECTIONS move it)
                glyph.Strength = glyph.Rune != RuneType.None ? Mathf.Clamp01(match) * sizePower : 0f;
                Runes.Add(glyph);

                // INVISIBLE PROGRESSION (Marko: "as you play your character
                // becomes better at casting that rune naturally"): a CLEAN
                // cast of your own quietly joins your handwriting pool, so
                // recognition converges on how you actually draw. Throttled,
                // sloppy-but-accepted casts never teach — and neither do
                // DECLARED/stamped casts (their perfect score is a stamp,
                // not a reading; re-posing a body rune must not flood the
                // pool with the same drawing).
                if (glyph.Rune != RuneType.None && OwnerId == Grimoire.LocalPlayerId
                    && (glyph.Members.Count == 0 || glyph.Members[0].DeclaredRune == RuneType.None)
                    && glyph.Score >= DrawingConfig.HandLearnMinScore
                    && Time.time >= _nextHandLearn)
                {
                    _nextHandLearn = Time.time + DrawingConfig.HandLearnCooldown;
                    // QUIET: in-memory only — the file write is deferred so
                    // the cast frame never pays for the lesson
                    RuneLibrary.AddSample(glyph.Rune, RuneGlyph.RawStrokesOf(glyph.Members), quiet: true);
                }

                foreach (var member in glyph.Members)
                {
                    Payload.Add(member);
                    member.State = StrokeState.InSeal;
                    // BODY INK IS MARKED FOREVER (Marko: "first effect should
                    // be final - drawings on body are permanent thus special,
                    // runes are already marked as to what they will be"): the
                    // first successful reading STAMPS the stroke; every
                    // re-cast from re-posing trusts the stamp. No re-rolls.
                    if (member.Persistent && glyph.Rune != RuneType.None
                        && member.DeclaredRune == RuneType.None)
                        member.DeclaredRune = glyph.Rune;
                    member.SetColor(glyph.Rune != RuneType.None ? Stroke.RuneColor : Stroke.FizzleColor);
                }
            }

            bool Eligible(Stroke s)
            {
                if (s == null || s.State != StrokeState.Open || !s.Alive || s.Nodes.Count < 3) return false;
                if (s.SealResidue) return false; // closing-gesture leftovers are not rune content
                if (!s.ChainIntact()) return false;
                foreach (var e in Boundary)
                    if (e.Stroke == s) return false;
                return true;
            }
        }

        /// Bounding diagonal of the boundary — the seal's characteristic size.
        float SealDiagonal()
        {
            var b = new Bounds();
            bool any = false;
            foreach (var n in _loopNodes)
            {
                if (n == null) continue;
                if (!any) { b = new Bounds(n.transform.position, Vector3.zero); any = true; }
                else b.Encapsulate(n.transform.position);
            }
            return b.size.magnitude;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(IsCircle ? "circle" : $"{Edges} edges");
            sb.Append($" → {Duration:0.0}s, area {Area:0.00}m²");
            if (Runes.Count == 0)
            {
                sb.Append(", no runes (empty seal)");
                return sb.ToString();
            }

            sb.Append(", runes: ");
            for (int i = 0; i < Runes.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var g = Runes[i];
                string strokes = g.Members.Count > 1 ? $"×{g.Members.Count}" : "";
                sb.Append(g.Rune == RuneType.None
                    ? $"fizzle({g.Score:0.00})"
                    : $"{g.Rune}{strokes} str {g.Strength:0.00} (match {g.Score:0.00}, size {g.SizeRatio:0.00})");
            }
            return sb.ToString();
        }

        /// Advance the seal. Returns false when the seal ended this tick.
        public bool Tick(float dt)
        {
            // integrity: the ring must stay closed. A gap breaks only if it GROWS
            // past its drawn length by more than BreakDistance — so drawing speed
            // (wide node spacing) never falsely opens a seal, while ink being
            // erased or pulled apart still does.
            for (int i = 0; i < _loopNodes.Count; i++)
            {
                var a = _loopNodes[i];
                var b = _loopNodes[(i + 1) % _loopNodes.Count];
                if (a == null || b == null) { Break("ink destroyed"); return false; }
                float gap = Vector3.Distance(a.transform.position, b.transform.position);
                if (gap > _restGaps[i] + DrawingConfig.BreakDistance)
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

            return true;
        }

        /// The ring opened before the spell finished. Spell cancels, ink survives —
        /// if the loop closes again (objects pushed back together) it re-activates.
        void Break(string reason)
        {
            foreach (var e in Boundary) Release(e.Stroke);
            foreach (var s in Payload) Release(s);
            DrawingWorld.Instance?.OnSealEnded(this, $"Seal #{Id} broken ({reason}) with {Remaining:0.0}s left", false);
        }

        static void Release(Stroke s)
        {
            if (!s.Alive) return;
            s.State = StrokeState.Open;
            s.SetColor(Stroke.InkColor);
            s.SetLoop(false); // broken seals show their gap again
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

            if (spent.Count > 0)
                DrawingWorld.Instance?.RegisterSpentGroup(spent, pairs, new List<SealDetector.LoopEntry>(Boundary));

            string fate = spent.Count > 0
                ? burned > 0
                    ? $"{spent.Count} stroke(s) spent (open the loop to re-arm), {burned} consumed"
                    : $"{spent.Count} stroke(s) spent (open the loop to re-arm)"
                : "ink consumed";
            DrawingWorld.Instance?.OnSealEnded(this, $"Seal #{Id} resolved after {Duration:0.0}s: {fate}", true);
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

        // NO seal light — it washed out every rune effect near it. The gold
        // ink color IS the "active seal" indicator; only the Light rune
        // actually produces light now. (The glow machinery was removed.)
    }
}
