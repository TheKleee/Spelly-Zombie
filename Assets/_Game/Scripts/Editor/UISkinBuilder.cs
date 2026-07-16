using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// One-click wiring of the imported Kenney "UI Pack — Adventure":
    /// configures every PNG importer (sprite type, 9-slice borders so panels
    /// and bars stretch cleanly) and builds/refreshes the UISkin asset in
    /// _Game/Resources. Re-run any time the pack changes.
    public static class UISkinBuilder
    {
        const string PackDir = "Assets/UI/PNG/Default";
        const string SkinPath = "Assets/_Game/Resources/UISkin.asset";

        [MenuItem("Spelly Zombie/UI/Build UI Skin")]
        public static void Build()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PackDir });
            if (guids.Length == 0)
            {
                Debug.LogError($"[SpellyZombie] No textures under {PackDir} — is the Kenney pack imported?");
                return;
            }

            foreach (var guid in guids)
                ConfigureImporter(AssetDatabase.GUIDToAssetPath(guid));
            AssetDatabase.Refresh();

            System.IO.Directory.CreateDirectory("Assets/_Game/Resources");
            var skin = AssetDatabase.LoadAssetAtPath<UISkin>(SkinPath);
            if (skin == null)
            {
                skin = ScriptableObject.CreateInstance<UISkin>();
                AssetDatabase.CreateAsset(skin, SkinPath);
            }

            Sprite S(string file) =>
                AssetDatabase.LoadAssetAtPath<Sprite>($"{PackDir}/{file}.png");

            skin.PanelBrown = S("panel_brown");
            skin.PanelBrownDark = S("panel_brown_dark");
            skin.PanelGrey = S("panel_grey");
            skin.PanelGreyDark = S("panel_grey_dark");
            skin.PanelBorderBrown = S("panel_border_brown");
            skin.ButtonBrown = S("button_brown");
            skin.ButtonGrey = S("button_grey");
            skin.ButtonRed = S("button_red");
            skin.ProgressRed = S("progress_red");
            skin.ProgressBlue = S("progress_blue");
            skin.ProgressGreen = S("progress_green");
            skin.ProgressWhite = S("progress_white");
            skin.ProgressBack = S("progress_transparent");
            skin.BannerHanging = S("banner_hanging");
            skin.BannerCurtain = S("banner_classic_curtain");
            skin.RoundBrown = S("round_brown");
            skin.RoundGrey = S("round_grey");
            skin.HexBrown = S("hexagon_brown");
            skin.HexGrey = S("hexagon_grey");
            skin.HexRed = S("hexagon_grey_red");
            skin.HexGreen = S("hexagon_grey_green");

            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();
            Debug.Log("[SpellyZombie] UI skin built — the interface wears Kenney Adventure now. " +
                      "(Assign TextFont on the asset when the hand-drawn font is imported.)");
        }

        static void ConfigureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;

            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            Vector4 border = Vector4.zero; // (L, B, R, T)
            if (file.StartsWith("panel_")) border = new Vector4(20, 20, 20, 20);
            else if (file.StartsWith("button_")) border = new Vector4(10, 8, 10, 8);
            else if (file.StartsWith("progress_")) border = new Vector4(6, 10, 6, 10);
            else if (file.StartsWith("banner_")) border = new Vector4(100, 0, 100, 0);
            else if (file.StartsWith("scrollbar_")) border = new Vector4(6, 18, 6, 18);

            bool dirty = importer.textureType != TextureImporterType.Sprite
                || importer.spriteBorder != border
                || importer.mipmapEnabled;
            if (!dirty) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
    }
}
