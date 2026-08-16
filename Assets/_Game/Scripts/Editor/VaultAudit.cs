using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// THE SILENT-FAILURE KILLER (AXIOM, ). Dropping a prefab into
    /// Resources/Custom with a name no hook asks for loads NOTHING and says
    /// nothing - the GooglyEyes.prefab sat unused for weeks because the hook
    /// wanted "Eyes". This warns the moment a file lands, names the closest
    /// valid hook, and costs the shipped build exactly nothing (editor-only).
    class VaultAudit : AssetPostprocessor
    {
        const string Dir = "Assets/_Game/Resources/Custom";

        static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            foreach (var path in imported) Check(path);
            foreach (var path in moved) Check(path);
        }

        static void Check(string path)
        {
            if (!path.StartsWith(Dir) || !path.EndsWith(".prefab")) return;
            // ignore subfolders (Materials/ etc) and _-prefixed scratch files
            string rest = path.Substring(Dir.Length + 1);
            if (rest.Contains("/") || rest.StartsWith("_")) return;

            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            var known = KnownNames();
            if (known.Contains(name)) return;
            if (name.StartsWith("GrimoirePage_")
             || name.StartsWith("Z") && name.Contains("_")) return; // costume props

            string best = known.OrderBy(k => Distance(k.ToLowerInvariant(), name.ToLowerInvariant()))
                               .FirstOrDefault();
            Debug.LogWarning($"[SpellyZombie] Resources/Custom/{name} is not a hook name. NOTHING loads it." +
                             (best != null ? $" Closest hook: \"{best}\"." : "") +
                             " See the list at the top of PrefabVault.cs.",
                             AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }

        static HashSet<string> KnownNames()
        {
            var set = new HashSet<string>
            {
                "PlayerBody", "ZombieBody", "Wand", "Grimoire", "Eyes", "GooglyEyes",
                "Chest", "InkRuneStone", "Cauldron", "SpellPage", "SealTablet",
                "RuneChamber", "FX_StateBlob", "ScribblerHat",
            };
            foreach (ParticleKind k in System.Enum.GetValues(typeof(ParticleKind)))
                set.Add("FX_" + k);
            // every GrammarField subclass, wherever it lives
            var fieldBase = typeof(GrammarField);
            foreach (var t in fieldBase.Assembly.GetTypes())
                if (t.IsSubclassOf(fieldBase)) set.Add("FX_" + t.Name);
            foreach (RuneGrammar.ExoticKind e in System.Enum.GetValues(typeof(RuneGrammar.ExoticKind)))
                set.Add("FX_" + e);
            return set;
        }

        /// tiny Levenshtein — good enough to say "did you mean Eyes?"
        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Mathf.Min(Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                        d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
