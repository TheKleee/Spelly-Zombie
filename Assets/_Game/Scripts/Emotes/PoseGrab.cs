using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Pose mode: in third person R opens an orbiting easel around the frozen
    /// wizard. LMB-drag reaches a whole limb (a miss grabs the nearest limb);
    /// Shift+LMB arcball-rotates one bone; scroll twists; WASD/MMB orbit,
    /// wheel zooms; 1-9 load a pose, hold or Ctrl+1-9 save, F relax, R done.
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

        // the limb sculpting, shared with the Photo Booth
        PoseSculpt _sculpt;

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

        // a stuck IsOpen would make the controller skip its camera block forever
        void OnDisable()
        {
            if (IsOpen) Close();
            IsOpen = false;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            bool available = SimpleFPSController.ThirdPersonActive
                && !PoseStudio.IsOpen && !SelfPaint.IsActive
                && !GameMenu.IsOpen && !UIKit.Typing
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
            if (_sculpt == null || !_sculpt.Uses(_cam, _rig)) _sculpt = new PoseSculpt(_cam, _rig, transform);

            // acolytes never pose-sculpt; R in their third person is the shape pose mode
            if (Sides.Of(Grimoire.LocalPlayerId) == Side.Acolyte || ShapeShift.LocalIsShaped)
            {
                if (IsOpen) Close();
                return;
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                if (IsOpen) { Close(); return; }
                Open();
            }
            if (!IsOpen) return;
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }

            // F relaxes to rest unless the grimoire has a target (declare/absorb owns F)
            if (kb.fKey.wasPressedThisFrame
                && !GrimoireAbsorb.DeclareInReach && !GrimoireAbsorb.TargetInReach)
            {
                _sculpt.ReleaseBone();
                _emotes?.Interrupt();
                foreach (var j in _rig.Joints)
                    if (j.T != null) j.T.localRotation = j.Rest;
            }

            // tap = load, hold 0.6s = save, Ctrl+number = instant save
            // (the Unity editor eats Ctrl+numbers - holding works everywhere)
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
            // the limbs live on Ignore Raycast - include it explicitly
            _sculpt.Tick(mouse, Physics.DefaultRaycastLayers | (1 << 2), 30f, () => _emotes?.Interrupt());
            NetSync.PushLiveEmote(_rig); // friends' puppets follow the sculpt
        }

        static PoseGrab _live;

        /// Shove closes the easel - same rule as SelfPaint.
        public static void Blown() { if (IsOpen && _live != null) _live.Close(); }

        void Open()
        {
            IsOpen = true;
            _live = this;
            _camLocalPos = _cam.transform.localPosition;
            _camLocalRot = _cam.transform.localRotation;
            _yaw = transform.eulerAngles.y + 180f; // start facing the wizard
            _pitch = 10f;
            _dist = 2.6f;
            _pan = Vector3.zero;
            _emotes?.Interrupt(); // the doll holds whatever it's doing
            NetSync.BeginLiveEmote();
            ApplyOrbit();
        }

        void Close()
        {
            IsOpen = false;
            NetSync.EndLiveEmote();
            _sculpt?.Drop();
            if (_cam != null)
            {
                _cam.transform.localPosition = _camLocalPos;
                _cam.transform.localRotation = _camLocalRot;
            }
            // unsaved sculpts relax when the animator resumes
        }

        void Orbit(Keyboard kb, Mouse mouse)
        {
            // wheel zooms ONLY when nothing is held (held = twist)
            var rot = EaselOrbit.Tick(kb, mouse, ref _yaw, ref _pitch, ref _dist, ref _pan,
                allowZoom: _sculpt == null || !_sculpt.Holding, zoomMin: 1.0f);
            ApplyOrbit(rot);
        }

        void ApplyOrbit() => ApplyOrbit(Quaternion.Euler(_pitch, _yaw, 0f));

        void ApplyOrbit(Quaternion rot)
            => EaselOrbit.Apply(_cam, transform.position + _pan, rot, _dist);
    }

    /// Easel orbit - MMB rotates, WASD pans (clamped 2.2), scroll zooms.
    /// Shared by PoseGrab and SelfPaint.
    public static class EaselOrbit
    {
        public static Quaternion Tick(Keyboard kb, Mouse mouse, ref float yaw,
            ref float pitch, ref float dist, ref Vector3 pan,
            bool allowZoom, float zoomMin, float zoomMax = 4.5f)
        {
            if (mouse.middleButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * 0.3f;
                pitch = Mathf.Clamp(pitch - d.y * 0.3f, -85f, 85f);
            }
            float zoom = mouse.scroll.ReadValue().y;
            // the wheel over the floating book turns its pages, not the zoom
            if (allowZoom && !GrimoirePages.WheelOnBook && Mathf.Abs(zoom) > 0.01f)
                dist = Mathf.Clamp(dist * (1f - Mathf.Sign(zoom) * 0.12f), zoomMin, zoomMax);

            var rot = Quaternion.Euler(pitch, yaw, 0f);
            if (kb != null)
            {
                float panX = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float panY = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                if (panX != 0f || panY != 0f)
                {
                    // slide in the current view plane, a bit faster zoomed out
                    pan += (rot * new Vector3(panX, panY, 0f)) * (0.6f * dist) * Time.deltaTime;
                    pan = Vector3.ClampMagnitude(pan, 2.2f);
                }
            }
            return rot;
        }

        public static void Apply(Camera cam, Vector3 focus, Quaternion rot, float dist)
        {
            cam.transform.position = focus + rot * new Vector3(0f, 0f, -dist);
            cam.transform.rotation = rot;
        }

        /// Borrowed-camera lifecycle: remembers the camera's local pose,
        /// keeps yaw/pitch/dist/pan, restores it on close.
        /// Borrow(), Orbit() each frame, Release() when the mode closes.
        public class Borrowed
        {
            public float Yaw, Pitch, Dist = 3.5f;
            public Vector3 Pan;

            Camera _cam;
            Vector3 _homePos;
            Quaternion _homeRot;

            public Camera Cam => _cam;
            public bool Held => _cam != null;

            public void Borrow(Camera cam, float yaw, float pitch, float dist)
            {
                if (cam == null) return;
                _cam = cam;
                _homePos = cam.transform.localPosition;
                _homeRot = cam.transform.localRotation;
                Yaw = yaw; Pitch = pitch; Dist = dist; Pan = Vector3.zero;
            }

            /// One frame of orbiting a focus point. zoomMax 0 forbids zooming out.
            public void Orbit(Keyboard kb, Mouse mouse, Vector3 focus,
                float zoomMin = 1f, float zoomMax = 4.5f)
            {
                if (_cam == null) return;
                Quaternion rot = kb != null && mouse != null && zoomMax > zoomMin
                    ? Tick(kb, mouse, ref Yaw, ref Pitch, ref Dist, ref Pan, true, zoomMin, zoomMax)
                    : Quaternion.Euler(Pitch, Yaw, 0f);
                Dist = Mathf.Clamp(Dist, zoomMin, Mathf.Max(zoomMin, zoomMax));
                Apply(_cam, focus + Pan, rot, Dist);
            }

            /// Idempotent - safe to call from multiple close paths.
            public void Release()
            {
                if (_cam == null) return;
                _cam.transform.localPosition = _homePos;
                _cam.transform.localRotation = _homeRot;
                _cam = null;
            }
        }
    }
}
