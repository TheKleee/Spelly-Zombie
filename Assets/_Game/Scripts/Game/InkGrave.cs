using UnityEngine;

namespace SpellyZombie
{
    /// The map-center marker where vessel-less ink pools. Put this component
    /// on the authored ink blob at the center: hidden until ink grounds here,
    /// then the pot pipeline drives its size and colour.
    public class InkGrave : MonoBehaviour
    {
        public static InkGrave I { get; private set; }

        Renderer[] _rends;

        void Awake()
        {
            _rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in _rends)
                if (r != null) r.enabled = false;
        }

        /// The ink has landed: the authored blob becomes visible.
        public void Reveal()
        {
            if (_rends == null) return;
            foreach (var r in _rends)
                if (r != null) r.enabled = true;
        }

        void OnEnable() { I = this; }
        void OnDisable() { if (I == this) I = null; }
    }
}
