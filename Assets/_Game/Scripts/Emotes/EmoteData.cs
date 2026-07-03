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
    }

    /// The player's pose list: poses authored in the Pose Studio, saved to disk,
    /// bound to number slots for in-game use. Joints are referenced by string id,
    /// so a pose authored on the graybox mannequin plays unchanged on any future
    /// character whose EmoteRig registers the same joint names.
    ///
    /// A pose emote is the somatic trigger of whatever seal the player drew
    /// across their own joints: authoring poses = choosing how you cast.
    public static class EmoteLibrary
    {
        public const int SlotCount = 10;

        static EmoteSaveFile _data;
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
            SeedDefaults();
        }

        /// The game ships with default poses (bound to 1-4) so the pose list is
        /// never empty — players learn by playing them, then make their own.
        /// Every pose states ALL joints so switching poses never leaves a limb behind.
        static void SeedDefaults()
        {
            if (_data.emotes.Count > 0) return;

            void Add(int slot, string name, Vector3 shoulderL, Vector3 shoulderR, Vector3 neck)
            {
                var def = new EmoteDef { name = name, loop = false };
                var frame = new EmoteKeyframe();
                frame.poses.Add(new JointPose { joint = "shoulder.L", euler = shoulderL });
                frame.poses.Add(new JointPose { joint = "shoulder.R", euler = shoulderR });
                frame.poses.Add(new JointPose { joint = "neck", euler = neck });
                def.frames.Add(frame);
                _data.emotes.Add(def);
                _data.slots[slot] = _data.emotes.Count - 1;
            }

            Add(1, "Arm raise", new Vector3(0f, 0f, 105f), Vector3.zero, Vector3.zero);
            Add(2, "Victory", new Vector3(0f, 0f, 150f), new Vector3(0f, 0f, -150f), Vector3.zero);
            Add(3, "Arms crossed", new Vector3(0f, 0f, 100f), new Vector3(0f, 0f, -100f), Vector3.zero);
            Add(4, "Bow", new Vector3(0f, 0f, 20f), new Vector3(0f, 0f, -20f), new Vector3(45f, 0f, 0f));
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
            if (slot < 0 || slot >= _data.slots.Count) return null;
            int idx = _data.slots[slot];
            return idx >= 0 && idx < _data.emotes.Count ? _data.emotes[idx] : null;
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

        public static void DeleteSave()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch { }
            _data = null; // re-seed defaults on next access
        }
    }
}
