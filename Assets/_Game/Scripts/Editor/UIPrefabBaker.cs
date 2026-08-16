using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// the UI WORKFLOW: the UI is code-built once, then becomes the.
    ///
    ///   1. Play. Visit the MENU and then the LOBBY so every surface has
    ///      been built at least once (hidden ones stay on the canvas).
    /// 2. Spelly Zombie  UI  Save UI As Prefab.
    ///   3. Stop playing. Edit Assets/_Game/Resources/SZ_UI.prefab freely -
    ///      move things, resize, recolor, change fonts.
    ///
    /// From then on the game INSTANTIATES the prefab and the code only wires
    /// behavior (live texts, button clicks) and creates elements the prefab
    /// doesn't have. Re-bake after big UI code changes to pick up new
    /// elements while keeping edits (bake writes what's live, so re-baking
    /// with the prefab loaded preserves the current look).
    public static class UIPrefabBaker
    {
        const string PathToPrefab = "Assets/_Game/Resources/SZ_UI.prefab";

        [MenuItem("Spelly Zombie/UI/Save UI As Prefab (play mode)")]
        static void Bake()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Save UI As Prefab",
                    "Enter Play mode first, visit the Menu and the Lobby so all UI exists, then run this again.", "OK");
                return;
            }
            var canvas = GameObject.Find("SZ_UI");
            if (canvas == null)
            {
                EditorUtility.DisplayDialog("Save UI As Prefab",
                    "No SZ_UI canvas found. The UI hasn't been built yet this session.", "OK");
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources"))
                AssetDatabase.CreateFolder("Assets/_Game", "Resources");
            PrefabUtility.SaveAsPrefabAsset(canvas, PathToPrefab, out bool ok);
            Debug.Log(ok
                ? $"[SpellyZombie] UI saved → {PathToPrefab}. Stop playing and edit it freely. Your layout is law from now on."
                : "[SpellyZombie] UI prefab save FAILED. See errors above.");
        }
    }
}
