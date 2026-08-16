using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Owns all ink in the world: the stroke registry, the seal detector loop,
    /// every active seal, and the spent groups waiting to re-arm. Also draws the
    /// debug HUD for the graybox phase.
    /// Later this becomes the network-synced authority for drawings.
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
            RuneLibrary.Warm(); // recognition loads NOW, not on the first rune
            SpellParticle.PrewarmPool(); // and the casting hitch dies at load
            // (the pool ruling: "prepare the
                                         // particles in advance and freeze them")
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

        /// Pen lifted: the stroke is just ink now - runes are read when a seal
        /// closes around them. `silent` = repainted wall ink: skip recognition,
        /// net-send and claim (each cost ~150ms per Rune Studio sample at load).
        /// `preview: false` = the pen is still down and this is a structural
        /// split, so skip the READING only (recognition runs on ink
        /// release, same as on the floor). Everything else still happens.
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

            // MAGNET CLOSE (restored on the word to test it: "it might
            // not be as bad"). No tolerance is widened anywhere: the pen-up
            // GROWS one visible bridge segment to the nearest line end, and
            // the detector still demands exact touching - so lines can never
            // connect without touching; the touch is real drawn ink.
            MagnetClose(s);

            // MARKING IS JUST DRAWING: ink laid on a thing becomes your CLAIM
            // on it, and the more you spend the heavier a thing you can lift.
            // Static scenery counts too - that's how a rooted bench gets
            // enough ink on it to be torn loose.
            if (s.Surface != null && s.Surface.GetComponentInParent<SimpleFPSController>() == null
                && s.Surface.GetComponentInParent<Creature>() == null)
                InkMark.For(s.Surface, true)?.Add(s.OwnerId,
                    s.PathLength() * DrawingConfig.InkCostPerMeter * Perks.InkCostMul);

            RuneGlyph.Precognize(s, Strokes); // recognition runs at PEN-UP -
                                              // the seal close hits the cache

            NetSync.OnLocalStrokeFinished(s); // co-op: friends see your ink

            // pen lifted with both ends on the same existing line? that's a
            // closure - but rune-draft strokes never close (they're becoming a
            // rune, not a seal). SELF first: a circle whose ends also graze a
            // Y must seal on ITSELF, not through the Y (glyph-splitting).
            // CLIENTS close only BODY loops - the host performs the identical
            // world closure when it replays the replicated stroke (netcode §2).
            if (allowCloseOntoInk && (NetGame.IsAuthority || s.Persistent)
                && (TryCloseOntoSelf(s) || TryCloseOntoInk(s))) return;

            LastInk = s;
            if (preview) PreviewRune(s);
        }

        /// the live feedback: the moment ink changes, read the whole
        /// CONNECTED drawing (the same touch rule the seal recognizer uses)
        /// and float a small fading label over it. What the label says is
        /// what a seal will fire - green = clean read, amber = will fire but
        /// weak, ??? = fizzle. New readings replace the old label.
        void PreviewRune(Stroke seed)
        {
            if (seed == null || !seed.Alive || seed.State != StrokeState.Open) return;
            if (seed.OwnerId != Grimoire.LocalPlayerId) return; // your pen only
            if (seed.Hidden()) return;

            // the SHARED flood (seal-truth filters) - the old inline copy read
            // SealResidue ink a real seal never reads, so the label could lie
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
                // emoji, never words . A CORRUPT HAND READS ITS OWN BOOK:
                // an acolyte sees zombie and poison where a wizard sees solid and
                // liquid, from the very same glyph.
                label = RuneLibrary.IconFor(type, Grimoire.LocalPlayerId);
                color = score >= DrawingConfig.GoodRuneScore
                    ? new Color(0.45f, 1f, 0.6f)   // clean - fires at full strength
                    : new Color(1f, 0.85f, 0.4f);  // readable but sloppy
            }

            // the label floats along the SURFACE NORMAL, not world up: up ran
            // along a chest drawing and buried the icon inside the body. The
            // reader must be roughly normal to a surface to have drawn on it,
            // so off the surface is always toward their eyes.
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

        /// Erasing changes what the ink IS - re-read the drawing nearest the
        /// eraser when it lifts, so the label tells the new truth.
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

        // (BuildRecordingGlyph DELETED - it fed the F-key recording, which was
        // REMOVED by ruling; RuneWall.Snapshot clusters directly.)

        /// The design rule "nodes detect proximity to nodes": a stroke whose BOTH
        /// ends land on the same piece of existing ink closes a loop THROUGH it -
        /// even onto the middle of a line. The touched stroke is split at the two
        /// junctions; the segment between them becomes seal boundary, the rest
        /// stays ordinary ink. This is what makes closing shapes drawn in many
        /// sweeps (or silently split at long range) actually work.
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

                // (relative gap budget DELETED - size must never decide a touch,
                // same ruling as SealDetector.Dfs; CloseThreshold above is the law)

                // the loop must enclose something - retracing along a line is not a seal
                Vector3 junctionA = a.Nodes[k].transform.position;
                Vector3 junctionB = a.Nodes[i].transform.position;
                float bulge = Mathf.Max(
                    MaxBulge(b.Nodes, 0, b.Nodes.Count - 1, junctionA, junctionB),
                    MaxBulge(a.Nodes, lo, hi, junctionA, junctionB));
                if (bulge < DrawingConfig.MinLoopBulge) continue;

                // split A at the junctions; the outer pieces stay as ordinary ink
                // (visible, seal-able later - but never rune content: they're
                // amputated fragments of the closing gesture)
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

        /// Pen-up SELF-closure: the stroke's END REGION (the last 12cm - the
        /// hook) touched its OWN earlier ink. Plain 3D node distance under the
        /// SelfCloseThreshold (now flat CloseThreshold, no size scaling - pen-lift
        /// count and size must never decide a shape) - tolerant of cobblestone
        /// height offsets, unlike the crossing finder's exact plan-view
        /// intersection test. Pen-up ONLY: mid-draw stays gated to the stroke's
        /// first 12cm so stars are never truncated mid-glyph again. This is the
        /// path that was MISSING - TryCloseOntoInk skips a==b, the endpoint
        /// detector only tests tip-to-tip, and a curled hook tip sits farther
        /// than 6cm from the start even when the hook LINE lies on the ink.
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

        /// Same adoption from an explicit node list (erase-repair fragments,
        /// the mid-draw tail split) - ONE adoption path, not three copies.
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
            // F12: ink debug - SEE what the detector sees (endpoint dots)
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f12Key.wasPressedThisFrame) _inkDebug = !_inkDebug;

            // ink follows moving surfaces (static ink skips its rebuild internally)
            foreach (var s in Strokes)
                if (s.Alive) s.UpdateLine();

                // OLD INK EVAPORATES after a minute .
            // EXEMPT: Persistent body/weapon ink, ink being drawn, seal ink - only Open strokes are loose.
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

            // (the handwriting flush timer is gone - quiet learning is round-only
            // by ruling; Rune Studio writes on the spot)

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

        /// Run loop detection on the next frame instead of waiting out the
        /// periodic interval — "once lines close a loop they ARE a seal".
        public void RequestDetect() { _detectTimer = 0f; _forceDetect = true; }

        void Detect()
        {
            _eligibleCache.Clear();
            foreach (var s in Strokes)
            {
                if (!s.Alive) continue;
                // The stroke being drawn is deliberately excluded: while the pen is
                // down only the explicit back-to-start self-close applies, so your
                // live stroke can't chain with nearby runes and close prematurely.
                if (s.State != StrokeState.Open) continue;
                if (s.Nodes.Count < 3) continue;
                if (!s.ChainIntact()) continue;
                if (s.Hidden()) continue; // stowed-weapon ink doesn't exist right now
                if (!NetGame.IsAuthority && !s.Persistent) continue; // clients scan BODY ink only (netcode §2)
                _eligibleCache.Add(s);
            }
            if (_eligibleCache.Count == 0) return;

            // INK THAT HELD STILL CANNOT HAVE CHANGED TOPOLOGY ("It's
            // lagging when I'm switching poses with drawings on my body") — a
            // body covered in linked ink fed both detectors their full price
            // eight times a second FOREVER, moving or not, because the idle
            // animation jiggles bone ink past a 0.1mm check. The gate: sample
            // a handful of nodes per stroke, and only when something moved
            // beyond sway (or the stroke set itself changed, or a caller
            // forced it) do the detectors run. Real movement - pose switches,
            // carried objects, walking - still scans exactly as before.
            if (!_forceDetect && InkHeldStill()) return;
            _forceDetect = false;
            SnapshotInk();

            // BOTH detectors always run and the LARGEST seal wins ('s
            // rule): endpoint-chained loops AND ink-crossing loops are compared,
            // so a small sub-loop can never steal the big intended boundary and
            // multi-stroke boundaries close whether their strokes meet at ends
            // or cross in the middle.
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

            // surface why an ALMOST-loop was refused (once per changed reason).
            // BOTH detectors get to explain themselves - a stroke that stopped
            // 2cm short of the line it was aiming for is the single most common
            // refusal, and it used to be the most silent one.
            string why = SealDetector.LastNearMiss ?? CrossingFinder.LastNearMiss;
            if (why != null && why != _lastNearMissShown)
            {
                _lastNearMissShown = why;
                LogEvent(why);
            }
        }

        // ---- the held-still gate ------------------------------------------
        // Up to seven samples per stroke: both ends always, plus every Nth
        // node, so a MIDDLE riding a different bone (multi-limb ink) is
        // watched as well as the endpoints.
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
        /// The tolerance is a fifth of the link distance, derived so a tuned
        /// CloseThreshold carries its gate along: idle sway lives well under
        /// it, and ink parked that close to linking needs real movement to
        /// link - which is exactly what trips the gate and re-scans.
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

        /// THE SEAL-PAGE DECLARE ("a page for seals… but seal must
        /// always find a closed path - and when you recognize it it will be
        /// activated"): re-run BOTH closure detectors on just this drawing's
        /// strokes. The book never conjures a loop that isn't there - it only
        /// looks again, at exactly the ink you pointed at. True = a seal
        /// formed and ACTIVATED through the normal casting path.
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

            // the book tries everything the pen tries FIRST - else a pen-closable
            // drawing fell through to the mirror and got duplicated, not closed
            foreach (var s in eligible)
            {
                if (!s.Alive || s.State != StrokeState.Open) continue;
                if (TryCloseOntoSelf(s) || TryCloseOntoInk(s)) return true;
            }

            // a remote intent stops here: the mirror costs the DRAWER's ink,
            // which the host can't spend for them (netcode §2)
            if (!allowMirror)
            {
                LogEvent(SealDetector.LastNearMiss ?? CrossingFinder.LastNearMiss
                    ?? "no closed path. the line must come back around");
                return false;
            }

            // THE BOOK COMPLETES THE LOOP ("force launch a seal that
            // redraws it flipped to the other side making a complete seal") —
            // but NEVER ON A BODY (the ruling: "completing a seal on the body
            // should not be allowed"; a flat mirror can't follow limbs).
            foreach (var s in eligible)
                if (s.Persistent)
                {
                    LogEvent("the book can't complete a loop on a body. close it with a pose");
                    return false;
                }

            // completion costs ink (the rule: "same ink cost as drawing the half
            // of the seal… it should fizzle"), and only BOUNDARY is mirrored —
            // a rune is content, never a mouth (mirroring runes misplaced the axis)
            var mouth = BoundaryCandidates(eligible);

            float mirrorLen = 0f;
            foreach (var s in mouth) mirrorLen += s.PathLength();
            float mirrorCost = mirrorLen * DrawingConfig.InkCostPerMeter * Perks.InkCostMul;
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
                // BOTH detectors here too: the reflection meets the original at
                // the mouth's ends, but a wobbly hand-drawn arc can just as
                // easily land ON the original's ink instead of on its tip -
                // which is a touch, and touching is touching.
                var cross2 = CrossingFinder.Find(all2);
                if (cross2.Valid)
                {
                    ApplyCrossingLoop(cross2);
                    return true;
                }
                // rare: the reflection didn't close it - the mirrored ink
                // stays as ordinary ink, and the log says why
            }
            LogEvent(SealDetector.LastNearMiss ?? CrossingFinder.LastNearMiss
                ?? "no closed path. the line must come back around");
            return false;
        }

        /// The strokes that could be BOUNDARY - everything the drawing says is
        /// rune CONTENT is dropped. A stroke has declared is a rune outright;
        /// a stroke that reads as one on its own is content too. What's left is
        /// the open shape whose mouth the book is being asked to close.
        /// Falls back to the whole set rather than doing nothing: if every
        /// stroke reads as a rune, the mouth is whatever we have.
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

        /// Mirror every stroke of the cluster across the line between its two
        /// farthest endpoints (the shape's "mouth"). The reflections are real
        /// ink - marked SealResidue so they serve as BOUNDARY, never as rune
        /// content - and they touch the originals at both ends by geometry.
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
                    // should be unreachable (eligibility guarantees intact chains) -
                    // but the sources are already split/retired by now, so if this
                    // ever fires the drawing was consumed with NO seal: shout.
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

            // one cast per closure. Body ink never evaporates, so a chest seal
            // that jiggled open and shut (or resolved and re-closed) would
            // otherwise recast forever; it stays spent until the loop opens
            // past ReArmDistance and is re-closed by posing, or is redrawn.
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

            SpellLock.NotifySeal(seal); // Fable gates taste every seal

            // spell resolution: physics-rune zones + ComboBook announcements
            // (the sigil-table engine lost the A/B and was removed)
            var surface = ResolveSealSurface(seal);
            var spell = Spell.Create(seal, surface);
            if (spell != null) seal.AttachSpell(spell);

            NetSync.PushSeal(seal); // clients see the gold ring (netcode §2)

            // end-of-round gallery snapshot (ink positions are live right now)
            SealGallery.Capture(seal, null); // no combo names - mayhem is unlabeled
        }

        /// The material under the seal - raycast onto the surface just behind the
        /// seal plane; unmarked surfaces resolve to Unknown (neutral defaults).
        SurfaceMaterialType ResolveSealSurface(Seal seal)
        {
            if (Physics.Raycast(seal.PlaneOrigin + seal.PlaneNormal * 0.25f, -seal.PlaneNormal,
                    out var hit, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // painted terrain: the material is whatever layer
                // brushed under this exact spot (stone plaza, dirt path…)
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

            // a MIXED boundary (body ink chained with environment ink) loses its
            // environment strokes to the burn at expire - the ring can never
            // re-close, so tracking it would strand the body ink Spent forever.
            // Hand it straight back instead.
            foreach (var e in boundary)
                if (e.Stroke == null || !e.Stroke.Alive || !e.Stroke.Persistent)
                {
                    ReleaseSpent(strokes);
                    return;
                }

            // one owner per stroke: a re-armed group whose ink was re-sealed and
            // re-spent would otherwise linger and later fire an empty duplicate -
            // the NEWEST group owns the ink, stale trackers drop silently
            _spentGroups.RemoveAll(old =>
            {
                foreach (var s in old.Strokes)
                    if (strokes.Contains(s)) return true;
                return false;
            });

            _spentGroups.Add(new SpentGroup { Strokes = strokes, Pairs = pairs, Boundary = boundary });
        }

        /// Spent seals re-cast one way: the loop physically OPENS past
        /// ReArmDistance then RE-CLOSES within ReCloseDistance (a Schmitt
        /// trigger), which posing a joint does. The old limb-enters trigger is
        /// gone: walking arm swings fired chest seals over and over.
        void TickSpentGroups()
        {
            for (int i = _spentGroups.Count - 1; i >= 0; i--)
            {
                var g = _spentGroups[i];

                // stowed weapon = its spent seals are FROZEN: no re-arming, no
                // firing, nothing until the weapon is pulled out again
                bool stowed = false;
                foreach (var s in g.Strokes)
                    if (s.Hidden()) { stowed = true; break; }
                if (stowed) continue;

                // damaged ink can't seal anyway - don't hold it hostage (checked
                // every tick, or an armed group whose stroke burns would leak)
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
            // COUNT WHAT WE ARE ALLOWED TO BURN. The census used to include
            // every non-persistent stroke while the burn loop below only ever
            // reclaims Open ones, so seal and spent ink inflated the count and
            // could never be freed. In a seal-heavy session that meant the cap
            // read as exceeded forever: it burned away ALL of your loose ink
            // and then re-walked the whole list every tick achieving nothing.
            // Matching the two filters makes the cap mean what it says.
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

        /// Erasing punches holes in strokes; a stroke with holes can never seal.
        /// Split such strokes into their surviving contiguous pieces - each piece
        /// becomes a live stroke with fresh, snappable endpoints, so the eraser
        /// edits ink instead of killing it. Specks below MinStrokeNodes vanish.
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

        /// Debug erase (and later: water, spinner zombies).
        /// The pen lifted just short: grow the last small step to the nearest
        /// line end (the stroke's own start included, so a nearly-closed
        /// circle closes on itself). Only the LIFTED end is assisted - the
        /// start was placed deliberately. One bridge per pen-up, visible ink.
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

        /// Erase a thin TRACK along the cursor's path between frames. The
        /// eraser is only as wide as the pen now, so a fast hand would skip
        /// clean over nodes with point-erasing - sweeping the segment catches
        /// everything the cursor actually passed over.
        /// SCOOPING ("we can scoop up the ink from the floor by
        /// erasing it - our wand is growing"): pass the eraser's own ink pool
        /// and every rubbed-out node flows back into it. Null = plain erase
        /// (zombie soap gets nothing).
        public void EraseAlong(Vector3 from, Vector3 to, float radius, PlayerInk scoopInto = null)
        {
            NetSync.OnLocalErase(from, to, radius); // ink graphs must not drift (netcode §2)
            Vector3 seg = to - from;
            float len2 = seg.sqrMagnitude;
            float r2 = radius * radius;

            // ONLY LOOK AT INK THE ERASER COULD REACH. This used to test every
            // node of every stroke in the world, every frame the button is held,
            // so rubbing ink out got slower the more ink existed. The sweep box
            // is this frame's cursor segment grown by the eraser radius
            // (Bounds.Expand moves each face half, so x2 gives exactly radius).
            // A node within `radius` of the segment is inside this box AND
            // inside its own stroke's bounds, so the two must intersect: no ink
            // the old loop would have erased can be skipped by this test.
            var sweep = new Bounds(from, Vector3.zero);
            sweep.Encapsulate(to);
            sweep.Expand(radius * 2f);

            // the eraser's own near-miss channel (never silently refuse):
            // the sweep box is generous, so also look a few radii around it
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
                        // AT A LOSS : full-worth refunds made a
                        // free spell loop - draw, scoop, cast forever, never visit
                        // the cauldron. The pot must stay the only true source.
                        scoopInto?.Award(DrawingConfig.NodeSpacing * DrawingConfig.InkCostPerMeter
                            * DrawingConfig.ScoopRefund);
                        Destroy(n.gameObject);
                        ErasedTotal++;
                    }
                    else if (d2 < radius * 4f * (radius * 4f))
                    {
                        // rubbed NEAR ink without touching it - remembered so
                        // the eraser can say why nothing died (the silent
                        // "not allowing me to erase" bug class)
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

        // (the _events ring buffer is DELETED - write-only since the on-screen
        // HUD fell to the July-12 no-debug-on-screen rule)
        public void LogEvent(string msg) => Debug.Log($"[SpellyZombie] {msg}");

        void OnGUI()
        {
            // ink debug overlay: green dots = open stroke endpoints (these are
            // what must touch/cross to close), gold = sealed, white = drawing.
            // Two green dots kissing that DIDN'T seal  screenshot that.
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

            // the rule (July 12): NO instruction walls, NO debug spam on
            // screen - events go to the console only (LogEvent  Debug.Log).
            // The F12 overlay above is the sole exception: opt-in, off by default.
        }
    }
}
