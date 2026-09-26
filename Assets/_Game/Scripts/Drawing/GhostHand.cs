using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The ghost hand: a thin blue line that draws itself with a glowing bead riding its tip, holds a
    /// moment, thins away and rests. The lobby lessons draw with it, and the floating F draws
    /// what it is about to draw with it. Local only, never ink. Off in the options (Ghost lines).
    public class GhostHand
    {
        const string OnKey = "sz_ghost_lines";
        static int _on = -1;

        /// The options switch: the lessons and the previews together.
        public static bool Enabled
        {
            get { if (_on < 0) _on = PlayerPrefs.GetInt(OnKey, 1); return _on != 0; }
            set { _on = value ? 1 : 0; PlayerPrefs.SetInt(OnKey, _on); PlayerPrefs.Save(); }
        }

        public const float Width = 0.012f;        // the line, as thin as ink: the light on its tip is what catches the eye
        public const float DrawSeconds = 1.2f;    // a rune or a ring drawn, whatever its size
        const float Hold = 0.8f, FadeSeconds = 0.35f, Rest = 0.35f;
        static readonly Color Ink = new Color(0.4f, 0.8f, 1f, 0.95f);
        static readonly Color Faint = new Color(0.45f, 0.75f, 1f, 0.3f);
        static readonly Color Glow = new Color(0.55f, 0.85f, 1f, 1f);

        GameObject _ghost;                                       // a scene object: dies with the scene
        readonly List<LineRenderer> _lines = new List<LineRenderer>();
        readonly List<List<Vector3>> _paths = new List<List<Vector3>>();
        readonly List<float> _lengths = new List<float>();
        float _total, _reach, _t, _drawFor;
        int _state;                                              // 0 drawing, 1 holding, 2 fading, 3 resting
        Transform _tip;                                          // the light that rides the line's end

        /// Drawing, holding, fading or resting. False once the rest is over, or after Clear: begin again.
        public bool Alive => _ghost != null;

        /// Lay the lines out and start drawing them in order. `still` lines are drawn whole and
        /// faint from the start (the rune a lesson's ring is drawn around). `budget` is how many
        /// metres the wand could pay for: the hand stops short there, as F would. False = nothing to draw.
        public bool Begin(string name, List<List<Vector3>> paths, List<List<Vector3>> still = null,
            float drawFor = DrawSeconds, float budget = float.MaxValue)
        {
            Clear();
            if (paths != null) foreach (var p in paths) if (p != null && p.Count >= 2) _paths.Add(p);
            _total = 0f;
            foreach (var p in _paths) { float l = Length(p); _lengths.Add(l); _total += l; }
            _reach = Mathf.Min(_total, Mathf.Max(0f, budget));
            if (_reach < 0.05f) { _paths.Clear(); _lengths.Clear(); return false; }

            _ghost = new GameObject(name);
            if (still != null)
                foreach (var p in still) Fill(Line(p, Faint), p, float.MaxValue);
            _lines.Clear(); // the faint lines stay whole; only the lines below animate
            foreach (var p in _paths) Line(p, Ink);
            _drawFor = Mathf.Max(0.1f, drawFor) * (_reach / _total); // the same pace when it stops short
            BuildTip();
            _state = 0; _t = 0f;
            return true;
        }

        public void Clear()
        {
            if (_ghost != null) Object.Destroy(_ghost);
            _ghost = null;
            _tip = null;
            _lines.Clear(); _paths.Clear(); _lengths.Clear();
        }

        /// A hair off its surface, so the line never fights the floor it is drawn on.
        public static void Lift(List<Vector3> path, Vector3 by)
        {
            for (int i = 0; i < path.Count; i++) path[i] += by;
        }

        public static float Length(List<Vector3> path)
        {
            float l = 0f;
            for (int i = 1; i < path.Count; i++) l += Vector3.Distance(path[i - 1], path[i]);
            return l;
        }

        /// The glowing bead at the end of the line: what moves is what the eye finds. No light
        /// on it: a lamp there washed out the wall and the ink around the line.
        void BuildTip()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Tip";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_ghost.transform, false);
            go.transform.localScale = Vector3.one * (Width * 1.8f); // barely wider than the line: its tip, not a ball on it
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = MatterFX.Get(Glow, MoteShade.Additive);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _tip = go.transform;
            _tip.gameObject.SetActive(false);
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

        /// One step of the hand: it draws `_reach` metres over `_drawFor` seconds, line by line.
        public void Animate(float dt)
        {
            if (_ghost == null) return;
            _t += dt;
            switch (_state)
            {
                case 0: // the hand draws, line by line
                {
                    float drawn = _reach * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / _drawFor));
                    float acc = 0f;
                    for (int i = 0; i < _paths.Count; i++)
                    {
                        float have = Mathf.Clamp(drawn - acc, 0f, _lengths[i]);
                        if (have <= 0f) _lines[i].positionCount = 0;
                        else
                        {
                            int n = Fill(_lines[i], _paths[i], have);
                            // the light rides the end of the line being drawn
                            if (_tip != null && n > 0 && have < _lengths[i])
                            {
                                if (!_tip.gameObject.activeSelf) _tip.gameObject.SetActive(true);
                                _tip.position = _lines[i].GetPosition(n - 1); // centred on the line's end
                            }
                        }
                        acc += _lengths[i];
                    }
                    if (_t >= _drawFor)
                    {
                        _state = 1; _t = 0f;
                        if (_tip != null) _tip.gameObject.SetActive(false);
                    }
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
                default: // a breath, then again
                    if (_t >= Rest) Clear();
                    break;
            }
        }
    }
}
