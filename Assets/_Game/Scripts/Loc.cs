using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Every player-facing string. Loc.T("key"), or Loc.F("key", args) when
    /// it contains {0} placeholders.
    ///
    /// Translations are JSON in StreamingAssets/Loc, one file per language,
    /// switchable at runtime. A missing file or key falls back to English.
    public static class Loc
    {
        static readonly Dictionary<string, string> _en = new Dictionary<string, string>
        {
            // ---- interact prompts (the [E] bar) ----
            ["door.open"] = "open the door",
            ["door.close"] = "close the door",
            ["pickup.weapon"] = "pick up the weapon",
            ["pickup.full"] = "hands full, drop one first",
            ["revive.friend"] = "hold to revive your friend",
            ["revive.pct"] = "reviving…  {0}%",
            ["chest.try"] = "try the mystery chest",
            ["perk.drink"] = "drink {0}",
            ["perk.brewed"] = "{0} is already brewed",
            ["grimoire.open"] = "open the grimoire",
            ["grimoire.close"] = "close the grimoire",
            ["chip.done"] = "done",
            ["carry.down"] = "put it down",

            // ---- one-fact chips, three on screen at most ----
            ["scan.aim"] = "scan it, become it",
            ["absorb.aim"] = "absorb it, learn its rune",
            ["chip.grimoire"] = "grimoire",
            ["chip.paint"] = "paint your body",
            ["chip.first"] = "first person",
            ["chip.third"] = "third person",
            ["chip.pose"] = "pose your wizard",
            ["chip.watch"] = "watch your dead",
            ["chip.become"] = "become it again",
            ["chip.melt"] = "melt back to idle",
            ["chip.precise"] = "faster drawing",
            ["chip.erase"] = "erase ink",
            ["chip.absorb"] = "absorb it",

            // ---- loading screen tips, one shown at random per load ----
            ["hint.alt"] = "hold ALT to draw faster",
            ["hint.combine"] = "draw more runes inside of the same seal to combine them",
            ["hint.lift"] = "draw ink on things and press E to lift them",
            ["hint.erase"] = "erasing returns the ink to your wand",
            ["hint.body"] = "press R to paint runes on your own body",
            ["hint.pose"] = "striking a pose can close a body seal and cast it",
            ["hint.size"] = "bigger runes make stronger spells",
            ["hint.touch"] = "lines count as one drawing only when they touch",
            ["hint.declare"] = "the book can name a drawing that reads wrong",
            ["hint.trance"] = "fresh ink puts zombies in a trance",
            ["hint.wake"] = "throw a sleeping spell to wake it",
            ["hint.ghost"] = "the dead rise as ghosts. fly home to your body and a friend can revive you",
            ["hint.doors"] = "doors open when you walk into them",
            ["paint.done"] = "done painting",
            ["paint.pose"] = "strike a pose",
            ["paint.orbit"] = "orbit",
            ["hat.pillar"] = "pick your hat color",
            ["side.pillar"] = "change your side",
            ["hat.done"] = "done",
            ["shape.back"] = "back to yourself",
            ["shape.turn"] = "turns you",
            ["shape.save"] = "saves",
            ["shape.recall"] = "recalls",

            // ---- pause menu and options ----
            ["menu.resume"] = "Resume",
            ["menu.restart"] = "Restart run",
            ["menu.options"] = "Options",
            ["menu.wishlist"] = "♥ Wishlist on Steam",
            ["menu.quit"] = "Quit",
            ["menu.back"] = "Back",
            ["opt.sens"] = "Look sensitivity: {0}",
            ["opt.volume"] = "Volume: {0}%",
            ["opt.language"] = "Language: {0}",
            ["opt.immersive.on"] = "Immersive mode: ON",
            ["opt.immersive.off"] = "Immersive mode: OFF",
            ["opt.immersive.hint"] = "no HUD at all. for players who know the game",
            ["inspect.add"] = "add friend on Steam",
            ["inspect.close"] = "I closes",
            ["menu.leave"] = "Leave lobby",
            ["menu.delete"] = "Delete lobby",
            ["menu.play"] = "PLAY",
            ["lobby.readycall"] = "ready check. B yes, C no",
            ["lobby.ready.on"] = "READY {0}/{1}",
            ["lobby.ready.off"] = "READY {0}/{1}. B when ready",

            // ---- the book stand (host controller) ----
            ["stand.title"] = "the book stand",
            ["stand.hostprivate"] = "create private lobby (invite only)",
            ["stand.hostpublic"] = "CREATE PUBLIC LOBBY",
            ["stand.pw"] = "password (optional)",
            ["stand.code"] = "entry code (optional)",
            ["stand.botmatch"] = "test match vs a bot",
            ["stand.hint"] = "walk away to close",
            ["stand.map"] = "change map",
            ["stand.seed"] = "seed",
            ["stand.share"] = "acolytes at least {0}%",
            ["stand.setcode"] = "set code",
            ["stand.readycall"] = "ready check",
            ["stand.invite"] = "invite friends",
            ["stand.start"] = "START",
            ["stand.waiting"] = "waiting for ready",
            ["stand.delete"] = "delete lobby",
            ["stand.kick"] = "kick",
            ["stand.ban"] = "ban",
            ["stand.banned"] = "banned: {0}",
            ["stand.unban"] = "unban",
            ["stand.name"] = "lobby name",
            ["stand.size"] = "size {0}",
            ["stand.region"] = "region: {0}",
            ["stand.tab.host"] = "HOST",
            ["stand.tab.join"] = "JOIN",
            ["stand.settings"] = "Settings",
            ["stand.regions"] = "Regions",
            ["stand.langs"] = "Languages",
            ["stand.behaviors"] = "Behaviors",
            ["stand.duration"] = "time {0} min",
            ["stand.setpw"] = "set password",
            ["stand.hosting"] = "HOSTING your lobby",
            ["stand.players"] = "Players",
            ["filter.all"] = "all",

            // ---- lobby browser ----
            ["browse.refresh"] = "refresh",
            ["browse.join"] = "JOIN",
            ["browse.locked"] = "(password)",
            ["browse.none"] = "no lobbies found. host one!",
            ["browse.needpw"] = "this lobby wants a password",
            ["browse.cancel"] = "cancel",
            ["region."] = "any region",
            ["region.eu"] = "Europe",
            ["region.na"] = "North America",
            ["region.sa"] = "South America",
            ["region.asia"] = "Asia",
            ["region.oce"] = "Oceania",
            ["region.mea"] = "Middle East & Africa",
            ["tag.welcome"] = "everyone welcome",
            ["tag.beginners"] = "beginners welcome",
            ["tag.casual"] = "casual fun",
            ["tag.tryhard"] = "try hards",
            ["tag.mic"] = "mic on",
            ["tag.quiet"] = "quiet ok",
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

        // ---- the languages ----
        // Each named in its own script. English is index 0 and compiled in,
        // so the game shows text even with no files on disk.
        public struct Lang
        {
            public string Code, Native;
            public Lang(string code, string native) { Code = code; Native = native; }
        }

        public static readonly Lang[] Languages =
        {
            new Lang("en", "English"),
            new Lang("ja", "日本語"),
            new Lang("zh-CN", "简体中文"),
            new Lang("zh-TW", "繁體中文"),
            new Lang("ko", "한국어"),
            new Lang("es", "Español"),
            new Lang("pt-BR", "Português (BR)"),
            new Lang("fr", "Français"),
            new Lang("de", "Deutsch"),
            new Lang("it", "Italiano"),
            new Lang("ru", "Русский"),
            new Lang("tr", "Türkçe"),
            new Lang("ar", "العربية"),
        };

        /// Fired after a language change so open UI can rebuild its labels.
        public static event System.Action Changed;

        /// PlayerPrefs override, else the OS language.
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
                    case SystemLanguage.Turkish: return "tr";
                    default: return "en";
                }
            }
        }

        public static string NativeName(string code)
        {
            foreach (var l in Languages) if (l.Code == code) return l.Native;
            return code;
        }

        /// Swaps the language live and rebuilds listeners. Persisted.
        public static void SetLanguage(string code)
        {
            PlayerPrefs.SetString("sz_lang", code ?? "en");
            PlayerPrefs.Save();
            _loaded = false;
            _active = null;
            Load();
            Changed?.Invoke();
        }

        public static string NextLanguage()
        {
            string cur = LanguageCode;
            for (int i = 0; i < Languages.Length; i++)
                if (Languages[i].Code == cur)
                    return Languages[(i + 1) % Languages.Length].Code;
            return "en";
        }

        // ---- the files ----
        //   {persistentDataPath}/Loc/sz_loc_ja.json   overrides
        //   {StreamingAssets}/Loc/sz_loc_ja.json      shipped
        // Same shape as sz_tuning.json so JsonUtility reads it directly.
        [System.Serializable] class LocEntry { public string key; public string value; }
        [System.Serializable] class LocFile { public LocEntry[] entries; }

        public static string FileNameFor(string code) => "sz_loc_" + code + ".json";

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string code = LanguageCode;
            if (code == "en") return; // compiled in

            string name = FileNameFor(code);
            string user = System.IO.Path.Combine(
                System.IO.Path.Combine(Application.persistentDataPath, "Loc"), name);
            string shipped = System.IO.Path.Combine(
                System.IO.Path.Combine(Application.streamingAssetsPath, "Loc"), name);

            string path = System.IO.File.Exists(user) ? user
                : System.IO.File.Exists(shipped) ? shipped : null;
            if (path == null)
            {
                Debug.LogWarning($"[SpellyZombie] Loc: no file for '{code}' — "
                    + $"expected {name} in {user} or {shipped}. Showing English.");
                return;
            }

            try
            {
                var f = JsonUtility.FromJson<LocFile>(System.IO.File.ReadAllText(path,
                    System.Text.Encoding.UTF8));
                if (f == null || f.entries == null) return;
                _active = new Dictionary<string, string>();
                foreach (var e in f.entries)
                    if (!string.IsNullOrEmpty(e.key)) _active[e.key] = e.value;
                Debug.Log($"[SpellyZombie] Loc: '{code}' loaded ({_active.Count} strings) from {path}");
            }
            catch (System.Exception ex)
            {
                _active = null;
                Debug.LogError($"[SpellyZombie] Loc: '{code}' file is broken ({ex.Message}) — "
                    + "showing English. Check the JSON commas and quotes.");
            }
        }

        /// Every key with its English text, as a translator-ready JSON file.
        public static string EnglishTemplateJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n  \"entries\": [\n");
            int i = 0;
            foreach (var kv in _en)
            {
                sb.Append("    {\"key\": \"").Append(kv.Key).Append("\", \"value\": \"")
                  .Append(kv.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}");
                if (++i < _en.Count) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        public static int KeyCount => _en.Count;
    }
}
