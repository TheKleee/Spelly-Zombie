using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// THE THRESHOLD TABLE. Named spells are regions of payload space: a row
    /// says which axes must be past the fusion threshold and with which sign,
    /// and what effect that region has. Combining adds payloads, then this
    /// answers "what is it now" - order matters on its own, because each
    /// intermediate crosses its own region before the next merge lands.
    ///
    /// DATA, NOT CODE: defaults below are the ruled V2 set, and
    /// {persistentDataPath}/sz_spells.json can add or replace rows without a
    /// rebuild - a new spell is a new row (Workshop target, same doctrine as
    /// sz_tuning.json).
    public static class SpellTable
    {
        [Serializable]
        public class Row
        {
            public string Name;
            // per axis: +1 needs positive past threshold, -1 negative, 0 = don't care
            public int Heat, Lum, Weight, Stick, Affinity;
            public int NeedsPhase;      // 0 any · 1 solid · 2 liquid · 3 gas
            public string Effect;       // SpellEffects primitive id
            public float Param;         // effect-specific dial
            public int MaxLevel = 2;    // fusions never make biomes
        }

        [Serializable] class RowFile { public Row[] rows; }

        public const string FileName = "sz_spells.json";

        /// The fusion threshold: an axis counts once its magnitude passes this.
        public static float FusionAt => DrawingConfig.FusionAt;

        static List<Row> _rows;

        /// Ruled Aug 20 - the V2 list, most specific first (double fusions
        /// before their parents, so Plasma wins over Flame when both match).
        public static IReadOnlyList<Row> Rows
        {
            get
            {
                if (_rows != null) return _rows;
                _rows = new List<Row>
                {
                    // fusions of fusions
                    new Row { Name = "Plasma",   Heat = +1, Lum = +1, Weight = +1, Effect = "sun", Param = 1f },
                    new Row { Name = "Cloud",    Heat = -1, Lum = +1, Weight = -1, Effect = "cloud", Param = 1f },
                    new Row { Name = "Explosion",Stick = +1, Weight = +1, Affinity = -1, Effect = "explode_away", Param = 1f },
                    new Row { Name = "Meteor",   Heat = +1, Lum = +1, NeedsPhase = 1, Effect = "meteor", Param = 1f },
                    // fusions
                    new Row { Name = "Flame",        Heat = +1, Lum = +1, Effect = "flame", Param = 1f },
                    new Row { Name = "Lightning",    Lum = +1, Weight = +1, Effect = "zap", Param = 1f },
                    new Row { Name = "Heal",         Heat = -1, Lum = +1, Effect = "heal", Param = 25f },
                    new Row { Name = "Steam",        Heat = 0, Effect = "steam", Param = 1f, Stick = 0 }, // resolved by opposition, see IsSteam
                    new Row { Name = "Teleportation",Stick = -1, Weight = +1, Effect = "teleport", Param = 1f },
                    new Row { Name = "Buff",         Stick = +1, Weight = +1, Effect = "buff", Param = 30f },
                    new Row { Name = "Trail",        Stick = +1, Lum = +1, Effect = "trail", Param = 12f },
                    new Row { Name = "Transparency", Lum = -1, Weight = +1, Effect = "invisible", Param = 3f },
                };
                _rows.RemoveAll(r => r.Name == "Steam"); // steam is the opposition case below
                LoadOverlay();
                return _rows;
            }
        }

        static void LoadOverlay()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, FileName);
                if (!File.Exists(path)) return;
                var f = JsonUtility.FromJson<RowFile>(File.ReadAllText(path));
                if (f?.rows == null) return;
                foreach (var r in f.rows)
                {
                    if (string.IsNullOrEmpty(r.Name)) continue;
                    _rows.RemoveAll(x => x.Name == r.Name); // replace by name
                    _rows.Insert(0, r);                     // overlay rows win ties
                }
                Debug.Log($"[SpellyZombie] spell table overlay: {f.rows.Length} row(s)");
            }
            catch (Exception ex) { Debug.LogWarning($"[SpellyZombie] spell overlay skipped: {ex.Message}"); }
        }

        /// Heat and chill in one payload is the one true opposition product.
        public static bool IsSteam(SpellPayload a, SpellPayload b) =>
            Mathf.Abs(a.Heat) >= FusionAt && Mathf.Abs(b.Heat) >= FusionAt
            && Mathf.Sign(a.Heat) != Mathf.Sign(b.Heat);

        /// What this payload IS right now, or null while it is still just its
        /// strongest single axis.
        public static Row Resolve(SpellPayload p)
        {
            float t = FusionAt;
            foreach (var r in Rows)
            {
                if (r.Heat != 0 && (Mathf.Abs(p.Heat) < t || Mathf.Sign(p.Heat) != r.Heat)) continue;
                if (r.Lum != 0 && (Mathf.Abs(p.Lum) < t || Mathf.Sign(p.Lum) != r.Lum)) continue;
                if (r.Weight != 0 && (Mathf.Abs(p.Weight) < t || Mathf.Sign(p.Weight) != r.Weight)) continue;
                if (r.Stick != 0 && (Mathf.Abs(p.Stick) < t || Mathf.Sign(p.Stick) != r.Stick)) continue;
                if (r.Affinity != 0 && (Mathf.Abs(p.Affinity) < t || Mathf.Sign(p.Affinity) != r.Affinity)) continue;
                if (r.NeedsPhase != 0 && p.Phase != r.NeedsPhase) continue;
                return r;
            }
            return null;
        }
    }
}
