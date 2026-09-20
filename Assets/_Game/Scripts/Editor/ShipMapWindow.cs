using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ SHIP A MAP WITH THE GAME (his official boss map). A map saved in the
    /// Map Creator is copied, with its picture and its painted pages, into
    /// StreamingAssets/Maps: every copy of the game lists it, nobody can
    /// overwrite or delete it in a build, and beating its boss earns the boss
    /// achievement. Change the map in the Map Creator, then ship it again.
    public class ShipMapWindow : EditorWindow
    {
        [MenuItem("Spelly Zombie/Maps/Ship Maps With the Game")]
        static void Open() => GetWindow<ShipMapWindow>("Ship Maps").minSize = new Vector2(420, 300);

        const string ShippedAssets = "Assets/StreamingAssets/Maps";
        Vector2 _scroll;

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Your saved maps (the Map Creator's Maps folder). Ship copies one into the game: " +
                "everyone gets it, nobody can delete or overwrite it, and its boss earns the boss achievement. " +
                "After changing a shipped map in the Map Creator, ship it again.", MessageType.None);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("YOUR MAPS", EditorStyles.boldLabel);
            if (!Directory.Exists(MapLibrary.Folder) || Directory.GetFiles(MapLibrary.Folder, "*.json").Length == 0)
                EditorGUILayout.LabelField("No saved maps yet.", EditorStyles.miniLabel);
            else
                foreach (var file in Directory.GetFiles(MapLibrary.Folder, "*.json"))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    bool shipped = File.Exists(Path.Combine(ShippedAssets, stem + ".json"));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(stem);
                        if (GUILayout.Button(shipped ? "Ship again" : "Ship", GUILayout.Width(90))) Ship(stem);
                    }
                }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("COMES WITH THE GAME", EditorStyles.boldLabel);
            if (!Directory.Exists(ShippedAssets) || Directory.GetFiles(ShippedAssets, "*.json").Length == 0)
                EditorGUILayout.LabelField("Nothing shipped yet.", EditorStyles.miniLabel);
            else
                foreach (var file in Directory.GetFiles(ShippedAssets, "*.json"))
                {
                    string stem = Path.GetFileNameWithoutExtension(file);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(stem);
                        if (GUILayout.Button("Remove", GUILayout.Width(90))
                            && EditorUtility.DisplayDialog("Remove shipped map",
                                $"Stop shipping \"{stem}\" with the game? Your own saved copy stays.", "Remove", "Keep"))
                            Unship(stem);
                    }
                }
            EditorGUILayout.EndScrollView();
        }

        /// The map, its picture and its pages folder, into the game.
        static void Ship(string stem)
        {
            Directory.CreateDirectory(ShippedAssets);
            File.Copy(Path.Combine(MapLibrary.Folder, stem + ".json"), Path.Combine(ShippedAssets, stem + ".json"), true);
            string pic = Path.Combine(MapLibrary.Folder, stem + ".png");
            if (File.Exists(pic)) File.Copy(pic, Path.Combine(ShippedAssets, stem + ".png"), true);
            MapPages.CopyFolder(Path.Combine(MapLibrary.Folder, stem + " pages"), Path.Combine(ShippedAssets, stem + " pages"));
            AssetDatabase.Refresh();
            MatchLobby.ForgetMaps();
            Debug.Log($"[SpellyZombie] '{stem}' now ships with the game ({ShippedAssets}).");
        }

        static void Unship(string stem)
        {
            AssetDatabase.DeleteAsset($"{ShippedAssets}/{stem}.json");
            AssetDatabase.DeleteAsset($"{ShippedAssets}/{stem}.png");
            AssetDatabase.DeleteAsset($"{ShippedAssets}/{stem} pages");
            AssetDatabase.Refresh();
            MatchLobby.ForgetMaps();
            Debug.Log($"[SpellyZombie] '{stem}' no longer ships with the game.");
        }
    }
}
