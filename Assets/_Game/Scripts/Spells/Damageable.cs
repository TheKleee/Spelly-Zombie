using UnityEngine;

namespace SpellyZombie
{
    /// Minimal health sink so spell effects have something to damage. Damage is
    /// always a physical consequence (heat, impact, crush) — never a "damage
    /// spell". Destructible dynamic props (crates) are removed on death; static
    /// or marked-indestructible things just log.
    public class Damageable : MonoBehaviour
    {
        public float Health = 100f;
        public bool Destructible = true;

        /// Fired once, just before the object is removed (cause string passed).
        public System.Action<string> OnDeath;

        /// Fired on every hit (amount, cause) — lets AI flinch / interrupt casts.
        public System.Action<float, string> OnDamaged;

        float _logAccum;
        bool _dead;

        Rigidbody _body;

        void Awake()
        {
            // dynamic props can be destroyed; static geometry shouldn't vanish
            _body = GetComponent<Rigidbody>();
            Destructible = _body != null;
        }

        // ================================================ IMPACT HURTS ====
        // GETTING YEETED BREAKS THINGS (Marko: thrown objects take damage from
        // physics). Nothing ever hurt a prop by hitting something — Matter
        // damaged whatever it LANDED on, but the thrown bench itself was
        // immortal. A prop with a Rigidbody now takes damage from any hard
        // enough impact, and the damage scales with how hard it hit.
        static readonly float ImpactFloor = DrawingConfig.Overlay("ImpactDamageFloor", 4f);
        // OWN key (dedupe audit Aug 5): "ImpactDamagePerSpeed" is DrawingConfig's
        // CREATURE knob (default 4, Creature.cs) — one JSON key silently drove
        // both systems while the code defaults disagreed. Props keep their 2.2.
        static readonly float ImpactScale = DrawingConfig.Overlay("PropImpactDamagePerSpeed", 2.2f);

        void OnCollisionEnter(Collision col)
        {
            if (_dead) return;
            // rooted props gain their body only when torn loose, long after
            // Awake — so look again rather than trusting the cached null
            if (_body == null)
            {
                _body = GetComponent<Rigidbody>();
                if (_body == null) return;
                Destructible = true;   // it's a real object now
            }
            float speed = col.relativeVelocity.magnitude;
            if (speed < ImpactFloor) return;

            // heavier things carry more into the hit, and both sides feel it
            float mass = Mathf.Max(0.2f, _body.mass);
            float dmg = (speed - ImpactFloor) * ImpactScale * Mathf.Sqrt(mass);
            if (dmg < 1f) return;

            string what = col.collider != null ? col.collider.name : "the ground";
            TakeDamage(dmg, $"slammed into {what}");

            // and what it hit takes the same beating — a bench swung into a
            // crate should wreck the crate too
            var other = col.collider != null
                ? col.collider.GetComponentInParent<Damageable>() : null;
            if (other != null && other != this) other.TakeDamage(dmg * 0.7f, $"hit by {name}");
        }

        public void TakeDamage(float amount, string cause)
        {
            if (amount <= 0f || _dead) return;
            if (Barrier.Protects(this)) return; // two-way isolation holds for EVERYTHING

            // A LIMB IS NOT FURNITURE: damage landing on a bone of a living
            // character forwards to the BEING — a Damageable that sneaks onto
            // a skeleton bone must never Destroy() it (a burning leg once
            // vanished from the rig and the skin snapped to the world origin).
            var pilot = GetComponentInParent<SimpleFPSController>();
            Component owner = pilot != null ? (Component)pilot : GetComponentInParent<Creature>();
            if (owner != null && owner.gameObject != gameObject)
            {
                var rootDmg = owner.GetComponent<Damageable>();
                if (rootDmg != null && rootDmg != this) rootDmg.TakeDamage(amount, cause);
                return;
            }

            Health -= amount;
            OnDamaged?.Invoke(amount, cause);
            _logAccum += amount;
            if (_logAccum >= 30f)
            {
                Debug.Log($"[SpellyZombie] {name}: {cause} — {Mathf.Max(0, Health):0} hp left");
                _logAccum = 0f;
            }
            if (Health <= 0f)
            {
                _dead = true;
                Debug.Log($"[SpellyZombie] {name} destroyed by {cause}");
                OnDeath?.Invoke(cause);
                if (Destructible) Destroy(gameObject);
            }
        }
    }
}
