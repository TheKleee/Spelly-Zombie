using UnityEngine;

namespace SpellyZombie
{
    /// A tiny white bean with googly eyes that seeks the nearest zombie and
    /// pecks it. Not currently spawned by anything - waiting for its combo.
    public class Chicken : MonoBehaviour
    {
        Rigidbody _rb;
        Transform _target;
        float _retarget, _peck;

        public static Chicken Spawn(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Chicken";
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.22f, 0.18f, 0.22f);
            go.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.95f, 0.93f, 0.88f), MoteShade.Opaque);
            var beak = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(beak.GetComponent<Collider>());
            beak.transform.SetParent(go.transform, false);
            beak.transform.localPosition = new Vector3(0f, 0.5f, -0.9f);
            beak.transform.localScale = new Vector3(0.3f, 0.25f, 0.6f);
            beak.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.95f, 0.65f, 0.15f), MoteShade.Opaque);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 2f;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            go.AddComponent<Damageable>().Health = 15f;
            GooglyEyes.Attach(go.transform, 0.35f, 1.6f);
            var c = go.AddComponent<Chicken>();
            Destroy(go, 25f);
            return c;
        }

        void Awake() => _rb = GetComponent<Rigidbody>();

        void FixedUpdate()
        {
            _retarget -= Time.fixedDeltaTime;
            if (_retarget <= 0f)
            {
                _retarget = 0.7f;
                float best = 15f * 15f;
                _target = null;
                foreach (var z in Zombie.All)
                {
                    if (z == null) continue;
                    float d = (z.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _target = z.transform; }
                }
            }
            if (_target == null) return;

            Vector3 to = _target.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > 0.6f)
            {
                Vector3 dir = to / dist;
                _rb.linearVelocity = new Vector3(dir.x * 3.5f, _rb.linearVelocity.y, dir.z * 3.5f);
                transform.rotation = Quaternion.LookRotation(dir);
            }
            else
            {
                _peck -= Time.fixedDeltaTime;
                if (_peck <= 0f)
                {
                    _peck = 0.6f;
                    _target.GetComponentInParent<Damageable>()?.TakeDamage(4f, "pecked by chicken");
                    _rb.AddForce(Vector3.up * 2.5f, ForceMode.Impulse);
                }
            }
        }
    }
}
