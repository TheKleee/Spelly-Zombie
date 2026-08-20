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

        Animator _anim;
        float _weight;
        float _supportWeight; // the book hand: raised only while the grimoire is OPEN
        Vector3 _grip, _support; // last targets, for the ease-out

        // pen stance blends READING (book up) and CASTING (wand thrust, book
        // tucked). Stances live in IKAnchor_* children under the camera pivot,
        // adjustable in play mode via Character Fix.
        Vector3 _penGrip, _penSupport;
        static readonly Vector3 ReadGripDefault = new Vector3(0.17f, -0.25f, 0.38f);
        // the baked Player.prefab IKAnchor_* transforms override these
        // defaults - edit the prefab values, or changes here silently do nothing
        static readonly Vector3 ReadSupportDefault = new Vector3(-0.26f, -0.21f, 0.52f);
        static readonly Vector3 CastGripDefault = new Vector3(0.14f, -0.16f, 0.56f);
        static readonly Vector3 CastSupportDefault = new Vector3(-0.25f, -0.36f, 0.28f);
        Transform _aReadGrip, _aReadSupport, _aCastGrip, _aCastSupport;

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

        void OnAnimatorIK(int layerIndex)
        {
            if (_anim == null) return;
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
                var readGrip = Anchor(ref _aReadGrip, "IKAnchor_ReadGrip", ReadGripDefault);
                var readSupport = Anchor(ref _aReadSupport, "IKAnchor_ReadSupport", ReadSupportDefault);
                var castGrip = Anchor(ref _aCastGrip, "IKAnchor_CastGrip", CastGripDefault);
                var castSupport = Anchor(ref _aCastSupport, "IKAnchor_CastSupport", CastSupportDefault);
                Vector3 gripTarget = casting
                    ? (castGrip != null ? castGrip.localPosition : CastGripDefault)
                    : (readGrip != null ? readGrip.localPosition : ReadGripDefault);
                Vector3 supportTarget = casting
                    ? (castSupport != null ? castSupport.localPosition : CastSupportDefault)
                    : (readSupport != null ? readSupport.localPosition : ReadSupportDefault);
                if (_penGrip == Vector3.zero) { _penGrip = gripTarget; _penSupport = supportTarget; }
                _penGrip = Vector3.Lerp(_penGrip, gripTarget, Time.deltaTime * 7f);
                _penSupport = Vector3.Lerp(_penSupport, supportTarget, Time.deltaTime * 7f);
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
