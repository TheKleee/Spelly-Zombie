using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Groups the selection under empty parents pivoted at each composite's
    /// bottom center. The selection is first split into spatial clusters, so
    /// parts of two different houses become two groups, never one.
    public static class GroupBottomPivot
    {
        // parts whose bounds come within this gap belong to one building
        const float TouchGap = 0.75f;

        [MenuItem("Spelly Zombie/Group Selection (Bottom Pivot) &#g")]
        static void Group()
        {
            var parts = new List<(GameObject go, Bounds b)>();
            foreach (var go in Selection.gameObjects)
            {
                if (go == null || !go.scene.IsValid()) continue;
                var b = ShapeShift.FindObjectBounds(go.transform);
                if (b.size.sqrMagnitude < 1e-8f)
                    b = new Bounds(go.transform.position, Vector3.one * 0.1f);
                parts.Add((go, b));
            }
            if (parts.Count == 0)
            {
                Debug.LogWarning("[SpellyZombie] Select scene objects to group first.");
                return;
            }

            int n = parts.Count;
            var link = new int[n];
            for (int i = 0; i < n; i++) link[i] = i;
            int Find(int i) { while (link[i] != i) i = link[i] = link[link[i]]; return i; }

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = parts[i].b;
                    a.Expand(TouchGap * 2f);
                    if (!a.Intersects(parts[j].b)) continue;
                    int ri = Find(i), rj = Find(j);
                    if (ri != rj) link[rj] = ri;
                }

            var clusters = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(i);
                if (!clusters.TryGetValue(r, out var list)) clusters[r] = list = new List<int>();
                list.Add(i);
            }

            var made = new List<GameObject>();
            foreach (var list in clusters.Values)
            {
                var b = parts[list[0]].b;
                int biggest = list[0];
                foreach (int i in list)
                {
                    b.Encapsulate(parts[i].b);
                    if (Volume(parts[i].b) > Volume(parts[biggest].b)) biggest = i;
                }
                Vector3 pivot = new Vector3(b.center.x, b.min.y, b.center.z);

                var parent = new GameObject(parts[biggest].go.name + "_Group");
                Undo.RegisterCreatedObjectUndo(parent, "Group under bottom pivot");
                var home = parts[list[0]].go.transform.parent;
                if (home != null) parent.transform.SetParent(home, true);
                parent.transform.position = pivot;

                foreach (int i in list)
                    Undo.SetTransformParent(parts[i].go.transform, parent.transform,
                        "Group under bottom pivot");
                made.Add(parent);
            }

            Selection.objects = made.ToArray();
            Debug.Log($"[SpellyZombie] grouped {n} parts into {made.Count} group(s), "
                + "each pivoted at its bottom center. Rename, then Alt+Shift+P per group.");
        }

        static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;
    }
}
