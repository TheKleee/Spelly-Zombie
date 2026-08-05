using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE TURN-SEQUENCE RUNE MATCHER — Marko's alphabet, read the way he
    /// actually draws it.
    ///
    /// HIS CORRECTION (Aug 1), verbatim: "actually all of my runes are 1 long
    /// line except for push and pull… there's no difference with light and
    /// dark."
    ///
    /// That sentence rewrites this file. TEN OF THE TWELVE RUNES ARE A SINGLE
    /// CONTINUOUS OPEN POLYLINE — one line, with corners — and they differ from
    /// one another ONLY BY THEIR SEQUENCE OF TURNS. HEAT, CHILL, SOLID, LIQUID,
    /// GRIP, SLICK, COMPRESS, SPREAD, LIGHT and DARK are all one line each.
    /// LIGHT and DARK are NOT rings, NOT closed and NOT special: their star-like
    /// look is just a zigzag path, and there is no protrusion-versus-indentation
    /// distinction between them to model. Only PUSH (an arrow: shaft plus two
    /// barbs folding BACK) and PULL (a Y: stem plus two arms opening FORWARD)
    /// carry a BRANCH.
    ///
    /// WHAT WAS HERE BEFORE, AND WHY IT HAD TO GO. The previous descriptor was
    /// "a dominant STEM plus shorter LIMBS angled off it". That model fits PUSH
    /// and PULL exactly and nothing else, and it half-worked on the other ten by
    /// accident. It died outright on LIGHT and DARK, and the post-mortem is
    /// worth keeping because it is the whole argument for the descriptor below:
    ///   - "the stem" was only ever THE LONGEST SINGLE SEGMENT. The chain-walk
    ///     that was supposed to grow it used a 22° collinearity test, and the
    ///     SMALLEST corner anywhere in his alphabet is 47°, so it never once
    ///     fired on a real rune.
    ///   - LIGHT's six segments measure 19/18/18/17/14/13 percent of its ink.
    ///     The longest beats the runner-up by 4%. Which segment won was decided
    ///     by that 4% — and a different one won in each of his two drawings, so
    ///     the two descriptors were expressed in frames ~120° apart. Every
    ///     length ratio, attachment position and angle in them was measured
    ///     against a different line. LIGHT scored 0.18 AGAINST ITS OWN TWIN, and
    ///     0.61 against a copy of itself nudged by half a percent.
    ///   - the LIGHT/DARK "region flip" apparatus (convex hull, Out, MeanOut,
    ///     Bite) modelled a distinction he says does not exist. Measured, it was
    ///     not merely idle: it read DARK's two drawings as 0.46 and 1.00 and
    ///     charged DARK 34% for disagreeing with itself.
    ///
    /// THE DESCRIPTOR THAT ACTUALLY FITS HIS ALPHABET: the SIGNED TURN SEQUENCE
    /// along the stitched polyline, each turn paired with the length of the
    /// segment leading into it as a FRACTION of the whole. Every one of his laws
    /// is satisfied BY CONSTRUCTION rather than by a penalty term:
    ///   - each corner is measured RELATIVE TO THE PREVIOUS SEGMENT, so the
    ///     descriptor is ROTATION-INVARIANT by construction. No rotation clamp,
    ///     no alignment search, no world axes, ever. "Measure in the rune's own
    ///     frame, never the world's."
    ///   - MIRRORING NEGATES EVERY SIGN, so the mirror pairs separate for free —
    ///     no chirality scalar, no hand-tuned mirror hammer. Rotate freely,
    ///     mirror never, and that asymmetry is now the shape of the metric
    ///     instead of a term bolted onto it.
    ///   - angles are unitless and lengths are fractions, so SIZE AND OVERALL
    ///     LENGTH CANNOT MATTER. "The overall length of the runes shouldn't
    ///     matter, only their shape."
    ///   - stitching and collinear-collapse run first, so PEN LIFTS AND STROKE
    ///     COUNT CANNOT MATTER. An arrow drawn in three strokes and one drawn in
    ///     a single sweep produce the same sentence.
    ///   - A BARE STRAIGHT LINE HAS ZERO CORNERS, therefore no sentence,
    ///     therefore no rune. His rule ("a straight line should be detected as
    ///     NOTHING automatically") is now the definition rather than a heuristic.
    /// His own recordings, measured, are why this is the right descriptor —
    /// LIGHT's two drawings agree within 15° on EVERY turn:
    ///     LIGHT0  -119   68 -128   77 -113      LIGHT1  -112   65 -128   62 -124
    ///     DARK0   -108  -47  100  -51 -131      DARK1   -112  -47   76  -52 -124
    /// and the corner count alone is already a clean coarse index across all
    /// twelve: GRIP/SLICK 2, HEAT/CHILL/SOLID/LIQUID 3, COMPRESS/SPREAD 4,
    /// LIGHT/DARK 5, PUSH/PULL branched.
    ///
    /// BRANCHING IS THE SECONDARY TRAIT, and the ONLY thing it decides is which
    /// of two families a drawing belongs to. A junction means PUSH or PULL; no
    /// junction means one of the ten. Those two families are never compared to
    /// each other. Inside the branched family the old stem-plus-limbs matcher is
    /// kept verbatim — its own header describes an arrow and a Y and nothing
    /// else, it separates them by whether the branches fold BACK (obtuse to the
    /// shaft) or open FORWARD (acute), and Marko says PUSH and PULL are fine as
    /// they are. Do not regress them by "unifying" the two paths.
    ///
    /// HIS STANDING LAW, unchanged: "All shapes are distinct — I made them that
    /// way exactly because they can be flipped." No glyph resembles any other
    /// under ANY rotation or reflection, so full rotation invariance is SAFE and
    /// EVERY cross-fire is a bug in this file, never an ambiguity in his
    /// alphabet. "Re-record it with a more distinct shape" is always the wrong
    /// answer.
    ///
    /// Deterministic and allocation-light: Build touches only pooled scratch
    /// lists and allocates exactly one graph plus its segment array; Compare
    /// allocates nothing at all. Both are main-thread only (same discipline the
    /// old matcher's scratch buffers were written under — a fresh buffer per
    /// call once cost ~1.5M allocations in a single template audit). No LINQ.
    public sealed class RuneGraph
    {
        // ---------------------------------------------------------- tuning --
        // ALL of these are FRACTIONS OF THE DRAWING'S OWN SIZE (see Extent), of
        // its own ink length, or plain angles. Never introduce a threshold in
        // metres here: that is the bug that keeps coming back, and it makes a
        // rune's reading depend on how big it was drawn. Never take the size
        // from an axis-aligned bounding box either — see Extent for why.

        /// Douglas-Peucker tolerance: a bend that moves the line less than this
        /// fraction of the drawing's own size is a shaky hand, not a corner.
        /// Much coarser than the stitcher's denoise pass on purpose — this pass
        /// decides what a SEGMENT is, and therefore what a TURN is, and a
        /// phantom corner does not merely blur the sentence, it inserts a letter
        /// into it.
        ///
        /// SWEPT, not guessed. Identity on his 24 recordings is flat from 0.035
        /// to 0.09 (22/24 either way), but noise immunity is not: with random
        /// per-point jitter at 2% of the drawing's diameter, wrong-rune reads
        /// across 400 draws of all twelve runes go 768 at 0.035 → 80 at 0.055 →
        /// 1 at 0.075. The ceiling is just above: at 0.10 the tolerance starts
        /// eating DARK's shallowest real corner, DARK drops to five segments,
        /// and it cross-fires with LIGHT at 0.73 vs 0.72 — a coin flip, and
        /// exactly the kind he says is always a bug in this file. 0.075 sits
        /// with noise immunity won and a clear gap to that cliff.
        const float SimplifyFrac = 0.075f;

        /// Two vertices this close are the SAME node, and a vertex this close
        /// to the middle of a segment splits it (a T-junction). Same tight
        /// scale-relative rule the endpoint stitcher and the old connectivity
        /// test used — "looks connected" must mean "is connected".
        const float WeldFrac = 0.06f;

        /// Segments meeting within this angle are the SAME straight line, so a
        /// line drawn as two slightly-kinked strokes is still one line. This is
        /// load-bearing for the turn sentence, not merely tidy: a pen lift in
        /// the middle of a straight run would otherwise appear in the sentence
        /// as a 0° corner and shift every turn after it by one position. Tight
        /// enough that a real corner (47° and up) is never swallowed.
        const float CollinearDeg = 22f;

        /// BRANCHED FAMILY ONLY. How straight the stem walk must be to continue
        /// STRAIGHT THROUGH A FORK.
        ///
        /// At a degree-2 node there is only one way onward, so CollinearDeg is
        /// just a "is this a corner" test. At a FORK there are two, and picking
        /// "whichever is straighter" with a 22° window turns the stem into a
        /// coin flip — measured, PULL's arms open at roughly 25-35° from its
        /// stem, so a wobble of half a percent was enough to drop an arm under
        /// 22° and let the stem SWALLOW it. The descriptor then had one limb
        /// instead of two and PULL scored 0.05 AGAINST ITS OWN TEMPLATE, on 3%
        /// of draws at 0.5% wobble and 12% at 1%. That is the same failure that
        /// killed LIGHT, wearing a different hat.
        ///
        /// Passing straight through a fork is still legal and still needed — an
        /// arrow whose barbs land halfway up the shaft has its shaft SPLIT in
        /// two at a degree-4 node, and those two halves are one line and must
        /// chain. But they are near-0° apart, so the test can afford to be
        /// strict, and strict is what removes the flip.
        const float ForkCollinearDeg = 8f;

        /// BRANCHED FAMILY ONLY (PUSH / PULL). Below this the stem's two ends
        /// are genuinely interchangeable — the shape is symmetric end-to-end, so
        /// its own 180° rotation looks identical and BOTH orientations must be
        /// allowed. (Rotation is legal; this is not a mirror licence.)
        const float OrientTol = 0.06f;

        // ------------------------------------------------------ descriptor --

        /// ONE SEGMENT OF THE RUNE'S SINGLE LINE — the letter of the sentence.
        /// Both fields are measured in the rune's own terms; neither refers to
        /// the world, and neither carries a unit that a bigger drawing would
        /// change.
        public struct Seg
        {
            /// This segment's length ÷ the whole line's length. A PROPORTION,
            /// never a size. This is what separates the glyph families that
            /// share a turn sequence: SOLID, LIQUID, COMPRESS and SPREAD are all
            /// three or four near-90° corners and differ ONLY in where the long
            /// run and the short tick sit. Marko: "measure proportional line
            /// length and angles."
            public float Len;
            /// The SIGNED turn from this segment onto the NEXT one, degrees,
            /// (-180..180], positive = left. Measured against the PREVIOUS
            /// segment rather than any fixed axis, which is what makes the whole
            /// descriptor rotation-invariant without a single alignment search.
            /// NaN on the last segment, which has nothing to turn onto — that is
            /// a real value the matcher reads, not a hole (see Cell).
            public float Turn;
        }

        static readonly Seg[] NoSegs = new Seg[0];
        static readonly Limb[] NoLimbs = new Limb[0];

        /// THE PRIMARY DESCRIPTOR: the rune's line, segment by segment, in walk
        /// order. Empty for the branched family, which is read by stem and limbs
        /// instead.
        public Seg[] Segs = NoSegs;

        /// Corners in that line. The coarse identity, and the bare-line test.
        public int Corners => Segs.Length > 0 ? Segs.Length - 1 : 0;

        /// Straight segments in the graph — the coarse topology fingerprint.
        public int Edges;
        /// Free ends (degree-1 nodes).
        public int Ends;
        /// Junctions (degree-3-or-more nodes).
        public int Junctions;

        /// THE FAMILY GATE. A junction means the drawing is an arrow or a Y; no
        /// junction means it is one of the ten single-line runes. The two
        /// families carry different descriptors and are never scored against
        /// each other (see Compare).
        public bool Branched => Junctions > 0;

        /// No corners at all: a bare line. HIS RULE: "a straight line should be
        /// detected as NOTHING automatically." A bare line has no turn sequence
        /// to read, so it matches nothing, ever. It is still BUILT, never
        /// deleted — RuneLibrary needs a non-null descriptor to tell "unreadable
        /// drawing" from "no drawing at all".
        public bool BareLine;

        // ---- branched family only (PUSH / PULL): stem plus limbs ------------
        // Kept verbatim from the previous matcher, because it is a faithful
        // model of exactly those two glyphs — "a dominant line plus shorter
        // lines angled off it… if PUSH has one line longer and other smaller
        // lines are angled toward that line it's a PUSH, and the opposite is a
        // PULL" — and Marko reports the pair works. The parts that modelled the
        // OTHER ten (the convex hull, Out/MeanOut/Bite, Chirality) are deleted:
        // measured, they never fired on an arrow or a Y, and they actively cost
        // DARK 34% against its own twin.

        /// One shorter line hanging off the stem, measured ENTIRELY in the
        /// rune's own frame. Nothing here refers to the world.
        public struct Limb
        {
            /// length ÷ stem length. Proportion, never size.
            public float Ratio;
            /// where it attaches, projected on the stem: 0 = tail, 1 = tip.
            /// Deliberately NOT clamped — an overshoot is information.
            public float Along;
            /// signed sideways offset of the attach point from the stem line,
            /// ÷ stem length. Mirroring flips it; rotation does not.
            public float Off;
            /// SIGNED angle from the stem's forward direction to this limb's
            /// direction, degrees, (-180..180]. THE thing that separates PUSH's
            /// barbs (folding BACK, obtuse) from PULL's arms (opening FORWARD,
            /// acute).
            public float Angle;
        }

        /// Limbs, longest first (deterministic pairing order in MatchLimbs).
        public Limb[] Limbs = NoLimbs;
        /// stem length ÷ total ink length. His "one line longer" proportion:
        /// an arrow is shaft-dominated, a Y splits its ink much more evenly.
        public float StemFrac;
        /// The stem's two ends were interchangeable, so this shape looks the
        /// same rotated 180°. Compare may then try either orientation — that is
        /// rotation tolerance, not mirror tolerance. It exists ONLY because a
        /// stem frame needs a direction; the turn sentence has none and needs
        /// no such flag.
        public bool Reversible;

        // ------------------------------------------------------- scratch ----
        // Reused across calls. Build and Compare run on the main thread only,
        // never recursively, so one set of buffers is safe and keeps a
        // 2400-template audit out of the garbage collector.
        static readonly List<Vector2> _simp = new List<Vector2>(128);
        static readonly List<Vector2> _nodePos = new List<Vector2>(64);
        static readonly List<Vector2> _walk = new List<Vector2>(64);
        static readonly List<int> _edgeA = new List<int>(96);
        static readonly List<int> _edgeB = new List<int>(96);
        static readonly List<int> _deg = new List<int>(64);
        static readonly List<int> _chain = new List<int>(64);
        static readonly List<int> _bestChain = new List<int>(64);
        static readonly List<Vector2> _ext = new List<Vector2>(256);
        static bool[] _keep;
        static bool[] _edgeUsed;
        static bool[] _inStem;
        static bool[] _pairUsed;
        static Limb[] _revBuf = NoLimbs;
        static readonly Stack<int> _dpStack = new Stack<int>(64);

        /// Hard ceiling on graph size — a scribble with hundreds of corners is
        /// not a rune, and the split loop must never run away.
        const int MaxEdges = 96;

        /// The sequence-alignment table. Sized once for the worst legal graph so
        /// Compare never allocates.
        static readonly float[,] _dp = new float[MaxEdges + 2, MaxEdges + 2];

        /// Above this the pairwise Extent pass strides instead of visiting
        /// every point. Striding picks the same INDICES whatever the drawing's
        /// angle, so the answer stays exactly rotation-invariant — it just gets
        /// slightly conservative on absurdly dense ink.
        const int ExtentSampleCap = 256;

        /// THE DRAWING'S OWN SIZE: the greatest distance between any two of its
        /// points. Every scale-relative threshold in the recognizer is a
        /// fraction of THIS.
        ///
        /// It is not the bounding box. An axis-aligned bounding box is a
        /// WORLD-frame measurement — its diagonal breathes by up to 41% (23%
        /// measured on a real glyph) as the same drawing is rotated, which
        /// silently modulates the simplify tolerance, the weld radius and the
        /// stitch distance with hand tilt. Measured: at 50° of tilt the weld
        /// radius overtook a real tick, two segments were welded away and the
        /// glyph's score against its OWN template collapsed from 1.00 to 0.11 —
        /// a fizzle at 4 of 36 orientations, from a file whose header promises
        /// it never looks at the world. The point-set diameter is invariant
        /// under rotation and reflection by construction.
        public static float Extent(IReadOnlyList<IReadOnlyList<Vector2>> paths)
        {
            if (paths == null) return 0f;
            _ext.Clear();
            for (int s = 0; s < paths.Count; s++)
            {
                var p = paths[s];
                if (p == null) continue;
                for (int i = 0; i < p.Count; i++) _ext.Add(p[i]);
            }
            int n = _ext.Count;
            if (n < 2) return 0f;
            int stride = n > ExtentSampleCap ? (n / ExtentSampleCap) + 1 : 1;
            float best = 0f;
            for (int i = 0; i < n; i += stride)
                for (int j = i + stride; j < n; j += stride)
                {
                    float d = (_ext[i] - _ext[j]).sqrMagnitude;
                    if (d > best) best = d;
                }
            return Mathf.Sqrt(best);
        }

        // ========================================================== BUILD ====

        /// Build the descriptor from already-stitched, denoised paths.
        ///
        /// Returns null ONLY for genuinely degenerate input (no path with two
        /// distinct points). That contract is load-bearing: RuneLibrary treats
        /// a null descriptor as "this template does not exist", and a stricter
        /// gate here would silently disable Marko's studio walls. A straight
        /// line is NOT degenerate — it builds fine and comes back BareLine, so
        /// it reads as nothing without ever being deleted.
        public static RuneGraph Build(IReadOnlyList<IReadOnlyList<Vector2>> paths)
        {
            if (paths == null) return null;

            // ---- the drawing's own scale. EVERY threshold below is a
            // fraction of this, so a rune reads identically drawn tiny or huge
            // — and, because Extent is the point-set diameter rather than a
            // world-axis bounding box, identically drawn at any ANGLE too.
            float diag = Extent(paths);
            if (diag < 1e-5f) return null;

            float eps = diag * SimplifyFrac;
            float weld = diag * WeldFrac;
            float weld2 = weld * weld;

            // ---- 1. straight segments as edges, welded vertices as nodes ----
            // Douglas-Peucker IS the corner extractor. What survives it is the
            // straight-line skeleton of the drawing, and the turn sentence reads
            // straight off it. There is exactly one simplifier in this project;
            // do not write a second.
            _nodePos.Clear();
            _edgeA.Clear();
            _edgeB.Clear();
            for (int s = 0; s < paths.Count; s++)
            {
                var p = paths[s];
                if (p == null || p.Count < 2) continue;
                Simplify(p, eps, _simp);
                if (_simp.Count < 2) continue;
                int prev = -1;
                for (int i = 0; i < _simp.Count; i++)
                {
                    int n = NodeAt(_simp[i], weld2);
                    if (prev >= 0 && n != prev) AddEdge(prev, n);
                    prev = n;
                }
            }
            if (_edgeA.Count == 0) return null;

            // ---- 2. T-junctions. Endpoint welding alone misses the line that
            // lands on the MIDDLE of another — which is exactly how PUSH's barbs
            // and PULL's arms attach. Without this the same rune reads as two
            // shapes when drawn in two lines and one shape when drawn in one,
            // which is exactly what his pen-lift law forbids. It is also what
            // makes the branch test below trustworthy: a junction has to be
            // FOUND before it can gate anything.
            SplitTJunctions(weld, weld2);

            // ---- 2b. A PEN LIFT IN THE MIDDLE OF A STRAIGHT LINE IS NOT A
            // CORNER. Douglas-Peucker runs per path, so lifting the pen halfway
            // along a run forces a vertex there and the run arrives as two
            // segments. For the turn sentence that is not a cosmetic problem: it
            // inserts a phantom 0° letter and shifts every real turn after it by
            // one position, so the same drawing would read as a different word
            // depending on how many times he lifted the pen. Any degree-2 node
            // whose two segments are within CollinearDeg of being one line is
            // dissolved back into that line.
            CollapseCollinear();

            // ---- 3. degrees = the topology fingerprint ----
            _deg.Clear();
            for (int i = 0; i < _nodePos.Count; i++) _deg.Add(0);
            float inkLen = 0f;
            for (int e = 0; e < _edgeA.Count; e++)
            {
                _deg[_edgeA[e]]++;
                _deg[_edgeB[e]]++;
                inkLen += Vector2.Distance(_nodePos[_edgeA[e]], _nodePos[_edgeB[e]]);
            }

            int ends = 0, junctions = 0;
            for (int i = 0; i < _deg.Count; i++)
            {
                if (_deg[i] == 1) ends++;
                else if (_deg[i] >= 3) junctions++;
            }

            // ---- 4. WHICH FAMILY? This is the ONE thing branching decides.
            // A junction = PUSH or PULL, read as a stem plus limbs. No junction
            // = one of the ten single-line runes, read as a turn sentence.
            if (junctions > 0)
            {
                int tail = -1, tip = -1;
                FindStem(ref tail, ref tip);
                if (tail < 0 || tip < 0)
                    return new RuneGraph { Edges = _edgeA.Count, Ends = ends, Junctions = junctions, BareLine = true };
                return Frame(tail, tip, _bestChain, inkLen, ends, junctions);
            }

            return Sentence(ends, junctions);
        }

        // ------------------------------------- the ten: a turn sentence ------

        /// Walk the single line and write down what it does.
        ///
        /// The walk starts at a free end, which is the only canonical place to
        /// start; a polyline has two of them and no preferred one, so Compare
        /// tries the sentence in both directions (see Compare — reversing a WALK
        /// is not a mirror, and that distinction is the whole mirror law).
        static RuneGraph Sentence(int ends, int junctions)
        {
            var g = new RuneGraph { Edges = _edgeA.Count, Ends = ends, Junctions = junctions };

            if (!WalkChain(_walk) || _walk.Count < 2) { g.BareLine = true; return g; }

            int nSeg = _walk.Count - 1;
            var segs = new Seg[nSeg];
            float total = 0f;
            for (int i = 0; i < nSeg; i++)
            {
                float len = Vector2.Distance(_walk[i], _walk[i + 1]);
                segs[i].Len = len;
                total += len;
            }
            if (total < 1e-6f) { g.BareLine = true; return g; }

            for (int i = 0; i < nSeg; i++)
            {
                // LENGTH AS A FRACTION OF THE WHOLE LINE. Divide before anything
                // else looks at it: the moment a raw length reaches the metric,
                // how big he drew the rune starts deciding what it is.
                segs[i].Len /= total;
                // THE SIGNED TURN, measured against the PREVIOUS segment. This
                // one line is the entire reason the matcher needs no rotation
                // clamp, no alignment search and no world axes: turn the drawing
                // and every one of these is unchanged; mirror it and every one
                // of them negates.
                segs[i].Turn = i + 2 < _walk.Count
                    ? Vector2.SignedAngle(_walk[i + 1] - _walk[i], _walk[i + 2] - _walk[i + 1])
                    : float.NaN;
            }

            g.Segs = segs;
            // HIS RULE, now a definition rather than a heuristic: "a straight
            // line should be detected as NOTHING automatically." One segment
            // means zero corners means no sentence to read.
            g.BareLine = nSeg < 2;
            return g;
        }

        /// The longest continuous run of segments, walked from a free end.
        ///
        /// With no junctions every node has degree 2 at most, so each connected
        /// piece is a simple path and the walk is unambiguous — there is never a
        /// choice to make and therefore never a coin flip to lose. (That is the
        /// precise failure the old stem search had: LIGHT's longest segment beat
        /// the runner-up by 4%, so which one framed the descriptor was decided
        /// by hand noise, and the two drawings of LIGHT ended up measured in
        /// frames 120° apart.)
        ///
        /// Stitching normally leaves exactly one path, so "longest piece" is
        /// simply "the drawing". It matters only for ink that could not be
        /// stitched, where reading the largest piece beats reading nothing.
        /// No free end at all means the ink closes on itself — that is a SEAL,
        /// not a rune, and it walks away with no sentence.
        static bool WalkChain(List<Vector2> outVerts)
        {
            outVerts.Clear();
            int n = _edgeA.Count;
            if (n == 0) return false;

            float bestLen = -1f;
            int bestStart = -1;
            for (int s = 0; s < _nodePos.Count; s++)
            {
                if (_deg[s] != 1) continue;
                float len = ChainFrom(s, null);
                // Strict improvement: both ends of the same piece measure the
                // same length, so the first one found wins deterministically.
                // Which end that is does not matter — Compare reads the sentence
                // in both directions anyway.
                if (len > bestLen) { bestLen = len; bestStart = s; }
            }
            if (bestStart < 0) return false;

            ChainFrom(bestStart, outVerts);
            return outVerts.Count >= 2;
        }

        /// Follow the line from `start` until it runs out, optionally recording
        /// the vertices. Returns the total length walked.
        static float ChainFrom(int start, List<Vector2> verts)
        {
            int n = _edgeA.Count;
            if (_edgeUsed == null || _edgeUsed.Length < n)
                _edgeUsed = new bool[Mathf.Max(MaxEdges, n)];
            System.Array.Clear(_edgeUsed, 0, n);

            int at = start;
            float total = 0f;
            verts?.Add(_nodePos[at]);
            for (int guard = 0; guard <= n; guard++)
            {
                int nextE = -1, other = -1;
                for (int e = 0; e < n; e++)
                {
                    if (_edgeUsed[e]) continue;
                    if (_edgeA[e] == at) { nextE = e; other = _edgeB[e]; break; }
                    if (_edgeB[e] == at) { nextE = e; other = _edgeA[e]; break; }
                }
                if (nextE < 0) break;
                _edgeUsed[nextE] = true;
                total += Vector2.Distance(_nodePos[at], _nodePos[other]);
                at = other;
                verts?.Add(_nodePos[at]);
            }
            return total;
        }

        // ------------------------------- the branched pair: stem and limbs ---

        /// Everything that depends on WHICH line is the stem. PUSH and PULL
        /// only: for an arrow and a Y "the stem" is unambiguous — the shaft is
        /// twice the length of a barb — which is exactly the condition the ten
        /// single-line runes fail and why they are read a different way.
        static RuneGraph Frame(int tail, int tip, List<int> chain,
                               float inkLen, int ends, int junctions)
        {
            var g = new RuneGraph { Edges = _edgeA.Count, Ends = ends, Junctions = junctions };
            Vector2 tailP = _nodePos[tail], tipP = _nodePos[tip];
            float stemLen = Vector2.Distance(tailP, tipP);
            if (stemLen < 1e-6f) { g.BareLine = true; return g; }
            g.StemFrac = inkLen > 1e-6f ? Mathf.Clamp01(stemLen / inkLen) : 1f;

            if (_inStem == null || _inStem.Length < _edgeA.Count)
                _inStem = new bool[Mathf.Max(MaxEdges, _edgeA.Count)];
            System.Array.Clear(_inStem, 0, _edgeA.Count);
            for (int i = 0; i < chain.Count; i++) _inStem[chain[i]] = true;

            // ---- the limbs ----
            int limbCount = _edgeA.Count - chain.Count;
            if (limbCount <= 0) { g.BareLine = true; return g; }

            var limbs = new Limb[limbCount];
            // First pass in the AS-FOUND orientation, so the orientation vote
            // below has real numbers to work with.
            Vector2 fwd = (tipP - tailP) / stemLen;
            int k = 0;
            float limbLenSum = 0f;
            for (int e = 0; e < _edgeA.Count && k < limbCount; e++)
            {
                if (_inStem[e]) continue;
                int a = _edgeA[e], b = _edgeB[e];
                // WHICH END HANGS THE LIMB: the busier node. A limb hangs off a
                // junction; when the degrees tie (a tick on the far end of
                // another limb) the end nearer the stem is the attachment.
                int attach, free;
                if (_deg[a] != _deg[b]) { attach = _deg[a] > _deg[b] ? a : b; }
                else attach = PointSegDist(_nodePos[a], tailP, tipP)
                              <= PointSegDist(_nodePos[b], tailP, tipP) ? a : b;
                free = attach == a ? b : a;

                Vector2 ap = _nodePos[attach], fp = _nodePos[free];
                Vector2 d = fp - ap;
                float len = d.magnitude;
                if (len < 1e-6f) continue;
                d /= len;

                Vector2 rel = ap - tailP;
                limbs[k].Ratio = len / stemLen;
                limbs[k].Along = Vector2.Dot(rel, fwd) / stemLen;
                limbs[k].Off = (fwd.x * rel.y - fwd.y * rel.x) / stemLen;
                limbs[k].Angle = Vector2.SignedAngle(fwd, d);
                limbLenSum += len;
                k++;
            }
            if (k == 0) { g.BareLine = true; return g; }
            if (k < limbCount) System.Array.Resize(ref limbs, k);

            // ---- ORIENT THE STEM, mirror-neutrally.
            // A stem frame needs to know which end is the tail, or the limbs of
            // two drawings of the same arrow are measured from opposite ends.
            // The vote uses ONLY quantities a mirror leaves untouched — node
            // degrees and where the ink mass sits along the stem. It must never
            // use a signed angle or a side: canonicalising by chirality would
            // erase the mirror distinction, which is the one thing this matcher
            // exists to preserve.
            float pref = 0.5f * Mathf.Clamp(_deg[tip] - _deg[tail], -2, 2);
            if (limbLenSum > 1e-6f)
                for (int i = 0; i < k; i++)
                    pref += (limbs[i].Ratio * stemLen / limbLenSum) * (limbs[i].Along - 0.5f);

            if (pref < -OrientTol) ReverseInto(limbs, k, limbs); // flip in place
            // A near-tie means the shape genuinely looks the same end-for-end,
            // i.e. its own 180° ROTATION. Allowing both orientations is then
            // required by his rotation law, not a loophole in the mirror law.
            g.Reversible = Mathf.Abs(pref) <= OrientTol;

            // longest limb first — deterministic greedy pairing in MatchLimbs
            SortByRatio(limbs, k);
            g.Limbs = limbs;
            return g;
        }

        // ========================================================= COMPARE ===

        /// Similarity of two descriptors, 0..1. 1 = the same shape.
        /// `a` is the DRAWING, `b` is the template. Every caller passes them in
        /// that order.
        ///
        /// Calibrated against the score band the rest of the game reads
        /// (DrawingConfig). The turn sentence is a far tighter metric than the
        /// stem-and-limb score it replaced, so that band was re-measured on his
        /// own recordings and DrawingConfig moved with it. Leave-one-out — a
        /// drawing against his OTHER drawings, one template per rune, the
        /// harshest case there is:
        ///     right rune  0.89 to 1.00        best WRONG rune  never above 0.84
        /// where the old matcher put an honest LIGHT at 0.18 against its own
        /// twin. Under random rotation, scale x0.14 to x7, one to four pen lifts
        /// and 1.2% hand wobble, 11,474 draws produced ZERO wrong-rune wins.
        public static float Compare(RuneGraph a, RuneGraph b)
        {
            if (a == null || b == null) return 0f;
            // HIS RULE: a bare line is NOTHING. Not a weak match — nothing.
            if (a.BareLine || b.BareLine) return 0f;

            // THE FAMILY GATE. An arrow has a junction; a zigzag does not. The
            // two families carry different descriptors, so there is nothing to
            // compare across them — and nothing to gain: he designed no glyph to
            // resemble another, least of all across that divide.
            if (a.Branched != b.Branched) return 0f;

            return a.Branched ? CompareBranched(a, b) : CompareSentence(a, b);
        }

        // ------------------------------- the ten: sequence alignment ---------

        /// A MISSING OR EXTRA CORNER IS A MISSING FEATURE, not a rounding error.
        /// Turn count is most of a rune's identity here (2 corners = GRIP/SLICK,
        /// 3 = HEAT/CHILL/SOLID/LIQUID, 4 = COMPRESS/SPREAD, 5 = LIGHT/DARK), so
        /// the alignment is allowed to skip a corner but pays nearly the full
        /// price of a completely wrong one when it does. It is not free, and it
        /// is not fatal either: hands genuinely do drop a tick.
        const float GapCost = 0.90f;

        /// Free play before a turn counts as wrong, and the span over which it
        /// goes fully wrong. 12° is comfortably inside the spread of his own
        /// hand — his two LIGHTs agree within 15° on every corner and his two
        /// HEATs within 26° — while 47° (the smallest real corner in the
        /// alphabet) still lands most of the way to a full miss.
        const float TurnFree = 12f;
        const float TurnSpan = 68f;

        /// Same, for the length proportions. Relative difference, so a segment
        /// that is 40% of the line and one that is 20% disagree by the same
        /// amount whichever way round they are read. The +0.05 keeps a pair of
        /// genuinely tiny ticks from reading as wildly different just because
        /// they are small.
        const float LenFree = 0.15f;
        const float LenSpan = 0.70f;

        /// How the two halves of a letter split. The TURN is the headline — it
        /// is what separates HEAT from SOLID from LIGHT — but the LENGTH is not
        /// a tie-break, it is the ONLY thing separating SOLID from LIQUID and
        /// COMPRESS from SPREAD. Read his own recordings:
        ///     SOLID0  turns  -98  -90  -97      LIQUID0  turns  -95  -84  -91
        /// Those are the same three right-angle corners. What differs is where
        /// the long run and the short tick sit along the line — proportion, and
        /// nothing else. Exactly his rule: "measure proportional line length AND
        /// angles."
        ///
        /// Swept on his recordings against the thinnest correct margin anywhere
        /// in the alphabet: 0.30 length → 0.11 (SOLID/LIQUID), 0.50 → 0.14,
        /// 0.58 → 0.11 again but now HEAT/CHILL is the bottleneck and random
        /// scribbles reaching 0.85 climb from 5.0% to 6.0%. An even split is
        /// where the turn-family and the length-family bottlenecks balance.
        const float TurnWeight = 0.50f;
        const float LenWeight = 0.50f;

        static float CompareSentence(RuneGraph a, RuneGraph b)
        {
            // A POLYLINE HAS NO CANONICAL START. Both of its ends are equally
            // valid places to begin reading, so the drawing is offered in both
            // directions and the better reading wins.
            //
            // THIS IS NOT A MIRROR LICENCE, and the difference is the whole
            // mirror law. Walking a line backwards reverses the ORDER of the
            // turns and negates each one; MIRRORING the line negates each turn
            // and leaves the order alone. Only the first is accepted. So HEAT
            // still cannot read as CHILL, SOLID cannot read as LIQUID and GRIP
            // cannot read as SLICK — measured, each mirror pair separates with
            // no penalty term anywhere, purely from this asymmetry.
            float best = Align(a, false, b);
            float rev = Align(a, true, b);
            return rev > best ? rev : best;
        }

        /// Length of segment `i` of the sentence, optionally read backwards.
        static float SegLen(Seg[] s, int i, bool rev) => rev ? s[s.Length - 1 - i].Len : s[i].Len;

        /// Turn after segment `i` of the sentence, optionally read backwards.
        /// Walking the other way makes what was the turn ONTO a segment the turn
        /// OFF it, and swaps left for right — hence the index shift and the
        /// negation. The last segment of either reading turns onto nothing.
        static float SegTurn(Seg[] s, int i, bool rev)
        {
            if (!rev) return s[i].Turn;
            int n = s.Length;
            return i + 1 < n ? -s[n - 2 - i].Turn : float.NaN;
        }

        /// The cost of calling segment `i` of a the same as segment `j` of b.
        /// 0 = indistinguishable, 1 = completely different.
        static float Cell(Seg[] sa, int i, bool ra, Seg[] sb, int j)
        {
            float la = SegLen(sa, i, ra), lb = sb[j].Len;
            float rel = Mathf.Abs(la - lb) / (0.5f * (la + lb) + 0.05f);
            float lenC = Mathf.Clamp01((rel - LenFree) / LenSpan);

            float ta = SegTurn(sa, i, ra), tb = sb[j].Turn;
            float angC;
            // The terminal NaN is REAL INFORMATION, not a hole: it says "the
            // line stops here". Two lines that both stop agree; a line that
            // stops where the other turns disagrees, but only half — the
            // alignment can usually explain that better by taking a gap, and
            // this keeps it from being forced into one.
            if (float.IsNaN(ta) && float.IsNaN(tb)) angC = 0f;
            else if (float.IsNaN(ta) || float.IsNaN(tb)) angC = 0.5f;
            else angC = Mathf.Clamp01((Mathf.Abs(Mathf.DeltaAngle(ta, tb)) - TurnFree) / TurnSpan);

            return TurnWeight * angC + LenWeight * lenC;
        }

        /// Elastic alignment of two sentences — a Levenshtein/DTW hybrid over
        /// letters that are graded rather than equal-or-not. Tolerates one
        /// extra or missing corner (at GapCost each) without ever pretending a
        /// wrong turn is a right one. Deliberately NOT a fixed-length compare:
        /// hands add and lose corners, and a rune that grew a tick must still
        /// read as itself.
        static float Align(RuneGraph ga, bool rev, RuneGraph gb)
        {
            Seg[] sa = ga.Segs, sb = gb.Segs;
            int n = sa.Length, m = sb.Length;
            if (n == 0 || m == 0) return 0f;
            if (n >= _dp.GetLength(0) || m >= _dp.GetLength(1)) return 0f;

            // Seed the origin FIRST: _dp is a pooled buffer reused across calls,
            // and the two edge rows below read outward from [0,0].
            _dp[0, 0] = 0f;
            for (int i = 1; i <= n; i++) _dp[i, 0] = _dp[i - 1, 0] + GapCost;
            for (int j = 1; j <= m; j++) _dp[0, j] = _dp[0, j - 1] + GapCost;
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                {
                    float sub = _dp[i - 1, j - 1] + Cell(sa, i - 1, rev, sb, j - 1);
                    float del = _dp[i - 1, j] + GapCost;
                    float ins = _dp[i, j - 1] + GapCost;
                    float v = sub < del ? sub : del;
                    _dp[i, j] = v < ins ? v : ins;
                }

            // Normalise by the LONGER sentence: a five-corner LIGHT explained as
            // a two-corner GRIP plus three gaps must not score as if only the
            // two matched letters counted.
            return Mathf.Clamp01(1f - _dp[n, m] / Mathf.Max(n, m));
        }

        // ------------------------------- the branched pair: stem and limbs ---

        static float CompareBranched(RuneGraph a, RuneGraph b)
        {
            int na = a.Limbs.Length, nb = b.Limbs.Length;
            if (na == 0 || nb == 0) return 0f;

            float best = MatchLimbs(a, a.Limbs, na, b, b.Limbs, nb);

            // An end-for-end symmetric shape is identical to its own 180°
            // rotation, so both readings are the same drawing. Reversing ONE
            // side covers both cases: the metric only ever looks at
            // differences, and reversing both sides leaves every difference
            // unchanged.
            if (a.Reversible || b.Reversible)
            {
                if (_revBuf.Length < na) _revBuf = new Limb[Mathf.Max(16, na)];
                ReverseInto(a.Limbs, na, _revBuf);
                float rev = MatchLimbs(a, _revBuf, na, b, b.Limbs, nb);
                if (rev > best) best = rev;
            }
            return best;
        }

        static float MatchLimbs(RuneGraph ga, Limb[] la, int na, RuneGraph gb, Limb[] lb, int nb)
        {
            // ---- TOPOLOGY FIRST ----
            // Segment count "within one" is free: hands add and lose a corner
            // constantly. The junction count is not checked here any more — it
            // is the family gate in Compare, so by the time we are in this
            // function both shapes are already known to be branched.
            float topo = 1f;
            int dE = Mathf.Abs(ga.Edges - gb.Edges);
            if (dE > 1) topo *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01((dE - 1) / 4f));
            int dJ = Mathf.Abs(ga.Junctions - gb.Junctions);
            if (dJ > 0) topo *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(dJ / 2f));
            int dEnds = Mathf.Abs(ga.Ends - gb.Ends);
            if (dEnds > 1) topo *= Mathf.Lerp(1f, 0.65f, Mathf.Clamp01((dEnds - 1) / 3f));

            // ---- LIMBS: greedy pairing, longest first (both lists are sorted
            // by Ratio at build time, so this is deterministic) ----
            if (_pairUsed == null || _pairUsed.Length < nb)
                _pairUsed = new bool[Mathf.Max(16, nb)];
            System.Array.Clear(_pairUsed, 0, nb);

            float sum = 0f;
            int paired = 0, take = na < nb ? na : nb;
            for (int i = 0; i < na && paired < take; i++)
            {
                int bestJ = -1;
                float bestS = -1f;
                for (int j = 0; j < nb; j++)
                {
                    if (_pairUsed[j]) continue;
                    float s = LimbSim(la[i], lb[j]);
                    if (s > bestS) { bestS = s; bestJ = j; }
                }
                if (bestJ < 0) break;
                _pairUsed[bestJ] = true;
                sum += bestS;
                paired++;
            }
            if (paired == 0) return 0f;

            // ONE extra or missing limb is half-forgiven; the rest cost full
            // price. Short limbs are never ignored — they just don't get to
            // destroy an otherwise clean read on their own.
            int extra = Mathf.Abs(na - nb);
            float misses = Mathf.Max(0, extra - 1) + (extra > 0 ? 0.5f : 0f);
            float limbScore = sum / (paired + misses);

            // ---- HIS PROPORTION RULE: "if PUSH has one line longer…" ----
            // How much of the ink IS the dominant line. An arrow is
            // shaft-heavy (0.58), a Y splits far more evenly (0.36). This is the
            // size-free proportion the old AspectPenalty was reaching for with a
            // world-aligned bounding box.
            float dom = 1f - 0.55f * Mathf.Clamp01(
                (Mathf.Abs(ga.StemFrac - gb.StemFrac) - 0.05f) / 0.28f);

            return Mathf.Clamp01(topo * limbScore * dom);
        }

        static float LimbSim(in Limb a, in Limb b)
        {
            // PROPORTION, never size — and it GATES, it does not merely shade.
            // Marko: "measure proportional line length and angles."
            float rel = Mathf.Abs(a.Ratio - b.Ratio) / (0.5f * (a.Ratio + b.Ratio) + 0.15f);
            float rTerm = 1f - Mathf.Clamp01((rel - 0.12f) / 0.55f);

            // WHERE it hangs: how far along the stem, and which side.
            float aTerm = 1f - Mathf.Clamp01((Mathf.Abs(a.Along - b.Along) - 0.10f) / 0.45f);
            float oTerm = 1f - Mathf.Clamp01((Mathf.Abs(a.Off - b.Off) - 0.10f) / 0.40f);
            float pos = 0.55f * aTerm + 0.45f * oTerm;

            // THE ANGLE DECIDES, and on this pair it is the ONLY thing that has
            // to. PUSH's barbs fold BACK toward the tail (obtuse to the shaft);
            // PULL's arms open FORWARD (acute). Same topology, ~110° apart — so
            // the angle gate nearly zeroes the pair when it disagrees, and only
            // a sliver of the geometry survives.
            float dAng = Mathf.Abs(Mathf.DeltaAngle(a.Angle, b.Angle));
            float angTerm = 1f - Mathf.Clamp01((dAng - 15f) / 70f);

            // Three gates, each able to veto on its own, none able to fail a
            // limb that is merely drawn a bit loosely (every one keeps a floor).
            float sim = (0.25f + 0.75f * pos)
                      * (0.20f + 0.80f * rTerm)
                      * (0.12f + 0.88f * angTerm);

            // MIRRORING FLIPS A SIGNED ANGLE; ROTATION DOES NOT. Two limbs
            // leaning opposite ways are opposite runes, full stop — and the
            // wrapped angle difference alone cannot see it (a limb at +160°
            // and one at -160° differ by only 40° yet are reflections). Skip
            // the test near 0° and 180°, where the sign is just noise on a
            // limb that runs along the stem either way.
            float ma = Mathf.Abs(a.Angle), mb = Mathf.Abs(b.Angle);
            if ((a.Angle < 0f) != (b.Angle < 0f)
                && ma > 22f && ma < 158f && mb > 22f && mb < 158f)
                sim *= 0.18f;
            return sim;
        }

        /// Same shape, stem read tail-for-tip. A 180° frame ROTATION: the
        /// attachment slides to the other end, the side flips, and the angle
        /// swings half a turn. Not a reflection — CompareBranched only ever
        /// applies this to shapes that are symmetric end-to-end anyway.
        static void ReverseInto(Limb[] src, int n, Limb[] dst)
        {
            for (int i = 0; i < n; i++)
            {
                Limb l = src[i];
                l.Along = 1f - l.Along;
                l.Off = -l.Off;
                l.Angle = l.Angle > 0f ? l.Angle - 180f : l.Angle + 180f;
                dst[i] = l;
            }
        }

        // ==================================================== graph guts =====

        static int NodeAt(Vector2 p, float weld2)
        {
            for (int i = 0; i < _nodePos.Count; i++)
                if ((_nodePos[i] - p).sqrMagnitude <= weld2) return i;
            _nodePos.Add(p);
            return _nodePos.Count - 1;
        }

        static void AddEdge(int a, int b)
        {
            if (a == b || _edgeA.Count >= MaxEdges) return;
            for (int i = 0; i < _edgeA.Count; i++)
                if ((_edgeA[i] == a && _edgeB[i] == b) || (_edgeA[i] == b && _edgeB[i] == a)) return;
            _edgeA.Add(a);
            _edgeB.Add(b);
        }

        /// A line that lands on the MIDDLE of another creates a junction that
        /// endpoint welding cannot see. Split the crossed segment there so the
        /// degree count tells the truth. Terminates: every split consumes a
        /// distinct node as a new endpoint of a strictly shorter segment.
        static void SplitTJunctions(float weld, float weld2)
        {
            for (int e = 0; e < _edgeA.Count; e++)
            {
                if (_edgeA.Count >= MaxEdges) return;
                int a = _edgeA[e], b = _edgeB[e];
                Vector2 pa = _nodePos[a], ab = _nodePos[b] - pa;
                float len2 = ab.sqrMagnitude;
                if (len2 < 1e-10f) continue;
                float len = Mathf.Sqrt(len2);
                for (int n = 0; n < _nodePos.Count; n++)
                {
                    if (n == a || n == b) continue;
                    float t = Vector2.Dot(_nodePos[n] - pa, ab) / len2;
                    // AN END HIT IS ALREADY A SHARED NODE — and "already
                    // shared" means "inside the weld radius", which is the only
                    // definition of touching this file has. A fixed 0.12/0.88
                    // window left a BAND between the weld radius and the window
                    // edge where a limb attaching near the end of the stem was
                    // neither welded nor split: drawn in one stroke it made a
                    // junction, drawn as two it floated free. That is stroke
                    // count changing the topology, which his law forbids.
                    if (t * len < weld || (1f - t) * len < weld) continue;
                    if ((_nodePos[n] - (pa + ab * t)).sqrMagnitude > weld2) continue;
                    _edgeB[e] = n;
                    AddEdge(n, b);
                    e--;  // re-test the shortened half — another line may cross it too
                    break;
                }
            }
        }

        /// Dissolve every degree-2 node that is not really a corner. A pen lift
        /// halfway along a straight line plants a vertex there; so does a
        /// stroke that overshoots and gets stitched. Neither is a feature of
        /// the rune, and both would otherwise insert a phantom letter into the
        /// turn sentence and shift every real turn after it.
        static void CollapseCollinear()
        {
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < 64)
            {
                changed = false;
                _deg.Clear();
                for (int i = 0; i < _nodePos.Count; i++) _deg.Add(0);
                for (int e = 0; e < _edgeA.Count; e++) { _deg[_edgeA[e]]++; _deg[_edgeB[e]]++; }

                for (int n = 0; n < _nodePos.Count && !changed; n++)
                {
                    if (_deg[n] != 2) continue;
                    int e1 = -1, e2 = -1, a = -1, b = -1;
                    for (int e = 0; e < _edgeA.Count; e++)
                    {
                        int other = _edgeA[e] == n ? _edgeB[e] : (_edgeB[e] == n ? _edgeA[e] : -1);
                        if (other < 0) continue;
                        if (e1 < 0) { e1 = e; a = other; }
                        else { e2 = e; b = other; break; }
                    }
                    if (e2 < 0 || a == b) continue;
                    Vector2 d1 = _nodePos[n] - _nodePos[a];
                    Vector2 d2 = _nodePos[b] - _nodePos[n];
                    if (d1.sqrMagnitude < 1e-10f || d2.sqrMagnitude < 1e-10f) continue;
                    if (Vector2.Angle(d1, d2) >= CollinearDeg) continue; // a real corner: keep it
                    bool dup = false;
                    for (int e = 0; e < _edgeA.Count; e++)
                        if (e != e1 && e != e2
                            && ((_edgeA[e] == a && _edgeB[e] == b) || (_edgeA[e] == b && _edgeB[e] == a)))
                        { dup = true; break; }
                    if (dup) continue;
                    _edgeA[e1] = a; _edgeB[e1] = b;
                    _edgeA.RemoveAt(e2); _edgeB.RemoveAt(e2);
                    changed = true;
                }
            }
        }

        /// The longest near-collinear chain of segments, walked outward from
        /// every seed edge in both directions. BRANCHED FAMILY ONLY — this is
        /// the arrow's shaft and the Y's stem.
        ///
        /// It is NOT used on the other ten any more, and that is the single
        /// biggest fix in this file. On a glyph whose segments are all roughly
        /// equal (LIGHT: 19/18/18/17/14/13 percent of its ink) "the longest
        /// line" is a coin flip, the whole descriptor was framed on the winner,
        /// and LIGHT scored 0.18 against its own twin. An arrow's shaft wins by
        /// 2:1 and no coin is flipped.
        static void FindStem(ref int tail, ref int tip)
        {
            int n = _edgeA.Count;
            if (_edgeUsed == null || _edgeUsed.Length < n)
                _edgeUsed = new bool[Mathf.Max(MaxEdges, n)];

            float bestLen = -1f;
            _bestChain.Clear();
            for (int seed = 0; seed < n; seed++)
            {
                _chain.Clear();
                System.Array.Clear(_edgeUsed, 0, n);
                _edgeUsed[seed] = true;
                _chain.Add(seed);

                int endA = _edgeA[seed], endB = _edgeB[seed];
                endB = Extend(endA, endB);
                endA = Extend(endB, endA);

                float len = Vector2.Distance(_nodePos[endA], _nodePos[endB]);
                // A STRICT improvement, by a relative margin: refusing to swap
                // on a near-tie makes the choice deterministic across rotation
                // instead of noise-driven. Edge indices come from the pen order
                // and a rotation-invariant simplification, so they are the same
                // list at every angle.
                if (len > bestLen * (1f + 1e-4f))
                {
                    bestLen = len;
                    tail = endA;
                    tip = endB;
                    _bestChain.Clear();
                    for (int i = 0; i < _chain.Count; i++) _bestChain.Add(_chain[i]);
                }
            }
        }

        static int Extend(int anchor, int at)
        {
            while (true)
            {
                Vector2 dir = _nodePos[at] - _nodePos[anchor];
                if (dir.sqrMagnitude < 1e-10f) return at;
                dir.Normalize();

                int bestE = -1, bestOther = -1;
                // A FORK IS NOT A CORNER, IT IS A CHOICE — and a loose test on
                // a choice is a coin flip. See ForkCollinearDeg.
                float bestAng = _deg[at] >= 3 ? ForkCollinearDeg : CollinearDeg;
                for (int e = 0; e < _edgeA.Count; e++)
                {
                    if (_edgeUsed[e]) continue;
                    int other;
                    if (_edgeA[e] == at) other = _edgeB[e];
                    else if (_edgeB[e] == at) other = _edgeA[e];
                    else continue;
                    Vector2 d = _nodePos[other] - _nodePos[at];
                    if (d.sqrMagnitude < 1e-10f) continue;
                    float ang = Vector2.Angle(dir, d);
                    if (ang < bestAng) { bestAng = ang; bestE = e; bestOther = other; }
                }
                if (bestE < 0) return at;
                _edgeUsed[bestE] = true;
                _chain.Add(bestE);
                at = bestOther;
            }
        }

        static float PointSegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-12f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return (p - (a + ab * t)).magnitude;
        }

        static void SortByRatio(Limb[] limbs, int n)
        {
            for (int i = 1; i < n; i++)
            {
                Limb v = limbs[i];
                int j = i - 1;
                while (j >= 0 && limbs[j].Ratio < v.Ratio) { limbs[j + 1] = limbs[j]; j--; }
                limbs[j + 1] = v;
            }
        }

        // ------------------------------------------------ Douglas-Peucker ---

        /// Keep only the points that change the shape by more than `eps`.
        /// This is the CORNER EXTRACTOR: what survives IS the straight-line
        /// skeleton of the drawing, and the turn sentence is read directly off
        /// it. Shared with RuneLibrary.Denoise so there is exactly one
        /// straightening rule in the project — write a second one and the
        /// matcher and the stitcher will disagree about what a corner is.
        public static List<Vector2> Simplify(IReadOnlyList<Vector2> pts, float eps)
        {
            var outp = new List<Vector2>(pts != null ? pts.Count : 0);
            Simplify(pts, eps, outp);
            return outp;
        }

        public static void Simplify(IReadOnlyList<Vector2> pts, float eps, List<Vector2> outp)
        {
            outp.Clear();
            if (pts == null || pts.Count == 0) return;
            if (pts.Count < 3 || eps <= 0f)
            {
                for (int i = 0; i < pts.Count; i++) outp.Add(pts[i]);
                return;
            }
            int n = pts.Count;
            if (_keep == null || _keep.Length < n) _keep = new bool[Mathf.Max(256, n)];
            System.Array.Clear(_keep, 0, n);
            _keep[0] = _keep[n - 1] = true;

            _dpStack.Clear();
            _dpStack.Push(0);
            _dpStack.Push(n - 1);
            while (_dpStack.Count > 0)
            {
                int b = _dpStack.Pop();
                int a = _dpStack.Pop();
                if (b <= a + 1) continue;
                Vector2 pa = pts[a], ab = pts[b] - pa;
                float abLen = ab.magnitude;
                float worst = 0f;
                int worstAt = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = abLen < 1e-6f
                        ? Vector2.Distance(pts[i], pa)
                        : Mathf.Abs(ab.x * (pa.y - pts[i].y) - (pa.x - pts[i].x) * ab.y) / abLen;
                    if (d > worst) { worst = d; worstAt = i; }
                }
                if (worst > eps && worstAt > 0)
                {
                    _keep[worstAt] = true;
                    _dpStack.Push(a); _dpStack.Push(worstAt);
                    _dpStack.Push(worstAt); _dpStack.Push(b);
                }
            }
            for (int i = 0; i < n; i++) if (_keep[i]) outp.Add(pts[i]);
        }
    }
}
