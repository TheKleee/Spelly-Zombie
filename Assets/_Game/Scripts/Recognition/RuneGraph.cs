using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The turn-sequence rune matcher. Ten of the twelve runes are a single
    /// continuous open polyline and differ only by their sequence of signed
    /// turns; only PUSH (arrow) and PULL (Y) carry a branch, and the branched
    /// and unbranched families are never compared to each other.
    ///
    /// The descriptor: the signed turns along the stitched polyline, each
    /// paired with the leading segment's length as a fraction of the whole.
    /// Rotation-invariant by construction; mirroring negates every sign, so
    /// mirror pairs separate; size, stroke count and pen lifts cannot matter;
    /// a bare straight line has no sentence and reads as nothing. Corner
    /// count is a coarse index: GRIP/SLICK 2, HEAT/CHILL/SOLID/LIQUID 3,
    /// COMPRESS/SPREAD 4, LIGHT/DARK 5, PUSH/PULL branched.
    ///
    /// Build touches only pooled scratch lists and allocates one graph plus
    /// its segment array; Compare allocates nothing. Both main-thread only.
    public sealed class RuneGraph
    {
        // ---------------------------------------------------------- tuning --
        // All fractions of the drawing's own size (see Extent), of its ink
        // length, or plain angles. Never introduce a threshold in metres, and
        // never take the size from an axis-aligned bounding box (see Extent).

        /// Douglas-Peucker tolerance: a bend that moves the line less than this
        /// fraction of the drawing's own size is a shaky hand, not a corner.
        /// Coarser than the stitcher's denoise pass on purpose - this pass
        /// decides what a segment (and so a turn) is. Above ~0.10 it starts
        /// eating DARK's shallowest real corner.
        const float SimplifyFrac = 0.075f;

        /// Two vertices this close are the SAME node, and a vertex this close
        /// to the middle of a segment splits it (a T-junction). Scale-relative.
        const float WeldFrac = 0.06f;

        /// Segments meeting within this angle are the SAME straight line - a
        /// mid-run pen lift must not appear as a 0° corner and shift the
        /// sentence. Tight enough that a real corner (47° and up) is never
        /// swallowed.
        const float CollinearDeg = 22f;

        /// Branched family only: how straight the stem walk must be to continue
        /// straight through a fork. PULL's arms open ~25-35° from the stem, so
        /// a loose (22°) window could let the stem swallow an arm; shaft halves
        /// split at a junction are near-0° apart, so strict still chains them.
        const float ForkCollinearDeg = 8f;

        /// Branched family only (PUSH / PULL). Below this the stem's two ends
        /// are interchangeable - the shape is symmetric end-to-end, so both
        /// orientations must be allowed (rotation, not a mirror licence).
        const float OrientTol = 0.06f;

        // ------------------------------------------------------ descriptor --

        /// One segment of the rune's single line - the letter of the sentence.
        /// Both fields are measured in the rune's own terms, never the world's.
        public struct Seg
        {
            /// This segment's length ÷ the whole line's length - a proportion,
            /// never a size. Length placement is all that separates glyphs
            /// sharing a turn sequence (SOLID, LIQUID, COMPRESS, SPREAD).
            public float Len;
            /// The signed turn onto the NEXT segment, degrees, (-180..180],
            /// positive = left, measured against the previous segment (this is
            /// the rotation invariance). NaN on the last segment - a real value
            /// the matcher reads, not a hole (see Cell).
            public float Turn;
        }

        static readonly Seg[] NoSegs = new Seg[0];
        static readonly Limb[] NoLimbs = new Limb[0];

        /// The primary descriptor: the rune's line, segment by segment, in walk
        /// order. Empty for the branched family (read by stem and limbs).
        public Seg[] Segs = NoSegs;

        // (corner count = Segs.Length - 1; the coarse identity - see class header)

        /// Straight segments in the graph - the coarse topology fingerprint.
        public int Edges;
        /// Free ends (degree-1 nodes).
        public int Ends;
        /// Junctions (degree-3-or-more nodes).
        public int Junctions;

        /// The family gate: a junction means arrow or Y; none means one of the
        /// ten single-line runes. The two families carry different descriptors
        /// and are never scored against each other (see Compare).
        public bool Branched => Junctions > 0;

        /// No corners at all: a bare line has no turn sequence, so it matches
        /// nothing, ever. Still BUILT, never deleted - RuneLibrary needs a
        /// non-null descriptor to tell "unreadable" from "no drawing at all".
        public bool BareLine;

        // ---- branched family only (PUSH / PULL): stem plus limbs ------------
        // A faithful model of exactly those two glyphs: a dominant stem plus
        // shorter limbs angled off it.

        /// One shorter line hanging off the stem, measured ENTIRELY in the
        /// rune's own frame. Nothing here refers to the world.
        public struct Limb
        {
            /// length ÷ stem length. Proportion, never size.
            public float Ratio;
            /// where it attaches, projected on the stem: 0 = tail, 1 = tip.
            /// Deliberately NOT clamped - an overshoot is information.
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
        /// stem length ÷ total ink length: an arrow is shaft-dominated, a Y
        /// splits its ink much more evenly.
        public float StemFrac;
        /// The stem's two ends were interchangeable, so this shape looks the
        /// same rotated 180°; Compare may then try either orientation
        /// (rotation tolerance, not mirror tolerance).
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

        /// Hard ceiling on graph size - a scribble with hundreds of corners is
        /// not a rune, and the split loop must never run away.
        const int MaxEdges = 96;

        /// The sequence-alignment table. Sized once for the worst legal graph so
        /// Compare never allocates.
        static readonly float[,] _dp = new float[MaxEdges + 2, MaxEdges + 2];

        /// Above this the pairwise Extent pass strides instead of visiting
        /// every point. Striding picks the same indices at any angle, so the
        /// answer stays rotation-invariant - just slightly conservative on
        /// very dense ink.
        const int ExtentSampleCap = 256;

        /// The drawing's own size: the greatest distance between any two of
        /// its points. Every scale-relative threshold is a fraction of this.
        /// NOT a bounding box: an axis-aligned box is a world-frame measure
        /// whose diagonal breathes up to 41% as the drawing rotates; the
        /// point-set diameter is invariant under rotation and reflection.
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
        /// Returns null only for degenerate input (no path with two distinct
        /// points) - RuneLibrary reads null as "this template does not exist".
        /// A straight line is not degenerate: it builds and comes back BareLine.
        public static RuneGraph Build(IReadOnlyList<IReadOnlyList<Vector2>> paths)
        {
            if (paths == null) return null;

            // ---- the drawing's own scale: every threshold below is a
            // fraction of this
            float diag = Extent(paths);
            if (diag < 1e-5f) return null;

            float eps = diag * SimplifyFrac;
            float weld = diag * WeldFrac;
            float weld2 = weld * weld;

            // ---- 1. straight segments as edges, welded vertices as nodes ----
            // Douglas-Peucker is the corner extractor: what survives is the
            // straight-line skeleton the turn sentence reads off. There is
            // exactly one simplifier in this project; do not write a second.
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

            // ---- 2. T-junctions: endpoint welding misses a line landing on
            // the MIDDLE of another - how PUSH's barbs and PULL's arms attach.
            // Without this, stroke count would change the topology.
            SplitTJunctions(weld, weld2);

            // ---- 2b. a pen lift mid-line is not a corner: per-path
            // Douglas-Peucker forces a vertex there, which would insert a
            // phantom 0° letter and shift every turn after it. Dissolve
            // degree-2 nodes whose segments are within CollinearDeg.
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

            // ---- 4. which family - the one thing branching decides. A
            // junction = PUSH or PULL, read as stem plus limbs; no junction =
            // one of the ten single-line runes, read as a turn sentence.
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

        /// Walk the single line and write down what it does. The walk starts
        /// at a free end; a polyline has two and no preferred one, so Compare
        /// tries the sentence in both directions (reversing a walk is not a
        /// mirror).
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
                // length as a fraction of the whole line - raw lengths never
                // reach the metric
                segs[i].Len /= total;
                // the signed turn against the PREVIOUS segment: rotation
                // leaves it unchanged, mirroring negates it
                segs[i].Turn = i + 2 < _walk.Count
                    ? Vector2.SignedAngle(_walk[i + 1] - _walk[i], _walk[i + 2] - _walk[i + 1])
                    : float.NaN;
            }

            g.Segs = segs;
            // one segment = zero corners = no sentence to read
            g.BareLine = nSeg < 2;
            return g;
        }

        /// The longest continuous run of segments, walked from a free end.
        /// With no junctions each connected piece is a simple path, so the
        /// walk is unambiguous. Stitching normally leaves one piece;
        /// unstitchable ink reads its largest. No free end means the ink
        /// closes on itself - a seal, not a rune, and no sentence.
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
                // strict improvement: both ends measure the same length, so
                // the first found wins deterministically; Compare reads both
                // directions anyway
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

        /// Everything that depends on which line is the stem. PUSH and PULL
        /// only: for an arrow and a Y the stem is unambiguous (the shaft is
        /// about twice a barb's length).
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

            // ---- orient the stem, mirror-neutrally: the vote uses only
            // quantities a mirror leaves untouched (node degrees, ink mass
            // along the stem). Never use a signed angle or a side -
            // canonicalising by chirality would erase the mirror distinction.
            float pref = 0.5f * Mathf.Clamp(_deg[tip] - _deg[tail], -2, 2);
            if (limbLenSum > 1e-6f)
                for (int i = 0; i < k; i++)
                    pref += (limbs[i].Ratio * stemLen / limbLenSum) * (limbs[i].Along - 0.5f);

            if (pref < -OrientTol) ReverseInto(limbs, k, limbs); // flip in place
            // a near-tie means the shape looks the same end-for-end (its own
            // 180° rotation), so both orientations are allowed
            g.Reversible = Mathf.Abs(pref) <= OrientTol;

            // longest limb first - deterministic greedy pairing in MatchLimbs
            SortByRatio(limbs, k);
            g.Limbs = limbs;
            return g;
        }

        // ========================================================= COMPARE ===

        /// Similarity of two descriptors, 0..1. 1 = the same shape. `a` is the
        /// drawing, `b` is the template; every caller passes them in that
        /// order. The score band is calibrated against DrawingConfig.
        public static float Compare(RuneGraph a, RuneGraph b)
        {
            if (a == null || b == null) return 0f;
            // a bare line matches nothing
            if (a.BareLine || b.BareLine) return 0f;

            // the family gate: the two families carry different descriptors,
            // so there is nothing to compare across them
            if (a.Branched != b.Branched) return 0f;

            return a.Branched ? CompareBranched(a, b) : CompareSentence(a, b);
        }

        // ------------------------------- the ten: sequence alignment ---------

        /// A missing or extra corner is a missing feature: turn count is most
        /// of a rune's identity, so a skipped corner pays nearly the full
        /// price of a wrong one - costly, not fatal (hands do drop a tick).
        const float GapCost = 0.90f;

        /// Free play before a turn counts as wrong, and the span over which it
        /// goes fully wrong. 12° sits inside the hand's spread; 47° (the
        /// smallest real corner in the alphabet) lands most of the way to a
        /// full miss.
        const float TurnFree = 12f;
        const float TurnSpan = 68f;

        /// Same, for the length proportions. Relative difference, symmetric
        /// either way round; the +0.05 keeps a pair of tiny ticks from reading
        /// as wildly different just because they are small.
        const float LenFree = 0.15f;
        const float LenSpan = 0.70f;

        /// How the two halves of a letter split. The turn separates most
        /// glyphs, but the length is the ONLY thing separating SOLID from
        /// LIQUID and COMPRESS from SPREAD (same corners, different
        /// placement); an even split balances the two bottlenecks.
        const float TurnWeight = 0.50f;
        const float LenWeight = 0.50f;

        static float CompareSentence(RuneGraph a, RuneGraph b)
        {
            // A polyline has no canonical start, so the drawing is read in
            // both directions and the better reading wins. Not a mirror
            // licence: walking backwards reverses the ORDER of the turns and
            // negates each one; mirroring negates each turn but keeps the
            // order. Only the first is accepted, so mirror pairs stay apart.
            float best = Align(a, false, b);
            float rev = Align(a, true, b);
            return rev > best ? rev : best;
        }

        /// Length of segment `i` of the sentence, optionally read backwards.
        static float SegLen(Seg[] s, int i, bool rev) => rev ? s[s.Length - 1 - i].Len : s[i].Len;

        /// Turn after segment `i` of the sentence, optionally read backwards.
        /// Walking the other way makes what was the turn ONTO a segment the turn
        /// OFF it, and swaps left for right - hence the index shift and the
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
            // terminal NaN = "the line stops here". Both stop: agree. One
            // stops where the other turns: half a miss, so the alignment can
            // explain it with a gap instead.
            if (float.IsNaN(ta) && float.IsNaN(tb)) angC = 0f;
            else if (float.IsNaN(ta) || float.IsNaN(tb)) angC = 0.5f;
            else angC = Mathf.Clamp01((Mathf.Abs(Mathf.DeltaAngle(ta, tb)) - TurnFree) / TurnSpan);

            return TurnWeight * angC + LenWeight * lenC;
        }

        /// Elastic alignment of two sentences - a Levenshtein/DTW hybrid over
        /// graded letters. Tolerates an extra or missing corner (at GapCost
        /// each) without pretending a wrong turn is a right one.
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
            // ---- topology first: segment count "within one" is free (hands
            // add and lose corners); the junction gate lives in Compare ----
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
            // price. Short limbs are never ignored - they just don't get to
            // destroy an otherwise clean read on their own.
            int extra = Mathf.Abs(na - nb);
            float misses = Mathf.Max(0, extra - 1) + (extra > 0 ? 0.5f : 0f);
            float limbScore = sum / (paired + misses);

            // how much of the ink is the dominant line: an arrow is
            // shaft-heavy (0.58), a Y splits far more evenly (0.36)
            float dom = 1f - 0.55f * Mathf.Clamp01(
                (Mathf.Abs(ga.StemFrac - gb.StemFrac) - 0.05f) / 0.28f);

            return Mathf.Clamp01(topo * limbScore * dom);
        }

        static float LimbSim(in Limb a, in Limb b)
        {
            // proportion, never size - and it gates, not merely shades
            float rel = Mathf.Abs(a.Ratio - b.Ratio) / (0.5f * (a.Ratio + b.Ratio) + 0.15f);
            float rTerm = 1f - Mathf.Clamp01((rel - 0.12f) / 0.55f);

            // WHERE it hangs: how far along the stem, and which side.
            float aTerm = 1f - Mathf.Clamp01((Mathf.Abs(a.Along - b.Along) - 0.10f) / 0.45f);
            float oTerm = 1f - Mathf.Clamp01((Mathf.Abs(a.Off - b.Off) - 0.10f) / 0.40f);
            float pos = 0.55f * aTerm + 0.45f * oTerm;

            // the angle decides: PUSH's barbs fold BACK (obtuse to the shaft),
            // PULL's arms open FORWARD (acute) - same topology, ~110° apart,
            // so the gate nearly zeroes a disagreeing pair
            float dAng = Mathf.Abs(Mathf.DeltaAngle(a.Angle, b.Angle));
            float angTerm = 1f - Mathf.Clamp01((dAng - 15f) / 70f);

            // Three gates, each able to veto on its own, none able to fail a
            // limb that is merely drawn a bit loosely (every one keeps a floor).
            float sim = (0.25f + 0.75f * pos)
                      * (0.20f + 0.80f * rTerm)
                      * (0.12f + 0.88f * angTerm);

            // mirroring flips a signed angle; rotation does not. The wrapped
            // angle difference alone cannot see it (+160° vs -160° differ by
            // 40° yet are reflections). Skip near 0° and 180°, where the sign
            // is noise on a limb running along the stem.
            float ma = Mathf.Abs(a.Angle), mb = Mathf.Abs(b.Angle);
            if ((a.Angle < 0f) != (b.Angle < 0f)
                && ma > 22f && ma < 158f && mb > 22f && mb < 158f)
                sim *= 0.18f;
            return sim;
        }

        /// Same shape, stem read tail-for-tip. A 180° frame ROTATION: the
        /// attachment slides to the other end, the side flips, and the angle
        /// swings half a turn. Not a reflection - CompareBranched only ever
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
                    // an end hit is already a shared node, where "shared"
                    // means inside the weld radius - the same definition of
                    // touching used everywhere else
                    if (t * len < weld || (1f - t) * len < weld) continue;
                    if ((_nodePos[n] - (pa + ab * t)).sqrMagnitude > weld2) continue;
                    _edgeB[e] = n;
                    AddEdge(n, b);
                    e--;  // re-test the shortened half - another line may cross it too
                    break;
                }
            }
        }

        /// Dissolve every degree-2 node that is not really a corner (a pen
        /// lift or stitched overshoot plants one). Left alone it would insert
        /// a phantom letter and shift every real turn after it.
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
        /// every seed edge in both directions. Branched family only - the
        /// arrow's shaft and the Y's stem, where the winner is unambiguous
        /// (a shaft wins ~2:1; the ten single-line runes have no such line).
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
                // strict improvement by a relative margin: refusing to swap on
                // a near-tie keeps the choice deterministic across rotation
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
                // a fork is a choice, not a corner - see ForkCollinearDeg
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

        /// Keep only the points that change the shape by more than `eps` - the
        /// corner extractor. Shared with RuneLibrary.Denoise so there is
        /// exactly one straightening rule in the project.
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
            MarkKeep(pts, eps);
            for (int i = 0; i < pts.Count; i++) if (_keep[i]) outp.Add(pts[i]);
        }

        /// Same simplification, but reports the kept point indices (endpoints
        /// included) - GeometryUtil.ClosedLoopCorners needs corner indices.
        public static void SimplifyIndices(IReadOnlyList<Vector2> pts, float eps, List<int> keepIdx)
        {
            keepIdx.Clear();
            if (pts == null || pts.Count == 0) return;
            if (pts.Count < 3 || eps <= 0f)
            {
                for (int i = 0; i < pts.Count; i++) keepIdx.Add(i);
                return;
            }
            MarkKeep(pts, eps);
            for (int i = 0; i < pts.Count; i++) if (_keep[i]) keepIdx.Add(i);
        }

        static void MarkKeep(IReadOnlyList<Vector2> pts, float eps)
        {
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
        }
    }
}
