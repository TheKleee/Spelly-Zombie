using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum ParticleKind
    {
        Push,                         // level 0 — pure force
        Light, Dark,                  // level 1 — energy; opposites annihilate
        Dense, Spread, Glue, Repel,   // level 2 — property carriers
        Spark, Frost,                 // level 3 — elemental matter
        Lightning, Laser, Shadow,     // condensed states (level 3 — they absorb further)
        Flame,                        // GRAMMAR v4: Spark manifested by Dense — persistent fire, merges bigger
        BlackHole,                    // GRAMMAR v4: Dark lvl2 — pulls things in (family: Dark)
        BarrierMote                   // GRAMMAR v4: Dense+Spread paradox — isolates what it touches
    }

    /// THE LAW (Marko's matter-level rule, SPELL_PARTICLES.md): every particle
    /// has a matter level and carries attributes (temperature, luminance,
    /// density, stickiness, velocity). On collision the lower (more ethereal)
    /// particle DISSOLVES into the higher one, donating its attributes; equal
    /// levels annihilate if opposite, merge if same kind, otherwise just
    /// bounce. Transformations are ATTRIBUTE THRESHOLDS, not recipes:
    /// luminous + dense → LIGHTNING → LASER; dark + dense → SHADOW → VOID
    /// RIFT; hot + dense → fireball; cold + dense → ice shard. Density is
    /// WEIGHT: below air density a particle floats. Zombies fear dangerous
    /// particles they can SEE — darkness makes flames invisible to them.
    public class SpellParticle : MonoBehaviour
    {
        public ParticleKind Kind;
        public float Power = 1f;
        public Vector3 Vel;
        public float SrcSize = 1f; // zone radius of the rune that emitted it —
                                   // rides the chain so a rift knows how big a
                                   // demon the original DRAWING deserves
        public int Echo;           // ECHO powerup stacks: landing may re-emit

        // ---- GRAMMAR v4 (SPELL_PARTICLES.md): same+same levels up, opposites
        // synthesize, and every product carries its ANCESTRY — all 12 runes in
        // one chain summons the Demon.
        public int GrammarLevel = 1;  // 1 = base · 2 = radiant self · 3 = ultimate
        public ulong Lineage;         // union of every rune that fed this chain
        public int SealId;            // which SEAL emitted this — siblings of one
                                      // drawing prefer each other (Marko's rule:
                                      // runes inside a seal combine TOGETHER first)

        /// SUSTAIN LAW (Marko's ruling, Jul 20): a rune re-emits only when its
        /// particle is fully GONE — and "gone" follows combinations. When this
        /// particle dies INTO something (an eater, a field, a matter blob, a
        /// demon), that something is recorded here; the emitting rune walks the
        /// chain and waits for the FINAL product to disappear. One light rune
        /// makes one light, forever — never an accidental lightning.
        public Object BecameObj;
        public bool Dead => _dead;

        // the attribute payload every particle carries
        public float Temp, Lum, Density, Stick;

        const float AirDensity = 0.55f;      // effective density below this → rises
        const float VisibilityFloor = 0.15f; // dimmer than this = zombies can't see the danger
        const float PlasmaDensity = 1.0f;    // + luminance/darkness/heat → transformation
        const float RiftDensity = 2.2f;      // lightning→laser, shadow→void rift
        const float FireballTemp = 50f;

        static readonly List<SpellParticle> All = new List<SpellParticle>();
        static readonly Collider[] _scan = new Collider[32];

        Renderer _rend;
        float _age, _fearTick, _strikeTick, _retarget;
        float _chaosLeft;      // ChaosGrip paradox: random impulses, uncontrollable
        float _isolatedUntil;  // barrier-moted: refuses ALL chemistry until this
        float _auraTick;       // lvl2 particles radiate their effect around them
        float _donateTick;     // persistent particles (Flame, lvl2 Glue) re-donate on a beat
        float _appetite;       // 0..1 personality: how much this mote stalks LIVING things
        float _lureRetarget;
        Transform _lure;       // the thing this mote is currently stalking
        Transform _prey;
        int _generation;
        bool _dead, _settled, _explosive;

        // ------------------------------------------------------------- birth --
        public static SpellParticle Emit(ParticleKind kind, Vector3 pos, Vector3 dir,
            float intensity, int generation = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "P_" + kind;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * (kind == ParticleKind.Push ? 0.09f : 0.14f);
            go.GetComponent<SphereCollider>().isTrigger = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; // moves by script; triggers do the touching

            var p = go.AddComponent<SpellParticle>();
            p.Kind = kind;
            p.Power = Mathf.Clamp(intensity, 0.2f, 2f);
            p._generation = generation;
            p._appetite = Random.value; // personality: some motes stalk, some are lazy

            // the payload IS the rune: fixed spark heat (Marko: 3 hits on one
            // target = 3× the bump), symmetric both ways — the freeze POINT was
            // rescaled instead of making frost weaker
            float k = Mathf.Lerp(0.75f, 1.5f, Mathf.Clamp01(intensity));
            switch (kind)
            {
                case ParticleKind.Spark: p.Temp = DrawingConfig.SparkHeatDelta * k; break;
                case ParticleKind.Frost: p.Temp = -DrawingConfig.SparkHeatDelta * k; break;
                case ParticleKind.Light: p.Lum = k; break;
                case ParticleKind.Dark: p.Lum = -k; break;
                case ParticleKind.Dense: p.Density = 1.2f * k; break;
                case ParticleKind.Spread: p.Density = -1.2f * k; break;
                case ParticleKind.Glue: p.Stick = k; break;
                case ParticleKind.Repel: p.Stick = -k; break;
            }

            // push particles DRIFT with intent (Marko: "their own movement
            // shouldn't be as fast so that they can be combined") — the
            // DIRECTION is the payload, the mote itself is slow enough that
            // two arrows in one seal attract and entwine into the tornado.
            // Everything else blooms off the ink and hangs around the seal
            // until a force moves it (user rule: no drifting off on their own).
            float speed = kind == ParticleKind.Push ? 1.8f : 0.9f;
            p.Vel = dir.normalized * speed
                + Random.insideUnitSphere * (kind == ParticleKind.Push ? 0.2f : 0.22f);

            if (kind == ParticleKind.Light)
            {
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.range = 4.5f; l.intensity = 2.2f;
                l.color = new Color(1f, 0.96f, 0.8f);
            }

            p.RefreshLook();
            return p;
        }

        void Awake()
        {
            _rend = GetComponent<Renderer>();
            All.Add(this);
            if (All.Count > DrawingConfig.ParticleCap && All[0] != null)
                All[0].Die(); // oldest yields — mayhem needs a fence
        }

        void OnDestroy() => All.Remove(this);

        void Die()
        {
            if (_dead) return;
            _dead = true;
            All.Remove(this);
            Destroy(gameObject);
        }

        // ------------------------------------------------------------ living --
        void Update()
        {
            if (_dead) return;
            float dt = Time.deltaTime;
            _age += dt;

            if (Kind == ParticleKind.Lightning) TickLightning(dt);
            else if (Kind == ParticleKind.Shadow) TickShadow(dt);
            else if (Kind == ParticleKind.BlackHole) TickBlackHole(dt);

            // GRAMMAR v4: lvl2 particles radiate; flames burn where they sit;
            // chaos-grip products jitter uncontrollably
            if (GrammarLevel >= 2 || Kind == ParticleKind.Flame) TickAura(dt);
            if (_chaosLeft > 0f)
            {
                _chaosLeft -= dt;
                Vel += Random.insideUnitSphere * 6f * dt;
                _settled = false;
            }

            if (!_settled)
            {
                // DENSITY IS WEIGHT (Marko's rule): heavy falls, thinned floats
                // — gently. Strong drag keeps particles hovering near their
                // seal; only real forces (push hits, rifts, explosions) fling
                // them anywhere.
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.Shadow
                    && Kind != ParticleKind.BlackHole)
                    Vel += Vector3.down * (EffDensity() - AirDensity) * 2.5f * dt;

                // MOTES SEEK EACH OTHER (Marko: "they almost always miss") —
                // a gentle pull toward the nearest sibling makes the matter
                // law actually happen instead of depending on a lucky graze;
                // close enough = the law fires NOW, no trigger-roulette
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.Shadow
                    && Kind != ParticleKind.BlackHole && Kind != ParticleKind.BarrierMote)
                {
                    // SEAL KIN FIRST (Marko's rule): particles born of the SAME
                    // DRAWING want each other above all — the runes you enclosed
                    // together are a recipe, not roommates. Foreign particles
                    // only attract when no seal-sibling is in reach.
                    // VECTORS LOVE VECTORS: a Push only seeks other Pushes (two
                    // arrows entwine into the tornado) and never distracts an
                    // essence particle from its own chemistry.
                    bool iAmPush = Kind == ParticleKind.Push;
                    SpellParticle near = null;
                    bool nearIsKin = false;
                    float baseRange = DrawingConfig.ParticleKinRange * DrawingConfig.ParticleKinRange;
                    float kinRange = baseRange * 4f; // same-seal reach: 2× the distance
                    float bestSqr = float.MaxValue;
                    for (int i = 0; i < All.Count; i++)
                    {
                        var o = All[i];
                        if (o == this || o == null || o._dead) continue;
                        if ((o.Kind == ParticleKind.Push) != iAmPush)
                        {
                            // THE ARROW IS WIND (Marko: "their directions apply
                            // to other particles"): a push HERDS every non-push
                            // particle near it along its own direction — while
                            // still seeking only its fellow vectors to entwine
                            if (iAmPush && o.Kind != ParticleKind.BarrierMote)
                            {
                                float wd = (o.transform.position - transform.position).sqrMagnitude;
                                if (wd < baseRange)
                                {
                                    o.Vel += Vel.normalized * (3f * Power * dt);
                                    o._settled = false; // wind wakes resting embers
                                }
                            }
                            continue; // vectors with vectors, matter with matter
                        }
                        bool kin = SealId != 0 && o.SealId == SealId;
                        float d = (o.transform.position - transform.position).sqrMagnitude;
                        if (d > (kin ? kinRange : baseRange)) continue;
                        if (nearIsKin && !kin) continue;              // a stranger never beats family
                        if (kin && !nearIsKin) { near = o; bestSqr = d; nearIsKin = true; continue; }
                        if (d < bestSqr) { bestSqr = d; near = o; nearIsKin = kin; }
                    }
                    if (near != null)
                    {
                        // FIRST LOVE: other particles — affinity beats appetite
                        Vector3 to = near.transform.position - transform.position;
                        Vel += to.normalized * ((iAmPush ? 4.5f : 2.2f) * dt); // pushes curve hard or they'd fly past
                        if (bestSqr < 0.12f * 0.12f && GetInstanceID() < near.GetInstanceID())
                            ResolveLaw(this, near);
                    }
                    else if (!iAmPush)
                    {
                        TickLure(dt); // no kin around — maybe stalk something ALIVE
                    }
                }
                // condensed movers (lightning, shadow, black hole) integrate
                // their own motion in Tick* — moving them here too ran them at
                // DOUBLE speed with doubled drag (verified)
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.Shadow
                    && Kind != ParticleKind.BlackHole)
                {
                    Vel *= Mathf.Max(0f, 1f - (Kind == ParticleKind.Push ? 0.25f : 1.4f) * dt);
                    transform.position += Vel * dt;
                }
            }
            else if (Kind != ParticleKind.Push && Kind != ParticleKind.BarrierMote)
            {
                // a SETTLED ember still watches: prey wandering close wakes it
                // (TickLure clears _settled, and next frame it moves again)
                TickLure(dt);
            }

            // FEAR IS VISUAL: nearby zombies that can SEE a dangerous particle
            // panic — a flame carrying darkness is invisible, and they walk
            // right into it
            _fearTick -= dt;
            if (_fearTick <= 0f)
            {
                _fearTick = 0.4f;
                if (Dangerous()) ZombieBrain.ScareVisible(transform.position, 11f, EffectiveLum());
            }

            float life = DrawingConfig.ParticleLife
                * (Kind == ParticleKind.Shadow ? 2f
                 : Kind == ParticleKind.Flame ? 2.5f     // a flame is a fixture, not a spark
                 : Kind == ParticleKind.BlackHole ? 1.5f
                 : GrammarLevel >= 2 ? 1.5f : 1f);       // leveled particles earn their time
            if (_age > life - 0.8f)
                transform.localScale *= Mathf.Max(0.01f, 1f - dt / 0.8f);
            if (_age > life || transform.localScale.x < 0.015f) Die();
        }

        /// PARTICLES FEEL ALIVE (Marko's rule): a mote with no kin nearby
        /// stalks the nearest living thing — a spark drawn on a zombie FOLLOWS
        /// it when it walks away; the hungriest quarter will even stalk a
        /// wizard (friendly fire is a personality trait now). A settled ember
        /// WAKES when prey wanders close. Gentle accel: it's a stalk, not a
        /// homing missile — you can outrun your mistakes.
        void TickLure(float dt)
        {
            if (_appetite < 0.35f) return; // the lazy third just sits there — variety reads as life

            _lureRetarget -= dt;
            if (_lureRetarget <= 0f)
            {
                _lureRetarget = 0.5f;
                _lure = null;
                float best = DrawingConfig.ParticleChaseRange * DrawingConfig.ParticleChaseRange;
                foreach (var z in Zombie.All) // zombies are the favourite prey
                {
                    if (z == null) continue;
                    float d = (z.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _lure = z.transform; }
                }
                if (_appetite > 0.75f) // only the hungriest stalk wizards
                    foreach (var pl in SimpleFPSController.All)
                    {
                        if (pl == null) continue;
                        float d = (pl.transform.position - transform.position).sqrMagnitude;
                        if (d < best) { best = d; _lure = pl.transform; }
                    }
            }
            if (_lure == null) return;

            Vector3 to = _lure.position + Vector3.up * 0.8f - transform.position;
            float range = DrawingConfig.ParticleChaseRange;
            if (to.sqrMagnitude > range * range * 1.7f) { _lure = null; return; } // it got away
            _settled = false; // the waiting ember wakes
            Vel += to.normalized * (DrawingConfig.ParticleChaseAccel * _appetite * dt);
        }

        float EffDensity()
        {
            float baseD;
            switch (Kind)
            {
                case ParticleKind.Push: baseD = AirDensity; break;          // flies straight
                case ParticleKind.Light:
                case ParticleKind.Dark: baseD = 0.35f; break;               // energy drifts
                case ParticleKind.Spark:
                case ParticleKind.Frost: baseD = 0.9f; break;               // embers sink gently
                default: baseD = 0.8f; break;
            }
            return baseD + Density * 0.45f;
        }

        bool Dangerous() =>
            Mathf.Abs(Temp) >= 20f || _explosive
            || Kind == ParticleKind.Lightning || Kind == ParticleKind.Laser
            || Kind == ParticleKind.Flame; // (the black hole is invisible — never feared, always fatal)

        /// How visible this particle is to a googly eye. Base glow by kind,
        /// dimmed by any darkness it carries — the invisible-flame trap.
        float EffectiveLum()
        {
            float glow;
            switch (Kind)
            {
                case ParticleKind.Spark: glow = 0.6f; break;
                case ParticleKind.Frost: glow = 0.35f; break;
                case ParticleKind.Lightning: glow = 2.5f; break;
                case ParticleKind.Laser: glow = 6f; break;
                case ParticleKind.Shadow: glow = -1f; break; // never seen coming
                case ParticleKind.Flame: glow = 0.9f; break; // a flame is SEEN — unless darkness dims it
                case ParticleKind.BlackHole: glow = -1f; break; // light doesn't leave it
                default: glow = 0.3f; break;
            }
            return glow + Lum;
        }

        // --------------------------------------------------------- collision --
        void OnTriggerEnter(Collider other)
        {
            if (_dead) return;
            var op = other.GetComponent<SpellParticle>();
            if (op != null)
            {
                // both sides get the event — only one resolves the law
                if (GetInstanceID() < op.GetInstanceID()) ResolveLaw(this, op);
                return;
            }
            if (other.isTrigger) return;
            Touch(other);
        }

        /// Persistent particles (flames, lvl2 grip/slip) deliver on a BEAT to
        /// whatever STAYS in them — Enter alone fired once and the beat never
        /// repeated (verified: standing in fire was free after the first lick).
        void OnTriggerStay(Collider other)
        {
            if (_dead || other.isTrigger) return;
            if (other.GetComponent<SpellParticle>() != null) return;
            bool persistent = Kind == ParticleKind.Flame
                || (GrammarLevel >= 2 && (Kind == ParticleKind.Glue || Kind == ParticleKind.Repel));
            if (persistent) Touch(other);
        }

        static int Level(ParticleKind k)
        {
            switch (k)
            {
                case ParticleKind.Push: return 0;
                case ParticleKind.Light:
                case ParticleKind.Dark: return 1;
                case ParticleKind.Dense:
                case ParticleKind.Spread:
                case ParticleKind.Glue:
                case ParticleKind.Repel: return 2;
                default: return 3; // spark, frost, and the condensed states
            }
        }

        /// GRAMMAR v4 collision resolution (SPELL_PARTICLES.md). Order:
        ///   1. barrier isolation (nothing combines through a barrier)
        ///   2. same FAMILY → LEVEL UP (lvl2 radiant self, lvl3 ultimate area)
        ///   3. opposites → PARADOX SYNTHESIS (annihilation is repealed)
        ///   4. the v2 substrate: lower level dissolves into higher, carriers
        ///      pool, otherwise plain physics. "Old passive states are kept."
        static void ResolveLaw(SpellParticle a, SpellParticle b)
        {
            if (a._dead || b._dead) return;

            // an isolated particle refuses ALL chemistry — the barrier is the
            // system's insulator (and the only way to stop an accidental Demon)
            if (Time.time < a._isolatedUntil || Time.time < b._isolatedUntil)
            {
                BounceApart(a, b, 1.2f);
                return;
            }

            // a BarrierMote doesn't combine — it ISOLATES the other particle
            if (a.Kind == ParticleKind.BarrierMote || b.Kind == ParticleKind.BarrierMote)
            {
                var mote = a.Kind == ParticleKind.BarrierMote ? a : b;
                var tgt = mote == a ? b : a;
                if (tgt.Kind != ParticleKind.BarrierMote)
                {
                    tgt._isolatedUntil = Time.time + DrawingConfig.BarrierSeconds;
                    GrammarFX.PuffBurst(mote.transform.position, new Color(0.6f, 0.9f, 1f, 0.5f));
                    mote.Die();
                }
                else BounceApart(a, b, 1f); // two motes ignore each other
                return;
            }

            var fa = RuneGrammar.Family(a.Kind);
            var fb = RuneGrammar.Family(b.Kind);

            // SAME + SAME = LEVEL UP. (Flames are manifests, not levels — they
            // pool bigger. Push+Push becomes the tornado in the vectors phase.)
            if (fa == fb)
            {
                // a FLAME is a manifest, not a level — it EATS its kin and grows
                if (a.Kind == ParticleKind.Flame || b.Kind == ParticleKind.Flame)
                {
                    var flame = a.Kind == ParticleKind.Flame ? a : b;
                    flame.Absorb(flame == a ? b : a);
                    return;
                }
                if (fa == ParticleKind.Push)
                {
                    // VECTORS (Marko's law 7): Arrow+Arrow = TORNADO, Y+Y =
                    // WHIRLPOOL. The lineage bit knows which rune threw each
                    // push; a mixed arrow+Y pair is undefined on purpose —
                    // velocities just pool.
                    ulong awayBit = RuneGrammar.Bit(RuneType.DirectionAway);
                    ulong towardBit = RuneGrammar.Bit(RuneType.DirectionToward);
                    bool bothAway = (a.Lineage & awayBit) != 0 && (b.Lineage & awayBit) != 0
                        && (a.Lineage & towardBit) == 0 && (b.Lineage & towardBit) == 0;
                    bool bothToward = (a.Lineage & towardBit) != 0 && (b.Lineage & towardBit) != 0
                        && (a.Lineage & awayBit) == 0 && (b.Lineage & awayBit) == 0;
                    if (bothAway || bothToward)
                    {
                        Vector3 vat = (a.transform.position + b.transform.position) * 0.5f;
                        var storm = TornadoField.Open(vat, (a.Power + b.Power) * 0.5f,
                            down: bothToward, a.Lineage | b.Lineage);
                        a.BecameObj = storm; b.BecameObj = storm; // arrows wait out their storm
                        a.Die(); b.Die();
                        return;
                    }
                    a.Absorb(b);
                    return;
                }
                LevelMerge(a, b);
                return;
            }

            // OPPOSITES SYNTHESIZE a paradox object — never "nothing happens"
            var paradox = RuneGrammar.ParadoxOf(a.Kind, b.Kind);
            if (paradox != ParadoxKind.None) { Synthesize(a, b, paradox); return; }

            // EXOTICS: authored cross-pairs override the substrate (Healing…)
            var exotic = RuneGrammar.ExoticOf(a.Kind, b.Kind);
            if (exotic != RuneGrammar.ExoticKind.None)
            {
                Vector3 xat = (a.transform.position + b.transform.position) * 0.5f;
                ulong xlin = a.Lineage | b.Lineage;
                float xpow = (a.Power + b.Power) * 0.5f;
                RuneGrammar.TryDemon(xlin, xat, Mathf.Max(a.SrcSize, b.SrcSize));
                Object made = Exotics.Cast(exotic, a, b, xat, xpow);
                a.BecameObj = made; b.BecameObj = made; // sustain: wait on the product
                a.Die(); b.Die();
                return;
            }

            // ---- v2 substrate ----
            int la = Level(a.Kind), lb = Level(b.Kind);
            if (la != lb)
            {
                var hi = la > lb ? a : b;
                var lo = la > lb ? b : a;
                hi.Absorb(lo);
                return;
            }
            if (la == 2) { a.Absorb(b); return; } // property carriers pool payloads (heavy glue…)
            BounceApart(a, b, 1.2f); // rock bounces off ice
        }

        static void BounceApart(SpellParticle a, SpellParticle b, float force)
        {
            Vector3 apart = (a.transform.position - b.transform.position).normalized;
            a.Vel += apart * force; b.Vel -= apart * force;
        }

        /// The leveling engine: lvl1+lvl1 = lvl2 · anything+lvl2 = lvl3 ULTIMATE.
        /// Condensed states count as their family's lvl2 (Lightning IS Light 2).
        static int EffLevel(SpellParticle p) =>
            p.Kind == ParticleKind.Lightning || p.Kind == ParticleKind.BlackHole ? 2 : p.GrammarLevel;

        static void LevelMerge(SpellParticle a, SpellParticle b)
        {
            int la = EffLevel(a), lb = EffLevel(b);
            var hi = la >= lb ? a : b;
            var lo = hi == a ? b : a;

            if (Mathf.Max(la, lb) >= 3) { hi.Absorb(lo); return; } // capped: eats its kin

            // pool payload + ancestry into the survivor
            hi.Lineage |= lo.Lineage;
            hi.Temp += lo.Temp; hi.Lum += lo.Lum; hi.Density += lo.Density; hi.Stick += lo.Stick;
            // mismatched levels: the WEAKER half rules the product (Marko's law
            // 6) — equals pool their power instead
            hi.Power = la != lb
                ? Mathf.Min(3f, Mathf.Min(hi.Power, lo.Power) * 1.25f)
                : Mathf.Min(3f, hi.Power + lo.Power * 0.5f);
            hi.SrcSize = Mathf.Max(hi.SrcSize, lo.SrcSize);

            Vector3 at = (a.transform.position + b.transform.position) * 0.5f;
            int target = Mathf.Min(3, Mathf.Max(la, lb) + 1);
            var family = RuneGrammar.Family(hi.Kind);
            lo.BecameObj = hi; // sustain law: lo's rune waits on the survivor
            lo.Die();
            RuneGrammar.TryDemon(hi.Lineage, at, hi.SrcSize);

            if (target == 2) { hi.BecomeLevel2(); return; }

            // lvl3 for the carriers is a BIGGER carrier (Dense: larger+heavier;
            // Spread: copies come out LARGER); for everyone else it's an AREA
            if (family == ParticleKind.Dense || family == ParticleKind.Spread)
            {
                hi.GrammarLevel = 3;
                hi.transform.localScale = Vector3.one * 0.34f;
                hi.RefreshLook();
                return;
            }
            float power = hi.Power; float srcSize = hi.SrcSize; ulong lineage = hi.Lineage;
            hi.Die();
            hi.BecameObj = Ultimate(family, at, power, srcSize, lineage); // runes wait on the FIELD
        }

        void BecomeLevel2()
        {
            GrammarLevel = 2;
            _settled = false;
            switch (Kind)
            {
                case ParticleKind.Light: BecomeLightning(); return;      // Light+Light = LIGHTNING (Marko)
                case ParticleKind.Dark: BecomeBlackHole(); return;       // Dark+Dark = BLACK HOLE
                case ParticleKind.Glue:
                    _settled = true; // lvl2 grip doesn't move — and won't let go
                    break;
            }
            transform.localScale = Vector3.one * 0.26f;
            RefreshLook();
        }

        /// lvl3 — the ULTIMATES (each element taken to its absolute). Returns
        /// the created field so the sustain law can wait on it (a flame burst
        /// is instantaneous — returns null, the runes re-arm at once).
        static Object Ultimate(ParticleKind family, Vector3 at, float power, float srcSize, ulong lineage)
        {
            RuneGrammar.TryDemon(lineage, at, srcSize);
            switch (family)
            {
                case ParticleKind.Spark: GrammarFX.FlameBurst(at, power); return null;
                case ParticleKind.Frost: return SnowField.Open(at, power);
                case ParticleKind.Light: return PlasmaField.Open(at, power);
                case ParticleKind.Dark: return BlackHoleField.Open(at, power, growing: true);
                case ParticleKind.Glue: return TimeFreezeField.Open(at, power);
                case ParticleKind.Repel: return InertiaField.Open(at, power);
            }
            return null;
        }

        /// Opposites make PARADOX objects — the tension embodied, never a cancel.
        static void Synthesize(SpellParticle a, SpellParticle b, ParadoxKind paradox)
        {
            Vector3 at = (a.transform.position + b.transform.position) * 0.5f;
            ulong lineage = a.Lineage | b.Lineage;
            float power = (a.Power + b.Power) * 0.5f;
            float srcSize = Mathf.Max(a.SrcSize, b.SrcSize);
            bool big = EffLevel(a) >= 2 && EffLevel(b) >= 2; // lvl2 × lvl2 = the AREA version
            RuneGrammar.TryDemon(lineage, at, srcSize);

            switch (paradox)
            {
                case ParadoxKind.Steam:
                {
                    var f = SteamCloud.Open(at, power, big);
                    a.BecameObj = f; b.BecameObj = f; // sustain law: both runes wait on the cloud
                    a.Die(); b.Die();
                    return;
                }
                case ParadoxKind.WhiteHole:
                {
                    var f = WhiteHoleField.Open(at, power * (big ? 1.6f : 1f));
                    a.BecameObj = f; b.BecameObj = f;
                    a.Die(); b.Die();
                    return;
                }
                case ParadoxKind.ChaosGrip:
                {
                    // grip+slip: sticks to everything but can't be controlled
                    var keep = RuneGrammar.Family(a.Kind) == ParticleKind.Glue ? a : b;
                    var eat = keep == a ? b : a;
                    keep.Lineage = lineage;
                    keep.Stick = Mathf.Abs(keep.Stick) + Mathf.Abs(eat.Stick);
                    keep._chaosLeft = 999f;
                    keep._settled = false;
                    eat.BecameObj = keep;
                    eat.Die();
                    keep.RefreshLook();
                    return;
                }
                case ParadoxKind.Barrier:
                {
                    var mote = Emit(ParticleKind.BarrierMote, at, Vector3.up, power);
                    mote.Lineage = lineage;
                    mote.SrcSize = srcSize;
                    mote.SealId = a.SealId == b.SealId ? a.SealId : 0;
                    a.BecameObj = mote; b.BecameObj = mote;
                    a.Die(); b.Die();
                    return;
                }
            }
        }

        void Absorb(SpellParticle food)
        {
            if (_dead || food._dead) return;
            Temp += food.Temp; Lum += food.Lum; Density += food.Density; Stick += food.Stick;
            if (food.Kind == ParticleKind.Push || food.Kind == Kind) Vel += food.Vel * 0.55f;
            Power = Mathf.Min(3f, Power + food.Power * 0.35f);
            SrcSize = Mathf.Max(SrcSize, food.SrcSize);
            Lineage |= food.Lineage; // ancestry rides EVERY combination
            food.BecameObj = this;   // the food's rune now waits on ME (sustain law)
            _settled = false; // fresh attributes knock it loose
            transform.localScale = Vector3.one * Mathf.Min(0.45f, transform.localScale.x * 1.18f);
            // spread on EITHER side multiplies (the old food-only gate killed
            // Dark+Spread and Light+Spread and coin-flipped the level-2 pairs)
            bool split = food.Density < -0.5f || Density < -0.5f;
            int spreadLevel = RuneGrammar.Family(food.Kind) == ParticleKind.Spread ? EffLevel(food)
                : RuneGrammar.Family(Kind) == ParticleKind.Spread ? GrammarLevel : 1;
            food.Die();
            if (split) TrySplit(spreadLevel);
            RuneGrammar.TryDemon(Lineage, transform.position, SrcSize);
            CheckTransform();
            RefreshLook();
        }

        /// Spread's ladder rides the split: lvl1 halves the copies, lvl2 copies
        /// come out FULL SIZE, lvl3 copies come out LARGER than the original.
        void TrySplit(int spreadLevel)
        {
            if (_dead || _generation >= 2 || All.Count > DrawingConfig.ParticleCap - 6) return;
            float keep = spreadLevel >= 2 ? 1f : 0.5f;
            float twinMul = spreadLevel >= 3 ? 1.25f : keep;
            Temp *= keep; Lum *= keep; Density *= keep; Stick *= keep;
            var twin = Emit(Kind, transform.position + Random.insideUnitSphere * 0.18f,
                Random.onUnitSphere, Power * twinMul, _generation + 1);
            twin.Temp = Temp * twinMul; twin.Lum = Lum * twinMul;
            twin.Density = Density * twinMul; twin.Stick = Stick * twinMul;
            twin.SrcSize = SrcSize;
            twin.Lineage = Lineage;
            twin.SealId = SealId; // a split twin is STILL family (law 11)
            if (spreadLevel >= 3) twin.transform.localScale = transform.localScale * 1.2f;
            twin.RefreshLook();
            _generation++;
        }

        // ------------------------------------- transformations (GRAMMAR v4) --
        /// Dense = MANIFEST (Marko's law): the Dense payload turns an essence
        /// particle into its persistent physical object. The old attribute
        /// ladders (light+dense→lightning etc.) are gone — leveling took their
        /// place (Light+Light=lightning); those pairings become exotics in P3.
        void CheckTransform()
        {
            if (Kind == ParticleKind.Spark && Density >= PlasmaDensity) { BecomeFlame(); return; }
            if (Kind == ParticleKind.Frost && Density >= PlasmaDensity) BecomeSnowball();
        }

        /// HeatUp+Dense — FLAME: a persistent fire that stays where it lands,
        /// burns what's near, and merges with other flames into bigger flames.
        void BecomeFlame()
        {
            Kind = ParticleKind.Flame;
            GrammarLevel = Mathf.Max(GrammarLevel, 1);
            Temp = Mathf.Max(Temp, 60f);
            transform.localScale = Vector3.one * 0.3f;
            Vel *= 0.2f;
            DrawingWorld.Instance?.LogEvent("the fire TAKES — a flame");
            RefreshLook();
        }

        /// HeatDown+Dense — SNOWBALL: real matter, cold, rolls, merges via
        /// Matter's own chemistry; passively chills its surroundings (Matter side).
        void BecomeSnowball()
        {
            var m = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Solid,
                0.3f * Mathf.Max(1f, Power), transform.position);
            m.Temperature = -30f;
            if (m.TryGetComponent<Rigidbody>(out var rb))
                rb.linearVelocity = Vel + Vector3.down * 1f;
            DrawingWorld.Instance?.LogEvent("the cold TAKES — a snowball");
            m.Lineage = Lineage; // the demon chain survives the snowball
            BecameObj = m;       // the rune waits for the snowball to be gone
            Die();
        }

        /// Dark+Dark — BLACK HOLE: a particle that pulls things in. Feed it
        /// more darkness and it goes lvl3: the GROWING black hole area.
        void BecomeBlackHole()
        {
            Kind = ParticleKind.BlackHole;
            Lum = Mathf.Min(Lum, -1.5f);
            _settled = false;
            transform.localScale = Vector3.one * 0.4f;
            var l = GetComponent<Light>();
            if (l != null) Destroy(l);
            DrawingWorld.Instance?.LogEvent("the dark COLLAPSES — black hole");
            RefreshLook();
        }

        void TickBlackHole(float dt)
        {
            transform.position += Vel * dt;
            Vel *= 1f - 1.2f * dt;
            int n = Physics.OverlapSphereNonAlloc(transform.position, 3.5f, _scan, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = _scan[i];
                if (c == null) continue;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null)
                {
                    pl.AddSpellForce((transform.position - c.transform.position).normalized * 5f * Power, dt);
                    continue;
                }
                var p = c.GetComponent<SpellParticle>();
                if (p != null && p != this) { p.Pull(transform.position, dt); continue; }
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddForce((transform.position - rb.worldCenterOfMass).normalized * 8f, ForceMode.Acceleration);
            }
        }

        /// lvl2 particles RADIATE: they do their effect to everything around
        /// them on a beat (Marko: "increases in size and burns things around").
        void TickAura(float dt)
        {
            _auraTick -= dt;
            if (_auraTick > 0f) return;
            _auraTick = DrawingConfig.Lvl2AuraPeriod;
            int n = Physics.OverlapSphereNonAlloc(transform.position, DrawingConfig.Lvl2AuraRadius,
                _scan, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _scan[i];
                if (c == null || c.GetComponent<SpellParticle>() != null) continue;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null)
                {
                    // gentler chip: standing NEAR magic stings, it doesn't shred
                    if (Mathf.Abs(Temp) > 25f)
                        pl.TakeHit(Vector3.zero, Mathf.Abs(Temp) * 0.025f,
                            Temp > 0f ? "ember aura" : "frost aura");
                    continue;
                }
                if (Kind == ParticleKind.Flame || RuneGrammar.Family(Kind) == ParticleKind.Spark)
                    GiveHeat(c, 18f * Power);
                else if (RuneGrammar.Family(Kind) == ParticleKind.Frost)
                    GiveHeat(c, -16f * Power);
                else if (Kind == ParticleKind.Glue) // lvl2 grip: nothing near it moves
                {
                    var cr = c.GetComponentInParent<Creature>();
                    if (cr != null) cr.ApplyStuck(1.2f);
                    var rb = c.attachedRigidbody;
                    if (rb != null && !rb.isKinematic)
                        rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, Vector3.zero, 3f);
                }
                else if (Kind == ParticleKind.Repel) // lvl2 slip: nothing near it stands
                {
                    var cr = c.GetComponentInParent<Creature>();
                    if (cr != null) cr.ApplySlip(1f);
                }
            }
        }

        void BecomeLightning()
        {
            Kind = ParticleKind.Lightning;
            Lum = Mathf.Max(Lum, 2.5f);
            _settled = false;
            _strikeTick = 0.3f;
            transform.localScale = Vector3.one * 0.22f;
            if (GetComponent<Light>() == null)
            {
                var l = gameObject.AddComponent<Light>();
                l.type = LightType.Point; l.range = 7f; l.intensity = 5f;
                l.color = new Color(0.75f, 0.9f, 1f);
            }
            Juice.Crackle(transform.position);
            DrawingWorld.Instance?.LogEvent("the light CONDENSES — lightning");
            RefreshLook();
        }

        void TickLightning(float dt)
        {
            Vel = Vector3.Lerp(Vel, Random.insideUnitSphere * 2.2f, 0.2f); // erratic crackle-drift
            transform.position += Vel * dt;
            _strikeTick -= dt;
            if (_strikeTick <= 0f) { _strikeTick = 0.75f; Strike(); }
        }

        /// Lightning strikes the HIGHEST thing nearby, randomly (Marko's rule).
        void Strike()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, 8f, _scan, ~0, QueryTriggerInteraction.Ignore);
            Collider best = null;
            float bestY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var c = _scan[i];
                if (c == null || c.GetComponent<SpellParticle>() != null) continue;
                bool interesting = c.attachedRigidbody != null
                    || c.GetComponentInParent<Damageable>() != null
                    || c.GetComponent<SimpleFPSController>() != null;
                if (!interesting) continue; // strikes THINGS, not the map itself
                // "highest, RANDOMLY" — a big dice roll so it doesn't just
                // bully the tallest thing in sight (usually the player)
                float y = c.bounds.max.y + Random.value * 2.5f;
                if (y > bestY) { bestY = y; best = c; }
            }
            if (best == null) return;

            Vector3 hit = best.bounds.center + Vector3.up * best.bounds.extents.y;
            Bolt(transform.position, hit);
            Juice.Crackle(hit);
            var lib = FxLibrary.I;
            if (lib != null) FxLibrary.Spawn(lib.ElectricHit, hit, null, 3f);

            var pl = best.GetComponent<SimpleFPSController>();
            if (pl != null) { pl.TakeHit(Vector3.down * 2f, 10f); return; }
            var d = best.GetComponentInParent<Damageable>();
            if (d != null) d.TakeDamage(18f * Power, "struck by lightning");
            GiveHeat(best, 60f);
            var rb = best.attachedRigidbody;
            if (rb != null) rb.AddForce(Vector3.down * 3f, ForceMode.VelocityChange);
        }

        static void Bolt(Vector3 a, Vector3 b)
        {
            var go = new GameObject("Bolt");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.045f;
            lr.positionCount = 4;
            lr.SetPosition(0, a);
            lr.SetPosition(1, Vector3.Lerp(a, b, 0.35f) + Random.insideUnitSphere * 0.5f);
            lr.SetPosition(2, Vector3.Lerp(a, b, 0.7f) + Random.insideUnitSphere * 0.5f);
            lr.SetPosition(3, b);
            lr.sharedMaterial = MatterFX.Get(new Color(0.8f, 0.92f, 1f, 0.95f), MoteShade.Additive);
            Destroy(go, 0.12f);
        }

        /// The most powerful thing in the game. Goes through EVERYTHING.
        /// Everyone who sees it — bosses included — runs. Hits players too.
        void BecomeLaser()
        {
            Vector3 pos = transform.position;
            Vector3 dir = Vel.sqrMagnitude > 0.1f ? Vel.normalized : Random.onUnitSphere;

            Transform prey = null;
            float best = 35f * 35f;
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                float d = (z.transform.position - pos).sqrMagnitude;
                if (d < best) { best = d; prey = z.transform; }
            }
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null) continue;
                float d = (p.transform.position - pos).sqrMagnitude;
                if (d < best) { best = d; prey = p.transform; }
            }
            if (prey != null) dir = (prey.position + Vector3.up * 0.7f - pos).normalized;

            var hits = Physics.RaycastAll(pos, dir, 60f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                var pl = h.collider.GetComponent<SimpleFPSController>();
                if (pl != null) { pl.TakeHit(dir * 9f, 55f); continue; }
                var d = h.collider.GetComponentInParent<Damageable>();
                if (d != null) d.TakeDamage(140f * Power, "lasered");
                var mm = h.collider.GetComponent<Matter>();
                if (mm != null) mm.AddHeat(900f); // the beam MELTS what it can't kill
            }

            var go = new GameObject("Laser");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.11f;
            lr.positionCount = 2;
            lr.SetPosition(0, pos);
            lr.SetPosition(1, pos + dir * 60f);
            lr.sharedMaterial = MatterFX.Get(new Color(1f, 0.4f, 0.9f, 1f), MoteShade.Additive);
            var flash = go.AddComponent<Light>();
            flash.type = LightType.Point; flash.range = 10f; flash.intensity = 8f;
            flash.color = new Color(1f, 0.5f, 0.9f);
            Destroy(go, 0.3f);

            ZombieBrain.ScareVisible(pos, 35f, 6f); // universal terror
            Juice.Boom(pos, 0.9f);
            DrawingWorld.Instance?.LogEvent("L A S E R .");
            Die();
        }

        void BecomeShadow()
        {
            Kind = ParticleKind.Shadow;
            Lum = Mathf.Min(Lum, -1.5f); // no zombie ever sees it coming
            _settled = false;
            transform.localScale = Vector3.one * 0.5f;
            var l = GetComponent<Light>();
            if (l != null) Destroy(l);
            DrawingWorld.Instance?.LogEvent("the dark CONDENSES — a shadow creeps");
            RefreshLook();
        }

        void TickShadow(float dt)
        {
            _retarget -= dt;
            if (_retarget <= 0f)
            {
                _retarget = 0.7f;
                _prey = null;
                float best = 144f;
                foreach (var z in Zombie.All)
                {
                    if (z == null) continue;
                    float d = (z.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _prey = z.transform; }
                }
                foreach (var p in SimpleFPSController.All)
                {
                    if (p == null) continue;
                    float d = (p.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _prey = p.transform; }
                }
            }
            if (_prey != null)
                Vel = (_prey.position + Vector3.up * 0.8f - transform.position).normalized * 1.3f;
            transform.position += Vel * dt;
        }

        void BecomeIceShard()
        {
            var m = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Solid,
                0.24f * Mathf.Max(1f, Power), transform.position);
            m.Temperature = -40f;
            if (m.TryGetComponent<Rigidbody>(out var rb))
                rb.linearVelocity = Vel + Vector3.down * 2f; // a thrown chunk of ice
            Die();
        }

        // ------------------------------------------------- touching the world --
        void Touch(Collider c)
        {
            // BARRIER (Marko's ruling): isolation is two-way — the particle
            // can't reach what's inside, so it just bounces off the shell
            if (Barrier.Protects(c))
            {
                Vel = Vector3.Reflect(Vel, (transform.position - c.bounds.center).normalized) * 0.6f;
                _settled = false;
                return;
            }

            // a BarrierMote wraps the first THING it touches, then is spent
            if (Kind == ParticleKind.BarrierMote)
            {
                Transform root = null;
                var bp = c.GetComponentInParent<SimpleFPSController>();
                if (bp != null) root = bp.transform;
                else
                {
                    var bc = c.GetComponentInParent<Creature>();
                    if (bc != null) root = bc.transform;
                    else if (c.attachedRigidbody != null) root = c.attachedRigidbody.transform;
                }
                if (root != null)
                {
                    Barrier.Wrap(root);
                    BecameObj = root.GetComponent<Barrier>(); // runes wait out the shield
                    Die();
                }
                else { _settled = true; Vel = Vector3.zero; } // bare wall: wait for a target
                return;
            }

            var pilot = c.GetComponent<SimpleFPSController>();
            if (pilot != null) { TouchPlayer(pilot); return; }

            // the DEMON absorbs anything that touches it — it BECOMES the last
            // element it ate, so even a fireball is food, not a hit
            var demon = c.GetComponentInParent<Demon>();
            if (demon != null) { demon.AbsorbParticle(this); BecameObj = demon; Die(); return; }

            if (_explosive) { Explode(); return; }
            if (Kind == ParticleKind.Laser) return;

            var m = c.GetComponent<Matter>();
            var creature = c.GetComponentInParent<Creature>();
            var rb = c.attachedRigidbody;

            // bare world geometry (floor/walls): SETTLE instead of dying — a
            // spark on the ground is a waiting ember, a light is a torch, a
            // dark spot is a trap. A push particle can knock them loose again.
            if (m == null && creature == null && rb == null)
            {
                // a push BOUNCES off bare rock instead of dying — it still
                // owes the seal a tornado (Marko: arrows attract and combine;
                // dying on the first cobble bump prevented every entwine)
                if (Kind == ParticleKind.Push)
                {
                    Vel = Vector3.Reflect(Vel, Vector3.up) * 0.7f;
                    return;
                }
                // lvl2 slip CANNOT be stopped by anything — it bounces (Marko)
                if (GrammarLevel >= 2 && Kind == ParticleKind.Repel)
                {
                    Vel = Vector3.Reflect(Vel, Vector3.up) + Random.insideUnitSphere * 1.5f;
                    return;
                }
                _settled = true;
                Vel = Vector3.zero;
                return;
            }

            // PERSISTENT particles deliver on a beat and STAY: a flame keeps
            // burning what stands in it, lvl2 grip keeps holding, lvl2 slip
            // keeps slipping — they expire by lifetime, not by first contact
            bool persistent = Kind == ParticleKind.Flame
                || (GrammarLevel >= 2 && (Kind == ParticleKind.Glue || Kind == ParticleKind.Repel));
            if (persistent)
            {
                _donateTick -= Time.deltaTime;
                if (_donateTick > 0f) return;
                _donateTick = 0.5f;
                Donate(c, m, creature, rb);
                return;
            }

            Donate(c, m, creature, rb);

            // ECHO powerup: the payload delivered, the particle sometimes
            // ricochets back to life at half power (mayhem compounding)
            if (Echo > 0 && _generation < 2 && Random.value < 0.22f * Echo)
            {
                var e = Emit(Kind, transform.position + Vector3.up * 0.15f,
                    (Random.onUnitSphere + Vector3.up).normalized, Power * 0.6f, _generation + 1);
                e.SrcSize = SrcSize;
                e.Lineage = Lineage; // the echo remembers its ancestry
                e.SealId = SealId;   // and its family
            }
            Die();
        }

        void TouchPlayer(SimpleFPSController pilot)
        {
            if (Barrier.Protects(pilot)) // barriered player: nothing gets in
            {
                Vel = Vector3.Reflect(Vel, (transform.position - pilot.transform.position).normalized) * 0.6f;
                return;
            }
            if (Kind == ParticleKind.Flame) // standing in fire is a CHOICE
            {
                _donateTick -= Time.deltaTime;
                if (_donateTick > 0f) return;
                _donateTick = 0.7f;
                pilot.TakeHit(Vector3.zero, Mathf.Max(3f, Temp * 0.06f), "standing in fire");
                return;
            }
            // friendly fire stays ON — your own sparks burn you (exactness!)
            // (push shove halved — Marko: "I don't like how powerful the arrows are")
            if (Kind == ParticleKind.Push) { pilot.TakeHit(Vel.normalized * 1.1f * Power, 0f); Die(); return; }
            if (_explosive) { Explode(); return; }
            if (Kind == ParticleKind.Shadow) { pilot.TakeHit(Vector3.zero, 6f); Die(); return; }
            if (Temp > 20f) pilot.TakeHit(Vector3.zero, Temp * 0.12f, "spark");
            else if (Temp < -20f) pilot.TakeHit(Vector3.zero, -Temp * 0.08f, "frost");
            if (Stick < -0.4f) pilot.KnockDown(1f); // slipped out from under you
            Die();
        }

        /// Level 5 — the world absorbs everything: the payload becomes real
        /// temperature, light, blindness, weight, glue, and shove.
        void Donate(Collider c, Matter m, Creature creature, Rigidbody rb)
        {
            // GRAMMAR v4: matter is part of the chain — a particle donating
            // into a block hands over its ANCESTRY too (matter can complete
            // the all-12 Demon lineage just like particles can)
            if (m != null)
            {
                m.Lineage |= Lineage;
                RuneGrammar.TryDemon(m.Lineage, m.transform.position, SrcSize);
            }

            if (Mathf.Abs(Temp) > 0.5f)
            {
                if (m != null) m.AddHeat(Temp * 2f);
                else GiveHeat(c, Temp * (creature != null ? 1.5f : 1f)); // flesh catches fast
            }

            if (Lum > 0.4f) AttachLantern(c);
            else if (Lum < -0.4f && creature != null)
                creature.ApplyBlind(2.5f + -Lum);

            if (Stick > 0.4f)
            {
                if (creature != null) creature.ApplyStuck(1.4f * Stick);
                if (m != null) m.AddStickiness(0.35f * Stick);
                if (rb != null) rb.linearDamping = Mathf.Max(rb.linearDamping, 6f * Stick);
                TryWeld(c); // two recently-glued things → JOINED
            }
            else if (Stick < -0.4f)
            {
                if (creature != null) creature.ApplySlip(1.2f * -Stick);
                if (m != null) m.AddStickiness(0.35f * Stick);
                if (rb != null)
                {
                    rb.linearDamping = 0f;
                    rb.AddForce((c.transform.position - transform.position).normalized * 5f * -Stick,
                        ForceMode.VelocityChange);
                }
            }

            if (Mathf.Abs(Density) > 0.4f)
            {
                if (m != null) m.AddDensity(0.6f * Density);
                else if (creature != null) Resize(creature, Density > 0f ? 1.12f : 0.88f);
                else if (rb != null) rb.mass = Mathf.Max(0.05f, rb.mass * (1f + 0.28f * Density));
            }

            if (Kind == ParticleKind.Push && rb != null)
                rb.AddForce(Vel.normalized * 2.5f * Power, ForceMode.VelocityChange);
        }

        /// Dense-fed creatures grow heavy; spread-fed ones shrink — and a small
        /// enough zombie is lighter than air (Marko: eventually they FLY).
        static void Resize(Creature creature, float factor)
        {
            float s = Mathf.Clamp(creature.transform.localScale.x * factor, 0.5f, 1.7f);
            creature.transform.localScale = Vector3.one * s;
            var rb = creature.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = 70f * s * s;
                if (s <= 0.6f) rb.AddForce(Vector3.up * 4f, ForceMode.VelocityChange); // balloon zombie
            }
        }

        void Explode()
        {
            if (_dead) return;
            Vector3 pos = transform.position;
            Juice.Boom(pos, 0.8f);
            var lib = FxLibrary.I;
            if (lib != null) FxLibrary.Spawn(lib.Explosion, pos);
            WorldEvents.Report(WorldEventKind.Explosion, pos, 2f);
            int n = Physics.OverlapSphereNonAlloc(pos, 3f, _scan, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_scan[i] == null) continue;
                var pl = _scan[i].GetComponent<SimpleFPSController>();
                if (pl != null) { pl.TakeHit((pl.transform.position - pos).normalized * 6f, 14f); continue; }
                var cr = _scan[i].GetComponentInParent<Creature>();
                if (cr != null) cr.KnockDown(1.4f);
                GiveHeat(_scan[i], 90f);
                var rb = _scan[i].attachedRigidbody;
                if (rb != null)
                    rb.AddForce((rb.worldCenterOfMass - pos).normalized * 5f, ForceMode.VelocityChange);
            }
            for (int i = 0; i < 8; i++)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                f.name = "Fire";
                Destroy(f.GetComponent<Collider>());
                f.transform.position = pos + Random.insideUnitSphere * 0.3f;
                f.transform.localScale = Vector3.one * Random.Range(0.12f, 0.24f);
                f.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(
                    Color.Lerp(new Color(1f, 0.75f, 0.15f), new Color(1f, 0.25f, 0.05f), Random.value),
                    MoteShade.Additive);
                var frb = f.AddComponent<Rigidbody>();
                frb.useGravity = false;
                frb.linearVelocity = Random.onUnitSphere * 3f + Vector3.up * 1.5f;
                Destroy(f, Random.Range(0.4f, 0.8f));
            }
            Die();
        }

        /// Public doorway for the grammar's area fields (GrammarAreas.cs).
        public static void GiveHeatTo(Collider c, float delta) => GiveHeat(c, delta);

        static void GiveHeat(Collider c, float delta)
        {
            var m = c.GetComponent<Matter>();
            if (m != null) { m.AddHeat(delta); return; }
            var go = c.attachedRigidbody ? c.attachedRigidbody.gameObject : c.gameObject;

            // a CHARACTER limb is not furniture: the limb capsules carry
            // kinematic rigidbodies on the BONES — heat must route to the
            // BEING (a Damageable on a bone once burned a leg clean off the
            // skeleton and the skin snapped to the world origin)
            var pilot = c.GetComponentInParent<SimpleFPSController>();
            if (pilot != null) go = pilot.gameObject;
            else
            {
                var creature = c.GetComponentInParent<Creature>();
                if (creature != null) go = creature.gameObject;
            }

            // don't cook giant static surfaces — same guard the old zones used
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null && c.attachedRigidbody == null
                && rend.bounds.size.magnitude > DrawingConfig.MaxThermalObjectSize) return;
            var t = go.GetComponent<Thermal>();
            if (t == null)
            {
                t = go.AddComponent<Thermal>();
                t.HeatCapacity = SurfaceMaterialDB.Info(SurfaceMaterialDB.Resolve(go)).HeatCapacity;
                if (go.GetComponent<Rigidbody>() != null && go.GetComponent<Damageable>() == null)
                    go.AddComponent<Damageable>();
            }
            t.AddHeat(delta);
        }

        void AttachLantern(Collider c)
        {
            var go = new GameObject("Lantern");
            go.transform.SetParent(c.transform, true);
            go.transform.position = transform.position;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 5f;
            l.intensity = 2.6f * Mathf.Min(2f, Lum);
            l.color = new Color(1f, 0.95f, 0.75f);
            Destroy(go, 7f);
        }

        // glue memory: the last two glued bodies get JOINED if they meet soon
        static Rigidbody _lastGlued;
        static float _gluedAt;
        void TryWeld(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null) return;
            if (_lastGlued != null && _lastGlued != rb && Time.time - _gluedAt < 4f
                && (rb.worldCenterOfMass - _lastGlued.worldCenterOfMass).sqrMagnitude < 9f)
            {
                var joint = rb.gameObject.AddComponent<FixedJoint>();
                joint.connectedBody = _lastGlued;
                joint.breakForce = 400f;
                DrawingWorld.Instance?.LogEvent("GLUED TOGETHER.");
            }
            _lastGlued = rb;
            _gluedAt = Time.time;
        }

        /// The void rift can drag settled particles back into the air.
        public void Pull(Vector3 to, float dt)
        {
            if (_dead) return;
            _settled = false;
            Vel += (to - transform.position).normalized * 10f * dt;
        }

        // -------------------------------------------------------------- look --
        GameObject _customLook;
        ParticleKind _customFor;

        /// MARKO'S FX OVERRIDE: drop a prefab named FX_<Kind> (FX_Spark,
        /// FX_Frost, FX_Flame, FX_Lightning, FX_BlackHole…) into
        /// Resources/Custom and it BECOMES that particle's look — the code
        /// sphere hides, your prefab rides the particle. Zero code changes.
        void EnsureCustomLook()
        {
            if (_customLook != null && _customFor == Kind) return;
            if (_customLook != null) Destroy(_customLook);
            _customLook = null;
            var skin = PrefabVault.Get("FX_" + Kind);
            if (skin == null)
            {
                if (_rend != null) _rend.enabled = true;
                return;
            }
            _customFor = Kind;
            _customLook = Instantiate(skin, transform);
            _customLook.transform.localPosition = Vector3.zero;
            if (_rend != null) _rend.enabled = false;
        }

        void RefreshLook()
        {
            if (_rend == null) _rend = GetComponent<Renderer>();
            if (_rend == null) return;
            EnsureCustomLook();
            if (_customLook != null) return; // your art owns the look now
            Color c;
            MoteShade shade = MoteShade.Additive;
            switch (Kind)
            {
                case ParticleKind.Spark: c = _explosive ? new Color(1f, 0.25f, 0.05f) : new Color(1f, 0.55f, 0.12f); break;
                case ParticleKind.Frost: c = new Color(0.6f, 0.85f, 1f); break;
                case ParticleKind.Light: c = new Color(1f, 0.97f, 0.8f); break;
                case ParticleKind.Dark: c = new Color(0.2f, 0.1f, 0.3f); shade = MoteShade.Transparent; break;
                case ParticleKind.Glue: c = new Color(0.4f, 0.8f, 0.35f); break;
                case ParticleKind.Repel: c = new Color(0.85f, 0.85f, 0.9f); break;
                case ParticleKind.Dense: c = new Color(0.75f, 0.55f, 0.3f); break;
                case ParticleKind.Spread: c = new Color(0.7f, 1f, 0.8f); break;
                case ParticleKind.Push: c = new Color(1f, 0.95f, 0.4f); break;
                case ParticleKind.Lightning: c = new Color(0.75f, 0.9f, 1f); break;
                case ParticleKind.Shadow: c = new Color(0.05f, 0.02f, 0.1f); shade = MoteShade.Transparent; break;
                case ParticleKind.Flame: c = new Color(1f, 0.45f, 0.08f); break;
                case ParticleKind.BlackHole: c = new Color(0.03f, 0.01f, 0.06f); shade = MoteShade.Transparent; break;
                case ParticleKind.BarrierMote: c = new Color(0.6f, 0.9f, 1f, 0.7f); shade = MoteShade.Transparent; break;
                default: c = Color.white; break;
            }
            // carried darkness dims ANY particle — the invisible flame is also
            // hard for PLAYERS to see, which is only fair
            if (Lum < -0.2f && Kind != ParticleKind.Dark && Kind != ParticleKind.Shadow)
            {
                c.a = Mathf.Clamp01(0.9f + Lum * 0.55f);
                shade = MoteShade.Transparent;
            }

            // alive, not flat: jelly wobble + rim glow (SZParticle shader)
            float wobble = Kind == ParticleKind.Shadow ? 0.09f
                : Kind == ParticleKind.Glue ? 0.06f : 0.04f;
            float rim = shade == MoteShade.Additive ? 0.9f : 0.35f;
            _rend.sharedMaterial = MatterFX.Particle(c, shade, wobble, rim);
        }
    }

    /// Deep darkness + density tears a hole in the world (dark = antimatter =
    /// the summoning school): the rift INHALES for a few seconds, then spits
    /// out something from the other side — a shadow zombie that hates everyone
    /// equally, plus whatever it swallowed, transmuted.
    public class VoidRift : MonoBehaviour
    {
        public static bool Active { get; private set; }

        const float InhaleSeconds = 4.5f;
        const float PullRadius = 6.5f;
        static readonly Collider[] _pull = new Collider[48];

        float _age, _fearTick, _size = 1f;
        int _swallowed;
        Transform _rim;

        public static void Open(Vector3 pos, float srcSize = 1f)
        {
            if (Active) return; // one hole in reality at a time
            Active = true;
            var go = new GameObject("VoidRift");
            go.transform.position = pos + Vector3.up * 0.8f;
            go.AddComponent<VoidRift>()._size = srcSize;
        }

        void Start()
        {
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 1.5f;
            core.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.02f, 0.01f, 0.05f, 0.92f), MoteShade.Transparent);

            var rimGo = new GameObject("Rim");
            rimGo.transform.SetParent(transform, false);
            _rim = rimGo.transform;
            var lr = rimGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = 0.05f;
            lr.positionCount = 24;
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.95f);
            }
            lr.sharedMaterial = MatterFX.Get(new Color(0.6f, 0.2f, 0.9f, 0.9f), MoteShade.Additive);

            WorldEvents.Report(WorldEventKind.Explosion, transform.position, 2.5f);
            DrawingWorld.Instance?.LogEvent("THE DARK TEARS OPEN");
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_rim != null) _rim.Rotate(2f, 90f * dt, 40f * dt);

            _fearTick -= dt;
            if (_fearTick <= 0f)
            {
                _fearTick = 0.6f;
                ZombieBrain.ScareVisible(transform.position, 14f, 0.6f); // the rim glows — they see THIS
            }

            if (_age < InhaleSeconds) Inhale(dt);
            else { Spit(); Destroy(gameObject); }
        }

        void Inhale(float dt)
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, PullRadius, _pull, ~0,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = _pull[i];
                if (c == null) continue;

                var pilot = c.GetComponent<SimpleFPSController>();
                if (pilot != null) // players get DRAGGED, never swallowed
                {
                    pilot.AddSpellForce((transform.position - c.transform.position).normalized * 8f, dt);
                    continue;
                }

                var p = c.GetComponent<SpellParticle>();
                if (p != null)
                {
                    if ((c.transform.position - transform.position).sqrMagnitude < 1f) Destroy(c.gameObject);
                    else p.Pull(transform.position, dt);
                    continue;
                }

                var rb = c.attachedRigidbody;
                if (rb == null) continue;
                Vector3 to = transform.position - rb.worldCenterOfMass;
                float d = to.magnitude;
                rb.AddForce(to.normalized * Mathf.Lerp(14f, 3f, d / PullRadius), ForceMode.Acceleration);

                if (d < 1.0f && _swallowed < 12)
                {
                    bool edible = rb.GetComponentInParent<Zombie>() != null
                        || rb.GetComponent<Matter>() != null;
                    if (edible) { _swallowed++; Destroy(rb.gameObject); }
                }
            }
        }

        void Spit()
        {
            Active = false;

            // something from the other side — a DEMON, hostile to everyone,
            // sized by the drawing that tore the rift, and it BECOMES whatever
            // element touches it (Demon.cs)
            Demon.Summon(transform.position + Vector3.up * 0.5f, _size);

            // …and what it ate comes back, changed
            for (int i = 0; i < _swallowed; i++)
            {
                var mat = Random.value < 0.12f ? SurfaceMaterialType.Diamond : SurfaceMaterialType.Stone;
                var m = Matter.Spawn(mat, MatterPhase.Solid, Random.Range(0.15f, 0.3f),
                    transform.position + Random.insideUnitSphere * 0.5f);
                if (m.TryGetComponent<Rigidbody>(out var rb))
                    rb.linearVelocity = Random.onUnitSphere * 6f + Vector3.up * 4f;
            }

            WorldEvents.Report(WorldEventKind.Explosion, transform.position, 3f);
            Juice.Boom(transform.position, 1.2f);
            DrawingWorld.Instance?.LogEvent($"…and something came BACK ({_swallowed} things fed to it)");
        }

        void OnDestroy() => Active = false;
    }

    /// The rift's spawn hates everything equally: it keeps a fresh grudge
    /// against the nearest zombie while hunting players like any other.
    public class ShadowFeral : MonoBehaviour
    {
        float _timer;

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 2.5f;
            var brain = GetComponent<ZombieBrain>();
            if (brain == null) return;
            Zombie nearest = null;
            float best = 81f;
            foreach (var z in Zombie.All)
            {
                if (z == null || z.gameObject == gameObject) continue;
                float d = (z.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = z; }
            }
            if (nearest != null)
                brain.Remember(MemKind.MadAt, MemEvent.Grudge, nearest.transform.position, nearest.transform);
        }
    }
}
