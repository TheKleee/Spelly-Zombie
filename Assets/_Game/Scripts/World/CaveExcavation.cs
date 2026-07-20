using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum CaveNodeKind { Path, RoomStart, RoomExit, RoomRest, RoomCache, RoomPlain }

    /// ONE point of the cave graph (Marko's ruling: "you should be creating
    /// nodes, and nodes should have names"). Every node is a named object in
    /// the hierarchy — move it, delete it, add your own, then run
    /// "Cave: Rebuild Geometry From Nodes" and the rock re-forms around your
    /// edit. Links define the corridors; Room kinds grow cavities.
    public class CaveNode : MonoBehaviour
    {
        public CaveNodeKind Kind = CaveNodeKind.Path;
        public float Radius = 2.2f;
        public List<CaveNode> Links = new List<CaveNode>();

        void OnDrawGizmos()
        {
            Gizmos.color = Kind == CaveNodeKind.Path ? new Color(1f, 0.6f, 0.1f)
                : Kind == CaveNodeKind.RoomStart ? Color.green
                : Kind == CaveNodeKind.RoomExit ? Color.red
                : new Color(0.3f, 0.7f, 1f);
            Gizmos.DrawWireSphere(transform.position, Kind == CaveNodeKind.Path ? 0.6f : 1.4f);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            foreach (var l in Links)
                if (l != null) Gizmos.DrawLine(transform.position, l.transform.position);
        }
    }

    /// CAVE EXCAVATION v2 — node graph in, ONE continuous rock surface out.
    ///
    /// v1's separate tube/shell meshes could never truly connect (a room's
    /// collider wall blocked the corridor mouth; seams leaked). v2 builds the
    /// whole cave as a SIGNED DISTANCE FIELD — corridors are capsules between
    /// linked nodes, rooms are squashed spheres at room nodes, rocky noise on
    /// top — and polygonizes it with surface nets. Connections are UNIONS:
    /// holes and blocked mouths are mathematically impossible. The mesh gets
    /// an inside face, an outside shell (no more see-through from the editor),
    /// one MeshCollider, and a STONE tag (the walls are spell canvas).
    ///
    /// Marko's flow:
    ///   Spelly Zombie → Cave: Generate NEW Layout   (fresh graph + rock)
    ///   …move/rename/delete nodes in the hierarchy…
    ///   Spelly Zombie → Cave: Rebuild Geometry From Nodes
    ///
    /// MODULE CONTRACT (Resources/Custom) — rooms are DRESSING now, not
    /// shells (the rock provides the walls): Cave_Room_Rest / _Cache /
    /// _Plain / _Start / _Exit prefabs are furniture sets placed at the room
    /// node (origin = room floor center). InkRuneStone = the fuel stone.
    public class CaveExcavation : MonoBehaviour
    {
        [Header("Determinism (0 = roll a fresh cave)")]
        public int Seed;

        [Header("Layout")]
        public Vector3 Bounds = new Vector3(150f, 26f, 150f);
        public int WandererCount = 4;
        public int RoomCount = 7;
        public float CorridorRadius = 3.4f; // wide enough for a carriage AND a panicking crew
        public float RadiusJitter = 0.7f;
        public float MaxSlopeDeg = 11f; // the carriage must CLIMB, not mountaineer

        [Header("Rock surface")]
        public float CellSize = 1.25f;   // marching grid resolution (smaller = finer, slower)
        public float RockNoise = 0.85f;  // wall bumpiness
        public Material WallMaterial;    // Marko's rock material (greybox tint if empty)

        [Header("Floor (walkable, drawable)")]
        [Tooltip("Flat floor sits this fraction of the tunnel radius below the centerline. Lower = higher, wider floor; higher = deeper, narrower.")]
        public float FloorLevel = 0.45f;
        [Tooltip("Meters of gentle floor undulation. 0 = billiard table; keep small so runes draw clean.")]
        public float FloorBumps = 0.30f;

        [Header("Filled at build time")]
        public Transform StartRoom;
        public Transform ExitRoom;

        System.Random _rng;
        Transform _graph;

        float Rand(float lo, float hi) => lo + (float)_rng.NextDouble() * (hi - lo);

        // ================================================== lifecycle ==
        void Start()
        {
            if (transform.childCount == 0) Build();
        }

        [ContextMenu("Build (new graph + geometry)")]
        public void Build()
        {
            Clear(all: true);
            int seed = Seed != 0 ? Seed : System.Environment.TickCount;
            _rng = new System.Random(seed);
            Debug.Log($"[SpellyZombie] CAVE EXCAVATION — seed {seed}");
            GenerateGraph();
            BuildGeometry();
        }

        /// Marko edited the nodes by hand — keep his graph, re-form the rock.
        [ContextMenu("Rebuild Geometry From Nodes")]
        public void RebuildGeometry()
        {
            if (_graph == null) _graph = transform.Find("Graph");
            if (_graph == null) { Build(); return; }
            _rng = new System.Random(Seed != 0 ? Seed : 12345);
            Clear(all: false);
            BuildGeometry();
        }

        void Clear(bool all)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (!all && c.name == "Graph") continue;
                DestroyImmediate(c.gameObject);
            }
            if (all) _graph = null;
        }

        // ================================================ 1. the graph ==
        CaveNode NewNode(string nodeName, Vector3 pos, CaveNodeKind kind, float radius)
        {
            var go = new GameObject(nodeName);
            go.transform.SetParent(_graph, false);
            go.transform.position = pos;
            var n = go.AddComponent<CaveNode>();
            n.Kind = kind;
            n.Radius = radius;
            return n;
        }

        static void Link(CaveNode a, CaveNode b)
        {
            if (a == null || b == null || a == b) return;
            if (!a.Links.Contains(b)) a.Links.Add(b);
            if (!b.Links.Contains(a)) b.Links.Add(a);
        }

        // ---- the DANGER BAND rule: two unrelated corridors must either stay
        // clearly apart or fully merge into a junction. Almost-touching leaves
        // a thin rock membrane the wall noise punches through — or, with the
        // flat floor cut, a hidden pit in the upper corridor's 'billiard
        // table'. Carve-time bookkeeping; Marko's hand-edits stay his call.
        readonly List<(CaveNode a, CaveNode b)> _carved = new List<(CaveNode, CaveNode)>();

        void CarveLink(CaveNode a, CaveNode b)
        {
            Link(a, b);
            _carved.Add((a, b));
        }

        static Vector3 ClosestOnSeg(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return a + ab * t;
        }

        /// Links touching the anchor node OR its direct neighbours are the
        /// junction we're growing out of — connected air, never a membrane.
        static bool NearAnchor(CaveNode fromNode, CaveNode a, CaveNode b)
        {
            if (a == fromNode || b == fromNode) return true;
            return fromNode != null
                && (fromNode.Links.Contains(a) || fromNode.Links.Contains(b));
        }

        /// Approximate segment↔segment distance (samples along the candidate —
        /// plenty at corridor scale) against every carved link that isn't part
        /// of the anchor's own junction.
        bool InDangerBand(CaveNode fromNode, Vector3 from, Vector3 to, float r)
        {
            foreach (var (a, b) in _carved)
            {
                if (a == null || b == null || NearAnchor(fromNode, a, b)) continue;
                float sum = r + Mathf.Max(a.Radius, b.Radius);
                Vector3 pa = a.transform.position, pb = b.transform.position;
                for (int k = 0; k <= 8; k++)
                {
                    Vector3 s = Vector3.Lerp(from, to, k / 8f);
                    float d = Vector3.Distance(s, ClosestOnSeg(pa, pb, s));
                    // closer than sum-1.2 = honest open junction (fine);
                    // farther than sum+2.6 = clean rock between (fine);
                    // between = membrane / floor-pit territory
                    if (d > sum - 1.2f && d < sum + 2.6f) return true;
                }
            }
            return false;
        }

        /// Every re-roll stayed in the band: stop flirting — pull the node all
        /// the way INTO the offending corridor so the tubes MERGE into a
        /// visible junction. (Y-only pulling left horizontal near-misses as
        /// membranes.)
        Vector3 ForceJunction(CaveNode fromNode, Vector3 cand, float r)
        {
            float bestD = float.MaxValue;
            float bestR = CorridorRadius;
            Vector3 bestPt = cand;
            foreach (var (a, b) in _carved)
            {
                if (a == null || b == null || NearAnchor(fromNode, a, b)) continue;
                Vector3 pt = ClosestOnSeg(a.transform.position, b.transform.position, cand);
                float d = (pt - cand).sqrMagnitude;
                if (d < bestD) { bestD = d; bestPt = pt; bestR = Mathf.Max(a.Radius, b.Radius); }
            }
            if (bestD == float.MaxValue) return cand; // nothing carved yet
            Vector3 delta = cand - bestPt;
            float dist = delta.magnitude;
            float want = Mathf.Max(0.5f, r + bestR - 1.4f); // solidly overlapped
            if (dist > want) cand = bestPt + delta * (want / Mathf.Max(dist, 0.001f));
            return cand;
        }

        void GenerateGraph()
        {
            _graph = new GameObject("Graph").transform;
            _graph.SetParent(transform, false);
            _carved.Clear();

            Vector3 half = Bounds * 0.5f;
            Vector3 startPos = new Vector3(-half.x + 9f, Rand(-3f, 3f), Rand(-half.z * 0.5f, half.z * 0.5f));
            Vector3 exitPos = new Vector3(half.x - 9f, Rand(-3f, 3f), Rand(-half.z * 0.5f, half.z * 0.5f));

            var start = NewNode("Room_Start", startPos, CaveNodeKind.RoomStart, 6.5f);
            var exit = NewNode("Room_Exit", exitPos, CaveNodeKind.RoomExit, 6.5f);
            var pathNodes = new List<CaveNode>();

            // THE TRUNK: wanders but leans home — the guaranteed way out
            var trunk = WalkChain("Trunk", start, exitPos, biased: true, steps: 40);
            if (trunk.Count > 0) CarveLink(trunk[trunk.Count - 1], exit);
            pathNodes.AddRange(trunk);

            // WANDERERS: branch off anything carved so far; some just STOP in
            // the dark — dead ends that lead to nothing, on purpose
            for (int w = 0; w < WandererCount; w++)
            {
                var from = pathNodes.Count > 0 ? pathNodes[_rng.Next(pathNodes.Count)] : start;
                var chain = WalkChain($"Wander{w}", from, exitPos, biased: false, steps: _rng.Next(6, 20));
                pathNodes.AddRange(chain);
            }

            // ROOMS: random spots, stitched to the nearest carved node through
            // a CURVING connector chain (you never see a room down a straight)
            string[] kinds = { "Rest", "Rest", "Cache", "Cache", "Plain", "Plain", "Plain" };
            int placed = 0, guard = 0;
            var roomNodes = new List<CaveNode>();
            while (placed < RoomCount && guard++ < 250)
            {
                Vector3 pos = new Vector3(Rand(-half.x + 12f, half.x - 12f),
                    Rand(-half.y * 0.42f, half.y * 0.42f), Rand(-half.z + 12f, half.z - 12f));
                bool crowded = Vector3.Distance(pos, startPos) < 18f || Vector3.Distance(pos, exitPos) < 18f;
                foreach (var r in roomNodes)
                    if (Vector3.Distance(r.transform.position, pos) < 20f) { crowded = true; break; }
                if (crowded) continue;

                string kindName = kinds[placed % kinds.Length];
                var kind = kindName == "Rest" ? CaveNodeKind.RoomRest
                    : kindName == "Cache" ? CaveNodeKind.RoomCache : CaveNodeKind.RoomPlain;

                var nearest = Nearest(pathNodes, pos);
                pos.y = Mathf.Clamp(pos.y, nearest.transform.position.y - 3.5f, nearest.transform.position.y + 3.5f);
                var room = NewNode($"Room_{kindName}_{placed + 1}", pos, kind, Rand(4.5f, 7f));
                roomNodes.Add(room);
                ConnectorChain($"Link{placed}", nearest, room);

                if (_rng.NextDouble() < 0.35 && pathNodes.Count > 4) // loops (the sketch)
                {
                    var other = pathNodes[_rng.Next(pathNodes.Count)];
                    if (Vector3.Distance(other.transform.position, nearest.transform.position) > 14f)
                        ConnectorChain($"Loop{placed}", other, room);
                }
                placed++;
            }
        }

        /// A chain of named Path nodes: clamped random turns (corridors CURVE),
        /// gentle pitch (up AND down), linked as it goes.
        List<CaveNode> WalkChain(string tag, CaveNode from, Vector3 exitPos, bool biased, int steps)
        {
            var chain = new List<CaveNode>();
            Vector3 p = from.transform.position;
            Vector3 half = Bounds * 0.5f;
            float yaw = biased
                ? Mathf.Atan2(exitPos.z - p.z, exitPos.x - p.x) * Mathf.Rad2Deg
                : Rand(0f, 360f);
            float pitch = 0f;
            CaveNode prev = from;

            for (int i = 0; i < steps; i++)
            {
                float r = CorridorRadius + Rand(-RadiusJitter, RadiusJitter);
                Vector3 cand = p;
                float newYaw = yaw, newPitch = pitch;
                bool clean = false;
                for (int attempt = 0; attempt < 6 && !clean; attempt++)
                {
                    newYaw = yaw + Rand(-40f, 40f);
                    if (biased)
                    {
                        float home = Mathf.Atan2(exitPos.z - p.z, exitPos.x - p.x) * Mathf.Rad2Deg;
                        newYaw = Mathf.LerpAngle(newYaw, home, 0.12f + 0.5f * (i / (float)steps));
                    }
                    newPitch = Mathf.Clamp(pitch + Rand(-5f, 5f), -MaxSlopeDeg, MaxSlopeDeg);

                    // LONG strides (Marko: nodes were too close — short runs made
                    // every height change a cliff; spread out, the same climb is a
                    // grade the carriage can actually be dragged up)
                    Vector3 dir = Quaternion.Euler(0f, -newYaw, 0f) * Vector3.right;
                    dir = Quaternion.AngleAxis(newPitch, Vector3.Cross(dir, Vector3.up)) * dir;
                    cand = p + dir.normalized * Rand(9f, 13f);
                    cand.x = Mathf.Clamp(cand.x, -half.x + 7f, half.x - 7f);
                    cand.y = Mathf.Clamp(cand.y, -half.y * 0.45f, half.y * 0.45f);
                    cand.z = Mathf.Clamp(cand.z, -half.z + 7f, half.z - 7f);
                    clean = !InDangerBand(prev, p, cand, r);
                }
                if (!clean) cand = ForceJunction(prev, cand, r);
                yaw = newYaw;
                pitch = newPitch;
                p = cand;

                var n = NewNode($"Node_{tag}_{i}", p, CaveNodeKind.Path, r);
                CarveLink(prev, n);
                chain.Add(n);
                prev = n;

                if (biased && Vector3.Distance(p, exitPos) < 10f) break;
            }
            return chain;
        }

        /// Bend nodes between a path node and a room — sideways-pushed so the
        /// approach curves and the room stays hidden until the last turn.
        void ConnectorChain(string tag, CaveNode a, CaveNode room)
        {
            Vector3 pa = a.transform.position, pb = room.transform.position;
            Vector3 dir = pb - pa;
            Vector3 side = Vector3.Cross(dir.normalized, Vector3.up);
            int mids = Mathf.Max(1, Mathf.FloorToInt(dir.magnitude / 12f)); // longer, gentler bends
            CaveNode prev = a;
            for (int i = 1; i <= mids; i++)
            {
                float t = i / (float)(mids + 1);
                float r = CorridorRadius + Rand(-RadiusJitter, RadiusJitter);
                Vector3 p = pa + dir * t;
                bool clean = false;
                for (int attempt = 0; attempt < 6 && !clean; attempt++)
                {
                    p = pa + dir * t + side * Rand(-6f, 6f) + Vector3.up * Rand(-0.8f, 0.8f);
                    clean = !InDangerBand(prev, prev.transform.position, p, r);
                }
                if (!clean) p = ForceJunction(prev, p, r);
                var n = NewNode($"Node_{tag}_{i}", p, CaveNodeKind.Path, r);
                CarveLink(prev, n);
                prev = n;
            }
            CarveLink(prev, room);
        }

        static CaveNode Nearest(List<CaveNode> nodes, Vector3 to)
        {
            CaveNode best = nodes[0];
            float bestD = float.MaxValue;
            foreach (var n in nodes)
            {
                float d = (n.transform.position - to).sqrMagnitude;
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        // ============================================= 2. the geometry ==
        struct Seg { public Vector3 A, B; public float RA, RB; }
        readonly List<Seg> _segs = new List<Seg>();
        readonly List<CaveNode> _roomList = new List<CaveNode>();

        void CollectGraph()
        {
            _segs.Clear();
            _roomList.Clear();
            var seen = new HashSet<(CaveNode, CaveNode)>();
            foreach (var n in _graph.GetComponentsInChildren<CaveNode>())
            {
                if (n.Kind != CaveNodeKind.Path) _roomList.Add(n);
                foreach (var l in n.Links)
                {
                    if (l == null) continue;
                    var key = n.GetInstanceID() < l.GetInstanceID() ? (n, l) : (l, n);
                    if (!seen.Add(key)) continue;
                    _segs.Add(new Seg
                    {
                        A = n.transform.position, B = l.transform.position,
                        // room ends stay TUBE-sized: the room's own ellipsoid
                        // makes the cavity. Full room radius here let the
                        // capsule end-sphere swallow the room — gouging its
                        // floor (RoomR-CorrR)*FloorLevel deeper than the flat
                        // cut and giving it sphere headroom instead of the
                        // squashed 'wide, not tall' ceiling.
                        RA = SegRadius(n), RB = SegRadius(l)
                    });
                }
            }
        }

        float SegRadius(CaveNode c) => c.Kind == CaveNodeKind.Path
            ? c.Radius : Mathf.Min(c.Radius, CorridorRadius);

        /// The cave AS MATH: negative = air. Corridors are FLAT-BOTTOMED
        /// capsules (Marko: floors must be flat enough to navigate and draw
        /// runes), rooms are squashed spheres cut level with the corridors
        /// that enter them, noise makes the WALLS rock — the floor keeps only
        /// its own gentle 2D undulation.
        float SdfAir(Vector3 p)
        {
            // the floor's own bumps: 2D, low-amplitude — a cave floor, not a lens
            float floorWave = (Mathf.PerlinNoise(p.x * 0.22f, p.z * 0.22f) - 0.5f) * FloorBumps;

            // floorness: how much this point sits on SOME feature's flat floor.
            // A per-feature product (near its floor plane × near its air),
            // max-combined — CONTINUOUS everywhere. Tracking only the winning
            // feature's floor height made the noise term JUMP at feature tie
            // surfaces, extruding rock fins along every room mouth.
            float d = float.MaxValue;
            float floorness = 0f;
            for (int i = 0; i < _segs.Count; i++)
            {
                var s = _segs[i];
                Vector3 ab = s.B - s.A;
                float t = Mathf.Clamp01(Vector3.Dot(p - s.A, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                float r = Mathf.Lerp(s.RA, s.RB, t);
                // floor follows the corridor's slope; room-end radii are tube-
                // clamped (SegRadius) so this cut meets the room floor level
                float fy = Mathf.Lerp(s.A.y, s.B.y, t) - r * FloorLevel + floorWave;
                float di = Mathf.Max(Vector3.Distance(p, s.A + ab * t) - r,
                    fy - p.y); // intersect with the half-space above the floor
                d = Mathf.Min(d, di);
                floorness = Mathf.Max(floorness,
                    Mathf.Clamp01(1f - (p.y - fy) / 1.6f) * Mathf.Clamp01(1f - di / 2f));
            }
            for (int i = 0; i < _roomList.Count; i++)
            {
                var r = _roomList[i];
                Vector3 q = p - r.transform.position;
                q.y /= 0.55f; // squashed cavity — rooms are wide, not tall
                // room floor at CORRIDOR floor depth, so walking in is seamless
                float fy = r.transform.position.y - CorridorRadius * FloorLevel + floorWave;
                float di = Mathf.Max(q.magnitude - r.Radius, fy - p.y);
                d = Mathf.Min(d, di);
                floorness = Mathf.Max(floorness,
                    Mathf.Clamp01(1f - (p.y - fy) / 1.6f) * Mathf.Clamp01(1f - di / 2f));
            }
            // rocky WALLS (never enough to pinch a corridor shut) — faded to
            // nothing at floor level so the 3D noise can't re-crater the flat
            float n3 = Mathf.PerlinNoise(p.x * 0.35f, p.y * 0.35f)
                + Mathf.PerlinNoise(p.y * 0.35f, p.z * 0.35f)
                + Mathf.PerlinNoise(p.z * 0.35f, p.x * 0.35f);
            d += (n3 / 3f - 0.5f) * RockNoise * (1f - floorness);
            return d;
        }

        void BuildGeometry()
        {
            CollectGraph();
            if (_segs.Count == 0) { Debug.LogWarning("[SpellyZombie] Cave: no linked nodes."); return; }

            // grid over the graph's bounds
            Bounds box = new Bounds(_segs[0].A, Vector3.zero);
            foreach (var s in _segs) { box.Encapsulate(s.A); box.Encapsulate(s.B); }
            foreach (var r in _roomList) box.Encapsulate(r.transform.position);
            box.Expand((CorridorRadius + RadiusJitter + RockNoise + 8f) * 2f);

            float cell = CellSize;
            int nx = Mathf.CeilToInt(box.size.x / cell);
            int ny = Mathf.CeilToInt(box.size.y / cell);
            int nz = Mathf.CeilToInt(box.size.z / cell);
            while ((long)(nx + 1) * (ny + 1) * (nz + 1) > 2_500_000) // memory guard
            {
                cell *= 1.25f;
                nx = Mathf.CeilToInt(box.size.x / cell);
                ny = Mathf.CeilToInt(box.size.y / cell);
                nz = Mathf.CeilToInt(box.size.z / cell);
            }
            Vector3 origin = box.min;

            // sample the field once per grid corner
            int sx = nx + 1, sy = ny + 1, sz = nz + 1;
            var field = new float[sx * sy * sz];
            for (int x = 0; x < sx; x++)
                for (int y = 0; y < sy; y++)
                    for (int z = 0; z < sz; z++)
                        field[(x * sy + y) * sz + z] =
                            SdfAir(origin + new Vector3(x, y, z) * cell);
            float F(int x, int y, int z) => field[(x * sy + y) * sz + z];

            // ---- SURFACE NETS: one vertex per sign-change cell ----
            var verts = new List<Vector3>();
            var cellIndex = new int[nx * ny * nz];
            for (int i = 0; i < cellIndex.Length; i++) cellIndex[i] = -1;
            int CellId(int x, int y, int z) => (x * ny + y) * nz + z;

            for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        bool anyNeg = false, anyPos = false;
                        for (int c = 0; c < 8; c++)
                        {
                            float v = F(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1));
                            if (v < 0f) anyNeg = true; else anyPos = true;
                        }
                        if (!anyNeg || !anyPos) continue;

                        // vertex = mean of the edge crossings (classic surface nets)
                        Vector3 sum = Vector3.zero;
                        int cnt = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int c0 = EdgeC0[e], c1 = EdgeC1[e];
                            int x0 = x + (c0 & 1), y0 = y + ((c0 >> 1) & 1), z0 = z + ((c0 >> 2) & 1);
                            int x1 = x + (c1 & 1), y1 = y + ((c1 >> 1) & 1), z1 = z + ((c1 >> 2) & 1);
                            float v0 = F(x0, y0, z0), v1 = F(x1, y1, z1);
                            if ((v0 < 0f) == (v1 < 0f)) continue;
                            float t = v0 / (v0 - v1);
                            sum += Vector3.Lerp(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), t);
                            cnt++;
                        }
                        cellIndex[CellId(x, y, z)] = verts.Count;
                        verts.Add(origin + sum / Mathf.Max(1, cnt) * cell);
                    }

            // ---- quads across every sign-changing grid edge ----
            var tris = new List<int>();
            void Quad(int a, int b, int c, int d, Vector3 towardAir)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0) return;
                Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (Vector3.Dot(n, towardAir) < 0f) { (b, d) = (d, b); } // face the AIR
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(d);
            }

            for (int x = 0; x < sx; x++)
                for (int y = 0; y < sy; y++)
                    for (int z = 0; z < sz; z++)
                    {
                        float v0 = F(x, y, z);
                        // x-edge
                        if (x < nx && y > 0 && y < ny && z > 0 && z < nz)
                        {
                            float v1 = F(x + 1, y, z);
                            if ((v0 < 0f) != (v1 < 0f))
                                Quad(cellIndex[CellId(x, y - 1, z - 1)], cellIndex[CellId(x, y, z - 1)],
                                     cellIndex[CellId(x, y, z)], cellIndex[CellId(x, y - 1, z)],
                                     Vector3.right * (v0 < 0f ? -1f : 1f));
                        }
                        // y-edge
                        if (y < ny && x > 0 && x < nx && z > 0 && z < nz)
                        {
                            float v1 = F(x, y + 1, z);
                            if ((v0 < 0f) != (v1 < 0f))
                                Quad(cellIndex[CellId(x - 1, y, z - 1)], cellIndex[CellId(x, y, z - 1)],
                                     cellIndex[CellId(x, y, z)], cellIndex[CellId(x - 1, y, z)],
                                     Vector3.up * (v0 < 0f ? -1f : 1f));
                        }
                        // z-edge
                        if (z < nz && x > 0 && x < nx && y > 0 && y < ny)
                        {
                            float v1 = F(x, y, z + 1);
                            if ((v0 < 0f) != (v1 < 0f))
                                Quad(cellIndex[CellId(x - 1, y - 1, z)], cellIndex[CellId(x, y - 1, z)],
                                     cellIndex[CellId(x, y, z)], cellIndex[CellId(x - 1, y, z)],
                                     Vector3.forward * (v0 < 0f ? -1f : 1f));
                        }
                    }

            // ---- the rock, in CHUNKS: URP caps how many point lights can
            // touch ONE object (~8), so a single cave-sized mesh let only the
            // luckiest ores light anything ("not all ores are lit"). Chunked,
            // every region has its own light budget — and culling gets cheap.
            const float chunkSize = 22f;
            var chunks = new Dictionary<(int, int, int), (List<Vector3> v, List<int> t, Dictionary<int, int> map)>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 c = (verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f;
                var key = (Mathf.FloorToInt(c.x / chunkSize), Mathf.FloorToInt(c.y / chunkSize),
                    Mathf.FloorToInt(c.z / chunkSize));
                if (!chunks.TryGetValue(key, out var ch))
                {
                    ch = (new List<Vector3>(), new List<int>(), new Dictionary<int, int>());
                    chunks[key] = ch;
                }
                for (int k = 0; k < 3; k++)
                {
                    int src = tris[i + k];
                    if (!ch.map.TryGetValue(src, out int dst))
                    {
                        dst = ch.v.Count;
                        ch.v.Add(verts[src]);
                        ch.map[src] = dst;
                    }
                    ch.t.Add(dst);
                }
            }

            var rockMat = WallMaterial != null ? WallMaterial : LitRock(new Color(0.30f, 0.27f, 0.25f));
            var shellMat = LitRock(new Color(0.10f, 0.09f, 0.09f));
            int chunkNo = 0;
            foreach (var kv in chunks)
            {
                var (v, t, _) = kv.Value;

                var mesh = new Mesh { name = $"CaveRock_{chunkNo}" };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var rock = new GameObject($"Rock_{chunkNo}");
                rock.transform.SetParent(transform, false);
                rock.AddComponent<MeshFilter>().sharedMesh = mesh;
                rock.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
                rock.AddComponent<MeshCollider>().sharedMesh = mesh;
                rock.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Stone;

                // outer shell (flipped copy): no see-through from outside
                var ot = new List<int>(t.Count);
                for (int i = 0; i < t.Count; i += 3)
                { ot.Add(t[i]); ot.Add(t[i + 2]); ot.Add(t[i + 1]); }
                var outMesh = new Mesh { name = $"CaveRockOuter_{chunkNo}" };
                outMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                outMesh.SetVertices(v);
                outMesh.SetTriangles(ot, 0);
                outMesh.RecalculateNormals();
                outMesh.RecalculateBounds();
                var shell = new GameObject($"RockOuter_{chunkNo}");
                shell.transform.SetParent(rock.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = outMesh;
                shell.AddComponent<MeshRenderer>().sharedMaterial = shellMat;
                chunkNo++;
            }

            Physics.SyncTransforms(); // colliders queryable NOW — dressing and stones both raycast onto the floor
            DressRooms();
            ScatterStones();
            PlaceEntryPoints();
            SetCaveMood();
            PlacePlayersOnFloor();
        }

        /// Where the horde crawls in: every dead-end corridor tip gets a
        /// ZombieEntryPoint on the floor. Without them RoundDirector's ring
        /// fallback spawned zombies at the player's height 16m away — usually
        /// inside solid rock, where they fell through the hollow mesh into
        /// random tunnels or died to the void for free ink.
        void PlaceEntryPoints()
        {
            int count = 0;
            foreach (var n in _graph.GetComponentsInChildren<CaveNode>())
            {
                if (n.Kind != CaveNodeKind.Path || n.Links.Count != 1) continue;
                DropEntryPoint(n.transform.position, n.Radius, $"ZombieEntry_{n.name}");
                count++;
            }
            // a fully-looped graph has no dead ends — the exit mouth serves
            if (count == 0 && ExitRoom != null)
                DropEntryPoint(ExitRoom.position, CorridorRadius, "ZombieEntry_Exit");
        }

        void DropEntryPoint(Vector3 at, float radius, string epName)
        {
            var ep = new GameObject(epName);
            ep.transform.SetParent(transform, false);
            float fy = at.y - radius * FloorLevel;
            if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var hit, 16f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                fy = hit.point.y;
            ep.transform.position = new Vector3(at.x, fy, at.z);
            ep.AddComponent<ZombieEntryPoint>();
        }

        /// No landing platform, no air-drop (Marko's rule): players START
        /// standing on the start room's rock floor. Runs after every build and
        /// rebuild, so a scene-saved player can never dangle over fresh rock.
        /// Co-op spawns fan out in a small ring so nobody stacks.
        void PlacePlayersOnFloor()
        {
            if (StartRoom == null) return;
            var players = FindObjectsByType<SimpleFPSController>(FindObjectsSortMode.None);
            int i = 0;
            foreach (var p in players)
            {
                if (p == null) continue;
                float a = 2.4f * i;
                Vector3 at = StartRoom.position
                    + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (2.2f + 0.4f * i);
                // analytic flat-cut height, then snap to the actual polygonized rock
                float fy = StartRoom.position.y - CorridorRadius * FloorLevel;
                if (Physics.Raycast(new Vector3(at.x, StartRoom.position.y + 4f, at.z), Vector3.down,
                        out var hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    fy = hit.point.y;

                var cc = p.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false; // CharacterController fights teleports
                p.transform.position = new Vector3(at.x, fy + 1.05f, at.z);
                if (cc != null) cc.enabled = true;
                if (Application.isPlaying) p.CancelMomentum();
                i++;
            }
        }

        static readonly int[] EdgeC0 = { 0, 1, 3, 2, 4, 5, 7, 6, 0, 1, 3, 2 };
        static readonly int[] EdgeC1 = { 1, 3, 2, 0, 5, 7, 6, 4, 4, 5, 7, 6 };

        /// LIT rock — an unlit greybox material made the whole cave self-glow
        /// (Marko: "way too lit"). URP/Lit reacts to the ink-ores instead.
        static Material _rockA, _rockB;
        static Material LitRock(Color c)
        {
            bool dark = c.r < 0.2f;
            var cached = dark ? _rockB : _rockA;
            if (cached != null) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader) { color = c };
            m.SetFloat("_Smoothness", 0.08f); // rock, not marble
            if (dark) _rockB = m; else _rockA = m;
            return m;
        }

        // ================================================ 3. dressing ==
        void DressRooms()
        {
            foreach (var r in _roomList)
            {
                string kindName = r.Kind.ToString().Replace("Room", "");
                // the flat cut puts the floor at corridor depth, not at the
                // sphere's belly — then snap to the actual polygonized rock
                Vector3 floor = r.transform.position + Vector3.down * (CorridorRadius * FloorLevel - FloorBumps);
                if (Physics.Raycast(r.transform.position, Vector3.down, out var floorHit, 12f))
                    floor = floorHit.point;

                var dressing = PrefabVault.Get($"Cave_Room_{kindName}");
                if (dressing != null)
                {
                    var d = Instantiate(dressing, transform);
                    d.transform.position = floor;
                    d.transform.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
                    d.name = $"Dress_{kindName}";
                }

                if (r.Kind == CaveNodeKind.RoomStart)
                {
                    StartRoom = r.transform;
                    // parented under the cave: rebuilds must not leak an old
                    // cauldron entombed in the new layout's rock, still
                    // dispensing ink through the walls
                    var pot = CaveCauldron.Conjure(floor);
                    pot.transform.SetParent(transform, true);

                    // the safety net's home: without one, a fall in the cave
                    // teleported players to world (0,2,0) — usually solid
                    // rock — and re-caught them forever
                    var net = new GameObject("FallCatcher");
                    net.transform.SetParent(transform, false);
                    net.AddComponent<FallCatcher>().RespawnPoint =
                        floor + new Vector3(2.5f, 1.5f, 0f); // beside the pot, not in it
                }
                if (r.Kind == CaveNodeKind.RoomExit)
                {
                    ExitRoom = r.transform;
                    var beacon = new GameObject("ExitBeacon");
                    beacon.transform.SetParent(transform, false);
                    beacon.transform.position = r.transform.position;
                    var l = beacon.AddComponent<Light>();
                    l.type = LightType.Point; l.range = 10f; l.intensity = 1.5f; // a promise, not a floodlight
                    l.color = new Color(0.55f, 1f, 0.65f);
                }
            }
        }

        /// FUEL IS LIGHT: white stones along corridors — the only light you
        /// haven't made yourself. Feeding them to the cauldron blackens them.
        void ScatterStones()
        {
            // corridors: sparse — the dark walk between pools of light
            foreach (var s in _segs)
            {
                if (_rng.NextDouble() > 0.4) continue;
                PlaceStone(Vector3.Lerp(s.A, s.B, (float)_rng.NextDouble()));
            }

            // rooms: GENEROUS (Marko: "make more ores in rooms so we can see") —
            // rooms are where you rest, repair and read the space; Cache rooms
            // are the ore lodes themselves
            foreach (var r in _roomList)
            {
                int count = r.Kind == CaveNodeKind.RoomCache ? 6 : 3;
                for (int i = 0; i < count; i++)
                {
                    float a = (i / (float)count) * Mathf.PI * 2f + Rand(0f, 0.8f);
                    Vector3 at = r.transform.position
                        + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r.Radius * Rand(0.35f, 0.6f);
                    PlaceStone(at);
                }
            }
        }

        void PlaceStone(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 16f))
                at = hit.point + Vector3.up * 0.16f;

            var custom = PrefabVault.Get("InkRuneStone");
            GameObject stone;
            if (custom != null) stone = Instantiate(custom, at, Quaternion.identity, transform);
            else
            {
                stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                stone.transform.SetParent(transform, true);
                stone.transform.position = at;
                stone.transform.localScale = Vector3.one * 0.35f;
                stone.GetComponent<Renderer>().sharedMaterial =
                    MatterFX.Get(new Color(0.95f, 0.95f, 0.9f), MoteShade.Additive);
            }
            stone.name = "InkRuneStone";
            if (stone.GetComponent<InkRuneStone>() == null)
                stone.AddComponent<InkRuneStone>();
        }

        /// A cave is DARK (Marko: "it's not dark inside or lit properly").
        /// Near-black ambient, black fog for depth, directional suns muted —
        /// the ink-stones, the exit beacon and YOUR light runes do the rest.
        void SetCaveMood()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.08f, 0.088f, 0.115f); // readable cave, still night underground
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.012f, 0.014f, 0.022f);
            RenderSettings.fogDensity = 0.026f;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.intensity = 0f; // underground — no sun at all

            // URP: give every rock chunk the full per-object light budget —
            // the ores ARE the lighting design, they must not lose the lottery
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null && urp.maxAdditionalLightsCount < 8)
                urp.maxAdditionalLightsCount = 8;
        }
    }

    /// THE CAULDRON (Marko's economy, greybox until the carriage): heat runes
    /// keep it BURNING; feed it white ores while it burns and they're DESTROYED
    /// into ink — the fill is a visible white pool, and anyone standing close
    /// refills their wand from it while it lasts. Cold pot = nothing happens.
    public class CaveCauldron : MonoBehaviour
    {
        public static readonly List<CaveCauldron> All = new List<CaveCauldron>();

        public float Fill;                 // ores banked
        public const float Capacity = 12f;

        Thermal _thermal;
        Light _fire;
        Transform _pool;

        public bool Burning => _thermal != null && _thermal.Temperature > 60f;

        public static CaveCauldron Conjure(Vector3 floorAt)
        {
            var root = new GameObject("CaveCauldron");
            root.transform.position = floorAt;

            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Pot";
            pot.transform.SetParent(root.transform, false);
            pot.transform.localPosition = Vector3.up * 0.45f;
            pot.transform.localScale = new Vector3(1.15f, 0.45f, 1.15f);
            pot.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.16f, 0.15f, 0.17f), MoteShade.Opaque);

            // the visible INK POOL — rises as ores are fed (no bars, no text)
            var pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pool.name = "InkPool";
            // Conjure also runs from the EDITOR build menu, where Destroy is
            // illegal (logs an error, destroys nothing — the ghost collider
            // then blocked the pen above the pot)
            var poolCol = pool.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(poolCol);
            else Object.DestroyImmediate(poolCol);
            pool.transform.SetParent(root.transform, false);
            pool.transform.localPosition = Vector3.up * 0.62f;
            pool.transform.localScale = new Vector3(0.95f, 0.02f, 0.95f);
            pool.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.92f, 0.94f, 1f), MoteShade.Additive);

            var fireGo = new GameObject("FireGlow");
            fireGo.transform.SetParent(root.transform, false);
            fireGo.transform.localPosition = Vector3.up * 0.25f;
            var fire = fireGo.AddComponent<Light>();
            fire.type = LightType.Point;
            fire.range = 6f;
            fire.intensity = 0f;
            fire.color = new Color(1f, 0.55f, 0.2f);

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; // particles DONATE to rigidbodies — sparks can heat it

            root.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Metal;
            var dmg = root.AddComponent<Damageable>();
            dmg.Destructible = false; // the pot survives the mayhem around it

            var c = root.AddComponent<CaveCauldron>();
            c._thermal = root.AddComponent<Thermal>();
            c._thermal.HeatCapacity = 2.5f; // takes real fire to get going, cools slow
            c._fire = fire;
            c._pool = pool.transform;
            return c;
        }

        void Awake() => All.Add(this);
        void OnDestroy() => All.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;

            // the fire under the pot IS the thermometer (no gauges)
            if (_fire != null)
            {
                float target = Burning ? 1.6f + Mathf.PerlinNoise(Time.time * 6f, 0.3f) * 0.9f : 0f;
                _fire.intensity = Mathf.MoveTowards(_fire.intensity, target, 4f * dt);
            }

            // the pool level IS the fill meter
            if (_pool != null)
            {
                float f = Mathf.Clamp01(Fill / Capacity);
                var s = _pool.localScale;
                s.y = Mathf.Lerp(0.005f, 0.09f, f);
                _pool.localScale = s;
                _pool.localPosition = new Vector3(0f, 0.62f + f * 0.12f, 0f);
                _pool.gameObject.SetActive(Fill > 0.02f);
            }

            // dispensing: stand close while it holds ink and your wand drinks
            if (Fill > 0.02f)
                foreach (var p in PlayerInk.All)
                {
                    if (p == null) continue;
                    if ((p.transform.position - transform.position).sqrMagnitude > 2.8f * 2.8f) continue;
                    if (p.Ink >= Perks.InkMax - 0.5f) continue;
                    p.Award(DrawingConfig.CauldronInkPerSec * dt);
                    Fill = Mathf.Max(0f, Fill - dt * 0.14f); // an ore ≈ 7s of drinking
                }
        }

        /// A carried ore meets a burning pot: the ore DIES, the ink LIVES.
        public void FeedOre(InkRuneStone ore)
        {
            if (!Burning || ore == null) return;
            Fill = Mathf.Min(Capacity, Fill + 1f);
            ore.Blacken();
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent("the cauldron DRINKS — the corridor behind you is darker now");
        }
    }

    /// A fuel stone: WHITE and glowing until it's fed to the cauldron, then
    /// BLACK and dark — the corridor loses its light (Marko's economy).
    /// PICKABLE (Marko's ruling): E takes it, E drops it. A carried ore is a
    /// walking torch — you borrow the corridor's light to go somewhere darker.
    public class InkRuneStone : MonoBehaviour
    {
        public bool Spent { get; private set; }

        /// The ore the local player is holding (one pair of hands, one ore) —
        /// the cauldron feed will read this and call Blacken().
        public static InkRuneStone Carried { get; private set; }

        Light _glow;
        Collider _col;
        static int _actionFrame; // one E-action per frame (no drop-and-regrab)

        void Start()
        {
            _col = GetComponentInChildren<Collider>();
            _glow = GetComponentInChildren<Light>();
            if (_glow == null)
            {
                var go = new GameObject("StoneGlow");
                go.transform.SetParent(transform, false);
                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.range = 8.5f;
                _glow.intensity = 2f;
                _glow.color = new Color(0.95f, 0.95f, 0.88f);
            }
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            var player = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (player == null) return;

            if (Carried == this)
            {
                // ride in front of the chest — visible in first and third person
                var t = player.transform;
                transform.position = t.position + t.forward * 0.65f + Vector3.up * 0.35f;

                // near a BURNING cauldron, E means FEED, not drop (Marko's loop:
                // ores are destroyed in the burning pot to make more ink)
                CaveCauldron pot = null;
                foreach (var c in CaveCauldron.All)
                    if (c != null && c.Burning
                        && (c.transform.position - t.position).sqrMagnitude < 2.6f * 2.6f)
                    { pot = c; break; }

                if (pot != null && !Spent)
                {
                    UIPrompt.Show("E", "feed the ore to the cauldron", new Color(1f, 0.85f, 0.4f));
                    if (kb.eKey.wasPressedThisFrame && _actionFrame != Time.frameCount)
                    {
                        _actionFrame = Time.frameCount;
                        pot.FeedOre(this);
                        Drop(t.forward * 0.3f); // the blackened husk tumbles by the pot
                    }
                    return;
                }

                if (kb.eKey.wasPressedThisFrame && _actionFrame != Time.frameCount)
                {
                    _actionFrame = Time.frameCount;
                    Drop(t.forward);
                }
                return;
            }

            if (Carried == null
                && (player.transform.position - transform.position).sqrMagnitude < 2.2f * 2.2f)
            {
                UIPrompt.Show("E", Spent ? "take the dead ore" : "take the ore",
                    new Color(0.95f, 0.95f, 0.8f));
                if (kb.eKey.wasPressedThisFrame && _actionFrame != Time.frameCount)
                {
                    _actionFrame = Time.frameCount;
                    Carried = this;
                    if (_col != null) _col.enabled = false;
                    var rb = GetComponent<Rigidbody>();
                    if (rb != null) rb.isKinematic = true;
                }
            }
        }

        void Drop(Vector3 forward)
        {
            Carried = null;
            if (_col != null) _col.enabled = true;
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = 2f;
            rb.linearVelocity = forward * 2.2f + Vector3.up * 1.2f; // a gentle toss
        }

        void OnDestroy()
        {
            if (Carried == this) Carried = null;
        }

        /// The cauldron drank it: the stone goes black, the corridor goes dark.
        public void Blacken()
        {
            if (Spent) return;
            Spent = true;
            if (_glow != null) _glow.enabled = false;
            var r = GetComponentInChildren<Renderer>();
            if (r != null) r.sharedMaterial = MatterFX.Get(new Color(0.05f, 0.05f, 0.06f), MoteShade.Opaque);
        }
    }
}
