using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Menu drawing area: mouse strokes on the cauldron brew make real seals
    /// and spells, with every rune unlocked. The camera orbit pauses while
    /// drawing and stays paused until 3 quiet seconds pass.
    public class MenuCauldron : MonoBehaviour
    {
        public Collider Canvas; // the brew disc
        public static float LastDrawTime = -999f;

        const int OwnerId = 777001; // menu-local owner id
        Stroke _stroke;
        Vector3 _lastNode;
        Vector3 _lastErase;   // swept-erase track
        bool _hasEraseTrack;

        void Start()
        {
            LastDrawTime = -999f;
            foreach (RuneType rune in System.Enum.GetValues(typeof(RuneType)))
                if (rune != RuneType.None) Grimoire.UnlockRune(OwnerId, rune);
        }

        /// Raycast the Canvas collider first (works on curved interiors),
        /// then fall back to flat plane math for thin invisible discs.
        bool AimAtBrew(Vector2 mousePos, out Vector3 point, out Vector3 normal)
        {
            var ray = Camera.main.ScreenPointToRay(mousePos);

            if (Canvas.Raycast(ray, out var hit, 500f))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }

            point = Vector3.zero;
            normal = Canvas.transform.up;
            var plane = new Plane(normal, Canvas.transform.position);
            if (!plane.Raycast(ray, out float t)) return false;
            point = ray.GetPoint(t);
            float radius = Mathf.Max(Canvas.bounds.extents.x, Canvas.bounds.extents.z) * 1.05f;
            Vector3 flat = point - Canvas.transform.position;
            flat -= Vector3.Project(flat, normal);
            return flat.sqrMagnitude <= radius * radius;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || Canvas == null || Camera.main == null
                || DrawingWorld.Instance == null) return;

            if (mouse.leftButton.isPressed
                && AimAtBrew(mouse.position.ReadValue(), out var point, out var normal))
            {
                LastDrawTime = Time.time;
                if (_stroke == null) Begin(point, normal);
                else
                {
                    float step = (point - _lastNode).magnitude;
                    if (step > 0.35f) { End(); Begin(point, normal); } // pen jumped
                    else if (step >= DrawingConfig.NodeSpacing) Add(point, normal);
                }
                return;
            }
            End();

            if (mouse.rightButton.isPressed
                && AimAtBrew(mouse.position.ReadValue(), out var erasePoint, out _))
            {
                LastDrawTime = Time.time;
                // pen-thin eraser sweeps between frames so it never skips ink
                Vector3 eraseFrom = _hasEraseTrack && (erasePoint - _lastErase).magnitude < 0.5f
                    ? _lastErase : erasePoint;
                DrawingWorld.Instance.EraseAlong(eraseFrom, erasePoint, DrawingConfig.EraseRadius);
                _lastErase = erasePoint;
                _hasEraseTrack = true;
            }
            else
            {
                _hasEraseTrack = false;
            }
        }

        void Begin(Vector3 point, Vector3 normal)
        {
            ZombieScribe.PlaneBasis(normal, out var right, out var up);
            _stroke = new Stroke
            {
                BasisRight = right,
                BasisUp = up,
                Surface = Canvas.transform,
                OwnerId = OwnerId
            };
            DrawingWorld.Instance.Register(_stroke);
            Add(point, normal);
        }

        void Add(Vector3 point, Vector3 normal)
        {
            _stroke.AddNode(DrawNode.Create(_stroke, _stroke.Nodes.Count,
                point + normal * DrawingConfig.SurfaceOffset, normal, Canvas.transform));
            _lastNode = point;
        }

        void End()
        {
            if (_stroke == null) return;
            DrawingWorld.Instance.CompleteStroke(_stroke);
            DrawingWorld.Instance.RequestDetect();
            _stroke = null;
        }
    }
}
