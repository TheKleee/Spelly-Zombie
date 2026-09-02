using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellyZombie
{
    /// Materials that are the same material: same shader, same properties,
    /// different asset. FindIdentical serves the prefab tool; the menu item
    /// merges the copies already on disk into one and repoints every prefab,
    /// scene and asset at it.
    public static class MaterialTwins
    {
        static readonly string[] Folders =
        {
            "Assets/_Game/Prefabs/Materials",
            "Assets/_Game/Resources/Custom/Materials",
        };

        static Dictionary<string, Material> _byKey;

        /// Everything that makes a material look the way it does, name aside.
        public static string Key(Material m)
        {
            string json = EditorJsonUtility.ToJson(m);
            return Regex.Replace(json, "\"m_Name\":\"[^\"]*\",?", "");
        }

        public static void Forget() => _byKey = null;

        public static Material FindIdentical(Material m, params string[] folders)
        {
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, Material>();
                foreach (var guid in AssetDatabase.FindAssets("t:Material", folders))
                {
                    var a = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (a == null) continue;
                    string k = Key(a);
                    if (!_byKey.ContainsKey(k) || Rank(a) < Rank(_byKey[k])) _byKey[k] = a;
                }
            }
            return _byKey.TryGetValue(Key(m), out var twin) ? twin : null;
        }

        /// The copy to keep: the one in Custom/Materials, else the shortest name.
        static int Rank(Material a)
        {
            string path = AssetDatabase.GetAssetPath(a);
            int r = path.Contains("/Custom/") ? 0 : 1000;
            return r + path.Length + (Regex.IsMatch(a.name, @" \d+$") ? 100 : 0);
        }

        [MenuItem("Spelly Zombie/Studio/Merge duplicate materials")]
        static void Merge()
        {
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                EditorUtility.DisplayDialog("Merge duplicate materials",
                    "Close Prefab Mode first, the merge rewrites prefab files.", "OK");
                return;
            }
            Forget();
            var groups = new Dictionary<string, List<Material>>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", Folders))
            {
                var a = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null) continue;
                string k = Key(a);
                if (!groups.TryGetValue(k, out var list)) groups[k] = list = new List<Material>();
                list.Add(a);
            }
            var keep = new Dictionary<string, string>(); // duplicate guid -> kept guid
            var doomed = new List<string>();
            var lines = new List<string>();
            foreach (var list in groups.Values)
            {
                if (list.Count < 2) continue;
                list.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
                string keptPath = AssetDatabase.GetAssetPath(list[0]);
                string keptGuid = AssetDatabase.AssetPathToGUID(keptPath);
                var names = new List<string>();
                for (int i = 1; i < list.Count; i++)
                {
                    string path = AssetDatabase.GetAssetPath(list[i]);
                    keep[AssetDatabase.AssetPathToGUID(path)] = keptGuid;
                    doomed.Add(path);
                    names.Add(Path.GetFileNameWithoutExtension(path));
                }
                lines.Add($"{Path.GetFileName(keptPath)}  <-  {string.Join(", ", names)}");
            }
            if (doomed.Count == 0)
            {
                EditorUtility.DisplayDialog("Merge duplicate materials", "No identical materials found.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Merge duplicate materials",
                $"{doomed.Count} copies fold into their originals. Every prefab, scene and asset "
                + "under Assets/_Game is repointed, the open scene is swapped in place, then the copies are deleted.\n\n"
                + string.Join("\n", lines), "Merge", "Cancel")) return;

            // open scenes through the API, so what is loaded stays right
            var kept = new Dictionary<string, Material>();
            foreach (var kv in keep)
                kept[kv.Key] = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(kv.Value));
            int swapped = 0;
            foreach (var rend in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = rend.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string g = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mats[i]));
                    if (!string.IsNullOrEmpty(g) && kept.TryGetValue(g, out var k)) { mats[i] = k; changed = true; }
                    else if (!AssetDatabase.Contains(mats[i]))
                    {
                        // an embedded scene material with an identical asset becomes that asset
                        var twin = FindIdentical(mats[i], Folders);
                        if (twin != null) { mats[i] = twin; changed = true; }
                    }
                }
                if (changed) { Undo.RecordObject(rend, "merge materials"); rend.sharedMaterials = mats; swapped++; }
            }
            if (swapped > 0) EditorSceneManager.MarkAllScenesDirty();

            // everything else on disk: the guid is the reference
            var openScenes = new HashSet<string>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++) openScenes.Add(EditorSceneManager.GetSceneAt(i).path);
            int files = 0;
            foreach (var file in Directory.GetFiles("Assets/_Game", "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".prefab" && ext != ".unity" && ext != ".asset" && ext != ".mat") continue;
                string norm = file.Replace('\\', '/');
                if (openScenes.Contains(norm)) continue;
                string text = File.ReadAllText(file);
                string after = text;
                foreach (var kv in keep) after = after.Replace(kv.Key, kv.Value);
                if (after != text) { File.WriteAllText(file, after); files++; }
            }
            foreach (var path in doomed) AssetDatabase.DeleteAsset(path);
            AssetDatabase.Refresh();
            Forget();
            Debug.Log($"[SpellyZombie] Merged {doomed.Count} duplicate materials: {files} files repointed, {swapped} renderers in the open scene swapped. Save the scene to keep those.");
        }
    }
}
