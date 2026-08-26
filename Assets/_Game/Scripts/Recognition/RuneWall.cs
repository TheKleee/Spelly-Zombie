using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// One wall = one rune: the ink on the wall IS the rune's saved sample
    /// pool. Drawings save automatically once the ink settles, erasing
    /// removes them, and scene load repaints everything saved in a grid.
    public class RuneWall : MonoBehaviour
    {
        public RuneType Rune;

        const float SnapshotPeriod = 0.6f; // live wall census cadence

        TMPro.TextMeshPro _label;
        float _timer;
        int _shownCount = -1;
        bool _loadFailed;  // repaint blew up - this session must NOT save
        bool _sawInk;      // the census saw ink at least once this session
        bool _firstCensus = true;
        float _handErasedAt = -999f;
        int _prevSig = int.MinValue;      // last census signature (settle detector)
        int _lastSavedSig = int.MinValue; // what's already on disk
        int _slowBeat;                    // idle-wall heartbeat divider

        /// The player's own eraser worked this wall just now - the only key
        /// that can clear its saved pool.
        public void NoteHandErase() => _handErasedAt = Time.time;

        void Awake() => _slowBeat = GetInstanceID() & 7; // stagger idle walls across beats

        void Start()
        {
            _label = GetComponentInChildren<TMPro.TextMeshPro>();
            if (_label != null)
            {
                // sit just over the slab's top edge and read from the walkway
                // side - built scenes had the label huge, high and mirrored
                _label.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                _label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                _label.fontSize = 2.6f;
                _label.rectTransform.sizeDelta = new Vector2(4.2f, 0.8f);
            }

            // wipe-proofing: a session that never managed to show the saved
            // ink is never allowed to overwrite it
            int savedCount = RuneLibrary.AllSamples(Rune).Count;
            try
            {
                if (DrawingWorld.Instance != null)
                {
                    int painted = LoadSaved();
                    _shownCount = painted;
                    // a partial repaint is a failed repaint: any shortfall
                    // disables saving for the session so the recordings that
                    // never made it back can't be wiped
                    if (savedCount > 0 && painted < savedCount)
                    {
                        _loadFailed = true;
                        Debug.LogError($"[RuneWall] {RuneLibrary.ShortName(Rune)}: only {painted} of {savedCount} saved drawing(s) repainted. Saving is DISABLED for this wall this session so the missing recordings can't be wiped.");
                    }
                    else if (savedCount > 0)
                    {
                        Debug.Log($"[RuneWall] {RuneLibrary.ShortName(Rune)}: repainted {painted}/{savedCount} saved drawing(s)");
                    }
                }
                else
                {
                    _loadFailed = savedCount > 0; // no world to paint into - protect
                }
            }
            catch (System.Exception e)
            {
                _loadFailed = true;
                Debug.LogError($"[RuneWall] {RuneLibrary.ShortName(Rune)}: repaint CRASHED ({e.Message}). Saving disabled this session to protect the recordings.");
            }
            RefreshLabel(Mathf.Max(0, _shownCount));
        }

        /// Census on a beat: the moment the wall's ink settles (two identical
        /// censuses, so mid-stroke churn doesn't spam saves) and differs from
        /// disk, the wall saves. The recognizer rebuilds from the new pool on
        /// its next call.
        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = SnapshotPeriod;

            // only the wall being drawn on pays the census every beat (the
            // last-drawn stroke names the active wall); idle walls check on a
            // slow, staggered heartbeat so erases still get noticed
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

            // fail-safe: an empty census on a wall that has recordings but
            // never showed ink means "loading broke", not "erased it"
            if (snap.Count == 0 && !_sawInk && RuneLibrary.AllSamples(Rune).Count > 0)
                return;

            RuneLibrary.ReplaceSamples(Rune, snap, Time.time - _handErasedAt < 3f);
            _lastSavedSig = sig;
        }

        /// Cheap census identity: drawing count + total ink points + a coarse
        /// positional checksum - any add, erase, or redraw changes it.
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
                // uses MinStrokeNodes (2), never a higher hard-coded floor, so
                // an arrowhead's barb or a LIGHT ray survives the save
                if (s.Nodes.Count < DrawingConfig.MinStrokeNodes
                    || !s.ChainIntact() || s.Hidden()) continue;
                if (s.SealResidue) continue;
                if (s.Surface == null) continue;
                if (s.Surface != transform && !s.Surface.IsChildOf(transform)) continue;
                mine.Add(s);
            }

            var samples = new List<List<List<Vector2>>>();
            foreach (var glyph in RuneGlyph.Cluster(mine, DrawingConfig.RuneTouchDistance))
            {
                var raw = glyph.BuildRawStrokes();
                if (raw != null && raw.Count > 0) samples.Add(raw);
            }
            // the census reports the wall EXACTLY as it is: no rotating, no
            // mirroring, no re-ordering, no normalising, no dropping short
            // strokes
            return samples;
        }

        // ------------------------------------------------------ loading ----
        /// Repaint the saved pool as REAL ink (same pipeline as the pen), on
        /// an adaptive grid - however many drawings there are, they all fit.
        int LoadSaved()
        {
            var samples = RuneLibrary.AllSamples(Rune);
            if (samples.Count == 0) return 0;
            // no collider = nothing for Place() to raycast onto, so nothing
            // can repaint; returning 0 lets the fail-safe catch it
            var col = GetComponentInChildren<Collider>();
            if (col == null) return 0;

            // ---- repaint in the frame the ink was saved in ----
            // Saving (RuneGlyph.RawStrokesOf) builds up = Cross(right, normal),
            // so Cross(right, up) == -normal - the OPPOSITE handedness to a raw
            // Unity transform basis (Cross(right, up) == +forward). Any other
            // frame law here is a reflection and mirrors every glyph.
            // PlaneBasis follows the same law, so save->load->save is the
            // identity. Never "fix" a flip by negating samples on load - that
            // hides a frame mismatch while the census keeps writing the wrong
            // thing to disk.
            Vector3 normal = transform.forward;                         // the face Place() raycasts onto
            ZombieScribe.PlaneBasis(normal, out var right, out var up); // SAME law as RuneGlyph.RawStrokesOf
            // the wall's real size, no minimum floor: a floored size solves
            // the grid for a bigger surface and off-face cells fail to repaint
            Vector3 size = col.bounds.size;
            float wallW = Mathf.Max(Vector3.Project(size, right).magnitude, 0.01f);
            float wallH = Mathf.Max(Vector3.Project(size, up).magnitude, 0.01f);

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
            // never magnify: stored coordinates are metres at true drawn size,
            // so scale 1 repaints exactly. Magnifying widens pen-lift gaps
            // past RuneTouchDistance and the census splits the drawing.
            // Shrink-to-fit only.
            float scale = Mathf.Min(1f, size / Mathf.Max(span.x, span.y));
            Vector2 mid = (min + max) * 0.5f;

            bool any = false;
            foreach (var stroke in sample)
            {
                if (stroke.Count < 2) continue;
                var s = new Stroke
                {
                    // the repainted ink carries the frame it was painted in, so
                    // the census recovers the stored coordinates unchanged; a
                    // mismatched basis here rewrites the drawings
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
                    // fill gaps to node spacing and no further: subdividing to
                    // the pen's own spacing is a fixed point, so repaint
                    // cycles never grow the drawing
                    if (has)
                    {
                        float gap = (p - prev).magnitude * scale;
                        int steps = Mathf.Clamp(
                            Mathf.CeilToInt(gap / Mathf.Max(DrawingConfig.NodeSpacing, 1e-4f)),
                            1, 64);
                        for (int i = 1; i < steps; i++)
                            Place(s, Vector2.Lerp(prev, p, i / (float)steps),
                                  mid, scale, at, right, up, normal);
                    }
                    Place(s, p, mid, scale, at, right, up, normal);
                    prev = p;
                    has = true;
                }
                if (s.Nodes.Count >= 2) any = true;
                // a picture on a wall - don't recognise it, don't network it,
                // don't claim ink with it (this was ~150ms PER SAMPLE at load)
                DrawingWorld.Instance.CompleteStroke(s, silent: true);
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
                _label.text = $"{RuneLibrary.IconInline(Rune)}  {count} saved";
        }
    }
}
