using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Preview, Reroll and Clear on an InteriorField: the field fills itself
    /// in edit mode into a throwaway child, so the hole rim and the stairs
    /// can be authored against the real layout. Nothing of it is saved or
    /// carried into play mode.
    [CustomEditor(typeof(InteriorField))]
    public class InteriorFieldEditor : Editor
    {
        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.ExitingEditMode) ClearAll();
            };
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var field = (InteriorField)target;
            EditorGUILayout.Space();
            if (EditorUtility.IsPersistent(field))
            {
                EditorGUILayout.HelpBox("Open the prefab or select a scene instance to preview the layout.", MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview layout")) Preview(field);
                if (GUILayout.Button("Reroll"))
                {
                    Undo.RecordObject(field, "reroll preview");
                    field.PreviewSeed++;
                    Preview(field);
                }
                if (GUILayout.Button("Clear")) Clear(field);
            }
            EditorGUILayout.HelpBox(
                "Gizmos: white = the cell inside a door, kept clear. Orange ceiling tiles, red open cell with its three rims, green = the exit edge, cyan = the flight down to its foot and landing. "
                + "Preview spawns the assigned prefabs for this seed; the preview child is never saved.",
                MessageType.None);
            if (field.Stairs != null && !field.StairsMeasured)
            {
                field.MeasureStairs();
                if (field.StairsMeasured) EditorUtility.SetDirty(field);
            }
            if (field.Stairs != null)
                EditorGUILayout.HelpBox(field.StairsMeasured
                    ? $"Stairs measured: rise {field.StairRise:0.00} m, run {field.StairLen:0.00} m. The field turns, scales and seats it. Rails count toward the rise, so prefer Simple or Solid."
                    : "Stairs not measured yet: no mesh found under the prefab.", MessageType.None);
            if (field.Stairs != null && field.StairsMeasured && field.StairRise < 0.5f)
                EditorGUILayout.HelpBox(
                    $"The stairs prefab is {field.StairRise * 100f:0} cm tall: it was saved at scale 1. The field scales it up, but save it at scale 100 like the other kit pieces.",
                    MessageType.Warning);
            if (field.Ceiling != null && (field.HoleSide == null || field.Stairs == null))
                EditorGUILayout.HelpBox(
                    "Hole Side or Stairs is empty, so Preview shows only their markers. One rim trim and one "
                    + "flight are enough: build each on its marker in the lobby, Alt+Shift+P, assign it here.",
                    MessageType.Info);
        }

        static void Preview(InteriorField field)
        {
            Clear(field);
            field.MeasureStairs();
            if (field.Stairs != null && field.StairsMeasured) EditorUtility.SetDirty(field);
            var root = new GameObject(InteriorField.PreviewName);
            root.hideFlags = HideFlags.DontSave;
            root.transform.SetParent(field.transform, false);
            try { field.Fill(new System.Random(field.PreviewSeed), root.transform); }
            finally
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.hideFlags = HideFlags.DontSave;
            }
            SceneView.RepaintAll();
        }

        static void Clear(InteriorField field)
        {
            var old = field.transform.Find(InteriorField.PreviewName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        static void ClearAll()
        {
            foreach (var f in Object.FindObjectsByType<InteriorField>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Clear(f);
        }
    }
}
