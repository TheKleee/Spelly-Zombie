using UnityEngine;

namespace SpellyZombie
{
    /// The charge, shared by golems and zombies. Three beats:
    ///   TELL   - it hops on the spot and looks angry. You get this long to read it.
    ///   COMMIT - the direction is locked to where the target WAS at that moment.
    ///   RUN    - it drives straight, hits whatever it meets, then recovers.
    /// It never steers mid-charge. A homing charge is undodgeable, so the
    /// tell plus the committed line IS the fight: move and it misses.
    [RequireComponent(typeof(Rigidbody))]
    public class ChargeAttack : MonoBehaviour
    {
        public float Range = 9f;          // starts looking to charge inside this
        public float Cooldown = 4f;
        public float Damage = 12f;

        enum Beat { Idle, Tell, Run, Recover }

        Beat _beat = Beat.Idle;
        float _until, _readyAt;
        Vector3 _dir;
        Rigidbody _rb;
        Creature _me;
        GooglyEyes _eyes;

        /// True while it is telling or running - the brain must not steer then.
        public bool Busy => _beat == Beat.Tell || _beat == Beat.Run;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _me = GetComponent<Creature>();
            _eyes = GetComponentInChildren<GooglyEyes>();
        }

        /// Ask for a charge at a target. Refused while cooling down, already
        /// charging, or when the creature cannot act.
        public bool TryStart(Vector3 targetPos)
        {
            if (_beat != Beat.Idle || Time.time < _readyAt) return false;
            if (_me != null && _me.SpeedMultiplier <= 0.01f) return false;

            Vector3 flat = targetPos - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.04f || flat.magnitude > Range) return false;

            _dir = flat.normalized;   // LOCKED HERE - where you were, not where you go
            _beat = Beat.Tell;
            _until = Time.time + DrawingConfig.ChargeTellSeconds;

            // the tell: a hop in place and wide angry eyes
            if (_rb != null && !_rb.isKinematic)
                _rb.AddForce(Vector3.up * DrawingConfig.ChargeTellHop, ForceMode.VelocityChange);
            if (_eyes != null) _eyes.Swell(DrawingConfig.ChargeTellSeconds, 1.6f);
            return true;
        }

        void FixedUpdate()
        {
            if (_beat == Beat.Idle) return;
            if (_me != null && _me.SpeedMultiplier <= 0.01f) { Stop(); return; }

            if (Time.time >= _until)
            {
                if (_beat == Beat.Tell)
                {
                    _beat = Beat.Run;
                    _until = Time.time + DrawingConfig.ChargeRunSeconds;
                }
                else { Stop(); return; }
            }

            if (_beat != Beat.Run || _rb == null || _rb.isKinematic) return;

            // strength IS health: a hurt charger hits softer and slower
            float mul = _me != null ? _me.StrengthMul : 1f;
            Vector3 want = _dir * (DrawingConfig.ChargeSpeed * mul);
            var v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(want.x, v.y, want.z);
            transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
        }

        void Stop()
        {
            if (_beat == Beat.Run || _beat == Beat.Tell)
                _readyAt = Time.time + Cooldown;
            _beat = Beat.Idle;
        }

        void OnCollisionEnter(Collision c)
        {
            if (_beat != Beat.Run) return;

            float mul = _me != null ? _me.StrengthMul : 1f;
            float hit = Damage * mul;
            Vector3 shove = _dir * DrawingConfig.ChargeShove + Vector3.up * 2f;

            var player = c.collider.GetComponentInParent<SimpleFPSController>();
            if (player != null) player.TakeHit(shove, hit, $"{name} charge");
            else
            {
                var dmg = c.collider.GetComponentInParent<Damageable>();
                if (dmg != null && dmg.gameObject != gameObject)
                    dmg.TakeDamage(hit, $"{name} charge");
                var orb = c.collider.attachedRigidbody;
                if (orb != null && !orb.isKinematic) orb.AddForce(shove, ForceMode.VelocityChange);
            }

            Stop(); // it spends itself on the first thing it meets
        }
    }
}
