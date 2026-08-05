using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Everything a carryable weapon shares — the SLIDE TABLET and the RUNE
    /// CHAMBER both wear this. Loose: shop-window bob until E (WeaponSlots).
    /// Held: holster pose, R raises it into DRAW MODE (free brush cursor,
    /// MMB-drag spins it, WASD slides the view, scroll zooms), F drops it.
    /// Subclasses implement UpdateArmed() — the trigger mechanics.
    public abstract class HeldWeapon : MonoBehaviour
    {
        /// One weapon at a time can be raised for engraving (it's the SELECTED
        /// one — WeaponSlots hides the others, so the key can't be stolen).
        public static bool DrawMode { get; protected set; }
        public static void CancelDrawMode() => DrawMode = false;

        /// Every weapon in the scene — WeaponSlots scans this for E-pickups.
        public static readonly List<HeldWeapon> All = new List<HeldWeapon>();

        public bool Held { get; private set; }

        protected static readonly Vector3 HolsterPos = new Vector3(0.3f, -0.26f, 0.55f);
        protected static readonly Quaternion HolsterRot = Quaternion.Euler(-42f, -12f, 0f);
        protected virtual Vector3 DrawPose => new Vector3(0f, -0.06f, 0.42f);
        protected virtual Quaternion DrawTilt => Quaternion.Euler(-72f, 0f, 0f);

        float _bob;
        Quaternion _drawSpin = Quaternion.identity;
        float _drawDist = 0.42f;
        Vector2 _drawPan; // WASD view slide (weapon shifts opposite, camera-style)

        Transform _pivot;              // the camera (aim space / draw mode)
        Transform _hand;               // the grip socket — the weapon lives HERE
        Vector3 _handLocalPos;         // glue pose captured at equip
        Quaternion _handLocalRot;

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        /// Marko's Blender look for this weapon ("Weapon_<SkinName>" in the
        /// library). Pivot convention: at the grip, +Z away from the holder,
        /// +Y up, meters — drop-in, no per-weapon tuning.
        protected virtual string SkinName => null;

        /// After the skin lands, subclasses re-point moving parts (slide,
        /// strip, bridge…) at the skin's same-named children.
        protected virtual void OnSkinApplied(Transform skin) { }

        protected virtual void Start() => ApplySkin();

        /// Instantiates the skin at the root and hands over the duties of any
        /// primitive part with a matching child name: drawable ink surface,
        /// collider, and the moving-part role — then the primitive is gone.
        void ApplySkin()
        {
            if (SkinName == null) return;
            var prefab = Wardrobe.WeaponSkin(SkinName);
            if (prefab == null) return; // primitives keep standing in

            var skin = Instantiate(prefab, transform).transform;
            skin.name = "Skin";
            skin.localPosition = Vector3.zero;
            skin.localRotation = Quaternion.identity;

            foreach (Transform part in skin)
            {
                var old = transform.Find(part.name);
                if (old == null || old == skin) continue;
                if (old.GetComponent<PersistentInkSurface>() != null
                    && part.GetComponent<PersistentInkSurface>() == null)
                    part.gameObject.AddComponent<PersistentInkSurface>();
                if (old.GetComponent<Collider>() != null && part.GetComponent<Collider>() == null)
                {
                    var mc = part.gameObject.AddComponent<MeshCollider>();
                    mc.convex = true;
                }
                Destroy(old.gameObject);
            }
            OnSkinApplied(skin);
        }

        /// Shared primitive grip + pickup trigger bubble for the code-built pickups (temp look — Marko restyles later).
        protected static void BuildGripAndBubble(GameObject root, Color gripColor)
        {
            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "Grip";
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0f, -0.09f, -0.12f);
            grip.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);
            grip.transform.localScale = new Vector3(0.05f, 0.1f, 0.05f);
            grip.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(gripColor, MoteShade.Opaque);

            var bubble = root.AddComponent<SphereCollider>();
            bubble.isTrigger = true;
            bubble.radius = 0.9f;
        }

        /// Deep search — parts may live at the root or inside the skin.
        protected Transform FindPart(string partName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == partName) return t;
            return null;
        }

        protected virtual void OnDestroy()
        {
            if (Held) DrawMode = false; // never leave the cursor stranded
        }

        void Update()
        {
            if (!Held)
            {
                // shop-window shimmy: E takes it (WeaponSlots calls EquipTo)
                _bob += Time.deltaTime;
                transform.Rotate(0f, 60f * Time.deltaTime, 0f);
                transform.position += Vector3.up * (Mathf.Sin(_bob * 2.2f) * 0.15f * Time.deltaTime);
                return;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || PoseStudio.IsOpen || GameMenu.IsOpen || SelfPaint.IsActive
                || UIKit.Typing) return;
            if (SimpleFPSController.ThirdPersonActive)
            {
                DrawMode = false;
                return;
            }

            // R = engrave mode (WeaponSlots hides this weapon unless selected).
            // Not while floored or flying — the controller would cancel it a
            // frame later anyway; refusing at the door kills the cursor flicker
            var pilot = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            bool floored = pilot != null
                && (pilot.IsDowned || pilot.IsSprawled || pilot.IsAirTumbling);
            if (kb.rKey.wasPressedThisFrame && !Powerups.IsChoosing && !floored)
            {
                DrawMode = !DrawMode;
                if (DrawMode)
                {
                    _drawSpin = Quaternion.identity;
                    _drawDist = DrawPose.z;
                    _drawPan = Vector2.zero;
                    DrawingWorld.Instance?.LogEvent(
                        "weapon raised — engrave it (MMB-drag turns it, WASD moves the view, scroll zooms, R lowers)");
                }
            }

            if (DrawMode)
            {
                // MMB-drag spins the weapon; WASD MOVES the camera view (the
                // camera can't leave your head, so the weapon slides opposite —
                // reads the same); scroll zooms
                if (mouse != null && mouse.middleButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    _drawSpin = Quaternion.AngleAxis(-d.x * 0.35f, Vector3.up)
                              * Quaternion.AngleAxis(d.y * 0.35f, Vector3.right)
                              * _drawSpin;
                }
                float panX = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float panY = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                if (panX != 0f || panY != 0f)
                {
                    _drawPan -= new Vector2(panX, panY) * 0.28f * Time.deltaTime;
                    _drawPan = Vector2.ClampMagnitude(_drawPan, 0.3f); // stays on screen
                }
                if (mouse != null)
                {
                    float zoom = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(zoom) > 0.01f)
                        _drawDist = Mathf.Clamp(_drawDist - Mathf.Sign(zoom) * 0.05f, 0.24f, 0.85f);
                }
            }

            // where the weapon LIVES: engraving happens in camera space (raise
            // to center), everything else in the HAND that grips it. The hand
            // itself is IK-anchored to the aim point, so pointing still works.
            if (DrawMode)
            {
                if (transform.parent != _pivot && _pivot != null)
                    transform.SetParent(_pivot, true);
                transform.localPosition = Vector3.Lerp(transform.localPosition,
                    new Vector3(DrawPose.x + _drawPan.x, DrawPose.y + _drawPan.y, _drawDist),
                    Time.deltaTime * 9f);
                transform.localRotation = Quaternion.Slerp(transform.localRotation,
                    _drawSpin * DrawTilt, Time.deltaTime * 9f);
            }
            else if (_hand != null)
            {
                if (transform.parent != _hand)
                    transform.SetParent(_hand, true);
                transform.localPosition = Vector3.Lerp(transform.localPosition,
                    _handLocalPos, Time.deltaTime * 9f);
                transform.localRotation = Quaternion.Slerp(transform.localRotation,
                    _handLocalRot, Time.deltaTime * 9f);
            }
            else
            {
                transform.localPosition = Vector3.Lerp(transform.localPosition,
                    HolsterPos, Time.deltaTime * 9f);
                transform.localRotation = Quaternion.Slerp(transform.localRotation,
                    HolsterRot, Time.deltaTime * 9f);
            }

            // a downed/dead/sprawled wizard's trigger finger doesn't work
            // (firing while bleeding out made you impossible to revive)
            if (Wielder == null || !(Wielder.IsDowned || Wielder.IsSprawled || Wielder.IsAirTumbling))
                UpdateArmed(kb, mouse);
        }

        /// The weapon's own mechanics — trigger, moving parts, seal nudges.
        protected abstract void UpdateArmed(Keyboard kb, Mouse mouse);

        /// Whoever holds the weapon — trigger gates read their state.
        protected SimpleFPSController Wielder { get; private set; }

        /// WeaponSlots hands the weapon to a player (E). With the real body,
        /// the weapon is GLUED INTO THE HAND: struck into its camera-space aim
        /// pose first, then parented to the grip socket keeping that pose —
        /// hand IK holds the aim, animation adds the life.
        public virtual void EquipTo(SimpleFPSController player)
        {
            if (player == null || player.CameraPivot == null) return;
            Held = true;
            Wielder = player;
            foreach (var col in GetComponentsInChildren<Collider>())
                if (col.isTrigger) col.enabled = false; // pickup bubble sleeps; plates stay drawable

            _pivot = player.CameraPivot;
            var rig = player.GetComponent<CharacterRig>();
            _hand = rig != null ? rig.GripSocketR : null;

            transform.SetParent(_pivot, false);
            transform.localPosition = HolsterPos;
            transform.localRotation = HolsterRot; // notepad tilt — plates face you
            if (_hand != null)
            {
                transform.SetParent(_hand, true); // glue, keeping the aim pose
                _handLocalPos = transform.localPosition;
                _handLocalRot = transform.localRotation;
            }
            Juice.Chime(transform.position);
        }

        /// F puts it back into the world as a pickup, engraving intact.
        public virtual void Drop(Vector3 at)
        {
            Held = false;
            Wielder = null;
            DrawMode = false;
            transform.SetParent(null, true);
            transform.position = at;
            transform.rotation = Quaternion.identity;
            gameObject.SetActive(true);
            foreach (var col in GetComponentsInChildren<Collider>())
                if (col.isTrigger) col.enabled = true;
        }
    }
}
