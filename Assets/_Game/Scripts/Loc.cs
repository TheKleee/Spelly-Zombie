using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ONE table for every player-facing string (Marko's launch plan: 12
    /// languages, minimal words — images do the teaching). Use Loc.T("key")
    /// or Loc.F("key", args) for strings with numbers/names in them.
    ///
    /// ADDING A LANGUAGE IS DROPPING ONE FILE, no code:
    ///   Assets/_Game/Resources/Custom/Loc_<code>.txt     e.g. Loc_ja.txt
    /// one line per entry:
    ///   door.open = ドアを開ける
    /// ({0}/{1} placeholders survive translation; # starts a comment line).
    /// Language follows the OS; PlayerPrefs "sz_lang" (e.g. "ja") overrides.
    /// A missing file or missing key falls back to the built-in English —
    /// the game never shows a blank.
    /// Launch codes: en ja es zh-CN ko fr it de ar pt-BR ru zh-TW.
    public static class Loc
    {
        static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            // ---- interact prompts (the [E] bar) ----
            ["door.open"] = "open the door",
            ["door.close"] = "close the door",
            ["pickup.weapon"] = "pick up the weapon",
            ["pickup.full"] = "hands full — drop one first",
            ["revive.friend"] = "hold to revive your friend",
            ["revive.pct"] = "reviving…  {0}%",
            ["team.wear"] = "wear TEAM {0}'s color",
            ["chest.try"] = "try the mystery chest  ({0} riches)",
            ["perk.drink"] = "drink {0}  ({1} riches)",
            ["perk.brewed"] = "{0} — already brewed",
            ["grimoire.open"] = "open the grimoire",
            ["grimoire.close"] = "close the grimoire  ·  , and . turn pages",
            ["pose.grab"] = "grab limbs · tap 1-9 = load · HOLD 1-9 = save · F = relax · R = done",
            ["pose.key"] = "pose mode · F = relax · X = reset this key",
            ["pose.enter"] = "pose mode — sculpt your wizard, save to keys 1-9",
        };

        static Dictionary<string, string> _active; // loaded translation (null = English)
        static bool _loaded;

        public static string T(string key)
        {
            Load();
            if (_active != null && _active.TryGetValue(key, out var s)) return s;
            return _en.TryGetValue(key, out var e) ? e : key;
        }

        public static string F(string key, params object[] args)
            => string.Format(T(key), args);

        /// The Loc_<code>.txt suffix in use ("en" loads nothing).
        public static string LanguageCode
        {
            get
            {
                var forced = PlayerPrefs.GetString("sz_lang", "");
                if (!string.IsNullOrEmpty(forced)) return forced;
                switch (Application.systemLanguage)
                {
                    case SystemLanguage.Japanese: return "ja";
                    case SystemLanguage.Spanish: return "es";
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseSimplified: return "zh-CN";
                    case SystemLanguage.ChineseTraditional: return "zh-TW";
                    case SystemLanguage.Korean: return "ko";
                    case SystemLanguage.French: return "fr";
                    case SystemLanguage.Italian: return "it";
                    case SystemLanguage.German: return "de";
                    case SystemLanguage.Arabic: return "ar";
                    case SystemLanguage.Portuguese: return "pt-BR";
                    case SystemLanguage.Russian: return "ru";
                    default: return "en";
                }
            }
        }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string code = LanguageCode;
            if (code == "en") return;
            var file = Resources.Load<TextAsset>("Custom/Loc_" + code);
            if (file == null) return;
            _active = new Dictionary<string, string>();
            foreach (var raw in file.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                _active[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            Debug.Log($"[SpellyZombie] Loc: '{code}' loaded ({_active.Count} strings)");
        }
    }
}
