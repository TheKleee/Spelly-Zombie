using UnityEngine;

namespace SpellyZombie
{
    /// THE STATE BLOB (Marko's sketch, Jul 22, corrected same night: "it's
    /// just a soft body... 1 single object with bones"): ONE skin — a single
    /// sphere mesh — deformed by internal BONES. The state decides what the
    /// bones do, and the skin follows:
    ///
    ///   SOLID  — bones stand still, the shape is stiff · opaque
    ///   LIQUID — bones fall with gravity, the skin slumps wide · half-seen
    ///   GAS    — bones lose weight, drift up and everywhere · barely there
    ///
    /// Rides an existing Matter (which keeps ALL chemistry: heat, reactions,
    /// wading, crushing). An FX_StateBlob prefab in Resources/Custom (his
    /// shader/art pass) replaces this look outright.
    public class StateBlob : MonoBehaviour
    {
        const int Bones = 7;
        const float SkinScale = 1.55f;
        const float StateLerpPerSec = 0.9f; // states MELT into each other, never snap

        /// The Solid+Liquid boundary state (Marko): thick sludge — the slider
        /// pins BETWEEN solid and liquid, half-slumped, mostly opaque.
        public bool Muddy;

        Matter _matter;
        Vector3[] _home;     // the stiff arrangement (solid's truth), blob-local
        Vector3[] _pos;      // where each bone is right now
        Vector3[] _wander;   // per-bone gas drift phase
        Transform _skinT;
        Mesh _mesh;
        Vector3[] _baseVerts;  // blob-local rest vertices
        Vector3[] _workVerts;  // skin-local output buffer
        float[,] _weights;     // [vertex, bone] — precomputed skinning
        Material _mat;
        float _stateT = 1f;    // 1 solid · 0.5 liquid · ~0.1 gas (continuous)

        public float StateT => _stateT;

        void Start()
        {
            _matter = GetComponent<Matter>();

            // the old look retires — the SKIN is the body now
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;

            // his art hook: FX_StateBlob replaces the code look entirely
            var skinPrefab = PrefabVault.Get("FX_StateBlob");
            if (skinPrefab != null) { Instantiate(skinPrefab, transform, false); return; }

            // ---- bones ----
            _home = new Vector3[Bones];
            _pos = new Vector3[Bones];
            _wander = new Vector3[Bones];
            for (int i = 0; i < Bones; i++)
            {
                _home[i] = i == 0 ? Vector3.zero : Random.insideUnitSphere * 0.5f;
                _pos[i] = _home[i];
                _wander[i] = new Vector3(Random.value * 10f, Random.value * 10f, Random.value * 10f);
            }

            // ---- ONE skin: a sphere mesh the bones deform ----
            var skin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            skin.name = "Skin";
            Destroy(skin.GetComponent<Collider>()); // Matter owns physics
            skin.transform.SetParent(transform, false);
            skin.transform.localScale = Vector3.one * SkinScale;
            _skinT = skin.transform;
            var mf = skin.GetComponent<MeshFilter>();
            _mesh = Instantiate(mf.sharedMesh); // private instance — safe to bash
            mf.sharedMesh = _mesh;
            var raw = _mesh.vertices;
            _baseVerts = new Vector3[raw.Length];
            _workVerts = new Vector3[raw.Length];
            for (int v = 0; v < raw.Length; v++)
                _baseVerts[v] = raw[v] * SkinScale; // into blob space

            // skin weights: each vertex belongs to the bones NEAR it — smooth
            // falloff, normalized, so the surface is one continuous body
            _weights = new float[_baseVerts.Length, Bones];
            const float sigma2 = 0.45f * 0.45f * 2f;
            for (int v = 0; v < _baseVerts.Length; v++)
            {
                float sum = 0f;
                for (int b = 0; b < Bones; b++)
                {
                    float w = Mathf.Exp(-(_baseVerts[v] - _home[b]).sqrMagnitude / sigma2);
                    _weights[v, b] = w;
                    sum += w;
                }
                if (sum < 1e-5f) sum = 1e-5f;
                for (int b = 0; b < Bones; b++) _weights[v, b] /= sum;
            }

            var baseColor = SurfaceMaterialDB.Info(
                _matter != null ? _matter.Material : SurfaceMaterialType.Stone).SolidColor;
            _mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _mat.color = baseColor;
            SetupTransparent(_mat);
            skin.GetComponent<Renderer>().sharedMaterial = _mat;
        }

