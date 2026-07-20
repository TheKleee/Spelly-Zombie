using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// One drawn rune, possibly made of several strokes.
    ///
    /// Glyphs don't exist while drawing — ink is just ink, with no timers and no
    /// grouping. Only when a seal closes (or a template is recorded) do the
    /// strokes involved get clustered by PURE spatial proximity: ink lying close
    /// together reads as one glyph, no matter when each stroke was drawn.
    /// Runes drawn on top of one another therefore merge into mush and fizzle —
    /// simple, and the player's lesson to learn.
    public class RuneGlyph
    {
        /// TESTING ONLY: the compound-sigil word parse casts 2+ runes from a
        /// scribble that FAILED single-rune recognition — a direct violation of
        /// "the right rune or none". Default OFF until Marko rules otherwise.
        public static bool CompoundSigilsEnabled = false;

        public readonly List<Stroke> Members = new List<Stroke>();
        public RuneType Rune = RuneType.None;
        public float Score;      // raw $P match confidence 0..1
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

        /// Flatten a set of strokes into one shared 2D frame — IN THE SURFACE'S
        /// OWN PLANE, oriented to how the drawer saw it. Projecting onto raw
        /// camera axes perspective-squashed floor drawings (looking down at 60°
        /// halved every "away" stroke), which wrecked recognition on the ground.
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
            // The frame RIDES the surface (SurfaceDelta): an engraved tablet or
            // a posed body re-reads identically no matter where its carrier now
            // faces — orientation in the world never changes what a drawing is.
            Vector3 right = Vector3.ProjectOnPlane(
                lead.First.SurfaceDelta * lead.BasisRight, normal);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.ProjectOnPlane(Vector3.forward, normal);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, normal).normalized;

            // UNROLL THE SURFACE (Marko: "fix recognition on uneven
            // surfaces"): every pen step — including the pen-up jumps
            // BETWEEN strokes — is measured in a parallel-transported local
            // tangent frame and laid flat, one CONTINUOUS unroll for the
            // whole glyph. Ink over slopes and bumps keeps its true drawn
            // shape; strokes that touch in 3D still touch in 2D (the
            // stitcher's 5cm law survives terrain); on flat surfaces the sum
            // telescopes to the old planar projection EXACTLY. Transporting
            // the frame node-to-node (instead of re-projecting the global
            // axis) prevents mirror-flips on high-curvature surfaces like
            // limbs and trunks. [Fleet-verified: the first draft anchored
            // each stroke planar-side and split multi-stroke glyphs on
            // bumps — this version is the corrected one.]
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

        /// Group strokes into glyphs by spatial proximity. The join distance for
        /// a pair scales with the smaller stroke's size, so a big multi-line rune
        /// groups (its lines are large and near each other) while small distinct
        /// runes placed apart stay separate.
        public static List<RuneGlyph> Cluster(IReadOnlyList<Stroke> strokes, float baseDist, float sizeFactor)
        {
            int n = strokes.Count;
            var parent = new int[n];
            var sizes = new float[n];
            var bounds = new Bounds[n];
            for (int i = 0; i < n; i++)
            {
                parent[i] = i;
                bounds[i] = StrokeBounds(strokes[i]);
                sizes[i] = bounds[i].size.magnitude;
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
                    float join = baseDist + sizeFactor * Mathf.Min(sizes[i], sizes[j]);
                    var bi = bounds[i]; bi.Expand(join);
                    if (!bi.Intersects(bounds[j])) continue;
                    if (InkTouches(strokes[i], strokes[j], join))
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

        /// Split the enclosed ink into runes. DECLARED strokes (zombie-scribed
        /// glyphs) are perfect matches if the seal's owner knows the rune;
        /// everything hand-drawn goes through RECOGNITION — draw your runes,
        /// circle them, they're read when the seal closes. No stamping, no
        /// choosing, no timers.
        public static List<RuneGlyph> Segment(IReadOnlyList<Stroke> strokes, float baseDist, float sizeFactor, int ownerId)
        {
            var result = new List<RuneGlyph>();
            var handDrawn = new List<Stroke>();
            foreach (var s in strokes)
            {
                if (s.DeclaredRune != RuneType.None)
                {
                    var glyph = new RuneGlyph();
                    glyph.Members.Add(s);
                    if (RuneLibrary.IsUnlocked(ownerId, s.DeclaredRune))
                    {
                        glyph.Rune = s.DeclaredRune;
                        glyph.Score = 1f;
                    }
                    result.Add(glyph);
                }
                else
                {
                    handDrawn.Add(s);
                }
            }
            result.AddRange(SegmentByRecognition(handDrawn, baseDist, sizeFactor, ownerId));

            // COMPOUND SIGILS: a glyph that fizzled as a single rune might be a
            // WORD — several runes drawn as one connected scribble. Parse its
            // letter-sentence; if it decomposes into 2+ readable runes, they
            // ALL fire (sharing the scribble's location and size).
            // DEFAULT OFF: casting spells FROM a fizzled scribble violates
            // "the right rune or none" — any mush could fire something.
            if (CompoundSigilsEnabled)
            for (int i = result.Count - 1; i >= 0; i--)
            {
                var glyph = result[i];
                if (glyph.Rune != RuneType.None || glyph.Members.Count == 0) continue;
                // only true mush gets the word-parse: a glyph that ALMOST read
                // as one rune (or fizzled as ambiguous) must never be
                // reinterpreted as several other runes — that's an accidental
                // cast waiting to happen
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

        static List<RuneGlyph> SegmentByRecognition(IReadOnlyList<Stroke> strokes, float baseDist, float sizeFactor, int ownerId)
        {
            int n = strokes.Count;
            var result = new List<RuneGlyph>();
            if (n == 0) return result;

            // MARKO'S RULE (the sticky-vs-heat misfire): physically CONNECTED
            // ink is ONE glyph — recognition reads everything that touches as
            // a single drawing, no matter how many pen strokes built it or
            // how old the ink is. Touching runes merging into unreadable mush
            // and fizzling is the player's lesson (see the class doc). The
            // old subset search would happily SPLIT touching ink into
            // whatever combination scored best — finishing a HEAT glyph
            // against leftover ink fired STICKY×2. Components are atomic.
            // (A fizzled component can still decompose as a COMPOUND SIGIL —
            // that path stays, it's the deliberate multi-letter feature.)
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }
            var bounds = new Bounds[n];
            for (int i = 0; i < n; i++) bounds[i] = StrokeBounds(strokes[i]);
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (Find(i) == Find(j)) continue;
                    float join = DrawingConfig.RuneTouchDistance;
                    var bi = bounds[i];
                    bi.Expand(join);
                    if (bi.Intersects(bounds[j]) && InkTouches(strokes[i], strokes[j], join))
                        parent[Find(j)] = Find(i);
                }

            var byRoot = new Dictionary<int, List<Stroke>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!byRoot.TryGetValue(root, out var members))
                {
                    members = new List<Stroke>();
                    byRoot[root] = members;
                }
                members.Add(strokes[i]);
            }
            foreach (var members in byRoot.Values)
                result.Add(Recognize(members, ownerId));
            return result;
        }

        static RuneGlyph Recognize(List<Stroke> members, int ownerId)
        {
            var glyph = new RuneGlyph();
            glyph.Members.AddRange(members);
            var (rune, score) = RuneLibrary.Classify(ownerId, RawStrokesOf(members));
            glyph.Score = score;
            glyph.Rune = score >= DrawingConfig.MinRuneScore ? rune : RuneType.None;
            return glyph;
        }

        static Bounds StrokeBounds(Stroke s)
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

        public static bool InkTouches(Stroke a, Stroke b, float maxDist)
        {
            foreach (var na in a.Nodes)
            {
                if (na == null) continue;
                Vector3 pa = na.transform.position;
                foreach (var nb in b.Nodes)
                {
                    if (nb == null) continue;
                    if (Vector3.Distance(pa, nb.transform.position) <= maxDist)
                        return true;
                }
            }
            return false;
        }
    }
}
