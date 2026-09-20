using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// ★ INK IN A PHOTO (his ask: "drawing ink on anything that's posed is
    /// allowed"): a line drawn on a subject lands on the shape the pen would
    /// hit in play (a limb, a hat, a golem) and rides that bone like body ink,
    /// so a limb posed afterwards carries its drawing along.
    public partial class PhotoBooth
    {
        class InkLine
        {
            public PhotoDef.Ink Data;
            public Transform Bone;
            public Transform Root;
            public LineRenderer Line;
            public Vector3[] Buffer;
        }

        readonly List<InkLine> _inks = new List<InkLine>();
        InkLine _drawing;
        Color _inkColor = Stroke.InkColor;
        float _inkWidth = 0.02f;
        static Material _inkMat;

        /// The pen's own ink material (DrawingWorld's), made the same way.
        static Material InkMaterial
        {
            get
            {
                if (_inkMat != null) return _inkMat;
                if (DrawingWorld.Instance != null && DrawingWorld.Instance.LineMaterial != null)
                    return _inkMat = DrawingWorld.Instance.LineMaterial;
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                return _inkMat = new Material(shader);
            }
        }

        /// The game's ink colours first, then plain ones.
        static readonly (string Key, Color Color)[] InkColors =
        {
            ("photo.ink.ink", Stroke.InkColor),
            ("photo.ink.green", DrawingConfig.CorruptInkColor),
            ("photo.ink.gold", Stroke.SealColor),
            ("photo.ink.blue", Stroke.RuneColor),
            ("photo.ink.white", Color.white),
            ("photo.ink.black", new Color(0.05f, 0.05f, 0.06f)),
            ("photo.ink.red", new Color(0.85f, 0.12f, 0.1f)),
        };

        void StartInk()
        {
            if (_posing != null) StopPosing();
            _tool = Tool.Ink;
            _pick = null;
            BakeShells();
            BuildAddWindow();
            BuildInkWindow();
            Hint();
        }

        void TickInk(Mouse mouse)
        {
            if (mouse.leftButton.wasPressedThisFrame && !MapCreator.OverUI()) BeginInk(mouse.position.ReadValue());
            if (_drawing == null) return;
            if (mouse.leftButton.isPressed) ContinueInk(mouse.position.ReadValue());
            else EndInk();
        }

        bool InkHit(Vector2 screen, out RaycastHit hit)
        {
            Physics.SyncTransforms();
            return Physics.Raycast(_cam.ScreenPointToRay(screen), out hit, 5000f, 1 << Layer, QueryTriggerInteraction.Ignore);
        }

        void BeginInk(Vector2 screen)
        {
            if (!InkHit(screen, out var hit)) return;
            // a baked shell hands the line to the bone nearest where it landed
            var bone = hit.collider.transform;
            if (_shells.TryGetValue(hit.collider, out var skin) && skin != null) bone = NearestBone(skin, hit.point);
            var s = Owner(bone);
            if (s == null) return;
            float k = Mathf.Max(0.0001f, s.Root.transform.lossyScale.x);
            var data = new PhotoDef.Ink
            {
                Item = _subjects.IndexOf(s),
                Bone = PathOf(s.Root.transform, bone),
                Color = _inkColor,
                Width = _inkWidth / k, // in the subject's own size: the line grows with it
            };
            Editing.Inks.Add(data);
            _drawing = DrawInk(data);
            if (_drawing == null) { Editing.Inks.Remove(data); return; }
            AddPoint(_drawing, hit);
        }

        void ContinueInk(Vector2 screen)
        {
            if (_drawing.Bone == null || _drawing.Line == null) { EndInk(); return; }
            if (InkHit(screen, out var hit)) AddPoint(_drawing, hit);
        }

        /// A point just above the surface, kept in the bone's space.
        void AddPoint(InkLine line, RaycastHit hit)
        {
            float width = line.Data.Width * Mathf.Max(0.0001f, line.Root.lossyScale.x);
            Vector3 p = hit.point + hit.normal * (width * 0.5f);
            var pts = line.Data.Points;
            if (pts.Count > 0 && (line.Bone.TransformPoint(pts[pts.Count - 1]) - p).sqrMagnitude < width * width * 0.36f) return;
            pts.Add(line.Bone.InverseTransformPoint(p));
            Refresh(line);
        }

        /// The line is let go; a tap that never became a line leaves nothing.
        void EndInk()
        {
            if (_drawing == null) return;
            if (_drawing.Data.Points.Count < 2)
            {
                Editing.Inks.Remove(_drawing.Data);
                _inks.Remove(_drawing);
                if (_drawing.Line != null) Destroy(_drawing.Line.gameObject);
            }
            _drawing = null;
        }

        /// One saved line on its subject; null when that subject or bone is gone.
        InkLine DrawInk(PhotoDef.Ink data)
        {
            if (data.Item < 0 || data.Item >= _subjects.Count) return null;
            var s = _subjects[data.Item];
            if (s.Root == null) return null;
            var bone = Find(s.Root.transform, data.Bone);
            if (bone == null) return null;
            var go = new GameObject("~Ink") { layer = Layer };
            go.transform.SetParent(s.Root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = InkMaterial;
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = lr.endColor = data.Color;
            var line = new InkLine { Data = data, Bone = bone, Root = s.Root.transform, Line = lr };
            _inks.Add(line);
            Refresh(line);
            return line;
        }

        /// The subject stood again: its lines are drawn on the new copy.
        void RedrawInk(int index)
        {
            _inks.RemoveAll(l => l.Data.Item == index);
            foreach (var ink in Editing.Inks) if (ink.Item == index) DrawInk(ink);
        }

        /// Every line follows its bone (a posed limb, a moved or resized subject).
        void RefreshInk()
        {
            foreach (var line in _inks) Refresh(line);
        }

        static void Refresh(InkLine line)
        {
            if (line.Line == null || line.Bone == null) return;
            var pts = line.Data.Points;
            if (line.Buffer == null || line.Buffer.Length != pts.Count) line.Buffer = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++) line.Buffer[i] = line.Bone.TransformPoint(pts[i]);
            line.Line.positionCount = pts.Count;
            line.Line.SetPositions(line.Buffer);
            line.Line.widthMultiplier = line.Data.Width * Mathf.Max(0.0001f, line.Root.lossyScale.x);
        }

        /// The last line drawn goes.
        void UndoInk()
        {
            if (Editing.Inks.Count == 0) return;
            var data = Editing.Inks[Editing.Inks.Count - 1];
            Editing.Inks.RemoveAt(Editing.Inks.Count - 1);
            var line = _inks.Find(l => l.Data == data);
            if (line == null) return;
            _inks.Remove(line);
            if (line.Line != null) Destroy(line.Line.gameObject);
        }

        /// Every line goes, or only the picked subject's.
        void WipeInk(Subject only)
        {
            int index = only != null ? _subjects.IndexOf(only) : -1;
            Editing.Inks.RemoveAll(k => only == null || k.Item == index);
            foreach (var line in _inks.ToArray())
            {
                if (only != null && line.Data.Item != index) continue;
                if (line.Line != null) Destroy(line.Line.gameObject);
                _inks.Remove(line);
            }
        }

        // ------------------------------------------------------------ shells --
        /// A skinned body with no shape of its own (a zombie, a spell) gets its
        /// pose baked into a shell while the pen is out, the way body paint does.
        readonly Dictionary<Collider, SkinnedMeshRenderer> _shells = new Dictionary<Collider, SkinnedMeshRenderer>();

        void BakeShells()
        {
            DropShells();
            foreach (var s in _subjects)
            {
                if (s.Root == null) continue;
                bool solid = false;
                foreach (var c in s.Root.GetComponentsInChildren<Collider>())
                    if (c != null && c.enabled && !c.isTrigger) { solid = true; break; }
                if (solid) continue;
                foreach (var smr in s.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (smr == null || !smr.enabled || smr.sharedMesh == null) continue;
                    var mesh = new Mesh { name = "InkShell" };
                    smr.BakeMesh(mesh, true); // scale baked in: world = position + rotation * vertex
                    var go = new GameObject("~InkShell") { layer = Layer };
                    go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    _shells[mc] = smr;
                }
            }
        }

        void DropShells()
        {
            foreach (var kv in _shells)
            {
                if (kv.Key == null) continue;
                if (kv.Key is MeshCollider mc && mc.sharedMesh != null) Destroy(mc.sharedMesh);
                Destroy(kv.Key.gameObject);
            }
            _shells.Clear();
        }

        static Transform NearestBone(SkinnedMeshRenderer skin, Vector3 at)
        {
            Transform best = skin.transform;
            float bd = float.MaxValue;
            foreach (var b in skin.bones)
            {
                if (b == null) continue;
                float d = (b.position - at).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        // ---------------------------------------------------------- bone paths --
        /// The way down from the subject's root to a bone: names, with the place
        /// among same-named brothers when a name repeats.
        static string PathOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var w = t; w != null && w != root; w = w.parent)
            {
                int nth = 0;
                if (w.parent != null)
                    for (int i = 0; i < w.GetSiblingIndex(); i++)
                        if (w.parent.GetChild(i).name == w.name) nth++;
                parts.Add(nth > 0 ? w.name + "#" + nth : w.name);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static Transform Find(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            var at = root;
            foreach (var part in path.Split('/'))
            {
                string name = part;
                int nth = 0;
                int cut = part.LastIndexOf('#');
                if (cut > 0 && int.TryParse(part.Substring(cut + 1), out int n)) { name = part.Substring(0, cut); nth = n; }
                Transform next = null;
                for (int i = 0; i < at.childCount; i++)
                {
                    var c = at.GetChild(i);
                    if (c.name != name) continue;
                    if (nth-- == 0) { next = c; break; }
                }
                if (next == null) return null;
                at = next;
            }
            return at;
        }
    }
}
