using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE FIRST STEPS, in the lobby: a see-through blue hand draws in front
    /// of you what your own hand has never drawn. Never drew a rune: it draws
    /// Push on the ground, again and again. Drew one but never a seal: it draws
    /// the ring around your latest rune (around a faint rune of its own once
    /// yours is gone). Both done: it never comes back. Local only, never ink;
    /// off with the hints switch. Your first rune also opens the book on the
    /// seal page, the way an absorb opens it on the new rune's page.
    public class FirstSteps : MonoBehaviour
    {
        const string RuneKey = "sz_first_rune", SealKey = "sz_first_seal";
        static bool _loaded, _runeDone, _sealDone;
        static readonly List<Stroke> _lastRune = new List<Stroke>();

        public static bool RuneDone { get { Load(); return _runeDone; } }
        public static bool SealDone { get { Load(); return _sealDone; } }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _runeDone = PlayerPrefs.GetInt(RuneKey, 0) != 0;
            _sealDone = PlayerPrefs.GetInt(SealKey, 0) != 0;
        }

        /// A drawing of your own hand read as a rune (DrawingWorld, pen-up).
        public static void RuneDrawn(List<Stroke> cluster)
        {
            Load();
            _lastRune.Clear();
            if (cluster != null) _lastRune.AddRange(cluster);
            if (_runeDone) return;
            _runeDone = true;
            PlayerPrefs.SetInt(RuneKey, 1);
            PlayerPrefs.Save();
            GrimoirePages.ShowSealPage(); // the next thing to learn
        }

        /// A seal of your own closed (DrawingWorld.CreateSeal).
        public static void SealDrawn()
        {
            Load();
            if (_sealDone) return;
            _sealDone = true;
            PlayerPrefs.SetInt(SealKey, 1);
            PlayerPrefs.Save();
        }

        /// Options: the lessons from the start again (with the hints).
        public static void ResetAll()
        {
            Load();
            _runeDone = _sealDone = false;
            PlayerPrefs.DeleteKey(RuneKey);
            PlayerPrefs.DeleteKey(SealKey);
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
        const float Width = 0.012f;        // the line
        const float DrawSeconds = 1.2f;    // a whole lesson drawn, whatever its size
        const float Hold = 0.8f, FadeSeconds = 0.35f, Rest = 0.35f;
        static readonly Color Ink = new Color(0.45f, 0.75f, 1f, 0.6f);
        static readonly Color Faint = new Color(0.45f, 0.75f, 1f, 0.22f);

        GameObject _ghost;                                       // a scene object: dies with the scene
        readonly List<LineRenderer> _lines = new List<LineRenderer>();
        readonly List<List<Vector3>> _paths = new List<List<Vector3>>();
        readonly List<float> _lengths = new List<float>();
        float _total, _t;
        int _state;                                              // 0 drawing, 1 holding, 2 fading, 3 resting
        bool _ringLesson;
        Vector3 _spot;

        void Update()
        {
            Load();
            if (_runeDone && _sealDone) { Clear(); return; }
            if (ActiveScene.Name != "Lobby" || !Hints.Enabled || GameMenu.IsOpen || LoadEgg.Closed) { Clear(); return; }
            var pilot = LocalPilot();
            if (pilot == null) { Clear(); return; }

            bool ring = _runeDone;
            if (_ghost == null || ring != _ringLesson)
            {
                Clear();
                if (!Begin(pilot, ring)) return;
            }
            // walked off mid-lesson: it starts over where you are
            if (Vector3.Distance(pilot.transform.position, _spot) > 7f) { Clear(); return; }
            Animate();
        }

        static SimpleFPSController LocalPilot()
        {
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) return p;
            return null;
        }

        void Clear()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            _lines.Clear(); _paths.Clear(); _lengths.Clear();
        }

        /// One lesson laid out: the rune ahead on the ground, or the ring around
        /// your rune. False when there is no ground to draw on.
        bool Begin(SimpleFPSController pilot, bool ring)
        {
            _ringLesson = ring;
            _paths.Clear(); _lengths.Clear();
            var pivot = pilot.CameraPivot;
            Vector3 fwd = pivot != null ? pivot.forward : pilot.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = pilot.transform.forward;
            fwd.Normalize();

            _ghost = new GameObject("~FirstStepsGhost");
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
                var glyph = GlyphOnGround(at, fwd);
                if (glyph == null) { Clear(); return false; }
                if (!ring) _paths.AddRange(glyph);
                else
                {
                    still = glyph;
                    _paths.Add(RingAround(glyph, at, fwd));
                }
            }

            foreach (var p in _paths) { float l = Length(p); _lengths.Add(l); }
            _total = 0f;
            foreach (var l in _lengths) _total += l;
            if (_total < 0.05f) { Clear(); return false; }

            if (still != null)
                foreach (var p in still) Fill(Line(p, Faint), p, float.MaxValue);
            _lines.Clear(); // the faint rune stays whole; only the lines below animate
            foreach (var p in _paths) Line(p, Ink);
            _state = 0; _t = 0f;
            return true;
        }

        LineRenderer Line(List<Vector3> path, Color c)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(_ghost.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = MatterFX.Get(c, MoteShade.Transparent);
            lr.widthMultiplier = Width;
            lr.useWorldSpace = true;
            lr.numCapVertices = 3;
            lr.numCornerVertices = 3;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;
            _lines.Add(lr);
            return lr;
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

        /// The push arrow (Repel), Size across, flat on the ground at `at`, its up pointing
        /// away from you: the way a drawing on the floor reads from where you stand.
        static List<List<Vector3>> GlyphOnGround(Vector3 at, Vector3 fwd)
        {
            var sample = RuneLibrary.SamplePath(RuneType.Repel);
            if (sample == null || sample.Count == 0) return null;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var s in sample) foreach (var q in s) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }
            float extent = Mathf.Max(1e-3f, Mathf.Max(max.x - min.x, max.y - min.y));
            float k = Size / extent;
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

        /// The ring the seal page would draw around a glyph on the ground.
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
            return Ellipse(at, right, fwd, minX, maxX, minY, maxY, p => OnGround(p));
        }

        /// The ring around your latest rune, on its own surface. False when that
        /// ink is gone, far, or on a body (a body seal is its own lesson).
        bool RingAroundYours(out List<Vector3> ring)
        {
            ring = null;
            var members = new List<Stroke>();
            foreach (var s in _lastRune)
                if (s != null && s.Alive && !s.Persistent && s.Nodes.Count > 0) members.Add(s);
            if (members.Count == 0) return false;
            if (!RuneGlyph.Frame(members, out var origin, out var right, out var up, out var normal)) return false;
            var pilot = LocalPilot();
            if (pilot == null || Vector3.Distance(pilot.transform.position, origin) > 8f) return false;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var s in members)
                foreach (var n in s.Nodes)
                {
                    if (n == null) continue;
                    Vector3 d = n.transform.position - origin;
                    float x = Vector3.Dot(d, right), y = Vector3.Dot(d, up);
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            if (minX > maxX) return false;
            var surface = members[0].Surface;
            _spot = origin;
            ring = Ellipse(origin, right, up, minX, maxX, minY, maxY,
                p => AutoComplete.OnSurface(p, normal, surface) + normal * 0.01f);
            return true;
        }

        /// The seal page's own ring: an ellipse hugging the box, the corners
        /// inside it, the margin the book keeps.
        static List<Vector3> Ellipse(Vector3 origin, Vector3 right, Vector3 up,
            float minX, float maxX, float minY, float maxY, System.Func<Vector3, Vector3> onto)
        {
            float m = DrawingConfig.AutoSealMargin;
            Vector2 c = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float rx = (maxX - minX) * 0.5f * 1.42f + m, ry = (maxY - minY) * 0.5f * 1.42f + m;
            const int N = 48;
            var ring = new List<Vector3>(N + 1);
            for (int i = 0; i <= N; i++)
            {
                float a = -Mathf.PI * 0.5f + i * Mathf.PI * 2f / N; // starts at the bottom, like a hand would
                Vector2 q = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                ring.Add(onto(origin + right * q.x + up * q.y));
            }
            return ring;
        }

        static float Length(List<Vector3> path)
        {
            float l = 0f;
            for (int i = 1; i < path.Count; i++) l += Vector3.Distance(path[i - 1], path[i]);
            return l;
        }

        /// The line up to `upTo` metres along its path, the tip interpolated.
        /// Returns the point count set.
        static int Fill(LineRenderer lr, List<Vector3> path, float upTo)
        {
            if (path.Count == 0) { lr.positionCount = 0; return 0; }
            var pts = new List<Vector3>(path.Count + 1) { path[0] };
            float acc = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                if (acc + seg >= upTo)
                {
                    pts.Add(Vector3.Lerp(path[i - 1], path[i], seg > 1e-6f ? (upTo - acc) / seg : 1f));
                    break;
                }
                acc += seg;
                pts.Add(path[i]);
            }
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            return pts.Count;
        }

        void Animate()
        {
            _t += Time.deltaTime;
            switch (_state)
            {
                case 0: // the hand draws, line by line
                {
                    float drawn = _total * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / DrawSeconds));
                    float acc = 0f;
                    for (int i = 0; i < _paths.Count; i++)
                    {
                        float have = Mathf.Clamp(drawn - acc, 0f, _lengths[i]);
                        if (have <= 0f) _lines[i].positionCount = 0;
                        else Fill(_lines[i], _paths[i], have);
                        acc += _lengths[i];
                    }
                    if (_t >= DrawSeconds) { _state = 1; _t = 0f; }
                    break;
                }
                case 1:
                    if (_t >= Hold) { _state = 2; _t = 0f; }
                    break;
                case 2: // it thins away
                {
                    float k = 1f - Mathf.Clamp01(_t / FadeSeconds);
                    foreach (var lr in _lines)
                    {
                        lr.widthMultiplier = Width * Mathf.Max(0.05f, k);
                        lr.sharedMaterial = MatterFX.Get(new Color(Ink.r, Ink.g, Ink.b, Ink.a * k), MoteShade.Transparent);
                    }
                    if (_t >= FadeSeconds) { _state = 3; _t = 0f; }
                    break;
                }
                default: // a breath, then again where you now stand
                    if (_t >= Rest) Clear();
                    break;
            }
        }
    }
}
