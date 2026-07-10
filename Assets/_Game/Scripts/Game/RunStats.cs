using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// One CSV row per finished run — the playtest evidence for tuning and the
    /// GO/NO-GO gate. Lives at persistentDataPath/runstats.csv; open in any
    /// spreadsheet. Columns: when, outcome, round reached, kills, combos,
    /// riches, minutes.
    public static class RunStats
    {
        static string PathFile => Path.Combine(Application.persistentDataPath, "runstats.csv");

        public static void Log(string outcome, int round, int kills, int combos, int riches, float seconds)
        {
            try
            {
                bool fresh = !File.Exists(PathFile);
                using (var w = File.AppendText(PathFile))
                {
                    if (fresh) w.WriteLine("when,outcome,round,kills,combos,riches,minutes");
                    w.WriteLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm},{outcome},{round},{kills},{combos},{riches},{seconds / 60f:0.0}");
                }
                Debug.Log($"[SpellyZombie] Run logged → {PathFile}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] RunStats write failed: {e.Message}");
            }
        }
    }
}
