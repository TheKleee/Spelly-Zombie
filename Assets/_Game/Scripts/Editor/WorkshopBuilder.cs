using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// THE WIZARD WORKSHOP — map 1's heart, built so Marko can SEE it before
    /// sculpting the terrain it sits in. One 24×12m two-row building of seven
    /// rooms behind an open MAIN HALL, courtyard with the Grand Cauldron in
    /// front, observatory tower over the library. Every room is sealed by a
    /// WARD (SpellLock — draw the demanded runes at the door to open) and
    /// holds its part of the economy: pages, materials, perk pots, weapons.
    /// Room floors carry their own MATERIAL tags — unlocking the forge
    /// literally adds metal to what State runes can conjure.
    ///
    /// Division of labor: this is a one-shot scaffold HE runs — geometry,
    /// wards, markers, canvases under one deletable SZ_Workshop root. All
    /// dressing/beauty/terrain is his. Ink rules honored: one ground slab =
    /// seamless courtyard drawing, one solid canvas box per room floor,
    /// facade canvases outside (interior wall canvases deferred — strokes
    /// split at interior module seams for now, noted in the log).
    ///
    /// Layout (meters; cauldron at origin, building to the north):
    ///     z=20 ┌───────┬────────┬───────┬───────┐
    ///          │Pantry │Library │Cellar │ Study │   north row (6m deep)
    ///          │       │ +tower │       │       │
    ///     z=14 ├───────┴──┬─────┴─┬─────┴───────┤
    ///          │  Forge   │ HALL  │ Greenhouse  │   south row (6m deep)
    ///     z=8  └──────────┴─▯▯▯▯─┴─────────────┘   ▯▯ = open double arch
    ///        x=-12      -4       4             12
    ///                 COURTYARD · cauldron (0,0,0) · fences with gaps
    public static class WorkshopBuilder
    {
        // one-knob fixes, builder tradition — flip if a kit piece faces wrong
        const float StairYaw = 0f;      // library stairs should CLIMB north

        const float W = 2f;             // authored wall module width
        static float H => VillageBuilder.WallH;    // measured module height (3.12)
        static float Yaw => VillageBuilder.WallYawAuto; // kit wall-axis correction

        enum Cell { Plain, Grid, Window, Door, Arch }

        static Transform _root;
        static int _windowCount;

        [MenuItem("Spelly Zombie/Build WIZARD WORKSHOP (empty scene only)")]
        public static void Build()
        {
            foreach (var guardName in new[]
                { "SZ_Workshop", "SZ_Village", "SZ_Menu", "SZ_GameMap", "SZ_Test", "SZ_Ship", "SZ_Player", "SZ_DrawingWorld" })
                if (GameObject.Find(guardName) != null)
                {
                    Debug.LogError($"[SpellyZombie] REFUSING to build: this scene contains '{guardName}'. " +
                                   "The workshop wants a fresh scene (File → New Scene) — terrain-only is fine too.");
                    return;
                }

            EnvironmentTools.WireFxLibrary();
            _root = new GameObject("SZ_Workshop").transform;
            _windowCount = 0;
            VillageBuilder.BeginPlacement(_root);

            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                cam.gameObject.SetActive(false);
            if (Object.FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("SZ_Sun");
                sun.transform.SetParent(_root, false);
                var light = sun.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            Ground();
            Walls();
            Floors();
            Roofs();
            ObservatoryStairs();
            Courtyard();
            RoomContents();
            FacadeCanvases();

            var drawWorld = new GameObject("SZ_DrawingWorld");
            drawWorld.transform.SetParent(_root, false); // deleting SZ_Workshop takes it too
            drawWorld.AddComponent<DrawingWorld>();
            TestSandboxBuilder.BuildBeanPlayer(_root);
            var player = GameObject.Find("SZ_Player");
            if (player != null) player.transform.position = new Vector3(0f, 1.15f, -4f);
            Selection.activeGameObject = player;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[SpellyZombie] WIZARD WORKSHOP ready — 7 warded rooms + hall + tower, {_windowCount} windows " +
                      "(each with a DORMANT entry marker for the siege code), 5 live courtyard/rear entries, " +
                      "Grand Cauldron + 3 room perk pots, both weapons racked. Draw the hovering runes AT a door " +
                      "to open it (lobby-free scenes have the full grimoire). Knobs: WorkshopBuilder.StairYaw. " +
                      "Deferred on purpose: interior wall canvases (strokes split at interior seams).");
        }

        // ------------------------------------------------------------ ground --
        /// One slab = one collider = seamless courtyard ink (the plaza rule).
        /// Marko's terrain later replaces the LOOK; a slab this size keeps the
        /// heart drawable and walkable standalone today.
        static void Ground()
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "GroundSlab";
            slab.transform.SetParent(_root, false);
            slab.transform.position = new Vector3(0f, -0.15f, 3f);
            slab.transform.localScale = new Vector3(34f, 0.3f, 38f); // top face at y=0, z −16..22
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(lit) { name = "SZ_WorkshopGround", color = new Color(0.38f, 0.34f, 0.26f) };
            mat.SetFloat("_Smoothness", 0.05f);
            slab.GetComponent<Renderer>().sharedMaterial = mat;
            slab.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Earth;
        }

        // ------------------------------------------------------------- walls --
        static void Walls()
        {
            // ---- south face (exterior toward the courtyard, yaw 180) ----
            Run(new Vector3(-11f, 0f, 8f), Vector3.right, 180f, "Forge",
                Cell.Plain, Cell.Window, Cell.Window, Cell.Grid);
            Run(new Vector3(-3f, 0f, 8f), Vector3.right, 180f, "Hall",
                Cell.Window, Cell.Arch, Cell.Arch, Cell.Window);
            Run(new Vector3(5f, 0f, 8f), Vector3.right, 180f, "Greenhouse",
                Cell.Grid, Cell.Window, Cell.Window, Cell.Plain);

            // ---- north face (exterior, yaw 0) ----
            Run(new Vector3(-11f, 0f, 20f), Vector3.right, 0f, "Pantry",
                Cell.Plain, Cell.Window, Cell.Grid);
            Run(new Vector3(-5f, 0f, 20f), Vector3.right, 0f, "Library",
                Cell.Grid, Cell.Window, Cell.Plain);
            Run(new Vector3(1f, 0f, 20f), Vector3.right, 0f, "Cellar",
                Cell.Plain, Cell.Window, Cell.Plain);
            Run(new Vector3(7f, 0f, 20f), Vector3.right, 0f, "Study",
                Cell.Grid, Cell.Window, Cell.Plain);

            // ---- west face (yaw 270) + east face (yaw 90) ----
            Run(new Vector3(-12f, 0f, 9f), Vector3.forward, 270f, "Forge",
                Cell.Plain, Cell.Window, Cell.Grid);
            Run(new Vector3(-12f, 0f, 15f), Vector3.forward, 270f, "Pantry",
                Cell.Grid, Cell.Window, Cell.Plain);
            Run(new Vector3(12f, 0f, 9f), Vector3.forward, 90f, "Greenhouse",
                Cell.Window, Cell.Window, Cell.Window); // the glasshouse flank
            Run(new Vector3(12f, 0f, 15f), Vector3.forward, 90f, "Study",
                Cell.Plain, Cell.Window, Cell.Grid);

            // ---- row divider z=14 (doors are WARDS into the north rooms) ----
            Run(new Vector3(-11f, 0f, 14f), Vector3.right, 0f, "Divider",
                Cell.Plain, Cell.Plain, Cell.Grid, Cell.Plain,
                Cell.Door, Cell.Plain, Cell.Plain, Cell.Door,
                Cell.Plain, Cell.Grid, Cell.Plain, Cell.Plain);
            Ward(new Vector3(-3f, 0f, 14f), 0f, "Library", RuneType.LuminanceUp);
            Ward(new Vector3(3f, 0f, 14f), 0f, "Cellar", RuneType.StateLiquid);

            // ---- interior partitions (X-normal walls) ----
            Run(new Vector3(-4f, 0f, 9f), Vector3.forward, 90f, "Forge|Hall",
                Cell.Plain, Cell.Door, Cell.Plain);
            Ward(new Vector3(-4f, 0f, 11f), 90f, "Forge", RuneType.HeatUp);
            Run(new Vector3(4f, 0f, 9f), Vector3.forward, 90f, "Hall|Greenhouse",
                Cell.Plain, Cell.Door, Cell.Plain);
            Ward(new Vector3(4f, 0f, 11f), 90f, "Greenhouse", RuneType.StickyUp);
            Run(new Vector3(-6f, 0f, 15f), Vector3.forward, 90f, "Pantry|Library",
                Cell.Plain, Cell.Door, Cell.Plain);
            Ward(new Vector3(-6f, 0f, 17f), 90f, "Pantry", RuneType.DensityDown);
            Run(new Vector3(0f, 0f, 15f), Vector3.forward, 90f, "Library|Cellar",
                Cell.Plain, Cell.Plain, Cell.Plain);
            Run(new Vector3(6f, 0f, 15f), Vector3.forward, 90f, "Cellar|Study",
                Cell.Plain, Cell.Door, Cell.Plain);
            Ward(new Vector3(6f, 0f, 17f), 90f, "Study",
                RuneType.HeatUp, RuneType.StateSolid, RuneType.DirectionAway);

            // ---- observatory: a second floor over the library ----
            Run(new Vector3(-5f, H, 14f), Vector3.right, 180f, "Observatory",
                Cell.Plain, Cell.Window, Cell.Plain);
            Run(new Vector3(-5f, H, 20f), Vector3.right, 0f, "Observatory",
                Cell.Plain, Cell.Window, Cell.Plain);
            Run(new Vector3(-6f, H, 15f), Vector3.forward, 270f, "Observatory",
                Cell.Plain, Cell.Window, Cell.Plain);
            Run(new Vector3(0f, H, 15f), Vector3.forward, 90f, "Observatory",
                Cell.Plain, Cell.Window, Cell.Plain);

            // corner trim hides the vertical seams
            foreach (var c in new[]
            {
                new Vector3(-12f, 0f, 8f), new Vector3(12f, 0f, 8f),
                new Vector3(-12f, 0f, 20f), new Vector3(12f, 0f, 20f),
            })
                VillageBuilder.Place("Corner_Exterior_Wood", c, 0f, 1f, SurfaceMaterialType.Wood);
            foreach (var c in new[]
            {
                new Vector3(-6f, H, 14f), new Vector3(0f, H, 14f),
                new Vector3(-6f, H, 20f), new Vector3(0f, H, 20f),
            })
                VillageBuilder.Place("Corner_Exterior_Wood", c, 0f, 1f, SurfaceMaterialType.Wood);
        }

        /// A row of wall modules from `start`, stepping 2m along `along`.
        /// Windows get their glass insert + a DORMANT (inactive) zombie entry
        /// marker just outside — the siege code wakes a room's windows when
        /// its ward opens. Doors/arches leave openings (wards placed after).
        static void Run(Vector3 start, Vector3 along, float faceYaw, string room, params Cell[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                Vector3 at = start + along * (i * W);
                switch (cells[i])
                {
                    case Cell.Plain:
                        VillageBuilder.Place("Wall_Plaster_Straight", at, faceYaw + Yaw, 1f, SurfaceMaterialType.Wood);
                        break;
                    case Cell.Grid:
                        VillageBuilder.Place("Wall_Plaster_WoodGrid", at, faceYaw + Yaw, 1f, SurfaceMaterialType.Wood);
                        break;
                    case Cell.Arch:
                        VillageBuilder.Place("Wall_Arch", at, faceYaw + Yaw, 1f, SurfaceMaterialType.Stone);
                        break;
                    case Cell.Window:
                        var wall = VillageBuilder.Place("Wall_Plaster_Window_Wide_Flat", at,
                            faceYaw + Yaw, 1f, SurfaceMaterialType.Wood);
                        if (wall != null)
                            VillageBuilder.PlacePivot("Window_Wide_Flat1", wall.transform.position,
                                wall.transform.rotation, SurfaceMaterialType.Wood);
                        // the dormant boarding point OUTSIDE the face. Tower
                        // windows push farther out so their columns clear the
                        // tower roof's plan footprint — the spawn ground-snap
                        // then lands zombies on the ADJACENT roof, not inside
                        // the attic (review-fleet finding).
                        Vector3 outDir = Quaternion.Euler(0f, faceYaw, 0f) * Vector3.forward;
                        float outDist = start.y > 0.5f ? 1.4f : 0.8f;
                        var entry = new GameObject($"WindowEntry_{room}_{++_windowCount}");
                        entry.transform.SetParent(_root, false);
                        entry.transform.position = at + outDir * outDist + Vector3.up * 0.3f;
                        entry.AddComponent<ZombieEntryPoint>();
                        entry.SetActive(false); // the ward's unlock wakes it (siege code)
                        break;
                    case Cell.Door:
                        VillageBuilder.Place("Wall_Plaster_Door_Round", at, faceYaw + Yaw, 1f, SurfaceMaterialType.Wood);
                        break;
                }
            }
        }

        /// The WARD: door frame + leaf in an already-placed doorway wall,
        /// sealed by a SpellLock demanding runes. The leaf must NOT be
        /// breakable — wards fall to knowledge, never to arson (the doctrine:
        /// demolition opens defenses, never progression).
        static void Ward(Vector3 doorwayGround, float faceYaw, string room, params RuneType[] demands)
        {
            // the frame slots with the same yaw math the doorway wall used
            var probe = VillageBuilder.Place("DoorFrame_Round_WoodDark", doorwayGround,
                faceYaw + Yaw, 1f, SurfaceMaterialType.Wood);
            var leaf = VillageBuilder.Place("Door_1_Round", doorwayGround,
                faceYaw + Yaw, 1f, SurfaceMaterialType.Wood);
            if (leaf == null) return;
            leaf.name = $"Ward_{room}";

            var stretch = leaf.transform.localScale;
            stretch.x *= 1.28f; // fill the 1.4m arch (the bounds-centered door fix)
            leaf.transform.localScale = stretch;
            var lb = VillageBuilder.BoundsOf(leaf);
            leaf.transform.position += new Vector3(
                doorwayGround.x - lb.center.x, 0f, doorwayGround.z - lb.center.z);

            // Prepare() auto-breakabled the wooden leaf — a ward laughs at fire
            Object.DestroyImmediate(leaf.GetComponent<Breakable>());
            Object.DestroyImmediate(leaf.GetComponent<Damageable>());
            Object.DestroyImmediate(leaf.GetComponent<Rigidbody>());
            Object.DestroyImmediate(leaf.GetComponent<DoorInteract>());

            var lockComp = leaf.AddComponent<SpellLock>();
            lockComp.Demands = demands;
            if (probe != null) probe.name = $"WardFrame_{room}";
        }

        // ------------------------------------------------------------ floors --
        /// Plank/brick visuals + ONE solid walkable canvas box per room. The
        /// canvas TAG is the room's conjuring palette: the forge floor is
        /// stone, the greenhouse floor is EARTH — State runes read it.
        static void Floors()
        {
            void Room(float x0, float x1, float z0, float z1, string plank,
                SurfaceMaterialType tag, string roomName, float baseY = 0f, Vector3? skipPlank = null)
            {
                if (plank != null)
                    for (float x = x0 + 1f; x < x1; x += 2f)
                        for (float z = z0 + 1f; z < z1; z += 2f)
                        {
                            if (skipPlank.HasValue && Mathf.Abs(x - skipPlank.Value.x) < 0.9f
                                && Mathf.Abs(z - skipPlank.Value.z) < 0.9f) continue;
                            var p = VillageBuilder.Place(plank,
                                new Vector3(x, baseY + 0.02f, z), 0f, 1f, tag);
                            VillageBuilder.StripColliders(p);
                        }
                var walk = new GameObject($"FloorCanvas_{roomName}");
                walk.transform.SetParent(_root, false);
                walk.transform.position = new Vector3((x0 + x1) * 0.5f, baseY + 0.045f, (z0 + z1) * 0.5f);
                var box = walk.AddComponent<BoxCollider>();
                box.size = new Vector3(x1 - x0 - 0.3f, 0.05f, z1 - z0 - 0.3f);
                walk.AddComponent<SurfaceMaterialTag>().Material = tag;
            }

            Room(-12f, -4f, 8f, 14f, "Floor_Brick", SurfaceMaterialType.Stone, "Forge");
            Room(-4f, 4f, 8f, 14f, "Floor_WoodDark", SurfaceMaterialType.Wood, "Hall");
            Room(4f, 12f, 8f, 14f, null, SurfaceMaterialType.Earth, "Greenhouse"); // bare earth beds
            Room(-12f, -6f, 14f, 20f, "Floor_WoodDark", SurfaceMaterialType.Wood, "Pantry");
            Room(-6f, 0f, 14f, 20f, "Floor_WoodDark", SurfaceMaterialType.Wood, "Library",
                0f, new Vector3(-5f, 0f, 15f)); // stairwell corner stays plank-free
            Room(0f, 6f, 14f, 20f, "Floor_Brick", SurfaceMaterialType.Stone, "Cellar");
            Room(6f, 12f, 14f, 20f, "Floor_WoodDark", SurfaceMaterialType.Wood, "Study");

            // observatory deck: two canvases around the stairwell hole (the
            // one place a seam is accepted — a lid over the stairs is worse)
            void Deck(float x0, float x1, float z0, float z1, string deckName)
            {
                for (float x = x0 + 1f; x < x1; x += 2f)
                    for (float z = z0 + 1f; z < z1; z += 2f)
                    {
                        var p = VillageBuilder.Place("Floor_WoodDark",
                            new Vector3(x, H + 0.02f, z), 0f, 1f, SurfaceMaterialType.Wood);
                        VillageBuilder.StripColliders(p);
                    }
                var walk = new GameObject(deckName);
                walk.transform.SetParent(_root, false);
                walk.transform.position = new Vector3((x0 + x1) * 0.5f, H + 0.045f, (z0 + z1) * 0.5f);
                var box = walk.AddComponent<BoxCollider>();
                box.size = new Vector3(x1 - x0 - 0.15f, 0.05f, z1 - z0 - 0.15f);
                walk.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
            }
            Deck(-4f, 0f, 14f, 20f, "DeckCanvas_Observatory");      // main deck

            // behind the stair top: CANVAS ONLY, no plank (a 2m plank here
            // would poke through the north wall — review-fleet measurement);
            // starts at z=18.9 so the 4.62m stair surfaces unobstructed
            var strip = new GameObject("DeckCanvas_ObservatoryWest");
            strip.transform.SetParent(_root, false);
            strip.transform.position = new Vector3(-5f, H + 0.045f, 19.45f);
            var stripBox = strip.AddComponent<BoxCollider>();
            stripBox.size = new Vector3(1.85f, 0.05f, 1.05f);
            strip.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
        }

        // ------------------------------------------------------------- roofs --
        static void Roofs()
        {
            VillageBuilder.Roof(new Vector3(-8f, 0f, 11f), 0f, 8f, 6f, H);   // forge
            VillageBuilder.Roof(new Vector3(0f, 0f, 11f), 0f, 8f, 6f, H);    // hall
            VillageBuilder.Roof(new Vector3(8f, 0f, 11f), 0f, 8f, 6f, H);    // greenhouse
            VillageBuilder.Roof(new Vector3(-9f, 0f, 17f), 0f, 6f, 6f, H);   // pantry
            VillageBuilder.Roof(new Vector3(3f, 0f, 17f), 0f, 6f, 6f, H);    // cellar
            VillageBuilder.Roof(new Vector3(9f, 0f, 17f), 0f, 6f, 6f, H);    // study
            VillageBuilder.Roof(new Vector3(-3f, 0f, 17f), 0f, 6f, 6f, H * 2f); // observatory tower cap

            // GABLE CAPS close the triangular void between wall-top and roof
            // ridge (Marko: "the house once again has holes on the side" —
            // the exact complaint the village houses fixed the same way).
            // Rooms are 6m deep, so the Brick6 triangle; one cap per gable
            // plane, boundary planes shared by neighbors get a single cap.
            void Cap(float x, float z, float yaw, float topY) =>
                VillageBuilder.Place("Roof_Front_Brick6", new Vector3(x, topY, z),
                    yaw, 1f, SurfaceMaterialType.Stone);
            foreach (float x in new[] { -12f, -4f, 4f, 12f })   // south row ends + shared planes
                Cap(x, 11f, x >= 12f ? 90f : -90f, H);
            foreach (float x in new[] { -12f, -6f, 0f, 6f, 12f }) // north row
                Cap(x, 17f, x >= 12f ? 90f : -90f, H);
            Cap(-6f, 17f, -90f, H * 2f);                          // observatory tower gables
            Cap(0f, 17f, 90f, H * 2f);
        }

        // ----------------------------------------------------- library stairs --
        static void ObservatoryStairs()
        {
            // the kit stair is authored 4.62m deep (fleet-measured from the
            // OBJ) — centered at z=16.55 its foot just clears the divider
            // wall and its top surfaces in front of the canvas strip
            VillageBuilder.Place("Stair_Interior_Simple",
                new Vector3(-5f, 0.05f, 16.55f), StairYaw, 1f, SurfaceMaterialType.Wood);

            // the stair-base ward: the observatory demands the LIGHTNING pair
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Ward_Observatory";
            block.transform.SetParent(_root, false);
            block.transform.position = new Vector3(-5f, 1.15f, 14.6f);
            block.transform.localScale = new Vector3(1.9f, 2.2f, 0.25f);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            block.GetComponent<Renderer>().sharedMaterial =
                new Material(lit) { name = "SZ_WardBlock", color = new Color(0.45f, 0.3f, 0.5f) };
            block.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
            var obsLock = block.AddComponent<SpellLock>();
            obsLock.Demands = new[] { RuneType.LuminanceUp, RuneType.DensityUp };
        }

        // ---------------------------------------------------------- courtyard --
        static void Courtyard()
        {
            // THE HEART: the Grand Cauldron — ink well, perk pot, and (siege
            // code next) the thing the horde comes to smash
            var grand = VillageBuilder.Place("Cauldron", new Vector3(0f, 0f, 0f), 0f,
                1.25f, SurfaceMaterialType.Metal);
            if (grand != null)
            {
                grand.name = "GrandCauldron";
                grand.AddComponent<CauldronMarker>().Type = CauldronType.Drawing;
                VillageBuilder.AddWarmLight(grand, 1.5f, 7f);
            }

            VillageBuilder.Place("Dummy", new Vector3(3.2f, 0f, -3f), 200f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Bench", new Vector3(-3.4f, 0f, -2f), 100f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Bench", new Vector3(3.6f, 0f, 2.4f), 285f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Stall_Cart_Empty", new Vector3(-8.6f, 0f, -4f), 25f, 1f, SurfaceMaterialType.Wood);
            foreach (var t in new[]
            {
                new Vector3(-6f, 1.15f, 2f), new Vector3(6f, 1.15f, 2f),
                new Vector3(-6f, 1.15f, -6f), new Vector3(6f, 1.15f, -6f),
            })
                VillageBuilder.PlaceFloat("Torch_Metal", t, 0f, 1f, SurfaceMaterialType.Metal, warmLight: true);

            // fence ring with three deliberate gaps — the live siege membrane
            for (float x = -12.3f; x <= 12.3f; x += 2.05f)          // south run
                if (Mathf.Abs(x) > 1.4f)
                    VillageBuilder.Place("Prop_WoodenFence_Single",
                        new Vector3(x, 0f, -9f), 0f, 1f, SurfaceMaterialType.Wood);
            foreach (float side in new[] { -1f, 1f })               // west + east runs
                for (float z = -7.6f; z <= 6.6f; z += 2.05f)
                    if (Mathf.Abs(z + 0.5f) > 1.4f)
                        VillageBuilder.Place("Prop_WoodenFence_Single",
                            new Vector3(side * 13f, 0f, z), 90f, 1f, SurfaceMaterialType.Wood);

            Entry("Gate_South", new Vector3(0f, 0.3f, -8.6f));
            Entry("Gate_West", new Vector3(-12.6f, 0.3f, -0.5f));
            Entry("Gate_East", new Vector3(12.6f, 0.3f, -0.5f));
            Entry("Rear_West", new Vector3(-6f, 0.3f, 21.4f));      // behind the building, ON the slab
            Entry("Rear_East", new Vector3(6f, 0.3f, 21.4f));
        }

        static void Entry(string entryName, Vector3 pos)
        {
            var e = new GameObject(entryName);
            e.transform.SetParent(_root, false);
            e.transform.position = pos;
            e.AddComponent<ZombieEntryPoint>();
        }

        // ------------------------------------------------------ room contents --
        static void RoomContents()
        {
            // HALL — open from the start: the armory wall and the first chest
            // (stand centered on the back wall between the two ward doors —
            // its old spot physically blocked the Library doorway)
            VillageBuilder.Place("WeaponStand", new Vector3(0f, 0f, 13.2f), 180f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.PlaceWeaponPickup(new Vector3(1.1f, 0.4f, 13f));
            VillageBuilder.Place("Table_Large", new Vector3(2.4f, 0f, 12.4f), 90f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.PlaceFloat("Banner_1", new Vector3(0f, 2.5f, 13.7f), 0f, 1f, SurfaceMaterialType.Wood);
            Spot<MysteryChestSpawnPoint>("ChestSpot_Hall", new Vector3(-3f, 0.3f, 9.4f));

            // FORGE (Heat) — metal palette, the second weapon, Quick Hands pot
            VillageBuilder.Place("Anvil_Log", new Vector3(-9.4f, 0f, 11.6f), 320f, 1f, SurfaceMaterialType.Metal);
            VillageBuilder.Place("Workbench", new Vector3(-11f, 0f, 9.6f), 90f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Whetstone", new Vector3(-6.2f, 0f, 12.6f), 40f, 1f, SurfaceMaterialType.Stone);
            VillageBuilder.Place("Crate_Metal", new Vector3(-5.4f, 0f, 9f), 15f, 1f, SurfaceMaterialType.Metal);
            var chamber = RuneChamberWeapon.CreatePickup(new Vector3(-8.2f, 0.4f, 12.8f));
            if (chamber != null) chamber.transform.SetParent(_root, true);
            Pot(CauldronType.Weapon, new Vector3(-7f, 0f, 9.6f));

            // GREENHOUSE (Sticky) — earth beds, living things, Iron Gut pot
            VillageBuilder.Place("FarmCrate_Empty", new Vector3(6f, 0f, 12.6f), 10f, 1f, SurfaceMaterialType.Earth);
            VillageBuilder.Place("FarmCrate_Apple", new Vector3(10.6f, 0f, 12.6f), 350f, 1f, SurfaceMaterialType.Earth);
            VillageBuilder.PlacePT(PTPlants + "PT_Grass_02.prefab", new Vector3(7.6f, 0f, 11f),
                40f, 1.1f, SurfaceMaterialType.Unknown, keepColliders: false);
            VillageBuilder.PlacePT(PTShrubs + "PT_Generic_Shrub_01_green.prefab", new Vector3(9.6f, 0f, 10f),
                200f, 0.9f, SurfaceMaterialType.Wood);
            VillageBuilder.PlacePT(PTFlowers + "PT_Poppy_02.prefab", new Vector3(6.4f, 0f, 9.4f),
                120f, 1f, SurfaceMaterialType.Unknown, keepColliders: false);
            Pot(CauldronType.Survival, new Vector3(10.4f, 0f, 9.4f));

            // PANTRY (Density) — the cheap first unlock: throwables and a card
            VillageBuilder.Place("Barrel_Apples", new Vector3(-11f, 0f, 18.6f), 75f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Barrel", new Vector3(-10.6f, 0f, 15.2f), 10f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("FarmCrate_Carrot", new Vector3(-7.4f, 0f, 18.8f), 200f, 1f, SurfaceMaterialType.Earth);
            VillageBuilder.Place("Bag", new Vector3(-8.6f, 0f, 15.4f), 150f, 1f, SurfaceMaterialType.Wood);
            Spot<RuneCardSpawnPoint>("CardSpot_Pantry", new Vector3(-9f, 0.4f, 17f));

            // LIBRARY (Light) — pages live here; candles keep it readable
            VillageBuilder.Place("Bookcase_2", new Vector3(-0.8f, 0f, 16f), 270f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Bookcase_2", new Vector3(-0.8f, 0f, 18.4f), 270f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("BookStand", new Vector3(-3.2f, 0f, 18.6f), 180f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Book_Stack_1", new Vector3(-2.2f, 0f, 16.4f), 30f, 1f, SurfaceMaterialType.Wood);
            var candle = VillageBuilder.Place("CandleStick_Stand", new Vector3(-1.6f, 0f, 19f),
                0f, 1f, SurfaceMaterialType.Metal);
            VillageBuilder.AddWarmLight(candle, 1.1f, 5f);
            Spot<RuneCardSpawnPoint>("CardSpot_Library", new Vector3(-2.6f, 0.4f, 17.4f));

            // ALCHEMY CELLAR (Liquid) — COAL for oil, ink stores, Double Brew
            var coalA = VillageBuilder.Place("Crate_Wooden", new Vector3(4.8f, 0f, 18.8f), 25f, 1f, SurfaceMaterialType.Coal);
            var coalB = VillageBuilder.Place("Crate_Wooden", new Vector3(5.2f, 0f, 17.4f), 340f, 1f, SurfaceMaterialType.Coal);
            if (coalA != null) coalA.name = "CoalCrate";
            if (coalB != null) coalB.name = "CoalCrate";
            VillageBuilder.Place("Barrel", new Vector3(1f, 0f, 18.8f), 55f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Bucket_Metal", new Vector3(1.4f, 0f, 15.2f), 0f, 1f, SurfaceMaterialType.Metal);
            VillageBuilder.Place("Pot_1", new Vector3(4.6f, 0f, 15f), 80f, 1f, SurfaceMaterialType.Metal);
            Pot(CauldronType.Spell, new Vector3(2.8f, 0f, 16.6f));

            // MASTER'S STUDY (3 runes) — the origin: lore, the second chest
            VillageBuilder.Place("Table_Large", new Vector3(9f, 0f, 17.4f), 0f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("BookStand", new Vector3(7.4f, 0f, 19f), 160f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Cage_Small", new Vector3(11.2f, 0f, 15f), 15f, 1f, SurfaceMaterialType.Metal);
            VillageBuilder.Place("Scroll_1", new Vector3(9.4f, 0f, 15.4f), 220f, 1f, SurfaceMaterialType.Wood);
            var chandelier = VillageBuilder.PlaceFloat("Chandelier", new Vector3(9f, 2.55f, 17f),
                0f, 1f, SurfaceMaterialType.Metal);
            VillageBuilder.AddWarmLight(chandelier, 1.2f, 6f);
            Spot<MysteryChestSpawnPoint>("ChestSpot_Study", new Vector3(10.6f, 0.3f, 18.8f));
            var shrine = new GameObject("LoreShrine_Study"); // Spark of Tales hook
            shrine.transform.SetParent(_root, false);
            shrine.transform.position = new Vector3(11.6f, 1.3f, 17f);

            // OBSERVATORY (Light+Density) — the vantage, the rare card
            VillageBuilder.Place("Table_Large", new Vector3(-2f, H + 0.06f, 18.4f), 90f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Book_5", new Vector3(-3.4f, H + 0.06f, 18.8f), 300f, 1f, SurfaceMaterialType.Wood);
            var triple = VillageBuilder.Place("CandleStick_Triple", new Vector3(-1f, H + 0.06f, 15.4f),
                0f, 1f, SurfaceMaterialType.Metal);
            VillageBuilder.AddWarmLight(triple, 1f, 5f);
            var rare = new GameObject("CardSpot_Observatory");
            rare.transform.SetParent(_root, false);
            rare.transform.position = new Vector3(-2f, H + 0.5f, 16.4f);
            rare.AddComponent<RuneCardSpawnPoint>().Rare = true;
        }

        static void Spot<T>(string spotName, Vector3 pos) where T : Component
        {
            var s = new GameObject(spotName);
            s.transform.SetParent(_root, false);
            s.transform.position = pos;
            s.AddComponent<T>();
        }

        /// A room perk pot: the prop + its marker (PerkCauldron self-attaches
        /// on scene load — Marko's shop wiring, reused verbatim).
        static void Pot(CauldronType type, Vector3 at)
        {
            var pot = VillageBuilder.Place("Cauldron", at, 0f, 0.85f, SurfaceMaterialType.Metal);
            if (pot == null) return;
            pot.name = $"PerkPot_{type}";
            pot.AddComponent<CauldronMarker>().Type = type;
            VillageBuilder.AddWarmLight(pot, 1f, 4f);
        }

        // ------------------------------------------------------ facade canvas --
        /// Seamless drawing planes over the EXTERIOR faces (the house rule:
        /// strokes must never split at wall-module seams). The hall front gets
        /// two flanking canvases — never one across the open arches.
        static void FacadeCanvases()
        {
            // south face: forge | hall-left | hall-right | greenhouse
            VillageBuilder.AddCanvas(new Vector3(-8f, 0f, 8f - 0.24f), 180f, 8f, H, SurfaceMaterialType.Wood);
            VillageBuilder.AddCanvas(new Vector3(-3f, 0f, 8f - 0.24f), 180f, 2f, H, SurfaceMaterialType.Wood);
            VillageBuilder.AddCanvas(new Vector3(3f, 0f, 8f - 0.24f), 180f, 2f, H, SurfaceMaterialType.Wood);
            VillageBuilder.AddCanvas(new Vector3(8f, 0f, 8f - 0.24f), 180f, 8f, H, SurfaceMaterialType.Wood);
            // north face: one long plane + ALL FOUR of the tower's upper faces
            VillageBuilder.AddCanvas(new Vector3(0f, 0f, 20f + 0.24f), 0f, 24f, H, SurfaceMaterialType.Wood);
            VillageBuilder.AddCanvas(new Vector3(-3f, H, 20f + 0.24f), 0f, 6f, H, SurfaceMaterialType.Wood);
            VillageBuilder.AddCanvas(new Vector3(-3f, H, 14f - 0.24f), 180f, 6f, H, SurfaceMaterialType.Wood);
            VillageBuilder.AddCanvas(new Vector3(-6f - 0.24f, H, 17f), 270f, 6f, H, SurfaceMaterialType.Wood);
            VillageBuilder.AddCanvas(new Vector3(0.24f, H, 17f), 90f, 6f, H, SurfaceMaterialType.Wood);
            // west + east flanks
            VillageBuilder.AddCanvas(new Vector3(-12f - 0.24f, 0f, 14f), 270f, 12f, H, SurfaceMaterialType.Wood);
            VillageBuilder.AddCanvas(new Vector3(12f + 0.24f, 0f, 14f), 90f, 12f, H, SurfaceMaterialType.Wood);
        }

        // Polytope nature paths (same folders VillageBuilder uses)
        const string PTPlants = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Plants/";
        const string PTShrubs = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Shrubs/";
        const string PTFlowers = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Flowers/";
    }
}
