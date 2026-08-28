using UnityEngine;

namespace SpellyZombie
{
    /// Spell-born rubble: temporary, never deforms - a hard hit POPS it into
    /// smaller debris that flies, hurts what it sprays, and can merge into
    /// small golems (the ordinary matter-merge law does that part).
    /// ★ NOTHING EVER SHRINKS OUT (his rule): expiry is a pop too - a spell
    /// dies as an event, never by quietly getting smaller.
    public class SpellDebris : MonoBehaviour
    {
        SurfaceMaterialType _mat;
        MatterPhase _phase;
        float _size;
        float _life;
        float _grace = 0.8f; // let the scatter land first - pops come later
        public int OwnerId = -1; // the team chain rides the rubble too

        public void Init(SurfaceMaterialType mat, MatterPhase phase, float size)
        {
            _mat = mat;
            _phase = phase;
            _size = size;
            _life = DrawingConfig.ParticleLife * 1.4f; // lingers, then goes out with a bang
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_grace > 0f) _grace -= dt;
            _life -= dt;
            if (_life <= 0f) Pop(); // expiry IS the pop - never a shrink
        }

        void OnCollisionEnter(Collision c)
        {
            if (_grace > 0f) return;                          // settling is allowed
            if (c.relativeVelocity.sqrMagnitude < 16f) return; // soft touches are fine
            Pop();
        }

        /// ★ THE POP (his design): the rock falls apart into debris flying
        /// around, dealing area damage; the chunks are real matter, so two of
        /// them meeting can still rise as a small golem on the caster's team.
        void Pop()
        {
            Vector3 at = transform.position;

            // area damage scaled by how big the piece was - a thrown boulder
            // bursts hard, dust barely stings
            float r = Mathf.Clamp(_size * 3f, 0.6f, 2.5f);
            float dmg = Mathf.Clamp(_size * 30f, 4f, 22f);
            var seen = new System.Collections.Generic.HashSet<Element>();
            foreach (var c in Physics.OverlapSphere(at, r))
            {
                var el = c.GetComponentInParent<Element>();
                if (el != null && el.gameObject != gameObject && seen.Add(el))
                    el.TakeDamage(dmg, "flying debris", OwnerId);
                var rb = c.attachedRigidbody;
                if (rb != null && rb.gameObject != gameObject)
                    rb.AddForce((rb.worldCenterOfMass - at).normalized * (3f + _size * 6f),
                        ForceMode.VelocityChange);
            }
            Juice.Thud(at);

            if (_size > 0.16f) // dust dies in the blast alone
            {
                int pieces = Random.Range(3, 6);
                for (int i = 0; i < pieces; i++)
                {
                    Vector3 d = (Random.onUnitSphere + Vector3.up * 0.5f).normalized;
                    var chunk = Matter.Spawn(_mat, _phase, _size * 0.5f,
                        at + d * 0.3f, 0);
                    if (chunk == null) continue;
                    var sd = chunk.gameObject.AddComponent<SpellDebris>();
                    sd.Init(_mat, _phase, _size * 0.5f);
                    sd.OwnerId = OwnerId;
                    chunk.StampOwner(OwnerId);
                    if (chunk.TryGetComponent<Rigidbody>(out var crb))
                        crb.linearVelocity = d * (5f + _size * 4f);
                }
            }
            Destroy(gameObject);
        }
    }
}
