using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// ★ THE GAME'S PREVIEW PANE: the thing you are making, rendered live into
    /// a picture on the panel, the same way the editor's pane shows it. Drag
    /// turns it, the wheel zooms, the sliders recolour it, and its bones are
    /// squares you drag to reshape it. A hidden camera watches it in a pocket
    /// far below the map, on a layer nothing else draws.
    public class PreviewPane : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public const int Layer = 30; // an unnamed layer nothing else uses
        static readonly Vector3 Pocket = new Vector3(0f, -4000f, 0f);
        internal static readonly SpellTable.Look Quiet = new SpellTable.Look();
        static int _pockets; // each pane its own pocket, out of the others' sight

        public GameObject Shown { get; private set; }
        /// Bones may be dragged.
        public bool Posable = true;
        /// A bone was dragged.
        public Action Posed;

        RawImage _img;
        RenderTexture _rt;
        Camera _cam;
        Transform _pocket, _floor;
        Vector2 _orbit = new Vector2(25f, 20f); // above the floor, looking down at the spell
        float _zoom = 3.2f;
        Transform[] _bones = new Transform[0];
        RectTransform _handleRoot;
        readonly List<Image> _handles = new List<Image>();
        int _grabbed = -1;

        public static PreviewPane Create(RectTransform parent, float size)
        {
            var go = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var pane = go.AddComponent<PreviewPane>();
            pane._img = go.GetComponent<RawImage>();
            pane._img.raycastTarget = true;
            UIKit.Row(pane._img, size, size);
            pane._handleRoot = UIKit.Group((RectTransform)go.transform, "Bones");
            UIKit.Stretch(pane._handleRoot);
            pane.BuildRig();
            return pane;
        }

        void BuildRig()
        {
            _rt = new RenderTexture(512, 512, 24);
            _img.texture = _rt;

            _pocket = new GameObject("~CreatorPreview").transform;
            _pocket.position = Pocket + Vector3.right * (200f * (_pockets++ % 50));

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_pocket, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 60f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            _cam.cullingMask = 1 << Layer;
            _cam.targetTexture = _rt;
            _cam.useOcclusionCulling = false;

            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(_pocket, false);
            lightGo.transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.8f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << Layer;

            // a floor grid under the body: which way is up
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "PreviewFloor";
            Destroy(floor.GetComponent<Collider>());
            floor.layer = Layer;
            floor.transform.SetParent(_pocket, false);
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(6f, 6f, 1f);
            var mat = new Material(MatterFX.Get(new Color(1f, 1f, 1f, 0.35f), MoteShade.Transparent));
            mat.mainTexture = MapCreator.GridTexture();
            mat.mainTextureScale = new Vector2(12f, 12f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            _floor = floor.transform;
        }

        /// Put this prefab in the pane. The previous one is thrown away.
        public void Show(GameObject prefab)
        {
            Clear();
            if (prefab == null || _pocket == null) return;

            // born under a sleeping parent: nothing on it wakes before its
            // scripts are gone, so a golem in the pane is a body, not a golem
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            holder.transform.SetParent(_pocket, false);
            Shown = Instantiate(prefab, holder.transform);
            Shown.transform.localPosition = Vector3.zero;
            Strip(Shown);
            foreach (var t in Shown.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            if (Shown.GetComponentInChildren<StateView>(true) == null) Shown.AddComponent<StateView>();
            foreach (var ps in Shown.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.useUnscaledTime = true;
            }
            Shown.transform.SetParent(_pocket, false);
            Destroy(holder);

            var bones = new List<Transform>();
            foreach (var t in Shown.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("D_")) bones.Add(t);
            _bones = bones.ToArray();
            BuildHandles();
        }

        /// Everything that acts goes; what draws stays. Components another
        /// one on the same object requires go after it. `keepColliders` keeps
        /// the shapes (the Photo Booth's ink lands on them).
        public static void Strip(GameObject go, bool keepColliders = false)
        {
            var left = new List<MonoBehaviour>();
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                // the eyes stay, switched off: StateView knows them by it and leaves them unpainted
                if (mb != null && !(mb is StateView) && !(mb is GooglyEyes)) left.Add(mb);
            foreach (var eyes in go.GetComponentsInChildren<GooglyEyes>(true)) eyes.enabled = false;
            for (int pass = 0; pass < 16 && left.Count > 0; pass++)
            {
                bool progress = false;
                for (int i = left.Count - 1; i >= 0; i--)
                {
                    var mb = left[i];
                    if (mb == null) { left.RemoveAt(i); progress = true; continue; }
                    if (Needed(mb, left)) continue;
                    DestroyImmediate(mb);
                    left.RemoveAt(i);
                    progress = true;
                }
                if (!progress) break;
            }
            foreach (var mb in left) if (mb != null) mb.enabled = false;
            foreach (var j in go.GetComponentsInChildren<Joint>(true)) DestroyImmediate(j);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            if (keepColliders) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        }

        static bool Needed(MonoBehaviour mb, List<MonoBehaviour> others)
        {
            var mine = mb.GetType();
            foreach (var o in others)
            {
                if (o == null || o == mb || o.gameObject != mb.gameObject) continue;
                foreach (var a in o.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                {
                    var rc = (RequireComponent)a;
                    if (Wants(rc.m_Type0, mine) || Wants(rc.m_Type1, mine) || Wants(rc.m_Type2, mine)) return true;
                }
            }
            return false;
        }

        static bool Wants(Type required, Type mine) => required != null && required.IsAssignableFrom(mine);

        /// Wear a saved pose: bones match by name, the rest stays put.
        public void ApplyPose(ShapeDef pose)
        {
            if (Shown != null) SpellParticle.PoseNow(Shown.transform, pose);
        }

        /// The bones as they stand now, as book data.
        public ShapeDef CapturePose(string name)
        {
            var def = new ShapeDef { Name = name };
            foreach (var t in _bones)
                if (t != null)
                    def.Bones.Add(new BonePose { Bone = t.name, P = t.localPosition, R = t.localRotation, S = t.localScale });
            return def;
        }

        /// Colour, state and the material sliders, through the one writer the
        /// body has (StateView), the way the game drives it.
        public void Tint(Color c, float state01, SpellTable.Look look)
        {
            if (Shown == null) return;
            var view = Shown.GetComponentInChildren<StateView>(true);
            if (view == null) return;
            view.Tint = c;
            view.DriveTint = true;
            view.StateT = state01;
            view.Look = look ?? Quiet;
            view.PushNow();
        }

        // a hidden pane draws nothing
        void OnEnable() { if (_cam != null) _cam.enabled = true; }
        void OnDisable() { if (_cam != null) _cam.enabled = false; }

        void LateUpdate()
        {
            if (_cam == null) return;
            Vector3 centre = Shown != null ? Bounds().center : _pocket.position;
            var rot = Quaternion.Euler(_orbit.y, _orbit.x, 0f);
            _cam.transform.position = centre + rot * new Vector3(0f, 0f, -_zoom);
            _cam.transform.LookAt(centre);
            if (_floor != null)
            {
                float floorY = Shown != null ? Bounds().min.y - 0.02f : _pocket.position.y;
                _floor.position = new Vector3(centre.x, floorY, centre.z);
            }
            PlaceHandles();
        }

        // ------------------------------------------------------------ input --
        public void OnDrag(PointerEventData e)
        {
            if (_grabbed >= 0) return;
            var canvas = GetComponentInParent<Canvas>();
            float k = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            _orbit += new Vector2(e.delta.x, e.delta.y) * (0.6f / k);
            _orbit.y = Mathf.Clamp(_orbit.y, -89f, 89f);
        }

        public void OnScroll(PointerEventData e)
        {
            _zoom = Mathf.Clamp(_zoom - e.scrollDelta.y * 0.15f, 0.8f, 12f);
        }

        // ------------------------------------------------------------ bones --
        void BuildHandles()
        {
            foreach (var h in _handles) if (h != null) Destroy(h.gameObject);
            _handles.Clear();
            if (!Posable || _handleRoot == null) return;
            for (int i = 0; i < _bones.Length; i++)
            {
                var go = new GameObject("Bone", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_handleRoot, false);
                var img = go.GetComponent<Image>();
                img.color = BoneColor(_bones[i].name);
                img.raycastTarget = true;
                go.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(12f, 12f);
                var h = go.AddComponent<BoneHandle>();
                h.Pane = this;
                h.Index = i;
                _handles.Add(img);
            }
        }

        void PlaceHandles()
        {
            if (_handles.Count == 0) return;
            var rect = ((RectTransform)transform).rect;
            for (int i = 0; i < _handles.Count && i < _bones.Length; i++)
            {
                var img = _handles[i];
                if (img == null) continue;
                var bone = _bones[i];
                Vector3 v = bone != null ? _cam.WorldToViewportPoint(bone.position) : Vector3.back;
                bool show = bone != null && v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
                if (img.gameObject.activeSelf != show) img.gameObject.SetActive(show);
                if (!show) continue;
                img.rectTransform.anchoredPosition = new Vector2((v.x - 0.5f) * rect.width, (v.y - 0.5f) * rect.height);
                img.color = i == _grabbed ? Color.yellow : BoneColor(bone.name);
            }
        }

        /// ★ WHICH BONE IS WHICH. The rig names bones by the direction they
        /// push - D_Up, D_Dn, D_Xp, D_Xn, D_Yp, D_Yn - so the colour comes off
        /// the name like the scene gizmo's arrows: the positive end full, the
        /// negative end pale, anything unnamed grey. Without it every bone was
        /// the same green and dragging the wrong one turned a funnel inside out.
        /// The editor's preview uses this too.
        public static Color BoneColor(string name)
        {
            Color Pale(Color c) => Color.Lerp(c, Color.white, 0.55f);
            var up = new Color(0.35f, 1f, 0.35f);
            var right = new Color(1f, 0.35f, 0.35f);
            var fwd = new Color(0.4f, 0.55f, 1f);
            switch (name)
            {
                case "D_Up": return up;
                case "D_Dn": return Pale(up);
                case "D_Xp": return right;
                case "D_Xn": return Pale(right);
                case "D_Yp": return fwd;
                case "D_Yn": return Pale(fwd);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        internal void GrabBone(int i) => _grabbed = i;

        internal void ReleaseBone()
        {
            _grabbed = -1;
            Posed?.Invoke();
        }

        /// The bone follows the pointer in the camera's own plane.
        internal void DragBone(int i, Vector2 screenDelta)
        {
            if (i < 0 || i >= _bones.Length || _bones[i] == null || _cam == null) return;
            var canvas = GetComponentInParent<Canvas>();
            float k = canvas != null ? Mathf.Max(0.01f, canvas.scaleFactor) : 1f;
            Vector2 d = screenDelta / k;
            var t = _bones[i];
            var ct = _cam.transform;
            float depth = Vector3.Dot(t.position - ct.position, ct.forward);
            float perPixel = 2f * depth * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad)
                / Mathf.Max(1f, ((RectTransform)transform).rect.height);
            t.position += (ct.right * d.x + ct.up * d.y) * perPixel;
        }

        Bounds Bounds()
        {
            var rends = Shown.GetComponentsInChildren<Renderer>(true);
            var b = new Bounds(Shown.transform.position, Vector3.one);
            bool any = false;
            foreach (var r in rends)
            {
                // a particle renderer with no particles reports a zero box at the origin
                if (r is ParticleSystemRenderer && r.bounds.size.sqrMagnitude < 0.0001f) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(Shown.transform.position, Vector3.one * 1.5f);
            return b;
        }

        // --------------------------------------------------------- lifetime --
        public void Clear()
        {
            if (Shown != null) Destroy(Shown);
            Shown = null;
            _bones = new Transform[0];
            _grabbed = -1;
            foreach (var h in _handles) if (h != null) Destroy(h.gameObject);
            _handles.Clear();
        }

        void OnDestroy()
        {
            Clear();
            if (_cam != null) _cam.targetTexture = null;
            if (_pocket != null) Destroy(_pocket.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }

    /// One bone's square on the pane: drag it, the bone follows.
    public class BoneHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public PreviewPane Pane;
        public int Index;

        public void OnBeginDrag(PointerEventData e) => Pane?.GrabBone(Index);
        public void OnDrag(PointerEventData e) => Pane?.DragBone(Index, e.delta);
        public void OnEndDrag(PointerEventData e) => Pane?.ReleaseBone();
    }
}
