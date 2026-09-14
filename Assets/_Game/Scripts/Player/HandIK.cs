using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Hand IK through the Animator's IK pass: a held weapon gets both hands,
    /// the pen slot holds wand + grimoire in front of the view. Lives on the
    /// model object (OnAnimatorIK must share the Animator's GameObject).
    public class HandIK : MonoBehaviour
    {
        public WeaponSlots Slots;
        public Transform Pivot; // the camera pivot - pen/grimoire anchor
        /// Set on a friend's puppet: stances come from their presence, never
        /// from the local statics (whose hands would otherwise reach for OUR load).
        [System.NonSerialized] public NetAvatar Puppet;

        Animator _anim;
        float _weight;
        float _supportWeight; // the book hand: raised only while the grimoire is OPEN
        Vector3 _grip, _support; // last targets, for the ease-out

        // pen stance blends READING (book up) and CASTING (wand thrust, book
        // tucked). Reading has two spots: the corner, where the open book hints
        // without covering the view, and ReadUp, where the hand brings it when
        // you look down at it. Stances live in IKAnchor_* children under the
        // camera pivot, adjustable in play mode via Character Fix.
        Vector3 _penGrip, _penSupport;
        static readonly Vector3 ReadGripDefault = new Vector3(0.17f, -0.25f, 0.38f);
        // the baked Player.prefab IKAnchor_* transforms override these
        // defaults - edit the prefab values, or changes here silently do nothing
        static readonly Vector3 ReadSupportDefault = new Vector3(-0.26f, -0.21f, 0.52f);
        static readonly Vector3 CastGripDefault = new Vector3(0.14f, -0.16f, 0.56f);
        static readonly Vector3 CastSupportDefault = new Vector3(-0.25f, -0.36f, 0.28f);
        Transform _aReadGrip, _aReadSupport, _aCastGrip, _aCastSupport;
        static readonly Vector3 ReadUpDefault = new Vector3(-0.10f, -0.17f, 0.48f);
        Transform _aReadUp;
        float _readUp;   // 0 = the corner, 1 = raised to read
        bool _reading;   // set by the look gesture, cleared by any look away
        Vector3 _lastFlat; float _lastBelow; bool _lookSeen;
        Vector2 _lookVel; // degrees per second: x right, y down
        bool _carried;    // last pass had both hands on a load

        void Awake() => _anim = GetComponent<Animator>();

        Transform Anchor(ref Transform cache, string anchorName, Vector3 def)
        {
            if (cache != null || Pivot == null) return cache;
            var t = Pivot.Find(anchorName);
            if (t == null)
            {
                t = new GameObject(anchorName).transform;
                t.SetParent(Pivot, false);
                t.localPosition = def;
            }
            cache = t;
            return cache;
        }

        /// A puppet's hands: the wand thrust while their pen is down, the
        /// book raised while it is open, both hands on what they carry. The
        /// stances hang off a virtual pivot at their head, facing their look.
        void PuppetIK()
        {
            var head = Puppet.Head;
            Vector3 pivotPos = head != null ? head.position : Puppet.transform.position + Vector3.up * 1.5f;
            var pivotRot = Quaternion.Euler(Puppet.Pitch, Puppet.transform.eulerAngles.y, 0f);
            Vector3 At(Vector3 local) => pivotPos + pivotRot * local;

            var held = Puppet.HeldTransform;
            bool carry = held != null || Puppet.HandsFull;
            bool cast = !carry && Puppet.PenDown;
            bool read = !carry && !cast && Puppet.BookOpen;
            Vector3 grip, support;
            if (carry)
            {
                Vector3 c;
                float half = 0.18f;
                if (held != null)
                {
                    var rb = held.GetComponent<Rigidbody>();
                    c = rb != null ? rb.worldCenterOfMass : held.position;
                    var cargoCol = held.GetComponent<Collider>();
                    if (cargoCol != null)
                        half = Mathf.Clamp(cargoCol.bounds.extents.magnitude * 0.55f, 0.14f, 0.5f);
                }
                else c = At(new Vector3(0f, -0.25f, 0.45f)); // a load this machine cannot see
                Vector3 side = _anim.transform.right * half;
                grip = c + side;
                support = c - side;
            }
            else if (cast)
            {
                grip = At(CastGripDefault);
                support = At(CastSupportDefault);
            }
            else
            {
                grip = At(ReadGripDefault);
                support = At(ReadSupportDefault);
            }
            bool any = carry || cast || read;
            if (_grip == Vector3.zero) { _grip = grip; _support = support; }
            _grip = Vector3.Lerp(_grip, grip, Time.deltaTime * 7f);
            _support = Vector3.Lerp(_support, support, Time.deltaTime * 7f);
            _weight = Mathf.MoveTowards(_weight, any ? 1f : 0f, Time.deltaTime * 5f);
            _supportWeight = Mathf.MoveTowards(_supportWeight, carry || read ? 1f : 0f, Time.deltaTime * 5f);
            _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, _weight);
            _anim.SetIKPosition(AvatarIKGoal.RightHand, _grip);
            _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, _supportWeight);
            _anim.SetIKPosition(AvatarIKGoal.LeftHand, _support);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (_anim == null) return;
            if (Puppet != null) { PuppetIK(); return; }
            var weapon = Slots != null ? Slots.CurrentWeapon : null;
            bool weaponHold = weapon != null && weapon.gameObject.activeInHierarchy;
            bool penHold = !weaponHold && Slots != null && Slots.PenSelected && Pivot != null
                && (!SimpleFPSController.ThirdPersonActive && !SelfPaint.IsActive);

            if (weaponHold)
            {
                // IK holds the hand at the camera-anchored aim point so the
                // weapon points where you look; in draw mode the weapon sits
                // at screen center and the hands follow it
                _grip = HeldWeapon.DrawMode || Pivot == null
                    ? weapon.transform.TransformPoint(new Vector3(0.02f, -0.08f, -0.1f))
                    : Pivot.TransformPoint(new Vector3(0.3f, -0.26f, 0.55f));
                _support = weapon.transform.TransformPoint(new Vector3(-0.12f, 0f, 0.05f));
            }
            else if (penHold)
            {
                // casting stance while ink flows or hands are full; open
                // grimoire = read stance; closed, the book hand hangs free
                bool casting = SurfaceDrawer.IsPenActive || HandGrab.LocalHolding;
                if (_carried)
                {
                    // the hands come back from the load itself, not from
                    // wherever the tucked stance drifted to meanwhile
                    _penGrip = Pivot.InverseTransformPoint(_grip);
                    _penSupport = Pivot.InverseTransformPoint(_support);
                    _carried = false;
                }
                var readGrip = Anchor(ref _aReadGrip, "IKAnchor_ReadGrip", ReadGripDefault);
                var readSupport = Anchor(ref _aReadSupport, "IKAnchor_ReadSupport", ReadSupportDefault);
                var castGrip = Anchor(ref _aCastGrip, "IKAnchor_CastGrip", CastGripDefault);
                var castSupport = Anchor(ref _aCastSupport, "IKAnchor_CastSupport", CastSupportDefault);
                var readUp = Anchor(ref _aReadUp, "IKAnchor_ReadUp", ReadUpDefault);
                // the book rises when the look moves toward it, down and left,
                // and sinks the moment the look moves any other way. Deltas, not
                // angles: the book is glued to the camera, so only motion tells
                Vector3 flat = Pivot.forward; flat.y = 0f;
                float below = -Mathf.Asin(Mathf.Clamp(Pivot.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                if (_lookSeen && flat.sqrMagnitude > 1e-4f && _lastFlat.sqrMagnitude > 1e-4f)
                {
                    var v = new Vector2(Vector3.SignedAngle(_lastFlat, flat, Vector3.up) / dt,
                                        (below - _lastBelow) / dt);
                    _lookVel = Vector2.Lerp(_lookVel, v, 1f - Mathf.Exp(-dt * 10f));
                }
                _lastFlat = flat; _lastBelow = below; _lookSeen = true;
                float toward = Vector2.Dot(_lookVel, new Vector2(-0.7071f, 0.7071f));
                if (casting || !GrimoirePages.BookOpen) _reading = false;
                else if (toward > DrawingConfig.BookLookSpeed) _reading = true;
                else if (toward < -DrawingConfig.BookLookSpeed) _reading = false;
                if (GrimoirePages.BookOpen)
                    _readUp = Mathf.MoveTowards(_readUp, _reading ? 1f : 0f,
                        Time.deltaTime * DrawingConfig.BookRaiseSpeed);
                else if (_supportWeight <= 0.001f)
                    _readUp = 0f; // closed: the hand lets go where it is, the next open starts in the corner
                Vector3 gripTarget = casting
                    ? (castGrip != null ? castGrip.localPosition : CastGripDefault)
                    : (readGrip != null ? readGrip.localPosition : ReadGripDefault);
                Vector3 supportTarget = casting
                    ? (castSupport != null ? castSupport.localPosition : CastSupportDefault)
                    : Vector3.Lerp(readSupport != null ? readSupport.localPosition : ReadSupportDefault,
                                   readUp != null ? readUp.localPosition : ReadUpDefault, _readUp);
                if (_penGrip == Vector3.zero) { _penGrip = gripTarget; _penSupport = supportTarget; }
                _penGrip = Vector3.Lerp(_penGrip, gripTarget, Time.deltaTime * 7f);
                // closing: the book hand fades out from where it is and chases
                // nothing; once gone, the next open starts at the corner
                if (GrimoirePages.BookOpen)
                    _penSupport = Vector3.Lerp(_penSupport, supportTarget, Time.deltaTime * 7f);
                else if (_supportWeight <= 0.001f)
                    _penSupport = supportTarget;
                _grip = Pivot.TransformPoint(_penGrip);
                _support = Pivot.TransformPoint(_penSupport);
            }

            // carrying: both hands reach the load, overriding pen/weapon stances
            var carried = InkRuneStone.Carried;
            var grabbed = HandGrab.LocalHeldBody;
            var grabbedMote = HandGrab.LocalHeldMote;
            bool carryHold = carried != null || grabbed != null || grabbedMote != null;
            if (carryHold)
            {
                Vector3 c = carried != null ? carried.transform.position
                    : grabbed != null ? grabbed.worldCenterOfMass
                    : grabbedMote.transform.position;
                float half = 0.18f;
                if (grabbed != null)
                {
                    var cargoCol = grabbed.GetComponent<Collider>();
                    if (cargoCol != null)
                        half = Mathf.Clamp(cargoCol.bounds.extents.magnitude * 0.55f, 0.14f, 0.5f);
                }
                Vector3 side = _anim.transform.right * half;
                _grip = c + side;
                _support = c - side;
                _carried = true;
            }

            bool bookUp = weaponHold || carryHold || (penHold && GrimoirePages.BookOpen);
            _weight = Mathf.MoveTowards(_weight, weaponHold || penHold || carryHold ? 1f : 0f,
                Time.deltaTime * 5f);
            _supportWeight = Mathf.MoveTowards(_supportWeight, bookUp ? 1f : 0f,
                Time.deltaTime * 5f);
            if (_weight <= 0.001f && _supportWeight <= 0.001f)
            {
                _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                return;
            }
            _anim.SetIKPositionWeight(AvatarIKGoal.RightHand, _weight);
            _anim.SetIKPosition(AvatarIKGoal.RightHand, _grip);
            _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, _supportWeight); // full when the book is UP
            _anim.SetIKPosition(AvatarIKGoal.LeftHand, _support);
        }
    }
}
