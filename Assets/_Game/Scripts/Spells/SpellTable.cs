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
            /// SIGNED THRESHOLDS, in multiples of FusionAt. +2 means "at least
            /// twice the fusion threshold, positive". 0 means the axis is not
            /// part of this region at all.
            ///
            /// THE NUMBER UNLOCKS THE PARTICLE. Not a recipe - a particle sits
            /// somewhere on each axis, and crossing a value is what makes it
            /// something. How it got there does not matter and is not recorded:
            /// two runes, ten runes, or drifting into a hot biome all reach the
            /// same place and produce the same thing.
            public float Temp, Lum, Pressure, Balance, State;
            public float Affinity, Strength, Int, Courage, Clones;

            /// How specific this region is. The tighter one wins when two
            /// overlap, so ordering is a property of the data rather than of
            /// the order somebody happened to type it in.
            public float Specificity
            {
                get
                {
                    float n = 0f;
                    foreach (var v in new[] { Temp, Lum, Pressure, Balance, State,
                                              Affinity, Strength, Int, Courage, Clones })
                        n += Mathf.Abs(v);
                    return n;
                }
            }
            /// ★ THE NAME OF AN ENGINE HOOK, and the only part of a spell that
            /// is not data. Reserved for what numbers genuinely cannot say:
            /// teleport moves a body, invisible and trail are states on one. A
            /// Workshop author can USE any hook by name alongside their own
            /// numbers; inventing a fourth needs code, which is an honest
            /// boundary rather than a pretend one.
            ///
            /// Anything that merely moves a number does NOT belong here - it
            /// would bill the target twice, as payload and again as effect.
            public string Effect;
            public float Param;         // effect-specific dial
            public int MaxLevel = 2;    // fusions never make biomes

            /// DOES THIS SPREAD? His ruling: spreading is a shared ability,
            /// not a flame feature - "if something is spreading it should try
            /// to clone itself on the nearest object as well to spread its
            /// influence." A spreading coat pushes its numbers into the
            /// nearest element, which then satisfies the same region and
            /// spreads in turn. Fire and poison are one mechanic.
            public bool Spreads;

            /// DOES IT SPARE ITS OWN TEAM? His ruling: one simple bool, and
            /// the only two answers are "affects everything" and "affects
            /// everything except my own side". Poison is the second kind.
            ///
            /// Anything with no side of its own - scenery, golems - is on
            /// nobody's team, so it is never spared by this.
            public bool SparesOwnTeam;

            // ---- SUMMONING: a spell that casts another spell ----------------
            // ★ THIS IS WHAT A METEOR IS. Not an event with its own code - a
            // spell that puts another spell high above the target and pushes it
            // down at where it was cast. Once that exists, a meteor SHOWER is
            // the same row with more of them, spread on X and Z, staggered in
            // time; and an aura is the same row again with no offset at all,
            // riding the parent for as long as the parent still qualifies.
            //
            // One mechanic, three spells, and a Workshop author can build a
            // fourth without writing code.

            /// How many children. 0 = this row summons nothing.
            /// ★ THE THIRD KIND OF PARTICLE. A normal one spends itself on
            /// impact and combines with whatever it meets. An ATTACHED one
            /// does neither: it rides the thing it hit and keeps working for
            /// as long as its numbers still put it in this region.
            ///
            /// That is what invisibility, a tracking trail and clinging poison
            /// actually are - not effects with their own code, but particles
            /// that stuck to you. It is also why they can be removed the same
            /// way anything else is: change their numbers and they stop
            /// qualifying.
            public bool Attaches;

            /// ★ ONLY THINGS WITH A MIND. A payload reaches everything it
            /// touches by definition, so anything that must CHOOSE its victims
            /// needs to say so. This is the whole of what made poison special.
            public bool OnlyLiving;

            /// ★ A SHOVE, away from the point (negative pulls in). This is the
            /// whole of what made an explosion, and a zap's kick, special.
            public float Impulse;

            public int Summons;
            /// How far ABOVE the point they appear.
            public float SummonHeight;
            /// Random spread on X and Z. 0 = straight overhead.
            public float SummonSpread;
            /// Before the first one, and between each after it.
            public float SummonDelay, SummonStagger;
            /// How hard each is pushed back toward the point it was cast at.
            public float SummonSpeed;
            /// Offset 0 and glued to the parent - that is all an aura is.
            public bool SummonFollows;

            /// ★ ON IMPACT, NOT ON CASTING. The children appear where it LANDS
            /// and are thrown outward - which is the whole of what makes
            /// meteor debris debris. They carry a share of the payload, so they
            /// burn what they hit for the same reason the meteor did.
            public bool SummonOnImpact;

            /// ★ DOES IT HUNT? A striking spell picks a target at birth and
            /// slams into it rather than drifting. Lightning and meteors do;
            /// a sitting puddle does not.
            public bool Strikes;

            /// ★ A TRAIL BEHIND IT. Was a named effect that only one spell
            /// could have; it is a number now, so a tracking mark and a falling
            /// meteor both leave one and neither is special.
            /// 0 = none. Width in metres, and how long the tail lingers.
            public float TrailWidth, TrailSeconds;

            /// ★ SENDS WHAT IT HITS BACK TO WHERE IT WAS CAST. That is all a
            /// teleport is: the spell remembers its own seal, and the target is
            /// moved there. As data it is one flag, and any spell can have it -
            /// a recall trap, a hook, a swap.
            public bool MovesToOrigin;

            /// ★ SPENT ON CONTACT. It rides its summoner doing nothing until
            /// something is hit, delivers once, and is gone. That is the shape
            /// of a teleport: carried along, fired once, over.
            ///
            /// Different from Attaches, which STAYS on what it hit. These two
            /// are the two ways a spell can be carried - riding the thing that
            /// made it, or riding the thing it caught.
            public bool OneShot;

            /// How this one looks and moves. Optional - no Look means the
            /// material stays as authored.
            public Look Skin;
            /// What share of the parent's numbers each child carries.
            public float SummonShare = 1f;

            public float this[int axis] => axis switch
            {
                0 => Temp, 1 => Lum, 2 => Pressure, 3 => Balance, 4 => State,
                5 => Affinity, 6 => Strength, 7 => Int, 8 => Courage, _ => Clones,
            };

            /// ★ HOW STRONGLY this region applies. 1 at the threshold, higher
            /// the further past it the numbers sit - so a hotter flame IS a
            /// bigger flame. The AREA is the tell a player can read from
            /// across the map; colour only reads up close at the particle
            /// itself, which is no use to whoever is about to walk into it.
            public float Influence(SpellPayload p)
            {
                float sum = 0f; int n = 0;
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                {
                    float need = this[i];
                    if (Mathf.Approximately(need, 0f)) continue;
                    sum += Mathf.Abs(p.Unit(i)) / Mathf.Abs(need);
                    n++;
                }
                return n == 0 ? 1f : sum / n;
            }
        }

        /// ★ HOW IT MOVES AND BREAKS UP. Everything is made of the one blob,
        /// so the material is the other half of what tells two spells apart -
        /// a tornado swirls hard, a puddle wobbles and does neither.
        ///
        /// Every field is a slider on the state material. Leave one at -1 and
        /// the material keeps whatever it was authored with, so a row only has
        /// to name the handful it actually cares about.
        [Serializable]
        public class Look
        {
            // EVERYTHING STARTS AT ZERO. A spell with no material authored is
            // still, plain and quiet, and every slider is something you ADD -
            // not a checkbox that decides whether the material's own number
            // leaks through. Sizes start at their floor because a zero-sized
            // bubble is not a thing.
            public float Wobble, WobbleSpeed;              // liquid
            public float Swirl, SwirlSpeed, Turbulence;    // gas
            public float Bubbles, BubbleSize = 1f, BubbleRise;
            public float Holes, HoleSize = 1f, Rim;

            /// Your particle effect, by prefab name, riding the particle for as
            /// long as it is this thing. Fire, sparks, whatever it needs.
            public string Fx;
            /// And one for the moment it lands.
            public string ImpactFx;
        }

        [Serializable] public class RowFile { public Row[] rows; }

        /// The overlay file everything authored lands in. The Spell Window
        /// writes it; the game reads it at startup; a Workshop package ships
        /// one. Same file, three readers.
        public static string OverlayPath =>
            Path.Combine(Application.persistentDataPath, FileName);

        /// Throw away what is loaded so the next read picks the file up again.
        /// The window calls this after saving, so edits show without a restart.
        public static void Reload() { _rows = null; }

        /// Everything currently known, defaults and overlay together.
        public static List<Row> Editable => new List<Row>(Rows);

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
                    // hot, VERY bright, dense. Light is what separates it from
                    // a flame - one route there is flame plus lightning, but
                    // anything that reaches these numbers is a plasma.
                    new Row { Name = "Plasma",   Temp = +1, Lum = +2, Pressure = +1, Effect = "sun", Param = 1f },
                    new Row { Name = "Cloud",    Temp = -1, Lum = +1, Pressure = -1, Effect = "cloud", Param = 1f },
                    new Row { Name = "Explosion",Balance = +1, Pressure = +1, Affinity = -1, Impulse = 9f },
                    // hot, bright and SOLID. State is what separates it from a
                    // flame - being made of rock, which is not the same as
                    // being heavy.
                    new Row { Name = "Meteor",   Temp = +1, Lum = +1, State = +1, Strikes = true,
                              TrailWidth = 0.5f, TrailSeconds = 0.6f,
                              Summons = 1, SummonHeight = 26f, SummonSpeed = 34f, SummonShare = 1f },
                    // WHAT A METEOR LEAVES. Same seven fields, read at the
                    // moment of impact instead of the moment of casting - so
                    // debris is not a second system, it is the same one aimed
                    // at a different instant.
                    new Row { Name = "Debris", Temp = +1, State = +1, Strikes = true,
                              Summons = 4, SummonOnImpact = true, SummonSpread = 1.2f,
                              SummonSpeed = 7f, SummonShare = 0.4f },
                    // the shower is the SAME spell with more of them, spread and
                    // staggered - nothing about it is a second implementation
                    new Row { Name = "Meteor Shower", Temp = +2, Lum = +2, State = +2,
                              Summons = 6, SummonHeight = 26f, SummonSpread = 9f,
                              SummonStagger = 0.25f, SummonSpeed = 34f, SummonShare = 0.5f },
                    // fusions
                    // GOO - the liquid poison. Liquid because that is what it
                    // is made of, and its STRENGTH is the damage it carries:
                    // on impact it takes that much strength off, and the area
                    // it leaves keeps taking it off anything alive standing in
                    // it. It spreads, which is why a puddle grows rather than
                    // just sitting where it landed.
                    new Row { Name = "Goo", State = -1, Strength = +1,
                              Effect = "poison", Param = 9f, Spreads = true, OnlyLiving = true },
                    // ★ THE HOOK, and not one line of code knows it exists.
                    // It catches what it hits, trails back to the seal, and its
                    // affinity reels the catch in. Slick to fly, attract to
                    // pull - a Workshop author could have written this row.
                    new Row { Name = "Tornado", Affinity = +2, Pressure = -1,
                              Skin = new Look { Swirl = 5f, SwirlSpeed = 4f, Turbulence = 0.8f,
                                                Holes = 0.7f, Rim = 1.6f } },
                    new Row { Name = "Hook", Balance = -1, Affinity = +2,
                              Attaches = true, TrailWidth = 0.09f, TrailSeconds = 3f },
                    new Row { Name = "Flame",        Temp = +1, Lum = +1, Effect = "flame", Param = 1f, Spreads = true },
                    // it arcs: SPREADS carries it to the nearest thing, which then
                    // carries it on - the same rule flame uses, because it is
                    // the same rule
                    new Row { Name = "Lightning",    Lum = +1, Pressure = +1, Effect = "zap", Param = 1f,
                              Strikes = true, Spreads = true },
                    new Row { Name = "Heal",         Temp = -1, Lum = +1, Effect = "heal", Param = 25f },
                    new Row { Name = "Steam",        Temp = 0, Effect = "steam", Param = 1f, Balance = 0 }, // resolved by opposition, see IsSteam
                    new Row { Name = "Teleportation",Balance = -1, Pressure = +1,
                              MovesToOrigin = true, OneShot = true },
                    new Row { Name = "Buff",         Balance = +1, Pressure = +1, Effect = "buff", Param = 30f },
                    new Row { Name = "Trail",        Balance = +1, Lum = +1, Attaches = true,
                              TrailWidth = 0.14f, TrailSeconds = 12f },
                    // INVISIBILITY IS THE STATE AXIS. The state material already
                    // fades a thing out as it goes toward gas, so becoming
                    // see-through is not an effect - it is being made of air.
                    // His own note said as much: "you become liquid state".
                    new Row { Name = "Transparency", Lum = -1, Pressure = +1, State = -1, Attaches = true },
                };
                _rows.RemoveAll(r => r.Name == "Steam"); // steam is the opposition case below
                // Tightest first. Nothing WINS any more - every region a
                // payload sits in applies - but naming the particle reads
                // better when the most specific one leads.
                _rows.Sort((x, y) => y.Specificity.CompareTo(x.Specificity));
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
        /// Does this payload sit inside that region? Every named axis must be
        /// on the right side AND far enough out.
        static bool Meets(SpellPayload p, Row r)
        {
            float t = FusionAt;
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                float need = Need(r, i);
                if (Mathf.Approximately(need, 0f)) continue;      // not part of it
                // IN UNITS, not raw. A row's "+1" has to mean the same size of
                // push on every axis or the table is not data anybody could
                // author: raw, Temp's degrees cleared any threshold on sight
                // while Lum had to work for it.
                float have = p.Unit(i);
                if (Mathf.Sign(have) != Mathf.Sign(need)) return false;
                if (Mathf.Abs(have) < Mathf.Abs(need) * t) return false;
            }
            return true;
        }

        static float Need(Row r, int axis) => axis switch
        {
            0 => r.Temp, 1 => r.Lum, 2 => r.Pressure, 3 => r.Balance, 4 => r.State,
            5 => r.Affinity, 6 => r.Strength, 7 => r.Int, 8 => r.Courage, _ => r.Clones,
        };

        public static bool IsSteam(SpellPayload a, SpellPayload b) =>
            Mathf.Abs(a.Unit(0)) >= FusionAt && Mathf.Abs(b.Unit(0)) >= FusionAt
            && Mathf.Sign(a.Temp) != Mathf.Sign(b.Temp);

        /// EVERY region this payload sits inside - not one winner. A particle
        /// whose numbers satisfy both flame and lightning IS both, because
        /// neither is a thing it turned into: flame is a fire area riding on
        /// the particle and lightning is a lightning area riding on it, so a
        /// particle in both regions simply wears both. Nothing is ranked and
        /// nothing is beaten.
        ///
        /// This is why drifting through a strange biome causes mayhem - the
        /// numbers move, regions are entered and left, and what the particle
        /// carries changes underneath it with nobody casting anything.
        /// One row by name. Anything that needs a SPECIFIC spell's numbers -
        /// a zombie puking its goo - asks here rather than carrying its own
        /// copy of them, so tuning the spell tunes every source of it.
        public static Row ByName(string name)
        {
            foreach (var r in Rows) if (r.Name == name) return r;
            return null;
        }

        public static void All(SpellPayload p, List<Row> into)
        {
            into.Clear();
            foreach (var r in Rows)
                if (Meets(p, r)) into.Add(r);
        }
    }
}
