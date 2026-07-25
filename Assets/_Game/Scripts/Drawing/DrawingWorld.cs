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

        /// The most recently completed stroke — anchor for template recording.
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
            public HashSet<Transform> BaselineInside;     // capsules resting inside (the body's OWN fat torso/neighbour
                                                          // capsules, or a limb already crossed at spend-time) — never intruders
            public int BaselineTicks;                     // settle counter: fold resting capsules into the baseline for a
                                                          // few ticks before the "a NEW limb entered" trigger arms
        }

        const int SealBaselineSettleTicks = 3; // ~0.36s at DetectInterval — a limb must arrive AFTER this to count

        readonly List<SpentGroup> _spentGroups = new List<SpentGroup>();
        static readonly Collider[] _limbHits = new Collider[64];          // limb-intrusion overlap buffer
        static readonly HashSet<Transform> _ownBones = new HashSet<Transform>();  // the loop's own carrier bones
        static readonly HashSet<Transform> _insideNow = new HashSet<Transform>(); // capsules inside the loop this tick
        readonly List<string> _events = new List<string>();
        float _detectTimer;
        readonly List<Stroke> _eligibleCache = new List<Stroke>();
        string _lastNearMissShown;
        bool _inkDebug; // F12: show what the detector sees (stroke endpoints)

        void Awake()
        {
            Instance = this;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            LineMaterial = new Material(shader);
            RuneLibrary.Warm(); // recognition loads NOW, not on the first rune
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Register(Stroke stroke)
        {
            stroke.EnsureLine(LineMaterial);
            Strokes.Add(stroke);
        }

        /// Pen lifted: the stroke is just ink now. No grouping, no classification,
        /// no timers — runes are read when a seal closes around them.
        public void CompleteStroke(Stroke s, bool allowCloseOntoInk = true)
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

            NetSync.OnLocalStrokeFinished(s); // co-op: friends see your ink

            // pen lifted with both ends on the same existing line? that's a
            // closure — but rune-draft strokes never close (they're becoming a
            // rune, not a seal). SELF first: a circle whose ends also graze a
            // Y must seal on ITSELF, not through the Y (glyph-splitting).
            if (allowCloseOntoInk && (TryCloseOntoSelf(s) || TryCloseOntoInk(s))) return;

            LastInk = s;
            PreviewRune(s);
        }

        /// Marko's live feedback: the moment ink changes, read the whole
        /// CONNECTED drawing (the same touch rule the seal recognizer uses)
        /// and float a small fading label over it. What the label says is
        /// what a seal will fire — green = clean read, amber = will fire but
        /// weak, ??? = fizzle. New readings replace the old label.
        void PreviewRune(Stroke seed)
        {
            if (seed == null || !seed.Alive || seed.State != StrokeState.Open) return;
            if (seed.OwnerId != Grimoire.LocalPlayerId) return; // your pen only
            if (seed.Hidden()) return;

            var members = new List<Stroke> { seed };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var s in Strokes)
                {
                    if (!s.Alive || s.State != StrokeState.Open || s.Hidden()
                        || members.Contains(s)) continue;
                    foreach (var m in members)
                        if (RuneGlyph.InkTouches(s, m, DrawingConfig.RuneTouchDistance))
                        {
                            members.Add(s);
                            grew = true;
                            break;
                        }
                    if (grew) break;
                }
            }

            var (type, score) = RuneLibrary.Classify(seed.OwnerId, RuneGlyph.RawStrokesOf(members));
            string label;
            Color color;
            if (type == RuneType.None || score < DrawingConfig.MinRuneScore)
            {
                label = "???";
                color = new Color(0.78f, 0.78f, 0.78f);
            }
            else
            {
                label = RuneLibrary.ShortName(type);
                color = score >= DrawingConfig.GoodRuneScore
                    ? new Color(0.45f, 1f, 0.6f)   // clean — fires at full strength
                    : new Color(1f, 0.85f, 0.4f);  // readable but sloppy
            }

            Vector3 pos = Vector3.zero;
            int count = 0;
            foreach (var m in members)
            {
                pos += m.Centroid();
                count++;
            }
            if (count == 0) return;
            RunePreview.Show(pos / count + Vector3.up * 0.18f, label, color);
        }

        /// Erasing changes what the ink IS — re-read the drawing nearest the
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

        /// For F-key template recording: the spatial ink cluster around the most
        /// recently drawn stroke, flattened for the recognizer.
        public List<List<Vector2>> BuildRecordingGlyph(out int strokeCount)
        {
            strokeCount = 0;
            if (LastInk == null || !LastInk.Alive || LastInk.State != StrokeState.Open) return null;

            var open = new List<Stroke>();
            foreach (var s in Strokes)
                if (s.Alive && s.State == StrokeState.Open && s.Nodes.Count >= 3 && s.ChainIntact()
                    && !s.Hidden())
                    open.Add(s);

            // SAME grouping rule as live recognition (RuneTouchDistance union-
            // find) — recording with a looser join than reading meant templates
            // captured neighbour ink the reader would never include
            foreach (var glyph in RuneGlyph.Cluster(open, DrawingConfig.RuneTouchDistance, 0f))
            {
                if (!glyph.Members.Contains(LastInk)) continue;
                strokeCount = glyph.Members.Count;
                return glyph.BuildRawStrokes();
            }
            return null;
        }

        /// The design rule "nodes detect proximity to nodes": a stroke whose BOTH
        /// ends land on the same piece of existing ink closes a loop THROUGH it —
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
                if (a.Nodes.Count < 3 || !a.ChainIntact() || a.Hidden()) continue;

                int i = NearestNodeIndex(a, bLast.transform.position, DrawingConfig.CloseThreshold);
                if (i < 0) continue;
                int k = NearestNodeIndex(a, bFirst.transform.position, DrawingConfig.CloseThreshold);
                if (k < 0) continue;

                int lo = Mathf.Min(i, k);
                int hi = Mathf.Max(i, k);
                if (hi - lo + 1 < 2) continue; // both ends on the same spot — nothing enclosed

                // size guards on the would-be loop (b + a[lo..hi])
                float loopLength = a.LengthBetween(lo, hi) + b.PathLength();
                int loopNodes = (hi - lo + 1) + b.Nodes.Count;
                if (loopNodes < DrawingConfig.MinLoopNodes || loopLength < DrawingConfig.MinLoopPerimeter) continue;

                // GAP BUDGET relative to the WHOLE loop being formed — same rule
                // as chained loops. This lets a small stroke close a big circle
                // (big perimeter = generous budget) while a hatch mark next to a
                // line still can't fake a closure (tiny loop = tiny budget).
                float gapSum = Vector3.Distance(bLast.transform.position, a.Nodes[i].transform.position)
                             + Vector3.Distance(bFirst.transform.position, a.Nodes[k].transform.position);
                if (gapSum > loopLength * DrawingConfig.MaxLoopGapFraction)
                {
                    LogEvent($"almost closed onto ink — {gapSum * 100f:0.0}cm of air vs {loopLength * DrawingConfig.MaxLoopGapFraction * 100f:0.0}cm allowed ({loopLength * 100f:0}cm candidate loop)");
                    continue;
                }

                // the loop must enclose something — retracing along a line is not a seal
                Vector3 junctionA = a.Nodes[k].transform.position;
                Vector3 junctionB = a.Nodes[i].transform.position;
                float bulge = Mathf.Max(
                    MaxBulge(b.Nodes, 0, b.Nodes.Count - 1, junctionA, junctionB),
                    MaxBulge(a.Nodes, lo, hi, junctionA, junctionB));
                if (bulge < DrawingConfig.MinLoopBulge) continue;

                // split A at the junctions; the outer pieces stay as ordinary ink
                // (visible, seal-able later — but never rune content: they're
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

        /// Pen-up SELF-closure: the stroke's END REGION (the last 12cm — the
        /// hook) touched its OWN earlier ink. Plain 3D node distance under the
        /// EXISTING SelfCloseThreshold (2-6cm clamp) — tolerant of cobblestone
        /// height offsets, unlike the crossing finder's exact plan-view
        /// intersection test. Pen-up ONLY: mid-draw stays gated to the stroke's
        /// first 12cm so stars are never truncated mid-glyph again. This is the
        /// path that was MISSING — TryCloseOntoInk skips a==b, the endpoint
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
                for (int j = 0; j < i; j++)   // j ascending = LARGEST loop first (Marko: big boundary wins)
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

        /// Move a node range out of `src` into a fresh independent stroke — just
        /// ink; recognition happens later when a seal closes around it. When
        /// `reverse` is set the node order is flipped, so an arc traversed the
        /// other way still forms a continuous ring.
        Stroke AdoptPiece(Stroke src, int from, int to, bool allowTiny, bool reverse = false)
        {
            var piece = new Stroke { BasisRight = src.BasisRight, BasisUp = src.BasisUp, OwnerId = src.OwnerId };
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
            CreateSeal(new List<SealDetector.LoopEntry> { new SealDetector.LoopEntry(s, true) }, "closed while drawing");
        }

        void Update()
        {
            // F12: ink debug — SEE what the detector sees (endpoint dots)
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f12Key.wasPressedThisFrame) _inkDebug = !_inkDebug;

            // ink follows moving surfaces (static ink skips its rebuild internally)
            foreach (var s in Strokes)
                if (s.Alive) s.UpdateLine();

            // active seals: integrity + duration
            for (int i = ActiveSeals.Count - 1; i >= 0; i--)
                if (!ActiveSeals[i].Tick(Time.deltaTime))
                    ActiveSeals.RemoveAt(i);

            // periodic scans — loop detection, spent re-arming, erase repair, ink budget
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
        public void RequestDetect() => _detectTimer = 0f;

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
                _eligibleCache.Add(s);
            }
            if (_eligibleCache.Count == 0) return;

            // BOTH detectors always run and the LARGEST seal wins (Marko's
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

            // surface why an ALMOST-loop was refused (once per changed reason)
            if (SealDetector.LastNearMiss != null && SealDetector.LastNearMiss != _lastNearMissShown)
            {
                _lastNearMissShown = SealDetector.LastNearMiss;
                LogEvent(SealDetector.LastNearMiss);
            }
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
                    // should be unreachable (eligibility guarantees intact chains) —
                    // but the sources are already split/retired by now, so if this
                    // ever fires the drawing was consumed with NO seal: shout.
                    LogEvent("crossing seal ABORTED mid-adopt — report this drawing");
                    return;
                }
                boundary.Add(new SealDetector.LoopEntry(pieceForArc[k], true));
            }
            if (boundary.Count == 0) return;

            CreateSeal(boundary, boundary.Count == 1 ? "self-crossing" : $"{boundary.Count} arcs enclosed");
        }

        void CreateSeal(List<SealDetector.LoopEntry> loop, string how)
        {
            var seal = new Seal(loop);
            seal.CapturePayload(Strokes);
            ActiveSeals.Add(seal);
            LogEvent($"SEAL #{seal.Id} ACTIVATED ({how}): {seal.Describe()}");

            SpellLock.NotifySeal(seal); // Fable gates taste every seal

            // spell resolution: physics-rune zones + ComboBook announcements
            // (the sigil-table engine lost the A/B and was removed)
            var surface = ResolveSealSurface(seal);
            var spell = Spell.Create(seal, surface);
            if (spell != null) seal.AttachSpell(spell);

            // end-of-round gallery snapshot (ink positions are live right now)
            SealGallery.Capture(seal, null); // no combo names — mayhem is unlabeled
        }

        /// The material under the seal — raycast onto the surface just behind the
        /// seal plane; unmarked surfaces resolve to Unknown (neutral defaults).
        SurfaceMaterialType ResolveSealSurface(Seal seal)
        {
            if (Physics.Raycast(seal.PlaneOrigin + seal.PlaneNormal * 0.25f, -seal.PlaneNormal,
                    out var hit, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // painted terrain: the material is whatever layer Marko
                // brushed under this exact spot (stone plaza, dirt path…)
                var painted = hit.collider.GetComponent<TerrainSurfaceMap>();
                if (painted != null) return painted.MaterialAt(hit.point);
                return SurfaceMaterialDB.Resolve(hit.collider);
            }
            return SurfaceMaterialType.Unknown;
        }

        public void OnSealEnded(Seal seal, string message)
        {
            seal.Spell?.End(); // spell cancels the instant the seal breaks or expires
            LogEvent(message);
        }

        // ---- spent ink (characters & weapons) ----

        public void RegisterSpentGroup(List<Stroke> strokes, List<(DrawNode a, DrawNode b)> pairs,
            List<SealDetector.LoopEntry> boundary)
        {
            // no surviving junctions means the loop can never close again
            // (its other half burned) — hand the ink straight back
            if (pairs.Count == 0)
            {
                ReleaseSpent(strokes);
                return;
            }

            // a MIXED boundary (body ink chained with environment ink) loses its
            // environment strokes to the burn at expire — the ring can never
            // re-close, so tracking it would strand the body ink Spent forever.
            // Hand it straight back instead.
            foreach (var e in boundary)
                if (e.Stroke == null || !e.Stroke.Alive || !e.Stroke.Persistent)
                {
                    ReleaseSpent(strokes);
                    return;
                }

            // one owner per stroke: a re-armed group whose ink was re-sealed and
            // re-spent would otherwise linger and later fire an empty duplicate —
            // the NEWEST group owns the ink, stale trackers drop silently
            _spentGroups.RemoveAll(old =>
            {
                foreach (var s in old.Strokes)
                    if (strokes.Contains(s)) return true;
                return false;
            });

            _spentGroups.Add(new SpentGroup { Strokes = strokes, Pairs = pairs, Boundary = boundary });
        }

        /// Spent seals re-cast two ways, both mode-independent:
        ///  · A LIMB ENTERS the enclosed loop (Marko's rule — "get my hands near
        ///    it"): edge-triggered, so crossing an arm into a chest seal fires it,
        ///    and it re-arms when the limb leaves. This is the ONLY re-cast a loop
        ///    living on a single rigid body part can get — its own ends never part.
        ///  · The loop physically OPENS past ReArmDistance then RE-CLOSES within
        ///    ReCloseDistance (a Schmitt trigger). This carries joint-spanning
        ///    seals — pose the joint open, then closed, and it fires. Routing
        ///    re-closure through Detect()'s tight fresh-draw threshold (3.5cm)
        ///    never fired for hand-posing, which can't reproduce a pose exactly.
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

                // damaged ink can't seal anyway — don't hold it hostage (checked
                // every tick, or an armed group whose stroke burns would leak)
                bool damaged = false;
                foreach (var s in g.Strokes)
                    if (!s.Alive || !s.ChainIntact()) { damaged = true; break; }
                if (damaged)
                {
                    ReleaseSpent(g.Strokes);
                    _spentGroups.RemoveAt(i);
                    continue;
                }

                // ---- TRIGGER 1: a limb crosses INTO the still-closed loop ----
                // The body's OWN fat torso capsules (hips/head) rest permanently
                // inside a chest loop, so a plain "is anything inside" test would
                // be stuck true and never fire. Instead we snapshot what's inside
                // at spend-time as the BASELINE and only fire on a capsule that was
                // NOT already there — a limb the player deliberately moved in.
                CollectLimbsInLoop(g, _insideNow);
                if (g.BaselineInside == null) g.BaselineInside = new HashSet<Transform>();
                if (g.BaselineTicks < SealBaselineSettleTicks)
                {
                    // SETTLE: for the first few ticks fold whatever is resting on the
                    // body (torso capsules, a limb already crossed when the seal
                    // spent) into the baseline, so only a limb that ARRIVES after
                    // this reads as a deliberate intruder — no spurious self-recast.
                    g.BaselineInside.UnionWith(_insideNow);
                    g.BaselineTicks++;
                }
                else if (!g.Armed)
                {
                    // a limb that LEAVES the loop drops out of the baseline, so
                    // leaving and coming back fires again (the torso capsules
                    // never leave, so they stay whitelisted forever)
                    g.BaselineInside.IntersectWith(_insideNow);

                    bool fresh = false;
                    foreach (var t in _insideNow)
                        if (!g.BaselineInside.Contains(t)) { fresh = true; break; }
                    if (fresh)
                    {
                        _spentGroups.RemoveAt(i);
                        if (BoundaryReady(g))
                        {
                            ReleaseSpent(g.Strokes); // re-open the enclosed runes so the re-cast re-reads them
                            CreateSeal(new List<SealDetector.LoopEntry>(g.Boundary), "a limb entered the seal");
                        }
                        else
                        {
                            ReleaseSpent(g.Strokes); // never strand ink Spent with no tracker
                        }
                        continue;
                    }
                }

                // ---- TRIGGER 2: the loop opens, then re-closes (posed joints) ----
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
                        ReleaseSpent(g.Strokes);
                        g.Armed = true;
                        LogEvent("Spent seal re-armed — the loop opened");
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

        /// Collect every capsule (of the SAME body, excluding the loop's own
        /// carrier bones) whose nearest point falls inside the enclosed loop —
        /// the "bring a hand to the seal" test. The loop's plane, centre and
        /// radius are read live from its boundary nodes (they ride the posed
        /// bones). Fills `into` with the intruding bone transforms; the caller
        /// diffs this against the spend-time baseline so the body's own fat
        /// torso capsules (always inside) never count as a fresh intruder.
        void CollectLimbsInLoop(SpentGroup g, HashSet<Transform> into)
        {
            into.Clear();
            if (g.Boundary == null) return;
            var nodes = SealDetector.BuildLoopNodes(g.Boundary);
            if (nodes.Count < 3) return;

            Vector3 c = Vector3.zero;
            int cnt = 0;
            Transform ownRoot = null;
            _ownBones.Clear();
            foreach (var nd in nodes)
            {
                if (nd == null) continue;
                c += nd.transform.position;
                cnt++;
                if (ownRoot == null) ownRoot = nd.transform.root;
                if (nd.transform.parent != null) _ownBones.Add(nd.transform.parent);
            }
            if (cnt < 3 || ownRoot == null) return;
            c /= cnt;

            // average radius + best-fit plane normal (Newell's method — robust to
            // a non-planar, body-draped loop)
            Vector3 normal = Vector3.zero;
            float radius = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i];
                if (a == null) continue;
                radius += Vector3.Distance(a.transform.position, c);
                var b = nodes[(i + 1) % nodes.Count];
                if (b == null) continue;
                Vector3 p = a.transform.position, q = b.transform.position;
                normal.x += (p.y - q.y) * (p.z + q.z);
                normal.y += (p.z - q.z) * (p.x + q.x);
                normal.z += (p.x - q.x) * (p.y + q.y);
            }
            radius /= cnt;
            if (radius < 1e-3f) return;
            bool haveN = normal.sqrMagnitude > 1e-10f;
            if (haveN) normal.Normalize();

            // thin DISC, not a ball: reach grows a little with big loops but is
            // hard-capped, so a hand passing well in FRONT of a chest seal (or a
            // walk-cycle arm swing) never reads as "inside"
            float reach = Mathf.Clamp(radius * 0.5f, DrawingConfig.SealLimbReach, 0.2f);
            int hits = Physics.OverlapSphereNonAlloc(c, radius + reach, _limbHits,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                var col = _limbHits[i];
                if (col == null || !(col is CapsuleCollider)) continue; // limb segments are capsules
                var t = col.transform;
                if (t == ownRoot || t.root != ownRoot) continue;        // only YOUR body, not the fat controller
                if (_ownBones.Contains(t)) continue;                    // never the loop's own carrier bones

                Vector3 d = col.ClosestPoint(c) - c;
                float planeDist = haveN ? Mathf.Abs(Vector3.Dot(d, normal)) : 0f;
                float radial = haveN ? Vector3.ProjectOnPlane(d, normal).magnitude : d.magnitude;
                if (planeDist <= reach && radial <= radius * 1.25f)
                    into.Add(t);
            }
        }

        static void ReleaseSpent(List<Stroke> strokes)
        {
            foreach (var s in strokes)
            {
                if (!s.Alive || s.State != StrokeState.Spent) continue;
                s.State = StrokeState.Open;
                s.SetColor(Stroke.InkColor);
                s.SetLoop(false);
            }
        }

        /// Perf guard: characters/weapons carry bounded ink, but the environment
        /// doesn't — fade the oldest unsealed world scribbles beyond the cap.
        void EnforceInkBudget()
        {
            int env = 0;
            foreach (var s in Strokes)
                if (s.Alive && !s.Persistent) env++;
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
                LogEvent($"Old environment ink faded ({burned} strokes) — world ink caps at {DrawingConfig.MaxEnvironmentStrokes}");
        }

        /// Erasing punches holes in strokes; a stroke with holes can never seal.
        /// Split such strokes into their surviving contiguous pieces — each piece
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
                    if (fragment.Count < DrawingConfig.MinStrokeNodes)
                    {
                        foreach (var n in fragment) Destroy(n.gameObject);
                        continue;
                    }
                    var piece = new Stroke { BasisRight = s.BasisRight, BasisUp = s.BasisUp, OwnerId = s.OwnerId };
                    foreach (var n in fragment)
                    {
                        n.SetStroke(piece);
                        piece.AddNode(n);
                    }
                    Register(piece);
                    piece.State = StrokeState.Open;
                    piece.CachePersistence();
                    piece.ComputeRawShape();
                    if (wasLastInk) LastInk = piece; // keep template-recording anchor alive
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
        public void EraseAt(Vector3 point, float radius) => EraseAlong(point, point, radius);

        /// Erase a thin TRACK along the cursor's path between frames. The
        /// eraser is only as wide as the pen now, so a fast hand would skip
        /// clean over nodes with point-erasing — sweeping the segment catches
        /// everything the cursor actually passed over.
        public void EraseAlong(Vector3 from, Vector3 to, float radius)
        {
            Vector3 seg = to - from;
            float len2 = seg.sqrMagnitude;
            float r2 = radius * radius;
            foreach (var s in Strokes)
            {
                if (!s.Alive || s.Hidden()) continue; // can't rub out invisible ink
                foreach (var n in s.Nodes)
                {
                    if (n == null) continue;
                    Vector3 p = n.transform.position;
                    float t = len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - from, seg) / len2) : 0f;
                    if ((p - (from + seg * t)).sqrMagnitude <= r2)
                        Destroy(n.gameObject);
                }
            }
        }

        public void LogEvent(string msg)
        {
            Debug.Log($"[SpellyZombie] {msg}");
            _events.Insert(0, msg);
            if (_events.Count > 6) _events.RemoveAt(_events.Count - 1);
        }

        void OnGUI()
        {
            // ink debug overlay: green dots = open stroke endpoints (these are
            // what must touch/cross to close), gold = sealed, white = drawing.
            // Two green dots kissing that DIDN'T seal → screenshot that.
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
                GUI.Label(new Rect(10, 224, 560, 20), "F12 ink debug ON — dots = endpoints the detector sees");
            }

            // Marko's rule (July 12): NO instruction walls, NO debug spam on
            // screen — events go to the console only (LogEvent → Debug.Log).
            // The F12 overlay above is the sole exception: opt-in, off by default.
        }

        static GUIStyle _rich;
        static GUIStyle Rich()
        {
            if (_rich == null)
                _rich = new GUIStyle(GUI.skin.label) { richText = true };
            return _rich;
        }
    }
}
