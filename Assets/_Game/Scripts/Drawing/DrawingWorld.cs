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
        public Stroke LastCompleted { get; private set; }

        public Material LineMaterial { get; private set; }

        /// Persistent ink whose spell resolved: locked until the loop physically
        /// opens (any junction beyond ReArmDistance), then it can fire again.
        class SpentGroup
        {
            public List<Stroke> Strokes;
            public List<(DrawNode a, DrawNode b)> Pairs;
        }

        readonly List<SpentGroup> _spentGroups = new List<SpentGroup>();
        readonly List<string> _events = new List<string>();
        float _detectTimer;
        readonly List<Stroke> _eligibleCache = new List<Stroke>();

        void Awake()
        {
            Instance = this;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            LineMaterial = new Material(shader);
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

        /// Pen lifted: finish the stroke, classify it as a rune.
        public void CompleteStroke(Stroke s)
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

            var (type, score) = RuneLibrary.Classify(s.RawShape);
            s.RuneScore = score;
            s.Rune = score >= DrawingConfig.MinRuneScore ? type : RuneType.None;
            LastCompleted = s;

            LogEvent(s.Rune != RuneType.None
                ? $"Rune drawn: {s.Rune} (accuracy {score:0.00})"
                : $"Stroke unrecognized (best {type} at {score:0.00}) — fizzle");
        }

        /// The stroke being drawn came back to its own start: close it as a seal immediately.
        public void CloseSingleStroke(Stroke s)
        {
            s.State = StrokeState.Open;
            s.CachePersistence();
            s.ComputeRawShape(); // kept for debugging/inspection, not classified
            LastCompleted = s;
            CreateSeal(new List<SealDetector.LoopEntry> { new SealDetector.LoopEntry(s, true) }, "closed while drawing");
        }

        void Update()
        {
            // ink follows moving surfaces (static ink skips its rebuild internally)
            foreach (var s in Strokes)
                if (s.Alive) s.UpdateLine();

            // active seals: integrity + duration
            for (int i = ActiveSeals.Count - 1; i >= 0; i--)
                if (!ActiveSeals[i].Tick(Time.deltaTime))
                    ActiveSeals.RemoveAt(i);

            // periodic scans — loop detection, spent re-arming, ink budget
            _detectTimer -= Time.deltaTime;
            if (_detectTimer <= 0f)
            {
                _detectTimer = DrawingConfig.DetectInterval;
                Strokes.RemoveAll(s => !s.Alive);
                TickSpentGroups();
                EnforceInkBudget();
                Detect();
            }
        }

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
                _eligibleCache.Add(s);
            }
            if (_eligibleCache.Count == 0) return;

            var loop = SealDetector.FindLoop(_eligibleCache);
            if (loop != null)
                CreateSeal(loop, loop.Count == 1 ? "endpoints met" : $"{loop.Count} strokes linked");
        }

        void CreateSeal(List<SealDetector.LoopEntry> loop, string how)
        {
            var seal = new Seal(loop);
            seal.CapturePayload(Strokes);
            ActiveSeals.Add(seal);
            LogEvent($"SEAL #{seal.Id} ACTIVATED ({how}): {seal.Describe()}");

            // >>> spell resolution hook — surface material + runes + zone go here next <<<
        }

        public void OnSealEnded(Seal seal, string message)
        {
            LogEvent(message);
        }

        // ---- spent ink (characters & weapons) ----

        public void RegisterSpentGroup(List<Stroke> strokes, List<(DrawNode a, DrawNode b)> pairs)
        {
            // no surviving junctions means the loop can never close again
            // (its other half burned) — hand the ink straight back
            if (pairs.Count == 0)
            {
                ReleaseSpent(strokes);
                return;
            }
            _spentGroups.Add(new SpentGroup { Strokes = strokes, Pairs = pairs });
        }

        void TickSpentGroups()
        {
            for (int i = _spentGroups.Count - 1; i >= 0; i--)
            {
                var g = _spentGroups[i];
                bool open = false;

                foreach (var (a, b) in g.Pairs)
                {
                    if (a == null || b == null ||
                        Vector3.Distance(a.transform.position, b.transform.position) > DrawingConfig.ReArmDistance)
                    {
                        open = true;
                        break;
                    }
                }

                // damaged ink can't seal anyway — don't hold it hostage
                if (!open)
                {
                    foreach (var s in g.Strokes)
                        if (!s.Alive || !s.ChainIntact()) { open = true; break; }
                }

                if (open)
                {
                    ReleaseSpent(g.Strokes);
                    _spentGroups.RemoveAt(i);
                    LogEvent("Spent seal re-armed — the loop opened");
                }
            }
        }

        static void ReleaseSpent(List<Stroke> strokes)
        {
            foreach (var s in strokes)
            {
                if (!s.Alive || s.State != StrokeState.Spent) continue;
                s.State = StrokeState.Open;
                s.SetColor(Stroke.InkColor);
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

        /// Debug erase (and later: water, spinner zombies).
        public void EraseAt(Vector3 point, float radius)
        {
            foreach (var s in Strokes)
            {
                if (!s.Alive) continue;
                foreach (var n in s.Nodes)
                {
                    if (n == null) continue;
                    if (Vector3.Distance(n.transform.position, point) <= radius)
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
            GUI.color = Color.white;
            var box = new Rect(10, 10, 560, 210);
            GUILayout.BeginArea(box);

            int spentCount = 0;
            foreach (var s in Strokes)
                if (s.State == StrokeState.Spent) spentCount++;

            GUILayout.Label($"<b>Strokes:</b> {Strokes.Count} (spent: {spentCount})   <b>Active seals:</b> {ActiveSeals.Count}", Rich());
            if (LastCompleted != null && LastCompleted.Alive)
            {
                string runeText = LastCompleted.Rune != RuneType.None
                    ? $"{LastCompleted.Rune} ({LastCompleted.RuneScore:0.00})"
                    : $"unrecognized ({LastCompleted.RuneScore:0.00})";
                GUILayout.Label($"<b>Last stroke:</b> {runeText}", Rich());
            }
            foreach (var seal in ActiveSeals)
                GUILayout.Label($"  Seal #{seal.Id}: {(seal.IsCircle ? "circle" : seal.Edges + " edges")} — {seal.Remaining:0.0}s / {seal.Duration:0.0}s", Rich());

            GUILayout.Space(6);
            foreach (var e in _events)
                GUILayout.Label(e, Rich());

            GUILayout.EndArea();

            // controls + template recording reference, bottom left
            var help = new Rect(10, Screen.height - 132, 980, 126);
            GUI.Label(help,
                "LMB draw ink  ·  hold LeftAlt = precision cursor  ·  hold R = erase  ·  T / 1-9 = poses  ·  B = Pose Studio (make your own)\n" +
                "Close a loop = seal activates (0.1s per edge, circle = 36s). Open strokes inside the loop = runes. Opening the ring cancels the spell.\n" +
                "Ink on characters/weapons is permanent: after the spell it goes SPENT (dim gold) and re-arms when the pose opens the loop. Environment ink is consumed.\n" +
                "Record templates — draw a glyph, then press:  F1 HeatUp  F2 HeatDown  F3 StateSolid  F4 StateLiquid  F5 LumUp  F6 LumDown\n" +
                "F7 StickyUp  F8 StickyDown  F9 DirAway  F10 DirToward  F11 DensityUp  F12 DensityDown");
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
