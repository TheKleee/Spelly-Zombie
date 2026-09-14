using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// A build cannot cook a convex collider from a mesh whose import has
    /// Read/Write off: the lifted prop loses its collider and falls through
    /// the ground. The editor always reads meshes, so only builds break.
    /// This walks every prefab under Assets/_Game/Prefabs, finds the meshes
    /// on their MeshColliders and turns Read/Write on for those models.
    public static class PropMeshTools
    {
        [MenuItem("Spelly Zombie/Build/Make Prop Collider Meshes Readable")]
        static void MakeReadable()
        {
            var models = new HashSet<string>();
            var meshes = new HashSet<Mesh>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (var mc in prefab.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc.sharedMesh == null || mc.sharedMesh.isReadable) continue;
                    meshes.Add(mc.sharedMesh);
                    string path = AssetDatabase.GetAssetPath(mc.sharedMesh);
                    if (!string.IsNullOrEmpty(path)) models.Add(path);
                }
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
            Debug.Log($"[SpellyZombie] Prop collider meshes: {meshes.Count} unreadable meshes on " +
                      $"{models.Count} models, Read/Write turned on for {flipped}. Build again.");
        }
    }
}
