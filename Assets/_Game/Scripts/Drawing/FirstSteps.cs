using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE FIRST STEPS, in the lobby while you are alone in it: a glowing blue
    /// line draws in front of you, a glowing bead riding its tip. Three lessons, in
    /// order: a rune (Push) on the ground; the ring around your latest rune
    /// (around a faint one of its own once yours is gone); two runes inside ONE
    /// ring. A lesson you have never done repeats until you do it. One you
    /// have done before comes back as a reminder on every launch: a few times,
    /// or until you do it again. Local only, never ink; off with the hints
    /// switch or the ghost lines switch. Your first rune ever also opens the
    /// book on the seal page.
    public class FirstSteps : MonoBehaviour
    {
        const string RuneKey = "sz_first_rune", SealKey = "sz_first_seal", ComboKey = "sz_first_combo";
        const int ReminderTimes = 4;      // how often a lesson you already know plays per launch
        static bool _loaded, _runeDone, _sealDone, _comboDone;
        // this launch: done again, or shown often enough as a reminder
        static readonly bool[] _seenNow = new bool[3];
        static readonly int[] _shown = new int[3];
        static readonly List<Stroke> _lastRune = new List<Stroke>();

        public static bool RuneDone { get { Load(); return _runeDone; } }
        public static bool SealDone { get { Load(); return _sealDone; } }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _runeDone = PlayerPrefs.GetInt(RuneKey, 0) != 0;
            _sealDone = PlayerPrefs.GetInt(SealKey, 0) != 0;
            _comboDone = PlayerPrefs.GetInt(ComboKey, 0) != 0;
        }

        static bool EverDone(int lesson) => lesson == 0 ? _runeDone : lesson == 1 ? _sealDone : _comboDone;

        /// The lesson to show now: the first one not done (or reminded enough) this launch. -1 none.
        static int Lesson()
        {
            for (int i = 0; i < 3; i++) if (!_seenNow[i]) return i;
            return -1;
        }

        /// A drawing of your own hand read as a rune (DrawingWorld, pen-up).
        public static void RuneDrawn(List<Stroke> cluster)
        {
            Load();
            _lastRune.Clear();
            if (cluster != null) _lastRune.AddRange(cluster);
            _seenNow[0] = true;
            bool first = !_runeDone;
            if (first)
            {
                _runeDone = true;
                PlayerPrefs.SetInt(RuneKey, 1);
                PlayerPrefs.Save();
            }
            // the next thing to learn: the book opens on the seal page, where F rings the rune by
            // itself. The first time ever, anywhere; after that a reminder while the ring lesson plays
            if (first || (!_seenNow[1] && LessonsOn())) GrimoirePages.ShowSealPage();
        }

        /// The lobby is the sandbox only while you are alone in it, and the hints switch rules.
        static bool LessonsOn() => ActiveScene.Name == "Lobby" && Hints.Enabled && GhostHand.Enabled
            && NetSync.RemoteCount == 0;

        /// A seal of your own closed (DrawingWorld.CreateSeal), holding this many runes.
        public static void SealDrawn(int runes)
        {
            Load();
            _seenNow[0] = _seenNow[1] = true;
            if (runes >= 2) _seenNow[2] = true;
            bool save = false;
            if (!_sealDone) { _sealDone = true; PlayerPrefs.SetInt(SealKey, 1); save = true; }
            if (runes >= 2 && !_comboDone) { _comboDone = true; PlayerPrefs.SetInt(ComboKey, 1); save = true; }
            if (save) PlayerPrefs.Save();
        }

        /// Options: the lessons from the start again (with the hints).
        public static void ResetAll()
        {
            Load();
            _runeDone = _sealDone = _comboDone = false;
            for (int i = 0; i < 3; i++) { _seenNow[i] = false; _shown[i] = 0; }
            PlayerPrefs.DeleteKey(RuneKey);
            PlayerPrefs.DeleteKey(SealKey);
            PlayerPrefs.DeleteKey(ComboKey);
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~FirstSteps");
            DontDestroyOnLoad(go);
            go.AddComponent<FirstSteps>();
        }

        // ------------------------------------------------------ the ghost hand
        const float Ahead = 2.4f;          // metres in front of you
        const float Size = 0.5f;           // the rune's larger side
        readonly GhostHand _hand = new GhostHand();
        readonly List<List<Vector3>> _paths = new List<List<Vector3>>();
        int _lesson = -1;                                        // the one on show: 0 rune, 1 ring, 2 two runes in one ring
        Vector3 _spot;

        void Update()
        {
            Load();
            int lesson = Lesson();
            if (lesson < 0) { Clear(); return; }
            if (!LessonsOn() || GameMenu.IsOpen || LoadEgg.Closed) { Clear(); return; }
            // the floating F draws its own preview with the hand: one thing drawn at a time
            if (GrimoireAbsorb.PreviewLive) { Clear(); return; }
            var pilot = LocalPilot();
            if (pilot == null) { Clear(); return; }

            if (!_hand.Alive || lesson != _lesson)
            {
                Clear();
                if (!Begin(pilot, lesson)) return;
                // a lesson you already know is a reminder: a few times, then it lets you be
                if (EverDone(lesson) && ++_shown[lesson] > ReminderTimes) { _seenNow[lesson] = true; Clear(); return; }
            }
            // walked off mid-lesson: it starts over where you are
            if (Vector3.Distance(pilot.transform.position, _spot) > 7f) { Clear(); return; }
            _hand.Animate(Time.deltaTime);
        }

        static SimpleFPSController LocalPilot()
        {
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) return p;
            return null;
        }

        void Clear()
        {
            _hand.Clear();
            _paths.Clear();
        }

        /// One lesson laid out: the rune ahead on the ground, the ring around your rune, or
        /// two runes and the one ring around both. False when there is no ground to draw on.
        bool Begin(SimpleFPSController pilot, int lesson)
        {
            _lesson = lesson;
            bool ring = lesson == 1;
            _paths.Clear();
            var pivot = pilot.CameraPivot;
            Vector3 fwd = pivot != null ? pivot.forward : pilot.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = pilot.transform.forward;
            fwd.Normalize();

            List<List<Vector3>> still = null; // the faint rune a ring is drawn around

            if (ring && RingAroundYours(out var ringPath))
            {
                _paths.Add(ringPath);
            }
            else
            {
                if (!Ground(pilot.transform.position + fwd * Ahead, out var at)
                    && !Ground(pilot.transform.position + fwd * (Ahead * 0.5f), out at))
                { Clear(); return false; }
                _spot = at;
                if (lesson == 2)
                {
                    // push and pull side by side, then ONE ring around the two
                    Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized * (Size * 0.62f);
                    var left = GlyphOnGround(RuneType.Repel, at - side, fwd, Size * 0.8f);
                    var right = GlyphOnGround(RuneType.Attract, at + side, fwd, Size * 0.8f);
                    if (left == null || right == null) { Clear(); return false; }
                    _paths.AddRange(left);
                    _paths.AddRange(right);
                    var both = new List<List<Vector3>>(left);
                    both.AddRange(right);
                    _paths.Add(RingAround(both, at, fwd));
                }
                else
                {
                    var glyph = GlyphOnGround(RuneType.Repel, at, fwd, Size);
                    if (glyph == null) { Clear(); return false; }
                    if (!ring) _paths.AddRange(glyph);
                    else
                    {
                        still = glyph;
                        _paths.Add(RingAround(glyph, at, fwd));
                    }
                }
            }

            // two runes and a ring take their time
            float drawFor = GhostHand.DrawSeconds * (_paths.Count > 2 ? 3 : 1);
            if (!_hand.Begin("~FirstStepsGhost", _paths, still, drawFor)) { Clear(); return false; }
            return true;
        }

        /// The ground under a point, off the ink and off every body.
        static bool Ground(Vector3 near, out Vector3 at)
        {
            at = default;
            int mask = Physics.DefaultRaycastLayers & ~(1 << InkCanvasLayer.Layer) & ~(1 << VesselShell.Layer);
            var hits = Physics.RaycastAll(near + Vector3.up * 2.5f, Vector3.down, 6f, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.distance >= best || h.normal.y < 0.6f) continue;
                if (h.collider.GetComponentInParent<SimpleFPSController>() != null
                    || h.collider.GetComponentInParent<NetAvatar>() != null) continue;
                best = h.distance;
                at = h.point;
            }
            return best < float.MaxValue;
        }

        /// A rune, `size` across, flat on the ground at `at`, its up pointing away from you:
        /// the way a drawing on the floor reads from where you stand.
        static List<List<Vector3>> GlyphOnGround(RuneType rune, Vector3 at, Vector3 fwd, float size)
        {
            var sample = RuneLibrary.SamplePath(rune);
            if (sample == null || sample.Count == 0) return null;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var s in sample) foreach (var q in s) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
            float extent = Mathf.Max(1e-3f, Mathf.Max(max.x - min.x, max.y - min.y));
            float k = size / extent;
            Vector2 c = (min + max) * 0.5f;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            var result = new List<List<Vector3>>();
            foreach (var s in sample)
            {
                var path = new List<Vector3>(s.Count);
                foreach (var q in s)
                {
                    Vector2 d = (q - c) * k;
                    path.Add(OnGround(at + right * d.x + fwd * d.y));
                }
                if (path.Count >= 2) result.Add(path);
            }
            return result;
        }

        /// A point pressed onto the ground below it, a hair above the floor.
        static Vector3 OnGround(Vector3 p)
        {
            return Ground(p, out var at) ? at + Vector3.up * 0.01f : p + Vector3.up * 0.01f;
        }

        /// The ring F would draw around a glyph on the ground: the seal's own ellipse.
        static List<Vector3> RingAround(List<List<Vector3>> glyph, Vector3 at, Vector3 fwd)
        {
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in glyph)
                foreach (var v in p)
                {
                    Vector3 d = v - at;
                    float x = Vector3.Dot(d, right), y = Vector3.Dot(d, fwd);
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            return AutoComplete.Ellipse(at, right, fwd, minX, maxX, minY, maxY, p => OnGround(p));
        }

        /// The ring F would draw around your latest rune, on its own surface: the very path
        /// AutoComplete draws. False when that ink is gone, far, or on a body (a body seal is
        /// its own lesson).
        bool RingAroundYours(out List<Vector3> ring)
        {
            ring = null;
            var members = new List<Stroke>();
            foreach (var s in _lastRune)
                if (s != null && s.Alive && !s.Persistent && s.Nodes.Count > 0) members.Add(s);
            if (members.Count == 0) return false;
            var pilot = LocalPilot();
            Vector3 at = members[0].Centroid();
            if (pilot == null || Vector3.Distance(pilot.transform.position, at) > 8f) return false;
            if (!AutoComplete.SealPath(members, out ring, out var normal, out _)) return false;
            GhostHand.Lift(ring, normal * 0.01f);
            _spot = at;
            return true;
        }
    }
}
