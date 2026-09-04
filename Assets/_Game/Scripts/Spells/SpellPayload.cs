using UnityEngine;

namespace SpellyZombie
{
    /// How an axis answers the place it is standing in.
    public enum AxisLaw
    {
        /// The biome IMPOSES it, freely, past whatever is natural for you -
        /// that is how burning and freezing happen at all.
        Impose,
        /// A CAPACITY: you get min(your own, the place's). A dreadful biome
        /// unnerves the brave, but a brave one does not embolden a coward; a
        /// clone-rich place gives nothing to something that never had one.
        Lesser,
    }

    /// THE ONE PARAMETER SET. A biome carries it (what is natural HERE), an
    /// object carries it twice (what it was BORN as, and what it is NOW), and a
    /// spell carries it once (the change it makes). Nothing is an exception.
    ///
    /// Runes push axes and meeting particles ADD their values. What a thing
    /// IS gets decided by WHERE ITS NUMBERS SIT - never by what was mixed to
    /// get there. Two particles that meet are one larger particle with the
    /// summed values, and it becomes something the moment those values cross a
    /// threshold. Nothing anywhere records a recipe.
    ///
    /// State and Pressure are DIFFERENT things. Pressure is how heavy you are
    /// against the medium, and decides whether you rise or sink. State is what
    /// you are MADE of. A meteor is flame plus solid; that is state, not weight.
    [System.Serializable]
    public struct SpellPayload
    {
        // ---- IMPOSED: the place wins, past whatever you naturally are -----
        public float Temp;      // + hot     - chill
        public float Lum;       // + light   - dark
        public float Pressure;  // + compress - spread. Under the medium and you rise
        public float Balance;   // + planted - slippery. Inertia, and staying upright
        public float State;     // + solid   - gas. WHAT IT IS MADE OF, not how heavy
        // ---- CAPACITIES: you get min(yours, the place's) ------------------
        public float Affinity;  // + attract - repel. Its own gravity, on everything near
        public float Strength;  // what it can bear, and its health
        public float Int;       // 0 mindless - high follows its task perfectly
        public float Courage;   // 0 afraid of everything - high faces anything
        /// PARKED, Aug 22, on his order: it is carried like the other nine and
        /// deliberately does nothing. Not in the demo. Do not wire it up
        /// without asking - the axis is cheap, but player clones are not.
        public float Clones;    // whole copies of itself; a copy exists per whole point

        /// Which law each axis obeys. The only place this is written down.
        public static AxisLaw LawOf(int axis) => axis switch
        {
            0 or 1 or 2 or 3 or 4 or 5 => AxisLaw.Impose, // Temp, Lum, Pressure, Balance, State, Affinity
            _ => AxisLaw.Lesser,                          // Strength, Int, Courage, Clones
        };

        public const int AxisCount = 10;

        public float this[int axis]
        {
            get => axis switch
            {
                0 => Temp, 1 => Lum, 2 => Pressure, 3 => Balance, 4 => State,
                5 => Affinity, 6 => Strength, 7 => Int, 8 => Courage, _ => Clones,
            };
            set
            {
                switch (axis)
                {
                    case 0: Temp = value; break;
                    case 1: Lum = value; break;
                    case 2: Pressure = value; break;
                    case 3: Balance = value; break;
                    case 4: State = value; break;
                    case 5: Affinity = value; break;
                    case 6: Strength = value; break;
                    case 7: Int = value; break;
                    case 8: Courage = value; break;
                    default: Clones = value; break;
                }
            }
        }

        /// Combining ADDS. That is the whole rule - what the sum IS gets
        /// decided by thresholds afterwards.
        public static SpellPayload operator +(SpellPayload a, SpellPayload b)
        {
            var s = new SpellPayload();
            for (int i = 0; i < AxisCount; i++) s[i] = a[i] + b[i];
            return s;
        }

        public static SpellPayload operator -(SpellPayload a, SpellPayload b)
        {
            var s = new SpellPayload();
            for (int i = 0; i < AxisCount; i++) s[i] = a[i] - b[i];
            return s;
        }

        public SpellPayload Scaled(float k)
        {
            var s = new SpellPayload();
            for (int i = 0; i < AxisCount; i++) s[i] = this[i] * k;
            return s;
        }

