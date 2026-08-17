using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// THE REWORKED TRANSFORMATION : scanning
    /// something TURNS YOU INTO IT INSTANTLY - no menu, no roster. TAB
    /// returns you to yourself; TAB again wears the last scan. R rotates the
    /// worn shape; Ctrl+1..9 saves the angle, 1..9 recalls it (the game's own
    /// save convention, same as poses). No scan limit at all.
    ///
    /// WHAT COUNTS : "acolyte should simply be
    /// able to transform into anything that's LIFT-ABLE anyway" — the same
    /// LiftMaxDimension law the grab uses, no Scannable marker required.
    /// Scanning leaves the instance MARKED GREEN forever (the trail wizards
    /// read) and feeds the wand ONCE per instance, bigger object = more ink =
    /// worse hiding spot.
    ///
    /// WHILE SHAPED YOU CANNOT DRAW. That is the whole trade: hiding is safe
    /// and powerless (SurfaceDrawer reads LocalIsShaped).
    [DefaultExecutionOrder(220)] // camera override must land AFTER the controller
    public class ShapeShift : MonoBehaviour
    {
        /// Shaped acolytes cannot draw (SurfaceDrawer reads this).
        public static bool LocalIsShaped { get; private set; }

        /// ACOLYTES HAVE NO PLAIN THIRD PERSON ("before you scan
        /// anything you cannot tab out into the 3rd person mode as acolyte
        /// cause that's just the transformation view"). Their third person IS
        /// the disguise camera, owned here - the controller's Tab toggle
        /// should consult this and refuse them.
        /// TAB is refused for an acolyte with NOTHING STORED : their
        /// third person IS the transformation view, so with no shape there is
        /// nothing to switch into. the controller's Tab consults this.
        public static bool ThirdPersonAllowed =>
            Sides.Of(Grimoire.LocalPlayerId) != Side.Acolyte
            || (Local != null && Local._storedShape != null);

        static ShapeShift Local;

        /// True while the acolyte's shape pose mode is open - the controller
        /// reads this as a precision mode, so the cursor frees, the brush
        /// cursor shows, and the look stops turning the body.
        public static bool PoseOpen => Local != null && Local._posing;

        /// The last scan's name, for the ModeGuide's "become the barrel" line.
        public static string StoredShapeName =>
            Local != null && Local._storedShape != null ? Local._storedShape.name : null;

        /// BLOWN OUT OF THE POSE - a blast big enough ends shape posing so the
        /// body is free to be thrown . Driven by Shove.
        public static void Blown() { if (Local != null && Local._posing) Local.SetPosing(false); }

        [Tooltip("Scan reach in meters.")]
        public float LearnRange = 2.6f;

        GameObject _worn;          // the disguise, kept alive across TABs
        Quaternion _wornRot = Quaternion.identity; // WORLD rotation - a barrel must not spin when you strafe
        readonly Quaternion[] _slots = new Quaternion[10];
        readonly bool[] _slotUsed = new bool[10];
        readonly List<Renderer> _hidden = new List<Renderer>();
        SimpleFPSController _pilot;
        Camera _cam;

        void Awake()
        {
            _pilot = GetComponent<SimpleFPSController>();
            Local = this;
        }

        void OnDisable() => Unwear(false);

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || UIKit.Typing || GameMenu.IsOpen || PoseStudio.IsOpen) return;
            if (_pilot == null) return;

            bool acolyte = Sides.Of(Grimoire.LocalPlayerId) == Side.Acolyte;
            if (!acolyte)
            {
                if (LocalIsShaped) Unwear(); // switching sides sheds the disguise
                return;
            }
            if (_pilot.IsDowned) { if (LocalIsShaped) Unwear(); return; }

            // F = the whole scan, in ONE method that checks first and returns
            // if it cannot run
            if (kb.fKey.wasPressedThisFrame) TryScan();

            // TAB as an acolyte only works if you HAVE a stored shape (the
            // rule): no shape, no third person - that view IS the disguise.
            if (kb.tabKey.wasPressedThisFrame && _storedShape == null
                && !SimpleFPSController.ThirdPersonActive)
                DrawingWorld.Instance?.LogEvent("nothing to become yet. F on an object first");

            // Tab brought us back to first person = back to yourself
            if (LocalIsShaped && !SimpleFPSController.ThirdPersonActive) Unwear();
            // Tab into third person with a shape stored = wear it again.
            // RE-WEAR THE CLONE WE ALREADY HAVE (the bug, Aug 10: "I get the
            // green tint which I didn't have before") — re-cloning the source
            // would copy the green the scan just painted onto it.
            else if (!LocalIsShaped && SimpleFPSController.ThirdPersonActive && _storedShape != null)
            {
                if (_worn != null) Wear();
                else BecomeObject(_storedShape);
            }

            if (!LocalIsShaped) { if (_posing) SetPosing(false); return; }

            // R while TRANSFORMED = the POSE MODE : you stop moving,
            // the camera becomes an easel orbit, and dragging turns the shape.
            // R or Esc leaves.
            if (kb.rKey.wasPressedThisFrame) SetPosing(!_posing);
            if (_posing && kb.escapeKey.wasPressedThisFrame) SetPosing(false);

            // THE WAY OUT IS NAMED ("There's no indicator anywhere
            // that Tab will turn you back"). Worn-but-not-posing was the one
            // acolyte state with no prompt at all - you were a barrel with no
            // way to learn you could stop being one. Pose mode takes the slot
            // over with its own line below, so the two never fight for it.
            if (!_posing)
            {
                // BOTH WAYS OUT ARE NAMED - as one-fact chips (the block
                // law: one info per block, never a dotted line). ModeGuide
                // stands down while shaped, so these two own the state alone.
                var mint = new Color(0.75f, 1f, 0.8f);
                UIPrompt.Offer("TAB", Loc.T("shape.back"), mint);
                UIPrompt.Offer("R", Loc.T("shape.turn"), mint);
                return;
            }

            var mouse = Mouse.current;

            // the shared easel language (SelfPaint and PoseGrab use the same):
            // MMB-drag orbits the camera, WASD slides it, scroll zooms
            if (mouse != null && _cam != null)
            {
                var camRot = EaselOrbit.Tick(kb, mouse, ref _poseYaw, ref _posePitch,
                    ref _poseDist, ref _posePan, allowZoom: true, zoomMin: 0.9f);
                EaselOrbit.Apply(_cam, TargetCenterWorld() + _posePan, camRot, _poseDist);
            }

            // PHYSICALLY DRAGGING IT : RMB grabs a point on
            // the shape and draws a line from the centre to it. Move the mouse
            // and a NEW line is drawn from the centre to where the cursor now
            // is - then the shape turns so the old line lands on the new one,
            // eased with a slerp. The grabbed point follows your hand; it is
            // not an axis nudge, it is a hand on the object.
            if (mouse != null && _cam != null) DragRotate(mouse);
            // ONLY THE UNDISCOVERABLE SPEAKS (dragging teaches
            // itself, recall follows from the save chip — "let them discover
            // things on their own sometimes")
            UIPrompt.Offer("Ctrl+1-9", Loc.T("shape.save"));

            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            for (int i = 0; i < 9; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + i)];
                if (key == null || !key.wasPressedThisFrame) continue;
                if (ctrl)
                {
                    _slots[i] = _wornRot;
                    _slotUsed[i] = true;
                    DrawingWorld.Instance?.LogEvent($"pose saved to {i + 1}");
                }
                else if (_slotUsed[i])
                {
                    _wornRot = _slots[i];
                }
            }
        }

        bool _posing;
        Vector3 _posePin;
        float _poseYaw, _posePitch, _poseDist;
        Vector3 _posePan;
        Vector3 _camLocalPos;
        Quaternion _camLocalRot;
        Vector3 _wornCenterLocal;

        bool _dragging;
        Vector3 _grabLocalDir;   // centre  grabbed point, in the shape's own space
        float _grabRadius;

        /// the drag model, shared with lifted cargo - see ArcballDrag. LMB.
        void DragRotate(Mouse mouse)
        {
            if (_worn == null) return;
            Vector3 center = _worn.transform.TransformPoint(_wornCenterLocal);
            Vector2 screen = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                // a ball around the centre the size of the shape, so a point
                // can be taken anywhere on it without needing colliders
                _grabRadius = Mathf.Max(0.15f, FindObjectBounds(_worn.transform).extents.magnitude);
                _grabLocalDir = _worn.transform.InverseTransformDirection(
                    ArcballDrag.Grab(_cam, screen, center, _grabRadius));
                _dragging = true;
                return;
            }
            if (!mouse.leftButton.isPressed) { _dragging = false; return; }
            if (!_dragging) return;

            _wornRot = ArcballDrag.Turn(_cam, screen, center, _grabRadius,
                _worn.transform.TransformDirection(_grabLocalDir), _wornRot, Time.deltaTime);
        }

        /// Where the worn shape's CENTRE belongs: on your own axis, at the
        /// height above your feet that it rode above its own ground.
        Vector3 TargetCenterWorld()
        {
            // THE SKIN WIDTH IS WHY IT STILL FLOATS (the "not as much but
            // clearly floating"): a CharacterController rests with its capsule
            // that far ABOVE the ground, so the true contact point is one skin
            // width below the capsule's bottom.
            var cc = GetComponent<CharacterController>();
            float feetLocalY = cc != null
                ? cc.center.y - cc.height * 0.5f - cc.skinWidth
                : -0.79f;
            return new Vector3(transform.position.x,
                transform.position.y + feetLocalY + _storedGroundOffset,
                transform.position.z);
        }

        /// The disguise is only real if it is ENFORCED: other systems turn
        /// renderers back on every frame, so while shaped this re-hides them
        /// and holds the worn shape's own world rotation (walking or looking
        /// must never spin the barrel).
        void LateUpdate()
        {
            if (!LocalIsShaped || _worn == null) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.transform.IsChildOf(_worn.transform)) continue;
                if (r.enabled)
                {
                    r.enabled = false;
                    if (!_hidden.Contains(r)) _hidden.Add(r);
                }
            }
            // ROTATION HAPPENS AROUND THE CENTRE, ALWAYS: set the rotation,
            // then slide the shape so its centre lands back on target. That is
            // what makes it spin in place instead of swinging around whatever
            // point the artist left behind.
            _worn.transform.rotation = _wornRot;
            Vector3 c = _worn.transform.TransformPoint(_wornCenterLocal);
            _worn.transform.position += TargetCenterWorld() - c;

            // pose mode: frozen body, free cursor - this write runs after the
            // controller, so it is the one that sticks
            if (_posing)
            {
                transform.position = _posePin;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// THE POSE MODE IS ITS OWN THING ("I don't need held weapon at
        /// all. This is a pose mode"): the body is PINNED where it stood, the
        /// camera becomes an easel orbit, and the cursor is freed. No borrowed
        /// states, no weapon code.
        void SetPosing(bool on)
        {
            if (on == _posing) return;
            _posing = on;
            if (_cam == null) _cam = GetComponentInChildren<Camera>();
            if (on)
            {
                _posePin = transform.position;
                // the PRECISION cursor, not the menu arrow -
                // the same brush circle Alt gives in first person
                PrecisionCursor.Apply();
                if (_cam != null)
                {
                    _camLocalPos = _cam.transform.localPosition;
                    _camLocalRot = _cam.transform.localRotation;
                }
                _poseYaw = transform.eulerAngles.y + 180f;
                _posePitch = 12f;
                _poseDist = 2.6f;
                _posePan = Vector3.zero;
            }
            else
            {
                PrecisionCursor.Clear(); // hand the normal cursor back
                if (_cam != null)
                {
                    _cam.transform.localPosition = _camLocalPos;
                    _cam.transform.localRotation = _camLocalRot;
                }
            }
        }

        /// THE ONE F METHOD : check whether the scan can
        /// run AT ALL and RETURN if not - aiming at nothing must never flip
        /// the view. Only when a real target is found do the three calls fire,
        /// in the order: view, tint, become.
        void TryScan()
        {
            if (SimpleFPSController.ThirdPersonActive) return; // already over there
            if (HandGrab.LocalHolding) return;                // full hands
            // THE BOOK MUST BE OPEN ON THE SCAN PAGE :
            // scanning is a thing the grimoire does, not a free keypress
            if (!GrimoirePages.BookOpen || !GrimoirePages.ScanPageOpen)
            {
                DrawingWorld.Instance?.LogEvent("open the grimoire to the SCAN page first");
                return;
            }
            if (_cam == null) _cam = GetComponentInChildren<Camera>();
            if (_cam == null) return;
            if (!Physics.Raycast(_cam.transform.position, _cam.transform.forward,
                    out var hit, LearnRange * 1.6f)) return;  // aiming at nothing
            if (hit.collider.GetComponentInParent<SimpleFPSController>() != null) return;
            // NO LIVING THINGS (the bug, Aug 10: scanning your own zombie made
            // you invisible - a skinned creature cloned without its animator
            // collapses and culls itself). Props only.
            if (hit.collider.GetComponentInParent<Creature>() != null) return;
            // the paint shell rides the DRESS, outside the zombie's hierarchy -
            // without this a zombie was scannable again through its own skin
            if (ZombieOwner.From(hit.collider) != null) return;
            if (hit.collider.GetComponentInChildren<Renderer>() == null) return;

            var root = hit.collider.attachedRigidbody != null
                ? hit.collider.attachedRigidbody.transform
                : hit.collider.transform;

            _storedShape = root; // the last shape, remembered for TAB
            _storedGroundOffset = CenterHeightAboveGround(root);

            // INK FIRST, THEN THE MODE (the order, Aug 10: "first give them
            // the ink then make them go to the third person mode").
            //
            // Scanning is the acolyte's ONLY well - a wizard has the cauldron,
            // an acolyte has this and nothing else. The award used to live in
            // Scan(), which the 1-by-1 rebuild PARKED, so the live path granted
            // nothing and an acolyte could never get a wand at all. Invisible
            // until everyone started wandless with evaporation on top.
            //
            // It runs BEFORE the mode change so the wand is already full when
            // the disguise goes on, rather than reforming underneath one.
            var ink = GetComponent<PlayerInk>();
            if (ink != null)
            {
                ink.Award(Perks.InkMax);   // full, not a trickle
                Juice.Chime(transform.position);
                DrawingWorld.Instance?.LogEvent("the shape fills your wand");
            }

            _pilot.EnterThirdPerson();   // the mode by name - scanning always ENTERS it
            // BECOME IT FIRST, TINT IT AFTER : the clone
            // must be taken BEFORE the green goes on, or the disguise itself
            // would be born tinted and give the acolyte away instantly.
            BecomeObject(root);
            TintScanGreen(root);
        }

        /// the step 3: null until the first F, then the last scanned shape -
        /// TAB reads this to decide whether it may switch at all.
        Transform _storedShape;
        float _storedGroundOffset;
        static readonly RaycastHit[] _groundBuf = new RaycastHit[16];

        /// HOW THE OBJECT ACTUALLY SITS : raycast down from
        /// its PIVOT to the ground it rests on and keep that distance. Worn,
        /// the shape is placed the same distance above YOUR feet - so a
        /// half-buried post stays buried and a pivot anywhere in the mesh
        /// lands right, which a bounds guess could never do.
        /// THE OBJECT'S NATURAL CENTRE, in world space - the middle of
        /// everything it draws. Pivots lie: they sit at a corner, at the base,
        /// buried under the floor, or off in empty space. Shared on purpose -
        /// the ground measurement uses it, and rotation will too, since a
        /// shape must turn around its own middle and not around some arbitrary
        /// point an artist left behind.
        public static Bounds FindObjectBounds(Transform t)
        {
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        /// THE ONE CENTRE METHOD, and it is LOCAL :
        /// objects move, so a world centre is stale the moment it is taken.
        /// The local one stays true relative to its parent, which is the only
        /// form rotation can use - we turn the shape locally, and bounds are
        /// simply how the centre gets found in the first place.
        public static Vector3 FindObjectCenterLocal(Transform t) =>
            t.InverseTransformPoint(FindObjectBounds(t).center);

        /// How high the object's CENTRE rides above the ground it rests on.
        /// Measured from the centre, cast from the centre - the pivot is not
        /// consulted anywhere. The ray starts above the
        /// object's top so a sunk object still finds the surface overhead,
        /// which makes the answer legitimately negative when it is buried.
        float CenterHeightAboveGround(Transform source)
        {
            Bounds b = FindObjectBounds(source);
            Vector3 from = new Vector3(b.center.x, b.max.y + 0.05f, b.center.z);

            // NOT the ghost layers: the vessel-shell follower (the cauldron's
            // true bowl, which is NOT a child of the pot and sits exactly under
            // its centre - measuring to it planted a cauldron-disguise waist
            // deep, ) and the ink canvases. Neither is ground.
            int mask = Physics.DefaultRaycastLayers
                & ~(1 << VesselShell.Layer) & ~(1 << InkCanvasLayer.Layer);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _groundBuf, 12f,
                mask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _groundBuf[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(source)) continue;          // its own body
                if (h.collider.GetComponentInParent<SimpleFPSController>() != null) continue;
                if (h.point.y > best) best = h.point.y;  // the surface it rests on or in
            }
            if (float.IsNegativeInfinity(best)) return 0f; // nothing under it - sit at the feet
            // clamped both ways: a hanging lamp cannot strand the disguise in
            // the sky, a deeply sunk centre cannot drop it out of the world
            return Mathf.Clamp(b.center.y - best, -3f, 2.5f);
        }

        /// the step 2.1: the scanned object turns SLIGHTLY GREENER on EVERY
        /// scan - repeat scans stack visibly, so wizards can read how often a
        /// thing was used. the approach followed exactly: the material is
        /// cloned on first touch (renderer.material does that by itself) and
        /// nudged toward the corrupt green each time.
        void TintScanGreen(Transform source)
        {
            foreach (var r in source.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                var m = r.material; // instance - cloned here if it wasn't yet
                if (m.HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor",
                        Color.Lerp(m.GetColor("_BaseColor"), DrawingConfig.CorruptInkColor, 0.22f));
                else if (m.HasProperty("_Color"))
                    m.color = Color.Lerp(m.color, DrawingConfig.CorruptInkColor, 0.22f);
            }
        }

        /// the step 2.2: turn INTO the object exactly as it is - its shape,
        /// its rotation - with a POOF on the swap: the anti-spam tell wizards
        /// can see, and a breath of smoke to change behind when cornered.
        void BecomeObject(Transform source)
        {
            if (_worn != null) Destroy(_worn);
            _worn = Instantiate(source.gameObject, transform);
            _worn.name = "WornShape";

            // LIGHTS ARE NOT COPIED ("since light sources cannot
            // be copied... it should look for the lights inside of the object
            // and not copy them"). The generic strip below hit the Light before
            // URP's rider component and Unity refused: "Can't remove Light
            // because UniversalAdditionalLightData depends on it". Dependents
            // first, then the Light - and a disguised candlestick simply does
            // not glow, which is its own honest tell.
            foreach (var l in _worn.GetComponentsInChildren<Light>(true))
            {
                var rider = l.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
                if (rider != null) Destroy(rider);
                Destroy(l);
            }

            foreach (var c in _worn.GetComponentsInChildren<Component>(true))
            {
                if (c is Transform || c is Renderer || c is MeshFilter) continue;
                if (c is Light) continue; // handled above, in dependency order
                if (c is UnityEngine.Rendering.Universal.UniversalAdditionalLightData) continue;
                Destroy(c);
            }
            _worn.transform.localScale = source.lossyScale;
            _wornRot = source.rotation; // exactly as it stood
            _worn.transform.rotation = _wornRot;

            // PLACED BY ITS CENTRE, never by its pivot : put the
            // shape's natural centre on your own axis, at the same height
            // above your feet that it rode above its own ground.
            _worn.transform.localPosition = Vector3.zero;
            // the centre, measured ONCE and kept - bounds are world-aligned, so
            // re-measuring a rotated shape would let the centre creep
            _wornCenterLocal = FindObjectCenterLocal(_worn.transform);
            _worn.transform.position +=
                TargetCenterWorld() - _worn.transform.TransformPoint(_wornCenterLocal);
            Wear();
            if (FxLibrary.I != null)
                FxLibrary.Spawn(FxLibrary.I.Poof, transform.position + Vector3.up * 0.5f);
        }

        void Wear()
        {
            if (_worn == null) return;
            _hidden.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r.transform.IsChildOf(_worn.transform)) continue;
                r.enabled = false;
                _hidden.Add(r);
            }
            _worn.SetActive(true);
            LocalIsShaped = true;
        }

        float _puffAt = -999f;

        void Unwear(bool puff = true)
        {
            bool wasShaped = LocalIsShaped;
            foreach (var r in _hidden)
                if (r != null) r.enabled = true;
            _hidden.Clear();
            if (_worn != null) _worn.SetActive(false);
            _posing = false;
            LocalIsShaped = false;

            // leaving the disguise breathes out zombie poison: the escape
            // smoke and the ambush. Poison never touches acolytes, so it
            // only bites wizards standing really close.
            if (puff && wasShaped
                && Time.time >= _puffAt + DrawingConfig.PoisonExitCooldown)
            {
                _puffAt = Time.time;
                PoisonField.Open(transform.position + Vector3.up * 0.9f,
                    DrawingConfig.PoisonExitRadius, DrawingConfig.PoisonExitSeconds);
            }
        }


        /// Shaped but still in first person (before the first TAB): the
        /// controller owns the camera and would sit INSIDE the disguise - this
        /// runs after it (execution order) and pulls the view out to third
        /// person until the Tab toggle takes over.
    }

    /// The permanent green mark a scan leaves on an instance - the trail
    /// wizards read, and the once-only ink receipt.
    public class ScannedMark : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public void Paint()
        {
            var blk = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                r.GetPropertyBlock(blk);
                Color baseCol = r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId)
                    ? r.sharedMaterial.GetColor(BaseColorId) : Color.white;
                Color mixed = Color.Lerp(baseCol, DrawingConfig.CorruptInkColor, 0.45f);
                blk.SetColor(BaseColorId, mixed);
                blk.SetColor(ColorId, mixed);
                r.SetPropertyBlock(blk);
            }
        }
    }
}
