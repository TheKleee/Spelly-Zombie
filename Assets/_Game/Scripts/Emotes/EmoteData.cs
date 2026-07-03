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
        public string name = "Emote";
        public bool loop;                // dances loop; pose emotes stay on the last frame
        public List<EmoteKeyframe> frames = new List<EmoteKeyframe>();
    }

    [Serializable]
    class EmoteSaveFile
    {
        public List<EmoteDef> emotes = new List<EmoteDef>();
        public List<int> slots = new List<int>(); // slot (0..9) -> index into emotes, -1 = empty
    }

    /// Player-authored emotes: poses/sequences saved by name into number slots and
    /// persisted to disk. Joints are referenced by string id, so an emote authored
    /// on the graybox mannequin plays unchanged on any future character whose
    /// EmoteRig registers the same joint names ("shoulder.L", "neck", ...).
    ///
    /// This is why custom emotes matter here: a pose emote is the somatic trigger
    /// of whatever seal the player drew across their own joints. Authoring the
    /// emote = choosing how you cast.
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
                Debug.LogWarning($"[Emotes] Failed to load emotes: {e.Message}");
            }
            if (_data == null) _data = new EmoteSaveFile();
            while (_data.slots.Count < SlotCount) _data.slots.Add(-1);
            SeedDefaults();
        }

        /// Ship one starter emote in slot 1 so T/1 works out of the box.
        static void SeedDefaults()
        {
            if (_data.emotes.Count > 0) return;
            var raise = new EmoteDef { name = "Arm raise", loop = false };
            var frame = new EmoteKeyframe();
            frame.poses.Add(new JointPose { joint = "shoulder.L", euler = new Vector3(0f, 0f, 105f) });
            raise.frames.Add(frame);
            _data.emotes.Add(raise);
            _data.slots[1] = 0;
        }

        public static EmoteDef GetSlot(int slot)
        {
            Init();
            if (slot < 0 || slot >= _data.slots.Count) return null;
            int idx = _data.slots[slot];
            return idx >= 0 && idx < _data.emotes.Count ? _data.emotes[idx] : null;
        }

        public static void AssignToSlot(int slot, EmoteDef def)
        {
            Init();
            if (slot < 0 || slot >= SlotCount) return;
            int idx = _data.slots[slot];
            if (idx >= 0 && idx < _data.emotes.Count)
                _data.emotes[idx] = def;      // overwrite the emote living in this slot
            else
            {
                _data.emotes.Add(def);
                _data.slots[slot] = _data.emotes.Count - 1;
            }
            Save();
            Debug.Log($"[Emotes] Saved '{def.name}' ({def.frames.Count} frame(s)) to slot {slot} -> {SavePath}");
        }

        static void Save()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(_data)); }
            catch (Exception e) { Debug.LogWarning($"[Emotes] Failed to save emotes: {e.Message}"); }
        }

        public static void DeleteSave()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch { }
            _data = null; // re-seed defaults on next access
        }
    }
}
