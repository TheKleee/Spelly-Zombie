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
