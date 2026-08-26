using UnityEngine;

namespace SpellyZombie
{
    /// Weight you can SEE. A body carrying more than its own weight sinks at
    /// the hips and hunches forward, and the further past its strength it goes
    /// the lower it gets - so a Compress rune reads on the target before the
    /// crush ladder starts biting.
    /// Runs after the Animator, so it bends the pose the clip produced instead
    /// of fighting it.
    [RequireComponent(typeof(Element))]
    public class WeightSag : MonoBehaviour
    {
        [Tooltip("The bone that drops when this body is weighed down. Empty = the humanoid rig's own Hips, taken from the Avatar you set.")]
        public Transform Sags;

        [Tooltip("How far it sinks at full load, as a fraction of its own height.")]
        public float MaxDrop = 0.16f;

        [Tooltip("How far it hunches forward at full load, in degrees.")]
        public float MaxLean = 20f;

        Element _dmg;
        Animator _anim;
        Transform _bone;
        float _height, _shown;
        bool _looked;

        void Awake()
        {
            _dmg = GetComponent<Element>();
            _anim = GetComponentInChildren<Animator>();
        }

        /// The rig's OWN hips, through the Avatar - not a name search, so a
        /// differently named skeleton still resolves and nothing is guessed.
        Transform Bone()
        {
            if (Sags != null) return Sags;
            if (_anim != null && _anim.isHuman)
                return _anim.GetBoneTransform(HumanBodyBones.Hips);
            return null;
        }

        float Height()
        {
            if (_height > 0f) return _height;
            var rends = GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                if (r == null || !r.enabled) continue;
                _height = Mathf.Max(_height, r.bounds.size.y);
            }
            if (_height <= 0f) _height = 1f;
            return _height;
        }

        void LateUpdate()
        {
            if (_dmg == null) return;

            // ease it, so a body that suddenly gains weight settles under it
            _shown = Mathf.MoveTowards(_shown, _dmg.Burden01, Time.deltaTime * 1.5f);
            if (_shown <= 0.002f) return;

            if (!_looked) { _bone = Bone(); _looked = true; }
            if (_bone == null) return;   // no rig to bend; nothing to say

            // the offset is re-applied to a pose the Animator rewrites every
            // frame. Without one it would stack and sink the body forever.
            if (_anim == null || !_anim.enabled) return;

            _bone.position -= Vector3.up * (Height() * MaxDrop * _shown);
            // pitch about the BODY's right, not the bone's - a rig's bone axes
            // point wherever the artist's skeleton does
            _bone.rotation = Quaternion.AngleAxis(MaxLean * _shown, transform.right)
                * _bone.rotation;
        }
    }
}
