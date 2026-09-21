using UnityEngine;

namespace SpellyZombie
{
    /// Nature's answer to wrecking the place. A golem belongs to nobody: it
    /// wanders, and anything it sees is an enemy. It does not guard a summoner
    /// and it does not take orders. A wild one is born furious and loses its
    /// nerve to wounds, spells and anyone closing in on it or drawing; left
    /// alone it grows brave again, and cornered it turns and fights.
    /// Its one attack is the shared ChargeAttack - hop, lock the direction,
    /// drive straight - so it is dodgeable by moving, and its debuff is
    /// whatever its BiomeStamp made it.
    /// Built from the CollectionManager's Golem prefab; nothing is code-built.
    [RequireComponent(typeof(Rigidbody))]
    public class Golem : MonoBehaviour
    {
        public float SightRange = 13f;
        [Tooltip("A spell made or a blast this close sends the golem the other way.")]
        public float FearRange = 9f;
        public bool Alive => _dmg == null || _dmg.Health > 0f;

        // ---- possession (his rule): a ghost drives its own team's golem,
        // the way an acolyte ghost drives a zombie. The rider steers, the
        // click is the golem's own charge; fear and prey pause while ridden.
        public bool Possessed { get; private set; }
        public Vector3 PossessedMove { get; set; }
        public Vector3 PossessedFace { get; set; }

        public void PossessBy(bool on)
        {
            Possessed = on;
            PossessedMove = Vector3.zero;
            PossessedFace = Vector3.zero;
            if (_eyes != null) _eyes.SetMood(on ? EyeMood.Mad : EyeMood.Neutral, 0.6f);
            // the rider's own face is the golem's face now, and it looks out from between these:
            // they get out of the lens, as a ridden zombie's do and as the stand-ins on the other machines do
            if (_eyes != null)
                foreach (var r in _eyes.GetComponentsInChildren<Renderer>(true)) r.enabled = !on;
        }

        /// What the snapshot reads: the eyes and the charge beat.
        public GooglyEyes Eyes => _eyes;
        public byte ChargeBeat => _charge != null ? _charge.BeatWire : (byte)0;

        /// Where a rider looks from: the eyes, else the top of the body.
        public Vector3 HeadAt => _eyes != null ? _eyes.transform.position
            : transform.position + Vector3.up * _top.Above(transform);

        /// Where a rider sits: inside the body, high enough that only the
        /// hat shows above the top, the way a ghost sits in a zombie skull.
        public Vector3 SeatAt => transform.position + Vector3.up * (_top.Above(transform) - 0.22f);

        readonly BodyTop _top = new BodyTop();

        /// The rider's click: the golem's own charge, where the rider looks.
        public bool GhostAbility(Vector3 look)
        {
            if (_charge == null || _charge.Busy) return false;
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) return false;
            return _charge.TryStart(transform.position + look.normalized * 6f);
        }
        public float WalkSpeed = 1.9f;

        /// -1 = wild (debris-born): serves nobody, hunts anyone. Set by a
        /// seal summon: the golem WORKS FOR its summoner (his rule) and
        /// never hunts the owner side.
        public int OwnerId = -1;
        /// The size it was raised at: Spawn's clamped size multiplier.
        public float SizeMul = 1f;

        // how it lives: its definition's behaviour (Wear); one that wore none is
        // skittish when it serves nobody and roams when it serves someone.
        // Serialized so the halves of a split keep it.
        [SerializeField, HideInInspector] CreatureBehaviour _behaviour;
        [SerializeField, HideInInspector] bool _behaviourSet;
        [SerializeField, HideInInspector] float _guardRange = 10f;
        [SerializeField, HideInInspector] Vector3 _home;
        [SerializeField, HideInInspector] bool _hasHome;
        Vector3 _lastSeen;
        float _lastSeenUntil;

        public CreatureBehaviour Behaviour => _behaviourSet ? _behaviour
            : OwnerId < 0 ? CreatureBehaviour.Skittish : CreatureBehaviour.Roams;

        /// Where it stood up: a guard keeps to it.
        public void SetHome(Vector3 at) { _home = at; _hasHome = true; }

        Rigidbody _rb;
        Creature _me;
        bool _saidTeam; // one birth log so a wrong team is a FACT, not a guess
        Transform _loggedPrey;
        ChargeAttack _charge;
        Element _dmg;
        GooglyEyes _eyes;
        bool _sawPrey;
        float _alertUntil; // hurt lately: it feels around in every direction
        int _circleSign = 1; // which way it circles a wand it will not approach
        static readonly RaycastHit[] _sight = new RaycastHit[8];
        float _safeUntil;   // birth shield: it cannot be killed while rising
        float _nerve = 1f;          // wild only: 0 terrified .. 1 furious
        float _rageUntil;           // cornered: it fights whatever its nerve
        float _birthRageUntil;      // fresh from the ground: hard to scare
        float _lastScareAt = -1f;   // the blast already counted against its nerve
        float _blocked;             // seconds spent backing off and getting nowhere
        Vector3 _lastPos;

        /// Live registry - the host walks this to snapshot them for clients,
        /// the same way Zombie.All works.
        public static readonly System.Collections.Generic.List<Golem> All
            = new System.Collections.Generic.List<Golem>();

