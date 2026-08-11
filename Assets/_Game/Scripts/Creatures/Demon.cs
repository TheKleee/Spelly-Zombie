using UnityEngine;

namespace SpellyZombie
{
    /// The void rift's summon (Marko's design): a black demon that rampages at
    /// random — and ABSORBS whatever element touches it, BECOMING that element
    /// until something else touches it or it expires. Touch fire → flaming
    /// demon. Touch ice → ice demon. Eat a lava blob → lava demon. It leaks an
    /// aura of its current element (real SpellParticles, so a flame demon
    /// ignites the room just by walking through it), which means players can
    /// RE-SPEC a rampaging demon by shooting elements at it — taming it into a
    /// weapon, or watching a zombie scrawl turn it into a storm. Size comes
    /// from the rune that tore the rift open.
    public class Demon : MonoBehaviour
    {
        ParticleKind _aura = ParticleKind.Dark;
        string _form = "SHADOW";
        Color _tint = new Color(0.09f, 0.05f, 0.14f);
        bool _glows;
        float _life, _auraTick, _baseMass = 70f;
        Renderer[] _skin;
        Rigidbody _rb;

        // ---- THE GRAND DEMON (GRAMMAR v4, all-12 lineage): large, UNKILLABLE
        // (expires only with time), constantly summons random calamities, and
        // every zombie fears it unconditionally. Correct play: RUN, and hope
        // it eats the horde on its way out.
        bool _grand;
        float _calamityTick, _grandFearTick;
        Damageable _grandDmg;

        public static Demon SummonGrand(Vector3 pos, float srcSize)
        {
            var d = Summon(pos, Mathf.Max(srcSize, 2f));
            if (d == null) return null;
            d._grand = true;
            d._life = 42f;
            d.transform.localScale = Vector3.one * 2.8f; // LARGE — reads across the map
            if (d._rb != null) { d._baseMass = 70f * 8f; d._rb.mass = d._baseMass; }

            var lib = FxLibrary.I; // the arrival is an EVENT (Marko's JMO layer)
            if (lib != null)
            {
                FxLibrary.Spawn(lib.DemonBoom, pos);
                FxLibrary.Spawn(lib.SkullHead, pos + Vector3.up * 3.2f, d.transform, 7f);
            }

            // UNKILLABLE from frame ZERO: it spawns into the epicentre of an
            // all-12 chain — a meteor landing on it in the one frame before
            // Update's refresh permanently killed the apocalypse (verified)
            d._grandDmg = d.GetComponent<Damageable>();
            if (d._grandDmg != null) d._grandDmg.Health = 999999f;

            // and it fears NOTHING — its own calamities were writing Danger
            // memories that froze it mid-rampage (it scared itself, verified)
            var brain = d.GetComponent<ZombieBrain>();
            if (brain != null) brain.Fearless = true;
            DrawingWorld.Instance?.LogEvent("ALL TWELVE ANSWERED. RUN.");
            Juice.Boom(pos, 1.4f);
            Juice.Shake(1f, 0.8f);
            return d;
        }

        /// A random lvl2/lvl3 effect from the whole grammar, somewhere nearby —
        /// the Demon casts the same magic the players do, just constantly.
        void Calamity()
        {
            Vector3 at = transform.position
                + new Vector3(Random.Range(-7f, 7f), 0.4f, Random.Range(-7f, 7f));
            switch (Random.Range(0, 10))
            {
                case 0: GrammarFX.FlameBurst(at, 1f); break;
                case 1: SnowField.Open(at, 1f); break;
                case 2: PlasmaField.Open(at, 0.8f); break;
                case 3: BlackHoleField.Open(at, 1f, growing: false); break;
                case 4: WhiteHoleField.Open(at, 1f); break;
                case 5: TimeFreezeField.Open(at, 1f); break;
                case 6: InertiaField.Open(at, 1f); break;
                case 7: TornadoField.Open(at, 1f, Random.value < 0.5f, 0UL); break;
                case 8: FormConjures.Meteorite(at, Vector3.up, SurfaceMaterialType.Stone, 0.3f, 1.1f, 1, 0UL); break;
                default: FormConjures.IceSpikes(at, Vector3.up, SurfaceMaterialType.Stone, 0.25f, 0.9f, 0UL); break;
            }
            GetComponent<ZombieBrain>()?.Mumble("HAHAHA!!", 1.2f);
        }

        /// Rift-side entry point. srcSize = the zone radius of the rune chain
        /// that condensed into the rift — bigger drawing, bigger demon.
        public static Demon Summon(Vector3 pos, float srcSize)
        {
            var z = Zombie.Spawn(pos, ZombieKind.Walker, 1.15f);
            if (z == null) return null;
            z.name = "Demon";

            // A DEMON TOWERS (Marko: "it uses the same body as zombies...
            // since it's destroying everything that thing should be large") —
            // the same rig, but never below twice a normal zombie, and a big
            // drawing raises a proper kaiju.
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

            // horns, so nobody mistakes what came through
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
                db.Fearless = true; // a flame demon must not flee its own aura
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
                // GRAMMAR v4 condensed kinds — eating a FLAME (the most common
                // manifest) silently did nothing before (verified)
                case ParticleKind.Flame: Become("FLAME", new Color(1f, 0.45f, 0.08f), ParticleKind.Spark, true); break;
                case ParticleKind.BlackHole: Become("VOID", new Color(0.03f, 0.01f, 0.06f), ParticleKind.Dark, false); break;
                // a BarrierMote is just swallowed — isolating a demon from
                // itself is not a thing
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
                : ParticleKind.Dense;                                          // throws its weight around
            Become(m.Material.ToString().ToUpper(), tint, aura, hot);
            Destroy(m.gameObject); // gone. eaten.
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
                // UNKILLABLE: whatever hurt it this frame, it doesn't care —
                // only the clock ends a grand demon
                if (_grandDmg == null) _grandDmg = GetComponent<Damageable>();
                if (_grandDmg != null) _grandDmg.Health = 999999f;

                // unconditional terror — every zombie that exists nearby flees
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

            // the aura: the demon LEAKS its element as real particles — its
            // element does whatever that element does, no special cases
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
        /// credit — nothing killed it, it just went back.
        void Expire()
        {
            for (int i = 0; i < 4; i++)
                SpellParticle.Emit(_aura, transform.position + Vector3.up * 0.8f,
                    Random.onUnitSphere, 1f);
            WorldEvents.Report(WorldEventKind.Explosion, transform.position, 1.5f);
            DrawingWorld.Instance?.LogEvent($"the {_form} demon returns to the dark");
            Destroy(gameObject);
        }
    }
}
