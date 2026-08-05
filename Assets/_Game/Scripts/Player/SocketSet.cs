using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// STANDARD BONE SOCKETS — the costume mount points every dressed body
    /// (player, remote avatar, zombie) gets. Socket names are the contract:
    ///   Hat · Head · Cape · Chest · Belt · ShoulderL · ShoulderR ·
    ///   HandL · HandR · LegL · LegR
    /// Sockets sit at their bone but are rotated into PLAIN CHARACTER SPACE
    /// (+Z = the way the body faces, +Y = up), so Marko authors costume
    /// prefabs in normal orientation — no wrestling with Mixamo bone axes.
    /// MUST be built after the body faces forward (players: first LateUpdate
    /// after the animator's first frame; zombies: dress's first LateUpdate).
    /// (Own file on purpose — a MonoBehaviour hiding in a mismatched filename
    /// serializes as a broken "script can not be loaded" corpse on prefabs.)
    public class SocketSet : MonoBehaviour
    {
        readonly Dictionary<string, Transform> _sockets = new Dictionary<string, Transform>();

        /// Which sockets this body actually has — so a catalog slot that finds
        /// nothing can say WHY (AXIOM: never fail silently on his content).
        public IEnumerable<string> Names => _sockets.Keys;

        public Transform Get(string socketName) =>
            _sockets.TryGetValue(socketName, out var t) ? t : null;

        public static SocketSet Build(GameObject body, Transform facing)
        {
            var set = body.GetComponent<SocketSet>();
            if (set != null) return set;
            set = body.AddComponent<SocketSet>();

            var bones = body.GetComponentsInChildren<Transform>(true);
            Transform Find(string boneName)
            {
                foreach (var t in bones) if (t.name == "mixamorig:" + boneName) return t;
                foreach (var t in bones) if (t.name.EndsWith(boneName)) return t;
                foreach (var t in bones) if (t.name.Contains(boneName)) return t;
                return null;
            }

            Vector3 fwd = facing.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            var upright = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            void Sock(string socketName, Transform bone)
            {
                if (bone == null) return;
                // ADOPT a baked socket when the body carries one (Marko's
                // baked prefabs include sockets + their worn contents; a
                // second empty twin was the "Socket.Cape twice" confusion)
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
