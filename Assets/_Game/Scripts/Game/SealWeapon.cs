using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// THE SLIDE WEAPON (temp look, real mechanism): a rune TABLET made of a
    /// grip, a fixed plate, and a SLIDING plate with a 13cm gap between them.
    ///
    ///   HOLD Q / LMB → the slide racks shut (plates butt together)
    ///   Draw your runes + a loop ACROSS both plates while it's shut
    ///   RELEASE      → the slide opens, the loop is pulled apart, ink re-arms
    ///   PRESS again  → the plates meet, the loop closes, the seal FIRES
    ///
    /// This is the body-joint re-fire mechanic wearing a trigger: the moving
    /// part mechanically closes the seal, so one engraving casts forever.
    /// Carrying, dropping and engrave-mode live in HeldWeapon.
    public class SealWeapon : HeldWeapon
    {
        float _slideT; // 0 = open (armed), 1 = shut (firing)
        bool _wasShut;
        Transform _slide;
        static readonly Vector3 SlideOpen = new Vector3(0f, 0.012f, 0.40f);
        static readonly Vector3 SlideShut = new Vector3(0f, 0.012f, 0.265f);

        /// Build the pickup from primitives (temp look — Marko restyles later).
        public static GameObject CreatePickup(Vector3 pos)
        {
            var root = new GameObject("SealWeapon");
            root.transform.position = pos + Vector3.up * 0.75f;
            root.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Metal;
            root.AddComponent<PersistentInkSurface>(); // engraved ink is forever

            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "Grip";
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0f, -0.09f, -0.12f);
            grip.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);
            grip.transform.localScale = new Vector3(0.05f, 0.1f, 0.05f);
            grip.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.35f, 0.24f, 0.15f), MoteShade.Opaque);

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            plate.transform.SetParent(root.transform, false);
            plate.transform.localPosition = Vector3.zero;
            plate.transform.localScale = new Vector3(0.30f, 0.022f, 0.28f);
            plate.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.62f, 0.64f, 0.70f), MoteShade.Opaque);
            plate.AddComponent<PersistentInkSurface>();

            var slide = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slide.name = "Slide";
            slide.transform.SetParent(root.transform, false);
            slide.transform.localPosition = SlideOpen;
            slide.transform.localScale = new Vector3(0.30f, 0.022f, 0.24f);
            slide.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.78f, 0.62f, 0.28f), MoteShade.Opaque); // brass — reads as "the moving bit"
            slide.AddComponent<PersistentInkSurface>();

            var bubble = root.AddComponent<SphereCollider>();
            bubble.isTrigger = true;
            bubble.radius = 0.9f;

            var weapon = root.AddComponent<SealWeapon>();
            weapon._slide = slide.transform;
            return root;
        }

        void Awake()
        {
            // prefab-instantiated copies lose the private reference — recover it
            if (_slide == null) _slide = transform.Find("Slide");
        }

        protected override string SkinName => "SealTablet";

        protected override void OnSkinApplied(Transform skin)
        {
            var slide = FindPart("Slide"); // Marko's slide takes over the racking
            if (slide != null) _slide = slide;
        }

        protected override void UpdateArmed(Keyboard kb, Mouse mouse)
        {
            // the slide racks shut while Q — or this weapon's own LEFT CLICK —
            // is held, and springs open on release. (In draw mode LMB is the
            // pen, so only Q racks it there.)
            bool pressing = kb.qKey.isPressed
                || (!DrawMode && mouse != null && mouse.leftButton.isPressed);
            _slideT = Mathf.MoveTowards(_slideT, pressing ? 1f : 0f,
                9f * Perks.RackSpeedMul * Time.deltaTime); // Quick Hands racks faster
            if (_slide != null)
                _slide.localPosition = Vector3.Lerp(SlideOpen, SlideShut, _slideT);

            // nudge seal detection exactly when the plates meet or part
            bool shut = _slideT > 0.98f;
            if (shut != _wasShut)
            {
                _wasShut = shut;
                DrawingWorld.Instance?.RequestDetect();
                if (shut) Juice.Crackle(transform.position); // the rack-shut clack
            }
        }
    }
}
