using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Bulk-adds only what is missing to selected scenery: Element (HP by
    /// size), Breakable (empty prefab slots), SurfaceMaterialTag (guessed from
    /// the name). Never adds/replaces colliders, materials, or values already
    /// set. Warns about mesh colliders and pre-attached Thermal.
    public static class SceneryTools
    {
        [MenuItem("Spelly Zombie/Scenery/Make Selected Breakable (fills gaps only)")]
        static void MakeBreakable()
        {
            var roots = Selection.gameObjects;
            if (roots == null || roots.Length == 0)
            {
                Debug.LogWarning("[SpellyZombie] Select the trees/rocks (or a parent) first.");
                return;
            }

            int touched = 0, alreadyOk = 0, meshColliders = 0, thermals = 0, noCollider = 0;

            foreach (var root in roots)
                foreach (var rend in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // the hierarchy decides the breakable unit, not this tool
                    var go = rend.gameObject;
                    if (go.GetComponentInParent<Element>() != null
                     && go.GetComponentInParent<Breakable>() != null) { alreadyOk++; continue; }

                    var bounds = rend.bounds;
                    float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

                    var dmg = Adopt.Component<Element>(go, out bool madeDmg);
                    if (madeDmg)
                    {
                        Undo.RegisterCreatedObjectUndo(dmg, "Make Breakable");
                        dmg.Health = Mathf.Clamp(15f + 18f * maxDim, 20f, 90f);
                    }

                    var br = Adopt.Component<Breakable>(go, out bool madeBr);
                    if (madeBr) Undo.RegisterCreatedObjectUndo(br, "Make Breakable");

                    if (go.GetComponentInParent<SurfaceMaterialTag>() == null)
                    {
                        var t = go.AddComponent<SurfaceMaterialTag>();
                        Undo.RegisterCreatedObjectUndo(t, "Make Breakable");
                        t.Material = GuessMaterial(go.name);
                    }

                    // report the things that actually cost performance
                    var col = go.GetComponent<Collider>();
                    if (col == null) noCollider++;
                    else if (col is MeshCollider) meshColliders++;
                    if (go.GetComponent<Thermal>() != null) thermals++;

                    EditorUtility.SetDirty(go);
                    touched++;
                }

            Debug.Log($"[SpellyZombie] Scenery: prepared {touched} object(s), skipped {alreadyOk} already set up.\n" +
                      "Now fill in YOUR pieces on the Breakable components: DebrisPrefabs (your logs), " +
                      "BreakFx (your particle effect), Standing (your stump).");

            if (noCollider > 0)
                Debug.LogWarning($"[SpellyZombie] {noCollider} object(s) have NO collider. Spells and the pen " +
                                 "will pass straight through. Add the collider YOU want (capsule for trunks, box/sphere for rocks).");
            if (meshColliders > 0)
                Debug.LogWarning($"[SpellyZombie] {meshColliders} object(s) use MeshColliders. They're the most " +
                                 "expensive kind at forest scale. A capsule/box is far cheaper if the shape allows it.");
            if (thermals > 0)
                Debug.LogWarning($"[SpellyZombie] {thermals} object(s) have Thermal pre-attached. Thermal ticks EVERY " +
                                 "FRAME per object. At forest scale that is the main lag risk. Let spells add it on " +
                                 "contact instead (they already do).");
        }

        static SurfaceMaterialType GuessMaterial(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("rock") || n.Contains("stone") || n.Contains("boulder") || n.Contains("menhir"))
                return SurfaceMaterialType.Stone;
            if (n.Contains("tree") || n.Contains("log") || n.Contains("wood") || n.Contains("trunk")
             || n.Contains("plank") || n.Contains("fence") || n.Contains("stump"))
                return SurfaceMaterialType.Wood;
            return SurfaceMaterialType.Wood;
        }

        /// Every mesh under the selection gets a MeshCollider of its own mesh
        /// where it has no collider at all. Works on scene objects (undoable)
        /// and on prefab assets picked in the Project window (saved in place).
        /// Nothing that already has a collider is touched.
        [MenuItem("Spelly Zombie/Scenery/Add Mesh Colliders To Selection (fills gaps only)")]
        static void AddMeshColliders()
        {
            int added = 0, assets = 0, scene = 0;
            foreach (var obj in Selection.objects)
            {
                var go = obj as GameObject;
                if (go == null) continue;
                string path = AssetDatabase.GetAssetPath(go);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    int n = FillColliders(root, false);
                    if (n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); added += n; assets++; }
                    PrefabUtility.UnloadPrefabContents(root);
                }
                else if (go.scene.IsValid())
                {
                    int n = FillColliders(go, true);
                    if (n > 0) { added += n; scene++; }
                }
            }
            Debug.Log($"[SpellyZombie] Mesh colliders added: {added} ({assets} prefab asset(s), {scene} scene object(s)). "
                + "Objects that already had a collider were left alone.");
        }

        static int FillColliders(GameObject root, bool undo)
        {
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                var mc = undo ? Undo.AddComponent<MeshCollider>(mf.gameObject)
                              : mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                n++;
            }
            return n;
        }

        [MenuItem("Spelly Zombie/Scenery/Report Performance Risks In Scene")]
        static void Report()
        {
            int meshCol = 0, thermal = 0, rbs = 0, breakables = 0;
            foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)) meshCol++;
            foreach (var t in Object.FindObjectsByType<Thermal>(FindObjectsSortMode.None)) thermal++;
            foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                if (!rb.isKinematic) rbs++;
            foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None)) breakables++;

            Debug.Log($"[SpellyZombie] Scene performance report:\n" +
                      $"  Breakables: {breakables} (these cost NOTHING until they break, no Update)\n" +
                      $"  Thermal components: {thermal}  ← these DO tick every frame, per object\n" +
                      $"  Non-kinematic Rigidbodies: {rbs}  ← every one is simulated continuously\n" +
                      $"  MeshColliders: {meshCol}  ← the most expensive collider type\n" +
                      "  (Trees/rocks should be: primitive collider, no Rigidbody, no Thermal.)");
        }
    }
}