        /// Golems in someone's hands: disabled, so out of All, but the
        /// snapshot still streams them.
        public static readonly System.Collections.Generic.List<Golem> Carried
            = new System.Collections.Generic.List<Golem>();

        void OnEnable()
        {
            if (_dmg == null) _dmg = GetComponent<Element>();
            All.Add(this);
        }

        void OnDisable() => All.Remove(this);

        void OnDestroy() => Carried.Remove(this);

        /// What colour it came out of the ground: clients paint their copy with
        /// this rather than re-deriving a biome they cannot see.
        public Color Skin
        {
            get
            {
                var view = GetComponent<StateView>();
                return view != null && view.DriveTint ? view.Tint : Color.gray;
            }
        }

        void LateUpdate()
        {
            // while the shield holds, damage cannot finish it - it is topped
            // back up rather than made immune, so hits still register and read
            if (_dmg == null || Time.time >= _safeUntil) return;
            if (_dmg.Health < _dmg.MaxStrength * 0.35f)
                _dmg.Health = _dmg.MaxStrength * 0.35f;
        }

        Vector3 _wander;
        float _pickAt, _skipAt;

        /// Wear a creature: its numbers, its colour over stone, its movement
        /// and shape on the body. Eyes stay eyes - StateView leaves them alone.
        public void Wear(CreatureDef spell)
        {
            if (spell == null) return;

            // BORN AS what the definition says - stamped onto the Element the
            // way a biome stamps anything - so a solid golem is solid and a
            // liquid one is liquid, and each drifts from there.
            var el = GetComponent<Element>();
            if (el != null) el.WearBorn(spell.Payload);
            Abilities.Clear();
            Abilities.AddRange(spell.Abilities);
            Worn = spell;
            if (spell.Boss)
            {
                var mark = GetComponent<BossMark>();
                if (mark == null) mark = gameObject.AddComponent<BossMark>();
                mark.Name = spell.Name;
            }
            _boss = GetComponent<BossMark>();
            _behaviour = spell.Behaviour;
            _behaviourSet = true;
            _guardRange = spell.GuardRange;

            var charge = GetComponent<ChargeAttack>();
            if (charge != null)
            {
                charge.TellClip = spell.MoveClip(Zombie.Charge);
                charge.Reach = Reach;
            }

            var view = GetComponent<StateView>() ?? gameObject.AddComponent<StateView>();
            view.Tint = Color.Lerp(new Color(0.55f, 0.55f, 0.5f), spell.Payload.Tint(),
                                   DrawingConfig.BiomeTintStrength);
            view.DriveTint = true;
            view.Look = spell.Skin;
            CreatureLook.Shape(gameObject, spell);
        }

        /// What it can do, from its creature. Empty for a natural golem.
        public readonly System.Collections.Generic.List<string> Abilities =
            new System.Collections.Generic.List<string>();
        public CreatureDef Worn { get; private set; }

        float _castCooldown;
        int _castTurn;
        BossMark _boss;

        /// A boss reaches as far as it is big (his pick): its sight, casts and
        /// charge grow with its size. Every other golem reaches 1.
        float Reach => _boss != null ? SizeMul : 1f;

        /// ★ WHAT IT CASTS (its Can do): facing its mark and within reach, the
        /// spell whose turn it is leaves its head. True when it cast.
        bool TryCast(Transform prey, Vector3 to, float dist)
        {
            _castCooldown -= Time.fixedDeltaTime;
            // a boss casts by its phase: nothing at full health, all out in its last 20%
            if (_boss != null)
            {
                if (!_boss.Casts) return false;
                if (_boss.NewPhase(CreatureCasts.CastableCount(Abilities)))
                {
                    _castCooldown = 0f;
                    if (_eyes != null) _eyes.SetMood(EyeMood.Mad, 3f);
                }
            }
            string spell = CreatureCasts.Pick(Abilities, _castTurn);
            if (spell == null || _castCooldown > 0f || dist > DrawingConfig.GooThrowRange * Reach || to.sqrMagnitude < 0.01f)
                return false;
            Vector3 face = to.normalized;
            if (Vector3.Dot(transform.forward, face) < 0.9f) return false; // it walks round to face it first
            float wait = CreatureCasts.Cooldown(spell);
            _castCooldown = _boss != null ? _boss.WaitAfterCast(wait) : wait;
            _castTurn++;
            Vector3 head = transform.position + Vector3.up * (transform.localScale.y * 0.9f);
            Vector3 muzzle = head + face * 0.4f;
            // thrown at the body's middle, so a tall one does not throw over heads
            Vector3 aim = (prey.position + Vector3.up * LockOn.AimHeight(prey) - muzzle).normalized;
            CreatureCasts.Cast(spell, OwnerId, muzzle, aim, transform.position,
                CreatureCasts.ClearReach(transform), transform);
            if (_eyes != null) _eyes.Swell(0.4f, 1.3f);
            return true;
        }

