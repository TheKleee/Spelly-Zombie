using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Build Settings helper: registers the hand-made scenes in the build.
    /// It does not generate scenes.
    public static class MenuBuilder
    {
        [MenuItem("Spelly Zombie/Scenes/Register Scenes In Build (Menu, Lobby, Spelly Island)")]
        public static void RegisterScenes()
        {
            var wanted = new[] { "Menu", "Lobby", "Spelly Island" };
            var list = new List<EditorBuildSettingsScene>();
            foreach (var sceneName in wanted)
            {
                string found = null;
                foreach (var guid in AssetDatabase.FindAssets($"t:Scene {sceneName}"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName) { found = path; break; }
                }
                if (found != null) list.Add(new EditorBuildSettingsScene(found, true));
                else Debug.LogWarning($"[SpellyZombie] Scene '{sceneName}' not found. Save it first.");
            }
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"[SpellyZombie] Build Settings now has {list.Count} scenes (in order).");
        }
    }
}
