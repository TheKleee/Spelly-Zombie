using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// SHIP GRAYBOX — Marko's ask (July 16): "patch up a ship from the assets
    /// we have" to prove the sea-siege loop. One walkable deck over open
    /// water: kit planks + ONE solid deck canvas (seamless ink), fence
    /// railings with deliberate boarding gaps (breakable — zombies chew
    /// through to board), sterncastle with stairs, mast with a flight-perch
    /// crow's nest, cauldron midship (perk shop + the "come back to refill"
    /// anchor), and a FallCatcher just under the waves so anything overboard
    /// is handled: players teleport back aboard (downed mid-run — the sea's
    /// price), zombies/props simply sink and vanish.
    ///
    /// Graybox honesty: the hull SHELL is plain slabs (reliable, restyle-me);
    /// everything you stand on or touch is real kit. All under one deletable
    /// "SZ_Ship" root. Refuses non-empty scenes, same guard as the sandbox.
    public static class ShipBuilder
    {
        // one-knob fixes, VillageBuilder tradition — flip if a kit piece
        // faces the wrong way in the first build
        const float StairYaw = 180f;   // stairs should CLIMB toward the sterncastle (-Z)
        const float FenceYaw = 0f;     // OBJ oracle: kit fences run along X; +90 turns them to run along Z

        const float DeckY = 3.2f;      // main deck height over the waterline
        const float HalfW = 4.2f;      // hull half-width
        const float SternZ = -11.5f, BowZ = 11.5f, BowTipZ = 15.2f;

        static Transform _root;
        static Material _wood, _woodDark, _sea, _sail;

        [MenuItem("Spelly Zombie/Build SHIP Graybox (empty scene only)")]
        public static void Build()
        {
            foreach (var guardName in new[] { "SZ_Menu", "SZ_Village", "SZ_GameMap", "SZ_Player", "SZ_Test", "SZ_Ship" })
                if (GameObject.Find(guardName) != null)
                {
                    Debug.LogError($"[SpellyZombie] REFUSING to build: this scene contains '{guardName}'. " +
                                   "The ship only builds into a fresh empty scene (File → New Scene).");
                    return;
                }

            EnvironmentTools.WireFxLibrary();
            _root = new GameObject("SZ_Ship").transform;
            VillageBuilder.BeginPlacement(_root);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            _wood = new Material(lit) { name = "SZ_ShipWood", color = new Color(0.48f, 0.34f, 0.22f) };
            _woodDark = new Material(lit) { name = "SZ_ShipWoodDark", color = new Color(0.36f, 0.25f, 0.16f) };
            _sea = new Material(lit) { name = "SZ_Sea", color = new Color(0.16f, 0.38f, 0.5f) };
            _sail = new Material(lit) { name = "SZ_Sail", color = new Color(0.92f, 0.89f, 0.8f) };
            _wood.SetFloat("_Smoothness", 0.08f);
            _woodDark.SetFloat("_Smoothness", 0.08f);
            _sail.SetFloat("_Smoothness", 0.05f);
            _sea.SetFloat("_Smoothness", 0.55f); // the one shiny thing: water

            // template cameras off — the player's camera takes over
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

            Sea();
            Hull();
            Deck();
            Sterncastle();
            Mast();
            DeckDressing();
            BoardingPoints();

            new GameObject("SZ_DrawingWorld").AddComponent<DrawingWorld>();

            TestSandboxBuilder.BuildBeanPlayer(_root);
            var player = GameObject.Find("SZ_Player");
            if (player != null) player.transform.position = new Vector3(0f, DeckY + 1.15f, -4.5f);
            Selection.activeGameObject = player;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SpellyZombie] SHIP graybox ready. Deck is one seamless ink canvas — draw anywhere. " +
                      "Fence gaps + bow + stern are zombie boarding points (Z starts the round). Fences are " +
                      "chewable barricades. Fall in the sea and the magic throws you back aboard (downed " +
                      "mid-run). Cauldron midship = perk shop. Crow's nest = flight perch. " +
                      "Knobs if a kit piece faces wrong: ShipBuilder.StairYaw / FenceYaw.");
        }

        // ------------------------------------------------------------ water --
        static void Sea()
        {
            var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea";
            sea.transform.SetParent(_root, false);
            sea.transform.localScale = new Vector3(60f, 1f, 60f); // 600 m of open water
            sea.GetComponent<Renderer>().sharedMaterial = _sea;
            Object.DestroyImmediate(sea.GetComponent<Collider>()); // visual only — everything falls THROUGH
            sea.AddComponent<WaterSurface>(); // doctrine: ink cannot exist on water

            // the sea's actual behavior: a net just under the waves. Players
            // teleport back aboard (FallCatcher downs them mid-run); zombies,
            // barrels and escaped spells sink out of existence.
            var net = new GameObject("SZ_SafetyNet");
            net.transform.SetParent(_root, false);
            net.transform.position = new Vector3(0f, -2.2f, 0f);
            var netBox = net.AddComponent<BoxCollider>();
            netBox.size = new Vector3(620f, 2f, 620f);
            netBox.isTrigger = true;
            // offset from the cauldron so the ground-snap raycast lands on DECK
            net.AddComponent<FallCatcher>().RespawnPoint = new Vector3(1.1f, DeckY + 1.3f, -1.6f);
        }

        // ------------------------------------------------------------- hull --
        /// Slab shell with an actual BOAT silhouette: the side is a polyline
        /// in plan view (stern corner → shoulders → bow cheek → tip) and each
        /// segment becomes a wall slab whose yaw/length are COMPUTED from its
        /// endpoints — no hand-set angles, so the V physically cannot open
        /// backward again. Every face is drawable wood.
        static void Hull()
        {
            // port-side outline, bow at +Z (starboard is the x-mirror)
            Vector2[] outline =
            {
                new Vector2(-2.7f, SternZ),          // stern corner (tapered tail)
                new Vector2(-HalfW, -6.5f),          // aft shoulder
                new Vector2(-HalfW, 6.5f),           // bow shoulder
                new Vector2(-2.9f, BowZ),            // bow cheek
                new Vector2(0f, BowTipZ),            // tip
            };
            foreach (float mirror in new[] { 1f, -1f })
                for (int i = 0; i < outline.Length - 1; i++)
                {
                    Vector2 a = outline[i] * new Vector2(mirror, 1f);
                    Vector2 b = outline[i + 1] * new Vector2(mirror, 1f);
                    WallBetween($"Hull_{(mirror > 0f ? "Port" : "Starboard")}_{i}", a, b, 4.4f, 2.0f, _wood);
                }
            // stern transom caps the tail between the two stern corners
            Slab("Hull_Stern", new Vector3(0f, 2.0f, SternZ), new Vector3(5.7f, 4.4f, 0.3f), _wood);

            // keel mass under the deck, inset so the taper reads from the water
            Slab("Hull_Keel", new Vector3(0f, 1.3f, -0.2f), new Vector3(6.8f, 2.6f, 20f), _woodDark);
            Slab("Hull_KeelBow", new Vector3(0f, 1.3f, 10.8f), new Vector3(3.4f, 2.6f, 4.6f), _woodDark);

            // foredeck wedge filling the bow nose (walkable, fits the taper)
            Slab("Foredeck", new Vector3(0f, DeckY - 0.025f, 11.9f), new Vector3(3.4f, 0.05f, 2.4f), _woodDark);

            // bowsprit — the ship points somewhere
            var sprit = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sprit.name = "Bowsprit";
            sprit.transform.SetParent(_root, false);
            sprit.transform.localScale = new Vector3(0.22f, 2.6f, 0.22f);
            sprit.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            sprit.transform.position = new Vector3(0f, DeckY + 1.1f, BowTipZ + 1.2f);
            sprit.GetComponent<Renderer>().sharedMaterial = _woodDark;
            sprit.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
        }

        /// A wall slab spanning exactly from plan-point a to plan-point b:
        /// yaw and length derive from the endpoints, center is the midpoint.
        static void WallBetween(string name, Vector2 a, Vector2 b, float height, float centerY, Material mat)
        {
            Vector2 d = b - a;
            float len = d.magnitude + 0.35f; // slight overlap hides corner seams
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            var slab = Slab(name, new Vector3((a.x + b.x) * 0.5f, centerY, (a.y + b.y) * 0.5f),
                new Vector3(0.3f, height, len), mat);
            slab.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ------------------------------------------------------------- deck --
        /// Kit planks for the look, ONE solid box for feet and pen — the
        /// plaza-canvas rule: strokes must never split at module seams.
        static void Deck()
        {
            for (float z = -10.5f; z <= 9.5f; z += 2f)
            {
                // the hull tapers toward bow and stern — outer plank columns
                // would poke through the sides there, so the end rows narrow
                bool narrow = z < -8.6f || z > 8.6f;
                for (float x = narrow ? -1f : -3f; x <= (narrow ? 1f : 3f); x += 2f)
                {
                    var plank = VillageBuilder.Place("Floor_WoodDark",
                        new Vector3(x, DeckY - 0.02f, z), 0f, 1f, SurfaceMaterialType.Wood);
                    VillageBuilder.StripColliders(plank);
                }
            }

            // ONE walkable drawing surface — a seam would split strokes, so a
            // single box slightly inside the hull beats two fitted ones
            var walk = new GameObject("DeckCanvas");
            walk.transform.SetParent(_root, false);
            walk.transform.position = new Vector3(0f, DeckY, -1.0f);
            var box = walk.AddComponent<BoxCollider>();
            box.size = new Vector3(7.2f, 0.06f, 20f);
            walk.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
        }

        // ------------------------------------------------------ sterncastle --
        /// Raised aft platform one wall-module high: stairs up from the main
        /// deck, railed edges, the loot chest — high ground worth holding.
        static void Sterncastle()
        {
            float topY = DeckY + VillageBuilder.WallH; // stairs climb exactly one module
            float midZ = -9.2f;

            // supports: wood-grid walls under the front edge (decor + cover),
            // solid side cheeks so the castle doesn't float — the space under
            // it becomes a sheltered alcove worth hiding in
            VillageBuilder.Place("Wall_Plaster_WoodGrid", new Vector3(-1f, DeckY, -6.9f), 0f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Wall_Plaster_WoodGrid", new Vector3(1f, DeckY, -6.9f), 0f, 1f, SurfaceMaterialType.Wood);
            Slab("Sterncastle_CheekPort", new Vector3(-3.75f, DeckY + VillageBuilder.WallH * 0.5f, -9.1f),
                new Vector3(0.3f, VillageBuilder.WallH, 4.6f), _wood);
            Slab("Sterncastle_CheekStarboard", new Vector3(3.75f, DeckY + VillageBuilder.WallH * 0.5f, -9.1f),
                new Vector3(0.3f, VillageBuilder.WallH, 4.6f), _wood);

            Slab("Sterncastle_Floor", new Vector3(0f, topY - 0.06f, midZ), new Vector3(HalfW * 2f - 0.3f, 0.12f, 4.6f), _woodDark);
            for (float x = -3f; x <= 3f; x += 2f)
                for (float z = -10.4f; z <= -8.4f; z += 2f)
                {
                    var plank = VillageBuilder.Place("Floor_WoodDark",
                        new Vector3(x, topY - 0.02f, z), 0f, 1f, SurfaceMaterialType.Wood);
                    VillageBuilder.StripColliders(plank);
                }
            var walk = new GameObject("SterncastleCanvas");
            walk.transform.SetParent(_root, false);
            walk.transform.position = new Vector3(0f, topY, midZ);
            var box = walk.AddComponent<BoxCollider>();
            box.size = new Vector3(HalfW * 2f - 0.4f, 0.06f, 4.5f);
            walk.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;

            VillageBuilder.Place("Stairs_Exterior_Straight",
                new Vector3(0f, DeckY, -6.2f), StairYaw, 1f, SurfaceMaterialType.Wood);

            // rails around the aft edges — breakable, like everything wooden.
            // back rail runs along X (the kit's authored axis), sides get +90
            for (float x = -3.1f; x <= 3.1f; x += 2.05f)
                VillageBuilder.Place("Prop_WoodenFence_Single",
                    new Vector3(x, topY, -11.2f), FenceYaw, 1f, SurfaceMaterialType.Wood);
            foreach (float side in new[] { -1f, 1f })
                for (float z = -10.3f; z <= -7.3f; z += 2.05f)
                    VillageBuilder.Place("Prop_WoodenFence_Single",
                        new Vector3(side * (HalfW - 0.35f), topY, z), FenceYaw + 90f, 1f, SurfaceMaterialType.Wood);

            VillageBuilder.Place("Chest_Wood",
                new Vector3(1.6f, topY, -10.4f), -25f, 1f, SurfaceMaterialType.Wood);
            var chestSpot = new GameObject("ChestSpot_Sterncastle");
            chestSpot.transform.SetParent(_root, false);
            chestSpot.transform.position = new Vector3(-1.6f, topY + 0.3f, -10.2f);
            chestSpot.AddComponent<MysteryChestSpawnPoint>();
        }

        // ------------------------------------------------------------- mast --
        static void Mast()
        {
            var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mast.name = "Mast";
            mast.transform.SetParent(_root, false);
            mast.transform.localScale = new Vector3(0.4f, 5.0f, 0.4f); // 10 m tall
            mast.transform.position = new Vector3(0f, DeckY + 5.0f, 2.5f);
            mast.GetComponent<Renderer>().sharedMaterial = _woodDark;
            mast.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;

            var yard = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            yard.name = "Yard";
            yard.transform.SetParent(_root, false);
            yard.transform.localScale = new Vector3(0.18f, 2.7f, 0.18f);
            yard.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            yard.transform.position = new Vector3(0f, DeckY + 7.6f, 2.5f);
            yard.GetComponent<Renderer>().sharedMaterial = _woodDark;
            yard.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;

            var sail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sail.name = "Sail";
            sail.transform.SetParent(_root, false);
            sail.transform.localScale = new Vector3(5.2f, 3.6f, 0.06f);
            sail.transform.position = new Vector3(0f, DeckY + 5.5f, 2.62f);
            sail.GetComponent<Renderer>().sharedMaterial = _sail;
            sail.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood; // canvas burns — gloriously

            // crow's nest: a small platform — updraft-seal flyers perch here
            Slab("CrowsNest", new Vector3(0f, DeckY + 8.6f, 2.5f), new Vector3(1.7f, 0.12f, 1.7f), _woodDark);

            VillageBuilder.PlaceFloat("Banner_2", new Vector3(0f, DeckY + 10.6f, 2.5f), 0f, 1f, SurfaceMaterialType.Wood);
        }

        // ----------------------------------------------------- deck dressing --
        static void DeckDressing()
        {
            // THE anchor: cauldron midship — ink perks, and the reason the
            // team keeps returning to the middle of the chaos
            var cauldron = VillageBuilder.Place("Cauldron",
                new Vector3(0f, DeckY, -2.5f), 0f, 1f, SurfaceMaterialType.Metal);
            if (cauldron != null)
            {
                cauldron.AddComponent<CauldronMarker>().Type = CauldronType.Drawing;
                VillageBuilder.AddWarmLight(cauldron, 1.3f, 6f);
            }

            VillageBuilder.PlaceFloat("Torch_Metal", new Vector3(-3.4f, DeckY + 1.15f, 5.5f), 0f, 1f,
                SurfaceMaterialType.Metal, warmLight: true);
            VillageBuilder.PlaceFloat("Torch_Metal", new Vector3(3.4f, DeckY + 1.15f, -5.5f), 0f, 1f,
                SurfaceMaterialType.Metal, warmLight: true);

            // cargo clutter: cover, fuel for fires, physics toys
            VillageBuilder.Place("Barrel", new Vector3(-2.9f, DeckY, 8.6f), 15f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Barrel", new Vector3(-2.2f, DeckY, 9.3f), 160f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Barrel_Apples", new Vector3(-2.7f, DeckY, 7.6f), 80f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Crate_Wooden", new Vector3(2.8f, DeckY, 7.9f), 30f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Crate_Wooden", new Vector3(2.3f, DeckY, 9.1f), -12f, 1f, SurfaceMaterialType.Wood);
            VillageBuilder.Place("Bench", new Vector3(-2.6f, DeckY, -4.6f), 90f, 1f, SurfaceMaterialType.Wood);

            // practice dummy on the foredeck — draw on it, burn it, sorry dummy
            var dummy = VillageBuilder.Place("Dummy", new Vector3(0.8f, DeckY, 10.6f), 200f, 1f, SurfaceMaterialType.Wood);
            if (dummy != null) dummy.AddComponent<RuneCardSpawnPoint>();
            var cardSpot = new GameObject("RuneCardSpot");
            cardSpot.transform.SetParent(_root, false);
            cardSpot.transform.position = new Vector3(-1.8f, DeckY + 0.4f, 3.5f);
            cardSpot.AddComponent<RuneCardSpawnPoint>();

            var chestSpot = new GameObject("ChestSpot_Foredeck");
            chestSpot.transform.SetParent(_root, false);
            chestSpot.transform.position = new Vector3(-0.9f, DeckY + 0.3f, 9.8f);
            chestSpot.AddComponent<MysteryChestSpawnPoint>();

            // both weapons aboard, port and starboard
            var slide = SealWeapon.CreatePickup(new Vector3(2.6f, DeckY + 0.4f, 1.5f));
            if (slide != null) slide.transform.SetParent(_root, true);
            var chamber = RuneChamberWeapon.CreatePickup(new Vector3(-2.6f, DeckY + 0.4f, 1.5f));
            if (chamber != null) chamber.transform.SetParent(_root, true);
        }

        // -------------------------------------------------- boarding points --
        /// Railings with deliberate GAPS: each gap is a zombie entry point —
        /// you SEE where boarders come over the side, and the fences between
        /// gaps are chewable barricades (auto-breakable wooden props).
        static void BoardingPoints()
        {
            // rails cover the straight midship run; past z≈7.6 the hull tapers
            // and its own 1.2m bulwark lip takes over as the railing
            foreach (float side in new[] { -1f, 1f })
                for (float z = -5.4f; z <= 7.6f; z += 2.05f)
                {
                    bool gap = Mathf.Abs(z - 0.75f) < 1.1f              // midship gaps, both sides
                               || (side > 0f && Mathf.Abs(z - 6.9f) < 1.1f)   // starboard bow-quarter
                               || (side < 0f && Mathf.Abs(z + 3.35f) < 1.1f); // port aft-quarter
                    if (gap) continue;
                    VillageBuilder.Place("Prop_WoodenFence_Single",
                        new Vector3(side * (HalfW - 0.35f), DeckY, z), FenceYaw + 90f, 1f, SurfaceMaterialType.Wood);
                }

            Entry("Boarding_PortMid", new Vector3(-(HalfW - 0.7f), DeckY + 0.3f, 0.75f));
            Entry("Boarding_StarboardMid", new Vector3(HalfW - 0.7f, DeckY + 0.3f, 0.75f));
            Entry("Boarding_StarboardBow", new Vector3(HalfW - 0.7f, DeckY + 0.3f, 6.9f));
            Entry("Boarding_PortAft", new Vector3(-(HalfW - 0.7f), DeckY + 0.3f, -3.35f));
            Entry("Boarding_BowTip", new Vector3(0f, DeckY + 0.3f, 11.8f));
            Entry("Boarding_SternRail", new Vector3(0f, DeckY + VillageBuilder.WallH + 0.3f, -10.9f));
        }

        static void Entry(string name, Vector3 pos)
        {
            var entryPoint = new GameObject(name);
            entryPoint.transform.SetParent(_root, false);
            entryPoint.transform.position = pos;
            entryPoint.AddComponent<ZombieEntryPoint>();
        }

        // ---------------------------------------------------------- helpers --
        /// A drawable graybox slab: box + collider + wood tag. The hull shell
        /// is slabs on purpose — Marko restyles; the pen and the chemistry
        /// already treat them as real wood.
        static GameObject Slab(string name, Vector3 center, Vector3 size, Material mat)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(_root, false);
            slab.transform.position = center;
            slab.transform.localScale = size;
            slab.GetComponent<Renderer>().sharedMaterial = mat;
            slab.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;
            return slab;
        }
    }
}
