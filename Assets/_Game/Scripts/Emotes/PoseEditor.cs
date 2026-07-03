using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// In-world pose editor — how players author their own casting gestures.
    ///
    /// Press B to enter. Aim at any EmoteRig character: the nearest joint
    /// highlights; hold LMB and move the mouse to drag the limb (camera-relative),
    /// scroll to roll it. N snapshots the current body pose as a keyframe (repeat
    /// for a sequence), Ctrl+1..9 saves the pose/sequence into that emote slot.
    /// Seals drawn across the joints close and fire live while you pose — you
    /// feel the exact trigger point of your own spell while designing it.
    public class PoseEditor : MonoBehaviour
    {
        public Camera Cam;
        public float GrabRadius = 0.6f;   // how close the aim point must be to a joint
        public float RotateSpeed = 0.25f; // degrees per pixel of mouse movement
        public float RollSpeed = 12f;     // degrees per scroll notch

        public static bool IsEditing { get; private set; }
        public static bool IsRotatingJoint { get; private set; }

        EmoteRig _rig;               // last rig aimed at (edit target)
        EmoteRig.JointEntry _hover;
        EmoteRig.JointEntry _selected;
        readonly List<EmoteKeyframe> _capturedFrames = new List<EmoteKeyframe>();

        Transform _hoverMarker, _selectedMarker;

        void Start()
        {
            _hoverMarker = MakeMarker(new Color(1f, 0.9f, 0.3f));
            _selectedMarker = MakeMarker(new Color(0.3f, 0.9f, 1f));
        }

        void OnDestroy()
        {
            IsEditing = false;
            IsRotatingJoint = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null || Cam == null)
            {
                IsRotatingJoint = false;
                UpdateMarkers();
                return;
            }

            if (kb.bKey.wasPressedThisFrame)
            {
                IsEditing = !IsEditing;
                if (!IsEditing) { _hover = null; _selected = null; IsRotatingJoint = false; }
                DrawingWorld.Instance?.LogEvent(IsEditing
                    ? "Pose editor ON — aim at a joint, hold LMB to move the limb"
                    : "Pose editor OFF");
            }

            if (!IsEditing)
            {
                IsRotatingJoint = false;
                UpdateMarkers();
                return;
            }

            // ---- hover pick (only while not dragging) ----
            if (!mouse.leftButton.isPressed)
            {
                _hover = null;
                if (Physics.Raycast(GetAimRay(mouse), out var hit, DrawingConfig.DrawRange * 1.5f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var rig = hit.collider.GetComponentInParent<EmoteRig>();
                    if (rig != null)
                    {
                        _rig = rig;
                        _hover = rig.NearestJoint(hit.point, GrabRadius);
                    }
                }
            }

            // ---- grab & drag ----
            if (mouse.leftButton.wasPressedThisFrame && _hover != null)
            {
                _selected = _hover;
                _rig.GetComponent<EmotePlayer>()?.Interrupt(); // don't fight the animator
            }

            if (mouse.leftButton.isPressed && _selected?.T != null)
            {
                IsRotatingJoint = true;
                Vector2 d = mouse.delta.ReadValue() * RotateSpeed;
                var t = _selected.T;
                t.rotation = Quaternion.AngleAxis(d.x, Cam.transform.up)
                           * Quaternion.AngleAxis(-d.y, Cam.transform.right)
                           * t.rotation;
            }
            else
            {
                IsRotatingJoint = false;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (_selected?.T != null && Mathf.Abs(scroll) > 0.01f)
                _selected.T.rotation = Quaternion.AngleAxis(Mathf.Sign(scroll) * RollSpeed, Cam.transform.forward)
                                     * _selected.T.rotation;

            // ---- pose keys ----
            if (kb.xKey.wasPressedThisFrame && _rig != null)
            {
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)
                {
                    _rig.ResetAll();
                    DrawingWorld.Instance?.LogEvent("Pose reset to rest");
                }
                else if (_selected != null)
                {
                    _rig.ResetJoint(_selected);
                }
            }

            if (kb.nKey.wasPressedThisFrame && _rig != null)
            {
                _capturedFrames.Add(_rig.CapturePose());
                DrawingWorld.Instance?.LogEvent($"Keyframe {_capturedFrames.Count} captured — Ctrl+number saves the sequence");
            }

            if (kb.cKey.wasPressedThisFrame && _capturedFrames.Count > 0)
            {
                _capturedFrames.Clear();
                DrawingWorld.Instance?.LogEvent("Captured keyframes cleared");
            }

            // ---- save to slot ----
            if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)
            {
                for (int slot = 1; slot <= 9; slot++)
                {
                    var key = kb[(Key)((int)Key.Digit1 + slot - 1)];
                    if (key == null || !key.wasPressedThisFrame) continue;
                    if (_rig == null)
                    {
                        DrawingWorld.Instance?.LogEvent("Aim at a character before saving an emote");
                        break;
                    }
                    var def = new EmoteDef { name = $"Custom {slot}", loop = false };
                    def.frames = _capturedFrames.Count > 0
                        ? new List<EmoteKeyframe>(_capturedFrames)
                        : new List<EmoteKeyframe> { _rig.CapturePose() };
                    EmoteLibrary.AssignToSlot(slot, def);
                    _capturedFrames.Clear();
                    DrawingWorld.Instance?.LogEvent($"Emote saved to slot {slot} ({def.frames.Count} frame(s)) — press {slot} to play");
                    break;
                }
            }

            UpdateMarkers();
        }

        Ray GetAimRay(Mouse mouse)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                return Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return Cam.ScreenPointToRay(mouse.position.ReadValue());
        }

        Transform MakeMarker(Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "PoseMarker";
            Destroy(go.GetComponent<Collider>()); // the pen must never hit markers
            go.transform.localScale = Vector3.one * 0.07f;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
                go.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = color };
            go.SetActive(false);
            return go.transform;
        }

        void UpdateMarkers()
        {
            if (_hoverMarker == null) return;
            bool showHover = IsEditing && _hover?.T != null && _hover != _selected;
            _hoverMarker.gameObject.SetActive(showHover);
            if (showHover) _hoverMarker.position = _hover.T.position;

            bool showSelected = IsEditing && _selected?.T != null;
            _selectedMarker.gameObject.SetActive(showSelected);
            if (showSelected) _selectedMarker.position = _selected.T.position;
        }

        void OnGUI()
        {
            if (!IsEditing) return;
            var r = new Rect(Screen.width - 430, 10, 420, 130);
            string hover = _hover != null ? _hover.Id : "—";
            string selected = _selected != null ? _selected.Id : "—";
            GUI.Label(r,
                $"POSE EDITOR (B exits)   hover: {hover}   selected: {selected}   frames: {_capturedFrames.Count}\n" +
                "Hold LMB on a joint = drag limb · scroll = roll · X = reset joint · Shift+X = reset all\n" +
                "N = snapshot keyframe (repeat for a sequence) · C = clear keyframes\n" +
                "Ctrl+1..9 = save pose/sequence as that emote · 1..9 / T = play · drawing is paused while posing");
        }
    }
}
