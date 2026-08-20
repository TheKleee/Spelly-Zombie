using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Slide weapon: a rune tablet with a fixed and a sliding plate. Racking
    /// shut closes a seal drawn across both plates so it fires; opening
    /// re-arms it. Carrying, dropping and engrave mode live in HeldWeapon.
    public class SealWeapon : HeldWeapon
    {
        float _slideT; // 0 = open (armed), 1 = shut (firing)
        bool _wasShut;
        Transform _slide;
        static readonly Vector3 SlideOpen = new Vector3(0f, 0.012f, 0.40f);
        static readonly Vector3 SlideShut = new Vector3(0f, 0.012f, 0.265f);

        /// Build the pickup from primitives (temp look).
        public static GameObject CreatePickup(Vector3 pos)
        {
            var root = new GameObject("SealWeapon");
            root.transform.position = pos + Vector3.up * 0.75f;
            root.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Metal;
            root.AddComponent<PersistentInkSurface>();

            BuildGripAndBubble(root, new Color(0.35f, 0.24f, 0.15f));

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
                MatterFX.Get(new Color(0.78f, 0.62f, 0.28f), MoteShade.Opaque); // brass
            slide.AddComponent<PersistentInkSurface>();

            var weapon = root.AddComponent<SealWeapon>();
            weapon._slide = slide.transform;
            return root;
        }

        void Awake()
        {
            // prefab-instantiated copies lose the private reference - recover it
            if (_slide == null) _slide = transform.Find("Slide");
        }

        protected override string SkinName => "SealTablet";

        protected override void OnSkinApplied(Transform skin)
        {
            var slide = FindPart("Slide"); // the slide takes over the racking
            if (slide != null) _slide = slide;
        }

        protected override void UpdateArmed(Keyboard kb, Mouse mouse)
        {
            // in draw mode LMB is the pen, so only Q racks the slide there
            bool pressing = kb.qKey.isPressed
                || (!DrawMode && mouse != null && mouse.leftButton.isPressed);
            _slideT = Mathf.MoveTowards(_slideT, pressing ? 1f : 0f,
                9f * Time.deltaTime);
            if (_slide != null)
                _slide.localPosition = Vector3.Lerp(SlideOpen, SlideShut, _slideT);

            // nudge seal detection exactly when the plates meet or part
            bool shut = _slideT > 0.98f;
            if (shut != _wasShut)
            {
                _wasShut = shut;
                DrawingWorld.Instance?.RequestDetect();
                if (shut) Juice.Crackle(transform.position);
            }
        }
    }
}
