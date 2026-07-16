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
        Lightning, Laser, Shadow      // condensed states (level 3 — they absorb further)
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

            // push particles FLY (they're the force carriers); everything else
            // just blooms off the ink and hangs around the seal until a force
            // moves it (user rule: no drifting off on their own). Scatter kept
            // SMALL so siblings stay in each other's reach (Marko: motes kept
            // missing each other)
            float speed = kind == ParticleKind.Push ? 9f : 0.9f;
            p.Vel = dir.normalized * speed
                + Random.insideUnitSphere * (kind == ParticleKind.Push ? 0.4f : 0.22f);

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

            if (!_settled)
            {
                // DENSITY IS WEIGHT (Marko's rule): heavy falls, thinned floats
                // — gently. Strong drag keeps particles hovering near their
                // seal; only real forces (push hits, rifts, explosions) fling
                // them anywhere.
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.Shadow)
                    Vel += Vector3.down * (EffDensity() - AirDensity) * 2.5f * dt;

                // MOTES SEEK EACH OTHER (Marko: "they almost always miss") —
                // a gentle pull toward the nearest sibling makes the matter
                // law actually happen instead of depending on a lucky graze;
                // close enough = the law fires NOW, no trigger-roulette
                if (Kind != ParticleKind.Push && Kind != ParticleKind.Lightning
                    && Kind != ParticleKind.Shadow)
                {
                    SpellParticle near = null;
                    float bestSqr = 0.9f * 0.9f; // seek range
                    for (int i = 0; i < All.Count; i++)
                    {
                        var o = All[i];
                        if (o == this || o == null || o._dead || o.Kind == ParticleKind.Push) continue;
                        float d = (o.transform.position - transform.position).sqrMagnitude;
                        if (d < bestSqr)
                        {
                            bestSqr = d;
                            near = o;
                        }
                    }
                    if (near != null)
                    {
                        Vector3 to = near.transform.position - transform.position;
                        Vel += to.normalized * (2.2f * dt);
                        if (bestSqr < 0.1f * 0.1f && GetInstanceID() < near.GetInstanceID())
                            ResolveLaw(this, near);
                    }
                }
                Vel *= 1f - (Kind == ParticleKind.Push ? 0.25f : 1.4f) * dt;
                transform.position += Vel * dt;
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

            float life = DrawingConfig.ParticleLife * (Kind == ParticleKind.Shadow ? 2f : 1f);
            if (_age > life - 0.8f)
                transform.localScale *= Mathf.Max(0.01f, 1f - dt / 0.8f);
            if (_age > life || transform.localScale.x < 0.015f) Die();
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
            || Kind == ParticleKind.Lightning || Kind == ParticleKind.Laser;

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

        static bool Opposites(ParticleKind a, ParticleKind b) =>
            (a == ParticleKind.Light && b == ParticleKind.Dark) || (a == ParticleKind.Dark && b == ParticleKind.Light)
            || (a == ParticleKind.Dense && b == ParticleKind.Spread) || (a == ParticleKind.Spread && b == ParticleKind.Dense)
            || (a == ParticleKind.Glue && b == ParticleKind.Repel) || (a == ParticleKind.Repel && b == ParticleKind.Glue)
            || (a == ParticleKind.Spark && b == ParticleKind.Frost) || (a == ParticleKind.Frost && b == ParticleKind.Spark);

        static void ResolveLaw(SpellParticle a, SpellParticle b)
        {
            if (a._dead || b._dead) return;
            int la = Level(a.Kind), lb = Level(b.Kind);

            if (la != lb)
            {
                // the more material absorbs the more ethereal
                var hi = la > lb ? a : b;
                var lo = la > lb ? b : a;
                hi.Absorb(lo);
                return;
            }

            if (Opposites(a.Kind, b.Kind)) { Annihilate(a, b); return; }

            if (a.Kind == b.Kind)
            {
                if (a.Kind == ParticleKind.Repel)
                {
                    // repel particles repel EACH OTHER (Marko's rule)
                    Vector3 away = (a.transform.position - b.transform.position).normalized;
                    a.Vel += away * 4f; b.Vel -= away * 4f;
                    a._settled = b._settled = false;
                    return;
                }
                a.Absorb(b); // sparks pool into flame, lights brighten, glue clumps
                return;
            }

            if (la == 2) { a.Absorb(b); return; } // property carriers pool payloads (heavy glue…)

            // same level, nothing defined → normal physics: rock bounces off ice
            Vector3 apart = (a.transform.position - b.transform.position).normalized;
            a.Vel += apart * 1.2f; b.Vel -= apart * 1.2f;
        }

        void Absorb(SpellParticle food)
        {
            if (_dead || food._dead) return;
            Temp += food.Temp; Lum += food.Lum; Density += food.Density; Stick += food.Stick;
            if (food.Kind == ParticleKind.Push || food.Kind == Kind) Vel += food.Vel * 0.55f;
            Power = Mathf.Min(3f, Power + food.Power * 0.35f);
            SrcSize = Mathf.Max(SrcSize, food.SrcSize);
            _settled = false; // fresh attributes knock it loose
            transform.localScale = Vector3.one * Mathf.Min(0.45f, transform.localScale.x * 1.18f);
            bool split = food.Density < -0.5f; // spread donated: multiply, halved
            food.Die();
            if (split) TrySplit();
            CheckTransform();
            RefreshLook();
        }

        void TrySplit()
        {
            if (_dead || _generation >= 2 || All.Count > DrawingConfig.ParticleCap - 6) return;
            Temp *= 0.5f; Lum *= 0.5f; Density *= 0.5f; Stick *= 0.5f;
            var twin = Emit(Kind, transform.position + Random.insideUnitSphere * 0.18f,
                Random.onUnitSphere, Power * 0.5f, _generation + 1);
            twin.Temp = Temp; twin.Lum = Lum; twin.Density = Density; twin.Stick = Stick;
            twin.SrcSize = SrcSize;
            twin.RefreshLook();
            _generation++;
        }

        static void Annihilate(SpellParticle a, SpellParticle b)
        {
            Vector3 at = (a.transform.position + b.transform.position) * 0.5f;
            bool steam = a.Kind == ParticleKind.Spark || a.Kind == ParticleKind.Frost;
            for (int i = 0; i < 4; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = "Puff";
                Destroy(puff.GetComponent<Collider>());
                puff.transform.position = at + Random.insideUnitSphere * 0.12f;
                puff.transform.localScale = Vector3.one * Random.Range(0.08f, 0.16f);
                puff.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(
                    steam ? new Color(0.92f, 0.94f, 0.97f, 0.5f) : new Color(0.6f, 0.5f, 0.8f, 0.6f),
                    MoteShade.Transparent);
                var rb = puff.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearVelocity = Vector3.up * Random.Range(0.6f, 1.3f) + Random.insideUnitSphere * 0.3f;
                Destroy(puff, 0.7f);
            }
            a.Die(); b.Die();
        }

        // ---------------------------------------- transformations (ladders) --
        /// The ladders are attribute thresholds, whoever ended up carrying the
        /// attributes — no recipes anywhere.
        void CheckTransform()
        {
            switch (Kind)
            {
                case ParticleKind.Lightning:
                    if (Density >= RiftDensity) BecomeLaser();
                    return;
                case ParticleKind.Shadow:
                    if (Density >= RiftDensity || Lum <= -2.5f) { VoidRift.Open(transform.position, SrcSize); Die(); }
                    return;
                case ParticleKind.Laser: return;
            }

            if (Lum >= 0.8f && Density >= PlasmaDensity) { BecomeLightning(); return; }
            if (Lum <= -0.8f && Density >= PlasmaDensity) { BecomeShadow(); return; }
            if (Temp >= FireballTemp && Density >= PlasmaDensity && !_explosive)
            {
                _explosive = true; // hot + dense = it goes off on impact
                transform.localScale = Vector3.one * 0.3f;
                return;
            }
            if (Temp <= -FireballTemp && Density >= PlasmaDensity) BecomeIceShard();
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
            var pilot = c.GetComponent<SimpleFPSController>();
            if (pilot != null) { TouchPlayer(pilot); return; }

            // the DEMON absorbs anything that touches it — it BECOMES the last
            // element it ate, so even a fireball is food, not a hit
            var demon = c.GetComponentInParent<Demon>();
            if (demon != null) { demon.AbsorbParticle(this); Die(); return; }

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
                if (Kind == ParticleKind.Push) { Die(); return; }
                _settled = true;
                Vel = Vector3.zero;
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
            }
            Die();
        }

        void TouchPlayer(SimpleFPSController pilot)
        {
            // friendly fire stays ON — your own sparks burn you (exactness!)
            if (Kind == ParticleKind.Push) { pilot.TakeHit(Vel.normalized * 2.2f * Power, 0f); Die(); return; }
            if (_explosive) { Explode(); return; }
            if (Kind == ParticleKind.Shadow) { pilot.TakeHit(Vector3.zero, 6f); Die(); return; }
            if (Temp > 20f) pilot.TakeHit(Vector3.zero, Temp * 0.12f);
            else if (Temp < -20f) pilot.TakeHit(Vector3.zero, -Temp * 0.08f);
            if (Stick < -0.4f) pilot.KnockDown(1f); // slipped out from under you
            Die();
        }

        /// Level 5 — the world absorbs everything: the payload becomes real
        /// temperature, light, blindness, weight, glue, and shove.
        void Donate(Collider c, Matter m, Creature creature, Rigidbody rb)
        {
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
                rb.AddForce(Vel.normalized * 4.5f * Power, ForceMode.VelocityChange);
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
        void RefreshLook()
        {
            if (_rend == null) _rend = GetComponent<Renderer>();
            if (_rend == null) return;
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