        /// Where an axis wants to sit, given what is natural for the body and
        /// what the place says. Imposed axes take the place's word; capacities
        /// take the lesser. This ONE function is the difference between burning
        /// in a fire and staying stupid in a clever room.
        public static float TargetFor(int axis, float natural, float here) =>
            LawOf(axis) == AxisLaw.Impose ? here : Mathf.Min(natural, here);

        /// ★ HIS COUPLING TABLE (Aug 26): the effect axes are BYPRODUCTS of
        /// the carried data, offset onto the capacity drift targets.
        ///   Lum      -> Courage (light emboldens, darkness frightens)
        ///   Pressure -> Strength up + Clones down (and the mirror)
        ///   State    -> Mind (solid sharp, gas empty-headed)
        ///   Affinity -> Courage up + Mind down (attract); reverse (repel)
        ///   Balance  -> Mind up + Strength down (planted); reverse (slick)
        ///   Temp     -> nothing here: the burn/freeze damage law IS that
        ///               coupling already ("fire should hurt but so should
        ///               coldness").
        /// dev = the thing's data measured from its own natural, so a
        /// creature born in darkness is not frightened by its home.
        public static float EffectCoupling(int axis, SpellPayload dev) => axis switch
        {
            6 => dev.Pressure * DrawingConfig.CouplePressureStrength
               - dev.Balance * DrawingConfig.CoupleBalanceStrength,
            7 => dev.State * DrawingConfig.CoupleStateMind
               + dev.Balance * DrawingConfig.CoupleBalanceMind
               - dev.Affinity * DrawingConfig.CoupleAffinityMind,
            8 => dev.Lum * DrawingConfig.CoupleLumCourage
               + dev.Affinity * DrawingConfig.CoupleAffinityCourage,
            9 => -dev.Pressure * DrawingConfig.CouplePressureClones,
            _ => 0f,
        };

        /// The whole body's target, axis by axis.
        public static SpellPayload TargetFor(SpellPayload natural, SpellPayload here)
        {
            var t = new SpellPayload();
            for (int i = 0; i < AxisCount; i++) t[i] = TargetFor(i, natural[i], here[i]);
            return t;
        }

        /// Move toward a target at `perSecond`. Drift is how a place changes
        /// you; nothing here snaps.
        public SpellPayload DriftedTo(SpellPayload target, float perSecond, float dt)
        {
            var s = this;
            float step = perSecond * dt;
            for (int i = 0; i < AxisCount; i++) s[i] = Mathf.MoveTowards(s[i], target[i], step);
            return s;
        }

        /// One rune's push. THE ONLY rune-to-payload mapping in the game.
        public static SpellPayload Of(RuneType rune, float power = 1f)
        {
            var p = new SpellPayload();
            switch (rune)
            {
                case RuneType.HeatUp: p.Temp = power; break;
                case RuneType.HeatDown: p.Temp = -power; break;
                case RuneType.LuminanceUp: p.Lum = power; break;
                case RuneType.LuminanceDown: p.Lum = -power; break;
                case RuneType.DensityUp: p.Pressure = power; break;
                case RuneType.DensityDown: p.Pressure = -power; break;
                case RuneType.StickyUp: p.Balance = power; break;
                case RuneType.StickyDown: p.Balance = -power; break;
                case RuneType.Attract: p.Affinity = power; break;   // attract: moves the target where it pointed
                case RuneType.Repel: p.Affinity = -power; break;// repel: swaps the force to negative
                // STATE IS WHAT A THING IS MADE OF - not how heavy it is.
                // Pressure decides whether you rise or sink; State decides
                // whether you are rock, water or air. A meteor is flame plus
                // SOLID, and that has nothing to do with being dense.
                case RuneType.StateSolid: p.State = power; break;
                case RuneType.StateLiquid: p.State = -power * 0.5f; break;
            }
            return p;
        }

        /// WHAT A STATE NUMBER MEANS. Solid, liquid and gas are REGIONS on one
        /// axis, never stored anywhere - so a thing crosses from one to the
        /// next because its number moved, and mud is simply the thick part of
        /// liquid rather than a fourth thing needing its own name.
        /// Gas sits at -1, liquid at 0, solid at +1.
        public static MatterPhase PhaseOf(float state) =>
            state >= SolidAt ? MatterPhase.Solid
          : state <= GasAt ? MatterPhase.Gas
          : MatterPhase.Liquid;

