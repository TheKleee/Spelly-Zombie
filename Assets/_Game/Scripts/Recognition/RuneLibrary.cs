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

    /// One collectable card = both directions of the pair (the Heat card
    /// teaches heat-up AND heat-down at once).
    public enum RuneCardType
    {
        Heat, State, Luminance, Sticky, Direction, Density
    }

    /// One turn-sequence descriptor per rune (see RuneGraph): a rune reads
    /// the same at any angle and size but never as its mirror. Pen lifts are
    /// stitched away before anything is measured, so stroke count and order
    /// don't matter. Ships with synthesized seed glyphs; drawings on a rune's
    /// Rune Studio wall replace them.
    public static class RuneLibrary
    {
        class Entry
        {
            public RuneType Type;
            /// Built once by SetTemplateInternal and cached for the pool's
            /// life; never re-derived per match.
            public RuneGraph Graph;

            List<List<Vector2>> _stitched; // held only until the sentences build
            List<byte[]> _sentences;

            /// The stitched paths this template was built from; Sentences
            /// encode from these on first demand.
            public void SetSource(List<List<Vector2>> stitched)
            {
                _stitched = stitched;
                _sentences = null;
            }

            /// Compound sigils only (see ClassifyCompound); built lazily on
            /// first use. No vote on which rune a single glyph is.
            public List<byte[]> Sentences
            {
                get
                {
                    if (_sentences == null && _stitched != null)
                    {
                        _sentences = ChainCodeRecognizer.EncodeAll(_stitched);
                        _stitched = null;
                    }
                    return _sentences;
                }
            }

            // every drawing on a rune's wall is a variant; scoring keeps the
            // best match across all of them
            public readonly List<Entry> Variants = new List<Entry>();
        }

        /// Bumped whenever the template pool changes; readers compare it and
        /// drop stale recognition caches.
        public static int PoolGeneration { get; private set; }

        /// Strokes whose endpoints meet are stitched into continuous paths
        /// before any shape math runs; the corner between two stitched lines
        /// counts as a corner, so pen-lift count never matters.
        static List<List<Vector2>> StitchStrokes(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            var paths = new List<List<Vector2>>();
            foreach (var s in strokes)
                if (s != null && s.Count >= 2) paths.Add(new List<Vector2>(s));
            if (paths.Count == 0) return paths;

            // stitch at 6% of the drawing's own Extent (point-set diameter,
            // never a world-axis box) so size never decides
            float stitchDist = RuneGraph.Extent(paths) * 0.06f;
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

            return Denoise(paths, stitchDist);
        }

        /// Straighten each path (Douglas-Peucker) with a scale-relative
        /// epsilon, collapsing hand wobble that would otherwise read as fake
        /// corners.
        static List<List<Vector2>> Denoise(List<List<Vector2>> paths, float scale)
        {
            if (paths.Count == 0) return paths;

            // no stroke is ever deleted for being short (barbs and LIGHT rays
            // ARE short strokes); only wobble within a line is noise
            float eps = scale * 0.10f;
            var outp = new List<List<Vector2>>(paths.Count);
            foreach (var p in paths)
            {
                var s = RuneGraph.Simplify(p, eps);
                outp.Add(s.Count >= 2 ? s : p);
            }
            return outp;
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
            // older samples, oldest first; capped at MaxSamples total
            public List<SavedSample> older = new List<SavedSample>();
        }

        [Serializable]
        class SavedTemplateSet
        {
            public int version;
            public List<SavedTemplate> items = new List<SavedTemplate>();
            /// Cached audit result: the rune pairs whose drawings read alike.
            /// Persisted because the audit is O(samples²) - 26-41s on a
            /// 204-sample library.
            public List<int> confusable = new List<int>();
            /// Sample count the cache was built from; a mismatch means stale
            /// and forces one re-audit.
            public int auditedCount = -1;
            /// Matcher version that produced the cache; a mismatch forces one
            /// re-audit (absent in old files, so it reads 0).
            public int matcher;
        }

        /// Bump when the default glyph alphabet changes - stale recordings from
        /// an older alphabet are discarded instead of shadowing the new shapes.
        const int GlyphSetVersion = 6;

        /// Bump when the scoring changes shape. Invalidates only the audit
        /// cache, never a recording. 1 = segment-graph matcher,
        /// 2 = signed-turn-sequence matcher.
        const int MatcherVersion = 2;

        static List<Entry> _entries;
        static string SavePath => Path.Combine(Application.persistentDataPath, "sz_rune_templates.json");

        // ---- unlocks: per-owner, in memory only (see Grimoire) ----

        /// Graybox switch: when true everything is drawable by everyone (all runes
        /// unlocked, no starting-rune picker) - flip on only for combo testing.
        public static bool AllRunesUnlockedForTesting = false;

        /// Convenience: unlock a card for the LOCAL player (pickups use this).
        public static void UnlockCard(RuneCardType card) => Grimoire.Unlock(Grimoire.LocalPlayerId, card);

        /// The card gate applies only during a run or in the selected map
        /// scene; lobby, menu and sandboxes are free practice grounds.
        /// Public: the rune chooser and grimoire display follow the same rule.
        public static bool RestrictedArena =>
            RoundDirector.RunActive
            || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == MatchLobby.SelectedMap;

        /// The acolyte kit, the one canonical copy; pages and recognition both
        /// read this.
        public static readonly RuneType[] AcolyteKit =
        {
            RuneType.StateSolid, RuneType.StateLiquid,
            RuneType.DirectionAway, RuneType.DirectionToward
        };

        /// Nothing is free, lobby included: both sides own only what they
        /// earned. Wizards earn by absorbing, acolytes by deeds.
        public static bool IsUnlocked(int ownerId, RuneType type)
        {
            if (type == RuneType.None) return false;
            return AllRunesUnlockedForTesting || Grimoire.HasRune(ownerId, type);
        }

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
                case RuneCardType.Heat: return "Heat: the jagged flame heats; the flat zigzag chills.";
                case RuneCardType.State: return "State: bracket open at the bottom = SOLID; open at the top = LIQUID.";
                case RuneCardType.Luminance: return "Luminance: the star brightens; the collapsed star darkens.";
                case RuneCardType.Sticky: return "Sticky: the slope-hook grips; its mirror slides.";
                case RuneCardType.Direction: return "Direction: the arrow pushes the way you drew it; the Y pulls.";
                default: return "Density: small bracket open-down compresses; open-up spreads.";
            }
        }

        /// Compound sigils: one continuous scribble can be several runes - its
        /// direction sentence is parsed like a word, and consecutive chunks
        /// that each read as a rune all fire. Returns the parts (≥2) or an
        /// empty list.
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
                // compound words are 9-24 letters; shorter scribbles are one
                // glyph's business
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
                            if (sc < 0.7f) continue; // spans have no fingerprint guard
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

        /// Below this a recording can't describe a shape and misreads angular
        /// drawings, so it never becomes a template (the seed shape recognizes
        /// instead). It is NOT deleted - the ink stays on the wall and in the
        /// file (see ReplaceSamples).
        const int MinTemplatePoints = 12;

        static int PointCount(List<List<Vector2>> strokes)
        {
            int n = 0;
            foreach (var s in strokes) n += s.Count;
            return n;
        }

        /// The player's recorded strokes for a rune, raw as drawn - null when
        /// nothing usable was recorded. Displays prefer these. Sparse
        /// recordings don't count (see MinTemplatePoints).
        public static List<List<Vector2>> RecordedStrokes(RuneType type)
        {
            Init();
            if (_saved == null || _saved.items == null) return null;
            var item = _saved.items.Find(i => i.rune == (int)type);
            if (item == null) return null;
            // newest USABLE sample: the newest can be sparse, so walk the
            // older ones too
            var strokes = ToStrokeLists(item);
            if (strokes.Count > 0 && PointCount(strokes) >= MinTemplatePoints) return strokes;
            if (item.older != null)
                for (int i = item.older.Count - 1; i >= 0; i--)
                {
                    var older = SampleStrokes(item.older[i]?.strokes);
                    if (older.Count > 0 && PointCount(older) >= MinTemplatePoints) return older;
                }
            return null;
        }

        /// Player-facing name is the emoji; ShortName's English words are for
        /// dev console logs only.
        public static string Icon(RuneType r)
        {
            switch (r)
            {
                case RuneType.HeatUp: return "🔥";
                // bare U+2744, no U+FE0F variation selector - TMP has no glyph
                // for the selector and draws a missing-box beside it
                case RuneType.HeatDown: return "❄";
                case RuneType.StateSolid: return "🗿";
                case RuneType.StateLiquid: return "💦";
                case RuneType.LuminanceUp: return "🌞";
                case RuneType.LuminanceDown: return "🌚";
                case RuneType.StickyUp: return "🍯";
                case RuneType.StickyDown: return "🍌";
                case RuneType.DirectionAway: return "🚀";
                case RuneType.DirectionToward: return "🧲";
                case RuneType.DensityUp: return "🤏";
                case RuneType.DensityDown: return "💨";
                default: return "?";
            }
        }

        /// Acolytes see zombie/skull for Solid/Liquid. The recognizer is
        /// untouched - it still reads the shape as StateSolid/StateLiquid;
        /// only what the acolyte's book calls it changes. New icons = one Noto
        /// png in Assets/_Game/Fonts/sz-emoji (see EmojiGridBuilder).
        public static string IconFor(RuneType r, int owner)
        {
            if (!Sides.IsAcolyte(owner)) return Icon(r);
            switch (r)
            {
                case RuneType.StateSolid: return "🧟";   // U+1F9DF
                case RuneType.StateLiquid: return "💀";  // U+1F480
                default: return Icon(r);
            }
        }

        public static string IconInlineFor(RuneType r, int owner) =>
            !Sides.IsAcolyte(owner) ? IconInline(r) : IconFor(r, owner);

        public static string ShortNameFor(RuneType r, int owner)
        {
            if (!Sides.IsAcolyte(owner)) return ShortName(r);
            switch (r)
            {
                case RuneType.StateSolid: return "ZOMBIE";
                case RuneType.StateLiquid: return "POISON";
                default: return ShortName(r);
            }
        }

        /// Inline form: the icon inside a line of text. Alignment is the
        /// sprite asset's job, not this string's - with correct metrics this
        /// returns the bare icon. The knobs are only a trim: at 0 / 100 no
        /// tags are emitted; set them in sz_tuning.json if a font ever needs
        /// a nudge.
        public static string IconInline(RuneType r)
        {
            string icon = Icon(r);
            bool lift = Mathf.Abs(DrawingConfig.RuneIconLift) > 0.001f;
            bool scale = Mathf.Abs(DrawingConfig.RuneIconScale - 100f) > 0.5f;
            if (!lift && !scale) return icon;
            if (lift) icon = $"<voffset={DrawingConfig.RuneIconLift:0.##}em>{icon}</voffset>";
            if (scale) icon = $"<size={DrawingConfig.RuneIconScale:0}%>{icon}</size>";
            return icon;
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

        /// True while the live matcher holds samples learned this round that
        /// were never saved - so we know a rebuild is needed when it ends.
        static bool _roundLearned;

        /// Round end: rebuild the matcher from the saved file, dropping
        /// everything learned during play.
        public static void ForgetRoundLearning()
        {
            if (!_roundLearned) return;
            _roundLearned = false;
            _entries = null;      // force a clean rebuild from _saved
            Init();
        }

        /// Trim every rune's recorded pool down to its first (oldest) drawing
        /// and write the file.
        public static void KeepOnlyFirstSample()
        {
            Init();
            if (_saved?.items == null) return;
            int trimmed = 0;
            foreach (var item in _saved.items)
            {
                // `strokes` holds the newest sample, `older` the earlier ones
                // in order, so older[0] is the first recording
                if (item.older != null && item.older.Count > 0)
                {
                    var first = item.older[0];
                    if (first?.strokes != null && first.strokes.Count > 0)
                        item.strokes = first.strokes;
                    item.older = new List<SavedSample>();
                    trimmed++;
                }
                item.points = new List<Vector2>(); // legacy field, kept empty
            }
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(_saved)); }
            catch (Exception e) { Debug.LogWarning($"[RuneLibrary] Failed to save: {e.Message}"); }
            _entries = null;
            Init();
            Debug.Log($"[RuneLibrary] Trimmed {trimmed} rune(s) down to their first drawing.");
        }

        static void Init()
        {
            if (_entries != null) return;
            _entries = new List<Entry>();
            foreach (var pair in DefaultGlyphs())
                SetTemplateInternal(pair.Key, new List<List<Vector2>> { pair.Value });
            LoadRecorded();

            // the audit is O(samples²) (26-41s on a full library), so its
            // confusable set is cached in the save file and recomputed only
            // when the library changes; re-run any time via Spelly Zombie >
            // Runes - Re-audit templates
            int have = CountSamples();
            if (_saved != null && _saved.confusable != null
                && _saved.auditedCount == have
                && _saved.matcher == MatcherVersion) // a stale matcher's cache exempts the wrong pairs
                _confusable = new HashSet<int>(_saved.confusable);
            else
                AuditTemplates(); // library or matcher changed (or first run) - pay it once

            PoolGeneration++; // the pool is new: stale recognition caches must drop
        }

        /// Total recorded samples across every rune - the cache's staleness key.
        static int CountSamples()
        {
            if (_saved?.items == null) return -1;
            int n = 0;
            foreach (var it in _saved.items)
            {
                if (it.strokes != null && it.strokes.Count > 0) n++;
                if (it.older != null) n += it.older.Count;
            }
            return n;
        }

        /// Store the audit result so the next load reads it instead of
        /// recomputing 40,000 point-cloud matches.
        static void SaveAuditCache()
        {
            if (_saved == null) return;
            _saved.confusable = _confusable != null
                ? new List<int>(_confusable) : new List<int>();
            _saved.auditedCount = CountSamples();
            _saved.matcher = MatcherVersion;
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(_saved)); }
            catch (Exception e) { Debug.LogWarning($"[RuneLibrary] audit cache not saved: {e.Message}"); }
        }

        /// Run the template audit by hand (editor menu).
        public static void ReAudit()
        {
            Init();
            AuditTemplates();
            Debug.Log("[RuneLibrary] Template audit finished; result cached.");
        }

        /// Pay the whole recognition bill at scene load: load every recorded
        /// sample, audit the pools, then push throwaway glyphs through the
        /// full scoring path (ownerId null scores the entire library) so code
        /// and buffers are hot before anyone draws.
        public static void Warm()
        {
            Init();
            // two pokes, two code paths: a zigzag warms the turn-sentence
            // path, a Y warms the stem-and-limb path (the family gate returns
            // early across families, so one alone leaves the other cold)
            var zigzag = new List<IReadOnlyList<Vector2>>
            {
                new List<Vector2>
                {
                    new Vector2(0f, 0f), new Vector2(0.3f, 0.5f),
                    new Vector2(0.6f, 0f), new Vector2(0.9f, 0.5f)
                }
            };
            Top2(null, zigzag);

            var fork = new List<IReadOnlyList<Vector2>>
            {
                new List<Vector2> { new Vector2(0.5f, 0f), new Vector2(0.5f, 0.6f) },
                new List<Vector2> { new Vector2(0.2f, 0.9f), new Vector2(0.5f, 0.6f), new Vector2(0.8f, 0.9f) }
            };
            Top2(null, fork);
        }

        /// Classify a glyph (raw 2D strokes in a shared frame) against the
        /// runes the OWNER has unlocked - the seal's owner is whoever
        /// completed it. A rune reads at any angle and size; its mirror reads
        /// as the opposite rune. Ambiguity guard: two runes scoring within
        /// RuneAmbiguityMargin fizzle, never misfire.
        public static (RuneType type, float score) Classify(int ownerId, IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            Init();
            var (t1, s1, t2, s2) = Top2(ownerId, rawStrokes);
            // a top at or above RuneTrustScore is trusted outright; the
            // coin-flip fizzle referees only weak tops
            bool ambiguous = t1 != RuneType.None && t2 != RuneType.None && t2 != t1
                && s1 < DrawingConfig.RuneTrustScore
                && s1 - s2 < DrawingConfig.RuneAmbiguityMargin
                && s2 >= DrawingConfig.MinRuneScore
                && (_confusable == null || !_confusable.Contains(PairKey(t1, t2)));

            RuneType rune = t1 == RuneType.None || ambiguous ? RuneType.None : t1;
            float score = t1 == RuneType.None ? 0f : s1;
            bool hit = rune != RuneType.None && score >= DrawingConfig.MinRuneScore;

            // costs a stack-traced Editor log plus a CSV append per classify;
            // flip LogClassifies only while debugging recognition
            if (LogClassifies)
            {
                int nStrokes = 0, nPts = 0;
                foreach (var s in rawStrokes) { nStrokes++; nPts += s.Count; }
                Debug.Log($"[SpellyZombie] CLASSIFY {(hit ? "HIT" : "fizzle")} (segment graph): " +
                    $"input {nStrokes} strokes/{nPts} pts, top {ShortName(t1)} {s1:0.00}, " +
                    $"next {ShortName(t2)} {s2:0.00}{(ambiguous ? " AMBIGUOUS" : "")} " +
                    $"(floor {DrawingConfig.MinRuneScore:0.00})");
                InkChamfer.DumpClassify(rawStrokes,
                    $"{ShortName(t1)} {s1:0.00} / {ShortName(t2)} {s2:0.00}", hit);
            }

            return (rune, score);
        }

        /// The drawing becomes one descriptor (signed corners, or stem plus
        /// limbs for arrow/Y) and every unlocked rune's whole sample pool is
        /// scored against it. Returns the two best DIFFERENT runes.
        static (RuneType t1, float s1, RuneType t2, float s2) Top2(int? ownerId,
            IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            // stitch to the end shape first, then measure; every matcher reads
            // the same stitched paths
            var stitched = StitchStrokes(rawStrokes);
            var graph = RuneGraph.Build(stitched);
            if (graph == null) return (RuneType.None, 0f, RuneType.None, 0f);

            // no corners, no rune: a bare line matches nothing (the graph
            // owns this rule)
            if (graph.BareLine) return (RuneType.None, 0f, RuneType.None, 0f);

            return Top2Descriptor(ownerId, graph, null);
        }

        /// The one scoring loop, over an already-built descriptor. `skip`
        /// keeps a template from scoring against itself (leave-one-out
        /// audits). Every rune is scored because the ambiguity guard needs the
        /// runner-up; ownerId gates by unlocks, null scores the full library.
        static (RuneType t1, float s1, RuneType t2, float s2) Top2Descriptor(int? ownerId,
            RuneGraph graph, Entry skip)
        {
            RuneType bestType = RuneType.None, secondType = RuneType.None;
            float bestScore = 0f, secondScore = 0f;
            foreach (var e in _entries)
            {
                if (ownerId.HasValue && !IsUnlocked(ownerId.Value, e.Type)) continue;
                // nearest-of-many: the drawing matches whichever of this
                // rune's samples it most resembles
                float score = e == skip ? 0f : RuneGraph.Compare(graph, e.Graph);
                foreach (var v in e.Variants)
                {
                    if (v == skip) continue;
                    float s = RuneGraph.Compare(graph, v.Graph);
                    if (s > score) score = s;
                }
                if (score > bestScore)
                {
                    secondType = bestType; secondScore = bestScore;
                    bestType = e.Type; bestScore = score;
                }
                else if (score > secondScore)
                {
                    secondType = e.Type; secondScore = score;
                }
            }
            return (bestType, bestScore, secondType, secondScore);
        }

        // ---- template health ----

        /// Per-classify logging + CSV dump; off by default (costs a log and a
        /// file append on every recognition).
        public static bool LogClassifies = false;

        static HashSet<int> _confusable; // rune pairs whose templates read alike
        static int PairKey(RuneType a, RuneType b)
        {
            int x = (int)a, y = (int)b;
            return x < y ? (x << 8) | y : (y << 8) | x;
        }

        /// Console health check: which runes still use default shapes, and
        /// which two templates read alike. Alike pairs are remembered so the
        /// ambiguity guard doesn't render them uncastable.
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
                // sparse templates score weakly even against their own
                // drawings, so a richer rune's template can outbid them
                if (pts < 12)
                    Debug.LogWarning($"[RuneLibrary] AUDIT: the {ShortName(r)} template is only {pts} points. Sparse recordings misread easily; re-record it drawing larger and slower.");
            }
            if (seedRunes.Count > 0)
                Debug.LogWarning($"[RuneLibrary] AUDIT: {seedRunes.Count} rune(s) still use DEFAULT shapes, not your handwriting: {string.Join(", ", seedRunes)}. Draw each and press its F-key to record.");
            else
                Debug.Log("[RuneLibrary] AUDIT: all 12 runes use YOUR recorded handwriting.");

            // leave-one-out: each sample scores against every template except
            // itself (self-match is 1.00 by definition and reports nothing).
            // A rune with only one drawing cannot be cross-checked at all.
            var entangled = new List<string>();
            var lonely = new List<string>();
            var crossReads = new List<string>(); // summarized in ONE line at the end
            foreach (var e in _entries)
            {
                int pool = 1 + e.Variants.Count;
                if (pool < 2) { lonely.Add(ShortName(e.Type)); continue; }

                for (int i = 0; i < pool; i++)
                {
                    var self = i == 0 ? e : e.Variants[i - 1];
                    if (self.Graph == null || self.Graph.BareLine) continue;
                    var (t1, s1, t2, s2) = Top2Descriptor(null, self.Graph, self);
                    if (t1 == RuneType.None) continue;

                    if (t1 != e.Type)
                    {
                        // collected, not logged per sample
                        crossReads.Add($"{ShortName(e.Type)} reads {ShortName(t1)} {s1:F2}");
                        if (_confusable.Add(PairKey(e.Type, t1)))
                            entangled.Add($"{ShortName(e.Type)}~{ShortName(t1)}");
                    }
                    // a near-tie marks the pair entangled; entangled pairs are
                    // exempt from the ambiguity guard (top score wins), and
                    // the console names them
                    else if (t2 != RuneType.None
                             && s1 - s2 < DrawingConfig.RuneAmbiguityMargin + 0.03f)
                    {
                        if (_confusable.Add(PairKey(e.Type, t2)))
                            entangled.Add($"{ShortName(e.Type)}~{ShortName(t2)}");
                    }
                }
            }
            if (crossReads.Count > 0)
                Debug.LogWarning($"[RuneLibrary] AUDIT: {crossReads.Count} drawing(s) read as the WRONG rune against the rest of the pool "
                    + $"({string.Join(", ", crossReads.GetRange(0, Mathf.Min(4, crossReads.Count)))}{(crossReads.Count > 4 ? ", ..." : "")}). "
                    + "Top score decides between entangled pairs; clean a wall and re-audit via Spelly Zombie > Runes.");
            if (lonely.Count > 0)
                Debug.LogWarning($"[RuneLibrary] AUDIT: {lonely.Count} rune(s) have only ONE usable drawing, so nothing can cross-check them: {string.Join(", ", lonely)}. Draw each a second time on its wall.");
            if (entangled.Count > 0)
                Debug.Log($"[RuneLibrary] AUDIT: pairs entangled in your handwriting (top score decides between them): {string.Join(", ", entangled)}");

            SaveAuditCache(); // never pay for this twice
        }

        /// Everything drawn on a wall is saved; this bound only guards against
        /// a runaway file.
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

        /// The Rune Studio save: a wall's ink IS the rune's sample pool - this
        /// replaces the whole pool with the wall snapshot; an empty wall
        /// clears the rune back to its seed shape. Returns how many were kept.
        /// EVERY drawing is kept however small; sparse ones just aren't used
        /// as templates (see MinTemplatePoints). The ink is never deleted.
        public static int ReplaceSamples(RuneType type, List<List<List<Vector2>>> samples)
        {
            Init();
            var kept = new List<List<List<Vector2>>>();
            int sparse = 0;
            foreach (var s in samples)
            {
                if (s == null || s.Count == 0) continue;
                if (PointCount(s) < MinTemplatePoints) sparse++;
                kept.Add(s);
                if (kept.Count == MaxSamples) break;
            }
            if (sparse > 0)
                Debug.Log($"[RuneLibrary] {ShortName(type)}: {sparse} wall drawing(s) too sparse to teach the matcher. Kept on the wall, not used as templates (draw larger/slower to teach them).");

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
                item.points = new List<Vector2>(); // legacy field, kept empty
                item.older = new List<SavedSample>();
                for (int i = 0; i < kept.Count - 1; i++)
                    item.older.Add(new SavedSample { strokes = ToSavedStrokes(kept[i]) });
                item.strokes = ToSavedStrokes(kept[kept.Count - 1]);
            }
            _saved.version = GlyphSetVersion;
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(_saved)); }
            catch (Exception e) { Debug.LogWarning($"[RuneLibrary] Failed to save samples: {e.Message}"); }

            // in-place matcher update: only THIS rune's variants rebuild; the
            // template audit stays a session-start affair
            if (_entries != null)
            {
                // flip `first` only on success: a false from
                // SetTemplateInternal means no descriptor was built, and the
                // next usable sample must replace, not append
                bool first = true;
                foreach (var sample in kept)
                {
                    if (PointCount(sample) < MinTemplatePoints) continue; // kept on the wall, not taught
                    if (SetTemplateInternal(type, sample, append: !first)) first = false;
                }
                if (first)
                {
                    // nothing on the wall was usable as a template (an empty
                    // wall, or only sparse drawings) - the seed shape takes over
                    var poly = GlyphPolyline(type);
                    if (poly != null)
                        SetTemplateInternal(type, new List<List<Vector2>> { poly });
                }

                // refresh THIS rune's entanglements immediately, leave-one-out
                // (each cached descriptor against every template but itself)
                var entry = _entries.Find(x => x.Type == type);
                if (_confusable != null && entry != null)
                {
                    int pool = 1 + entry.Variants.Count;
                    for (int i = 0; i < pool && pool >= 2; i++)
                    {
                        var self = i == 0 ? entry : entry.Variants[i - 1];
                        if (self.Graph == null || self.Graph.BareLine) continue;
                        var (t1, s1, t2, s2) = Top2Descriptor(null, self.Graph, self);
                        if (t1 == RuneType.None) continue;
                        if (t1 != type) _confusable.Add(PairKey(type, t1));
                        else if (t2 != RuneType.None
                            && s1 - s2 < DrawingConfig.RuneAmbiguityMargin + 0.03f)
                            _confusable.Add(PairKey(type, t2));
                    }
                }
            }
            InkChamfer.Invalidate();
            Debug.Log($"[RuneLibrary] {ShortName(type)}: sample pool = {kept.Count} drawing(s)" +
                (kept.Count == 0 ? " (synthetic seed shape takes over)" : ""));
            return kept.Count;
        }

        /// Append a played drawing to the rune's pool; a full pool rolls the
        /// oldest sample out. Returns false for drawings too sparse to teach.
        /// Quiet (the in-game silent learn) joins the live matcher only - no
        /// rebuild, no confusable refresh, no file write ever; it lasts one
        /// round. A full pool declines quiet samples.
        public static bool AddSample(RuneType type, List<List<Vector2>> sample, bool quiet = false)
        {
            Init();
            if (type == RuneType.None || sample == null
                || PointCount(sample) < MinTemplatePoints) return false;

            if (quiet)
            {
                // round-only: a quiet sample joins the live matcher and never
                // touches _saved or the file; only Rune Studio persists
                var e = _entries.Find(x => x.Type == type);
                if (e == null || e.Variants.Count >= MaxSamples - 1) return false;
                if (!SetTemplateInternal(type, sample, append: true)) return false;
                _roundLearned = true;
                return true;
            }

            var pool = new List<List<List<Vector2>>>();
            var item = _saved.items.Find(i => i.rune == (int)type);
            if (item != null)
            {
                if (item.older != null)
                    foreach (var old in item.older)
                    {
                        var s = SampleStrokes(old.strokes);
                        if (s.Count > 0) pool.Add(s);
                    }
                var newest = SampleStrokes(item.strokes);
                if (newest.Count > 0) pool.Add(newest);
            }
            pool.Add(sample);
            while (pool.Count > MaxSamples) pool.RemoveAt(0); // the oldest rolls out
            return ReplaceSamples(type, pool) > 0;
        }

        static List<SavedStroke> ToSavedStrokes(List<List<Vector2>> strokes)
        {
            var outp = new List<SavedStroke>(strokes.Count);
            foreach (var s in strokes) outp.Add(new SavedStroke { points = new List<Vector2>(s) });
            return outp;
        }

        /// The one place a descriptor is built; every load, record and learn
        /// path funnels through here and the result is cached for the pool's
        /// life. Returns false only for a drawing that cannot teach (no graph,
        /// or a bare line that can never match) - callers read false as "this
        /// template does not exist" and let the next sample take over. The ink
        /// itself stays on the wall and in the file (see ReplaceSamples).
        static bool SetTemplateInternal(RuneType type, List<List<Vector2>> rawStrokes, bool append = false)
        {
            // templates normalize exactly like drawings: end shape, not pen lifts
            var stitched = StitchStrokes(ToReadOnly(rawStrokes));
            var graph = RuneGraph.Build(stitched);
            if (graph == null) return false;
            if (graph.BareLine)
            {
                Debug.LogWarning($"[RuneLibrary] {ShortName(type)}: a wall drawing straightens out to a bare line (no corners). It cannot match anything, so it is kept on the wall but not taught to the matcher. Redraw that one with its corners clearly bent.");
                return false;
            }
            var existing = _entries.Find(e => e.Type == type);
            PoolGeneration++; // the matcher's view of this rune just changed
            if (append && existing != null)
            {
                if (existing.Variants.Count < MaxSamples - 1)
                {
                    var variant = new Entry { Type = type, Graph = graph };
                    variant.SetSource(stitched); // sentences build lazily (compound path)
                    existing.Variants.Add(variant);
                }
                return true;
            }
            if (existing != null)
            {
                existing.Graph = graph;
                existing.SetSource(stitched);
                existing.Variants.Clear(); // non-append replaces the whole pool
            }
            else
            {
                var entry = new Entry { Type = type, Graph = graph };
                entry.SetSource(stitched);
                _entries.Add(entry);
            }
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
                    // every wall drawing becomes a matcher variant: oldest
                    // first, the first usable one replaces the seed shape,
                    // the rest append to the pool
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
                        Debug.LogWarning($"[RuneLibrary] {ShortName((RuneType)t.rune)}: all {all.Count} saved drawing(s) too sparse. Seed shape recognizes instead. Draw larger/slower on its wall.");
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

        // ReplaceSamples is the one write path, driven by the Rune Studio walls.

        // ---- synthesized default glyphs (y-up, arbitrary units), one stroke each ----
        // Runes must stay OPEN - a clear gap between start and end - or they
        // close into a seal instead.
        static Dictionary<RuneType, List<Vector2>> DefaultGlyphs()
        {
            List<Vector2> P(params float[] xy)
            {
                var list = new List<Vector2>(xy.Length / 2);
                for (int i = 0; i < xy.Length; i += 2) list.Add(new Vector2(xy[i], xy[i + 1]));
                return list;
            }

            // stamp templates: the player sketches, chooses, and this exact
            // shape appears as perfect ink
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
