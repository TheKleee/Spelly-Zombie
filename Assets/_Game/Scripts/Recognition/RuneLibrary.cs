using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    public enum RuneType
    {
        None = 0,
        HeatUp = 1,
        HeatDown = 2,
        StateSolid = 3,
        StateLiquid = 4,
        LuminanceUp = 5,
        LuminanceDown = 6,
        StickyUp = 7,
        StickyDown = 8,
        DirectionAway = 9,   // arrow pointing away from the surface
        DirectionToward = 10,// Y pulling toward the surface
        DensityUp = 11,
        DensityDown = 12
    }

    /// One collectable card = one of these = BOTH directions of the pair.
    /// Picking up the Heat card teaches heat-up AND heat-down at once.
    public enum RuneCardType
    {
        Heat, State, Luminance, Sticky, Direction, Density
    }

    /// Holds one $P point-cloud template per rune. Templates may be drawn in any
    /// number of strokes, in any order and direction — an arrow recorded as
    /// shaft + barbs matches an arrow drawn barbs-first. Ships with rough
    /// synthesized glyphs; each can be overwritten in play mode (draw the glyph,
    /// press F1-F12) and the recording persists to disk.
    public static class RuneLibrary
    {
        class Entry
        {
            public RuneType Type;
            public Vector2[] Cloud;                 // $P point-cloud template
            public List<byte[]> Sentences;          // chain-code direction sentences
            public float Elongation = 1f;           // proportion — separates the bracket family
            public ShapeFeel Feel;                  // fingerprint — vetoes impostors of a different KIND
            // MULTI-TEMPLATE (Marko's studio walls): every drawing on a rune's
            // wall is a variant of HIS hand — the ensemble scores against all
            // of them and keeps the best. More samples = recognition converges
            // on how he actually draws, not one lucky snapshot.
            public readonly List<Entry> Variants = new List<Entry>();
        }

        /// Cheap shape fingerprint. The matchers measure "how much does it
        /// LOOK like" — this measures "is it even the same KIND of shape":
        /// how far the pen TURNED in total (a star turns ~3x more than a
        /// zigzag), which way an open shape's MOUTH faces (the only thing
        /// separating the solid/liquid brackets), and the pen-lift count.
        struct ShapeFeel
        {
            public float TotalTurn;   // summed |direction change|, degrees
            public float GapBearing;  // where the opening faces (deg); NaN = closed shape
            public float LongestFrac; // share of total ink in the longest STRAIGHT RUN —
                                      // an arrow is shaft-dominated, a Y splits evenly
                                      // (Marko's "length of the parts involved")
            public int Strokes;       // count of STITCHED paths — topology, never pen lifts
        }

        /// MARKO'S LAW: the pen-lift count NEVER matters — only the end shape.
        /// Strokes whose ENDPOINTS meet are stitched into continuous paths
        /// before any shape math runs, so an arrow drawn as three separate
        /// lines measures identically to one drawn in a single sweep (the
        /// corner between two stitched lines COUNTS as a corner).
        static List<List<Vector2>> StitchStrokes(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var paths = new List<List<Vector2>>();
            foreach (var s in strokes)
                if (s != null && s.Count >= 2) paths.Add(new List<Vector2>(s));

            const float stitchDist = 0.05f; // endpoints this close = one continuous line
            bool merged = true;
            while (merged && paths.Count > 1)
            {
                merged = false;
                int bi = -1, bj = -1;
                bool revA = false, revB = false;
                float best = stitchDist * stitchDist;
                for (int i = 0; i < paths.Count; i++)
                    for (int j = i + 1; j < paths.Count; j++)
                    {
                        var A = paths[i];
                        var B = paths[j];
                        void Check(Vector2 pa, Vector2 pb, bool ra, bool rb)
                        {
                            float d = (pa - pb).sqrMagnitude;
                            if (d < best) { best = d; bi = i; bj = j; revA = ra; revB = rb; }
                        }
                        Check(A[A.Count - 1], B[0], false, false);
                        Check(A[A.Count - 1], B[B.Count - 1], false, true);
                        Check(A[0], B[0], true, false);
                        Check(A[0], B[B.Count - 1], true, true);
                    }
                if (bi >= 0)
                {
                    var A = paths[bi];
                    var B = paths[bj];
                    if (revA) A.Reverse();
                    if (revB) B.Reverse();
                    A.AddRange(B);
                    paths.RemoveAt(bj);
                    merged = true;
                }
            }
            return paths;
        }

        /// Expects STITCHED paths (see StitchStrokes) — callers stitch once.
        static ShapeFeel Fingerprint(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var f = new ShapeFeel { GapBearing = float.NaN, LongestFrac = 1f };
            int longest = -1;
            float longestLen = 0f, totalLen = 0f, turn = 0f, longestRun = 0f;
            for (int s = 0; s < strokes.Count; s++)
            {
                var pts = strokes[s];
                if (pts == null || pts.Count < 2) continue;
                f.Strokes++;
                float len = 0f;
                for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
                totalLen += len;
                if (len > longestLen) { longestLen = len; longest = s; }

                // fixed-step resample so pen speed / point density can't fake
                // corners; straight RUNS end at corners (> 45°), and the
                // longest run is the shape's dominant part — pen-lift-proof
                var r = ResampleStroke(pts, 24);
                float run = 0f;
                for (int i = 1; i < r.Count; i++)
                {
                    run += Vector2.Distance(r[i - 1], r[i]);
                    if (i < 2) continue;
                    Vector2 a = r[i - 1] - r[i - 2], b = r[i] - r[i - 1];
                    if (a.sqrMagnitude < 1e-10f || b.sqrMagnitude < 1e-10f) continue;
                    float ang = Mathf.Abs(Vector2.SignedAngle(a, b));
                    turn += ang;
                    if (ang > 45f)
                    {
                        longestRun = Mathf.Max(longestRun, run);
                        run = 0f;
                    }
                }
                longestRun = Mathf.Max(longestRun, run);
            }
            f.TotalTurn = turn;
            if (totalLen > 1e-5f) f.LongestFrac = longestRun / totalLen;

            if (longest >= 0)
            {
                var pts = strokes[longest];
                Vector2 min = pts[0], max = pts[0], centroid = Vector2.zero;
                foreach (var p in pts)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                    centroid += p;
                }
                centroid /= pts.Count;
                float diag = (max - min).magnitude;
                Vector2 mouth = (pts[0] + pts[pts.Count - 1]) * 0.5f;
                if (diag > 1e-4f && Vector2.Distance(pts[0], pts[pts.Count - 1]) > diag * 0.22f
                    && (mouth - centroid).sqrMagnitude > 1e-8f)
                    f.GapBearing = Mathf.Atan2(mouth.y - centroid.y, mouth.x - centroid.x) * Mathf.Rad2Deg;
            }
            return f;
        }

        static List<Vector2> ResampleStroke(IReadOnlyList<Vector2> pts, int n)
        {
            var result = new List<Vector2>(n) { pts[0] };
            float total = 0f;
            for (int i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
            if (total <= 1e-6f) return result;
            float step = total / (n - 1), acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 prev = pts[i - 1], cur = pts[i];
                float d = Vector2.Distance(prev, cur);
                while (acc + d >= step && d > 1e-8f)
                {
                    float t = (step - acc) / d;
                    Vector2 q = Vector2.Lerp(prev, cur, t);
                    result.Add(q);
                    prev = q;
                    d = Vector2.Distance(prev, cur);
                    acc = 0f;
                }
                acc += d;
            }
            return result;
        }

        /// 1 = same kind of shape; sinks as fingerprints disagree. Multiplied
        /// into every match score, so an impostor that "looks similar" but
        /// turns half as much (star vs zigzag) or opens the wrong way (solid
        /// vs liquid bracket) loses to the honest candidate BEFORE argmax.
        static float FeelPenalty(in ShapeFeel d, in ShapeFeel t)
        {
            float p = 1f;

            float turnRatio = (Mathf.Min(d.TotalTurn, t.TotalTurn) + 60f)
                / (Mathf.Max(d.TotalTurn, t.TotalTurn) + 60f); // +60 forgives tiny shapes
            if (turnRatio < 0.62f)
            {
                float k = Mathf.Clamp01(turnRatio / 0.62f);
                p *= Mathf.Lerp(0.4f, 1f, k * k);
            }

            if (!float.IsNaN(d.GapBearing) && !float.IsNaN(t.GapBearing))
            {
                float dAng = Mathf.Abs(Mathf.DeltaAngle(d.GapBearing, t.GapBearing));
                if (dAng > 70f)
                    p *= Mathf.Lerp(1f, 0.5f, Mathf.Clamp01((dAng - 70f) / 70f));
            }

            // Marko's rule: when shapes are topological cousins (arrow vs Y),
            // the LENGTH DISTRIBUTION decides — a shaft-heavy drawing is an
            // arrow even when an overshoot makes the junction look like a fork
            float dFrac = Mathf.Abs(d.LongestFrac - t.LongestFrac);
            if (dFrac > 0.18f)
                p *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((dFrac - 0.18f) / 0.25f));

            if (Mathf.Abs(d.Strokes - t.Strokes) >= 2) p *= 0.75f;
            return p;
        }

        [Serializable]
        class SavedStroke { public List<Vector2> points = new List<Vector2>(); }

        [Serializable]
        class SavedSample { public List<SavedStroke> strokes = new List<SavedStroke>(); }

        [Serializable]
        class SavedTemplate
        {
            public int rune;
            public List<Vector2> points = new List<Vector2>();      // legacy single-stroke format
            public List<SavedStroke> strokes = new List<SavedStroke>(); // NEWEST sample (multi-stroke)
            // OLDER samples, oldest first (Marko's multi-sample recording:
            // F-key APPENDS, never overwrites; capped at MaxSamples total).
            // Current matchers read only the newest; the multi-template
            // matcher will read the whole pool.
            public List<SavedSample> older = new List<SavedSample>();
        }

        [Serializable]
        class SavedTemplateSet
        {
            public int version;
            public List<SavedTemplate> items = new List<SavedTemplate>();
        }

        /// Bump when the default glyph alphabet changes — stale recordings from
        /// an older alphabet are discarded instead of shadowing the new shapes.
        const int GlyphSetVersion = 6; // v6 = the ORIGINAL alphabet restored (Marko's final pick)

        static List<Entry> _entries;
        static string SavePath => Path.Combine(Application.persistentDataPath, "sz_rune_templates.json");

        // ---- unlocks: per-OWNER, in memory only (design: every run starts with
        // a single chosen card; the rest are collected — see Grimoire) ----

        /// Graybox switch: when true everything is drawable by everyone (all runes
        /// unlocked, no starting-rune picker) — flip on only for combo testing.
        public static bool AllRunesUnlockedForTesting = false;

        /// Convenience: unlock a card for the LOCAL player (pickups use this).
        public static void UnlockCard(RuneCardType card) => Grimoire.Unlock(Grimoire.LocalPlayerId, card);

        /// The card gate applies only WHERE THE MATCH HAPPENS (Marko's rule:
        /// "limited just in the game"): during a run, or standing in the
        /// selected map scene. The lobby, menu and sandboxes are free practice
        /// grounds — draw everything, learn everything. Public: the starting
        /// rune chooser and the grimoire display follow the same geography.
        public static bool RestrictedArena =>
            RoundDirector.RunActive
            || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == MatchLobby.SelectedMap;

        public static bool IsUnlocked(int ownerId, RuneType type) =>
            type != RuneType.None
            && (AllRunesUnlockedForTesting || !RestrictedArena || Grimoire.Has(ownerId, CardOf(type)));

        public static RuneCardType CardOf(RuneType type)
        {
            switch (type)
            {
                case RuneType.HeatUp:
                case RuneType.HeatDown: return RuneCardType.Heat;
                case RuneType.StateSolid:
                case RuneType.StateLiquid: return RuneCardType.State;
                case RuneType.LuminanceUp:
                case RuneType.LuminanceDown: return RuneCardType.Luminance;
                case RuneType.StickyUp:
                case RuneType.StickyDown: return RuneCardType.Sticky;
                case RuneType.DirectionAway:
                case RuneType.DirectionToward: return RuneCardType.Direction;
                default: return RuneCardType.Density;
            }
        }

        /// Grimoire copy: what the card teaches, in plain words.
        public static string CardDescription(RuneCardType card)
        {
            switch (card)
            {
                case RuneCardType.Heat: return "Heat — the jagged flame heats; the flat zigzag chills.";
                case RuneCardType.State: return "State — bracket open at the bottom = SOLID; open at the top = LIQUID.";
                case RuneCardType.Luminance: return "Luminance — the star brightens; the collapsed star darkens.";
                case RuneCardType.Sticky: return "Sticky — the slope-hook grips; its mirror slides.";
                case RuneCardType.Direction: return "Direction — the arrow pushes the way you drew it; the Y pulls.";
                default: return "Density — small bracket open-down compresses; open-up spreads.";
            }
        }

        /// COMPOUND SIGILS (Marko's "multiple letters" idea): one continuous
        /// scribble can BE several runes — its direction sentence is parsed
        /// like a word: consecutive chunks that each read as a rune all fire.
        /// Draw flame+arrow+bracket as one gibberish sigil → Heat, Direction
        /// and Solid all cast. Returns the parts (≥2) or an empty list.
        public static List<(RuneType type, float score)> ClassifyCompound(int ownerId,
            IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            Init();
            var none = new List<(RuneType, float)>();
            var readings = ChainCodeRecognizer.EncodeAll(rawStrokes);

            List<(RuneType, float)> bestParts = null;
            float bestQuality = 0f;

            foreach (var reading in readings)
            {
                int n = reading.Length;
                // a WORD is LONG — two-plus runes of pen. Short scribbles are
                // one glyph's business (a small rectangle once read as PULL×3
                // through this path; never again)
                if (n < 9 || n > 24) continue;

                // dp[i]: best way to explain the first i letters
                var dpScore = new float[n + 1]; // length-weighted score sum
                var dpSkips = new int[n + 1];
                var dpParts = new List<(RuneType, float)>[n + 1];
                for (int i = 1; i <= n; i++) dpScore[i] = -1f;
                dpParts[0] = new List<(RuneType, float)>();

                for (int i = 1; i <= n; i++)
                {
                    // option: this letter is junk between words (max 1 junk letter)
                    if (dpScore[i - 1] >= 0f && dpSkips[i - 1] < 1)
                    {
                        dpScore[i] = dpScore[i - 1];
                        dpSkips[i] = dpSkips[i - 1] + 1;
                        dpParts[i] = dpParts[i - 1];
                    }

                    // option: letters j..i are one rune
                    for (int len = 2; len <= Mathf.Min(i, 10); len++)
                    {
                        int j = i - len;
                        if (dpScore[j] < 0f || dpParts[j].Count >= 4) continue;

                        foreach (var e in _entries)
                        {
                            if (!IsUnlocked(ownerId, e.Type)) continue;
                            float sc = e.Sentences != null
                                ? ChainCodeRecognizer.ScoreSpan(reading, j, len, e.Sentences) : 0f;
                            foreach (var v in e.Variants)
                                if (v.Sentences != null)
                                    sc = Mathf.Max(sc, ChainCodeRecognizer.ScoreSpan(reading, j, len, v.Sentences));
                            if (sc < 0.7f) continue; // letters must be CLEAN — spans have no fingerprint guard
                            float total = dpScore[j] + sc * len;
                            if (total > dpScore[i])
                            {
                                dpScore[i] = total;
                                dpSkips[i] = dpSkips[j];
                                var parts = new List<(RuneType, float)>(dpParts[j]) { (e.Type, sc) };
                                dpParts[i] = parts;
                            }
                        }
                    }
                }

                if (dpScore[n] < 0f || dpParts[n] == null || dpParts[n].Count < 2) continue;
                float quality = dpScore[n] / Mathf.Max(1, n - dpSkips[n]);
                if (quality < 0.7f) continue; // an absolute floor, not just best-of-the-bad
                if (quality > bestQuality)
                {
                    bestQuality = quality;
                    bestParts = dpParts[n];
                }
            }

            return bestParts ?? none;
        }

        /// The default polyline for a rune's glyph — zombies "draw" runes by
        /// tracing this (see ZombieScribe). Returns null for unknown runes.
        public static List<Vector2> GlyphPolyline(RuneType type)
        {
            return DefaultGlyphs().TryGetValue(type, out var pts) ? pts : null;
        }

        /// A recording thinner than this can't describe a shape — it misreads
        /// everything angular as itself (the 7-point GRIP flick ate an arrow).
        /// Sparse recordings are treated as NOT RECORDED: seed shape used for
        /// recognition AND display, until re-recorded properly.
        const int MinTemplatePoints = 12;

        static int PointCount(List<List<Vector2>> strokes)
        {
            int n = 0;
            foreach (var s in strokes) n += s.Count;
            return n;
        }

        /// The player's RECORDED strokes for a rune (raw, as drawn with F1-F12)
        /// — null when nothing usable was recorded. Displays (menu rune ring,
        /// zombie scrawl) prefer these: the alphabet shown is the one YOU
        /// taught it. Sparse recordings don't count (see MinTemplatePoints).
        public static List<List<Vector2>> RecordedStrokes(RuneType type)
        {
            Init();
            if (_saved == null || _saved.items == null) return null;
            var item = _saved.items.Find(i => i.rune == (int)type);
            if (item == null) return null;
            var strokes = ToStrokeLists(item);
            return strokes.Count > 0 && PointCount(strokes) >= MinTemplatePoints ? strokes : null;
        }

        /// Similarity of a partial sketch to EVERY owned rune, best first — the
        /// choose-and-stamp candidate list. This only SORTS the options; it
        /// never gates anything (the player's choice is the truth).
        public static List<(RuneType type, float score)> ScoreAll(int ownerId,
            IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            Init();
            var results = new List<(RuneType, float)>();
            var stitchedAll = StitchStrokes(rawStrokes); // end shape, not pen lifts
            var candidate = PointCloudRecognizer.Normalize(rawStrokes);
            var sentences = ChainCodeRecognizer.EncodeAll(stitchedAll);
            float elongation = ChainCodeRecognizer.Elongation(stitchedAll);
            var feel = Fingerprint(stitchedAll);
            foreach (var e in _entries)
            {
                if (!IsUnlocked(ownerId, e.Type)) continue;
                float score = VariantScore(e, candidate, sentences, elongation, feel);
                foreach (var v in e.Variants)
                    score = Mathf.Max(score, VariantScore(v, candidate, sentences, elongation, feel));
                results.Add((e.Type, score));
            }
            results.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return results;
        }

        public static string ShortName(RuneType r)
        {
            switch (r)
            {
                case RuneType.HeatUp: return "HEAT";
                case RuneType.HeatDown: return "CHILL";
                case RuneType.StateSolid: return "SOLID";
                case RuneType.StateLiquid: return "LIQUID";
                case RuneType.LuminanceUp: return "LIGHT";
                case RuneType.LuminanceDown: return "DARK";
                case RuneType.StickyUp: return "GRIP";
                case RuneType.StickyDown: return "SLICK";
                case RuneType.DirectionAway: return "PUSH";
                case RuneType.DirectionToward: return "PULL";
                case RuneType.DensityUp: return "COMPRESS";
                case RuneType.DensityDown: return "SPREAD";
                default: return "?";
            }
        }

        public static readonly RuneType[] RecordableRunes =
        {
            RuneType.HeatUp, RuneType.HeatDown, RuneType.StateSolid, RuneType.StateLiquid,
            RuneType.LuminanceUp, RuneType.LuminanceDown, RuneType.StickyUp, RuneType.StickyDown,
            RuneType.DirectionAway, RuneType.DirectionToward, RuneType.DensityUp, RuneType.DensityDown
        };

        static void Init()
        {
            if (_entries != null) return;
            _entries = new List<Entry>();
            foreach (var pair in DefaultGlyphs())
                SetTemplateInternal(pair.Key, new List<List<Vector2>> { pair.Value });
            LoadRecorded();
            AuditTemplates();
        }

        /// Pay the whole recognition bill at SCENE LOAD, never on the first
        /// rune (Marko: the map hitched when the first drawing classified).
        /// Loads every recorded sample, audits the pools, then pushes one
        /// throwaway glyph through the full scoring path — ownerId null scores
        /// the ENTIRE library — so code and buffers are hot before anyone draws.
        public static void Warm()
        {
            Init();
            var poke = new List<IReadOnlyList<Vector2>>
            {
                new List<Vector2>
                {
                    new Vector2(0f, 0f), new Vector2(0.3f, 0.5f),
                    new Vector2(0.6f, 0f), new Vector2(0.9f, 0.5f)
                }
            };
            Top2(null, poke);
        }

        /// Classify a glyph (one or more raw 2D strokes in a shared frame)
        /// against the runes the given OWNER has unlocked — the seal's owner is
        /// whoever completed it, so zombie-closed seals read with zombie cards.
        ///
        /// REVERTED to the $P + direction-sentence ensemble (Marko's ruling
        /// Jul 20: his line-scan design measured 76% with the star pair at
        /// ~12%, below his bar — "then we revert back to what used to work").
        /// The chamfer matcher stays in the project (InkChamfer), benched.
        /// AMBIGUITY GUARD kept: two different runes scoring within
        /// RuneAmbiguityMargin = coin flip → fizzle, never misfire.
        /// Every call logs what it received and concluded, and dumps the raw
        /// input for offline replay — the silent-fizzle hunt stays armed.
        public static (RuneType type, float score) Classify(int ownerId, IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            Init();
            var (t1, s1, t2, s2) = Top2(ownerId, rawStrokes);
            // A STRONG top is trusted outright (Marko's Jul 22 bug: the big
            // wall pools raised every rune's runner-up score, so honest CHILL
            // and COMPRESS draws sat 0.03 above some unrelated rune and the
            // guard ate them). The coin-flip fizzle now referees only WEAK
            // tops, where a near-tie genuinely is a scribble.
            bool ambiguous = t1 != RuneType.None && t2 != RuneType.None && t2 != t1
                && s1 < DrawingConfig.RuneTrustScore
                && s1 - s2 < DrawingConfig.RuneAmbiguityMargin
                && s2 >= DrawingConfig.MinRuneScore
                && (_confusable == null || !_confusable.Contains(PairKey(t1, t2)));

            RuneType rune = t1 == RuneType.None || ambiguous ? RuneType.None : t1;
            float score = t1 == RuneType.None ? 0f : s1;
            bool hit = rune != RuneType.None && score >= DrawingConfig.MinRuneScore;

            int nStrokes = 0, nPts = 0;
            foreach (var s in rawStrokes) { nStrokes++; nPts += s.Count; }
            Debug.Log($"[SpellyZombie] CLASSIFY {(hit ? "HIT" : "fizzle")} ($P ensemble) — " +
                $"input {nStrokes} strokes/{nPts} pts, top {ShortName(t1)} {s1:0.00}, " +
                $"next {ShortName(t2)} {s2:0.00}{(ambiguous ? " AMBIGUOUS" : "")} " +
                $"(floor {DrawingConfig.MinRuneScore:0.00})");
            InkChamfer.DumpClassify(rawStrokes,
                $"{ShortName(t1)} {s1:0.00} / {ShortName(t2)} {s2:0.00}", hit);

            return (rune, score);
        }

        /// ENSEMBLE scoring: the $P point-cloud matcher AND the direction-
        /// sentence matcher both score every rune; each rune keeps its best
        /// reading. Returns the two best DIFFERENT runes. ownerId gates by
        /// unlocks; pass null to score the full library (template audit).
        static (RuneType t1, float s1, RuneType t2, float s2) Top2(int? ownerId,
            IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            // pen lifts never matter: stitch to the END SHAPE first, then measure
            var stitched = StitchStrokes(rawStrokes);
            var candidate = PointCloudRecognizer.Normalize(rawStrokes); // point set — lift-proof already
            var sentences = ChainCodeRecognizer.EncodeAll(stitched);
            float elongation = ChainCodeRecognizer.Elongation(stitched);
            if (candidate == null && sentences.Count == 0)
                return (RuneType.None, 0f, RuneType.None, 0f);
            var feel = Fingerprint(stitched);

            RuneType bestType = RuneType.None, secondType = RuneType.None;
            float bestScore = 0f, secondScore = 0f;
            Entry bestEntry = null, secondEntry = null;
            foreach (var e in _entries)
            {
                if (ownerId.HasValue && !IsUnlocked(ownerId.Value, e.Type)) continue;
                // nearest-of-many: the drawing matches whichever of this
                // rune's samples it most resembles
                float score = VariantScore(e, candidate, sentences, elongation, feel);
                foreach (var v in e.Variants)
                    score = Mathf.Max(score, VariantScore(v, candidate, sentences, elongation, feel));
                if (score > bestScore)
                {
                    secondType = bestType; secondScore = bestScore; secondEntry = bestEntry;
                    bestType = e.Type; bestScore = score; bestEntry = e;
                }
                else if (score > secondScore)
                {
                    secondType = e.Type; secondScore = score; secondEntry = e;
                }
            }

            // SECOND STAGE — THE FOOT-LINE JUDGE (Marko's Solid-vs-Compress
            // catch: glyphs that differ by one small feature score near-equal
            // on $P, which shrugs at a missing foot). When the top two are
            // close, re-rank by mutual COVERAGE: a drawing with a second foot
            // leaves a one-footed sample's corner uncovered, and pays for it.
            if (candidate != null && bestEntry != null && secondEntry != null
                && bestScore - secondScore < 0.18f)
            {
                float c1 = BestCoverage(bestEntry, candidate);
                float c2 = BestCoverage(secondEntry, candidate);
                float r1 = bestScore * Mathf.Lerp(0.7f, 1f, c1);
                float r2 = secondScore * Mathf.Lerp(0.7f, 1f, c2);
                if (r2 > r1)
                {
                    (bestType, secondType) = (secondType, bestType);
                    (bestScore, secondScore) = (r2, r1);
                }
                else { bestScore = r1; secondScore = r2; }
            }
            return (bestType, bestScore, secondType, secondScore);
        }

        /// Best mutual-coverage between the drawing and any sample of this
        /// rune: the fraction of each cloud that finds a neighbour in the
        /// other. Missing features (an absent foot, an extra tick) live
        /// exactly in the uncovered remainder.
        static float BestCoverage(Entry e, Vector2[] candidate)
        {
            float best = Coverage(candidate, e.Cloud);
            foreach (var v in e.Variants)
                best = Mathf.Max(best, Coverage(candidate, v.Cloud));
            return best;
        }

        static float Coverage(Vector2[] a, Vector2[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return 1f;
            const float eps2 = 0.09f * 0.09f;
            int hitA = 0;
            for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++)
                    if ((a[i] - b[j]).sqrMagnitude <= eps2) { hitA++; break; }
            int hitB = 0;
            for (int j = 0; j < b.Length; j++)
                for (int i = 0; i < a.Length; i++)
                    if ((a[i] - b[j]).sqrMagnitude <= eps2) { hitB++; break; }
            return 0.5f * (hitA / (float)a.Length + hitB / (float)b.Length);
        }

        static float VariantScore(Entry v, Vector2[] candidate,
            List<byte[]> sentences, float elongation, ShapeFeel feel)
        {
            float p = candidate != null && v.Cloud != null
                ? PointCloudRecognizer.Score(PointCloudRecognizer.CloudDistance(candidate, v.Cloud)) : 0f;
            float chain = v.Sentences != null
                ? ChainCodeRecognizer.Match(sentences, v.Sentences)
                  * ChainCodeRecognizer.AspectPenalty(elongation, v.Elongation) : 0f;
            return Mathf.Max(p, chain) * FeelPenalty(feel, v.Feel);
        }

        // ---- template health: is the alphabet YOURS, and is it unambiguous? --

        static HashSet<int> _confusable; // rune pairs whose templates read alike
        static int PairKey(RuneType a, RuneType b)
        {
            int x = (int)a, y = (int)b;
            return x < y ? (x << 8) | y : (y << 8) | x;
        }

        /// Runs at load and after every re-recording. Answers two questions in
        /// the Console: (1) which runes still use DEFAULT shapes instead of
        /// your handwriting, (2) do any two templates read so alike that a
        /// cross-fire is possible? Alike pairs are remembered so the ambiguity
        /// guard doesn't render them uncastable — re-recording is the real fix.
        static void AuditTemplates()
        {
            _confusable = new HashSet<int>();

            var seedRunes = new List<string>();
            foreach (var r in RecordableRunes)
            {
                var rec = RecordedStrokes(r);
                if (rec == null) { seedRunes.Add(ShortName(r)); continue; }
                int pts = 0;
                foreach (var s in rec) pts += s.Count;
                // a 7-point flick barely describes a shape — sparse templates
                // score weakly against their OWN drawings, so a richer rune's
                // template can outbid them (how GRIP once read as SOLID)
                if (pts < 12)
                    Debug.LogWarning($"[RuneLibrary] AUDIT: the {ShortName(r)} template is only {pts} points — sparse recordings misread easily; re-record it drawing larger and slower.");
            }
            if (seedRunes.Count > 0)
                Debug.LogWarning($"[RuneLibrary] AUDIT: {seedRunes.Count} rune(s) still use DEFAULT shapes, not your handwriting: {string.Join(", ", seedRunes)} — draw each and press its F-key to record.");
            else
                Debug.Log("[RuneLibrary] AUDIT: all 12 runes use YOUR recorded handwriting.");

            foreach (var e in _entries)
            {
                // read this rune's own template back as if freshly drawn
                var src = RecordedStrokes(e.Type);
                if (src == null)
                {
                    var poly = GlyphPolyline(e.Type);
                    if (poly == null) continue;
                    src = new List<List<Vector2>> { poly };
                }
                var (t1, s1, t2, s2) = Top2(null, ToReadOnly(src));
                if (t1 == RuneType.None) continue;

                if (t1 != e.Type)
                {
                    Debug.LogError($"[RuneLibrary] AUDIT: the {ShortName(e.Type)} template reads as {ShortName(t1)} ({s1:F2}) — re-record {ShortName(e.Type)} with a more distinct shape!");
                    _confusable.Add(PairKey(e.Type, t1));
                }
                else if (t2 != RuneType.None && s1 - s2 < 0.12f)
                {
                    Debug.LogWarning($"[RuneLibrary] AUDIT: {ShortName(e.Type)} and {ShortName(t2)} templates score within {s1 - s2:F2} of each other — cross-fires possible; consider re-recording one of them.");
                    _confusable.Add(PairKey(e.Type, t2));
                }
            }

            // POOL-AWARE CONFUSABILITY (the multi-template era): every saved
            // wall drawing is read back as if freshly drawn. When a drawing
            // of rune A reads as B — or nearly ties with B — those two are
            // genuinely entangled IN MARKO'S HAND, and the coin-flip guard
            // would eat every valid cast between them (PULL 0.80/PUSH 0.79
            // fizzled his correct Ys). Entangled pairs are exempt: the top
            // score wins. The console names them so cleaning up a wall stays
            // his informed choice.
            var entangled = new List<string>();
            foreach (var e in _entries)
            {
                foreach (var sample in AllSamples(e.Type))
                {
                    var (t1, s1, t2, s2) = Top2(null, ToReadOnly(sample));
                    if (t1 == RuneType.None) continue;
                    int key;
                    if (t1 != e.Type) key = PairKey(e.Type, t1);
                    else if (t2 != RuneType.None
                        && s1 - s2 < DrawingConfig.RuneAmbiguityMargin + 0.03f)
                        key = PairKey(e.Type, t2);
                    else continue;
                    if (_confusable.Add(key))
                        entangled.Add($"{ShortName(e.Type)}~{ShortName(t1 != e.Type ? t1 : t2)}");
                }
            }
            if (entangled.Count > 0)
                Debug.Log($"[RuneLibrary] AUDIT: pairs entangled in your handwriting (top score decides between them): {string.Join(", ", entangled)}");
        }

        /// Marko's rule: EVERYTHING drawn on a wall is saved — no practical
        /// cap (this bound only guards against a runaway file).
        const int MaxSamples = 200;

        /// ALL saved samples for a rune, oldest first (each sample = the
        /// strokes of one drawing). The Rune Studio walls repaint from this.
        public static List<List<List<Vector2>>> AllSamples(RuneType type)
        {
            Init();
            var result = new List<List<List<Vector2>>>();
            var item = _saved?.items?.Find(i => i.rune == (int)type);
            if (item == null) return result;
            if (item.older != null)
                foreach (var s in item.older)
                {
                    var strokes = SampleStrokes(s?.strokes);
                    if (strokes.Count > 0) result.Add(strokes);
                }
            var newest = ToStrokeLists(item);
            if (newest.Count > 0) result.Add(newest);
            return result;
        }

        static List<List<Vector2>> SampleStrokes(List<SavedStroke> src)
        {
            var outp = new List<List<Vector2>>();
            if (src == null) return outp;
            foreach (var s in src)
                if (s != null && s.points != null && s.points.Count >= 2)
                    outp.Add(s.points);
            return outp;
        }

        /// THE RUNE STUDIO SAVE (Marko's design): a wall's ink IS the rune's
        /// sample pool — this replaces the whole pool with the wall snapshot.
        /// Too-sparse drawings are skipped (logged); an empty wall clears the
        /// rune back to its synthetic seed shape. Returns how many were kept.
        public static int ReplaceSamples(RuneType type, List<List<List<Vector2>>> samples)
        {
            Init();
            var kept = new List<List<List<Vector2>>>();
            foreach (var s in samples)
            {
                if (s == null || s.Count == 0) continue;
                if (PointCount(s) < MinTemplatePoints)
                {
                    Debug.LogWarning($"[RuneLibrary] {ShortName(type)}: a wall drawing has only {PointCount(s)} points — skipped (draw larger/slower).");
                    continue;
                }
                kept.Add(s);
                if (kept.Count == MaxSamples) break;
            }

            var item = _saved.items.Find(i => i.rune == (int)type);
            if (kept.Count == 0)
            {
                if (item != null) _saved.items.Remove(item);
            }
            else
            {
                if (item == null)
                {
                    item = new SavedTemplate { rune = (int)type };
                    _saved.items.Add(item);
                }
                item.points = new List<Vector2>(); // legacy field stays retired
                item.older = new List<SavedSample>();
                for (int i = 0; i < kept.Count - 1; i++)
                    item.older.Add(new SavedSample { strokes = ToSavedStrokes(kept[i]) });
                item.strokes = ToSavedStrokes(kept[kept.Count - 1]);
            }
            _saved.version = GlyphSetVersion;
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(_saved)); }
            catch (Exception e) { Debug.LogWarning($"[RuneLibrary] Failed to save samples: {e.Message}"); }

            // IN-PLACE matcher update (Marko: the full reload + audit on every
            // save lagged the studio): only THIS rune's variants rebuild; the
            // template audit stays a session-start affair.
            if (_entries != null)
            {
                if (kept.Count == 0)
                {
                    var poly = GlyphPolyline(type);
                    if (poly != null)
                        SetTemplateInternal(type, new List<List<Vector2>> { poly });
                }
                else
                {
                    bool first = true;
                    foreach (var sample in kept)
                    {
                        SetTemplateInternal(type, sample, append: !first);
                        first = false;
                    }
                }

                // refresh THIS rune's entanglements immediately — the studio
                // test loop must judge by the wall as it is NOW
                if (_confusable != null)
                    foreach (var sample in kept)
                    {
                        var (t1, s1, t2, s2) = Top2(null, ToReadOnly(sample));
                        if (t1 == RuneType.None) continue;
                        if (t1 != type) _confusable.Add(PairKey(type, t1));
                        else if (t2 != RuneType.None
                            && s1 - s2 < DrawingConfig.RuneAmbiguityMargin + 0.03f)
                            _confusable.Add(PairKey(type, t2));
                    }
            }
            InkChamfer.Invalidate();
            Debug.Log($"[RuneLibrary] {ShortName(type)}: sample pool = {kept.Count} drawing(s)" +
                (kept.Count == 0 ? " (synthetic seed shape takes over)" : ""));
            return kept.Count;
        }

        static List<SavedStroke> ToSavedStrokes(List<List<Vector2>> strokes)
        {
            var outp = new List<SavedStroke>(strokes.Count);
            foreach (var s in strokes) outp.Add(new SavedStroke { points = new List<Vector2>(s) });
            return outp;
        }

        static bool SetTemplateInternal(RuneType type, List<List<Vector2>> rawStrokes, bool append = false)
        {
            var cloud = PointCloudRecognizer.Normalize(rawStrokes);
            if (cloud == null) return false;
            // templates normalize exactly like drawings: end shape, not pen lifts
            var stitched = StitchStrokes(ToReadOnly(rawStrokes));
            var sentences = ChainCodeRecognizer.EncodeAll(stitched);
            float elongation = ChainCodeRecognizer.Elongation(stitched);
            var feel = Fingerprint(stitched);
            var existing = _entries.Find(e => e.Type == type);
            if (append && existing != null)
            {
                // one more sample of his hand joins the pool
                if (existing.Variants.Count < MaxSamples - 1)
                    existing.Variants.Add(new Entry
                    {
                        Type = type, Cloud = cloud, Sentences = sentences,
                        Elongation = elongation, Feel = feel
                    });
                return true;
            }
            if (existing != null)
            {
                existing.Cloud = cloud;
                existing.Sentences = sentences;
                existing.Elongation = elongation;
                existing.Feel = feel;
                existing.Variants.Clear(); // fresh identity: the pool restates itself
            }
            else _entries.Add(new Entry
            {
                Type = type, Cloud = cloud, Sentences = sentences,
                Elongation = elongation, Feel = feel
            });
            return true;
        }

        static List<IReadOnlyList<Vector2>> ToReadOnly(List<List<Vector2>> strokes)
        {
            var ro = new List<IReadOnlyList<Vector2>>(strokes.Count);
            foreach (var s in strokes) ro.Add(s);
            return ro;
        }

        // ---- persistence ----

        static SavedTemplateSet _saved;

        static void LoadRecorded()
        {
            _saved = new SavedTemplateSet();
            try
            {
                if (!File.Exists(SavePath)) return;
                _saved = JsonUtility.FromJson<SavedTemplateSet>(File.ReadAllText(SavePath)) ?? new SavedTemplateSet();
                if (_saved.version != GlyphSetVersion)
                {
                    Debug.Log($"[RuneLibrary] Discarding recorded templates from glyph set v{_saved.version} (current v{GlyphSetVersion})");
                    _saved = new SavedTemplateSet { version = GlyphSetVersion };
                    return;
                }
                int loaded = 0, totalSamples = 0;
                foreach (var t in _saved.items)
                {
                    // EVERY wall drawing becomes a matcher variant (Marko's
                    // studio): oldest first, first usable one replaces the
                    // seed shape, the rest append to the pool
                    var all = new List<List<List<Vector2>>>();
                    if (t.older != null)
                        foreach (var s in t.older)
                        {
                            var st = SampleStrokes(s?.strokes);
                            if (st.Count > 0) all.Add(st);
                        }
                    var newest = ToStrokeLists(t);
                    if (newest.Count > 0) all.Add(newest);

                    bool first = true;
                    int used = 0;
                    foreach (var sample in all)
                    {
                        if (PointCount(sample) < MinTemplatePoints) continue;
                        if (SetTemplateInternal((RuneType)t.rune, sample, append: !first))
                        {
                            first = false;
                            used++;
                        }
                    }
                    if (used > 0) { loaded++; totalSamples += used; }
                    else if (all.Count > 0)
                        Debug.LogWarning($"[RuneLibrary] {ShortName((RuneType)t.rune)}: all {all.Count} saved drawing(s) too sparse — seed shape recognizes instead. Draw larger/slower on its wall.");
                }
                Debug.Log($"[RuneLibrary] Loaded {loaded} rune(s), {totalSamples} handwriting sample(s) from {SavePath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RuneLibrary] Failed to load recorded templates: {e.Message}");
            }
        }

        static List<List<Vector2>> ToStrokeLists(SavedTemplate t)
        {
            var result = new List<List<Vector2>>();
            if (t.strokes != null && t.strokes.Count > 0)
            {
                foreach (var s in t.strokes)
                    if (s != null && s.points != null && s.points.Count >= 2)
                        result.Add(s.points);
            }
            else if (t.points != null && t.points.Count >= 2)
            {
                result.Add(t.points); // legacy single-stroke recording
            }
            return result;
        }

        // (per-press template saving removed with the F-keys — ReplaceSamples
        // above is the one write path, driven by the Rune Studio walls)

        public static void DeleteRecordings()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch { }
            _entries = null; // force re-init from defaults
        }

        // ---- synthesized default glyphs (y-up, arbitrary units), one stroke each ----
        // Rough approximations of the sketch alphabet; recording real hand-drawn
        // templates (F1-F12) is always more accurate. Runes must stay OPEN — a
        // clear gap between start and end — or they close into a seal instead.
        static Dictionary<RuneType, List<Vector2>> DefaultGlyphs()
        {
            List<Vector2> P(params float[] xy)
            {
                var list = new List<Vector2>(xy.Length / 2);
                for (int i = 0; i < xy.Length; i += 2) list.Add(new Vector2(xy[i], xy[i + 1]));
                return list;
            }

            // THE ORIGINAL ALPHABET (v6 — Marko's final pick, restored verbatim
            // from v1). These are STAMP TEMPLATES: the player sketches, chooses,
            // and this exact shape appears as perfect ink. Must stay OPEN (a
            // closed shape would read as a seal).
            return new Dictionary<RuneType, List<Vector2>>
            {
                // tall jagged triangle, open at the base
                { RuneType.HeatUp, P(30,20, 52,88, 58,60, 44,60, 80,16, 45,14) },
                // squat flat zigzag
                { RuneType.HeatDown, P(15,25, 70,45, 45,40, 85,18, 30,12) },
                // wide bracket, open at the bottom, tick inward
                { RuneType.StateSolid, P(15,35, 15,65, 85,65, 85,32, 70,32) },
                // wide bracket, open at the top, tick inward
                { RuneType.StateLiquid, P(15,65, 15,35, 85,35, 85,68, 70,68) },
                // four-point star, drawn around, left open
                { RuneType.LuminanceUp, P(8,55, 38,62, 52,88, 64,62, 94,56, 72,40, 80,12, 52,32, 24,10, 30,40) },
                // collapsed star / wide double-V
                { RuneType.LuminanceDown, P(8,70, 35,25, 52,52, 72,22, 92,65) },
                // long slope up-right, drop, short base back
                { RuneType.StickyUp, P(5,20, 70,75, 72,30, 30,25) },
                // mirrored
                { RuneType.StickyDown, P(95,20, 30,78, 28,30, 70,25) },
                // arrow: shaft up, barb left, back to tip, barb right
                { RuneType.DirectionAway, P(50,10, 50,80, 35,60, 50,80, 65,60) },
                // Y: stem up, branch left, back to fork, branch right
                { RuneType.DirectionToward, P(50,10, 50,50, 30,80, 50,50, 70,80) },
                // small square bracket open at the bottom
                { RuneType.DensityUp, P(25,25, 25,70, 75,70, 75,25, 58,25) },
                // small square bracket open at the top
                { RuneType.DensityDown, P(25,75, 25,30, 75,30, 75,75, 58,75) },
            };
        }
    }
}
