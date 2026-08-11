using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// YOUR BODY INK TRAVELS WITH YOU — WITHIN ONE SITTING (Marko Aug 11:
    /// "when you draw ink on your body I want it to come with you to the
    /// game mode... persistent in the multiplayer as well"). Prep combos in
    /// the lobby; they are on your skin in the match.
    ///
    /// NO DISK. The first version also saved to a file and restored at boot,
    /// and Marko hit both failure modes the same day: ghost ink from an old
    /// session appearing uninvited, and unerasable strokes (a boot-time
    /// restore can run before Grimoire.LocalPlayerId exists, so the ink
    /// belonged to player 0 — nobody — and the eraser refused it). Ink now
    /// lives exactly as long as the play session.
    ///
    /// Nothing new is invented here — this is the NETCODE'S OWN BODY-INK
    /// CODEC held in memory: strokes are kept in BONE-LOCAL space keyed by
    /// bone name (exactly what StrokeMsg ships), and the restore is
    /// ApplyBodyStroke's recipe run on your own skeleton with YOUR owner id.
    /// Because the restore completes strokes through DrawingWorld like any
    /// local pen-up, NetSync.OnLocalStrokeFinished fires on its own — so in
    /// multiplayer the restored ink replicates to everyone through the same
    /// pipe fresh ink uses. Persistence and parity from one code path.
    ///
    /// THE RUNE GATE COSTS NOTHING: recognition only reads runes the owner
    /// has UNLOCKED (unknown glyphs score None and build no zone), so a body
    /// seal drawn in the lobby simply does nothing until the wizard collects
    /// those runes — his "not before you have the runes", already enforced
    /// where recognition lives.
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
            // eraser forever — wait until the pilot has an identity
            if (Grimoire.LocalPlayerId == 0) return;

            // ink already on my body (drawn this scene, or an earlier restore)
            // = nothing to do — restoring over it would double every stroke
            foreach (var s in DrawingWorld.Instance.Strokes)
                if (MineOnBone(s)) { _restored = true; return; }

            if (_kept.Count == 0) { _restored = true; return; }

            // the rig assembles over several frames — wait until the bones exist
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
                // completes like any local pen-up — which is what makes the
                // netcode broadcast it to everyone, unprompted
                DrawingWorld.Instance.CompleteStroke(s,
                    allowCloseOntoInk: false, silent: true, preview: false);
            }
            _restored = true;
        }
    }
}
