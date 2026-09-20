using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE HOST'S LAST SETUP, KEPT (his call): lobby name, size, match
    /// length, map, acolyte share, region, language and tags come back the next
    /// time the game starts, so friends who always play the same way never set
    /// it up again. Written only while this machine is the one setting them
    /// (offline or hosting): a joined player's copy of someone else's settings
    /// never lands in its own. The password is never kept.
    public static class HostSettings
    {
        const string P = "sz_host_";
        static string _written;
        static string _map;      // the saved map, handed to the lobby once
        static float _nextLook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            if (PlayerPrefs.HasKey(P + "name")) SteamLobby.PendingName = PlayerPrefs.GetString(P + "name");
            if (PlayerPrefs.HasKey(P + "size"))
                SteamLobby.PendingSize = Mathf.Clamp(PlayerPrefs.GetInt(P + "size"), 2, SteamLobby.MaxPlayers);
            if (PlayerPrefs.HasKey(P + "minutes"))
                MatchLobby.DurationMin = Mathf.Clamp(PlayerPrefs.GetInt(P + "minutes"), 0, 15);
            if (PlayerPrefs.HasKey(P + "acolytes"))
                MatchLobby.AcolytePercent = Mathf.Clamp(PlayerPrefs.GetInt(P + "acolytes"), 10, 90);
            if (PlayerPrefs.HasKey(P + "region")) SteamLobby.PendingRegion = PlayerPrefs.GetString(P + "region");
            if (PlayerPrefs.HasKey(P + "lang")) SteamLobby.PendingLang = PlayerPrefs.GetString(P + "lang");
            if (PlayerPrefs.HasKey(P + "tags")) SteamLobby.PendingTags = PlayerPrefs.GetInt(P + "tags");
            _map = PlayerPrefs.GetString(P + "map", "");
            if (_map.Length == 0) _map = null;
            _written = Snapshot();
        }

        /// The last session's map, once: the lobby picks it on its first build.
        public static string TakeMap()
        {
            string map = _map;
            _map = null;
            return map;
        }

        /// The lobby asks twice a second; anything the host changed is written.
        public static void Keep()
        {
            if (NetGame.Connected && !NetGame.IsHost) return;
            if (Time.unscaledTime < _nextLook) return;
            _nextLook = Time.unscaledTime + 0.5f;
            string now = Snapshot();
            if (now == _written) return;
            _written = now;
            PlayerPrefs.SetString(P + "name", SteamLobby.PendingName ?? "");
            PlayerPrefs.SetInt(P + "size", SteamLobby.PendingSize);
            PlayerPrefs.SetInt(P + "minutes", MatchLobby.DurationMin);
            PlayerPrefs.SetInt(P + "acolytes", MatchLobby.AcolytePercent);
            PlayerPrefs.SetString(P + "region", SteamLobby.PendingRegion ?? "");
            PlayerPrefs.SetString(P + "lang", SteamLobby.PendingLang ?? "");
            PlayerPrefs.SetInt(P + "tags", SteamLobby.PendingTags);
            PlayerPrefs.SetString(P + "map", MatchLobby.SelectedMap ?? "");
            PlayerPrefs.Save();
        }

        static string Snapshot() =>
            $"{SteamLobby.PendingName}|{SteamLobby.PendingSize}|{MatchLobby.DurationMin}|{MatchLobby.AcolytePercent}|" +
            $"{SteamLobby.PendingRegion}|{SteamLobby.PendingLang}|{SteamLobby.PendingTags}|{MatchLobby.SelectedMap}";
    }
}
