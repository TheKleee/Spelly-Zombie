using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpellyZombie
{
    /// After every desktop build, the folders Unity writes beside the game that must never ship
    /// (IL2CPP's _BackUpThisFolder_ButDontShipItWithYourGame, Burst's _BurstDebugInformation_DoNotShip)
    /// move out of the build folder into "<product> Symbols/<date time>" next to it. The upload never
    /// sees them, and a released build's crash reports can still be read with its own folder.
    public class ShipClean : IPostprocessBuildWithReport
    {
        public int callbackOrder => 1000;

        static readonly string[] NeverShip =
            { "_BackUpThisFolder_ButDontShipItWithYourGame", "_BurstDebugInformation_DoNotShip" };

        // the build folder still to clean: kept in EditorPrefs so a script reload cannot lose it
        const string PendingKey = "SpellyZombie.ShipClean.Pending";

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;
            string output = report.summary.outputPath;
            string buildDir = Directory.Exists(output) ? output : Path.GetDirectoryName(output);
            if (string.IsNullOrEmpty(buildDir)) return;
            EditorPrefs.SetString(PendingKey, buildDir);
            Debug.Log($"[SpellyZombie] build done: what must not ship leaves {buildDir} as soon as the build has finished");
            Arm();
        }

        /// Every script reload re-arms it, so a clean still pending from before the reload happens.
        [InitializeOnLoadMethod]
        static void Arm()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (BuildPipeline.isBuildingPlayer) return;
            EditorApplication.update -= Tick;
            string buildDir = EditorPrefs.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(buildDir)) return;
            EditorPrefs.DeleteKey(PendingKey);
            Move(buildDir);
        }

        static void Move(string buildDir)
        {
            if (!Directory.Exists(buildDir)) return;
            string root = Path.Combine(Path.GetDirectoryName(buildDir) ?? buildDir,
                Application.productName + " Symbols", DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
            int moved = 0;
            foreach (var dir in Directory.GetDirectories(buildDir))
            {
                string name = Path.GetFileName(dir);
                if (!Array.Exists(NeverShip, s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                try
                {
                    Directory.CreateDirectory(root);
                    string to = Path.Combine(root, name);
                    try { Directory.Move(dir, to); }
                    catch (IOException) { CopyAll(dir, to); Directory.Delete(dir, true); } // another drive
                    moved++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SpellyZombie] {name} is still in the build folder and must not be uploaded: {e.Message}");
                }
            }
            Debug.Log(moved > 0
                ? $"[SpellyZombie] {moved} folder(s) that must not ship moved out of the build to {root}"
                : $"[SpellyZombie] {buildDir} holds nothing that must not ship");
        }

        static void CopyAll(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyAll(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
