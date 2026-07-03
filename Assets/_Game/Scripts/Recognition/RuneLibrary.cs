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

    /// Holds one $1 template per rune. Ships with rough synthesized glyphs based on
    /// the design sketches; each can be overwritten in play mode (draw the glyph,
    /// press F1-F12) and the recording persists to disk.
    public static class RuneLibrary
    {
        class Entry
        {
            public RuneType Type;
            public Vector2[] Normalized;
        }

        [Serializable]
        class SavedTemplate { public int rune; public List<Vector2> points = new List<Vector2>(); }

        [Serializable]
        class SavedTemplateSet { public List<SavedTemplate> items = new List<SavedTemplate>(); }

        static List<Entry> _entries;
        static string SavePath => Path.Combine(Application.persistentDataPath, "sz_rune_templates.json");

        // ---- unlocks: per-run, in memory only (design: every run starts with no runes) ----

        /// Graybox switch. The match flow sets this false and grants cards as
        /// players find them; while true, everything is drawable for testing.
        public static bool AllRunesUnlockedForTesting = true;

        static readonly HashSet<RuneCardType> _unlockedCards = new HashSet<RuneCardType>();

        public static void UnlockCard(RuneCardType card) => _unlockedCards.Add(card);
        public static void ResetUnlocks() => _unlockedCards.Clear();

        public static bool IsUnlocked(RuneType type) =>
            type != RuneType.None && (AllRunesUnlockedForTesting || _unlockedCards.Contains(CardOf(type)));

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
                case RuneCardType.Heat: return "Heat — one glyph raises temperature, the mirrored glyph lowers it.";
                case RuneCardType.State: return "State — turn matter solid, or melt it toward liquid.";
                case RuneCardType.Luminance: return "Luminance — brighten the area, or swallow the light.";
                case RuneCardType.Sticky: return "Sticky — make surfaces grip, or make them slick.";
                case RuneCardType.Direction: return "Direction — arrow pushes away from the surface, Y pulls toward it.";
                default: return "Density — thicken matter so it sinks, or thin it so it rises.";
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
                SetTemplateInternal(pair.Key, pair.Value);
            LoadRecorded();
        }

        /// Classify a raw 2D stroke against the runes the player has UNLOCKED
        /// (design: strokes are compared to unlocked templates only). Returns the
        /// best rune and its raw score; the caller decides if it's good enough.
        public static (RuneType type, float score) Classify(IReadOnlyList<Vector2> rawShape)
        {
            Init();
            var candidate = DollarRecognizer.Normalize(rawShape);
            if (candidate == null) return (RuneType.None, 0f);

            var candidates = new List<Entry>(_entries.Count);
            foreach (var e in _entries)
                if (IsUnlocked(e.Type)) candidates.Add(e);
            if (candidates.Count == 0) return (RuneType.None, 0f);

            var templates = new List<Vector2[]>(candidates.Count);
            foreach (var e in candidates) templates.Add(e.Normalized);
            var (index, score) = DollarRecognizer.Recognize(candidate, templates);
            if (index < 0) return (RuneType.None, 0f);
            return (candidates[index].Type, score);
        }

        /// Replace the template for a rune with a player-recorded stroke and persist it.
        public static bool RecordTemplate(RuneType type, IReadOnlyList<Vector2> rawShape)
        {
            Init();
            if (rawShape == null || rawShape.Count < 6) return false;
            if (!SetTemplateInternal(type, new List<Vector2>(rawShape))) return false;
            SaveRecorded(type, rawShape);
            return true;
        }

        static bool SetTemplateInternal(RuneType type, List<Vector2> raw)
        {
            var normalized = DollarRecognizer.Normalize(raw);
            if (normalized == null) return false;
            var existing = _entries.Find(e => e.Type == type);
            if (existing != null) existing.Normalized = normalized;
            else _entries.Add(new Entry { Type = type, Normalized = normalized });
            return true;
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
                foreach (var t in _saved.items)
                    SetTemplateInternal((RuneType)t.rune, t.points);
                Debug.Log($"[RuneLibrary] Loaded {_saved.items.Count} recorded rune templates from {SavePath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RuneLibrary] Failed to load recorded templates: {e.Message}");
            }
        }

        static void SaveRecorded(RuneType type, IReadOnlyList<Vector2> rawShape)
        {
            try
            {
                var item = _saved.items.Find(i => i.rune == (int)type);
                if (item == null)
                {
                    item = new SavedTemplate { rune = (int)type };
                    _saved.items.Add(item);
                }
                item.points = new List<Vector2>(rawShape);
                File.WriteAllText(SavePath, JsonUtility.ToJson(_saved));
                Debug.Log($"[RuneLibrary] Recorded template for {type} ({rawShape.Count} pts) -> {SavePath}");
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

        // ---- synthesized default glyphs (y-up, arbitrary units) ----
        // Rough approximations of the sketch alphabet. They make recognition work out
        // of the box, but recording real hand-drawn templates (F1-F12) will always be
        // more accurate. Runes must stay OPEN — a big gap between start and end —
        // or they will close into a seal instead.
        static Dictionary<RuneType, List<Vector2>> DefaultGlyphs()
        {
            List<Vector2> P(params float[] xy)
            {
                var list = new List<Vector2>(xy.Length / 2);
                for (int i = 0; i < xy.Length; i += 2) list.Add(new Vector2(xy[i], xy[i + 1]));
                return list;
            }

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
