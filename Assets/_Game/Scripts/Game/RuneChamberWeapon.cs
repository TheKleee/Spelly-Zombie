using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// THE RUNE CHAMBER (second weapon mechanism from the design: "player draws
    /// a rune sequence, each trigger pull cycles the next rune into the seal").
    ///
    /// A magazine STRIP with four engraving slots slides under a fixed WINDOW.
    /// Around the window sits a factory-engraved seal loop with a gap, and a
    /// BRIDGE bar that closes it - the same mechanically-closed-loop trick as
    /// the slide tablet:
    ///
    /// R            raise it and engrave a rune on each strip slot
    /// HOLD Q/LMB   the bridge snaps in, the loop closes around whichever
    ///                 rune sits in the window - that seal FIRES
    /// RELEASE      the loop opens (ink re-arms) and the strip ADVANCES,
    ///                 cycling the next rune into the window
    ///
    /// Four slots = a drawn firing sequence: fire-fire-ice-push, repeat. The
    /// strip carriage-returns to slot 1 after the last. Recognition reads the
    /// windowed rune through the surface-riding frame (SurfaceDelta), so the
    /// strip's travels never change what you engraved.
    public class RuneChamberWeapon : HeldWeapon
    {
        public const int Slots = 4;
        const float SlotPitch = 0.14f;

        // window/loop geometry in weapon-local space (see CreatePickup)
        static readonly Vector3 BridgeClosed = new Vector3(0f, 0.055f, 0.215f);
        static readonly Vector3 BridgeOpen = new Vector3(0f, 0.055f, 0.085f); // 0.13 travel > ReArm 0.10

        Transform _strip, _bridge, _frame;
        readonly List<Stroke> _factoryInk = new List<Stroke>();
        bool _inked;
        int _slot;
        float _bridgeT; // 0 = open (armed), 1 = closed (firing)
        bool _wasClosed;

        protected override Vector3 DrawPose => new Vector3(0f, -0.1f, 0.5f); // it's chunkier

        /// Build the pickup from primitives (temp look - restyles later).
        public static GameObject CreatePickup(Vector3 pos)
        {
            var root = new GameObject("RuneChamberWeapon");
            root.transform.position = pos + Vector3.up * 0.75f;
            root.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Metal;
            root.AddComponent<PersistentInkSurface>(); // engraved ink is forever

            BuildGripAndBubble(root, new Color(0.3f, 0.2f, 0.14f));

            // the magazine strip: one long drawable plate, four slots along it
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "Strip";
            strip.transform.SetParent(root.transform, false);
            strip.transform.localPosition = new Vector3(-SlotX(0), 0.012f, 0.30f);
            strip.transform.localScale = new Vector3(0.60f, 0.02f, 0.16f);
            strip.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.58f, 0.60f, 0.66f), MoteShade.Opaque);
            strip.AddComponent<PersistentInkSurface>();

            // the window frame: four narrow bezel bars floating above the strip
            var frame = new GameObject("Frame");
            frame.transform.SetParent(root.transform, false);
            frame.transform.localPosition = new Vector3(0f, 0f, 0.30f);
            void Bar(string barName, Vector3 at, Vector3 size)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = barName;
                bar.transform.SetParent(frame.transform, false);
                bar.transform.localPosition = at;
                bar.transform.localScale = size;
                bar.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.78f, 0.62f, 0.28f), MoteShade.Opaque); // brass = mechanism
            }
            Bar("Bezel_L", new Vector3(-0.085f, 0.055f, 0f), new Vector3(0.02f, 0.02f, 0.19f));
            Bar("Bezel_R", new Vector3(0.085f, 0.055f, 0f), new Vector3(0.02f, 0.02f, 0.19f));
            Bar("Bezel_Back", new Vector3(0f, 0.055f, 0.085f), new Vector3(0.19f, 0.02f, 0.02f));
            // front bezel has the GAP - its two stubs flank the bridge's berth
            Bar("Bezel_FL", new Vector3(-0.0725f, 0.055f, -0.085f), new Vector3(0.045f, 0.02f, 0.02f));
            Bar("Bezel_FR", new Vector3(0.0725f, 0.055f, -0.085f), new Vector3(0.045f, 0.02f, 0.02f));

            // the bridge: the sliding bar that closes the loop (the "hammer")
            var bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bridge.name = "Bridge";
            bridge.transform.SetParent(root.transform, false);
            bridge.transform.localPosition = BridgeOpen;
            bridge.transform.localScale = new Vector3(0.14f, 0.02f, 0.025f);
            bridge.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.85f, 0.5f, 0.25f), MoteShade.Opaque); // copper - THE moving bit
            bridge.AddComponent<PersistentInkSurface>();

            var weapon = root.AddComponent<RuneChamberWeapon>();
            weapon._strip = strip.transform;
            weapon._bridge = bridge.transform;
            weapon._frame = frame.transform;
            return root;
        }

        static float SlotX(int slot) => (slot - (Slots - 1) * 0.5f) * SlotPitch;

        void Awake()
        {
            // prefab-instantiated copies lose the private references - recover
            if (_strip == null) _strip = transform.Find("Strip");
            if (_bridge == null) _bridge = transform.Find("Bridge");
            if (_frame == null) _frame = transform.Find("Frame");
        }

        protected override string SkinName => "RuneChamber";

        protected override void OnSkinApplied(Transform skin)
        {
            // the parts take over: the strip is the drawable magazine, the
            // bridge is the closing bar - re-point before the factory ink lands
            var strip = FindPart("Strip");
            if (strip != null)
            {
                _strip = strip;
                if (strip.GetComponent<PersistentInkSurface>() == null)
                    strip.gameObject.AddComponent<PersistentInkSurface>();
            }
            var bridge = FindPart("Bridge");
            if (bridge != null)
            {
                _bridge = bridge;
                _bridge.localPosition = BridgeOpen;
            }
            var frame = FindPart("Frame");
            if (frame != null) _frame = frame;
        }

        protected override void Start()
        {
            base.Start(); // skin first, so the loop engraves onto the parts
            if (!_inked && DrawingWorld.Instance != null) EngraveFactoryLoop();
        }

        /// The pre-engraved seal machinery: a C-shaped loop around the window
        /// (on the frame) and the short closing segment (on the bridge). Drawn
        /// with the bridge in its CLOSED berth so the ends align exactly, then
        /// the bridge springs open - 0.13 apart, safely past re-arm distance.
        /// allowCloseOntoInk:false keeps it from sealing itself while inking.
        void EngraveFactoryLoop()
        {
            _inked = true;
            if (_frame == null || _bridge == null) return;

            // ALL coordinates live in weapon-ROOT local space (scale 1) - going
            // through the scaled primitive transforms would crush them. Nodes
            // still PARENT to their moving part (worldPositionStays), so the
            // bridge carries its ink and the frame keeps the ring.
            float y = 0.073f; // just above the bezel tops
            var c = new List<Vector3>(); // the C ring, gap at the front edge
            void Edge(Vector3 from, Vector3 to)
            {
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.02f));
                for (int i = 0; i <= steps; i++) c.Add(Vector3.Lerp(from, to, i / (float)steps));
            }
            Edge(new Vector3(-0.06f, y, 0.215f), new Vector3(-0.085f, y, 0.215f));
            Edge(new Vector3(-0.085f, y, 0.215f), new Vector3(-0.085f, y, 0.385f));
            Edge(new Vector3(-0.085f, y, 0.385f), new Vector3(0.085f, y, 0.385f));
            Edge(new Vector3(0.085f, y, 0.385f), new Vector3(0.085f, y, 0.215f));
            Edge(new Vector3(0.085f, y, 0.215f), new Vector3(0.06f, y, 0.215f));

            _factoryInk.Add(Engrave(c, _frame));

            // bridge ink: drawn in the closed berth, then the bar springs open
            var openPos = _bridge.localPosition;
            _bridge.localPosition = BridgeClosed;
            var seg = new List<Vector3>();
            for (int i = 0; i <= 6; i++)
                seg.Add(new Vector3(Mathf.Lerp(-0.06f, 0.06f, i / 6f), y, 0.215f));
            _factoryInk.Add(Engrave(seg, _bridge));
            _bridge.localPosition = openPos;
        }

        /// rootLocalPts are in weapon-root space; nodes parent to `surface` so
        /// they ride the moving part it belongs to.
        Stroke Engrave(List<Vector3> rootLocalPts, Transform surface)
        {
            Vector3 normal = transform.up;
            ZombieScribe.PlaneBasis(normal, out var right, out var up);
            var s = new Stroke { BasisRight = right, BasisUp = up, Surface = surface, OwnerId = 0 };
            DrawingWorld.Instance.Register(s);
            foreach (var p in rootLocalPts)
                s.AddNode(DrawNode.Create(s, s.Nodes.Count, transform.TransformPoint(p), normal, surface));
            DrawingWorld.Instance.CompleteStroke(s, allowCloseOntoInk: false);
            return s;
        }

        /// The loop must cast with ITS WIELDER's cards - the seal belongs to
        /// whoever last drew on the boundary, and the boundary is factory ink.
        public override void EquipTo(SimpleFPSController player)
        {
            base.EquipTo(player);
            if (player == null) return;
            int owner = player.gameObject.GetInstanceID();
            foreach (var s in _factoryInk)
                if (s != null && s.Alive) s.OwnerId = owner;
        }

        protected override void UpdateArmed(Keyboard kb, Mouse mouse)
        {
            // trigger: bridge in while held (Q always; LMB when not engraving)
            bool pressing = kb.qKey.isPressed
                || (!DrawMode && mouse != null && mouse.leftButton.isPressed);
            _bridgeT = Mathf.MoveTowards(_bridgeT, pressing ? 1f : 0f,
                9f * Perks.RackSpeedMul * Time.deltaTime); // Quick Hands again
            if (_bridge != null)
                _bridge.localPosition = Vector3.Lerp(BridgeOpen, BridgeClosed, _bridgeT);

            bool closed = _bridgeT > 0.98f;
            if (closed != _wasClosed)
            {
                _wasClosed = closed;
                DrawingWorld.Instance?.RequestDetect();
                if (closed)
                {
                    Juice.Crackle(transform.position); // the hammer drops
                }
                else
                {
                    // release complete  CYCLE: next slot glides into the window
                    _slot = (_slot + 1) % Slots;
                    if (_slot == 0) Juice.Chime(transform.position); // carriage return
                }
            }

            if (_strip != null)
            {
                var p = _strip.localPosition;
                p.x = Mathf.MoveTowards(p.x, -SlotX(_slot), 1.1f * Time.deltaTime);
                _strip.localPosition = p;
            }
        }
    }
}
