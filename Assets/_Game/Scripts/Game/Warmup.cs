using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ READY BEFORE THE EGG OPENS (his call, Sep 18): while the travel egg is
    /// closed after a scene loads, every effect and the golem's and zombie's
    /// materials are drawn once by a camera nobody sees, far below the world,
    /// so the driver compiles their shaders now and not at the first meteor of
    /// a fight. The recorded variants in FxLibrary > Warm Shaders compile once
    /// a session on top.
    public class Warmup : MonoBehaviour
    {
        const int Layer = 28;           // a layer nothing else draws
        const float Spacing = 3f;
        static readonly Vector3 At = new Vector3(0f, -3000f, 0f);
        static Warmup _running;
        static bool _variantsWarm, _saidNoVariants;

        public static void Run()
        {
            if (_running != null) return;
            var go = new GameObject("~Warmup");
            DontDestroyOnLoad(go);
            _running = go.AddComponent<Warmup>();
        }

        IEnumerator Start()
        {
            WarmVariants();
            var looks = new List<GameObject>();
            var lib = FxLibrary.I;
            if (lib != null)
                foreach (var prefab in lib.AllPrefabs())
                    looks.Add(Instantiate(prefab));
            foreach (var mat in CreatureMaterials())
                looks.Add(Plate(mat));

            int side = Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, looks.Count)));
            for (int i = 0; i < looks.Count; i++)
            {
                var go = looks[i];
                if (go == null) continue;
                go.transform.position = At + new Vector3(i % side, i / side, 0f) * Spacing;
                SetLayer(go.transform);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Simulate(0.25f, true, true); // particles to draw, then held
            }

            var cam = new GameObject("~WarmupCam").AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = side * Spacing * 0.6f + 2f;
            cam.transform.SetPositionAndRotation(At + new Vector3(side - 1, side - 1, 0f) * (Spacing * 0.5f)
                + Vector3.back * 20f, Quaternion.identity);
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            var rt = new RenderTexture(128, 128, 24);
            cam.targetTexture = rt;

            yield return null; // drawn: the shaders compile here
            yield return null;

            cam.targetTexture = null;
            rt.Release();
            Destroy(rt);
            Destroy(cam.gameObject);
            foreach (var go in looks) if (go != null) Destroy(go);
            _running = null;
            Destroy(gameObject);
        }

        static void WarmVariants()
        {
            if (_variantsWarm) return;
            var set = FxLibrary.I != null ? FxLibrary.I.WarmShaders : null;
            if (set == null)
            {
                if (!_saidNoVariants)
                {
                    _saidNoVariants = true;
                    Debug.Log("[SpellyZombie] FxLibrary 'Warm Shaders' is empty: after a play session, save the tracked " +
                              "variants in Project Settings > Graphics > Shader Loading and drop the asset in.");
                }
                return;
            }
            set.WarmUp();
            _variantsWarm = true;
        }

        /// The creatures' own looks (no scripts woken): each material once on a plate.
        static IEnumerable<Material> CreatureMaterials()
        {
            if (CollectionManager.I == null) yield break; // the menu has no bodies
            var seen = new HashSet<Material>();
            foreach (var body in new[] { CollectionManager.Golem, CollectionManager.ZombieBody })
            {
                if (body == null) continue;
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && seen.Add(m)) yield return m;
            }
        }

        static GameObject Plate(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i));
        }
    }
}
