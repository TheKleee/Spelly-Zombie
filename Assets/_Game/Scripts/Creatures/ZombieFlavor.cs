using UnityEngine;

namespace SpellyZombie
{
    /// Per-zombie seeded variation + undead posture composed over the shared
    /// animations: scale/tint jitter, per-kind reach/hunch/sway, placeholder
    /// mouth. Seeded by the zombie's id so client proxies roll the identical look.
    public class ZombieFlavor : MonoBehaviour
    {
        /// Kill-switch for the placeholder mouth.
        public static bool GiveMouths = true;

        [Header("Flavour passes")]
        [SerializeField] bool _posture = true;      // per-frame bone posing
        [SerializeField] bool _scaleJitter = true;  // random size variety
        [SerializeField] bool _mouth = true;        // placeholder mouth quad

        Animator _anim;
        Transform _armL, _armR, _foreL, _foreR, _handL, _handR, _head, _spine;
        float _reach, _hunch, _tiltRoll, _tiltPitch, _swayAmp, _swayRate, _phase;
        float _spreadL, _spreadR, _liftL, _liftR;
        bool _staticPosed;

        public void Init(int seed, Animator anim,
            Color skin, SkinnedMeshRenderer smr, GameObject body, bool customBody = false)
        {
            _anim = anim;
            var rng = new System.Random(seed);
            float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

            Transform Bone(string boneName)
            {
                foreach (var t in body.GetComponentsInChildren<Transform>(true))
                    if (t.name == "mixamorig:" + boneName || t.name.EndsWith(boneName)) return t;
                return null;
            }
            _armL = Bone("LeftArm");
            _armR = Bone("RightArm");
            _foreL = Bone("LeftForeArm");
            _foreR = Bone("RightForeArm");
            _handL = Bone("LeftHand");
            _handR = Bone("RightHand");
            _head = Bone("Head");
            _spine = Bone("Spine1");

            // ---- quantized tint + scale jitter ----
            // (tint only on the shared placeholder body; a custom prefab keeps its materials)
            if (!customBody)
            {
                float hueJ = (rng.Next(5) - 2) * 0.016f;
                float valJ = (rng.Next(3) - 1) * 0.08f;
                Color.RGBToHSV(skin, out float h, out float s, out float v);
                var varied = Color.HSVToRGB(Mathf.Repeat(h + hueJ, 1f),
                    Mathf.Clamp01(s + valJ * 0.5f), Mathf.Clamp01(v + valJ));
                if (smr != null) smr.sharedMaterial = MatterFX.Get(varied, MoteShade.Opaque);
            }
            // every flavour pass respects customBody and has an Inspector switch
            if (_scaleJitter && !customBody)
            {
                float wj = R(0.92f, 1.1f), hj = R(0.97f, 1.03f);
                body.transform.localScale = Vector3.Scale(body.transform.localScale,
                    new Vector3(wj, hj, wj));
            }

            // ONE BODY, and the personal touch is what tells two zombies
            // apart. The per-kind postures went with the kinds.
            if (_tiltRoll == 0f) _tiltRoll = R(4f, 12f) * (rng.Next(2) == 0 ? -1f : 1f);
            _tiltPitch = R(-9f, 3f); // mostly looking up
            _phase = R(0f, 6.28f);

            // ---- placeholder mouth (never on the custom body) ----
            if (GiveMouths && _mouth && !customBody && _head != null)
            {
                var mouth = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mouth.name = "Mouth";
                Destroy(mouth.GetComponent<Collider>());
                mouth.transform.SetParent(_head, false);
                mouth.transform.localPosition = CharacterRig.EyeLocalPos
                    + new Vector3(R(-0.012f, 0.012f), R(-0.085f, -0.065f), 0f);
                float mw = R(0.12f, 0.18f);
                mouth.transform.localScale = new Vector3(mw, R(0.05f, 0.11f), mw);
                mouth.transform.localRotation = Quaternion.Euler(0f, 0f, R(-8f, 8f));
                mouth.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.1f, 0.07f, 0.07f), MoteShade.Opaque);
            }
        }

        void LateUpdate()
        {
            if (_head == null) return;

            bool animated = _anim != null && _anim.enabled
                && _anim.runtimeAnimatorController != null;

            // an animator rewrites the pose every frame, so composing over it never
            // accumulates; without one, pose ONCE or the bones corkscrew
            if (animated)
            {
                ApplyPosture();
            }
            else if (!_staticPosed)
            {
                _staticPosed = true;
                ApplyPosture();
            }
        }

        void ApplyPosture()
        {
            if (!_posture) return;
            _phase += Time.deltaTime * _swayRate;
            float sway = Mathf.Sin(_phase) * _swayAmp;

            if (_spine != null)
                _spine.localRotation = _spine.localRotation
                    * Quaternion.Euler(_hunch, sway, sway * 0.6f);
            _head.localRotation = _head.localRotation
                * Quaternion.Euler(_tiltPitch, -sway * 0.8f, _tiltRoll);

            Vector3 fwd = transform.forward, right = transform.right;
            Vector3 reachL = (fwd - right * _spreadL + Vector3.down * _liftL).normalized;
            Vector3 reachR = (fwd + right * _spreadR + Vector3.down * _liftR).normalized;
            Reach(_armL, _handL, reachL, _reach);
            Reach(_foreL, _handL, (reachL + Vector3.up * 0.14f).normalized, _reach * 0.7f);
            Reach(_armR, _handR, reachR, _reach);
            Reach(_foreR, _handR, (reachR + Vector3.up * 0.14f).normalized, _reach * 0.7f);
        }

        /// Partial FromToRotation so the walk swing survives underneath.
        static void Reach(Transform bone, Transform tip, Vector3 dir, float weight)
        {
            if (bone == null || tip == null || weight <= 0f) return;
            Vector3 cur = tip.position - bone.position;
            if (cur.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.Slerp(Quaternion.identity,
                Quaternion.FromToRotation(cur, dir), weight) * bone.rotation;
        }
    }
}
