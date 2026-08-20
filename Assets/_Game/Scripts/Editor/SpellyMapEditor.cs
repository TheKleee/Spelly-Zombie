using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// The button the map deserves: no hunting through context menus.
    [CustomEditor(typeof(SpellyMap))]
    public class SpellyMapEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var map = (SpellyMap)target;

            GUILayout.Space(8f);
            if (GUILayout.Button("GENERATE PREVIEW", GUILayout.Height(34f)))
                map.GeneratePreview();
            if (GUILayout.Button("Clear Generated"))
                map.ClearGenerated();
        }
    }

    /// LIVE REBUILD: watches the scene while AutoRegenerate is on and
    /// regenerates the whole map about a second after the last edit to any
    /// biome box or path node - move things, watch the island follow.
    [InitializeOnLoad]
    static class SpellyMapLive
    {
        static double _nextCheck, _quietSince;
        static int _lastKey;
        static bool _dirty;

        static SpellyMapLive() => EditorApplication.update += Tick;

        static void Tick()
        {
            if (Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup < _nextCheck) return;
            _nextCheck = EditorApplication.timeSinceStartup + 0.35;

            var map = Object.FindFirstObjectByType<SpellyMap>();
            if (map == null || !map.AutoRegenerate) return;

            int key = map.LayoutKey();
            if (key != _lastKey)
            {
                _lastKey = key;
                _quietSince = EditorApplication.timeSinceStartup;
                _dirty = true;
                return;
            }
            if (_dirty && EditorApplication.timeSinceStartup - _quietSince > 1.0)
            {
                _dirty = false;
                map.GeneratePreview();
            }
        }
    }
}
