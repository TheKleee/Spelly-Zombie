using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// The map's spell list, picked from the book by name - no typing.
    [CustomEditor(typeof(MapSpells))]
    public class MapSpellsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var map = (MapSpells)target;
            var book = SpellBook.Load();

            EditorGUILayout.LabelField("SPELLS ON THIS MAP", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Empty = every spell in the book plays.", EditorStyles.miniLabel);

            for (int i = 0; i < map.Spells.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool known = book.Spell(map.Spells[i]) != null;
                    EditorGUILayout.LabelField(map.Spells[i] + (known ? "" : "  (not in the book!)"));
                    if (GUILayout.Button("-", GUILayout.Width(24)))
                    {
                        Undo.RecordObject(map, "remove spell");
                        map.Spells.RemoveAt(i);
                        EditorUtility.SetDirty(map);
                        break;
                    }
                }
            }

            var options = new List<string> { "add a spell..." };
            foreach (var s in book.spells)
                if (!map.Spells.Contains(s.Name)) options.Add(s.Name);
            int pick = EditorGUILayout.Popup(0, options.ToArray());
            if (pick > 0)
            {
                Undo.RecordObject(map, "add spell");
                map.Spells.Add(options[pick]);
                EditorUtility.SetDirty(map);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Every spell"))
                {
                    Undo.RecordObject(map, "all spells");
                    map.Spells.Clear();
                    foreach (var s in book.spells) map.Spells.Add(s.Name);
                    EditorUtility.SetDirty(map);
                }
                if (GUILayout.Button("Clear"))
                {
                    Undo.RecordObject(map, "clear spells");
                    map.Spells.Clear();
                    EditorUtility.SetDirty(map);
                }
            }
        }
    }
}
