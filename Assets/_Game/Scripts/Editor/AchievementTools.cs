using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    public static class AchievementTools
    {
        [MenuItem("Spelly Zombie/Studio/Steam - Reset achievements (this account)")]
        static void Reset()
        {
            if (!Application.isPlaying || !SteamManager.Initialized)
            {
                Debug.LogWarning("[SpellyZombie] Reset achievements: enter Play mode with Steam running first.");
                return;
            }
            Achievements.ResetAll();
            Debug.Log($"[SpellyZombie] {Achievements.All.Length} achievements cleared for this Steam account.");
        }

        [MenuItem("Spelly Zombie/Studio/Steam - Print achievement API names")]
        static void Print()
        {
            Debug.Log("[SpellyZombie] achievement API names:\n" + string.Join("\n", Achievements.All));
        }
    }
}
