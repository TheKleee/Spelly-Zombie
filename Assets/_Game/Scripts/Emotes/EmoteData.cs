using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    [Serializable]
    public class JointPose
    {
        public string joint;   // model-agnostic id, e.g. "shoulder.L"
        public Vector3 euler;  // target local rotation
    }

    [Serializable]
    public class EmoteKeyframe
    {
        public float transition = 0.35f; // seconds to blend into this frame
        public float hold = 0.4f;        // seconds to wait before the next frame
        public List<JointPose> poses = new List<JointPose>();
    }

    [Serializable]
    public class EmoteDef
    {
        public string name = "Pose";
        public bool loop;                // dances loop; pose emotes stay on the last frame
        public List<EmoteKeyframe> frames = new List<EmoteKeyframe>();
    }

    [Serializable]
    class EmoteSaveFile
    {
        public List<EmoteDef> emotes = new List<EmoteDef>();
        public List<int> slots = new List<int>(); // slot (0..9) -> index into emotes, -1 = empty
        public int rigVersion; // 0/1 = graybox bean pivots, 2 = the Mixamo skeleton
    }

    /// The player's pose list: authored in the Pose Studio, saved to disk,
    /// bound to number slots; joints are referenced by string id so poses
    /// survive a character swap. Slots layer a custom binding over a default.
    public static class EmoteLibrary
    {
        public const int SlotCount = 10;

        static EmoteSaveFile _data;
        static Dictionary<int, EmoteDef> _defaults;
        static string SavePath => Path.Combine(Application.persistentDataPath, "sz_emotes.json");

        static void Init()
        {
            if (_data != null) return;
            try
            {
                if (File.Exists(SavePath))
                    _data = JsonUtility.FromJson<EmoteSaveFile>(File.ReadAllText(SavePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Emotes] Failed to load poses: {e.Message}");
            }
            if (_data == null) _data = new EmoteSaveFile();
            while (_data.slots.Count < SlotCount) _data.slots.Add(-1);

            // one-time migration: bean-era poses use different bone axes and
            // would override the baked defaults - unbind them, keep the pose list
            const int currentRigVersion = 2;
            if (_data.rigVersion < currentRigVersion)
            {
                for (int i = 0; i < _data.slots.Count; i++) _data.slots[i] = -1;
                _data.rigVersion = currentRigVersion;
                Save();
                Debug.Log("[Emotes] Bean-era pose bindings cleared. Keys now play the rig-baked defaults; re-bind your own in the Pose Studio (B).");
            }
        }

        /// The undeletable ships-with-the-game layer (slots 1-3). Every pose
        /// states ALL joints so switching poses never leaves a limb behind.
        static void BuildDefaults()
        {
            if (_defaults != null) return;
            _defaults = new Dictionary<int, EmoteDef>();

            void Add(int slot, string name, Vector3 shoulderL, Vector3 shoulderR, Vector3 neck)
            {
                var def = new EmoteDef { name = name, loop = false };
                var frame = new EmoteKeyframe();
                frame.poses.Add(new JointPose { joint = "shoulder.L", euler = shoulderL });
                frame.poses.Add(new JointPose { joint = "shoulder.R", euler = shoulderR });
                frame.poses.Add(new JointPose { joint = "neck", euler = neck });
                def.frames.Add(frame);
                _defaults[slot] = def;
            }

            Add(1, "Arm raise", new Vector3(0f, 0f, 105f), Vector3.zero, Vector3.zero);
            Add(2, "Victory", new Vector3(0f, 0f, 150f), new Vector3(0f, 0f, -150f), Vector3.zero);
            Add(3, "Arms crossed", new Vector3(0f, 0f, 100f), new Vector3(0f, 0f, -100f), Vector3.zero);
        }

        public static EmoteDef DefaultForSlot(int slot)
        {
            BuildDefaults();
            return _defaults.TryGetValue(slot, out var def) ? def : null;
        }

        /// The character rig overrides the built-ins at runtime with poses
        /// baked on the real skeleton.
        public static void SetDefault(int slot, string name, EmoteKeyframe frame)
        {
            BuildDefaults();
            var def = new EmoteDef { name = name, loop = false };
            def.frames.Add(frame);
            _defaults[slot] = def;
        }

        /// Does this slot carry a player-made binding (as opposed to falling
        /// through to the system default)?
        public static bool HasCustom(int slot)
        {
            Init();
            return slot >= 0 && slot < _data.slots.Count
                && _data.slots[slot] >= 0 && _data.slots[slot] < _data.emotes.Count;
        }

        /// Remove the custom binding; the system default takes over. The pose
        /// itself stays in the player's pose list.
        public static void ClearSlot(int slot)
        {
            Init();
            if (slot < 0 || slot >= _data.slots.Count) return;
            _data.slots[slot] = -1;
            Save();
        }

        public static IReadOnlyList<EmoteDef> Poses
        {
            get { Init(); return _data.emotes; }
        }

        public static int AddPose(EmoteDef def)
        {
            Init();
            _data.emotes.Add(def);
            Save();
            return _data.emotes.Count - 1;
        }

        public static void DeletePose(int index)
        {
            Init();
            if (index < 0 || index >= _data.emotes.Count) return;
            _data.emotes.RemoveAt(index);
            for (int i = 0; i < _data.slots.Count; i++)
            {
                if (_data.slots[i] == index) _data.slots[i] = -1;
                else if (_data.slots[i] > index) _data.slots[i]--;
            }
            Save();
        }

        public static void AssignSlot(int slot, int poseIndex)
        {
            Init();
            if (slot < 0 || slot >= SlotCount) return;
            if (poseIndex < -1 || poseIndex >= _data.emotes.Count) return;
            _data.slots[slot] = poseIndex;
            Save();
        }

        public static EmoteDef GetSlot(int slot)
        {
            Init();
            if (HasCustom(slot)) return _data.emotes[_data.slots[slot]];
            return DefaultForSlot(slot); // the built-in layer shows through
        }

        /// "[1][4]" style tag showing which keys a pose is bound to.
        public static string SlotTag(int poseIndex)
        {
            Init();
            string tag = "";
            for (int slot = 1; slot < _data.slots.Count; slot++)
                if (_data.slots[slot] == poseIndex) tag += $"[{slot}]";
            return tag;
        }

        static void Save()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(_data)); }
            catch (Exception e) { Debug.LogWarning($"[Emotes] Failed to save poses: {e.Message}"); }
        }
    }
}
