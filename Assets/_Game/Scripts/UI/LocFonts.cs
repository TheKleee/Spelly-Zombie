using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpellyZombie
{
    /// The font follows the language: Noto for the four Asian scripts, the
    /// skin font elsewhere. Legacy UI.Text swaps its font; TextMesh Pro puts
    /// the language's SDF first in the default fallback chain, so shared
    /// Chinese and Japanese code points draw in the right national form.
    public static class LocFonts
    {
        static bool _hooked;
        static Font _last;

        static bool Asian(string code) => code == "ja" || code == "zh-CN" || code == "zh-TW" || code == "ko";

        public static Font LegacyFor(string code)
        {
            var skin = UISkin.I;
            Font pick = null;
            if (skin != null)
                switch (code)
                {
                    case "ja": pick = skin.JapaneseFont; break;
                    case "zh-CN": pick = skin.ChineseSimplifiedFont; break;
                    case "zh-TW": pick = skin.ChineseTraditionalFont; break;
                    case "ko": pick = skin.KoreanFont; break;
                    case "en": break;
                    default: pick = skin.OtherFont; break;
                }
            if (pick == null && Asian(code))
                Debug.LogError($"[SpellyZombie] UISkin has no font for language '{code}'. Assign the Noto font on the skin or its text shows as boxes.");
            if (pick == null)
                pick = skin != null && skin.TextFont != null ? skin.TextFont
                     : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return pick;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Loc.Changed += Apply;
            Apply();
        }

        /// Re-dresses live legacy texts and reorders the TMP fallback chain.
        public static void Apply()
        {
            Font before = UIKit.CachedFont ?? _last;
            UIKit.ForgetFont();
            Font now = UIKit.Font;
            _last = now;
            // texts authored in the UI prefab carry the base font; while an
            // Asian language is up they follow the pick too, from the first frame
            Font baseFont = UISkin.I != null && UISkin.I.TextFont != null
                ? UISkin.I.TextFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bool swapBefore = before != null && before != now;
            bool swapBase = now != baseFont;
            if (swapBefore || swapBase)
                foreach (var t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if ((swapBefore && t.font == before) || (swapBase && t.font == baseFont)) t.font = now;

            var skin = UISkin.I;
            var def = TMPro.TMP_Settings.defaultFontAsset;
            if (skin == null || def == null) return;
            TMPro.TMP_FontAsset first = null;
            switch (Loc.LanguageCode)
            {
                case "ja": first = skin.JapaneseTMP; break;
                case "zh-CN": first = skin.ChineseSimplifiedTMP; break;
                case "zh-TW": first = skin.ChineseTraditionalTMP; break;
                case "ko": first = skin.KoreanTMP; break;
            }
            if (first == null)
            {
                if (Asian(Loc.LanguageCode))
                    Debug.LogWarning($"[SpellyZombie] UISkin has no TMP font for '{Loc.LanguageCode}': run Spelly Zombie/Localization/Wire Selected Font Into TMP Fallback on its Noto font and assign the SDF asset on the skin.");
                return;
            }
            var table = def.fallbackFontAssetTable;
            if (table == null) { table = new List<TMPro.TMP_FontAsset>(); def.fallbackFontAssetTable = table; }
            table.Remove(first);
            table.Insert(0, first);
        }
    }
}
