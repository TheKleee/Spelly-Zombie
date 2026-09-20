using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellyZombie
{
    /// The Collection Manager's texture lists are keyed by file name, its area
    /// looks by the area's name. These add whatever the open scene's lists
    /// lack, so a scene saved in binary needs no hand entry.
    public static class CollectionListTools
    {
        [MenuItem("Spelly Zombie/Grimoire/Add Missing Page Art To Collection Manager")]
        public static void AddPages() => AddMissing("Assets/_Game/Art/2D/Book Pages", "_bookPages", "GrimoirePage_", "page");

        [MenuItem("Spelly Zombie/Maps/Add Missing Map Pictures To Collection Manager")]
        static void AddMapPictures() => AddMissing(MapPictureTools.Folder, "_mapPictures", "", "map picture");

        [MenuItem("Spelly Zombie/Spells/Add Missing Area Looks To Collection Manager")]
        static void AddAreaLooks()
        {
            var cm = Object.FindFirstObjectByType<CollectionManager>(FindObjectsInactive.Include);
            if (cm == null)
            {
                Debug.LogWarning("[SpellyZombie] No Collection Manager in the open scene. Open the Lobby first.");
                return;
            }
            var so = new SerializedObject(cm);
            var list = so.FindProperty("_areaLooks");
            var have = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                var look = e.FindPropertyRelative("Look").objectReferenceValue as GameObject;
                string area = e.FindPropertyRelative("Area").stringValue;
                if (look != null) have.Add(string.IsNullOrEmpty(area) ? look.name : area);
            }

            var added = new List<string>();
            var gone = new List<string>();
            foreach (var a in SpellBook.Live.aoes)
            {
                if (a == null || string.IsNullOrEmpty(a.PrefabGuid) || have.Contains(a.Name)) continue;
                string path = AssetDatabase.GUIDToAssetPath(a.PrefabGuid);
                var go = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { gone.Add(a.Name); continue; }
                list.arraySize++;
                var entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("Area").stringValue = a.Name;
                entry.FindPropertyRelative("Look").objectReferenceValue = go;
                have.Add(a.Name);
                added.Add($"{a.Name} = {go.name}");
            }
            if (gone.Count > 0)
                Debug.LogWarning("[SpellyZombie] Areas whose look prefab no longer exists (pick it again in the AOE Creator): "
                    + string.Join(", ", gone));
            if (added.Count == 0)
            {
                Debug.Log("[SpellyZombie] Collection Manager already lists the look of every area in the spellbook.");
                return;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(cm);
            EditorSceneManager.MarkSceneDirty(cm.gameObject.scene);
            Debug.Log($"[SpellyZombie] Collection Manager: {added.Count} area look(s) added, save the scene: "
                + string.Join(", ", added));
        }

        static void AddMissing(string folder, string field, string prefix, string what)
        {
            var cm = Object.FindFirstObjectByType<CollectionManager>(FindObjectsInactive.Include);
            if (cm == null)
            {
                Debug.LogWarning("[SpellyZombie] No Collection Manager in the open scene.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[SpellyZombie] No folder {folder} yet - nothing to add.");
                return;
            }
            var so = new SerializedObject(cm);
            var list = so.FindProperty(field);
            var have = new HashSet<Texture2D>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var t = list.GetArrayElementAtIndex(i).objectReferenceValue as Texture2D;
                if (t != null) have.Add(t);
            }

            var added = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D " + prefix, new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null || !tex.name.StartsWith(prefix) || have.Contains(tex)) continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = tex;
                added.Add(tex.name);
            }
            if (added.Count == 0)
            {
                Debug.Log($"[SpellyZombie] Collection Manager already lists every {what} in {folder}");
                return;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(cm);
            EditorSceneManager.MarkSceneDirty(cm.gameObject.scene);
            Debug.Log($"[SpellyZombie] Collection Manager: {added.Count} {what}(s) added, save the scene: "
                + string.Join(", ", added));
        }
    }
}
