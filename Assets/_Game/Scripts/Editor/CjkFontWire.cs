using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SpellyZombie
{
    /// Wires a CJK-capable font (Noto Sans JP or your pick) into TextMesh Pro
    /// as a DYNAMIC fallback: glyphs bake at runtime, so every kanji works
    /// without a two-thousand-glyph atlas, and every TMP text in the game
    /// gains Japanese through the default font's fallback chain - no per-label
    /// changes anywhere.
    public static class CjkFontWire
    {
        const string LiberationPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        [MenuItem("Spelly Zombie/Localization/Wire Selected Font Into TMP Fallback")]
        static void Wire()
        {
            var font = Selection.activeObject as Font;
            if (font == null)
            {
                EditorUtility.DisplayDialog("CJK font",
                    "Select the font file (.ttf/.otf) in the Project window first - "
                    + "for Japanese, Noto Sans JP is the safe pick (SIL OFL, free for "
                    + "commercial use). Nothing is auto-picked for you.", "OK");
                return;
            }

            string fontPath = AssetDatabase.GetAssetPath(font);
            string dir = Path.GetDirectoryName(fontPath).Replace('\\', '/');
            string assetPath = $"{dir}/{font.name} SDF.asset";

            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fa == null)
            {
                fa = TMP_FontAsset.CreateFontAsset(font, 90, 9,
                    GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic, true);
                if (fa == null)
                {
                    EditorUtility.DisplayDialog("CJK font",
                        "TMP could not read that font file.", "OK");
                    return;
                }
                fa.name = font.name + " SDF";
                AssetDatabase.CreateAsset(fa, assetPath);
                fa.material.name = fa.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
                foreach (var tex in fa.atlasTextures)
                    if (tex != null) AssetDatabase.AddObjectToAsset(tex, fa);
                AssetDatabase.SaveAssets();
            }

            var lib = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationPath);
            if (lib == null)
            {
                EditorUtility.DisplayDialog("CJK font",
                    $"Default TMP font not found at:\n{LiberationPath}\n\nThe SDF asset "
                    + "was still created - add it to your default font's fallback list "
                    + "by hand.", "OK");
                return;
            }
            lib.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
            if (!lib.fallbackFontAssetTable.Contains(fa))
            {
                lib.fallbackFontAssetTable.Add(fa);
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[SpellyZombie] '{fa.name}' wired as a dynamic TMP fallback - "
                + "every TMP text now reaches its glyphs. Test with a Japanese string.");
            EditorGUIUtility.PingObject(fa);
        }
    }
}
