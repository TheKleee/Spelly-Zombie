using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// POSE MODE (Marko's spec, reworked Jul 22): in THIRD person press R —
    /// the camera becomes an orbiting easel around your frozen wizard:
    ///
    ///   LMB-drag a limb — the WHOLE extremity follows your cursor from
    ///     wherever you grabbed it (two-bone reach, elbows/knees obey hinges)
    ///   SHIFT+LMB — grab the INDIVIDUAL bone under the cursor and rotate it
    ///   scroll twists what you hold · WASD / MMB-drag orbit · wheel zooms
    ///   1-9 load a pose to start from · hold/Ctrl+1-9 SAVE · F relax · R done
    ///
    /// (Body DRAWING stays on R in first person with the wand — this is the
    /// posing counterpart, same camera language.)
    public class PoseGrab : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        SimpleFPSController _pilot;
        EmoteRig _rig;
        EmotePlayer _emotes;
        Camera _cam;

        // orbit state (same easel language as body paint)
        Vector3 _camLocalPos;
        Quaternion _camLocalRot;
        float _yaw, _pitch, _dist;
        Vector3 _pan;

        // grab state — SHIFT rotates one bone; plain drag reaches a whole limb
        EmoteRig.JointEntry _grabbed;          // shift mode: the bone being rotated
        bool _rotateMode;                      // true = shift bone-rotate
        EmoteRig.JointEntry _ikRoot, _ikMid;   // limb mode: shoulder/hip + elbow/knee
        Transform _ikEnd;                      // the limb tip that chases the cursor
        Vector3 _ikOffset;                     // tip − click point (no snap on grab)
        Vector3 _grabLocalDir, _grabPlanePoint;

        // hold-a-number-to-save state
        int _holdSlot = -1;
        float _holdTime;
        bool _holdSaved;

        void SaveTo(int slot)
        {
            var def = new EmoteDef { name = $"Pose {slot}", loop = false };
            def.frames.Add(_rig.CapturePose());
            EmoteLibrary.AssignSlot(slot, EmoteLibrary.AddPose(def));
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent($"Pose saved to key {slot}");
        }

        void Awake() => _pilot = GetComponent<SimpleFPSController>();

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            bool available = SimpleFPSController.ThirdPersonActive
                && !PoseStudio.IsOpen && !SelfPaint.IsActive
                && !GameMenu.IsOpen && !Powerups.IsChoosing && !UIKit.Typing
                && (_pilot == null
                    || (!_pilot.IsDowned && !_pilot.IsSprawled && !_pilot.IsAirTumbling));
            if (!available)
            {
                if (IsOpen) Close();
                return;
            }

            if (_rig == null) _rig = GetComponent<EmoteRig>();
            if (_emotes == null) _emotes = GetComponent<EmotePlayer>();
            if (_cam == null) _cam = GetComponentInChildren<Camera>();
            if (_rig == null || _cam == null) return;

            if (kb.rKey.wasPressedThisFrame)
            {
                if (IsOpen) { Close(); return; }
                Open();
            }
            if (!IsOpen) return;
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }

            // F melts the sculpt back to rest
            if (kb.fKey.wasPressedThisFrame)
            {
                _grabbed = null;
                _emotes?.Interrupt();
                foreach (var j in _rig.Joints)
                    if (j.T != null) j.T.localRotation = j.Rest;
            }

            // numbers: TAP loads a pose to start from, HOLD (0.6s) SAVES the
            // current shape to that key. (Ctrl+number saves instantly too, but
            // the Unity editor eats Ctrl+numbers — holding works everywhere.)
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            for (int slot = 1; slot <= 9; slot++)
            {
                var key = kb[(Key)((int)Key.Digit1 + slot - 1)];
                if (key == null) continue;
                if (key.wasPressedThisFrame)
                {
                    if (ctrl) { SaveTo(slot); break; }
                    _holdSlot = slot;
                    _holdTime = 0f;
                    _holdSaved = false;
                }
                if (_holdSlot == slot && key.isPressed)
                {
                    _holdTime += Time.deltaTime;
                    if (!_holdSaved && _holdTime >= 0.6f)
                    {
                        _holdSaved = true;
                        SaveTo(slot);
                    }
                }
                if (_holdSlot == slot && key.wasReleasedThisFrame)
                {
                    if (!_holdSaved) _emotes?.ToggleSlot(slot); // quick tap = load
                    _holdSlot = -1;
                }
            }

            Orbit(kb, mouse);
            HandleGrab(mouse);
        }

        void Open()
        {
            IsOpen = true;
            _camLocalPos = _cam.transform.localPosition;
            _camLocalRot = _cam.transform.localRotation;
            _yaw = transform.eulerAngles.y + 180f; // start facing the wizard
            _pitch = 10f;
            _dist = 2.6f;
            _pan = Vector3.zero;
            _emotes?.Interrupt(); // the doll holds whatever it's doing
            ApplyOrbit();
        }

        void Close()
        {
            IsOpen = false;
            _grabbed = null;
            _ikRoot = null; _ikMid = null; _ikEnd = null;
            if (_cam != null)
            {
                _cam.transform.localPosition = _camLocalPos;
                _cam.transform.localRotation = _camLocalRot;
            }
            // unsaved sculpts relax when the animator resumes — Ctrl+number
            // is the save button, and the prompt says so
        }

        void Orbit(Keyboard kb, Mouse mouse)
        {
            if (mouse.middleButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yaw += d.x * 0.3f;
                _pitch = Mathf.Clamp(_pitch - d.y * 0.3f, -85f, 85f);
            }
            float zoom = mouse.scroll.ReadValue().y;
            // wheel zooms ONLY when nothing is held (held = twist)
            if (_grabbed == null && _ikRoot == null && Mathf.Abs(zoom) > 0.01f)
                _dist = Mathf.Clamp(_dist * (1f - Mathf.Sign(zoom) * 0.12f), 1.0f, 4.5f);

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            float panX = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float panY = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            if (panX != 0f || panY != 0f)
            {
                _pan += (rot * new Vector3(panX, panY, 0f)) * (0.6f * _dist) * Time.deltaTime;
                _pan = Vector3.ClampMagnitude(_pan, 2.2f);
            }
            ApplyOrbit(rot);
        }

        void ApplyOrbit() => ApplyOrbit(Quaternion.Euler(_pitch, _yaw, 0f));

        void ApplyOrbit(Quaternion rot)
        {
            Vector3 focus = transform.position + _pan;
            _cam.transform.position = focus + rot * new Vector3(0f, 0f, -_dist);
            _cam.transform.rotation = rot;
        }

        void HandleGrab(Mouse mouse)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                var ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
                // the limbs live on Ignore Raycast — include it explicitly
                int mask = Physics.DefaultRaycastLayers | (1 << 2);
                // SHIFT = grab the exact BONE under the cursor and rotate it;
                // plain click = grab the whole EXTREMITY and drag it around
                // (Marko's rework: moving a limb should just work)
                var kb = Keyboard.current;
                bool fine = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                if (Physics.Raycast(ray, out var hit, 30f, mask, QueryTriggerInteraction.Ignore))
                {
                    if (fine)
                    {
                        var joint = NearestJointUp(hit.transform);
                        if (joint != null)
                        {
                            _rotateMode = true;
                            _grabbed = joint;
                            _grabPlanePoint = hit.point;
                            _grabLocalDir = joint.T.InverseTransformDirection(
                                (hit.point - joint.T.position).normalized);
                            _emotes?.Interrupt();
                        }
                    }
                    else if (ResolveLimb(hit.transform, out _ikRoot, out _ikMid, out _ikEnd))
                    {
                        _rotateMode = false;
                        _grabbed = null;
                        _grabPlanePoint = hit.point;
                        _ikOffset = _ikEnd.position - hit.point; // grab ANYWHERE, no snap
                        _emotes?.Interrupt();
                    }
                }
            }
            if (!mouse.leftButton.isPressed) { _grabbed = null; _ikRoot = null; _ikMid = null; _ikEnd = null; }

            if (_rotateMode) DragRotate(mouse);
            else DragLimb(mouse);
        }

        /// SHIFT drag: the one grabbed bone rotates so the clicked spot chases
        /// the cursor; scroll twists it. Hinges still obey their axis.
        void DragRotate(Mouse mouse)
        {
            if (_grabbed?.T == null) return;
            var dragRay = _cam.ScreenPointToRay(mouse.position.ReadValue());
            var plane = new Plane(-_cam.transform.forward, _grabPlanePoint);
            if (plane.Raycast(dragRay, out float d))
            {
                Vector3 cursor = dragRay.GetPoint(d);
                Vector3 cur = _grabbed.T.TransformDirection(_grabLocalDir);
                Vector3 want = cursor - _grabbed.T.position;
                if (want.sqrMagnitude > 1e-6f)
                {
                    _grabbed.T.rotation =
                        Quaternion.FromToRotation(cur, want.normalized) * _grabbed.T.rotation;
                    EmoteRig.Constrain(_grabbed); // hinges only bend their way
                }
            }

            float twist = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(twist) > 0.01f)
            {
                Vector3 axis = _grabbed.T.TransformDirection(_grabLocalDir);
                _grabbed.T.rotation =
                    Quaternion.AngleAxis(Mathf.Sign(twist) * 10f, axis) * _grabbed.T.rotation;
                EmoteRig.Constrain(_grabbed); // a hinged joint can't twist free
            }
        }

        /// Plain drag: the whole limb REACHES — the tip chases the cursor
        /// (offset by where you grabbed, so nothing snaps) and a short CCD
        /// pass swings shoulder+elbow (hip+knee) to get it there. Constrain
        /// runs inside the loop, so anatomy holds while the limb finds a way.
        void DragLimb(Mouse mouse)
        {
            if (_ikRoot?.T == null || _ikEnd == null) return;
            var dragRay = _cam.ScreenPointToRay(mouse.position.ReadValue());
            var plane = new Plane(-_cam.transform.forward, _grabPlanePoint);
            if (plane.Raycast(dragRay, out float d))
            {
                Vector3 target = dragRay.GetPoint(d) + _ikOffset;
                for (int i = 0; i < 3; i++) // tiny CCD — converges in a blink
                {
                    if (_ikMid?.T != null)
                    {
                        Vector3 toEnd = _ikEnd.position - _ikMid.T.position;
                        Vector3 toTarget = target - _ikMid.T.position;
                        if (toEnd.sqrMagnitude > 1e-6f && toTarget.sqrMagnitude > 1e-6f)
                        {
                            _ikMid.T.rotation = Quaternion.FromToRotation(toEnd, toTarget) * _ikMid.T.rotation;
                            EmoteRig.Constrain(_ikMid);
                        }
                    }
                    {
                        Vector3 toEnd = _ikEnd.position - _ikRoot.T.position;
                        Vector3 toTarget = target - _ikRoot.T.position;
                        if (toEnd.sqrMagnitude > 1e-6f && toTarget.sqrMagnitude > 1e-6f)
                        {
                            _ikRoot.T.rotation = Quaternion.FromToRotation(toEnd, toTarget) * _ikRoot.T.rotation;
                            EmoteRig.Constrain(_ikRoot);
                        }
                    }
                }
            }

            float twist = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(twist) > 0.01f)
            {
                Vector3 axis = _ikEnd.position - _ikRoot.T.position;
                if (axis.sqrMagnitude > 1e-6f)
                {
                    _ikRoot.T.rotation =
                        Quaternion.AngleAxis(Mathf.Sign(twist) * 10f, axis.normalized) * _ikRoot.T.rotation;
                    EmoteRig.Constrain(_ikRoot);
                }
            }
        }

        /// SHIFT: the FIRST joint at or above the clicked collider — the exact
        /// bone you pointed at, hinge or not.
        EmoteRig.JointEntry NearestJointUp(Transform hitTransform)
        {
            var t = hitTransform;
            while (t != null)
            {
                foreach (var j in _rig.Joints)
                    if (j.T == t) return j;
                if (t == transform) break;
                t = t.parent;
            }
            return null;
        }

        /// Plain click: resolve the whole extremity — walk up remembering any
        /// hinge passed (elbow/knee = mid) until the un-hinged limb joint
        /// (shoulder/hip = root). The tip is the joint's GrabHint (the hand or
        /// foot marker) when set, else the deepest thing you actually clicked.
        bool ResolveLimb(Transform hitTransform, out EmoteRig.JointEntry root,
            out EmoteRig.JointEntry mid, out Transform end)
        {
            root = null; mid = null; end = null;
            var t = hitTransform;
            while (t != null)
            {
                foreach (var j in _rig.Joints)
                {
                    if (j.T != t) continue;
                    if (j.Limited) { if (mid == null) mid = j; }
                    else { root = j; }
                    break;
                }
                if (root != null || t == transform) break;
                t = t.parent;
            }
            if (root == null) return false;
            end = root.GrabHint != null ? root.GrabHint
                : mid?.T != null && mid.T.childCount > 0 ? mid.T.GetChild(0)
                : mid?.T != null ? mid.T
                : root.T.childCount > 0 ? root.T.GetChild(0)
                : root.T;
            return true;
        }
    }
}
