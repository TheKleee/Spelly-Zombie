using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Declares which transforms on a character can be posed by emotes.
    /// Joint ids ("shoulder.L", "neck", ...) are the contract: saved emotes
    /// reference joints by id and skip unknown ids, surviving character swaps.
    public class EmoteRig : MonoBehaviour
    {
        [Serializable]
        public class JointEntry
        {
            public string Id;
            public Transform T;
            /// Where the visible "grab me" marker sits (e.g. the hand at the end
            /// of the arm). Falls back to the joint pivot when unset.
            public Transform GrabHint;
            [NonSerialized] public Quaternion Rest;

            /// Hinge limit (elbows/knees). Axis lives in the joint's rest
            /// frame; the allowed pose is Rest rotated [MinDeg..MaxDeg]
            /// around it. Unlimited joints leave Limited false.
            public bool Limited;
            public Vector3 HingeAxis;
            public float MinDeg, MaxDeg;
        }

        public List<JointEntry> Joints = new List<JointEntry>();

        /// Enforce a limited joint's hinge: the written rotation is reduced
        /// to its component around the hinge axis, clamped to the range.
        /// Runs at every write site (grab, pose playback).
        public static void Constrain(JointEntry j)
        {
            if (j == null || !j.Limited || j.T == null) return;
            Quaternion delta = Quaternion.Inverse(j.Rest) * j.T.localRotation;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsNaN(angle) || axis.sqrMagnitude < 1e-8f)
            {
                j.T.localRotation = j.Rest;
                return;
            }
            if (angle > 180f) angle -= 360f;
            float around = angle * Vector3.Dot(axis.normalized, j.HingeAxis.normalized);
            around = Mathf.Clamp(around, j.MinDeg, j.MaxDeg);
            j.T.localRotation = j.Rest * Quaternion.AngleAxis(around, j.HingeAxis);
        }

        void Awake() => CaptureRest();

        /// Rest must be captured after the animator's first evaluated frame:
        /// on a baked prefab, Awake sees the raw FBX bind pose. CharacterRig
        /// re-captures at its first LateUpdate; Awake only pre-initialises.
        public void CaptureRest()
        {
            foreach (var j in Joints)
                if (j.T != null) j.Rest = j.T.localRotation;
        }

        public JointEntry Find(string id)
        {
            foreach (var j in Joints)
                if (j.Id == id) return j;
            return null;
        }

        /// The first registered joint at or above a clicked transform (walk
        /// up, stop at the rig root). Used by PoseStudio and PoseGrab.
        public JointEntry JointAtOrAbove(Transform t)
        {
            while (t != null)
            {
                foreach (var j in Joints)
                    if (j.T == t) return j;
                if (t == transform) break;
                t = t.parent;
            }
            return null;
        }

        /// Snapshot every joint's current local rotation as one keyframe.
        public EmoteKeyframe CapturePose()
        {
            var frame = new EmoteKeyframe();
            foreach (var j in Joints)
                if (j.T != null)
                    frame.poses.Add(new JointPose { joint = j.Id, euler = j.T.localEulerAngles });
            return frame;
        }

        public void ResetJoint(JointEntry j)
        {
            if (j?.T != null) j.T.localRotation = j.Rest;
        }

        public void ResetAll()
        {
            foreach (var j in Joints) ResetJoint(j);
        }
    }
}
