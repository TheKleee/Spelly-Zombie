using UnityEngine;

namespace SpellyZombie
{
    /// ★ WHERE THE POT IS (his call: nobody reads text in a friends game, so
    /// the rules have to show). From the moment the ink shows in the pot a
    /// beacon of light (FxLibrary's Pot Beacon, his prefab) stands over it,
    /// until any player gets near it; never while the pot is still closed.
    /// Every machine runs its own from the positions it already sees, so
    /// nothing crosses the wire.
    public class PotBeacon : MonoBehaviour
    {
        const float Reach = 4f;          // a player this close has found the pot
        const float FadeSeconds = 3f;    // the particles die out instead of vanishing

        GameObject _beacon;
        bool _found, _wasLive, _warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("~PotBeacon");
            DontDestroyOnLoad(go);
            go.AddComponent<PotBeacon>();
        }

        void Update()
        {
            bool live = RoundDirector.RunActive && ActiveScene.Name != "Lobby" && !MapCreator.Active && !PhotoBooth.Active;
            if (live && !_wasLive) _found = false; // a new match: it has to be found again
            _wasLive = live;

            var pot = CauldronEconomy.Active;
            if (!live || _found || pot == null || !CauldronEconomy.InkShows) { PutOut(); return; }
            if (SomeoneNear(pot.transform.position))
            {
                _found = true;
                Juice.Sound(Sfx.PotDiscovered, pot.transform.position);
                PutOut();
                return;
            }

            if (_beacon == null && !Light()) return;
            // over the pot's top, following it wherever it is carried; never parented,
            // the kit roots carry a large scale
            Bounds b = ShapeShift.FindObjectBounds(pot.transform);
            _beacon.transform.position = new Vector3(b.center.x, b.max.y, b.center.z);
        }

        bool Light()
        {
            var prefab = FxLibrary.I != null ? FxLibrary.I.PotBeacon : null;
            if (prefab == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogError("[SpellyZombie] FxLibrary: the 'Pot Beacon' slot is empty. Drop in a pillar of " +
                                   "light prefab: it stands over the pot until someone reaches it. " +
                                   "Spelly Zombie/Particles/Make Placeholder Pot Beacon makes a stand-in.");
                }
                return false;
            }
            _beacon = Instantiate(prefab);
            _beacon.name = "PotBeacon";
            return true;
        }

        static bool SomeoneNear(Vector3 at)
        {
            float reach = Reach * Reach;
            foreach (var p in SimpleFPSController.All)
                if (p != null && (p.transform.position - at).sqrMagnitude <= reach) return true;
            foreach (var a in NetAvatar.All)
                if (a != null && (a.transform.position - at).sqrMagnitude <= reach) return true;
            return false;
        }

        /// The light goes out: the particles stop and die away, anything else goes at once.
        void PutOut()
        {
            if (_beacon == null) return;
            foreach (var ps in _beacon.GetComponentsInChildren<ParticleSystem>())
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (var l in _beacon.GetComponentsInChildren<Light>()) l.enabled = false;
            foreach (var r in _beacon.GetComponentsInChildren<Renderer>())
                if (!(r is ParticleSystemRenderer)) r.enabled = false;
            Destroy(_beacon, FadeSeconds);
            _beacon = null;
        }
    }
}
