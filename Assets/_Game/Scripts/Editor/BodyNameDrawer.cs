using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// A [BodyName] string field lists the book's creatures (the Creature
    /// Creator) instead of asking for typing.
    [CustomPropertyDrawer(typeof(BodyNameAttribute))]
    public class BodyNameDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
        {
            var names = new List<string> { "(none)" };
            var values = new List<string> { "" };
            foreach (var c in SpellBook.Live.creatures)
            { names.Add(c.Name); values.Add(c.Name); }
            int at = values.IndexOf(prop.stringValue);
            if (at < 0) // a name the book no longer has still shows, so nothing is lost silently
            {
                names.Add(prop.stringValue + " (not in the book)");
                values.Add(prop.stringValue);
                at = values.Count - 1;
            }
            int pick = EditorGUI.Popup(pos, label.text, at, names.ToArray());
            prop.stringValue = values[pick];
        }
    }
}
