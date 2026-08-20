using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Keeps your body ink across scene loads within one play session (never
    /// disk). Strokes are stored bone-local by bone name - the netcode's own
    /// body-ink codec - and restored through DrawingWorld like a local pen-up,
    /// so NetSync replicates restored ink on its own.
    public class BodyInkKeeper : MonoBehaviour
    {
        struct SavedStroke
        {
            public string Bone;
            public Vector3 Normal;    // bone-local
            public Vector3[] Pts;     // bone-local
            public int Declared;
        }

        static List<SavedStroke> _kept; // survives scene loads in-process only

        SimpleFPSController _pilot;
        float _scanIn = 1.5f;
        bool _restored;

        void Update()
        {
            if (_pilot == null) _pilot = GetComponent<SimpleFPSController>();
            if (_pilot == null || !_pilot.IsLocalViewer) return;
            if (DrawingWorld.Instance == null) return;

            if (_kept == null) _kept = new List<SavedStroke>();

            if (!_restored) { TryRestore(); return; }

            _scanIn -= Time.deltaTime;
            if (_scanIn > 0f) return;
            _scanIn = 2f;
            Capture();
        }

        bool MineOnBone(Stroke s) =>
            s != null && s.Alive && s.Persistent
            && s.OwnerId == Grimoire.LocalPlayerId
            && s.Surface != null && s.Surface.name.StartsWith("mixamorig:")
            && s.Surface.GetComponentInParent<SimpleFPSController>() == _pilot;

        void Capture()
        {
            var world = DrawingWorld.Instance;
            var snap = new List<SavedStroke>();
            foreach (var s in world.Strokes)
            {
                if (!MineOnBone(s)) continue;
                var pts = new List<Vector3>();
                foreach (var n in s.Nodes)
                    if (n != null) pts.Add(s.Surface.InverseTransformPoint(n.transform.position));
                if (pts.Count < 2) continue;
                snap.Add(new SavedStroke
                {
                    Bone = s.Surface.name,
                    Normal = s.Surface.InverseTransformDirection(
                        s.Nodes[0] != null ? s.Nodes[0].SurfaceNormal : s.Surface.forward),
                    Pts = pts.ToArray(),
                    Declared = (int)s.DeclaredRune,
                });
            }
            _kept = snap;
        }

        void TryRestore()
        {
            // no owner yet = strokes would belong to player 0 and refuse the
            // eraser forever - wait until the pilot has an identity
            if (Grimoire.LocalPlayerId == 0) return;

            // ink already on my body (drawn this scene, or an earlier restore)
            // = nothing to do - restoring over it would double every stroke
            foreach (var s in DrawingWorld.Instance.Strokes)
                if (MineOnBone(s)) { _restored = true; return; }

            if (_kept.Count == 0) { _restored = true; return; }

            // the rig assembles over several frames - wait until the bones exist
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("mixamorig:") && !bones.ContainsKey(t.name))
                    bones[t.name] = t;
            if (bones.Count == 0) return; // not built yet, retry next frame

            foreach (var saved in _kept)
            {
                if (saved.Pts == null || saved.Pts.Length < 2) continue;
                if (!bones.TryGetValue(saved.Bone, out var bone)) continue;

                Vector3 normal = bone.TransformDirection(saved.Normal);
                ZombieScribe.PlaneBasis(normal, out var right, out var up);
                var s = new Stroke
                {
                    BasisRight = right,
                    BasisUp = up,
                    Surface = bone,
                    OwnerId = Grimoire.LocalPlayerId,
                    DeclaredRune = (RuneType)saved.Declared,
                };
                DrawingWorld.Instance.Register(s);
                for (int i = 0; i < saved.Pts.Length; i++)
                    s.AddNode(DrawNode.Create(s, i,
                        bone.TransformPoint(saved.Pts[i]), normal, bone));
                // completes like any local pen-up - which is what makes the
                // netcode broadcast it to everyone, unprompted
                DrawingWorld.Instance.CompleteStroke(s,
                    allowCloseOntoInk: false, silent: true, preview: false);
            }
            _restored = true;
        }
    }
}
