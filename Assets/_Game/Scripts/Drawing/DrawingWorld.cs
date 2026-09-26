using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Owns all ink in the world: the stroke registry, the seal detector loop,
    /// every active seal, and the spent groups waiting to re-arm.
    public class DrawingWorld : MonoBehaviour
    {
        public static DrawingWorld Instance { get; private set; }

        public readonly List<Stroke> Strokes = new List<Stroke>();
        public readonly List<Seal> ActiveSeals = new List<Seal>();

        /// The most recently completed stroke - anchor for template recording.
        public Stroke LastInk { get; private set; }

        public Material LineMaterial { get; private set; }

        /// Persistent ink whose spell resolved: locked until the loop physically
        /// opens (any junction beyond ReArmDistance), then it can fire again.
        class SpentGroup
        {
            public List<Stroke> Strokes;
            public List<(DrawNode a, DrawNode b)> Pairs;
            public List<SealDetector.LoopEntry> Boundary; // ring order, kept so the seal can re-fire itself
            public bool Armed;                            // the loop has OPENED and is waiting to re-close
        }

        readonly List<SpentGroup> _spentGroups = new List<SpentGroup>();

        // boundaries (by stroke ids) that already cast. A body drawing fires
        // once; it fires again only after the loop opens wide and re-closes.
        readonly HashSet<string> _castKeys = new HashSet<string>();
        float _detectTimer;
        float _evapTimer;
        readonly Dictionary<int, Vector3> _ownerAt = new Dictionary<int, Vector3>();
        readonly List<Stroke> _eligibleCache = new List<Stroke>();
        string _lastNearMissShown;
        bool _forceDetect;
        // sampled ink geometry at the last REAL scan - the held-still gate
        // (see Detect) compares against this before paying for the detectors
        readonly List<Vector3> _detectSnap = new List<Vector3>();
        int _detectSnapSig;
        // each stroke's ink as the last scan that looked at it saw it: a scan hands
        // the detectors only what changed since, plus all ink it reaches (ScanSet)
        class ScanRef
        {
            public int Nodes;
            public bool AllNodes; // nodes ride several carriers: every node is compared
            public readonly List<Vector3> Pts = new List<Vector3>();
            public Bounds Reach;  // node box grown by the detectors' reach
            public int Tick;
        }
        readonly Dictionary<Stroke, ScanRef> _scanRefs = new Dictionary<Stroke, ScanRef>();
        readonly Stack<ScanRef> _scanRefPool = new Stack<ScanRef>();
        readonly List<Stroke> _scanSet = new List<Stroke>();
        readonly List<Stroke> _scanGone = new List<Stroke>();
        readonly List<int> _scanFlood = new List<int>();
        bool[] _inScan = new bool[64];
        Bounds[] _scanReach = new Bounds[64];
        int _scanTick;
        bool _fullScanNext; // a scan sealed: the next covers all ink, so chains of seals close as before

        void Awake()
        {
            Instance = this;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            LineMaterial = new Material(shader);
            RuneLibrary.Warm();
            SpellParticle.PrewarmPool();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(Stroke stroke)
        {
            stroke.EnsureLine(LineMaterial);
            stroke.BornAt = Time.time; // the evaporation clock starts now
            Strokes.Add(stroke);
        }

        /// Pen lifted: the stroke becomes plain ink; runes are read when a seal closes.
        /// `silent` = repainted wall ink: skip recognition, net-send and claim.
        /// `preview: false` = pen still down (structural split): skip only the reading.
        public void CompleteStroke(Stroke s, bool allowCloseOntoInk = true, bool silent = false,
            bool preview = true)
        {
            if (s.Nodes.Count < DrawingConfig.MinStrokeNodes)
            {
                NetSync.OnLocalStrokeDropped(s); // its live preview goes too
                s.Burn();
                Strokes.Remove(s);
                return;
            }

            s.State = StrokeState.Open;
            s.CachePersistence();
            s.ComputeRawShape();
            if (silent) return;

            // grows one visible bridge segment to the nearest line end; no
            // detection tolerance is widened
            MagnetClose(s);

            // ink laid on a surface adds to the owner's claim on it (static scenery included)
            if (s.Surface != null && s.Surface.GetComponentInParent<SimpleFPSController>() == null
                && s.Surface.GetComponentInParent<Creature>() == null && !s.OnPuppet)
                InkMark.For(s.Surface, true)?.Add(s.OwnerId,
                    s.PathLength() * DrawingConfig.InkCostPerMeter);

            // one touching-cluster flood per pen-up: the reading, the net send
            // and the preview all use it
            var cluster = new List<Stroke>();
            RuneGlyph.Precognize(s, Strokes, cluster); // recognition at pen-up; seal close reads the cache
            if (s.OwnerId == Grimoire.LocalPlayerId && cluster.Count > 0)
            {
                // your own hand read as a rune: the first steps and the seal page hear it
                var (rune, score) = RuneGlyph.ReadVerdict(cluster, s.OwnerId);
                if (rune != RuneType.None && score >= DrawingConfig.MinRuneScore) FirstSteps.RuneDrawn(cluster);
            }

            NetSync.OnLocalStrokeFinished(s, cluster); // co-op: friends see your ink

            // self-closure first (a circle grazing a Y seals on itself); clients
            // close body loops only - the host closes world loops (netcode §2);
            // ink on a puppet or zombie proxy closes nothing here
            if (allowCloseOntoInk && (NetGame.IsAuthority || s.Persistent) && !s.OnPuppet
                && (TryCloseOntoSelf(s) || TryCloseOntoInk(s))) return;

            LastInk = s;
            if (preview) PreviewRune(s, cluster: cluster);
        }

        /// Read the connected drawing and float a fading label over it showing
        /// what a seal would fire: green = clean, amber = weak, ??? = fizzle.
        /// Recognized ink wears its rune's colour, so what it is can be told
        /// later from across the room; unreadable ink goes back to plain ink.
        public static void TintCluster(List<Stroke> members, RuneType rune, float score)
        {
            bool read = rune != RuneType.None && score >= DrawingConfig.MinRuneScore;
            // the colour is the power read: fuller and brighter the better the
            // rune reads, dimmer for what the game filled in
            float quality = read ? Mathf.InverseLerp(DrawingConfig.MinRuneScore, DrawingConfig.GoodRuneScore, score) : 0f;
            foreach (var m in members)
            {
                if (m == null || !m.Alive) continue;
                Color baseInk = Stroke.InkColorFor(m.OwnerId);
                if (!read) { m.SetColor(baseInk); continue; }
                float len = m.PathLength();
                float autoShare = len > 1e-4f ? Mathf.Clamp01(m.AutoLength / len) : (m.AutoDrawn ? 1f : 0f);
                float p = Mathf.Clamp01(quality) * Mathf.Lerp(1f, DrawingConfig.AutoCompletePowerMul, autoShare);
                Color tint = BiomeStamp.RuneTint(rune) * Mathf.Lerp(0.75f, 1.2f, p);
                tint.a = 1f;
                m.SetColor(Color.Lerp(baseInk, tint, Mathf.Lerp(0.55f, 0.9f, p)));
            }
        }

        /// `net` = the verdict is a re-read (erase lifted): the copies wear it too.
        /// Pen-up verdicts already ride the StrokeMsg.
        /// `cluster` = the pen-up's flood, reused while it still stands.
        void PreviewRune(Stroke seed, bool net = false, List<Stroke> cluster = null)
        {
            if (seed == null || !seed.Alive || seed.State != StrokeState.Open) return;
            if (seed.OwnerId != Grimoire.LocalPlayerId) return; // your pen only
            if (seed.Hidden()) return;

            // same touch-cluster flood the seal recognizer uses
            var members = cluster;
            if (!RuneGlyph.FloodedFor(cluster, seed))
            {
                members = new List<Stroke> { seed };
                RuneGlyph.GrowTouchingCluster(members, Strokes);
            }

            // guarded read (cache + foreign-ink fizzle) - same verdict a seal gets (netcode §1)
            var (type, score) = RuneGlyph.ReadVerdict(members, seed.OwnerId);
            TintCluster(members, type, score);
            if (net) NetSync.PushTint(members, type, score);
            string label;
            Color color;
            if (type == RuneType.None || score < DrawingConfig.MinRuneScore)
            {
                label = "???";
                color = new Color(0.78f, 0.78f, 0.78f);
            }
            else
            {
                // per-reader icon: an acolyte sees its own book's icons for the same glyph
                label = RuneLibrary.IconFor(type, Grimoire.LocalPlayerId);
                color = score >= DrawingConfig.GoodRuneScore
                    ? new Color(0.45f, 1f, 0.6f)   // clean - fires at full strength
                    : new Color(1f, 0.85f, 0.4f);  // readable but sloppy
            }

            // offset along the surface normal, not world up - keeps the label out of the body
            Vector3 pos = Vector3.zero, normal = Vector3.zero;
            int count = 0;
            foreach (var m in members)
            {
                pos += m.Centroid();
                count++;
                foreach (var n in m.Nodes)
                    if (n != null) normal += n.SurfaceNormal;
            }
            if (count == 0) return;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            RunePreview.Show(pos / count + normal * 0.22f, label, color);
        }

        /// Re-read the drawing nearest the eraser when it lifts.
        public void PreviewNear(Vector3 point)
        {
            Stroke bestStroke = null;
            float best = 0.09f; // within 0.3m of the eraser
            foreach (var s in Strokes)
            {
                if (!s.Alive || s.State != StrokeState.Open || s.Hidden()) continue;
                foreach (var n in s.Nodes)
                {
                    if (n == null) continue;
                    float d = (n.transform.position - point).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        bestStroke = s;
                    }
                }
            }
            if (bestStroke != null) PreviewRune(bestStroke, net: true);
        }

        /// A stroke whose both ends land on the same existing stroke closes a loop
        /// through it (middle included). The touched stroke is split at the two
        /// junctions; the middle becomes boundary, the rest stays ordinary ink.
        public bool TryCloseOntoInk(Stroke b)
        {
            if (b == null || b.Nodes.Count < 3) return false;
            var bFirst = b.First;
            var bLast = b.Last;
            if (bFirst == null || bLast == null) return false;
            Vector3 penLast = bLast.transform.position, penFirst = bFirst.transform.position;

            foreach (var a in Strokes)
            {
                if (a == b || !a.Alive || a.State != StrokeState.Open || a.OnPuppet) continue;
                // clients close BODY loops only - never split a world stroke the host owns (netcode §2)
                if (!NetGame.IsAuthority && !a.Persistent) continue;
                if (a.Nodes.Count < 3) continue;
                // far ink costs one box test: both pen ends need a node within CloseThreshold
                var near = a.NodeBounds();
                near.Expand(DrawingConfig.CloseThreshold * 2f + 0.001f);
                if (!near.Contains(penLast) || !near.Contains(penFirst)) continue;
                if (!a.ChainIntact() || a.Hidden()) continue;

                int i = NearestNodeIndex(a, bLast.transform.position, DrawingConfig.CloseThreshold);
                if (i < 0) continue;
                int k = NearestNodeIndex(a, bFirst.transform.position, DrawingConfig.CloseThreshold);
                if (k < 0) continue;

                int lo = Mathf.Min(i, k);
                int hi = Mathf.Max(i, k);
                if (hi - lo + 1 < 2) continue; // both ends on the same spot - nothing enclosed

                // size guards on the would-be loop (b + a[lo..hi])
                float loopLength = a.LengthBetween(lo, hi) + b.PathLength();
                int loopNodes = (hi - lo + 1) + b.Nodes.Count;
                if (loopNodes < DrawingConfig.MinLoopNodes || loopLength < DrawingConfig.MinLoopPerimeter) continue;

                // the loop must enclose something - retracing along a line is not a seal
                Vector3 junctionA = a.Nodes[k].transform.position;
                Vector3 junctionB = a.Nodes[i].transform.position;
                float bulge = Mathf.Max(
                    MaxBulge(b.Nodes, 0, b.Nodes.Count - 1, junctionA, junctionB),
                    MaxBulge(a.Nodes, lo, hi, junctionA, junctionB));
                if (bulge < DrawingConfig.MinLoopBulge) continue;

                // split A at the junctions; the outer pieces stay ink but never rune content
                if (lo > 0) SplitPiece(a, 0, lo - 1, allowTiny: false, residue: true);
                var midPiece = SplitPiece(a, lo, hi, allowTiny: true);
                if (hi < a.Nodes.Count - 1) SplitPiece(a, hi + 1, a.Nodes.Count - 1, allowTiny: false, residue: true);
                RetireSplit(a);
                if (midPiece == null) return false; // defensive; loop guards make this impossible

                b.State = StrokeState.Open;
                b.CachePersistence();
                b.ComputeRawShape();

                // loop order: b start -> b end -> touches A at i -> along A to k -> back to b start
                var loop = new List<SealDetector.LoopEntry>
                {
                    new SealDetector.LoopEntry(b, true),
                    new SealDetector.LoopEntry(midPiece, i == lo)
                };
                CreateSeal(loop, "closed onto existing ink");
                return true;
            }
            return false;
        }

        static int NearestNodeIndex(Stroke s, Vector3 pos, float maxDist)
        {
            int best = -1;
            float bestD = maxDist;
            for (int idx = 0; idx < s.Nodes.Count; idx++)
            {
                var n = s.Nodes[idx];
                if (n == null) continue;
                float d = Vector3.Distance(n.transform.position, pos);
                if (d < bestD) { bestD = d; best = idx; }
            }
            return best;
        }

        /// Pen-up self-closure: the stroke's end region touched its own earlier ink.
        /// Plain 3D node distance under SelfCloseThreshold; runs at pen-up only
        /// (mid-draw closure stays gated to the stroke's start region).
        bool TryCloseOntoSelf(Stroke b)
        {
            var nodes = b.Nodes;
            int last = nodes.Count - 1;
            if (last + 1 < DrawingConfig.MinLoopNodes) return false;
            for (int i = last; i >= 0 && b.LengthBetween(i, last) <= DrawingConfig.MidDrawCloseStartRegion; i--)
            {
                if (nodes[i] == null) continue;
                for (int j = 0; j < i; j++) // j ascending = LARGEST loop first
                {
                    if (nodes[j] == null) continue;
                    float loopLen = b.LengthBetween(j, i);
                    if (loopLen < DrawingConfig.MinLoopPerimeter) break; // only shrinks as j grows
                    if (i - j + 1 < DrawingConfig.MinLoopNodes) break;
                    float d = Vector3.Distance(nodes[i].transform.position, nodes[j].transform.position);
                    if (d > DrawingConfig.SelfCloseThreshold(loopLen)) continue;
                    Vector3 ja = nodes[j].transform.position, jb = nodes[i].transform.position;
                    if (MaxBulge(nodes, j, i, ja, jb) < DrawingConfig.MinLoopBulge) continue; // must enclose something
                    if (j > 0) SplitPiece(b, 0, j - 1, allowTiny: false, residue: true);
                    var loop = SplitPiece(b, j, i, allowTiny: true);
                    if (i < last) SplitPiece(b, i + 1, last, allowTiny: false, residue: true);
                    RetireSplit(b);
                    if (loop == null) return false;
                    CreateSeal(new List<SealDetector.LoopEntry> { new SealDetector.LoopEntry(loop, true) },
                        "end touched own ink");
                    return true;
                }
            }
            return false;
        }

        static float MaxBulge(List<DrawNode> nodes, int from, int to, Vector3 ja, Vector3 jb)
        {
            Vector3 ab = jb - ja;
            float len2 = ab.sqrMagnitude;
            float max = 0f;
            for (int idx = from; idx <= to && idx < nodes.Count; idx++)
            {
                var n = nodes[idx];
                if (n == null) continue;
                Vector3 p = n.transform.position;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - ja, ab) / len2) : 0f;
                float d = Vector3.Distance(p, ja + ab * t);
                if (d > max) max = d;
            }
            return max;
        }

        /// Move a node range out of `src` into a fresh independent stroke - just
        /// ink; recognition happens later when a seal closes around it. When
        /// `reverse` is set the node order is flipped, so an arc traversed the
        /// other way still forms a continuous ring.
        Stroke AdoptPiece(Stroke src, int from, int to, bool allowTiny, bool reverse = false, int netId = 0)
        {
            var piece = new Stroke { BasisRight = src.BasisRight, BasisUp = src.BasisUp, OwnerId = src.OwnerId,
                NetId = netId != 0 ? netId : src.NetId, LiveId = src.LiveId };
            if (reverse)
            {
                for (int idx = Mathf.Min(to, src.Nodes.Count - 1); idx >= from; idx--)
                {
                    var n = src.Nodes[idx];
                    if (n == null) continue;
                    n.SetStroke(piece);
                    piece.AddNode(n);
                }
            }
            else
            {
                for (int idx = from; idx <= to && idx < src.Nodes.Count; idx++)
                {
                    var n = src.Nodes[idx];
                    if (n == null) continue;
                    n.SetStroke(piece);
                    piece.AddNode(n);
                }
            }
            return FinishPiece(piece, src, allowTiny);
        }

        // ---- closure splits ship, so every copy splits into the same pieces (netcode §0) ----
        readonly List<NetSync.SplitPart> _splitParts = new List<NetSync.SplitPart>();

        /// AdoptPiece for a seal closure: the piece gets its own net id when
        /// this machine's split is the one that counts, and the range is recorded
        /// for the SplitMsg that RetireSplit sends.
        Stroke SplitPiece(Stroke src, int from, int to, bool allowTiny, bool reverse = false, bool residue = false)
        {
            int id = NetSync.ShipsSplit(src) ? NetSync.NextSplitId() : 0;
            var piece = AdoptPiece(src, from, to, allowTiny, reverse, id);
            if (piece != null && residue) piece.SealResidue = true; // stays ink, never rune content
            if (id != 0)
                _splitParts.Add(new NetSync.SplitPart
                    { From = from, To = to, Reverse = reverse, Tiny = allowTiny, Residue = residue, Id = id });
            return piece;
        }

        /// Retire a closure-split source and ship its pieces.
        void RetireSplit(Stroke src)
        {
            if (_splitParts.Count > 0) NetSync.PushSplit(src, _splitParts);
            _splitParts.Clear();
            src.Retire();
        }

        /// A friend's closure split, replayed on this machine's copy with the ids
        /// their seal messages will name.
        public void ApplySplit(Stroke src, int[] from, int[] to, bool[] reverse, bool[] tiny, bool[] residue, int[] ids)
        {
            if (src == null || !src.Alive) return;
            bool wasLastInk = LastInk == src;
            for (int i = 0; i < from.Length; i++)
            {
                var piece = AdoptPiece(src, from[i], to[i], tiny[i], reverse[i], ids[i]);
                if (piece == null) continue;
                if (residue[i]) piece.SealResidue = true;
                if (wasLastInk) LastInk = piece;
            }
            if (wasLastInk && LastInk == src) LastInk = null;
            src.Retire();
        }

        /// Same adoption from an explicit node list (erase-repair fragments, mid-draw tail split).
        public Stroke AdoptPiece(Stroke src, List<DrawNode> nodes, bool allowTiny)
        {
            var piece = new Stroke { BasisRight = src.BasisRight, BasisUp = src.BasisUp, OwnerId = src.OwnerId,
                NetId = src.NetId, LiveId = src.LiveId };
            foreach (var n in nodes)
            {
                if (n == null) continue;
                n.SetStroke(piece);
                piece.AddNode(n);
            }
            return FinishPiece(piece, src, allowTiny);
        }

        Stroke FinishPiece(Stroke piece, Stroke src, bool allowTiny)
        {
            if (piece.Nodes.Count == 0) return null;
            if (!allowTiny && piece.Nodes.Count < DrawingConfig.MinStrokeNodes)
            {
                foreach (var n in piece.Nodes)
                    if (n != null) Destroy(n.gameObject);
                return null;
            }

            Register(piece);
            piece.BornAt = src.BornAt; // cut ink dries when the uncut line would have
            piece.SetColor(src.Color); // a rune tint survives the split on every machine
            piece.State = StrokeState.Open;
            // body ink keeps its limb: the piece still travels, stays a puppet's, and is kept across scenes
            if (src.Persistent || src.OnPuppet) piece.Surface = src.Surface;
            piece.CachePersistence();
            piece.ComputeRawShape();
            NetSync.RegisterNetStroke(piece); // same (owner, id) as its source: burns and declares still find it
            return piece;
        }

        /// The stroke being drawn came back to its own start: close it as a seal immediately.
        public void CloseSingleStroke(Stroke s)
        {
            s.State = StrokeState.Open;
            s.CachePersistence();
            s.ComputeRawShape(); // kept for debugging/inspection, not classified
            NetSync.OnLocalStrokeFinished(s); // friends' worlds close this loop too
            // client world loop: the HOST closes it from the replicated stroke (netcode §2)
            if ((!NetGame.IsAuthority && !s.Persistent) || s.OnPuppet) return;
            CreateSeal(new List<SealDetector.LoopEntry> { new SealDetector.LoopEntry(s, true) }, "closed while drawing");
        }

        void OnEnable()
        {
            Application.onBeforeRender += RefreshInk;
            Sides.Changed += OnSideChanged;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= RefreshInk;
            Sides.Changed -= OnSideChanged;
        }

        /// A hand changed side: its plain ink takes the new side's colour
        /// (rune tints, seal gold and spent brown are left as they are).
        void OnSideChanged(int owner, Side side)
        {
            Color was = side == Side.Acolyte ? Stroke.InkColor : DrawingConfig.CorruptInkColor;
            Color now = Stroke.InkColorFor(owner);
            foreach (var s in Strokes)
                if (s.Alive && s.OwnerId == owner && s.State == StrokeState.Open && s.Color == was)
                    s.SetColor(now);
        }

        /// The player's id changed (offline id to connected owner id): their ink follows.
        public void RekeyOwner(int oldId, int newId)
        {
            if (oldId == newId) return;
            foreach (var s in Strokes)
                if (s.OwnerId == oldId) s.OwnerId = newId;
        }

        // ink follows moving surfaces: read right before the frame renders, after
        // every script has moved its bones (static ink skips its rebuild inside)
        void RefreshInk()
        {
            foreach (var s in Strokes)
                if (s.Alive) s.UpdateLine();
        }

        void Update()
        {
            AutoComplete.Tick(this); // rested drawings finish themselves

            // loose Open world ink evaporates; Persistent, drawing and seal ink are exempt
            _evapTimer -= Time.deltaTime;
            if (_evapTimer <= 0f)
            {
                _evapTimer = 1f;
                GatherOwnerSpots();
                for (int i = Strokes.Count - 1; i >= 0; i--)
                {
                    var s = Strokes[i];
                    if (s == null || !s.Alive) { Strokes.RemoveAt(i); continue; }
                    if (s.State != StrokeState.Open) continue;
                    // rune-wall ink IS the saved sample pool - it never dries
                    if (s.Surface != null && s.Surface.GetComponentInParent<RuneWall>() != null) continue;
                    // left behind: the HOST decides, every machine burns it and the
                    // owner's wand gets it back there. Ink nobody else holds
                    // (NetId 0) keeps the local rule.
                    if ((NetGame.IsAuthority || s.NetId == 0) && LeftBehind(s))
                    {
                        ReturnToWand(s);
                        NetSync.PushLeashBurn(s);
                        s.Burn(); Strokes.RemoveAt(i); continue;
                    }
                    if (s.Persistent) continue;
                    float over = (Time.time - s.BornAt) - DrawingConfig.InkEvaporateSeconds;
                    if (over <= 0f) continue;
                    float k = 1f - over / Mathf.Max(0.5f, DrawingConfig.InkEvaporateFadeSeconds);
                    if (k <= 0f) { s.Burn(); Strokes.RemoveAt(i); }
                    else s.SetEvaporation(k); // thins visibly before it goes
                }
            }

            // active seals: integrity + duration
            for (int i = ActiveSeals.Count - 1; i >= 0; i--)
                if (!ActiveSeals[i].Tick(Time.deltaTime))
                    ActiveSeals.RemoveAt(i);

            // periodic scans - loop detection, spent re-arming, erase repair, ink budget
            _detectTimer -= Time.deltaTime;
            if (_detectTimer <= 0f)
            {
                _detectTimer = DrawingConfig.DetectInterval;
                Strokes.RemoveAll(s => !s.Alive);
                TickSpentGroups();
                RepairErasedStrokes();
                EnforceInkBudget();
                Detect();
            }
        }

        /// Run loop detection on the next frame instead of waiting out the periodic interval.
        public void RequestDetect() { _detectTimer = 0f; _forceDetect = true; }

        void Detect()
        {
            _eligibleCache.Clear();
            foreach (var s in Strokes)
            {
                if (!s.Alive) continue;
                // the stroke being drawn is excluded: mid-draw only the back-to-start self-close applies
                if (s.State != StrokeState.Open) continue;
                if (s.Nodes.Count < 3) continue;
                if (!s.ChainIntact()) continue;
                if (s.Hidden()) continue; // stowed-weapon ink doesn't exist right now
                if (s.OnPuppet) continue; // a friend's body ink copy: its owner seals it, not this machine
                if (!NetGame.IsAuthority && !s.Persistent) continue; // clients scan BODY ink only (netcode §2)
                _eligibleCache.Add(s);
            }
            if (_eligibleCache.Count == 0) return;

            // held-still gate: skip the detectors unless sampled ink moved beyond
            // sway, the stroke set changed, or a caller forced the scan
            if (!_forceDetect && InkHeldStill()) return;
            bool forced = _forceDetect;
            _forceDetect = false;
            SnapshotInk();

            // the detectors see only ink that changed since they last saw it, plus
            // all ink it reaches; null = ink only went away, which closes nothing
            var scan = ScanSet(_fullScanNext, forced);
            _fullScanNext = false;
            if (scan == null) return;

            // both detectors always run and the largest seal wins - a small
            // sub-loop must never steal the intended boundary
            SealDetector.LastNearMiss = null;
            var loop = SealDetector.FindLoop(scan);
            float loopPerim = loop != null ? SealDetector.LoopPerimeter(loop) : -1f;

            var cross = CrossingFinder.Find(scan);
            float crossPerim = cross.Valid ? cross.Perimeter : -1f;

            if (loop != null && loopPerim >= crossPerim)
            {
                _fullScanNext = true;
                CreateSeal(loop, loop.Count == 1 ? "endpoints met" : $"{loop.Count} strokes linked");
                return;
            }
            if (cross.Valid)
            {
                _fullScanNext = true;
                ApplyCrossingLoop(cross);
                return;
            }

            // surface why an almost-loop was refused (once per changed reason)
            string why = SealDetector.LastNearMiss ?? CrossingFinder.LastNearMiss;
            if (why != null && why != _lastNearMissShown)
            {
                _lastNearMissShown = why;
                LogEvent(why);
            }
        }

        // held-still gate: up to seven samples per stroke - both ends plus every Nth node
        static int SampleStep(Stroke s) => Mathf.Max(1, s.Nodes.Count / 6);

        /// Cheap identity of the eligible set - membership or node-count
        /// changes (new ink, erase splits, seals releasing their strokes)
        /// must always re-scan even when nothing moved.
        int InkSig()
        {
            int sig = _eligibleCache.Count;
            foreach (var s in _eligibleCache)
                sig = sig * 31 + s.Id * 17 + s.Nodes.Count;
            return sig;
        }

        void SnapshotInk()
        {
            _detectSnap.Clear();
            _detectSnapSig = InkSig();
            foreach (var s in _eligibleCache)
            {
                int step = SampleStep(s);
                for (int i = 0; i < s.Nodes.Count; i += step)
                {
                    var n = s.Nodes[i];
                    _detectSnap.Add(n != null ? n.transform.position : Vector3.zero);
                }
                var last = s.Nodes[s.Nodes.Count - 1];
                _detectSnap.Add(last != null ? last.transform.position : Vector3.zero);
            }
        }

        /// True when every sampled node sits where the last real scan saw it.
        /// Tolerance is a fifth of CloseThreshold, so tuning the threshold carries the gate.
        bool InkHeldStill()
        {
            if (InkSig() != _detectSnapSig) return false;
            float jiggle = DrawingConfig.CloseThreshold * 0.2f;
            float j2 = jiggle * jiggle;
            int k = 0;
            foreach (var s in _eligibleCache)
            {
                int step = SampleStep(s);
                for (int i = 0; i < s.Nodes.Count; i += step)
                {
                    if (k >= _detectSnap.Count) return false;
                    var n = s.Nodes[i];
                    Vector3 p = n != null ? n.transform.position : Vector3.zero;
                    if ((p - _detectSnap[k++]).sqrMagnitude > j2) return false;
                }
                if (k >= _detectSnap.Count) return false;
                var last = s.Nodes[s.Nodes.Count - 1];
                Vector3 lp = last != null ? last.transform.position : Vector3.zero;
                if ((lp - _detectSnap[k++]).sqrMagnitude > j2) return false;
            }
            return k == _detectSnap.Count;
        }

        /// What the detectors scan: strokes new, split or moved since the last scan
        /// that saw them, flooded through every stroke whose reach box meets one
        /// already in (whole connected ink, never part of it). Untouched ink was
        /// scanned as it is; ink that left may have blocked a ring, so what it
        /// touched is scanned. Null = nothing to scan.
        IReadOnlyList<Stroke> ScanSet(bool full, bool forced)
        {
            int n = _eligibleCache.Count;
            if (_inScan.Length < n)
            {
                int cap = Mathf.NextPowerOfTwo(n);
                _inScan = new bool[cap];
                _scanReach = new Bounds[cap];
            }
            _scanTick++;
            _scanFlood.Clear();
            for (int i = 0; i < n; i++)
            {
                var s = _eligibleCache[i];
                bool changed = !_scanRefs.TryGetValue(s, out var r) || !SameInk(s, r);
                if (changed)
                {
                    if (r == null)
                    {
                        r = _scanRefPool.Count > 0 ? _scanRefPool.Pop() : new ScanRef();
                        _scanRefs[s] = r;
                    }
                    RememberInk(s, r); // every changed stroke is scanned this tick
                    _scanFlood.Add(i);
                }
                r.Tick = _scanTick;
                _inScan[i] = changed;
                _scanReach[i] = r.Reach;
            }
            // ink that left the set counts as new if it comes back
            _scanGone.Clear();
            foreach (var kv in _scanRefs)
                if (kv.Value.Tick != _scanTick) _scanGone.Add(kv.Key);
            foreach (var s in _scanGone)
            {
                var gone = _scanRefs[s];
                for (int i = 0; i < n; i++)
                    if (!_inScan[i] && gone.Reach.Intersects(_scanReach[i]))
                    {
                        _inScan[i] = true;
                        _scanFlood.Add(i);
                    }
                _scanRefPool.Push(gone);
                _scanRefs.Remove(s);
            }

            if (full) return _eligibleCache;
            // a forced scan with nothing changed runs whole, as it always did
            if (_scanFlood.Count == 0) return forced ? _eligibleCache : null;
            for (int k = 0; k < _scanFlood.Count && _scanFlood.Count < n; k++)
            {
                Bounds m = _scanReach[_scanFlood[k]];
                for (int i = 0; i < n; i++)
                    if (!_inScan[i] && m.Intersects(_scanReach[i]))
                    {
                        _inScan[i] = true;
                        _scanFlood.Add(i);
                    }
            }
            if (_scanFlood.Count == n) return _eligibleCache;
            _scanSet.Clear();
            for (int i = 0; i < n; i++)
                if (_inScan[i]) _scanSet.Add(_eligibleCache[i]); // registry order, as a full scan sees it
            return _scanSet;
        }

        /// Record a stroke's ink as the detectors are about to see it.
        static void RememberInk(Stroke s, ScanRef r)
        {
            var nodes = s.Nodes;
            r.Nodes = nodes.Count;
            var carrier = nodes[0].transform.parent;
            var box = new Bounds(nodes[0].transform.position, Vector3.zero);
            bool many = false;
            for (int i = 1; i < nodes.Count; i++)
            {
                var t = nodes[i].transform;
                box.Encapsulate(t.position);
                if (t.parent != carrier) many = true;
            }
            r.AllNodes = many;
            r.Pts.Clear();
            int step = many ? 1 : SampleStep(s);
            for (int i = 0; i < nodes.Count; i += step) r.Pts.Add(nodes[i].transform.position);
            r.Pts.Add(nodes[nodes.Count - 1].transform.position);
            // two reach boxes meet whenever either detector could join their ink:
            // crossings weld within 3 touch widths, open ends are named within
            // 3 x CloseThreshold (Expand grows each face by half)
            box.Expand(Mathf.Max(DrawingConfig.InkTouchDistance, DrawingConfig.CloseThreshold) * 3f + 0.001f);
            r.Reach = box;
        }

        /// True when the stroke's ink sits exactly where its last scan saw it.
        static bool SameInk(Stroke s, ScanRef r)
        {
            var nodes = s.Nodes;
            if (r.Nodes != nodes.Count) return false;
            int step = r.AllNodes ? 1 : SampleStep(s);
            int k = 0;
            for (int i = 0; i < nodes.Count; i += step)
                if (!nodes[i].transform.position.Equals(r.Pts[k++])) return false;
            return nodes[nodes.Count - 1].transform.position.Equals(r.Pts[k]);
        }

        /// Turn a detected crossing cycle into a seal: split every crossed stroke
        /// at its crossings, adopt the enclosed arcs as boundary (in ring order),
        /// leave the leftover tails as ordinary ink.
        void ApplyCrossingLoop(CrossingFinder.Result r)
        {
            // group the cycle's arcs by the stroke they came from
            var byStroke = new Dictionary<Stroke, List<int>>();
            for (int k = 0; k < r.Cycle.Count; k++)
            {
                var stroke = r.Cycle[k].Stroke;
                if (!byStroke.TryGetValue(stroke, out var list)) { list = new List<int>(); byStroke[stroke] = list; }
                list.Add(k);
            }

            var pieceForArc = new Stroke[r.Cycle.Count];

            foreach (var kv in byStroke)
            {
                var stroke = kv.Key;
                var arcIndices = kv.Value;
                arcIndices.Sort((x, y) => r.Cycle[x].Lo.CompareTo(r.Cycle[y].Lo));
                int end = stroke.Nodes.Count - 1;
                int cursor = 0;

                foreach (var ai in arcIndices)
                {
                    var arc = r.Cycle[ai];
                    if (arc.Lo > cursor)
                        SplitPiece(stroke, cursor, arc.Lo - 1, allowTiny: false, residue: true); // leftover ink
                    pieceForArc[ai] = SplitPiece(stroke, arc.Lo, arc.Hi, allowTiny: true, reverse: arc.Reversed);
                    cursor = arc.Hi + 1;
                }
                if (cursor <= end) SplitPiece(stroke, cursor, end, allowTiny: false, residue: true);
                RetireSplit(stroke);
            }

            var boundary = new List<SealDetector.LoopEntry>();
            for (int k = 0; k < r.Cycle.Count; k++)
            {
                if (pieceForArc[k] == null)
                {
                    // should be unreachable; the sources are already split/retired,
                    // so a silent return would consume the drawing with no seal
                    LogEvent("crossing seal ABORTED mid-adopt. report this drawing");
                    return;
                }
                boundary.Add(new SealDetector.LoopEntry(pieceForArc[k], true));
            }
            if (boundary.Count == 0) return;

            CreateSeal(boundary, boundary.Count == 1 ? "self-crossing" : $"{boundary.Count} arcs enclosed");
        }

        /// Every line of the loop is body ink (a player's or a zombie's), none of it the world's.
        static bool BodyLoop(List<SealDetector.LoopEntry> loop)
        {
            foreach (var e in loop)
                if (e.Stroke == null || !e.Stroke.Persistent) return false;
            return true;
        }

        void CreateSeal(List<SealDetector.LoopEntry> loop, string how)
        {
            // CLIENT: only BODY seals live here - body ink never replicates, so
            // detection stays owner-side and the CAST ships to the host (netcode §2)
            if (!NetGame.IsAuthority)
                foreach (var e in loop)
                    if (e.Stroke == null || !e.Stroke.Persistent)
                    {
                        LogEvent("world seals close on the host. the ink stays ink here");
                        return;
                    }

            // one cast per closure: a re-closed body seal stays spent until the
            // loop opens past ReArmDistance and re-closes (or is redrawn)
            string key = CastKey(loop);
            if (key != null && _castKeys.Contains(key))
            {
                var pairs = JunctionPairs(loop);
                if (pairs.Count > 0)
                {
                    var strokes = new List<Stroke>();
                    foreach (var e in loop)
                        if (e.Stroke.Alive && e.Stroke.State != StrokeState.Spent)
                        {
                            e.Stroke.State = StrokeState.Spent;
                            e.Stroke.SetColor(Stroke.SpentColor);
                            strokes.Add(e.Stroke);
                        }
                    NetSync.PushInkState(strokes, NetSync.InkLookSpent);
                    RegisterSpentGroup(strokes, pairs, new List<SealDetector.LoopEntry>(loop));
                }
                return;
            }

            var seal = new Seal(loop);
            seal.CapturePayload(Strokes);
            ActiveSeals.Add(seal);
            if (key != null) _castKeys.Add(key);
            LogEvent($"SEAL #{seal.Id} ACTIVATED ({how}): {seal.Describe()} | player {seal.OwnerId}");
            Juice.Sound(Sfx.SealComplete, seal.PlaneOrigin);
            // an acolyte's seal on a body makes no spell the others are told of: its close reaches them here
            if (Grimoires.HeldBy(seal.OwnerId) == BookKind.Acolyte && BodyLoop(loop))
                NetSync.PushInkFx(NetSync.InkFxChime, seal.PlaneOrigin);
            NetSync.PushSealLook(seal, NetSync.InkLookSealed); // gold ring and rune tints on every machine
            if (seal.OwnerId == Grimoire.LocalPlayerId)
            {
                int sealed_ = 0;
                foreach (var glyph in seal.Runes) if (glyph != null && glyph.Rune != RuneType.None) sealed_++;
                FirstSteps.SealDrawn(sealed_);
                if (BodyLoop(loop)) Achievements.Unlock(Achievements.BodyCast);
            }

            if (!NetGame.IsAuthority)
            {
                NetSync.SendBodySealFire(seal); // the host builds the spell (netcode §2)
                SealGallery.Capture(seal, null);
                return;
            }

            SpellLock.NotifySeal(seal);

            // spell resolution: physics-rune zones + ComboBook announcements
            var surface = ResolveSealSurface(seal);
            var spell = Spell.Create(seal, surface);
            if (spell != null) seal.AttachSpell(spell);
            CoCast.Fired(seal.OwnerId, seal.CoCasters, spell == null); // everyone whose ink made it shares its kills

            NetSync.PushSeal(seal); // clients see the gold ring (netcode §2)

            // end-of-round gallery snapshot (ink positions are live right now)
            SealGallery.Capture(seal, null); // no combo names
        }

        /// The material under the seal - raycast onto the surface just behind the
        /// seal plane; unmarked surfaces resolve to Unknown (neutral defaults).
        SurfaceMaterialType ResolveSealSurface(Seal seal)
        {
            if (Physics.Raycast(seal.PlaneOrigin + seal.PlaneNormal * 0.25f, -seal.PlaneNormal,
                    out var hit, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // painted terrain: material comes from the layer painted under this spot
                var painted = hit.collider.GetComponent<TerrainSurfaceMap>();
                if (painted != null) return painted.MaterialAt(hit.point);
                return SurfaceMaterialDB.Resolve(hit.collider);
            }
            return SurfaceMaterialType.Unknown;
        }

        public void OnSealEnded(Seal seal, string message, bool resolved)
        {
            seal.Spell?.End(); // spell cancels the instant the seal breaks or expires
            if (resolved && seal.OwnerId == Grimoire.LocalPlayerId) Achievements.Unlock(Achievements.FirstSpell);
            NetSync.PushSealEnd(seal, resolved); // clients drop the ring, burn matching ink (netcode §2)
            NetSync.PushSealLook(seal, resolved ? NetSync.InkLookSpent : NetSync.InkLookPlain); // the copies' colours follow
            LogEvent(message);
        }

        // ---- spent ink (characters & weapons) ----

        public void RegisterSpentGroup(List<Stroke> strokes, List<(DrawNode a, DrawNode b)> pairs,
            List<SealDetector.LoopEntry> boundary)
        {
            // no surviving junctions means the loop can never close again
            // (its other half burned) - hand the ink straight back
            if (pairs.Count == 0)
            {
                ReleaseSpent(strokes);
                return;
            }

            // a mixed boundary (body + environment ink) can never re-close after
            // its environment strokes burn - hand the ink straight back
            foreach (var e in boundary)
                if (e.Stroke == null || !e.Stroke.Alive || !e.Stroke.Persistent)
                {
                    ReleaseSpent(strokes);
                    return;
                }

            // one owner per stroke: the newest group owns the ink, stale trackers drop
            _spentGroups.RemoveAll(old =>
            {
                foreach (var s in old.Strokes)
                    if (strokes.Contains(s)) return true;
                return false;
            });

            _spentGroups.Add(new SpentGroup { Strokes = strokes, Pairs = pairs, Boundary = boundary });
        }

        /// Spent seals re-cast one way: the loop opens past ReArmDistance then
        /// re-closes within ReCloseDistance (a Schmitt trigger).
        void TickSpentGroups()
        {
            for (int i = _spentGroups.Count - 1; i >= 0; i--)
            {
                var g = _spentGroups[i];

                // stowed weapon: spent seals freeze until it is drawn again
                bool stowed = false;
                foreach (var s in g.Strokes)
                    if (s.Hidden()) { stowed = true; break; }
                if (stowed) continue;

                // damaged ink can't seal - release it (checked every tick so armed groups can't leak)
                bool damaged = false;
                foreach (var s in g.Strokes)
                    if (!s.Alive || !s.ChainIntact()) { damaged = true; break; }
                if (damaged)
                {
                    _castKeys.Remove(CastKey(g.Boundary) ?? "");
                    ReleaseSpent(g.Strokes);
                    _spentGroups.RemoveAt(i);
                    continue;
                }
                float widest = 0f;
                foreach (var (a, b) in g.Pairs)
                {
                    if (a == null || b == null) { widest = float.MaxValue; break; }
                    widest = Mathf.Max(widest, Vector3.Distance(a.transform.position, b.transform.position));
                }

                if (!g.Armed)
                {
                    // re-arm once the loop breaks open. The strokes go back to Open
                    // so they read/erase/chain normally while broken, but the GROUP
                    // is kept so it can re-fire itself on re-close.
                    if (widest > DrawingConfig.ReArmDistance)
                    {
                        _castKeys.Remove(CastKey(g.Boundary) ?? ""); // opened wide: earned a fresh cast
                        ReleaseSpent(g.Strokes);
                        g.Armed = true;
                        LogEvent("Spent seal re-armed: the loop opened");
                    }
                }
                else if (widest <= DrawingConfig.ReCloseDistance)
                {
                    // the pose closed the loop again: re-fire it DIRECTLY from the
                    // kept boundary (skip only if Detect already re-used the ink).
                    _spentGroups.RemoveAt(i);
                    if (BoundaryReady(g))
                        CreateSeal(new List<SealDetector.LoopEntry>(g.Boundary), "body seal re-closed by posing");
                }
            }
        }

        /// A spent loop's boundary is castable again only while every stroke is
        /// live, whole, present, and not already re-used by another seal.
        static bool BoundaryReady(SpentGroup g)
        {
            foreach (var e in g.Boundary)
            {
                var s = e.Stroke;
                if (s == null || !s.Alive || !s.ChainIntact() || s.Hidden()
                    || s.State == StrokeState.InSeal) return false;
            }
            return true;
        }

        /// Boundary fingerprint for the one-cast-per-closure rule. Null when
        /// any stroke is environment ink (consumed on cast, can't loop).
        static string CastKey(List<SealDetector.LoopEntry> loop)
        {
            var ids = new List<int>();
            foreach (var e in loop)
            {
                if (e.Stroke == null || !e.Stroke.Persistent) return null;
                ids.Add(e.Stroke.Id);
            }
            ids.Sort();
            return string.Join(",", ids);
        }

        /// The junction node pairs where a loop can physically open, same
        /// walk Seal.Expire does.
        static List<(DrawNode a, DrawNode b)> JunctionPairs(List<SealDetector.LoopEntry> loop)
        {
            var pairs = new List<(DrawNode a, DrawNode b)>();
            for (int i = 0; i < loop.Count; i++)
            {
                var cur = loop[i];
                var next = loop[(i + 1) % loop.Count];
                var exit = cur.Forward ? cur.Stroke.Last : cur.Stroke.First;
                var entry = next.Forward ? next.Stroke.First : next.Stroke.Last;
                if (exit != null && entry != null) pairs.Add((exit, entry));
            }
            return pairs;
        }

        static void ReleaseSpent(List<Stroke> strokes)
        {
            foreach (var s in strokes)
            {
                if (!s.Alive || s.State != StrokeState.Spent) continue;
                s.State = StrokeState.Open;
                s.SetColor(Stroke.InkColorFor(s.OwnerId));
                s.SetLoop(false);
            }
            NetSync.PushInkState(strokes, NetSync.InkLookPlain); // re-armed everywhere
        }

        /// Perf guard: characters/weapons carry bounded ink, but the environment
        /// doesn't - fade the oldest unsealed world scribbles beyond the cap.
        /// Where each player's body stands on this machine: the local pilot and
        /// every friend's puppet. Their ink is measured from there.
        void GatherOwnerSpots()
        {
            _ownerAt.Clear();
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) { _ownerAt[Grimoire.LocalPlayerId] = p.transform.position; break; }
            foreach (var a in NetAvatar.All)
                if (a != null) _ownerAt[NetSync.OwnerIdOf(a.Id)] = a.transform.position;
        }

        /// Loose ink farther than InkLeashMeters from its owner's body. Ink on a
        /// body rides that body and is never left behind. The pieces an erase
        /// left share one wire id, so they go together or not at all.
        bool LeftBehind(Stroke s)
        {
            if (!FarFromOwner(s)) return false;
            if (s.NetId == 0) return true;
            _leashBuf.Clear();
            NetSync.PiecesOf(s.OwnerId, s.NetId, _leashBuf);
            foreach (var p in _leashBuf)
                if (p != s && !FarFromOwner(p)) return false;
            return true;
        }
        static readonly List<Stroke> _leashBuf = new List<Stroke>();

        bool FarFromOwner(Stroke s)
        {
            float leash = DrawingConfig.InkLeashMeters;
            if (leash <= 0f || s.Nodes.Count == 0 || !_ownerAt.TryGetValue(s.OwnerId, out var at)) return false;
            if (s.Surface != null && (s.Surface.GetComponentInParent<SimpleFPSController>() != null
                || s.Surface.GetComponentInParent<NetAvatar>() != null)) return false;
            float r2 = leash * leash;
            foreach (var n in s.Nodes)
                if (n != null && (n.transform.position - at).sqrMagnitude <= r2) return false;
            return true;
        }

        /// The owner's own machine takes the ink back at the share it was paid;
        /// a disguised acolyte's waits in the reserve, as a scan does, a wizard's
        /// wand takes it at once (only acolytes drain the reserve).
        public static void ReturnToWand(Stroke s)
        {
            if (s.OwnerId != Grimoire.LocalPlayerId) return;
            foreach (var ink in PlayerInk.All)
            {
                var pilot = ink != null ? ink.GetComponent<SimpleFPSController>() : null;
                if (pilot == null || !pilot.IsLocalViewer) continue;
                float worth = s.PathLength() * DrawingConfig.InkCostPerMeter * ink.DrawRate;
                if (ShapeShift.LocalIsShaped && Sides.IsAcolyte(Grimoire.LocalPlayerId)) ink.Store(worth);
                else ink.Award(worth);
                return;
            }
        }

        void EnforceInkBudget()
        {
            // the census filter must match the burn loop's filter, or the cap can never be satisfied
            int env = 0;
            foreach (var s in Strokes)
                if (s.Alive && !s.Persistent && s.State == StrokeState.Open) env++;
            if (env <= DrawingConfig.MaxEnvironmentStrokes) return;

            int burned = 0;
            foreach (var s in Strokes) // registration order = oldest first
            {
                if (env - burned <= DrawingConfig.MaxEnvironmentStrokes) break;
                if (s.Alive && !s.Persistent && s.State == StrokeState.Open)
                {
                    s.Burn();
                    burned++;
                }
            }
            if (burned > 0)
                LogEvent($"Old environment ink faded ({burned} strokes) at the world cap of {DrawingConfig.MaxEnvironmentStrokes}");
        }

        /// Erasing punches holes in strokes; a holed stroke can never seal. Split
        /// into surviving contiguous pieces with fresh endpoints; specks below
        /// MinStrokeNodes vanish.
        void RepairErasedStrokes()
        {
            for (int i = Strokes.Count - 1; i >= 0; i--)
            {
                var s = Strokes[i];
                if (!s.Alive || s.State != StrokeState.Open) continue;
                if (!s.HasDestroyedNodes()) continue;

                bool wasLastInk = s == LastInk;
                if (wasLastInk) LastInk = null;
                // your own body ink the others hold: they take what is left of it, piece by piece
                bool resend = s.NetId != 0 && s.Persistent && !s.OnPuppet && s.OwnerId == Grimoire.LocalPlayerId;
                _cutPieces.Clear();

                var runs = new List<List<DrawNode>>();
                List<DrawNode> run = null;
                foreach (var n in s.Nodes)
                {
                    if (n == null) { run = null; continue; }
                    if (run == null)
                    {
                        run = new List<DrawNode>();
                        runs.Add(run);
                    }
                    run.Add(n);
                }

                foreach (var fragment in runs)
                {
                    var piece = AdoptPiece(s, fragment, allowTiny: false); // specks below MinStrokeNodes vanish
                    if (piece != null && wasLastInk) LastInk = piece; // keep the recording anchor alive
                    if (piece != null && resend) _cutPieces.Add(piece);
                }

                s.Retire(); // nodes now belong to the pieces
                if (resend) NetSync.OnOwnBodyInkCut(s.NetId, _cutPieces);
            }
        }
        readonly List<Stroke> _cutPieces = new List<Stroke>();

        /// Grow the last step to the nearest line end (own start included). Only
        /// the lifted end is assisted; one bridge per pen-up, as visible ink.
        void MagnetClose(Stroke s)
        {
            float reach = DrawingConfig.MagnetCloseRange;
            var node = s.Last;
            if (reach <= 0f || node == null) return;
            Vector3 p = node.transform.position;
            Vector3 best = Vector3.zero;
            float bestD = reach;
            bool found = false;
            void Try(DrawNode cap)
            {
                if (cap == null || cap == node) return;
                float d = Vector3.Distance(p, cap.transform.position);
                if (d > DrawingConfig.CloseThreshold * 0.9f && d < bestD)
                { bestD = d; best = cap.transform.position; found = true; }
            }
            Try(s.First);
            foreach (var t in Strokes)
            {
                if (t == null || t == s || !t.Alive || t.State == StrokeState.Drawing) continue; // a friend's live line is not an end yet
                if (t.Persistent != s.Persistent) continue; // body bridges body, world bridges world
                Try(t.First);
                Try(t.Last);
            }
            if (found)
                s.AddNode(DrawNode.Create(s, s.Nodes.Count, best,
                    node.SurfaceNormal, s.Surface));
        }

        public void EraseAt(Vector3 point, float radius) => EraseAlong(point, point, radius);

        /// Erase a thin track along the cursor's path between frames (a swept
        /// segment, so a fast hand can't skip over nodes). `scoopInto`: rubbed-out
        /// ink flows back into that pool; null = plain erase.
        public void EraseAlong(Vector3 from, Vector3 to, float radius, PlayerInk scoopInto = null,
            bool netSend = true)
        {
            if (netSend) NetSync.OnLocalErase(from, to, radius); // ink graphs must not drift (netcode §2)
            Vector3 seg = to - from;
            float len2 = seg.sqrMagnitude;
            float r2 = radius * radius;

            // cull to the sweep box: the cursor segment grown by the eraser radius
            // (Bounds.Expand grows each face by half the amount, so x2 = radius)
            var sweep = new Bounds(from, Vector3.zero);
            sweep.Encapsulate(to);
            sweep.Expand(radius * 2f);

            // near-miss channel: also look a few radii around the sweep box
            var missBox = sweep;
            missBox.Expand(radius * 6f);

            foreach (var s in Strokes)
            {
                if (!s.Alive || s.Hidden()) continue; // can't rub out invisible ink
                if (!missBox.Intersects(RuneGlyph.StrokeBounds(s))) continue;
                bool rubbed = false;
                foreach (var n in s.Nodes)
                {
                    if (n == null) continue;
                    Vector3 p = n.transform.position;
                    float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - from, seg) / len2) : 0f;
                    float d2 = (p - (from + seg * t)).sqrMagnitude;
                    if (d2 <= r2)
                    {
                        // refund below full worth: scooping must never be a free ink loop
                        scoopInto?.Award(DrawingConfig.NodeSpacing * DrawingConfig.InkCostPerMeter
                            * DrawingConfig.ScoopRefund * scoopInto.DrawRate);
                        Destroy(n.gameObject);
                        ErasedTotal++;
                        rubbed = true;
                        SfxLoops.Rub(n.transform.position);
                    }
                    else if (d2 < radius * 4f * (radius * 4f))
                    {
                        // remember near misses so the eraser can say why nothing was erased
                        float d = Mathf.Sqrt(d2);
                        if (Time.time > LastEraseMissTime + 0.5f || d < LastEraseMissDist)
                        {
                            LastEraseMissDist = d;
                            LastEraseMissTime = Time.time;
                        }
                    }
                }
                // only the player's own eraser may clear a rune wall's saved
                // pool - stamp the wall this rub worked
                if (rubbed && s.Surface != null)
                    s.Surface.GetComponentInParent<RuneWall>()?.NoteHandErase();
            }
        }

        /// Lifetime count of erased nodes + the eraser's nearest recent miss -
        /// SurfaceDrawer reads these to speak when a whole rub erased nothing.
        public static int ErasedTotal;
        public static float LastEraseMissDist = float.MaxValue;
        public static float LastEraseMissTime = -999f;

        public void LogEvent(string msg)
        {
            using (PerfMarkers.Logs.Auto()) Debug.Log($"[SpellyZombie] {msg}");
        }

    }
}
