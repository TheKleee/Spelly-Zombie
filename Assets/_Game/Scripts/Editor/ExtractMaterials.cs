using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Turns runtime-only materials into real assets: select anything (scene
    /// object, play-mode object, prefab instance) and every runtime material
    /// on it is saved as an editable .mat in Resources/Custom/Materials.
    /// The selection keeps referencing the same material, now persistent.
    /// Runtime materials exist because the chemistry tints by color value:
    /// conjured matter, team colors, zombie variety.
    public static class ExtractMaterials
    {
        const string MatDir = "Assets/_Game/Resources/Custom/Materials";

        [MenuItem("Spelly Zombie/Extract Materials From Selection")]
        public static void Extract()
        {
            if (Selection.gameObjects == null || Selection.gameObjects.Length == 0)
            {
                Debug.LogWarning("[SpellyZombie] Select something (scene or play mode) first.");
                return;
            }
            System.IO.Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            int saved = 0, already = 0;
            foreach (var go in Selection.gameObjects)
                foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = rend.sharedMaterials;
                    foreach (var m in mats)
                    {
                        if (m == null) continue;
                        if (AssetDatabase.Contains(m)) { already++; continue; }
                        string path = AssetDatabase.GenerateUniqueAssetPath(
                            $"{MatDir}/{Sanitize(m.name)}.mat");
                        AssetDatabase.CreateAsset(m, path); // live reference stays valid
                        saved++;
                        Debug.Log($"[SpellyZombie] Material extracted: {path}", m);
                    }
                }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpellyZombie] Extract done: {saved} material(s) saved to {MatDir}" +
                      (already > 0 ? $", {already} were already assets." : "."));
        }

        static string Sanitize(string raw)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
