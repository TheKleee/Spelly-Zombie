using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// The travel egg: during scene loads the local player stands frozen in a
    /// dark shell around the camera; only egg-layer objects render, a few
    /// spell motes float around. When the match opens the shell grows from
    /// the eye outward - the world comes back near to far - and the far shell
    /// burns away. Purely local, never replicated.
    public class LoadEgg : MonoBehaviour
    {
        const float ShellRadius = 2.4f;  // the closed egg at most, around the camera
        const float FloorGap = 0.3f;     // the closed egg's bottom stays this far above the floor
        const float MinRadius = 0.6f;
        const float FarRadius = 70f;     // where the opening shell burns out
        const int EggLayer = 31;
        const float RevealSeconds = 2.2f;
        const float FormSeconds = 1.1f;  // the shell closes in from the horizon
        const float MusicDuck = 0.15f;   // the music under a closed egg

        static LoadEgg _live;

        /// Shell up and the camera seeing only the egg: the veil may drop.
        public static bool Closed => _live != null && _live._cam != null && _live._opening < 0f && !_live.Forming;

        /// A trip is underway: the shell forming or closed, not yet breaking open.
        public static bool Leaving => _live != null && _live._opening < 0f;

        /// An egg exists at all, breaking open included.
        public static bool Active => _live != null;

        float _form = -1f;               // 0..1 while the shell closes in
        bool Forming => _form >= 0f && _form < 1f;
        System.Action _load;             // where the forming shell goes when it closes
        bool _loading;                   // the next scene is asked for and not here yet

        /// What the music should sit at: full with no egg, down as the shell
        /// forms, low while it is closed, back up as it breaks apart.
        public static float MusicLevel
        {
            get
            {
                if (_live == null) return 1f;
                if (_live.Forming) return Mathf.Lerp(1f, MusicDuck, _live._form);
                if (_live._opening < 0f) return MusicDuck;
                return Mathf.Lerp(MusicDuck, 1f, Mathf.Clamp01(_live._opening));
            }
        }

        /// The trip, in order: the shell forms around the eye with the world
        /// still in view, then the loading text rides its dark and the scene
        /// loads, then the new scene breaks it open.
        public static void Travel(string scene) => Trip(() => SceneManager.LoadScene(scene));
        public static void Travel(int buildIndex) => Trip(() => SceneManager.LoadScene(buildIndex));

        /// A trip that does something first as the scene changes (once the shell has closed, or at
        /// once without one): a walk-out leaves the session there, so the world stays in play meanwhile.
        public static void Travel(string scene, System.Action first)
        {
            if (!Trip(() => { first(); SceneManager.LoadScene(scene); })) first();
        }

        /// The host has set out for another scene: this shell closes with the host's and waits,
        /// held, until the host's arrival carries it on (the phase follow's Travel). CallOff or the
        /// safety net opens it here again.
        public static void Hold() => Trip(null);

        /// The trip is off before it left this scene (the host went away): a shell closing in or
        /// held here opens again where it stands. An egg that has landed opens on its own.
        public static void CallOff()
        {
            if (_live == null || _live._loading || _live._arrivedAt >= 0f || _live._opening >= 0f) return;
            _live.StopAllCoroutines();
            _live._form = -1f;
            _live._load = null;
            _live.Reveal();
        }

        /// False when another trip already has the scene change and this one is dropped.
        static bool Trip(System.Action load)
        {
            if (_live != null && _live.Forming)
            {
                if (_live._load != null) return false; // a trip keeps its own destination
                _live._load = load;                    // a held shell takes the host's
                return true;
            }
            if (_live != null && _live._loading) return false; // the next scene is asked for: one load per trip
            if (_live != null && _live._opening >= 0f)
            {
                // still breaking open from the last trip: that egg is done, this trip forms its own
                var old = _live;
                _live = null;
                old._pilot = null; // its OnDestroy must not unstick the feet the new shell sticks
                if (old._shell != null) old._shell.gameObject.SetActive(false); // gone before the new one forms
                Destroy(old.gameObject);
            }
            if (_live != null)
            {
                // closed around the camera already (a dark room, or held): it rides on and opens there, never here
                _live._arrivedAt = -1f;
                if (load == null) return true;
                _live._loading = true;
                LoadingHints.Show();
                Debug.Log("[SpellyZombie] egg already closed, loading");
                load();
                return true;
            }
            var cam = Camera.main;
            if (cam != null && Shader.Find("SpellyZombie/EggDissolve") != null)
            {
                var go = new GameObject("LoadEgg");
                DontDestroyOnLoad(go);
                _live = go.AddComponent<LoadEgg>();
                // a lobby ghost stands up for the match; a ghost leaving a map rides home as it is
                if (SceneManager.GetActiveScene().name == "Lobby") GhostState.ReviveLocalNow();
                Juice.Sound2D(Sfx.Egg);
                _live._load = load;
                _live.StartCoroutine(_live.FormThen(cam));
                return true;
            }
            if (load == null) return false; // no shell to hold
            Debug.Log($"[SpellyZombie] no egg for this trip: {(cam == null ? "no main camera" : "no egg shader")}");
            Cover();
            if (_live != null) _live._loading = true;
            LoadingHints.Show();
            load();
            return true;
        }

        IEnumerator FormThen(Camera cam)
        {
            _cam = cam;
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) { _pilot = p; break; }
            if (_pilot != null) _pilot.StickFeet(600f);
            EnsureShell();
            _form = 0f;
            _radius = FarRadius;
            while (true)
            {
                _form = Mathf.Min(1f, _form + Time.unscaledDeltaTime / FormSeconds);
                float e = 1f - (1f - _form) * (1f - _form); // fast from afar, soft landing
                _radius = Mathf.Lerp(FarRadius, ClosedRadius(), e);
                if (_mat != null) _mat.SetFloat("_Cut", Mathf.Clamp01(1f - _form / 0.35f)); // embers knit into a shell
                if (_form >= 1f) break; // closed: the scene is asked for in this same frame
                yield return null;
            }
            Attach(); // closed: the world masked away, the body wrapped
            LoadingHints.Show();
            Debug.Log($"[SpellyZombie] egg closed in {SceneManager.GetActiveScene().name}" + (_load == null ? ", held for the host" : ", loading"));
            if (_load == null) yield break; // held: the host's arrival carries it on
            _loading = true;
            yield return null; // one frame shown with the hint: the load freezes the picture on screen
            _load();
        }

        /// Closed, the egg stops short of the floor under the eye, so no ground shows
        /// inside it. Third person stands back, so there it still wraps the body.
        float ClosedRadius()
        {
            if (_cam == null) return ShellRadius;
            Vector3 eye = _cam.transform.position;
            if (_pilot != null && SimpleFPSController.ThirdPersonActive)
            {
                Vector3 head = _pilot.transform.position + Vector3.up * 1.6f;
                return Mathf.Max(ShellRadius, (head - eye).magnitude + 0.9f);
            }
            // NOBODY RIDES INSIDE (the menu, the Map Creator's arrival): the
            // shell closes past the nearest thing around the eye in any
            // direction, or the menu cauldron stands inside it in plain sight
            if (_pilot == null)
            {
                float room = ShellRadius + FloorGap;
                int near = Physics.OverlapSphereNonAlloc(eye, room, _around,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < near; i++)
                {
                    var c = _around[i];
                    if (c == null) continue;
                    // terrain and hollow meshes refuse the question and log a warning for it, every frame;
                    // Unity then answers with the eye itself, so they have always counted as touching
                    if (c is TerrainCollider || (c is MeshCollider mesh && !mesh.convex)) { room = 0f; break; }
                    room = Mathf.Min(room, Vector3.Distance(eye, c.ClosestPoint(eye)));
                }
                return Mathf.Clamp(room - FloorGap, MinRadius, ShellRadius);
            }
            float floor = ShellRadius + FloorGap;
            int n = Physics.RaycastNonAlloc(eye, Vector3.down, _floorHits, floor, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_pilot != null && _floorHits[i].collider.transform.IsChildOf(_pilot.transform)) continue; // the body rides inside
                floor = Mathf.Min(floor, _floorHits[i].distance);
            }
            return Mathf.Clamp(floor - FloorGap, MinRadius, ShellRadius);
        }

        readonly RaycastHit[] _floorHits = new RaycastHit[8];
        readonly Collider[] _around = new Collider[16];
        bool _bodiless; // no body arrives (the menu, the Map Creator): the egg closes around its camera

        Transform _shell;
        Material _mat;
        Camera _cam;
        int _prevMask;
        bool _maskTaken;
        SimpleFPSController _pilot;
        readonly List<Transform> _relayered = new List<Transform>();
        readonly List<int> _prevLayers = new List<int>();
        readonly List<SpellParticle> _toys = new List<SpellParticle>();
        float _toyIn, _relayerIn, _opening = -1f;
        float _radius = ShellRadius, _openFrom = ShellRadius;
        float _arrivedAt = -1f, _bornAt;
        float _dark; // seconds of dark room the body has stood through at its start here

        public static void Cover()
        {
            if (_live != null) return;
            if (Shader.Find("SpellyZombie/EggDissolve") == null)
            {
                Debug.LogError("[SpellyZombie] SZEggDissolve.shader missing or failed to compile.");
                return;
            }
            var go = new GameObject("LoadEgg");
            DontDestroyOnLoad(go);
            _live = go.AddComponent<LoadEgg>();
            // a lobby ghost stands up for the match; a ghost leaving a map rides home as it is
            if (SceneManager.GetActiveScene().name == "Lobby") GhostState.ReviveLocalNow();
            _live.Attach();
        }

        // The trip belongs to the egg: what is on the screen (buttons, chips, meters) steps aside
        // from the moment the shell starts closing in until it breaks open again. The loading text
        // is the egg's own and stays (LoadingHints draws on a canvas of its own).
        static void ScreenStepsAside(bool aside)
        {
            StepAside(UIKit.Root, aside);
            StepAside(UIKit.FloatRoot, aside); // the floating key badges and page cards too
        }

        static void StepAside(RectTransform root, bool aside)
        {
            if (root == null) return;
            var group = root.GetComponent<CanvasGroup>();
            if (group == null) group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = aside ? 0f : 1f;
            group.blocksRaycasts = !aside;
            group.interactable = !aside;
        }

        void Awake()
        {
            GameMenu.CloseForTrip();
            ScreenStepsAside(true);
            _bornAt = Time.unscaledTime;
            _bodiless = Bodiless(SceneManager.GetActiveScene().name);
            SceneManager.sceneLoaded += OnLoaded;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Unwrap();
            if (_shell != null) Destroy(_shell.gameObject);
            if (_live == this) { _live = null; ScreenStepsAside(false); }
        }

        void OnLoaded(Scene s, LoadSceneMode m)
        {
            _loading = false;
            _arrivedAt = Time.unscaledTime;
            _bornAt = Time.unscaledTime; // the safety net counts from each arrival: a joiner's egg rides two loads
            _dark = 0f;
            _bodiless = Bodiless(s.name);
            Debug.Log($"[SpellyZombie] egg landed in {s.name}");
            Unwrap();  // the old scene's player is gone
            Attach();  // take the new one
            Warmup.Run(); // what the fight will draw compiles now, behind the closed shell
        }

        /// Scenes nobody's body arrives in: the menu, and the map the creator opens.
        static bool Bodiless(string scene) => scene == "Menu" || MapCreator.Active || PhotoBooth.Active;

        void Attach()
        {
            _pilot = null;
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) { _pilot = p; break; }
            if (_pilot == null && !_bodiless) return;

            if (_pilot != null) _pilot.StickFeet(600f); // stuck in place until the match opens

            EnsureShell();

            Wrap();
            _cam = Camera.main;
            if (_cam != null)
            {
                if (!_maskTaken)
                {
                    _prevMask = _cam.cullingMask;
                    _maskTaken = true;
                }
                _cam.cullingMask = 1 << EggLayer;
                FollowCamera();
            }
        }

        /// The shell is centred on the camera, set right before the camera
        /// renders so it never lags the eye. Closed, it is a tight egg - but
        /// never smaller than the body needs (third person stands back).
        void EnsureShell()
        {
            if (_shell == null)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "EggShell";
                Destroy(sphere.GetComponent<Collider>());
                _shell = sphere.transform;
                _shell.SetParent(transform, false); // rides the egg object across loads
                _mat = new Material(Shader.Find("SpellyZombie/EggDissolve"));
                sphere.GetComponent<Renderer>().sharedMaterial = _mat;
            }
            _shell.gameObject.layer = EggLayer;
        }

        void FollowCamera()
        {
            if (_shell == null || _cam == null) return;
            _shell.position = _cam.transform.position;
            if (_opening < 0f && !Forming && (_pilot != null || _bodiless)) _radius = ClosedRadius();
            _shell.localScale = Vector3.one * (_radius * 2f);
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == _cam) FollowCamera();
        }

        /// The player and the toys live on the egg's layer while the shell is
        /// closed; the camera renders only that layer, so the world - and
        /// anything physically inside the shell - simply does not exist yet.
        void Wrap()
        {
            if (_pilot != null)
                foreach (var t in _pilot.GetComponentsInChildren<Transform>(true))
                {
                    if (_relayered.Contains(t)) continue;
                    _relayered.Add(t);
                    _prevLayers.Add(t.gameObject.layer);
                    t.gameObject.layer = EggLayer;
                }
            foreach (var toy in _toys)
                if (toy != null)
                    foreach (var t in toy.GetComponentsInChildren<Transform>(true))
                        t.gameObject.layer = EggLayer;
        }

        void Unwrap()
        {
            for (int i = 0; i < _relayered.Count; i++)
                if (_relayered[i] != null)
                    _relayered[i].gameObject.layer = _prevLayers[i];
            _relayered.Clear();
            _prevLayers.Clear();
            if (_cam != null && _maskTaken) _cam.cullingMask = _prevMask;
            _maskTaken = false;
            if (_pilot != null) _pilot.UnstickFeet();
            foreach (var toy in _toys)
                if (toy != null) Destroy(toy.gameObject);
            _toys.Clear();
        }

        void TickToys()
        {
            if (_pilot == null) return;
            Vector3 eye = _cam != null ? _cam.transform.position
                : _pilot.transform.position + Vector3.up * 1.4f;
            // a toy that drifted out through the shell is gone; a fresh one comes in its place
            for (int i = _toys.Count - 1; i >= 0; i--)
            {
                var t = _toys[i];
                if (t != null && !t.Dead && (t.transform.position - eye).sqrMagnitude < _radius * _radius) continue;
                if (t != null) Destroy(t.gameObject);
                _toys.RemoveAt(i);
            }
            _toyIn -= Time.unscaledDeltaTime;
            if (_toys.Count >= 3 || _toyIn > 0f) return;
            _toyIn = 1.6f;
            ParticleKind kind = Random.value < 0.4f ? ParticleKind.Push
                : Random.value < 0.5f ? ParticleKind.Light : ParticleKind.Dense;
            Vector3 at = eye + Random.insideUnitSphere * (_radius * 0.55f);
            var p = SpellParticle.EmitToy(kind, at, Random.onUnitSphere, 0.7f);
            if (p == null) return;
            foreach (var t in p.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = EggLayer; // seen from its first frame
            _toys.Add(p);
        }

        void Update()
        {
            if (_opening >= 0f)
            {
                _opening += Time.unscaledDeltaTime / RevealSeconds;
                float p = Mathf.Clamp01(_opening);
                // the world comes back from the eye outward: slow at first,
                // then rushing away; the far shell burns off at the end
                _radius = Mathf.Lerp(_openFrom, FarRadius, p * p);
                if (_mat != null) _mat.SetFloat("_Cut", Mathf.Clamp01((p - 0.65f) / 0.35f));
                if (_opening >= 1f) Destroy(gameObject);
                return;
            }
            if (Forming) return; // the shell is still closing in

            if (_cam == null || (!_bodiless && (_pilot == null || !_pilot.gameObject.activeInHierarchy))) Attach();
            _relayerIn -= Time.unscaledDeltaTime;
            if (_relayerIn <= 0f)
            {
                _relayerIn = 0.5f;
                Wrap(); // toys grow FX children on later frames
            }
            TickToys();

            // a trip that never lands must not trap anyone in the dark
            if (Time.unscaledTime - _bornAt > 25f) { Reveal(); return; }
            if (_arrivedAt < 0f) return;

            // the dark room counts from the body standing at its start (a client's is the host's
            // answer), never from before the load and the island growing; a heavy frame is not
            // dark time either
            if (!_bodiless && !SpawnPlan.IsPlaced(_pilot)) return;
            _dark += Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            bool lobby = _bodiless || SceneManager.GetActiveScene().name == "Lobby";
            bool open = lobby || RoundDirector.InMatch; // a map opens on its match, live or already decided
            // the dark room registers before it burns
            if (open && _dark > 1.5f) Reveal();
        }

        /// A trip with nobody inside (the Map Creator's island): no egg to open.
        public static void Dismiss()
        {
            if (_live != null) Destroy(_live.gameObject);
        }

        /// The world comes back: layers and camera first, then the shell
        /// grows away from the eye until it burns out at the horizon.
        void Reveal()
        {
            Debug.Log("[SpellyZombie] egg opens " + (_arrivedAt >= 0f
                ? $"{Time.unscaledTime - _arrivedAt:0.0} s after landing ({_dark:0.0} s of dark room)"
                : "where it closed"));
            ScreenStepsAside(false);
            Unwrap();
            _openFrom = _radius;
            _opening = 0f;
            if (_shell != null) _shell.gameObject.layer = 0;
        }
    }
}
