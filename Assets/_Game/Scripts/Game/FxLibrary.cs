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
        public GameObject IceHit;      // freeze impact — frost delivery, snow bursts
        public GameObject RunicAura;   // looping arcane circle (rifts, cauldron, healing ring)

        // ---- the full mapping (Marko approved Jul 22: "You may map the effects") ----
        public GameObject Sun;         // Plasma lvl3 — literally a small sun
        public GameObject FireBurst;   // Spark lvl3 flame burst
        public GameObject HealShine;   // healing area sparkle loop
        public GameObject SoulsOut;    // black hole pull · player death (the soul leaves)
        public GameObject Flash;       // white hole ignition
        public GameObject Stars;       // white hole falling stars
        public GameObject TimeDome;    // time zone — calm sparkle dome
        public GameObject Scuffle;     // inertia field — the cartoon dust-up
        public GameObject WindTrails;  // tornado
        public GameObject Ripples;     // whirlpool
        public GameObject Splash;      // liquid blob impact
        public GameObject Smoke;       // steam cloud
        public GameObject GasCloud;    // flammable gas — green reads "don't ignite"
        public GameObject Shield;      // barrier wrap loop
        public GameObject DemonBoom;   // the Demon arrives
        public GameObject SkullHead;   // rides the fresh Demon
        public GameObject BrokenHeart; // floats over a DOWNED body (ally-dying gap fix)
        public GameObject GroundHit;   // thrown-thing landing
        public GameObject TextBoom;    // comic _BOOM_ on explosions
        public GameObject TextBoing;   // comic _BOING_ on glue-stick
        public GameObject TextWow;     // comic _WOW_ from scared zombies
        public GameObject TextFrozen;  // comic _FROZEN_ in the snow field
        public GameObject TextPow;     // comic _POW_ — momentum hits land
        public GameObject TextWham;    // comic _WHAM_ — crushed outright
        public GameObject HitSpark;    // heat mote impact
        public GameObject HitLight;    // light mote impact
        public GameObject HitVector;   // arrow/Y slamming home
        public GameObject HitThud;     // rock-on-rock, dense thumps
        public GameObject Blood;       // wound drips — the walking HP readout

        /// The CFXR effect that rides a grammar field, by field class name.
        /// Null = that field keeps its code look. Marko's FX_<FieldClass>
        /// override in Resources/Custom always wins over this.
        public GameObject FieldFor(string fieldClass)
        {
            switch (fieldClass)
            {
                case "SnowField": return IceHit;
                case "PlasmaField": return Sun;
                case "BlackHoleField": return SoulsOut;
                case "WhiteHoleField": return Stars;
                case "SteamCloud": return Smoke;
                case "TimeFreezeField": return TimeDome;
                case "InertiaField": return Scuffle;
                case "HealingField": return HealShine;
                default: return null; // TornadoField picks per spin in its Open
            }
        }

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
                    // silent-nothing guard: unwired roles spawn NOTHING — say so
                    if (_instance == null)
                        Debug.LogWarning("[SpellyZombie] No FxLibrary asset — run 'Spelly Zombie → Art/7 — Wire FX Library (JMO)'");
                    else if (_instance.IceHit == null || _instance.HitSpark == null || _instance.TextPow == null)
                        Debug.LogWarning("[SpellyZombie] FxLibrary has EMPTY roles (effects will be invisible) — re-run 'Spelly Zombie → Art/7 — Wire FX Library (JMO)'");
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
