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
        readonly List<Stroke> _eligibleCache = new List<Stroke>();
        string _lastNearMissShown;
        bool _forceDetect;
        // sampled ink geometry at the last REAL scan - the held-still gate
        // (see Detect) compares against this before paying for the detectors
        readonly List<Vector3> _detectSnap = new List<Vector3>();
        int _detectSnapSig;
        bool _inkDebug; // F12: show what the detector sees (stroke endpoints)

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
                && s.Surface.GetComponentInParent<Creature>() == null)
                InkMark.For(s.Surface, true)?.Add(s.OwnerId,
                    s.PathLength() * DrawingConfig.InkCostPerMeter);

            RuneGlyph.Precognize(s, Strokes); // recognition at pen-up; seal close reads the cache

            NetSync.OnLocalStrokeFinished(s); // co-op: friends see your ink

            // self-closure first (a circle grazing a Y seals on itself); clients
            // close body loops only - the host closes world loops (netcode §2)
            if (allowCloseOntoInk && (NetGame.IsAuthority || s.Persistent)
                && (TryCloseOntoSelf(s) || TryCloseOntoInk(s))) return;

            LastInk = s;
            if (preview) PreviewRune(s);
        }

        /// Read the connected drawing and float a fading label over it showing
        /// what a seal would fire: green = clean, amber = weak, ??? = fizzle.
        void PreviewRune(Stroke seed)
        {
            if (seed == null || !seed.Alive || seed.State != StrokeState.Open) return;
            if (seed.OwnerId != Grimoire.LocalPlayerId) return; // your pen only
            if (seed.Hidden()) return;

            // same touch-cluster flood the seal recognizer uses
            var members = new List<Stroke> { seed };
            RuneGlyph.GrowTouchingCluster(members, Strokes);

            // guarded read (cache + foreign-ink fizzle) - same verdict a seal gets (netcode §1)
            var (type, score) = RuneGlyph.ReadVerdict(members, seed.OwnerId);
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
            if (bestStroke != null) PreviewRune(bestStroke);
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

            foreach (var a in Strokes)
            {
                if (a == b || !a.Alive || a.State != StrokeState.Open) continue;
                // clients close BODY loops only - never split a world stroke the host owns (netcode §2)
                if (!NetGame.IsAuthority && !a.Persistent) continue;
                if (a.Nodes.Count < 3 || !a.ChainIntact() || a.Hidden()) continue;

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
                var beforePiece = lo > 0 ? AdoptPiece(a, 0, lo - 1, allowTiny: false) : null;
                var midPiece = AdoptPiece(a, lo, hi, allowTiny: true);
                var afterPiece = hi < a.Nodes.Count - 1 ? AdoptPiece(a, hi + 1, a.Nodes.Count - 1, allowTiny: false) : null;
                if (beforePiece != null) beforePiece.SealResidue = true;
                if (afterPiece != null) afterPiece.SealResidue = true;
                a.Retire();
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
                    Stroke leadIn = j > 0 ? AdoptPiece(b, 0, j - 1, allowTiny: false) : null;
                    var loop = AdoptPiece(b, j, i, allowTiny: true);
                    Stroke hookTail = i < last ? AdoptPiece(b, i + 1, last, allowTiny: false) : null;
                    if (leadIn != null) leadIn.SealResidue = true;     // stays ink, never rune content
                    if (hookTail != null) hookTail.SealResidue = true;
                    b.Retire();
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
        Stroke AdoptPiece(Stroke src, int from, int to, bool allowTiny, bool reverse = false)
        {
            var piece = new Stroke { BasisRight = src.BasisRight, BasisUp = src.BasisUp, OwnerId = src.OwnerId, NetId = src.NetId };
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
            return FinishPiece(piece, allowTiny);
        }

        /// Same adoption from an explicit node list (erase-repair fragments, mid-draw tail split).
        public Stroke AdoptPiece(Stroke src, List<DrawNode> nodes, bool allowTiny)
        {
            var piece = new Stroke { BasisRight = src.BasisRight, BasisUp = src.BasisUp, OwnerId = src.OwnerId, NetId = src.NetId };
            foreach (var n in nodes)
            {
                if (n == null) continue;
                n.SetStroke(piece);
                piece.AddNode(n);
            }
            return FinishPiece(piece, allowTiny);
        }

        Stroke FinishPiece(Stroke piece, bool allowTiny)
        {
            if (piece.Nodes.Count == 0) return null;
            if (!allowTiny && piece.Nodes.Count < DrawingConfig.MinStrokeNodes)
            {
                foreach (var n in piece.Nodes)
                    if (n != null) Destroy(n.gameObject);
                return null;
            }

            Register(piece);
            piece.State = StrokeState.Open;
            piece.CachePersistence();
            piece.ComputeRawShape();
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
            if (!NetGame.IsAuthority && !s.Persistent) return;
            CreateSeal(new List<SealDetector.LoopEntry> { new SealDetector.LoopEntry(s, true) }, "closed while drawing");
        }

        void Update()
        {
            // F12: toggle ink debug (endpoint dots)
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f12Key.wasPressedThisFrame) _inkDebug = !_inkDebug;

            // ink follows moving surfaces (static ink skips its rebuild internally)
            foreach (var s in Strokes)
                if (s.Alive) s.UpdateLine();

            // loose Open world ink evaporates; Persistent, drawing and seal ink are exempt
            _evapTimer -= Time.deltaTime;
            if (_evapTimer <= 0f)
            {
                _evapTimer = 1f;
                for (int i = Strokes.Count - 1; i >= 0; i--)
                {
                    var s = Strokes[i];
                    if (s == null || !s.Alive) { Strokes.RemoveAt(i); continue; }
                    if (s.State != StrokeState.Open || s.Persistent) continue;
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
                if (!NetGame.IsAuthority && !s.Persistent) continue; // clients scan BODY ink only (netcode §2)
                _eligibleCache.Add(s);
            }
            if (_eligibleCache.Count == 0) return;

            // held-still gate: skip the detectors unless sampled ink moved beyond
            // sway, the stroke set changed, or a caller forced the scan
            if (!_forceDetect && InkHeldStill()) return;
            _forceDetect = false;
            SnapshotInk();

            // both detectors always run and the largest seal wins - a small
            // sub-loop must never steal the intended boundary
            SealDetector.LastNearMiss = null;
            var loop = SealDetector.FindLoop(_eligibleCache);
            float loopPerim = loop != null ? SealDetector.LoopPerimeter(loop) : -1f;

            var cross = CrossingFinder.Find(_eligibleCache);
            float crossPerim = cross.Valid ? cross.Perimeter : -1f;

            if (loop != null && loopPerim >= crossPerim)
            {
                CreateSeal(loop, loop.Count == 1 ? "endpoints met" : $"{loop.Count} strokes linked");
                return;
            }
            if (cross.Valid)
            {
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

        /// Seal-page declare: re-run both closure detectors on just this drawing's
        /// strokes. True = a seal formed and activated through the normal casting path.
        public bool TryDeclareSeal(List<Stroke> cluster, bool allowMirror = true)
        {
            if (cluster == null) return false;
            var eligible = new List<Stroke>();
            foreach (var s in cluster)
            {
                if (s == null || !s.Alive || s.State != StrokeState.Open) continue;
                if (s.Nodes.Count < 3 || !s.ChainIntact() || s.Hidden()) continue;
                eligible.Add(s);
            }
            if (eligible.Count == 0) return false;

            SealDetector.LastNearMiss = null;
            var loop = SealDetector.FindLoop(eligible);
            float loopPerim = loop != null ? SealDetector.LoopPerimeter(loop) : -1f;
            var cross = CrossingFinder.Find(eligible);
            float crossPerim = cross.Valid ? cross.Perimeter : -1f;

            if (loop != null && loopPerim >= crossPerim)
            {
                CreateSeal(loop, "declared at the book");
                return true;
            }
            if (cross.Valid)
            {
                ApplyCrossingLoop(cross);
                return true;
            }

            // try the pen's closure paths before the mirror
            foreach (var s in eligible)
            {
                if (!s.Alive || s.State != StrokeState.Open) continue;
                if (TryCloseOntoSelf(s) || TryCloseOntoInk(s)) return true;
            }

            // remote intents stop here: the mirror costs the drawer's ink (netcode §2)
            if (!allowMirror)
            {
                LogEvent(SealDetector.LastNearMiss ?? CrossingFinder.LastNearMiss
                    ?? "no closed path. the line must come back around");
                return false;
            }

            // mirror completion is never allowed on a body (a flat mirror can't follow limbs)
            foreach (var s in eligible)
                if (s.Persistent)
                {
                    LogEvent("the book can't complete a loop on a body. close it with a pose");
                    return false;
                }

            // completion costs ink; only boundary is mirrored - runes are content, never the mouth
            var mouth = BoundaryCandidates(eligible);

            float mirrorLen = 0f;
            foreach (var s in mouth) mirrorLen += s.PathLength();
            float mirrorCost = mirrorLen * DrawingConfig.InkCostPerMeter;
            var payer = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            var payerInk = payer != null ? payer.GetComponent<PlayerInk>() : null;
            if (payerInk != null && !payerInk.TrySpend(mirrorCost))
            {
                LogEvent("not enough ink, the seal fizzles");
                return false;
            }
            var made = MirrorComplete(mouth);
            // nothing drawn = nothing owed: refund when the mirror produced no ink
            if (made.Count == 0 && payerInk != null) payerInk.Award(mirrorCost);
            if (made.Count > 0)
            {
                var all2 = new List<Stroke>(eligible);
                all2.AddRange(made);
                SealDetector.LastNearMiss = null;
                var loop2 = SealDetector.FindLoop(all2);
                if (loop2 != null)
                {
                    CreateSeal(loop2, "the book completed the loop");
                    return true;
                }
                // both detectors: the reflection can land on the original's ink, not just its tips
                var cross2 = CrossingFinder.Find(all2);
                if (cross2.Valid)
                {
                    ApplyCrossingLoop(cross2);
                    return true;
                }
                // rare: the reflection didn't close it - the mirrored ink stays ordinary ink
            }
            LogEvent(SealDetector.LastNearMiss ?? CrossingFinder.LastNearMiss
                ?? "no closed path. the line must come back around");
            return false;
        }

        /// Boundary candidates: drop strokes that are rune content (declared or
        /// recognized). Falls back to the whole set when every stroke reads as a rune.
        List<Stroke> BoundaryCandidates(List<Stroke> eligible)
        {
            var mouth = new List<Stroke>();
            var one = new List<Stroke>(1) { null };
            foreach (var s in eligible)
            {
                if (s.DeclaredRune != RuneType.None) continue; // already said what this is
                one[0] = s;
                // guarded read: cached verdicts + foreign-ink fizzle (netcode §1)
                var (type, score) = RuneGlyph.ReadVerdict(one, s.OwnerId);
                if (type != RuneType.None && score >= DrawingConfig.MinRuneScore) continue;
                mouth.Add(s);
            }
            return mouth.Count > 0 ? mouth : eligible;
        }

        /// Mirror every stroke across the line between the cluster's two farthest
        /// endpoints (the mouth). Reflections are real ink, marked SealResidue so
        /// they serve as boundary, never rune content.
        List<Stroke> MirrorComplete(List<Stroke> cluster)
        {
            var made = new List<Stroke>();
            var ends = new List<Vector3>();
            foreach (var s in cluster)
            {
                if (s.First != null) ends.Add(s.First.transform.position);
                if (s.Last != null) ends.Add(s.Last.transform.position);
            }
            if (ends.Count < 2) return made;
            Vector3 a = ends[0], b = ends[0];
            float best = -1f;
            foreach (var p1 in ends)
                foreach (var p2 in ends)
                {
                    float d = (p1 - p2).sqrMagnitude;
                    if (d > best) { best = d; a = p1; b = p2; }
                }
            if (best < 0.02f * 0.02f) return made; // a closed dot has no mouth

            Vector3 axis = (b - a).normalized;
            foreach (var src in cluster)
            {
                var piece = new Stroke
                {
                    BasisRight = src.BasisRight,
                    BasisUp = src.BasisUp,
                    Surface = src.Surface,
                    OwnerId = src.OwnerId
                };
                int idx = 0;
                foreach (var n in src.Nodes)
                {
                    if (n == null) continue;
                    Vector3 d = n.transform.position - a;
                    Vector3 along = Vector3.Dot(d, axis) * axis;
                    Vector3 mirrored = a + along - (d - along);
                    var node = DrawNode.Create(piece, idx++, mirrored, n.SurfaceNormal, src.Surface);
                    piece.AddNode(node);
                }
                if (piece.Nodes.Count < 2)
                {
                    foreach (var n in piece.Nodes)
                        if (n != null) Destroy(n.gameObject);
                    continue;
                }
                Register(piece);
                piece.State = StrokeState.Open;
                piece.SealResidue = true; // boundary ink, never rune content
                piece.CachePersistence();
                piece.ComputeRawShape();
                made.Add(piece);
            }
            return made;
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
                    {
                        var left = AdoptPiece(stroke, cursor, arc.Lo - 1, allowTiny: false); // leftover ink, never rune content
                        if (left != null) left.SealResidue = true;
                    }
                    pieceForArc[ai] = AdoptPiece(stroke, arc.Lo, arc.Hi, allowTiny: true, reverse: arc.Reversed);
                    cursor = arc.Hi + 1;
                }
                if (cursor <= end)
                {
                    var tail = AdoptPiece(stroke, cursor, end, allowTiny: false);
                    if (tail != null) tail.SealResidue = true;
                }
                stroke.Retire();
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
                    RegisterSpentGroup(strokes, pairs, new List<SealDetector.LoopEntry>(loop));
                }
                return;
            }

            var seal = new Seal(loop);
            seal.CapturePayload(Strokes);
            ActiveSeals.Add(seal);
            if (key != null) _castKeys.Add(key);
            LogEvent($"SEAL #{seal.Id} ACTIVATED ({how}): {seal.Describe()}");

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
            NetSync.PushSealEnd(seal, resolved); // clients drop the ring, burn matching ink (netcode §2)
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
        }

        /// Perf guard: characters/weapons carry bounded ink, but the environment
        /// doesn't - fade the oldest unsealed world scribbles beyond the cap.
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
                }

                s.Retire(); // nodes now belong to the pieces
            }
        }

        void DebugDot(Vector3 world, Color c)
        {
            Vector3 sp = Camera.main.WorldToScreenPoint(world);
            if (sp.z <= 0f) return; // behind the camera
            GUI.color = c;
            GUI.DrawTexture(new Rect(sp.x - 4f, Screen.height - sp.y - 4f, 8f, 8f), Texture2D.whiteTexture);
        }

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
                if (t == null || t == s || !t.Alive) continue;
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
        public void EraseAlong(Vector3 from, Vector3 to, float radius, PlayerInk scoopInto = null)
        {
            NetSync.OnLocalErase(from, to, radius); // ink graphs must not drift (netcode §2)
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
                            * DrawingConfig.ScoopRefund);
                        Destroy(n.gameObject);
                        ErasedTotal++;
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
            }
        }

        /// Lifetime count of erased nodes + the eraser's nearest recent miss -
        /// SurfaceDrawer reads these to speak when a whole rub erased nothing.
        public static int ErasedTotal;
        public static float LastEraseMissDist = float.MaxValue;
        public static float LastEraseMissTime = -999f;

        public void LogEvent(string msg) => Debug.Log($"[SpellyZombie] {msg}");

        void OnGUI()
        {
            // ink debug overlay: green = open endpoints, gold = sealed, white = drawing
            if (_inkDebug && Camera.main != null)
            {
                foreach (var s in Strokes)
                {
                    if (!s.Alive || s.First == null || s.Last == null) continue;
                    Color c = s.State == StrokeState.Open ? new Color(0.3f, 1f, 0.4f)
                        : s.State == StrokeState.Drawing ? Color.white
                        : s.State == StrokeState.InSeal ? new Color(1f, 0.8f, 0.25f)
                        : new Color(0.6f, 0.6f, 0.6f);
                    DebugDot(s.First.transform.position, c);
                    DebugDot(s.Last.transform.position, c);
                }
                GUI.color = Color.white;
                GUI.Label(new Rect(10, 224, 560, 20), "F12 ink debug ON: dots = endpoints the detector sees");
            }

            // no on-screen debug: events go to the console; the F12 overlay is the sole opt-in exception
        }
    }
}
