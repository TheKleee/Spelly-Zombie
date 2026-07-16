using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Marko's prefab tool: SELECT anything in a scene → it becomes a prefab
    /// in _Game/Prefabs, exactly as it looks. Scene-only materials (like the
    /// builder's *_SZ twins) are saved into _Game/Prefabs/Materials first, so
    /// the prefab carries its true look into ANY scene. The scene object
    /// itself is not modified in any way.
    public static class MakePrefabFromSelection
    {
        const string Dir = "Assets/_Game/Prefabs";
        const string MatDir = Dir + "/Materials";

        [MenuItem("Spelly Zombie/Make Prefab From Selection")]
        public static void Make()
        {
            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("[SpellyZombie] Select something in the scene first.");
                return;
            }
            System.IO.Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            foreach (var go in Selection.gameObjects)
            {
                if (go.GetComponentInChildren<DrawNode>() != null)
                {
                    Debug.LogWarning($"[SpellyZombie] '{go.name}' has INK on it — erase it (RMB) " +
                                     "before prefabbing, or the ink gets baked into the prefab.");
                    continue;
                }

                // persist any scene-only materials so the prefab keeps its look
                foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = rend.sharedMaterials;
                    foreach (var m in mats)
                    {
                        if (m == null || AssetDatabase.Contains(m)) continue;
                        string matPath = AssetDatabase.GenerateUniqueAssetPath(
                            $"{MatDir}/{Sanitize(m.name)}.mat");
                        AssetDatabase.CreateAsset(m, matPath); // scene reference stays live
                    }
                }

                string path = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/{Sanitize(go.name)}.prefab");
                PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
                Debug.Log(ok
                    ? $"[SpellyZombie] Prefab saved: {path} — materials included, drop it in any scene."
                    : $"[SpellyZombie] FAILED to save prefab for '{go.name}'.");
            }
            AssetDatabase.SaveAssets();
        }

        static string Sanitize(string raw)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
