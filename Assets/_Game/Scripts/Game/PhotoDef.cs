using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// What a Photo Booth subject is.
    public enum PhotoKind { Character, Creature, Spell, Area, Effect, Piece }

    /// ★ A PHOTO SETUP, made in the Photo Booth: data only. The eye, the light
    /// and the look, every posed subject and every ink line. Saved as JSON
    /// beside its picture, opened again to keep editing.
    [Serializable]
    public class PhotoDef
    {
        public string Name = "New photo";
        public Vector3 CamPos = new Vector3(0f, 2.5f, -7f);
        public float CamYaw, CamPitch = 12f, Lens = 50f;
        public int Width = 1920, Height = 1080;
        public int Background = 2;             // 0 the world, 1 a colour, 2 see-through (his call: most photos are thumbnails)
        public Color BackColor = new Color(0.25f, 0.55f, 0.9f);
        public bool Island;                    // the island's ground grows around the subjects
        public int IslandSeed;                 // which island
        public bool Shaded = true;             // characters take light and shadow (the booth only)
        public bool SunMoved;                  // false = the scene's own sun
        public float SunTurn, SunHeight = 50f, SunPower = 1f;
        public float Rim, RimTurn = 180f;
        public float Brightness, Contrast, Warmth, Saturation, Glow, Vignette, Blur;
        public float Focus = 10f;              // metres to what stays sharp
        public List<Item> Items = new List<Item>();
        public List<Ink> Inks = new List<Ink>();

        /// The folder it sits in ("" = no folder) and the name it was saved
        /// under: set when it is read or saved, never written into the file.
        [NonSerialized] public string Folder = "";
        [NonSerialized] public string SavedAs;

        /// One posed thing.
        [Serializable]
        public class Item
        {
            public PhotoKind Kind;
            public string What = "";           // the spell, creature, area, effect or piece
            public SpellBody Body;             // a creature with no design by that name: this plain body
            public Vector3 Pos;
            public float Yaw, Tilt, Roll;      // it turns around its middle
            public float Size = 1f;
            public float Time;                 // how far its effects or its clip have run
            public string Clip = "";           // the animation it holds, "" = none
            public int Seed = 1;               // its particles fall the same way every time
            // a character
            public bool Acolyte;
            public string Outfit = "";
            public bool HatSet;
            public Color Hat = Color.white;
            // a character or a creature
            public int Mood;                   // EyeMood
            public bool TintSet;               // a creature in a colour of its own choosing
            public Color Tint = Color.white;
            public bool LookAtCamera = true;
            public float Height = 1f, Width = 1f, Head = 1f, Arms = 1f, Legs = 1f;
            public List<JointPose> Pose = new List<JointPose>();
            // an effect or an area: the parts switched off, by their path inside it
            public List<string> Hidden = new List<string>();

            public Item Clone() => JsonUtility.FromJson<Item>(JsonUtility.ToJson(this));
        }

        /// One ink line, riding the bone it was drawn on.
        [Serializable]
        public class Ink
        {
            public int Item = -1;              // the subject it rides, -1 = nothing
            public string Bone = "";           // the path from the subject's root, "" = the root
            public Color Color = Stroke.InkColor;
            public float Width = 0.02f;
            public List<Vector3> Points = new List<Vector3>(); // in the bone's space
        }

        public static string ToJson(PhotoDef d) => JsonUtility.ToJson(d, true);

        public static PhotoDef FromJson(string json)
        {
            try
            {
                var d = JsonUtility.FromJson<PhotoDef>(json);
                if (d == null) return null;
                if (d.Items == null) d.Items = new List<Item>();
                if (d.Inks == null) d.Inks = new List<Ink>();
                foreach (var it in d.Items)
                {
                    if (it.Pose == null) it.Pose = new List<JointPose>();
                    if (it.Hidden == null) it.Hidden = new List<string>();
                }
                foreach (var k in d.Inks) if (k.Points == null) k.Points = new List<Vector3>();
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] photo unreadable: {e.Message}");
                return null;
            }
        }
    }

    /// ★ THE PHOTO SETUPS ON DISK: one folder of setups, with folders inside it
    /// for grouping. A setup is its JSON and its picture side by side. The
    /// finished photos go to the player's Pictures, in a folder of the same name.
    public static class PhotoLibrary
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "Photos");

        static string Stem(string name)
        {
            string s = MapLibrary.Stem(name);
            return s == "map" && string.IsNullOrWhiteSpace(name) ? "Photo" : s;
        }

        static string Dir(string folder) =>
            string.IsNullOrEmpty(folder) ? Folder : Path.Combine(Folder, Stem(folder));

        static string FileFor(string folder, string name) => Path.Combine(Dir(folder), Stem(name) + ".json");
        static string PictureFor(string folder, string name) => Path.Combine(Dir(folder), Stem(name) + ".png");

        public static bool Exists(string folder, string name) =>
            !string.IsNullOrEmpty(name) && File.Exists(FileFor(folder, name));

        /// The folders, by name.
        public static List<string> Folders()
        {
            var list = new List<string>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var d in Directory.GetDirectories(Folder)) list.Add(Path.GetFileName(d));
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// The setups in one folder ("" = outside every folder), newest first.
        public static List<PhotoDef> In(string folder)
        {
            var list = new List<PhotoDef>();
            string dir = Dir(folder);
            if (!Directory.Exists(dir)) return list;
            var files = new List<string>(Directory.GetFiles(dir, "*.json"));
            files.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            foreach (var f in files)
            {
                PhotoDef d;
                try { d = PhotoDef.FromJson(File.ReadAllText(f)); }
                catch (Exception e) { Debug.LogWarning($"[SpellyZombie] photo unreadable: {e.Message}"); continue; }
                if (d == null) continue;
                d.Folder = folder ?? "";
                d.SavedAs = d.Name;
                list.Add(d);
            }
            return list;
        }

        public static PhotoDef Load(string folder, string name)
        {
            if (!Exists(folder, name)) return null;
            var d = PhotoDef.FromJson(File.ReadAllText(FileFor(folder, name)));
            if (d != null) { d.Folder = folder ?? ""; d.SavedAs = d.Name; }
            return d;
        }

        /// Writes the setup where it says it sits, with its picture.
        public static void Save(PhotoDef def, byte[] png)
        {
            Directory.CreateDirectory(Dir(def.Folder));
            File.WriteAllText(FileFor(def.Folder, def.Name), PhotoDef.ToJson(def));
            if (png != null && png.Length > 0) File.WriteAllBytes(PictureFor(def.Folder, def.Name), png);
        }

        public static void Delete(string folder, string name)
        {
            string file = FileFor(folder, name), pic = PictureFor(folder, name);
            if (File.Exists(file)) File.Delete(file);
            if (File.Exists(pic)) File.Delete(pic);
        }

        /// A name nobody in that folder has yet: the name, else the name with a number.
        public static string FreeName(string folder, string name)
        {
            string stem = string.IsNullOrWhiteSpace(name) ? "Photo" : name.Trim();
            string pick = stem;
            for (int n = 2; Exists(folder, pick); n++) pick = stem + " " + n;
            return pick;
        }

        /// The setup and its picture into another folder, renamed if the name is taken there.
        public static void Move(PhotoDef def, string folder)
        {
            folder = folder ?? "";
            if (string.Equals(Dir(folder), Dir(def.Folder), StringComparison.OrdinalIgnoreCase)) return;
            string oldFolder = def.Folder, oldName = def.Name;
            byte[] png = File.Exists(PictureFor(oldFolder, oldName)) ? File.ReadAllBytes(PictureFor(oldFolder, oldName)) : null;
            def.Folder = folder;
            def.Name = FreeName(folder, def.Name);
            Save(def, png);
            Delete(oldFolder, oldName);
            def.SavedAs = def.Name;
        }

        /// A second setup just like this one, beside it.
        public static PhotoDef Copy(PhotoDef def)
        {
            var copy = PhotoDef.FromJson(PhotoDef.ToJson(def));
            if (copy == null) return null;
            copy.Folder = def.Folder;
            copy.Name = FreeName(def.Folder, def.Name);
            string pic = PictureFor(def.Folder, def.SavedAs ?? def.Name);
            Save(copy, File.Exists(pic) ? File.ReadAllBytes(pic) : null);
            copy.SavedAs = copy.Name;
            return copy;
        }

        public static bool FolderExists(string name) =>
            !string.IsNullOrWhiteSpace(name) && Directory.Exists(Dir(name));

        /// A new empty folder; its name is made free first. Returns the name it got.
        public static string MakeFolder(string name)
        {
            string stem = Stem(string.IsNullOrWhiteSpace(name) ? "Folder" : name);
            string pick = stem;
            for (int n = 2; FolderExists(pick); n++) pick = stem + " " + n;
            Directory.CreateDirectory(Dir(pick));
            return pick;
        }

        /// False when the new name is taken or empty.
        public static bool RenameFolder(string from, string to)
        {
            if (string.IsNullOrWhiteSpace(to) || !FolderExists(from)) return false;
            string a = Dir(from), b = Dir(to);
            if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase))
            {
                if (a == b) return true;
                string hop = a + "~"; // a change of case only: Windows needs a step between
                Directory.Move(a, hop);
                Directory.Move(hop, b);
                return true;
            }
            if (Directory.Exists(b)) return false;
            Directory.Move(a, b);
            return true;
        }

        /// Only an empty folder goes.
        public static bool DeleteFolder(string name)
        {
            if (!FolderExists(name)) return false;
            string dir = Dir(name);
            if (Directory.GetFileSystemEntries(dir).Length > 0) return false;
            Directory.Delete(dir);
            return true;
        }

        public static int CountIn(string folder)
        {
            string dir = Dir(folder);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Length : 0;
        }

        /// The setup's picture, or null. The caller destroys the texture.
        public static Texture2D Picture(string folder, string name) => MapLibrary.LoadPng(PictureFor(folder, name));

        /// The newest setup's picture in a folder, or null. The caller destroys the texture.
        public static Texture2D Cover(string folder)
        {
            string dir = Dir(folder);
            if (!Directory.Exists(dir)) return null;
            string best = null;
            DateTime when = DateTime.MinValue;
            foreach (var f in Directory.GetFiles(dir, "*.png"))
            {
                var t = File.GetLastWriteTimeUtc(f);
                if (t > when) { when = t; best = f; }
            }
            return best != null ? MapLibrary.LoadPng(best) : null;
        }

        /// Where finished photos go: the player's Pictures, in the game's folder
        /// and then the setup's.
        public static string PhotosFolder(string folder)
        {
            string pictures = "";
            try { pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); }
            catch (Exception) { pictures = ""; }
            if (string.IsNullOrEmpty(pictures)) pictures = Path.Combine(Folder, "Pictures");
            string root = Path.Combine(pictures, "Spelly Zombie");
            return string.IsNullOrEmpty(folder) ? root : Path.Combine(root, Stem(folder));
        }

        /// Writes a finished photo and returns its path.
        public static string WritePhoto(string folder, string name, byte[] png)
        {
            string dir = PhotosFolder(folder);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Stem(name) + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
            File.WriteAllBytes(path, png);
            return path;
        }

        /// Shows a folder in the system's file window.
        public static void Reveal(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Application.OpenURL(new Uri(dir).AbsoluteUri);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] could not open {dir}: {e.Message}");
            }
        }
    }
}
