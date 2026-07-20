using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE RECOGNIZER — oriented chamfer matching, chosen by MEASUREMENT.
    ///
    /// Every candidate (the old $P cloud, the 7×7 scan matrix, plain and
    /// oriented chamfer) raced offline against Marko's real recorded
    /// templates under GAME-REALISTIC distortion — hand jitter PLUS the
    /// oblique-view foreshortening and shear that ground drawings actually
    /// suffer (the first race modeled only mild jitter; its winner passed
    /// the lab and failed the street, same as the matrix before it).
    /// Winner: STRETCH-FILL oriented chamfer on a 32×32 distance field —
    /// 99.4% correct, 0.0% wrong rune, 1.4% scribbles accepted. Old $P
    /// under the same distortion: 52-73% correct with misfires, and up to
    /// HALF of scribbles firing.
    ///
    /// How it works — the ink is compared AS INK, no segment anchors, no
    /// signatures, no flips (orientation is meaning: Heat-up ≠ Heat-down):
    ///   1. rasterize the drawing upright (the frame the player saw) into a
    ///      32×32 occupancy mask, each cell keeping its dominant stroke
    ///      direction (V, \, H, /);
    ///   2. build a distance field per direction channel;
    ///   3. similarity = symmetric mean distance from each drawing cell to
    ///      the template's compatible ink (same direction free, adjacent
    ///      direction +1 cell) and back — so a wobble costs millimetres,
    ///      not a flipped signature;
    ///   4. ±9° readings of both sides absorb hand tilt;
    ///   5. accept only when the best rune clears RuneChamferFloor AND
    ///      beats the runner-up by RuneChamferMargin — the right rune or
    ///      none, enforced by construction.
    public static class InkChamfer
    {
        const int G = 32;                       // field resolution
        static readonly float[] Rotations = { -9f, 0f, 9f };

        class Reading
        {
            public byte[] Codes;                // 0 empty, else 1=V 2=\ 3=H 4=/
            public int[] Cells;                 // indices of inked cells
            public float[][] DtCode;            // [code 1..4] distance fields
        }

        static Dictionary<RuneType, List<Reading>> _templates;

        /// Call when a rune recording is (re)saved so templates rebuild.
        public static void Invalidate() => _templates = null;

        static void EnsureTemplates()
        {
            if (_templates != null) return;
            _templates = new Dictionary<RuneType, List<Reading>>();
            var report = new System.Text.StringBuilder("[SpellyZombie] InkChamfer templates:");
            foreach (RuneType t in System.Enum.GetValues(typeof(RuneType)))
            {
                if (t == RuneType.None) continue;
                var recorded = RuneLibrary.RecordedStrokes(t);
                List<IReadOnlyList<Vector2>> strokes = null;
                string source = "MISSING";
                if (recorded != null && recorded.Count > 0)
                {
                    strokes = new List<IReadOnlyList<Vector2>>();
                    int pts = 0;
                    foreach (var s in recorded) { strokes.Add(s); pts += s.Count; }
                    source = $"recorded({pts}pts)";
                }
                else
                {
                    var poly = RuneLibrary.GlyphPolyline(t);
                    if (poly != null && poly.Count >= 2)
                    {
                        strokes = new List<IReadOnlyList<Vector2>> { poly };
                        source = "synthetic";
                    }
                }
                if (strokes != null)
                {
                    var readings = BuildReadings(strokes);
                    if (readings.Count > 0) _templates[t] = readings;
                    else source += "+NO-READINGS";
                }
                report.Append($" {RuneLibrary.ShortName(t)}={source}");
            }
            Debug.Log(report.ToString());
        }

        /// The public entry: strokes → the rune they draw, or None (fizzle).
        /// ownerId gates to the player's unlocked runes.
        public static RuneType Recognize(int? ownerId,
            IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var (rune, _) = RecognizeScored(ownerId, strokes);
            return rune;
        }

        /// TEMPORARY DIAGNOSTICS: every classify appends its exact input to
        /// sz_classify_dump.csv in persistentDataPath, so a failing shape can
        /// be replayed through the offline harness bit-for-bit. Flip off once
        /// recognition is proven in play.
        public static bool DumpClassifies = true;
        static int _dumpSeq;

        public static (RuneType rune, float score) RecognizeScored(int? ownerId,
            IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            EnsureTemplates();
            var drawn = BuildReadings(strokes);

            int nStrokes = 0, nPts = 0;
            foreach (var s in strokes) { nStrokes++; nPts += s.Count; }

            var scores = new List<(RuneType t, float s)>();
            int locked = 0;
            if (drawn.Count > 0)
                foreach (var kv in _templates)
                {
                    if (ownerId.HasValue && !RuneLibrary.IsUnlocked(ownerId.Value, kv.Key)) { locked++; continue; }
                    float sr = 0f;
                    foreach (var d in drawn)
                        foreach (var t in kv.Value)
                            sr = Mathf.Max(sr, Similarity(d, t));
                    scores.Add((kv.Key, sr));
                }
            scores.Sort((a, b) => b.s.CompareTo(a.s));
            float s1 = scores.Count > 0 ? scores[0].s : 0f;
            float s2 = scores.Count > 1 ? scores[1].s : 0f;
            RuneType best = scores.Count > 0 ? scores[0].t : RuneType.None;

            bool accept = s1 >= DrawingConfig.RuneChamferFloor
                && s1 - s2 >= DrawingConfig.RuneChamferMargin;

            // EVIDENCE, not guesses: every classify states exactly what it
            // received and what it concluded.
            string top = "";
            for (int i = 0; i < Mathf.Min(3, scores.Count); i++)
                top += $" {RuneLibrary.ShortName(scores[i].t)} {scores[i].s:0.00}";
            Debug.Log($"[SpellyZombie] CLASSIFY {(accept ? "HIT" : "fizzle")} — " +
                $"input {nStrokes} strokes/{nPts} pts, readings {drawn.Count}, " +
                $"templates {_templates.Count} ({locked} locked), top:{top} " +
                $"(floor {DrawingConfig.RuneChamferFloor:0.00} margin {DrawingConfig.RuneChamferMargin:0.00})");
            if (DumpClassifies) DumpClassify(strokes, top, accept);

            return accept ? (best, s1) : (RuneType.None, 0f);
        }

        public static void DumpClassify(IReadOnlyList<IReadOnlyList<Vector2>> strokes, string top, bool accept)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                int seq = _dumpSeq++;
                sb.AppendLine($"# classify {seq} accept={accept} top={top.Trim()}");
                for (int si = 0; si < strokes.Count; si++)
                    foreach (var p in strokes[si])
                        sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0},{1},{2:0.####},{3:0.####}", seq, si, p.x, p.y));
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(Application.persistentDataPath, "sz_classify_dump.csv"),
                    sb.ToString());
            }
            catch { /* diagnostics must never break the game */ }
        }

        // ---------------------------------------------------- the field ----
        static List<Reading> BuildReadings(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var outp = new List<Reading>();
            foreach (var rot in Rotations)
            {
                var r = Rasterize(strokes, rot);
                if (r != null) outp.Add(r);
            }
            return outp;
        }

        static Reading Rasterize(IReadOnlyList<IReadOnlyList<Vector2>> strokes, float rotDeg)
        {
            float rad = rotDeg * Mathf.Deg2Rad;
            float ca = Mathf.Cos(rad), sa = Mathf.Sin(rad);
            float cx = 0f, cy = 0f; int cn = 0;
            foreach (var s in strokes)
                foreach (var p in s) { cx += p.x; cy += p.y; cn++; }
            if (cn == 0) return null;
            cx /= cn; cy /= cn;
            Vector2 R(Vector2 p)
            {
                float x = p.x - cx, y = p.y - cy;
                return new Vector2(x * ca - y * sa, x * sa + y * ca);
            }

            float minx = float.MaxValue, maxx = float.MinValue, miny = float.MaxValue, maxy = float.MinValue;
            foreach (var s in strokes)
                foreach (var raw in s)
                {
                    var p = R(raw);
                    minx = Mathf.Min(minx, p.x); maxx = Mathf.Max(maxx, p.x);
                    miny = Mathf.Min(miny, p.y); maxy = Mathf.Max(maxy, p.y);
                }
            if (minx > maxx) return null;
            float w = Mathf.Max(maxx - minx, 1e-4f), h = Mathf.Max(maxy - miny, 1e-4f);
            float size = Mathf.Max(w, h);
            // STRETCH-FILL both axes (the fix that survived the field): ink
            // drawn on the ground is FORESHORTENED by the view angle — up to
            // 2× squash — which uniform scale-fit faithfully preserved and
            // then failed to match. Filling the grid on both axes makes
            // aspect a non-signal on BOTH sides, template and drawing alike.
            // Sliver guard: nearly-1D marks keep uniform fit so a straight
            // line doesn't explode into noise.
            float scaleX, scaleY, ox, oy;
            if (Mathf.Min(w, h) > 0.10f * size)
            {
                scaleX = (G - 1) / w; scaleY = (G - 1) / h; ox = 0f; oy = 0f;
            }
            else
            {
                float scale = (G - 1) / size;
                scaleX = scaleY = scale;
                ox = (G - 1 - w * scale) * 0.5f; oy = (G - 1 - h * scale) * 0.5f;
            }
            float step = Mathf.Max(size / (G * 6f), 1e-4f);

            var hist = new int[G * G, 5];
            foreach (var s in strokes)
                for (int i = 0; i + 1 < s.Count; i++)
                {
                    Vector2 a = R(s[i]), b = R(s[i + 1]);
                    Vector2 d = b - a;
                    float len = d.magnitude;
                    if (len < 1e-6f) continue;
                    // orientation from the GRID-SPACE direction — a squashed
                    // diagonal reads as what it looks like after the fit
                    byte code = Orient(new Vector2(d.x * scaleX, d.y * scaleY));
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
                    for (int k = 0; k <= n; k++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, k / (float)n);
                        int col = Mathf.Clamp(Mathf.RoundToInt((p.x - minx) * scaleX + ox), 0, G - 1);
                        int row = Mathf.Clamp(Mathf.RoundToInt((G - 1) - ((p.y - miny) * scaleY + oy)), 0, G - 1);
                        hist[row * G + col, code]++;
                    }
                }

            var reading = new Reading { Codes = new byte[G * G] };
            var cells = new List<int>();
            for (int i = 0; i < G * G; i++)
            {
                int bestCode = 0, bestCount = 0;
                for (int code = 1; code <= 4; code++)
                    if (hist[i, code] > bestCount) { bestCount = hist[i, code]; bestCode = code; }
                if (bestCode != 0) { reading.Codes[i] = (byte)bestCode; cells.Add(i); }
            }
            if (cells.Count == 0) return null;
            reading.Cells = cells.ToArray();
            reading.DtCode = new float[5][];
            for (byte code = 1; code <= 4; code++)
            {
                var mask = new bool[G * G];
                for (int i = 0; i < G * G; i++) mask[i] = reading.Codes[i] == code;
                reading.DtCode[code] = DistanceField(mask);
            }
            return reading;
        }

        /// Direction from raw components — sign/compare only, no trig, so the
        /// same strokes read the same on every client.
        static byte Orient(Vector2 d)
        {
            float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y);
            // tan(22.5°)=0.4142, tan(67.5°)=2.4142
            if (ay <= ax * 0.4142f) return 3;                       // H
            if (ay >= ax * 2.4142f) return 1;                       // V
            return (d.x > 0f) == (d.y > 0f) ? (byte)4 : (byte)2;    // / : \
        }

        /// Two-pass chamfer distance transform (1 / √2 steps), cell units.
        static float[] DistanceField(bool[] mask)
        {
            const float BIG = 1e9f, D = 1.4142f;
            var dt = new float[G * G];
            for (int i = 0; i < G * G; i++) dt[i] = mask[i] ? 0f : BIG;
            for (int r = 0; r < G; r++)
                for (int c = 0; c < G; c++)
                {
                    int i = r * G + c;
                    if (r > 0) dt[i] = Mathf.Min(dt[i], dt[(r - 1) * G + c] + 1f);
                    if (c > 0) dt[i] = Mathf.Min(dt[i], dt[r * G + c - 1] + 1f);
                    if (r > 0 && c > 0) dt[i] = Mathf.Min(dt[i], dt[(r - 1) * G + c - 1] + D);
                    if (r > 0 && c < G - 1) dt[i] = Mathf.Min(dt[i], dt[(r - 1) * G + c + 1] + D);
                }
            for (int r = G - 1; r >= 0; r--)
                for (int c = G - 1; c >= 0; c--)
                {
                    int i = r * G + c;
                    if (r < G - 1) dt[i] = Mathf.Min(dt[i], dt[(r + 1) * G + c] + 1f);
                    if (c < G - 1) dt[i] = Mathf.Min(dt[i], dt[r * G + c + 1] + 1f);
                    if (r < G - 1 && c < G - 1) dt[i] = Mathf.Min(dt[i], dt[(r + 1) * G + c + 1] + D);
                    if (r < G - 1 && c > 0) dt[i] = Mathf.Min(dt[i], dt[(r + 1) * G + c - 1] + D);
                }
            return dt;
        }

        // ---------------------------------------------------- the score ----
        static float Similarity(Reading a, Reading b)
        {
            float c = (Half(a, b) + Half(b, a)) * 0.5f * 24f / G; // 24-cell reference scale
            return 1f / (1f + c * 0.9f);
        }

        /// Mean distance from a's ink to b's COMPATIBLE ink: same direction
        /// channel free, adjacent channel (V or H vs a diagonal) one cell
        /// extra. V never matches H, \ never matches / — orientation is
        /// meaning, but a hand wobble between neighbours is cheap.
        static float Half(Reading a, Reading b)
        {
            float sum = 0f;
            foreach (var i in a.Cells)
            {
                byte code = a.Codes[i];
                bool diag = code == 2 || code == 4;
                float d = b.DtCode[code][i];
                for (byte other = 1; other <= 4; other++)
                {
                    if (other == code) continue;
                    bool otherDiag = other == 2 || other == 4;
                    if (otherDiag == diag) continue; // opposite class only
                    d = Mathf.Min(d, b.DtCode[other][i] + 1f);
                }
                sum += d;
            }
            return sum / a.Cells.Length;
        }
    }
}
