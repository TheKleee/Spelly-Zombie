using System.Collections.Generic;
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
        Vector3 _smoothedLocal, _lastNormalLocal; // the pen's smoothed point and normal in that space, for the tail
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
        static string _noWorldScene; // the scene already told about its missing DrawingWorld

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || kb == null || Cam == null || DrawingWorld.Instance == null)
            {
                IsPenActive = false;
                // a pilot in a scene with no DrawingWorld can never draw: say so once
                if (DrawingWorld.Instance == null && Cam != null && ActiveScene.Name != _noWorldScene)
                {
                    _noWorldScene = ActiveScene.Name;
                    Debug.LogError($"[SpellyZombie] Scene '{_noWorldScene}' has no DrawingWorld: nothing can be " +
                        "drawn here. Add an empty object with the DrawingWorld component (the Lobby's SZ_DrawingWorld).", this);
                }
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
                || PoseStudio.IsOpen || UIKit.Typing
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
                    // body paint: the rub travels in the limb's own space, so the
                    // copies on your puppet (posed differently) are rubbed too
                    Transform bone = SelfPaint.IsActive ? EraseBone(eraseHit) : null;
                    void Rub(Vector3 a, Vector3 b)
                    {
                        // rubbed-out ink flows back to YOUR wand
                        DrawingWorld.Instance.EraseAlong(a, b, DrawingConfig.EraseRadius, _inkPool, netSend: bone == null);
                        if (bone != null) NetSync.OnLocalBodyErase(bone, a, b, DrawingConfig.EraseRadius);
                    }
                    Rub(from, eraseHit.point);
                    // body-paint depth slack: older body ink can hover a few cm off
                    // today's pen surface - same pen width, forgiving depth, along the aim
                    if (SelfPaint.IsActive)
                    {
                        Vector3 depth = eraseRay.direction * 0.03f;
                        Rub(eraseHit.point - depth, eraseHit.point + depth);
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
                if (!penHeld) LayTail(); // released: the line ends where the pen was
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

        /// The limb a body-paint rub lands on (the shell resolves to the nearest
        /// bone, like the pen); null when the hit is not on a bone.
        static Transform EraseBone(RaycastHit hit)
        {
            Transform t = hit.collider.transform;
            if (t.name == "PaintShell" && SelfPaint.ActiveRoot != null)
            {
                var rig = SelfPaint.ActiveRoot.GetComponent<CharacterRig>();
                var limb = rig != null ? rig.NearestLimbSurface(hit.point) : null;
                if (limb != null) t = limb;
            }
            return t.name.StartsWith("mixamorig:") ? t : null;
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
            else
            {
                // a host zombie's stand-in: the ink rides its root, the same
                // frame the host mounts it in; the host freezes the real one
                var proxy = hit.collider.GetComponentInParent<NetZombieProxy>();
                if (proxy != null) surface = proxy.transform;
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
            bool jumped = _current != null && Vector3.Distance(hit.point, lastPoint) > allowedJump;

            // one stroke, one surface - crossing a joint ends it; a seam-weld node
            // at the crossing keeps both sides touching in every pose. A far jump
            // gets no weld: the ink separates and the pen draws on where it is.
            if (_current != null && _current.Surface != null && surface != _current.Surface)
            {
                if (!jumped)
                {
                    var weld = DrawNode.Create(_current, _current.Nodes.Count,
                        hit.point, hit.normal, _current.Surface);
                    _current.AddNode(weld);
                }
                EndStroke(penLifted: false); // still drawing - don't re-read yet
            }
            else if (jumped)
                EndStroke(penLifted: false); // a shove or a flick: this line ends, the next starts here

            if (_current == null)
            {
                _current = new Stroke
                {
                    BasisRight = Cam.transform.right,
                    BasisUp = Cam.transform.up,
                    Surface = surface, // the LIMB on a body, the collider elsewhere
                    OwnerId = Grimoire.LocalPlayerId, // your pen, your ink
                    LiveId = NetSync.ClaimLiveId()    // friends watch it grow under this name
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
            _smoothedLocal = surface != null ? surface.InverseTransformPoint(_smoothedPoint) : _smoothedPoint;
            _lastNormalLocal = surface != null ? surface.InverseTransformDirection(hit.normal) : hit.normal;
            WorldEvents.Report(WorldEventKind.Ink, _smoothedPoint, 0.5f); // eyes follow the pen - ink is a decoy
            NetSync.OnLocalStrokeGrow(_current); // the line grows on every screen (10 Hz inside)

            // spacing is measured on the surface: a node floats SurfaceOffset
            // above it, and a still pen must lay nothing
            var last = _current.Last;
            if (last != null && Vector3.Distance(_smoothedPoint, OnSurface(last)) < DrawingConfig.NodeSpacing)
                return;

            // ink economy: every centimetre of line costs; kills refill.
            if (_ink == null) _ink = GetComponentInParent<PlayerInk>();
            if (_ink == null && _pilot != null) _ink = _pilot.GetComponent<PlayerInk>();

            // a fast hand between two slow frames covered a curve, not a straight
            // hop: lay that whole curve, node by node, so the same hand draws the
            // same line at any frame rate. Anything it can't do falls through to
            // the single node below.
            if (last != null && PlanFill(last, hit, surface))
            {
                LayFill(hit, surface);
                return;
            }

            if (last != null && !Pay(OnSurface(last), _smoothedPoint)) return;

            var node = DrawNode.Create(_current, _current.Nodes.Count, _smoothedPoint, hit.normal, surface);
            _current.AddNode(node);

            TryCloseMidDraw(node);
        }

        // the gap curve and the nodes planned along it, reused every frame
        static readonly List<Vector3> _curve = new List<Vector3>();
        static readonly List<Vector3> _fillPoints = new List<Vector3>();
        static readonly List<Vector3> _fillNormals = new List<Vector3>();
        const int CurveSamples = 24; // measuring steps along the gap curve

        /// Charges the wand for one piece of line. False = the well ran dry, so
        /// the stroke ends where it was and nothing draws until the pen lifts.
        bool Pay(Vector3 from, Vector3 to)
        {
            if (_ink == null) return true;
            float cost = Vector3.Distance(from, to) * DrawingConfig.InkCostPerMeter * _ink.DrawRate;
            if (_ink.TrySpend(cost)) return true;
            DrawingWorld.Instance.LogEvent("OUT OF INK. kills refill the well");
            EndStroke();
            _suppressUntilRelease = true;
            return false;
        }

        /// A node's point ON its surface - DrawNode.Create lifts every node clear
        /// of it, and the gap curve is measured in surface points.
        static Vector3 OnSurface(DrawNode n) =>
            n.transform.position - n.SurfaceNormal * DrawingConfig.SurfaceOffset;

        /// Plans the nodes that fill the gap the pen left this frame: one every
        /// NodeSpacing of arc along a curve from the last node to the smoothed
        /// point, each one put back on the surface being drawn on. False = there
        /// is no gap, or it cannot be filled here - the caller lays its one node.
        bool PlanFill(DrawNode last, RaycastHit hit, Transform surface)
        {
            _fillPoints.Clear();
            _fillNormals.Clear();
            Vector3 a = OnSurface(last);
            Vector3 b = _smoothedPoint;
            float chord = Vector3.Distance(a, b);
            if (chord < DrawingConfig.NodeSpacing * 2f) return false; // one node covers this, as always

            Vector3 startDir = StartTangent(a);
            if (startDir.sqrMagnitude < 0.5f) startDir = (b - a).normalized; // no line behind it yet
            Vector3 endDir = (b - a).normalized;
            int back = BackNode(_current.Nodes.Count - 1, a, chord);
            if (back >= 0)
            {
                Vector3 p = OnSurface(_current.Nodes[back]);
                if (Vector3.Distance(p, a) >= DrawingConfig.NodeSpacing * 0.5f)
                {
                    Vector3 arced = ArcTangent(p, a, b);
                    if (arced.sqrMagnitude > 0.5f) endDir = arced;
                }
            }

            // one Hermite piece: it leaves along the line's own direction and
            // arrives along the pen's, so a curve fills as a curve
            Vector3 m1 = startDir * chord, m2 = endDir * chord;
            _curve.Clear();
            _curve.Add(a);
            float arc = 0f;
            Vector3 prev = a;
            for (int i = 1; i <= CurveSamples; i++)
            {
                float t = i / (float)CurveSamples;
                float t2 = t * t, t3 = t2 * t;
                Vector3 p = a * (2f * t3 - 3f * t2 + 1f) + m1 * (t3 - 2f * t2 + t)
                    + b * (-2f * t3 + 3f * t2) + m2 * (t3 - t2);
                arc += Vector3.Distance(prev, p);
                prev = p;
                _curve.Add(p);
            }

            int pieces = Mathf.FloorToInt(arc / DrawingConfig.NodeSpacing);
            if (pieces < 2) return false; // still one node's worth of line

            float tolerance = Mathf.Min(chord * 0.5f, 0.05f);
            float step = arc / pieces;
            int seg = 1;
            float walked = 0f;
            float segLen = Vector3.Distance(_curve[0], _curve[1]);
            for (int i = 1; i < pieces; i++)
            {
                float want = step * i;
                while (seg < _curve.Count - 1 && walked + segLen < want)
                {
                    walked += segLen;
                    seg++;
                    segLen = Vector3.Distance(_curve[seg - 1], _curve[seg]);
                }
                Vector3 p = Vector3.Lerp(_curve[seg - 1], _curve[seg],
                    segLen > 1e-6f ? Mathf.Clamp01((want - walked) / segLen) : 0f);
                if (!OnSameSurface(p, hit, surface, tolerance, out var onSurf, out var normal))
                    return false; // not on this canvas: the frame lays its one node
                _fillPoints.Add(onSurf);
                _fillNormals.Add(normal);
            }
            return _fillPoints.Count > 0;
        }

        /// Lays the planned nodes, paying for each piece as it goes, and stops
        /// the moment the stroke ends or closes under it.
        void LayFill(RaycastHit hit, Transform surface)
        {
            var stroke = _current;
            Vector3 from = OnSurface(stroke.Last);
            for (int i = 0; i < _fillPoints.Count; i++)
            {
                if (!Pay(from, _fillPoints[i])) return;
                var filled = DrawNode.Create(stroke, stroke.Nodes.Count, _fillPoints[i], _fillNormals[i], surface);
                stroke.AddNode(filled);
                from = _fillPoints[i];
                TryCloseMidDraw(filled);
                if (_current != stroke) return; // it closed (or split) under the pen
            }
            if (!Pay(from, _smoothedPoint)) return;
            var node = DrawNode.Create(stroke, stroke.Nodes.Count, _smoothedPoint, hit.normal, surface);
            stroke.AddNode(node);
            TryCloseMidDraw(node);
        }

        /// Nodes land every NodeSpacing, so the last one can sit short of the
        /// pen when it comes up: one more puts the end where the pen was.
        void LayTail()
        {
            var stroke = _current;
            var last = stroke?.Last;
            if (last == null || _lastHitSurface == null) return;
            if (stroke.Nodes.Count < DrawingConfig.MinStrokeNodes) return; // a tap stays a tap
            // the release frame ran no raycast: the pen's last point rides the surface
            Vector3 tip = _lastHitSurface.TransformPoint(_smoothedLocal);
            Vector3 normal = _lastHitSurface.TransformDirection(_lastNormalLocal);
            Vector3 from = OnSurface(last);
            // no shorter than one node's scoop refund: erasing it must not pay back more than it cost
            if (Vector3.Distance(from, tip) < DrawingConfig.NodeSpacing * DrawingConfig.ScoopRefund) return;
            if (!Pay(from, tip)) return;
            var node = DrawNode.Create(stroke, stroke.Nodes.Count, tip, normal, _lastHitSurface);
            stroke.AddNode(node);
            TryCloseMidDraw(node);
        }

        /// Puts a planned point back on the surface with the pen's own ray, from
        /// the eye through the point: only a hit on the same collider, close to
        /// where the curve wanted it, counts as ink on this canvas.
        bool OnSameSurface(Vector3 point, RaycastHit hit, Transform surface, float tolerance,
            out Vector3 onSurface, out Vector3 normal)
        {
            onSurface = point;
            normal = hit.normal;
            Vector3 eye = Cam.transform.position;
            Vector3 dir = point - eye;
            if (dir.sqrMagnitude < 1e-8f) return false;
            if (!AimHit(new Ray(eye, dir.normalized), out var probe)) return false;
            if (probe.collider != hit.collider) return false;
            if (Vector3.Distance(probe.point, point) > tolerance) return false;
            // body paint: ink belongs to the limb nearest it, so a fill that
            // reached across a joint waits for the frame that aims there
            if (hit.collider.transform.name == "PaintShell" && SelfPaint.ActiveRoot != null)
            {
                var rig = SelfPaint.ActiveRoot.GetComponent<CharacterRig>();
                var limb = rig != null ? rig.NearestLimbSurface(probe.point) : null;
                if (limb != null && limb != surface) return false;
            }
            onSurface = probe.point;
            normal = probe.normal;
            return true;
        }

        /// Index of the newest node before `before` at least `minDist` from
        /// `from`; the oldest one when none is that far, -1 when there are none.
        int BackNode(int before, Vector3 from, float minDist)
        {
            var nodes = _current.Nodes;
            int oldest = -1;
            for (int i = before - 1; i >= 0; i--)
            {
                var n = nodes[i];
                if (n == null) continue;
                oldest = i;
                if (Vector3.Distance(OnSurface(n), from) >= minDist) return i;
            }
            return oldest;
        }

        /// The direction the line was already heading at its last node, read off
        /// the nodes behind it. Zero = too little line behind it to tell.
        Vector3 StartTangent(Vector3 a)
        {
            var nodes = _current.Nodes;
            int mid = BackNode(nodes.Count - 1, a, DrawingConfig.NodeSpacing);
            if (mid < 0) return Vector3.zero;
            Vector3 m = OnSurface(nodes[mid]);
            if (Vector3.Distance(m, a) < DrawingConfig.NodeSpacing * 0.5f) return Vector3.zero;
            int back = BackNode(mid, m, DrawingConfig.NodeSpacing);
            if (back < 0) return (a - m).normalized;
            Vector3 p = OnSurface(nodes[back]);
            if (Vector3.Distance(p, m) < DrawingConfig.NodeSpacing * 0.5f) return (a - m).normalized;
            return ArcTangent(p, m, a);
        }

        /// Tangent at `c` of the circle through the three points - a turning line
        /// keeps turning. Straight (or degenerate) input gives the plain chord.
        static Vector3 ArcTangent(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 chord = (c - b).normalized;
            Vector3 u = a - c, v = b - c;
            Vector3 n = Vector3.Cross(u, v);
            float n2 = n.sqrMagnitude;
            if (n2 < 1e-10f * u.sqrMagnitude * v.sqrMagnitude) return chord; // straight enough
            // circumcentre of the three, measured from c
            Vector3 centre = (Vector3.Cross(v, n) * u.sqrMagnitude
                + Vector3.Cross(n, u) * v.sqrMagnitude) / (2f * n2);
            Vector3 t = Vector3.Cross(n / Mathf.Sqrt(n2), -centre); // .normalized zeroes a small n
            if (t.sqrMagnitude < 1e-12f) return chord;
            t.Normalize();
            return Vector3.Dot(t, chord) < 0f ? -t : t;
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