        // ★ AN OWNER CARRIES THEIR GOLEM EASILY (his rule): while carried it
        // does not resist or animate - no thinking, no hops, feather-light.
        // Everything restores the moment it is let go or thrown.
        float _carriedMass = -1f;
        public void BeCarried()
        {
            if (!Carried.Contains(this)) Carried.Add(this);
            enabled = false;
            var ch = GetComponent<ChargeAttack>();
            if (ch != null) ch.enabled = false;
            var an = GetComponentInChildren<Animator>();
            if (an != null) an.speed = 0f;
            if (_carriedMass < 0f && TryGetComponent<Rigidbody>(out var rb))
            {
                _carriedMass = rb.mass;
                rb.mass = 1f;
            }
        }
        public void BeReleased()
        {
            Carried.Remove(this);
            enabled = true;
            var ch = GetComponent<ChargeAttack>();
            if (ch != null) ch.enabled = true;
            var an = GetComponentInChildren<Animator>();
            if (an != null) an.speed = 1f;
            if (_carriedMass >= 0f && TryGetComponent<Rigidbody>(out var rb))
            {
                rb.mass = _carriedMass;
                _carriedMass = -1f;
            }
        }

        public static Golem Spawn(Vector3 at, float sizeMul = 1f, float maxScale = -1f)
        {
            using (PerfMarkers.GolemBirths.Auto()) return Raise(at, sizeMul, maxScale);
        }

        static Golem Raise(Vector3 at, float sizeMul, float maxScale)
        {
            // golems exist only on the host; clients get NetGolemProxy stand-ins
            if (NetGame.Connected && !NetGame.IsHost) return null;

            var prefab = CollectionManager.Golem;
            if (prefab == null) return null;

            // SIZE IS THE INGREDIENTS' BUSINESS, TOUGHNESS IS NOT. A golem
            // raised from two small blobs stays small - that reads right - but
            // it is never made of paper: strength and mass have floors, so a
            // little one is still worth fighting. Bigger still means stronger.
            // A boss follows its Size slider past the usual cap (maxScale).
            float scale = Mathf.Clamp(sizeMul,
                DrawingConfig.GolemMinScale, maxScale > 0f ? maxScale : DrawingConfig.GolemMaxScale);

            var go = Instantiate(prefab, at, Quaternion.Euler(0f, Random.value * 360f, 0f));
            go.transform.localScale *= scale;

            var g = go.GetComponent<Golem>();
            if (g == null) g = go.AddComponent<Golem>();

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = Mathf.Max(DrawingConfig.GolemMinMass,
                    DrawingConfig.GolemBaseMass * scale * scale * scale);
                // A SMALL GOLEM MOVES FURTHER PER STEP THAN IT IS WIDE. At
                // charge speed a 0.08m body travels ~0.2m per physics tick, so
                // Discrete collision walks it straight through the terrain and
                // it is gone. Sweeping is the only thing that catches it. A big
                // one cannot outrun its own width: the cheaper speculative test.
                rb.collisionDetectionMode = scale < DrawingConfig.GolemSweepBelow
                    ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.ContinuousSpeculative;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                // the script owns facing (yaw-only slerp in Step). Free
                // physics rotation let every hop landing pitch the body
                // nose-down faster than the slerp recovered - the golem
                // spent its life staring at the ground.
                rb.freezeRotation = true;
            }

            // ★ A REAL COLLIDER, HIS ORDER: the shape body renders far larger
            // than any authored collider, so spells flew straight through the
            // visible golem. Size a capsule to what is actually drawn.
            var rend = go.GetComponentInChildren<Renderer>();
            var col = go.GetComponent<CapsuleCollider>();
            if (rend != null)
            {
                var b = rend.bounds;
                float body = Mathf.Max(b.size.x, b.size.y, b.size.z);
                bool tiny = true;
                foreach (var c in go.GetComponentsInChildren<Collider>())
                    if (!c.isTrigger && c.bounds.size.magnitude > body * 0.3f)
                    { tiny = false; break; }
                if (tiny)
                {
                    if (col == null) col = go.AddComponent<CapsuleCollider>();
                    col.center = go.transform.InverseTransformPoint(b.center);
                    col.height = b.size.y / go.transform.lossyScale.y;
                    col.radius = Mathf.Max(b.size.x, b.size.z) * 0.5f
                        / go.transform.lossyScale.x;
                }
            }

            var dmg = go.GetComponent<Element>();
            if (dmg != null)
            {
                dmg.Rename(go.GetInstanceID()); // the snapshot's name for it, the stand-in's too
                dmg.MaxStrength = Mathf.Max(DrawingConfig.GolemMinStrength,
                    Element.StrengthFromBody(scale, rb != null ? rb.mass : 0f));
                dmg.Health = dmg.MaxStrength;
            }

            // it rises WHOLE: nothing can kill it mid-birth, so a golem always
            // gets to take at least one step
            if (g != null) g.SizeMul = scale;
            if (g != null) g._safeUntil = Time.time + DrawingConfig.GolemBirthShield;

            // spread it thin enough and it cannot hold together: two golems
            if (go.GetComponent<DensitySplit>() == null) go.AddComponent<DensitySplit>();

            // weight you can see before it crushes
            if (go.GetComponent<WeightSag>() == null) go.AddComponent<WeightSag>();

