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

        // HIS FX_StateBlob skin — instantiated AND DRIVEN (the committed
        // version instantiated and returned, so his State Matter material
        // never received _StateT and "the liquid is not the old liquid")
        GameObject _custom;
        Renderer[] _customRends; // cached once — fetching per melt frame allocated an array
        Animator _customAnim;
        Quaternion _customRot = Quaternion.identity;
        bool _animHasStateT, _animHasMuddy, _customMat, _customMsg, _fitted;
        float _lastPushed = float.NaN;
        MaterialPropertyBlock _mpb;
        SphereCollider _sphere; float _sphereR0; Vector3 _sphereC0; float _lastFluid = -1f;
        float _spawnR; // collider radius at birth — the honest fallback when import bounds lie

        // ---- HIS jiggle rig (Marko: "bones drive the shape and have their own
        // colliders to keep the distance from each other and the ground") ----
        Transform _boneRoot;   // SMR root bone — rest-pose anchor
        Transform[] _bones;    // the D_ bones he weighted in Blender
        Rigidbody[] _boneRbs;
        Collider[] _boneCols;
        Vector3[] _boneRest;   // root-local rest positions
        int _boneLayer = -1;   // forces first layer/shell sync

        /// collider fits HIS mesh (a smaller export floated on the default
        /// 0.5 sphere), then breathes with the state so puddles rest low
        void FitColliderToSkin()
        {
            if (_custom == null || _sphere == null) { _fitted = true; return; }
            var rends = _customRends;
            if (rends == null || rends.Length == 0) { _fitted = true; return; }
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float scale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
            float r = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) / scale;
            // his rig imports 0.005 bounds — keep the SPAWN size over a broken read,
            // retry until updateWhenOffscreen skinning reports honest bounds
            if (r < Mathf.Max(1e-3f, _spawnR * 0.25f)) return;
            _fitted = true;
            _sphereR0 = r;
            _sphereC0 = transform.InverseTransformPoint(b.center);
            _sphere.radius = r;
            _sphere.center = _sphereC0;
            _lastFluid = -1f;
        }

        void ReshapeCollider()
        {
            float fluid = 1f - Mathf.InverseLerp(0.5f, 1f, _stateT); // 0 solid … 1 fluid
            if (_sphere == null || Mathf.Abs(fluid - _lastFluid) < 0.02f) return;
            _lastFluid = fluid;
            _sphere.radius = Mathf.Lerp(_sphereR0, _sphereR0 * 0.55f, fluid);
            var c = _sphereC0;
            c.y -= _sphereR0 * 0.28f * fluid; // sink the contact — it rests, not floats
            _sphere.center = c;
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.WakeUp();
        }

        void Start()
        {
            _matter = GetComponent<Matter>();
            _sphere = GetComponent<SphereCollider>();
            if (_sphere != null) { _sphereR0 = _sphere.radius; _sphereC0 = _sphere.center; _spawnR = _sphereR0; }

            // the old look retires — the SKIN is the body now
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;

            // his art hook: FX_StateBlob replaces the code look entirely —
            // and is DRIVEN: state via Animator "StateT" / material "_StateT"
            // / OnStateT(float), colour from the matter (one material = water
            // here, lava there)
            var skinPrefab = PrefabVault.Get("FX_StateBlob");
            if (skinPrefab != null)
            {
                _custom = Instantiate(skinPrefab, transform, false);
                _customRends = _custom.GetComponentsInChildren<Renderer>(true); // once, here only
                _customRot = _custom.transform.localRotation;
                _customAnim = _custom.GetComponentInChildren<Animator>();
                if (_customAnim != null)
                {
                    foreach (var p in _customAnim.parameters)
                        if (p.type == AnimatorControllerParameterType.Float && p.name == "StateT") _animHasStateT = true;
                        else if (p.type == AnimatorControllerParameterType.Bool && p.name == "Muddy") _animHasMuddy = true;
                }
                foreach (var r in _customRends)
                    if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_StateT")) { _customMat = true; break; }
                _customMsg = _custom.GetComponentInChildren<MonoBehaviour>() != null;
                SetupJiggleBones();
                // born in its own phase — conjured steam must not melt its way down
                _stateT = Muddy ? 0.7f
                    : _matter == null || _matter.Phase == MatterPhase.Solid ? 1f
                    : _matter.Phase == MatterPhase.Liquid ? 0.5f : 0.1f;
                return;
            }

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

        /// HIS soft body (Marko: "bones drive the shape and have their own
        /// colliders to keep the distance from each other and the ground"):
        /// each D_ bone gets a small SphereCollider + Rigidbody, springs home,
        /// and the weighted skin follows by skinning — zero choreography.
        void SetupJiggleBones()
        {
            var smr = _custom.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) return;
            smr.updateWhenOffscreen = true; // import bounds are 0.005 — culling ate the blob (rig trap)
            var root = smr.rootBone;
            if (root == null) return;
            int n = 0;
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name.StartsWith("D_")) n++;
            if (n == 0) return;
            _boneRoot = root;
            _bones = new Transform[n];
            _boneRbs = new Rigidbody[n];
            _boneCols = new Collider[n];
            _boneRest = new Vector3[n];
            var core = GetComponent<Collider>();
            float blobScale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
            n = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var bone = root.GetChild(i);
                if (!bone.name.StartsWith("D_")) continue;
                _bones[n] = bone;
                _boneRest[n] = root.InverseTransformPoint(bone.position); // rest = his authored pose
                var sc = bone.gameObject.AddComponent<SphereCollider>();
                // radius meant in BLOB units, whatever the rig's import scale
                sc.radius = DrawingConfig.BlobBoneRadius * blobScale / Mathf.Max(1e-4f, Mathf.Abs(bone.lossyScale.x));
                var rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.freezeRotation = true; // position jiggle only — rolling bones would swirl the skin
                if (core != null) Physics.IgnoreCollision(sc, core); // never fights its own body
                _boneCols[n] = sc;
                _boneRbs[n] = rb;
                n++;
            }
        }

        /// Spring each bone to its rest spot relative to Root; gravity, the
        /// ground and bone-vs-bone collisions do everything else. No states,
        /// no choreography (his ruling) — the skin just follows the bones.
        void FixedUpdate()
        {
            if (_boneRbs == null) return;
            int blobLayer = gameObject.layer; // bones wear the blob's layer — liquids stay walk-through
            if (_boneLayer != blobLayer)
            {
                _boneLayer = blobLayer;
                var shell = transform.Find("LiquidShell"); // born on the same phase flip that moved the layer
                var shellCol = shell != null ? shell.GetComponent<Collider>() : null;
                for (int i = 0; i < _bones.Length; i++)
                {
                    _bones[i].gameObject.layer = blobLayer;
                    if (shellCol != null) Physics.IgnoreCollision(_boneCols[i], shellCol); // no self-wading
                }
            }
            float k = DrawingConfig.BlobBoneSpring, d = DrawingConfig.BlobBoneDamping;
            for (int i = 0; i < _boneRbs.Length; i++)
            {
                var rb = _boneRbs[i];
                // a bone can die before the blob (impact debris, component
                // cleanup order): skip it forever instead of spamming
                // MissingReference 696 times (his console, Aug 12)
                if (rb == null) continue;
                Vector3 home = _boneRoot.TransformPoint(_boneRest[i]);
                Vector3 off = rb.position - home;
                // LEASH: a hard drop can slingshot a bone past its siblings and
                // lock them crossed — "they entangle when dropped". A bone may
                // never stray further from its rest spot than its own reach.
                float reach = (home - _boneRoot.position).magnitude
                    * DrawingConfig.BlobBoneStray;
                if (off.sqrMagnitude > reach * reach && reach > 1e-4f)
                {
                    rb.position = home + off.normalized * reach;
                    rb.linearVelocity *= 0.5f;
                    off = rb.position - home;
                }
                rb.AddForce(-off * k - rb.linearVelocity * d, ForceMode.Acceleration);
            }
        }

        void Update()
        {
            if (_matter == null) return;

            // ---- the slider chases the phase (heat melts it down the ladder,
            // compression climbs it back — Matter already derives the phase) ----
            float target = Muddy ? 0.7f // MUD sits between solid and liquid
                : _matter.Phase == MatterPhase.Solid ? 1f
                : _matter.Phase == MatterPhase.Liquid ? 0.5f : 0.1f;
            _stateT = Mathf.MoveTowards(_stateT, target, StateLerpPerSec * Time.deltaTime);

            // HIS SKIN GETS THE STATE (the fix for "State Material is not
            // getting liquified"): push _StateT + the matter's colour when it
            // changes, keep fluids level with the world, fit the collider to
            // his mesh once, and sink it as the state melts.
            if (_custom != null)
            {
                if (!_fitted) FitColliderToSkin();
                if (float.IsNaN(_lastPushed) || Mathf.Abs(_stateT - _lastPushed) > 0.01f)
                {
                    _lastPushed = _stateT;
                    if (_animHasStateT) _customAnim.SetFloat("StateT", _stateT);
                    if (_animHasMuddy) _customAnim.SetBool("Muddy", Muddy);
                    if (_customMat)
                    {
                        var tint = SurfaceMaterialDB.Info(
                            _matter != null ? _matter.Material : SurfaceMaterialType.Stone).SolidColor;
                        if (_mpb == null) _mpb = new MaterialPropertyBlock();
                        foreach (var r in _customRends) // cached — the per-frame fetch was the melt's GC spike
                        {
                            r.GetPropertyBlock(_mpb);
                            _mpb.SetFloat("_StateT", _stateT);
                            _mpb.SetColor("_BaseColor", tint);
                            r.SetPropertyBlock(_mpb);
                        }
                    }
                    if (_customMsg)
                        _custom.SendMessage("OnStateT", _stateT, SendMessageOptions.DontRequireReceiver);
                }
                if (_stateT < 0.85f)
                    _custom.transform.rotation = transform.rotation * _customRot;
                ReshapeCollider();
                return;
            }

            if (_mesh == null) return;

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

            // A REAL SOLID IS RIGID (Marko: "objects that are really solid
            // shouldn't deform at all... the bones inside shouldn't deform
            // them. Muddy ones yes, and liquid, gas even more. Solid not.")
            // The state slider melts continuously and never quite reaches 1,
            // so a resting rock wore a permanent micro-slump. When the PHASE
            // says solid and it is not mud, the bones pin to home, exactly.
            bool rigid = !Muddy && _stateT > 0.9f
                && (_matter == null || _matter.Phase == MatterPhase.Solid);

            for (int i = 0; i < Bones; i++)
            {
                if (rigid) { _pos[i] = _home[i]; continue; }
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
