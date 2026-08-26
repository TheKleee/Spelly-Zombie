using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Handles to the JMO Cartoon FX prefabs, wired by the editor menu into
    /// Assets/_Game/Resources/FxLibrary.asset. Systems ask for effects by role,
    /// not asset name - swapping the look is a one-field change in the asset.
    public class FxLibrary : ScriptableObject
    {
        public GameObject Fire;        // looping flames - burning wood wears this
        public GameObject Explosion;   // one-shot blast
        public GameObject Poof;        // magic puff - novas, annihilations
        public GameObject ElectricHit; // lightning strike impact
        public GameObject IceHit;      // freeze impact - frost delivery, snow bursts
        public GameObject RunicAura;   // looping arcane circle (rifts, cauldron, healing ring)

        // ---- the full mapping ----
        public GameObject Sun;         // Plasma lvl3 - literally a small sun
        public GameObject FireBurst;   // Spark lvl3 flame burst
        public GameObject HealShine;   // healing area sparkle loop
        public GameObject SoulsOut;    // black hole pull · player death (the soul leaves)
        public GameObject Flash;       // white hole ignition
        public GameObject Stars;       // white hole falling stars
        public GameObject TimeDome;    // time zone - calm sparkle dome
        public GameObject Scuffle;     // inertia field - the cartoon dust-up
        public GameObject WindTrails;  // tornado
        public GameObject Ripples;     // whirlpool
        public GameObject Splash;      // liquid blob impact
        public GameObject Smoke;       // steam cloud
        public GameObject GasCloud;    // flammable gas — green reads "don't ignite"
        public GameObject Shield;      // barrier wrap loop

        [Header("YOUR OWN, BY NAME")]
        [Tooltip("Any effect a spell row can ask for by name. THE PREFAB'S NAME IS THE KEY - " +
                 "a row with Fx \"Sparks\" finds the prefab called Sparks. Nothing here is " +
                 "referenced by code, which is the point: it is how a Workshop spell brings " +
                 "its own effects without one.")]
        public GameObject[] Named;
        public GameObject DemonBoom;   // the Demon arrives
        public GameObject SkullHead;   // rides the fresh Demon
        public GameObject BrokenHeart; // floats over a downed body
        public GameObject GroundHit;   // thrown-thing landing
        public GameObject TextBoom;    // comic _BOOM_ on explosions
        public GameObject TextBoing;   // comic _BOING_ on glue-stick
        public GameObject TextWow;     // comic _WOW_ from scared zombies
        public GameObject TextFrozen;  // comic _FROZEN_ in the snow field
        public GameObject TextPow;     // comic _POW_ - momentum hits land
        public GameObject TextWham;    // comic _WHAM_ - crushed outright
        public GameObject HitSpark;    // heat mote impact
        public GameObject HitLight;    // light mote impact
        public GameObject HitVector;   // arrow/Y slamming home
        public GameObject HitThud;     // rock-on-rock, dense thumps
        public GameObject Blood;       // wound drips - the walking HP readout

        /// The CFXR effect that rides a grammar field, by field class name.
        /// Null = that field keeps its code look. the FX_<FieldClass>
        /// override in Resources/Custom always wins over this.
        public GameObject FieldFor(string fieldClass)
        {
            // poison keeps its own CFXR cloud path; everything else that used
            // to live here died with the old combination fields
            return null;
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
                    // unwired roles spawn nothing - warn once
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

        // FX budget: past 8/frame the extras drop
        static int _frame, _spawnedThisFrame;
        const int MaxPerFrame = 8;

        // ===================================================== THE POOL ====
        // pooled: built once and reused instead of Instantiate/Destroy per effect
        static readonly Dictionary<GameObject, Stack<GameObject>> _pool
            = new Dictionary<GameObject, Stack<GameObject>>();
        static readonly Dictionary<GameObject, GameObject> _origin
            = new Dictionary<GameObject, GameObject>();

        /// Spawn an effect (null-safe, frame-budgeted, pooled), tinting every
        /// particle system; pooled instances are re-dressed each spawn.
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

        /// ★ AN EFFECT BY NAME. The typed fields above are the ones code
        /// asks for directly; this is the open list, so a spell row can name an
        /// effect that nothing in the engine has ever heard of.
        ///
        /// Falls back to the typed fields, so "Fire" and "Splash" work without
        /// being duplicated into the list.
        public static GameObject Named_(string name)
        {
            if (I == null || string.IsNullOrEmpty(name)) return null;
            if (I.Named != null)
                foreach (var go in I.Named)
                    if (go != null && string.Equals(go.name, name,
                            System.StringComparison.OrdinalIgnoreCase))
                        return go;

            // the built-in ones answer to their field names too
            var f = typeof(FxLibrary).GetField(name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return f != null && f.FieldType == typeof(GameObject) ? f.GetValue(I) as GameObject : null;
        }

        /// Spawn one by name; nothing happens if no such effect is authored.
        public static GameObject SpawnNamed(string name, Vector3 pos, Transform parent = null, float life = 0f)
        {
            var prefab = Named_(name);
            return prefab == null ? null : Spawn(prefab, pos, parent, life);
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
                keeper.Systems = fx.GetComponentsInChildren<ParticleSystem>(true); // cached ONCE - reuse spawns stay alloc-free
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

            keeper.Arm(life > 0f ? life : 3f);   // everything returns to the pool
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

        /// Build effects at load - first spawn compiles shader variants, and mid-fight that's the hitch you can feel.
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
