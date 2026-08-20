using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// UI skin: sprite references built by "Spelly Zombie/UI/Build UI Skin"
    /// from the Kenney Adventure pack, loaded at runtime from Resources.
    /// Swapping a sprite or the font here re-dresses the whole interface.
    public class UISkin : ScriptableObject
    {
        [Header("Panels")]
        public Sprite PanelBrown;
        public Sprite PanelBrownDark;
        public Sprite PanelGrey;
        public Sprite PanelGreyDark;
        public Sprite PanelBorderBrown;

        [Header("Buttons")]
        public Sprite ButtonBrown;
        public Sprite ButtonGrey;
        public Sprite ButtonRed;

        [Header("Progress bars")]
        public Sprite ProgressRed;
        public Sprite ProgressBlue;
        public Sprite ProgressGreen;
        public Sprite ProgressWhite;
        public Sprite ProgressBack;

        [Header("Banners")]
        public Sprite BannerHanging;
        public Sprite BannerCurtain;

        [Header("Badges")]
        public Sprite RoundBrown;
        public Sprite RoundGrey;
        public Sprite HexBrown;
        public Sprite HexGrey;
        public Sprite HexRed;
        public Sprite HexGreen;

        [Header("Type: assign the hand-drawn font when it lands")]
        public Font TextFont;

        [Header("the UI prefab: when assigned, it IS the interface")]
        [Tooltip("Drag your edited SZ_UI prefab here. The game instantiates it untouched. Code only rebinds button clicks, feeds live values into existing texts (counters, timers), and adds elements the prefab doesn't have. Empty = code-built UI.")]
        public GameObject UIPrefab;

        [Header("the surface prefabs: prefab NAME = the UI group it replaces")]
        [Tooltip("Drop ANY menu/submenu/popup prefab here. Whenever the game is about to code-build a UI group whose name matches a prefab in this list, YOUR prefab is instantiated instead, untouched. Known names: MainMenu, Settings, PauseMenu, HUD, Vitals, RoundBanner, Downed, LobbyBanner, LobbyBoard, NetPanel, PromptGroup, RuneChooser, PowerupChooser, Announcement, SealGallery. Buttons and live texts wire up by name + sibling order.")]
        public List<GameObject> SurfacePrefabs = new List<GameObject>();

        /// The prefab for a named UI surface; null = code builds it.
        public static GameObject SurfacePrefab(string name)
        {
            var s = I;
            if (s == null || s.SurfacePrefabs == null) return null;
            foreach (var p in s.SurfacePrefabs)
                if (p != null && p.name == name) return p;
            return null;
        }

        static UISkin _loaded;
        static bool _tried;

        /// Null until "Build UI Skin" has been run; UIKit then falls back to flat colors.
        public static UISkin I
        {
            get
            {
                if (_loaded == null && !_tried)
                {
                    _tried = true;
                    _loaded = Resources.Load<UISkin>("UISkin");
                }
                return _loaded;
            }
        }
    }
}
