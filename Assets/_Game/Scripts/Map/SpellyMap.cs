using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Builds a Unity Terrain from the Biome boxes in the scene: each box
    /// sets place and height, higher layers cut lower ones, cliffs paint by
    /// slope. Preview: right-click the component header -> Generate Preview.
    public class SpellyMap : MonoBehaviour
    {
        [Tooltip("The loop: +-this many metres from center is the playable universe - the wrap seam and the spawn clip. The TERRAIN's size comes from the biome boxes alone; keep the water box comfortably larger than the loop.")]
        public float LoopDistance = 80f;

        [Tooltip("Randomized maps reshuffle biome boxes per seed (not built yet - layout is authored for now).")]
        public bool Randomized;

        [Tooltip("Your grey stone brush, painted automatically wherever the ground is steeper than Cliff Slope.")]
        public TerrainLayer CliffLayer;
        [Tooltip("Degrees. Steeper than this = cliff paint (and later: no walking, no spawns).")]
        public float CliffSlope = 38f;

        [Tooltip("How soft the region borders are: smoothing passes over the cuts. 0 = paper walls, 4 = steep cliffs, 8+ = rolling blends (each pass widens transitions by roughly a metre).")]
        [Range(0, 12)]
        public int Softness = 4;

        [Tooltip("Path width in metres (the walkable ribbon; shoulders blend a bit wider).")]
        public float PathWidth = 2.4f;
        [Tooltip("Degrees. Paths are CARVED so they never exceed this grade - the ramps through your cliffs come from here.")]
        public float PathSlope = 16f;

        [Tooltip("Metres of organic wobble on every biome border - kills the square island. All borders bend TOGETHER, so nested rings stay nested.")]
        public float CoastWobble = 10f;
        [Tooltip("Metres per wobble bump. Bigger = long lazy bays, smaller = busy fjords.")]
        public float WobbleScale = 32f;

        [Tooltip("Heightmap resolution. 257 is plenty for a lowpoly island.")]
        public int Resolution = 257;

        [Header("HORIZON")]
        [Tooltip("Distance fog color - match the sky at the waterline.")]
        public Color FogColor = new Color(0.62f, 0.85f, 0.92f);
        [Tooltip("Fog begins here (metres from camera).")]
        public float FogStart = 30f;
        [Tooltip("Full fog here. Keep it under LoopDistance so the wrap seam and the world edge are never visible.")]
        public float FogEnd = 72f;

        [Tooltip("At most this many cauldrons per map: biomes with a Cauldron slot filled are the candidates, the seed picks the winners - one pot per chosen biome.")]
        public int CauldronLimit = 3;

        [Tooltip("Seed used by the editor preview button. Matches use the daily pool instead.")]
        public int PreviewSeed = 12345;

        [Tooltip("Draw the path network as purple lines in the Scene view, updating live as you move nodes and boxes. Needs one Generate first.")]
        public bool AlwaysPreviewPaths = true;

        [Tooltip("Rebuild the whole map automatically about a second after you stop editing boxes or nodes. Edit-mode only.")]
        public bool AutoRegenerate = false;

        const string GeneratedName = "~SpellyTerrain";

        void Start()
        {
            Generate(MatchLobby.Seed != 0 ? MatchLobby.Seed : PreviewSeed);

            var cam = Camera.main;
            if (cam != null && cam.GetComponent<LiquidSense>() == null)
                cam.gameObject.AddComponent<LiquidSense>();

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null) continue;
                var wrap = p.GetComponent<LoopWrap>();
                if (wrap == null) wrap = p.gameObject.AddComponent<LoopWrap>();
                wrap.Limit = LoopDistance;
            }
        }

        float _wrapCheck;

        void Update()
        {
            // late joiners: ensure LoopWrap at 1Hz
            if (!Application.isPlaying || (_wrapCheck -= Time.deltaTime) > 0f) return;
            _wrapCheck = 1f;
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null) continue;
                var wrap = p.GetComponent<LoopWrap>();
                if (wrap == null) wrap = p.gameObject.AddComponent<LoopWrap>();
                wrap.Limit = LoopDistance;
            }
        }

        [ContextMenu("Generate Preview")]
        public void GeneratePreview() => Generate(PreviewSeed);

        [ContextMenu("Clear Generated")]
        public void ClearGenerated()
        {
            var old = transform.Find(GeneratedName);
            if (old != null) DestroyImmediate(old.gameObject);
        }

        /// Grow the world from the boxes. Deterministic per seed.
        public void Generate(int seed)
        {
            var biomes = FindObjectsByType<Biome>(FindObjectsSortMode.None);
            if (biomes.Length == 0)
            {
                Debug.LogError("[SpellyZombie] SpellyMap: no Biome boxes in the scene - nothing to grow.", this);
                return;
            }

            ClearGenerated();
            var root = new GameObject(GeneratedName);
            root.transform.SetParent(transform, false);

            // highest layer first: the first box containing a point wins
            System.Array.Sort(biomes, (a, b) => b.Layer.CompareTo(a.Layer));

            // the vertical range the terrain must be able to express
            float baseY = float.MaxValue, topY = float.MinValue;
            foreach (var b in biomes)
            {
                baseY = Mathf.Min(baseY, b.Area.min.y);
                topY = Mathf.Max(topY, b.Area.max.y);
            }
            baseY -= 1f;
            float heightRange = Mathf.Max(4f, topY - baseY + 2f);

            int res = Mathf.ClosestPowerOfTwo(Mathf.Clamp(Resolution - 1, 32, 1024)) + 1;

            var rng = new System.Random(seed);
            float nx = (float)rng.NextDouble() * 4096f;
            float nz = (float)rng.NextDouble() * 4096f;

            _sorted = biomes;
            _warpX = (float)rng.NextDouble() * 4096f;
            _warpZ = (float)rng.NextDouble() * 4096f;

            PrepareLayout(biomes, rng);

            // who owns which ground: one wizard home, the rest acolyte
            SpawnPlan.Build(biomes, seed);

            // terrain bounds = union of the biome boxes
            float wMinX = float.MaxValue, wMaxX = float.MinValue;
            float wMinZ = float.MaxValue, wMaxZ = float.MinValue;
            for (int i = 0; i < _sorted.Length; i++)
            {
                wMinX = Mathf.Min(wMinX, _minX[i]); wMaxX = Mathf.Max(wMaxX, _maxX[i]);
                wMinZ = Mathf.Min(wMinZ, _minZ[i]); wMaxZ = Mathf.Max(wMaxZ, _maxZ[i]);
            }
            float world = Mathf.Max(4f, Mathf.Max(wMaxX - wMinX, wMaxZ - wMinZ));
            _west = (wMinX + wMaxX) * 0.5f - world * 0.5f;
            _south = (wMinZ + wMaxZ) * 0.5f - world * 0.5f;

            // ---- heights: winner box -> its band + its noise ----
            float[,] heights = new float[res, res];
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float wx = _west + world * x / (res - 1);
                    float wz = _south + world * z / (res - 1);
                    Biome win = WinnerAt(wx, wz);
                    float h;
                    if (win == null) h = baseY + 0.5f; // outside all biomes
                    else
                    {
                        float n = Mathf.PerlinNoise(nx + wx / Mathf.Max(1f, win.NoiseScale),
                                                    nz + wz / Mathf.Max(1f, win.NoiseScale));
                        h = win.Area.min.y + Mathf.Min(win.FloorNoise * n, win.Size.y);
                    }
                    heights[z, x] = Mathf.Clamp01((h - baseY) / heightRange);
                }
            }

            // each 3x3 pass widens the layer seams by about a sample
            var raw = (float[,])heights.Clone();
            for (int s = 0; s < Softness; s++)
                Smooth(heights, res);

            // interiors revert toward the raw heights so only borders stay soft
            float probe = Mathf.Max(2f, Softness) * (world / (res - 1)) * 1.5f;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float wx = _west + world * x / (res - 1);
                    float wz = _south + world * z / (res - 1);
                    var w = WinnerAt(wx, wz);
                    if (w == null) continue;
                    if (WinnerAt(wx + probe, wz) == w && WinnerAt(wx - probe, wz) == w
                        && WinnerAt(wx, wz + probe) == w && WinnerAt(wx, wz - probe) == w)
                        heights[z, x] = Mathf.Lerp(heights[z, x], raw[z, x], 0.75f);
                }
            }

            // ---- paths: carved into the heights ----
            float[,] pathMask = BuildPaths(heights, biomes, res, world, heightRange);
            _maskRef = pathMask; _resRef = res; _worldRef = world;

            var data = new TerrainData();
            data.heightmapResolution = res;
            data.size = new Vector3(world, heightRange, world);
            data.SetHeights(0, 0, heights);

            var terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(root.transform, false);
            terrainGo.transform.position = new Vector3(_west, baseY, _south);

            // ---- liquid surfaces: the Surface prefab stretched across each box top ----
            foreach (var b in biomes)
            {
                var liquid = b as LiquidBiome;
                if (liquid == null || liquid.Surface == null) continue;
                var plane = Instantiate(liquid.Surface, root.transform);
                plane.name = liquid.name + "Surface";
                Vector2 lc = CenterOf(liquid);
                plane.transform.position = new Vector3(lc.x, liquid.Area.max.y, lc.y);
                // Unity plane = 10m at scale 1; padded just past the border waves
                float pad = Mathf.Min(CoastWobble,
                    Mathf.Min(liquid.Size.x, liquid.Size.z) * 0.25f) * 2f + 1f;
                plane.transform.localScale = new Vector3(
                    (liquid.Size.x + pad) / 10f, 1f, (liquid.Size.z + pad) / 10f);
            }

            // ---- landmarks: centerpiece prefabs at each biome's center ----
            foreach (var b in biomes)
            {
                if (b.Landmark == null) continue;
                Vector2 c = CenterOf(b);
                float y = baseY + SampleH(heights, res, world, c) * heightRange;
                var mark = Instantiate(b.Landmark, new Vector3(c.x, y, c.y),
                    Quaternion.identity, root.transform);
                mark.name = b.name + "Landmark";
            }

            // ---- fog hides the map edge and the wrap seam ----
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;

            // ---- cauldron lottery: up to CauldronLimit pots, one per winning biome ----
            _potWinners.Clear();
            var candidates = new List<Biome>();
            foreach (var b in biomes)
                if (b.Cauldron != null) candidates.Add(b);
            for (int k = 0; k < CauldronLimit && candidates.Count > 0; k++)
            {
                int pick = rng.Next(candidates.Count);
                _potWinners.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }

            // ---- fill: each biome places its Props on its own ground ----
            _spurs.Clear();
            Physics.SyncTransforms(); // the fresh terrain collider must answer rays
            int placed = FillBiomes(biomes, rng, pathMask, res, world, root.transform);

            // ---- door spurs: carve a trail from each entrance to the network ----
            foreach (var (door, target) in _spurs)
                CarveRoute(heights, pathMask, res, world, heightRange,
                    FindRoute(door, target, heights, res, world, heightRange));
            _spurs.Clear();
            data.SetHeights(0, 0, heights);

            // ---- brushes last, over the final ground: floors, cliffs, paths ----
            PaintSplats(data, biomes, res, world, heightRange, pathMask);

            _heightsRef = heights;
            _rangeRef = heightRange;
            _baseRef = baseY;
            _previewKey = 0; // force a fresh preview

            Debug.Log($"[SpellyZombie] SpellyMap grew {biomes.Length} biomes, {placed} props, seed {seed}.");
        }

        /// Highest layer containing the point wins. Each face waves by
        /// zero-mean noise; faces at the same coordinate share the wave, so
        /// snapped neighbours stay seamless.
        Biome WinnerRaw(float wx, float wz)
        {
            // a protected core wins outright; the core is an ellipse, not a square
            for (int i = 0; i < _sorted.Length; i++)
            {
                float pc = _sorted[i].ProtectedCore;
                if (pc <= 0.01f) continue;
                float rx = Mathf.Max(0.01f, (_maxX[i] - _minX[i]) * 0.5f * pc);
                float rz = Mathf.Max(0.01f, (_maxZ[i] - _minZ[i]) * 0.5f * pc);
                float dx = (wx - (_minX[i] + _maxX[i]) * 0.5f) / rx;
                float dz = (wz - (_minZ[i] + _maxZ[i]) * 0.5f) / rz;
                if (dx * dx + dz * dz <= 1f) return _sorted[i];
            }

            float s = Mathf.Max(2f, WobbleScale);
            for (int i = 0; i < _sorted.Length; i++)
            {
                // wobble capped at a quarter of the box
                float wob = Mathf.Min(CoastWobble,
                    Mathf.Min(_maxX[i] - _minX[i], _maxZ[i] - _minZ[i]) * 0.25f);
                if (wx < _minX[i] - wob || wx > _maxX[i] + wob
                    || wz < _minZ[i] - wob || wz > _maxZ[i] + wob) continue;

                float minX = _minX[i] + Wave(_warpX + _minX[i] * 7.31f, wz / s) * wob;
                float maxX = _maxX[i] + Wave(_warpX + _maxX[i] * 7.31f, wz / s) * wob;
                float minZ = _minZ[i] + Wave(_warpZ + _minZ[i] * 7.31f, wx / s) * wob;
                float maxZ = _maxZ[i] + Wave(_warpZ + _maxZ[i] * 7.31f, wx / s) * wob;
                if (wx >= minX && wx <= maxX && wz >= minZ && wz <= maxZ)
                    return _sorted[i];
            }
            return null;
        }

        static float Wave(float a, float t) => (Mathf.PerlinNoise(a, t) - 0.5f) * 2f;

        Biome[] _sorted;
        float _warpX, _warpZ;
        float _west, _south;                     // terrain's south-west corner
        readonly HashSet<Biome> _potWinners = new HashSet<Biome>();
        readonly List<Bounds> _reserved = new List<Bounds>();   // roundabout plazas: no spawns
        readonly List<(Vector2 door, Vector2 target)> _spurs = new List<(Vector2, Vector2)>();
        float[] _minX, _maxX, _minZ, _maxZ;      // effective rects, _sorted order
        Dictionary<Biome, Vector2> _shift;       // this generation's virtual moves

        /// Seeded shuffle: a box moves only within its lower-layer host, host
        /// moves carry it, protected cores and containers constrain it.
        /// Authored transforms are never written - moves live in _shift.
        void PrepareLayout(Biome[] biomes, System.Random rng)
        {
            _shift = new Dictionary<Biome, Vector2>();
            foreach (var b in biomes) _shift[b] = Vector2.zero;

            if (Randomized)
            {
                // deterministic rng order: ascending layer, then name
                var asc = (Biome[])biomes.Clone();
                System.Array.Sort(asc, (a, b) =>
                    a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer)
                                       : string.CompareOrdinal(a.name, b.name));
                var containers = FindObjectsByType<BiomeContainer>(FindObjectsSortMode.None);

                foreach (var b in asc)
                {
                    // the immediate host: highest lower layer whose box holds our center
                    Biome host = null;
                    Vector2 c0 = AuthoredCenter(b);
                    foreach (var h in asc)
                    {
                        if (h == b || h.Layer >= b.Layer) continue;
                        var a = h.Area;
                        if (c0.x < a.min.x || c0.x > a.max.x || c0.y < a.min.z || c0.y > a.max.z) continue;
                        if (host == null || h.Layer > host.Layer) host = h;
                    }
                    if (host != null) _shift[b] = _shift[host]; // carried first
                    if (!b.RandomPosition || host == null) continue;

                    // allowed center range: inside the host's moved box...
                    var hr = host.Area;
                    Vector2 hc = AuthoredCenter(host) + _shift[host];
                    float loX = hc.x - (hr.size.x - b.Size.x) * 0.5f;
                    float hiX = hc.x + (hr.size.x - b.Size.x) * 0.5f;
                    float loZ = hc.y - (hr.size.z - b.Size.z) * 0.5f;
                    float hiZ = hc.y + (hr.size.z - b.Size.z) * 0.5f;
                    // ...and inside the container that lists this box
                    foreach (var box in containers)
                    {
                        if (box.Contained == null || System.Array.IndexOf(box.Contained, b) < 0) continue;
                        var ca = box.Area;
                        loX = Mathf.Max(loX, ca.min.x + b.Size.x * 0.5f);
                        hiX = Mathf.Min(hiX, ca.max.x - b.Size.x * 0.5f);
                        loZ = Mathf.Max(loZ, ca.min.z + b.Size.z * 0.5f);
                        hiZ = Mathf.Min(hiZ, ca.max.z - b.Size.z * 0.5f);
                    }
                    if (hiX < loX || hiZ < loZ) continue; // no room: stay carried

                    for (int t = 0; t < 24; t++)
                    {
                        float cx = Mathf.Round(Mathf.Lerp(loX, hiX, (float)rng.NextDouble()) * 2f) / 2f;
                        float cz = Mathf.Round(Mathf.Lerp(loZ, hiZ, (float)rng.NextDouble()) * 2f) / 2f;
                        if (CoversProtectedCore(b, cx, cz, asc)) continue;
                        _shift[b] = new Vector2(cx, cz) - c0;
                        break;
                    }
                }
            }

            // bake the effective rects in _sorted (highest-layer-first) order
            int n = _sorted.Length;
            _minX = new float[n]; _maxX = new float[n];
            _minZ = new float[n]; _maxZ = new float[n];
            for (int i = 0; i < n; i++)
            {
                var b = _sorted[i];
                var s = _shift[b];
                var a = b.Area;
                _minX[i] = a.min.x + s.x; _maxX[i] = a.max.x + s.x;
                _minZ[i] = a.min.z + s.y; _maxZ[i] = a.max.z + s.y;
            }
        }

        /// Grid fill per biome (FieldSize): one ray per field from box top to
        /// box bottom; a hit must pass slope, path and claim checks or the
        /// field is skipped. Liquids float their fill inside the volume.
        int FillBiomes(Biome[] biomes, System.Random rng, float[,] pathMask,
            int res, float world, Transform root)
        {
            int total = 0;
            var claims = new List<Bounds>(); // world-space claimed boxes
            claims.AddRange(_reserved);      // roundabout plazas stay empty

            foreach (var b in biomes)
            {
                bool hasProps = b.Props != null && b.Props.Length > 0;
                bool hasSources = b.Sources != null && b.Sources.Length > 0;
                if (!hasProps && !hasSources) continue;

                var fill = new GameObject(b.name + "Fill").transform;
                fill.SetParent(root, false);

                var area = b.Area;
                Vector2 c = CenterOf(b);
                int count = 0, cap = 400;
                bool liquid = b is LiquidBiome;
                // riders: disabled Sources authored inside placed props
                var riders = new List<GameObject>();

                // nearest-sample mask read for placement rules
                float MaskW(float mx, float mz)
                {
                    if (pathMask == null) return 0f;
                    int qx = Mathf.Clamp(Mathf.RoundToInt((mx - _west) / world * (res - 1)), 0, res - 1);
                    int qz = Mathf.Clamp(Mathf.RoundToInt((mz - _south) / world * (res - 1)), 0, res - 1);
                    return pathMask[qz, qx];
                }

                // every placement rule in one place
                bool TryPlace(GameObject prefab, float wx, float wz)
                {
                    if (prefab == null) return false;
                    if (Mathf.Abs(wx) > LoopDistance || Mathf.Abs(wz) > LoopDistance) return false;
                    if (WinnerAt(wx, wz) != b) return false;

                    Vector3 at;
                    Vector3 up = Vector3.up;
                    if (liquid)
                    {
                        // floating fill: anywhere in the volume above the bed
                        float y = Mathf.Lerp(area.min.y + 1f, area.max.y - 0.4f,
                            (float)rng.NextDouble());
                        at = new Vector3(wx, y, wz);
                    }
                    else
                    {
                        // ray from box top down, stopping at box bottom
                        var rayFrom = new Vector3(wx, area.max.y + 0.5f, wz);
                        if (!Physics.Raycast(rayFrom, Vector3.down, out var hit,
                                area.size.y + 1f, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore)) return false;
                        if (!(hit.collider is TerrainCollider)) return false; // ground only, never on a prop
                        if (hit.point.y < area.min.y - 0.5f) return false;
                        if (hit.normal.y < 0.7f) return false; // too steep = wall
                        // nothing spawns on the path bed
                        int hx = Mathf.Clamp(Mathf.RoundToInt((wx - _west) / world * (res - 1)), 0, res - 1);
                        int hz = Mathf.Clamp(Mathf.RoundToInt((wz - _south) / world * (res - 1)), 0, res - 1);
                        if (pathMask != null && pathMask[hz, hx] > 0.45f) return false;
                        at = hit.point;
                        up = hit.normal;
                    }

                    // the claim: authored ObjectBox wins, else a modest guess
                    var box = prefab.GetComponent<ObjectBox>();
                    Vector3 claimSize = box != null ? box.Size : Vector3.one * (b.FieldSize * 0.8f);
                    Vector3 claimCenter = at + (box != null ? box.Center : Vector3.up * claimSize.y * 0.5f);
                    var claim = new Bounds(claimCenter, claimSize);
                    for (int i = 0; i < claims.Count; i++)
                        if (claims[i].Intersects(claim)) return false;

                    // the whole claim area is tested against the path mask
                    if (!liquid && pathMask != null)
                    {
                        for (float mx = claim.min.x; mx <= claim.max.x + 0.01f; mx += 1.5f)
                            for (float mz = claim.min.z; mz <= claim.max.z + 0.01f; mz += 1.5f)
                                if (MaskW(Mathf.Min(mx, claim.max.x),
                                          Mathf.Min(mz, claim.max.z)) > 0.45f) return false;
                    }

                    // entrances face the nearest path, else the biome center.
                    // Author doors on the prefab's +Z.
                    float yaw;
                    Vector2 spurTarget = default;
                    bool hasSpur = false;
                    if (!liquid && prefab.GetComponentInChildren<PathPoint>(true) != null)
                    {
                        Vector2 flat = new Vector2(at.x, at.z);
                        if (NearestPath(flat, 27f, out spurTarget))
                        {
                            Vector2 d = (spurTarget - flat).normalized;
                            yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                            hasSpur = true;
                        }
                        else
                        {
                            Vector2 d = CenterOf(b) - flat;
                            yaw = d.sqrMagnitude > 0.01f
                                ? Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg
                                : (float)rng.NextDouble() * 360f;
                        }
                    }
                    else yaw = (float)rng.NextDouble() * 360f;

                    var go = Instantiate(prefab, at,
                        Quaternion.AngleAxis(yaw, Vector3.up), fill);
                    // EVERYTHING PLACED IS AN ELEMENT. A prop with no Element
                    // is outside the world entirely - it cannot be burnt,
                    // broken or read, and nothing tells you. Prefabs should
                    // carry their own (Spelly Zombie/Elements/Set Up Prefabs);
                    // this is the net for the ones that do not.
                    EnsureElements(go);
                    // a placed prefab carrying interior fields fills them on
                    // the MAP's rng - same seed, same clutter, every machine
                    foreach (var f in go.GetComponentsInChildren<InteriorField>(true))
                        f.Fill(rng);
                    // gentle tilt onto slopes
                    if (!liquid && up.y < 0.995f)
                        go.transform.rotation = Quaternion.FromToRotation(Vector3.up,
                            Vector3.Slerp(Vector3.up, up, 0.35f)) * go.transform.rotation;

                    if (go.GetComponentInChildren<Collider>() == null)
                        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                            if (mf.sharedMesh != null)
                                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

                    foreach (var a in go.GetComponentsInChildren<Analyzable>(true))
                        if (!a.gameObject.activeSelf) riders.Add(a.gameObject);

                    // the placed door's position feeds the spur pass
                    if (hasSpur)
                    {
                        var doorNow = go.GetComponentInChildren<PathPoint>(true);
                        if (doorNow != null)
                            _spurs.Add((new Vector2(doorNow.transform.position.x,
                                doorNow.transform.position.z), spurTarget));
                    }

                    claims.Add(claim);
                    count++;
                    total++;
                    return true;
                }

                // the Props grid fill
                if (hasProps)
                {
                    int nx = Mathf.Max(1, Mathf.FloorToInt(b.Size.x / b.FieldSize));
                    int nz = Mathf.Max(1, Mathf.FloorToInt(b.Size.z / b.FieldSize));
                    for (int iz = 0; iz < nz && count < cap; iz++)
                    {
                        for (int ix = 0; ix < nx && count < cap; ix++)
                        {
                            float wx = c.x - b.Size.x * 0.5f + (ix + 0.5f) * b.FieldSize
                                + ((float)rng.NextDouble() - 0.5f) * b.FieldSize * 0.6f;
                            float wz = c.y - b.Size.z * 0.5f + (iz + 0.5f) * b.FieldSize
                                + ((float)rng.NextDouble() - 0.5f) * b.FieldSize * 0.6f;
                            if (rng.NextDouble() < 0.3) continue; // breathing room
                            TryPlace(b.Props[rng.Next(b.Props.Length)], wx, wz);
                        }
                    }
                }

                // sources: riders get revealed, standalones get spawned, at
                // random until MinSources..MaxSources exist
                if (hasSources || riders.Count > 0)
                {
                    int want = rng.Next(Mathf.Max(0, b.MinSources),
                        Mathf.Max(b.MinSources, b.MaxSources) + 1);
                    int placedSrc = 0;
                    for (int t = 0; t < 160 && placedSrc < want; t++)
                    {
                        bool useRider = riders.Count > 0
                            && (!hasSources || rng.NextDouble() < 0.5);
                        if (useRider)
                        {
                            int i = rng.Next(riders.Count);
                            riders[i].SetActive(true);
                            riders.RemoveAt(i);
                            placedSrc++;
                        }
                        else if (hasSources)
                        {
                            float wx = c.x + ((float)rng.NextDouble() - 0.5f) * b.Size.x;
                            float wz = c.y + ((float)rng.NextDouble() - 0.5f) * b.Size.z;
                            if (TryPlace(b.Sources[rng.Next(b.Sources.Length)], wx, wz))
                                placedSrc++;
                        }
                        else break; // both pools dry
                    }
                    if (placedSrc < b.MinSources)
                        Debug.LogWarning($"[SpellyZombie] '{b.name}' placed only {placedSrc}/{b.MinSources} sources - not enough valid ground or riders.", b);
                }

                // lottery winner: the pot stands at a random valid spot
                if (_potWinners.Contains(b))
                {
                    bool stood = false;
                    for (int t = 0; t < 80 && !stood; t++)
                    {
                        float wx = c.x + ((float)rng.NextDouble() - 0.5f) * b.Size.x;
                        float wz = c.y + ((float)rng.NextDouble() - 0.5f) * b.Size.z;
                        stood = TryPlace(b.Cauldron, wx, wz);
                    }
                    if (!stood)
                        Debug.LogWarning($"[SpellyZombie] '{b.name}' won a cauldron but found no ground for it.", b);
                }
            }
            return total;
        }

        float[,] _maskRef, _heightsRef;
        int _resRef;
        float _worldRef, _rangeRef, _baseRef;
        bool _logRoutes;
        readonly List<Vector3[]> _routeLog = new List<Vector3[]>();
        List<Vector3[]> _previewLines;
        int _previewKey;
        float _keyCheckAt;

        /// A cheap fingerprint of everything that shapes the network - when
        /// it changes, the preview (and auto-rebuild) know to refresh.
        public int LayoutKey()
        {
            unchecked
            {
                int h = 17;
                foreach (var b in FindObjectsByType<Biome>(FindObjectsSortMode.None))
                {
                    h = h * 31 + b.transform.position.GetHashCode();
                    h = h * 31 + b.Size.GetHashCode();
                    h = h * 31 + b.Layer;
                    h = h * 31 + (b.CanPath ? 1 : 0);
                    h = h * 31 + Mathf.RoundToInt(b.PathCurve * 100f);
                }
                foreach (var nd in FindObjectsByType<PathNode>(FindObjectsSortMode.None))
                {
                    h = h * 31 + nd.transform.position.GetHashCode();
                    h = h * 31 + Mathf.RoundToInt(nd.Radius * 10f);
                    h = h * 31 + (nd.JoinWeb ? 1 : 0);
                    if (nd.LinksTo != null)
                        foreach (var l in nd.LinksTo)
                            if (l != null) h = h * 31 + l.GetInstanceID();
                }
                return h;
            }
        }

        /// Re-route the network over the LAST generated ground (no terrain
        /// touch, no spawns) purely to harvest the polylines for drawing.
        void RebuildPreview()
        {
            _previewLines = null;
            if (_heightsRef == null || _sorted == null) return;
            _routeLog.Clear();
            _logRoutes = true;
            var scratch = (float[,])_heightsRef.Clone();
            BuildPaths(scratch, _sorted, _resRef, _worldRef, _rangeRef);
            _logRoutes = false;
            _previewLines = new List<Vector3[]>(_routeLog);
            _routeLog.Clear();
        }

        /// Path preview lines, refreshed when any box or node changes.
        void OnDrawGizmos()
        {
            if (!AlwaysPreviewPaths || _heightsRef == null) return;
            if (Time.realtimeSinceStartup > _keyCheckAt)
            {
                _keyCheckAt = Time.realtimeSinceStartup + 0.4f;
                int key = LayoutKey();
                if (key != _previewKey) { _previewKey = key; RebuildPreview(); }
            }
            if (_previewLines == null) return;
            Gizmos.color = new Color(0.8f, 0.4f, 1f, 0.95f);
            foreach (var line in _previewLines)
                for (int i = 1; i < line.Length; i++)
                    Gizmos.DrawLine(line[i - 1], line[i]);
        }

        /// The nearest point of the path network within reach: a widening
        /// ring scan over the bed mask. Entrances face this; spurs grow to it.
        bool NearestPath(Vector2 at, float maxR, out Vector2 hit)
        {
            hit = default;
            if (_maskRef == null) return false;
            for (float r = 3f; r <= maxR; r += 3f)
            {
                for (int k = 0; k < 16; k++)
                {
                    float ang = k / 16f * Mathf.PI * 2f;
                    Vector2 p = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    int qx = Mathf.Clamp(Mathf.RoundToInt((p.x - _west) / _worldRef * (_resRef - 1)), 0, _resRef - 1);
                    int qz = Mathf.Clamp(Mathf.RoundToInt((p.y - _south) / _worldRef * (_resRef - 1)), 0, _resRef - 1);
                    if (_maskRef[qz, qx] > 0.6f) { hit = p; return true; }
                }
            }
            return false;
        }

        /// When selected, draws the shuffled layout: colored boxes with names.
        void OnDrawGizmosSelected()
        {
            if (_sorted == null || _minX == null || _minX.Length != _sorted.Length) return;
            for (int i = 0; i < _sorted.Length; i++)
            {
                var b = _sorted[i];
                if (b == null) continue;
                var a = b.Area;
                Vector3 c = new Vector3((_minX[i] + _maxX[i]) * 0.5f, a.center.y,
                                        (_minZ[i] + _maxZ[i]) * 0.5f);
                Vector3 s = new Vector3(_maxX[i] - _minX[i], a.size.y, _maxZ[i] - _minZ[i]);
                Gizmos.color = b.GizmoTint;
                Gizmos.DrawWireCube(c, s);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(c + Vector3.up * (s.y * 0.5f + 1.2f), b.name);
#endif
            }
        }

        static Vector2 AuthoredCenter(Biome b) =>
            new Vector2(b.transform.position.x, b.transform.position.z);

        Vector2 CenterOf(Biome b) => AuthoredCenter(b) + _shift[b];

        /// Would this box, centered there, sit on someone's untouchable core?
        bool CoversProtectedCore(Biome b, float cx, float cz, Biome[] all)
        {
            float bMinX = cx - b.Size.x * 0.5f, bMaxX = cx + b.Size.x * 0.5f;
            float bMinZ = cz - b.Size.z * 0.5f, bMaxZ = cz + b.Size.z * 0.5f;
            foreach (var o in all)
            {
                if (o == b || o.Layer >= b.Layer || o.ProtectedCore <= 0.01f) continue;
                Vector2 oc = AuthoredCenter(o) + _shift[o];
                float hx = o.Size.x * o.ProtectedCore * 0.5f;
                float hz = o.Size.z * o.ProtectedCore * 0.5f;
                if (bMinX < oc.x + hx && bMaxX > oc.x - hx
                    && bMinZ < oc.y + hz && bMaxZ > oc.y - hz) return true;
            }
            return false;
        }

        Biome WinnerAt(float wx, float wz) => WinnerRaw(wx, wz);

        static SpellyMap _live;
        void OnEnable() { _live = this; }
        void OnDisable() { if (_live == this) _live = null; }

        /// Which biome owns a world position at runtime, or null where there
        /// is no generated map (the lobby). Callers treat null as "natural".
        public static Biome BiomeAt(Vector3 world)
        {
            if (_live == null || _live._sorted == null || _live._minX == null
                || _live._minX.Length != _live._sorted.Length) return null;
            return _live.WinnerRaw(world.x, world.z);
        }

        static void Smooth(float[,] h, int res)
        {
            for (int z = 1; z < res - 1; z++)
                for (int x = 1; x < res - 1; x++)
                    h[z, x] = (h[z, x] * 4f
                        + h[z - 1, x] + h[z + 1, x] + h[z, x - 1] + h[z, x + 1]
                        + (h[z - 1, x - 1] + h[z - 1, x + 1] + h[z + 1, x - 1] + h[z + 1, x + 1]) * 0.5f)
                        / 10f;
        }

        // ------------------------------------------------------------ paths --
        /// Connects every CanPath biome into one network, then carves each
        /// route so the grade never exceeds PathSlope. Returns the ribbon
        /// mask used for painting and spawn reservation.
        float[,] BuildPaths(float[,] heights, Biome[] sorted, int res, float world, float heightRange)
        {
            var mask = new float[res, res];

            var pts = new List<Vector2>();
            var own = new List<Biome>();
            var ring = new List<float>();     // >0 = this node is a roundabout
            var joinWeb = new List<bool>();
            _reserved.Clear();

            // authored PathNodes first: a biome containing any gets no auto
            // nodes; nodes ride their biome's shuffle
            var authored = FindObjectsByType<PathNode>(FindObjectsSortMode.None);
            var authoredIdx = new Dictionary<PathNode, int>();
            var authoredBiomes = new HashSet<Biome>();
            foreach (var mk in authored)
            {
                Vector2 p = new Vector2(mk.transform.position.x, mk.transform.position.z);
                // a node parented under a Biome belongs to it; unparented
                // nodes are world-fixed junctions
                Biome host = mk.GetComponentInParent<Biome>();
                if (host != null && _shift.ContainsKey(host))
                {
                    p += _shift[host];
                    authoredBiomes.Add(host);
                }
                authoredIdx[mk] = pts.Count;
                pts.Add(p);
                own.Add(host);
                ring.Add(Mathf.Max(0f, mk.Radius));
                joinWeb.Add(mk.JoinWeb);
            }

            // auto waypoints only in biomes with no authored nodes
            foreach (var b in sorted)
            {
                if (!b.CanPath || authoredBiomes.Contains(b)) continue;
                var owned = new List<Vector2>();
                for (int z = 0; z < res; z += 6)
                {
                    for (int x = 0; x < res; x += 6)
                    {
                        float wx = _west + world * x / (res - 1);
                        float wz = _south + world * z / (res - 1);
                        if (WinnerAt(wx, wz) == b) owned.Add(new Vector2(wx, wz));
                    }
                }
                if (owned.Count == 0)
                {
                    Debug.LogWarning($"[SpellyZombie] path skipped: '{b.name}' owns no surface (fully cut away).", b);
                    continue;
                }

                int want = 1 + Mathf.Min(3, Mathf.FloorToInt(b.Size.x * b.Size.z / 1600f));
                Vector2 c = CenterOf(b);
                Vector2 first = owned[0];
                float bd = float.MaxValue;
                foreach (var p in owned)
                {
                    float d = (p - c).sqrMagnitude;
                    if (d < bd) { bd = d; first = p; }
                }
                var mine = new List<Vector2> { first };
                while (mine.Count < want)
                {
                    Vector2 far = first;
                    float fd = -1f;
                    foreach (var p in owned)
                    {
                        float d = float.MaxValue;
                        foreach (var q in mine) d = Mathf.Min(d, (p - q).sqrMagnitude);
                        if (d > fd) { fd = d; far = p; }
                    }
                    if (fd < 100f) break; // 10m spacing floor: too small for more
                    mine.Add(far);
                }
                for (int i = 0; i < mine.Count; i++)
                {
                    pts.Add(mine[i]);
                    own.Add(b);
                    ring.Add(0f); // roundabouts are hand-placed only (PathNode.Radius)
                    joinWeb.Add(true);
                }
            }
            if (pts.Count < 2) return mask;

            // authored links first, exactly as linked
            var edgesEarly = new List<(int a, int b)>();
            foreach (var mk in authored)
            {
                if (mk.LinksTo == null) continue;
                foreach (var other in mk.LinksTo)
                    if (other != null && authoredIdx.ContainsKey(other))
                        edgesEarly.Add((authoredIdx[mk], authoredIdx[other]));
            }

            int n = pts.Count;
            var edges = new List<(int a, int b)>();
            var seen = new HashSet<int>();
            void AddEdge(int a, int b)
            {
                if (a == b) return;
                int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                if (seen.Add(lo * 4096 + hi)) edges.Add((lo, hi));
            }

            foreach (var (a, b) in edgesEarly) AddEdge(a, b);

            // spanning tree keeps every waypoint reachable
            var inTree = new bool[n];
            inTree[0] = true;
            for (int added = 1; added < n; added++)
            {
                int bi = -1, bj = -1;
                float bd = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (!inTree[i]) continue;
                    for (int j = 0; j < n; j++)
                    {
                        if (inTree[j]) continue;
                        float d = (pts[i] - pts[j]).sqrMagnitude;
                        if (d < bd) { bd = d; bi = i; bj = j; }
                    }
                }
                if (bj < 0) break;
                inTree[bj] = true;
                AddEdge(bi, bj);
            }

            // each willing waypoint also joins its nearest neighbour;
            // JoinWeb=false nodes get only authored links and the tree bridge
            for (int i = 0; i < n; i++)
            {
                if (!joinWeb[i]) continue;
                int bj = -1;
                float bd = float.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (j == i || !joinWeb[j]) continue;
                    float d = (pts[i] - pts[j]).sqrMagnitude;
                    if (d < bd) { bd = d; bj = j; }
                }
                if (bj >= 0 && bd < 55f * 55f) AddEdge(i, bj);
            }

            foreach (var (a, b) in edges)
            {
                // spokes stop at a roundabout's RING, not its center
                Vector2 pa = pts[a] + (pts[b] - pts[a]).normalized * ring[a];
                Vector2 pb = pts[b] + (pts[a] - pts[b]).normalized * ring[b];
                Vector2? via = own[a] != own[b]
                    ? RouteVia(own[a], own[b], pa, pb) : null;
                var route = new List<Vector2>();
                if (via.HasValue)
                {
                    route.AddRange(FindRoute(pa, via.Value, heights, res, world, heightRange));
                    route.AddRange(FindRoute(via.Value, pb, heights, res, world, heightRange));
                }
                else route = FindRoute(pa, pb, heights, res, world, heightRange);
                CarveRoute(heights, mask, res, world, heightRange, route);
            }

            // ROUNDABOUTS: the ring path itself + the keep-clear plaza inside
            for (int i = 0; i < pts.Count; i++)
            {
                if (ring[i] <= 0f) continue;
                var circle = new List<Vector2>();
                for (int k = 0; k <= 20; k++)
                {
                    float ang = k / 20f * Mathf.PI * 2f;
                    circle.Add(pts[i] + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ring[i]);
                }
                CarveRoute(heights, mask, res, world, heightRange, circle);
                float side = Mathf.Max(1f, (ring[i] - 1.2f) * 2f);
                _reserved.Add(new Bounds(new Vector3(pts[i].x, 0f, pts[i].y),
                    new Vector3(side, 400f, side)));
            }
            return mask;
        }

        /// A* over a coarse lattice where steepness is expensive, so routes
        /// fold into switchbacks instead of forcing deep carves.
        List<Vector2> FindRoute(Vector2 from, Vector2 to, float[,] heights, int res, float world, float heightRange)
        {
            const int stride = 4;
            int n = (res - 1) / stride + 1;
            float step = world / (res - 1) * stride;
            float walkTan = Mathf.Tan(PathSlope * Mathf.Deg2Rad);

            int NodeOf(Vector2 p)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt((p.x - _west) / step), 0, n - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt((p.y - _south) / step), 0, n - 1);
                return z * n + x;
            }
            Vector2 PosOf(int id) => new Vector2(
                _west + (id % n) * step, _south + (id / n) * step);
            float HeightOf(int id) =>
                heights[Mathf.Min(res - 1, (id / n) * stride), Mathf.Min(res - 1, (id % n) * stride)]
                * heightRange;

            int start = NodeOf(from), goal = NodeOf(to);
            var g = new float[n * n];
            var parent = new int[n * n];
            var closed = new bool[n * n];
            for (int i = 0; i < g.Length; i++) { g[i] = float.MaxValue; parent[i] = -1; }

            var open = new List<int>();       // tiny frontier: scan-min is fine at this size
            var f = new float[n * n];
            g[start] = 0f;
            f[start] = (PosOf(goal) - PosOf(start)).magnitude;
            open.Add(start);

            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

            while (open.Count > 0)
            {
                int bestI = 0;
                for (int i = 1; i < open.Count; i++)
                    if (f[open[i]] < f[open[bestI]]) bestI = i;
                int cur = open[bestI];
                open.RemoveAt(bestI);
                if (cur == goal) break;
                if (closed[cur]) continue;
                closed[cur] = true;

                int cx = cur % n, cz = cur / n;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + dx[d], nz = cz + dz[d];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int nb = nz * n + nx;
                    if (closed[nb]) continue;

                    float dist = step * (d < 4 ? 1f : 1.41421f);
                    float grade = Mathf.Abs(HeightOf(nb) - HeightOf(cur)) / dist;
                    // quadratic steepness cost, water worse; finite so a route always exists
                    float cost = dist * (1f + Mathf.Pow(grade / Mathf.Max(0.05f, walkTan), 2f) * 6f);
                    if (WinnerAt(PosOf(nb).x, PosOf(nb).y) is LiquidBiome) cost *= 40f;

                    if (g[cur] + cost < g[nb])
                    {
                        g[nb] = g[cur] + cost;
                        f[nb] = g[nb] + (PosOf(goal) - PosOf(nb)).magnitude;
                        parent[nb] = cur;
                        open.Add(nb);
                    }
                }
            }

            var pts = new List<Vector2>();
            int walk = goal;
            while (walk >= 0 && walk != start) { pts.Add(PosOf(walk)); walk = parent[walk]; }
            pts.Add(from);
            pts.Reverse();
            if (pts.Count < 2) { pts.Clear(); pts.Add(from); pts.Add(to); }
            pts[pts.Count - 1] = to;
            return pts;
        }

        bool Touch(Biome a, Biome b)
        {
            Vector2 ca = CenterOf(a), cb = CenterOf(b);
            return Mathf.Abs(ca.x - cb.x) <= (a.Size.x + b.Size.x) * 0.5f + 1f
                && Mathf.Abs(ca.y - cb.y) <= (a.Size.z + b.Size.z) * 0.5f + 1f;
        }

        /// The border crossing: an authored Entry when one exists (nearest to
        /// the straight line), else straight through. Entries ride their
        /// biome's shuffle.
        Vector2? RouteVia(Biome a, Biome b, Vector2 pa, Vector2 pb)
        {
            Vector2? via = null;
            float best = float.MaxValue;
            foreach (var biome in new[] { a, b })
            {
                if (biome == null || biome.Entries == null) continue;
                Vector2 s = _shift[biome];
                foreach (var e in biome.Entries)
                {
                    if (e == null) continue;
                    Vector2 p = new Vector2(e.position.x + s.x, e.position.z + s.y);
                    float d = DistToSegment(p, pa, pb);
                    if (d < best) { best = d; via = p; }
                }
            }
            return via;
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        void CarveRoute(float[,] heights, float[,] mask, int res, float world, float heightRange,
            List<Vector2> route)
        {
            float step = world / (res - 1);

            // PathCurve bends the route sideways with noise before carving
            for (int i = 1; i < route.Count - 1; i++)
            {
                var w = WinnerAt(route[i].x, route[i].y);
                float amp = (w != null ? w.PathCurve : 0.3f) * 3.5f;
                if (amp <= 0.01f) continue;
                Vector2 dir = (route[i + 1] - route[i - 1]).normalized;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                float nz = Mathf.PerlinNoise(_warpX + i * 0.37f, _warpZ + route[i].x * 0.05f) - 0.5f;
                route[i] += perp * nz * 2f * amp;
            }

            // resample the trail at heightmap density
            var pts = new List<Vector2>();
            for (int i = 0; i < route.Count - 1; i++)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((route[i + 1] - route[i]).magnitude / step));
                for (int k = 0; k < n; k++) pts.Add(Vector2.Lerp(route[i], route[i + 1], k / (float)n));
            }
            pts.Add(route[route.Count - 1]);
            if (pts.Count < 2) return;

            // relax the line to the PathSlope grade
            var lineH = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
                lineH[i] = SampleH(heights, res, world, pts[i]);
            var terrH = (float[])lineH.Clone();
            float maxDh = Mathf.Tan(PathSlope * Mathf.Deg2Rad) * step / heightRange;
            for (int pass = 0; pass < 24; pass++)
            {
                for (int i = 1; i < pts.Count; i++)
                    lineH[i] = Mathf.Clamp(lineH[i], lineH[i - 1] - maxDh, lineH[i - 1] + maxDh);
                for (int i = pts.Count - 2; i >= 0; i--)
                    lineH[i] = Mathf.Clamp(lineH[i], lineH[i + 1] - maxDh, lineH[i + 1] + maxDh);
            }

            // preview harvesting: the relaxed line IS the drawable route
            if (_logRoutes)
            {
                var line = new Vector3[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                    line[i] = new Vector3(pts[i].x,
                        _baseRef + lineH[i] * heightRange + 0.35f, pts[i].y);
                _routeLog.Add(line);
                return; // preview never stamps
            }

            // stamp: flat bed; shoulder width grows with cut/fill depth
            float half = PathWidth * 0.5f;
            for (int i = 0; i < pts.Count; i++)
            {
                float depth = Mathf.Abs(lineH[i] - terrH[i]) * heightRange;
                float shoulder = half + 1.6f + depth * 1.4f;
                int reach = Mathf.CeilToInt(shoulder / step) + 1;
                int cx = Mathf.RoundToInt((pts[i].x - _west) / step);
                int cz = Mathf.RoundToInt((pts[i].y - _south) / step);
                for (int dz = -reach; dz <= reach; dz++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= res || z >= res) continue;
                        float wx = _west + x * step;
                        float wz = _south + z * step;
                        float d = (new Vector2(wx, wz) - pts[i]).magnitude;
                        if (d > shoulder) continue;
                        float w = d <= half ? 1f
                            : Mathf.SmoothStep(1f, 0f, (d - half) / (shoulder - half));
                        heights[z, x] = Mathf.Lerp(heights[z, x], lineH[i], w);
                        if (w > mask[z, x]) mask[z, x] = w; // full soft ribbon
                    }
                }
            }
        }

        float SampleH(float[,] heights, int res, float world, Vector2 p)
        {
            float step = world / (res - 1);
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - _west) / step), 0, res - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.y - _south) / step), 0, res - 1);
            return heights[z, x];
        }

        void PaintSplats(TerrainData data, Biome[] sorted, int res, float world, float heightRange,
            float[,] pathMask)
        {
            // collect the brushes actually assigned; index 0 stays a fallback
            var layers = new List<TerrainLayer>();
            int IndexOf(TerrainLayer l)
            {
                if (l == null) return -1;
                int i = layers.IndexOf(l);
                if (i < 0) { layers.Add(l); i = layers.Count - 1; }
                return i;
            }
            foreach (var b in sorted) { IndexOf(b.FloorLayer); IndexOf(b.PathLayer); }
            int cliff = IndexOf(CliffLayer);
            if (layers.Count == 0) return; // no brushes yet: bare terrain is fine for shape checks

            data.terrainLayers = layers.ToArray();
            int ares = Mathf.Min(res * 2 - 1, 512);
            data.alphamapResolution = ares;
            float[,,] maps = new float[ares, ares, layers.Count];
            float slopeTan = Mathf.Tan(CliffSlope * Mathf.Deg2Rad);
            float texel = world / (ares - 1);

            // bilinear mask read so the ribbon paints smoothly
            float MaskAt(float fx, float fz)
            {
                if (pathMask == null) return 0f;
                float gx = fx * (res - 1), gz = fz * (res - 1);
                int x0 = Mathf.Clamp((int)gx, 0, res - 2);
                int z0 = Mathf.Clamp((int)gz, 0, res - 2);
                float tx = gx - x0, tz = gz - z0;
                return Mathf.Lerp(
                    Mathf.Lerp(pathMask[z0, x0], pathMask[z0, x0 + 1], tx),
                    Mathf.Lerp(pathMask[z0 + 1, x0], pathMask[z0 + 1, x0 + 1], tx), tz);
            }

            // one tap: floor + slope-faded cliff + feathered path
            void Tap(float wx, float wz, int z, int x, float share)
            {
                float fx = Mathf.Clamp01((wx - _west) / world);
                float fz = Mathf.Clamp01((wz - _south) / world);
                int idx = 0;
                var win = WinnerAt(wx, wz);
                if (win != null && win.FloorLayer != null) idx = layers.IndexOf(win.FloorLayer);
                idx = Mathf.Clamp(idx, 0, layers.Count - 1);

                float pathW = 0f;
                int pathIdx = -1;
                if (win != null && win.PathLayer != null)
                {
                    pathIdx = layers.IndexOf(win.PathLayer);
                    pathW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.8f, MaskAt(fx, fz)));
                }

                float cliffW = 0f;
                if (cliff >= 0)
                {
                    float hC = data.GetInterpolatedHeight(fx, fz);
                    float hX = data.GetInterpolatedHeight(Mathf.Min(1f, fx + 1f / ares), fz);
                    float hZ = data.GetInterpolatedHeight(fx, Mathf.Min(1f, fz + 1f / ares));
                    float grade = Mathf.Max(Mathf.Abs(hX - hC), Mathf.Abs(hZ - hC)) / texel;
                    cliffW = Mathf.InverseLerp(slopeTan * 0.7f, slopeTan * 1.35f, grade);
                }

                float ground = share * (1f - pathW);
                maps[z, x, idx] += ground * (1f - cliffW);
                if (cliff >= 0) maps[z, x, cliff] += ground * cliffW;
                if (pathIdx >= 0) maps[z, x, pathIdx] += share * pathW;
            }

            // five taps in a circle per texel so borders blend, never stamp squares
            float r = texel * 1.1f;
            for (int z = 0; z < ares; z++)
            {
                for (int x = 0; x < ares; x++)
                {
                    float wx = _west + texel * x;
                    float wz = _south + texel * z;
                    Tap(wx, wz, z, x, 0.2f);
                    Tap(wx + r, wz + r, z, x, 0.2f);
                    Tap(wx - r, wz + r, z, x, 0.2f);
                    Tap(wx + r, wz - r, z, x, 0.2f);
                    Tap(wx - r, wz - r, z, x, 0.2f);
                }
            }
            data.SetAlphamaps(0, 0, maps);
        }
    
        /// ONE ELEMENT PER OBJECT, AT ITS ROOT - matching the editor pass.
        /// `go` IS the prefab root here, so children are parts of this one
        /// thing and never get their own. A prop with no solid collider is not
        /// in the world at all and is left alone.
        static void EnsureElements(GameObject go)
        {
            if (go == null) return;
            // authored anywhere in the object - root OR a child someone chose
            // to put one on - means it is already spoken for
            if (go.GetComponentInParent<Element>() != null) return;
            if (go.GetComponentInChildren<Element>(true) != null) return;
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null && !cols[i].isTrigger)
                {
                    go.AddComponent<Element>();
                    return;
                }
        }
}
}
