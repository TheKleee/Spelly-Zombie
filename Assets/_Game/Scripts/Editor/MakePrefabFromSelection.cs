using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// THE prefab tool (Alt+Shift+P): select anything in a scene, it becomes
    /// a prefab in the Project folder you have selected - or _Game/Prefabs
    /// when none is. Scene-only materials AND meshes are saved as assets
    /// first, so the prefab carries its true look into ANY scene - the native
    /// drag-to-project loses them, this never does. A scene-only material
    /// identical to an existing asset is swapped for that asset, never copied.
    public static class MakePrefabFromSelection
    {
        const string Dir = "Assets/_Game/Prefabs";
        const string MatDir = Dir + "/Materials";
        const string MeshDir = Dir + "/Meshes";
        const string CustomMatDir = "Assets/_Game/Resources/Custom/Materials";

        [MenuItem("Spelly Zombie/Make Prefab From Selection &#p")]
        public static void Make()
        {
            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("[SpellyZombie] Select something in the scene first.");
                return;
            }
            System.IO.Directory.CreateDirectory(MatDir);
            System.IO.Directory.CreateDirectory(MeshDir);
            AssetDatabase.Refresh();
            string outDir = ActiveProjectFolder();

            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponentInChildren<DrawNode>() != null)
                {
                    Debug.LogWarning($"[SpellyZombie] '{go.name}' has INK on it. Erase it (RMB) " +
                                     "before prefabbing, or the ink gets baked into the prefab.");
                    continue;
                }

                // persist any scene-only materials so the prefab keeps its look;
                // an identical asset already on disk is used instead of a copy
                foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = rend.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || AssetDatabase.Contains(m)) continue;
                        var twin = MaterialTwins.FindIdentical(m, MatDir, CustomMatDir);
                        if (twin != null) { mats[i] = twin; changed = true; continue; }
                        AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(
                            $"{MatDir}/{Sanitize(m.name)}.mat")); // scene reference stays live
                        MaterialTwins.Forget();
                    }
                    if (changed) rend.sharedMaterials = mats;
                }

                // and any scene-built meshes - same trap, same cure
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    PersistMesh(mf.sharedMesh);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    PersistMesh(smr.sharedMesh);
                foreach (var mc in go.GetComponentsInChildren<MeshCollider>(true))
                    PersistMesh(mc.sharedMesh);

                string path = AssetDatabase.GenerateUniqueAssetPath(
                    $"{outDir}/{Sanitize(go.name)}.prefab");
                PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
                Debug.Log(ok
                    ? $"[SpellyZombie] Prefab saved with materials: {path}. Drop it in any scene."
                    : $"[SpellyZombie] FAILED to save prefab for '{go.name}'.");
            }
            AssetDatabase.SaveAssets();
        }

        static void PersistMesh(Mesh m)
        {
            if (m == null || AssetDatabase.Contains(m)) return;
            AssetDatabase.CreateAsset(m, AssetDatabase.GenerateUniqueAssetPath(
                $"{MeshDir}/{Sanitize(m.name)}.asset"));
        }

        /// The folder selected in the Project window; _Game/Prefabs when the
        /// selection is not a folder.
        static string ActiveProjectFolder()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(p)) return p;
            }
            var m = typeof(ProjectWindowUtil).GetMethod("GetActiveFolderPath",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var r = m?.Invoke(null, null) as string;
            return !string.IsNullOrEmpty(r) && AssetDatabase.IsValidFolder(r) ? r : Dir;
        }

        static string Sanitize(string raw)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
