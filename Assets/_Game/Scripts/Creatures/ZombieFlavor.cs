using UnityEngine;

namespace SpellyZombie
{
    /// The ZOMBIE-NESS layer (Marko: "zombies look terrible" — they were
    /// naked green wizard clones wearing the player's body). Per-zombie seeded
    /// variation + an undead POSTURE composed over the shared animations every
    /// frame, so the horde reads as a horde instead of a clone army:
    ///   · scale + tint jitter (quantized steps — the material cache stays small)
    ///   · classic reach-for-brains arms, hunch, head tilt, slow sway —
    ///     per-kind presets, per-zombie personality on top
    ///   · a dark gaping MOUTH under the googly eyes (players are eyeless
    ///     beans — the mouth is the friend-or-food tell at a glance)
    /// All of it is seeded by the zombie's id, so client proxies can later
    /// roll the identical look from the snapshot id (B6) for free.
    /// (Locomotion speed is the animator's business — the zombie controller's
    /// Shamble blend already carries idle/walk/run across the speed range.)
    public class ZombieFlavor : MonoBehaviour
    {
        /// Marko's kill-switch if the placeholder maw offends the art.
        public static bool GiveMouths = true;

        [Header("Flavour passes — YOUR switches (AXIOM: nothing is forced)")]
        [SerializeField] bool _posture = true;      // per-frame bone posing
        [SerializeField] bool _scaleJitter = true;  // random size variety
        [SerializeField] bool _mouth = true;        // placeholder mouth quad

        Animator _anim;
        Transform _armL, _armR, _foreL, _foreR, _handL, _handR, _head, _spine;
        float _reach, _hunch, _tiltRoll, _tiltPitch, _swayAmp, _swayRate, _phase;
        float _spreadL, _spreadR, _liftL, _liftR;
        bool _staticPosed;

        public void Init(ZombieKind kind, int seed, Animator anim,
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

            // ---- a body of one's own: quantized tint + scale jitter ----
            // (tint only on the shared placeholder body — Marko's custom
            // ZombieBody prefab keeps his materials verbatim)
            if (!customBody)
            {
                float hueJ = (rng.Next(5) - 2) * 0.016f;
                float valJ = (rng.Next(3) - 1) * 0.08f;
                Color.RGBToHSV(skin, out float h, out float s, out float v);
                var varied = Color.HSVToRGB(Mathf.Repeat(h + hueJ, 1f),
                    Mathf.Clamp01(s + valJ * 0.5f), Mathf.Clamp01(v + valJ));
                if (smr != null) smr.sharedMaterial = MatterFX.Get(varied, MoteShade.Opaque);
            }
            // AXIOM (Marko Jul 25): tint and mouths correctly stand down for HIS
            // body — the scale jitter did not, so his authored proportions got
            // randomised anyway. Now every flavour pass respects customBody,
            // and each is an Inspector switch he can turn off.
            if (_scaleJitter && !customBody)
            {
                float wj = R(0.92f, 1.1f), hj = R(0.97f, 1.03f);
                body.transform.localScale = Vector3.Scale(body.transform.localScale,
                    new Vector3(wj, hj, wj));
            }

            // ---- per-kind posture, then the personal touch ----
            switch (kind)
            {
                case ZombieKind.Runner: // sprinter: leaning, arms mostly animation
                    _reach = R(0.3f, 0.45f); _hunch = R(14f, 24f);
                    _swayAmp = R(1.5f, 3f); _swayRate = R(2.2f, 3.2f);
                    _liftL = _liftR = 0.5f;
                    break;
                case ZombieKind.Charger: // wide grapple arms, slow menace
                    _reach = R(0.5f, 0.7f); _hunch = R(6f, 12f);
                    _swayAmp = R(2.5f, 4f); _swayRate = R(0.8f, 1.4f);
                    _spreadL = R(0.4f, 0.7f); _spreadR = R(0.4f, 0.7f);
                    _liftL = _liftR = R(0.25f, 0.4f);
                    break;
                case ZombieKind.Scribbler: // the intellectual: upright, quill arm raised
                    _reach = R(0.4f, 0.55f); _hunch = R(2f, 7f);
                    _swayAmp = R(1f, 2f); _swayRate = R(0.9f, 1.5f);
                    _liftL = R(0.3f, 0.5f); _liftR = -0.35f; // right arm UP — the quill hand
                    _tiltRoll = R(9f, 18f) * (rng.Next(2) == 0 ? -1f : 1f);
                    break;
                case ZombieKind.Swarm: // frantic gremlins, arms high
                    _reach = R(0.75f, 0.9f); _hunch = R(5f, 12f);
                    _swayAmp = R(4f, 7f); _swayRate = R(2.8f, 4f);
                    _liftL = _liftR = R(-0.15f, 0.05f);
                    break;
                default: // Walker: the classic
                    _reach = R(0.6f, 0.85f); _hunch = R(11f, 20f);
                    _swayAmp = R(2f, 4f); _swayRate = R(1.1f, 2f);
                    _spreadL = R(-0.1f, 0.2f); _spreadR = R(-0.1f, 0.2f);
                    _liftL = R(0.15f, 0.45f); _liftR = R(0.15f, 0.45f);
                    break;
            }
            if (_tiltRoll == 0f) _tiltRoll = R(4f, 12f) * (rng.Next(2) == 0 ? -1f : 1f);
            _tiltPitch = R(-9f, 3f); // mostly looking up at you. creepily.
            _phase = R(0f, 6.28f);

            // ---- the maw: players are eyeless beans; zombies get a MOUTH ----
            // (never on Marko's custom body — his face is his)
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

            // the animator rewrites the pose every frame, so composing OVER it
            // never accumulates. Without one, pose ONCE and stand still —
            // multiplying a static pose per-frame would corkscrew the bones.
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
            if (!_posture) return; // his switch — code must never own his bones outright
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

        /// Partial FromToRotation toward the grope direction — the walk swing
        /// survives underneath, so the arms LIVE instead of pointing rigidly.
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