        /// ★ THE MATERIAL'S 0..1 FROM THE AXIS, in one place. Every caller
        /// used (State + 1) / 2, which assumed the axis ran -1..1 - it runs
        /// +-AxisCap internally, so "slightly liquid" was landing near gas on
        /// screen, in the editor and in the game alike.
        public static float StateT01(float internalState) =>
            Mathf.Clamp01((internalState / DrawingConfig.AxisCap + 1f) * 0.5f);

        /// The two crossings. Mud is liquid just under SolidAt - dense enough
        /// to wade and not yet ground.
        // ONE HUNDRED PER PHASE. State runs -150 to +150 so each phase gets
        // a full hundred and the crossings land on round numbers:
        //     -150 ........ -50 ........ +50 ........ +150
        //           gas          liquid         solid
        // Solid 25 is thick liquid - mud - and Solid 60 is rock. His call:
        // "-150 and +150 giving the ability to have 300 (100 + 100 + 100)".
        // In internal units the crossings are a third of the way out.
        public static float SolidAt => DrawingConfig.AxisCap / 3f;
        public static float GasAt => -DrawingConfig.AxisCap / 3f;

        /// ★ WHAT IS ALIVE. A mind is what makes a thing living: a wall has no
        /// brain and a zombie does, so poison eats one and ignores the other
        /// without anybody keeping a list of creatures.
        ///
        /// His ruling: "it can be determined via brain what is living. So
        /// poison condition is brain not being 0."
        public bool Alive => Int > 0f;

        /// ★ EVERY AXIS TOPS OUT. Without a ceiling, stacking heat onto
        /// something already burning keeps it burning forever; with one it
        /// maxes out, drift begins pulling it back, and keeping a thing alight
        /// costs the wizard repeated casting. The cap is what turns a spell
        /// into something you MAINTAIN rather than something you land once.
        ///
        /// STRENGTH is deliberately not here: its ceiling is per-body - a
        /// 90-cap acolyte, a 140-cap wizard, dragged down by the biome - and
        /// belongs on the carrier, not in a global number.
        public static float CapOf(int axis) => axis switch
        {
            // Temperature is REAL DEGREES and needs the range degrees need:
            // wood ignites in the hundreds and lava is hotter still. The unit
            // cap would have pinned everything at 100 and quietly made molten
            // things impossible.
            0 => DrawingConfig.TempCeiling,
            6 => float.MaxValue,                        // Strength: per-body
            7 or 8 => DrawingConfig.CapacityCap,        // Int, Courage
            9 => DrawingConfig.CloneCap,                // whole copies
            _ => DrawingConfig.AxisCap * UnitOf(axis),
        };

        /// The other end. Mind, courage, copies and strength have no negative
        /// side - zero is mindless, terrified, alone, and dead.
        public static float FloorOf(int axis) => axis switch
        {
            0 => DrawingConfig.TempFloor,
            6 or 7 or 8 or 9 => 0f,
            _ => -CapOf(axis),
        };

        /// Hold every axis inside its band. Call after anything that ADDS.
        public SpellPayload Clamped()
        {
            var s = this;
            for (int i = 0; i < AxisCount; i++)
                s[i] = Mathf.Clamp(s[i], FloorOf(i), CapOf(i));
            return s;
        }

        /// ★ ONE AXIS'S UNIT. Temp is carried in DEGREES - a spark is 25, a
        /// room is 18 - while every other axis is carried in units, where a
        /// light mote is 1. Anything that COMPARES axes must divide by this
        /// first, or Temp wins every contest by a factor of twenty-five.
        public static float UnitOf(int axis) => axis == 0 ? DrawingConfig.SparkHeatDelta : 1f;

        // ---- ★ WHAT A HUMAN TYPES ----------------------------------------
        // Each axis speaks its own language, in whole numbers: temperature in
        // DEGREES, strength in HP, clones as a COUNT, the rest as a percent.
        // "Temperature 50" is hot; "Clones 1.27" was nothing at all.
        //
        // A SPELL IS ALWAYS SIGNED, even on axes the world keeps one-sided.
        // A spell is a delta, and a delta must be allowed to point down or
        // nothing could ever drain, confuse, frighten or un-clone. The WORLD
        // clamps at zero on those axes; the spell does not.

