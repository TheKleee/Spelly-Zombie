using UnityEngine;

namespace SpellyZombie
{
    /// Spell-born rubble: temporary, never deforms - a hard hit pops it into
    /// smaller debris, down to dust that expires.
    public class SpellDebris : MonoBehaviour
    {
        SurfaceMaterialType _mat;
        MatterPhase _phase;
        float _size;
        float _life;
        float _grace = 0.8f; // let the scatter land first - pops come later

        public void Init(SurfaceMaterialType mat, MatterPhase phase, float size)
        {
            _mat = mat;
            _phase = phase;
            _size = size;
            _life = DrawingConfig.ParticleLife * 1.4f; // lingers, then stabilizes away
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_grace > 0f) _grace -= dt;
            _life -= dt;
            if (_life < 1f) // the last second: visibly crumble out
                transform.localScale *= Mathf.Max(0.01f, 1f - dt);
            if (_life <= 0f || transform.localScale.x < 0.02f) Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            if (_grace > 0f) return;                          // settling is allowed
            if (c.relativeVelocity.sqrMagnitude < 16f) return; // soft touches are fine
            Pop();
        }

        /// Breaks into smaller chunks instead of deforming.
        void Pop()
        {
            if (_size <= 0.16f) { Destroy(gameObject); return; } // dust just dies
            for (int i = 0; i < 2; i++)
            {
                Vector3 d = (Random.onUnitSphere + Vector3.up * 0.4f).normalized;
                var chunk = Matter.Spawn(_mat, _phase, _size * 0.5f,
                    transform.position + d * 0.25f, 0);
                if (chunk == null) continue;
                chunk.gameObject.AddComponent<SpellDebris>().Init(_mat, _phase, _size * 0.5f);
                if (chunk.TryGetComponent<Rigidbody>(out var crb))
                    crb.linearVelocity = d * 5f;
            }
            Destroy(gameObject);
        }
    }
}
