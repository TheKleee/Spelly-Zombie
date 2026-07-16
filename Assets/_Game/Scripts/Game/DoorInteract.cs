using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Doors open in EVERY scene — including Marko's hand-built lobby —
    /// WITHOUT rebuilding anything: on scene load, any mesh whose name says
    /// "door" (not doorframe/doorway) gets a DoorInteract if it lacks one.
    /// Models with a CENTERED pivot are wrapped in a hinge parent computed
    /// from their bounds, so they swing like doors instead of spinning like
    /// revolving ones. Doors marked Static can't move (batching bakes them) —
    /// those get one console line telling Marko to untick Static.
    public static class DoorAutoWire
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => Wire();
            Wire();
        }

        static bool IsDoorName(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("door") && !n.Contains("frame") && !n.Contains("way")
                // a WALL MODULE with a door hole (Polytope: Wall_..._Door) is
                // never the swinging leaf — neither is a building piece
                && !n.Contains("wall") && !n.Contains("house") && !n.Contains("roof")
                && !n.Contains("arch") && !n.Contains("window");
        }

        static void Wire()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t == null || !IsDoorName(t.name)) continue;
                // only the OUTERMOST door node wires (a door's handle child
                // named "DoorHandle" must not become its own door)
                bool nested = false;
                for (var p = t.parent; p != null; p = p.parent)
                    if (IsDoorName(p.name)) { nested = true; break; }
                if (nested) continue;
                if (t.GetComponentInParent<DoorInteract>() != null) continue;
                if (t.GetComponentInChildren<DoorInteract>() != null) continue;

                var rends = t.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue; // markers, triggers — not a door

                // measure in the DOOR'S OWN frame (world boxes lie when the
                // building is rotated), then gate by shape: a door LEAF is
                // tall, about a meter wide, and THIN. Anything else named
                // "door" is scenery and stays put.
                Bounds local = LocalBounds(t, rends);
                float[] ext = { local.extents.x, local.extents.y, local.extents.z };
                System.Array.Sort(ext); // [thickness, width, height] halves
                if (ext[2] < 0.6f || ext[2] > 1.7f       // height 1.2–3.4m
                    || ext[1] < 0.25f || ext[1] > 1.15f  // width 0.5–2.3m
                    || ext[0] > 0.22f)                   // thickness < 0.45m
                    continue;

                if (t.gameObject.isStatic)
                {
                    Debug.Log($"[SpellyZombie] '{t.name}' looks like a door but is marked Static — untick Static and it will open on E.");
                    continue;
                }
                Attach(t, local, ext[1]);
            }
        }

        /// Renderer bounds transformed into `frame`'s local space (corner-wise).
        static Bounds LocalBounds(Transform frame, Renderer[] rends)
        {
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in rends)
            {
                var lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var p = frame.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        static void Attach(Transform door, Bounds local, float halfWidth)
        {
            // the WIDTH axis = the door-local axis with the mid extent
            Vector3 widthLocal =
                Mathf.Approximately(local.extents.x, halfWidth) ? Vector3.right
                : Mathf.Approximately(local.extents.y, halfWidth) ? Vector3.up
                : Vector3.forward;
            Vector3 widthAxis = door.TransformDirection(widthLocal).normalized;
            Vector3 worldCenter = door.TransformPoint(local.center);
            float pivotOff = Vector3.Dot(worldCenter - door.position, widthAxis);

            // hinge at the pivot when the pivot already rides an edge,
            // else at the bounds edge. ALWAYS a hinge parent, ALWAYS world-
            // upright: it swings around true world-up no matter how the
            // model was exported (Blender's -90° imports made "swing"
            // into "slide" when we trusted the door's own axes).
            Vector3 hingePos = Mathf.Abs(pivotOff) > halfWidth * 0.55f
                ? door.position
                : worldCenter - widthAxis * halfWidth;

            Vector3 flat = door.forward; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) { flat = door.up; flat.y = 0f; }
            Quaternion upright = flat.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : Quaternion.identity;

            var hinge = new GameObject(door.name + "_Hinge");
            hinge.transform.SetPositionAndRotation(hingePos, upright);
            hinge.transform.SetParent(door.parent, true);
            door.SetParent(hinge.transform, true);
            hinge.AddComponent<DoorInteract>();
        }
    }

    /// Polite entry: stand near a door and press E to swing it open (or shut).
    /// The impolite entry — fire and boulders — still works; this component
    /// rides the same breakable leaf. The transform origin is the hinge edge,
    /// so rotating around local Y swings it like a real door.
    public class DoorInteract : MonoBehaviour
    {
        const float Range = 2.3f;
        bool _open;
        float _angle;
        Quaternion _closedRot;

        void Start() => _closedRot = transform.localRotation;

        void Update()
        {
            var kb = Keyboard.current;
            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player != null && !PoseStudio.IsOpen && !GameMenu.IsOpen
                && (player.transform.position - transform.position).sqrMagnitude < Range * Range)
            {
                UIPrompt.Show("E", Loc.T(_open ? "door.close" : "door.open"));
                if (kb != null && kb.eKey.wasPressedThisFrame)
                {
                    _open = !_open;
                    Juice.Thud(transform.position);
                }
            }

            float target = _open ? 105f : 0f;
            if (!Mathf.Approximately(_angle, target))
            {
                _angle = Mathf.MoveTowards(_angle, target, 240f * Time.deltaTime);
                transform.localRotation = _closedRot * Quaternion.Euler(0f, _angle, 0f);
            }
        }
    }
}
