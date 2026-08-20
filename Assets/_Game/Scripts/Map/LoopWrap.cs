using UnityEngine;

namespace SpellyZombie
{
    /// Crossing the loop boundary on an axis wraps that coordinate to the
    /// far side (x = +80 becomes x = -80), momentum and heading intact.
    public class LoopWrap : MonoBehaviour
    {
        public float Limit = 80f;
        CharacterController _cc;

        void Awake() => _cc = GetComponent<CharacterController>();

        void LateUpdate()
        {
            Vector3 p = transform.position;
            Vector3 q = p;
            if (p.x > Limit) q.x = p.x - Limit * 2f;
            else if (p.x < -Limit) q.x = p.x + Limit * 2f;
            if (p.z > Limit) q.z = p.z - Limit * 2f;
            else if (p.z < -Limit) q.z = p.z + Limit * 2f;
            if (q == p) return;

            // CharacterController must be disabled to teleport
            if (_cc != null) _cc.enabled = false;
            transform.position = q;
            if (_cc != null) _cc.enabled = true;
        }
    }
}
