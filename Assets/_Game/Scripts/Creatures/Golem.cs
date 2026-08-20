using UnityEngine;

namespace SpellyZombie
{
    /// Nature's answer to wrecking the place. A golem belongs to nobody: it
    /// wanders, and anything it sees is an enemy. It does not guard a summoner
    /// and it does not take orders.
    /// Its one attack is the shared ChargeAttack - hop, lock the direction,
    /// drive straight - so it is dodgeable by moving, and its debuff is
    /// whatever its BiomeStamp made it.
    /// Built from the CollectionManager's Golem prefab; nothing is code-built.
    [RequireComponent(typeof(Rigidbody))]
    public class Golem : MonoBehaviour
    {
        public float SightRange = 13f;
        public float WalkSpeed = 1.9f;

        Rigidbody _rb;
        Creature _me;
        ChargeAttack _charge;
        Damageable _dmg;

        Vector3 _wander;
        float _pickAt, _skipAt;

        /// Raise one from the authored prefab. Null (and a loud log) when the
        /// CollectionManager slot is empty - nothing is substituted.
        public static Golem Spawn(Vector3 at, float sizeMul = 1f)
        {
            var prefab = CollectionManager.Golem;
            if (prefab == null) return null;

            var go = Instantiate(prefab, at, Quaternion.Euler(0f, Random.value * 360f, 0f));
            if (sizeMul != 1f) go.transform.localScale *= sizeMul;

            var g = go.GetComponent<Golem>();
            if (g == null) g = go.AddComponent<Golem>();

            // strength from the body it has: bigger and heavier is stronger
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.mass *= sizeMul * sizeMul * sizeMul;  // mass is volume
            var dmg = go.GetComponent<Damageable>();
            if (dmg != null) dmg.SetStrengthFromBody(sizeMul, rb != null ? rb.mass : 0f);

            // spread it thin enough and it cannot hold together: two golems
            if (go.GetComponent<DensitySplit>() == null) go.AddComponent<DensitySplit>();

            // the ground that raised it decides what it resists and how it looks
            BiomeStamp.Apply(go, at);
            return g;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _me = GetComponent<Creature>();
            _dmg = GetComponent<Damageable>();
            _charge = GetComponent<ChargeAttack>();
            if (_charge == null) _charge = gameObject.AddComponent<ChargeAttack>();
            PickWander();
        }

        void PickWander()
        {
            float a = Random.value * Mathf.PI * 2f;
            _wander = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            _pickAt = Time.time + Random.Range(2.5f, 6f);
        }

        void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic) return;
            if (_dmg != null && _dmg.Health <= 0f) return;
            if (_charge != null && _charge.Busy) return;   // the charge owns movement

            float mul = _me != null ? _me.SpeedMultiplier : 1f;
            if (mul <= 0.01f) return;

            // anything alive in sight is an enemy - no teams, no owner
            var prey = NearestTarget();
            if (prey != null)
            {
                Vector3 to = prey.position - transform.position;
                to.y = 0f;
                if (_charge != null && _charge.TryStart(prey.position)) return;
                if (to.sqrMagnitude > 0.04f) _wander = to.normalized;
            }
            else if (Time.time >= _pickAt) PickWander();

            Step(_wander, mul);
        }

        /// Walks by SKIPPING: a little hop with every step rather than a glide.
        void Step(Vector3 dir, float mul)
        {
            Vector3 want = dir * (WalkSpeed * mul);
            var v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(want.x, v.y, want.z);
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir, Vector3.up), 6f * Time.fixedDeltaTime);

            if (Time.time < _skipAt) return;
            _skipAt = Time.time + DrawingConfig.GolemSkipEvery / Mathf.Max(0.2f, mul);
            if (Mathf.Abs(v.y) < 0.6f)  // only when it is actually on the ground
                _rb.AddForce(Vector3.up * DrawingConfig.GolemSkipHop, ForceMode.VelocityChange);
        }

        Transform NearestTarget()
        {
            Transform best = null;
            float bestSqr = SightRange * SightRange;

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDead) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p.transform; }
            }
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                float d = (z.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = z.transform; }
            }
            return best;
        }
    }
}
