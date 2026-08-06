using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// THE DRAG-AND-DROP GUARANTEE (Marko's rule: "no buttons for everything -
    /// let me drag ANYTHING into a prefab and your code picks it up"):
    /// every prefab saved anywhere under Resources/Custom is automatically
    /// healed the moment it lands, play mode or edit mode:
    ///
    ///   · runtime-only materials → saved as real .mat assets and remapped
    ///     (kills the magenta prefab forever)
    ///   · missing/unloadable scripts → stripped (kills the "script can not
    ///     be loaded" corpse components)
    ///   · ink nodes → stripped (doodles don't become part of a species)
    ///   · dead material slots → a loud warning naming the object, so a
    ///     prefab dragged AFTER play stopped (references already dead) says
    ///     exactly how to redo it instead of silently rendering pink
    ///
    /// So the workflow is just: drag it from the Hierarchy into
    /// Resources/Custom, name it for its hook (see ART_TODO registry), edit.
    public class CustomPrefabDoctor : AssetPostprocessor
    {
        static bool _healing;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFromPaths)
        {
            if (_healing) return;
            foreach (var assetPath in imported)
            {
                if (!assetPath.EndsWith(".prefab")) continue;
                if (!assetPath.Replace('\\', '/').Contains("/Resources/Custom")) continue;
                string captured = assetPath;
                EditorApplication.delayCall += () => Heal(captured); // outside the import pipeline
            }
        }

        static void Heal(string path)
        {
            if (_healing || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            _healing = true;
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
                // AXIOM opt-out: a HandsOff component means "never touch this"
                if (root.GetComponentInChildren<HandsOff>(true) != null) return;
                int fixes = 0;
                var report = new List<string>();

                // AXIOM (Marko Jul 25): this runs automatically on EVERY import
                // and every prefab-mode save. It must NEVER delete his work.
                // Both passes below are now REPORT-ONLY — a renamed .cs file
                // used to silently eat components (and their Inspector values)
                // out of his authored prefabs with no undo.
                // ---- corpse scripts: report, never strip ----
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int broken = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if (broken > 0)
                        Debug.LogWarning($"[SpellyZombie] '{t.name}' in {path} has {broken} missing-script " +
                            "component(s). LEFT ALONE (menu: Spelly Zombie/Clean Selected Custom Prefab).", root);
                }

                // ---- ink: report, never strip ----
                var inkNodes = root.GetComponentsInChildren<DrawNode>(true);
                if (inkNodes.Length > 0)
                    Debug.LogWarning($"[SpellyZombie] {path} carries {inkNodes.Length} ink node(s). LEFT ALONE. " +
                        "Erase the ink before making the prefab if you didn't mean to keep it.", root);

                // ---- materials persisted ----
                const string matDir = "Assets/_Game/Resources/Custom/Materials";
                System.IO.Directory.CreateDirectory(matDir);
                int deadSlots = 0;
                foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = rend.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) { deadSlots++; continue; }
                        if (AssetDatabase.Contains(mats[i])) continue;
                        var copy = new Material(mats[i]); // survives play mode ending
                        string matPath = AssetDatabase.GenerateUniqueAssetPath(
                            $"{matDir}/{Sanitize(mats[i].name)}.mat");
                        AssetDatabase.CreateAsset(copy, matPath);
                        mats[i] = copy;
                        changed = true;
                        fixes++;
                        report.Add($"saved material '{copy.name}'");
                    }
                    if (changed) rend.sharedMaterials = mats;
                }

                if (fixes > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                if (fixes > 0 || deadSlots > 0)
                    Debug.Log($"[SpellyZombie] Custom prefab healed: {path}\n  " +
                              string.Join("\n  ", report) +
                              (deadSlots > 0
                                  ? $"\n  ⚠ {deadSlots} material slot(s) were ALREADY DEAD (prefab was " +
                                    "made after play stopped). Delete it and re-drag DURING play mode."
                                  : ""));
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
                _healing = false;
            }
        }

        /// The DESTRUCTIVE strip, now opt-in only (AXIOM: he decides, not the
        /// importer). Select a prefab and run it when you actually want the
        /// broken scripts / stray ink gone.
        [MenuItem("Spelly Zombie/Clean Selected Custom Prefab")]
        static void CleanSelected()
        {
            var sel = Selection.activeGameObject;
            string path = sel != null ? AssetDatabase.GetAssetPath(sel) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
            {
                Debug.LogWarning("[SpellyZombie] Select a PREFAB ASSET in the Project window first.");
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int scripts = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    scripts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                var ink = root.GetComponentsInChildren<DrawNode>(true);
                if (!EditorUtility.DisplayDialog("Clean Custom Prefab",
                        $"{path}\n\nRemove {scripts} missing-script component(s) and {ink.Length} ink node(s)?\n\n" +
                        "This CANNOT be undone.", "Clean it", "Cancel"))
                    return;
                foreach (var node in ink) if (node != null) Object.DestroyImmediate(node.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[SpellyZombie] Cleaned {path}: {scripts} script(s), {ink.Length} ink node(s).");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static string Sanitize(string raw)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
