using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// A ray against a zombie's skin as it is posed right now: world = position + rotation * baked
    /// vertex. Its colliders (a fitted capsule, a bind-pose shell) only tell the pen a ray is at it.
    public static class SkinHit
    {
        static Mesh _bake;
        static SkinnedMeshRenderer _baked;
        static int _bakedFrame = -1;
        static readonly List<Vector3> _verts = new List<Vector3>();
        static Mesh _shape;
        static int[] _tris;
        // the BakeMesh flag that bakes it world-sized differs per import (CharacterRig auditions it
        // too): 0 false, 1 true, -1 neither
        static readonly Dictionary<Mesh, int> _flag = new Dictionary<Mesh, int>();

        /// The skin behind a zombie's collider (the host's body or a client's stand-in); null = not a
        /// zombie, or a skin no bake fits (the pen keeps the collider hit then).
        public static SkinnedMeshRenderer OfZombie(Collider c)
        {
            if (c == null) return null;
            SkinnedMeshRenderer smr = null;
            var z = ZombieOwner.From(c);
            if (z != null)
                smr = (z.Dress != null && z.Dress.BodyGO != null ? z.Dress.BodyGO : z.gameObject)
                    .GetComponentInChildren<SkinnedMeshRenderer>();
            else
            {
                var proxy = c.GetComponentInParent<NetZombieProxy>();
                if (proxy != null) smr = proxy.GetComponentInChildren<SkinnedMeshRenderer>();
            }
            return smr != null && smr.sharedMesh != null && Flag(smr) >= 0 ? smr : null;
        }

        static Mesh _plainBake, _scaledBake;
        static readonly Dictionary<Mesh, (bool scaled, bool own)> _mounts = new Dictionary<Mesh, (bool, bool)>();

        /// ★ How a bake of this skin sits in the world (the Photo Booth's ink shells and boxes):
        /// `scaled` = BakeMesh(…, true); `own` = in the renderer's whole transform, else at its
        /// position and rotation with scale 1. BakeMesh reads differently per import (CharacterRig
        /// auditions it, ZombieDress gave up on it), so every way is tried against the skeleton the
        /// skin wraps and the nearest kept, remembered per model once it is clear. False: no bones.
        public static bool Mount(SkinnedMeshRenderer smr, out bool scaled, out bool own)
        {
            scaled = true;
            own = false;
            if (smr == null || smr.sharedMesh == null) return false;
            if (_mounts.TryGetValue(smr.sharedMesh, out var known)) { (scaled, own) = known; return true; }
            bool any = false;
            var skeleton = new Bounds();
            foreach (var b in smr.bones)
            {
                if (b == null) continue;
                if (!any) { skeleton = new Bounds(b.position, Vector3.zero); any = true; }
                else skeleton.Encapsulate(b.position);
            }
            if (!any) return false;
            if (_plainBake == null) _plainBake = new Mesh();
            if (_scaledBake == null) _scaledBake = new Mesh();
            smr.BakeMesh(_plainBake, false);
            _plainBake.RecalculateBounds();
            smr.BakeMesh(_scaledBake, true);
            _scaledBake.RecalculateBounds();
            var t = smr.transform;
            float best = float.MaxValue, next = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                bool s = (i & 1) != 0, o = (i & 2) != 0;
                float miss = Miss(World((s ? _scaledBake : _plainBake).bounds, Placement(t, o)), skeleton);
                if (miss < best) { next = best; best = miss; scaled = s; own = o; }
                else if (miss < next) next = miss;
            }
            if (next - best > 0.5f) _mounts[smr.sharedMesh] = (scaled, own); // a clear winner is the model's way
            return true;
        }

        /// Where a bake mounted `own` or not puts its vertices.
        public static Matrix4x4 Placement(Transform t, bool own) =>
            own ? t.localToWorldMatrix : Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

        /// A box in some space, as the world box around it.
        public static Bounds World(Bounds local, Matrix4x4 m)
        {
            var b = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
            Vector3 e = local.extents;
            for (int i = 0; i < 8; i++)
                b.Encapsulate(m.MultiplyPoint3x4(local.center + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z)));
            return b;
        }

        /// How badly a skin's box misses the skeleton inside it: the centres apart, and the sizes
        /// apart on every axis (a skin lying on its side, or a hundred times too big, misses by a lot).
        static float Miss(Bounds skin, Bounds skeleton)
        {
            Vector3 size = Vector3.Max(skeleton.size, Vector3.one * 0.05f);
            Vector3 got = Vector3.Max(skin.size, Vector3.one * 1e-4f);
            return (skin.center - skeleton.center).magnitude / size.magnitude
                + Mathf.Abs(Mathf.Log(got.x / size.x)) + Mathf.Abs(Mathf.Log(got.y / size.y)) + Mathf.Abs(Mathf.Log(got.z / size.z));
        }

        /// Measured once per mesh against its bind pose at world scale, never assumed.
        static int Flag(SkinnedMeshRenderer smr)
        {
            var mesh = smr.sharedMesh;
            if (_flag.TryGetValue(mesh, out int f)) return f;
            float want = Vector3.Scale(mesh.bounds.size, smr.transform.lossyScale).magnitude;
            f = WorldSized(smr, false, want) ? 0 : WorldSized(smr, true, want) ? 1 : -1;
            if (f < 0) Debug.LogError("[SpellyZombie] " + mesh.name + ": no BakeMesh flag bakes it world-sized; the pen keeps the collider hit.");
            _flag[mesh] = f;
            _baked = null; // the measuring bake is not a pose
            return f;
        }

        static bool WorldSized(SkinnedMeshRenderer smr, bool useScale, float want)
        {
            if (_bake == null) _bake = new Mesh();
            smr.BakeMesh(_bake, useScale);
            _bake.RecalculateBounds();
            float got = _bake.bounds.size.magnitude;
            return got > want / 3f && got < want * 3f;
        }

        /// Nearest skin triangle the ray crosses within maxDistance; false = it passes the body by.
        public static bool Raycast(SkinnedMeshRenderer smr, Ray ray, float maxDistance,
            out Vector3 point, out Vector3 normal, out float distance)
        {
            point = normal = Vector3.zero;
            distance = 0f;
            if (_baked != smr || _bakedFrame != Time.frameCount) // one bake per body per frame
            {
                if (_bake == null) _bake = new Mesh();
                smr.BakeMesh(_bake, Flag(smr) == 1);
                _bake.GetVertices(_verts);
                _baked = smr;
                _bakedFrame = Time.frameCount;
            }
            if (_shape != smr.sharedMesh)
            {
                _shape = smr.sharedMesh;
                _tris = _shape.triangles;
            }

            // the ray in the bake's frame: position and rotation, the scale is in the vertices
            Transform t = smr.transform;
            Quaternion back = Quaternion.Inverse(t.rotation);
            Vector3 o = back * (ray.origin - t.position), dir = back * ray.direction;
            if (!RayTriangles(_verts, _tris, o, dir, maxDistance, out float best, out int tri)) return false;

            Vector3 c0 = _verts[_tris[tri]];
            Vector3 n = Vector3.Cross(_verts[_tris[tri + 1]] - c0, _verts[_tris[tri + 2]] - c0).normalized;
            if (Vector3.Dot(n, dir) > 0f) n = -n; // faces the pen
            distance = best;
            point = ray.origin + ray.direction * best;
            normal = t.rotation * n;
            return true;
        }

        /// The nearest of these triangles a ray crosses within maxDistance, everything in one frame:
        /// how far along it and the triangle's first index. False = it passes them by.
        public static bool RayTriangles(List<Vector3> verts, int[] tris, Vector3 o, Vector3 dir, float maxDistance,
            out float along, out int tri)
        {
            along = maxDistance;
            tri = -1;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]];
                Vector3 e1 = verts[tris[i + 1]] - a, e2 = verts[tris[i + 2]] - a;
                Vector3 p = Vector3.Cross(dir, e2);
                float det = Vector3.Dot(e1, p);
                if (det > -1e-12f && det < 1e-12f) continue;
                float inv = 1f / det;
                Vector3 s = o - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(dir, q) * inv;
                if (v < 0f || u + v > 1f) continue;
                float d = Vector3.Dot(e2, q) * inv;
                if (d <= 0f || d >= along) continue;
                along = d;
                tri = i;
            }
            return tri >= 0;
        }
    }
}
