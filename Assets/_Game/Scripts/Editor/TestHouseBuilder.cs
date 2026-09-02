using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Assembles ONE house from the Medieval Village MegaKit in the open
    /// scene: floor tiles, wall ring with a door wall and window walls,
    /// corners, the closest-sized one-piece roof, a chimney. Everything is
    /// placed by measured bounds, grouped under a bottom-pivot root, and
    /// selected, ready for a hand pass and Alt+Shift+P.
    public static class TestHouseBuilder
    {
        const string Kit = "Assets/Medieval Village MegaKit[Standard]/FBX/";

        [MenuItem("Spelly Zombie/Scenes/Build TEST House (open scene)")]
        static void Build()
        {
            var missing = new List<string>();
            GameObject Load(string name)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}{name}.fbx");
                if (go == null) missing.Add(name);
                return go;
            }

            var wallP = Load("Wall_Plaster_Straight_Base");
            var wallWin = Load("Wall_Plaster_Window_Wide_Flat");
            var wallDoor = Load("Wall_Plaster_Door_Round");
            var door = Load("Door_1_Round");
            var corner = Load("Corner_Exterior_Wood");
            var floor = Load("Floor_WoodDark");
            var chimney = Load("Prop_Chimney");
            string[] roofNames = { "Roof_RoundTiles_4x4", "Roof_RoundTiles_4x6", "Roof_RoundTiles_4x8" };
            var roofs = new List<GameObject>();
            foreach (var rn in roofNames) { var r = Load(rn); if (r != null) roofs.Add(r); }
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog("Build TEST House",
                    "Missing kit pieces:\n" + string.Join("\n", missing), "OK");
                return;
            }

            Undo.SetCurrentGroupName("Build TEST House");
            int undoGroup = Undo.GetCurrentGroup();

            var root = new GameObject("TestHouse");
            Undo.RegisterCreatedObjectUndo(root, "house");
            var parts = new List<GameObject>();

            GameObject Place(GameObject prefab, float yaw)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(go, "part");
                // the kit imports Z up in centimetres: X -90 stands it up,
                // 100 makes metres, yaw rides Z
                go.transform.rotation = Quaternion.Euler(-90f, 0f, yaw);
                go.transform.localScale = Vector3.one * 100f;
                go.transform.SetParent(root.transform, true);
                parts.Add(go);
                return go;
            }

            Bounds BoundsOf(GameObject go)
            {
                var rends = go.GetComponentsInChildren<Renderer>(true);
                var b = new Bounds(go.transform.position, Vector3.zero);
                bool any = false;
                foreach (var r in rends)
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                return b;
            }

            // move so the piece's bounds land where asked
            void PutBounds(GameObject go, System.Func<Bounds, Vector3> offsetFrom)
            {
                var b = BoundsOf(go);
                go.transform.position += offsetFrom(b);
            }

            // measure one wall to learn the ring size
            var probe = Place(wallP, 0f);
            var wb = BoundsOf(probe);
            float wallLen = Mathf.Max(wb.size.x, wb.size.z);
            float wallTopY = wb.size.y;
            bool longIsX = wb.size.x >= wb.size.z;
            float baseYaw = longIsX ? 0f : 90f;   // long axis along X after this
            Object.DestroyImmediate(probe);
            parts.Clear();

            float half = wallLen;                  // two segments per side
            float N = half, S = -half, E = half, W = -half;

            // floor: tile a square that covers the ring
            var ftProbe = Place(floor, 0f);
            var fb = BoundsOf(ftProbe);
            float tile = Mathf.Max(fb.size.x, fb.size.z);
            Object.DestroyImmediate(ftProbe);
            parts.Clear();
            int tiles = Mathf.Max(1, Mathf.CeilToInt(wallLen * 2f / tile));
            float floorSpan = tiles * tile;
            for (int ix = 0; ix < tiles; ix++)
                for (int iz = 0; iz < tiles; iz++)
                {
                    var t = Place(floor, 0f);
                    float cx = -floorSpan * 0.5f + tile * (ix + 0.5f);
                    float cz = -floorSpan * 0.5f + tile * (iz + 0.5f);
                    PutBounds(t, b => new Vector3(cx - b.center.x, -b.min.y, cz - b.center.z));
                }
            float floorTop = 0f;
            foreach (var p in parts) floorTop = Mathf.Max(floorTop, BoundsOf(p).max.y);

            // wall ring: front = door + window, back = plain + plain,
            // sides = window + plain. Yaw turns the long axis to the side.
            void Wall(GameObject prefab, float yaw, Vector3 at)
            {
                var go = Place(prefab, yaw);
                PutBounds(go, b => new Vector3(at.x - b.center.x, floorTop - b.min.y, at.z - b.center.z));
            }
            float off = wallLen * 0.5f;
            Wall(wallDoor, baseYaw, new Vector3(-off, 0f, S));
            Wall(wallWin, baseYaw, new Vector3(off, 0f, S));
            Wall(wallP, baseYaw, new Vector3(-off, 0f, N));
            Wall(wallP, baseYaw, new Vector3(off, 0f, N));
            Wall(wallWin, baseYaw + 90f, new Vector3(W, 0f, -off));
            Wall(wallP, baseYaw + 90f, new Vector3(W, 0f, off));
            Wall(wallP, baseYaw + 90f, new Vector3(E, 0f, -off));
            Wall(wallWin, baseYaw + 90f, new Vector3(E, 0f, off));

            // corners, one per turn
            Wall(corner, 0f, new Vector3(W, 0f, S));
            Wall(corner, 90f, new Vector3(W, 0f, N));
            Wall(corner, 180f, new Vector3(E, 0f, N));
            Wall(corner, 270f, new Vector3(E, 0f, S));

            // the door leaf inside the door wall opening
            var leaf = Place(door, baseYaw);
            PutBounds(leaf, b => new Vector3(-off - b.center.x, floorTop - b.min.y, S - b.center.z));

            // roof: the variant whose footprint is closest to the ring
            GameObject bestRoof = null;
            float bestGap = float.MaxValue;
            foreach (var r in roofs)
            {
                var tmp = Place(r, 0f);
                var rb = BoundsOf(tmp);
                float gap = Mathf.Abs(Mathf.Max(rb.size.x, rb.size.z) - wallLen * 2f)
                          + Mathf.Abs(Mathf.Min(rb.size.x, rb.size.z) - wallLen * 2f);
                Object.DestroyImmediate(tmp);
                parts.RemoveAt(parts.Count - 1);
                if (gap < bestGap) { bestGap = gap; bestRoof = r; }
            }
            float wallTop = floorTop + wallTopY;
            var roof = Place(bestRoof, 0f);
            PutBounds(roof, b => new Vector3(-b.center.x, wallTop - 0.05f - b.min.y, -b.center.z));

            // chimney on the back half of the roof
            var ch = Place(chimney, 0f);
            var roofB = BoundsOf(roof);
            PutBounds(ch, b => new Vector3(off * 0.5f - b.center.x,
                roofB.center.y - b.min.y, N * 0.5f - b.center.z));

            // bottom-pivot root: detach, move the pivot, reattach in place
            var all = BoundsOf(root);
            foreach (var p in parts) if (p != null) p.transform.SetParent(null, true);
            root.transform.position = new Vector3(all.center.x, all.min.y, all.center.z);
            foreach (var p in parts) if (p != null) p.transform.SetParent(root.transform, true);
            Selection.activeGameObject = root;

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[SpellyZombie] TestHouse built: {parts.Count} parts, roof {bestRoof.name}. "
                + "Likely hand fixes: corner facing, door leaf fit, chimney seat. "
                + "Then Alt+Shift+P makes the prefab.");
        }
    }
}
