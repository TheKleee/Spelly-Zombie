using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Standard bone sockets - costume mount points on every dressed body.
    /// Names are the contract: Hat · Head · Cape · Chest · Belt · ShoulderL ·
    /// ShoulderR · HandL · HandR · LegL · LegR. Sockets sit at their bone
    /// rotated into plain character space (+Z = facing, +Y = up). Must be
    /// built after the body faces forward. Kept in its own file - a class in
    /// a mismatched filename breaks prefab serialization.
    public class SocketSet : MonoBehaviour
    {
        readonly Dictionary<string, Transform> _sockets = new Dictionary<string, Transform>();

        /// Which sockets this body actually has - used by missing-socket warnings.
        public IEnumerable<string> Names => _sockets.Keys;

        public Transform Get(string socketName) =>
            _sockets.TryGetValue(socketName, out var t) ? t : null;

        /// Three-pass bone search (exact, EndsWith, Contains; HeadTop exports
        /// as HeadTop_End) - shared so every body resolves bones identically.
        public static Transform FindBone(Transform[] bones, string boneName)
        {
            foreach (var t in bones) if (t.name == "mixamorig:" + boneName) return t;
            foreach (var t in bones) if (t.name.EndsWith(boneName)) return t;
            foreach (var t in bones) if (t.name.Contains(boneName)) return t;
            return null;
        }

        public static SocketSet Build(GameObject body, Transform facing)
        {
            // a baked body carries this component, but Unity cannot serialize
            // the Dictionary - adopt the component, always rebuild the lookup
            var set = body.GetComponent<SocketSet>();
            if (set != null && set._sockets.Count > 0) return set; // resolved already this session
            if (set == null) set = body.AddComponent<SocketSet>();
            set._sockets.Clear();

            var bones = body.GetComponentsInChildren<Transform>(true);
            Transform Find(string boneName) => FindBone(bones, boneName);

            Vector3 fwd = facing.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            var upright = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            void Sock(string socketName, Transform bone)
            {
                if (bone == null) return;
                // adopt a baked socket when the body carries one - never
                // create an empty twin
                Transform s = null;
                foreach (Transform child in bone)
                    if (child.name == "Socket." + socketName) { s = child; break; }
                if (s == null)
                {
                    s = new GameObject("Socket." + socketName).transform;
                    s.SetParent(bone, false);
                    s.position = bone.position;
                    s.rotation = upright; // costume prefabs live in plain space
                }
                set._sockets[socketName] = s;
            }

            Sock("Hat", Find("HeadTop") != null ? Find("HeadTop") : Find("Head"));
            Sock("Head", Find("Head"));
            Sock("Cape", Find("Spine2") != null ? Find("Spine2") : Find("Spine1"));
            Sock("Chest", Find("Spine1"));
            Sock("Belt", Find("Hips"));
            Sock("ShoulderL", Find("LeftArm"));
            Sock("ShoulderR", Find("RightArm"));
            Sock("HandL", Find("LeftHand"));
            Sock("HandR", Find("RightHand"));
            Sock("LegL", Find("LeftUpLeg"));
            Sock("LegR", Find("RightUpLeg"));
            return set;
        }
    }
}
