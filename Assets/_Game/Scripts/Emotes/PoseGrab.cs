using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// POSE MODE (Marko's spec, reworked Jul 22): in THIRD person press R —
    /// the camera becomes an orbiting easel around your frozen wizard:
    ///
    ///   LMB-drag a limb — the WHOLE extremity follows your cursor from
    ///     wherever you grabbed it (two-bone reach, elbows/knees obey hinges);
    ///     a click that misses the body still grabs the nearest limb on screen
    ///   SHIFT+LMB — THAT BONE ONLY, on an arcball: drag inside the invisible
    ///     ball to tumble it in ANY direction, drag around the ball's rim to
    ///     TWIST it (Marko's sketch: "Allow for twisting as well!")
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
        Vector3 _arcPrev;                      // last arcball point (shift rotate)

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

        // the static must NEVER outlive the component (Marko: entering a
        // pose in plain third person moved the camera "as if it was in the
        // pose mode" — a stuck IsOpen makes the controller skip its camera
        // block forever, since only Close() ever clears it)
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

            // ACOLYTES NEVER POSE-SCULPT (Marko Aug 10: their third person is
            // ONLY the transformation view; R there is the shape pose mode)
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

            // F melts the sculpt back to rest — unless the grimoire has a
            // target (declare/absorb owns F for that press)
            if (kb.fKey.wasPressedThisFrame
                && !GrimoireAbsorb.DeclareInReach && !GrimoireAbsorb.TargetInReach)
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
            // wheel zooms ONLY when nothing is held (held = twist)
            var rot = EaselOrbit.Tick(kb, mouse, ref _yaw, ref _pitch, ref _dist, ref _pan,
                allowZoom: _grabbed == null && _ikRoot == null, zoomMin: 1.0f);
            ApplyOrbit(rot);
        }

        void ApplyOrbit() => ApplyOrbit(Quaternion.Euler(_pitch, _yaw, 0f));

        void ApplyOrbit(Quaternion rot)
            => EaselOrbit.Apply(_cam, transform.position + _pan, rot, _dist);

        void HandleGrab(Mouse mouse)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 mp = mouse.position.ReadValue();
                var ray = _cam.ScreenPointToRay(mp);
                // the limbs live on Ignore Raycast — include it explicitly
                int mask = Physics.DefaultRaycastLayers | (1 << 2);
                var kb = Keyboard.current;
                bool fine = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                bool hitBody = Physics.Raycast(ray, out var hit, 30f, mask, QueryTriggerInteraction.Ignore);

                if (fine)
                {
                    // SHIFT = THE BONE, ONLY THE BONE (Marko: "I only want to
                    // be selecting the bone"): the one you clicked, or the one
                    // whose pivot is nearest the cursor — within a tight
                    // radius, so a far click never grabs something surprising.
                    var joint = hitBody ? _rig.JointAtOrAbove(hit.transform) : null;
                    if (joint == null) joint = NearestJointScreen(mp, 60f);
                    if (joint != null)
                    {
                        _rotateMode = true;
                        _grabbed = joint;
                        _arcPrev = ArcPoint(mp, joint.T.position);
                        _emotes?.Interrupt();
                    }
                }
                else
                {
                    // plain click: the clicked extremity — or, on a miss, the
                    // limb whose tip is nearest on screen (thin arms are hard
                    // to hit and that was the whole frustration)
                    bool got = (hitBody && ResolveLimb(hit.transform, out _ikRoot, out _ikMid, out _ikEnd))
                        || NearestLimbScreen(mp, 120f, out _ikRoot, out _ikMid, out _ikEnd);
                    if (got)
                    {
                        _rotateMode = false;
                        _grabbed = null;
                        // NO-TELEPORT GRAB (Marko: clicking after orbiting
                        // teleported the limb): capture the offset ON THE
                        // DRAG PLANE itself, so frame one's target IS the
                        // tip's current spot — every drag moves the limb
                        // from where it stands, from any camera angle.
                        var plane = new Plane(-_cam.transform.forward, _ikRoot.T.position);
                        _ikOffset = plane.Raycast(ray, out float d0)
                            ? _ikEnd.position - ray.GetPoint(d0)
                            : Vector3.zero;
                        _emotes?.Interrupt();
                    }
                }
            }
            if (!mouse.leftButton.isPressed) { _grabbed = null; _ikRoot = null; _ikMid = null; _ikEnd = null; }

            if (_rotateMode) DragRotate(mouse);
            else DragLimb(mouse);
        }

        /// SHIFT drag: ARCBALL — the bone sits inside an invisible trackball
        /// under your cursor. Dragging across the middle tumbles it in any
        /// direction; dragging around the RIM rolls it — that's the twist
        /// (Marko's sketch: "Allow for twisting as well!"). Continuous by
        /// construction: no plane crossings, no direction flips, no
        /// spaghetti. Scroll still twists around the bone's own axis.
        void DragRotate(Mouse mouse)
        {
            if (_grabbed?.T == null) return;
            Vector3 cur = ArcPoint(mouse.position.ReadValue(), _grabbed.T.position);
            if (Vector3.Angle(_arcPrev, cur) > 0.01f)
            {
                _grabbed.T.rotation =
                    Quaternion.FromToRotation(_arcPrev, cur) * _grabbed.T.rotation;
                EmoteRig.Constrain(_grabbed); // hinges only bend their way
                _arcPrev = cur;
            }

            float twist = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(twist) > 0.01f)
            {
                _grabbed.T.rotation =
                    Quaternion.AngleAxis(Mathf.Sign(twist) * 10f, SegmentDir(_grabbed))
                    * _grabbed.T.rotation;
                EmoteRig.Constrain(_grabbed); // a hinged joint can't twist free
            }
        }

        /// Map a screen point onto the virtual trackball around a pivot.
        /// Inside the ball = a 3D point on its surface (tumble); outside =
        /// the hyperbolic sheet along the rim (pure roll/twist).
        Vector3 ArcPoint(Vector2 screen, Vector3 pivot)
        {
            const float RadiusPx = 120f;
            Vector3 c = _cam.WorldToScreenPoint(pivot);
            Vector2 d = (screen - (Vector2)c) / RadiusPx;
            float l2 = d.sqrMagnitude;
            Vector3 v = l2 <= 0.5f
                ? new Vector3(d.x, d.y, Mathf.Sqrt(1f - l2))
                : new Vector3(d.x, d.y, 0.5f / Mathf.Sqrt(l2));
            v.Normalize();
            return (_cam.transform.right * v.x
                  + _cam.transform.up * v.y
                  - _cam.transform.forward * v.z).normalized;
        }

        /// The bone's own long axis — what a "twist" spins around.
        Vector3 SegmentDir(EmoteRig.JointEntry j)
            => j.T.childCount > 0
                ? (j.T.GetChild(0).position - j.T.position).normalized
                : (HandleTip(j) - j.T.position).sqrMagnitude > 1e-6f
                    ? (HandleTip(j) - j.T.position).normalized
                    : _cam.transform.forward;

        /// Plain drag: the whole limb REACHES — the tip chases the cursor
        /// (offset by where you grabbed, so nothing snaps) and a short CCD
        /// pass swings shoulder+elbow (hip+knee) to get it there. Constrain
        /// runs inside the loop, so anatomy holds while the limb finds a way.
        void DragLimb(Mouse mouse)
        {
            if (_ikRoot?.T == null || _ikEnd == null) return;
            var dragRay = _cam.ScreenPointToRay(mouse.position.ReadValue());
            // plane through the limb ROOT — cursor motion maps 1:1 onto the
            // limb's reach all the way through the drag
            var plane = new Plane(-_cam.transform.forward, _ikRoot.T.position);
            if (plane.Raycast(dragRay, out float d))
            {
                Vector3 target = dragRay.GetPoint(d) + _ikOffset;
                // PARTIAL steps (0.6 per iteration, ×3 ≈ 95%): full-strength
                // FromToRotation snapped 180° when the cursor crossed the
                // root — that was the spaghetti. Damped pursuit can't flip.
                for (int i = 0; i < 3; i++)
                {
                    if (_ikMid?.T != null)
                    {
                        Vector3 toEnd = _ikEnd.position - _ikMid.T.position;
                        Vector3 toTarget = target - _ikMid.T.position;
                        if (toEnd.sqrMagnitude > 1e-6f && toTarget.sqrMagnitude > 1e-6f)
                        {
                            _ikMid.T.rotation = Quaternion.Slerp(Quaternion.identity,
                                Quaternion.FromToRotation(toEnd, toTarget), 0.6f) * _ikMid.T.rotation;
                            EmoteRig.Constrain(_ikMid);
                        }
                    }
                    {
                        Vector3 toEnd = _ikEnd.position - _ikRoot.T.position;
                        Vector3 toTarget = target - _ikRoot.T.position;
                        if (toEnd.sqrMagnitude > 1e-6f && toTarget.sqrMagnitude > 1e-6f)
                        {
                            _ikRoot.T.rotation = Quaternion.Slerp(Quaternion.identity,
                                Quaternion.FromToRotation(toEnd, toTarget), 0.6f) * _ikRoot.T.rotation;
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

        /// Where a joint's visible "handle" sits — the hand/foot marker when
        /// one exists, else the bone's child, else the pivot itself.
        Vector3 HandleTip(EmoteRig.JointEntry j)
            => j.GrabHint != null && j.GrabHint != j.T ? j.GrabHint.position
             : j.T.childCount > 0 ? j.T.GetChild(0).position
             : j.T.position;

        /// The joint whose PIVOT is nearest the cursor on screen, within
        /// maxPx — the bone you meant, never a surprise across the body.
        EmoteRig.JointEntry NearestJointScreen(Vector2 mp, float maxPx)
        {
            EmoteRig.JointEntry best = null;
            float bd = maxPx * maxPx;
            foreach (var j in _rig.Joints)
            {
                if (j.T == null) continue;
                Vector3 sp = _cam.WorldToScreenPoint(j.T.position);
                if (sp.z < 0f) continue; // behind the camera
                float d = ((Vector2)sp - mp).sqrMagnitude;
                if (d < bd) { bd = d; best = j; }
            }
            return best;
        }

        /// Plain-click fallback: the LIMB (root + optional hinge + tip) whose
        /// tip is within maxPx of the cursor on screen.
        bool NearestLimbScreen(Vector2 mp, float maxPx,
            out EmoteRig.JointEntry root, out EmoteRig.JointEntry mid, out Transform end)
        {
            root = null; mid = null; end = null;
            float bd = maxPx * maxPx;
            foreach (var j in _rig.Joints)
            {
                if (j.T == null || j.Limited) continue; // roots only
                Vector3 sp = _cam.WorldToScreenPoint(HandleTip(j));
                if (sp.z < 0f) continue;
                float d = ((Vector2)sp - mp).sqrMagnitude;
                if (d < bd) { bd = d; root = j; }
            }
            if (root == null) return false;
            // the hinge on the way to the tip, when the rig has one
            foreach (var j2 in _rig.Joints)
                if (j2.Limited && j2.T != null && j2.T.IsChildOf(root.T)
                    && root.GrabHint != null && root.GrabHint.IsChildOf(j2.T))
                { mid = j2; break; }
            end = root.GrabHint != null ? root.GrabHint
                : mid?.T != null && mid.T.childCount > 0 ? mid.T.GetChild(0)
                : mid?.T != null ? mid.T
                : root.T.childCount > 0 ? root.T.GetChild(0)
                : root.T;
            return true;
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

    /// THE easel orbit — MMB rotates, WASD pans (clamped 2.2 so the body is
    /// never lost), scroll zooms. PoseGrab and SelfPaint share this one
    /// camera language (it was implemented twice, drifting apart).
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
            if (allowZoom && Mathf.Abs(zoom) > 0.01f)
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
    }
}
