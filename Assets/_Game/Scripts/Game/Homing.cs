using UnityEngine;

namespace SpellyZombie
{
    /// A thrown body steering into the enemy the throw locked onto. Keeps its
    /// speed, bends its path; gone at first contact or when the lock runs out.
    public class Homing : MonoBehaviour
    {
        Rigidbody _rb;
        Transform _target;
        float _until;

        public static void Steer(Rigidbody rb, Transform target)
        {
            if (rb == null || target == null) return;
            var h = rb.GetComponent<Homing>();
            if (h == null) h = rb.gameObject.AddComponent<Homing>();
            h._rb = rb;
            h._target = target;
            h._until = Time.time + DrawingConfig.ThrowLockSeconds;
        }

        void FixedUpdate()
        {
            if (_rb == null || _target == null || _rb.isKinematic || Time.time > _until) { Destroy(this); return; }
            Vector3 v = _rb.linearVelocity;
            float speed = v.magnitude;
            if (speed < 1f) { Destroy(this); return; }
            Vector3 want = _target.position + Vector3.up * 0.9f - _rb.position;
            if (want.sqrMagnitude < 0.01f) { Destroy(this); return; }
            _rb.linearVelocity = Vector3.RotateTowards(v / speed, want.normalized,
                DrawingConfig.ThrowLockTurn * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f) * speed;
        }

        void OnCollisionEnter(Collision c) => Destroy(this);
    }
}
