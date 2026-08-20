using UnityEngine;

namespace SpellyZombie
{
    /// Ink comet: a bright drop with a long trail. It climbs to the sky when
    /// ink leaves and falls out of the sky onto the pot when ink arrives.
    public class SkyBeam : MonoBehaviour
    {
        const float Height = 70f;
        const float UpTime = 2.2f;
        /// Fall duration; callers fire the comet this many seconds before
        /// filling the pot so the ink appears at contact.
        public const float DownSeconds = 1.5f;

        Vector3 _from, _to;
        float _age, _time;
        Transform _target;        // homing target while falling
        System.Action _onArrive;  // fired at contact; the caller spawns the ink then
        bool _arrived;

        /// Ink leaves: the drop climbs away.
        public static void Up(Vector3 at, Color c) => Spawn(at, null, c, true, null);

        /// Ink arrives: the drop falls onto a fixed spot.
        public static void Down(Vector3 at, Color c) => Spawn(at, null, c, false, null);

        /// The drop homes onto a moving target while it falls and fires
        /// onArrive at contact; the caller spawns the ink then.
        public static void Down(Transform target, Color c, System.Action onArrive)
            => Spawn(target.position, target, c, false, onArrive);

        static void Spawn(Vector3 at, Transform target, Color c, bool rising, System.Action onArrive)
        {
            // trail uses the ink's actual color; dark ink cannot glow
            // additively (black adds nothing), so it rides an opaque streak
            bool dark = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.3f;
            var mat = dark
                ? MatterFX.Get(new Color(c.r, c.g, c.b, 0.95f), MoteShade.Transparent)
                : MatterFX.Get(new Color(c.r, c.g, c.b, 0.9f), MoteShade.Additive);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = rising ? "InkComet_Up" : "InkComet_Down";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.55f;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 1.1f;
            trail.startWidth = 0.5f;
            trail.endWidth = 0.02f;
            trail.sharedMaterial = mat;
            trail.minVertexDistance = 0.3f;

            var b = go.AddComponent<SkyBeam>();
            b._target = target;
            b._onArrive = onArrive;
            b._from = rising ? at + Vector3.up * 0.4f : at + Vector3.up * Height;
            b._to = rising ? at + Vector3.up * Height : at + Vector3.up * 0.4f;
            b._time = rising ? UpTime : DownSeconds;
            go.transform.position = b._from;
        }

        void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _time);
            t *= t; // a drop gathering speed, both directions
            if (_target != null)
            {
                // both endpoints track the target so the drop still falls
                // straight down onto a moving pot
                Vector3 basePos = _target.position;
                _from = basePos + Vector3.up * Height;
                _to = basePos + Vector3.up * 0.4f;
            }
            transform.position = Vector3.Lerp(_from, _to, t);
            if (!_arrived && _age >= _time)
            {
                _arrived = true;
                _onArrive?.Invoke();
            }
            if (_age >= _time + 1.3f) Destroy(gameObject); // the trail finishes fading
        }
    }
}
