using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Declares which transforms on a character can be posed by emotes.
    /// Works on anything - graybox pivots today, Mixamo bones later - as long as
    /// the joint ids stay consistent ("shoulder.L", "shoulder.R", "neck", ...).
    /// Saved emotes reference joints only by id; unknown ids are skipped, so
    /// emotes survive a character swap.
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

            /// HINGE limit (elbows/knees - the rule: constrained joints make
            /// body-seal PLACEMENT a puzzle). Axis lives in the joint's rest
            /// frame; the allowed pose is Rest rotated [MinDeg..MaxDeg] around
            /// it, nothing else. Unlimited joints leave Limited false.
            public bool Limited;
            public Vector3 HingeAxis;
            public float MinDeg, MaxDeg;
        }

        public List<JointEntry> Joints = new List<JointEntry>();

        /// Enforce a limited joint's hinge: whatever was just written to the
        /// bone is reduced to its component around the hinge axis, clamped to
        /// the range. Runs at EVERY write site (grab, pose playback) so saved
        /// files with illegal angles obey the same anatomy as live posing.
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

        /// REST IS RUNTIME-ONLY and must be read AFTER the animator's first
        /// frame, or every value lives in the wrong basis.
        ///
        /// AXIOM (, "drawing on the body doesn't work properly
        /// again... probably something happened cause of the prefab"): this
        /// used to be captured in Awake and never again. That was safe only
        /// while EmoteRig was ADDED AT RUNTIME, after the model was worn and
        /// the animator had evaluated. On a BAKED PLAYER PREFAB it is a
        /// serialized component, so Awake fires at scene load and Rest takes
        /// the raw FBX bind pose. RelaxForPaint then snapped the body into
        /// that on every R, and the paint shell baked against a pose nobody
        /// could see. CharacterRig re-captures at its first LateUpdate, the
        /// same moment it builds the sockets, which is the earliest point the
        /// pose is real. Awake still runs so nothing is ever uninitialised.
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

        /// The first registered joint at or above a clicked transform (walk up,
        /// stop at the rig root) - the ONE limb-resolve both PoseStudio and
        /// PoseGrab use.
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
