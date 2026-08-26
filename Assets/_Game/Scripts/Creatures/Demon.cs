using UnityEngine;

namespace SpellyZombie
{
    /// Rift summon: rampages, absorbs whatever element touches it and becomes
    /// it, leaking an aura of that element as real SpellParticles. Size comes
    /// from the rune that opened the rift.
    public class Demon : MonoBehaviour
    {
        ParticleKind _aura = ParticleKind.Dark;
        string _form = "SHADOW";
        Color _tint = new Color(0.09f, 0.05f, 0.14f);
        bool _glows;
        float _life, _auraTick, _baseMass = 70f;
        Renderer[] _skin;
        Rigidbody _rb;

        // grand demon: unkillable (expires only with time), summons random
        // calamities, feared by every zombie
        bool _grand;
        float _calamityTick, _grandFearTick;
        float _patrolTick, _castTick;
        Zombie _body2;
        Element _grandDmg;

        public static Demon SummonGrand(Vector3 pos, float srcSize)
        {
            var d = Summon(pos, Mathf.Max(srcSize, 2f));
            if (d == null) return null;
            d._grand = true;
            d._life = 42f;
            d.transform.localScale = Vector3.one * 2.8f; // reads across the map
            if (d._rb != null) { d._baseMass = 70f * 8f; d._rb.mass = d._baseMass; }

            var lib = FxLibrary.I;
            if (lib != null)
            {
                FxLibrary.Spawn(lib.DemonBoom, pos);
                FxLibrary.Spawn(lib.SkullHead, pos + Vector3.up * 3.2f, d.transform, 7f);
            }

            // unkillable from frame zero: Update's refresh alone is one frame too late
            d._grandDmg = d.GetComponent<Element>();
            if (d._grandDmg != null) d._grandDmg.Health = 999999f;

            // fearless, or its own calamities write Danger memories that freeze it
            var brain = d.GetComponent<ZombieBrain>();
            if (brain != null) brain.AlwaysFearless = true;
            DrawingWorld.Instance?.LogEvent("ALL TWELVE ANSWERED. RUN.");
            Juice.Boom(pos, 1.4f);
            Juice.Shake(1f, 0.8f);
            return d;
        }

        /// The demon speaks the SAME language as everything else: it rewrites
        /// the nature around it (hostile temporary biomes), rains meteors and
        /// raises teamless golems. No private spell list.
        void Calamity()
        {
            Vector3 at = transform.position
                + new Vector3(Random.Range(-7f, 7f), 0.4f, Random.Range(-7f, 7f));
            switch (Random.Range(0, 6))
            {
                case 0: GrammarFX.FlameBurst(at, 1f); break;
                case 1: ArtificialBiome.Open(at, new SpellPayload { Temp = 30f }, 5f, 1f); break;
                case 2: ArtificialBiome.Open(at, new SpellPayload { Temp = -30f, Lum = -0.8f }, 5f, 1f); break;
                case 3: ArtificialBiome.Open(at, new SpellPayload { Affinity = 1f, Pressure = 0.6f }, 6f, 1f); break;
                case 4: SpellEffects.Meteor(at, 1f, Random.Range(1, 4)); break;
                default: Golem.Spawn(at, Random.Range(0.7f, 1.4f)); break;
            }
            GetComponent<ZombieBrain>()?.Mumble("HAHAHA!!", 1.2f);
        }

        /// Rift-side entry point. srcSize = the zone radius of the rune chain
        /// that condensed into the rift - bigger drawing, bigger demon.
        public static Demon Summon(Vector3 pos, float srcSize)
        {
            var z = Zombie.Spawn(pos, 1.15f);
            // THE DEMON HAS EVERY SPELL AS AN ABILITY - which is the reason a
            // creature stores a LIST rather than wearing one label.
            if (z != null)
            {
                // SPELLS ONLY. It does not charge and it does not throw - it
                // spams spells, wrecks what is around it, and patrols at
                // random. Every row in the table is one of its abilities,
                // which is the whole reason a creature stores a list.
                foreach (var row in SpellTable.Rows) z.Abilities.Add(row.Name);
            }
            if (z == null) return null;
            z.name = "Demon";

            // same rig, never below twice a normal zombie; bigger drawing, bigger demon
            float scale = Mathf.Clamp(1.8f + srcSize * 0.6f, 2.2f, 4f);
            z.transform.localScale = Vector3.one * scale;

            var demon = z.gameObject.AddComponent<Demon>();
            z.IsDemon = true; // RoundDirector's countable-zombie flag
            demon._life = 20f + 8f * scale; // big demons rampage longer
            demon._rb = z.GetComponent<Rigidbody>();
            if (demon._rb != null)
            {
                demon._baseMass = 70f * scale * scale;
                demon._rb.mass = demon._baseMass;
            }

            // horns
            for (int side = -1; side <= 1; side += 2)
            {
                var horn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                horn.name = "Horn";
                Destroy(horn.GetComponent<Collider>());
                horn.transform.SetParent(z.transform, false);
                horn.transform.localPosition = new Vector3(0.14f * side, 0.98f, 0f);
                horn.transform.localRotation = Quaternion.Euler(0f, 0f, -28f * side);
                horn.transform.localScale = new Vector3(0.07f, 0.3f, 0.07f);
            }
            demon._skin = z.GetComponentsInChildren<Renderer>(); // horns included

            z.gameObject.AddComponent<ShadowFeral>(); // hates everyone equally
            var db = z.GetComponent<ZombieBrain>();
            if (db != null)
            {
                db.AlwaysFearless = true; // a flame demon must not flee its own aura
                db.Mumble("RRAAAH!!", 2f);
            }
            demon.Retint();
            return demon;
        }

