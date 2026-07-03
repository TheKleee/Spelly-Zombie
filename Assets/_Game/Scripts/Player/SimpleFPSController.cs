using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Graybox first-person controller: WASD + mouse look + jump, pushes rigidbodies
    /// (needed to shove crates together and close proximity seals). Holding LeftAlt
    /// frees the cursor for precision drawing and freezes the view.
    [RequireComponent(typeof(CharacterController))]
    public class SimpleFPSController : MonoBehaviour
    {
        public Transform CameraPivot;
        public float MoveSpeed = 4.5f;
        public float SprintSpeed = 7f;
        public float JumpSpeed = 4.6f;
        public float Gravity = -14f;
        public float LookSensitivity = 0.12f;
        public float PushStrength = 1.6f;

        CharacterController _cc;
        float _pitch;
        float _verticalVelocity;
        bool _wasPrecision;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            LockCursor();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            // ---- cursor handling ----
            bool precision = kb.leftAltKey.isPressed;
            if (precision)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (_wasPrecision)
            {
                LockCursor();
            }
            else if (kb.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
            {
                LockCursor();
            }
            _wasPrecision = precision;

            // ---- look (only while the cursor is captured, and not while a
            //      limb is being dragged in the pose editor) ----
            if (Cursor.lockState == CursorLockMode.Locked && !PoseEditor.IsRotatingJoint)
            {
                // steady the hand: the camera slows way down while ink is flowing
                float sens = SurfaceDrawer.IsPenActive
                    ? LookSensitivity * DrawingConfig.DrawLookSensitivityScale
                    : LookSensitivity;
                Vector2 d = mouse.delta.ReadValue() * sens;
                transform.Rotate(0f, d.x, 0f);
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
                if (CameraPivot != null)
                    CameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }

            // ---- move ----
            Vector2 mv = Vector2.zero;
            if (kb.wKey.isPressed) mv.y += 1f;
            if (kb.sKey.isPressed) mv.y -= 1f;
            if (kb.dKey.isPressed) mv.x += 1f;
            if (kb.aKey.isPressed) mv.x -= 1f;
            if (mv.sqrMagnitude > 1f) mv.Normalize();

            float speed = kb.leftShiftKey.isPressed ? SprintSpeed : MoveSpeed;
            Vector3 planar = (transform.right * mv.x + transform.forward * mv.y) * speed;

            if (_cc.isGrounded)
            {
                _verticalVelocity = -1f;
                if (kb.spaceKey.wasPressedThisFrame)
                    _verticalVelocity = JumpSpeed;
            }
            else
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }

            _cc.Move((planar + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // shove crates around so half-drawn arcs on two objects can meet and seal
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var body = hit.collider.attachedRigidbody;
            if (body == null || body.isKinematic) return;
            if (hit.moveDirection.y < -0.3f) return; // don't push things we stand on

            Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            body.linearVelocity = new Vector3(dir.x * PushStrength, body.linearVelocity.y, dir.z * PushStrength);
        }
    }
}
