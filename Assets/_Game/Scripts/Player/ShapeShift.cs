using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Acolyte transformation: scanning (F) turns you into the object; TAB
    /// toggles self/last scan; R opens pose mode; Ctrl+1..9 saves angles,
    /// 1..9 recalls. Anything under the LiftMaxDimension law is scannable;
    /// a scan tints the instance green and feeds the wand once per instance.
    /// While shaped you cannot draw (SurfaceDrawer reads LocalIsShaped).
    [DefaultExecutionOrder(220)] // camera override must land AFTER the controller
    public class ShapeShift : MonoBehaviour
    {
        /// Shaped acolytes cannot draw (SurfaceDrawer reads this).
        public static bool LocalIsShaped { get; private set; }

        /// An acolyte with nothing stored gets no third person - that view IS
        /// the disguise camera. The controller's Tab toggle consults this.
        public static bool ThirdPersonAllowed =>
            Sides.Of(Grimoire.LocalPlayerId) != Side.Acolyte
            || (Local != null && Local._storedShape != null);

        static ShapeShift Local;

        /// True while shape pose mode is open - the controller treats it as a
        /// precision mode (free cursor, look stops turning the body).
        public static bool PoseOpen => Local != null && Local._posing;

        /// Whether a scan is stored at all. NOT its name: a prefab name is
        /// English and untranslatable, and TAB twice already tells you what you
        /// were - at the cost of a puff of gas, which is a fair price for
        /// forgetting.
        public static bool HasStoredShape =>
            Local != null && Local._storedShape != null;

        /// A big enough blast ends shape posing. Driven by Shove.
        public static void Blown() { if (Local != null && Local._posing) Local.SetPosing(false); }

        [Tooltip("Scan reach in meters.")]
        public float LearnRange = 2.6f;

        /// The one scan distance: the badge offers F only inside it and TryScan
        /// only reaches inside it, so the key never lies.
        public static float ScanReach => (Local != null ? Local.LearnRange : 2.6f) * 1.6f;

        GameObject _worn;          // the disguise, kept alive across TABs
        /// This pilot is currently AN OBJECT (instance truth, not the local
        /// static) - the body-size laws read it to leave disguises true-size.
        public bool IsShapedNow => _worn != null && _worn.activeSelf;

        /// The biome that raised this acolyte - captured once, preferring the
        /// one they stand in when first needed (the spawn ground, in play).
        Biome _home;
        Biome MyHomeBiome()
        {
            if (_home != null) return _home;
            Biome any = null;
            foreach (var b in Object.FindObjectsByType<Biome>(FindObjectsSortMode.None))
            {
                if (b.Props == null || b.Props.Length == 0) continue;
                if (any == null) any = b;
                Vector3 l = transform.position - b.transform.position;
                var half = b.Size * 0.5f;
                if (Mathf.Abs(l.x) <= half.x && Mathf.Abs(l.y) <= half.y
                    && Mathf.Abs(l.z) <= half.z)
                { _home = b; return _home; }
            }
            _home = any; // not standing in one: the island still lends something
            return _home;
        }

        /// True when this transform IS the local player's disguise (or part of
        /// it). The thing you are wearing must never read as a target.
        public static bool IsLocalShape(Transform t)
        {
            var s = Local;
            if (t == null || s == null) return false;
            if (s._worn != null
                && (t == s._worn.transform || t.IsChildOf(s._worn.transform))) return true;
            return s._storedShape != null && t == s._storedShape;
        }
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

            if (kb.fKey.wasPressedThisFrame) TryScan();

            // ★ THE HOME BIOME DRESSES YOU (his design): TAB with an empty
            // wardrobe grants ONE random shape from your natural biome -
            // "your spawn biome is your home and your body is showing". A
            // brand-new acolyte has a working hide from second one; scanning
            // deliberately stays strictly better because you don't pick.
            if (kb.tabKey.wasPressedThisFrame && _storedShape == null
                && !SimpleFPSController.ThirdPersonActive)
            {
                var home = MyHomeBiome();
                var pick = home != null && home.Props != null && home.Props.Length > 0
                    ? home.Props[Random.Range(0, home.Props.Length)] : null;
                if (pick != null)
                {
                    _storedShape = pick.transform; // a prefab clones fine as a source
                    var r = pick.GetComponentInChildren<Renderer>();
                    _storedGroundOffset = r != null ? r.bounds.extents.y : 0.25f;
                    DrawingWorld.Instance?.LogEvent("your home lends you a shape");
                    // the wear machinery below picks it up from here
                }
                else
                {
                    // no biomes here (the lobby): the ROOM lends a shape -
                    // the nearest scannable prop, so the panic button never
                    // dead-ends anywhere
                    Transform found = null; float best = 18f * 18f;
                    foreach (var h in Physics.OverlapSphere(transform.position, 18f))
                    {
                        if (!CanScan(h, out var root2)) continue;
                        float d = (h.transform.position - transform.position).sqrMagnitude;
                        if (d < best) { best = d; found = root2; }
                    }
                    if (found != null)
                    {
                        _storedShape = found;
                        _storedGroundOffset = CenterHeightAboveGround(found);
                        DrawingWorld.Instance?.LogEvent("the room lends you a shape");
                    }
                    else DrawingWorld.Instance?.LogEvent("nothing to become yet. F on an object first");
                }
            }

            if (LocalIsShaped && !SimpleFPSController.ThirdPersonActive)
            {
                AcolyteDeeds.RevertedToSelf(Grimoire.LocalPlayerId);
                Unwear();
            }
            // re-wear the existing clone - re-cloning the source would copy
            // the green the scan painted onto it
            else if (!LocalIsShaped && SimpleFPSController.ThirdPersonActive && _storedShape != null)
            {
                if (_worn != null) Wear();
                else BecomeObject(_storedShape);
            }

            if (!LocalIsShaped) { if (_posing) SetPosing(false); return; }

            if (kb.rKey.wasPressedThisFrame) SetPosing(!_posing);
            if (_posing && kb.escapeKey.wasPressedThisFrame) SetPosing(false);

            if (!_posing)
            {
                // ModeGuide stands down while shaped - these prompts own the state
                var mint = new Color(0.75f, 1f, 0.8f);
                UIPrompt.Offer("TAB", Loc.T("shape.back"), mint);
                UIPrompt.Offer("R", Loc.T("shape.turn"), mint);
                return;
            }

            var mouse = Mouse.current;

            // shared easel controls (same as SelfPaint and PoseGrab)
            if (mouse != null && _cam != null)
            {
                var camRot = EaselOrbit.Tick(kb, mouse, ref _poseYaw, ref _posePitch,
                    ref _poseDist, ref _posePan, allowZoom: true, zoomMin: 0.9f);
                EaselOrbit.Apply(_cam, TargetCenterWorld() + _posePan, camRot, _poseDist);
            }

            if (mouse != null && _cam != null) DragRotate(mouse);
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
        Vector3 _grabLocalDir;   // direction from centre to grabbed point, shape-local
        float _grabRadius;

        /// the drag model, shared with lifted cargo - see ArcballDrag. LMB.
        void DragRotate(Mouse mouse)
        {
            if (_worn == null) return;
            Vector3 center = _worn.transform.TransformPoint(_wornCenterLocal);
            Vector2 screen = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                // a grab ball around the centre sized to the shape - no colliders needed
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
            // a CharacterController rests its capsule skinWidth above the
            // ground - the true contact point is below the capsule's bottom
            var cc = GetComponent<CharacterController>();
            float feetLocalY = cc != null
                ? cc.center.y - cc.height * 0.5f - cc.skinWidth
                : -0.79f;
            return new Vector3(transform.position.x,
                transform.position.y + feetLocalY + _storedGroundOffset,
                transform.position.z);
        }

        /// Other systems re-enable renderers every frame, so while shaped this
        /// re-hides them and holds the worn shape's world rotation.
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
            // set the rotation, then slide the centre back onto target - the
            // shape spins in place instead of swinging around its pivot
            _worn.transform.rotation = _wornRot;
            Vector3 c = _worn.transform.TransformPoint(_wornCenterLocal);
            _worn.transform.position += TargetCenterWorld() - c;

            // pose mode: pinned body, free cursor - runs after the controller, so it sticks
            if (_posing)
            {
                transform.position = _posePin;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// Pose mode: body pinned where it stood, easel-orbit camera, freed cursor.
        void SetPosing(bool on)
        {
            if (on == _posing) return;
            _posing = on;
            if (_cam == null) _cam = GetComponentInChildren<Camera>();
            if (on)
            {
                _posePin = transform.position;
                // precision brush cursor, same as Alt in first person
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
                PrecisionCursor.Clear();
                if (_cam != null)
                {
                    _cam.transform.localPosition = _camLocalPos;
                    _cam.transform.localRotation = _camLocalRot;
                }
            }
        }

        /// F: validate first and return if the scan cannot run - aiming at
        /// nothing must never flip the view.
        /// The one scan-eligibility rule: props only, lift-sized or the pot.
        /// AimBadge predicts with this; TryScan enforces it.
        public static bool CanScan(Collider col, out Transform root)
        {
            root = null;
            if (col == null) return false;
            if (col.GetComponentInParent<SimpleFPSController>() != null) return false;
            // props only - a skinned creature cloned without its animator
            // collapses and culls itself
            if (col.GetComponentInParent<Creature>() != null) return false;
            // the zombie paint shell sits outside its hierarchy - check it too
            if (ZombieOwner.From(col) != null) return false;
            if (col.GetComponentInParent<SpellParticle>() != null) return false;

            // the cauldron is scannable despite the size cap
            var pot = col.GetComponentInParent<CauldronEconomy>();
            if (pot == null)
            {
                var s = col.bounds.size;
                float dim = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                if (dim <= 0.05f || dim > DrawingConfig.LiftMaxDimension) return false;
            }

            root = pot != null ? pot.transform
                : col.attachedRigidbody != null ? col.attachedRigidbody.transform
                : col.transform;
            return root.GetComponentInChildren<Renderer>() != null;
        }

        void TryScan()
        {
            if (SimpleFPSController.ThirdPersonActive) return; // already over there
            if (HandGrab.LocalHolding) return;                // full hands
            // ★ NO BOOK NEEDED (his rework): scanning is aim + F, raw - the
            // grimoire is the library you MAY consult, never the trigger.
            // The tension is standing there doing it, not the page.
            if (_cam == null) _cam = GetComponentInChildren<Camera>();
            if (_cam == null) return;
            // the same ray the badge drew its promise with - same reach, same
            // mask, or F offers on things the scan cannot reach or resolve
            if (!Physics.Raycast(_cam.transform.position, _cam.transform.forward,
                    out var hit, ScanReach, AimBadge.AimMask,
                    QueryTriggerInteraction.Collide)) return;  // aiming at nothing
            if (!CanScan(hit.collider, out var root)) return;

            _storedShape = root; // the last shape, remembered for TAB
            _storedGroundOffset = CenterHeightAboveGround(root);

            // award ink BEFORE the mode change so the wand is full when the
            // disguise goes on. Scanning is the acolyte's only ink source.
            var ink = GetComponent<PlayerInk>();
            if (ink != null)
            {
                // a scan fills the RESERVE, invisible until you are yourself
                // again - the wand you had is the wand you keep wearing
                ink.Store(DrawingConfig.InkMax);
                Juice.Chime(transform.position);
                DrawingWorld.Instance?.LogEvent("the shape fills your wand");
            }

            AcolyteDeeds.Scanned(Grimoire.LocalPlayerId);
            _pilot.EnterThirdPerson();   // scanning always ENTERS, never toggles
            // clone BEFORE tinting, or the disguise is born green
            BecomeObject(root);
            TintScanGreen(root);
        }

        /// Null until the first scan; TAB reads this to decide if it may switch.
        Transform _storedShape;
        float _storedGroundOffset;
        static readonly RaycastHit[] _groundBuf = new RaycastHit[16];

        /// World-space bounds of everything the object draws - the reliable
        /// centre, since pivots can sit anywhere.
        public static Bounds FindObjectBounds(Transform t)
        {
            var rends = t.GetComponentsInChildren<Renderer>();
            var b = new Bounds(t.position, Vector3.zero);
            bool any = false;
            foreach (var r in rends)
            {
                // a disabled or empty renderer reports an invalid AABB and
                // Unity logs a conversion warning on the read - skip those
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// The object's visual centre in its own local space - stays valid as
        /// the object moves.
        public static Vector3 FindObjectCenterLocal(Transform t) =>
            t.InverseTransformPoint(FindObjectBounds(t).center);

        /// How high the object's centre rides above the ground under it. The
        /// ray starts above its top, so a buried object returns a negative.
        float CenterHeightAboveGround(Transform source)
        {
            Bounds b = FindObjectBounds(source);
            Vector3 from = new Vector3(b.center.x, b.max.y + 0.05f, b.center.z);

            // vessel shells and ink canvases are not ground
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

        /// Each scan nudges the object's materials toward corrupt green -
        /// repeat scans stack visibly.
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

        /// Turn into the object exactly as it stands, with a poof on the swap.
        void BecomeObject(Transform source)
        {
            if (_worn != null) Destroy(_worn);
            _worn = Instantiate(source.gameObject, transform);
            _worn.name = "WornShape";

            // strip lights. UniversalAdditionalLightData depends on Light, so
            // destroy the rider first or Unity refuses
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
                // the look driver stays - without it a StateView-painted thing
                // (absorbable motes included) wears back to bare white and the
                // disguise gives itself away. Instantiate froze its values at
                // scan time, which is exactly what a disguise should show.
                if (c is StateView) continue;
                Destroy(c);
            }
            _worn.transform.localScale = source.lossyScale;
            _wornRot = source.rotation; // exactly as it stood
            _worn.transform.rotation = _wornRot;

            // placed by its centre, never by its pivot
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

            // leaving the disguise releases poison - never touches acolytes
            if (puff && wasShaped
                && Time.time >= _puffAt + DrawingConfig.PoisonExitCooldown)
            {
                _puffAt = Time.time;
                PoisonField.Open(transform.position + Vector3.up * 0.9f,
                    DrawingConfig.PoisonExitRadius, DrawingConfig.PoisonExitSeconds);
            }
        }


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