        // ------------------------------------------------------- absorption --
        /// A spell particle touched the demon: it BECOMES that element.
        /// (Called by SpellParticle.Touch; the particle dies into it.)
        public void AbsorbParticle(SpellParticle p)
        {
            switch (p.Kind)
            {
                case ParticleKind.Spark: Become("FLAME", new Color(1f, 0.45f, 0.08f), ParticleKind.Spark, true); break;
                case ParticleKind.Frost: Become("ICE", new Color(0.65f, 0.87f, 1f), ParticleKind.Frost, false); break;
                case ParticleKind.Light: Become("RADIANT", new Color(1f, 0.96f, 0.75f), ParticleKind.Light, true); break;
                case ParticleKind.Dark:
                case ParticleKind.Shadow: Become("SHADOW", new Color(0.09f, 0.05f, 0.14f), ParticleKind.Dark, false); break;
                case ParticleKind.Glue: Become("GOO", new Color(0.35f, 0.8f, 0.3f), ParticleKind.Glue, false); break;
                case ParticleKind.Repel: Become("SLICK", new Color(0.82f, 0.84f, 0.9f), ParticleKind.Repel, false); break;
                case ParticleKind.Dense: Become("STONE", new Color(0.45f, 0.42f, 0.4f), ParticleKind.Dense, false); break;
                case ParticleKind.Spread: Become("AIRY", new Color(0.75f, 1f, 0.85f), ParticleKind.Spread, false); break;
                case ParticleKind.Push: Become("GALE", new Color(0.95f, 0.95f, 0.7f), ParticleKind.Push, false); break;
                case ParticleKind.Lightning: Become("STORM", new Color(0.7f, 0.88f, 1f), ParticleKind.Lightning, true); break;
                // condensed kinds map to base elements
                case ParticleKind.Flame: Become("FLAME", new Color(1f, 0.45f, 0.08f), ParticleKind.Spark, true); break;
                case ParticleKind.BlackHole: Become("VOID", new Color(0.03f, 0.01f, 0.06f), ParticleKind.Dark, false); break;
                // a BarrierMote is swallowed with no form change
            }
        }

        /// It EATS conjured matter whole and becomes that material.
        void OnCollisionEnter(Collision col)
        {
            var m = col.collider.GetComponent<Matter>();
            if (m == null) return;
            var info = SurfaceMaterialDB.Info(m.Material);
            Color tint = m.Phase == MatterPhase.Liquid ? info.LiquidColor : info.SolidColor;
            bool hot = m.Temperature > 150f;
            ParticleKind aura =
                hot ? ParticleKind.Spark
                : m.Material == SurfaceMaterialType.Water ? ParticleKind.Frost
                : m.Material == SurfaceMaterialType.Slime ? ParticleKind.Glue
                : m.Material == SurfaceMaterialType.Coal ? ParticleKind.Repel // oily
                : ParticleKind.Dense;
            Become(m.Material.ToString().ToUpper(), tint, aura, hot);
            Destroy(m.gameObject);
        }

        void Become(string form, Color tint, ParticleKind aura, bool glows)
        {
            bool changed = form != _form;
            _form = form;
            _tint = tint;
            _aura = aura;
            _glows = glows;

            // the element changes the body: stone is ponderous, air is loose
            if (_rb != null)
                _rb.mass = _baseMass * (aura == ParticleKind.Dense ? 1.8f
                    : aura == ParticleKind.Spread ? 0.55f : 1f);

            Retint();
            if (changed)
            {
                GetComponent<ZombieBrain>()?.Mumble("MMM… " + _form + ".", 1.6f);
                DrawingWorld.Instance?.LogEvent($"the demon DRINKS it. {_form} DEMON");
                WorldEvents.Report(WorldEventKind.Sparkle, transform.position, 1.5f);
            }
        }

