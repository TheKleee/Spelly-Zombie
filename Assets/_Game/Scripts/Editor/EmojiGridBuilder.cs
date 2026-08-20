using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

namespace SpellyZombie
{
    /// Rebuilds EmojiGrid from Assets/_Game/Fonts/sz-emoji: any Noto png named
    /// emoji_u<hex>[_<hex>...].png joins the grid on next editor load (same
    /// asset and GUID, so references hold). Fixed metrics: BearingX 0,
    /// BearingY 462.3, advance 512. ZWJ sequences are name-only (0xFFFE).
    [InitializeOnLoad]
    public static class EmojiGridBuilder
    {
        const string Folder = "Assets/_Game/Fonts/sz-emoji";
        const string PngPath = "Assets/_Game/Fonts/EmojiGrid.png";
        const string AssetPath = "Assets/_Game/Fonts/EmojiGrid.asset";
        const int Cell = 512;
        const int Atlas = 4096; // 8x8 cells

        static EmojiGridBuilder()
        {
            EditorApplication.delayCall += AutoCheck;
        }

        /// The folder and the grid must agree - if a png exists that the
        /// asset doesn't carry (or one was removed), rebuild without asking.
        static void AutoCheck()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!Directory.Exists(Folder)) return;
            var asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AssetPath);
            if (asset == null) return;

            var stems = Stems();
            var have = new HashSet<string>();
            foreach (var c in asset.spriteCharacterTable)
                if (c != null) have.Add(c.name);
            if (stems.Count == have.Count && stems.All(have.Contains)) return;

            Debug.Log($"[SpellyZombie] EmojiGrid is behind the sz-emoji folder " +
                $"({have.Count} sprites vs {stems.Count} pngs) — rebuilding.");
            Rebuild();
        }

        static List<string> Stems() =>
            Directory.GetFiles(Folder, "emoji_u*.png")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(s => s, System.StringComparer.Ordinal)
                .ToList();

        [MenuItem("Spelly Zombie/Rebuild Emoji Grid")]
        public static void Rebuild()
        {
            var stems = Stems();
            if (stems.Count == 0)
            {
                Debug.LogError($"[SpellyZombie] No emoji_u*.png files in {Folder} — nothing to build.");
                return;
            }
            if (stems.Count > (Atlas / Cell) * (Atlas / Cell))
            {
                Debug.LogError($"[SpellyZombie] {stems.Count} emojis but the {Atlas} atlas holds " +
                    $"{(Atlas / Cell) * (Atlas / Cell)} — raise Atlas in EmojiGridBuilder.");
                return;
            }

            // ---- paint the grid ----
            var px = new Color32[Atlas * Atlas]; // starts fully transparent
            var rects = new List<GlyphRect>();
            int cols = Atlas / Cell;
            var cellTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            for (int i = 0; i < stems.Count; i++)
            {
                cellTex.LoadImage(File.ReadAllBytes(Path.Combine(Folder, stems[i] + ".png")));
                int col = i % cols, row = i / cols;
                int x0 = col * Cell, y0 = Atlas - Cell - row * Cell; // rows fill from the top
                int w = Mathf.Min(cellTex.width, Cell), h = Mathf.Min(cellTex.height, Cell);
                int cx = x0 + (Cell - w) / 2, cy = y0 + (Cell - h) / 2; // smaller art centers
                var src = cellTex.GetPixels32();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        px[(cy + y) * Atlas + cx + x] = src[y * cellTex.width + x];
                rects.Add(new GlyphRect(x0, y0, Cell, Cell));
            }
            Object.DestroyImmediate(cellTex);

            var atlasTex = new Texture2D(Atlas, Atlas, TextureFormat.RGBA32, false);
            atlasTex.SetPixels32(px);
            File.WriteAllBytes(PngPath, atlasTex.EncodeToPNG());
            Object.DestroyImmediate(atlasTex);

            // the importer must not downscale the grid or the rects go stale
            AssetDatabase.ImportAsset(PngPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(PngPath);
            if (imp != null && (imp.maxTextureSize < Atlas || imp.mipmapEnabled))
            {
                imp.maxTextureSize = Atlas;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }

            // ---- refill the SAME asset (guid, material, references all live) ----
            var asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(AssetPath);
            if (asset == null)
            {
                Debug.LogError($"[SpellyZombie] {AssetPath} is missing — the grid png was written " +
                    "but there is no sprite asset to fill. It should exist in the project.");
                return;
            }
            asset.spriteGlyphTable.Clear();
            asset.spriteCharacterTable.Clear();
            for (int i = 0; i < stems.Count; i++)
            {
                var glyph = new TMP_SpriteGlyph
                {
                    index = (uint)i,
                    metrics = new GlyphMetrics(Cell, Cell, 0f, 462.3f, Cell),
                    glyphRect = rects[i],
                    scale = 1f,
                    atlasIndex = 0,
                };
                asset.spriteGlyphTable.Add(glyph);

                // one codepoint = unicode entry; several = name-only
                // (0xFFFE is TMP's "no unicode")
                var parts = stems[i].Substring("emoji_u".Length).Split('_');
                uint unicode = parts.Length == 1
                    ? uint.Parse(parts[0], System.Globalization.NumberStyles.HexNumber)
                    : 0xFFFE;
                var ch = new TMP_SpriteCharacter(unicode, glyph)
                {
                    name = stems[i],
                    glyphIndex = (uint)i,
                    scale = 1f,
                };
                asset.spriteCharacterTable.Add(ch);
            }
            asset.UpdateLookupTables();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpellyZombie] EmojiGrid rebuilt: {stems.Count} sprites — {string.Join(" ", stems.Select(s => s.Substring("emoji_u".Length)))}");
        }
    }
}
