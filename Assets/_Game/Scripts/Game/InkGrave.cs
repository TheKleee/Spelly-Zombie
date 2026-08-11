using UnityEngine;

namespace SpellyZombie
{
    /// THE MAP'S HEART — where the ink falls when every cauldron is rubble
    /// (Marko Aug 11: "ink falls at the center of the map flat on the floor
    /// and can no longer be lifted... turning what was hide and seek into a
    /// base defense game"). Drop this on an empty at the arena center, one
    /// per gameplay map; the lobby never needs it. No marker on a map = a
    /// LOUD error at the moment it matters, and the last broken pot's
    /// position stands in so the round can still finish.
    public class InkGrave : MonoBehaviour
    {
        public static InkGrave I { get; private set; }
        void OnEnable() { I = this; }
        void OnDisable() { if (I == this) I = null; }
    }
}
