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

        /// The joint table on a mixamo skeleton - the pilot's rig and every
        /// puppet share it, so a pose saved on one plays on the other.
        /// `root` gives the body's facing for the hinge axes.
        public static void Populate(EmoteRig rig, Transform[] bones, Transform root)
        {
            rig.Joints.Clear();
            Transform B(string boneName) => SocketSet.FindBone(bones, boneName);
            var armL = B("LeftArm"); var armR = B("RightArm");
            var handL = B("LeftHand"); var handR = B("RightHand");
            var foreL = B("LeftForeArm"); var foreR = B("RightForeArm");
            var head = B("Head"); var neck = B("Neck"); var headTop = B("HeadTop");
            var spine1 = B("Spine1"); var spine2 = B("Spine2");
            var upLegL = B("LeftUpLeg"); var upLegR = B("RightUpLeg");
            var legL = B("LeftLeg"); var legR = B("RightLeg");
            var footL = B("LeftFoot"); var footR = B("RightFoot");

            void Joint(string id, Transform bone, Transform hint)
            {
                if (bone == null) return;
                rig.Joints.Add(new JointEntry
                {
                    Id = id, T = bone, GrabHint = hint != null ? hint : bone,
                    Rest = bone.localRotation
                });
            }
            Joint("shoulder.L", armL, handL);
            Joint("shoulder.R", armR, handR);
            Joint("neck", neck != null ? neck : head, headTop != null ? headTop : head);
            Joint("spine", spine2 != null ? spine2 : spine1, spine2);
            Joint("leg.L", upLegL, footL);
            Joint("leg.R", upLegR, footR);

            // elbows and knees are hinge-limited. The hinge axis is the bind
            // pose's side axis in each joint's own rest frame; flip the sign
            // consts if a test bend goes backwards.
            void Hinge(string id, Transform bone, Transform hint, float sign, float maxFlex)
            {
                if (bone == null) return;
                Vector3 sideWorld = Vector3.Cross(Vector3.up, root.forward);
                rig.Joints.Add(new JointEntry
                {
                    Id = id, T = bone, GrabHint = hint != null ? hint : bone,
                    Rest = bone.localRotation, Limited = true,
                    HingeAxis = (Quaternion.Inverse(bone.rotation) * (sideWorld * sign)).normalized,
                    MinDeg = -5f, MaxDeg = maxFlex,
                });
            }
            Hinge("elbow.L", foreL, handL, CharacterRig.ElbowHingeSign, 140f);
            Hinge("elbow.R", foreR, handR, CharacterRig.ElbowHingeSign, 140f);
            Hinge("knee.L", legL, footL, CharacterRig.KneeHingeSign, 135f);
            Hinge("knee.R", legR, footR, CharacterRig.KneeHingeSign, 135f);
        }

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
