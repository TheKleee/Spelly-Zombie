using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// MARKO'S MATERIAL FAUCET (his rule: "allow me to create materials from
    /// a selected object — extract them to resources/materials"): select
    /// ANYTHING — scene object, play-mode runtime object, prefab instance —
    /// and every runtime-only material on it becomes a real, editable .mat
    /// asset in Resources/Custom/Materials. The selection keeps referencing
    /// the SAME material (now persistent), so what you see doesn't change —
    /// it just stops being ephemeral. Drag the saved .mat onto any prefab.
    ///
    /// (Why runtime materials exist at all: the chemistry tints things by
    /// COLOR VALUE at runtime — conjured matter, team colors, zombie variety.
    /// This faucet turns any of them into assets the moment you want one.)
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
            Debug.Log($"[SpellyZombie] Extract done — {saved} material(s) saved to {MatDir}" +
                      (already > 0 ? $", {already} were already assets." : "."));
        }

        static string Sanitize(string raw)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw;
        }
    }
}
