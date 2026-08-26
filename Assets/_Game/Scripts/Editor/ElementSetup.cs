using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellyZombie
{
    /// EVERYTHING IN THE WORLD IS AN ELEMENT. A collider is what makes a thing
    /// interactable at all, so having one IS being an element - that is the
    /// whole test, and it means nobody keeps a list of what counts.
    ///
    /// ONE ELEMENT PER OBJECT, AT ITS ROOT. Children of a rooted object are
    /// parts of that one thing, not things of their own - a wall is a separate
    /// object already, so it gets its own without anyone splitting anything.
    ///
    /// Runs in the EDITOR and saves into the asset rather than adding
    /// components at runtime: what it stamps is authored, inspectable, and
    /// yours to override afterwards.
    public static class ElementSetup
    {
        /// The authored OBJECT this collider belongs to. NOT transform.root -
        /// generated props sit under a map container, and rooting there would
        /// make the entire island one element.
        static GameObject ObjectRoot(Collider col)
        {
            var pr = PrefabUtility.GetOutermostPrefabInstanceRoot(col.gameObject);
            return pr != null ? pr : col.gameObject;
        }

        [MenuItem("Spelly Zombie/Elements/Set Up Open Scene")]
        static void SetUpScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            int added = 0, hadOne = 0, triggers = 0, noCollider = 0;
            var done = new HashSet<GameObject>();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var col in root.GetComponentsInChildren<Collider>(true))
                {
                    // a trigger is a volume, not a body - you cannot hit it,
                    // so it is not a thing that can break
                    if (col.isTrigger) { triggers++; continue; }

                    var target = ObjectRoot(col);
                    if (!done.Add(target)) continue;   // several colliders, one object

                    // anything already carrying one - here or above - owns
                    // this body, and its children are parts of it
                    if (target.GetComponentInParent<Element>() != null
                        || target.GetComponentInChildren<Element>(true) != null)
                    { hadOne++; continue; }

                    Undo.AddComponent<Element>(target);
                    added++;
                }

                foreach (var el in root.GetComponentsInChildren<Element>(true))
                    if (el.GetComponentInChildren<Collider>(true) == null)
                    {
                        noCollider++;
                        Debug.LogWarning($"[SpellyZombie] {el.name} is an Element with NO COLLIDER - " +
                            "nothing can ever touch it. Give it a collider or remove the Element.", el);
                    }
            }

            if (added > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[SpellyZombie] Elements: {added} added, {hadOne} already covered, " +
                      $"{triggers} triggers skipped, {noCollider} with no collider.");
        }

        [MenuItem("Spelly Zombie/Elements/Set Up Prefabs In Map Objects")]
        static void SetUpPrefabs()
        {
            // map props are INSTANTIATED at generation, so the prefab asset is
            // the only place to stamp them - a runtime pass would be building
            // components on the fly, every match, forever
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs" });
            int touched = 0, fine = 0, noCollider = 0;
            var report = new List<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);

                bool solid = false;
                foreach (var col in root.GetComponentsInChildren<Collider>(true))
                    if (!col.isTrigger) { solid = true; break; }

                if (!solid) { noCollider++; fine++; PrefabUtility.UnloadPrefabContents(root); continue; }

                // THE PREFAB ROOT IS THE OBJECT. Its children are its parts.
                if (root.GetComponentInChildren<Element>(true) == null)
                {
                    root.AddComponent<Element>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    touched++;
                    report.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                }
                else fine++;

                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[SpellyZombie] Elements into prefabs: {touched} changed, {fine} already fine, " +
                      $"{noCollider} had no solid collider (left alone)." +
                      (report.Count > 0 ? "\n  " + string.Join(", ", report) : ""));
        }
    }
}