            // the ground that raised it decides what it resists and how it looks
            BiomeStamp.Apply(go, at);
            return g;
        }

        void Awake()
        {
            _lookAt = Time.time + Random.value * DrawingConfig.GolemLookSeconds; // spread across frames
            _rb = GetComponent<Rigidbody>();
            _me = GetComponent<Creature>();
            _dmg = GetComponent<Element>();
            // a golem walks and decides, so it is alive by the same test
            if (_dmg != null && _dmg.Natural.Int <= 0f)
            {
                var n = _dmg.Natural; n.Int = 1f; n.Courage = 1f; _dmg.Natural = n;
                var d = _dmg.Data; d.Int = 1f; d.Courage = 1f; _dmg.Data = d;
            }
            _charge = GetComponent<ChargeAttack>();
            if (_charge == null) _charge = gameObject.AddComponent<ChargeAttack>();

            // eyes ride a blob BONE, whose axes point wherever the rig says -
            // usually not where the golem walks. Aim their idle gaze at the
            // body so it reads as a creature looking around, not an ornament.
            _eyes = GetComponentInChildren<GooglyEyes>();
            if (_eyes != null) _eyes.Facing = transform;
            _circleSign = (GetInstanceID() & 1) == 0 ? 1 : -1;
            // born angry: it goes for whoever woke it, from any direction
            _birthRageUntil = Time.time + DrawingConfig.GolemBirthRage;
            _alertUntil = _birthRageUntil;
            _lastPos = transform.position;

            // it never just disappears: whatever kills it, it comes apart in
            // its own colour so you can see it happen
            if (_dmg == null) _dmg = GetComponent<Element>();
            if (_dmg != null)
            {
                _dmg.OnDeath += _ => Poof();
                // every wound reads: a chip of it flies off where it was hit
                _dmg.OnDamaged += (amount, _) =>
                {
                    Felt();
                    if (amount < 2f) return;
                    var view = GetComponent<StateView>();
                    Color c = view != null && view.DriveTint ? view.Tint : Color.gray;
                    GrammarFX.PuffBurst(transform.position + Vector3.up * 0.3f, c,
                        amount > 15f ? 4 : 2);
                    if (_eyes != null) _eyes.SetMood(EyeMood.Scared, 0.35f);
                };
            }

            PickWander();
        }

        /// The end: a burst of the stuff it was made of, then gone.
        public void Poof()
        {
            Color c = Color.gray;
            var view = GetComponent<StateView>();
            if (view != null && view.DriveTint) c = view.Tint;

            GrammarFX.PuffBurst(transform.position + Vector3.up * 0.2f, c, 7);
            if (FxLibrary.I != null)
                FxLibrary.SpawnTinted(FxLibrary.I.Poof, transform.position + Vector3.up * 0.2f, c);
            // it goes as what it was made of (stone crumbles, water splashes, gas whooshes): bigger is louder and lower
            float big = Mathf.InverseLerp(0.5f, 3f, transform.localScale.y);
            var phase = view != null ? view.Phase : MatterPhase.Solid;
            Sfx end = phase == MatterPhase.Liquid ? Sfx.LiquidImpact : phase == MatterPhase.Gas ? Sfx.ExpandImpact : Sfx.BreakStone;
            if (!Juice.Sound(end, transform.position, Mathf.Lerp(0.55f, 1f, big), Mathf.Lerp(1.15f, 0.8f, big)))
                Juice.Thud(transform.position);
        }

        float _left = DrawingConfig.GolemLifeSeconds;
        /// A map creature (CreatureSpawn): no visitor's clock.
        [HideInInspector] public bool Permanent; // serialized: the halves of a split keep it

        readonly Footfalls _feet = new Footfalls();

        void Update() { using (PerfMarkers.UpdGolems.Auto()) Turn(); }

        void Turn()
        {
            // its steps, by the rule its stand-ins on the other machines use
            _feet.TickHeavy(transform.position, transform.localScale.y, false, Time.deltaTime);

            // one that slipped through the world dies where you last saw it,
            // rather than falling forever out of sight
            if (transform.position.y < DrawingConfig.GolemFloorY && _dmg != null)
                _dmg.TakeDamage(_dmg.Health + 1f, "swallowed by the ground");

            // ★ A GOLEM IS A VISITOR, NOT A RESIDENT (his call: lobby golems
            // forever = annoying). Time out like a summoned zombie: the poof,
            // no strength-death, no debris shower every thirty seconds.
            if (Permanent) return;
            _left -= Time.deltaTime;
            if (_left <= 0f)
            {
                Poof();
                DrawingWorld.Instance?.LogEvent("the golem crumbles back to rest");
                Destroy(gameObject);
            }
        }

        void PickWander()
        {
            float a = Random.value * Mathf.PI * 2f;
            _wander = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            _pickAt = Time.time + Random.Range(2.5f, 6f);
        }

        /// ★ A GOLEM SHEDS ITS AREA. The definition's area - a rock prefab for
        /// a solid golem, a liquid blob for a liquid one - is raised around it
        /// on a beat, carrying the golem's own numbers, and if it is marked
        /// spreading it lands on whatever is nearby. That is "solid drops rocks
        /// around, liquid drops liquid blobs around", from the same machinery
        /// a meteor shower uses. Nothing here knows what a rock is.
        long _shedBeat = -1;
        void Shed()
        {
            if (Worn == null || !Worn.HasAoe) return;
            if (!WorldClock.IsBeat(DrawingConfig.GolemShedSeconds, GetInstanceID(), ref _shedBeat)) return;
            var area = SpellBook.Live.Aoe(Worn.Aoe);
            if (area == null) return;

            var el = GetComponent<Element>();
            var load = el != null ? el.Data : Worn.Payload;
            // a body's Temp is its whole warmth, a payload's is heat handed over:
            // the blobs carry the heat its definition is made of plus what it is off its own natural
            if (el != null) load.Temp = el.Data.Temp - el.Natural.Temp + Worn.Payload.Temp;
            Vector3 d = Random.insideUnitCircle.normalized;
            Vector3 at = transform.position + new Vector3(d.x, 0.8f, d.y) * 0.6f + area.Offset;
            var mote = SpellParticle.Emit(ParticleKind.Push, at,
                (new Vector3(d.x, 0.4f, d.y)).normalized, 1f, 1);
            if (mote == null) return;
            mote.Data = load.Scaled(DrawingConfig.GolemShedShare).Clamped();
            mote.OwnerId = OwnerId;   // the rocks answer to whoever the golem does
            // stray rocks hitting a teammate are ACCIDENTS and accidents land
            // (his rule) - only the HUNT is team-aware
            mote.SrcSize = DrawingConfig.RuneSizeMin;
            mote.Vel = new Vector3(d.x, 0.5f, d.y).normalized * DrawingConfig.GolemShedSpeed;
            mote.Wake();
            mote.WearArea(area);
        }

        void FixedUpdate()
        {
            using (PerfMarkers.GolemBrains.Auto()) Think();
        }

        void Think()
        {
            Shed();
            if (_rb == null || _rb.isKinematic) return;
            if (_dmg != null && _dmg.Health <= 0f) return;
            // a guard's charge ends at the edge of its ground
            if (_charge != null && _charge.Running && LeavingGround(_charge.Heading)) _charge.Halt();
            // the charge owns movement while it runs, and while it is dazed
            // afterwards the golem just stands there shaking it off
            if (_charge != null && _charge.Busy) return;

            float mul = _me != null ? _me.SpeedMultiplier : 1f;
            if (mul <= 0.01f) return;

            if (Possessed)
            {
                // the rider steers; standing still it turns to the rider's look
                if (PossessedMove.sqrMagnitude > 0.01f)
                {
                    _wander = PossessedMove.normalized;
                    Step(_wander, mul);
                }
                else if (PossessedFace.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(PossessedFace, Vector3.up), 6f * Time.fixedDeltaTime);
                return;
            }

            float dt = Time.fixedDeltaTime;
            // how far it really got since last tick: a wall shows up here
            Vector3 stepped = transform.position - _lastPos;
            stepped.y = 0f;
            float moved = stepped.magnitude / Mathf.Max(1e-4f, dt);
            _lastPos = transform.position;
            var behaviour = Behaviour;
            bool wild = behaviour == CreatureBehaviour.Skittish; // the nerve: wands and spells scare it
            bool steady = behaviour == CreatureBehaviour.Hunts || behaviour == CreatureBehaviour.Guards;

            // ★ EVERYONE FEARS A SPELL BEING MADE (his rule): a fresh spell
            // or a blast nearby sends the golem off the other way, no charge.
            // Hunters and guards stand their ground.
            if (!steady && WorldEvents.TryGetLoudest(2.5f, out var scare)
                && (scare.Kind == WorldEventKind.Spell || scare.Kind == WorldEventKind.Explosion)
                && scare.Intensity >= 2f)
            {
                Vector3 away = transform.position - scare.Pos;
                away.y = 0f;
                if (away.sqrMagnitude > 0.01f && away.sqrMagnitude < FearRange * FearRange)
                {
                    if (scare.Time != _lastScareAt) { _lastScareAt = scare.Time; Frighten(DrawingConfig.GolemFearBlast); }
                    _wander = away.normalized;
                    _pickAt = Time.time + 1.5f;
                    if (_eyes != null) _eyes.SetMood(EyeMood.Scared, 1f);
                    _sawPrey = false;
                    Step(_wander, mul * 1.5f);
                    return;
                }
            }

            // what it looks for, GolemLookSeconds apart (the steps and the
            // nerve still run every physics step): the searches are the costly
            // part, and golems take turns so they never all look on one frame
            if (Time.time >= _lookAt)
            {
                _lookAt = Time.time + DrawingConfig.GolemLookSeconds;
                _seen = NearestTarget();
                _seenSpell = wild ? NearestSpell() : null;
            }

            // the nearest AWAKE spell wears a wild one's nerve down; a sleeping
            // preview over a seal scares nothing
            SpellParticle near = null;
            float nearSqr = FearRange * FearRange;
            if (wild && _seenSpell != null && !_seenSpell.Dormant && !_seenSpell.Dead)
            {
                float dsq = (_seenSpell.transform.position - transform.position).sqrMagnitude;
                if (dsq < nearSqr) { nearSqr = dsq; near = _seenSpell; }
            }

            // anything alive in sight is an enemy - no teams, no owner
            var prey = _seen != null ? _seen : null;
            // ★ CHARGE ONLY LOOKS FOR ENEMIES (his order): whatever slipped
            // into sight, a non-enemy is NOT prey - the golem patrols on,
            // exactly like the zombie. Second gate on purpose: the target
            // filter and the verb must both agree before anything charges.
            if (prey != null && !Teams.Enemies(Teams.OfOwner(OwnerId), TeamOf(prey)))
                prey = null;
            if (prey != null && prey != _loggedPrey)
            {
                // the FOLLOWING moment itself, on the record
                _loggedPrey = prey;
                using (PerfMarkers.Logs.Auto())
                    Debug.Log($"[SpellyZombie] golem (owner {OwnerId}/{Teams.OfOwner(OwnerId)}) " +
                        $"HUNTS {prey.name} ({TeamOf(prey)})");
            }

            // ---- nerve (wild only): what scares it this tick, per second ----
            if (wild)
            {
                float fear = 0f;
                if (near != null) fear += DrawingConfig.GolemFearSpell * (1f - Mathf.Sqrt(nearSqr) / FearRange);
                if (prey != null && IsPlayer(prey))
                {
                    Vector3 toPrey = prey.position - transform.position;
                    toPrey.y = 0f;
                    float d = toPrey.magnitude;
                    if (d > 0.1f)
                    {
                        // closing in scares it; standing still does not
                        float closing = Vector3.Dot(PlayerVelocity(prey), -toPrey / d);
                        if (closing > 0.5f) fear += DrawingConfig.GolemFearApproach * closing;
                    }
                    if (PenDown(prey)) fear += DrawingConfig.GolemFearDrawing;
                }
                if (Time.time < _birthRageUntil) fear *= 0.35f;
                _nerve = fear > 0f ? Mathf.Max(0f, _nerve - fear * dt)
                                   : Mathf.Min(1f, _nerve + DrawingConfig.GolemNerveRecover * dt);
            }

            if (prey != null)
            {
                // the face does the talking: it stares at whatever it found
                if (_eyes != null)
                {
                    _eyes.LookTarget = prey.position + Vector3.up * 1.2f;
                    if (!_sawPrey) _eyes.SetMood(EyeMood.Mad, 1.2f);
                }
                _sawPrey = true;
                _lastSeen = prey.position;
                _lastSeenUntil = Time.time + 6f;

                Vector3 to = prey.position - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                // a working wand is respected by a wild one as far as its nerve
                // allows; wandless is the danger state, hunted whatever the nerve
                bool armed = wild && ArmedPlayer(prey);
                bool rage = Time.time < _rageUntil;
                if (armed && !rage && _nerve < DrawingConfig.GolemFleeAt)
                {
                    // scared: away from the nearest scare, a spell before a wizard
                    Vector3 from = near != null ? near.transform.position : prey.position;
                    Vector3 away = transform.position - from;
                    away.y = 0f;
                    if (away.sqrMagnitude > 0.01f) _wander = away.normalized;
                    if (_eyes != null) _eyes.SetMood(EyeMood.Scared, 0.6f);
                    Step(_wander, mul * 1.3f);
                    Cornered(moved, WalkSpeed * mul * 1.3f, dt);
                    return;
                }
                if (armed && !rage && _nerve < DrawingConfig.GolemBraveAt)
                {
                    // wary: it backs off to its respect distance and circles there
                    float respect = DrawingConfig.GolemRespectRange;
                    if (_eyes != null) _eyes.SetMood(EyeMood.Scared, 0.6f);
                    bool backing = dist < respect * 0.9f;
                    if (backing) _wander = -to.normalized;
                    else if (dist <= respect * 1.2f) _wander = Vector3.Cross(Vector3.up, to.normalized) * _circleSign;
                    else if (Time.time >= _pickAt) PickWander();
                    Step(_wander, mul);
                    Cornered(moved, backing ? WalkSpeed * mul : 0f, dt);
                    return;
                }
                _blocked = 0f;
                if (TryCast(prey, to, dist)) return;
                // a guard charges only what stands on its ground; the rest it casts at
                if (_charge != null && (!Guarding || OnGround(prey.position)) && _charge.TryStart(prey.position))
                {
                    // the second fact for his console: WHO it committed on
                    using (PerfMarkers.Logs.Auto())
                        Debug.Log($"[SpellyZombie] golem (owner {OwnerId}/{Teams.OfOwner(OwnerId)}) " +
                            $"CHARGES {prey.name} ({TeamOf(prey)})");
                    return;
                }
                if (to.sqrMagnitude > 0.04f) _wander = to.normalized;
            }
            else
            {
                _blocked = 0f;
                if (_sawPrey && _eyes != null) _eyes.SetMood(EyeMood.Neutral, 0.4f);
                _sawPrey = false;
                if (behaviour == CreatureBehaviour.Guards && _hasHome)
                {
                    // back to its post, and there it waits
                    Vector3 post = _home - transform.position;
                    post.y = 0f;
                    _wander = post.sqrMagnitude > 1.5f * 1.5f ? post.normalized : Vector3.zero;
                }
                else if (behaviour == CreatureBehaviour.Hunts && Time.time < _lastSeenUntil)
                {
                    // where it last saw someone, before giving up
                    Vector3 last = _lastSeen - transform.position;
                    last.y = 0f;
                    if (last.sqrMagnitude > 1f) _wander = last.normalized;
                    else _lastSeenUntil = 0f;
                }
                else if (wild && near != null && _nerve < DrawingConfig.GolemFleeAt)
                {
                    // no one in sight, still shaken: away from the spell
                    Vector3 away = transform.position - near.transform.position;
                    away.y = 0f;
                    if (away.sqrMagnitude > 0.01f) { _wander = away.normalized; _pickAt = Time.time + 1f; }
                }
                else if (Time.time >= _pickAt) PickWander();
            }

            // a guard never steps past its ground: at the edge it plants and faces what it wants
            if (LeavingGround(_wander))
            {
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(_wander, Vector3.up), 6f * dt);
                _wander = Vector3.zero;
            }
            Step(_wander, mul);
        }

        float _lookAt;
        Transform _seen;
        SpellParticle _seenSpell;

        bool Guarding => Behaviour == CreatureBehaviour.Guards && _hasHome;

        /// Within its guard range of its post, measured along the ground.
        bool OnGround(Vector3 at)
        {
            Vector3 d = at - _home;
            d.y = 0f;
            return d.sqrMagnitude <= _guardRange * _guardRange;
        }

        /// A guard past its range and still heading out.
        bool LeavingGround(Vector3 heading)
        {
            if (!Guarding || OnGround(transform.position)) return false;
            Vector3 from = transform.position - _home;
            from.y = 0f;
            return Vector3.Dot(heading, from) > 0f;
        }

        // whoever hurt it lately: a guard casts at them wherever they stand
        Transform _grudge;
        float _grudgeUntil;

        /// The nearest awake spell within fear range, or null.
        SpellParticle NearestSpell()
        {
            SpellParticle near = null;
            float nearSqr = FearRange * FearRange;
            var living = SpellParticle.Living;
            for (int i = 0; i < living.Count; i++)
            {
                var sp = living[i];
                if (sp == null || sp.Dormant || sp.Dead) continue;
                float dsq = (sp.transform.position - transform.position).sqrMagnitude;
                if (dsq < nearSqr) { nearSqr = dsq; near = sp; }
            }
            return near;
        }

        void Frighten(float amount) => _nerve = Mathf.Max(0f, _nerve - amount);

        /// Backing off and getting nowhere for a moment: it turns and fights.
        void Cornered(float moved, float want, float dt)
        {
            if (want <= 0.01f) { _blocked = 0f; return; }
            _blocked = moved < want * 0.3f ? _blocked + dt : 0f;
            if (_blocked < DrawingConfig.GolemCorneredSeconds) return;
            _blocked = 0f;
            _nerve = 1f;
            _rageUntil = Time.time + DrawingConfig.GolemRageSeconds;
            if (_eyes != null) _eyes.SetMood(EyeMood.Mad, DrawingConfig.GolemRageSeconds);
            Debug.Log("[SpellyZombie] golem cornered: it turns and fights");
        }

        // a player is the host's own body or a friend's stand-in; the friend's
        // wand state rides their presence (NetAvatar.Wandless)
        static bool IsPlayer(Transform t)
            => t.GetComponent<SimpleFPSController>() != null || t.GetComponent<NetAvatar>() != null;

        static bool ArmedPlayer(Transform t)
        {
            var pilot = t.GetComponent<SimpleFPSController>();
            if (pilot != null) return WandState.Armed(pilot);
            var a = t.GetComponent<NetAvatar>();
            return a != null && !a.Wandless;
        }

        static bool PenDown(Transform t)
        {
            var pilot = t.GetComponent<SimpleFPSController>();
            if (pilot != null) return pilot.IsLocalViewer && SurfaceDrawer.IsPenActive;
            var a = t.GetComponent<NetAvatar>();
            return a != null && a.PenDown;
        }

        static Vector3 PlayerVelocity(Transform t)
        {
            var pilot = t.GetComponent<SimpleFPSController>();
            if (pilot != null) return pilot.Velocity;
            var a = t.GetComponent<NetAvatar>();
            return a != null ? a.Velocity : Vector3.zero;
        }

        static Team TeamOf(Transform t)
        {
            var a = t.GetComponent<NetAvatar>();
            return a != null ? Teams.OfOwner(NetSync.OwnerIdOf(a.Id)) : Teams.Of(t);
        }

        /// Walks by SKIPPING: a little hop with every step rather than a glide.
        void Step(Vector3 dir, float mul)
        {
            Vector3 want = dir * (WalkSpeed * mul);
            var v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(want.x, v.y, want.z);
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir, Vector3.up), 6f * Time.fixedDeltaTime);

            if (Time.time < _skipAt) return;
            _skipAt = Time.time + DrawingConfig.GolemSkipEvery / Mathf.Max(0.2f, mul);
            if (Mathf.Abs(v.y) < 0.6f)  // only when it is actually on the ground
                _rb.AddForce(Vector3.up * DrawingConfig.GolemSkipHop, ForceMode.VelocityChange);
        }

        /// Hurt: it turns on whoever it can place and feels around for a while.
        void Felt()
        {
            _alertUntil = Time.time + DrawingConfig.GolemAlertSeconds;
            Frighten(DrawingConfig.GolemFearHit);
            int by = _dmg != null ? _dmg.LastHitBy : -1;
            // a wound booked to its own side is no attack: its own burning is booked to its owner
            if (by >= 0 && !Teams.Enemies(Teams.OfOwner(OwnerId), Teams.OfOwner(by))) return;
            var who = BodyOfOwner(by);
            if (who == null) return;
            _grudge = who;
            _grudgeUntil = Time.time + DrawingConfig.GolemAlertSeconds;
            Vector3 to = who.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.04f) _wander = to.normalized;
        }

        static Transform BodyOfOwner(int owner)
        {
            if (owner < 0) return null;
            if (owner == Grimoire.LocalPlayerId)
                foreach (var p in SimpleFPSController.All)
                    if (p != null && p.IsLocalViewer) return p.transform;
            return NetSync.AvatarTransformOf(owner);
        }

        Transform NearestTarget()
        {
            Transform best = null;
            float sight = SightRange * Reach;
            float bestSqr = sight * sight;
            Team mine = Teams.OfOwner(OwnerId);

            // eyes, not ears: in front and in view, or felt this close, or hurt lately;
            // a hunter looks everywhere, a guard at its ground and at whoever hurt it lately
            var behaviour = Behaviour;
            bool alert = Time.time < _alertUntil || behaviour == CreatureBehaviour.Hunts;
            bool guarding = Guarding;
            bool Near(Transform t) => !guarding || OnGround(t.position)
                || (t == _grudge && Time.time < _grudgeUntil);
            float feel = DrawingConfig.GolemFeelRange * Reach;
            float feelSqr = feel * feel;
            float halfCos = Mathf.Cos(DrawingConfig.GolemSightAngle * 0.5f * Mathf.Deg2Rad);
            Vector3 eye = _eyes != null ? _eyes.transform.position
                : transform.position + Vector3.up * (0.5f * transform.lossyScale.y);
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            bool Notices(Transform t, float sqr)
            {
                if (sqr <= feelSqr) return true;
                Vector3 to = t.position + Vector3.up * 0.8f - eye;
                if (!alert)
                {
                    Vector3 flat = to;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > 1e-4f && Vector3.Dot(flat.normalized, fwd) < halfCos) return false;
                }
                float len = to.magnitude;
                if (len < 0.05f) return true;
                // only the nearest thing on the line matters, so that is what is asked for: every hit
                // along a line through an army costs many times as much, ten times a second per golem
                if (!Physics.Raycast(eye, to / len, out var first, len,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
                Collider blocker = first.collider;
                if (blocker != null && blocker.transform.IsChildOf(transform))
                {
                    // its own body was in the way of its own eye: look past it
                    blocker = null;
                    int n = Physics.RaycastNonAlloc(eye, to / len, _sight, len,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    float nearest = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        var h = _sight[i];
                        if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                        if (h.distance < nearest) { nearest = h.distance; blocker = h.collider; }
                    }
                }
                if (blocker == null || blocker.transform.IsChildOf(t)) return true;
                var shell = ZombieOwner.From(blocker); // a zombie's paint shell sits outside its hierarchy
                return shell != null && shell.transform == t;
            }
            if (!_saidTeam)
            {
                _saidTeam = true;
                using (PerfMarkers.Logs.Auto()) Debug.Log($"[SpellyZombie] golem serves owner {OwnerId} / team {mine}");
            }

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDead) continue;
                // THE ONE TEAM LABEL: a summoned golem never hunts its own
                // team; a Neutral golem is against everyone (his rule)
                if (!Teams.Enemies(mine, Teams.Of(p))) continue;
                // A DISGUISE FOOLS NATURE TOO. A golem that walks past every
                // bench in the village but beelines for the one that is an
                // acolyte would make hiding pointless wherever golems roam.
                if (ShapeShift.Disguised(p)) continue;
                if (!Near(p.transform)) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr && Notices(p.transform, d)) { bestSqr = d; best = p.transform; }
            }
            foreach (var a in NetAvatar.All)
            {
                if (a == null || a.Downed || a.Disguised) continue;
                if (!Teams.Enemies(mine, Teams.OfOwner(NetSync.OwnerIdOf(a.Id)))) continue;
                if (!Near(a.transform)) continue;
                float d = (a.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr && Notices(a.transform, d)) { bestSqr = d; best = a.transform; }
            }
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                // a summoned zombie is the acolyte team, a wild one nobody's - same law
                if (!Teams.Enemies(mine, Teams.Of(z))) continue;
                if (!Near(z.transform)) continue;
                float d = (z.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr && Notices(z.transform, d)) { bestSqr = d; best = z.transform; }
            }
            // golems of another team are prey too (his rule): yours fight
            // the wild ones, the wild ones fight yours, nearest first
            foreach (var g in All)
            {
                if (g == null || g == this || !g.Alive) continue;
                if (!Teams.Enemies(mine, Teams.OfOwner(g.OwnerId))) continue;
                if (!Near(g.transform)) continue;
                float d = (g.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr && Notices(g.transform, d)) { bestSqr = d; best = g.transform; }
            }
            return best;
        }
    }

    /// How high a blob body really reaches above its root. Its scale does not say (the blob is a
    /// ball centred on its feet: a golem five times the size stands two and a half metres tall, not
    /// five, so a rider seated by the scale floated far above it) and the skin's import bounds lie.
    /// Measured from the posed skin, twice a second, and only while somebody asks.
    public class BodyTop
    {
        SkinnedMeshRenderer _skin;
        bool _looked;
        float _above = -1f, _nextAt;

        public float Above(Transform root)
        {
            if (_above >= 0f && Time.time < _nextAt) return _above;
            _nextAt = Time.time + 0.5f;
            if (!_looked) { _looked = true; _skin = root.GetComponentInChildren<SkinnedMeshRenderer>(); }
            _above = _skin != null && _skin.sharedMesh != null
                ? Mathf.Max(0.1f, ShapeShift.SkinBounds(_skin).max.y - root.position.y)
                : root.localScale.y * 0.95f;
            return _above;
        }
    }
}
