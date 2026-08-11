using System.Collections.Generic;
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
                        Debug.LogWarning("[SpellyZombie] No FxLibrary asset. Run 'Spelly Zombie → Art/7 - Wire FX Library (JMO)'");
                    else if (_instance.IceHit == null || _instance.HitSpark == null || _instance.TextPow == null)
                        Debug.LogWarning("[SpellyZombie] FxLibrary has EMPTY roles (effects will be invisible). Re-run 'Spelly Zombie → Art/7 - Wire FX Library (JMO)'");
                    // pay the shader-compile cost ONCE, here, not mid-fight
                    if (_instance != null) _instance.Prewarm();
                }
                return _instance;
            }
        }

        // THE FX BUDGET (Marko: "chain effects overly pop up and lag the game") — past 8/frame the extras drop
        static int _frame, _spawnedThisFrame;
        const int MaxPerFrame = 8;

        // ===================================================== THE POOL ====
        // FX LAG root cause (Marko: "our effects lag the game…"): Instantiate/Destroy per effect — pooled instead, built once and reused
        static readonly Dictionary<GameObject, Stack<GameObject>> _pool
            = new Dictionary<GameObject, Stack<GameObject>>();
        static readonly Dictionary<GameObject, GameObject> _origin
            = new Dictionary<GameObject, GameObject>();

        /// Spawn an effect (null-safe, frame-budgeted, POOLED).
        /// Spawn, then dress every particle system in the runtime color: the
        /// ink splash wears the ink (Marko: "make the splash be the color of
        /// the ink"). Pooled instances get re-dressed each spawn, so a green
        /// splash never haunts a black pot.
        public static GameObject SpawnTinted(GameObject prefab, Vector3 pos, Color c)
        {
            var go = Spawn(prefab, pos);
            if (go != null)
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.startColor = c;
                }
            return go;
        }

        public static GameObject Spawn(GameObject prefab, Vector3 pos, Transform parent = null, float life = 0f)
        {
            if (prefab == null) return null;
            if (Time.frameCount != _frame) { _frame = Time.frameCount; _spawnedThisFrame = 0; }
            if (++_spawnedThisFrame > MaxPerFrame) return null; // the budget holds

            if (!_pool.TryGetValue(prefab, out var stack))
                _pool[prefab] = stack = new Stack<GameObject>();

            GameObject fx = null;
            FxReturn keeper;
            while (stack.Count > 0 && fx == null) fx = stack.Pop(); // skip any destroyed
            if (fx == null)
            {
                fx = Instantiate(prefab, pos, Quaternion.identity, parent);
                _origin[fx] = prefab;
                keeper = fx.AddComponent<FxReturn>();
                keeper.Systems = fx.GetComponentsInChildren<ParticleSystem>(true); // cached ONCE — reuse spawns stay alloc-free
                // a pooled effect must NOT delete itself, or the pool hands out corpses
                foreach (var ps in keeper.Systems)
                {
                    var main = ps.main;
                    main.stopAction = ParticleSystemStopAction.None;
                }
            }
            else
            {
                fx.transform.SetParent(parent, false);
                fx.transform.position = pos;
                fx.transform.rotation = Quaternion.identity;
                fx.transform.localScale = prefab.transform.localScale; // callers rescale
                fx.SetActive(true);
                keeper = fx.GetComponent<FxReturn>();
                if (keeper == null) keeper = fx.AddComponent<FxReturn>();
                if (keeper.Systems == null)
                    keeper.Systems = fx.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in keeper.Systems)
                {
                    if (ps == null) continue;
                    ps.Clear(true);
                    ps.Play(true);
                }
            }

            keeper.Arm(life > 0f ? life : 3f);   // everything comes home
            return fx;
        }

        /// Put a finished effect back on the shelf instead of destroying it.
        public static void Recycle(GameObject fx)
        {
            if (fx == null) return;
            if (!_origin.TryGetValue(fx, out var prefab)) { Destroy(fx); return; }
            fx.SetActive(false);
            fx.transform.SetParent(null, false);
            if (!_pool.TryGetValue(prefab, out var stack))
                _pool[prefab] = stack = new Stack<GameObject>();
            if (stack.Count < 12) stack.Push(fx); else Destroy(fx);
        }

        /// Build effects at load — first spawn compiles shader variants, and mid-fight that's the hitch you can feel.
        public void Prewarm(int each = 2)
        {
            foreach (var f in typeof(FxLibrary).GetFields())
            {
                if (f.FieldType != typeof(GameObject)) continue;
                var prefab = f.GetValue(this) as GameObject;
                if (prefab == null) continue;
                for (int i = 0; i < each; i++)
                {
                    var fx = Spawn(prefab, new Vector3(0f, -999f, 0f), null, 0.01f);
                    if (fx != null) Recycle(fx);
                }
            }
        }
    }

    /// Hands a pooled effect back when its time is up (no Destroy, no garbage).
    public class FxReturn : MonoBehaviour
    {
        /// The instance's particle systems, cached at build (spares a GetComponentsInChildren per pooled spawn).
        public ParticleSystem[] Systems;

        float _due;
        public void Arm(float life) { _due = Time.time + life; enabled = true; }
        void Update()
        {
            if (Time.time < _due) return;
            enabled = false;
            FxLibrary.Recycle(gameObject);
        }
    }
}
