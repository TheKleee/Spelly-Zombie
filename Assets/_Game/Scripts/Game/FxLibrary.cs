using UnityEngine;

namespace SpellyZombie
{
    /// Handles to the JMO Cartoon FX prefabs (Marko's pack), wired once by
    /// the editor menu into Assets/_Game/Resources/FxLibrary.asset and loaded
    /// here at runtime. Systems ask for effects by ROLE, not by asset name —
    /// swapping the look later is a one-field change in the asset.
    public class FxLibrary : ScriptableObject
    {
        public GameObject Fire;        // looping flames — burning wood wears this
        public GameObject Explosion;   // one-shot blast
        public GameObject Poof;        // magic puff — novas, annihilations
        public GameObject ElectricHit; // lightning strike impact
        public GameObject IceHit;      // freeze impact
        public GameObject RunicAura;   // looping arcane circle (rifts, cauldron)

        static FxLibrary _instance;
        static bool _searched;

        public static FxLibrary I
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _instance = Resources.Load<FxLibrary>("FxLibrary");
                }
                return _instance;
            }
        }

        /// Spawn an effect (null-safe). CFXR one-shots clean themselves up;
        /// pass a life for loops or as a safety net.
        public static GameObject Spawn(GameObject prefab, Vector3 pos, Transform parent = null, float life = 0f)
        {
            if (prefab == null) return null;
            var fx = Instantiate(prefab, pos, Quaternion.identity, parent);
            if (life > 0f) Destroy(fx, life);
            return fx;
        }
    }
}
