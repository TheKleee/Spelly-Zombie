using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ONE WALL = ONE RUNE (Marko's Rune Studio design): the ink on the wall
    /// IS the rune's saved sample pool — nothing hidden, no buttons.
    ///
    ///   · draw samples of the rune anywhere on the wall — ALL of them count
    ///   · STOPPING THE SCENE saves automatically: whatever is on the wall at
    ///     that moment is that rune's memory (Marko: "I don't wanna press
    ///     anything")
    ///   · erased a drawing? stop the scene — it's deleted from memory too
    ///   · scene load repaints everything saved, laid out in a grid — what
    ///     you see is exactly what the recognizer knows
    public class RuneWall : MonoBehaviour
    {
        public RuneType Rune;

        const float SnapshotPeriod = 0.6f; // live wall census cadence

        TextMesh _label;
        float _timer;
        int _shownCount = -1;
        bool _loadFailed;  // repaint blew up — this session must NOT save
        bool _sawInk;      // the census saw ink at least once this session
        bool _firstCensus = true;
        int _prevSig = int.MinValue;      // last census signature (settle detector)
        int _lastSavedSig = int.MinValue; // what's already on disk
        int _slowBeat;                    // idle-wall heartbeat divider

        void Awake() => _slowBeat = GetInstanceID() & 7; // stagger idle walls across beats

        void Start()
        {
            _label = GetComponentInChildren<TextMesh>();

            // WIPE-PROOFING (after the Jul 20 data loss): loading is guarded,
            // and a session that never managed to show the saved ink is never
            // allowed to overwrite it.
            int savedCount = RuneLibrary.AllSamples(Rune).Count;
            try
            {
                if (DrawingWorld.Instance != null)
                {
                    int painted = LoadSaved();
                    _shownCount = painted;
                    if (savedCount > 0 && painted == 0)
                    {
                        _loadFailed = true;
                        Debug.LogError($"[RuneWall] {RuneLibrary.ShortName(Rune)}: {savedCount} saved drawing(s) but NONE repainted — saving is DISABLED for this wall this session so the recordings can't be wiped. Tell Claude what this log says.");
                    }
                    else if (savedCount > 0)
                    {
                        Debug.Log($"[RuneWall] {RuneLibrary.ShortName(Rune)}: repainted {painted}/{savedCount} saved drawing(s)");
                    }
                }
                else
                {
                    _loadFailed = savedCount > 0; // no world to paint into — protect
                }
            }
            catch (System.Exception e)
            {
                _loadFailed = true;
                Debug.LogError($"[RuneWall] {RuneLibrary.ShortName(Rune)}: repaint CRASHED ({e.Message}) — saving disabled this session to protect the recordings.");
            }
            RefreshLabel(Mathf.Max(0, _shownCount));
        }

        /// INSTANT SAVE (Marko: "if a wall can tell when I drew something on
        /// it, it can also save immediately — and override the old ones, so
        /// deleting saves what's on the wall now"): the census runs on a
        /// beat; the moment the wall's ink SETTLES (two identical censuses —
        /// so mid-stroke churn doesn't spam saves) and differs from what's on
        /// disk, the wall IS the rune. No buttons, no scene-stop, WYSIWYG.
        /// The recognizer rebuilds from the new pool on its next call, so
        /// test drawings are judged by the new samples immediately.
        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = SnapshotPeriod;

            // ONLY THE WALL BEING DRAWN ON pays the census every beat
            // (Marko: "it's starting to lag a lot") — the last-drawn stroke
            // names the active wall. Idle walls check on a slow, staggered
            // heartbeat so erases over there still get noticed eventually.
            var last = DrawingWorld.Instance != null ? DrawingWorld.Instance.LastInk : null;
            bool mine = last != null && last.Surface != null
                && (last.Surface == transform || last.Surface.IsChildOf(transform));
            _slowBeat++;
            if (!mine && !_firstCensus && (_slowBeat & 7) != 0) return;

            var snap = Snapshot();
            if (snap == null) return;

            if (snap.Count > 0) _sawInk = true;
            if (snap.Count != _shownCount)
            {
                _shownCount = snap.Count;
                RefreshLabel(snap.Count);
            }

            int sig = CensusSignature(snap);
            if (_firstCensus)
            {
                // the repainted saved state counts as already-on-disk
                _firstCensus = false;
                _prevSig = sig;
                _lastSavedSig = sig;
                return;
            }

            bool settled = sig == _prevSig;
            _prevSig = sig;
            if (!settled || sig == _lastSavedSig || _loadFailed) return;

            // FAIL-SAFE stays: an empty census on a wall that has recordings
            // but never showed ink means "loading broke", not "he erased it"
            if (snap.Count == 0 && !_sawInk && RuneLibrary.AllSamples(Rune).Count > 0)
                return;

            RuneLibrary.ReplaceSamples(Rune, snap);
            _lastSavedSig = sig;
        }

        /// Cheap census identity: drawing count + total ink points + a coarse
        /// positional checksum — any add, erase, or redraw changes it.
        static int CensusSignature(List<List<List<Vector2>>> samples)
        {
            unchecked
            {
                int sig = samples.Count * 486187739;
                foreach (var sample in samples)
                    foreach (var stroke in sample)
                    {
                        sig += stroke.Count * 1000003;
                        if (stroke.Count > 0)
                        {
                            var p = stroke[0];
                            sig ^= (int)(p.x * 97f) * 31 + (int)(p.y * 97f);
                        }
                    }
                return sig;
            }
        }

        // ------------------------------------------------------ census ----
        /// Everything currently drawn on this wall, one sample per
        /// spatially-separate drawing. Null if the world is gone (teardown).
        List<List<List<Vector2>>> Snapshot()
        {
            var world = DrawingWorld.Instance;
            if (world == null) return null;

            var mine = new List<Stroke>();
            foreach (var s in world.Strokes)
            {
                if (s == null || !s.Alive || s.State != StrokeState.Open) continue;
                if (s.Nodes.Count < 3 || !s.ChainIntact() || s.Hidden()) continue;
                if (s.SealResidue) continue;
                if (s.Surface == null) continue;
                if (s.Surface != transform && !s.Surface.IsChildOf(transform)) continue;
                mine.Add(s);
            }

            var samples = new List<List<List<Vector2>>>();
            foreach (var glyph in RuneGlyph.Cluster(mine, DrawingConfig.RuneTouchDistance, 0f))
            {
                var raw = glyph.BuildRawStrokes();
                if (raw != null && raw.Count > 0) samples.Add(raw);
            }
            AlignToFirst(samples);
            return samples;
        }

        // ---------------------------------------------------- alignment ----
        // MARKO'S RULE: the FIRST drawing on the wall determines the up
        // position — every other sample is rotated (never mirrored — mirrors
        // are other runes) to best match it before being stored. Drawn
        // upside-down? It's saved upright. Code never guesses what a rune
        // "looks like"; it only matches against HIS reference.
        static void AlignToFirst(List<List<List<Vector2>>> samples)
        {
            if (samples.Count < 2) return;
            var reference = CloudOf(samples[0], out _);
            if (reference == null) return;

            for (int i = 1; i < samples.Count; i++)
            {
                var cloud = CloudOf(samples[i], out Vector2 centroid);
                if (cloud == null) continue;

                // coarse sweep of the full circle, then a fine pass around
                // the winner — the sample keeps its shape, only its rotation
                // snaps to the first drawing's orientation
                float bestAng = 0f, bestD = float.MaxValue;
                for (int a = 0; a < 24; a++)
                {
                    float ang = a * (Mathf.PI * 2f / 24f);
                    float d = CloudDistance(cloud, reference, ang);
                    if (d < bestD) { bestD = d; bestAng = ang; }
                }
                for (float off = -0.2f; off <= 0.2f; off += 0.05f) // ±11° in 3° steps
                {
                    float ang = bestAng + off;
                    float d = CloudDistance(cloud, reference, ang);
                    if (d < bestD) { bestD = d; bestAng = ang; }
                }

                // normalize into (-π, π]; skip near-zero corrections
                while (bestAng > Mathf.PI) bestAng -= Mathf.PI * 2f;
                if (Mathf.Abs(bestAng) < 0.02f) continue;
                RotateSample(samples[i], centroid, bestAng);
            }
        }

        /// Normalized point cloud of a sample (centroid at origin, max
        /// dimension = 1), thinned to a manageable count. Null if degenerate.
        static List<Vector2> CloudOf(List<List<Vector2>> sample, out Vector2 centroid)
        {
            centroid = Vector2.zero;
            int total = 0;
            foreach (var s in sample) total += s.Count;
            if (total < 4) return null;
            foreach (var s in sample)
                foreach (var p in s) centroid += p;
            centroid /= total;

            float maxDim = 0f;
            foreach (var s in sample)
                foreach (var p in s)
                    maxDim = Mathf.Max(maxDim, (p - centroid).magnitude);
            if (maxDim < 1e-4f) return null;

            int stride = Mathf.Max(1, total / 64);
            var cloud = new List<Vector2>(Mathf.Min(total, 80));
            int k = 0;
            foreach (var s in sample)
                foreach (var p in s)
                {
                    if (k++ % stride != 0) continue;
                    cloud.Add((p - centroid) / maxDim);
                }
            return cloud.Count >= 4 ? cloud : null;
        }

        /// Mean nearest-point distance from the rotated cloud to the
        /// reference cloud — cheap, symmetric enough for orientation search.
        static float CloudDistance(List<Vector2> cloud, List<Vector2> reference, float angle)
        {
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            float sum = 0f;
            foreach (var p0 in cloud)
            {
                var p = new Vector2(p0.x * ca - p0.y * sa, p0.x * sa + p0.y * ca);
                float best = float.MaxValue;
                foreach (var r in reference)
                    best = Mathf.Min(best, (p - r).sqrMagnitude);
                sum += best;
            }
            return sum / cloud.Count;
        }

        static void RotateSample(List<List<Vector2>> sample, Vector2 centroid, float angle)
        {
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            foreach (var stroke in sample)
                for (int i = 0; i < stroke.Count; i++)
                {
                    Vector2 d = stroke[i] - centroid;
                    stroke[i] = centroid + new Vector2(d.x * ca - d.y * sa, d.x * sa + d.y * ca);
                }
        }

        // ------------------------------------------------------ loading ----
        /// Repaint the saved pool as REAL ink (same pipeline as the pen), on
        /// an adaptive grid — however many drawings there are, they all fit.
        int LoadSaved()
        {
            var samples = RuneLibrary.AllSamples(Rune);
            var col = GetComponentInChildren<Collider>();
            if (col == null || samples.Count == 0) return samples.Count;

            Vector3 normal = transform.forward;
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            Vector3 size = col.bounds.size;
            float wallW = Mathf.Max(Vector3.Project(size, right).magnitude, 2f);
            float wallH = Mathf.Max(Vector3.Project(size, up).magnitude, 1.5f);

            int count = samples.Count;
            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count * (wallW / wallH))));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));
            float cellW = wallW / cols, cellH = wallH / rows;
            float glyphSize = Mathf.Min(cellW, cellH) * 0.62f;
            Vector3 faceCenter = col.bounds.center + normal * (Vector3.Project(size, normal).magnitude * 0.5f);

            int painted = 0;
            for (int i = 0; i < count; i++)
            {
                int cx = i % cols, cy = i / cols;
                Vector3 cell = faceCenter
                    + right * ((cx + 0.5f) / cols - 0.5f) * wallW
                    + up * (0.5f - (cy + 0.5f) / rows) * wallH;
                if (PaintSample(samples[i], cell, glyphSize, right, up, normal)) painted++;
            }
            return painted;
        }

        /// True when at least one stroke of the sample actually landed on the
        /// wall — the fail-safe distinguishes "painted" from "stored".
        bool PaintSample(List<List<Vector2>> sample, Vector3 at, float size,
            Vector3 right, Vector3 up, Vector3 normal)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (var stroke in sample)
                foreach (var p in stroke)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            Vector2 span = Vector2.Max(max - min, Vector2.one * 0.001f);
            float scale = size / Mathf.Max(span.x, span.y);
            Vector2 mid = (min + max) * 0.5f;

            bool any = false;
            foreach (var stroke in sample)
            {
                if (stroke.Count < 2) continue;
                var s = new Stroke
                {
                    BasisRight = right,
                    BasisUp = up,
                    Surface = transform,
                    OwnerId = 0
                };
                DrawingWorld.Instance.Register(s);
                Vector2 prev = default;
                bool has = false;
                foreach (var p in stroke)
                {
                    if (has)
                    {
                        Vector2 m = (prev + p) * 0.5f;
                        Place(s, m, mid, scale, at, right, up, normal);
                    }
                    Place(s, p, mid, scale, at, right, up, normal);
                    prev = p;
                    has = true;
                }
                if (s.Nodes.Count >= 2) any = true;
                DrawingWorld.Instance.CompleteStroke(s);
            }
            return any;
        }

        void Place(Stroke s, Vector2 p, Vector2 mid, float scale, Vector3 at,
            Vector3 right, Vector3 up, Vector3 normal)
        {
            Vector2 local = (p - mid) * scale;
            Vector3 world = at + right * local.x + up * local.y;
            if (Physics.Raycast(world + normal * 0.5f, -normal, out var hit, 1.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && (hit.transform == transform || hit.transform.IsChildOf(transform)))
                s.AddNode(DrawNode.Create(s, s.Nodes.Count, hit.point, hit.normal, hit.transform));
        }

        void RefreshLabel(int count)
        {
            if (_label != null)
                _label.text = $"{RuneLibrary.ShortName(Rune)}\n{count} drawing(s) — auto-saves";
        }
    }
}
