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
                    // A PARTIAL REPAINT IS A FAILED REPAINT. The guard used to
                    // fire only at painted == 0: with 3 of 12 samples on the
                    // wall it stayed silent, the first census adopted those 3 as
                    // "what's on disk", and the next stroke he drew saved 4 —
                    // PERMANENTLY DELETING the 9 that never made it back. Any
                    // shortfall now disables saving for the session, exactly as
                    // a total failure does.
                    if (savedCount > 0 && painted < savedCount)
                    {
                        _loadFailed = true;
                        Debug.LogError($"[RuneWall] {RuneLibrary.ShortName(Rune)}: only {painted} of {savedCount} saved drawing(s) repainted — saving is DISABLED for this wall this session so the missing recordings can't be wiped. Tell Claude what this log says.");
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
                // SHORT STROKES ARE THE POINT (Marko, Jul 31). MinStrokeNodes
                // was lowered to 2 the same day precisely so an arrowhead's
                // barb or a LIGHT ray can exist as a line at all — and this
                // census was still hard-coded to 3, so those limbs rendered on
                // the wall, stayed visible, and were invisible to the save. The
                // signature never changed, the stored sample was a barbless
                // shaft, and next session the wall repainted without them: the
                // wall silently editing his drawing, 20 lines above the comment
                // promising it never does.
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
            // THE WALL NEVER TOUCHES HIS DRAWINGS (Marko, Jul 31: "I never
            // drew any of these... you flipped heat and chill on the Y axis,
            // flipped liquid and solid, flipped dark upside down. When I enter
            // into the game again I don't want to see any changes from what I
            // drew on the wall.")
            //
            // The flip he saw was NOT this method — it was the repaint frame,
            // fixed in LoadSaved below; read that comment before touching
            // anything here. But this method is the other half of the round
            // trip, so the rule stands: the census reports the wall EXACTLY as
            // it is. No rotating, no mirroring, no re-ordering, no
            // "normalising", no dropping short strokes.
            //
            // A rotation-snapper used to live here (AlignToFirst: rotate every
            // sample to match the first drawing's orientation). It was already
            // unwired; it is now DELETED, helpers and all, because dead code
            // that flips his ink is a loaded gun. It was pointless anyway — he
            // designed the alphabet so no glyph resembles any other under any
            // rotation or reflection, which is precisely why matching can be
            // rotation-free. Storing what he drew, untouched, costs nothing and
            // is the only honest thing to do.
            return samples;
        }

        // ------------------------------------------------------ loading ----
        /// Repaint the saved pool as REAL ink (same pipeline as the pen), on
        /// an adaptive grid — however many drawings there are, they all fit.
        int LoadSaved()
        {
            var samples = RuneLibrary.AllSamples(Rune);
            if (samples.Count == 0) return 0;
            // No collider = nothing for Place() to raycast onto, so NOTHING can
            // repaint. Reporting samples.Count here claimed a full repaint of a
            // wall that is empty, which is exactly the lie the fail-safe exists
            // to catch.
            var col = GetComponentInChildren<Collider>();
            if (col == null) return 0;

            // ---- REPAINT IN THE FRAME THE INK WAS SAVED IN ----
            // Marko, Jul 31: "I never drew any of these... you flipped heat and
            // chill on the Y axis, flipped liquid and solid, flipped dark upside
            // down. When I enter into the game again I don't want to see any
            // changes from what I drew on the wall."
            //
            // He was right, and it was never AlignToFirst. SAVING
            // (RuneGlyph.RawStrokesOf) builds its 2D basis with
            //     up = Cross(right, normal)          =>  Cross(right, up) == -normal
            // where `normal` is the PEN'S hit.normal, so it points out of the
            // face toward whoever drew it: a stored (x,y) is literally the
            // drawer's screen coordinates. Repainting used to grab the raw
            // transform basis instead — and for ANY Unity transform
            //     Cross(transform.right, transform.up) == +transform.forward
            // the OPPOSITE handedness. Two frames differing by a reflection, so
            // every repaint came back mirrored. It got worse from there:
            // PaintSample stamps its basis onto the repainted strokes, the next
            // census re-read them through that flipped frame and ReplaceSamples
            // wrote the flipped version to disk — the pool alternated
            // mirrored / upside-down with every cycle and was NEVER what he drew.
            // Those corrupted samples then WERE the templates the matcher scores
            // against, widening every pool with shapes that, by his own law, are
            // not that rune.
            //
            // Fix: derive the repaint frame from the SAME law, off the same face.
            // PlaneBasis also ends with up = Cross(right, normal), and returns
            // the frame of someone standing in front of the face — `right` is the
            // VIEWER's right, not the wall's (they are opposite; a viewer of the
            // +forward face has screen-right -transform.right). The round trip is
            // then the identity: paint lays x along `right`, the census
            // re-derives exactly `right` from BasisRight and exactly `up` from
            // Cross(right, hit.normal). Unlimited save->load->save cycles are a
            // no-op. WYSIWYG, permanently.
            //
            // DO NOT ever "fix" a future flip by negating the samples on load.
            // That hides a frame mismatch and leaves the census still writing the
            // wrong thing back to disk — which is how this bug survived so long.
            Vector3 normal = transform.forward;                         // the face Place() raycasts onto
            ZombieScribe.PlaneBasis(normal, out var right, out var up); // SAME law as RuneGlyph.RawStrokesOf
            // THE WALL'S REAL SIZE, never a floor. These used to be
            // Max(…, 2f) and Max(…, 1.5f): on any wall narrower or shorter than
            // that, the grid was solved for a surface bigger than the one that
            // exists, so the outer cells landed off the face, their raycasts
            // missed and those drawings simply did not repaint.
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
            // NEVER MAGNIFY (Marko: "I don't want to see any changes from what
            // I drew on the wall"). The stored coordinates are already in
            // metres, exactly as his hand laid them down, so scale 1 repaints
            // his drawing at its true size. This used to scale EVERY sample up
            // to fill its grid cell — on the studio slab with two samples that
            // was a 3.4x magnification of a 40cm rune — and the blow-up was not
            // cosmetic: the census regroups strokes by RuneTouchDistance, so a
            // 2cm pen-lift gap inside one drawing came back 7cm wide and the
            // drawing was split into TWO samples, which were then written back
            // as two. Repaint and census stopped being inverses of each other
            // and the pool drifted every cycle. Shrinking to fit is still
            // allowed — a drawing that no longer fits its cell has to go
            // somewhere — but the common case is now the identity.
            float scale = Mathf.Min(1f, size / Mathf.Max(span.x, span.y));
            Vector2 mid = (min + max) * 0.5f;

            bool any = false;
            foreach (var stroke in sample)
            {
                if (stroke.Count < 2) continue;
                var s = new Stroke
                {
                    // THIS is what closes the loop. The repainted ink carries the
                    // very frame it was painted in, so when the census re-reads it
                    // (RuneGlyph.RawStrokesOf: right = ProjectOnPlane(BasisRight,
                    // hit.normal), up = Cross(right, hit.normal)) it recovers the
                    // stored coordinates unchanged. Hand a mismatched basis in
                    // here and the wall starts rewriting his drawings again.
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
                    // FILL THE GAP TO NODE SPACING, AND NO FURTHER. Repainted
                    // ink has to be dense enough that the strokes read as
                    // connected, but this used to insert ONE midpoint per
                    // segment unconditionally — so an N-point sample repainted
                    // as 2N-1 nodes, the census wrote 2N-1 back, and every
                    // editing session DOUBLED the point count of every drawing
                    // already on the wall (HEAT's older sample is at 184 points
                    // against 84 for the newer one). Subdividing to the pen's
                    // own node spacing instead is a fixed point: after one
                    // repaint the gaps are already at spacing, so the next
                    // repaint adds nothing. The extra points are exactly on the
                    // segment, so the shape is untouched either way.
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
                // a picture on a wall — don't recognise it, don't network it,
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
                _label.text = $"{RuneLibrary.Icon(Rune)}\n{count} drawing(s) — auto-saves";
        }
    }
}
