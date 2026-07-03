using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Declares which transforms on a character can be posed by emotes.
    /// Works on anything — graybox pivots today, Mixamo bones later — as long as
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
        }

        public List<JointEntry> Joints = new List<JointEntry>();

        void Awake()
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

        public JointEntry NearestJoint(Vector3 worldPos, float maxDistance)
        {
            JointEntry best = null;
            float bestD = maxDistance;
            foreach (var j in Joints)
            {
                if (j.T == null) continue;
                float d = Vector3.Distance(j.T.position, worldPos);
                if (d < bestD) { bestD = d; best = j; }
            }
            return best;
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
