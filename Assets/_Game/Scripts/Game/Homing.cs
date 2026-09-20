using UnityEngine;

namespace SpellyZombie
{
    /// A thrown body drawn to the nearest enemy ahead of it while it flies,
    /// the same pull a flying spell feels, so debris curves the same way.
    /// Gone at first contact or once it has stopped flying.
    public class Homing : MonoBehaviour
    {
        Rigidbody _rb;
        int _owner;
        Transform _target;
        float _lift, _scanAt, _slowSince = -1f;

        public static void Arm(Rigidbody rb, int ownerId)
        {
            if (rb == null || DrawingConfig.SpellPullRange <= 0f) return;
            var h = rb.GetComponent<Homing>();
            if (h == null) h = rb.gameObject.AddComponent<Homing>();
            h._rb = rb;
            h._owner = ownerId;
            h._target = null;
            h._scanAt = 0f;
            h._slowSince = -1f;
        }

        void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic) { Destroy(this); return; }
            Vector3 v = _rb.linearVelocity;
            if (v.magnitude < DrawingConfig.SpellPullMinSpeed)
            {
                if (_slowSince < 0f) _slowSince = Time.time;
                if (Time.time - _slowSince > 0.5f) Destroy(this); // it has landed
                return;
            }
            _slowSince = -1f;
            float range = DrawingConfig.SpellPullRange;
            if (Time.time >= _scanAt)
            {
                _scanAt = Time.time + 0.1f;
                _target = LockOn.Nearest(_rb.position, v, _owner, range, out _lift);
            }
            if (_target == null) return;
            Vector3 want = _target.position + Vector3.up * _lift - _rb.position;
            float dist = want.magnitude;
            if (dist > range || dist < 0.05f || Vector3.Dot(want, v) < 0f) { _target = null; return; }
            // the pull grows as it closes, on top of the throw: a natural curve
            _rb.linearVelocity = v + want / dist * (DrawingConfig.SpellPull * (1f - dist / range) * Time.fixedDeltaTime);
        }

        void OnCollisionEnter(Collision c)
        {
            // what was thrown lands: harder = louder
            float speed = c.relativeVelocity.magnitude;
            Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            Juice.Sound(Sfx.ThrownObjectHitting, at, Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(3f, 16f, speed)),
                Random.Range(0.92f, 1.08f));
            Destroy(this);
        }
    }
}
