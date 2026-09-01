using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ ONE ELEMENT IN THE WORLD. A wall, a crate, a zombie and a player are
    /// the same kind of thing: something that holds the ten numbers. The whole
    /// game is transferring those numbers between objects until one of them
    /// crosses a line.
    ///
    /// There is no such thing as "damageable". Strength IS health, health is
    /// one of the ten, and anything carrying the numbers can lose them - which
    /// is why nothing in the world is exempt from breaking.
    ///
    /// It starts as its OWN natural state plus the natural state of the biome
    /// it was born in, and from then on the ground it stands on drifts it.
    public class Element : MonoBehaviour, ISpellData
    {
        [Tooltip("WHAT THIS THING NATURALLY IS, before any place or spell touches it. " +
                 "The biome it is born in adds its own on top. Strength here is its health ceiling.")]
        /// Strength starts at 0 ON PURPOSE. A prefab authored before this field
        /// existed arrives with the initializer intact, so a non-zero default
        /// here would look already-migrated and quietly replace an authored
        /// 40-health crate with 100. Zero means "nobody has said yet", and the
        /// old Health value fills it in at Awake.
        /// Int and Courage start at ZERO, because most things in the world are
        /// a wall or a crate and neither has a mind. A default of 1 made every
        /// element read as ALIVE, which meant poison ate the scenery - and
        /// "living" is exactly Int > 0 by his ruling, so the default IS the
        /// answer to that question.
        public SpellPayload Natural = new SpellPayload();

        /// WHAT IT IS NOW. Runtime only - the authored numbers are Natural.
        [System.NonSerialized] public SpellPayload Data;

        SpellPayload ISpellData.Natural => Natural;
        SpellPayload ISpellData.Data { get => Data; set => Data = value; }

        /// WHO DID THIS TO ME. Set by whatever last pushed numbers in, and
        /// passed on again when this thing spreads. -1 until somebody does.
        [System.NonSerialized] public int Owner = -1;
        int ISpellData.Owner { get => Owner; set => Owner = value; }

        // What older prefabs and scenes authored. Read once at Awake and then
        // never again; Strength on the payload is the only storage.
        [UnityEngine.Serialization.FormerlySerializedAs("Health")]
        [SerializeField, HideInInspector] float _authoredHealth = 100f;
        [UnityEngine.Serialization.FormerlySerializedAs("MaxStrength")]
        [SerializeField, HideInInspector] float _authoredMax;

        [Tooltip("Does the object itself go when it reaches zero? Almost always yes - " +
                 "debris and stumps are what replace it. OFF only when something ELSE owns " +
                 "the removal: a network proxy waiting for snapshots, or the player " +
                 "controller which owns its own downs. This is NOT invulnerability - it was " +
                 "auto-set from 'has a Rigidbody', which is why static walls could be beaten " +
                 "to zero and go on standing there.")]
        public bool RemoveOnDeath = true;

        /// Health IS the Strength axis. Not a copy of it, not synced to it.
        public float Health { get => Data.Strength; set => Data.Strength = value; }

        /// True while the body is downed: damage passes through it entirely
        /// until a revive clears it. The controller owns both edges.
        public bool DeadStill;

        /// Its own ceiling, which is what it was naturally born with.
        public float MaxStrength { get => Natural.Strength; set => Natural.Strength = value; }

        /// 0..1 of its ceiling. Anything that scales with strength reads this.
        public float StrengthFraction =>
            MaxStrength <= 0f ? 1f : Mathf.Clamp01(Health / MaxStrength);

        /// A creature's ceiling comes from its BODY: bigger and heavier means
        /// stronger. Size counts more than mass (a big light thing is still
        /// strong), and the biome it was raised in caps the result.
        /// One definition, used by zombies and golems alike.
        /// Mass is taken as a ROOT, not straight: a body eight times heavier is
        /// a few times tougher, not eight. Linear mass ran away the moment
        /// something heavy existed - a scale-2 golem massing 360 came out with
        /// thousands of strength.
        public static float StrengthFromBody(float sizeMul, float massKg) =>
            DrawingConfig.BodyStrengthBase
            * Mathf.Pow(Mathf.Max(0.05f, sizeMul), DrawingConfig.BodyStrengthSizePower)
            * (1f + Mathf.Sqrt(Mathf.Max(0f, massKg)) * DrawingConfig.BodyStrengthPerKg);

        /// Set the ceiling from the body and fill it. Call once, after the
        /// thing has its final scale and mass.
        public void SetStrengthFromBody(float sizeMul, float massKg)
        {
            MaxStrength = Mathf.Max(1f, StrengthFromBody(sizeMul, massKg));
            Health = MaxStrength;
            NaturalMass = massKg;   // this IS its own weight from here on
        }

        /// The multiplier the world uses: never 0, so a nearly-dead thing is
        /// feeble rather than inert.
        public float StrengthMul =>
            Mathf.Lerp(DrawingConfig.StrengthFloorMul, 1f, StrengthFraction)
            * CoupledStrengthMul;

        /// Pressure and balance shift EFFECTIVE strength (his coupling
        /// table): compressed = mightier, planted = gentler; measured from
        /// this thing's own natural.
        public float CoupledStrengthMul =>
            Mathf.Clamp(1f + SpellPayload.EffectCoupling(6, Data - Natural)
                / DrawingConfig.AxisCap, 0.55f, 1.6f);

        /// Fired once, just before the object is removed (cause string passed).
        public System.Action<string> OnDeath;

        /// Fired on every hit (amount, cause) - lets AI flinch / interrupt casts.
        public System.Action<float, string> OnDamaged;

        float _logAccum;
        bool _dead;

        Rigidbody _body;

        // authored scene furniture (present at load) is what the lobby
        // rebuilds; runtime spawns - zombies, matter, debris - die for real
        bool _authored;

        void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _authored = Time.timeSinceLevelLoad < 1f;

            // what older prefabs authored, read across once
            if (Natural.Strength <= 0f)
                Natural.Strength = Mathf.Max(1f,
                    _authoredMax > 0f ? _authoredMax : _authoredHealth);

            // BORN AS ITSELF PLUS THE GROUND. A rock raised in a forest is a
            // forest rock for life - that is what it teaches, radiates and
            // resists, and its own ceiling is capped by what the place allows.
            _authoredNat = Natural;   // pre-ground snapshot, so a later spawn
            _snapped = true;          // point can re-derive without doubling
            DeriveFrom(transform.position);

            // A COLLIDER IS WHAT MAKES A THING INTERACTABLE. Without one this
            // element cannot be hit, burnt, lifted or touched by anything, so
            // it is not really in the world - say so rather than sitting there
            // looking fine.
            if (GetComponentInChildren<Collider>(true) == null)
                Debug.LogWarning($"[SpellyZombie] {name}: Element with NO COLLIDER - " +
                    "nothing can ever reach it.", this);
            if (_body != null) NaturalMass = _body.mass;

            // ITS NETWORK IDENTITY IS A PROPERTY OF THE OBJECT, not something
            // looked up when a hit lands. Authored scene things derive theirs
            // from their own path, so every machine computes the SAME id with
            // nothing sent. Runtime spawns take the host's instance id, which
            // is what the creature snapshots already carry, and a client's
            // stand-in is stamped with it when it is built.
            if (NetId == 0) NetId = _authored ? PathId(transform) : GetInstanceID();
            _byId[NetId] = this;
        }

        SpellPayload _authoredNat;
        bool _snapped;

        /// Natural = who I am plus the ground at a spawn point - the HOME
        /// biome (his rule). Awake derives from wherever the object woke;
        /// call again with the real spawn and this wins. Re-derives from the
        /// authored snapshot, so repeating it never doubles the ground.
        /// TEMPERATURE IS REAL DEGREES: room temperature is the floor, and
        /// anything with a mind runs warmer - flesh is 37, a crate is 18.
        public void DeriveFrom(Vector3 at)
        {
            if (!_snapped) return;
            bool alive = Natural.Alive;      // an "is alive" patch survives
            float str = Natural.Strength;    // authored or controller-set
            var n = _authoredNat;
            n.Strength = str;

            var born = SpellyMap.BiomeAt(at);
            if (born != null)
            {
                var ground = born.Natural;
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                    n[i] = SpellPayload.TargetFor(i, n[i], n[i] + ground[i]);
                if (born.StrengthCap > 0f)
                    n.Strength = Mathf.Min(n.Strength, born.StrengthCap);
            }

            if (alive)
            {
                n.Int = Mathf.Max(n.Int, 1f);
                n.Courage = Mathf.Max(n.Courage, 1f);
            }
            n.Temp += RoomTemp + (n.Int > 0f ? BodyWarmth : 0f);
            Natural = n;
            Data = n;                        // born here, born full
        }

        void OnDestroy()
        {
            if (_byId.TryGetValue(NetId, out var d) && d == this) _byId.Remove(NetId);
            _live.Remove(this);
        }

        void OnEnable() { if (!_live.Contains(this)) _live.Add(this); }
        void OnDisable() { _live.Remove(this); }

        // ---- THE WORLD'S BEAT ----------------------------------------------
        // Every element in the world on ONE driver rather than a MonoBehaviour
        // Update each: a map can hold thousands of props, and thousands of
        // Updates cost real milliseconds even when every one of them returns
        // immediately.
        static readonly List<Element> _live = new List<Element>();

        /// Everything in the world, for the host's state snapshot.
        public static IReadOnlyList<Element> Live => _live;
        long _beat = -1;
        StateView _view;

        /// How often an element answers to the world. Fine enough that fire
        /// feels immediate, coarse enough that a full map is cheap.
        public const float BeatSeconds = 0.2f;

        /// What an ordinary place is, so the burn and freeze thresholds keep
        /// the same MEANING once they are read as a distance from natural
        /// rather than as absolutes.
        public const float RoomTemp = 18f;

        /// Flesh runs warm: RoomTemp + this = 37, the temperature the living
        /// comfort band brackets. Only minded things carry it.
        public const float BodyWarmth = 19f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            // NO Clear() here. This runs AFTER the first scene awake/enable
            // pass, so clearing would drop every element already in the scene
            // and nothing would ever beat. Dead entries are swept in BeatAll.
            var loop = new GameObject("~ElementBeat") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(loop);
            loop.AddComponent<ElementBeat>();
        }

        /// One pass over everything in the world. Each element answers on its
        /// own phase of the shared clock, so the work spreads across frames
        /// instead of every prop on the map thinking at once.
        static long _pushBeat = -1;

        internal static void BeatAll()
        {
            // THE HOST SIMULATES, EVERYONE APPLIES. A client drifting its own
            // numbers would decide independently that a thing was burning and
            // fire a hurt-intent every beat for every burning object on the
            // map - and its idea of "hot" would not even match the host's.
            // Solo is the host with nobody connected, so this is not a
            // single-player branch.
            if (!NetGame.IsAuthority) return;

            // the picture goes out on its own slower beat - what things ARE
            // changes far less often than the simulation ticks
            if (WorldClock.IsBeat(DrawingConfig.StateSyncSeconds, 0, ref _pushBeat))
                NetSync.PushElementState();

            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var e = _live[i];
                if (e == null) { _live.RemoveAt(i); continue; }
                if (!WorldClock.IsBeat(BeatSeconds, e.NetId, ref e._beat)) continue;
                e.Beat(BeatSeconds);
            }
        }

        /// WHERE IT STANDS CHANGES WHAT IT IS, and what it is decides what
        // ★ THE AXES ARE PHYSICAL ON EVERYTHING (his rule: spells must be fun
        // no matter where they are cast): Pressure IS weight, Balance IS grip.
        // Compressed things get heavy and hit like trucks, spread things fly;
        // sticky things stop dead and hold, slick things slide. Read from the
        // deviation between what a thing IS and what it naturally is, so the
        // biome's own nature never punishes what belongs there.
        float _baseMass = -1f, _baseDamp = -1f, _balNow;
        System.Collections.Generic.List<FixedJoint> _glue;
        Collider[] _physCols;
        PhysicsMaterial[] _physOrig;
        bool _physSwapped;
        static PhysicsMaterial _grippyMat, _slickMat;

        void ApplyPhysicalAxes()
        {
            float press = SpellPayload.ToHuman(2, Data.Pressure - Natural.Pressure) / 100f;
            float bal = SpellPayload.ToHuman(3, Data.Balance - Natural.Balance) / 100f;
            AxisTellFx(press, bal); // the tell shows on EVERYONE, pilots included

            // ★ ONE BODY LAW FOR EVERYTHING ALIVE OR NOT (his rule):
            // compressed = visibly smaller, spread = an inflated balloon -
            // players, creatures and crates alike. Computed from the LIVE
            // deviation each beat, so as the data drifts home on the curve
            // (toward your nature, or toward what the biome forces you to
            // be), the body visibly changes back with it.
            if (_baseScale == Vector3.zero) _baseScale = transform.localScale;
            // ★ ACOLYTES ARE SMALL (his call, the Meccha way: hiders are
            // smaller than seekers) - their natural body is 70%, full speed,
            // full jump. Composes with the balloon law below.
            float sideMul = 1f;
            {
                var pilot = GetComponent<SimpleFPSController>();
                if (pilot != null && Sides.IsAcolytePlayer(pilot))
                {
                    // ★ THE DISGUISE IS TRUE SIZE (his rule): the small body
                    // is the acolyte's TRUE form only - worn as an object,
                    // the object is exactly the object, centering intact
                    if (!_ssScanned) { _ss = GetComponent<ShapeShift>(); _ssScanned = true; }
                    if (_ss == null || !_ss.IsShapedNow)
                        sideMul = DrawingConfig.AcolyteBodyScale;
                }
            }
            // OVER THE TOP on purpose (his rule): the old factor moved a hit
            // player ~5% - invisible. Now a real hit visibly crushes or
            // balloons anything.
            transform.localScale = _baseScale * sideMul
                * Mathf.Clamp(1f - press * 0.5f, 0.55f, 1.6f);
            AxisTint(press, bal);

            // ★ BALANCE SHOWS ON THE SKIN (his rule): sticky bodies jiggle,
            // slick bodies go perfectly smooth - through StateView, the one
            // material writer for bodies that have one
            if (!_svScanned) { _sv = GetComponentInChildren<StateView>(); _svScanned = true; }
            if (_sv != null) _sv.ExtraWobble = bal * 0.9f;

            var rb = GetComponent<Rigidbody>();
            if (rb == null || rb.isKinematic) return;
            if (GetComponent<SimpleFPSController>() != null) return; // pilots: BodyState owns the feel

            if (_baseMass < 0f) { _baseMass = rb.mass; _baseDamp = rb.linearDamping; }
            rb.mass = _baseMass * Mathf.Clamp(1f + press * 1.5f, 0.25f, 4f);
            _balNow = bal;

            // ★ SLICK THINGS GLIDE ON THEIR OWN (his rule): frictionless AND
            // restless - each slicked object drifts off in its own direction
            if (bal < -0.12f && rb.linearVelocity.sqrMagnitude < 4f)
            {
                rb.WakeUp();
                rb.AddForce(Quaternion.Euler(0f, (GetInstanceID() * 37) % 360, 0f)
                    * Vector3.forward * 1.4f, ForceMode.Acceleration);
            }

            // ★ BURNING THINGS POP (his rule): heat makes them jump - a
            // sudden launch, a bang, embers everywhere
            float tempDev = SpellPayload.ToHuman(0, Data.Temp - Natural.Temp);
            if (tempDev > 60f && Random.value < 0.12f)
            {
                rb.AddForce(Vector3.up * Random.Range(4f, 8f)
                    + Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
                rb.AddTorque(Random.onUnitSphere * 6f, ForceMode.VelocityChange);
                Juice.Thud(transform.position);
                GrammarFX.PuffBurst(transform.position, new Color(1f, 0.55f, 0.15f), 6);
            }

            if (Mathf.Abs(bal) > 0.12f)
            {
                if (_grippyMat == null)
                {
                    _grippyMat = new PhysicsMaterial("SZ_Sticky")
                    { dynamicFriction = 1.4f, staticFriction = 1.6f,
                      frictionCombine = PhysicsMaterialCombine.Maximum };
                    _slickMat = new PhysicsMaterial("SZ_Slick")
                    { dynamicFriction = 0.02f, staticFriction = 0.02f,
                      frictionCombine = PhysicsMaterialCombine.Minimum };
                }
                if (_physCols == null)
                {
                    _physCols = GetComponentsInChildren<Collider>();
                    _physOrig = new PhysicsMaterial[_physCols.Length];
                    for (int i = 0; i < _physCols.Length; i++)
                        _physOrig[i] = _physCols[i] != null ? _physCols[i].sharedMaterial : null;
                }
                var want = bal > 0f ? _grippyMat : _slickMat;
                foreach (var c in _physCols)
                    if (c != null && !c.isTrigger) c.sharedMaterial = want;
                rb.linearDamping = bal > 0f ? _baseDamp + 2.5f : 0f;
                _physSwapped = true;
            }
            else if (_physSwapped && Mathf.Abs(bal) < 0.08f)
            {
                for (int i = 0; i < _physCols.Length; i++)
                    if (_physCols[i] != null) _physCols[i].sharedMaterial = _physOrig[i];
                rb.linearDamping = _baseDamp;
                _physSwapped = false;
                // the glue dries out with the stickiness
                if (_glue != null)
                {
                    foreach (var j in _glue) if (j != null) Destroy(j);
                    _glue.Clear();
                }
            }

        }

        // ★ THE GOO LAYER, the cheap way (his Splatoon instinct, no boned
        // blob needed): the whole object tints while an axis rides it -
        // glue amber, slick sheen, stone gray, fluid pale. Bodies with a
        // StateView keep their one writer and get the puffs only.
        Vector3 _baseScale;
        Renderer[] _tintRends;
        MaterialPropertyBlock _tintMpb;
        bool _tinted;
        static readonly int TintColorID = Shader.PropertyToID("_BaseColor");

        void AxisTint(float press, float bal)
        {
            // ★ LIVING THINGS NEVER BODY-TINT (his ruling): tints cannot
            // stack when several effects ride one body - PARTICLES are the
            // tell on the living. The goo layer is for objects only.
            if (GetComponentInParent<SimpleFPSController>() != null
                || GetComponentInParent<Creature>() != null) return;
            bool wants = Mathf.Abs(press) > 0.2f || Mathf.Abs(bal) > 0.12f;
            if (!wants && !_tinted) return;
            if (GetComponentInChildren<StateView>() != null) return;
            if (_tintRends == null)
            {
                _tintRends = GetComponentsInChildren<Renderer>();
                _tintMpb = new MaterialPropertyBlock();
            }
            if (!wants)
            {
                foreach (var r in _tintRends)
                    if (r != null) r.SetPropertyBlock(null);
                _tinted = false;
                return;
            }
            Color mul = Color.white;
            if (press > 0.2f)
                mul = Color.Lerp(Color.white, new Color(0.5f, 0.5f, 0.52f), Mathf.Min(1f, press));
            else if (press < -0.2f)
                mul = Color.Lerp(Color.white, new Color(0.85f, 0.95f, 1f), Mathf.Min(1f, -press));
            if (bal > 0.12f) mul *= new Color(1f, 0.8f, 0.45f);
            else if (bal < -0.12f) mul *= new Color(0.7f, 0.9f, 1f);
            _tintMpb.SetColor(TintColorID, mul);
            // slick props gleam wet-smooth, sticky ones go matte-gluey
            if (bal < -0.12f) _tintMpb.SetFloat(SmoothID, 0.95f);
            else if (bal > 0.12f) _tintMpb.SetFloat(SmoothID, 0.15f);
            foreach (var r in _tintRends)
                if (r != null) r.SetPropertyBlock(_tintMpb);
            _tinted = true;
        }
        static readonly int SmoothID = Shader.PropertyToID("_Smoothness");
        StateView _sv;
        bool _svScanned;
        ShapeShift _ss;
        bool _ssScanned;

        // ★ THE TELL (his rule: the player must SEE something happening):
        // an axis riding a thing puffs its color on a slow beat - glue
        // amber, slick ice-blue, heavy dust, feather-light pale sparks
        float _nextAxisFx;
        void AxisTellFx(float press, float bal)
        {
            if (Time.time < _nextAxisFx) return;
            float aff = SpellPayload.ToHuman(5, Data.Affinity - Natural.Affinity) / 100f;
            float tmp = SpellPayload.ToHuman(0, Data.Temp - Natural.Temp);
            if (Mathf.Abs(bal) <= 0.12f && Mathf.Abs(press) <= 0.2f
                && Mathf.Abs(aff) <= 0.1f && Mathf.Abs(tmp) <= 40f) return;
            _nextAxisFx = Time.time + 0.65f;
            // balance FX live at the FEET (his rule: nothing floats) - glue
            // drips and slick sheen belong on the ground you stand on
            Vector3 at = transform.position + Vector3.up * 0.06f;

            // hot things smolder embers, cold things breathe frost
            if (tmp > 40f) GrammarFX.PuffBurst(at + Vector3.up * 0.3f,
                new Color(1f, 0.5f, 0.12f), 3);
            else if (tmp < -40f) GrammarFX.PuffBurst(at,
                new Color(0.85f, 0.95f, 1f), 3);

            // ★ ATTRACT/REPEL GET A TELL TOO (his rule: no one may believe
            // nothing happened). No colors - he removed those - so the SHAPE
            // says it: dotted streaks pointing IN toward a puller, OUT from
            // a pusher, at two random angles a beat.
            if (Mathf.Abs(aff) > 0.1f)
            {
                var neutral = new Color(0.9f, 0.9f, 0.95f);
                for (int a = 0; a < 2; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                    for (int i = 0; i < 3; i++)
                    {
                        float t = aff > 0f ? 1.4f - i * 0.45f : 0.5f + i * 0.45f;
                        GrammarFX.PuffBurst(at + dir * t, neutral, 1);
                    }
                }
            }
            if (bal > 0.12f) GrammarFX.PuffBurst(at, new Color(0.85f, 0.65f, 0.2f), 3);
            else if (bal < -0.12f) GrammarFX.PuffBurst(at, new Color(0.6f, 0.85f, 1f), 3);
            if (press > 0.2f) GrammarFX.PuffBurst(
                transform.position + Vector3.up * 0.05f, new Color(0.35f, 0.33f, 0.3f), 4);
            else if (press < -0.2f) GrammarFX.PuffBurst(
                at + Vector3.up * 0.4f, new Color(1f, 0.98f, 0.8f), 2);
        }

        /// happens to it. The same two lines for a wall, a crate, a zombie and
        /// a player - there is no list of who this applies to.
        void Beat(float span)
        {
            if (_dead) return;
            SpellLaw.Drift(this, span);
            Bear(span);
            ApplyPhysicalAxes();

            // a thing carrying Affinity is its own gravity until it drifts home
            if (Mathf.Abs(Data.Affinity) > 0.05f)
                SpellParticle.AffinityField(transform, Data.Affinity, span);

            TickInfluence(span);

            // THINGS BURN WHEN THEY ARE HOT. His sentence, and the whole law -
            // nothing asks what set it on fire, or whether it is a creature.
            // ★ MEASURED FROM WHAT IT NATURALLY IS, never in absolutes. A rock
            // raised in a fire biome is NATURALLY that hot and is perfectly
            // fine; against an absolute threshold it would be born burning and
            // die in its own home. What hurts is being pushed away from
            // yourself - so the same forty degrees kills the frost rock and
            // barely troubles the magma one.
            float from = Data.Temp - Natural.Temp;
            // FLESH IS FUSSIER THAN STONE. A living thing has a narrow comfort
            // band and a rock does not, which is the player band that already
            // existed generalised by the one rule he gave for living: a mind.
            // The band is asymmetric because bodies mind heat far more than
            // cold - the same shape BodyState always used.
            bool alive = Data.Alive;
            float over = alive ? DrawingConfig.LivingHeatTolerance
                               : DrawingConfig.BurnThreshold - RoomTemp;
            float under = alive ? DrawingConfig.LivingChillTolerance
                                : DrawingConfig.FreezeThreshold - RoomTemp;

            // and it scales with HOW FAR out, so a little too warm stings and
            // standing in a furnace kills - flat-rate made both identical
            float off = from > over ? from - over : from < under ? from - under : 0f;
            if (off != 0f)
                TakeDamage(Mathf.Abs(off) * DrawingConfig.TempDamagePerDegree * span,
                    off > 0f ? "burning" : "freezing", Owner);

            // ★ WHAT IT IS MADE OF IS WHAT IT LOOKS LIKE. The state material
            // already fades a thing out as it goes toward gas, so pushing the
            // State axis down IS turning something invisible - no effect, no
            // timer, and it comes back on its own as the number drifts home.
            ShowState();
            Spread();
        }

        /// ★ WHAT THE PLACE LETS YOU BEAR. Strength is a CAPACITY, so the
        /// ceiling here is the lesser of your own and the ground's - a 120
        /// biome does not lift a 90-cap acolyte to 120, and a 100 biome drags a
        /// 140-cap wizard down to 100.
        ///
        /// It is handled apart from the ordinary drift because drifting
        /// strength back toward natural IS healing, and healing has its own
        /// speed. Leaving it in the drift loop meant a 0-strength biome did
        /// nothing at all: the ceiling never moved, so nothing ever died of
        /// standing somewhere that could not hold it.
        /// ★ WHAT IT IS MADE OF IS WHAT IT LOOKS LIKE. Separate from the beat
        /// on purpose: a client never simulates, but it is TOLD these numbers
        /// and has to show them, so both sides run this and only the host works
        /// out what to put in it.
        public void ShowState()
        {
            if (_view == null) _view = GetComponentInChildren<StateView>();
            if (_view != null)
                _view.StateT = SpellPayload.StateT01(Data.State);
        }

        void Bear(float span)
        {
            float ground = SpellLaw.Here(this).Strength;
            float ceiling = ground > 0f ? Mathf.Min(Natural.Strength, ground)
                                        : Natural.Strength;

            if (Health > ceiling)
            {
                // the place cannot hold what you are - you give it up at the
                // speed the place allows, and a ceiling of zero is fatal
                Health = Mathf.MoveTowards(Health, ceiling,
                    DrawingConfig.StrengthYieldPerSec * span);
                if (Health <= 0f) { TakeDamage(1f, "the ground could not hold it", Owner); return; }
            }
            else if (Health < ceiling)
            {
                // MENDING. His rule: the lower your maximum, the faster you
                // recover - so a frail thing comes back quickly and a buffed
                // one takes its time. Buffing an enemy really does slow them.
                float rate = DrawingConfig.RegenBase
                           * (DrawingConfig.RegenReference / Mathf.Max(1f, Natural.Strength));
                Health = Mathf.MoveTowards(Health, ceiling, rate * span);
            }
        }

        static readonly List<SpellTable.Row> _coats = new List<SpellTable.Row>();

        /// ★ SPREADING IS ONE MECHANIC, not a flame feature. A thing whose
        /// numbers sit in a spreading region hands a share of them to the
        /// nearest element - which then satisfies the same region itself, and
        /// passes it on in turn. Fire crawling along a fence and poison moving
        /// between bodies are the same three lines.
        ///
        /// The share is under 1 on purpose: each hop is weaker than the last,
        /// so it dies out on its own rather than eating the map.
        static readonly List<SpellDef> _spreadBuf = new List<SpellDef>();

        void Spread()
        {
            // ★ SPREADING IS THE BOOK'S WORD, not the dead table's: a thing
            // spreads when its numbers are a spell whose AREA spreads. House
            // walls match no such spell and stop pretending to be fire.
            SpellBook.All(Data, _spreadBuf);
            SpellDef spreading = null;
            for (int i = 0; i < _spreadBuf.Count; i++)
            {
                var a = SpellBook.Live.Aoe(_spreadBuf[i].Aoe);
                if (a != null && a.Spreading) { spreading = _spreadBuf[i]; break; }
            }
            if (spreading == null) return;

            // the broadphase, not the whole world: walking every element for
            // every spreading one is fine with a hundred props and ruinous
            // with five thousand
            int n = Physics.OverlapSphereNonAlloc(transform.position,
                DrawingConfig.SpreadReach, GrammarFX.ScanBuffer, ~0,
                QueryTriggerInteraction.Ignore);

            Element nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null) continue;
                var o = c.GetComponentInParent<Element>();
                if (o == null || o == this || o._dead) continue;
                float d2 = (o.transform.position - transform.position).sqrMagnitude;
                if (d2 < best) { best = d2; nearest = o; }
            }
            if (nearest == null) return;
            // ★ THE SPELL SPREADS, NOT THE SELF: only the axes that make this
            // a spreading spell move, and the giver loses exactly what the
            // neighbour takes. Blanket copying compounded houses into
            // quintillions; blanket deduction gutted living things' minds
            // and hp along with the fire.
            float share = DrawingConfig.SpreadTransferShare;
            var taken = nearest.Data;
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                if (spreading.Axis[i] == 0) continue;
                float give = Data[i] * share;
                taken[i] += give;
                Data[i] -= give;
            }
            nearest.Data = taken.Clamped();
            // blame travels with the numbers, same as any hand-over
            ISpellData t = nearest, f = this;
            if (f.Owner >= 0) t.Owner = f.Owner;
        }

        static readonly System.Collections.Generic.HashSet<Element> _touched
            = new System.Collections.Generic.HashSet<Element>();

        /// ★ RANGE OF INFLUENCE (his rule): touching things trade their
        /// imposed axes down the gradient, second-law style - heat, light,
        /// weight, balance, state, affinity all conduct. Strength, mind and
        /// courage are each thing's own and never do. Conservative: what one
        /// side gains the other loses, so nothing can compound.
        void TickInfluence(float span)
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position,
                DrawingConfig.InfluenceReach, GrammarFX.ScanBuffer, ~0,
                QueryTriggerInteraction.Ignore);
            if (n <= 0) return;
            _touched.Clear();
            float k = Mathf.Clamp01(DrawingConfig.InfluenceSharePerSec * span) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null) continue;
                var o = c.GetComponentInParent<Element>();
                if (o == null || o == this || o._dead || !_touched.Add(o)) continue;
                var mine = Data; var theirs = o.Data;
                bool moved = false;
                for (int ax = 0; ax < SpellPayload.AxisCount; ax++)
                {
                    if (SpellPayload.LawOf(ax) != AxisLaw.Impose) continue;
                    float flow = (mine[ax] - theirs[ax]) * k;
                    if (Mathf.Abs(flow) < 1e-5f) continue;
                    mine[ax] -= flow; theirs[ax] += flow; moved = true;
                }
                if (!moved) continue;
                Data = mine.Clamped();
                o.Data = theirs.Clamped();
                // blame rides the data, same as every hand-over
                ISpellData g = this, t = o;
                if (g.Owner >= 0 && t.Owner < 0) t.Owner = g.Owner;
            }
        }

        /// This object's name on the wire. Set before Awake for a spawned
        /// thing; otherwise it derives itself.
        [System.NonSerialized] public int NetId;

        static readonly System.Collections.Generic.Dictionary<int, Element> _byId
            = new System.Collections.Generic.Dictionary<int, Element>();

        /// The one object that answers to this id, or null.
        public static Element ById(int id) =>
            _byId.TryGetValue(id, out var d) ? d : null;

        /// Stamp a spawned thing with the host's id, and re-file it.
        public void Rename(int id)
        {
            if (id == 0 || id == NetId) return;
            if (_byId.TryGetValue(NetId, out var had) && had == this) _byId.Remove(NetId);
            NetId = id;
            _byId[id] = this;
        }

        /// FNV-1a over the scene path. String.GetHashCode is not stable across
        /// runtimes, and two machines disagreeing on an id is the whole bug.
        static int PathId(Transform t)
        {
            string path = t.name;
            var up = t;
            while (up.parent != null) { up = up.parent; path = up.name + "/" + path; }
            return IdFor(path);
        }

        /// The same hash for anything that can name itself the same way on two
        /// machines - a scene path, or "player:3".
        public static int IdFor(string key)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in key) { h ^= c; h *= 16777619u; }
                int id = (int)h;
                return id == 0 ? 1 : id;   // 0 means "unset"
            }
        }

        /// What this body weighed when it was born, or when its strength was
        /// last set from its body. Anything past this is weight it is CARRYING.
        public float NaturalMass { get; private set; }

        /// 0 = carrying nothing but itself, 1 = at the point where it starts
        /// buckling. Views read this to show the strain before it kills.
        public float Burden01
        {
            get
            {
                if (_body == null || _body.isKinematic) return 0f;
                float extra = Mathf.Max(0f, _body.mass - NaturalMass);
                if (extra <= 0f) return 0f;
                float strength = Mathf.Max(1f, Health > 0f ? Health : MaxStrength);
                return Mathf.Clamp01(extra * DrawingConfig.PropWeightPerKg
                    / (strength * DrawingConfig.PropCrushLoad));
            }
        }

        // impact damage: a prop with a Rigidbody takes damage scaled by how hard it hit
        static readonly float ImpactFloor = DrawingConfig.Overlay("ImpactDamageFloor", 4f);
        // distinct key on purpose: "ImpactDamagePerSpeed" is the creature knob (Creature.cs)
        static readonly float ImpactScale = DrawingConfig.Overlay("PropImpactDamagePerSpeed", 2.2f);

        // ★ THE ENVIRONMENT REACTS (his rule): a spell-hit prop that survives
        // FLIES - launched tumbling into the distance, mass deciding how far.
        // A rooted thing SHAKES instead. Nothing eats a hit standing still.
        public void ImpactJolt(Vector3 from, float power)
        {
            if (_dead) return;
            if (GetComponent<SimpleFPSController>() != null) return; // pilots have TakeHit
            if (GetComponentInParent<Creature>() != null) return;    // creatures have their own hit acting
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                Vector3 dir = (transform.position - from);
                dir = (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up)
                    + Vector3.up * 0.6f;
                float fly = power * (2f + 10f / Mathf.Max(1f, rb.mass));
                rb.AddForce(dir.normalized * fly, ForceMode.VelocityChange);
                rb.AddTorque(Random.onUnitSphere * fly * 2f, ForceMode.VelocityChange);
            }
            else if (!Liftable.WorldScale(transform, out _))
                StartCoroutine(ImpactShake());
        }

        System.Collections.IEnumerator ImpactShake()
        {
            if (_shaking) yield break;
            _shaking = true;
            Vector3 home = transform.localPosition;
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                transform.localPosition = home + Random.insideUnitSphere * 0.045f * (1f - t / 0.25f);
                yield return null;
            }
            transform.localPosition = home;
            _shaking = false;
        }
        bool _shaking;

        void OnCollisionEnter(Collision col)
        {
            if (_dead) return;
            // rooted props gain their body after Awake when torn loose - re-check
            if (_body == null)
            {
                _body = GetComponent<Rigidbody>();
                if (_body == null) return;
                RemoveOnDeath = true;   // it's a real object now
            }
            float speed = col.relativeVelocity.magnitude;
            if (speed < ImpactFloor) return;

            // ★ DATA SPREADS ON IMPACT (his rule): two things colliding trade
            // a share of what rides them - a slicked crate slicks what it
            // lands on. Conservative: what one gives, it loses.
            var oel = col.collider.GetComponentInParent<Element>();
            if (oel != null && oel != this && !oel._dead)
            {
                var mine = Data; var theirs = oel.Data;
                bool moved = false;
                for (int i = 0; i < 6; i++)
                {
                    float dev = mine[i] - Natural[i];
                    if (Mathf.Abs(dev) < 0.1f) continue;
                    float give = dev * 0.25f;
                    mine[i] -= give; theirs[i] += give;
                    moved = true;
                }
                if (moved) { Data = mine; oel.Data = theirs.Clamped(); }
            }

            // ★ STICKY IS GLUE (his rule): a sticky thing GRIPS what it
            // touches - while it can carry the weight. The break force IS
            // the grip, so heavy loads tear free. Accidental glue keeps
            // clusters close, which is exactly what spreads an aoe.
            if (_balNow > 0.12f && col.rigidbody != null && !col.rigidbody.isKinematic
                && col.collider.GetComponentInParent<SimpleFPSController>() == null
                && GetComponentInParent<SimpleFPSController>() == null)
            {
                if (_glue == null) _glue = new System.Collections.Generic.List<FixedJoint>();
                _glue.RemoveAll(j => j == null);
                bool held = false;
                foreach (var j in _glue)
                    if (j.connectedBody == col.rigidbody) { held = true; break; }
                if (!held && _glue.Count < 3)
                {
                    var joint = gameObject.AddComponent<FixedJoint>();
                    joint.connectedBody = col.rigidbody;
                    joint.breakForce = 150f + _balNow * 500f;
                    joint.breakTorque = joint.breakForce;
                    _glue.Add(joint);
                }
            }

            // ★ FROZEN THINGS SHATTER (his rule): deep cold makes a thing
            // brittle - a hard knock cracks it and throws BONUS ice debris
            // from the ice itself, cold enough to chill what it lands on
            float frostDev = SpellPayload.ToHuman(0, Data.Temp - Natural.Temp);
            if (frostDev < -60f && speed > ImpactFloor + 2f)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 d = (Random.onUnitSphere + Vector3.up * 0.7f).normalized;
                    var ice = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Solid,
                        0.16f, transform.position + d * 0.3f);
                    if (ice == null) continue;
                    ice.Temperature = -30f;
                    ice.StampOwner(Owner);
                    var sd = ice.gameObject.AddComponent<SpellDebris>();
                    sd.Init(SurfaceMaterialType.Water, MatterPhase.Solid, 0.16f);
                    sd.OwnerId = Owner;
                    if (ice.TryGetComponent<Rigidbody>(out var irb))
                        irb.linearVelocity = d * 6f;
                }
                GrammarFX.PuffBurst(transform.position, new Color(0.85f, 0.95f, 1f), 6);
                Juice.Thud(transform.position);
            }

            // heavier things carry more into the hit, and both sides feel it
            float mass = Mathf.Max(0.2f, _body.mass);
            float dmg = (speed - ImpactFloor) * ImpactScale * Mathf.Sqrt(mass);
            if (dmg < 1f) return;

            string what = col.collider != null ? col.collider.name : "the ground";
            TakeDamage(dmg, $"slammed into {what}");

            // what it hit takes damage too
            var other = col.collider != null
                ? col.collider.GetComponentInParent<Element>() : null;
            if (other != null && other != this) other.TakeDamage(dmg * 0.7f, $"hit by {name}");
        }

        /// Clears the dead flag on lobby respawn; restoring Health alone is not enough.
        public void Revive(float health)
        {
            _dead = false;
            Health = health;
        }

        /// A thing too weak for its own mass buckles - the scenery obeys the
        /// same weight-against-strength law bodies do. Only free-standing
        /// objects: static geometry is held up by the world, not by itself.
        void FixedUpdate()
        {
            if (_dead || _body == null || _body.isKinematic) return;
            if (MaxStrength <= 0f) return;

            // WEIGHT AGAINST STRENGTH, not against a health fraction. Dividing
            // by StrengthMul (0.35..1) measured mass against nothing, so a
            // healthy 110kg charger buckled just for being a charger.
            // A hurt thing still holds itself up worse: strength IS health.
            float carried = _body.mass * DrawingConfig.PropWeightPerKg;
            float strength = Mathf.Max(1f, Health > 0f ? Health : MaxStrength);
            float load = carried / strength;
            if (load < DrawingConfig.PropCrushLoad) return;

            _crushCarry += (load - DrawingConfig.PropCrushLoad)
                * DrawingConfig.PropCrushPerSec * Time.fixedDeltaTime;
            if (_crushCarry < 1f) return;
            float bite = _crushCarry;
            _crushCarry = 0f;
            TakeDamage(bite, "buckling under its own weight");
        }

        float _crushCarry;

        /// `by` is the OWNER that did it - a player, a zombie, a golem, a
        /// thrown crate. The cause string is for the log; the id is what a
        /// curse reads later. -1 when nothing owns it (a fall, its own weight).
        public void TakeDamage(float amount, string cause, int by = -1)
        {
            // ★ DEATH ARRIVES ONCE (his rule): a downed body is dead-still -
            // no damage lands until it is revived, no matter what field it
            // lies in - and a corpse pays no wand credit either.
            if (DeadStill) return;

            // acolyte destruction pays the wand back (his rule) - spells and
            // zombies alike, but never by hitting your own side
            if (by >= 0 && amount > 0f && Sides.IsAcolyte(by)
                && Teams.OfOwner(by) != Teams.Of(this))
                PlayerInk.CreditWand(by, amount * 0.12f);
            if (amount <= 0f || _dead) return;

            // damage on a limb bone forwards to the owning being; never Destroy() a skeleton bone
            var pilot = GetComponentInParent<SimpleFPSController>();
            Component owner = pilot != null ? (Component)pilot : GetComponentInParent<Creature>();
            if (owner != null && owner.gameObject != gameObject)
            {
                var rootDmg = owner.GetComponent<Element>();
                if (rootDmg != null && rootDmg != this) rootDmg.TakeDamage(amount, cause, by);
                return;
            }

            // THE HOST OWNS THE NUMBERS. A client asks and waits: it must not
            // subtract locally, or the same tree ends up on different health on
            // every machine. Offline, !Connected makes us the authority.
            if (NetGame.Connected && !NetGame.IsHost)
            {
                NetSync.AskHurt(NetId, amount);
                return;
            }

            // the world's short memory: who hurt me, and who finished me
            Marks.Set(NetId, Mark.DamagedBy, by);
            Apply(amount, cause);
            if (Health <= 0f) Marks.Set(NetId, Mark.KilledBy, by);
            NetSync.PushHealth(NetId, Health, MaxStrength);
        }

        /// The host's answer, applied verbatim - no local arithmetic, so the
        /// number is the same everywhere by construction rather than by luck.
        public void TakeNetHealth(float health, float max)
        {
            if (max > 0f) MaxStrength = max;
            float lost = Health - health;
            Health = health;
            if (lost > 0f) OnDamaged?.Invoke(lost, "magic");
            if (Health > 0f || _dead) return;
            _dead = true;
            OnDeath?.Invoke("magic");
            if (RemoveOnDeath) Destroy(gameObject);
        }

        void Apply(float amount, string cause)
        {
            Health -= amount;
            OnDamaged?.Invoke(amount, cause);
            _logAccum += amount;
            if (_logAccum >= 30f)
            {
                Debug.Log($"[SpellyZombie] {name}: {cause}, {Mathf.Max(0, Health):0} hp left");
                _logAccum = 0f;
            }
            if (Health <= 0f)
            {
                _dead = true;
                Debug.Log($"[SpellyZombie] {name} destroyed by {cause}");
                OnDeath?.Invoke(cause);
                if (!RemoveOnDeath) return;
                // in the lobby, authored props respawn; creatures and runtime spawns die for real
                if (RoundDirector.InLobby && _authored
                    && GetComponent<Creature>() == null
                    && GetComponent<SimpleFPSController>() == null)
                    LobbyRespawn.Take(gameObject, DrawingConfig.LobbyRespawnSeconds);
                else
                    Destroy(gameObject);
            }
        }
    }

    /// The single driver. One Update in the whole game turns the world's
    /// elements, instead of one per prop.
    internal class ElementBeat : MonoBehaviour
    {
        void Update() => Element.BeatAll();
    }
}
