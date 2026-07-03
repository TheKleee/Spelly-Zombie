using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// The pen: raycasts from the camera onto any surface in range and lays down
    /// DrawNodes as the aim point moves. Aiming works two ways with the same code —
    /// locked cursor (draw at the crosshair by moving the view) or precision mode
    /// (hold LeftAlt: cursor is freed and the ray follows it).
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

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || kb == null || Cam == null || DrawingWorld.Instance == null)
            {
                IsPenActive = false;
                return;
            }

            // pose editor owns the mouse while active — the pen stays capped
            if (PoseEditor.IsEditing)
            {
                IsPenActive = false;
                EndStroke();
                return;
            }

            bool erasing = kb.rKey.isPressed;
            bool penDown = mouse.leftButton.isPressed && !erasing;
            IsPenActive = mouse.leftButton.isPressed || erasing;

            if (!mouse.leftButton.isPressed)
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
            if (_current != null && Vector3.Distance(hit.point, _lastHitPoint) > DrawingConfig.MaxStrokeJump)
                EndStroke(); // aim jumped to a distant surface — that's a new stroke

            if (_current == null)
            {
                _current = new Stroke
                {
                    BasisRight = Cam.transform.right,
                    BasisUp = Cam.transform.up
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

            var last = _current.Last;
            if (last != null && Vector3.Distance(_smoothedPoint, last.transform.position) < DrawingConfig.NodeSpacing)
                return;

            var node = DrawNode.Create(_current, _current.Nodes.Count, _smoothedPoint, hit.normal, hit.collider.transform);
            _current.AddNode(node);

            TryCloseMidDraw(node);
        }

        /// Closing while drawing: the pen returns to the stroke's own starting point.
        /// The threshold scales with stroke length so that small runes with small
        /// gaps don't accidentally self-seal, while deliberate loops snap shut.
        void TryCloseMidDraw(DrawNode newNode)
        {
            if (_current.Nodes.Count < DrawingConfig.MinLoopNodes) return;
            float len = _current.PathLength();
            if (len < DrawingConfig.MinLoopPerimeter) return;

            float threshold = DrawingConfig.SelfCloseThreshold(len);

            if (Vector3.Distance(newNode.transform.position, _current.First.transform.position) <= threshold)
            {
                var stroke = _current;
                _current = null;
                _suppressUntilRelease = true;
                DrawingWorld.Instance.CloseSingleStroke(stroke);
            }
        }

        void EndStroke()
        {
            if (_current == null) return;
            var stroke = _current;
            _current = null;
            DrawingWorld.Instance.CompleteStroke(stroke);
        }

        /// Draw a glyph, then press F1-F12 to save it as the template for that rune.
        void HandleTemplateKeys(Keyboard kb)
        {
            var last = DrawingWorld.Instance.LastCompleted;
            if (last == null || last.RawShape.Count < 6) return;

            for (int i = 0; i < RuneLibrary.RecordableRunes.Length; i++)
            {
                var key = kb[(Key)((int)Key.F1 + i)];
                if (key != null && key.wasPressedThisFrame)
                {
                    var rune = RuneLibrary.RecordableRunes[i];
                    if (RuneLibrary.RecordTemplate(rune, last.RawShape))
                        DrawingWorld.Instance.LogEvent($"Template recorded: {rune} now matches your handwriting");
                }
            }
        }

        void OnGUI()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;
            // crosshair dot
            float s = 6f;
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(Screen.width / 2f - s / 2f, Screen.height / 2f - s / 2f, s, s), Texture2D.whiteTexture);
        }
    }
}
