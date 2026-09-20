using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// Every RuneType field in an Inspector (an absorb source's Teaches, for
    /// one) lists the book's runes by name, made runes included, instead of
    /// the twelve enum names.
    [CustomPropertyDrawer(typeof(RuneType))]
    public class RuneTypeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
        {
            var names = new List<string> { "None" };
            var ids = new List<int> { 0 };
            foreach (var r in SpellBook.Live.runes)
            {
                names.Add((string.IsNullOrEmpty(r.Emoji) ? "" : r.Emoji + "  ") + r.Name);
                ids.Add(r.Id);
            }
            int at = ids.IndexOf(prop.intValue);
            if (at < 0) // an id the book no longer has still shows, so nothing is lost silently
            {
                names.Add("rune " + prop.intValue + " (not in the book)");
                ids.Add(prop.intValue);
                at = ids.Count - 1;
            }
            int pick = EditorGUI.Popup(pos, label.text, at, names.ToArray());
            prop.intValue = ids[pick];
        }
    }
}
