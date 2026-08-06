using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Build Settings helper. (All scene-BUILDING code was deleted at Marko's
    /// order — scenes are his, hand-made. This only registers them.)
    public static class MenuBuilder
    {
        [MenuItem("Spelly Zombie/Register Scenes In Build (Menu, Lobby, Game)")]
        public static void RegisterScenes()
        {
            var wanted = new[] { "Menu", "Lobby", "Game" };
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
