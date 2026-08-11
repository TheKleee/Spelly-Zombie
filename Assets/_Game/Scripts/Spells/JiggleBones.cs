using UnityEngine;

namespace SpellyZombie
{
    /// LIVE JIGGLE BONES FOR A SKINNED BLOB — the deformation system, extracted.
    ///
    /// THE BLOB DEFORMS BY BONES (Marko, repeatedly): one skinned sphere, bones
    /// named D_* under the SMR's rootBone, each dragged by physics and sprung
    /// back toward its AUTHORED rest — the skin just follows. This component is
    /// that rig alone, no state choreography: StateBlob keeps its own woven
    /// copy for conjured matter (migrating it is his call), and the CAULDRON
    /// INK uses this one so the bones he posed in the prefab "adapt in real
    /// time" — settle into the bowl, slosh when the pot is carried, tip out
    /// when it is flipped.
    ///
    /// Same knobs as the conjured blob, one tuning for every soft thing:
    /// BlobBoneSpring / BlobBoneStray / BlobBoneDamping / BlobBoneRadius.
    public class JiggleBones : MonoBehaviour
    {
        [Range(0f, 1f)]
        [Tooltip("How strongly physics may move the bones off YOUR pose. 1 = the conjured blob's full slosh, 0.1 = a whisper. This component is OPT-IN — nothing adds it for you (Marko Aug 11: 'I'll put the bones in place').")]
        public float Influence = 0.25f;

        Transform _root;
        Transform[] _bones;
        Rigidbody[] _rbs;
        Vector3[] _rest;     // in root space — HIS posed arrangement, captured once
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
            smr.updateWhenOffscreen = true; // rig trap: import bounds ~0.005, culling eats the blob
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
                _rest[i] = root.InverseTransformPoint(bone.position); // rest = his authored pose
                _reach[i] = Mathf.Max(0.02f, _rest[i].magnitude);

                var sc = bone.gameObject.AddComponent<SphereCollider>();
                sc.radius = DrawingConfig.BlobBoneRadius * blobScale
                    / Mathf.Max(1e-4f, Mathf.Abs(bone.lossyScale.x));

                // ⛔ CLAMPED IN WORLD TERMS. The formula above is StateBlob's,
                // authored for unit-scale conjured rigs — under his ~96x-scaled
                // cauldron the import scales stop cancelling and a "bone" can
                // become a multi-metre sphere shoving the whole plaza around.
                // A bone's collider may never exceed a third of the blob's own
                // visible radius, whatever the transforms claim.
                float worldR = sc.radius * Mathf.Abs(bone.lossyScale.x);
                float maxWorldR = Mathf.Max(0.02f, smr.bounds.extents.magnitude * 0.33f);
                if (worldR > maxWorldR)
                    sc.radius *= maxWorldR / worldR;

                var rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.freezeRotation = true; // position jiggle only — rolling bones would swirl the skin
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

                // ⛔ CRITICALLY DAMPED (Marko Aug 11: "it's literally
                // oscillating"). The raw spring rings — 220 stiffness against
                // damping 9 is a bell, and pot ink must SIT, not wobble
                // forever. The velocity term below is exact critical damping
                // for whatever stiffness the knob holds, so the ink settles
                // dead and moves only when something actually disturbs the pot.
                // BlobBoneDamping stays as extra syrup on top via linearDamping.
                // Influence scales the stiffness; the damping term tracks it so
                // the spring stays exactly critical at every slider position
                float k = DrawingConfig.BlobBoneSpring * Mathf.Max(0.01f, Influence);
                rb.AddForce(off * k - vel * (2f * Mathf.Sqrt(k)), ForceMode.Acceleration);

                // the leash — and it KILLS the velocity it corrects. The old
                // version teleported the bone back but let it keep its escape
                // velocity, so gravity and the leash traded the bone back and
                // forth every physics step: the oscillation's second engine.
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
