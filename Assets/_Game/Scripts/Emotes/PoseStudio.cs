using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Menu-style pose setup (layout copied from Meccha Chameleon's menu):
    /// your character stands in front of the camera with visible grab points on
    /// its extremities. Grab one and drag the limb into place, drag the body to
    /// spin the character, click Save — the pose lands in your pose list, and
    /// pressing 1-9 with a pose selected binds it to that key.
    ///
    /// Two modes:
    ///  - AlwaysOpen (main menu): it IS the screen, no toggling, camera untouched.
    ///  - Toggle (graybox test scene): B opens/closes it and it borrows the view.
    public class PoseStudio : MonoBehaviour
    {
        public Camera StudioCamera;
        public EmoteRig Target;
        public bool AlwaysOpen;
        public bool ManageCameras = true;
        public float RotateCharacterSpeed = 0.4f; // degrees per pixel
        public float RollSpeed = 12f;             // degrees per scroll notch

        public static bool IsOpen { get; private set; }

        static readonly Color GrabColor = new Color(0.95f, 0.80f, 0.20f);
        static readonly Color GrabbedColor = new Color(0.30f, 0.90f, 1f);
        static readonly Color BodyGrabColor = new Color(0.85f, 0.85f, 0.85f);

        Camera _gameCamera;

        // grab state
        EmoteRig.JointEntry _grabbed;
        Vector3 _grabLocalDir;
        bool _rotatingCharacter;

        // grab point markers
        class Marker
        {
            public EmoteRig.JointEntry Joint;
            public Transform Tf;
            public Renderer R;
        }
        readonly List<Marker> _markers = new List<Marker>();
        Transform _bodyMarker;

        // pose preview blend
        EmoteKeyframe _previewFrame;
        float _previewT;
        readonly Dictionary<string, Quaternion> _previewFrom = new Dictionary<string, Quaternion>();

        // UI state
        int _selectedPose = -1;
        string _poseName = "";
        string _status = "";
        Vector2 _scroll;
        Rect _panelRect;

        void Start()
        {
            if (AlwaysOpen) Open();
        }

        void OnDestroy()
        {
            IsOpen = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null || StudioCamera == null || Target == null) return;

            // B toggles the studio (R belongs to body paint / weapon engraving)
            if (!AlwaysOpen && kb.bKey.wasPressedThisFrame && !SelfPaint.IsActive)
            {
                if (IsOpen) Close();
                else Open();
            }
            if (!IsOpen) return;

            if (!AlwaysOpen && kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            HandleGrab(mouse);
            HandleCharacterRotate(mouse);
            HandleKeys(kb);
            UpdatePreviewBlend();
            UpdateMarkers();
        }

        void Open()
        {
            IsOpen = true;
            if (ManageCameras)
            {
                _gameCamera = Camera.main;
                if (_gameCamera != null) _gameCamera.gameObject.SetActive(false);
                PositionCamera();
                StudioCamera.gameObject.SetActive(true);
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Target.GetComponent<EmotePlayer>()?.Interrupt();
            CreateMarkers();
            _status = "";
        }

        void Close()
        {
            if (AlwaysOpen) return;
            IsOpen = false;
            _grabbed = null;
            _rotatingCharacter = false;
            _previewFrame = null;
            DestroyMarkers();
            if (ManageCameras)
            {
                StudioCamera.gameObject.SetActive(false);
                if (_gameCamera != null) _gameCamera.gameObject.SetActive(true);
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void PositionCamera()
        {
            var root = Target.transform;
            Vector3 focus = root.position + Vector3.up * 1.2f;
            StudioCamera.transform.position = focus + root.forward * 2.4f + Vector3.up * 0.1f;
            StudioCamera.transform.LookAt(focus);
        }

        // ---- grab point markers (the sketch's little squares) ----

        void CreateMarkers()
        {
            DestroyMarkers();
            foreach (var j in Target.Joints)
            {
                if (j.T == null) continue;
                var go = MakeMarkerCube(GrabColor);
                _markers.Add(new Marker { Joint = j, Tf = go.transform, R = go.GetComponent<Renderer>() });
            }
            _bodyMarker = MakeMarkerCube(BodyGrabColor).transform;
        }

        void DestroyMarkers()
        {
            foreach (var m in _markers)
                if (m.Tf != null) Destroy(m.Tf.gameObject);
            _markers.Clear();
            if (_bodyMarker != null) Destroy(_bodyMarker.gameObject);
            _bodyMarker = null;
        }

        GameObject MakeMarkerCube(Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "GrabMarker";
            Destroy(go.GetComponent<Collider>()); // must never block the grab raycast
            go.transform.localScale = Vector3.one * 0.055f;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
                go.GetComponent<Renderer>().material = new Material(shader) { color = color };
            return go;
        }

        void UpdateMarkers()
        {
            foreach (var m in _markers)
            {
                if (m.Tf == null || m.Joint.T == null) continue;
                var hint = m.Joint.GrabHint != null ? m.Joint.GrabHint : m.Joint.T;
                m.Tf.position = hint.position;
                bool grabbed = _grabbed == m.Joint;
                m.Tf.localScale = Vector3.one * (grabbed ? 0.075f : 0.055f);
                if (m.R != null) m.R.material.color = grabbed ? GrabbedColor : GrabColor;
            }
            if (_bodyMarker != null)
                _bodyMarker.position = Target.transform.position
                                     + Vector3.up * 1.15f
                                     + Target.transform.forward * 0.16f;
        }

        // ---- posing ----

        void HandleGrab(Mouse mouse)
        {
            if (mouse.leftButton.wasPressedThisFrame && !MouseOverUI(mouse))
            {
                var ray = StudioCamera.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out var hit, 25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var rig = hit.collider.GetComponentInParent<EmoteRig>();
                    if (rig == Target)
                    {
                        var joint = Target.JointAtOrAbove(hit.transform);
                        // too close to the pivot to give a stable direction -> treat as body
                        if (joint != null && Vector3.Distance(hit.point, joint.T.position) < 0.08f)
                            joint = null;

                        if (joint != null)
                        {
                            _grabbed = joint;
                            _grabLocalDir = joint.T.InverseTransformDirection((hit.point - joint.T.position).normalized);
                            Target.GetComponent<EmotePlayer>()?.Interrupt();
                            _previewFrame = null;
                        }
                        else
                        {
                            _rotatingCharacter = true; // grabbed the torso: spin the figure
                        }
                    }
                }
            }

            if (!mouse.leftButton.isPressed)
                _grabbed = null;

            if (_grabbed?.T == null) return;

            // drag across a camera-facing plane THROUGH THE PIVOT — the
            // handle tracks the mouse 1:1 for the whole swing. Constrain at
            // every write: what you sculpt is exactly what saves and loads.
            var dragRay = StudioCamera.ScreenPointToRay(mouse.position.ReadValue());
            var plane = new Plane(-StudioCamera.transform.forward, _grabbed.T.position);
            if (plane.Raycast(dragRay, out float d))
            {
                Vector3 cursorPoint = dragRay.GetPoint(d);
                Vector3 pivot = _grabbed.T.position;
                Vector3 currentDir = _grabbed.T.TransformDirection(_grabLocalDir);
                Vector3 targetDir = cursorPoint - pivot;
                if (targetDir.sqrMagnitude > 1e-6f)
                {
                    _grabbed.T.rotation = Quaternion.FromToRotation(currentDir, targetDir.normalized) * _grabbed.T.rotation;
                    EmoteRig.Constrain(_grabbed);
                }
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                Vector3 axis = _grabbed.T.TransformDirection(_grabLocalDir);
                _grabbed.T.rotation = Quaternion.AngleAxis(Mathf.Sign(scroll) * RollSpeed, axis) * _grabbed.T.rotation;
                EmoteRig.Constrain(_grabbed);
            }
        }

        void HandleCharacterRotate(Mouse mouse)
        {
            if (!mouse.leftButton.isPressed) _rotatingCharacter = false;

            bool rotating = _rotatingCharacter || (mouse.rightButton.isPressed && !MouseOverUI(mouse));
            if (!rotating) return;

            float dx = mouse.delta.ReadValue().x;
            if (Mathf.Abs(dx) > 0.01f)
                Target.transform.Rotate(0f, -dx * RotateCharacterSpeed, 0f, Space.World);
        }

        void HandleKeys(Keyboard kb)
        {
            // typing a pose name must not trigger hotkeys
            if (GUIUtility.keyboardControl != 0) return;

            if (kb.xKey.wasPressedThisFrame)
            {
                Target.ResetAll();
                _previewFrame = null;
                _status = "Pose reset";
            }

            // number keys bind the selected pose to a slot
            if (_selectedPose >= 0 && _selectedPose < EmoteLibrary.Poses.Count)
            {
                for (int slot = 1; slot <= 9; slot++)
                {
                    var key = kb[(Key)((int)Key.Digit1 + slot - 1)];
                    if (key == null || !key.wasPressedThisFrame) continue;
                    EmoteLibrary.AssignSlot(slot, _selectedPose);
                    _status = $"'{EmoteLibrary.Poses[_selectedPose].name}' bound to key {slot}";
                }
            }
        }

        void SavePose()
        {
            string name = string.IsNullOrWhiteSpace(_poseName)
                ? $"Pose {EmoteLibrary.Poses.Count + 1}"
                : _poseName.Trim();
            var def = new EmoteDef { name = name, loop = false };
            def.frames.Add(Target.CapturePose());
            _selectedPose = EmoteLibrary.AddPose(def);
            _poseName = "";
            GUIUtility.keyboardControl = 0;
            _status = $"'{name}' saved — press 1-9 to bind it to a key";
        }

        void Preview(EmoteDef def)
        {
            if (def.frames.Count == 0) return;
            _previewFrame = def.frames[def.frames.Count - 1];
            _previewT = 0f;
            _previewFrom.Clear();
            foreach (var j in Target.Joints)
                if (j.T != null) _previewFrom[j.Id] = j.T.localRotation;
        }

        void UpdatePreviewBlend()
        {
            if (_previewFrame == null) return;
            _previewT += Time.deltaTime / 0.25f;
            float a = Mathf.Clamp01(_previewT);
            a = a * a * (3f - 2f * a);
            foreach (var p in _previewFrame.poses)
            {
                var j = Target.Find(p.joint);
                if (j?.T == null || !_previewFrom.TryGetValue(p.joint, out var from)) continue;
                j.T.localRotation = Quaternion.Slerp(from, Quaternion.Euler(p.euler), a);
            }
            if (_previewT >= 1f) _previewFrame = null;
        }

        bool MouseOverUI(Mouse mouse)
        {
            Vector2 mp = mouse.position.ReadValue();
            var guiPoint = new Vector2(mp.x, Screen.height - mp.y);
            return _panelRect.Contains(guiPoint);
        }

        // ---- UI ----

        void OnGUI()
        {
            if (!IsOpen) return;

            _panelRect = new Rect(Screen.width - 350, 10, 340, Screen.height - 20);
            GUILayout.BeginArea(_panelRect, GUI.skin.box);

            GUILayout.Label("<b>POSES</b>", Rich());
            GUILayout.Label("Grab a glowing point and drag the limb into place. Drag the body to turn the character. Scroll while holding = twist. X starts over.", Wrap());
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            _poseName = GUILayout.TextField(_poseName, GUILayout.MinWidth(160));
            if (GUILayout.Button("Save pose", GUILayout.Width(90)))
                SavePose();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            GUILayout.Label("<b>Your poses</b> — click one to try it, press 1-9 to put it on that key:", Rich());
            _scroll = GUILayout.BeginScrollView(_scroll);
            var poses = EmoteLibrary.Poses;
            for (int i = 0; i < poses.Count; i++)
            {
                GUILayout.BeginHorizontal();
                bool selected = i == _selectedPose;
                string label = $"{EmoteLibrary.SlotTag(i)} {poses[i].name}";
                bool now = GUILayout.Toggle(selected, label, GUI.skin.button);
                if (now && !selected)
                {
                    _selectedPose = i;
                    Preview(poses[i]);
                }
                if (GUILayout.Button("✕", GUILayout.Width(28)))
                {
                    EmoteLibrary.DeletePose(i);
                    if (_selectedPose == i) _selectedPose = -1;
                    else if (_selectedPose > i) _selectedPose--;
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status, Wrap());
            if (!AlwaysOpen && GUILayout.Button("Done (B)"))
                Close();

            GUILayout.EndArea();
        }

        static GUIStyle _rich, _wrap;
        static GUIStyle Rich()
        {
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            return _rich;
        }
        static GUIStyle Wrap()
        {
            if (_wrap == null) _wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            return _wrap;
        }
    }
}
