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
    ///   Move   - + speed buff · − inverted inputs.
    /// On creatures only Grip/Weight/Move run; Thermal owns their temperature.
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

        public static readonly float MoveDriftPerSec = Tune("BodyMoveDriftPerSec", 0.45f);

        public static readonly float DriftExpo = Tune("BodyDriftExpo", 3f);

        static float Tune(string key, float def) => DrawingConfig.Overlay(key, def);

        // ---- the biome under my feet, sampled on a beat, not per frame ----
        float _ambHeat, _ambLight, _ambStick, _ambDensity, _ambNext;
        MatterPhase _ambPhase = MatterPhase.Gas;   // what the place is made of

        void TickAmbient(float dt)
        {
            _ambNext -= dt;
            if (_ambNext > 0f) return;
            _ambNext = 0.25f;
            var b = SpellyMap.BiomeAt(transform.position);
            _ambHeat = b != null ? b.HeatOffset : 0f;
            _ambLight = b != null ? b.LightOffset : 0f;
            _ambStick = b != null ? b.StickOffset : 0f;
            _ambDensity = b != null ? b.DensityOffset : 0f;
            _ambPhase = b != null ? b.NaturalPhase : MatterPhase.Gas; // no map = ordinary air
        }

        /// Recovery speeds up with the size of the effect: a slider pushed to
        /// its extreme sheds up to DriftExpo times faster than a light touch,
        /// so stacked spells cannot leave you stranded for long.
        static float Rush(float dev, float span) =>
            Mathf.Max(1f, Mathf.Abs(dev) / span * DriftExpo);

        // ---- the sliders ----
        public float Temp = NaturalTemp;
        public float Lum = NaturalLum;
        public float Grip;          // 0 natural · + sticky · − slick
        public float Weight = 1f;   // mass multiplier
        public float Move;          // 0 center · + arrow buff · − Y inversion

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
            _creature = GetComponent<Creature>();
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
        public void PushMove(float d) => Move = Mathf.Clamp(Move + d, -2.2f, 2.2f);

        /// Resets every slider to natural. Used by the sky catch.
        public void ClearSpellEffects()
        {
            Temp = NaturalTemp;
            Lum = NaturalLum;
            Grip = 0f;
            Weight = 1f;
            Move = 0f;

        }

        // ---- readings ----
        public bool FrozenSolid => _pilot != null && Temp <= FrozenSolidAt;
        // severity creep starts near natural temp; the damage band is unchanged
        public float BurnSeverity => Mathf.Clamp01((Temp - 41f) / 42f);
        public float FreezeSeverity => Mathf.Clamp01((33f - Temp) / 42f);
        /// 0 = normal sight · 1 = pitch black (this IS the vision reduction)
        public float DarknessSeverity => Mathf.Clamp01((NaturalLum - Lum) / (NaturalLum + 0.55f));
        public float BloomSeverity => Mathf.Clamp01((Lum - 1.4f) / 1.4f);

        /// STRENGTH IS THE OLD HP - one stat for players, creatures and
        /// scenery. A player's ceiling comes from Sides (side, buffs, the
        /// ground); everything else reads its own Damageable, which owns the
        /// definition. 0.35..1, never 0.
        public float StrengthMul
        {
            get
            {
                if (_pilot != null)
                {
                    float f = Sides.StrengthFraction(Grimoire.LocalPlayerId, _pilot.Health);
                    return Mathf.Lerp(DrawingConfig.StrengthFloorMul, 1f, f);
                }
                if (_dmg == null) _dmg = GetComponent<Damageable>();
                return _dmg != null ? _dmg.StrengthMul : 1f;
            }
        }

        Damageable _dmg;

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
        /// −1 while a Y owns you: your inputs walk you the other way.
        public float InputSign => Move < -0.05f ? -1f : 1f;

        /// One speed multiplier from the whole board, players and zombies
        /// both. Never returns zero - slows are proportional.
        public float SpeedMul
        {
            get
            {
                float m = 1f;
                if (Grip > GripSlowAt)
                    m *= Mathf.Lerp(1f, 0.09f, Mathf.InverseLerp(GripSlowAt, 1.5f, Grip));
                m *= Mathf.Lerp(1f, 0.3f, FreezeSeverity);  // being frozen IS the slow
                if (FrozenSolid) m *= 0.15f;                // frozen stiff: a shuffle, never a statue
                if (Move > 0.05f) m *= 1f + Move * 0.5f;    // arrow buff
                else if (Move < -0.05f)
                    m *= Mathf.Clamp(-Move, 0.15f, 1.3f);   // small Y = slow backwards walk
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
            if (_pilot != null)
            {
                float homeTemp = NaturalTemp + _ambHeat;
                float homeLum = NaturalLum + _ambLight;
                Temp = Mathf.MoveTowards(Temp, homeTemp, TempDriftPerSec * Rush(Temp - homeTemp, 40f) * dt);
                Lum = Mathf.MoveTowards(Lum, homeLum, LumDriftPerSec * Rush(Lum - homeLum, 1.5f) * dt);
            }
            Grip = Mathf.MoveTowards(Grip, _ambStick, GripDriftPerSec * Rush(Grip - _ambStick, 1.5f) * dt);
            Weight = Mathf.MoveTowards(Weight, 1f + _ambDensity, WeightDriftPerSec * Rush(Weight - 1f - _ambDensity, 0.9f) * dt);
            Move = Mathf.MoveTowards(Move, 0f, MoveDriftPerSec * Rush(Move, 2.2f) * dt);


            // ---- temp band damage (players only - Thermal burns creatures) ----
            if (_pilot != null && !_pilot.IsDead)
            {
                float off = Temp < TempBandLow ? TempBandLow - Temp
                    : Temp > TempBandHigh ? Temp - TempBandHigh : 0f;
                if (off > 0f)
                {
                    _hurtCarry += off * TempDamagePerDegree * dt;
                    if (_hurtCarry >= 0.75f)
                    {
                        _pilot.TakeHit(Vector3.zero, _hurtCarry,
                            Temp < NaturalTemp ? "freezing" : "burning");
                        _hurtCarry = 0f;
                    }
                }
            }

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
                    if (Random.value < Mathf.Lerp(0.08f, 0.75f, depth))
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

            if (_pilot != null) UpdateBodyFx(); // the body wears its damage
        }

        // ---- body FX: allies read your state at a glance ----
        readonly GameObject[] _bodyFlames = new GameObject[3];
        readonly GameObject[] _eyeWisps = new GameObject[2];
        readonly GameObject[] _eyeGlares = new GameObject[2];
        float _iceFxTick, _bleedTick;

        /// The body's REAL eye height - the fallback anchor when a model has
        /// no FX sockets at all (the graybox bean).
        float EyeY => _pilot != null && _pilot.CameraPivot != null
            ? Mathf.Max(0.4f, _pilot.CameraPivot.localPosition.y) : 1.5f;

        // FX sockets: empties named Socket_Burn / Socket_Freeze / Socket_Bleed
        // / Socket_Eyes, several of each fine; effects spawn as children so
        // they ride the bones. No sockets = bone/eye-height fallbacks;
        // authored sockets always win.
        readonly List<Transform> _burnS = new List<Transform>();
        readonly List<Transform> _freezeS = new List<Transform>();
        readonly List<Transform> _bleedS = new List<Transform>();
        readonly List<Transform> _eyeS = new List<Transform>();
        float _socketScan;

        void ResolveSockets()
        {
            // bodies build/rebuild at runtime (CharacterRig, bakes) - rescan
            // when stale, at most every 2s
            bool stale = _burnS.Count == 0 || _burnS[0] == null;
            if (!stale || Time.time < _socketScan) return;
            _socketScan = Time.time + 2f;
            _burnS.Clear(); _freezeS.Clear(); _bleedS.Clear(); _eyeS.Clear();

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Socket_Burn")) _burnS.Add(t);
                else if (t.name.StartsWith("Socket_Freeze")) _freezeS.Add(t);
                else if (t.name.StartsWith("Socket_Bleed")) _bleedS.Add(t);
                else if (t.name.StartsWith("Socket_Eyes")) _eyeS.Add(t);
            }
            // fallbacks fill only the EMPTY categories, on the bones themselves:
            // spine, chest, head, shoulders, hips, legs - never the arms (their
            // bones point the wrong way). A small forward nudge keeps effects
            // on the skin, not inside the ribs.
            var anim = GetComponentInChildren<Animator>();
            Transform B(HumanBodyBones b) =>
                anim != null && anim.isHuman ? anim.GetBoneTransform(b) : null;
            Vector3 fwd = transform.forward;
            Vector3 root = transform.position;
            float e = EyeY;

            Transform Mk(string n, Transform bone, float beanY)
            {
                var go = new GameObject(n);
                go.transform.SetParent(bone != null ? bone : transform, false);
                // world position: at the bone (or a body-height fraction on
                // the bean), pushed slightly out the character's front
                go.transform.position = bone != null
                    ? bone.position + fwd * 0.07f
                    : root + Vector3.up * (beanY * e) + fwd * (0.09f * e);
                return go.transform;
            }

            var chest = B(HumanBodyBones.Chest);
            var spine = B(HumanBodyBones.Spine);
            var head = B(HumanBodyBones.Head);
            var hips = B(HumanBodyBones.Hips);
            var shoulder = B(HumanBodyBones.RightShoulder);

            if (_burnS.Count == 0)
            {
                _burnS.Add(Mk("Socket_Burn_Auto", chest, 0.62f));
                _burnS.Add(Mk("Socket_Burn_Auto2", spine, 0.45f));
                // third flame high - shoulder first, head only as its stand-in
                _burnS.Add(Mk("Socket_Burn_Auto3", shoulder != null ? shoulder : head, 0.8f));
            }
            if (_freezeS.Count == 0)
            {
                _freezeS.Add(Mk("Socket_Freeze_Auto", chest, 0.55f));
                _freezeS.Add(Mk("Socket_Freeze_Auto2", hips, 0.35f));
            }
            if (_bleedS.Count == 0)
            {
                _bleedS.Add(Mk("Socket_Bleed_Auto", spine, 0.5f));
                _bleedS.Add(Mk("Socket_Bleed_Auto2", chest, 0.62f));
            }
            if (_eyeS.Count == 0)    // the one legitimate face socket
            {
                var go = new GameObject("Socket_Eyes_Auto");
                go.transform.SetParent(head != null ? head : transform, false);
                go.transform.localPosition = head != null
                    ? new Vector3(0f, 0.04f, 0.09f) : new Vector3(0f, e, 0.1f * e);
                _eyeS.Add(go.transform);
            }
        }

        /// CFXR prefabs ignore a small localScale unless their particle
        /// systems scale with the hierarchy - force it, then shrink for real.
        /// Sockets live on BONES, and bones carry rig scale (FBX armatures are
        /// often 0.01, baked parts can be anything) - cancel the parent's
        /// scale so the WORLD size is the size we asked for, on every rig.
        static GameObject Fit(GameObject fx, float scale)
        {
            if (fx == null) return null;
            var p = fx.transform.parent != null ? fx.transform.parent.lossyScale : Vector3.one;
            var s = fx.transform.localScale * scale;
            fx.transform.localScale = new Vector3(
                s.x / Mathf.Max(0.0001f, Mathf.Abs(p.x)),
                s.y / Mathf.Max(0.0001f, Mathf.Abs(p.y)),
                s.z / Mathf.Max(0.0001f, Mathf.Abs(p.z)));
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            return fx;
        }

        void UpdateBodyFx()
        {
            var lib = FxLibrary.I;
            if (lib == null) return;

            ResolveSockets();

            // every status: socket-mounted, same size family, severity read as
            // count first, pace second, slight growth third

            // burning: 1..3 small licks, one per Socket_Burn
            int want = BurnSeverity > 0.55f ? 3 : BurnSeverity > 0.28f ? 2 : BurnSeverity > 0.08f ? 1 : 0;
            for (int i = 0; i < _bodyFlames.Length; i++)
            {
                bool on = i < want && i < _burnS.Count && _burnS[i] != null;
                if (on && _bodyFlames[i] == null && lib.Fire != null)
                {
                    var fx = Fit(Instantiate(lib.Fire, _burnS[i]),
                        Mathf.Lerp(0.16f, 0.26f, BurnSeverity));
                    fx.name = "BodyFlame";
                    fx.transform.localPosition = Vector3.zero; // the socket IS the spot
                    // bones carry arbitrary axes - start the effect world-upright
                    fx.transform.rotation = Quaternion.identity;
                    _bodyFlames[i] = fx;
                }
                else if (!on && _bodyFlames[i] != null)
                {
                    Destroy(_bodyFlames[i]);
                    _bodyFlames[i] = null;
                }
            }

            // darkness: 1-2 wisps at the eyes; glare mirrors it in white
            int wisps = DarknessSeverity > 0.55f ? 2 : DarknessSeverity > 0.18f ? 1 : 0;
            int glares = BloomSeverity > 0.55f ? 2 : BloomSeverity > 0.18f ? 1 : 0;
            EyeFx(_eyeWisps, wisps, lib.Smoke, 0.08f, "EyeDark");
            EyeFx(_eyeGlares, glares, lib.HealShine, 0.09f, "EyeGlare");

            // bleeding is the HP readout: drips come more and faster as HP falls
            float hurt = _pilot != null
                ? 1f - Mathf.Clamp01(_pilot.Health / Sides.MaxHealthFor(Grimoire.LocalPlayerId))
                : 0f;
            if (hurt > 0.25f && _pilot != null && !_pilot.IsDead)
            {
                _bleedTick -= Time.deltaTime;
                if (_bleedTick <= 0f)
                {
                    _bleedTick = Mathf.Lerp(2.2f, 0.6f, hurt);
                    int drips = hurt > 0.75f ? 3 : hurt > 0.5f ? 2 : 1;
                    for (int i = 0; i < drips && _bleedS.Count > 0; i++)
                    {
                        var s = _bleedS[Random.Range(0, _bleedS.Count)];
                        if (s == null) continue;
                        Fit(FxLibrary.Spawn(lib.Blood, s.position, s, 2.5f),
                            Mathf.Lerp(0.12f, 0.2f, hurt)); // small smears - count+pace tell the story
                    }
                }
            }

            // freezing: ice crystals FORM on you on a beat - faster and
            // bigger the deeper you are
            if (FreezeSeverity > 0.08f)
            {
                _iceFxTick -= Time.deltaTime;
                if (_iceFxTick <= 0f)
                {
                    _iceFxTick = Mathf.Lerp(1.4f, 0.45f, FreezeSeverity);
                    if (_freezeS.Count > 0)
                    {
                        var s = _freezeS[Random.Range(0, _freezeS.Count)];
                        if (s != null)
                            Fit(FxLibrary.Spawn(lib.IceHit, s.position, s, 2f),
                                Mathf.Lerp(0.12f, 0.24f, FreezeSeverity)); // crystals ON the skin
                    }
                }
            }
        }

        /// One eye-status loop for wisps and glares.
        void EyeFx(GameObject[] cache, int want, GameObject prefab, float scale, string fxName)
        {
            for (int i = 0; i < cache.Length; i++)
            {
                bool on = i < want && _eyeS.Count > 0 && _eyeS[0] != null;
                if (on && cache[i] == null && prefab != null)
                {
                    // two authored eye sockets = one per eye; one = split around it
                    var anchor = _eyeS[Mathf.Min(i, _eyeS.Count - 1)];
                    var fx = Fit(Instantiate(prefab, anchor), scale);
                    fx.name = fxName;
                    fx.transform.position = _eyeS.Count > 1 ? anchor.position
                        : anchor.position + transform.right * (i == 0 ? 0.05f : -0.05f);
                    fx.transform.rotation = Quaternion.identity; // head-bone axes lie
                    cache[i] = fx;
                }
                else if (!on && cache[i] != null)
                {
                    Destroy(cache[i]);
                    cache[i] = null;
                }
            }
        }

        // ---- fullscreen tints are the status readout - local player only ----
        static Texture2D _white;
        void OnGUI()
        {
            if (_pilot == null || !_pilot.IsLocalViewer) return;
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            float dark = DarknessSeverity;
            if (dark > 0.01f) Tint(full, new Color(0f, 0f, 0.02f, Mathf.Min(1f, dark * 1.06f)));
            float bloom = BloomSeverity;
            if (bloom > 0.01f) Tint(full, new Color(1f, 1f, 0.94f, Mathf.Min(1f, bloom * 1.06f)));
            float frost = FreezeSeverity;
            if (frost > 0.01f) Tint(full, new Color(0.62f, 0.8f, 1f, frost * 0.34f));
            float burn = BurnSeverity;
            if (burn > 0.01f) Tint(full, new Color(1f, 0.32f, 0.06f, burn * 0.3f));
        }

        static void Tint(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }
    }
}
