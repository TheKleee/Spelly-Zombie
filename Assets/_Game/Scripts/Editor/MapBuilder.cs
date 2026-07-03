using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie.EditorTools
{
    /// "Spelly Zombie -> Build Island Map" — grayboxes the launch map into the
    /// open scene (meant for Game.unity). Kino der Toten skeleton on the design
    /// doc's zones: courtyard spawn with barricade windows -> left/right market
    /// alleys -> great hall -> narrow bridge down -> cold dungeon with the boss
    /// arena. The circuit courtyard->market->hall->market->courtyard is the
    /// zombie-training loop; the dungeon is the one deliberate dead end.
    public static class MapBuilder
    {
        static Transform _root;

        [MenuItem("Spelly Zombie/Build Island Map (Game Scene)")]
        public static void Build()
        {
            foreach (var old in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (old != null && old.scene.IsValid() && old.transform.parent == null && old.name.StartsWith("SZ_"))
                    Object.DestroyImmediate(old);
            foreach (var sceneCam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sceneCam.gameObject.SetActive(false);

            _root = new GameObject("SZ_IslandMap").transform;

            // ---- island ground + sky ----
            var sun = new GameObject("SZ_Sun");
            var sunLight = sun.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.1f;
            sunLight.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -28f, 0f);

            var ground = Box("Ground", new Vector3(0f, -0.11f, -10f), new Vector3(100f, 0.2f, 70f),
                new Color(0.46f, 0.50f, 0.38f), SurfaceMaterialType.Earth);
            ground.name = "Ground";
            Box("CloudFloor", new Vector3(0f, -20f, 20f), new Vector3(160f, 0.5f, 160f),
                new Color(0.92f, 0.93f, 0.96f), SurfaceMaterialType.Earth);

            Color cobble = new Color(0.55f, 0.55f, 0.58f);
            Color stone = new Color(0.62f, 0.60f, 0.56f);
            Color darkStone = new Color(0.40f, 0.40f, 0.44f);
            Color dungeonStone = new Color(0.30f, 0.32f, 0.38f);
            Color dirt = new Color(0.50f, 0.42f, 0.32f);
            Color wood = new Color(0.60f, 0.44f, 0.26f);

            // ================= COURTYARD (spawn) x[-9,9] z[-28,-10] =================
            Box("Courtyard_Floor", new Vector3(0f, -0.1f, -19f), new Vector3(18f, 0.2f, 18f), cobble, SurfaceMaterialType.Stone);

            // fountain — the classic seal-killer, dead center
            Cyl("Fountain_Rim", new Vector3(0f, 0.35f, -19f), new Vector3(4f, 0.35f, 4f), darkStone, SurfaceMaterialType.Stone);
            var fountainWater = Cyl("Fountain_Water", new Vector3(0f, 0.62f, -19f), new Vector3(3.4f, 0.06f, 3.4f),
                new Color(0.25f, 0.55f, 0.95f), SurfaceMaterialType.Water);
            fountainWater.AddComponent<WaterSurface>();

            // four team prep corners
            TeamCorner(new Vector3(-7f, 0.02f, -26f), new Color(0.9f, 0.3f, 0.3f));
            TeamCorner(new Vector3(7f, 0.02f, -26f), new Color(0.3f, 0.5f, 0.95f));
            TeamCorner(new Vector3(-7f, 0.02f, -12.5f), new Color(0.35f, 0.85f, 0.4f));
            TeamCorner(new Vector3(7f, 0.02f, -12.5f), new Color(0.95f, 0.85f, 0.3f));

            Cauldron(CauldronType.Survival, new Vector3(-6.5f, 0f, -26f), new Color(0.25f, 0.55f, 1f));

            // walls: south has 3 barricaded zombie windows; west/east have market doors
            WallX("Courtyard_S", -9f, 9f, -28f, 0f, 3.5f, stone, -6.5f, -4.5f, -1f, 1f, 4.5f, 6.5f);
            Window(new Vector3(-5.5f, 0f, -28f), 2f, true, wood);
            Window(new Vector3(0f, 0f, -28f), 2f, true, wood);
            Window(new Vector3(5.5f, 0f, -28f), 2f, true, wood);
            Entry(new Vector3(-5.5f, 0f, -30f));
            Entry(new Vector3(0f, 0f, -30f));
            Entry(new Vector3(5.5f, 0f, -30f));
            WallZ("Courtyard_W", -28f, -10f, -9f, 0f, 3.5f, stone, -12.5f, -10.5f);
            WallZ("Courtyard_E", -28f, -10f, 9f, 0f, 3.5f, stone, -12.5f, -10.5f);
            WallX("Courtyard_N", -9f, 9f, -10f, 0f, 3.5f, stone);

            // ================= CENTER BLOCK x[-9,9] z[-10,6] (forces the loop) =================
            Box("CenterBuilding", new Vector3(0f, 2f, -2f), new Vector3(18f, 4f, 16f), darkStone, SurfaceMaterialType.Stone);

            // ================= LEFT MARKET x[-16,-9] z[-13,6] =================
            Box("LeftMarket_Floor", new Vector3(-12.5f, -0.1f, -3.5f), new Vector3(7f, 0.2f, 19f), dirt, SurfaceMaterialType.Earth);
            WallZ("LeftMarket_W", -13f, 6f, -16f, 0f, 3.5f, stone, -5f, -3f);
            Window(new Vector3(-16f, 0f, -4f), 2f, false, wood);
            Entry(new Vector3(-17.5f, 0f, -4f));
            WallX("LeftMarket_S", -16f, -9f, -13f, 0f, 3.5f, stone);
            WallX("LeftMarket_N_stub", -16f, -14f, 6f, 0f, 3.5f, stone);
            Stall(new Vector3(-14.8f, 0f, -9f), wood);
            Stall(new Vector3(-14.8f, 0f, -1.5f), wood);
            Stall(new Vector3(-14.8f, 0f, 3f), wood);
            Cauldron(CauldronType.Drawing, new Vector3(-10.8f, 0f, 4.2f), new Color(0.3f, 0.9f, 0.4f));

            // ================= RIGHT MARKET x[9,16] z[-13,6] =================
            Box("RightMarket_Floor", new Vector3(12.5f, -0.1f, -3.5f), new Vector3(7f, 0.2f, 19f), dirt, SurfaceMaterialType.Earth);
            WallZ("RightMarket_E", -13f, 6f, 16f, 0f, 3.5f, stone, -5f, -3f);
            Window(new Vector3(16f, 0f, -4f), 2f, false, wood);
            Entry(new Vector3(17.5f, 0f, -4f));
            WallX("RightMarket_S", 9f, 16f, -13f, 0f, 3.5f, stone);
            WallX("RightMarket_N_stub", 14f, 16f, 6f, 0f, 3.5f, stone);
            Stall(new Vector3(14.8f, 0f, 3f), wood);
            var pond = Cyl("Market_Pond", new Vector3(13f, 0.015f, -8.5f), new Vector3(4.4f, 0.03f, 4.4f),
                new Color(0.25f, 0.55f, 0.95f), SurfaceMaterialType.Water);
            pond.AddComponent<WaterSurface>();
            CardSpawn(new Vector3(14.6f, 0f, -1.5f), false);
            CardSpawn(new Vector3(11f, 0f, 4.2f), false);

            // ================= GREAT HALL x[-14,14] z[6,22], tall & dark =================
            Box("Hall_Floor", new Vector3(0f, -0.1f, 14f), new Vector3(28f, 0.2f, 16f), darkStone, SurfaceMaterialType.Stone);
            Box("Hall_Ceiling", new Vector3(0f, 6.15f, 14f), new Vector3(28.8f, 0.3f, 16.8f), darkStone, SurfaceMaterialType.Stone);
            WallX("Hall_S", -14f, 14f, 6f, 0f, 6f, stone, -13.5f, -11.5f, 11.5f, 13.5f);
            WallX("Hall_N", -14f, 14f, 22f, 0f, 6f, stone, -2f, 2f);
            WallZ("Hall_W", 6f, 22f, -14f, 0f, 6f, stone);
            WallZ("Hall_E", 6f, 22f, 14f, 0f, 6f, stone);

            // the long feast table from the sketch
            var tableTop = Box("Hall_Table", new Vector3(0f, 0.95f, 15f), new Vector3(7f, 0.15f, 1.6f), wood, SurfaceMaterialType.Wood);
            Box("Hall_TableLegA", new Vector3(-3f, 0.45f, 15f), new Vector3(0.2f, 0.9f, 1.4f), wood, SurfaceMaterialType.Wood);
            Box("Hall_TableLegB", new Vector3(3f, 0.45f, 15f), new Vector3(0.2f, 0.9f, 1.4f), wood, SurfaceMaterialType.Wood);

            // chandelier: the hall's only light — and a future lightning conductor
            var chandelier = Cyl("SZ_Chandelier", new Vector3(0f, 5.2f, 14f), new Vector3(1.2f, 0.15f, 1.2f),
                new Color(0.75f, 0.72f, 0.55f), SurfaceMaterialType.Metal);
            chandelier.transform.SetParent(_root, true);
            var chLight = new GameObject("Chandelier_Light").AddComponent<Light>();
            chLight.transform.SetParent(chandelier.transform, false);
            chLight.transform.localPosition = new Vector3(0f, -1.5f, 0f);
            chLight.type = LightType.Point;
            chLight.color = new Color(1f, 0.87f, 0.65f);
            chLight.intensity = 2.2f;
            chLight.range = 14f;

            Cauldron(CauldronType.Spell, new Vector3(-12f, 0f, 20f), new Color(1f, 0.3f, 0.25f));
            var chest = Box("MysteryChest", new Vector3(12f, 0.4f, 8f), new Vector3(1.2f, 0.8f, 0.7f),
                new Color(0.55f, 0.35f, 0.7f), SurfaceMaterialType.Wood);
            chest.AddComponent<MysteryChestSpawnPoint>();

            // ================= BRIDGE: hall -> down -> dungeon =================
            Box("Bridge_Landing", new Vector3(0f, -0.1f, 23f), new Vector3(4f, 0.2f, 2f), darkStone, SurfaceMaterialType.Stone);
            var ramp = Box("Bridge_Ramp", new Vector3(0f, -2f, 27f), new Vector3(4f, 0.2f, 7.4f), darkStone, SurfaceMaterialType.Stone);
            ramp.transform.rotation = Quaternion.Euler(33.7f, 0f, 0f);
            Box("Bridge_Deck", new Vector3(0f, -4.15f, 35f), new Vector3(3f, 0.3f, 10.5f), darkStone, SurfaceMaterialType.Stone);
            Box("Bridge_RailW", new Vector3(-1.5f, -3.55f, 35f), new Vector3(0.15f, 0.9f, 10.5f), darkStone, SurfaceMaterialType.Stone);
            Box("Bridge_RailE", new Vector3(1.5f, -3.55f, 35f), new Vector3(0.15f, 0.9f, 10.5f), darkStone, SurfaceMaterialType.Stone);

            // ================= DUNGEON x[-12,12] z[40,58], floor y=-4, cold =================
            Box("Dungeon_Floor", new Vector3(0f, -4.1f, 49f), new Vector3(24f, 0.2f, 18f), dungeonStone, SurfaceMaterialType.Stone);
            Box("Dungeon_Ceiling", new Vector3(0f, 0.15f, 49f), new Vector3(24.8f, 0.3f, 18.8f), dungeonStone, SurfaceMaterialType.Stone);
            WallX("Dungeon_S", -12f, 12f, 40f, -4f, 4f, dungeonStone, -1.5f, 1.5f);
            WallX("Dungeon_N", -12f, 12f, 58f, -4f, 4f, dungeonStone, -8f, -6f, 6f, 8f);
            Entry(new Vector3(-7f, -4f, 59.5f));
            Entry(new Vector3(7f, -4f, 59.5f));
            WallZ("Dungeon_W", 40f, 58f, -12f, -4f, 4f, dungeonStone);
            WallZ("Dungeon_E", 40f, 58f, 12f, -4f, 4f, dungeonStone);

            Cauldron(CauldronType.Weapon, new Vector3(10f, -4f, 56f), new Color(0.9f, 0.9f, 0.95f));
            CardSpawn(new Vector3(-10f, -4f, 42f), true);
            CardSpawn(new Vector3(10f, -4f, 44f), true);
            CardSpawn(new Vector3(-3f, -4f, 57f), true);

            // boss arena: the self-drawing seal lives here later
            Cyl("BossSealArena", new Vector3(0f, -3.98f, 51f), new Vector3(8f, 0.02f, 8f),
                new Color(0.45f, 0.15f, 0.15f), SurfaceMaterialType.Stone);

            var puddleA = Cyl("Dungeon_PuddleA", new Vector3(-6f, -3.98f, 47f), new Vector3(2.4f, 0.02f, 2.4f),
                new Color(0.25f, 0.55f, 0.95f), SurfaceMaterialType.Water);
            puddleA.AddComponent<WaterSurface>();
            var puddleB = Cyl("Dungeon_PuddleB", new Vector3(7f, -3.98f, 52f), new Vector3(2.4f, 0.02f, 2.4f),
                new Color(0.25f, 0.55f, 0.95f), SurfaceMaterialType.Water);
            puddleB.AddComponent<WaterSurface>();

            var dungeonLamp = new GameObject("Dungeon_Lamp").AddComponent<Light>();
            dungeonLamp.transform.SetParent(_root, false);
            dungeonLamp.transform.position = new Vector3(0f, -1f, 42f);
            dungeonLamp.type = LightType.Point;
            dungeonLamp.color = new Color(0.6f, 0.7f, 1f);
            dungeonLamp.intensity = 1.2f;
            dungeonLamp.range = 9f;

            // ================= zone volumes (zone sim plugs in here) =================
            Zone("Courtyard", new Vector3(0f, 2f, -19f), new Vector3(18f, 7f, 18f), 18f);
            Zone("LeftMarket", new Vector3(-12.5f, 2f, -3.5f), new Vector3(7f, 7f, 19f), 18f);
            Zone("RightMarket", new Vector3(12.5f, 2f, -3.5f), new Vector3(7f, 7f, 19f), 18f);
            Zone("GreatHall", new Vector3(0f, 3f, 14f), new Vector3(28f, 7f, 16f), 15f);
            Zone("Bridge", new Vector3(0f, -2f, 31.5f), new Vector3(6f, 8f, 17f), 12f);
            Zone("Dungeon", new Vector3(0f, -2f, 49f), new Vector3(24f, 5f, 18f), 6f);

            // ---- test crates for proximity seals ----
            for (int i = 0; i < 3; i++)
            {
                var crate = Box($"Crate_{i}", new Vector3(-4f + i * 1.4f, 0.45f, -15f), Vector3.one * 0.9f, wood, SurfaceMaterialType.Wood);
                var crateBody = crate.AddComponent<Rigidbody>();
                crateBody.mass = 5f;
                crateBody.linearDamping = 1.2f;
                crateBody.angularDamping = 2f;
            }

            // ================= systems + player =================
            var world = new GameObject("SZ_DrawingWorld");
            world.AddComponent<DrawingWorld>();

            var player = new GameObject("SZ_Player");
            player.transform.position = new Vector3(0f, 1.05f, -24f);
            player.layer = 2;
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            var camGo = new GameObject("SZ_Camera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            camGo.tag = "MainCamera";
            var playerCam = camGo.AddComponent<Camera>();
            playerCam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            var controller = player.AddComponent<SimpleFPSController>();
            controller.CameraPivot = camGo.transform;
            var drawer = player.AddComponent<SurfaceDrawer>();
            drawer.Cam = playerCam;

            // training dummy near spawn — the only drawable body until the real character
            var rig = TestSceneBuilder.BuildMannequinShared(new Vector3(3f, 0f, -22f), 220f);
            rig.gameObject.AddComponent<EmotePlayer>();

            Selection.activeGameObject = player;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[SpellyZombie] Island map built: courtyard -> markets -> great hall -> bridge -> dungeon. Walk the loop!");
        }

        // ---------------- helpers ----------------

        static GameObject Box(string name, Vector3 center, Vector3 size, Color color, SurfaceMaterialType material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Setup(box, name, center, size, color, material);
            return box;
        }

        static GameObject Cyl(string name, Vector3 center, Vector3 scale, Color color, SurfaceMaterialType material)
        {
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Setup(cyl, name, center, scale, color, material);
            return cyl;
        }

        static void Setup(GameObject obj, string name, Vector3 center, Vector3 size, Color color, SurfaceMaterialType material)
        {
            obj.name = name;
            obj.transform.SetParent(_root, false);
            obj.transform.position = center;
            obj.transform.localScale = size;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                obj.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = color };
            obj.AddComponent<SurfaceMaterialTag>().Material = material;
        }

        /// Wall along X at fixed z. Gap pairs are x-coordinates (from, to).
        static void WallX(string name, float xFrom, float xTo, float z, float yBase, float height, Color color, params float[] gaps)
        {
            float prev = xFrom;
            int seg = 0;
            for (int i = 0; i + 1 < gaps.Length; i += 2)
            {
                if (gaps[i] > prev)
                    Box($"{name}_{seg++}", new Vector3((prev + gaps[i]) * 0.5f, yBase + height * 0.5f, z),
                        new Vector3(gaps[i] - prev, height, 0.4f), color, SurfaceMaterialType.Stone);
                prev = gaps[i + 1];
            }
            if (xTo > prev)
                Box($"{name}_{seg}", new Vector3((prev + xTo) * 0.5f, yBase + height * 0.5f, z),
                    new Vector3(xTo - prev, height, 0.4f), color, SurfaceMaterialType.Stone);
        }

        /// Wall along Z at fixed x. Gap pairs are z-coordinates (from, to).
        static void WallZ(string name, float zFrom, float zTo, float x, float yBase, float height, Color color, params float[] gaps)
        {
            float prev = zFrom;
            int seg = 0;
            for (int i = 0; i + 1 < gaps.Length; i += 2)
            {
                if (gaps[i] > prev)
                    Box($"{name}_{seg++}", new Vector3(x, yBase + height * 0.5f, (prev + gaps[i]) * 0.5f),
                        new Vector3(0.4f, height, gaps[i] - prev), color, SurfaceMaterialType.Stone);
                prev = gaps[i + 1];
            }
            if (zTo > prev)
                Box($"{name}_{seg}", new Vector3(x, yBase + height * 0.5f, (prev + zTo) * 0.5f),
                    new Vector3(0.4f, height, zTo - prev), color, SurfaceMaterialType.Stone);
        }

        /// Three wooden planks across a wall gap — the classic zombie barricade.
        static void Window(Vector3 center, float width, bool alongX, Color wood)
        {
            for (int i = -1; i <= 1; i++)
            {
                Vector3 offset = alongX ? new Vector3(i * width * 0.3f, 0f, 0f) : new Vector3(0f, 0f, i * width * 0.3f);
                Vector3 size = alongX ? new Vector3(0.3f, 2.4f, 0.12f) : new Vector3(0.12f, 2.4f, 0.3f);
                Box("Barricade_Plank", center + offset + Vector3.up * 1.2f, size, wood, SurfaceMaterialType.Wood);
            }
        }

        static void Entry(Vector3 pos)
        {
            var entry = new GameObject("ZombieEntry");
            entry.transform.SetParent(_root, false);
            entry.transform.position = pos;
            entry.AddComponent<ZombieEntryPoint>();
        }

        static void Stall(Vector3 pos, Color wood)
        {
            Box("Market_Stall", pos + new Vector3(0f, 0.5f, 0f), new Vector3(1.8f, 1f, 0.9f), wood, SurfaceMaterialType.Wood);
        }

        static void TeamCorner(Vector3 pos, Color color)
        {
            Box("TeamCorner", pos, new Vector3(3f, 0.04f, 3f), color, SurfaceMaterialType.Stone);
        }

        static void Cauldron(CauldronType type, Vector3 basePos, Color liquid)
        {
            var pot = Cyl($"Cauldron_{type}", basePos + new Vector3(0f, 0.5f, 0f), new Vector3(1.1f, 0.5f, 1.1f),
                new Color(0.18f, 0.18f, 0.2f), SurfaceMaterialType.Metal);
            pot.AddComponent<CauldronMarker>().Type = type;
            Cyl($"Cauldron_{type}_Brew", basePos + new Vector3(0f, 0.95f, 0f), new Vector3(0.85f, 0.04f, 0.85f),
                liquid, SurfaceMaterialType.Water);
            var glow = new GameObject($"Cauldron_{type}_Glow").AddComponent<Light>();
            glow.transform.SetParent(_root, false);
            glow.transform.position = basePos + new Vector3(0f, 1.4f, 0f);
            glow.type = LightType.Point;
            glow.color = liquid;
            glow.intensity = 1.1f;
            glow.range = 3.5f;
        }

        static void CardSpawn(Vector3 basePos, bool rare)
        {
            var pedestal = Box(rare ? "RareCardSpot" : "CardSpot", basePos + new Vector3(0f, 0.25f, 0f),
                new Vector3(0.45f, 0.5f, 0.45f), new Color(0.7f, 0.65f, 0.5f), SurfaceMaterialType.Stone);
            pedestal.AddComponent<RuneCardSpawnPoint>().Rare = rare;
        }

        static void Zone(string zoneName, Vector3 center, Vector3 size, float baselineTemp)
        {
            var zone = new GameObject($"Zone_{zoneName}");
            zone.transform.SetParent(_root, false);
            zone.transform.position = center;
            var col = zone.AddComponent<BoxCollider>();
            col.size = size;
            col.isTrigger = true;
            var vol = zone.AddComponent<ZoneVolume>();
            vol.ZoneName = zoneName;
            vol.BaselineTemperature = baselineTemp;
        }
    }
}