        void Retint()
        {
            if (_skin == null) return;
            var mat = MatterFX.Get(_tint, _glows ? MoteShade.Additive : MoteShade.Opaque);
            foreach (var r in _skin)
                if (r != null) r.sharedMaterial = mat;
        }

        // ---------------------------------------------------------- rampage --
        void Update()
        {
            float dt = Time.deltaTime;
            float scale = transform.localScale.x;

            if (_grand)
            {
                // refresh health: only the clock ends a grand demon
                if (_grandDmg == null) _grandDmg = GetComponent<Element>();
                if (_grandDmg != null) _grandDmg.Health = 999999f;

                // every zombie nearby flees
                _grandFearTick -= dt;
                if (_grandFearTick <= 0f)
                {
                    _grandFearTick = 0.5f;
                    ZombieBrain.ScareVisible(transform.position, 22f, 10f);
                }

                _calamityTick -= dt;
                if (_calamityTick <= 0f)
                {
                    _calamityTick = Random.Range(2f, 3.5f);
                    Calamity();
                }
            }

            Patrol(dt);
            SpamSpells(dt);

            // the aura: leaks its element as real particles, no special cases
            _auraTick -= dt;
            if (_auraTick <= 0f)
            {
                _auraTick = 1.4f;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 dir = (Random.onUnitSphere + Vector3.up * 0.3f).normalized;
                    SpellParticle.Emit(_aura,
                        transform.position + Vector3.up * 0.9f * scale + dir * 0.75f * scale,
                        dir, 0.8f * scale);
                }
            }

            _life -= dt;
            if (_life <= 0f) Expire();
        }

        /// Time's up: it bursts into its final element and is gone. No kill
        /// credit - nothing killed it, it just went back.
        void Expire()
        {
            for (int i = 0; i < 4; i++)
                SpellParticle.Emit(_aura, transform.position + Vector3.up * 0.8f,
                    Random.onUnitSphere, 1f);
            WorldEvents.Report(WorldEventKind.Explosion, transform.position, 1.5f);
            DrawingWorld.Instance?.LogEvent($"the {_form} demon returns to the dark");
            Destroy(gameObject);
        }
    
        /// IT WANDERS. No route, no guard post, no owner to protect - it picks
        /// somewhere and goes, and whatever it walks into is its problem. The
        /// zombie body it wears does the actual moving, so this only ever says
        /// WHERE.
        void Patrol(float dt)
        {
            _patrolTick -= dt;
            if (_patrolTick > 0f) return;
            _patrolTick = Random.Range(DrawingConfig.DemonPatrolMin, DrawingConfig.DemonPatrolMax);

            if (_body2 == null) _body2 = GetComponent<Zombie>();
            var brain = _body2 != null ? _body2.GetComponent<ZombieBrain>() : null;
            if (brain == null) return;

            float a = Random.value * Mathf.PI * 2f;
            float r = Random.Range(DrawingConfig.DemonPatrolNear, DrawingConfig.DemonPatrolFar);
            brain.Order(transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
        }

        /// IT SPAMS SPELLS. Every ability it has is a row in the table, so
        /// casting is picking one at random and putting a particle into the
        /// world whose NUMBERS sit in that row's region. Nothing here knows
        /// what any individual spell does - the numbers decide that, the same
        /// way they decide it for a player.
        void SpamSpells(float dt)
        {
            _castTick -= dt;
            if (_castTick > 0f) return;
            _castTick = Random.Range(DrawingConfig.DemonCastMin, DrawingConfig.DemonCastMax);

            if (_body2 == null) _body2 = GetComponent<Zombie>();
            if (_body2 == null || _body2.Abilities.Count == 0) return;

            var row = SpellTable.ByName(_body2.Abilities[Random.Range(0, _body2.Abilities.Count)]);
            if (row == null) return;   // charge and goo are not rows; it has neither

            // land it comfortably PAST the threshold so the region it names is
            // the region it actually falls in
            var load = new SpellPayload();
            for (int i = 0; i < SpellPayload.AxisCount; i++)
                load[i] = row[i] * SpellPayload.UnitOf(i)
                        * DrawingConfig.FusionAt * DrawingConfig.DemonCastPower;

            Vector3 from = transform.position + Vector3.up * transform.localScale.y;
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.35f;
            var mote = SpellParticle.Emit(ParticleKind.Push, from, dir.normalized, 1.4f);
            if (mote == null) return;
            mote.Data = load.Clamped();
            mote.OwnerId = -1;          // the demon serves nobody
            mote.Wake();
        }
}
}