        void Update()
        {
            if (_matter == null || _mesh == null) return;

            // ---- the slider chases the phase (heat melts it down the ladder,
            // compression climbs it back — Matter already derives the phase) ----
            float target = Muddy ? 0.7f // MUD sits between solid and liquid
                : _matter.Phase == MatterPhase.Solid ? 1f
                : _matter.Phase == MatterPhase.Liquid ? 0.5f : 0.1f;
            _stateT = Mathf.MoveTowards(_stateT, target, StateLerpPerSec * Time.deltaTime);

            // fluid states slump along the WORLD's down, never the body's
            // tilt (Marko's catch: a tumbled body made mud stand like a disc
            // on its side) — only true solids keep the rock's lean
            if (_skinT != null)
            {
                if (_stateT < 0.85f)
                    _skinT.rotation = Quaternion.identity;
                else
                    _skinT.localRotation = Quaternion.Slerp(_skinT.localRotation, Quaternion.identity,
                        3f * Time.deltaTime);
            }

            // ---- transparency: solid opaque · liquid half · gas barely there ----
            if (_mat != null)
            {
                var c = _mat.color;
                c.a = Mathf.Lerp(0.2f, 1f, Mathf.InverseLerp(0.1f, 1f, _stateT));
                _mat.color = c;
            }

            // ---- the bones act out the state (his sketch, literally) ----
            float dt = Time.deltaTime;
            float liquidness = 1f - Mathf.InverseLerp(0.5f, 1f, _stateT);
            float gasness = 1f - Mathf.InverseLerp(0.1f, 0.5f, _stateT);
            for (int i = 0; i < Bones; i++)
            {
                Vector3 want = _home[i];
                if (liquidness > 0.01f) // bones FALL — the skin slumps wide and low
                {
                    var slump = new Vector3(_home[i].x * 1.7f, Mathf.Min(_home[i].y, -0.1f) * 0.35f, _home[i].z * 1.7f);
                    want = Vector3.Lerp(want, slump, liquidness);
                }
                if (gasness > 0.01f) // bones lose weight — drift up, all directions
                {
                    float t = Time.time;
                    var drift = _home[i] * 1.9f + new Vector3(
                        (Mathf.PerlinNoise(_wander[i].x, t * 0.7f) - 0.5f) * 1.7f,
                        0.6f + (Mathf.PerlinNoise(_wander[i].y, t * 0.6f) - 0.3f) * 1.4f,
                        (Mathf.PerlinNoise(_wander[i].z, t * 0.7f) - 0.5f) * 1.7f);
                    want = Vector3.Lerp(want, drift, gasness);
                }
                float chase = Mathf.Lerp(1.6f, 9f, _stateT); // solid = stiff, gas = loose
                _pos[i] = Vector3.Lerp(_pos[i], want, chase * dt);
            }

            // ---- skinning: the bones' displacement flows into the ONE skin ----
            for (int v = 0; v < _baseVerts.Length; v++)
            {
                Vector3 p = _baseVerts[v];
                for (int b = 0; b < Bones; b++)
                {
                    float w = _weights[v, b];
                    if (w > 0.001f) p += (_pos[b] - _home[b]) * w;
                }
                _workVerts[v] = p / SkinScale; // back to skin-local
            }
            _mesh.vertices = _workVerts;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        static void SetupTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f); // URP transparent
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
