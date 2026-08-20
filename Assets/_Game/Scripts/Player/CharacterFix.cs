using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Play-mode tweak layer for the runtime-built character: move a fixable
    /// piece (held props, Socket.* contents, IKAnchor_* targets - never
    /// animated bones), save via "Spelly Zombie → Save CHARACTER Fix (play
    /// mode)", and the edit re-applies on every rig build.
    /// Storage: Assets/_Game/Resources/sz_character_fix.json, path-keyed -
    /// renaming rig pieces orphans keys.
    public class CharacterFix : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            public string path;
            public Vector3 pos, euler, scale;
        }

        [System.Serializable]
        public class Table
        {
            public List<Entry> entries = new List<Entry>();
        }

        static readonly List<CharacterFix> Active = new List<CharacterFix>();

        readonly Dictionary<string, Transform> _seen = new Dictionary<string, Transform>();
        Table _table;
        float _nextScan;

        void OnEnable()
        {
            Active.Add(this);
            _nextScan = 0f; // rescan forever at 2 Hz - pieces appear late and get rebuilt
            var saved = Resources.Load<TextAsset>("sz_character_fix");
            _table = saved != null ? JsonUtility.FromJson<Table>(saved.text) : null;
            if (_table == null) _table = new Table();
        }

        void OnDisable() => Active.Remove(this);

        /// A transform may claim: itself or any ancestor (below the
        /// player root) named like a prop/socket/anchor. Bones are not.
        bool IsFixable(Transform t)
        {
            for (var walk = t; walk != null && walk != transform; walk = walk.parent)
            {
                string n = walk.name;
                if (n.StartsWith("IKAnchor_") || n.StartsWith("Socket.")
                    || n == "Wand" || n == "Grimoire" || n == "GooglyEyes") return true;
            }
            return false;
        }

        string PathOf(Transform t)
        {
            string path = t.name + "[" + t.GetSiblingIndex() + "]";
            for (var walk = t.parent; walk != null && walk != transform; walk = walk.parent)
                path = walk.name + "[" + walk.GetSiblingIndex() + "]/" + path;
            return path;
        }

        void LateUpdate()
        {
            // rescan at ~2 Hz and re-apply when a path's Transform was replaced
            // - a rebuilt piece must get its saved offset back
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 0.5f;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!IsFixable(t)) continue;
                string path = PathOf(t);
                if (_seen.TryGetValue(path, out var known) && known == t) continue;
                _seen[path] = t;
                foreach (var e in _table.entries)
                    if (e.path == path)
                    {
                        t.localPosition = e.pos;
                        t.localRotation = Quaternion.Euler(e.euler);
                        t.localScale = e.scale;
                        break;
                    }
            }
        }

#if UNITY_EDITOR
        /// Called by the editor menu in play mode: saves every fixable piece's
        /// current local transform.
        public static int SaveNow()
        {
            // MERGES onto what is already saved; refuses to write when there
            // is no live rig to read
            if (Active.Count == 0)
            {
                Debug.LogError("[SpellyZombie] Save CHARACTER Fix: no live character in the scene. " +
                               "NOTHING was written (your saved fixes are untouched). Enter play mode with a wizard first.");
                return -1;
            }

            // seed from what's on disk - ONCE, outside the instance loop
            var merged = new Dictionary<string, Entry>();
            var loaded = Resources.Load<TextAsset>("sz_character_fix");
            var prior = loaded != null ? JsonUtility.FromJson<Table>(loaded.text) : null;
            if (prior != null && prior.entries != null)
                foreach (var e in prior.entries)
                    if (!string.IsNullOrEmpty(e.path)) merged[e.path] = e;
            int carried = merged.Count;

            foreach (var fix in Active)
            {
                if (fix == null) continue;
                foreach (var pair in fix._seen)
                {
                    if (pair.Value == null) continue;
                    merged[pair.Key] = new Entry
                    {
                        path = pair.Key,
                        pos = pair.Value.localPosition,
                        euler = pair.Value.localRotation.eulerAngles,
                        scale = pair.Value.localScale,
                    };
                }
            }

            var table = new Table();
            table.entries.AddRange(merged.Values);
            string path2 = Application.dataPath + "/_Game/Resources/sz_character_fix.json";
            string tmp = path2 + ".tmp";
            System.IO.File.WriteAllText(tmp, JsonUtility.ToJson(table, true));
            if (System.IO.File.Exists(path2)) System.IO.File.Replace(tmp, path2, null);
            else System.IO.File.Move(tmp, path2);
            Debug.Log($"[SpellyZombie] Character fix saved: {table.entries.Count} piece(s) " +
                      $"({carried} carried over from earlier sessions).");
            return table.entries.Count;
        }
#endif
    }
}
