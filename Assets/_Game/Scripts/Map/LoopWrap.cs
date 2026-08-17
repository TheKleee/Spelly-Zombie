using UnityEngine;

namespace SpellyZombie
{
    /// The infinite ocean: cross the loop boundary on an axis and that
    /// coordinate wraps to the far side (x = +80 becomes x = -80), momentum
    /// and heading intact. With the horizon fog, the island fading in ahead
    /// IS the island you left behind - the world reads endless.
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

            // CharacterController fights teleports - the FallCatcher pattern
            if (_cc != null) _cc.enabled = false;
            transform.position = q;
            if (_cc != null) _cc.enabled = true;
        }
    }
}
