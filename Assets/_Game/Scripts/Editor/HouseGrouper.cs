using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Gathers every piece of a house under a "House N" root. The ROOF is
    /// the anchor: each Roof_RoundTiles body is one house, and every house
    /// part within that roof's radius joins it. Plaza paving, fences and the
    /// village boundary wall never qualify. Re-running dissolves old groups
    /// first. Fully undoable.
    public static class HouseGrouper
    {
        // what a house is made of
        static readonly string[] PartPrefixes =
        {
            "Wall_", "Window", "WindowShutters", "Door", "DoorFrame", "Corner_",
            "Roof_", "Prop_Chimney", "Floor_WoodDark", "Balcony", "Beam", "Gable"
        };

        const float RadiusMargin = 1.5f;   // beyond the roof half diagonal
        const float YBelow = 1f, YAbove = 1.5f;

        static bool IsRoofBody(Transform t) => t.name.StartsWith("Roof_RoundTiles");

        static bool IsPart(Transform t) => PartPrefixes.Any(p => t.name.StartsWith(p));

        /// Half diagonal of the AxB footprint in the roof name, plus margin.
        static float RadiusOf(Transform roof)
        {
            var m = System.Text.RegularExpressions.Regex.Match(roof.name, @"(\d+)x(\d+)");
            float a = 4f, b = 6f;
            if (m.Success) { a = float.Parse(m.Groups[1].Value); b = float.Parse(m.Groups[2].Value); }
            return Mathf.Sqrt(a * a * 0.25f + b * b * 0.25f) + RadiusMargin;
        }

        /// A door wall outward normal is its thin axis pointing away from
        /// the pivot; the threshold is the door leaf nearest that wall.
        static List<(Vector3 at, Vector3 outward)> Doorways(List<Transform> parts, Vector3 pivot)
        {
            var result = new List<(Vector3, Vector3)>();
            var leaves = parts.Where(t => t.name.StartsWith("Door_1")).ToList();
            foreach (var wall in parts.Where(t => t.name.Contains("_Door_")))
            {
                var rends = wall.GetComponentsInChildren<Renderer>(true);
                if (rends.Length == 0) continue;
                Bounds b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                Vector3 n = WallNormal(wall, pivot, b);
                Vector3 at = b.center;
                float bd = float.MaxValue;
                foreach (var leaf in leaves)
                {
                    float d = Vector3.Distance(leaf.position, b.center);
                    if (d < bd) { bd = d; at = leaf.position; }
                }
                at.y = pivot.y;
                result.Add((at, n));
            }
            return result;
        }

        /// The thin axis of the wall mesh in its own space, turned into the
        /// world through its transform, so a rotated house keeps a true
        /// normal. Sign points away from the pivot.
        static Vector3 WallNormal(Transform wall, Vector3 pivot, Bounds worldBounds)
        {
            Vector3 n = Vector3.zero;
            var mf = wall.GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 sc = mf.transform.lossyScale;
                Vector3 e = Vector3.Scale(mf.sharedMesh.bounds.size,
                    new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z)));
                Vector3 axis = e.x <= e.y && e.x <= e.z ? Vector3.right
                             : e.y <= e.z ? Vector3.up : Vector3.forward;
                n = mf.transform.TransformDirection(axis);
                n.y = 0f;
            }
            if (n.sqrMagnitude < 0.01f)
                n = worldBounds.size.x < worldBounds.size.z ? Vector3.right : Vector3.forward;
            n.Normalize();
            Vector3 away = worldBounds.center - pivot; away.y = 0f;
            if (Vector3.Dot(n, away) < 0f) n = -n;
            return n;
        }

        [MenuItem("Spelly Zombie/Scenes/Group House Parts (open scene)")]
        static void Group()
        {
            Undo.SetCurrentGroupName("Group House Parts");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var old in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t != null && t.name.StartsWith("House ")
                    && int.TryParse(t.name.Substring(6), out _)).ToList())
            {
                while (old.childCount > 0)
                {
                    var c = old.GetChild(0);
                    // door marks are ours; they are remade, never regrouped
                    if (c.GetComponent<PathPoint>() != null && c.childCount == 0)
                    { Undo.DestroyObjectImmediate(c.gameObject); continue; }
                    Undo.SetTransformParent(c, old.parent, "ungroup");
                }
                Undo.DestroyObjectImmediate(old.gameObject);
            }

            var roofs = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(IsRoofBody)
                .OrderBy(t => Mathf.RoundToInt(t.position.x))
                .ThenBy(t => Mathf.RoundToInt(t.position.z))
                .ToList();
            if (roofs.Count == 0)
            {
                EditorUtility.DisplayDialog("Group House Parts",
                    "No Roof_RoundTiles roof bodies in the open scene. Nothing to anchor.", "OK");
                return;
            }

            var members = new Dictionary<Transform, List<Transform>>();
            foreach (var r in roofs) members[r] = new List<Transform>();
            var skippedNear = new List<string>();

            var candidates = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t != null && !IsRoofBody(t) && !t.name.StartsWith("House "))
                .ToList();
            foreach (var t in candidates)
            {
                Transform best = null;
                float bd = float.MaxValue;
                foreach (var r in roofs)
                {
                    float d = Vector2.Distance(new Vector2(t.position.x, t.position.z),
                                               new Vector2(r.position.x, r.position.z));
                    if (d < bd) { bd = d; best = r; }
                }
                if (bd > RadiusOf(best)) continue;
                if (t.position.y < best.position.y - 20f) continue;
                if (t.position.y < -YBelow || t.position.y > best.position.y + YAbove) continue;
                if (!IsPart(t))
                {
                    if (t.parent == best.parent) skippedNear.Add(t.name);
                    continue;
                }
                // only siblings of the roof (the village root) are house parts;
                // a part nested under something else belongs to that thing
                if (t.parent != best.parent) continue;
                members[best].Add(t);
            }

            int houseNo = 1, total = 0;
            foreach (var roof in roofs)
            {
                var list = members[roof];
                var floors = list.Where(t => t.name.StartsWith("Floor_WoodDark")).ToList();
                Vector3 pivot;
                if (floors.Count > 0)
                {
                    var sum = Vector3.zero;
                    float minY = float.MaxValue;
                    foreach (var f in floors) { sum += f.position; minY = Mathf.Min(minY, f.position.y); }
                    pivot = new Vector3(sum.x / floors.Count, minY, sum.z / floors.Count);
                }
                else
                {
                    float minY = list.Count > 0 ? list.Min(t => t.position.y) : roof.position.y;
                    pivot = new Vector3(roof.position.x, minY, roof.position.z);
                }

                var root = new GameObject($"House {houseNo++}");
                Undo.RegisterCreatedObjectUndo(root, "house root");
                root.transform.position = pivot;

                // every door wall gives a doorway: threshold + outward normal.
                // The first one is the prefab +Z, the map entrance law.
                var doors = Doorways(list, pivot);
                if (doors.Count > 0)
                    root.transform.rotation = Quaternion.LookRotation(doors[0].outward, Vector3.up);
                if (roof.parent != null) root.transform.SetParent(roof.parent, true);

                Undo.SetTransformParent(roof, root.transform, "house roof");
                foreach (var t in list) Undo.SetTransformParent(t, root.transform, "house part");
                foreach (var d in doors)
                {
                    var mark = new GameObject("Door");
                    Undo.RegisterCreatedObjectUndo(mark, "doorway");
                    mark.transform.SetPositionAndRotation(d.at,
                        Quaternion.LookRotation(d.outward, Vector3.up));
                    mark.transform.SetParent(root.transform, true);
                    mark.AddComponent<PathPoint>();
                }
                total += list.Count + 1;
                Debug.Log($"[SpellyZombie] {root.name}: {list.Count + 1} parts under {roof.name}, "
                    + $"{doors.Count} doorway(s).");
            }

            Undo.CollapseUndoOperations(undoGroup);
            if (skippedNear.Count > 0)
                Debug.Log("[SpellyZombie] near a house but not a house part, left alone: "
                    + string.Join(", ", skippedNear.Distinct()));
            Debug.Log($"[SpellyZombie] Grouped {roofs.Count} house(s), {total} parts. "
                + "Each root sits at the bottom center of its floor and faces its door. "
                + "Ctrl+Z reverts everything.");
        }
    }
}
