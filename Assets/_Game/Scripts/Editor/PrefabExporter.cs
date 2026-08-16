using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Exports the map-building vocabulary as PREFABS into _Game/Prefabs so
    /// composes the world BY HAND - houses, towers, sky islands, props,
    /// story clusters. Each prefab is fully wired: colliders, chemistry tags,
    /// ink canvases, breakables, lights. Drag, place, done.
    public static class PrefabExporter
    {
        const string Dir = "Assets/_Game/Prefabs";

        // AXIOM : these are prefabs OWNS and hand-edits. The
        // export used to replace all 20 at fixed paths with no dialog, no
        // backup and no undo. Default is now "only create the missing ones".
        static bool _replaceExisting;
        static readonly System.Collections.Generic.List<string> _kept = new System.Collections.Generic.List<string>();
        static readonly System.Collections.Generic.List<string> _wrote = new System.Collections.Generic.List<string>();

        [MenuItem("Spelly Zombie/Export Prefabs (_Game/Prefabs)")]
        public static void Export()
        {
            System.IO.Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();

            // 0 = Only create missing · 1 = Cancel · 2 = Replace everything
            int choice = EditorUtility.DisplayDialogComplex("Export Prefabs",
                $"{Dir} may already hold prefabs you have edited by hand.\n\n" +
                "\"Only create missing\" keeps every existing prefab exactly as it is.\n" +
                "\"Replace everything\" overwrites them with freshly generated versions (backups are written first).",
                "Only create missing", "Cancel", "Replace everything");
            if (choice == 1) return;
            _replaceExisting = choice == 2;
            _kept.Clear();
            _wrote.Clear();

            EnvironmentTools.WireFxLibrary();
            var randomState = Random.state;
            Random.InitState(5);

            Save("SZ_House_Plaster_Small", () => VillageBuilder.House(Vector3.zero, 0f, 3, 2, 1, false));
            Save("SZ_House_Brick_Tall", () => VillageBuilder.House(Vector3.zero, 0f, 3, 2, 2, true));
            Save("SZ_House_Plaster_Wide", () => VillageBuilder.House(Vector3.zero, 0f, 4, 2, 2, false));
            Save("SZ_House_Block_Large", () => VillageBuilder.House(Vector3.zero, 0f, 6, 3, 2, true));
            Save("SZ_GreatHall", () => VillageBuilder.House(Vector3.zero, 0f, 7, 4, 2, false, furnish: false));
            Save("SZ_Watchtower", () => GameMapBuilder.Watchtower(Vector3.zero, 0f));
            Save("SZ_SkyIsland", () => GameMapBuilder.SkyIsland(new Vector3(0f, 0.6f, 0f), 3f, 0));
            Save("SZ_CrateStack", () => GameMapBuilder.CrateStack(Vector3.zero));
            Save("SZ_Gravestone", () => GameMapBuilder.Gravestone(Vector3.zero, 0f));
            Save("SZ_WeaponPickup", () => VillageBuilder.PlaceWeaponPickup(Vector3.zero));
            Save("SZ_WeaponPickup_Chamber", () =>
            {
                var chamber = RuneChamberWeapon.CreatePickup(Vector3.zero);
                chamber.transform.SetParent(GameObject.Find("__export").transform, true);
            });
            Save("SZ_Torch_Lit", () =>
            {
                var torch = VillageBuilder.Place("Torch_Metal", Vector3.zero, 0f, 1.15f, SurfaceMaterialType.Metal);
                VillageBuilder.AddWarmLight(torch, 1.6f, 7f);
            });
            Save("SZ_FenceRun", () =>
            {
                for (int i = -2; i <= 2; i++)
                    VillageBuilder.Place("Prop_WoodenFence_Extension1",
                        new Vector3(i * 2.02f, 0f, 0f), 0f, 1f, SurfaceMaterialType.Wood);
            });
            Save("SZ_MarketStall", () =>
            {
                VillageBuilder.Place("Stall_Empty", Vector3.zero, 0f, 1f, SurfaceMaterialType.Wood);
                VillageBuilder.Place("FarmCrate_Apple", new Vector3(1.6f, 0f, 0.4f), 20f, 1f, SurfaceMaterialType.Wood);
                VillageBuilder.Place("Barrel", new Vector3(-1.5f, 0f, 0.5f), 0f, 1f, SurfaceMaterialType.Wood);
                VillageBuilder.Place("Bucket_Wooden_1", new Vector3(1.1f, 0f, -0.7f), 0f, 1f, SurfaceMaterialType.Wood);
            });
            Save("SZ_TreeCluster", () =>
            {
                var tree = VillageBuilder.PlacePT(
                    "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Fruit_Tree_01_green.prefab",
                    Vector3.zero, 0f, 1.1f, SurfaceMaterialType.Wood);
                if (tree != null) VillageBuilder.MakeBreakable(tree, 60f);
                VillageBuilder.NatureCluster(new Vector3(1.4f, 0f, 0.6f));
            });
            Save("SZ_Cauldron_Glow", () =>
            {
                var pot = VillageBuilder.Place("Cauldron", Vector3.zero, 0f, 1.35f, SurfaceMaterialType.Metal);
                if (pot == null) return;
                var brew = new GameObject("BrewGlow");
                brew.transform.SetParent(pot.transform, false);
                brew.transform.position = VillageBuilder.BoundsOf(pot).center + Vector3.up * 0.5f;
                var glow = brew.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = new Color(0.5f, 1f, 0.6f);
                glow.intensity = 1.4f;
                glow.range = 5f;
                // add a CauldronMarker in the inspector to make it a perk pot
            });
            Save("SZ_RuinWall", () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    var chunk = VillageBuilder.Place(
                        i == 1 ? "Corner_Exterior_Brick" : "Wall_UnevenBrick_Straight",
                        new Vector3(i * 2.1f - 2.1f, -0.45f - i * 0.15f, i * 0.4f),
                        Random.Range(-10f, 10f), 1f, SurfaceMaterialType.Stone);
                    if (chunk != null)
                        chunk.transform.Rotate(Random.Range(-9f, 9f), 0f, Random.Range(-9f, 9f), Space.World);
                }
                VillageBuilder.NatureCluster(new Vector3(1f, 0f, -1f));
            });
            Save("SZ_ZombieEntry", () =>
            {
                var e = new GameObject("ZombieEntry");
                e.transform.SetParent(GameObject.Find("__export").transform, false);
                e.AddComponent<ZombieEntryPoint>();
                // a rock pile so the entry reads as a place in the world
                VillageBuilder.PlacePT(
                    "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/PT_River_Rock_Pile_02.prefab",
                    new Vector3(1.2f, 0f, 0.5f), 0f, 1.1f, SurfaceMaterialType.Stone);
            });
            Save("SZ_CardSpot", () =>
            {
                var pedestal = VillageBuilder.Place("Barrel", Vector3.zero, 0f, 1f, SurfaceMaterialType.Wood);
                if (pedestal != null) pedestal.AddComponent<RuneCardSpawnPoint>();
            });
            Save("SZ_SafetyNet", () =>
            {
                // drop ONE under any map, low (y ≈ -12): players who fall off
                // teleport home, escaped junk gets deleted
                var net = new GameObject("SafetyNet");
                net.transform.SetParent(GameObject.Find("__export").transform, false);
                var trigger = net.AddComponent<BoxCollider>();
                trigger.size = new Vector3(300f, 4f, 300f);
                trigger.isTrigger = true;
                net.AddComponent<FallCatcher>();
            });

            Random.state = randomState;
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpellyZombie] Prefab export: wrote {_wrote.Count}, " +
                      $"KEPT YOUR VERSION of {_kept.Count}" +
                      (_kept.Count > 0 ? ". Code changes did NOT reach these:\n  " + string.Join("\n  ", _kept) : "."));
        }

        static void Save(string name, System.Action build)
        {
            // check the PATH FIRST - the old order built the whole hierarchy
            // and then threw it away, and overwrote the prefab regardless
            string path = $"{Dir}/{name}.prefab";
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            if (exists && !_replaceExisting) { _kept.Add(name); return; }
            if (exists)
            {
                System.IO.Directory.CreateDirectory($"{Dir}/_backup");
                AssetDatabase.CopyAsset(path, AssetDatabase.GenerateUniqueAssetPath(
                    $"{Dir}/_backup/{name}_{System.DateTime.Now:yyyyMMdd_HHmm}.prefab"));
            }

            var root = new GameObject("__export");
            VillageBuilder.BeginPlacement(root.transform);
            GameMapBuilder.SetRoot(root.transform);
            build();
            root.name = name;
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            if (!ok) Debug.LogError($"[SpellyZombie] FAILED to write {path}. Your existing prefab is untouched.");
            else _wrote.Add(name);
            Object.DestroyImmediate(root);
        }
    }
}
