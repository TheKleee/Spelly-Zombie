using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// ★ THE CREATORS' FLYING EYE (the Map Creator and the Photo Booth): the
    /// right button looks, the middle one drags the view with the grabbed
    /// ground under the pointer, the wheel moves an eighth of the way to what
    /// the view looks at, WASD flies, Space rises and Ctrl sinks, Shift is
    /// three times faster.
    public class CreatorFly
    {
        public const float Speed = 24f;
        public readonly Camera Cam;
        public readonly Transform Rig;
        public float Yaw, Pitch = 40f;
        bool _looking;
        float _grabDepth = -1f; // the middle button's grab: how far the grabbed point lies, -1 = not grabbing
        float _ctrlSunk;        // metres Ctrl has sunk the camera since it went down
        bool _chord;            // Ctrl was part of a shortcut: no sinking until it is let go
        readonly int _ground;   // the layers that count as ground

        public CreatorFly(Camera cam, int groundMask)
        {
            Cam = cam;
            Rig = cam.transform;
            _ground = groundMask;
        }

        public void Face(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
            Rig.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
        }

        /// Something else took the input: nothing stays held.
        public void Release()
        {
            _looking = false;
            _grabDepth = -1f;
        }

        /// A shortcut used Ctrl: whatever it sank the camera comes back.
        public void Chord()
        {
            if (_ctrlSunk > 0f && Rig != null) Rig.position += Vector3.up * _ctrlSunk;
            _ctrlSunk = 0f;
            _chord = true;
        }

        /// One frame of flying. `wheel` false = the wheel is busy elsewhere this frame.
        public void Tick(Keyboard kb, Mouse mouse, bool typing, bool wheel = true)
        {
            float dt = Time.unscaledDeltaTime;

            // looking: the right button, begun off the windows
            if (mouse.rightButton.wasPressedThisFrame) _looking = !MapCreator.OverUI();
            if (!mouse.rightButton.isPressed) _looking = false;
            if (_looking)
            {
                Vector2 d = mouse.delta.ReadValue();
                Yaw += d.x * 0.15f;
                Pitch = Mathf.Clamp(Pitch - d.y * 0.15f, -89f, 89f);
                Rig.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            }
            // grabbing: the middle button drags the view, the grabbed ground stays under the pointer
            if (mouse.middleButton.wasPressedThisFrame) _grabDepth = MapCreator.OverUI() ? -1f : GrabDepth(mouse.position.ReadValue());
            if (!mouse.middleButton.isPressed) _grabDepth = -1f;
            if (_grabDepth > 0f)
            {
                Vector2 d = mouse.delta.ReadValue();
                float perPixel = 2f * _grabDepth * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, Cam.pixelHeight);
                Rig.position -= (Rig.right * d.x + Rig.up * d.y) * perPixel;
            }
            float fast = kb.leftShiftKey.isPressed ? 3f : 1f;
            float scroll = mouse.scroll.ReadValue().y;
            if (wheel && Mathf.Abs(scroll) > 0.01f && !MapCreator.OverUI())
                Rig.position += Rig.forward * (Mathf.Sign(scroll) * WheelStep() * fast);

            if (!kb.leftCtrlKey.isPressed) { _chord = false; _ctrlSunk = 0f; }
            if (!typing)
            {
                // Space and Ctrl like a driven ghost
                bool sink = kb.leftCtrlKey.isPressed && !_chord;
                Vector3 move = Vector3.zero;
                if (kb.wKey.isPressed) move += Rig.forward;
                if (kb.sKey.isPressed) move -= Rig.forward;
                if (kb.dKey.isPressed) move += Rig.right;
                if (kb.aKey.isPressed) move -= Rig.right;
                if (kb.spaceKey.isPressed) move += Vector3.up;
                if (sink) { move -= Vector3.up; _ctrlSunk += Speed * fast * dt; }
                Rig.position += move * (Speed * fast * dt);
            }
        }

        /// How deep the ground under the pointer lies along the view, so a grab moves it one for one.
        float GrabDepth(Vector2 screen)
        {
            if (!MapCreator.GroundUnder(Cam.ScreenPointToRay(screen), null, out var at, _ground)) return 40f;
            return Mathf.Max(1f, Vector3.Dot(at - Rig.position, Rig.forward));
        }

        /// A wheel notch covers an eighth of the way to what the view looks at.
        float WheelStep()
        {
            float reach = MapCreator.GroundUnder(new Ray(Rig.position, Rig.forward), null, out var at, _ground)
                ? Vector3.Distance(Rig.position, at) : 40f;
            return Mathf.Clamp(reach * 0.125f, 1f, 40f);
        }
    }
}
