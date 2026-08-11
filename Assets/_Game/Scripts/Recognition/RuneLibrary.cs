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

    /// Holds one TURN-SEQUENCE descriptor per rune (see RuneGraph — the signed
    /// sequence of corners along the rune's single line, so a rune reads the
    /// same drawn at ANY angle and at ANY size, but never reads as its mirror).
    /// Templates may be drawn in any number of strokes, in any order and
    /// direction — an arrow recorded as shaft + barbs matches an arrow drawn
    /// barbs-first, because pen lifts are stitched away before anything is
    /// measured. Ships with rough synthesized glyphs; every drawing on a rune's
    /// Rune Studio wall replaces them.
    public static class RuneLibrary
    {
        class Entry
        {
            public RuneType Type;
            /// THE SIGNAL. Built once per template here — SetTemplateInternal
            /// is the single funnel every load, record and learn path goes
            /// through, so this field IS the descriptor cache; nothing
            /// re-derives it per match.
            public RuneGraph Graph;

            List<List<Vector2>> _stitched; // held only until the sentences build
            List<byte[]> _sentences;

            /// The stitched paths this template was built from — Sentences
            /// encode from these on first demand.
            public void SetSource(List<List<Vector2>> stitched)
            {
                _stitched = stitched;
                _sentences = null;
            }

            /// COMPOUND SIGILS ONLY (see ClassifyCompound). Built LAZILY on the
            /// first compound parse: EncodeAll used to run for EVERY template on
            /// every load/save purely to feed a path that is unreachable while
            /// CompoundSigilsEnabled stays false — feature preserved, load-time
            /// cost gone. They get no vote on which rune a single glyph is — see
            /// the note above Top2Descriptor.
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

            // MULTI-TEMPLATE (Marko's studio walls): every drawing on a rune's
            // wall is a variant of HIS hand — the ensemble scores against all
            // of them and keeps the best. More samples = recognition converges
            // on how he actually draws, not one lucky snapshot.
            public readonly List<Entry> Variants = new List<Entry>();
        }

        /// BUMPED WHENEVER THE TEMPLATE POOL CHANGES. The recognition cache in
        /// RuneGlyph is keyed by stroke ids only, so after a wall save it used
        /// to hand back the PRE-SAVE verdict for unchanged ink — which in the
        /// Rune Studio test loop looks exactly like "the matcher ignored my
        /// drawing". Readers compare this and drop their cache.
        public static int PoolGeneration { get; private set; }

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
            if (paths.Count == 0) return paths;

            // SIZE MUST NOT MATTER (Marko: "the overall length of the runes
            // shouldn't matter, only their shape"): stitch at 6% of the drawing's
            // own Extent (point-set diameter, never a world-axis box) — every
            // fixed-metre or clamped version of this quietly let size decide.
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

        /// FORGIVE THE SMALL MISTAKES (Marko: "lines popping up even if they
        /// might be part of the line"). A hand-drawn rune is full of things
        /// that aren't features: a wobble that reads as a corner, an overshoot
        /// that reads as a new limb, a slip of the pen that leaves a stub.
        /// Every one of them invents a fake endpoint and a fake corner, and
        /// every shape feature downstream then measures the mistake instead of
        /// the rune. Two passes, both scale-relative so size never matters:
        ///   1. throw away strokes too short to be a limb
        ///   2. straighten each path (Douglas-Peucker), collapsing wobble into
        ///      the line it was always meant to be
        static List<List<Vector2>> Denoise(List<List<Vector2>> paths, float scale)
        {
            if (paths.Count == 0) return paths;

            // NO STROKE IS EVER DELETED (Marko's law: never delete a stroke for
            // being short — barbs and LIGHT rays ARE short strokes); only wobble
            // WITHIN a line is noise. One straightening rule: RuneGraph owns RDP.
            float eps = scale * 0.10f;
            var outp = new List<List<Vector2>>(paths.Count);
            foreach (var p in paths)
            {
                var s = RuneGraph.Simplify(p, eps);
                outp.Add(s.Count >= 2 ? s : p);
            }
            return outp;
        }

        // THE SHAPEFEEL SUITE IS GONE (Jul 31, the segment-graph swap).
        // Fingerprint / MeasureBranchAngle / ConnectedParts / ResampleStroke /
        // FeelPenalty all lived here: total turn, gap bearing, longest-run
        // fraction, branch lean, connected-part count. Every one of them was a
        // hand-rolled approximation of a question RuneGraph now answers
        // exactly, in the rune's OWN frame:
        //   longest-run fraction  -> RuneGraph.StemFrac
        //   branch lean           -> Limb.Angle (SIGNED, so mirrors separate)
        //   connected parts       -> node degrees + T-junction splitting
        //   gap bearing           -> measured against the WORLD, which is
        //                            exactly what Marko's law forbids. Deleted,
        //                            not ported.
        // Keeping them as a second opinion would have meant a world-frame
        // measurement quietly vetoing a correct rotation-invariant read.

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
            /// Cached result of the last template audit — the rune pairs whose
            /// drawings read alike. Persisted because RECOMPUTING IT IS THE
            /// SINGLE SLOWEST THING IN THE GAME: the audit is O(samples²) and
            /// was MEASURED at 26-41 SECONDS on a 204-sample library, against
            /// ~200ms for the same scene without it. It ran on every scene load.
            public List<int> confusable = new List<int>();
            /// How many samples the cache was built from — if the library grew
            /// or shrank, the cache is stale and the audit is re-run once.
            public int auditedCount = -1;
            /// WHICH MATCHER produced that cache. Sample count alone was not
            /// enough: swap the matcher and Init would load the $P-era
            /// confusable list unchanged and silently exempt the WRONG pairs
            /// from the ambiguity guard. Absent in old files, so it reads 0 and
            /// forces exactly one re-audit. Deliberately NOT done by bumping
            /// GlyphSetVersion — that would delete every one of Marko's
            /// recordings.
            public int matcher;
        }

        /// Bump when the default glyph alphabet changes — stale recordings from
        /// an older alphabet are discarded instead of shadowing the new shapes.
        const int GlyphSetVersion = 6; // v6 = the ORIGINAL alphabet restored (Marko's final pick)

        /// Bump when the SCORING changes shape. Only invalidates the audit
        /// cache; never touches a recording. 1 = the segment-graph matcher,
        /// 2 = the signed-turn-sequence matcher (Aug 1). The bump matters:
        /// without it Init would load the OLD matcher's `confusable` list and
        /// silently exempt the wrong pairs from the ambiguity guard — and the
        /// old list is empty anyway, because the audit that produced it was
        /// scoring every sample against itself.
        const int MatcherVersion = 2;

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
            && (AllRunesUnlockedForTesting || !RestrictedArena || Grimoire.HasRune(ownerId, type));

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
        /// Sparse recordings are treated as NOT RECORDED for MATCHING: they
        /// never become a template, and the seed shape recognizes instead until
        /// one is drawn properly. They are NOT deleted — his ink stays on his
        /// wall and in the file (see ReplaceSamples).
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
            // NEWEST USABLE, not "newest, or nothing". Sparse drawings now stay
            // on the wall instead of being deleted (see ReplaceSamples), so the
            // newest one can be a small scribble — that must not make the whole
            // rune report as un-recorded and hand the displays a seed shape when
            // his handwriting is sitting right there.
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

        // ScoreAll (the choose-and-stamp candidate list) is DELETED. It had
        // zero callers and duplicated Top2's scoring loop verbatim — a second
        // copy of the matcher wiring that nothing exercised and that would
        // silently rot. Top2 is the one scoring loop.

        /// Player-facing name = EMOJI (Marko: memeable, zero translation).
        /// ShortName's English words stay for dev console logs only.
        public static string Icon(RuneType r)
        {
            switch (r)
            {
                case RuneType.HeatUp: return "🔥";
                // BARE 2744, no U+FE0F variation selector — the selector has no
                // glyph, so TMP drew a missing-box beside the snowflake
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

        /// THE SAME GLYPH MEANS SOMETHING ELSE IN A CORRUPT HAND. Marko, Aug 6:
        /// "When acolyte is drawing the runes should not be recognized the same
        /// way as they are for the wizard. They should only recognize zombie icon
        /// instead of solid and poison instead of liquid."
        ///
        /// The recogniser is untouched: it still reads the shape as StateSolid.
        /// Only what the acolyte's book CALLS it changes, which is the same
        /// corrupt-ink-reinterprets rule that makes an arrow command the dead
        /// instead of shoving a crate.
        ///
        /// EmojiGrid rebuilds itself from Assets/_Game/Fonts/sz-emoji (see
        /// EmojiGridBuilder) — zombie and skull are in the atlas, and any new
        /// icon is one Noto png dropped in that folder. Art is Marko's call.
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

        /// INLINE form: the icon sitting INSIDE a line of text.
        ///
        /// Alignment is the SPRITE ASSET'S job, not this string's. EmojiGrid
        /// shipped with HorizontalBearingX -256 (half a glyph to the LEFT, so
        /// every icon collided with the word before it) and BearingY 256 (only
        /// half the glyph above the baseline, so half of every emoji hung
        /// BELOW it). Fixed in the asset to 0 and 462.3, matching Unity's own
        /// EmojiOne ratio of 0.90 x height. With correct metrics this returns
        /// the bare icon and adds NOTHING to the string.
        ///
        /// The knobs stay only as a trim: leave them at 0 / 100 and no tags are
        /// emitted at all. Set them in sz_tuning.json if a font's line spacing
        /// ever needs a nudge.
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
        /// were never saved — so we know a rebuild is needed when it ends.
        static bool _roundLearned;

        /// FORGET THIS ROUND'S HANDWRITING. Called when a round ends: the
        /// matcher is rebuilt from the saved file, dropping everything the
        /// game picked up while playing. Next round starts from your recorded
        /// drawings again, exactly as it did this one.
        public static void ForgetRoundLearning()
        {
            if (!_roundLearned) return;
            _roundLearned = false;
            _entries = null;      // force a clean rebuild from _saved
            Init();
        }

        /// KEEP ONLY THE FIRST DRAWING of every rune (Marko: "remove all but
        /// the first drawing on each wall"). Trims the recorded pools down to
        /// one sample each and writes the file.
        public static void KeepOnlyFirstSample()
        {
            Init();
            if (_saved?.items == null) return;
            int trimmed = 0;
            foreach (var item in _saved.items)
            {
                // the OLDEST recording is the first one you made; `strokes`
                // holds the newest, `older` the ones before it in order
                if (item.older != null && item.older.Count > 0)
                {
                    var first = item.older[0];
                    if (first?.strokes != null && first.strokes.Count > 0)
                        item.strokes = first.strokes;
                    item.older = new List<SavedSample>();
                    trimmed++;
                }
                item.points = new List<Vector2>(); // legacy field stays retired
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

            // THE AUDIT NO LONGER RUNS ON EVERY LOAD. It is O(samples²) and was
            // measured at 26-41 SECONDS with a full library — the entire scene
            // load time. Its only output is the `confusable` pair set, so that
            // is cached in the save file and only recomputed when the library
            // actually changes. Re-run it any time from
            // Spelly Zombie ▸ Runes — Re-audit templates.
            int have = CountSamples();
            if (_saved != null && _saved.confusable != null
                && _saved.auditedCount == have
                && _saved.matcher == MatcherVersion) // a $P-era cache exempts the WRONG pairs
                _confusable = new HashSet<int>(_saved.confusable);
            else
                AuditTemplates(); // library or matcher changed (or first run) — pay it once

            PoolGeneration++; // the pool is new: stale recognition caches must drop
        }

        /// Total recorded samples across every rune — the cache's staleness key.
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

        /// Run the health check by hand (editor menu). Everything it prints —
        /// "PULL reads as PUSH", "X and Y score within 0.12" — comes from here.
        public static void ReAudit()
        {
            Init();
            AuditTemplates();
            Debug.Log("[RuneLibrary] Template audit finished; result cached.");
        }

        /// Pay the whole recognition bill at SCENE LOAD, never on the first
        /// rune (Marko: the map hitched when the first drawing classified).
        /// Loads every recorded sample, audits the pools, then pushes one
        /// throwaway glyph through the full scoring path — ownerId null scores
        /// the ENTIRE library — so code and buffers are hot before anyone draws.
        public static void Warm()
        {
            Init();
            // TWO pokes, because there are two code paths. A zigzag exercises
            // the turn-sentence path (the ten single-line runes); a Y exercises
            // the stem-and-limb path (PUSH and PULL). The family gate in
            // RuneGraph.Compare returns early across families, so a zigzag alone
            // would leave the whole branched matcher cold and hand the first
            // arrow of the session the hitch this method exists to prevent.
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

        /// Classify a glyph (one or more raw 2D strokes in a shared frame)
        /// against the runes the given OWNER has unlocked — the seal's owner is
        /// whoever completed it, so zombie-closed seals read with zombie cards.
        ///
        /// THE TURN-SEQUENCE MATCHER (Marko's Aug 1 correction: "all of my
        /// runes are 1 long line except for push and pull… there's no
        /// difference with light and dark"). A rune IS its sequence of signed
        /// corners, so it reads the same drawn at any angle and any size, and
        /// its mirror reads as the opposite rune. "All shapes are distinct - I
        /// made them that way exactly because they can be flipped", so any
        /// cross-fire between two of them is a bug in RuneGraph, never a reason
        /// to re-record a glyph. The chamfer matcher stays in the project
        /// (InkChamfer), benched.
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

            // DIAGNOSTICS ARE OFF BY DEFAULT. These two lines ran on EVERY
            // classify: a stack-traced Editor log plus a CSV append that had
            // grown to 14MB and is never truncated. Flip LogClassifies when
            // you're actually debugging recognition.
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

        /// TURN-SEQUENCE scoring. The drawing becomes ONE descriptor — the
        /// signed corners along its line, or (for an arrow or a Y) a stem plus
        /// limbs — and every unlocked rune's whole sample pool is scored
        /// against it. Returns the two best DIFFERENT runes.
        static (RuneType t1, float s1, RuneType t2, float s2) Top2(int? ownerId,
            IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            // Pen lifts never matter: stitch to the END SHAPE first, then
            // measure. EVERYTHING reads the same stitched paths — an older path
            // handed RAW strokes to one matcher and stitched ones to the rest,
            // so the two halves of the ensemble were literally looking at
            // different drawings.
            var stitched = StitchStrokes(rawStrokes);
            var graph = RuneGraph.Build(stitched);
            if (graph == null) return (RuneType.None, 0f, RuneType.None, 0f);

            // A BARE LINE IS NOT A RUNE (Marko: "a straight line should be
            // detected as NOTHING automatically"). Every rune in the alphabet
            // is one line WITH CORNERS — that is what the pairs differ by. A
            // drawing that is one straight run has no turn sequence at all, and
            // letting it match the nearest template is how every stray line
            // became a PUSH. The graph owns this rule: no corners, no rune. It
            // is the definition now, not a heuristic on turn totals.
            if (graph.BareLine) return (RuneType.None, 0f, RuneType.None, 0f);

            return Top2Descriptor(ownerId, graph, null);
        }

        /// The one scoring loop, over an ALREADY-BUILT descriptor.
        ///
        /// `skip` is how a template is kept from scoring against ITSELF — see
        /// AuditTemplates, which could not see a single one of its own bugs
        /// until this existed.
        ///
        /// EVERY rune is scored, never just the argmax: the unlock gate and the
        /// ambiguity guard both need the runner-up. ownerId gates by unlocks;
        /// pass null to score the full library.
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

        // THE CHAIN CODE NO LONGER TOUCHES A RUNE VERDICT (Aug 1). It used to
        // be wired in here as `graph * Lerp(0.88, 1.06, chain)` — the graph held
        // an absolute veto and the direction sentence was worth ±9%. That
        // weighting was measured and it was the worst of both worlds:
        //   - it could not rescue anything. Scored ALONE on Marko's recordings
        //     the chain code gets 22/24 and reads LIGHT and DARK correctly, but
        //     ±9% cannot lift the graph's broken 0.18 for LIGHT anywhere near
        //     the 0.42 floor. The good signal was outvoted by the broken one.
        //   - now that the primary descriptor works, the same ±9% is purely
        //     destructive: correct reads and their runner-ups are separated by
        //     as little as 0.14 (SOLID vs LIQUID), so a ±9% swing driven by a
        //     45°-bucketed, length-blind sentence read in a frame GUESSED off
        //     the drawing can flip the ranking on its own.
        // ChainCodeRecognizer stays in the project and stays in use — it is what
        // parses COMPOUND SIGILS (see ClassifyCompound), a job the single-glyph
        // descriptor does not do. It just does not get a vote on which rune a
        // single glyph is.

        // ---- template health: is the alphabet YOURS, and is it unambiguous? --

        /// Per-classify logging + CSV dump. OFF by default — it cost a
        /// stack-traced Editor log and a file append on every recognition.
        public static bool LogClassifies = false;

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
                    Debug.LogWarning($"[RuneLibrary] AUDIT: the {ShortName(r)} template is only {pts} points. Sparse recordings misread easily; re-record it drawing larger and slower.");
            }
            if (seedRunes.Count > 0)
                Debug.LogWarning($"[RuneLibrary] AUDIT: {seedRunes.Count} rune(s) still use DEFAULT shapes, not your handwriting: {string.Join(", ", seedRunes)}. Draw each and press its F-key to record.");
            else
                Debug.Log("[RuneLibrary] AUDIT: all 12 runes use YOUR recorded handwriting.");

            // LEAVE-ONE-OUT, AND THAT IS THE WHOLE POINT.
            //
            // THE AUDIT USED TO BE BLIND BY CONSTRUCTION. It re-derived each
            // rune's drawing from the saved strokes and scored it with
            // Top2(null, sample) — against a pool that CONTAINED THAT EXACT
            // DRAWING. Self-match is 1.00 by definition, so the audit could
            // never report anything: the saved file proves it, `"confusable":
            // []` with `auditedCount: 24`, while a freshly drawn LIGHT was
            // fizzling at 0.18 against its own wall. That is also why the Rune
            // Studio test loop kept saying a rune worked when it did not — the
            // studio was scoring his ink against a pool containing that ink.
            //
            // A sample is now scored against every template EXCEPT ITSELF, so
            // "does my handwriting read as itself" is a real question with a
            // real answer. It also means a rune with only ONE drawing on its
            // wall cannot be cross-checked at all — there is nothing else of
            // his to recognise it by — and saying so out loud is more useful
            // than a fake 1.00.
            var entangled = new List<string>();
            var lonely = new List<string>();
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
                        Debug.LogError($"[RuneLibrary] AUDIT: a {ShortName(e.Type)} drawing reads as {ShortName(t1)} ({s1:F2}) when scored against every OTHER template. This is a bug in RuneGraph, not in your handwriting (he ruled no glyph resembles another under any rotation or reflection).");
                        if (_confusable.Add(PairKey(e.Type, t1)))
                            entangled.Add($"{ShortName(e.Type)}~{ShortName(t1)}");
                    }
                    // POOL-AWARE CONFUSABILITY: when a drawing of rune A merely
                    // NEARLY ties with B, those two are entangled IN MARKO'S
                    // HAND, and the coin-flip guard would eat every valid cast
                    // between them (PULL 0.80/PUSH 0.79 once fizzled his correct
                    // Ys). Entangled pairs are exempt from that guard: the top
                    // score wins. The console names them so cleaning up a wall
                    // stays his informed choice.
                    else if (t2 != RuneType.None
                             && s1 - s2 < DrawingConfig.RuneAmbiguityMargin + 0.03f)
                    {
                        Debug.LogWarning($"[RuneLibrary] AUDIT: {ShortName(e.Type)} and {ShortName(t2)} score within {s1 - s2:F2} of each other ({s1:F2} vs {s2:F2}). Cross-fires possible.");
                        if (_confusable.Add(PairKey(e.Type, t2)))
                            entangled.Add($"{ShortName(e.Type)}~{ShortName(t2)}");
                    }
                }
            }
            if (lonely.Count > 0)
                Debug.LogWarning($"[RuneLibrary] AUDIT: {lonely.Count} rune(s) have only ONE usable drawing, so nothing can cross-check them: {string.Join(", ", lonely)}. Draw each a second time on its wall.");
            if (entangled.Count > 0)
                Debug.Log($"[RuneLibrary] AUDIT: pairs entangled in your handwriting (top score decides between them): {string.Join(", ", entangled)}");

            SaveAuditCache(); // never pay for this twice
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
        /// An empty wall clears the rune back to its synthetic seed shape.
        /// Returns how many were kept.
        ///
        /// EVERY DRAWING ON THE WALL IS KEPT, however small. This used to DELETE
        /// any drawing under MinTemplatePoints — and since nodes are laid at a
        /// fixed world spacing, a point count is a proxy for PHYSICAL SIZE, so
        /// that was "his small drawings are erased from his wall", breaking both
        /// "size must never matter" and "I don't want to see any changes from
        /// what I drew". Sparse drawings are still not used as matcher
        /// templates (they misread everything angular as themselves) — but that
        /// is a recognition decision, taken below and in LoadRecorded. It is not
        /// a licence to throw his ink away.
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
                // THE RETURN VALUE IS LOAD-BEARING (see SetTemplateInternal):
                // false means the descriptor could not be built, so that sample
                // is NOT the one that restates the rune's identity. Flipping
                // `first` regardless left the very first sample appending
                // instead of replacing — the Variants.Clear() branch never ran
                // and the rune kept matching against drawings that are no
                // longer on the wall. LoadRecorded has always done this
                // correctly; this path had drifted.
                bool first = true;
                foreach (var sample in kept)
                {
                    if (PointCount(sample) < MinTemplatePoints) continue; // kept on the wall, not taught
                    if (SetTemplateInternal(type, sample, append: !first)) first = false;
                }
                if (first)
                {
                    // nothing on the wall was usable as a template (an empty
                    // wall, or only sparse drawings) — the seed shape takes over
                    var poly = GlyphPolyline(type);
                    if (poly != null)
                        SetTemplateInternal(type, new List<List<Vector2>> { poly });
                }

                // refresh THIS rune's entanglements immediately — the studio
                // test loop must judge by the wall as it is NOW.
                //
                // LEAVE-ONE-OUT here too. This used to re-derive each wall
                // drawing and score it with Top2(null, sample) against a pool
                // that had just been rebuilt FROM that same drawing, so it
                // always saw a 1.00 and never flagged anything. Scoring the
                // cached descriptor against every template but itself is the
                // only version of this check that can fail.
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

        /// ONE MORE SAMPLE OF THE PLAYER'S HAND (Marko's invisible natural
        /// progression: "the game learns the handwriting… your character
        /// becomes better at casting that rune naturally"). Appends a played
        /// drawing to the rune's pool; when the pool is full the OLDEST
        /// sample rolls out, so the ensemble follows how the player draws
        /// NOW. Returns false for drawings too sparse to teach anything.
        ///
        /// QUIET mode (the in-game silent learn): NO pool rebuild, NO
        /// confusable refresh, NO file write on the cast frame (Marko: closing
        /// a seal must never hitch) — and no LATER file write either. A quiet
        /// sample joins the live matcher and nothing else, because what the
        /// game learns while you play lasts one round; only Rune Studio
        /// persists. A full pool simply declines quiet samples; the declare
        /// path still rolls.
        public static bool AddSample(RuneType type, List<List<Vector2>> sample, bool quiet = false)
        {
            Init();
            if (type == RuneType.None || sample == null
                || PointCount(sample) < MinTemplatePoints) return false;

            if (quiet)
            {
                // WHAT THE GAME LEARNS WHILE YOU PLAY LASTS ONE ROUND (Marko:
                // "when someone memorizes the handwriting it should only be in
                // memory for that round and not all the rounds"). So a quiet
                // sample joins the LIVE matcher and nothing else — it never
                // touches _saved and never reaches the file. Only what you
                // deliberately record in Rune Studio persists.
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
            while (pool.Count > MaxSamples) pool.RemoveAt(0); // the oldest hand rolls out
            return ReplaceSamples(type, pool) > 0;
        }

        // FlushSaves() IS GONE, with its `_savedDirty` flag and the 10-second
        // timer in DrawingWorld that called it. Nothing ever set the flag, so
        // the method could not write even in principle — it was dead code that
        // READ as load-bearing, and AddSample's own doc used to promise a
        // deferred save that would never happen. It has nothing left to do
        // either: quiet in-game learning is round-only by ruling, so it
        // deliberately never touches _saved, and the two real write paths
        // (ReplaceSamples, SaveAuditCache) write immediately.

        static List<SavedStroke> ToSavedStrokes(List<List<Vector2>> strokes)
        {
            var outp = new List<SavedStroke>(strokes.Count);
            foreach (var s in strokes) outp.Add(new SavedStroke { points = new List<Vector2>(s) });
            return outp;
        }

        /// THE ONE PLACE a descriptor is built. Every load, record and learn
        /// path funnels through here, so the RuneGraph on an Entry is computed
        /// exactly once and then cached for the life of the pool — the matcher
        /// itself never re-derives anything.
        ///
        /// Returns false ONLY for a drawing that cannot teach anything. That
        /// contract is load-bearing: LoadRecorded / AddSample / ReplaceSamples
        /// all read it as "this template does not exist", and a false makes the
        /// NEXT sample take over the rune's identity instead of appending to it.
        ///
        /// A BARE LINE IS REJECTED HERE, and it has to be. Marko's newest PULL
        /// wall drawing is 63 points that deviate from their own chord by 0.52%
        /// — a straight line, as far as any shape math is concerned. It built a
        /// perfectly non-null graph, so the old `graph == null` gate waved it
        /// through; it then occupied PULL's newest-sample slot and returned 0.00
        /// against every drawing on earth, because a bare line matches nothing
        /// by his own rule. PULL had ONE live template and looked like it had
        /// two. Accepting a template that can never match is strictly worse than
        /// admitting the rune is under-taught — and the warning tells him which
        /// wall to go redraw.
        ///
        /// This is NOT a licence to throw his ink away: the drawing stays on the
        /// wall and in the save file (see ReplaceSamples). It just does not get
        /// to be a template.
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
                // one more sample of his hand joins the pool
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
                existing.Variants.Clear(); // fresh identity: the pool restates itself
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

        // (per-press template saving removed with the F-keys — ReplaceSamples
        // above is the one write path, driven by the Rune Studio walls.
        // DeleteRecordings is DELETED too: zero callers, and dead code that
        // hard-deletes Marko's recordings file is a loaded gun — same reasoning
        // that removed RuneWall's AlignToFirst.)

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
