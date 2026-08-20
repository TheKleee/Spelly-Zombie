using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// One drawn rune, possibly made of several strokes. Glyphs don't exist
    /// while drawing - only when a seal closes (or a template is recorded)
    /// are strokes clustered, by spatial proximity alone; runes drawn on top
    /// of one another merge and fizzle.
    public class RuneGlyph
    {
        /// Testing only: the compound-sigil word parse casts 2+ runes from a
        /// scribble that failed single-rune recognition. Default off.
        public static bool CompoundSigilsEnabled = false;

        public readonly List<Stroke> Members = new List<Stroke>();
        public RuneType Rune = RuneType.None;
        public float Score;      // raw match confidence 0..1
        public float SizeRatio;  // glyph size / seal size, 0..1
        public float Strength;   // final per-rune power fed to the spell system

        public Bounds WorldBounds()
        {
            var b = new Bounds();
            bool any = false;
            foreach (var m in Members)
            {
                if (m == null || !m.Alive) continue;
                foreach (var n in m.Nodes)
                {
                    if (n == null) continue;
                    if (!any) { b = new Bounds(n.transform.position, Vector3.zero); any = true; }
                    else b.Encapsulate(n.transform.position);
                }
            }
            return b;
        }

        public Vector3 Centroid()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var m in Members)
            {
                if (m == null || !m.Alive) continue;
                foreach (var n in m.Nodes)
                {
                    if (n == null) continue;
                    sum += n.transform.position;
                    count++;
                }
            }
            return count > 0 ? sum / count : Vector3.zero;
        }

        /// All member strokes flattened into ONE shared 2D frame, ready for
        /// recognition or template recording.
        public List<List<Vector2>> BuildRawStrokes() => RawStrokesOf(Members);

        /// Flatten a set of strokes into one shared 2D frame in the surface's
        /// own plane, oriented to how the drawer saw it (raw camera axes would
        /// perspective-squash floor drawings).
        public static List<List<Vector2>> RawStrokesOf(IReadOnlyList<Stroke> members)
        {
            var result = new List<List<Vector2>>();
            Stroke lead = null;
            Vector3 normal = Vector3.zero;
            foreach (var m in members)
            {
                if (m == null || !m.Alive) continue;
                if (lead == null && m.First != null) lead = m;
                foreach (var n in m.Nodes)
                    if (n != null) normal += n.SurfaceNormal;
            }
            if (lead == null) return result;
            normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;

            Vector3 origin = lead.First.transform.position;
            // "right" = the drawer's screen-right laid flat onto the surface;
            // "up" = up the wall / away from the drawer on the floor.
            // The frame rides the surface (SurfaceDelta): a carried or posed
            // surface re-reads identically wherever it now faces.
            Vector3 right = Vector3.ProjectOnPlane(
                lead.First.SurfaceDelta * lead.BasisRight, normal);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.ProjectOnPlane(Vector3.forward, normal);
            right.Normalize();
            // the handedness law: every path that writes or repaints ink must
            // build its frame with THIS line. It implies
            //     Cross(right, up) == -normal
            // the OPPOSITE sign to a raw Unity transform basis; mixing the two
            // conventions is a reflection and mirrors every glyph.
            // ZombieScribe.PlaneBasis ends with this same line - use it rather
            // than hand-rolling a basis anywhere else.
            Vector3 up = Vector3.Cross(right, normal).normalized;

            // unroll the surface: every pen step, pen-up jumps included, is
            // measured in a parallel-transported local tangent frame and laid
            // flat as one continuous unroll, so ink over slopes keeps its
            // drawn shape and strokes touching in 3D still touch in 2D. On
            // flat surfaces this telescopes to the planar projection exactly;
            // transporting node-to-node prevents mirror-flips on
            // high-curvature surfaces.
            Vector3 prevPos = default;
            Vector2 pen = default;
            Vector3 lrPrev = right;
            bool glyphFirst = true;
            foreach (var m in members)
            {
                if (m == null || !m.Alive) continue;
                var pts = new List<Vector2>();
                foreach (var n in m.Nodes)
                {
                    if (n == null) continue;
                    Vector3 ln = n.SurfaceNormal;
                    if (ln.sqrMagnitude < 1e-6f) ln = normal;
                    ln.Normalize();
                    Vector3 lr = Vector3.ProjectOnPlane(lrPrev, ln);
                    if (lr.sqrMagnitude < 1e-6f) lr = lrPrev;
                    lr.Normalize();
                    Vector3 lu = Vector3.Cross(lr, ln).normalized;

                    if (glyphFirst)
                    {
                        Vector3 d0 = n.transform.position - origin;
                        pen = new Vector2(Vector3.Dot(d0, right), Vector3.Dot(d0, up));
                        glyphFirst = false;
                    }
                    else
                    {
                        Vector3 step = n.transform.position - prevPos;
                        pen += new Vector2(Vector3.Dot(step, lr), Vector3.Dot(step, lu));
                    }
                    prevPos = n.transform.position;
                    lrPrev = lr;
                    pts.Add(pen);
                }
                if (pts.Count >= 2) result.Add(pts);
            }
            return result;
        }

        /// Group strokes into glyphs by spatial proximity - touching only,
        /// joined at `baseDist`.
        public static List<RuneGlyph> Cluster(IReadOnlyList<Stroke> strokes, float baseDist)
        {
            int n = strokes.Count;
            var parent = new int[n];
            var bounds = new Bounds[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = i;
                bounds[i] = StrokeBounds(strokes[i]);
            }

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (Find(i) == Find(j)) continue;
                    // Bounds.Expand grows the SIZE (each face moves half), so
                    // × 2 keeps every pair InkTouches could accept
                    var bi = bounds[i]; bi.Expand(baseDist * 2f);
                    if (!bi.Intersects(bounds[j])) continue;
                    if (InkTouches(strokes[i], strokes[j], baseDist))
                        parent[Find(j)] = Find(i);
                }
            }

            var byRoot = new Dictionary<int, RuneGlyph>();
            var result = new List<RuneGlyph>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!byRoot.TryGetValue(root, out var glyph))
                {
                    glyph = new RuneGlyph();
                    byRoot[root] = glyph;
                    result.Add(glyph);
                }
                glyph.Members.Add(strokes[i]);
            }
            return result;
        }

        /// Split the enclosed ink into runes. Declared strokes (zombie-scribed
        /// glyphs) are perfect matches if the seal's owner knows the rune;
        /// hand-drawn ink goes through recognition when the seal closes.
        public static List<RuneGlyph> Segment(IReadOnlyList<Stroke> strokes, int ownerId)
        {
            var result = new List<RuneGlyph>();
            var handDrawn = new List<Stroke>();
            var declared = new List<Stroke>();
            foreach (var s in strokes)
            {
                if (s.DeclaredRune != RuneType.None) declared.Add(s);
                else handDrawn.Add(s);
            }

            // declared strokes group by touch + same rune: a multi-stroke
            // declared drawing is ONE glyph (one cast), never one per pen
            // lift; two separate drawings stay two casts. Undeclared ink that
            // touches a declared drawing inherits the declaration instead of
            // going to recognition.
            while (declared.Count > 0)
            {
                var glyph = new RuneGlyph();
                glyph.Members.Add(declared[0]);
                declared.RemoveAt(0);
                var groupRune = glyph.Members[0].DeclaredRune;
                float join = DrawingConfig.RuneTouchDistance;
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int i = declared.Count - 1; i >= 0; i--)
                    {
                        var s = declared[i];
                        if (s.DeclaredRune != groupRune) continue;
                        foreach (var m in glyph.Members)
                            if (InkTouches(s, m, join))
                            {
                                glyph.Members.Add(s);
                                declared.RemoveAt(i);
                                grew = true;
                                break;
                            }
                    }
                    // undeclared ink joining the forced drawing (it can also
                    // BRIDGE two declared strokes, which is why this sweep
                    // lives inside the same grow loop)
                    for (int i = handDrawn.Count - 1; i >= 0; i--)
                    {
                        var s = handDrawn[i];
                        foreach (var m in glyph.Members)
                            if (InkTouches(s, m, join))
                            {
                                glyph.Members.Add(s);
                                handDrawn.RemoveAt(i);
                                grew = true;
                                break;
                            }
                    }
                }
                if (RuneLibrary.IsUnlocked(ownerId, glyph.Members[0].DeclaredRune))
                {
                    glyph.Rune = glyph.Members[0].DeclaredRune;
                    glyph.Score = 1f;
                }
                result.Add(glyph);
            }
            result.AddRange(SegmentByRecognition(handDrawn, ownerId));

            // compound sigils: a glyph that fizzled as a single rune might be
            // several runes drawn as one connected scribble - if it decomposes
            // into 2+ readable runes they all fire (sharing the scribble's
            // location and size)
            if (CompoundSigilsEnabled)
            for (int i = result.Count - 1; i >= 0; i--)
            {
                var glyph = result[i];
                if (glyph.Rune != RuneType.None || glyph.Members.Count == 0) continue;
                // only true mush gets the word-parse: a near-miss or ambiguous
                // fizzle must never be reinterpreted as several other runes
                if (glyph.Score >= DrawingConfig.MinRuneScore) continue;
                var parts = RuneLibrary.ClassifyCompound(ownerId, RawStrokesOf(glyph.Members));
                if (parts.Count < 2) continue;

                result.RemoveAt(i);
                var names = new List<string>();
                foreach (var (rune, score) in parts)
                {
                    var component = new RuneGlyph();
                    component.Members.AddRange(glyph.Members);
                    component.Rune = rune;
                    component.Score = score;
                    result.Add(component);
                    names.Add(RuneLibrary.ShortName(rune));
                }
                DrawingWorld.Instance?.LogEvent($"COMPOUND SIGIL: {string.Join(" + ", names)}");
            }
            return result;
        }

        // connected ink is ONE glyph, components are atomic: Cluster + one
        // recognition per component
        static List<RuneGlyph> SegmentByRecognition(IReadOnlyList<Stroke> strokes, int ownerId)
        {
            var result = new List<RuneGlyph>();
            if (strokes.Count == 0) return result;
            foreach (var glyph in Cluster(strokes, DrawingConfig.RuneTouchDistance))
                result.Add(Recognize(glyph.Members, ownerId));
            return result;
        }

        // result cache: recognition runs once per distinct drawing - pen-up
        // warms it via Precognize, the seal close hits it for free. Keyed by
        // stroke ids + node counts (ink is immutable while Open; erasing
        // changes the node count and misses cleanly).
        static readonly Dictionary<long, (RuneType rune, float score)> _recogCache =
            new Dictionary<long, (RuneType, float)>();

        /// Drops when the templates change: RuneLibrary bumps PoolGeneration
        /// on every pool edit; one int compare per read invalidates the cache.
        static int _cacheGen = -1;

        static long CacheKey(List<Stroke> members, int ownerId)
        {
            long h = ownerId;
            foreach (var m in members)
                h ^= m.Id * 2654435761L + m.Nodes.Count * 31L;
            return h;
        }

        /// Pen-up warm-up: classify the touching cluster around a finished
        /// stroke NOW, so the seal close pays nothing for recognition.
        public static void Precognize(Stroke seed, IReadOnlyList<Stroke> all)
        {
            if (seed == null || !seed.Alive || seed.DeclaredRune != RuneType.None) return;
            // remote ink is read by ITS owner and the verdict shipped - never here (netcode §1)
            if (NetGame.Connected && seed.OwnerId != Grimoire.LocalPlayerId) return;
            var members = new List<Stroke> { seed };
            GrowTouchingCluster(members, all);
            Recognize(members, seed.OwnerId);
        }

        /// Owner's pen-up verdict, straight from the cache - what ships on the wire (netcode §1).
        public static bool CachedVerdict(List<Stroke> members, int ownerId, out RuneType rune, out float score)
        {
            rune = RuneType.None;
            score = 0f;
            if (_cacheGen != RuneLibrary.PoolGeneration) return false;
            if (!_recogCache.TryGetValue(CacheKey(members, ownerId), out var r)) return false;
            rune = r.rune;
            score = r.score;
            return true;
        }

        /// Host-side: adopt the OWNER's shipped verdict for its cluster - recognition
        /// is never recomputed for foreign ink (netcode §1).
        public static void Prime(List<Stroke> members, int ownerId, RuneType rune, float score)
        {
            if (_cacheGen != RuneLibrary.PoolGeneration)
            {
                _recogCache.Clear();
                _cacheGen = RuneLibrary.PoolGeneration;
            }
            if (_recogCache.Count > 128) _recogCache.Clear();
            _recogCache[CacheKey(members, ownerId)] = (rune, score);
        }

        /// Guarded read for one-off classify sites (book boundary picks) - cache +
        /// foreign-ink fizzle, same law as Recognize (netcode §1).
        public static (RuneType rune, float score) ReadVerdict(List<Stroke> members, int ownerId)
        {
            var g = Recognize(members, ownerId);
            return (g.Rune, g.Score);
        }

        /// The one touching-cluster flood: grow `members` with every open
        /// stroke whose ink touches the cluster, using the seal-truth filter
        /// set - residue, declared, hidden and dead ink never join, so the
        /// preview label and the seal read the same ink.
        public static void GrowTouchingCluster(List<Stroke> members, IReadOnlyList<Stroke> all)
        {
            // AABB cull before InkTouches: far ink costs one bounds test
            // instead of a node-vs-segment sweep. Bounds.Expand grows the SIZE
            // (each face moves half), so x2 keeps every pair InkTouches could
            // accept.
            float join = DrawingConfig.RuneTouchDistance;

            _memberBounds.Clear();
            for (int i = 0; i < members.Count; i++)
                _memberBounds.Add(StrokeBounds(members[i]));

            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var s in all)
                {
                    if (s == null || !s.Alive || s.State != StrokeState.Open || s.SealResidue) continue;
                    if (s.DeclaredRune != RuneType.None || s.Hidden() || members.Contains(s)) continue;

                    var bs = StrokeBounds(s);
                    bs.Expand(join * 2f);

                    for (int i = 0; i < members.Count; i++)
                    {
                        if (!bs.Intersects(_memberBounds[i])) continue;
                        if (!InkTouches(s, members[i], join)) continue;
                        members.Add(s);
                        _memberBounds.Add(StrokeBounds(s));
                        grew = true;
                        break;   // members just grew: leave the loop, never enumerate past a mutation
                    }
                }
            }
        }

        /// Bounds of the strokes already in the cluster, index-aligned with
        /// `members`. Reused between calls so the flood allocates nothing.
        static readonly List<Bounds> _memberBounds = new List<Bounds>();

        static RuneGlyph Recognize(List<Stroke> members, int ownerId)
        {
            var glyph = new RuneGlyph();
            glyph.Members.AddRange(members);
            if (_cacheGen != RuneLibrary.PoolGeneration)
            {
                _recogCache.Clear();
                _cacheGen = RuneLibrary.PoolGeneration;
            }
            long key = CacheKey(members, ownerId);
            if (!_recogCache.TryGetValue(key, out var r))
            {
                // foreign ink never re-reads - the owner's shipped verdict or fizzle (netcode §1)
                bool foreign = false;
                if (NetGame.Connected)
                    foreach (var m in members)
                        if (m != null && m.OwnerId != Grimoire.LocalPlayerId) { foreign = true; break; }
                r = foreign ? (RuneType.None, 0f)
                    : RuneLibrary.Classify(ownerId, RawStrokesOf(members));
                if (_recogCache.Count > 128) _recogCache.Clear(); // tiny, self-pruning
                _recogCache[key] = r;
            }
            glyph.Score = r.score;
            glyph.Rune = r.score >= DrawingConfig.MinRuneScore ? r.rune : RuneType.None;
            return glyph;
        }

        /// Public so the erase sweep in DrawingWorld can cull by it too.
        public static Bounds StrokeBounds(Stroke s)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var n in s.Nodes)
            {
                if (n == null) continue;
                if (!any) { b = new Bounds(n.transform.position, Vector3.zero); any = true; }
                else b.Encapsulate(n.transform.position);
            }
            return b;
        }

        /// Touching = node-to-LINE, never node-to-node (node-to-node sampling
        /// error would force a visibly loose threshold). Run both ways round
        /// so the coarser-sampled stroke is measured against the other's line.
        public static bool InkTouches(Stroke a, Stroke b, float maxDist)
        {
            if (a == null || b == null) return false;
            // far apart = never touching, answered from two bounding boxes -
            // this is the cluster flood's hottest inner call, paid per stroke
            // pair per grow iteration
            var ba = StrokeBounds(a);
            ba.Expand(maxDist * 2f);
            if (!ba.Intersects(StrokeBounds(b))) return false;
            float m2 = maxDist * maxDist;
            return NodesTouchLine(a, b, m2) || NodesTouchLine(b, a, m2);
        }

        /// Any node of `from` within `m2` (squared) of the polyline of `onto`.
        static bool NodesTouchLine(Stroke from, Stroke onto, float m2)
        {
            foreach (var na in from.Nodes)
            {
                if (na == null) continue;
                Vector3 p = na.transform.position;
                Vector3 prev = default;
                bool has = false;
                foreach (var nb in onto.Nodes)
                {
                    if (nb == null) { has = false; continue; } // erased hole: no segment across it
                    Vector3 q = nb.transform.position;
                    if ((p - q).sqrMagnitude <= m2) return true;
                    if (has)
                    {
                        Vector3 d = q - prev;
                        float l2 = d.sqrMagnitude;
                        float t = l2 > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - prev, d) / l2) : 0f;
                        if ((p - (prev + d * t)).sqrMagnitude <= m2) return true;
                    }
                    prev = q;
                    has = true;
                }
            }
            return false;
        }
    }
}
