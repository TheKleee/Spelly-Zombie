using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Per-body sliders. Spells only push them; effects are readings derived
    /// each tick, and the environment drifts every slider home.
    ///   Temp   - the damage band; deep cold = frozen solid.
    ///   Lum    - vision only: darkness low, bloom high.
    ///   Grip   - + sticky (slow, then stuck) · − slick (slides, ragdolls).
    ///   Weight - light = higher jumps then float; heavy = movement gates.
    /// On creatures only Grip/Weight run; Thermal owns their temperature.
    public class BodyState : MonoBehaviour
    {
        // ---- naturals, bands, thresholds ----
        // static readonly + sz_tuning.json overlay: tunable without a rebuild
        // (const would inline at call sites)
        public static readonly float NaturalTemp = Tune("BodyNaturalTemp", 37f);
        public static readonly float TempBandLow = Tune("BodyTempBandLow", 15f);
        public static readonly float TempBandHigh = Tune("BodyTempBandHigh", 45f);
        public static readonly float FrozenSolidAt = Tune("BodyFrozenSolidAt", -25f);
        public static readonly float TempDriftPerSec = Tune("BodyTempDriftPerSec", 2.4f);
        public static readonly float TempDamagePerDegree = Tune("BodyTempDamagePerDegree", 0.10f);

        public static readonly float NaturalLum = Tune("BodyNaturalLum", 0.55f);   // daylight — torches sit INSIDE the band
        public static readonly float LumDriftPerSec = Tune("BodyLumDriftPerSec", 0.4f);

        public static readonly float GripDriftPerSec = Tune("BodyGripDriftPerSec", 0.45f);
        public static readonly float GripSlowAt = Tune("BodyGripSlowAt", 0.45f);
        public static readonly float GripStuckAt = Tune("BodyGripStuckAt", 1.15f);
        public static readonly float SlickSlideAt = Tune("BodySlickSlideAt", -0.45f);
        public static readonly float SlickDeepAt = Tune("BodySlickDeepAt", -1.4f); // full soap-hell depth

        public static readonly float WeightDriftPerSec = Tune("BodyWeightDriftPerSec", 0.30f);
        public static readonly float FloatBelow = Tune("BodyFloatBelow", 0.35f);   // REALLY light: you float
        // The load ladder, all measured as weight ÷ strength (see Load).
        public static readonly float RunLimit = Tune("BodyRunLimit", 1.55f);       // sprint refuses above
        public static readonly float JumpLimit = Tune("BodyJumpLimit", 2.0f);      // feet leave the ground below this only
        public static readonly float WalkLimit = Tune("BodyWalkLimit", 2.4f);      // crouch-crawl only above
        public static readonly float CrushLimit = Tune("BodyCrushLimit", 3.2f);    // above: your own weight bleeds strength
        public static readonly float CollapseLimit = Tune("BodyCollapseLimit", 4f);// above: too weak to stand, you ragdoll
        public static readonly float CrushDrainPerSec = Tune("BodyCrushDrainPerSec", 4f);


        public static readonly float DriftExpo = Tune("BodyDriftExpo", 3f);

        static float Tune(string key, float def) => DrawingConfig.Overlay(key, def);

        // ---- the biome under my feet, sampled on a beat, not per frame ----
        float _ambHeat, _ambLight, _ambStick, _ambDensity, _ambAffinity, _ambNext;
        MatterPhase _ambPhase = MatterPhase.Gas;   // what the place is made of

        /// What is natural where this body is standing right now. The whole
        /// parameter set, so capacities can read it without a second lookup.
        SpellPayload _here = new SpellPayload { Int = 1f, Courage = 1f };

        void TickAmbient(float dt)
        {
            _ambNext -= dt;
            if (_ambNext > 0f) return;
            _ambNext = 0.25f;
            var b = SpellyMap.BiomeAt(transform.position);
            float w = b != null ? b.WeightAt(transform.position) : 0f; // by-objects biomes fade with distance
            // payload units like _ambAffinity - raw human offsets pegged the
            // small sliders the moment a biome authored anything nonzero
            _ambHeat = b != null ? b.HeatOffset * w : 0f;
            _ambLight = b != null ? SpellPayload.FromHuman(1, b.LightOffset) * w : 0f;
            _ambStick = b != null ? SpellPayload.FromHuman(3, b.StickOffset) * w : 0f;
            _ambDensity = b != null ? SpellPayload.FromHuman(2, b.DensityOffset) * w : 0f;
            // map biome plus any spell-made air standing here (lingers, lvl3)
            _ambAffinity = (b != null ? SpellPayload.FromHuman(5, b.AffinityOffset) * w : 0f)
                + ArtificialBiome.SampleAt(transform.position).Affinity;
            _ambPhase = b != null ? b.NaturalPhase : MatterPhase.Gas; // no map = ordinary air
            // no map (the lobby) means nothing is imposed and no capacity is
            // capped - you are simply yourself
            _here = b != null ? b.PayloadAt(transform.position) : Natural;
        }

        /// Recovery speeds up with the size of the effect: a slider pushed to
        /// its extreme sheds up to DriftExpo times faster than a light touch,
        /// so stacked spells cannot leave you stranded for long.
        static float Rush(float dev, float span) =>
            Mathf.Max(1f, Mathf.Abs(dev) / span * DriftExpo);

        // ---- the sliders ----
        /// ★ THE ELEMENT'S, not its own. A player is hot in the same place a
        /// crate is. Before this there were four separate temperatures in the
        /// game and heating one never showed up in the others.
        public float Temp
        {
            get => _el != null ? _el.Data.Temp : _looseTemp;
            set
            {
                if (_el == null) { _looseTemp = value; return; }
                var d = _el.Data; d.Temp = value; _el.Data = d;
            }
        }
        float _looseTemp = NaturalTemp;
        Element _el;
        BodyFx _fx;
        /// ★ THE ELEMENT'S, like Temp: a dark spell, a dark linger and a dark
        /// biome all move the one luminance the body has, so sight dims by
        /// exactly as much as the place is dark. Read in board units around
        /// NaturalLum so the severities keep their scale.
        public float Lum
        {
            get => _el != null ? NaturalLum + (_el.Data.Lum - _el.Natural.Lum) : _looseLum;
            set
            {
                if (_el == null) { _looseLum = value; return; }
                var d = _el.Data; d.Lum = _el.Natural.Lum + (value - NaturalLum); _el.Data = d;
            }
        }
        float _looseLum = NaturalLum;
        public float Grip;          // 0 natural · + sticky · − slick
        public float Weight = 1f;   // mass multiplier

        /// ★ THE ELEMENT'S, like Temp. Hit by attract or repel you CARRY the
        /// axis - your own gravity on everything near - until it drifts home.
        public float Affinity
        {
            get => _el != null ? _el.Data.Affinity : _looseAffinity;
            set
            {
                if (_el == null) { _looseAffinity = value; return; }
                var d = _el.Data; d.Affinity = value; _el.Data = d;
            }
        }
        float _looseAffinity;

        /// The body's own phase - Transparency drops it to liquid, Cloud to
        /// gas. Written through StateView so the art shows it, and read by the
        /// phase cycle (liquid beats solid, gas beats liquid, solid beats gas).
        public MatterPhase Phase { get; private set; } = MatterPhase.Solid;

        /// Seconds left of a spell-forced phase; 0 = back to solid.
        float _phaseLeft;

        /// Push the body into a phase for a while. Solid is the natural home.
        public void SetPhase(MatterPhase p, float seconds)
        {
            Phase = p;
            _phaseLeft = Mathf.Max(_phaseLeft, seconds);
            var view = GetComponentInChildren<StateView>();
            if (view != null) view.Set(p);
        }

        /// Weight of what your arms hold - added to body weight. The movement
        /// gates are the only carry limit.
        public float CarriedWeight;
        public float TotalWeight => Weight + CarriedWeight;

        SimpleFPSController _pilot;
        Creature _creature;
        float _hurtCarry;   // sub-point band damage lands in readable chunks
        float _crushCarry;  // same, for own-weight crushing

        float _slipTick;    // ragdoll-roulette beat while deep slick

        /// Resolve the body a collider belongs to, adding the board on first
        /// contact with the slider law (players and creatures self-heal one).
        public static BodyState Of(Component c)
        {
            if (c == null) return null;
            var found = c.GetComponentInParent<BodyState>();
            if (found != null) return found;
            Component host = c.GetComponentInParent<SimpleFPSController>();
            if (host == null) host = c.GetComponentInParent<Creature>();
            return host == null ? null : host.gameObject.AddComponent<BodyState>();
        }

        void Awake()
        {
            _pilot = GetComponent<SimpleFPSController>();
            // only a player's board ever draws on the screen: the veil gets its own component,
            // because Unity runs OnGUI several times a frame on EVERY component that has one,
            // and golems and zombies carry boards by the hundred
            if (_pilot != null) gameObject.AddComponent<BodyVeil>().Board = this;
            _creature = GetComponent<Creature>();
            _el = GetComponentInParent<Element>();
            _fx = GetComponent<BodyFx>();
            if (_fx == null) _fx = gameObject.AddComponent<BodyFx>(); // the looks, shared with puppets
            _fx.Driver = this;

            // A BODY WITH A BOARD IS ALIVE. Same law as the controller: mind
            // up, warmth up, once. Element's own warmth law does the rest -
            // re-basing temp here on top of it double-added body heat.
            if (_el != null && _el.Natural.Int <= 0f)
            {
                // assert aliveness ONLY - warmth is DeriveFrom's job (see the
                // controller's twin patch for why)
                var n = _el.Natural; n.Int = 1f; n.Courage = Mathf.Max(n.Courage, 1f);
                _el.Natural = n;
                var d = _el.Data; d.Int = 1f; d.Courage = Mathf.Max(d.Courage, 1f);
                _el.Data = d;
                _el.DeriveFrom(transform.position);
            }
        }

        // ---- pushes (the ONLY thing spells are allowed to do) ----
        public void PushTemp(float d)
        {
            if (_creature != null) // creatures: Thermal owns temp - route there
            {
                var col = GetComponentInChildren<Collider>();
                if (col != null) SpellParticle.GiveHeatTo(col, d);
                return;
            }
            Temp = Mathf.Clamp(Temp + d, -60f, 160f);
        }
        public void PushLum(float d) => Lum = Mathf.Clamp(Lum + d, -1.5f, 3f);
        public void PushGrip(float d) => Grip = Mathf.Clamp(Grip + d, -1.4f, 1.6f);
        public void PushWeight(float d) => Weight = Mathf.Clamp(Weight + d, 0.12f, 4f);
        public void PushAffinity(float d) =>
            Affinity = Mathf.Clamp(Affinity + d, -DrawingConfig.AxisCap, DrawingConfig.AxisCap);

        /// Resets every slider to natural HERE - the same biome homes the
        /// drift pulls toward, not bare ordinaries. Used by the sky catch.
        public void ClearSpellEffects()
        {
            if (_el != null)
            {
                var d = _el.Natural;
                d.Strength = _el.Data.Strength; // a rescue is not a heal
                _el.Data = d;
            }
            else { _looseTemp = NaturalTemp; _looseAffinity = _ambAffinity; }
            if (_el == null) _looseLum = NaturalLum + _ambLight; // element bodies: reset above
            Grip = _ambStick;
            Weight = 1f + _ambDensity;
            SetPhase(MatterPhase.Solid, 0f);
        }

        // ---- readings ----
        public bool FrozenSolid => _pilot != null && Temp <= FrozenSolidAt;
        // severity creep starts near natural temp; the damage band is unchanged
        public float BurnSeverity => BurnOf(Temp);
        public float FreezeSeverity => FreezeOf(Temp);
        /// 0 = normal sight · 1 = pitch black (this IS the vision reduction)
        public float DarknessSeverity => DarknessOf(Lum);
        public float BloomSeverity => BloomOf(Lum);
        // the same bands for a body without a board (a puppet reads its Element)
        public static float BurnOf(float temp) => Mathf.Clamp01((temp - 41f) / 42f);
        public static float FreezeOf(float temp) => Mathf.Clamp01((33f - temp) / 42f);
        public static float DarknessOf(float lum) => Mathf.Clamp01((NaturalLum - lum) / (NaturalLum + 0.55f));
        public static float BloomOf(float lum) => Mathf.Clamp01((lum - NaturalLum - 0.15f) / 1.1f);

        /// STRENGTH IS THE OLD HP - one stat for players, creatures and
        /// scenery. A player's ceiling comes from Sides (side, buffs, the
        /// ground); everything else reads its own Element, which owns the
        /// definition. 0.35..1, never 0.
        public float StrengthMul
        {
            get
            {
                if (_pilot != null)
                {
                    float f = Sides.StrengthFraction(Grimoire.LocalPlayerId, _pilot.Health);
                    float coupled = _el != null ? _el.CoupledStrengthMul
                        : Mathf.Clamp(1f + SpellPayload.EffectCoupling(6, BoardDeviation())
                            / DrawingConfig.AxisCap, 0.55f, 1.6f);
                    return Mathf.Lerp(DrawingConfig.StrengthFloorMul, 1f, f) * coupled;
                }
                if (_dmg == null) _dmg = GetComponent<Element>();
                return _dmg != null ? _dmg.StrengthMul : 1f;
            }
        }

        Element _dmg;

        /// WEIGHT MEASURED AGAINST STRENGTH. The same load crushes a wounded
        /// body and is carried by a healthy one - wounds make the world
        /// heavier.
        /// A BODY AT ITS NATURAL WEIGHT IS NEVER CRUSHED BY ITSELF, however
        /// hurt it is: only weight ABOVE natural is measured against strength.
        /// Without that, being nearly dead was enough to buckle a zombie that
        /// was carrying nothing at all.
        public float Load
        {
            get
            {
                float excess = Mathf.Max(0f, TotalWeight - 1f);
                return 1f + excess / Mathf.Max(0.05f, StrengthMul);
            }
        }

        /// THE DENSITY OF WHAT YOU ARE STANDING IN. A biome is normally gas,
        /// sometimes liquid, and its phase sets the base while its
        /// INT, and anything else that is a CAPACITY (courage alone follows the
        /// ground both ways, SpellPayload.GroundTarget). They do not
        /// work like Temp: a mindless place drags a sharp mind down, but a
        /// clever place never makes a stupid thing clever. That is
        /// min(what you are, what the place allows) - and the ONE function
        /// that knows the difference is SpellPayload.TargetFor.
        public float Int { get; private set; } = 1f;
        public float Courage { get; private set; } = 1f;

        /// What this body was BORN as. Stamped once; thresholds measure from
        /// here, so a naturally fearless thing is not the same as a brave one
        /// standing somewhere safe.
        public SpellPayload Natural = new SpellPayload { Int = 1f, Courage = 1f };

        /// The board sliders as a data deviation from natural - what the
        /// coupling table reads for bodies without an element of their own.
        SpellPayload BoardDeviation() => new SpellPayload
        {
            Lum = Lum - NaturalLum,
            Pressure = Weight - 1f,
            Balance = Grip,
            State = SpellPayload.FromHuman(4,
                Phase == MatterPhase.Solid ? 0f : Phase == MatterPhase.Liquid ? -50f : -120f),
            Affinity = Affinity,
        };

        void DriftCapacities(float dt)
        {
            var here = _here;
            float rate = DrawingConfig.CapacityDriftPerSec * dt;

            var dev = BoardDeviation();

            Int = Mathf.MoveTowards(Int,
                SpellPayload.TargetFor(7, Natural.Int, here.Int)
                + SpellPayload.EffectCoupling(7, dev), rate);
            Courage = Mathf.MoveTowards(Courage,
                SpellPayload.GroundTarget(8, Natural.Courage, here.Courage)
                + SpellPayload.EffectCoupling(8, dev), rate);
        }

        /// A spell pushes a capacity directly; drift then pulls it back toward
        /// what the ground allows, which is why a spell lasts as long as the
        /// place agrees with it and no longer.
        public void PushInt(float d) => Int = Mathf.Clamp(Int + d, 0f, 4f);
        public void PushCourage(float d) => Courage = Mathf.Clamp(Courage + d, 0f, 4f);

        /// DensityOffset shifts it - so thin peak air is just a negative
        /// offset on a box stacked high, never altitude maths.
        /// SWIMMING IS NOT A MODE: water is simply a medium dense enough to
        /// hold you, so the same number that makes a light body drift makes a
        /// normal body float in a lake.
        public float MediumDensity
        {
            get
            {
                float baseD = _ambPhase == MatterPhase.Liquid ? DrawingConfig.LiquidMediumDensity
                            : _ambPhase == MatterPhase.Solid ? DrawingConfig.SolidMediumDensity
                            : FloatBelow;                       // gas: the ordinary air
                return Mathf.Max(0f, baseD + _ambDensity);
            }
        }

        /// Nothing flies and nothing "enters swim mode". Gravity weakens as
        /// the medium closes on your own weight, and once the medium is the
        /// denser of the two it goes negative and you rise - helium in air,
        /// a body bobbing up in water, the same arithmetic.
        public float GravityMul
        {
            get
            {
                float med = MediumDensity;
                return Mathf.Max(DrawingConfig.FloatRiseMax,
                    (TotalWeight - med) / Mathf.Max(0.01f, 1f - med));
            }
        }

        /// How much you can push yourself around inside the medium: none when
        /// it barely holds you, full when it carries you. This is what makes
        /// water swimmable and thin air not - one reading, no swim flag.
        public float MediumControl => Mathf.Clamp01(1f - GravityMul);

        /// Dense enough around you to move through rather than fall through.
        public bool Swimmable => MediumControl >= DrawingConfig.SwimAt;
        public bool CanSprint => Load < RunLimit;
        public bool CanJump => Load < JumpLimit;
        public bool CrawlOnly => Load >= WalkLimit;
        /// Past this your own body is more than you can hold up: strength
        /// bleeds away because you are carrying yourself and losing.
        public bool Crushing => Load >= CrushLimit;
        /// Too weak to stand at all - the legs go.
        public bool Collapsing => Load >= CollapseLimit;
        /// Lighter bodies spring higher and fall softer; heavy is gated, not nerfed.
        public float JumpMul => !CanJump ? 0f
            : TotalWeight < 1f ? Mathf.Lerp(1.5f, 1f, TotalWeight) : 1f;

        /// One speed multiplier from the whole board, players and zombies
        /// both. Never returns zero - slows are proportional.
        /// Set each frame by the pilot. Balance is friction with the ground:
        /// airborne, the planted slow does not exist (his rule).
        public bool Grounded = true;

        public float SpeedMul
        {
            get
            {
                float m = 1f;
                if (Grounded && Grip > GripSlowAt)
                    m *= Mathf.Lerp(1f, 0.09f, Mathf.InverseLerp(GripSlowAt, 1.5f, Grip));
                m *= Mathf.Lerp(1f, 0.3f, FreezeSeverity);  // being frozen IS the slow
                if (FrozenSolid) m *= 0.15f;                // frozen stiff: a shuffle, never a statue
                return m;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // ---- the drift home: the environment IS the cure ----
            // HOME IS THE BIOME. Its offsets are 0 by default, so an
            // unauthored place (and the whole lobby, which has no map) drifts
            // to the same naturals as before. A cold peak drags you under the
            // band and the existing damage does the rest.
            TickAmbient(dt);

            // a forced phase wears off back to solid, the body's natural home
            if (_phaseLeft > 0f)
            {
                _phaseLeft -= dt;
                if (_phaseLeft <= 0f) SetPhase(MatterPhase.Solid, 0f);
            }
            if (_pilot != null && _el == null)
            {
                // temperature and luminance drift on the ELEMENT beat - a
                // second loop here pulled against it. Only a board with no
                // element homes its own light.
                float homeLum = NaturalLum + _ambLight;
                _looseLum = Mathf.MoveTowards(_looseLum, homeLum,
                    LumDriftPerSec * Rush(_looseLum - homeLum, 1.5f) * dt);
            }
            Grip = Mathf.MoveTowards(Grip, _ambStick, GripDriftPerSec * Rush(Grip - _ambStick, 1.5f) * dt);
            // the balloon and the feel share one source: while a spell holds
            // the element pressure down you STAY light (floaty falls, higher
            // jumps, a real float when really light), for exactly as long as
            // the body visibly shows it
            float spellW = _el != null
                ? SpellPayload.ToHuman(2, _el.Data.Pressure - _el.Natural.Pressure) * 0.01f
                : 0f;
            float homeW = 1f + _ambDensity + spellW;
            Weight = Mathf.MoveTowards(Weight, homeW, WeightDriftPerSec * Rush(Weight - homeW, 0.9f) * dt);
            // element bodies shed and radiate affinity on the element beat;
            // loose bodies do both here - a high-affinity biome makes the
            // things standing in it the magnets, never its own center
            if (_el == null)
            {
                _looseAffinity = Mathf.MoveTowards(_looseAffinity, _ambAffinity, SpellLaw.RateFor(5) * dt);
                if (Mathf.Abs(_looseAffinity) > 0.05f)
                    SpellParticle.AffinityField(transform, _looseAffinity, dt);
            }

            DriftCapacities(dt);


            // TEMP BAND DAMAGE MOVED TO Element. It used to live here because
            // "Thermal burns creatures" and players were the exception; now one
            // law burns everything, and leaving this would bill a player twice.

            // ---- crushed by your own weight (strength IS health) ----
            // Past the crush limit you are holding up more than you can, and
            // it costs you: strength bleeds, which raises Load further, which
            // bleeds faster. Get lighter or get out. Past collapse the legs
            // simply go - too weak to stand.
            if (_pilot != null && !_pilot.IsDead && !_pilot.IsDowned && Crushing)
            {
                float over = Load - CrushLimit;
                _crushCarry += (1f + over) * CrushDrainPerSec * dt;
                if (_crushCarry >= 0.75f)
                {
                    _pilot.TakeHit(Vector3.zero, _crushCarry, "crushed by your own weight");
                    _crushCarry = 0f;
                }
                if (Collapsing) _pilot.KnockDown(0.6f); // re-applied while it lasts
            }

            // ---- slick: depth drives ragdoll odds, pace and sprawl ----
            if (Grip < SlickSlideAt)
            {
                float depth = Mathf.InverseLerp(SlickSlideAt, SlickDeepAt, Grip); // 0 faint … 1 soap hell
                _slipTick -= dt;
                if (_slipTick <= 0f)
                {
                    _slipTick = Mathf.Lerp(1.7f, 0.75f, depth);
                    // HIS BALANCE LAW: speed is what costs you. Move slowly
                    // and even deep slick holds; run and you lose it.
                    float v = _pilot != null ? _pilot.Velocity.magnitude
                        : _creature != null && _creature.TryGetComponent<Rigidbody>(out var crb)
                            ? crb.linearVelocity.magnitude : 2f;
                    float pace = Mathf.Clamp01(v / 4.5f);
                    if (Random.value < Mathf.Lerp(0.08f, 0.75f, depth) * pace)
                    {
                        float sprawl = Mathf.Lerp(0.45f, 1.6f, depth);
                        if (_pilot != null) _pilot.KnockDown(sprawl);
                        else _creature?.KnockDown(sprawl);
                    }
                }
            }

            // ---- creatures read the shared sliders through their own laws ----
            if (_creature != null)
            {
                if (Grip >= GripStuckAt) _creature.ApplyStuck(0.3f);
                else if (Grip <= SlickSlideAt) _creature.ApplySlip(0.3f);
            }

            if (_pilot != null && _fx != null) // the body wears its damage
            {
                _fx.EyeHeight = EyeY;
                float hurt = 1f - Mathf.Clamp01(_pilot.Health / Sides.MaxHealthFor(Grimoire.LocalPlayerId));
                _fx.Tick(BurnSeverity, DarknessSeverity, BloomSeverity, FreezeSeverity, hurt, !_pilot.IsDead);
            }
            SightLog();
        }

        // ---- body FX: allies read your state at a glance ----
        /// The body's REAL eye height - the fallback anchor when a model has
        /// no FX sockets at all (the graybox bean).
        float EyeY => _pilot != null && _pilot.CameraPivot != null
            ? Mathf.Max(0.4f, _pilot.CameraPivot.localPosition.y) : 1.5f;
        // ---- fullscreen tints are the status readout - local player only ----
        float _sightLogAt;
        /// Why the sight is dark or glaring, once per ten seconds, for the Console.
        void SightLog()
        {
            if (_pilot == null || !_pilot.IsLocalViewer || _el == null || Time.time < _sightLogAt) return;
            float dark = DarknessSeverity, bloom = BloomSeverity;
            if (dark < 0.25f && bloom < 0.25f) return;
            _sightLogAt = Time.time + 10f;
            Debug.Log($"[SpellyZombie] sight {(dark >= bloom ? "dark" : "glare")} {Mathf.Max(dark, bloom):0.00}: light here " +
                $"{SpellPayload.ToHuman(1, _el.Data.Lum - _el.Natural.Lum):+0;-0} from home '{_el.BornName}', " +
                $"standing in {Biome.NamesAt(transform.position)}");
        }

        static Texture2D _white, _tunnel;
        GhostState _ghost;
        /// A ghost is not affected by biomes: the body's readings stay on the body.
        bool Ghosting => (_ghost != null || (_ghost = GetComponent<GhostState>()) != null) && _ghost.IsGhost;

        internal void DrawVeil()
        {
            if (_pilot == null || !_pilot.IsLocalViewer || Ghosting) return;
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            float dark = DarknessSeverity;
            if (dark > 0.01f)
            {
                // sight goes by degrees: the edges close in first as a
                // tunnel, the middle dims behind it, deep dark is near blind
                Tint(full, new Color(0f, 0f, 0.02f, Mathf.Clamp01(dark * dark * 0.85f + dark * 0.1f)));
                if (_tunnel == null) _tunnel = HUD.VignetteTex(0.35f, 0.6f);
                float grow = Mathf.Lerp(2.4f, 0.95f, dark);
                var r = new Rect(Screen.width * (1f - grow) * 0.5f, Screen.height * (1f - grow) * 0.5f,
                    Screen.width * grow, Screen.height * grow);
                Tint(r, new Color(0f, 0f, 0.02f, Mathf.Clamp01(dark * 1.6f)), _tunnel);
            }
            float bloom = BloomSeverity;
            if (bloom > 0.01f) Tint(full, new Color(1f, 1f, 0.94f, Mathf.Min(1f, bloom * 1.06f)));
            float frost = FreezeSeverity;
            if (frost > 0.01f) Tint(full, new Color(0.62f, 0.8f, 1f, frost * 0.34f));
            float burn = BurnSeverity;
            if (burn > 0.01f) Tint(full, new Color(1f, 0.32f, 0.06f, burn * 0.3f));
        }

        static void Tint(Rect r, Color c, Texture2D tex = null)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, tex != null ? tex : _white);
            GUI.color = prev;
        }
    }

    /// The darkness, glare, frost and burn over a player's screen (BodyState.DrawVeil).
    class BodyVeil : MonoBehaviour
    {
        public BodyState Board;

        void Awake() => useGUILayout = false; // it only paints: no layout pass

        void OnGUI()
        {
            if (Board != null && Event.current.type == EventType.Repaint) Board.DrawVeil();
        }
    }
}
