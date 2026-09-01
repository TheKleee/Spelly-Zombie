using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Absorb: hold the book out and absorb the aimed object. Lives on the
    /// player; scans the small Analyzable registry only when the key is available.
    public class GrimoireAbsorb : MonoBehaviour
    {
        public const float Cone = 0.72f;   // wider than the grab cone

        /// True while the local player is aiming at something learnable.
        /// Absorb is on F and takes priority when a drawing and a learnable
        /// object are both under the cursor.
        public static bool TargetInReach { get; private set; }

        /// True while F would DECLARE the aimed drawing (book open on a rune
        /// page or the seal page). WeaponSlots yields the F weapon-drop while
        /// either this or TargetInReach is up.
        public static bool DeclareInReach { get; private set; }

        SimpleFPSController _pilot;

        // Declare flow: book open to a rune page, F stamps DeclaredRune on the
        // aimed drawing; the ink stays and the strokes teach the handwriting pool.
        /// Max off-axis miss as a fraction of distance along the aim ray
        /// (0.28 ≈ 16°). Eligibility only - candidates rank by metres of miss.
        const float PointingCone = 0.28f;

        readonly List<Stroke> _inkMembers = new List<Stroke>();
        readonly List<Stroke> _near = new List<Stroke>(); // flood scratch
        readonly List<(Stroke s, Vector3 p, float miss)> _cands =
            new List<(Stroke, Vector3, float)>();         // aim ranking scratch
        readonly List<Stroke> _tinted = new List<Stroke>();      // ink lit up as "F acts on this"
        readonly List<Color> _tintedWas = new List<Color>();     // ...and what colour it was before
        readonly List<Stroke> _tintKeep = new List<Stroke>();    // scratch, so Highlight allocates nothing
        readonly List<Color> _tintKeepWas = new List<Color>();
        RuneType _inkRune;      // the rune the open page lets you declare
        bool _inkSeal;          // the SEAL page lets this drawing be traced
        bool _fizzledInSight;   // an unreadable drawing is aimed - teach the flow
        float _fizzleShowUntil; // chip grace so aim wobble can't strobe the lesson
        float _inkScan;
        Analyzable _promptTarget;
        string _absorbPrompt;
        int _lastHash;          // aimed-drawing identity - classify ONLY on change
        Stroke _lastSeed;       // steady-aim skip: same seed + same ink = no re-flood
        int _lastStrokeCount;
        int _lastBookKey = int.MinValue; // book open/page state the cached offer was made under
        RuneType _lastReadAs;
        float _lastReadScore;
        Stroke _poppedFor;   // the drawing whose hover already popped the book

        /// The floating F rides the highlighted drawing itself - midway along
        /// the aimed stroke, where the eye already is.
        void ShowDeclareBadge()
        {
            var s = _lastSeed;
            if (s == null || s.First == null || s.Last == null) return;
            AimBadge.OfferAt(
                (s.First.transform.position + s.Last.transform.position) * 0.5f, "F");
        }

        /// Aim lost: the whole ink read clears together - members, hash, rune,
        /// seal - so a stale rune can never outlive its drawing.
        void ClearInkRead()
        {
            _inkMembers.Clear();
            _lastHash = 0;
            _inkRune = RuneType.None;
            _inkSeal = false;
        }

        void Awake() => _pilot = GetComponent<SimpleFPSController>();
        void OnDisable() { TargetInReach = false; DeclareInReach = false; Highlight(null); }

        void Update()
        {
            if (_pilot == null || _pilot.IsDowned)
            { TargetInReach = false; DeclareInReach = false; Highlight(null); return; }

            // acolytes declare but never absorb - Best() is not consulted for them
            bool acolyteHand = Sides.IsAcolyte(Grimoire.LocalPlayerId);

            // while carrying, F belongs to drop - all declare/absorb prompts stand down
            if (HandGrab.LocalHolding)
            { TargetInReach = false; DeclareInReach = false; Highlight(null); return; }

            var kb = Keyboard.current;
            // first person aims with the crosshair, third person with the
            // camera, the easel with the cursor
            if (kb == null)
            { TargetInReach = false; DeclareInReach = false; Highlight(null); return; }

            // ABSORBING IS AIM + F, like the acolyte's scan. The source itself
            // is only a label; the badge already decided it has something to
            // teach and that we are close enough, so this just takes it.
            if (kb.fKey.wasPressedThisFrame && !UIKit.Typing
                && !GameMenu.IsOpen && !PoseStudio.IsOpen
                && AimBadge.Aimed is AbsorbSource source)
            {
                NetSync.AbsorbCast(source, Grimoire.LocalPlayerId);
                return;
            }

            // already-absorbed things are skipped by Best(), so F falls
            // through to the drawing behind them
            var target = acolyteHand ? null : Best();
            TargetInReach = target != null;
            if (target == null)
            {
                // no absorbable object: declare flow (rune page stamps, seal page traces)
                ScanInk();
                // an OBJECT under the acolyte's aim owns F for scanning - the
                // declare only speaks when no scan target is on offer
                bool scanWins = acolyteHand && AimBadge.ScanTarget != null;
                bool declRune = !scanWins && _inkRune != RuneType.None && _inkMembers.Count > 0;
                bool declSeal = !scanWins && _inkSeal && _inkMembers.Count > 0;
                DeclareInReach = declRune || declSeal;
                Highlight(DeclareInReach ? _inkMembers : null);

                // easel: hover-enter over an unnamed line pops the book,
                // once per hovered drawing
                var hovered = _inkMembers.Count > 0 ? _inkMembers[0] : null;
                if (SelfPaint.IsActive && hovered != null && hovered != _poppedFor
                    && !GrimoirePages.BookOpen)
                {
                    _poppedFor = hovered;
                    GrimoirePages.RequestOpen();
                }
                if (hovered == null) _poppedFor = null;
                if (declRune)
                {
                    ShowDeclareBadge();
                    if (kb.fKey.wasPressedThisFrame) DeclareInk();
                }
                else if (declSeal)
                {
                    ShowDeclareBadge();
                    if (kb.fKey.wasPressedThisFrame) DeclareSeal();
                }
                else if (_fizzledInSight || Time.time < _fizzleShowUntil)
                {
                    // grace keeps the chip steady while the aim slips off the line and back
                    if (_fizzledInSight) _fizzleShowUntil = Time.time + 0.6f;
                }

                if (!DeclareInReach && kb.fKey.wasPressedThisFrame && GrimoirePages.BookOpen
                    && (GrimoirePages.SealPageOpen || GrimoirePages.PageRune != RuneType.None))
                    DrawingWorld.Instance?.LogEvent(
                        "the book has nothing selected. aim at one of your own LINES (it lights up when the book has it)");
                return;
            }
            DeclareInReach = false;
            Highlight(null); // an absorbable thing took the key - drop the ink highlight

            // ABSORB WINS THE KEY when both are possible
            if (!ReferenceEquals(target, _promptTarget) || _absorbPrompt == null)
            {
                _promptTarget = target;
                _absorbPrompt = $"absorb to learn {RuneLibrary.IconInline(target.NextFor(Grimoire.LocalPlayerId))}";
            }
            UIPrompt.Show("F", _absorbPrompt, new Color(0.85f, 0.8f, 1f));
            Hints.Offer(Hints.Id.Absorb);
            if (kb.fKey.wasPressedThisFrame)
            {
                target.AbsorbInto(Grimoire.LocalPlayerId);
                Hints.Retire(Hints.Id.Absorb);
            }
        }

        /// Find the best-aimed drawing of the player's own open ink (seed by
        /// aim, then the touching cluster) and decide what the book offers.
        /// The recognizer runs only when the aimed drawing changes.
        void ScanInk()
        {
            if (Time.time < _inkScan) return;
            _inkScan = Time.time + 0.25f;
            // recognizer sleeps while the pen is down
            var w0 = DrawingWorld.Instance;
            if (w0 != null)
                for (int i = 0; i < w0.Strokes.Count; i++)
                    if (w0.Strokes[i].State == StrokeState.Drawing) return;
            // cleared only on aim-lost exits and on a full recompute - the
            // steady-aim cache below returns early
            _fizzledInSight = false;
            var world = DrawingWorld.Instance;
            if (world == null) { ClearInkRead(); return; }

            bool Mine(Stroke s) => s != null && s.Alive && s.State == StrokeState.Open
                && s.OwnerId == Grimoire.LocalPlayerId && !s.SealResidue
                && s.ChainIntact() && !s.Hidden();

            Stroke seed = null;
            Vector3 seedC = default;
            // easel / free-cursor: ink is picked under the cursor - no aim
            // cone, no occlusion ray
            bool easel = SelfPaint.IsActive || Cursor.lockState != CursorLockMode.Locked;
            if (easel)
            {
                var cam = Camera.main;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (cam == null || mouse == null) { ClearInkRead(); return; }
                var mray = cam.ScreenPointToRay(mouse.position.ReadValue());
                float bestD = 0.35f; // the cursor must pass near the INK
                foreach (var s in world.Strokes)
                {
                    if (!Mine(s)) continue;
                    if (NearestNodeToRay(s, mray.origin, mray.direction, 0f, 60f, float.MaxValue,
                            out var p, out float miss) && miss < bestD)
                    { bestD = miss; seed = s; seedC = p; }
                }
                if (seed == null) { ClearInkRead(); return; }
                // sticky cursor, same reflood killer as the aim branch: the
                // previous drawing keeps the seat while it stays nearly as close
                if (seed != _lastSeed && _lastSeed != null && Mine(_lastSeed)
                    && NearestNodeToRay(_lastSeed, mray.origin, mray.direction, 0f, 60f, float.MaxValue,
                        out var lastP, out float lastMiss)
                    && lastMiss < bestD + 0.12f && lastMiss < 0.35f)
                { seed = _lastSeed; seedC = lastP; }
            }
            else
            {
                var pivot = _pilot.CameraPivot;
                Vector3 eye = pivot != null ? pivot.position
                    : _pilot.transform.position + Vector3.up * 1.4f;
                Vector3 look = pivot != null ? pivot.forward : _pilot.transform.forward;

                // score the closest node to the aim ray in metres of miss;
                // reach matches DrawRange so far-wall ink stays declarable
                float reach = DrawingConfig.DrawRange;
                float prefilter = (reach + 2f) * (reach + 2f); // centroid can sit past the nearest node
                _cands.Clear();
                foreach (var s in world.Strokes)
                {
                    if (!Mine(s)) continue;
                    if ((s.Centroid() - eye).sqrMagnitude > prefilter) continue; // cheap prefilter, generous
                    if (NearestNodeToRay(s, eye, look, 0.05f, reach, PointingCone, out var p, out float miss))
                        _cands.Add((s, p, miss));
                }
                if (_cands.Count == 0) { ClearInkRead(); return; }
                _cands.Sort((x, y) => x.miss.CompareTo(y.miss));

                // sticky aim: the previous seed keeps the seat while it stays
                // nearly as good as the winner
                for (int i = 1; i < _cands.Count; i++)
                    if (_cands[i].s == _lastSeed)
                    {
                        if (_cands[i].miss <= _cands[0].miss + 0.12f)
                        {
                            var keep = _cands[i];
                            _cands.RemoveAt(i);
                            _cands.Insert(0, keep);
                        }
                        break;
                    }

                // occlusion falls through to the runner-up
                foreach (var (s, p, _) in _cands)
                {
                    Vector3 toSeed = p - eye;
                    float dist = toSeed.magnitude;
                    if (dist < 1e-3f) continue;
                    if (Physics.Raycast(eye, toSeed / dist, out var hit, dist,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && (s.Surface == null
                            || (hit.transform != s.Surface && !hit.transform.IsChildOf(s.Surface)
                                && !s.Surface.IsChildOf(hit.transform))))
                        continue; // behind something - try the next best
                    seed = s;
                    seedC = p;
                    break;
                }
                if (seed == null) { ClearInkRead(); return; }
            }

            // steady-aim cache: same seed (or cached member) + same stroke
            // count + all members still eligible = keep the cluster and verdict.
            // The offer depends on the BOOK too - opening it or turning a page
            // must recompute even when the aimed ink hasn't changed.
            int bookKey = !GrimoirePages.BookOpen ? 0
                : GrimoirePages.SealPageOpen ? -1 : (int)GrimoirePages.PageRune + 1;
            if ((seed == _lastSeed || _inkMembers.Contains(seed))
                && world.Strokes.Count == _lastStrokeCount
                && bookKey == _lastBookKey
                && _inkMembers.Count > 0)
            {
                bool stillTrue = true;
                foreach (var m in _inkMembers)
                    if (!Mine(m)) { stillTrue = false; break; }
                if (stillTrue) { _lastSeed = seed; return; }
            }
            _inkRune = RuneType.None;   // full recompute: read fresh
            _inkSeal = false;
            _lastSeed = seed;
            _lastStrokeCount = world.Strokes.Count;
            _lastBookKey = bookKey;

            // flood among nearby strokes only
            _near.Clear();
            foreach (var s in world.Strokes)
            {
                if (s == seed || !Mine(s)) continue;
                if ((s.Centroid() - seedC).sqrMagnitude > 16f) continue;
                _near.Add(s);
            }
            // one drawing = ink that touches
            float joinGap = DrawingConfig.RuneTouchDistance;
            _inkMembers.Clear();
            _inkMembers.Add(seed);
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = _near.Count - 1; i >= 0; i--)
                {
                    var s = _near[i];
                    foreach (var m in _inkMembers)
                        if (RuneGlyph.InkTouches(s, m, joinGap))
                        {
                            _inkMembers.Add(s);
                            _near.RemoveAt(i);
                            grew = true;
                            break;
                        }
                }
            }

            // classify only when the aimed drawing changed. 0 is the
            // nothing-aimed sentinel, so the hash must never be 0; node counts
            // in the key force a recompute after erasing.
            int hash = 17;
            foreach (var m in _inkMembers)
                hash ^= unchecked(m.Id * (int)2654435761 + m.Nodes.Count * 31);
            if (hash == 0) hash = 1;
            if (hash != _lastHash)
            {
                _lastHash = hash;
                (_lastReadAs, _lastReadScore) = RuneGlyph.ReadVerdict(
                    _inkMembers, Grimoire.LocalPlayerId); // guarded: never re-reads foreign ink (netcode §1)
            }
            bool readsFine = _lastReadAs != RuneType.None
                && _lastReadScore >= DrawingConfig.MinRuneScore;

            // seal page: world drawings only - the detector must still find
            // the loop on F. Body ink is excluded; body seals close via poses.
            if (GrimoirePages.BookOpen && GrimoirePages.SealPageOpen)
            {
                bool onBody = false;
                foreach (var m in _inkMembers)
                    if (m.Persistent) { onBody = true; break; }
                _inkSeal = !onBody;
                return;
            }

            var page = GrimoirePages.PageRune;
            if (GrimoirePages.BookOpen && page != RuneType.None
                && RuneLibrary.IsUnlocked(Grimoire.LocalPlayerId, page))
            {
                // the open page is the declaration; skip if already declared
                // or already reading so
                bool allDeclared = true;
                foreach (var m in _inkMembers)
                    if (m.DeclaredRune != page) { allDeclared = false; break; }
                if (allDeclared) return;
                if (readsFine && _lastReadAs == page) return;
                _inkRune = page;
                return;
            }

            // book closed (or elsewhere): an unreadable, undeclared drawing
            // in sight pops the hint that this feature exists
            bool anyDeclared = false;
            foreach (var m in _inkMembers)
                if (m.DeclaredRune != RuneType.None) { anyDeclared = true; break; }
            _fizzledInSight = !readsFine && !anyDeclared;
        }

        /// Seal page + F: run the closure detectors over the aimed drawing;
        /// found = the seal forms and casts, not found = the log says why.
        void DeclareSeal()
        {
            var world = DrawingWorld.Instance;
            if (world == null) return;
            Highlight(null); // put the real colours back before the seal repaints them
            if (!NetGame.IsAuthority)
            {
                // world seals are HOST business - ship the intent, keep the UX (netcode §2)
                NetSync.SendDeclareSealIntent(_inkMembers);
                _inkMembers.Clear();
                _inkSeal = false;
                DeclareInReach = false;
                _lastHash = 0;
                _inkScan = 0f;
                return;
            }
            if (world.TryDeclareSeal(new List<Stroke>(_inkMembers)))
            {
                Juice.Chime(_pilot.transform.position);
                // the seal page's own bar (its ramp lives on RuneType.None)
                Grimoire.BumpWriting(Grimoire.LocalPlayerId, RuneType.None,
                    DrawingConfig.WritingPerDeclare);
            }
            _inkMembers.Clear();
            _inkSeal = false;
            DeclareInReach = false;
            _lastHash = 0;
            _inkScan = 0f;
        }

        /// Stamp DeclaredRune on the drawing (seals trust it outright), leave
        /// the ink in place, and teach the strokes as handwriting.
        void DeclareInk()
        {
            Highlight(null); // drop the aim tint first - RuneColor below is the real answer
            var raw = RuneGlyph.RawStrokesOf(_inkMembers);
            foreach (var m in _inkMembers)
            {
                m.DeclaredRune = _inkRune;
                m.SetColor(Stroke.RuneColor); // it reads as a rune NOW
                m.MarkDirty();
            }
            // ★ ROUND-ONLY (his rule): a match declare teaches the matcher for
            // THIS run and is forgotten at round end - it must never write the
            // template file, where it was even ROLLING OUT his oldest authored
            // studio drawing to make room. Only Rune Studio persists.
            bool learned = RuneLibrary.AddSample(_inkRune, raw, quiet: true);
            NetSync.PushDeclare(_inkMembers, _inkRune); // every machine stamps the same ink (netcode §1)
            // a correction is real practice - the writing bar takes a full step
            Grimoire.BumpWriting(Grimoire.LocalPlayerId, _inkRune, DrawingConfig.WritingPerDeclare);
            Vector3 at = Vector3.zero;
            foreach (var m in _inkMembers) at += m.Centroid();
            at /= _inkMembers.Count;
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, at);
            Juice.Chime(at);
            DrawingWorld.Instance?.LogEvent(learned
                ? $"declared: this is {RuneLibrary.IconInline(_inkRune)}. the book learns your hand"
                : $"declared: this is {RuneLibrary.IconInline(_inkRune)}");
            _inkMembers.Clear();
            _inkRune = RuneType.None;
            DeclareInReach = false;
            _inkScan = 0f;
        }

        /// The node of `s` closest to the aim ray, and how far off-axis it sits
        /// (in metres). Nodes nearer than `minAlong` or further than `maxAlong`
        /// down the ray don't count, and neither does anything outside the
        /// pointing cone (`coneSlope` × distance). False = this stroke is not
        /// being pointed at at all.
        static bool NearestNodeToRay(Stroke s, Vector3 origin, Vector3 dir,
                                     float minAlong, float maxAlong, float coneSlope,
                                     out Vector3 point, out float miss)
        {
            point = default;
            miss = float.MaxValue;
            foreach (var n in s.Nodes)
            {
                if (n == null) continue;
                Vector3 p = n.transform.position;
                float along = Vector3.Dot(p - origin, dir);
                if (along < minAlong || along > maxAlong) continue;
                float d = Vector3.Distance(p, origin + dir * along);
                if (coneSlope < float.MaxValue && d > along * coneSlope) continue;
                if (d < miss) { miss = d; point = p; }
            }
            return miss < float.MaxValue;
        }

        /// Tints the ink F will act on. Runs every frame: allocates nothing,
        /// repaints only on change, and restores the colour saved when a stroke
        /// was first lit (never the highlight) so the tint can't bake in.
        void Highlight(List<Stroke> members)
        {
            _tintKeep.Clear();
            _tintKeepWas.Clear();
            for (int i = 0; i < _tinted.Count; i++)
            {
                var s = _tinted[i];
                bool live = s != null && s.Alive && s.State == StrokeState.Open;
                if (live && members != null && members.Contains(s))
                {
                    _tintKeep.Add(s);
                    _tintKeepWas.Add(_tintedWas[i]);
                    continue;
                }
                // a stroke that got sealed, burned or split under the highlight
                // is NOT repainted - its new state already owns its colour
                if (live) s.SetColor(_tintedWas[i]);
            }
            _tinted.Clear();
            _tintedWas.Clear();
            _tinted.AddRange(_tintKeep);
            _tintedWas.AddRange(_tintKeepWas);

            if (members == null) return;
            foreach (var m in members)
            {
                if (m == null || !m.Alive || m.State != StrokeState.Open) continue;
                if (_tinted.Contains(m)) continue; // already lit; its colour is already saved
                _tinted.Add(m);
                _tintedWas.Add(m.Color);
                m.SetColor(DeclareTint);
            }
        }

        static readonly Color DeclareTint = new Color(0.85f, 0.8f, 1f);

        /// The most centred analyzable thing in reach. Things whose rune is
        /// already held are skipped entirely.
        Analyzable Best()
        {
            Analyzable best = null;
            float bestAim = 0f;
            int me = Grimoire.LocalPlayerId;
            foreach (var a in Analyzable.Living)
            {
                if (a == null || !a.CanTeach) continue;
                if (a.NextFor(me) == RuneType.None) continue; // nothing left for you here
                float aim = _pilot.AimScore(a.transform.position, a.Range, Cone, a.transform);
                if (aim > bestAim) { bestAim = aim; best = a; }
            }
            return best;
        }
    }
}
