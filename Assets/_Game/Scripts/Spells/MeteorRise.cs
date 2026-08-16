using UnityEngine;

namespace SpellyZombie
{
    /// THE METEOR'S WHOLE ARC : born at
    /// the seal, thrown upward, SWELLS as it climbs, then a TERMINAL DIVE and
    /// an EXPLOSION ON IMPACT. "It should explode on impact and it should
    /// fall really fast."
    ///
    /// Three captors had stolen the fall and are evicted here:
    ///  - MatterStrike made the rock a hovering turret (the grey ball hanging
    ///    over the square). A meteor is ordnance, not a sentry: the strike
    ///    component is destroyed at birth.
    ///  - The StateBlob soft body kept deforming it ("it tends to change
    ///    shape"). Bones are frozen for the flight: a meteor is RIGID.
    ///  - The old "stopped = landed" check exploded it mid-hover. Now the
    ///    explosion is a real collision, nothing else.
    public class MeteorRise : MonoBehaviour
    {
        /// Final size as a multiple of the size it was born at.
        public float Grow = 10f;

        /// The blast's FIRE spread, the summed-ingredient reach dial.
        public float Reach = 1f;

        /// Released from a dormant ghost: no rise story, it FALLS FROM THE
        /// SKY at full size ("when I release the dormant meteor it
        /// should fall from the sky"). The seal-cast keeps its erupt-and-rise.
        public bool SkyDrop;

        Vector3 _birth;
        float _t;
        float _age;
        bool _diving;
        bool _spent;

        void Start()
        {
            _birth = transform.localScale;

            // ordnance, not a sentry: no target-lock hovering. DISABLED, not
            // destroyed: a destroyed MatterStrike tears down the Rigidbody it
            // adopted, and everything still holding that rb starts throwing.
            if (TryGetComponent<MatterStrike>(out var strike)) strike.enabled = false;

            // NOTE: StateBlob stays ENABLED. Disabling it killed the skin it
            // builds and the giant rock rendered as a raw black slab (the
            // "flat out invisible" meteor). Rigidity is already law for true
            // solids inside StateBlob itself: phase Solid pins the bones.

            if (SkyDrop) _diving = true; // born overhead, already falling
        }

        void FixedUpdate()
        {
            if (_spent) return;
            _age += Time.fixedDeltaTime;
            if (!TryGetComponent<Rigidbody>(out var rb)) { _spent = true; return; }

            if (!_diving)
            {
                // swelling only on the way UP; the apex flips it to the dive
                if (rb.linearVelocity.y <= 0.05f && _age > 0.2f) { _diving = true; return; }
                _t = Mathf.Min(1f, _t + Time.fixedDeltaTime / Mathf.Max(0.05f,
                    DrawingConfig.MeteorGrowSeconds));
                transform.localScale = _birth * Mathf.Lerp(1f, Mathf.Max(1f, Grow), _t);
                return;
            }

            // TERMINAL DIVE : straight down,
            // held at the dive speed, drift damped so it lands where it hung
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x * 0.96f,
                Mathf.Min(v.y, -DrawingConfig.MeteorFallSpeed), v.z * 0.96f);

            // a SKY DROP keeps swelling on the way DOWN (the Madara framing:
            // "starts off big and becomes bigger and falls from really high")
            if (SkyDrop && _t < 1f)
            {
                _t = Mathf.Min(1f, _t + Time.fixedDeltaTime / 1.8f);
                transform.localScale = _birth * Mathf.Lerp(1f, Mathf.Max(1f, Grow), _t);
            }
        }

        void OnCollisionEnter(Collision hit)
        {
            // armed after launch; the birth frames at the seal do not count
            if (_spent || _age < 0.25f) return;
            _spent = true;

            Vector3 at = transform.position;
            Juice.Boom(at, 0.95f);
            DrawingWorld.Instance?.LogEvent("METEOR IMPACT");

            // FULL ULTIMATE POWER RESTORED ("bring back the power of
            // the meteor... you removed effects I never said you should").
            // Same numbers the FlameBurst ultimate carries: heavy hit, real
            // ignition heat on everything, hard shove. Only the FLAMES stay
            // modest, per the separate ruling that the fires were too large.
            // damage to EVERYONE in the area, no exceptions (the rule: "the
            // meteor is the kind of a spell that just deals damage to
            // everyone in the area") — players, zombies, anything alive
            float r = DrawingConfig.UltimateRadius;
            var seen = new System.Collections.Generic.HashSet<Damageable>();
            var hits = Physics.OverlapSphere(at, r);
            foreach (var c in hits)
            {
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null)
                {
                    pl.TakeHit((pl.transform.position - at).normalized * 9f, 28f);
                    pl.KnockDown(1.2f);
                    continue;
                }
                SpellParticle.GiveHeatTo(c, 200f); // houses catch, wood burns
                var alive = c.GetComponentInParent<Damageable>();
                if (alive != null && seen.Add(alive))
                    alive.TakeDamage(30f, "meteor impact");
                var rb2 = c.attachedRigidbody;
                if (rb2 != null && rb2.gameObject != gameObject)
                    rb2.AddForce((rb2.worldCenterOfMass - at).normalized * 9f,
                        ForceMode.VelocityChange);
            }

