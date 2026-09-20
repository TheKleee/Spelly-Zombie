using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ AN ANIMATION'S NAME IS ITS STATE'S NAME. Mixamo calls every take
    /// "mixamo.com", and a built game cannot read a controller's states, so the
    /// editor writes each controller's clips with their state names to
    /// StreamingAssets on Play and on build (AnimNamesMirror). Read here.
    public static class AnimNames
    {
        public const string FileName = "sz_anim_names.json";

        [Serializable] public class Clip { public string name = ""; public float length; }
        [Serializable] public class Controller { public string name = ""; public List<Clip> clips = new List<Clip>(); }
        [Serializable] public class Table { public List<Controller> controllers = new List<Controller>(); }

        static Dictionary<string, Controller> _byName;

        static void Load()
        {
            if (_byName != null) return;
            _byName = new Dictionary<string, Controller>();
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, FileName);
                if (!File.Exists(path)) return;
                var table = JsonUtility.FromJson<Table>(File.ReadAllText(path));
                if (table?.controllers == null) return;
                foreach (var c in table.controllers)
                    if (c != null && !string.IsNullOrEmpty(c.name)) _byName[c.name] = c;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] animation names unreadable: {e.Message}");
            }
        }

        /// Every clip of a controller once, in its own order, each with its
        /// state's name; a name that still repeats gets a number.
        public static AnimationClip[] Of(RuntimeAnimatorController rc, out string[] names)
        {
            var clips = new List<AnimationClip>();
            var labels = new List<string>();
            if (rc != null)
            {
                Load();
                _byName.TryGetValue(rc.name, out var known);
                var used = new HashSet<int>();
                var all = rc.animationClips;
                for (int i = 0; i < all.Length; i++)
                {
                    var clip = all[i];
                    if (clip == null || clips.Contains(clip)) continue;
                    string name = clip.name;
                    // the entry at the same place when it is this clip (same length), else the first unused one as long
                    int at = known != null && i < known.clips.Count && Same(known.clips[i], clip) && !used.Contains(i) ? i : -1;
                    if (at < 0 && known != null)
                        for (int j = 0; j < known.clips.Count; j++)
                            if (!used.Contains(j) && Same(known.clips[j], clip)) { at = j; break; }
                    if (at >= 0)
                    {
                        used.Add(at);
                        if (!string.IsNullOrEmpty(known.clips[at].name)) name = known.clips[at].name;
                    }
                    clips.Add(clip);
                    labels.Add(name);
                }
            }
            // "mixamo.com" twice (no table yet, or a state the table missed) still tells them apart
            var seen = new Dictionary<string, int>();
            foreach (var n in labels) seen[n] = seen.TryGetValue(n, out int k) ? k + 1 : 1;
            var count = new Dictionary<string, int>();
            for (int i = 0; i < labels.Count; i++)
            {
                if (seen[labels[i]] < 2) continue;
                count[labels[i]] = count.TryGetValue(labels[i], out int k) ? k + 1 : 1;
                labels[i] = labels[i] + " " + count[labels[i]];
            }
            names = labels.ToArray();
            return clips.ToArray();
        }

        static bool Same(Clip entry, AnimationClip clip) => Mathf.Abs(entry.length - clip.length) < 0.001f;
    }
}
