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
            return sp != null && a.Names().Contains(sp.Name);
        }

        // the listed spells, and the spell an allowed spell's area loads: a meteor's falling rock
        // is a book spell of its own, and it comes with the meteor
        HashSet<string> _names;
        SpellBook _namesFor;
        int _namesCount;
        float _namesAt;

        HashSet<string> Names()
        {
            var book = SpellBook.Live;
            if (_names != null && _namesFor == book && _namesCount == Spells.Count
                && Time.unscaledTime < _namesAt + 1f) return _names; // a book edited in play shows within a second
            _names = new HashSet<string>(Spells);
            var todo = new List<string>(Spells);
            for (int i = 0; i < todo.Count; i++)
            {
                var host = book.Spell(todo[i]);
                var area = host != null ? book.Aoe(host.Aoe) : null;
                if (area != null && !string.IsNullOrEmpty(area.Spell) && _names.Add(area.Spell)) todo.Add(area.Spell);
            }
            _namesFor = book;
            _namesCount = Spells.Count;
            _namesAt = Time.unscaledTime;
            return _names;
        }
    }
}
