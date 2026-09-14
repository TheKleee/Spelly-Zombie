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
}
