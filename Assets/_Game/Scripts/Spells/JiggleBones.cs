using UnityEngine;

namespace SpellyZombie
{
    /// Jiggle bones for a skinned blob: D_* bones under the SMR rootBone are
    /// dragged by physics and sprung back to their authored rest; the skin follows.
    /// Tuned by BlobBoneSpring / BlobBoneStray / BlobBoneDamping / BlobBoneRadius.
    public class JiggleBones : MonoBehaviour
    {
        [Range(0f, 1f)]
        [Tooltip("How strongly physics may move the bones off the pose. 1 = the conjured blob's full slosh, 0.1 = a whisper. Opt-in: nothing adds this component automatically.")]
        public float Influence = 0.25f;

        Transform _root;
        Transform[] _bones;
        Rigidbody[] _rbs;
        Vector3[] _rest;     // in root space - the posed arrangement, captured once
        float[] _reach;      // rest distance from root, for the stray leash

        public static JiggleBones Adopt(Transform host)
        {
            if (host == null) return null;
            var j = host.GetComponent<JiggleBones>();
            if (j == null) j = host.gameObject.AddComponent<JiggleBones>();
            return j;
        }

        void Start()
        {
            var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null) return;
            smr.updateWhenOffscreen = true; // import bounds ~0.005, culling eats the blob
            var root = smr.rootBone;
            if (root == null) return;

            var list = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name.StartsWith("D_")) list.Add(root.GetChild(i));
            if (list.Count == 0)
            {
                Debug.LogWarning("[SpellyZombie] JiggleBones: no D_* bones under the rig's rootBone — " +
                    "the blob will not deform. This rig may not be the weighted blob.", this);
                return;
            }

            _root = root;
            _bones = list.ToArray();
            _rbs = new Rigidbody[_bones.Length];
            _rest = new Vector3[_bones.Length];
            _reach = new float[_bones.Length];
            float blobScale = Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));

            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                _rest[i] = root.InverseTransformPoint(bone.position); // rest = the authored pose
                _reach[i] = Mathf.Max(0.02f, _rest[i].magnitude);

                var sc = bone.gameObject.AddComponent<SphereCollider>();
                sc.radius = DrawingConfig.BlobBoneRadius * blobScale
                    / Mathf.Max(1e-4f, Mathf.Abs(bone.lossyScale.x));

                // clamp in world terms: a bone collider may never exceed a third
                // of the blob's visible radius (large import scales break the formula)
                float worldR = sc.radius * Mathf.Abs(bone.lossyScale.x);
                float maxWorldR = Mathf.Max(0.02f, smr.bounds.extents.magnitude * 0.33f);
                if (worldR > maxWorldR)
                    sc.radius *= maxWorldR / worldR;

                var rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.freezeRotation = true; // position jiggle only - rolling bones would swirl the skin
                rb.linearDamping = DrawingConfig.BlobBoneDamping;
                _rbs[i] = rb;

                bone.gameObject.layer = gameObject.layer; // the blob's own layer, bowl included
            }
        }

        void FixedUpdate()
        {
            if (_rbs == null || _root == null) return;
            for (int i = 0; i < _rbs.Length; i++)
            {
                var rb = _rbs[i];
                if (rb == null) continue;

                Vector3 rest = _root.TransformPoint(_rest[i]);
                Vector3 off = rest - rb.position;
                Vector3 vel = rb.linearVelocity;

                // critically damped: the velocity term is exact critical damping for
                // the Influence-scaled stiffness; BlobBoneDamping adds extra via linearDamping
                float k = DrawingConfig.BlobBoneSpring * Mathf.Max(0.01f, Influence);
                rb.AddForce(off * k - vel * (2f * Mathf.Sqrt(k)), ForceMode.Acceleration);

                // the leash kills the escape velocity it corrects, or the bone oscillates
                float leash = DrawingConfig.BlobBoneStray * _reach[i]
                    * Mathf.Max(1e-4f, Mathf.Abs(transform.lossyScale.x));
                if (off.magnitude > leash)
                {
                    Vector3 outward = -off.normalized;
                    rb.position = rest + outward * leash;
                    float escaping = Vector3.Dot(vel, outward);
                    if (escaping > 0f) rb.linearVelocity = vel - outward * escaping;
                }
            }
        }
    }
}
