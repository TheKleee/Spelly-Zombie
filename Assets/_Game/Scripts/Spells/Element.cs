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
            Mathf.Lerp(DrawingConfig.StrengthFloorMul, 1f, StrengthFraction);

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
            var born = SpellyMap.BiomeAt(transform.position);
            if (born != null)
            {
                var ground = born.Natural;
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                    Natural[i] = SpellPayload.TargetFor(i, Natural[i], Natural[i] + ground[i]);
                if (born.StrengthCap > 0f)
                    Natural.Strength = Mathf.Min(Natural.Strength, born.StrengthCap);
            }

            // TEMPERATURE IS REAL DEGREES. A thing with no biome sits at room
            // temperature, not at zero - the same scale Matter has always used,
            // so one store can serve both. A biome's HeatOffset shifts it from
            // there, which is why a magma rock is naturally 200 and content.
            Natural.Temp += RoomTemp;

            Data = Natural;            // born full and born itself

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
        /// happens to it. The same two lines for a wall, a crate, a zombie and
        /// a player - there is no list of who this applies to.
        void Beat(float span)
        {
            if (_dead) return;
            SpellLaw.Drift(this, span);
            Bear(span);

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
