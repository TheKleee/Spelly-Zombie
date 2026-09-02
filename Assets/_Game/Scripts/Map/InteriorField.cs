using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ AN INTERIOR/EXTERIOR FIELD (his design, Aug 27): a region that
    /// belongs to a PREFAB, not to a biome - a house's inside, the ground
    /// around a tree. When the prefab lands in the world, the field fills
    /// itself with random details exactly the way a biome fills its box:
    /// grid fields, jittered points, a ray down, a claim. Every match the
    /// clutter is different, which is what lets an acolyte hide in it.
    ///
    /// It does NOT write biome data - it only places things.
    ///
    /// UPPER FLOOR: author-set ("some houses simply have an upper floor").
    /// The structure goes in FIRST - the ceiling tiles itself from ONE
    /// piece, one random cell stays open with a rim piece on each closed
    /// edge, one flight climbs into it - and only then the random details
    /// fill both floors.
    ///
    /// Details are ordinary prefabs, so they can be anything he sets up -
    /// including an AbsorbSource that teaches.
    public class InteriorField : MonoBehaviour
    {
        [System.Serializable]
        public class Detail
        {
            public GameObject Prefab;
            [Range(0f, 1f)]
            [Tooltip("Per grid field: the chance this detail tries to spawn there.")]
            public float Chance = 0.25f;
            [Tooltip("Never more than this many of it in one field.")]
            public int Max = 3;
        }

        [Tooltip("The region, local to this prefab. Inside a house, or the ground around a tree - the field doesn't care which.")]
        public Vector3 Size = new Vector3(6f, 3f, 6f);
        public Vector3 Center = new Vector3(0f, 1.5f, 0f);

        [Tooltip("Grid field size in metres - the same searching law a biome box uses.")]
        public float FieldSize = 1.2f;

        [Tooltip("What may appear here. Prefabs can be anything - flowers, pebbles, furniture, an AbsorbSource.")]
        public Detail[] Details;

        [Header("UPPER FLOOR (leave Ceiling empty for a single floor)")]
        [Tooltip("ONE ceiling tile. The field tiles the room with it, top surface at Ceiling Height. Needs a collider or the upper floor cannot be walked or filled.")]
        public GameObject Ceiling;
        [Tooltip("Top surface of the ceiling tiles above this transform: the upper floor you walk on.")]
        public float CeilingHeight = 2.6f;
        [Tooltip("ONE rim piece for ONE edge of the opening, a kit HoleCover as it comes. The field leaves a random cell open, drops this on the missing tile's pivot and turns it onto each of the three closed edges; the stairs side stays open. Empty = no upper floor access.")]
        public GameObject HoleSide;
        [Tooltip("One flight, a kit Stair_Interior as it comes (Simple or Solid: rails count toward the rise). The field reads its top and foot from the mesh, turns it, scales the rise to Ceiling Height, shrinks the run to what the room allows and seats the top step on the open cell's exit edge.")]
        public GameObject Stairs;

        [Tooltip("Editor only: the random layout the Preview button and the gizmos show. Reroll for another.")]
        public int PreviewSeed = 1;

        public const string PreviewName = "InteriorPreview";
        bool _filled;

        // the flight's top and foot edges, in the Stairs prefab root space,
        // measured in the editor so a build never reads mesh data
        [SerializeField, HideInInspector] Vector3 _stairTop, _stairFoot;
        [SerializeField, HideInInspector] float _stairRise, _stairLen, _stairWidth;
        [SerializeField, HideInInspector] bool _stairMeasured;
        public bool StairsMeasured => _stairMeasured;
        public float StairRise => _stairRise;
        public float StairLen => _stairLen;

#if UNITY_EDITOR
        void OnValidate() { MeasureStairs(); }

        /// Editor only, outside play mode: reads the flight's mesh once and
        /// stores its ends. Keeps the old measurement if reading fails.
        public void MeasureStairs()
        {
            if (Application.isPlaying) return;
            if (Stairs == null) { _stairMeasured = false; return; }
            var pts = new List<Vector3>();
            try
            {
                foreach (var mf in Stairs.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    foreach (var v in mf.sharedMesh.vertices)
                        pts.Add(mf.transform.TransformPoint(v)); // prefab space, Y up
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[InteriorField] {name}: could not read the stairs mesh: {e.Message}");
                return;
            }
            if (pts.Count == 0) { _stairMeasured = false; return; }
            Ends(pts, out Vector3 top, out Vector3 foot, out float rise);
            Vector3 climb = top - foot; climb.y = 0f;
            float len = climb.magnitude;
            Vector3 side = len > 0.01f ? Vector3.Cross(Vector3.up, climb / len) : Vector3.right;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var q in pts) { float w = Vector3.Dot(q, side); lo = Mathf.Min(lo, w); hi = Mathf.Max(hi, w); }
            _stairRise = rise;
            _stairLen = len;
            _stairWidth = hi - lo;
            _stairTop = Stairs.transform.InverseTransformPoint(top);
            _stairFoot = Stairs.transform.InverseTransformPoint(foot);
            _stairMeasured = true;
        }
#endif

        /// Hand-placed in a scene (studio, lobby): fill itself with a seed
        /// carved from where it stands, so every client rolls the same room.
        void Start()
        {
            if (_filled) return;
            int seed = Element.IdFor($"{name}:{Mathf.RoundToInt(transform.position.x * 10f)}:{Mathf.RoundToInt(transform.position.z * 10f)}");
            Fill(new System.Random(seed));
        }

        /// Map generation calls this with ITS rng, so the fill rides the map
        /// seed and stays identical on every machine. The editor preview
        /// passes its own throwaway root and leaves the real fill untouched.
        public void Fill(System.Random rng, Transform under = null)
        {
            if (under == null)
            {
                if (_filled) return;
                _filled = true;
                var stale = transform.Find(PreviewName);
                if (stale != null) { stale.gameObject.SetActive(false); Destroy(stale.gameObject); }
            }
            var root = under != null ? under : transform;
            Physics.SyncTransforms(); // the house may be seconds old - its floors must catch rays
            var scene = gameObject.scene;
            var physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;

            // claims live in this field's space, so a turned house keeps its whole room
            var claims = new List<Bounds>();
            var area = new Bounds(Center, Size + Vector3.up * 0.1f); // a floor hit at exactly floor height counts
            float floorY = Center.y - Size.y * 0.5f;

            // ---- 1. STRUCTURE FIRST (his order): ceiling, the open cell, stairs ----
            bool twoFloors = Ceiling != null;
            Vector3 tile = twoFloors ? AssetBounds(Ceiling).size : Vector3.zero;
            if (tile.x < 0.1f || tile.z < 0.1f) tile = new Vector3(2f, 0.02f, 2f);
            float slabThick = twoFloors ? tile.y : 0f;
            float stairsRun = Stairs != null && _stairMeasured ? _stairLen : 0f;
            var doorCells = new List<Vector2Int>();
            Plan(rng, tile, stairsRun, false, out int cx, out int cz, out float x0, out float z0,
                 out int hx, out int hz, out Vector3 dir, doorCells);
            // the way in stays clear of clutter on the ground floor, every house
            foreach (var c in doorCells)
                claims.Add(new Bounds(new Vector3(x0 + c.x * tile.x, floorY + 1f, z0 + c.y * tile.z),
                    new Vector3(tile.x, 2f, tile.z)));

            float holeLX = x0 + hx * tile.x, holeLZ = z0 + hz * tile.z;
            bool opening = twoFloors && HoleSide != null && cx * cz >= 2;
            if (twoFloors && HoleSide != null && !opening)
                Debug.LogWarning($"[InteriorField] {name}: the room is smaller than one ceiling tile, no upper floor access.");
            if (twoFloors)
            {
                if (Ceiling.GetComponentInChildren<Collider>(true) == null)
                    Debug.LogWarning($"[InteriorField] {name}: ceiling tile {Ceiling.name} has no collider. The upper floor cannot be walked or filled.");
                for (int ix = 0; ix < cx; ix++)
                    for (int iz = 0; iz < cz; iz++)
                    {
                        if (opening && ix == hx && iz == hz) continue; // the way up
                        var piece = Instantiate(Ceiling, root);
                        Seat(piece, x0 + ix * tile.x, CeilingHeight, z0 + iz * tile.z);
                    }
            }
            if (opening)
            {
                bool alongX = Mathf.Abs(dir.x) > 0.5f;
                float len = alongX ? tile.x : tile.z;
                float edge = len * 0.5f;
                float run = RunFor(dir, holeLX, holeLZ, len, stairsRun);
                Vector3 hole = new Vector3(holeLX, CeilingHeight, holeLZ);
                Vector3 top = hole - dir * edge; // the top step: the exit edge onto the next tile

                // the missing tile's pivot, without spawning it: kit rims are built around it
                Bounds ab = AssetBounds(Ceiling);
                Vector3 cellPivot = Ceiling.transform.localPosition
                    + new Vector3(holeLX - ab.center.x, CeilingHeight - ab.max.y, holeLZ - ab.center.z);

                Vector3[] sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                foreach (var n in sides)
                {
                    if (Vector3.Dot(n, -dir) > 0.5f) continue; // the exit edge stays open
                    var trim = Instantiate(HoleSide, root);
                    trim.transform.localPosition = cellPivot;
                    Vector3 off = LocalBounds(trim).center - cellPivot;
                    off.y = 0f;
                    if (off.magnitude > 0.25f)
                    {
                        // geometry sits on one edge of its pivot: turn it onto this edge
                        trim.transform.localRotation = Quaternion.AngleAxis(
                            Vector3.SignedAngle(off.normalized, n, Vector3.up), Vector3.up)
                            * trim.transform.localRotation;
                    }
                    else
                    {
                        // authored on the edge middle, +Z into the opening
                        trim.transform.localRotation =
                            Quaternion.LookRotation(-n) * trim.transform.localRotation;
                        trim.transform.localPosition =
                            hole + n * ((Mathf.Abs(n.x) > 0.5f ? tile.x : tile.z) * 0.5f);
                    }
                    // clean corners from straight pieces: the rim over the flight
                    // grows past both corners, the side rims stop at its inner face
                    Bounds tb = LocalBounds(trim);
                    bool farSide = Vector3.Dot(n, dir) > 0.5f;
                    bool edgeX = Mathf.Abs(n.x) > 0.5f;
                    float along = edgeX ? tb.size.z : tb.size.x;
                    float across = edgeX ? tb.size.x : tb.size.z;
                    if (along > 0.1f && across > 0.01f)
                    {
                        float k = farSide ? (along + across) / along : (along - across * 0.5f) / along;
                        Quaternion inv = Quaternion.Inverse(trim.transform.localRotation);
                        Vector3 sc = trim.transform.localScale;
                        sc[Axis(inv * (edgeX ? Vector3.forward : Vector3.right))] *= k;
                        trim.transform.localScale = sc;
                        if (!farSide) trim.transform.localPosition -= dir * (across * 0.25f);
                    }
                }

                if (Stairs != null)
                {
                    // the flight stays inside the walls even where the tile grid overhangs them
                    Vector3 topAt = top;
                    float width = _stairRise > 0.001f && _stairRise < 0.5f
                        ? _stairWidth * (CeilingHeight - floorY) / _stairRise : _stairWidth;
                    if (_stairMeasured && width > 0.1f)
                    {
                        float half = width * 0.5f + 0.05f;
                        if (alongX) topAt.z = Mathf.Clamp(topAt.z, Center.z - Size.z * 0.5f + half, Center.z + Size.z * 0.5f - half);
                        else topAt.x = Mathf.Clamp(topAt.x, Center.x - Size.x * 0.5f + half, Center.x + Size.x * 0.5f - half);
                    }
                    var stairs = Instantiate(Stairs, root);
                    if (!FitStairs(stairs, dir, topAt, floorY, run))
                    {
                        if (Application.isPlaying) Destroy(stairs); else DestroyImmediate(stairs);
                    }
                }

                // the flight with its landing, the open cell and the exit stay clear of clutter
                float strip = run + FootClearance;
                claims.Add(new Bounds(
                    new Vector3(top.x, (floorY + CeilingHeight) * 0.5f, top.z) + dir * (strip * 0.5f),
                    new Vector3(alongX ? strip : tile.x, CeilingHeight - floorY, alongX ? tile.z : strip)));
                claims.Add(new Bounds(hole, new Vector3(tile.x + 0.24f, 1f, tile.z + 0.24f))); // the rims straddle the edges
                claims.Add(new Bounds(top - dir * (FootClearance * 0.5f) + Vector3.up,
                    new Vector3(alongX ? FootClearance : tile.x, 2f, alongX ? tile.z : FootClearance)));
            }
            if (twoFloors) Physics.SyncTransforms(); // the new tiles must catch the upper floor rays

            // ---- 2. THEN THE RANDOM DETAILS, both floors ----
            if (Details == null || Details.Length == 0) return;
            var placed = new int[Details.Length];

            int nx = Mathf.Max(1, Mathf.FloorToInt(Size.x / FieldSize));
            int nz = Mathf.Max(1, Mathf.FloorToInt(Size.z / FieldSize));
            float gx0 = Center.x - (nx - 1) * FieldSize * 0.5f; // centered, like the tiles
            float gz0 = Center.z - (nz - 1) * FieldSize * 0.5f;
            int floors = twoFloors ? 2 : 1;
            int groundHits = 0, groundRays = 0;

            for (int floor = 0; floor < floors; floor++)
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        int di = rng.Next(Details.Length);
                        var d = Details[di];
                        if (d == null || d.Prefab == null) continue;
                        if (placed[di] >= Mathf.Max(1, d.Max)) continue;
                        if (rng.NextDouble() > d.Chance) continue;

                        float lx = gx0 + ix * FieldSize + ((float)rng.NextDouble() - 0.5f) * FieldSize * 0.6f;
                        float lz = gz0 + iz * FieldSize + ((float)rng.NextDouble() - 0.5f) * FieldSize * 0.6f;

                        // ray from just under this floor's own roof, exactly
                        // the biome law but inside the prefab's world
                        float topLY = floor == 0
                            ? (twoFloors ? CeilingHeight - slabThick - 0.05f : Center.y + Size.y * 0.5f)
                            : Center.y + Size.y * 0.5f;
                        Vector3 from = transform.TransformPoint(new Vector3(lx, topLY, lz));
                        float maxDist = twoFloors && floor == 0
                            ? CeilingHeight - floorY + 0.5f : Size.y + 0.5f;
                        if (floor == 0) groundRays++;
                        if (!physics.Raycast(from, Vector3.down, out var hit, maxDist,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            continue;
                        Vector3 lp = transform.InverseTransformPoint(hit.point);
                        if (!area.Contains(lp)) continue;
                        if (floor == 0) groundHits++;
                        if (hit.normal.y < 0.7f) continue;        // walls are not floors
                        if (twoFloors && floor == 1 && lp.y < CeilingHeight - 0.3f)
                            continue; // the upper pass only dresses the slab

                        var box = d.Prefab.GetComponent<ObjectBox>();
                        Vector3 claimSize = box != null ? box.Size
                            : Vector3.one * (FieldSize * 0.8f);
                        // headroom: nothing spawns where the roof or a slab comes down onto it
                        if (physics.Raycast(hit.point + Vector3.up * 0.05f, Vector3.up, out _,
                                claimSize.y + 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            continue;
                        var claim = new Bounds(lp + (box != null ? box.Center
                            : Vector3.up * claimSize.y * 0.5f), claimSize);
                        bool blocked = false;
                        for (int i = 0; i < claims.Count && !blocked; i++)
                            if (claims[i].Intersects(claim)) blocked = true;
                        if (blocked) continue;
                        claims.Add(claim);

                        var go = Instantiate(d.Prefab, hit.point,
                            Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up),
                            root);
                        if (go.GetComponentInChildren<Element>(true) == null
                            && go.GetComponentInChildren<Collider>(true) != null)
                            go.AddComponent<Element>();
                        placed[di]++;
                    }
            if (groundRays > 0 && groundHits == 0)
                Debug.LogWarning($"[InteriorField] {name}: no ground floor ray landed inside the room. The floor pieces need colliders.");
        }

        /// Moves a child so its bounds center sits on (lx, lz) with its top at topY, all local.
        void Seat(GameObject piece, float lx, float topY, float lz)
        {
            Bounds b = LocalBounds(piece);
            piece.transform.localPosition += new Vector3(lx - b.center.x, topY - b.max.y, lz - b.center.z);
        }

        /// Mesh bounds of every renderer under go, in this field's local space.
        Bounds LocalBounds(GameObject go)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds m = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? m.min.x : m.max.x,
                                            (i & 2) == 0 ? m.min.y : m.max.y,
                                            (i & 4) == 0 ? m.min.z : m.max.z);
                    Vector3 p = transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(transform.InverseTransformPoint(go.transform.position), Vector3.one * 0.5f);
            return b;
        }

        /// A prefab's mesh bounds in its own space, which is what an instance
        /// occupies in this field's space before anything moves it.
        static Bounds AssetBounds(GameObject prefab)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds m = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? m.min.x : m.max.x,
                                            (i & 2) == 0 ? m.min.y : m.max.y,
                                            (i & 4) == 0 ? m.min.z : m.max.z);
                    Vector3 p = mf.transform.TransformPoint(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(Vector3.zero, Vector3.zero);
        }

        const float FootClearance = 1f;   // floor kept in front of the first step
        const float MinRun = 1.8f;        // shorter than this the flight gets too steep
        const float Headroom = 2f;        // kept above the tread where it passes under the next tile
        const float StepMargin = 0.25f;   // one riser plus the slab, the tread line sits below the treads

        /// The run the room allows: the top step sits on the far edge of the
        /// open cell and the flight runs the cell and on under the next tile,
        /// as far as the wall, the landing and the headroom allow.
        float RunFor(Vector3 dir, float holeLX, float holeLZ, float len, float stairsRun)
        {
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            float hole = alongX ? holeLX : holeLZ;
            float wall = alongX ? (dir.x > 0f ? Center.x + Size.x * 0.5f : Center.x - Size.x * 0.5f)
                                : (dir.z > 0f ? Center.z + Size.z * 0.5f : Center.z - Size.z * 0.5f);
            float available = Mathf.Abs(wall - hole) + len * 0.5f - FootClearance;
            float cap = len * (CeilingHeight - (Center.y - Size.y * 0.5f)) / (Headroom + StepMargin);
            float want = stairsRun > 0.1f ? stairsRun : len;
            return Mathf.Min(want, Mathf.Min(available, cap));
        }

        /// Grid, open cell and stairs direction for one rng. The fill and the
        /// gizmo share it, so the markers show exactly what will spawn. Valid:
        /// a tile beyond the top step to walk onto, a flight that fits without
        /// getting too steep, and no door cell under the flight or its landing.
        void Plan(System.Random rng, Vector3 tile, float stairsRun, bool quiet, out int cx, out int cz,
                  out float x0, out float z0, out int hx, out int hz, out Vector3 dir,
                  List<Vector2Int> doorCells)
        {
            cx = Mathf.Max(1, Mathf.CeilToInt(Size.x / Mathf.Max(0.1f, tile.x) - 0.02f));
            cz = Mathf.Max(1, Mathf.CeilToInt(Size.z / Mathf.Max(0.1f, tile.z) - 0.02f));
            x0 = Center.x - (cx - 1) * tile.x * 0.5f;
            z0 = Center.z - (cz - 1) * tile.z * 0.5f;

            doorCells.Clear();
            foreach (var door in GetComponentsInChildren<PathPoint>(true))
            {
                Vector3 at = transform.InverseTransformPoint(door.transform.position);
                Vector3 inward = -transform.InverseTransformDirection(door.transform.forward);
                inward.y = 0f;
                Vector3 inside = at + inward.normalized * Mathf.Min(tile.x, tile.z) * 0.5f;
                var c = new Vector2Int(Mathf.RoundToInt((inside.x - x0) / tile.x),
                                       Mathf.RoundToInt((inside.z - z0) / tile.z));
                if (c.x >= 0 && c.x < cx && c.y >= 0 && c.y < cz)
                {
                    if (!doorCells.Contains(c)) doorCells.Add(c);
                }
                else if (!quiet)
                    Debug.LogWarning($"[InteriorField] {name}: door {door.name} lies outside the field box, its cell is not kept clear.");
            }

            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            var ok = new List<(int x, int z, Vector3 d)>();
            for (int ix = 0; ix < cx; ix++)
                for (int iz = 0; iz < cz; iz++)
                {
                    if (doorCells.Contains(new Vector2Int(ix, iz))) continue;
                    float lx = x0 + ix * tile.x, lz = z0 + iz * tile.z;
                    foreach (var d in dirs)
                    {
                        int sx = Mathf.RoundToInt(d.x), sz = Mathf.RoundToInt(d.z);
                        // a tile beyond the top step to walk onto
                        if (ix - sx < 0 || ix - sx >= cx || iz - sz < 0 || iz - sz >= cz) continue;
                        bool alongX = Mathf.Abs(d.x) > 0.5f;
                        float len = alongX ? tile.x : tile.z;
                        float run = RunFor(d, lx, lz, len, stairsRun);
                        float want = Mathf.Min(stairsRun > 0.1f ? stairsRun : len, MinRun);
                        if (run + 0.01f < want) continue;
                        // every ground cell under the flight and its landing is a free cell
                        int beyond = Mathf.Max(0, Mathf.CeilToInt((run + FootClearance - len) / len - 0.001f));
                        bool blocked = false;
                        for (int k = 1; k <= beyond && !blocked; k++)
                        {
                            var c = new Vector2Int(ix + sx * k, iz + sz * k);
                            if (c.x < 0 || c.x >= cx || c.y < 0 || c.y >= cz || doorCells.Contains(c))
                                blocked = true;
                        }
                        if (blocked) continue;
                        ok.Add((ix, iz, d));
                    }
                }
            if (ok.Count > 0)
            {
                var pick = ok[rng.Next(ok.Count)];
                hx = pick.x; hz = pick.z; dir = pick.d;
            }
            else
            {
                // nothing fits: a free cell if there is one, stairs toward the room center
                var free = new List<Vector2Int>();
                for (int ix = 0; ix < cx; ix++)
                    for (int iz = 0; iz < cz; iz++)
                        if (!doorCells.Contains(new Vector2Int(ix, iz))) free.Add(new Vector2Int(ix, iz));
                if (free.Count > 0) { var c = free[rng.Next(free.Count)]; hx = c.x; hz = c.y; }
                else { hx = rng.Next(cx); hz = rng.Next(cz); }
                float dx = Center.x - (x0 + hx * tile.x), dz = Center.z - (z0 + hz * tile.z);
                dir = Mathf.Abs(dx) >= Mathf.Abs(dz) ? new Vector3(dx >= 0f ? 1f : -1f, 0f, 0f)
                                                     : new Vector3(0f, 0f, dz >= 0f ? 1f : -1f);
                if (!quiet)
                    Debug.LogWarning($"[InteriorField] {name}: no flight fits this room ({Size.x:0.0} x {Size.z:0.0} m, ceiling {CeilingHeight:0.00}); placed anyway toward the center.");
            }
        }

        static int Axis(Vector3 v) =>
            Mathf.Abs(v.x) >= Mathf.Abs(v.y) && Mathf.Abs(v.x) >= Mathf.Abs(v.z) ? 0
            : Mathf.Abs(v.y) >= Mathf.Abs(v.z) ? 1 : 2;

        /// The flight's top and foot edges read from its mesh: the highest
        /// point, and the leading edge of the top face; the lowest point, and
        /// the trailing edge of the bottom face.
        static void Ends(List<Vector3> pts, out Vector3 top, out Vector3 foot, out float rise)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            rise = maxY - minY;
            // the climb direction from the wide bands, the edges from the faces
            float band = Mathf.Max(1e-4f, rise * 0.06f), face = Mathf.Max(1e-5f, rise * 0.005f);
            Vector3 topSum = Vector3.zero, footSum = Vector3.zero;
            int nt = 0, nf = 0;
            foreach (var p in pts)
            {
                if (p.y > maxY - band) { topSum += p; nt++; }
                if (p.y < minY + band) { footSum += p; nf++; }
            }
            top = nt > 0 ? topSum / nt : Vector3.zero;
            foot = nf > 0 ? footSum / nf : Vector3.zero;
            Vector3 climb = top - foot; climb.y = 0f;
            if (climb.sqrMagnitude < 0.01f) { top.y = maxY; foot.y = minY; return; }
            climb.Normalize();
            float maxAlong = float.MinValue, minAlong = float.MaxValue;
            Vector3 topSide = Vector3.zero, footSide = Vector3.zero;
            int ns = 0, nfs = 0;
            foreach (var p in pts)
            {
                float along = Vector3.Dot(p, climb);
                if (p.y >= maxY - face) { maxAlong = Mathf.Max(maxAlong, along); topSide += p - climb * along; ns++; }
                if (p.y <= minY + face) { minAlong = Mathf.Min(minAlong, along); footSide += p - climb * along; nfs++; }
            }
            top = (ns > 0 ? topSide / ns : top - climb * Vector3.Dot(top, climb)) + climb * maxAlong;
            foot = (nfs > 0 ? footSide / nfs : foot - climb * Vector3.Dot(foot, climb)) + climb * minAlong;
            top.y = maxY;
            foot.y = minY;
        }

        /// Turns a flight to climb toward the exit edge, scales its rise to the
        /// ceiling and its run down to what fits, and seats the top step on
        /// that edge, all from the two ends measured in the editor. False when
        /// the flight was never measured: nothing is guessed.
        bool FitStairs(GameObject go, Vector3 dir, Vector3 topTarget, float floorY, float run)
        {
            if (!_stairMeasured)
            {
                Debug.LogError($"[InteriorField] {name}: stairs {Stairs.name} were never measured. Select the field in the editor with the stairs assigned, then save the prefab. No flight placed.");
                return false;
            }
            Vector3 P(Vector3 rootLocal) =>
                transform.InverseTransformPoint(go.transform.TransformPoint(rootLocal));

            Vector3 top = P(_stairTop), foot = P(_stairFoot);
            float rise = top.y - foot.y;
            if (rise > 0.001f && rise < 0.5f)
            {
                // a kit piece saved at scale 1 is in centimetres: bring it to size first
                float u = (topTarget.y - floorY) / rise;
                go.transform.localScale *= u;
                Debug.LogWarning($"[InteriorField] {name}: stairs {Stairs.name} are {rise * 100f:0} cm tall in the prefab, scaled up {u:0} times. Save the prefab at scale 100 like the other kit pieces.");
                top = P(_stairTop); foot = P(_stairFoot);
                rise = top.y - foot.y;
            }
            Vector3 climb = top - foot; climb.y = 0f;
            if (climb.sqrMagnitude < 1e-6f) climb = Vector3.forward;
            climb.Normalize();
            float length = Vector3.Dot(top - foot, climb);

            go.transform.localRotation = Quaternion.AngleAxis(
                Vector3.SignedAngle(climb, -dir, Vector3.up), Vector3.up) * go.transform.localRotation;

            float ky = rise > 0.1f ? (topTarget.y - floorY) / rise : 1f;
            float kr = length > 0.1f && run < length ? run / length : 1f;
            Quaternion inv = Quaternion.Inverse(go.transform.localRotation);
            Vector3 sc = go.transform.localScale;
            sc[Axis(inv * Vector3.up)] *= ky;
            sc[Axis(inv * -dir)] *= kr;
            go.transform.localScale = sc;

            go.transform.localPosition += topTarget - P(_stairTop);
            return true;
        }

        /// Green = the field, white = the cell inside a door, orange = ceiling
        /// tiles, red = the open cell with its three rims, cyan = the flight
        /// from its exit edge down to the foot and landing, all for PreviewSeed.
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.9f, 0.6f, 0.5f);
            Gizmos.DrawWireCube(Center, Size);

            // no tile yet: the guide assumes the 2 m kit tile
            Vector3 tile = Ceiling != null ? AssetBounds(Ceiling).size : Vector3.zero;
            if (tile.x < 0.1f || tile.z < 0.1f) tile = new Vector3(2f, 0.02f, 2f);
            float stairsRun = Stairs != null && _stairMeasured ? _stairLen : 0f;
            var doorCells = new List<Vector2Int>();
            Plan(new System.Random(PreviewSeed), tile, stairsRun, true, out int cx, out int cz,
                 out float x0, out float z0, out int hx, out int hz, out Vector3 dir, doorCells);
            float floorY = Center.y - Size.y * 0.5f;
            Gizmos.color = Color.white;
            foreach (var c in doorCells)
                Gizmos.DrawWireCube(
                    new Vector3(x0 + c.x * tile.x, floorY + 0.02f, z0 + c.y * tile.z),
                    new Vector3(tile.x, 0.04f, tile.z));
            Gizmos.color = new Color(0.9f, 0.7f, 0.3f, 0.6f);
            for (int ix = 0; ix < cx; ix++)
                for (int iz = 0; iz < cz; iz++)
                    if (ix != hx || iz != hz)
                        Gizmos.DrawWireCube(
                            new Vector3(x0 + ix * tile.x, CeilingHeight - tile.y * 0.5f, z0 + iz * tile.z),
                            new Vector3(tile.x, tile.y, tile.z));

            float holeLX = x0 + hx * tile.x, holeLZ = z0 + hz * tile.z;
            Vector3 hole = new Vector3(holeLX, CeilingHeight, holeLZ);
            bool alongX = Mathf.Abs(dir.x) > 0.5f;
            float len = alongX ? tile.x : tile.z;
            float edge = len * 0.5f;
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(hole, new Vector3(tile.x, 0.05f, tile.z));
            Vector3[] sides = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (var n in sides)
            {
                if (Vector3.Dot(n, -dir) > 0.5f) continue;
                bool nx = Mathf.Abs(n.x) > 0.5f;
                Vector3 at = hole + n * ((nx ? tile.x : tile.z) * 0.5f);
                Gizmos.DrawWireCube(at, new Vector3(nx ? 0.24f : tile.x, 0.2f, nx ? tile.z : 0.24f));
            }

            float run = RunFor(dir, holeLX, holeLZ, len, stairsRun);
            Vector3 top = hole - dir * edge;
            Vector3 foot = new Vector3(top.x, floorY, top.z) + dir * run;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(
                new Vector3(top.x, (floorY + CeilingHeight) * 0.5f, top.z) + dir * (run * 0.5f),
                new Vector3(alongX ? run : tile.x, CeilingHeight - floorY, alongX ? tile.z : run));
            Gizmos.DrawLine(foot, top);
            Gizmos.DrawSphere(foot, 0.12f);
            Gizmos.DrawLine(foot, foot + dir * FootClearance); // the landing
            Gizmos.color = Color.green;
            Gizmos.DrawLine(top, top - dir * 0.5f);            // the exit onto the next tile
#if UNITY_EDITOR
            Gizmos.matrix = Matrix4x4.identity;
            UnityEditor.Handles.Label(transform.TransformPoint(foot) + Vector3.up * 0.25f,
                $"Stairs foot, run {run:0.0} m, rise auto");
            UnityEditor.Handles.Label(transform.TransformPoint(top) + Vector3.up * 0.25f,
                "Top step, exit onto the next tile");
#endif
        }
    }
}
