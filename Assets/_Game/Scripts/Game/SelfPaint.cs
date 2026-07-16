using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// BODY PAINT (Marko's spec): R in third person — or in first person with
    /// the wand (slot 1) selected — parks you IDLING exactly as you stand
    /// (current emote pose included) and turns the camera into an easel orbit:
    ///
    ///   MMB-drag → rotate all the way around (even the feet)
    ///   WASD → move the camera up/down/left/right · scroll → zoom
    ///   LMB inks, RMB erases — brush cursor, no aiming
    ///   R (or Esc) → done, camera returns
    ///
    /// You can't walk while painting, but you're NOT safe: gravity, shoves and
    /// damage keep applying (the controller keeps simulating), and getting
    /// floored kicks you out of the mode.
    /// Your own body is invisible to the pen in normal play (Ignore Raycast);
    /// while painting it flips to Default so the ray can land, then flips back.
    public class SelfPaint : MonoBehaviour
    {
        public static bool IsActive { get; private set; }

        /// The painter's root while the mode is open — the pen raycasts ONLY
        /// against this hierarchy (a miss draws NOTHING; never ink on the
        /// world behind you = no accidental spells).
        public static Transform ActiveRoot { get; private set; }

        SimpleFPSController _pilot;
        WeaponSlots _slots;
        Camera _cam;
        Vector3 _camLocalPos;
        Quaternion _camLocalRot;
        float _yaw, _pitch, _dist;
        Vector3 _pan; // WASD camera slide (world offset from the body), MMB rotates
        readonly List<GameObject> _relayered = new List<GameObject>();
        readonly List<(CapsuleCollider col, float radius)> _slimmed
            = new List<(CapsuleCollider, float)>();

        void Awake() => _pilot = GetComponent<SimpleFPSController>();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (PoseStudio.IsOpen || GameMenu.IsOpen || Powerups.IsChoosing || UIKit.Typing)
            {
                if (IsActive) Exit();
                return;
            }

            // a zombie swipe ends the art session
            if (IsActive && _pilot != null
                && (_pilot.IsDowned || _pilot.IsSprawled || _pilot.IsAirTumbling))
            {
                Exit();
                return;
            }

            if (kb.rKey.wasPressedThisFrame)
            {
                if (IsActive) { Exit(); return; }
                if (CanEnter()) Enter();
            }
            if (IsActive && kb.escapeKey.wasPressedThisFrame) { Exit(); return; }
            if (!IsActive) return;

            Orbit();
        }

        /// FIRST person + the wand slot only: with a weapon selected R engraves
        /// the WEAPON, and in third person R belongs to POSE MODE (PoseGrab) —
        /// Marko's split: draw on yourself with the wand, pose from outside.
        bool CanEnter()
        {
            if (_pilot != null
                && (_pilot.IsDowned || _pilot.IsSprawled || _pilot.IsAirTumbling)) return false;
            if (SimpleFPSController.ThirdPersonActive) return false;
            if (_slots == null) _slots = GetComponent<WeaponSlots>();
            return _slots == null || _slots.PenSelected;
        }

        void Enter()
        {
            if (_cam == null) _cam = GetComponentInChildren<Camera>();
            if (_cam == null) return;
            IsActive = true;

            _camLocalPos = _cam.transform.localPosition;
            _camLocalRot = _cam.transform.localRotation;

            // start facing the front, slightly above eye line
            _yaw = transform.eulerAngles.y + 180f;
            _pitch = 12f;
            _dist = 2.3f;
            _pan = Vector3.zero;

            // the pen paints the LIMB CAPSULES on the bones — the body's
            // official ink surfaces (one stroke per limb, joints bridged by
            // strokes meeting: the seal design). The skinned mesh is never a
            // pen target; it's looks-only. Relax the pose first so the canvas
            // is a neutral stance that holds perfectly still.
            var body = GetComponent<CharacterRig>();
            if (body != null && body.HasBody) body.RelaxForPaint();
            _relayered.Clear();
            _slimmed.Clear();

            // THE CANVAS IS THE SKIN: the frozen pose bakes into an invisible
            // collider glued over the visible mesh — ink lands exactly where
            // you see it, capsules never enter into it. Only when the bake is
            // impossible (mesh not readable) do the ragdoll capsules step in.
            bool skin = body != null && body.HasBody && body.BeginBodyPaint();
            Debug.Log(skin
                ? "[SpellyZombie] CANVAS: skin shell ('PaintShell' in the Hierarchy)"
                : "[SpellyZombie] CANVAS: limb capsules — the skin shell was unavailable, see the message above");
            if (!skin)
            {
                bool skipRoot = body != null && body.HasBody; // fat CC capsule stays pen-proof
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.gameObject.layer == 2 && !(skipRoot && t == transform))
                    {
                        _relayered.Add(t.gameObject);
                        t.gameObject.layer = 0;

                        if (skipRoot)
                        {
                            var cap = t.GetComponent<CapsuleCollider>();
                            if (cap != null)
                            {
                                // the auto-fit capsules are fat (ragdoll
                                // padding) — slim them so ink sits nearer
                                // the skin, restored on exit. The TORSO
                                // capsules barely clear the mesh already:
                                // halving them sank chest ink UNDER the
                                // skin (invisible strokes), so they keep
                                // most of their girth.
                                _slimmed.Add((cap, cap.radius));
                                cap.radius *= body != null && body.IsTorsoBone(t) ? 0.85f : 0.5f;
                            }
                        }
                    }
            }

            ActiveRoot = transform;
            Orbit(); // snap the easel into place this frame
            Debug.Log("[SpellyZombie] Body paint — MMB-drag rotates, WASD moves the camera, scroll zooms, LMB ink / RMB erase, R done");
        }

        void Exit()
        {
            IsActive = false;
            ActiveRoot = null;
            // skin-shell ink moves onto the bones so it rides the animation;
            // fallback capsule ink already lives there
            GetComponent<CharacterRig>()?.EndBodyPaint();
            foreach (var go in _relayered)
                if (go != null) go.layer = 2;
            _relayered.Clear();
            foreach (var (cap, radius) in _slimmed)
                if (cap != null) cap.radius = radius; // ragdoll gets its padding back
            _slimmed.Clear();
            if (_cam != null)
            {
                _cam.transform.localPosition = _camLocalPos;
                _cam.transform.localRotation = _camLocalRot;
            }
        }

        void OnDestroy()
        {
            if (IsActive) IsActive = false;
            if (ActiveRoot == transform) ActiveRoot = null;
        }

        void Orbit()
        {
            var mouse = Mouse.current;
            if (mouse == null || _cam == null) return;

            // MMB-drag rotates; WASD MOVES the camera (screen-space slide,
            // Marko's spec) — W/S up/down, A/D left/right
            if (mouse.middleButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yaw += d.x * 0.3f;
                _pitch = Mathf.Clamp(_pitch - d.y * 0.3f, -85f, 85f);
            }
            float zoom = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(zoom) > 0.01f)
                _dist = Mathf.Clamp(_dist * (1f - Mathf.Sign(zoom) * 0.12f), 0.9f, 4.5f);

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);

            var kb = Keyboard.current;
            if (kb != null)
            {
                float panX = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float panY = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                if (panX != 0f || panY != 0f)
                {
                    // slide in the current view plane, a bit faster zoomed out
                    float speed = 0.6f * _dist;
                    _pan += (rot * new Vector3(panX, panY, 0f)) * speed * Time.deltaTime;
                    _pan = Vector3.ClampMagnitude(_pan, 2.2f); // never lose the body
                }
            }

            // player root sits mid-body (the CC is centered on it) — orbiting
            // this point keeps head AND feet reachable
            Vector3 focus = transform.position + _pan;
            _cam.transform.position = focus + rot * new Vector3(0f, 0f, -_dist);
            _cam.transform.rotation = rot;
        }
    }
}
