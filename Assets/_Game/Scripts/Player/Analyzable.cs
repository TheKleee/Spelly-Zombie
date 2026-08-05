using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// THE WORLD TEACHES (Marko Jul 25): you don't find floating runes — you
    /// find a FLAME, a PUDDLE, a LOG, and absorb it into the grimoire. The
    /// thing you absorb IS the lesson: fire teaches Heat, water teaches Liquid,
    /// a log teaches Solid.
    ///
    /// ABSORBING CONSUMES THE SOURCE (his ruling): the torch goes out, so that
    /// one torch teaches exactly ONE player. Everyone else must find their own
    /// fire — which is what keeps kits different and forces you to combine
    /// spells instead of all owning the same ones.
    ///
    /// SPELL-BORN THINGS CAN NEVER BE ANALYZED — not "isn't currently a spell"
    /// but PROVENANCE: anything a spell created is permanently ineligible, even
    /// after the touch law turns it into an object. Otherwise conjure → touch →
    /// analyze is a free rune printer.
    ///
    /// AXIOM: everything here is yours. Put this on your torch/puddle/log
    /// prefab, pick the card it teaches, and supply your own absorb effect.
    /// Nothing about your object is altered except what you asked for.
    public class Analyzable : MonoBehaviour
    {
        [Header("WHAT THIS TEACHES")]
        [Tooltip("The ONE rune the grimoire learns from this object. One page = one rune " +
                 "(all 12 collected individually) — a flame teaches HeatUp, not its opposite.")]
        public RuneType Teaches = RuneType.HeatUp;

        [Header("WHAT HAPPENS WHEN IT'S ABSORBED — your call")]
        [Tooltip("Your effect at the moment of absorbing. Empty = the default poof.")]
        public GameObject AbsorbFx;
        [Tooltip("What's left behind — a spent torch, a dry basin. Empty = see Consume below.")]
        public GameObject Remains;
        [Tooltip("ON (his ruling): the source is used up, so only ONE player can learn from it. " +
                 "Turn OFF for a teaching object you want everyone to be able to read.")]
        public bool Consume = true;
        [Tooltip("With Consume on and no Remains prefab: destroy the whole object, or just " +
                 "switch off these renderers/lights (a torch that goes out but stays standing).")]
        public bool DestroyOnAbsorb = false;
        [Tooltip("Objects switched off instead of destroyed. Empty = this object's own renderers + lights.")]
        public GameObject[] Extinguish;

        [Header("Reach")]
        public float Range = 2.6f;

        /// Set by the spell systems on anything they create — such a thing can
        /// NEVER teach a rune, however many times it changes hands.
        [System.NonSerialized] public bool SpellBorn;

        public bool Spent { get; private set; }

        static readonly List<Analyzable> All = new List<Analyzable>();
        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// The one live registry the grimoire scans — no per-object Update.
        public static IReadOnlyList<Analyzable> Living => All;

        public bool CanTeach => !Spent && !SpellBorn;

        /// Absorb into `owner`'s grimoire. Returns false when there was
        /// nothing to learn (already known, spent, or spell-born).
        public bool AbsorbInto(int ownerId)
        {
            if (!CanTeach) return false;
            if (Grimoire.HasRune(ownerId, Teaches)) return false;   // already in the book

            Grimoire.UnlockRune(ownerId, Teaches);
            Spent = true;

            if (AbsorbFx != null) Instantiate(AbsorbFx, transform.position, Quaternion.identity);
            else if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, transform.position);
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent($"the grimoire drinks it in — {Teaches} is yours");

            if (!Consume) return true;

            if (Remains != null)
            {
                Instantiate(Remains, transform.position, transform.rotation, transform.parent);
                Destroy(gameObject);
                return true;
            }
            if (DestroyOnAbsorb) { Destroy(gameObject); return true; }

            // it goes OUT but stays standing — the spent torch
            if (Extinguish != null && Extinguish.Length > 0)
            {
                foreach (var go in Extinguish) if (go != null) go.SetActive(false);
            }
            else
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r is ParticleSystemRenderer) r.gameObject.SetActive(false);
                foreach (var l in GetComponentsInChildren<Light>(true)) l.enabled = false;
            }
            return true;
        }
    }

    /// THE GRIMOIRE'S EYE — hold out the book and absorb what you're looking
    /// at. Lives on the player; scans the small Analyzable registry only when
    /// the key is available, so it costs nothing per object in the world.
    public class GrimoireAbsorb : MonoBehaviour
    {
        public const float Cone = 0.72f;   // a bit wider than the grab cone — you're reading, not grabbing

        /// True while the local player is aiming at something LEARNABLE.
        /// ABSORB IS ON **F** (Marko: "Grimoire using G to absorb is wrong —
        /// it's going to use F, the same thing it's using to set the
        /// handwriting"), and **ABSORB TAKES PRIORITY** when a drawing and a
        /// learnable object are both under the cursor. G is now purely the
        /// book's up/down key.
        public static bool TargetInReach { get; private set; }

        /// True while F would DECLARE the aimed drawing (book open on a rune
        /// page or the seal page). WeaponSlots yields the F weapon-drop while
        /// either this or TargetInReach is up.
        public static bool DeclareInReach { get; private set; }

        SimpleFPSController _pilot;

        // ---- DECLARE A DRAWING (Marko's correction: "the book shouldn't
        // take away the ink"): open the grimoire to a rune's page, aim at a
        // drawing of yours the game read WRONG, and STATE that it is in fact
        // this rune. The strokes get DeclaredRune stamped (seals trust it
        // outright — enclosing it casts exactly what you declared), the ink
        // STAYS where you drew it, and the drawing joins your handwriting
        // pool so next time recognition gets it right on its own.
        /// How far off the aim axis a piece of ink may sit and still count as
        /// "what he is pointing at", as a fraction of its distance — being a
        /// fraction it means the same POINTING gesture at any range. The old
        /// test was a fixed 44° cone on the stroke's CENTROID with no distance
        /// term at all; scoring nodes instead of the centroid was the fix.
        ///
        /// 0.28 ≈ 16°, not the 7° it briefly was. A seal is a LOOP, and the
        /// natural "this is a seal" gesture is to aim at the middle of it —
        /// where there is no ink. At 7° a 1m seal viewed from 2m put its nearest
        /// line 0.5m off axis (slope 0.25) and every candidate was rejected, so
        /// F did nothing: problem (B) again, from the opposite direction.
        /// Candidates are still RANKED by metres of miss, so near-and-under-the-
        /// crosshair keeps beating far-and-vaguely-that-way — the cone only says
        /// what is eligible, not what wins.
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
        bool _fizzledInSight;   // an unreadable drawing is aimed — teach the flow
        float _inkScan;
        RuneType _promptRune = RuneType.None; // prompt caches — the strings were
        string _declarePrompt;                // rebuilt EVERY frame in reach
        Analyzable _promptTarget;
        string _absorbPrompt;
        int _lastHash;          // aimed-drawing identity — classify ONLY on change
        RuneType _lastReadAs;
        float _lastReadScore;

        void Awake() => _pilot = GetComponent<SimpleFPSController>();
        void OnDisable() { TargetInReach = false; DeclareInReach = false; Highlight(null); }

        void Update()
        {
            if (_pilot == null || _pilot.IsDowned)
            { TargetInReach = false; DeclareInReach = false; Highlight(null); return; }
            var kb = Keyboard.current;
            // THE BOOK WORKS EVERYWHERE YOU CAN SEE INK (Marko: "there is
            // still no way for me to command the grimoire" — he tests in
            // third person): first person aims with the crosshair, third
            // person with the camera, the body-paint easel with the cursor.
            if (kb == null)
            { TargetInReach = false; DeclareInReach = false; Highlight(null); return; }

            // ALREADY-ABSORBED THINGS ARE INVISIBLE TO THE BOOK (his rule:
            // "if you already have absorbed this element you can't do it
            // again so it's ignored") — Best() skips them, so F falls
            // straight through to the drawing behind them.
            var target = Best();
            TargetInReach = target != null;
            if (target == null)
            {
                // no absorbable object — the DECLARE flow: with the book OPEN
                // to a rune's page, F states a misread drawing IS that rune;
                // on the SEAL page, F traces the drawing's loop and casts it.
                ScanInk();
                bool declRune = _inkRune != RuneType.None && _inkMembers.Count > 0;
                bool declSeal = _inkSeal && _inkMembers.Count > 0;
                DeclareInReach = declRune || declSeal;
                Highlight(DeclareInReach ? _inkMembers : null);
                if (declRune)
                {
                    if (_promptRune != _inkRune || _declarePrompt == null)
                    {
                        _promptRune = _inkRune;
                        _declarePrompt = $"this is {RuneLibrary.Icon(_inkRune)} — declare it";
                    }
                    UIPrompt.Show("F", _declarePrompt, new Color(0.85f, 0.8f, 1f));
                    if (kb.fKey.wasPressedThisFrame) DeclareInk();
                }
                else if (declSeal)
                {
                    UIPrompt.Show("F", "this is a seal — trace its loop",
                        new Color(0.85f, 0.8f, 1f));
                    if (kb.fKey.wasPressedThisFrame) DeclareSeal();
                }
                else if (_fizzledInSight)
                {
                    // nobody knows this exists by default (Marko: "it should
                    // pop up to show them") — an unreadable drawing teaches
                    // the flow the moment you look at one
                    UIPrompt.Show("G", "unreadable — open the book to its rune's page, F declares it",
                        new Color(0.7f, 0.7f, 0.75f));
                }

                // NEVER SILENTLY FAIL (his standing rule). With the book open on
                // a page that F can act on, but nothing under the aim, F used to
                // do absolutely nothing and say absolutely nothing — which reads
                // exactly like "the declare is broken".
                if (!DeclareInReach && kb.fKey.wasPressedThisFrame && GrimoirePages.BookOpen
                    && (GrimoirePages.SealPageOpen || GrimoirePages.PageRune != RuneType.None))
                    DrawingWorld.Instance?.LogEvent(
                        "the book has nothing selected — aim at one of your own LINES (it lights up when the book has it)");
                return;
            }
            DeclareInReach = false;
            Highlight(null); // an absorbable thing took the key — drop the ink highlight

            // ABSORB WINS THE KEY when both are possible (his priority rule)
            if (!ReferenceEquals(target, _promptTarget) || _absorbPrompt == null)
            {
                _promptTarget = target;
                _absorbPrompt = $"absorb — learn {RuneLibrary.Icon(target.Teaches)}";
            }
            UIPrompt.Show("F", _absorbPrompt, new Color(0.85f, 0.8f, 1f));
            Hints.Offer(Hints.Id.Absorb);
            if (kb.fKey.wasPressedThisFrame)
            {
                target.AbsorbInto(Grimoire.LocalPlayerId);
                Hints.Retire(Hints.Id.Absorb);
            }
        }

        /// Find the best-aimed drawing of MY OWN open ink (seed by aim, then
        /// the whole touching cluster — connected ink is one drawing, same
        /// law as the seals) and decide what the book offers for it.
        ///
        /// THE LAG FIX (Marko: "it's lagging a lot but the system works"):
        /// the old scan fired a physics RAYCAST per stroke and re-ran the
        /// full recognizer ensemble five times a second forever. Now the aim
        /// pass is pure cone math with ONE raycast for the winner, the flood
        /// only tests strokes near the seed, and the recognizer runs ONLY
        /// when the aimed drawing actually changes.
        void ScanInk()
        {
            if (Time.time < _inkScan) return;
            _inkScan = Time.time + 0.25f;
            // the recognizer SLEEPS while the pen is down (Marko: "should not
            // fire all the time — only after you release the mouse")
            var w0 = DrawingWorld.Instance;
            if (w0 != null)
                for (int i = 0; i < w0.Strokes.Count; i++)
                    if (w0.Strokes[i].State == StrokeState.Drawing) return;
            _inkRune = RuneType.None;
            _inkSeal = false;
            _fizzledInSight = false;
            var world = DrawingWorld.Instance;
            if (world == null) { _inkMembers.Clear(); _lastHash = 0; return; }

            bool Mine(Stroke s) => s != null && s.Alive && s.State == StrokeState.Open
                && s.OwnerId == Grimoire.LocalPlayerId && !s.SealResidue
                && s.ChainIntact() && !s.Hidden();

            Stroke seed = null;
            Vector3 seedC = default;
            // ON THE EASEL (body paint or any free-cursor mode) the pen IS
            // the pointer (Marko: "while drawing on the body you should also
            // be able to use grimoire — drawing on body is the main quick
            // weapon"): ink is picked under the CURSOR — no aim cone, no
            // occlusion ray, you are looking straight at your own body.
            bool easel = SelfPaint.IsActive || Cursor.lockState != CursorLockMode.Locked;
            if (easel)
            {
                var cam = Camera.main;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (cam == null || mouse == null) { _inkMembers.Clear(); _lastHash = 0; return; }
                var mray = cam.ScreenPointToRay(mouse.position.ReadValue());
                float bestD = 0.35f; // the cursor must pass near the INK
                foreach (var s in world.Strokes)
                {
                    if (!Mine(s)) continue;
                    if (NearestNodeToRay(s, mray.origin, mray.direction, 0f, 60f, float.MaxValue,
                            out var p, out float miss) && miss < bestD)
                    { bestD = miss; seed = s; seedC = p; }
                }
                if (seed == null) { _inkMembers.Clear(); _lastHash = 0; return; }
            }
            else
            {
                var pivot = _pilot.CameraPivot;
                Vector3 eye = pivot != null ? pivot.position
                    : _pilot.transform.position + Vector3.up * 1.4f;
                Vector3 look = pivot != null ? pivot.forward : _pilot.transform.forward;

                // AIM AT THE INK, NOT AT THE MIDDLE OF NOTHING.
                //
                // This used to score each stroke by the cosine between the look
                // axis and its CENTROID, and a seal is a LOOP — its centroid is
                // the empty hole in the middle of it. He aims at the line, the
                // game scored the hole; on a 1m circle that puts the scored point
                // half a metre off his crosshair. Worse, a rune drawn INSIDE the
                // seal has almost the same centroid, so seal-versus-rune was
                // being decided by a few centimetres of noise. That is exactly
                // "it sometimes doesn't select what we're looking at".
                //
                // Two more defects went with it: distance wasn't in the score at
                // all, so a drawing 3m away could out-score the one under the
                // crosshair just by being far enough that its offset subtended a
                // smaller angle — the book reaching across the room. And the
                // cone was 44° wide.
                //
                // Now: score the closest NODE to the aim ray, in METRES OF MISS,
                // through a cone that scales with distance (~7°). Near-and-under-
                // the-crosshair beats far-and-slightly-more-centred, and seedC
                // lands ON the ink, which is also the only honest place to start
                // the occlusion ray from.
                // THE BOOK MUST REACH AS FAR AS THE PEN. This capped the aim at
                // 3.2m while DrawRange is 8m, so ink drawn on a far wall — legal
                // ink, in pen range — could never be declared: _cands came back
                // empty, ScanInk returned, and F did nothing and said nothing.
                // That is his (B) with a brand-new cause. The cone does the
                // discriminating; distance is not what makes an aim wrong.
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
                if (_cands.Count == 0) { _inkMembers.Clear(); _lastHash = 0; return; }
                _cands.Sort((x, y) => x.miss.CompareTo(y.miss));

                // OCCLUSION FALLS THROUGH TO THE RUNNER-UP. It used to abort the
                // whole scan the moment the winner turned out to be behind
                // something, so the drawing he was actually aiming at never got
                // a second look — the "F did nothing" flavour of the bug.
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
                        continue; // behind something — try the next best
                    seed = s;
                    seedC = p;
                    break;
                }
                if (seed == null) { _inkMembers.Clear(); _lastHash = 0; return; }
            }

            // the whole DRAWING: flood among NEARBY strokes only (a drawing
            // is metres, not the map) — node-pair touch math runs only for
            // genuine neighbour candidates
            _near.Clear();
            foreach (var s in world.Strokes)
            {
                if (s == seed || !Mine(s)) continue;
                if ((s.Centroid() - seedC).sqrMagnitude > 16f) continue;
                _near.Add(s);
            }
            // THE MAIN RULE, EVERYTHING, NO EXCEPTIONS (Marko: "only when
            // lines are touching is the main rule for everything: seals or
            // runes" — the arrow/Y gap allowance is DEAD, it merged drawings
            // from across the floor): one drawing = ink that touches.
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

            // Classify ONLY when the aimed drawing changed.
            //
            // 0 IS THE "NOTHING AIMED" SENTINEL — every early return above writes
            // _lastHash = 0 — so the identity must never BE 0. It was: the first
            // stroke of the session has Id 0 (Stroke._nextId starts at 0), so a
            // one-stroke drawing hashed to exactly 0, the change test never
            // fired, RuneLibrary.Classify never ran, and _lastReadAs/_lastReadScore
            // stayed stale. readsFine was then wrong in both directions: the book
            // offered to "declare" ink that already read correctly, and popped
            // "unreadable" over a drawing that reads fine.
            //
            // Node counts are in the key too, matching RuneGlyph.CacheKey — erase
            // half the drawing and the verdict must be recomputed, not remembered.
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

            // THE SEAL PAGE (Marko: "a page for seals… seal must always find
            // a closed path"): any open WORLD drawing of yours can be traced —
            // the detector still has to FIND the loop when you press F. Body
            // ink is excluded (his ruling: the flat mirror can't follow
            // limbs) — body seals close naturally through poses instead.
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
                // the open page IS the declaration — no guessing, no scoring
                // (Marko: you state that it is in fact this rune). Only
                // gate: nothing to fix if it already is / already reads so.
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

        /// SEAL PAGE + F: run the real closure detectors over the aimed
        /// drawing — found = the seal forms and CASTS (the normal path);
        /// not found = the log says why. The book never fakes a loop.
        void DeclareSeal()
        {
            var world = DrawingWorld.Instance;
            if (world == null) return;
            Highlight(null); // put the real colours back before the seal repaints them
            if (!NetGame.IsAuthority)
            {
                // world seals are HOST business — ship the intent, keep the UX (netcode §2)
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

        /// "That is in fact this rune": stamp the drawing (seals trust
        /// DeclaredRune outright — no recognition, no guessing), leave the
        /// ink exactly where it is, and teach the strokes as handwriting so
        /// next time it reads right on its own.
        void DeclareInk()
        {
            Highlight(null); // drop the aim tint first — RuneColor below is the real answer
            var raw = RuneGlyph.RawStrokesOf(_inkMembers);
            foreach (var m in _inkMembers)
            {
                m.DeclaredRune = _inkRune;
                m.SetColor(Stroke.RuneColor); // it reads as a rune NOW
                m.MarkDirty();
            }
            bool learned = RuneLibrary.AddSample(_inkRune, raw);
            NetSync.PushDeclare(_inkMembers, _inkRune); // every machine stamps the same ink (netcode §1)
            // a correction is real practice — the writing bar takes a full step
            Grimoire.BumpWriting(Grimoire.LocalPlayerId, _inkRune, DrawingConfig.WritingPerDeclare);
            Vector3 at = Vector3.zero;
            foreach (var m in _inkMembers) at += m.Centroid();
            at /= _inkMembers.Count;
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, at);
            Juice.Chime(at);
            DrawingWorld.Instance?.LogEvent(learned
                ? $"declared: this is {RuneLibrary.Icon(_inkRune)} — the book learns your hand"
                : $"declared: this is {RuneLibrary.Icon(_inkRune)}");
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
        ///
        /// NODES, not the centroid: the centroid of a seal is the hole he is
        /// looking THROUGH, and the centroid of a long stroke can sit anywhere
        /// but on the stroke.
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

        /// SHOW HIM WHAT F WILL ACT ON, BEFORE HE PRESSES IT. His rule is that
        /// nothing may fail silently — and picking the wrong drawing is a silent
        /// failure you only discover after the seal fires on the wrong ink. With
        /// the selection lit up, a wrong pick is visible while it's still free to
        /// fix, and this whole class of bug reports itself from now on.
        /// Runs every frame, so it allocates nothing and only repaints on CHANGE:
        /// ink that stays selected keeps the colour saved when it was first lit,
        /// never the highlight itself — or the tint would bake in permanently.
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
                // is NOT repainted — its new state already owns its colour
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

        /// The most CENTRED analyzable thing in reach (same aim rule as every
        /// other interaction — never "whatever happens to be nearest").
        /// Things whose rune you ALREADY hold are skipped entirely, so they
        /// never sit between you and a drawing you meant to declare.
        Analyzable Best()
        {
            Analyzable best = null;
            float bestAim = 0f;
            int me = Grimoire.LocalPlayerId;
            foreach (var a in Analyzable.Living)
            {
                if (a == null || !a.CanTeach) continue;
                if (Grimoire.HasRune(me, a.Teaches)) continue; // already yours — ignored
                float aim = _pilot.AimScore(a.transform.position, a.Range, Cone, a.transform);
                if (aim > bestAim) { bestAim = aim; best = a; }
            }
            return best;
        }
    }
}
