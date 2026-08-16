using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// "Spelly Zombie → Group House Parts": gathers every piece belonging to
    /// a house under a "House 1", "House 2", … root so can drag each
    /// straight to the Project window as a prefab.
    ///
    /// How it decides what belongs: every house has exactly one HouseFloor
    /// (the drawable floor the builder made) - its footprint is the house's
    /// ground truth. Structural pieces (walls, roofs, windows, shutters,
    /// balconies, chimneys, canvases…) join if they stand on or lean over
    /// the footprint (edge tolerance for walls); everything else (furniture)
    /// joins only if it stands strictly INSIDE, so street props leaning on a
    /// facade stay in the street. Fully undoable (Ctrl+Z), safe to re-run.
    public static class HouseGrouper
    {
        // name prefixes that read as house STRUCTURE (edge-tolerant match)
        static readonly string[] StructureNames =
        {
            "Wall", "Roof", "Window", "Shutter", "Balcony", "Chimney",
            "Gable", "Door", "Beam", "HouseFloor", "WallCanvas", "Prop_Chimney"
        };

        [MenuItem("Spelly Zombie/Group House Parts (open scene)")]
        static void Group()
        {
            var floors = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.name == "HouseFloor")
                .OrderBy(t => Mathf.RoundToInt(t.position.x))
                .ThenBy(t => Mathf.RoundToInt(t.position.z))
                .ToList();
            if (floors.Count == 0)
            {
                EditorUtility.DisplayDialog("Group House Parts",
                    "No 'HouseFloor' objects found in the open scene. Nothing to group.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Group House Parts");
            int undoGroup = Undo.GetCurrentGroup();

            // footprints from the floors' own renderers
            var rects = new List<(Transform floor, Bounds b)>();
            foreach (var f in floors)
            {
                if (f.parent != null && f.parent.name.StartsWith("House ")) continue; // already grouped
                var rends = f.GetComponentsInChildren<Renderer>(true);
                if (rends.Length == 0) continue;
                Bounds b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                rects.Add((f, b));
            }
            if (rects.Count == 0)
            {
                EditorUtility.DisplayDialog("Group House Parts",
                    "Every house is already grouped. Nothing to do.", "OK");
                return;
            }

            int houseNo = NextHouseNumber();
            int totalMoved = 0;
            foreach (var (floor, b) in rects)
            {
                var villageRoot = floor.parent; // pieces are its siblings
                var root = new GameObject($"House {houseNo}");
                Undo.RegisterCreatedObjectUndo(root, "house root");
                root.transform.position = new Vector3(b.center.x, floor.position.y, b.center.z);
                if (villageRoot != null)
                    root.transform.SetParent(villageRoot, true);

                int moved = 0;
                var siblings = villageRoot != null
                    ? Enumerable.Range(0, villageRoot.childCount).Select(villageRoot.GetChild).ToList()
                    : new List<Transform>();
                foreach (var t in siblings)
                {
                    if (t == root.transform || t == floor) continue;
                    if (t.name.StartsWith("House ")) continue;
                    if (t.name == "HouseFloor") continue; // another house's anchor

                    Vector3 p = t.position;
                    if (p.y < b.min.y - 1f || p.y > b.min.y + 14f) continue;

                    bool structure = StructureNames.Any(s => t.name.StartsWith(s));
                    float pad = structure ? 1.25f : -0.2f; // walls sit ON the edge; furniture must be inside
                    if (p.x < b.min.x - pad || p.x > b.max.x + pad
                        || p.z < b.min.z - pad || p.z > b.max.z + pad) continue;

                    // between two touching houses the NEAREST floor wins
                    if (NearerToAnotherFloor(p, floor, rects)) continue;

                    Undo.SetTransformParent(t, root.transform, "house part");
                    moved++;
                }

                Undo.SetTransformParent(floor, root.transform, "house floor");
                moved++;
                totalMoved += moved;
                Debug.Log($"[SpellyZombie] {root.name}: {moved} part(s) grouped.");
                houseNo++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"[SpellyZombie] Grouped {rects.Count} house(s), {totalMoved} parts total. Drag each 'House N' to the Project window to make a prefab. Ctrl+Z reverts everything.");
        }

        static bool NearerToAnotherFloor(Vector3 p, Transform mine,
            List<(Transform floor, Bounds b)> rects)
        {
            float myDist = Flat(p, mine.position);
            foreach (var (other, _) in rects)
                if (other != mine && Flat(p, other.position) < myDist)
                    return true;
            return false;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        static int NextHouseNumber()
        {
            int max = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("House ")
                    && int.TryParse(t.name.Substring(6), out int n) && n > max)
                    max = n;
            return max + 1;
        }
    }
}
