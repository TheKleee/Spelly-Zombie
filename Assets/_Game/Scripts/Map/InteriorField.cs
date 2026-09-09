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
    ///
    /// A detail can carry a field of its own (a table top, a shelf): it fills
    /// right after the piece lands, on the same rng, so small things stack.
    /// Size, Center, FieldSize and Walkway are metres whatever the root's scale,
    /// and the box always stands upright whatever the root's tilt.
    /// The upper floor kit assumes an unscaled root, as a house is.
    public class InteriorField : MonoBehaviour
    {
        [System.Serializable]
        public class Detail
        {
            public GameObject Prefab;
            [Tooltip("Which floor it may take. Upper falls back to the ground floor in a house with no ceiling.")]
            public Floor Where = Floor.Anywhere;
        }

        public enum Floor { Anywhere, Ground, Upper }

        [Tooltip("The region, local to this prefab. Inside a house, or the ground around a tree - the field doesn't care which.")]
        public Vector3 Size = new Vector3(6f, 3f, 6f);
        public Vector3 Center = new Vector3(0f, 1.5f, 0f);

        [Tooltip("Grid field size in metres - the same searching law a biome box uses.")]
        public float FieldSize = 1.2f;

        [Tooltip("What may appear here, fully random: every cell draws one of these and it spawns if it fits the room. Size = its ObjectBox, else its meshes. A detail with its own InteriorField fills it in turn. An absorbable inside a detail is a biome rider: the biome's Sources Min and Max decide whether it shows.")]
        public Detail[] Details;

        [Tooltip("Least walking space kept between things, in metres. Every thing keeps this much plus a random bit more, so the gaps never look measured.")]
        public float Walkway = 0.6f;

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

        /// The field's yaw about world up, read from whichever of its axes lies
        /// flattest: a kit root saved at -90 X points its forward at the sky.
        float FrameYaw
        {
            get
            {
                Vector3 f = transform.forward; f.y = 0f;
                if (f.sqrMagnitude < 0.01f) { f = transform.up; f.y = 0f; }
                if (f.sqrMagnitude < 0.01f) { f = transform.right; f.y = 0f; }
                return f.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }
        }

        /// The field's frame: its place and yaw at unit scale, standing upright
        /// whatever the root's tilt, so the metres he types stay metres and Y
        /// stays up on a scaled, tilted kit root.
        public Matrix4x4 FieldToWorld =>
            Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, FrameYaw, 0f), Vector3.one);
        Matrix4x4 WorldToField => FieldToWorld.inverse;

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
        public void Fill(System.Random rng, Transform under = null, bool riders = true)
        {
            if (under == null)
            {
                if (_filled) return;
                _filled = true;
                var stale = transform.Find(PreviewName);
                if (stale != null) { stale.gameObject.SetActive(false); Destroy(stale.gameObject); }
            }
            var root = under != null ? under : transform;
            // a chest decides what its inside holds before anything lands
            var chest = GetComponentInParent<ChestLid>(true);
            if (chest != null && chest.Inside == this && !chest.Roll(rng, under)) return;
            Matrix4x4 f2w = FieldToWorld, w2f = f2w.inverse;
            Physics.SyncTransforms(); // the house may be seconds old - its floors must catch rays
            var scene = gameObject.scene;
            var physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;

            // claims live in this field's space, so a turned house keeps its whole room
            var claims = new List<Bounds>();
            // furniture he placed by hand keeps its space when it carries an
            // ObjectBox; the house's own box (the prop claim) is not a room item
            foreach (var ob in GetComponentsInChildren<ObjectBox>(true))
            {
                if (ob.transform == transform || ob.transform.IsChildOf(root) && root != transform) continue;
                claims.Add(BoxIn(ob.Center, ob.Size, ob.transform.localToWorldMatrix));
            }
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
            // fully random: cells come in random order, each draws one of the
            // details and it spawns if it fits the room. The first thing that
            // fits always appears; every thing after it makes the next slot
            // likelier to stay empty, so a house is never bare and rarely full
            if (Details == null || Details.Length == 0) return;

            int nx = Mathf.Max(1, Mathf.FloorToInt(Size.x / FieldSize));
            int nz = Mathf.Max(1, Mathf.FloorToInt(Size.z / FieldSize));
            float gx0 = Center.x - (nx - 1) * FieldSize * 0.5f; // centered, like the tiles
            float gz0 = Center.z - (nz - 1) * FieldSize * 0.5f;
            int floors = twoFloors ? 2 : 1;
            int landed = 0;
            var rays = new int[floors]; var hits = new int[floors];
            var tried = new int[floors]; var placed = new int[floors];

            // what may take a cell on each floor
            var pools = new List<Detail>[floors];
            for (int floor = 0; floor < floors; floor++)
            {
                pools[floor] = new List<Detail>();
                foreach (var d in Details)
                {
                    if (d == null || d.Prefab == null) continue;
                    if (d.Where == Floor.Ground && floor != 0) continue;
                    if (d.Where == Floor.Upper && twoFloors && floor != 1) continue;
                    pools[floor].Add(d);
                }
            }

            // one cell: rays for the floor, then offers the details in random
            // order and the first one that fits lands - a bed that cannot fit
            // never costs the cell its stool
            var order = new List<Detail>();
            var keys = new List<float>();
            // a piece's floor area: every cell offers the largest first, so a
            // bed asks for its room before a stool takes the corner
            var weight = new Dictionary<Detail, float>();
            foreach (var d in Details)
            {
                if (d == null || d.Prefab == null || weight.ContainsKey(d)) continue;
                Vector3 fp = Footprint(d.Prefab, Vector3.zero, SpellyMap.Facing(d.Prefab, 0f)).size;
                weight[d] = Mathf.Max(0.01f, fp.x * fp.z);
            }
            float fieldYaw = FrameYaw;

            // the editor preview cannot trust the stage's physics (a prefab
            // opened from its scene instance can leave collider poses stale),
            // so a ray that finds nothing asks the collider meshes themselves
#if UNITY_EDITOR
            var meshRay = under != null && !Application.isPlaying ? new EditorMeshRay(transform) : null;
#endif
            bool Down(Vector3 from, float maxDist, out RaycastHit hit)
            {
                if (physics.Raycast(from, Vector3.down, out hit, maxDist,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
#if UNITY_EDITOR
                if (meshRay != null) return meshRay.Cast(from, Vector3.down, maxDist, out hit);
#endif
                return false;
            }
            bool Up(Vector3 from, float maxDist)
            {
                if (physics.Raycast(from, Vector3.up, out _, maxDist,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
#if UNITY_EDITOR
                if (meshRay != null) return meshRay.Cast(from, Vector3.up, maxDist, out _);
#endif
                return false;
            }

            bool Cell(int floor, int ix, int iz)
            {
                var pool = pools[floor];
                if (pool.Count == 0) return false;

                float lx = gx0 + ix * FieldSize + ((float)rng.NextDouble() - 0.5f) * FieldSize * 0.6f;
                float lz = gz0 + iz * FieldSize + ((float)rng.NextDouble() - 0.5f) * FieldSize * 0.6f;

                // ray from just under this floor's own roof, exactly
                // the biome law but inside the prefab's world
                float topLY = floor == 0
                    ? (twoFloors ? CeilingHeight - slabThick - 0.05f : Center.y + Size.y * 0.5f)
                    : Center.y + Size.y * 0.5f;
                Vector3 from = f2w.MultiplyPoint3x4(new Vector3(lx, topLY, lz));
                float maxDist = twoFloors && floor == 0 ? CeilingHeight - floorY + 0.5f : Size.y + 0.5f;
                rays[floor]++;
                if (!Down(from, maxDist, out var hit)) return false;
                Vector3 lp = w2f.MultiplyPoint3x4(hit.point);
                if (!area.Contains(lp)) return false;
                if (hit.normal.y < 0.7f) return false;        // walls are not floors
                // a floor, not the top of a bed: the house dresses its floors,
                // a piece dresses itself through its own field
                if (floor == 0 && lp.y > floorY + FloorTolerance) return false;
                if (twoFloors && floor == 1 && lp.y < CeilingHeight - 0.3f)
                    return false; // the upper pass only dresses the slab
                hits[floor]++;

                order.Clear();
                keys.Clear();
                foreach (var d in pool)
                {
                    // largest first, equals in random order
                    float key = weight[d] + (float)rng.NextDouble() * 0.001f;
                    int k = 0;
                    while (k < keys.Count && keys[k] >= key) k++;
                    order.Insert(k, d);
                    keys.Insert(k, key);
                }
                foreach (var d in order)
                {
                    // headroom: nothing spawns where the roof, a slab or the next
                    // board comes down onto it; a finger of air above is enough
                    float height = Footprint(d.Prefab, hit.point, SpellyMap.Facing(d.Prefab, 0f)).size.y;
                    if (Up(hit.point + Vector3.up * HeadroomAir, height + HeadroomAir)) continue;

                    // of the four square turns, the one whose front looks at the
                    // room comes first and the back-to-the-room one last: a cabinet
                    // against a wall shows its doors. Front = the standing piece's
                    // +Z, the kit's Blender front
                    Vector2 toMid = new Vector2(Center.x - lx, Center.z - lz);
                    int facing = 0;
                    float bestDot = float.NegativeInfinity;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = 90f * k * Mathf.Deg2Rad;
                        float dot = Mathf.Sin(a) * toMid.x + Mathf.Cos(a) * toMid.y;
                        if (dot > bestDot) { bestDot = dot; facing = k; }
                    }
                    for (int t = 0; t < 7; t++)
                    {
                        // three random turns, then the four square to the room
                        // for what only fits along a wall
                        float yawDeg = t < 3 ? (float)rng.NextDouble() * 360f
                                             : fieldYaw + 90f * ((facing + SquareOrder[t - 3]) & 3);
                        Quaternion rot = SpellyMap.Facing(d.Prefab, yawDeg);
                        Bounds claim = Footprint(d.Prefab, hit.point, rot);
                        // inside the walls
                        if (claim.min.x < area.min.x - 0.05f || claim.max.x > area.max.x + 0.05f
                            || claim.min.z < area.min.z - 0.05f || claim.max.z > area.max.z + 0.05f) continue;
                        // walking space to doors, stairs and everything placed before:
                        // at least Walkway, randomly more
                        Bounds padded = claim;
                        padded.Expand(new Vector3(1f, 0f, 1f) * (Mathf.Max(0f, Walkway) * (1f + (float)rng.NextDouble())));
                        bool blocked = false;
                        for (int i = 0; i < claims.Count && !blocked; i++)
                            if (claims[i].Intersects(padded)) blocked = true;
                        if (blocked) continue;
                        claims.Add(padded);

                        var go = Instantiate(d.Prefab, hit.point, rot, root);
                        // the piece keeps its own size under a scaled root
                        Vector3 ps = root.lossyScale;
                        go.transform.localScale = Vector3.Scale(d.Prefab.transform.localScale,
                            new Vector3(1f / Mathf.Max(1e-4f, ps.x), 1f / Mathf.Max(1e-4f, ps.y), 1f / Mathf.Max(1e-4f, ps.z)));
                        if (go.GetComponentInChildren<Element>(true) == null
                            && go.GetComponentInChildren<Collider>(true) != null)
                            go.AddComponent<Element>();
                        // a detail carrying an absorbable is a biome RIDER: the WHOLE
                        // detail spawns disabled and the biome's scarcity reveals it or
                        // not, the same MinSources..MaxSources law as an unticked object
                        // inside a house
                        if (riders && (go.GetComponentInChildren<AbsorbSource>(true) != null
                                       || go.GetComponentInChildren<Analyzable>(true) != null))
                            go.SetActive(false);
                        // a piece with a field of its own fills it now, on the same rng:
                        // small things on a table, on a shelf
                        foreach (var nf in go.GetComponentsInChildren<InteriorField>(true))
                            nf.Fill(rng, under != null ? nf.transform : null, riders);
                        landed++;
                        placed[floor]++;
                        return true;
                    }
                }
                return false;
            }

            // every cell of every floor, in random order
            var cells = new List<Vector3Int>();
            for (int floor = 0; floor < floors; floor++)
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                        cells.Add(new Vector3Int(ix, floor, iz));
            for (int i = cells.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = cells[i]; cells[i] = cells[j]; cells[j] = tmp;
            }
            foreach (var c in cells)
            {
                if (rng.NextDouble() * (1 + landed) >= 1.0) continue; // this slot stays empty
                tried[c.y]++;
                Cell(c.y, c.x, c.z);
            }
            if (rays[0] > 0 && hits[0] == 0)
                Debug.LogWarning($"[InteriorField] {name}: no ground floor ray landed inside the room. The floor pieces need colliders.");
            if (under != null)
            {
                // the editor preview reports the roll, floor by floor
                string upper = floors > 1
                    ? $"; upper {placed[1]} placed of {tried[1]} tried, {hits[1]}/{rays[1]} rays found a floor" : "";
                Debug.Log($"[InteriorField] {name} preview: {nx * nz} cells per floor, ground {placed[0]} placed of {tried[0]} tried, {hits[0]}/{rays[0]} rays found a floor{upper}");
            }
        }

        /// The space a detail takes standing at 'at' turned by 'rot', as the
        /// axis-aligned box it covers in this field's space: its ObjectBox when
        /// it has one, else its meshes.
        Bounds Footprint(GameObject prefab, Vector3 at, Quaternion rot)
        {
            var box = prefab.GetComponent<ObjectBox>();
            if (box != null)
                return BoxIn(box.Center, box.Size, Matrix4x4.TRS(at, rot, prefab.transform.localScale));
            // the meshes measured UPRIGHT and un-yawed around the root's pivot,
            // so a piece saved turned in its file still claims its tight box;
            // the instance is that box turned about up by the rest of rot
            Quaternion stand = SpellyMap.Facing(prefab, 0f);
            Bounds b = UprightBounds(prefab, stand * Quaternion.Inverse(prefab.transform.rotation));
            if (b.size.x < 0.01f && b.size.z < 0.01f) b = new Bounds(Vector3.up * 0.4f, Vector3.one * 0.8f);
            Quaternion yaw = rot * Quaternion.Inverse(stand);
            return BoxIn(b.center, b.size, Matrix4x4.TRS(at, yaw, Vector3.one));
        }

        /// A prefab's mesh bounds around its pivot after 'untilt' stands it up
        /// square: the tight box the piece really needs.
        static Bounds UprightBounds(GameObject prefab, Quaternion untilt)
        {
            bool any = false;
            var b = new Bounds();
            Vector3 pivot = prefab.transform.position;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds m = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? m.min.x : m.max.x,
                                            (i & 2) == 0 ? m.min.y : m.max.y,
                                            (i & 4) == 0 ? m.min.z : m.max.z);
                    Vector3 p = untilt * (mf.transform.TransformPoint(c) - pivot);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(Vector3.zero, Vector3.zero);
        }

        /// A box (center, size) carried by toWorld, as the axis-aligned box it
        /// covers in this field's space.
        Bounds BoxIn(Vector3 center, Vector3 size, Matrix4x4 toWorld)
        {
            Matrix4x4 m = WorldToField * toWorld;
            Vector3 lo = center - size * 0.5f, hi = center + size * 0.5f;
            var b = new Bounds(m.MultiplyPoint3x4(lo), Vector3.zero);
            for (int i = 1; i < 8; i++)
                b.Encapsulate(m.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z)));
            return b;
        }

        /// Moves a child so its bounds center sits on (lx, lz) with its top at topY, all local.
        void Seat(GameObject piece, float lx, float topY, float lz)
        {
            Bounds b = LocalBounds(piece);
            piece.transform.position += FieldToWorld.MultiplyVector(new Vector3(lx - b.center.x, topY - b.max.y, lz - b.center.z));
        }

        /// Mesh bounds of every renderer under go, in this field's local space.
        Bounds LocalBounds(GameObject go)
        {
            Matrix4x4 w2f = WorldToField;
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
                    Vector3 p = w2f.MultiplyPoint3x4(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(w2f.MultiplyPoint3x4(go.transform.position), Vector3.one * 0.5f);
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

        static readonly int[] SquareOrder = { 0, 1, 3, 2 }; // room-facing, its two sides, its back
        const float FloorTolerance = 0.35f; // a floor sits within this of the box bottom; higher is furniture
        const float HeadroomAir = 0.02f;    // air kept over a piece; a book on a 13 cm shelf gap must still fit
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
            Matrix4x4 w2f = WorldToField;
            foreach (var door in GetComponentsInChildren<PathPoint>(true))
            {
                Vector3 at = w2f.MultiplyPoint3x4(door.transform.position);
                Vector3 inward = -w2f.MultiplyVector(door.transform.forward);
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
            Matrix4x4 w2f = WorldToField;
            Vector3 P(Vector3 rootLocal) => w2f.MultiplyPoint3x4(go.transform.TransformPoint(rootLocal));

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

#if UNITY_EDITOR
        /// Preview only: ray hits against the mesh colliders under a root
        /// through the editor's own mesh intersection, no physics scene needed,
        /// so the preview lands exactly where the game's rays would.
        class EditorMeshRay
        {
            static System.Reflection.MethodInfo _intersect;
            readonly Transform _root;
            readonly object[] _args = new object[4];

            public EditorMeshRay(Transform root)
            {
                _root = root;
                if (_intersect == null)
                    _intersect = typeof(UnityEditor.HandleUtility).GetMethod("IntersectRayMesh",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            }

            public bool Cast(Vector3 from, Vector3 dir, float maxDist, out RaycastHit best)
            {
                best = default;
                if (_intersect == null) return false;
                var ray = new Ray(from, dir);
                float bestDist = maxDist;
                bool any = false;
                // the mesh colliders themselves, so a skinned piece with its
                // collider on the root counts exactly as physics would count it
                foreach (var mc in _root.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc.sharedMesh == null || !mc.enabled || mc.isTrigger || !mc.gameObject.activeInHierarchy) continue;
                    _args[0] = ray; _args[1] = mc.sharedMesh; _args[2] = mc.transform.localToWorldMatrix; _args[3] = null;
                    if (!(bool)_intersect.Invoke(null, _args)) continue;
                    var h = (RaycastHit)_args[3];
                    if (h.distance < bestDist) { bestDist = h.distance; best = h; any = true; }
                }
                return any;
            }
        }
#endif

        /// Green = the field, white = the cell inside a door, orange = ceiling
        /// tiles, red = the open cell with its three rims, cyan = the flight
        /// from its exit edge down to the foot and landing, all for PreviewSeed.
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = FieldToWorld;
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
            UnityEditor.Handles.Label(FieldToWorld.MultiplyPoint3x4(foot) + Vector3.up * 0.25f,
                $"Stairs foot, run {run:0.0} m, rise auto");
            UnityEditor.Handles.Label(FieldToWorld.MultiplyPoint3x4(top) + Vector3.up * 0.25f,
                "Top step, exit onto the next tile");
#endif
        }
    }
}