            // THE ROCK SHATTERS (the "it should blow into smaller chunks so
            // that we feel the pressure and power"): glowing debris hurled
            // out of the crater, each chunk a real hot solid that ignites
            // whatever it lands on and can be grabbed once it cools
            var tag = GetComponentInChildren<SurfaceMaterialTag>();
            var mt = tag != null ? tag.Material : SurfaceMaterialType.Stone;
            // PUSHED FROM THE INSIDE : shards burst radially out of
            // the rock's core at high force, splatter across the square, and
            // EACH ONE EXPLODES where it lands, leaving that spot hotter
            float rockR = Mathf.Max(0.5f, transform.lossyScale.x);
            Vector3 core = at + Vector3.up * rockR * 0.4f;
            for (int i = 0; i < 12; i++)
            {
                Vector3 shell = core + Random.onUnitSphere * rockR * 0.35f;
                var ch = Matter.Spawn(mt, MatterPhase.Solid,
                    Mathf.Max(0.15f, rockR * 0.09f), shell);
                if (ch == null) continue;
                ch.Temperature = 300f; // still glowing
                ch.gameObject.AddComponent<MeteorShard>();
                if (ch.TryGetComponent<Rigidbody>(out var crb))
                {
                    // the SKETCH: a fountain. Shards leap mostly UP in tall
                    // arcs, fan outward, and rain down far across the square,
                    // each little comet landing on its own spot.
                    Vector3 side = (shell - core);
                    side.y = 0f;
                    side = side.sqrMagnitude > 0.001f ? side.normalized : Random.insideUnitSphere;
                    Vector3 dir = (side * Random.Range(0.35f, 1f)
                        + Vector3.up * Random.Range(1.3f, 2.3f)).normalized;
                    // fast enough to still splatter WIDE under the shard's
                    // heavy gravity: the whole fountain plays at whipcrack
                    // speed
                    crb.linearVelocity = dir * Random.Range(36f, 58f);
                }
            }

            // fires: reach-scaled, a notch wider now (the "increase the aoe
            // effect a bit... too small right now")
            GrammarFX.FireBloom(at, 8, Mathf.Max(1.7f, Reach * 2f), 2f);

            Destroy(gameObject); // the rock itself IS the explosion
        }
    }

    /// A shard of the shattered meteor: flies hot, and EXPLODES where it
    /// lands (the spec: "each exploding and leaving the area hotter, more
    /// burning") — a small burst of fire and heat, then the shard is spent.
    public class MeteorShard : MonoBehaviour
    {
        float _age;
        bool _done;

        void Start()
        {
            // shrapnel, not a sentry: no hover, no target lock (disabled, not
            // destroyed: see MeteorRise, the rb-teardown lesson)
            if (TryGetComponent<MatterStrike>(out var s)) s.enabled = false;
            if (TryGetComponent<Rigidbody>(out var rb))
                rb.angularVelocity = Random.insideUnitSphere * 9f; // tumbling menace
        }

        void FixedUpdate()
        {
            // REAL DANGEROUS THINGS (the words: falling "slowly and
            // anticlimactically"): shrapnel slams down under heavy gravity,
            // the arc is a whipcrack, never a drift
            if (TryGetComponent<Rigidbody>(out var rb))
                rb.AddForce(Physics.gravity * 3.5f, ForceMode.Acceleration);
        }

        void Update() => _age += Time.deltaTime;

        void OnCollisionEnter(Collision c)
        {
            if (_done || _age < 0.15f) return; // clear the mother rock first
            _done = true;
            Vector3 at = transform.position;
            GrammarFX.FireBloom(at, 2, 1.3f, 1.5f);
            foreach (var h in Physics.OverlapSphere(at, 1.6f))
                SpellParticle.GiveHeatTo(h, 90f);
            Juice.Thud(at);
            // immediate, not delayed: the 0.05s grace let physics touch a
            // half-destroyed Rigidbody (the MissingReferenceException)
            Destroy(gameObject);
        }
    }
}
