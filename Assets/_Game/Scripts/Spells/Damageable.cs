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

        void Awake()
        {
            // dynamic props can be destroyed; static geometry shouldn't vanish
            Destructible = GetComponent<Rigidbody>() != null;
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
