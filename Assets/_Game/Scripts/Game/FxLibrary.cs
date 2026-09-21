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
        public GameObject PotBeacon;   // a pillar of light over the pot holding the ink until someone reaches it (YOUR pick; empty = none)
        public GameObject SoulsOut;    // black hole pull · player death (the soul leaves)
        public GameObject AbsorbBurst; // an object gives its knowledge away (assign YOUR pick; empty = chime only)
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

        [Header("WARM UP")]
        [Tooltip("Optional. Shader variants recorded in play (Project Settings > Graphics > Shader Loading > " +
                 "Save to asset, after a session that saw the fights). They compile behind the first " +
                 "travel egg instead of mid-fight.")]
        public ShaderVariantCollection WarmShaders;

        [Header("SOUNDS")]
        [Tooltip("The Audio Library asset (Assets/_Game/Sound/AudioLibrary): every sound of the game, one slot each. " +
                 "Empty = the game plays its synthesized placeholders.")]
        public AudioLibrary Sounds;

        /// Every effect this library can spawn: the typed roles and the named ones.
        public IEnumerable<GameObject> AllPrefabs()
        {
            foreach (var f in typeof(FxLibrary).GetFields())
                if (f.FieldType == typeof(GameObject) && f.GetValue(this) is GameObject go && go != null)
                    yield return go;
            if (Named != null)
                foreach (var go in Named)
                    if (go != null) yield return go;
        }

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
                    if (_instance != null && _instance.Sounds == null)
                        Debug.LogError("[SpellyZombie] FxLibrary: the 'Sounds' slot is empty. Drop in the Audio Library " +
                                       "asset (Assets/_Game/Sound/AudioLibrary), or every sound stays a placeholder.");
                    else if (_instance != null)
                    {
                        var empty = _instance.Sounds.Missing();
                        if (empty.Count > 0)
                            Debug.LogWarning($"[SpellyZombie] AudioLibrary: {empty.Count} empty slots play their placeholder " +
                                             $"or nothing: {string.Join(", ", empty)}");
                    }
                    // pay the shader-compile cost ONCE, here, not mid-fight
                    if (_instance != null) _instance.Prewarm();
                }
                return _instance;
            }
        }

        // FX budget: past 8/frame the extras drop
        static int _frame, _spawnedThisFrame;
        const int MaxPerFrame = 8;

        // ================================================== THE WIRE IDS ====
        // Prefab kinds: the typed roles in field order, then the Named list -
        // the same asset on every machine, so an index names a prefab without
        // sending its name. Code-built looks and sounds take fixed ids above.
        public const byte FxPuff = 200, FxFireBloom = 201, FxFireCone = 202, FxBolt = 203,
            FxLantern = 204, FxGlint = 205, FxCometDown = 206;
        public const byte SndBoom = 220, SndPop = 221, SndWhoosh = 222, SndCrackle = 223,
            SndThud = 224, SndChime = 225, SndSting = 226, SndDrum = 227, SndWhistle = 228;
        /// His clips: SndClips + the Sfx id, for the world sounds (the first AudioLibrary.WorldCount).
        public const byte SndClips = 229;
        public const byte FxNone = 255;

        static List<GameObject> _wire;
        static Dictionary<GameObject, byte> _wireId;

        static void BuildWire()
        {
            _wire = new List<GameObject>();
            _wireId = new Dictionary<GameObject, byte>();
            if (I == null) return;
            foreach (var f in typeof(FxLibrary).GetFields())
            {
                if (f.FieldType != typeof(GameObject)) continue;
                Add(f.GetValue(I) as GameObject);
            }
            if (I.Named != null) foreach (var go in I.Named) Add(go);
        }

        static void Add(GameObject go)
        {
            if (go == null || _wire.Count >= 190) return;
            if (_wireId.ContainsKey(go)) return;
            _wireId[go] = (byte)_wire.Count;
            _wire.Add(go);
        }

        /// The wire id of a library prefab, FxNone for anything else.
        public static byte IdOf(GameObject prefab)
        {
            if (prefab == null) return FxNone;
            if (_wire == null) BuildWire();
            return _wireId.TryGetValue(prefab, out var id) ? id : FxNone;
        }

        public static GameObject PrefabAt(byte id)
        {
            if (_wire == null) BuildWire();
            return id < _wire.Count ? _wire[id] : null;
        }

        public static byte SoundId(string clip) => clip switch
        {
            "SZ_boom" => SndBoom, "SZ_pop" => SndPop, "SZ_whoosh" => SndWhoosh,
            "SZ_crackle" => SndCrackle, "SZ_thud" => SndThud, "SZ_chime" => SndChime,
            "SZ_sting" => SndSting, "SZ_drum" => SndDrum, "SZ_whistle" => SndWhistle,
            _ => FxNone,
        };

        // ===================================================== THE POOL ====
        // pooled: built once and reused instead of Instantiate/Destroy per effect
        static readonly Dictionary<GameObject, Stack<GameObject>> _pool
            = new Dictionary<GameObject, Stack<GameObject>>();
        static readonly Dictionary<GameObject, GameObject> _origin
            = new Dictionary<GameObject, GameObject>();

        /// Spawn an effect (null-safe, frame-budgeted, pooled), tinting every
        /// particle system; pooled instances are re-dressed each spawn.
        public static GameObject SpawnTinted(GameObject prefab, Vector3 pos, Color c,
            Transform parent = null, float life = 0f, bool relay = true)
        {
            var go = Spawn(prefab, pos, parent, life, relay);
            if (go != null)
            {
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.startColor = c;
                }
                var keeper = go.GetComponent<FxReturn>();
                if (keeper != null) { keeper.Tinted = true; keeper.Tint = c; }
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

        /// relay: the host ships it to the clients (FxMsg). False at sites the
        /// clients already replay on their own.
        public static GameObject Spawn(GameObject prefab, Vector3 pos, Transform parent = null, float life = 0f,
            bool relay = true)
        {
            if (prefab == null) return null;
            if (Time.frameCount != _frame) { _frame = Time.frameCount; _spawnedThisFrame = 0; }
            if (!_warming && ++_spawnedThisFrame > MaxPerFrame) return null; // the budget holds (the warm-up is exempt)
            // a comic WHAM or POW is heard as well as read, on every machine that shows it
            if (!_warming && I != null && (prefab == I.TextWham || prefab == I.TextPow))
                Juice.Sound(Sfx.Wham, pos, 1f, Random.Range(0.92f, 1.08f), false);

            if (!_pool.TryGetValue(prefab, out var stack))
                _pool[prefab] = stack = new Stack<GameObject>();

            GameObject fx = null;
            FxReturn keeper;
            while (stack.Count > 0 && fx == null) fx = stack.Pop(); // skip any destroyed
            if (fx == null)
            {
                using (PerfMarkers.NewEffect.Auto()) fx = Instantiate(prefab, pos, Quaternion.identity, parent);
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
            keeper.Prefab = prefab;
            keeper.Tinted = false;
            // shipped next frame, once the caller has sized it
            keeper.Relay = relay && NetSync.WantsFxRelay;
            return fx;
        }

        /// Put a finished effect back on the shelf instead of destroying it.
        public static void Recycle(GameObject fx)
        {
            if (fx == null) return;
            if (!_origin.TryGetValue(fx, out var prefab)) { Destroy(fx); return; }
            fx.SetActive(false);
            fx.transform.SetParent(null, false);
            DontDestroyOnLoad(fx); // the shelf outlives the scene: stocked once a session
            if (!_pool.TryGetValue(prefab, out var stack))
                _pool[prefab] = stack = new Stack<GameObject>();
            if (stack.Count < 12) stack.Push(fx);
            else { _origin.Remove(fx); Destroy(fx); }
        }

        /// An effect gone for good (destroyed with its parent) leaves the origin map.
        public static void Forget(GameObject fx) => _origin.Remove(fx);

        /// Build effects at load - first spawn compiles shader variants, and mid-fight that's the hitch you can feel.
        public void Prewarm(int each = 2)
        {
            _warming = true; // every effect, not just the first frame's worth
            try
            {
                foreach (var prefab in AllPrefabs())
                {
                    // all of one kind out before any goes back, or the shelf hands the same one out again
                    _stock.Clear();
                    for (int i = 0; i < each; i++)
                    {
                        var fx = Spawn(prefab, new Vector3(0f, -999f, 0f), null, 0.01f, false);
                        if (fx != null) _stock.Add(fx);
                    }
                    foreach (var fx in _stock) Recycle(fx);
                }
            }
            finally { _warming = false; }
        }

        static bool _warming;
        static readonly List<GameObject> _stock = new List<GameObject>();
    }

    /// Hands a pooled effect back when its time is up (no Destroy, no garbage).
    public class FxReturn : MonoBehaviour
    {
        /// The instance's particle systems, cached at build (spares a GetComponentsInChildren per pooled spawn).
        public ParticleSystem[] Systems;

        // the relay waits a frame so the caller's scale and tint ride along
        [System.NonSerialized] public GameObject Prefab;
        [System.NonSerialized] public bool Relay, Tinted;
        [System.NonSerialized] public Color Tint;

        float _due;
        int _armedFrame;
        public void Arm(float life) { _due = Time.time + life; _armedFrame = Time.frameCount; enabled = true; }
        void OnDestroy() => FxLibrary.Forget(gameObject);
        void Update()
        {
            if (Relay && Time.frameCount > _armedFrame)
            {
                Relay = false;
                var el = transform.parent != null ? transform.parent.GetComponentInParent<Element>() : null;
                NetSync.PushFx(FxLibrary.IdOf(Prefab), transform.position, transform.localScale,
                    Tinted ? Tint : Color.white, el != null ? el.NetId : 0, _due - Time.time,
                    (byte)(Tinted ? 1 : 0));
            }
            if (Time.time < _due) return;
            enabled = false;
            FxLibrary.Recycle(gameObject);
        }
    }
}
