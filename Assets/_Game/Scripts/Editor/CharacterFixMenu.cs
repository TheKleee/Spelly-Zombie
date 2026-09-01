using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// The save button for the character edits (see CharacterFix.cs):
    /// play, nudge the wand/book/eyes/sockets/IK anchors, save, stop.
    public static class CharacterFixMenu
    {
        [MenuItem("Spelly Zombie/Legacy/Save CHARACTER Fix (play mode)")]
        public static void Save()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("[SpellyZombie] Character Fix saves PLAY-MODE edits: " +
                               "enter play, adjust the pieces, then save.");
                return;
            }
            int saved = CharacterFix.SaveNow();
            AssetDatabase.Refresh();
            Debug.Log($"[SpellyZombie] Character Fix saved: {saved} piece(s) locked in. " +
                      "They re-apply on every rig build from now on.");
        }
    }
}
