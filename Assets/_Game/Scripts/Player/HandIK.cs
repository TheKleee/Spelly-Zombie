using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The hands work through the Animator's IK pass - upper body grips while
    /// the legs keep running . A held weapon gets both hands on
    /// it; the wand slot holds PEN + GRIMOIRE positions in front of the view,
    /// so you always see your hands while drawing. Lives on the model object
    /// (OnAnimatorIK must share the Animator's GameObject).
    public class HandIK : MonoBehaviour
    {
        public WeaponSlots Slots;
        public Transform Pivot; // the camera pivot - pen/grimoire anchor

        Animator _anim;
        float _weight;
        float _supportWeight; // the book hand: raised only while the grimoire is OPEN
        Vector3 _grip, _support; // last targets, for the ease-out

        // pen stance blends between READING (book up, you consult it) and
        // CASTING (wand hand thrusts at the surface, book tucks away).
        // The stances live in IKAnchor_* child transforms under the camera
        // pivot - MOVES THEM in play mode and saves via Character Fix
        // ("the grimoire is way too low": raise IKAnchor_ReadSupport).
        Vector3 _penGrip, _penSupport;
        static readonly Vector3 ReadGripDefault = new Vector3(0.17f, -0.25f, 0.38f);
        // the open book must be SEEN, but not OWN the screen
        // "It's still hovering over most of the screen". Down and left into the
        // corner, and slightly further out so the big grimoire reads smaller.
        //
        // THE ANCHOR IN Player.prefab IS THE ONE THAT RUNS. All four IKAnchor_*
        // transforms were auto-created from these constants and then BAKED into
        // the prefab, so Anchor() finds them and this number is dead weight on
        // the baked rig - the prefab value has to be edited alongside it or the
        // change silently does nothing (it did, twice).
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
                // the weapon is GLUED to this hand - the IK holds the hand at
                // the camera-anchored aim point, so the weapon points where
                // you look while the animation adds the sway. In draw mode the
                // weapon sits at screen center; the hands follow it there.
                _grip = HeldWeapon.DrawMode || Pivot == null
                    ? weapon.transform.TransformPoint(new Vector3(0.02f, -0.08f, -0.1f))
                    : Pivot.TransformPoint(new Vector3(0.3f, -0.26f, 0.55f));
                _support = weapon.transform.TransformPoint(new Vector3(-0.12f, 0f, 0.05f));
            }
            else if (penHold)
            {
                // ink flowing = the wand hand lunges forward and the book gets
                // out of the way; otherwise an OPEN grimoire is held up to
                // READ (G raised it) - closed, the book hand hangs free.
                // Stances read from the IKAnchor transforms .
                // CARRYING = THE BOOK DROPS OUT OF THE WAY ("the
                // grimoire is too big... it's covering more than half of the
                // screen and you can't see what you're lifting and where to
                // throw it"). The cast stance already tucks the book low, so a
                // full hand borrows it.
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

            // CARRYING — both
            // hands reach the carried load, overriding pen/weapon stances.
            // The sticky hand's cargo counts too: rocks, blobs, held spells -
            // the wizard VISIBLY grips what carries.
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
