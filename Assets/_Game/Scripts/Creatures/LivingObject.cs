using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// LIFE NEEDLE ON AN OBJECT (his rule): the object gets eyes and lives for the needle's time as
    /// its caster's golem, made of that very object; then the eyes go and it stands where it is, a
    /// plain object again. Its mesh and colliders stay the body, its Element the golem's health, its
    /// scene path its name on every machine (never renamed). HOST: Wake and Sleep. Every machine:
    /// PutEyes (clients through NetGolemProxy.Adopt), worked out in the object's own frame so the
    /// eyes sit on the same spot everywhere.
    public class LivingObject : MonoBehaviour
    {
        const float ScalePerMetre = 2f;   // a scale-1 golem stands half a metre (BodyTop)
        const float EyesAcross = 0.6f;    // the pair spans this much of its width,
        const float EyeOfHeight = 0.25f;  // an eyeball at most this much of its height,
        const float EyeFloor = 0.06f;     // and at least this much, however thin it is
        const float MinEye = 0.04f;       // metres: never smaller than this
        const float Glued = 0.15f;        // of an eyeball, pressed into the surface

        /// Goes up whenever an object wakes or sleeps on this machine: caches of "is this alive" start over.
        public static int Era { get; internal set; }

        SpellPayload _nature;             // what it was made of before it woke (a meal changes it)
        bool _froze;
        RigidbodyInterpolation _interp;
        CollisionDetectionMode _sweep;
        GooglyEyes _eyes;
        bool _ownEyes;                    // put there by the needle, so they go with its life
        Golem _golem;

        /// HOST: the object wakes as `owner`'s golem for `seconds`. A needle on one already awake
        /// makes it the newest caster's and only ever lengthens its life.
        public static Golem Wake(Rigidbody rb, int owner, float seconds)
        {
            if (rb.TryGetComponent<LivingObject>(out var awake))
            {
                if (awake._golem == null) return null;
                awake._golem.OwnerId = owner;
                awake._golem.LifeLeft = Mathf.Max(awake._golem.LifeLeft, seconds);
                return awake._golem;
            }
            if (rb.GetComponent<Golem>() != null || !rb.TryGetComponent<Element>(out var el)) return null;
            var life = rb.gameObject.AddComponent<LivingObject>();
            life._nature = el.Natural;
            life._froze = rb.freezeRotation;
            life._interp = rb.interpolation;
            life._sweep = rb.collisionDetectionMode;
            // the eyes first: the brain and its charge look for them as they wake
            life._eyes = PutEyes(rb.transform, out float scale, out life._ownEyes);
            Golem.Moves(rb, scale);
            var golem = rb.gameObject.AddComponent<Golem>();
            golem.Tilt = TiltOf(rb.transform);
            golem.TurnCenter = Middle(rb.transform);
            golem.ObjectScale = scale;
            golem.SizeMul = scale;
            golem.OwnerId = owner;
            golem.LifeLeft = seconds;
            golem.ShieldBirth();
            if (life._eyes != null) life._eyes.Facing = null; // they look out of its own front, not the tilted root's
            life._golem = golem;
            el.OnDeath += life.Died;
            // in someone's hands: still until let go, as a lifted golem is
            if (HandGrab.HeldByAnother(rb)) golem.BeCarried();
            Era++;
            Debug.Log($"[SpellyZombie] {rb.name} comes alive as player {owner}'s for {seconds:0} s");
            return golem;
        }

        void Died(string cause) { if (this != null) Sleep(false); }

        /// Its time is up (awake) or it died as the object it is: a plain object again, where it stands.
        public void Sleep(bool awake = true)
        {
            // the charge pays back its strength loan before anything reads the strength
            if (TryGetComponent<ChargeAttack>(out var charge)) { charge.Settle(); Destroy(charge); }
            if (TryGetComponent<Element>(out var el))
            {
                el.OnDeath -= Died;
                // its own nature back: the mind the brain gave it and whatever it ate go
                el.Natural = _nature;
                var d = el.Data; d.Int = _nature.Int; d.Courage = _nature.Courage; el.Data = d;
                if (el.Health > el.MaxStrength) el.Health = el.MaxStrength;
            }
            Vector3 life = _eyes != null ? _eyes.transform.position : transform.position;
            // the life leaves it as a ghost leaves a body (every machine plays its own)
            if (awake) Juice.Sound(Sfx.GhostOut, life, 1f, 1.2f, false);
            if (_eyes != null && _ownEyes)
            {
                if (awake && FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, life);
                Destroy(_eyes.gameObject);
            }
            if (TryGetComponent<ZombieBuff>(out var buff)) { buff.End(); Destroy(buff); }
            if (_golem != null)
            {
                if (!_golem.enabled) _golem.BeReleased(); // carried: its own weight back
                Destroy(_golem);
            }
            if (TryGetComponent<Rigidbody>(out var rb))
            {
                rb.freezeRotation = _froze;
                rb.interpolation = _interp;
                rb.collisionDetectionMode = _sweep;
                if (awake) NetSync.TrackPropLater(rb); // a prop again: the prop beat carries it once the brain is gone
            }
            Era++;
            Destroy(this);
        }

        /// The object's own upright: whichever of its axes points most up now, turned onto world up.
        /// Snapped to an axis, so copies leaning a little differently on two machines agree.
        public static Quaternion TiltOf(Transform body)
        {
            Vector3 up = body.InverseTransformDirection(Vector3.up);
            Vector3 a = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
            Vector3 axis = a.x >= a.y && a.x >= a.z ? new Vector3(Mathf.Sign(up.x), 0f, 0f)
                : a.y >= a.z ? new Vector3(0f, Mathf.Sign(up.y), 0f)
                : new Vector3(0f, 0f, Mathf.Sign(up.z));
            return Quaternion.FromToRotation(axis, Vector3.up);
        }

        /// Its middle in its own space, from its own meshes: what it turns about.
        public static Vector3 Middle(Transform body)
        {
            OwnRenderers(body, _rends);
            return _rends.Count > 0 ? DoorAutoWire.LocalBounds(body, _rends.ToArray()).center : Vector3.zero;
        }

        /// The golem's own eyes (the Golem prefab's pair) on the object's front, just under the top
        /// of whatever faces front there, sized to the object; the ones it brings if it has some.
        /// Worked out from its own meshes in its own frame, so every machine puts them on the same
        /// spot. An empty named Socket_Eyes under the object places them instead. `scale` = its size
        /// in golem scale; `made` = these eyes are new, and go when its life does.
        public static GooglyEyes PutEyes(Transform body, out float scale, out bool made)
        {
            made = false;
            OwnRenderers(body, _rends);
            Bounds box = _rends.Count > 0 ? DoorAutoWire.LocalBounds(body, _rends.ToArray()) : new Bounds(Vector3.zero, Vector3.one);
            Vector3 s = body.lossyScale;
            s = new Vector3(Mathf.Max(1e-5f, Mathf.Abs(s.x)), Mathf.Max(1e-5f, Mathf.Abs(s.y)), Mathf.Max(1e-5f, Mathf.Abs(s.z)));
            Quaternion tilt = TiltOf(body);
            // its own axes: up, the front a kit prop's model faces (-Y in the kit), and across
            Vector3 up = Quaternion.Inverse(tilt) * Vector3.up;
            Vector3 front = Quaternion.Inverse(tilt) * Vector3.forward;
            Vector3 right = Vector3.Cross(up, front);
            // metres from here on: its box along each of its own axes
            Vector3 min = Vector3.Scale(box.min, s), max = Vector3.Scale(box.max, s);
            Span(min, max, up, out float low, out float top);
            Span(min, max, front, out float back, out float face);
            Span(min, max, right, out float left, out float rightEdge);
            float height = top - low, width = rightEdge - left, mid = (left + rightEdge) * 0.5f;
            scale = Mathf.Max(DrawingConfig.GolemMinScale, height * ScalePerMetre);

            var own = body.GetComponentInChildren<GooglyEyes>(true);
            if (own != null) return own;
            var prefab = CollectionManager.Golem;
            var pair = prefab != null ? prefab.GetComponentInChildren<GooglyEyes>(true) : null;
            if (pair == null)
            {
                if (prefab != null)
                    Debug.LogError("[SpellyZombie] The Golem prefab has no GooglyEyes: a living object has no eyes to wear.", prefab);
                return null;
            }
            var go = Instantiate(pair.gameObject, body, false);
            go.name = EyesName;
            var eyes = go.GetComponent<GooglyEyes>();
            made = true;
            if (eyes == null || !eyes.Layout(out float half, out float ball, out Vector3 centre))
            {
                Debug.LogError("[SpellyZombie] The Golem prefab's eyes have no Eye/Pupil pair to place.", prefab);
                return eyes;
            }

            // big enough to read, small enough to sit on it: by its width, capped by its height
            float per = Mathf.Max(1e-4f, (2f * half + ball) / ball); // the pair's width in eyeballs
            float d = Mathf.Min(EyesAcross * width / per, EyeOfHeight * height);
            d = Mathf.Max(d, EyeFloor * height, MinEye);
            float k = d / ball; // the pair's own units to metres

            // his spot wins, where he put one
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Socket_Eyes") && !t.IsChildOf(go.transform))
                {
                    go.transform.SetPositionAndRotation(t.position, t.rotation);
                    go.transform.localScale = Vector3.one * (k * 3f / (s.x + s.y + s.z));
                    return eyes;
                }

            // the surface its front shows, found by rays from in front, top down
            Surface.Build(body, _rends, s);
            float margin = 0.05f, reach = (face - back) + 2f * margin;
            float apart = half * k;
            bool Ray(float across, float h, out float depth)
            {
                Vector3 from = right * across + up * h + front * (face + margin);
                depth = 0f;
                if (!Surface.Cast(body, s, from, -front, reach, out float t)) return false;
                depth = face + margin - t;
                return true;
            }
            float step = Mathf.Max(d / 3f, height / 40f);
            float first = -1f, firstDepth = 0f;
            for (float h = top - height * 0.02f; h >= low + height * 0.3f; h -= step)
                if (Ray(mid, h, out float dep)) { first = h; firstDepth = dep; break; }
            float centreH, surfaceDepth;
            if (first < 0f)
            {
                // no surface to find (unreadable meshes, no colliders): the front of its box, high up
                centreH = top - Mathf.Max(0.6f * d, height * 0.2f);
                surfaceDepth = face;
            }
            else
            {
                // how far the face that was found goes down: a thin edge is straddled, a tall face
                // wears them just under its top
                float lowest = first;
                for (float h = first - step; h >= first - 2f * d && h >= low; h -= step)
                {
                    if (!Ray(mid, h, out float dep) || Mathf.Abs(dep - firstDepth) > 0.5f * d) break;
                    lowest = h;
                }
                centreH = first - lowest < d ? (first + lowest) * 0.5f : first - 0.6f * d;
                surfaceDepth = firstDepth;
                // neither eye sinks into a bulge: the frontmost of the middle and both eyes
                if (Ray(mid, centreH, out float atMid)) surfaceDepth = atMid;
                if (Ray(mid - apart, centreH, out float atL)) surfaceDepth = Mathf.Max(surfaceDepth, atL);
                if (Ray(mid + apart, centreH, out float atR)) surfaceDepth = Mathf.Max(surfaceDepth, atR);
            }
            centreH = Mathf.Max(centreH, low + 0.5f * d);
            Surface.Clear();

            // the backs of the eyeballs pressed a little into the surface
            float backZ = centre.z - ball * 0.5f;
            Vector3 at = right * (mid - centre.x * k) + up * (centreH - centre.y * k)
                + front * (surfaceDepth - Glued * d - backZ * k);
            go.transform.localPosition = new Vector3(at.x / s.x, at.y / s.y, at.z / s.z);
            go.transform.localRotation = Quaternion.LookRotation(front, up);
            go.transform.localScale = new Vector3(k / Along(right, s), k / Along(up, s), k / Along(front, s));
            return eyes;
        }

        /// The name the needle's eyes carry: a disguise cloned from a living object leaves them behind.
        public const string EyesName = "LivingEyes";

        static readonly List<Renderer> _rends = new List<Renderer>();

        /// What the object itself is drawn with: its meshes, never eyes, effects riding it or a
        /// transformation look it wears for a while.
        static void OwnRenderers(Transform body, List<Renderer> into)
        {
            into.Clear();
            var worn = body.GetComponent<WornLook>();
            Transform look = worn != null ? worn.Look : null;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (r.GetComponentInParent<GooglyEyes>(true) != null || r.GetComponentInParent<FxReturn>(true) != null) continue;
                if (look != null && r.transform.IsChildOf(look)) continue;
                into.Add(r);
            }
        }

        static void Span(Vector3 min, Vector3 max, Vector3 axis, out float lo, out float hi)
        {
            float a = Vector3.Dot(min, axis), b = Vector3.Dot(max, axis);
            lo = Mathf.Min(a, b);
            hi = Mathf.Max(a, b);
        }

        /// Metres per unit of its own space along one of its axes.
        static float Along(Vector3 axis, Vector3 s) =>
            Mathf.Max(1e-5f, Mathf.Abs(axis.x) * s.x + Mathf.Abs(axis.y) * s.y + Mathf.Abs(axis.z) * s.z);

        /// The object's surface, for the eye rays: its own triangles where its meshes can be read,
        /// else its colliders (made the same on every machine before the eyes are placed).
        static class Surface
        {
            static readonly List<List<Vector3>> _verts = new List<List<Vector3>>();
            static readonly List<int[]> _tris = new List<int[]>();
            static readonly HashSet<Mesh> _warned = new HashSet<Mesh>();

            /// Its readable meshes, in metres in its own frame.
            public static void Build(Transform body, List<Renderer> rends, Vector3 s)
            {
                Clear();
                foreach (var r in rends)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    if (!mesh.isReadable)
                    {
                        if (_warned.Add(mesh))
                            Debug.LogWarning($"[SpellyZombie] {mesh.name} is not readable: a living {body.name} finds its surface " +
                                "by its colliders. Spelly Zombie/Build/Make Prop Collider Meshes Readable fixes the ones on colliders.", body);
                        continue;
                    }
                    var list = new List<Vector3>();
                    mesh.GetVertices(list);
                    Matrix4x4 to = body.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    for (int i = 0; i < list.Count; i++) list[i] = Vector3.Scale(to.MultiplyPoint3x4(list[i]), s);
                    _verts.Add(list);
                    _tris.Add(mesh.triangles);
                }
            }

            public static void Clear() { _verts.Clear(); _tris.Clear(); }

            /// The nearest surface along a ray in its own frame (metres); t = how far along.
            public static bool Cast(Transform body, Vector3 s, Vector3 from, Vector3 dir, float reach, out float t)
            {
                t = reach;
                bool any = false;
                if (_verts.Count > 0)
                {
                    for (int m = 0; m < _verts.Count; m++)
                        if (SkinHit.RayTriangles(_verts[m], _tris[m], from, dir, t, out float along, out _))
                        {
                            t = along;
                            any = true;
                        }
                    return any;
                }
                // metres in its own frame back to the world: the same length along an axis of it
                var ray = new Ray(body.TransformPoint(new Vector3(from.x / s.x, from.y / s.y, from.z / s.z)),
                    body.rotation * dir);
                foreach (var c in body.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger || !c.enabled) continue;
                    if (c.GetComponentInParent<GooglyEyes>(true) != null || c.GetComponentInParent<FxReturn>(true) != null) continue;
                    if (c.Raycast(ray, out var hit, t)) { t = hit.distance; any = true; }
                }
                return any;
            }
        }
    }
}
