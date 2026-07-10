using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellyZombie
{
    /// The palette pipeline (one source of color truth):
    ///   1 — Generate Palette PNG: a swatch grid built FROM SurfaceMaterialDB
    ///       (so visuals match the chemistry) + team colors + accents. Marko
    ///       UV-maps his own Blender/Asset Forge models onto these cells; my
    ///       generated materials sample the same rows — everything matches.
    ///   2 — Build Surface Materials: one SZSurface material per material
    ///       type, colored from the same table.
    ///   3 — Apply To Tagged Scene: every SurfaceMaterialTag'd renderer wears
    ///       its material (skips creatures, conjured matter, and ink lines).
    public static class PaletteBuilder
    {
        const string ArtDir = "Assets/_Game/Art";
        const string MatDir = ArtDir + "/Materials";
        const string PalettePath = ArtDir + "/sz_palette.png";
        const int Cell = 8;   // px per swatch
        const int Shades = 8; // ramp steps per row

        static readonly SurfaceMaterialType[] PaletteRows =
        {
            SurfaceMaterialType.Stone, SurfaceMaterialType.Earth,
            SurfaceMaterialType.Wood, SurfaceMaterialType.Metal,
            SurfaceMaterialType.Water, SurfaceMaterialType.Flesh,
            SurfaceMaterialType.Coal, SurfaceMaterialType.Gold,
            SurfaceMaterialType.Diamond, SurfaceMaterialType.Bone,
            SurfaceMaterialType.Slime, SurfaceMaterialType.Unknown,
        };

        // extra rows: warm sand/sky ambient + magic accents (ink gold, rune purple)
        static readonly Color[] AccentRows =
        {
            new Color(0.93f, 0.86f, 0.72f), // ambient sand/plaster
            new Color(0.55f, 0.70f, 0.95f), // sky/ice accents
            new Color(1.00f, 0.80f, 0.25f), // ink gold / treasure
            new Color(0.55f, 0.30f, 0.80f), // arcane purple
        };

        [MenuItem("Spelly Zombie/Art/1 — Generate Palette PNG")]
        public static void GeneratePalette()
        {
            Directory.CreateDirectory(ArtDir);

            int rows = PaletteRows.Length + 1 + AccentRows.Length; // +1 team row
            int w = Shades * Cell, h = rows * Cell;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);

            int row = 0;
            foreach (var type in PaletteRows)
                PaintRamp(tex, row++, SurfaceMaterialDB.Info(type).SolidColor);

            // team row: 4 colors × 2 shades each (colorblind-safe already)
            for (int t = 0; t < MatchLobby.TeamColors.Length; t++)
            {
                var c = MatchLobby.TeamColors[t];
                PaintCell(tex, row, t * 2, c);
                PaintCell(tex, row, t * 2 + 1, Color.Lerp(c, Color.black, 0.35f));
            }
            row++;

            foreach (var accent in AccentRows)
                PaintRamp(tex, row++, accent);

            File.WriteAllBytes(PalettePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(PalettePath);

            if (AssetImporter.GetAtPath(PalettePath) is TextureImporter imp)
            {
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.filterMode = FilterMode.Point; // crisp swatches for UV work
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            Debug.Log($"[SpellyZombie] Palette written: {PalettePath} ({w}×{h}, {rows} rows × {Shades} shades)");
        }

        static void PaintRamp(Texture2D tex, int row, Color baseColor)
        {
            var dark = Color.Lerp(baseColor, Color.black, 0.55f);
            var light = Color.Lerp(baseColor, Color.white, 0.35f);
            for (int s = 0; s < Shades; s++)
                PaintCell(tex, row, s, Color.Lerp(dark, light, s / (float)(Shades - 1)));
        }

        static void PaintCell(Texture2D tex, int row, int col, Color c)
        {
            c.a = 1f;
            // rows top-to-bottom for humans; texture y runs bottom-to-top
            int y0 = tex.height - (row + 1) * Cell;
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                    tex.SetPixel(col * Cell + x, y0 + y, c);
        }

        [MenuItem("Spelly Zombie/Art/2 — Build Surface Materials")]
        public static void BuildMaterials()
        {
            var shader = Shader.Find("SpellyZombie/Surface");
            if (shader == null)
            {
                Debug.LogError("[SpellyZombie] SZSurface.shader missing or failed to compile.");
                return;
            }
            Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            foreach (SurfaceMaterialType type in System.Enum.GetValues(typeof(SurfaceMaterialType)))
            {
                string path = $"{MatDir}/SZ_{type}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_BaseColor", SurfaceMaterialDB.Info(type).SolidColor);
                // per-family grain feel: wood is grainy, metal near-flat
                float grain = type == SurfaceMaterialType.Wood ? 0.08f
                    : type == SurfaceMaterialType.Metal || type == SurfaceMaterialType.Gold
                        || type == SurfaceMaterialType.Diamond ? 0.02f
                    : type == SurfaceMaterialType.Earth ? 0.07f : 0.05f;
                mat.SetFloat("_GrainStrength", grain);
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpellyZombie] Surface materials built in {MatDir}");
        }

        [MenuItem("Spelly Zombie/Art/3 — Apply Materials To Tagged Scene")]
        public static void ApplyToScene()
        {
            int applied = 0;
            foreach (var tag in Object.FindObjectsByType<SurfaceMaterialTag>(FindObjectsSortMode.None))
            {
                if (tag.GetComponentInParent<Creature>() != null) continue; // zombies keep their skins
                if (tag.GetComponent<Matter>() != null) continue;           // conjured matter is runtime-styled

                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/SZ_{tag.Material}.mat");
                if (mat == null) continue;

                foreach (var rend in tag.GetComponentsInChildren<MeshRenderer>())
                {
                    if (rend.GetComponent<LineRenderer>() != null) continue; // never touch ink
                    rend.sharedMaterial = mat;
                    applied++;
                }
            }
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[SpellyZombie] Applied surface materials to {applied} renderers (save the scene to keep them).");
        }
    }
}
