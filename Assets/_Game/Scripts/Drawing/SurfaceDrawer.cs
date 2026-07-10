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

            // the wand's other end: right-click (or R / left trigger) rubs ink out
            var gp = Gamepad.current;
            bool gpDraw = gp != null && gp.rightTrigger.ReadValue() > 0.4f;
            bool gpErase = gp != null && gp.leftTrigger.ReadValue() > 0.4f;
            bool penHeld = mouse.leftButton.isPressed || gpDraw;
            bool erasing = kb.rKey.isPressed || mouse.rightButton.isPressed || gpErase;
            _erasing = erasing;
            bool penDown = penHeld && !erasing;
            IsPenActive = penHeld || erasing;

            if (!penHeld)
                _suppressUntilRelease = false;

            if (erasing)
            {
                EndStroke();
                if (Physics.Raycast(GetAimRay(mouse), out var eraseHit, DrawingConfig.DrawRange,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    DrawingWorld.Instance.EraseAt(eraseHit.point, DrawingConfig.EraseRadius);
            }
            else if (penDown && !_suppressUntilRelease)
            {
                if (Physics.Raycast(GetAimRay(mouse), out var hit, DrawingConfig.DrawRange,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    HandleDrawHit(hit);
                else
                    EndStroke(); // pen ran off the end of the world
            }
            else
            {
                EndStroke();
            }

            HandleTemplateKeys(kb);
        }

        Ray GetAimRay(Mouse mouse)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                return Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return Cam.ScreenPointToRay(mouse.position.ReadValue());
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
                EndStroke(); // aim jumped to a distant surface — that's a new stroke

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
            else if (DrawingConfig.DrawSmoothingTime > 0f)
            {
                // frame-rate-independent exponential smoothing of hand jitter
                float k = 1f - Mathf.Exp(-Time.deltaTime / DrawingConfig.DrawSmoothingTime);
                _smoothedPoint = Vector3.Lerp(_smoothedPoint, hit.point, k);
            }
            else
            {
                _smoothedPoint = hit.point;
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
                float cost = Vector3.Distance(_smoothedPoint, last.transform.position) * DrawingConfig.InkCostPerMeter;
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
            if (Cursor.lockState != CursorLockMode.Locked) return;

            // crosshair dot — red and bigger while the eraser end is active
            float dot = _erasing ? 10f : 6f;
            GUI.color = _erasing ? new Color(1f, 0.3f, 0.25f, 0.95f) : new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(Screen.width / 2f - dot / 2f, Screen.height / 2f - dot / 2f, dot, dot),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// Draw a glyph (one or more strokes), then press F1-F12 to save the ink
        /// cluster around your last stroke as the template for that rune — the
        /// recognizer learns YOUR handwriting.
        void HandleTemplateKeys(Keyboard kb)
        {
            for (int i = 0; i < RuneLibrary.RecordableRunes.Length; i++)
            {
                var key = kb[(Key)((int)Key.F1 + i)];
                if (key == null || !key.wasPressedThisFrame) continue;

                var world = DrawingWorld.Instance;
                var rawStrokes = world.BuildRecordingGlyph(out int strokeCount);
                if (rawStrokes == null) return;

                int points = 0;
                foreach (var stroke in rawStrokes) points += stroke.Count;
                if (points < 6) return;

                var rune = RuneLibrary.RecordableRunes[i];
                if (RuneLibrary.RecordTemplate(rune, rawStrokes))
                    world.LogEvent($"Template recorded: {rune} now matches your handwriting ({strokeCount} stroke(s))");
                return;
            }
        }
    }
}
