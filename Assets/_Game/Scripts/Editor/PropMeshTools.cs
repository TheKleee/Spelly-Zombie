using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellyZombie
{
    /// A build cannot cook a convex collider from a mesh whose import has
    /// Read/Write off: the lifted prop gets a box instead of its own shape
    /// (Liftable.MakePhysicsLegal). The editor always reads meshes, so only
    /// builds show it. This finds the meshes on every MeshCollider a build can
    /// reach and turns Read/Write on for their models: our prefabs, what stands
    /// in the build scenes, and every prefab those scenes and the Resources
    /// folders point at (kit props placed by hand, the collections).
    public static class PropMeshTools
    {
        [MenuItem("Spelly Zombie/Build/Make Prop Collider Meshes Readable")]
        static void MakeReadable()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SpellyZombie] Prop collider meshes: leave Play mode first.");
                return;
            }

            var models = new HashSet<string>();
            var meshes = new HashSet<Mesh>();

            var prefabs = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs" }))
                prefabs.Add(AssetDatabase.GUIDToAssetPath(guid));

            var roots = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled) roots.Add(s.path);
            foreach (var path in AssetDatabase.GetAllAssetPaths())
                if (path.StartsWith("Assets/") && path.Contains("/Resources/") && !AssetDatabase.IsValidFolder(path))
                    roots.Add(path);
            foreach (var path in AssetDatabase.GetDependencies(roots.ToArray(), true))
                if (path.EndsWith(".prefab")) prefabs.Add(path);

            foreach (var path in prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) Collect(prefab.GetComponentsInChildren<MeshCollider>(true), meshes, models);
            }

            // what stands in the scenes themselves; a scene that is not open is opened beside the current one and closed again
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (!s.enabled) continue;
                var scene = SceneManager.GetSceneByPath(s.path);
                bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Additive);
                foreach (var root in scene.GetRootGameObjects())
                    Collect(root.GetComponentsInChildren<MeshCollider>(true), meshes, models);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }

            int flipped = 0;
            foreach (var path in models)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || importer.isReadable) continue;
                importer.isReadable = true;
                importer.SaveAndReimport();
                flipped++;
            }
            Debug.Log($"[SpellyZombie] Prop collider meshes: looked at {prefabs.Count} prefabs and the build scenes, " +
                      $"{meshes.Count} unreadable meshes on {models.Count} models, Read/Write turned on for {flipped}." +
                      (flipped > 0 ? " Build again." : ""));
        }

        static void Collect(MeshCollider[] colliders, HashSet<Mesh> meshes, HashSet<string> models)
        {
            foreach (var mc in colliders)
            {
                if (mc.sharedMesh == null || mc.sharedMesh.isReadable) continue;
                meshes.Add(mc.sharedMesh);
                string path = AssetDatabase.GetAssetPath(mc.sharedMesh);
                if (!string.IsNullOrEmpty(path)) models.Add(path);
            }
        }
    }
}
