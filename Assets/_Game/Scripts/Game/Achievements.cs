using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace SpellyZombie
{
    /// Steam achievements. Each constant is the API name to create on the
    /// Steamworks page; the game only calls SetAchievement by that name.
    /// Unlocks are mirrored in PlayerPrefs per Steam account so Steam is
    /// asked once per deed. Without Steam nothing happens.
    public static class Achievements
    {
        public enum Ending : byte { None = 0, PotDry = 1, NoWizards = 2, Sweep = 3, GreenBell = 4, CleanBell = 5 }

        // ways to win
        public const string WinWizards = "SZ_WIN_WIZARDS";
        public const string WinAcolytes = "SZ_WIN_ACOLYTES";
        public const string EndPotDry = "SZ_END_POT_DRY";
        public const string EndNoWizards = "SZ_END_NO_WIZARDS";
        public const string EndGreenBell = "SZ_END_GREEN_BELL";
        public const string EndCleanBell = "SZ_END_CLEAN_BELL";
        public const string EndSweep = "SZ_END_SWEEP";
        public const string TenWins = "SZ_TEN_WINS";

        // things to do
        public const string FirstRune = "SZ_FIRST_RUNE";
        public const string AllRunes = "SZ_ALL_RUNES";
        public const string FirstSpell = "SZ_FIRST_SPELL";
        public const string BodyCast = "SZ_BODY_CAST";
        public const string Disguise = "SZ_DISGUISE";
        public const string RideZombie = "SZ_RIDE_ZOMBIE";
        public const string RideGolem = "SZ_RIDE_GOLEM";
        public const string GolemBorn = "SZ_GOLEM_BORN";
        public const string ReviveFriend = "SZ_REVIVE_FRIEND";
        public const string CameBack = "SZ_CAME_BACK";
        public const string FatBounce = "SZ_FAT_BOUNCE";
        public const string PoisonPot = "SZ_POISON_POT";
        public const string CleanPot = "SZ_CLEAN_POT";

        public static readonly string[] All =
        {
            WinWizards, WinAcolytes, EndPotDry, EndNoWizards, EndGreenBell, EndCleanBell, EndSweep, TenWins,
            FirstRune, AllRunes, FirstSpell, BodyCast, Disguise, RideZombie, RideGolem, GolemBorn,
            ReviveFriend, CameBack, FatBounce, PoisonPot, CleanPot,
        };

        static readonly HashSet<string> _done = new HashSet<string>();
        static readonly HashSet<string> _refused = new HashSet<string>();

        static string Prefix()
        {
            ulong sid = SteamManager.Initialized ? SteamUser.GetSteamID().m_SteamID : 0UL;
            return "sz_ach_" + sid + "_";
        }

        public static bool Has(string id) => _done.Contains(id) || PlayerPrefs.GetInt(Prefix() + id, 0) == 1;

        public static void Unlock(string id)
        {
            if (string.IsNullOrEmpty(id) || _done.Contains(id)) return;
            string key = Prefix() + id;
            if (PlayerPrefs.GetInt(key, 0) == 1) { _done.Add(id); return; }
            if (!SteamManager.Initialized) return;
            if (SteamUserStats.GetAchievement(id, out bool have) && have)
            {
                Remember(key, id);
                return;
            }
            if (!SteamUserStats.SetAchievement(id))
            {
                if (_refused.Add(id))
                    Debug.LogWarning($"[SpellyZombie] Steam refused achievement '{id}'. Create it with that API name on the Steamworks page of app {SteamUtils.GetAppID()}.");
                return;
            }
            SteamUserStats.StoreStats();
            Remember(key, id);
            Debug.Log($"[SpellyZombie] achievement {id}");
        }

        static void Remember(string key, string id)
        {
            _done.Add(id);
            PlayerPrefs.SetInt(key, 1);
        }

        /// A per-account counter for the deeds that take several tries.
        public static int Bump(string counter)
        {
            string key = Prefix() + "n_" + counter;
            int n = PlayerPrefs.GetInt(key, 0) + 1;
            PlayerPrefs.SetInt(key, n);
            return n;
        }

        /// Clears every achievement and counter for this Steam account. Testing only.
        public static void ResetAll()
        {
            if (SteamManager.Initialized)
            {
                foreach (var id in All) SteamUserStats.ClearAchievement(id);
                SteamUserStats.StoreStats();
            }
            string p = Prefix();
            foreach (var id in All) PlayerPrefs.DeleteKey(p + id);
            PlayerPrefs.DeleteKey(p + "n_wins");
            _done.Clear();
            _refused.Clear();
        }

        // ---- deeds ----

        /// Host and clients both call this once per match with the referee's ending.
        public static void MatchEnded(int winner, Ending ending)
        {
            if (winner != 1 && winner != 2) return;
            bool acolyte = Sides.Local == Side.Acolyte;
            if (acolyte != (winner == 2)) return; // the losing side earns nothing
            Unlock(acolyte ? WinAcolytes : WinWizards);
            switch (ending)
            {
                case Ending.PotDry: Unlock(EndPotDry); break;
                case Ending.NoWizards: Unlock(EndNoWizards); break;
                case Ending.Sweep: Unlock(EndSweep); break;
                case Ending.GreenBell: Unlock(EndGreenBell); break;
                case Ending.CleanBell: Unlock(EndCleanBell); break;
            }
            if (Bump("wins") >= 10) Unlock(TenWins);
        }

        public static void RuneLearned(int known)
        {
            Unlock(FirstRune);
            if (known >= 12) Unlock(AllRunes);
        }

        // ---- polled every frame by RoundDirector ----
        static bool _wasCorrupt, _corruptKnown;
        static float _golemPoll;

        public static void Tick()
        {
            if (!RoundDirector.RunActive) { _corruptKnown = false; return; }

            bool corrupt = CauldronEconomy.IsCorrupt;
            if (_corruptKnown && corrupt != _wasCorrupt)
            {
                bool acolyte = Sides.Local == Side.Acolyte;
                if (corrupt && acolyte) Unlock(PoisonPot);
                if (!corrupt && !acolyte) Unlock(CleanPot);
            }
            _wasCorrupt = corrupt;
            _corruptKnown = true;

            _golemPoll -= Time.deltaTime;
            if (_golemPoll > 0f || Has(GolemBorn)) return;
            _golemPoll = 1f;
            int me = Grimoire.LocalPlayerId;
            if (NetGame.IsAuthority)
            {
                foreach (var g in Golem.All)
                    if (g != null && g.Alive && g.OwnerId == me) { Unlock(GolemBorn); return; }
            }
            else if (NetSync.AnyGolemOwnedBy(me)) Unlock(GolemBorn);
        }
    }
}
