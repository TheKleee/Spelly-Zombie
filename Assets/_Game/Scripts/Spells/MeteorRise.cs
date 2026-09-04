using UnityEngine;

namespace SpellyZombie
{
    /// Meteor flight: born at the seal, thrown upward, swells as it climbs,
    /// then a terminal dive and an explosion on real impact.
    public class MeteorRise : MonoBehaviour
    {
        /// Final size as a multiple of the size it was born at.
        public float Grow = 10f;

        /// The blast's FIRE spread, the summed-ingredient reach dial.
        public float Reach = 1f;

        /// Dormant release: falls from the sky at full size; seal-cast keeps erupt-and-rise.
        public bool SkyDrop;

        Vector3 _birth;
        float _t;
        float _age;
        bool _diving;
        bool _spent;

        void Start()
        {
            _birth = transform.localScale;

            // disabled, not destroyed: destroying MatterStrike tears down the Rigidbody it adopted
            if (TryGetComponent<MatterStrike>(out var strike)) strike.enabled = false;

            // StateBlob stays enabled: disabling kills the skin; solid phase already pins the bones

            if (SkyDrop) _diving = true; // born overhead, already falling
        }

        void FixedUpdate()
        {
            if (_spent) return;
            _age += Time.fixedDeltaTime;
            if (!TryGetComponent<Rigidbody>(out var rb)) { _spent = true; return; }

            if (!_diving)
            {
                // swelling only on the way up; the apex flips it to the dive
                if (rb.linearVelocity.y <= 0.05f && _age > 0.2f) { _diving = true; return; }
                _t = Mathf.Min(1f, _t + Time.fixedDeltaTime / Mathf.Max(0.05f,
                    DrawingConfig.MeteorGrowSeconds));
                transform.localScale = _birth * Mathf.Lerp(1f, Mathf.Max(1f, Grow), _t);
                return;
            }

            // terminal dive: straight down at dive speed, drift damped
            var v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x * 0.96f,
                Mathf.Min(v.y, -DrawingConfig.MeteorFallSpeed), v.z * 0.96f);

            // a sky drop keeps swelling on the way down
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

            // same numbers as the FlameBurst ultimate; damage hits everyone in the area
            float r = DrawingConfig.UltimateRadius;
            var seen = new System.Collections.Generic.HashSet<Element>();
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
                var alive = c.GetComponentInParent<Element>();
                if (alive != null && seen.Add(alive))
                    alive.TakeDamage(30f, "meteor impact");
                var rb2 = c.attachedRigidbody;
                if (rb2 != null && rb2.gameObject != gameObject)
                    rb2.AddForce((rb2.worldCenterOfMass - at).normalized * 9f,
                        ForceMode.VelocityChange);
            }

            // the rock shatters into hot solid chunks that ignite what they land on
            var tag = GetComponentInChildren<SurfaceMaterialTag>();
            var mt = tag != null ? tag.Material : SurfaceMaterialType.Stone;
            // shards burst radially from the core; each explodes where it lands
            float rockR = Mathf.Max(0.5f, transform.lossyScale.x);
            Vector3 core = at + Vector3.up * rockR * 0.4f;
            for (int i = 0; i < 12; i++)
            {
                Vector3 shell = core + Random.onUnitSphere * rockR * 0.35f;
                var ch = Matter.Spawn(mt, MatterPhase.Solid,
                    Mathf.Max(0.15f, rockR * 0.09f), shell);
                if (ch == null) continue;
                var own = GetComponent<Matter>();
                if (own != null) { ch.StampOwner(own.TeamOwner); ch.SpellBorn = own.SpellBorn; } // shards keep the team
                ch.Temperature = 300f; // still glowing
                ch.gameObject.AddComponent<MeteorShard>();
                if (ch.TryGetComponent<Rigidbody>(out var crb))
                {
                    // fountain: shards leap up in tall arcs and fan outward
                    Vector3 side = (shell - core);
                    side.y = 0f;
                    side = side.sqrMagnitude > 0.001f ? side.normalized : Random.insideUnitSphere;
                    Vector3 dir = (side * Random.Range(0.35f, 1f)
                        + Vector3.up * Random.Range(1.3f, 2.3f)).normalized;
                    // fast enough to splatter wide under the shard's heavy gravity
                    crb.linearVelocity = dir * Random.Range(36f, 58f);
                }
            }

            // fires scale with reach
            GrammarFX.FireBloom(at, 8, Mathf.Max(1.7f, Reach * 2f), 2f);

            Destroy(gameObject); // the rock itself is the explosion
        }
    }

    /// A shard of the shattered meteor: flies hot and explodes where it lands.
    public class MeteorShard : MonoBehaviour
    {
        float _age;
        bool _done;

        void Start()
        {
            // no hover, no target lock (disabled, not destroyed: see MeteorRise)
            if (TryGetComponent<MatterStrike>(out var s)) s.enabled = false;
            if (TryGetComponent<Rigidbody>(out var rb))
                rb.angularVelocity = Random.insideUnitSphere * 9f;
        }

        void FixedUpdate()
        {
            // heavy gravity: shrapnel slams down fast
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
            // destroy immediately; a delay lets physics touch a half-destroyed Rigidbody
            Destroy(gameObject);
        }
    }
}
