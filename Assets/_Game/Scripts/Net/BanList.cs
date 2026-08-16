using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The host's personal ban list, persisted locally. Banned Steam ids are
    /// kicked the moment they identify on any lobby this player hosts, until
    /// unbanned at the book stand.
    public static class BanList
    {
        const string Key = "sz_bans";

        static Dictionary<ulong, string> _bans; // steamId -> last known name

        static Dictionary<ulong, string> Load()
        {
            if (_bans != null) return _bans;
            _bans = new Dictionary<ulong, string>();
            foreach (var entry in PlayerPrefs.GetString(Key, "").Split('|'))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0) continue;
                if (ulong.TryParse(entry.Substring(0, colon), out var id))
                    _bans[id] = entry.Substring(colon + 1);
            }
            return _bans;
        }

        static void Save()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _bans)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(kv.Key).Append(':').Append(kv.Value.Replace('|', ' ').Replace(':', ' '));
            }
            PlayerPrefs.SetString(Key, sb.ToString());
            PlayerPrefs.Save();
        }

        public static bool Contains(ulong steamId) => steamId != 0 && Load().ContainsKey(steamId);

        public static void Ban(ulong steamId, string name)
        {
            if (steamId == 0) return;
            Load()[steamId] = string.IsNullOrEmpty(name) ? steamId.ToString() : name;
            Save();
        }

        public static void Unban(ulong steamId)
        {
            if (Load().Remove(steamId)) Save();
        }

        public static IReadOnlyDictionary<ulong, string> All => Load();
    }
}
