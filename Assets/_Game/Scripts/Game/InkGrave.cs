using UnityEngine;

namespace SpellyZombie
{
    /// The map-center marker where vessel-less ink pools. Put this component
    /// ON the authored ink blob placed at the center: the blob stays hidden
    /// until ink actually grounds here, then the pot pipeline drives its
    /// size and colour like any pot's ink.
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
