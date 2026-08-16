using UnityEngine;

namespace SpellyZombie
{
    /// Doors open in EVERY scene - including the hand-built lobby
    /// WITHOUT rebuilding anything: on scene load, any mesh whose name says
    /// "door" (not doorframe/doorway) gets a DoorInteract if it lacks one.
    /// Models with a CENTERED pivot are wrapped in a hinge parent computed
    /// from their bounds, so they swing like doors instead of spinning like
    /// revolving ones. Doors marked Static can't move (batching bakes them) -
    /// those get one console line telling to untick Static.
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
                // never the swinging leaf - neither is a building piece
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
                if (rends.Length == 0) continue; // markers, triggers - not a door

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
                    Debug.Log($"[SpellyZombie] '{t.name}' looks like a door but is marked Static. Untick Static and it will swing.");
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

    /// Code-driven door, no physics. It yields by PRESSURE: the closer a body
    /// stands to the doorway, the wider it opens, and it eases back as they
    /// retreat. An open door reads its direction from its own rotation and
    /// never swings back through the center while someone is there. Bodies on
    /// both sides freeze it; the side with more bodies wins the push.
    public class DoorInteract : MonoBehaviour
    {
        const float OpenAngle = 100f; // swing at zero distance
        const float OpenSpeed = 200f; // degrees per second opening
        const float ShutSpeed = 140f; // degrees per second closing
        const float Reach = 1f;       // push radius, meters

        [Tooltip("Frozen local axes. The unfrozen one is the swing axis.")]
        public bool FreezeRotX = true;
        public bool FreezeRotY;
        public bool FreezeRotZ = true;

        Quaternion _closedRot;
        Vector3 _leafAt; // doorway center at the closed pose
        Vector3 _tip;    // far edge of the leaf at the closed pose
        Vector3 _axis;   // world swing axis at the closed pose
        Vector3 _face;   // leaf normal: where the tip travels under + swing
        float _angle;    // signed swing, 0 = closed
        int _dir;
        int _nFront, _nBack;
        float _dFront, _dBack;

        void Start()
        {
            _closedRot = transform.rotation;

            if (FreezeRotX && FreezeRotY && FreezeRotZ) { enabled = false; return; }
            Vector3 local = !FreezeRotY ? Vector3.up
                : !FreezeRotX ? Vector3.right : Vector3.forward;
            _axis = (_closedRot * local).normalized;

            var rends = GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { enabled = false; return; }
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            _leafAt = b.center;
            Vector3 arm = Vector3.ProjectOnPlane(_leafAt - transform.position, _axis);
            _face = Vector3.Cross(_axis, arm).normalized;
            _tip = transform.position + arm * 2f;

            // leftovers from the physics-door experiment fight the script
            foreach (var rb in GetComponentsInChildren<Rigidbody>())
                Destroy(rb);

            StartCoroutine(Swing());
        }

        /// Local player, remote players, zombies: counted per side of the
        /// leaf, with the closest distance on each side.
        void Scan()
        {
            _nFront = _nBack = 0;
            _dFront = _dBack = Reach;
            foreach (var p in SimpleFPSController.All)
                if (p != null) Consider(p.transform.position);
            foreach (var z in Zombie.All)
                if (z != null) Consider(z.transform.position);
            foreach (var a in NetAvatar.All)
                if (a != null) Consider(a.transform.position);
        }

        /// Distance to the closest point along the closed leaf, flat, so the
        /// hinge half of the door pushes as easily as the handle half.
        void Consider(Vector3 pos)
        {
            if (Mathf.Abs(pos.y - _leafAt.y) > 1.6f) return;
            Vector3 h = transform.position; h.y = 0f;
            Vector3 t = _tip; t.y = 0f;
            Vector3 q = pos; q.y = 0f;
            Vector3 seg = t - h;
            float u = seg.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(q - h, seg) / seg.sqrMagnitude) : 0f;
            float dist = Vector3.Distance(q, h + seg * u);
            if (dist > Reach) return;
            if (Vector3.Dot(pos - transform.position, _face) > 0f)
            {
                _nFront++;
                if (dist < _dFront) _dFront = dist;
            }
            else
            {
                _nBack++;
                if (dist < _dBack) _dBack = dist;
            }
        }

        /// Sleeps on a slow tick while shut and alone; animates otherwise.
        System.Collections.IEnumerator Swing()
        {
            var rest = new WaitForSeconds(0.15f);
            while (true)
            {
                Scan();
                int n = _nFront + _nBack;
                if (n == 0 && _angle == 0f)
                {
                    _dir = 0;
                    yield return rest;
                    continue;
                }

                if (n == 0)
                {
                    _angle = Mathf.MoveTowards(_angle, 0f, ShutSpeed * Time.deltaTime);
                    if (_angle == 0f) _dir = 0;
                }
                else if (_nFront != _nBack)
                {
                    // majority side pushes; a tied door stands frozen
                    int push = _nFront > _nBack ? -1 : 1;
                    float dist = _nFront > _nBack ? _dFront : _dBack;
                    _dir = _angle != 0f ? (_angle > 0f ? 1 : -1) : push;

                    // pushing against the open side holds instead of
                    // sweeping the leaf back through the doorway
                    if (push == _dir)
                    {
                        float target = _dir * OpenAngle * (1f - dist / Reach);
                        float speed = Mathf.Abs(target) > Mathf.Abs(_angle)
                            ? OpenSpeed : ShutSpeed;
                        float was = _angle;
                        _angle = Mathf.MoveTowards(_angle, target, speed * Time.deltaTime);
                        if (was == 0f && _angle != 0f) Juice.Thud(_leafAt);
                    }
                }

                transform.rotation = Quaternion.AngleAxis(_angle, _axis) * _closedRot;
                yield return null;
            }
        }
    }
}
