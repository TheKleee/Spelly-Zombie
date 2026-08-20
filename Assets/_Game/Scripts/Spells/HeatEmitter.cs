using UnityEngine;

namespace SpellyZombie
{
    /// A permanent heat source: a torch, a brazier, a campfire. Anything that
    /// enters the radius warms up, and keeps warming until it burns.
    /// The heat RULE is not reimplemented here - every hit goes through
    /// SpellParticle.GiveHeatTo, the same doorway the spell fields use, so
    /// players, creatures, matter and props all react exactly as they do to
    /// a conjured flame.
    /// Needs no collider: OverlapSphere finds targets, so the flame itself can
    /// stay walk-through.
    public class HeatEmitter : MonoBehaviour
    {
        [Tooltip("Metres. Anything inside this warms up.")]
        public float Radius = 1.4f;

        [Tooltip("Raw heat energy per second at the centre, falling off to 0 at the rim. " +
                 "0 = use the DrawingConfig default.")]
        public float HeatPerSecond = 0f;

        [Tooltip("Seconds between sweeps. Cheap: a torch does not need per-frame physics.")]
        public float Interval = 0.15f;

        static readonly Collider[] _buf = new Collider[24];
        float _next;

        void Update()
        {
            _next -= Time.deltaTime;
            if (_next > 0f) return;
            float dt = Interval + Mathf.Max(0f, -_next);
            _next = Interval;

            float heat = HeatPerSecond > 0f ? HeatPerSecond : DrawingConfig.TorchHeatPerSec;
            Vector3 at = transform.position;

            int n = Physics.OverlapSphereNonAlloc(at, Radius, _buf,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _buf[i];
                if (c == null || c.transform.IsChildOf(transform)) continue; // never cook itself
                // closer burns harder
                float d = Vector3.Distance(c.ClosestPoint(at), at);
                float fall = 1f - Mathf.Clamp01(d / Mathf.Max(0.01f, Radius));
                if (fall <= 0f) continue;
                SpellParticle.GiveHeatTo(c, heat * fall * dt);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, Radius);
        }
    }
}
