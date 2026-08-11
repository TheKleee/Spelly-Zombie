using UnityEngine;

namespace SpellyZombie
{
    /// A REMEMBERED POSE FOR A WHOLE HIERARCHY — capture once, come back to it
    /// any time, softly or at once.
    ///
    /// Extracted on Marko's standing order (Aug 11: "actually reuse the code
    /// instead of writing a new one... who knows where we might need it again"),
    /// because the game had grown FOUR private copies of exactly this idea:
    /// CharacterRig's _boneHome, EmoteRig.CaptureRest, RelaxForPaint's settle,
    /// and the zombie paint freeze. Same concept, four spellings, drifting
    /// apart. This is the one spelling; the zombie freeze uses it today and the
    /// player-side copies can migrate whenever he says go.
    ///
    /// `skip` lets a caller exclude subtrees that are NOT pose (props, sockets,
    /// wands) — the exact lesson _boneHome learned the hard way when it
    /// overwrote his attached props.
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

        /// One frame of easing toward the captured pose — the magic taking
        /// hold, not a glitch snap. Call every frame while the pose should hold.
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
