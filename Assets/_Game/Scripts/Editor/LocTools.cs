using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Writes and checks the translation files under
    /// Assets/StreamingAssets/Loc. Templates come out with the English
    /// filled in, so a translator only replaces values.
    public static class LocTools
    {
        const string Menu = "Spelly Zombie/Languages/";

        [MenuItem(Menu + "Export English template (JSON)")]
        static void ExportEnglish() => Write("en");

        [MenuItem(Menu + "Create empty files for ALL launch languages")]
        static void ExportAll()
        {
            int made = 0;
            foreach (var l in Loc.Languages)
            {
                string path = PathFor(l.Code);
                if (File.Exists(path)) continue; // never overwrite a translation in progress
                Write(l.Code, silent: true);
                made++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[SpellyZombie] Languages: {made} new file(s) in {Folder()}. "
                + $"Each holds all {Loc.KeyCount} keys with the English text ready to replace.");
        }

        [MenuItem(Menu + "Open the language folder")]
        static void OpenFolder()
        {
            Directory.CreateDirectory(Folder());
            EditorUtility.RevealInFinder(Folder());
        }

        [MenuItem(Menu + "Check translations for missing keys")]
        static void Check()
        {
            string folder = Folder();
            if (!Directory.Exists(folder))
            {
                Debug.LogWarning("[SpellyZombie] Languages: no folder yet. Export the template first.");
                return;
            }

            foreach (var l in Loc.Languages)
            {
                if (l.Code == "en") continue;
                string path = PathFor(l.Code);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[SpellyZombie] {l.Native} ({l.Code}): NO FILE. Players there see English.");
                    continue;
                }

                var text = File.ReadAllText(path, System.Text.Encoding.UTF8);
                int keys = 0, blank = 0;
                foreach (var line in text.Split('\n'))
                {
                    if (!line.Contains("\"key\"")) continue;
                    keys++;
                    int v = line.IndexOf("\"value\"", System.StringComparison.Ordinal);
                    if (v < 0 || line.IndexOf("\"\"", v, System.StringComparison.Ordinal) > 0) blank++;
                }

                int missing = Loc.KeyCount - keys;
                if (missing > 0 || blank > 0)
                    Debug.LogWarning($"[SpellyZombie] {l.Native} ({l.Code}): {keys}/{Loc.KeyCount} keys"
                        + (missing > 0 ? $", {missing} MISSING" : "")
                        + (blank > 0 ? $", {blank} empty" : "") + ". Missing keys fall back to English.");
                else
                    Debug.Log($"[SpellyZombie] {l.Native} ({l.Code}): complete, {keys} keys.");
            }
        }

        static string Folder() => Path.Combine(Application.streamingAssetsPath, "Loc");
        static string PathFor(string code) => Path.Combine(Folder(), Loc.FileNameFor(code));

        static void Write(string code, bool silent = false)
        {
            string folder = Folder();
            Directory.CreateDirectory(folder);
            string path = PathFor(code);
            File.WriteAllText(path, Loc.EnglishTemplateJson(), new System.Text.UTF8Encoding(false));
            if (silent) return;
            AssetDatabase.Refresh();
            Debug.Log($"[SpellyZombie] Languages: wrote {Loc.KeyCount} keys to {path}");
            EditorUtility.RevealInFinder(path);
        }
    }
}
