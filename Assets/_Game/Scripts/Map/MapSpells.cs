using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ WHICH SPELLS THIS MAP USES. Put one on the map root and list the
    /// spells; everything else in the book sits this map out - regions never
    /// match, seals never raise. No component, or an empty list, means the
    /// whole book plays (the lobby).
    public class MapSpells : MonoBehaviour
    {
        [Tooltip("Spell names from the book. Empty = every spell plays.")]
        public List<string> Spells = new List<string>();

        public static MapSpells Active { get; private set; }

        void OnEnable()
        {
            if (Active != null && Active != this)
                Debug.LogWarning($"[SpellyZombie] two MapSpells in the scene - '{name}' replaces '{Active.name}'.");
            Active = this;
        }

        void OnDisable() { if (Active == this) Active = null; }

        public static bool Allows(SpellDef sp)
        {
            var a = Active;
            if (a == null || a.Spells.Count == 0) return true;
            return sp != null && a.Spells.Contains(sp.Name);
        }
    }
}
