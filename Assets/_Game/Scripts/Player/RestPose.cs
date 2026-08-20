using UnityEngine;

namespace SpellyZombie
{
    /// A remembered pose for a whole hierarchy - capture once, return softly
    /// (Settle) or at once (Snap). `skip` excludes subtrees that are not pose
    /// (props, sockets, wands).
    public class RestPose
    {
        (Transform t, Vector3 pos, Quaternion rot)[] _bones;

        public static RestPose Capture(Transform root, System.Func<Transform, bool> skip = null)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            var list = new System.Collections.Generic.List<(Transform, Vector3, Quaternion)>(all.Length);
            foreach (var t in all)
            {
                if (skip != null && skip(t)) continue;
                list.Add((t, t.localPosition, t.localRotation));
            }
            return new RestPose { _bones = list.ToArray() };
        }

        /// One frame of easing toward the captured pose. Call every frame
        /// while the pose should hold.
        public void Settle(float dt, float sharpness = 14f)
        {
            if (_bones == null) return;
            float k = 1f - Mathf.Exp(-sharpness * dt);
            foreach (var b in _bones)
            {
                if (b.t == null) continue;
                b.t.localPosition = Vector3.Lerp(b.t.localPosition, b.pos, k);
                b.t.localRotation = Quaternion.Slerp(b.t.localRotation, b.rot, k);
            }
        }

        /// Straight to the captured pose, no easing.
        public void Snap()
        {
            if (_bones == null) return;
            foreach (var b in _bones)
            {
                if (b.t == null) continue;
                b.t.localPosition = b.pos;
                b.t.localRotation = b.rot;
            }
        }
    }
}
