using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// The travel egg: during scene loads the local player stands frozen in a
    /// dark shell; only egg-layer objects render, a few spell motes float
    /// around, then the shell dissolves. Purely local, never replicated.
    public class LoadEgg : MonoBehaviour
    {
        const float Radius = 8f;   // covers the third person camera with room
        const int EggLayer = 31;
        const float DissolveSeconds = 1.6f;

        static LoadEgg _live;

        Transform _shell;
        Material _mat;
        Camera _cam;
        int _prevMask;
        bool _maskTaken;
        SimpleFPSController _pilot;
        readonly List<Transform> _relayered = new List<Transform>();
        readonly List<int> _prevLayers = new List<int>();
        readonly List<SpellParticle> _toys = new List<SpellParticle>();
        float _toyIn, _relayerIn, _dissolve = -1f;
        float _arrivedAt = -1f, _bornAt;

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
            GhostState.ReviveLocalNow(); // a lobby ghost stands up for the trip
            _live.Attach();
        }

        void Awake()
        {
            _bornAt = Time.unscaledTime;
            SceneManager.sceneLoaded += OnLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            Unwrap();
            if (_shell != null) Destroy(_shell.gameObject);
            if (_live == this) _live = null;
        }

        void OnLoaded(Scene s, LoadSceneMode m)
        {
            _arrivedAt = Time.unscaledTime;
            Unwrap();  // the old scene's player is gone
            Attach();  // take the new one
        }

        void Attach()
        {
            _pilot = null;
            foreach (var p in SimpleFPSController.All)
                if (p != null && p.IsLocalViewer) { _pilot = p; break; }
            if (_pilot == null) return;

            _pilot.StickFeet(600f); // stuck in place until the match opens

            if (_shell == null)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "EggShell";
                Destroy(sphere.GetComponent<Collider>());
                _shell = sphere.transform;
                _mat = new Material(Shader.Find("SpellyZombie/EggDissolve"));
                sphere.GetComponent<Renderer>().sharedMaterial = _mat;
            }
            _shell.SetParent(_pilot.transform, false);
            _shell.localPosition = Vector3.up;
            _shell.localRotation = Quaternion.identity;
            _shell.localScale = Vector3.one * (Radius * 2f);
            _shell.gameObject.layer = EggLayer;

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
            }
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
            _toys.RemoveAll(t => t == null || t.Dead);
            _toyIn -= Time.unscaledDeltaTime;
            if (_toys.Count >= 3 || _toyIn > 0f || _pilot == null) return;
            _toyIn = 1.6f;
            ParticleKind kind = Random.value < 0.4f ? ParticleKind.Push
                : Random.value < 0.5f ? ParticleKind.Light : ParticleKind.Dense;
            Vector3 at = _pilot.transform.position + Vector3.up * 1.4f
                + Random.insideUnitSphere * 2f;
            var p = SpellParticle.Emit(kind, at, Random.onUnitSphere, 0.7f);
            if (p != null) _toys.Add(p);
        }

        void Update()
        {
            if (_dissolve >= 0f)
            {
                _dissolve += Time.unscaledDeltaTime / DissolveSeconds;
                if (_mat != null) _mat.SetFloat("_Cut", Mathf.Clamp01(_dissolve));
                if (_dissolve >= 1f) Destroy(gameObject);
                return;
            }

            if (_pilot == null || !_pilot.gameObject.activeInHierarchy) Attach();
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

            bool lobby = SceneManager.GetActiveScene().name == "Lobby";
            bool open = lobby
                ? Time.unscaledTime - _arrivedAt > 1.1f
                : NetGame.Connected && !NetGame.IsHost
                    ? NetSync.HasRound && NetSync.NetPhase == 1
                    : RoundDirector.RunActive;
            if (open && Time.unscaledTime - _arrivedAt > 0.8f) Reveal();
        }

        /// The world fades in: layers and camera come back first, then the
        /// shell burns away around the player.
        void Reveal()
        {
            Unwrap();
            _dissolve = 0f;
            if (_shell != null)
            {
                _shell.SetParent(null, true);
                _shell.gameObject.layer = 0;
            }
        }
    }
}
