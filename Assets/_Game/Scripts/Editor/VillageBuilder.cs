using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellyZombie
{
    /// Assembles the lobby village square from the Quaternius kits. Placement
    /// is bounds-driven (pieces measured at build time, dropped onto the ground).
    /// Every piece gets mesh colliders and a SurfaceMaterialTag; root: "SZ_Village".
    public static class VillageBuilder
    {
        const string VillageFbx = "Assets/Medieval Village MegaKit[Standard]/FBX/";
        const string PropsFbx = "Assets/Fantasy Props/Exports/FBX/";

        // Polytope nature prefab folders
        const string PTTrees = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/";
        const string PTPlants = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Plants/";
        const string PTFlowers = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Flowers/";
        const string PTShrubs = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Shrubs/";
        const string PTRocks = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/";
        const string PTMushrooms = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Mushrooms/";
        const string RootName = "SZ_Village";

        const float WallYawOffset = 0f;   // if every wall faces inward, set 180
        const float PlazaRadius = 13.5f;

        static readonly Dictionary<string, GameObject> _models = new Dictionary<string, GameObject>();
        static readonly Dictionary<Material, Material> _fixedMats = new Dictionary<Material, Material>();
        static readonly HashSet<string> _fromVillage = new HashSet<string>();
        static Transform _root;
        static float _wallW = 2f, _wallH = 3f;

        // both kits import ~1/100 scale; each pack gets a multiplier from a
        // probe piece. _wallYawAuto corrects the kit's wall axis convention.
        static float _villageFix = 1f, _propsFix = 1f, _wallYawAuto;

        public static float WallW => _wallW;
        public static float WallH => _wallH;
        /// The kit's measured wall-axis correction - external builders must
        /// compose it into wall yaws exactly like House() does.
        public static float WallYawAuto => _wallYawAuto;

        /// Point the placement toolkit at a root and measure the kit - the
        /// GameMapBuilder shares all of this (Place/House/lights/materials).
        public static void BeginPlacement(Transform root)
        {
            _models.Clear();
            _fixedMats.Clear();
            _fromVillage.Clear();
            _root = root;
            _villageFix = _propsFix = 1f;

            // scale anchor: a door wall must be person-height (~2.7m)
            var probe = Place("Wall_Plaster_Door_Round", new Vector3(0f, -50f, 0f), 0f);
            if (probe != null)
            {
                var raw = BoundsOf(probe).size;
                float maxDim = Mathf.Max(raw.x, Mathf.Max(raw.y, raw.z));
                float anchor = raw.y >= maxDim * 0.5f ? raw.y : maxDim; // trust Y if it's plausibly the height
                // walls are authored 2.00m wide × 3.12m tall
                if (anchor < 1.4f) _villageFix = 3.12f / Mathf.Max(0.001f, anchor);
                Object.DestroyImmediate(probe);

                var probe2 = Place("Wall_Plaster_Door_Round", new Vector3(0f, -50f, 0f), 0f);
                if (probe2 != null) // re-measure WITH the fix applied
                {
                    var s = BoundsOf(probe2).size;
                    _wallH = s.y;                     // authored 3.12 - exact stacking
                    _wallW = Mathf.Max(s.x, s.z);     // authored 2.00 - exact snapping
                    _wallYawAuto = s.z > s.x ? 90f : 0f; // authored: walls run along X
                    Debug.Log($"[SpellyZombie] wall probe (fixed): {s.x:0.00} × {s.y:0.00} × {s.z:0.00}, yawAuto {_wallYawAuto}°");
                    Object.DestroyImmediate(probe2);
                }
            }

            // probe the props kit: a barrel should be ~0.8m tall
            var keg = Place("Barrel", new Vector3(0f, -50f, 0f), 0f);
            if (keg != null)
            {
                var kb = BoundsOf(keg);
                float raw = Mathf.Max(0.001f, kb.size.y);
                if (raw < 0.3f) _propsFix = 0.8f / raw;
                Object.DestroyImmediate(keg);
            }

            Debug.Log($"[SpellyZombie] Kit scale probes: village ×{_villageFix:0.0}, props ×{_propsFix:0.0} " +
                      $"→ wall module {_wallW:0.00}×{_wallH:0.00}m");
        }

        static float KitFix(string model) =>
            _fromVillage.Contains(model) ? _villageFix : _propsFix;

        // the props kit has per-file export scales. Expected real-world size of
        // each piece's largest dimension; pieces more than 2× off after the kit
        // fix get normalized to this.
        static readonly Dictionary<string, float> ExpectedMax = new Dictionary<string, float>
        {
            { "Cauldron", 1.2f }, { "Bench", 1.6f }, { "Coin_Pile", 0.5f }, { "Coin_Pile_2", 0.5f },
            { "Stall_Empty", 2.6f }, { "Stall_Cart_Empty", 2.4f }, { "FarmCrate_Apple", 0.6f },
            { "FarmCrate_Carrot", 0.6f }, { "FarmCrate_Empty", 0.6f }, { "Barrel", 0.9f },
            { "Barrel_Apples", 0.9f }, { "Prop_Wagon", 3.2f }, { "Anvil_Log", 0.9f },
            { "Workbench", 1.6f }, { "WeaponStand", 1.6f }, { "Whetstone", 1.1f },
            { "Crate_Metal", 0.9f }, { "Table_Large", 2.2f }, { "BookGroup_Medium_1", 0.5f },
            { "Potion_1", 0.25f }, { "Potion_2", 0.25f }, { "CandleStick_Triple", 0.5f },
            { "BookStand", 1.3f }, { "Book_Stack_1", 0.35f }, { "Chest_Wood", 1.1f },
            { "Torch_Metal", 1.7f }, { "Lantern_Wall", 0.55f }, { "Banner_1", 1.6f },
            { "Chandelier", 1.5f }, { "Dummy", 1.7f }, { "Cage_Small", 1.0f },
            { "Bucket_Wooden_1", 0.5f }, { "Vase_2", 0.9f }, { "Vase_4", 0.6f },
            { "Pot_1", 0.6f }, { "Book_Stack_2", 0.35f }, { "CandleStick_Stand", 1.3f },
            { "Bed_Twin1", 2.2f }, { "Bed_Twin2", 2.2f }, { "Cabinet", 2.0f },
            { "Bookcase_2", 2.0f }, { "Nightstand_Shelf", 1.2f }, { "Stool", 0.6f },
            { "Axe_Bronze", 0.9f },
        };

        static void NormalizeSize(GameObject inst, string model)
        {
            if (!ExpectedMax.TryGetValue(model, out float target)) return;
            var b = BoundsOf(inst);
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (maxDim < 0.001f) return;
            float ratio = maxDim / target;
            if (ratio > 2f || ratio < 0.5f) // trust the table, not the file
                inst.transform.localScale *= target / maxDim;
        }

        // the helpers below exist for PrefabExporter

        /// A floating slide-weapon pickup (temp look, real mechanism).
        public static void PlaceWeaponPickup(Vector3 pos)
        {
            var weapon = SealWeapon.CreatePickup(pos);
            if (weapon != null) weapon.transform.SetParent(_root, true);
        }

        /// A complete module-built house: walls with windows/shutters/doors,
        /// corner trim, gable-capped roof, chimney, furnished interior with
        /// its own drawable floor, and ink canvases on every facade.
        public static void House(Vector3 center, float facingDeg, int wide, int deep, int floors, bool brick,
            bool furnish = true)
        {
            Quaternion rot = Quaternion.Euler(0f, facingDeg, 0f);
            Vector3 fwd = rot * Vector3.forward;
            Vector3 right = rot * Vector3.right;
            float halfW = wide * _wallW * 0.5f, halfD = deep * _wallW * 0.5f;

            string plain = brick ? "Wall_UnevenBrick_Straight" : "Wall_Plaster_Straight";
            string window = brick ? "Wall_UnevenBrick_Window_Wide_Flat" : "Wall_Plaster_Window_Wide_Flat";
            string door = brick ? "Wall_UnevenBrick_Door_Round" : "Wall_Plaster_Door_Round";
            var wallTag = brick ? SurfaceMaterialType.Stone : SurfaceMaterialType.Wood;

            float wallYaw = WallYawOffset + _wallYawAuto;
            for (int f = 0; f < floors; f++)
            {
                float y = f * _wallH;
                for (int i = 0; i < wide; i++) // front + back rows
                {
                    float off = (i - (wide - 1) * 0.5f) * _wallW;
                    string pickFront = f == 0 && i == wide / 2 ? door
                        : Random.value < 0.6f ? window : plain;
                    var frontWall = Place(pickFront, center + fwd * halfD + right * off + Vector3.up * y,
                        facingDeg + wallYaw, 1f, wallTag);
                    if (frontWall != null && pickFront.Contains("Window"))
                    {
                        PlacePivot("Window_Wide_Flat1", frontWall.transform.position,
                            frontWall.transform.rotation, SurfaceMaterialType.Wood);
                        if (Random.value < 0.55f)
                            PlacePivot("WindowShutters_Wide_Flat_Open", frontWall.transform.position,
                                frontWall.transform.rotation, SurfaceMaterialType.Wood);
                        if (f > 0 && Random.value < 0.45f)
                            PlacePivot("Balcony_Simple_Straight", frontWall.transform.position,
                                frontWall.transform.rotation, SurfaceMaterialType.Wood);
                    }
                    else if (frontWall != null && pickFront.Contains("Door_Round"))
                    {
                        PlaceWallDoorDetail(frontWall, facingDeg + wallYaw);
                    }
                    PlaceWallDetailed(Random.value < 0.3f ? window : plain,
                        center - fwd * halfD + right * off + Vector3.up * y,
                        facingDeg + 180f + wallYaw, wallTag);
                }
                for (int i = 0; i < deep; i++) // side rows
                {
                    float off = (i - (deep - 1) * 0.5f) * _wallW;
                    PlaceWallDetailed(Random.value < 0.35f ? window : plain,
                        center + right * halfW + fwd * off + Vector3.up * y,
                        facingDeg + 90f + wallYaw, wallTag);
                    PlaceWallDetailed(Random.value < 0.35f ? window : plain,
                        center - right * halfW + fwd * off + Vector3.up * y,
                        facingDeg - 90f + wallYaw, wallTag);
                }
            }

            if (Random.value < 0.7f)
                PlaceFloat("Prop_Chimney",
                    center + right * (halfW * 0.45f) + Vector3.up * (floors * _wallH + 2.3f),
                    facingDeg, 1f, SurfaceMaterialType.Stone);

            // gable caps close the triangular void between wall top and roof ridge
            float topY = floors * _wallH;
            Place("Roof_Front_Brick4", center + right * halfW + Vector3.up * topY,
                facingDeg + 90f, 1f, SurfaceMaterialType.Stone);
            Place("Roof_Front_Brick4", center - right * halfW + Vector3.up * topY,
                facingDeg - 90f, 1f, SurfaceMaterialType.Stone);

            if (furnish) Interior(center, fwd, right, wide, deep, facingDeg);

            // one seamless drawing plane per facade
            float canvasH = floors * _wallH;
            AddCanvas(center + fwd * (halfD + 0.24f), facingDeg, wide * _wallW, canvasH, wallTag);
            AddCanvas(center - fwd * (halfD + 0.24f), facingDeg + 180f, wide * _wallW, canvasH, wallTag);
            AddCanvas(center + right * (halfW + 0.24f), facingDeg + 90f, deep * _wallW, canvasH, wallTag);
            AddCanvas(center - right * (halfW + 0.24f), facingDeg - 90f, deep * _wallW, canvasH, wallTag);

            // corner trim columns hide the vertical seams where rows meet
            string cornerPiece = brick ? "Corner_Exterior_Brick" : "Corner_Exterior_Wood";
            for (int f = 0; f < floors; f++)
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cz = -1; cz <= 1; cz += 2)
                        Place(cornerPiece,
                            center + right * (halfW * cx) + fwd * (halfD * cz) + Vector3.up * (f * _wallH),
                            facingDeg, 1f, wallTag);

            Roof(center, facingDeg, wide * _wallW, deep * _wallW, floors * _wallH);

            PlaceFloat("Lantern_Wall", center + fwd * (halfD + 0.12f) + right * (_wallW * 0.7f)
                + Vector3.up * 2.1f, facingDeg, 1f, SurfaceMaterialType.Metal, warmLight: true);
            if (Random.value < 0.6f)
                PlaceFloat("Banner_1", center + fwd * (halfD + 0.15f) + Vector3.up * (floors * _wallH - 0.4f),
                    facingDeg, 1f, SurfaceMaterialType.Wood);
            if (Random.value < 0.8f)
                PlaceFloat("Prop_Vine" + (Random.value < 0.5f ? "1" : "5"),
                    center + fwd * (halfD + 0.06f) - right * (_wallW * 0.8f) + Vector3.up * 1.4f,
                    facingDeg, 1f, SurfaceMaterialType.Wood);

            if (Random.value < 0.85f)
            {
                Vector3 stoop = center + fwd * (halfD + 0.9f) + right * (_wallW * 1.1f);
                Place(Random.value < 0.5f ? "Barrel" : "Crate_Wooden", stoop,
                    Random.Range(0f, 360f), 1f, SurfaceMaterialType.Wood);
                Place(Random.value < 0.5f ? "Bucket_Wooden_1" : "Pot_1",
                    stoop + new Vector3(Random.Range(-0.7f, 0.7f), 0f, Random.Range(-0.7f, 0.7f)),
                    Random.Range(0f, 360f), 1f, SurfaceMaterialType.Wood);
            }
            PlacePT(PTPlants + "PT_Grass_02.prefab",
                center + fwd * (halfD + 0.4f) - right * (halfW - 0.3f),
                Random.Range(0f, 360f), Random.Range(0.7f, 1.1f),
                SurfaceMaterialType.Unknown, keepColliders: false);
        }

        /// Furnishes a house interior: plank floor visuals over one solid
        /// walkable collider, furniture, and candlelight.
        static void Interior(Vector3 center, Vector3 fwd, Vector3 right, int wide, int deep, float facingDeg)
        {
            float halfW = wide * _wallW * 0.5f, halfD = deep * _wallW * 0.5f;

            for (int ix = 0; ix < wide; ix++)
                for (int iz = 0; iz < deep; iz++)
                {
                    var plank = Place("Floor_WoodDark",
                        center + right * ((ix - (wide - 1) * 0.5f) * _wallW)
                               + fwd * ((iz - (deep - 1) * 0.5f) * _wallW) + Vector3.up * 0.03f,
                        facingDeg, 1f, SurfaceMaterialType.Wood);
                    StripColliders(plank);
                }
            var floor = new GameObject("HouseFloor");
            floor.transform.SetParent(_root, false);
            floor.transform.rotation = Quaternion.Euler(0f, facingDeg, 0f);
            floor.transform.position = center + Vector3.up * 0.045f;
            var floorBox = floor.AddComponent<BoxCollider>();
            floorBox.size = new Vector3(wide * _wallW - 0.3f, 0.05f, deep * _wallW - 0.3f);
            floor.AddComponent<SurfaceMaterialTag>().Material = SurfaceMaterialType.Wood;

            // furnishings hug the walls; the doorway (front middle) stays clear
            Place(Random.value < 0.5f ? "Bed_Twin1" : "Bed_Twin2",
                center - fwd * (halfD - 1.0f)
                    + right * Random.Range(-(halfW - 1.2f), halfW - 1.2f),
                facingDeg, 1f, SurfaceMaterialType.Wood);
            Place(Random.value < 0.5f ? "Cabinet" : "Bookcase_2",
                center + right * (halfW - 0.75f) + fwd * Random.Range(-0.4f, halfD - 1.2f),
                facingDeg - 90f, 1f, SurfaceMaterialType.Wood);
            if (wide >= 4)
            {
                Vector3 tableAt = center - right * (halfW - 1.6f) + fwd * 0.3f;
                Place("Table_Large", tableAt, facingDeg + 90f, 1f, SurfaceMaterialType.Wood);
                Place("Stool", tableAt + fwd * 1.3f, Random.Range(0f, 360f), 1f, SurfaceMaterialType.Wood);
            }
            else
                Place("Nightstand_Shelf", center - right * (halfW - 0.7f),
                    facingDeg + 90f, 1f, SurfaceMaterialType.Wood);
            Place("Chest_Wood",
                center - fwd * (halfD - 0.8f) - right * (halfW - 0.9f),
                facingDeg + Random.Range(-20f, 20f), 1f, SurfaceMaterialType.Wood);
            var candle = Place("CandleStick_Stand",
                center + fwd * (halfD * 0.25f) + right * (halfW * 0.3f),
                0f, 1f, SurfaceMaterialType.Metal);
            AddWarmLight(candle, 1.1f, 5f);
        }

        /// A wall plus its insert (window glass/shutters or door leaf). Inserts
        /// sit at the wall's own pivot with its exact rotation; the kit's pieces
        /// are authored to slot together that way.
        static void PlaceWallDetailed(string wallPiece, Vector3 groundPos, float yaw, SurfaceMaterialType tag)
        {
            var wall = Place(wallPiece, groundPos, yaw, 1f, tag);
            if (wall == null) return;

            if (wallPiece.Contains("Window_Wide_Flat"))
            {
                PlacePivot("Window_Wide_Flat1", wall.transform.position, wall.transform.rotation,
                    SurfaceMaterialType.Wood);
                if (Random.value < 0.55f)
                    PlacePivot("WindowShutters_Wide_Flat_Open", wall.transform.position,
                        wall.transform.rotation, SurfaceMaterialType.Wood);
            }
            else if (wallPiece.Contains("Door_Round"))
            {
                PlaceWallDoorDetail(wall, yaw);
            }
        }

        /// Door frame + a breakable leaf, bounds-centered in the doorway and
        /// widened to fill the 1.4m arch (local X is world-horizontal here, so
        /// the stretch cannot shear).
        static void PlaceWallDoorDetail(GameObject wall, float yaw)
        {
            PlacePivot("DoorFrame_Round_WoodDark", wall.transform.position,
                wall.transform.rotation, SurfaceMaterialType.Wood);
            var leaf = Place("Door_1_Round", wall.transform.position, yaw,
                1f, SurfaceMaterialType.Wood);
            if (leaf != null)
            {
                var stretch = leaf.transform.localScale;
                stretch.x *= 1.28f;
                leaf.transform.localScale = stretch;
                var lb = BoundsOf(leaf);
                leaf.transform.position += new Vector3(
                    wall.transform.position.x - lb.center.x, 0f,
                    wall.transform.position.z - lb.center.z);
                MakeBreakable(leaf, 45f);
                leaf.AddComponent<DoorInteract>();
            }
        }

        /// Kinematic rigidbody + health; heat can ignite it, impacts crush it.
        public static void MakeBreakable(GameObject go, float health)
        {
            if (go == null || go.GetComponent<Rigidbody>() != null) return; // once only
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<Element>().Health = health;
            go.AddComponent<Breakable>();
        }

        public static void StripColliders(GameObject inst)
        {
            if (inst == null) return;
            foreach (var col in inst.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(col);
        }

        /// Invisible facade-sized drawing plane on the ink-canvas layer so
        /// strokes don't split at wall-module seams; players and particles pass
        /// through to the real geometry.
        public static void AddCanvas(Vector3 basePos, float yaw, float width, float height, SurfaceMaterialType tag)
        {
            var canvas = new GameObject("WallCanvas") { layer = InkCanvasLayer.Layer };
            canvas.transform.SetParent(_root, false);
            canvas.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            canvas.transform.position = basePos + Vector3.up * (height * 0.5f);
            var box = canvas.AddComponent<BoxCollider>();
            box.size = new Vector3(width, height, 0.04f);
            canvas.AddComponent<SurfaceMaterialTag>().Material = tag;
        }

        /// Exact-pivot placement for kit pieces that slot into other pieces
        /// (window inserts, door leaves) - no bounds-dropping.
        public static GameObject PlacePivot(string model, Vector3 pivotPos, Quaternion rotation, SurfaceMaterialType tag)
        {
            var prefab = Load(model);
            if (prefab == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _root);
            inst.transform.rotation = rotation; // the host wall's composed rotation
            inst.transform.localScale = Vector3.one * KitFix(model);
            inst.transform.position = pivotPos;
            Prepare(inst, tag);
            return inst;
        }

        public static void Roof(Vector3 center, float facingDeg, float footW, float footD, float topY)
        {
            // "Roof_RoundTiles_AxB" covers exactly A×B meters (~0.78m eaves all
            // around), pivot centered, base 0.52 below the pivot. Pick the
            // smallest listed roof that covers the footprint; never scale.
            int w = Mathf.RoundToInt(footW), d = Mathf.RoundToInt(footD);
            int lo = Mathf.Min(w, d), hi = Mathf.Max(w, d);
            Vector2Int[] roofSizes = // (A, B) as named - A is the X extent
            {
                new Vector2Int(4, 4), new Vector2Int(4, 6), new Vector2Int(4, 8),
                new Vector2Int(6, 4), new Vector2Int(6, 6), new Vector2Int(6, 8),
                new Vector2Int(6, 10), new Vector2Int(6, 12), new Vector2Int(6, 14),
                new Vector2Int(8, 8), new Vector2Int(8, 10), new Vector2Int(8, 12),
                new Vector2Int(8, 14),
            };
            Vector2Int pick = new Vector2Int(8, 14); // worst case: the biggest
            int bestArea = int.MaxValue;
            foreach (var s in roofSizes)
            {
                int sLo = Mathf.Min(s.x, s.y), sHi = Mathf.Max(s.x, s.y);
                if (sLo < lo || sHi < hi) continue; // doesn't cover
                if (s.x * s.y < bestArea) { bestArea = s.x * s.y; pick = s; }
            }
            var prefab = Load($"Roof_RoundTiles_{pick.x}x{pick.y}");
            if (prefab == null) return;
            // rotate so the name's X-extent lands on the matching house axis
            bool coverXisD = (pick.x <= pick.y) == (d <= w);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _root);
            inst.transform.localScale = Vector3.one * _villageFix;
            // yaw=facing maps roof local X onto the house width; if the name's
            // X-cover is the depth, turn 90°
            float yaw = coverXisD ? facingDeg + 90f : facingDeg;
            inst.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * inst.transform.rotation;
            inst.transform.position = center + Vector3.up * topY; // pivot AT wall top
            Prepare(inst, SurfaceMaterialType.Stone);
        }

        public static void NatureCluster(Vector3 at)
        {
            PlacePT(PTPlants + "PT_Grass_02.prefab", at, Random.Range(0f, 360f),
                Random.Range(1.1f, 1.5f), SurfaceMaterialType.Unknown, keepColliders: false);
            int minis = Random.Range(2, 5);
            for (int i = 0; i < minis; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(0.5f, 1.3f);
                PlacePT(PTPlants + "PT_Grass_02.prefab",
                    at + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist),
                    Random.Range(0f, 360f), Random.Range(0.5f, 0.9f),
                    SurfaceMaterialType.Unknown, keepColliders: false);
            }
            if (Random.value < 0.35f)
                PlacePT(PTFlowers + "PT_Poppy_02.prefab",
                    at + new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f)),
                    Random.Range(0f, 360f), 1f, SurfaceMaterialType.Unknown, keepColliders: false);
            if (Random.value < 0.18f)
                PlacePT(PTShrubs + "PT_Generic_Shrub_01_green.prefab",
                    at + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)),
                    Random.Range(0f, 360f), Random.Range(0.8f, 1.1f), SurfaceMaterialType.Wood);
        }

        /// Polytope prefabs come correctly scaled with their own materials -
        /// instantiate, bounds-drop, tag. No kit fixes, no material surgery.
        public static GameObject PlacePT(string path, Vector3 groundPos, float yRot, float scale,
            SurfaceMaterialType tag, bool keepColliders = true)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[SpellyZombie] PT prefab missing: {path}");
                return null;
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _root);
            inst.transform.rotation = Quaternion.Euler(0f, yRot, 0f) * inst.transform.rotation;
            inst.transform.localScale *= scale;
            var b = BoundsOf(inst);
            inst.transform.position += groundPos - new Vector3(b.center.x, b.min.y, b.center.z);
            if (keepColliders) EnsureColliders(inst);
            else StripColliders(inst);
            if (tag != SurfaceMaterialType.Unknown && inst.GetComponent<SurfaceMaterialTag>() == null)
                inst.AddComponent<SurfaceMaterialTag>().Material = tag;
            return inst;
        }

        // ---------------------------------------------------------- helpers --
        static GameObject Load(string model)
        {
            if (_models.TryGetValue(model, out var cached)) return cached;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VillageFbx + model + ".fbx");
            if (prefab != null) _fromVillage.Add(model);
            else prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropsFbx + model + ".fbx");
            if (prefab == null)
                Debug.LogWarning($"[SpellyZombie] Village piece not found: {model}");
            _models[model] = prefab;
            return prefab;
        }

        /// Instantiate + rotate + scale, then drop so the render bounds'
        /// BOTTOM-CENTER sits exactly on groundPos (pivot-agnostic placement).
        public static GameObject Place(string model, Vector3 groundPos, float yRot,
            float scale = 1f, SurfaceMaterialType tag = SurfaceMaterialType.Unknown)
        {
            var prefab = Load(model);
            if (prefab == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _root);
            // compose the yaw on top of the import rotation: Blender FBX stands
            // up via a baked -90° root rotation; overwriting it lays pieces flat
            inst.transform.rotation = Quaternion.Euler(0f, yRot, 0f) * inst.transform.rotation;
            inst.transform.localScale = Vector3.one * (scale * KitFix(model));
            NormalizeSize(inst, model);
            var b = BoundsOf(inst);
            inst.transform.position += groundPos - new Vector3(b.center.x, b.min.y, b.center.z);
            Prepare(inst, tag);
            return inst;
        }

        /// Like Place but centers the bounds ON the position (wall-mounts).
        public static GameObject PlaceFloat(string model, Vector3 centerPos, float yRot,
            float scale, SurfaceMaterialType tag, bool warmLight = false)
        {
            var prefab = Load(model);
            if (prefab == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _root);
            inst.transform.rotation = Quaternion.Euler(0f, yRot, 0f) * inst.transform.rotation;
            inst.transform.localScale = Vector3.one * (scale * KitFix(model));
            NormalizeSize(inst, model);
            var b = BoundsOf(inst);
            inst.transform.position += centerPos - b.center;
            Prepare(inst, tag);
            if (warmLight) AddWarmLight(inst, 1.2f, 5f);
            return inst;
        }

        static readonly string[] StructuralPrefixes =
            { "Wall_", "Roof_", "Floor_", "Corner_", "Overhang_", "Stair", "Balcony", "DoorFrame", "HoleCover" };

        static bool IsStructural(string pieceName)
        {
            foreach (var prefix in StructuralPrefixes)
                if (pieceName.StartsWith(prefix)) return true;
            return false;
        }

        static void Prepare(GameObject inst, SurfaceMaterialType tag)
        {
            FixMaterials(inst);
            EnsureColliders(inst);
            if (tag != SurfaceMaterialType.Unknown && inst.GetComponent<SurfaceMaterialTag>() == null)
                inst.AddComponent<SurfaceMaterialTag>().Material = tag;

            // wooden props are breakable; structure (walls/roofs/floors) is not
            if (tag == SurfaceMaterialType.Wood && !IsStructural(inst.name))
            {
                var b = BoundsOf(inst);
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                MakeBreakable(inst, Mathf.Clamp(15f + maxDim * 18f, 20f, 70f));
            }
        }

        public static void AddWarmLight(GameObject holder, float intensity, float range)
        {
            if (holder == null) return;
            var flame = new GameObject("Flame");
            flame.transform.SetParent(holder.transform, false);
            var hb = BoundsOf(holder);
            flame.transform.position = new Vector3(hb.center.x, hb.max.y - 0.1f, hb.center.z);
            var warmth = flame.AddComponent<Light>();
            warmth.type = LightType.Point;
            warmth.color = new Color(1f, 0.72f, 0.42f);
            warmth.intensity = intensity;
            warmth.range = range;
        }

        public static Bounds BoundsOf(GameObject inst)
        {
            var rends = inst.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(inst.transform.position, Vector3.one);
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        /// Every surface must be drawable: mesh colliders on every mesh.
        static void EnsureColliders(GameObject inst)
        {
            foreach (var filter in inst.GetComponentsInChildren<MeshFilter>())
                if (filter.GetComponent<Collider>() == null && filter.sharedMesh != null)
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }

        /// Keep the packs' own look, guarantee URP + hooked-up textures:
        /// each source material gets one cached URP Lit twin (same texture,
        /// same tint, matte) - fixes pink Standard imports and missing atlases.
        static void FixMaterials(GameObject inst)
        {
            foreach (var rend in inst.GetComponentsInChildren<Renderer>())
            {
                var mats = rend.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var fix = FixedMaterial(mats[i]);
                    if (fix != mats[i]) { mats[i] = fix; changed = true; }
                }
                if (changed) rend.sharedMaterials = mats;
            }
        }

        static Material FixedMaterial(Material src)
        {
            if (_fixedMats.TryGetValue(src, out var done) && done != null) return done;

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(lit) { name = src.name + "_SZ" };
            Texture tex = null;
            if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
            if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
            if (tex == null) tex = FindBaseColorTexture(src.name);
            Color tint = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                : src.HasProperty("_Color") ? src.color : Color.white;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", 0.08f); // matte village, no plastic shine
            _fixedMats[src] = m;
            return m;
        }

        static Texture FindBaseColorTexture(string matName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:Texture2D T_{matName}_BaseColor"))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (string guid in AssetDatabase.FindAssets($"t:Texture2D {matName}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Textures")) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }
    }
}
