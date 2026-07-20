using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// MARKO'S ULTIMATE-CONTROL LAYER for the runtime-built character. His
    /// ruling (July 17): "I must be in full control of all creative decisions
    /// anywhere" — and prefabbing the runtime player can NEVER work (the rig
    /// is assembled at runtime; runtime-added components serialize broken:
    /// that's the "managed part missing" error, by design not by bug).
    ///
    /// THE WORKFLOW INSTEAD:
    ///   Play → Hierarchy → SZ_Player → select any FIXABLE piece (wand,
    ///   grimoire, eyes, anything under a Socket.*, any IKAnchor_* stance
    ///   target) → move/rotate/scale it in the Inspector until it looks
    ///   right → menu "Spelly Zombie → Save CHARACTER Fix (play mode)" →
    ///   stop play. The edit re-applies on every rig build, forever.
    ///
    /// SCOPE (deliberate): held props, worn socket pieces, the googly-eye
    /// rig, and the IK stance anchors (where the wand/book HOVER in view —
    /// raise IKAnchor_ReadSupport and the open book rides higher). Animated
    /// BONES are excluded on purpose: poses and emotes own those.
    ///
    /// Storage: Assets/_Game/Resources/sz_character_fix.json, path-keyed.
    /// LimbFit lesson applies: renaming rig pieces in code orphans keys —
    /// check the json after prop refactors.
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
        float _scanUntil;

        void OnEnable()
        {
            Active.Add(this);
            _scanUntil = Time.time + 8f; // props/sockets appear over the first frames
            var saved = Resources.Load<TextAsset>("sz_character_fix");
            _table = saved != null ? JsonUtility.FromJson<Table>(saved.text) : null;
            if (_table == null) _table = new Table();
        }

        void OnDisable() => Active.Remove(this);

        /// A transform Marko may claim: itself or any ancestor (below the
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
            if (Time.time > _scanUntil) return;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!IsFixable(t)) continue;
                string path = PathOf(t);
                if (_seen.ContainsKey(path)) continue;
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
        /// Called by the editor menu in play mode: every fixable piece's
        /// CURRENT local transform becomes law. Simple and total — what you
        /// see when you save is what every future session shows.
        public static int SaveNow()
        {
            var table = new Table();
            foreach (var fix in Active)
            {
                if (fix == null) continue;
                foreach (var pair in fix._seen)
                {
                    if (pair.Value == null) continue;
                    table.entries.Add(new Entry
                    {
                        path = pair.Key,
                        pos = pair.Value.localPosition,
                        euler = pair.Value.localRotation.eulerAngles,
                        scale = pair.Value.localScale,
                    });
                }
            }
            System.IO.File.WriteAllText(
                Application.dataPath + "/_Game/Resources/sz_character_fix.json",
                JsonUtility.ToJson(table, true));
            return table.entries.Count;
        }
#endif
    }
}