        /// Human units per internal unit. The runtime keeps its own scale so
        /// nothing already built moves; this is the one conversion.
        public static float HumanPerUnit(int axis) => axis switch
        {
            0 => 1f,                    // degrees ARE degrees
            4 => 150f / DrawingConfig.AxisCap,   // state: a hundred per phase, three phases
            6 => 1f,                    // HP IS HP
            9 => 1f,                    // a clone is a clone
            _ => 100f / DrawingConfig.AxisCap,   // percent of the axis cap
        };

        public static float ToHuman(int axis, float internalValue) =>
            internalValue * HumanPerUnit(axis) / (axis == 0 ? 1f : UnitOf(axis));

        public static float FromHuman(int axis, float human) =>
            human / HumanPerUnit(axis) * (axis == 0 ? 1f : UnitOf(axis));

        /// The slider range a SPELL gets, signed on every axis.
        public static void SpellRange(int axis, out int lo, out int hi)
        {
            switch (axis)
            {
                case 0: lo = -200; hi = 300; break;   // degrees
                case 4: lo = -150; hi = 150; break;   // state: gas / liquid / solid, 100 each
                case 6: lo = -300; hi = 300; break;   // HP
                case 9: lo = -3;   hi = 3;   break;   // copies
                default: lo = -100; hi = 100; break;  // percent
            }
        }

        /// ★ THE LINE. How far along an axis, in HUMAN units, a thing has to
        /// be before it counts as that thing at all. Per axis, because "hot
        /// enough to be fire" and "bright enough to be light" are not the same
        /// distance. Was one global number in internal units nobody could read.
        public static float LineFor(int axis) => axis switch
        {
            0 => DrawingConfig.LineTemp,
            6 => DrawingConfig.LineStrength,
            9 => DrawingConfig.LineClones,
            _ => DrawingConfig.LinePercent,
        };

        public static string UnitName(int axis) => axis switch
        {
            0 => "°", 6 => " hp", 9 => "", _ => "%",
        };

        /// This payload's axis in comparable units. Compare these, never the
        /// raw values.
        public float Unit(int axis) => this[axis] / UnitOf(axis);

        /// How far from nothing this payload sits, in comparable units.
        public float Strongest
        {
            get
            {
                float m = 0f;
                for (int i = 0; i < AxisCount; i++) m = Mathf.Max(m, Mathf.Abs(Unit(i)));
                return m;
            }
        }

        /// The axis furthest from nothing, in comparable units - what this
        /// payload mostly IS. -1 when it is nothing in particular.
        public int Dominant
        {
            get
            {
                int best = -1; float m = 0.05f;
                for (int i = 0; i < AxisCount; i++)
                {
                    float a = Mathf.Abs(Unit(i));
                    if (a > m) { m = a; best = i; }
                }
                return best;
            }
        }

        /// His palette, blended by weight - the particle's colour IS its
        /// stats. Weighted in UNITS: by raw value a single spark's 25 degrees
        /// drowned out every other axis and every warm particle came out red.
        public Color Tint()
        {
            Color sum = Color.black; float w = 0f;
            void Add(Color c, float amount)
            {
                float a = Mathf.Abs(amount);
                if (a < 0.05f) return;
                sum += c * a; w += a;
            }
            Add(Temp > 0f ? new Color(0.95f, 0.25f, 0.15f) : new Color(0.92f, 0.96f, 1f), Unit(0));
            Add(Lum > 0f ? new Color(1f, 0.93f, 0.35f) : new Color(0.08f, 0.07f, 0.10f), Lum);
            Add(Pressure > 0f ? new Color(0.45f, 0.50f, 0.42f) : new Color(0.80f, 0.85f, 0.88f), Pressure);
            Add(Balance > 0f ? new Color(0.85f, 0.60f, 0.15f) : new Color(0.78f, 0.72f, 0.95f), Balance);
            // THREE PHASES, THREE COLOURS. It used to add one pale blue for
            // anything below zero and nothing at all for solid - two colours
            // for three states. Now the band the number sits in picks the
            // colour, and how far into the band sets the weight, so mud reads
            // as thick water and rock reads as rock.
            if (Mathf.Abs(State) > 0.05f)
            {
                var phase = PhaseOf(State);
                Color c = phase == MatterPhase.Solid ? new Color(0.50f, 0.45f, 0.40f)   // stone
                        : phase == MatterPhase.Gas   ? new Color(0.80f, 0.92f, 1.00f)   // vapour
                        :                              new Color(0.35f, 0.60f, 0.95f);  // water
                Add(c, State);
            }
            return w > 0.05f ? sum / w : Color.white;
        }
    }
}
