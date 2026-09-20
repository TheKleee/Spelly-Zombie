using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// ★ LIMB SCULPTING, shared by pose mode (PoseGrab) and the Photo Booth.
    /// A plain drag reaches a whole limb (a miss grabs the limb whose tip is
    /// nearest on screen); Shift drag arcball-rotates one bone; the wheel
    /// twists what is held. Hinges only bend their way.
    public class PoseSculpt
    {
        readonly Camera _cam;
        readonly EmoteRig _rig;
        readonly Transform _root;

        // SHIFT rotates one bone; plain drag reaches a whole limb
        EmoteRig.JointEntry _grabbed;          // shift mode: the bone being rotated
        bool _rotateMode;                      // true = shift bone-rotate
        EmoteRig.JointEntry _ikRoot, _ikMid;   // limb mode: shoulder/hip + elbow/knee
        Transform _ikEnd;                      // the limb tip that chases the cursor
        Vector3 _ikOffset;                     // tip − click point (no snap on grab)
        Vector3 _arcPrev;                      // last arcball point (shift rotate)

        public PoseSculpt(Camera cam, EmoteRig rig, Transform root)
        {
            _cam = cam;
            _rig = rig;
            _root = root;
        }

        public bool Uses(Camera cam, EmoteRig rig) => _cam == cam && _rig == rig;

        /// A bone or a limb is held: the wheel twists it.
        public bool Holding => _grabbed != null || _ikRoot != null;

        /// Lets go of the bone a Shift drag holds.
        public void ReleaseBone() => _grabbed = null;

        /// Lets go of everything.
        public void Drop() { _grabbed = null; _ikRoot = null; _ikMid = null; _ikEnd = null; }

        /// One frame of the left button. `mask` and `reach` are what a click can
        /// land on; `grabbed` runs whenever a grab begins.
        public void Tick(Mouse mouse, int mask, float reach, System.Action grabbed)
        {
            if (_cam == null || _rig == null) return;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 mp = mouse.position.ReadValue();
                var ray = _cam.ScreenPointToRay(mp);
                var kb = Keyboard.current;
                bool fine = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                bool hitBody = Physics.Raycast(ray, out var hit, reach, mask, QueryTriggerInteraction.Ignore);

                if (fine)
                {
                    // shift: the clicked bone, else the nearest pivot within a tight radius
                    var joint = hitBody ? _rig.JointAtOrAbove(hit.transform) : null;
                    if (joint == null) joint = NearestJointScreen(mp, 60f);
                    if (joint != null)
                    {
                        _rotateMode = true;
                        _grabbed = joint;
                        _arcPrev = ArcPoint(mp, joint.T.position);
                        grabbed?.Invoke();
                    }
                }
                else
                {
                    // plain click: the clicked extremity, or on a miss the
                    // limb whose tip is nearest on screen
                    bool got = (hitBody && ResolveLimb(hit.transform, out _ikRoot, out _ikMid, out _ikEnd))
                        || NearestLimbScreen(mp, 120f, out _ikRoot, out _ikMid, out _ikEnd);
                    if (got)
                    {
                        _rotateMode = false;
                        _grabbed = null;
                        // offset captured on the drag plane so frame one's
                        // target is the tip's current spot (no snap on grab)
                        var plane = new Plane(-_cam.transform.forward, _ikRoot.T.position);
                        _ikOffset = plane.Raycast(ray, out float d0)
                            ? _ikEnd.position - ray.GetPoint(d0)
                            : Vector3.zero;
                        grabbed?.Invoke();
                    }
                }
            }
            if (!mouse.leftButton.isPressed) Drop();

            if (_rotateMode) DragRotate(mouse);
            else DragLimb(mouse);
        }

        /// Shift drag: arcball. Inside the ball tumbles the bone, around the
        /// rim twists it. Scroll twists around the bone's own axis.
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

        /// Plain drag: the tip chases the cursor (offset by the grab point)
        /// and a short CCD pass swings shoulder+elbow (hip+knee) to get
        /// there. Constrain runs inside the loop so anatomy holds.
        void DragLimb(Mouse mouse)
        {
            if (_ikRoot?.T == null || _ikEnd == null) return;
            var dragRay = _cam.ScreenPointToRay(mouse.position.ReadValue());
            // plane through the limb root: cursor motion maps 1:1 onto the reach
            var plane = new Plane(-_cam.transform.forward, _ikRoot.T.position);
            if (plane.Raycast(dragRay, out float d))
            {
                Vector3 target = dragRay.GetPoint(d) + _ikOffset;
                // partial steps (0.6 per iteration, ×3 ≈ 95%): full-strength
                // FromToRotation flips 180° when the cursor crosses the root
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

        /// The joint whose pivot is nearest the cursor on screen, within maxPx.
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

        /// Resolve the whole extremity: walk up remembering any hinge passed
        /// (elbow/knee = mid) until the un-hinged limb joint (shoulder/hip =
        /// root); the tip is the joint's GrabHint when set.
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
                if (root != null || t == _root) break;
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
