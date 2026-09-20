using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ A MAP'S OWN GRIMOIRE PAGES: page art painted in the Map Creator, kept
    /// as pngs in a folder beside the map, shared with it on the Workshop, and
    /// asked for first by the book while that map is the active one.
    public static class MapPages
    {
        /// The pages folder's name inside a shared map's Workshop folder.
        public const string SubFolder = "pages";

        static readonly Dictionary<string, Texture2D> _pages = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        static MapDef _for;

        /// The active map's page under this name, or null.
        public static Texture2D Find(string pageName) =>
            !string.IsNullOrEmpty(pageName) && _pages.TryGetValue(pageName, out var t) && t != null ? t : null;

        /// The active map's pages from its folder; none while no map is up.
        public static void Use(MapDef map)
        {
            if (ReferenceEquals(map, _for)) return;
            _for = map;
            foreach (var t in _pages.Values) if (t != null) UnityEngine.Object.Destroy(t);
            _pages.Clear();
            if (map == null) return;
            string dir = !string.IsNullOrEmpty(map.PagesDir) ? map.PagesDir : MapLibrary.PagesFolder(map.Name);
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.png"))
            {
                var tex = MapLibrary.LoadPng(f);
                if (tex == null) continue;
                tex.name = Path.GetFileNameWithoutExtension(f);
                _pages[tex.name] = tex;
            }
        }

        /// A page painted in the creator: these pixels are the map's page now.
        public static void Put(string pageName, Color32[] px, int w, int h)
        {
            if (string.IsNullOrEmpty(pageName) || px == null) return;
            if (!_pages.TryGetValue(pageName, out var tex) || tex == null || tex.width != w || tex.height != h)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                _pages[pageName] = tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = pageName };
            }
            tex.SetPixels32(px);
            tex.Apply(false);
        }

        /// Writes every page beside the map, replacing the folder.
        public static void Save(string mapName)
        {
            string dir = MapLibrary.PagesFolder(mapName);
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (_pages.Count == 0) return;
                Directory.CreateDirectory(dir);
                foreach (var kv in _pages)
                    if (kv.Value != null) File.WriteAllBytes(Path.Combine(dir, kv.Key + ".png"), kv.Value.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] map pages for '{mapName}' not saved: {e.Message}");
            }
        }

        /// The pngs of one pages folder into another, which is replaced.
        public static void CopyFolder(string from, string to)
        {
            try
            {
                if (Directory.Exists(to)) Directory.Delete(to, true);
                if (!Directory.Exists(from)) return;
                Directory.CreateDirectory(to);
                foreach (var f in Directory.GetFiles(from, "*.png"))
                    File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SpellyZombie] map pages not copied: {e.Message}");
            }
        }
    }
}
