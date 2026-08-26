using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The pen: raycasts from the camera onto any surface in range and lays down
    /// DrawNodes as the aim point moves. Locked cursor draws at the crosshair;
    /// precision mode (hold LeftAlt) frees the cursor and the ray follows it.
    public class SurfaceDrawer : MonoBehaviour
    {
        public Camera Cam;

        /// True while the pen (or eraser) is engaged - the controller slows the
        /// camera so small glyphs are drawable without switching modes.
        public static bool IsPenActive { get; private set; }

        Stroke _current;
        Vector3 _lastHitPoint;   // raw world fallback, for surface-jump detection
        Transform _lastHitSurface; // the surface that hit landed on...
        Vector3 _lastHitLocal;     // ...and where, in ITS space - so its motion cancels
        Vector3 _smoothedPoint;  // jitter-filtered, nodes are placed here
        bool _suppressUntilRelease;
        bool _erasing;           // crosshair feedback
        PlayerInk _ink;
        SimpleFPSController _pilot;
        WeaponSlots _slots;
        Vector3 _lastErasePoint;  // swept-erase track
        bool _hasEraseTrack;
        PlayerInk _inkPool;       // scoop target - erased ink refills the wand
        bool _wasErasing;         // falling edge  re-preview the edited ink
        int _holdEraseStart;      // ErasedTotal at rub start - a rub that kills nothing speaks
        float _pilotRetry;        // controller lookup throttle

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || kb == null || Cam == null || DrawingWorld.Instance == null)
            {
                IsPenActive = false;
                return;
            }

            // menus own the mouse while open; a shaped acolyte cannot draw
            if (PoseStudio.IsOpen || GameMenu.IsOpen || ShapeShift.LocalIsShaped)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            // no drawing while downed (controller lookup throttled to 1s)
            if (_pilot == null && (_pilotRetry -= Time.deltaTime) <= 0f)
            {
                _pilotRetry = 1f;
                _pilot = GetComponentInParent<SimpleFPSController>();
                if (_pilot == null) _pilot = FindAnyObjectByType<SimpleFPSController>();
            }
            if (_pilot != null && _pilot.IsDowned)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            // in third person the pen only comes out for body paint
            if (SimpleFPSController.ThirdPersonActive && !SelfPaint.IsActive)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            // a held grab occupies the hand - no drawing until you let go
            if (HandGrab.LocalHolding)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            // no ink = no drawing; WandState defaults open so lobby/studio draw free
            if (!WandState.LocalCanDraw)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            // weapons 2/3 own the mouse buttons; the pen belongs to slot 1 and
            // the two draw modes (weapon engraving / body paint)
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

            // eraser: right-click or left trigger
            var gp = Gamepad.current;
            bool gpDraw = gp != null && gp.rightTrigger.ReadValue() > 0.4f;
            bool gpErase = gp != null && gp.leftTrigger.ReadValue() > 0.4f;

            // any open menu owns the mouse - no drawing through panels
            if (GameMenu.IsOpen || HatPillar.PanelOpen || LobbyStand.PanelOpen
                || PoseStudio.IsOpen || Powerups.IsChoosing || UIKit.Typing
                || LobbyInspect.PanelOpen)
            {
                EndStroke();
                _hasEraseTrack = false;
                _wasErasing = false;
                IsPenActive = false;
                return;
            }

            // no wand (or dry) = no drawing
            bool penHeld = (mouse.leftButton.isPressed || gpDraw) && WandState.LocalCanDraw;
            bool erasing = mouse.rightButton.isPressed || gpErase;
            // eraser lifted: the ink changed - re-read what's left (preview)
            if (_wasErasing && !erasing && DrawingWorld.Instance != null)
            {
                DrawingWorld.Instance.PreviewNear(_lastErasePoint);
                // a rub that erased nothing names the nearest gap
                if (DrawingWorld.ErasedTotal == _holdEraseStart
                    && Time.time < DrawingWorld.LastEraseMissTime + 1f)
                    DrawingWorld.Instance.LogEvent(
                        $"the eraser passed {DrawingWorld.LastEraseMissDist * 100f:0.0}cm from ink without touching it ({DrawingConfig.EraseRadius * 100f:0.0}cm reach)");
                DrawingWorld.LastEraseMissDist = float.MaxValue;
            }
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
                if (!_wasErasing) _holdEraseStart = DrawingWorld.ErasedTotal; // fresh rub
                var eraseRay = GetAimRay(mouse);
                if (AimHit(eraseRay, out var eraseHit))
                {
                    // sweep from last frame's point so the pen-thin eraser never
                    // skips nodes; a big jump means the aim leapt surfaces - restart
                    Vector3 from = _hasEraseTrack && Vector3.Distance(_lastErasePoint, eraseHit.point) < 0.75f
                        ? _lastErasePoint : eraseHit.point;
                    if (_inkPool == null) _inkPool = GetComponentInParent<PlayerInk>();
                    // rubbed-out ink flows back to YOUR wand
                    DrawingWorld.Instance.EraseAlong(from, eraseHit.point, DrawingConfig.EraseRadius, _inkPool);
                    // body-paint depth slack: older body ink can hover a few cm off
                    // today's pen surface - same pen width, forgiving depth, along the aim
                    if (SelfPaint.IsActive)
                    {
                        Vector3 depth = eraseRay.direction * 0.03f;
                        DrawingWorld.Instance.EraseAlong(eraseHit.point - depth, eraseHit.point + depth,
                            DrawingConfig.EraseRadius, _inkPool);
                    }
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
                // the pen's OTHER END - an affordance, so it shows every time
                // the pen is down and gets out of the way while erasing
                UIPrompt.Offer("RMB", Loc.T("chip.erase"));
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

        /// Body-painting: the pen sees only the painter's body; a fat sphere-cast
        /// backs up the thin ray so limb-capsule gaps don't break the line.
        static readonly RaycastHit[] _aimHits = new RaycastHit[32]; // NonAlloc buffer

        static bool AimHit(Ray ray, out RaycastHit hit)
        {
            if (SelfPaint.IsActive && SelfPaint.ActiveRoot != null)
            {
                int n = Physics.RaycastNonAlloc(ray, _aimHits, DrawingConfig.DrawRange,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (BestOnBody(_aimHits, n, out hit)) return true;
                n = Physics.SphereCastNonAlloc(ray, 0.04f, _aimHits, DrawingConfig.DrawRange,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                return BestOnBody(_aimHits, n, out hit);
            }
            // spell motes (gas clouds included) are not paper: the pen looks
            // straight through them to the surface behind
            int count = Physics.RaycastNonAlloc(ray, _aimHits, DrawingConfig.DrawRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            hit = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = _aimHits[i];
                if (h.collider == null || h.distance >= best) continue;
                if (h.collider.GetComponentInParent<SpellParticle>() != null) continue;
                best = h.distance;
                hit = h;
            }
            return best < float.MaxValue;
        }

        static bool BestOnBody(RaycastHit[] hits, int count, out RaycastHit hit)
        {
            hit = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var h = hits[i];
                // skip the floated grimoire riding the camera - it would catch every stroke
                if (SelfPaint.FloatingBook != null
                    && h.collider.transform.IsChildOf(SelfPaint.FloatingBook)) continue;
                if (h.distance < best && h.collider.transform.IsChildOf(SelfPaint.ActiveRoot))
                {
                    best = h.distance;
                    hit = h;
                }
            }
            return best < float.MaxValue;
        }

        void HandleDrawHit(RaycastHit hit)
        {
            // ink cannot exist on water - the pen refuses
            if (hit.collider.GetComponent<WaterSurface>() != null)
            {
                EndStroke();
                return;
            }

            // ★ OTHER PLAYERS ARE NOT PAPER (his rule): you draw on yourself
            // and on zombies. A moving player would be a lying canvas (the
            // shell can't follow animation) and freezing them would be worse.
            var victim = hit.collider.GetComponentInParent<SimpleFPSController>();
            if (victim != null && !victim.IsLocalViewer)
            {
                EndStroke();
                return;
            }

            // the shell is just the canvas - body ink parents to the nearest limb
            Transform surface = hit.collider.transform;
            if (surface.name == "PaintShell" && SelfPaint.ActiveRoot != null)
            {
                var rig = SelfPaint.ActiveRoot.GetComponent<CharacterRig>();
                var limb = rig != null ? rig.NearestLimbSurface(hit.point) : null;
                if (limb != null) surface = limb;
            }

            var zombieOwner = ZombieOwner.From(hit.collider);
            if (zombieOwner != null)
            {
                surface = zombieOwner.transform;
                // painting freezes the zombie into the shell's pose (plus a grace
                // period) so the next stroke doesn't start on a walking target
                zombieOwner.PaintFreeze(DrawingConfig.ZombiePaintFreezeSeconds);
            }

            // one stroke, one surface - crossing a joint ends it; a seam-weld node
            // at the crossing keeps both sides touching in every pose
            if (_current != null && _current.Surface != null && surface != _current.Surface)
            {
                var weld = DrawNode.Create(_current, _current.Nodes.Count,
                    hit.point, hit.normal, _current.Surface);
                _current.AddNode(weld);
                EndStroke(penLifted: false); // still drawing - don't re-read yet
            }

            // jump tolerance grows with distance: a small mouse flick sweeps a lot
            // of wall at 8m, and silent stroke splits break closing shapes
            float allowedJump = Mathf.Max(DrawingConfig.MaxStrokeJump, hit.distance * DrawingConfig.MaxStrokeJumpPerMeter);

            // the jump is measured against the surface, not the world: re-projecting
            // the last hit through the surface's current transform cancels the
            // surface's own motion, leaving only what the hand did
            Vector3 lastPoint = _lastHitSurface != null
                ? _lastHitSurface.TransformPoint(_lastHitLocal)
                : _lastHitPoint;
            if (_current != null && Vector3.Distance(hit.point, lastPoint) > allowedJump)
            {
                // diagnostic: silent splits turn self-crossings into cross-stroke gaps
                DrawingWorld.Instance.LogEvent(
                    $"stroke split mid-draw: aim jumped {Vector3.Distance(hit.point, lastPoint) * 100f:0}cm (limit {allowedJump * 100f:0}cm)");
                EndStroke(penLifted: false); // new stroke, but the pen never came up
            }

            if (_current == null)
            {
                _current = new Stroke
                {
                    BasisRight = Cam.transform.right,
                    BasisUp = Cam.transform.up,
                    Surface = surface, // the LIMB on a body, the collider elsewhere
                    OwnerId = Grimoire.LocalPlayerId // your pen, your ink
                };
                DrawingWorld.Instance.Register(_current);
                _smoothedPoint = hit.point;
            }
            else
            {
                // frame-rate-independent jitter smoothing (tau = 0 degrades to raw input)
                float tau = DrawingConfig.DrawSmoothingTime;
                float k = tau > 0f ? 1f - Mathf.Exp(-Time.deltaTime / tau) : 1f;
                _smoothedPoint = Vector3.Lerp(_smoothedPoint, hit.point, k);
            }

            _lastHitPoint = hit.point;
            // and in the surface's own space, so a moving target carries the
            // memory with it (null-safe: the surface can die mid-stroke)
            _lastHitSurface = surface;
            _lastHitLocal = surface != null ? surface.InverseTransformPoint(hit.point) : hit.point;
            WorldEvents.Report(WorldEventKind.Ink, _smoothedPoint, 0.5f); // eyes follow the pen - ink is a decoy

            var last = _current.Last;
            if (last != null && Vector3.Distance(_smoothedPoint, last.transform.position) < DrawingConfig.NodeSpacing)
                return;

            // ink economy: every centimetre of line costs; kills refill.
            if (_ink == null) _ink = GetComponentInParent<PlayerInk>();
            if (_ink == null && _pilot != null) _ink = _pilot.GetComponent<PlayerInk>();
            if (_ink != null && last != null)
            {
                float cost = Vector3.Distance(_smoothedPoint, last.transform.position)
                    * DrawingConfig.InkCostPerMeter;
                if (!_ink.TrySpend(cost))
                {
                    DrawingWorld.Instance.LogEvent("OUT OF INK. kills refill the well");
                    EndStroke();
                    _suppressUntilRelease = true;
                    return;
                }
            }

            var node = DrawNode.Create(_current, _current.Nodes.Count, _smoothedPoint, hit.normal, surface);
            _current.AddNode(node);

            TryCloseMidDraw(node);
        }

        /// Closing while drawing: returning to the start closes the whole loop;
        /// crossing your own line closes the crossed portion (lasso rule) and the
        /// tail before the crossing survives as its own stroke.
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
                    // mid-draw closure only near the stroke's start (the circle
                    // gesture); deeper self-crossings resolve at pen-up where the
                    // star-guard tells lassos from glyphs
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
                // not crossing ourselves - maybe crossing someone else's ink:
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
                // split off the tail drawn before the crossing point - the one
                // shared adoption path (tiny tails still Burn in CompleteStroke)
                var tailNodes = stroke.DetachNodesBefore(bestIndex);
                if (tailNodes.Count > 0)
                {
                    var tail = DrawingWorld.Instance.AdoptPiece(stroke, tailNodes, allowTiny: true);
                    if (tail != null) DrawingWorld.Instance.CompleteStroke(tail);
                }
            }

            DrawingWorld.Instance.CloseSingleStroke(stroke);
        }

        /// `penLifted: false` = the pen is still down and this is a structural
        /// split (bone seam, aim jump) - only the rune reading waits for release.
        void EndStroke(bool penLifted = true)
        {
            if (_current == null) return;
            var stroke = _current;
            _current = null;
            DrawingWorld.Instance.CompleteStroke(stroke, preview: penLifted);
        }

        void OnGUI()
        {
            if (PoseStudio.IsOpen || Cam == null) return;
            if (SimpleFPSController.ThirdPersonActive) return; // no indicator on the emote stage
            if (Cursor.lockState != CursorLockMode.Locked) return;

            // crosshair dot - red and bigger while the eraser end is active
            float dot = _erasing ? 10f : 6f;
            GUI.color = _erasing ? new Color(1f, 0.3f, 0.25f, 0.95f) : new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(Screen.width / 2f - dot / 2f, Screen.height / 2f - dot / 2f, dot, dot),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

    }
}
