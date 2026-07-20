using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The pen: raycasts from the camera onto any surface in range and lays down
    /// DrawNodes as the aim point moves. Aiming works two ways with the same code —
    /// locked cursor (draw at the crosshair by moving the view) or precision mode
    /// (hold LeftAlt: cursor is freed and the ray follows it).
    ///
    /// Drawing is just drawing: ink is ink, loops are seals, runes are read by
    /// recognition when a seal closes around them. (The draft/choose-and-stamp
    /// system was removed — it made drawing slow and unplayable.)
    public class SurfaceDrawer : MonoBehaviour
    {
        public Camera Cam;

        /// True while the pen (or eraser) is engaged — the controller slows the
        /// camera so small glyphs are drawable without switching modes.
        public static bool IsPenActive { get; private set; }

        Stroke _current;
        Vector3 _lastHitPoint;   // raw, for surface-jump detection
        Vector3 _smoothedPoint;  // jitter-filtered, nodes are placed here
        bool _suppressUntilRelease;
        bool _erasing;           // crosshair feedback
        PlayerInk _ink;
        SimpleFPSController _pilot;
        WeaponSlots _slots;
        Vector3 _lastErasePoint;  // swept-erase track
        bool _hasEraseTrack;
        bool _wasErasing;         // falling edge → re-preview the edited ink

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || kb == null || Cam == null || DrawingWorld.Instance == null)
            {
                IsPenActive = false;
                return;
            }

            // the Pose Studio / pause menu own the mouse while open
            if (PoseStudio.IsOpen || GameMenu.IsOpen)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            // no doodling while bleeding out on the floor
            if (_pilot == null) _pilot = GetComponentInParent<SimpleFPSController>();
            if (_pilot == null) _pilot = FindAnyObjectByType<SimpleFPSController>();
            if (_pilot != null && _pilot.IsDowned)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            // third person is for EMOTING — the pen only comes out there for
            // body paint (R)
            if (SimpleFPSController.ThirdPersonActive && !SelfPaint.IsActive)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            // weapons 2/3 own the mouse buttons; the pen belongs to the wand +
            // grimoire (slot 1) and to the two draw modes (engraving a raised
            // weapon / painting your body)
            if (!SelfPaint.IsActive && !HeldWeapon.DrawMode)
            {
                if (_slots == null) _slots = GetComponentInParent<WeaponSlots>();
                if (_slots != null && !_slots.PenSelected)
                {
                    IsPenActive = false;
                    EndStroke();
                    return;
                }
            }

            // the wand's other end: right-click (or left trigger) rubs ink out
            // (R belongs to weapon-draw mode / the emote editor now)
            var gp = Gamepad.current;
            bool gpDraw = gp != null && gp.rightTrigger.ReadValue() > 0.4f;
            bool gpErase = gp != null && gp.leftTrigger.ReadValue() > 0.4f;
            bool penHeld = mouse.leftButton.isPressed || gpDraw;
            bool erasing = mouse.rightButton.isPressed || gpErase;
            // eraser lifted: the ink changed — re-read what's left (preview)
            if (_wasErasing && !erasing && DrawingWorld.Instance != null)
                DrawingWorld.Instance.PreviewNear(_lastErasePoint);
            _wasErasing = erasing;
            _erasing = erasing;
            bool penDown = penHeld && !erasing;
            IsPenActive = penHeld || erasing;

            if (!penHeld)
                _suppressUntilRelease = false;
            if (!erasing)
                _hasEraseTrack = false;

            if (erasing)
            {
                EndStroke();
                if (AimHit(GetAimRay(mouse), out var eraseHit))
                {
                    // sweep from last frame's point so the pen-thin eraser never
                    // skips nodes; a big jump means the aim leapt surfaces — restart
                    Vector3 from = _hasEraseTrack && Vector3.Distance(_lastErasePoint, eraseHit.point) < 0.75f
                        ? _lastErasePoint : eraseHit.point;
                    DrawingWorld.Instance.EraseAlong(from, eraseHit.point, DrawingConfig.EraseRadius);
                    _lastErasePoint = eraseHit.point;
                    _hasEraseTrack = true;
                }
                else
                {
                    _hasEraseTrack = false;
                }
            }
            else if (penDown && !_suppressUntilRelease)
            {
                if (AimHit(GetAimRay(mouse), out var hit))
                    HandleDrawHit(hit);
                else
                    EndStroke(); // pen ran off the end of the world
            }
            else
            {
                EndStroke();
            }

        }

        Ray GetAimRay(Mouse mouse)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                return Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return Cam.ScreenPointToRay(mouse.position.ReadValue());
        }

        /// While body-painting, the pen sees ONLY the painter's body — a miss
        /// draws nothing, so you can never ink the world behind you and set
        /// off a spell by accident (Marko's rule). A thin ray slips through
        /// the gaps between limb capsules mid-stroke ("skips a place"), so a
        /// fat sphere-cast backs it up and keeps the line flowing. Normal
        /// play raycasts as is.
        static bool AimHit(Ray ray, out RaycastHit hit)
        {
            if (SelfPaint.IsActive && SelfPaint.ActiveRoot != null)
            {
                if (BestOnBody(Physics.RaycastAll(ray, DrawingConfig.DrawRange,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), out hit))
                    return true;
                return BestOnBody(Physics.SphereCastAll(ray, 0.04f, DrawingConfig.DrawRange,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), out hit);
            }
            return Physics.Raycast(ray, out hit, DrawingConfig.DrawRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        static bool BestOnBody(RaycastHit[] hits, out RaycastHit hit)
        {
            hit = default;
            float best = float.MaxValue;
            foreach (var h in hits)
                if (h.distance < best && h.collider.transform.IsChildOf(SelfPaint.ActiveRoot))
                {
                    best = h.distance;
                    hit = h;
                }
            return best < float.MaxValue;
        }

        void HandleDrawHit(RaycastHit hit)
        {
            // ink cannot exist on water — the pen just refuses (design: water is the seal-killer)
            if (hit.collider.GetComponent<WaterSurface>() != null)
            {
                EndStroke();
                return;
            }

            // one stroke lives on ONE surface: crossing onto a different collider
            // (a different body part) ends the stroke so ink stays rigid per-limb
            // and never stretches across a joint — joints are bridged by SEPARATE
            // strokes meeting, which the pose then opens/closes.
            if (_current != null && _current.Surface != null && hit.collider.transform != _current.Surface)
                EndStroke();

            // jump tolerance grows with distance: a small mouse flick sweeps a lot
            // of wall at 8m, and silent stroke splits break closing shapes
            float allowedJump = Mathf.Max(DrawingConfig.MaxStrokeJump, hit.distance * DrawingConfig.MaxStrokeJumpPerMeter);
            if (_current != null && Vector3.Distance(hit.point, _lastHitPoint) > allowedJump)
            {
                // diagnostic: silent splits turn self-crossings into unbridgeable
                // cross-stroke gaps — if this fires during honest circles, the
                // pen-continuity weld gets built next
                DrawingWorld.Instance.LogEvent(
                    $"stroke split mid-draw — aim jumped {Vector3.Distance(hit.point, _lastHitPoint) * 100f:0}cm (limit {allowedJump * 100f:0}cm)");
                EndStroke(); // aim jumped to a distant surface — that's a new stroke
            }

            if (_current == null)
            {
                _current = new Stroke
                {
                    BasisRight = Cam.transform.right,
                    BasisUp = Cam.transform.up,
                    Surface = hit.collider.transform,
                    OwnerId = Grimoire.LocalPlayerId // your pen, your ink
                };
                DrawingWorld.Instance.Register(_current);
                _smoothedPoint = hit.point;
            }
            else
            {
                // frame-rate-independent exponential smoothing of hand jitter.
                // The formula degrades to raw input on its own — at tau = 0,
                // k = 1 and the point snaps straight to the hit. Reading the
                // const into a LOCAL keeps that "0 = raw" knob real: as a
                // compile-time const the old `> 0f` guard folded to always-
                // true, so the separate raw branch was dead code.
                float tau = DrawingConfig.DrawSmoothingTime;
                float k = tau > 0f ? 1f - Mathf.Exp(-Time.deltaTime / tau) : 1f;
                _smoothedPoint = Vector3.Lerp(_smoothedPoint, hit.point, k);
            }

            _lastHitPoint = hit.point;
            WorldEvents.Report(WorldEventKind.Ink, _smoothedPoint, 0.5f); // eyes follow the pen — ink is a decoy

            var last = _current.Last;
            if (last != null && Vector3.Distance(_smoothedPoint, last.transform.position) < DrawingConfig.NodeSpacing)
                return;

            // ink economy: every centimetre of line costs; kills refill.
            if (_ink == null) _ink = GetComponentInParent<PlayerInk>();
            if (_ink == null && _pilot != null) _ink = _pilot.GetComponent<PlayerInk>();
            if (_ink != null && last != null)
            {
                float cost = Vector3.Distance(_smoothedPoint, last.transform.position)
                    * DrawingConfig.InkCostPerMeter * Perks.InkCostMul;
                if (!_ink.TrySpend(cost))
                {
                    DrawingWorld.Instance.LogEvent("OUT OF INK — kills refill the well");
                    EndStroke();
                    _suppressUntilRelease = true;
                    return;
                }
            }

            var node = DrawNode.Create(_current, _current.Nodes.Count, _smoothedPoint, hit.normal, hit.collider.transform);
            _current.AddNode(node);

            TryCloseMidDraw(node);
        }

        /// Closing while drawing — against ANY earlier point of the stroke, not
        /// just its start. Returning to the start closes the whole loop; crossing
        /// your own line closes the crossed portion (lasso rule) and the tail
        /// before the crossing survives as its own ordinary stroke.
        void TryCloseMidDraw(DrawNode newNode)
        {
            var nodes = _current.Nodes;
            int last = nodes.Count - 1;

            int bestIndex = -1;
            float bestDist = float.MaxValue;
            if (nodes.Count >= DrawingConfig.MinLoopNodes)
            {
                for (int j = 0; j <= last - DrawingConfig.MinLoopNodes; j++)
                {
                    // mid-draw closure = RETURNING TO THE START (the circle
                    // gesture). Crossing your own line deeper into the stroke
                    // no longer truncates the drawing on the spot — a five-
                    // point star crosses itself five times and used to get
                    // cut mid-glyph (slow stars died here, fast ones later at
                    // the detect tick; Marko: "most were not recognized").
                    // Deeper crossings resolve at pen-up, where the crossing
                    // finder's star-guard tells lassos from glyphs.
                    if (_current.LengthBetween(0, j) > DrawingConfig.MidDrawCloseStartRegion) break;
                    float loopLen = _current.LengthBetween(j, last);
                    if (loopLen < DrawingConfig.MinLoopPerimeter) break; // loops only shrink from here
                    float threshold = DrawingConfig.SelfCloseThreshold(loopLen);
                    float d = Vector3.Distance(newNode.transform.position, nodes[j].transform.position);
                    if (d <= threshold && d < bestDist)
                    {
                        bestDist = d;
                        bestIndex = j;
                    }
                }
            }
            if (bestIndex < 0)
            {
                // not crossing ourselves — maybe crossing someone else's ink:
                // if our start also sits on that ink, the loop closes through it
                if (nodes.Count >= 3 && DrawingWorld.Instance.TryCloseOntoInk(_current))
                {
                    _current = null;
                    _suppressUntilRelease = true;
                }
                return;
            }

            var stroke = _current;
            _current = null;
            _suppressUntilRelease = true;

            if (bestIndex > 0)
            {
                // split off the tail drawn before the crossing point
                var tailNodes = stroke.DetachNodesBefore(bestIndex);
                if (tailNodes.Count > 0)
                {
                    var tail = new Stroke { BasisRight = stroke.BasisRight, BasisUp = stroke.BasisUp, OwnerId = stroke.OwnerId };
                    foreach (var n in tailNodes)
                    {
                        n.SetStroke(tail);
                        tail.AddNode(n);
                    }
                    DrawingWorld.Instance.Register(tail);
                    DrawingWorld.Instance.CompleteStroke(tail);
                }
            }

            DrawingWorld.Instance.CloseSingleStroke(stroke);
        }

        void EndStroke()
        {
            if (_current == null) return;
            var stroke = _current;
            _current = null;
            DrawingWorld.Instance.CompleteStroke(stroke);
        }

        void OnGUI()
        {
            if (PoseStudio.IsOpen || Cam == null) return;
            if (SimpleFPSController.ThirdPersonActive) return; // no indicator on the emote stage
            if (Cursor.lockState != CursorLockMode.Locked) return;

            // crosshair dot — red and bigger while the eraser end is active
            float dot = _erasing ? 10f : 6f;
            GUI.color = _erasing ? new Color(1f, 0.3f, 0.25f, 0.95f) : new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(Screen.width / 2f - dot / 2f, Screen.height / 2f - dot / 2f, dot, dot),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // (F-key template recording REMOVED by Marko's ruling — the Rune
        // Studio walls are the one and only recording surface now.)
    }
}
