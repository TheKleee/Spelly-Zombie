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
        }

        [Serializable]
        class SavedStroke { public List<Vector2> points = new List<Vector2>(); }

        [Serializable]
        class SavedTemplate
        {
            public int rune;
            public List<Vector2> points = new List<Vector2>();      // legacy single-stroke format
            public List<SavedStroke> strokes = new List<SavedStroke>(); // current multi-stroke format
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
        /// unlocked, no starting-rune picker) — ON for combo testing. Flip false
        /// to exercise the real collect-your-runes progression loop.
        public static bool AllRunesUnlockedForTesting = true;

        /// Convenience: unlock a card for the LOCAL player (pickups use this).
        public static void UnlockCard(RuneCardType card) => Grimoire.Unlock(Grimoire.LocalPlayerId, card);

        public static bool IsUnlocked(int ownerId, RuneType type) =>
            type != RuneType.None && (AllRunesUnlockedForTesting || Grimoire.Has(ownerId, CardOf(type)));

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
                if (n < 5 || n > 24) continue; // too simple / too chaotic to be a word

                // dp[i]: best way to explain the first i letters
                var dpScore = new float[n + 1]; // length-weighted score sum
                var dpSkips = new int[n + 1];
                var dpParts = new List<(RuneType, float)>[n + 1];
                for (int i = 1; i <= n; i++) dpScore[i] = -1f;
                dpParts[0] = new List<(RuneType, float)>();

                for (int i = 1; i <= n; i++)
                {
                    // option: this letter is junk between words (max 2 junk letters)
                    if (dpScore[i - 1] >= 0f && dpSkips[i - 1] < 2)
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
                            if (!IsUnlocked(ownerId, e.Type) || e.Sentences == null) continue;
                            float sc = ChainCodeRecognizer.ScoreSpan(reading, j, len, e.Sentences);
                            if (sc < 0.55f) continue;
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

        /// Similarity of a partial sketch to EVERY owned rune, best first — the
        /// choose-and-stamp candidate list. This only SORTS the options; it
        /// never gates anything (the player's choice is the truth).
        public static List<(RuneType type, float score)> ScoreAll(int ownerId,
            IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            Init();
            var results = new List<(RuneType, float)>();
            var candidate = PointCloudRecognizer.Normalize(rawStrokes);
            var sentences = ChainCodeRecognizer.EncodeAll(rawStrokes);
            float elongation = ChainCodeRecognizer.Elongation(rawStrokes);
            foreach (var e in _entries)
            {
                if (!IsUnlocked(ownerId, e.Type)) continue;
                float p = candidate == null ? 0f
                    : PointCloudRecognizer.Score(PointCloudRecognizer.CloudDistance(candidate, e.Cloud));
                float chain = e.Sentences != null
                    ? ChainCodeRecognizer.Match(sentences, e.Sentences)
                      * ChainCodeRecognizer.AspectPenalty(elongation, e.Elongation) : 0f;
                results.Add((e.Type, Mathf.Max(p, chain)));
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
        }

        /// Classify a glyph (one or more raw 2D strokes in a shared frame)
        /// against the runes the given OWNER has unlocked — the seal's owner is
        /// whoever completed it, so zombie-closed seals read with zombie cards.
        public static (RuneType type, float score) Classify(int ownerId, IReadOnlyList<IReadOnlyList<Vector2>> rawStrokes)
        {
            Init();
            // ENSEMBLE: the $P point-cloud matcher AND Marko's direction-
            // sentence matcher both score every rune; the best answer wins —
            // two independent readings can only improve recognition.
            var candidate = PointCloudRecognizer.Normalize(rawStrokes);
            var sentences = ChainCodeRecognizer.EncodeAll(rawStrokes);
            float elongation = ChainCodeRecognizer.Elongation(rawStrokes);
            if (candidate == null && sentences.Count == 0) return (RuneType.None, 0f);

            RuneType bestType = RuneType.None;
            float bestScore = 0f;
            foreach (var e in _entries)
            {
                if (!IsUnlocked(ownerId, e.Type)) continue;
                float p = candidate != null
                    ? PointCloudRecognizer.Score(PointCloudRecognizer.CloudDistance(candidate, e.Cloud)) : 0f;
                float chain = e.Sentences != null
                    ? ChainCodeRecognizer.Match(sentences, e.Sentences)
                      * ChainCodeRecognizer.AspectPenalty(elongation, e.Elongation) : 0f;
                float score = Mathf.Max(p, chain);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestType = e.Type;
                }
            }
            return (bestType, bestScore);
        }

        /// Replace the template for a rune with a player-recorded glyph and persist it.
        public static bool RecordTemplate(RuneType type, List<List<Vector2>> rawStrokes)
        {
            Init();
            if (!SetTemplateInternal(type, rawStrokes)) return false;
            SaveRecorded(type, rawStrokes);
            return true;
        }

        static bool SetTemplateInternal(RuneType type, List<List<Vector2>> rawStrokes)
        {
            var cloud = PointCloudRecognizer.Normalize(rawStrokes);
            if (cloud == null) return false;
            var ro = ToReadOnly(rawStrokes);
            var sentences = ChainCodeRecognizer.EncodeAll(ro);
            float elongation = ChainCodeRecognizer.Elongation(ro);
            var existing = _entries.Find(e => e.Type == type);
            if (existing != null) { existing.Cloud = cloud; existing.Sentences = sentences; existing.Elongation = elongation; }
            else _entries.Add(new Entry { Type = type, Cloud = cloud, Sentences = sentences, Elongation = elongation });
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
                int loaded = 0;
                foreach (var t in _saved.items)
                {
                    var strokes = ToStrokeLists(t);
                    if (strokes.Count > 0 && SetTemplateInternal((RuneType)t.rune, strokes)) loaded++;
                }
                Debug.Log($"[RuneLibrary] Loaded {loaded} recorded rune templates from {SavePath}");
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

        static void SaveRecorded(RuneType type, List<List<Vector2>> rawStrokes)
        {
            try
            {
                var item = _saved.items.Find(i => i.rune == (int)type);
                if (item == null)
                {
                    item = new SavedTemplate { rune = (int)type };
                    _saved.items.Add(item);
                }
                _saved.version = GlyphSetVersion;
                item.points = new List<Vector2>(); // retire the legacy field
                item.strokes = new List<SavedStroke>();
                int pointCount = 0;
                foreach (var s in rawStrokes)
                {
                    item.strokes.Add(new SavedStroke { points = new List<Vector2>(s) });
                    pointCount += s.Count;
                }
                File.WriteAllText(SavePath, JsonUtility.ToJson(_saved));
                Debug.Log($"[RuneLibrary] Recorded template for {type} ({rawStrokes.Count} stroke(s), {pointCount} pts) -> {SavePath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RuneLibrary] Failed to save template: {e.Message}");
            }
        }

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
