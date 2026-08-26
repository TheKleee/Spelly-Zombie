using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// One-shot hints: a system calls Offer() every frame the option is
    /// genuinely available, Retire() the moment the player uses it. Retired
    /// hints never come back (persisted). Hints.Enabled is the options switch.
    public static class Hints
    {
        public enum Id
        {
            Pages,    // flip the book's pages
            Absorb,   // F learns a rune off the world
        }

        const string OnKey = "sz_hints_on";
        const string DoneKey = "sz_hint_done_";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(OnKey, 1) != 0;   // demo default: ON
            set { PlayerPrefs.SetInt(OnKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        static readonly HashSet<Id> _done = new HashSet<Id>();
        static bool _loaded;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            foreach (Id id in System.Enum.GetValues(typeof(Id)))
                if (PlayerPrefs.GetInt(DoneKey + id, 0) != 0) _done.Add(id);
        }

        /// Call every frame this option is actually available to the player.
        /// Renders as a chip in the shared row.
        public static void Offer(Id id)
        {
            Load();
            if (!Enabled || _done.Contains(id)) return;
            UIPrompt.Offer(KeyFor(id), Label(id));
        }

        /// The player just did it - the hint is finished, for good.
        public static void Retire(Id id)
        {
            Load();
            if (!_done.Add(id)) return;
            PlayerPrefs.SetInt(DoneKey + id, 1);
            PlayerPrefs.Save();
        }

        /// Options: show every hint again from scratch.
        public static void ResetAll()
        {
            Load();
            foreach (Id id in System.Enum.GetValues(typeof(Id)))
                PlayerPrefs.DeleteKey(DoneKey + id);
            _done.Clear();
            PlayerPrefs.Save();
        }

        static string KeyFor(Id id)
        {
            switch (id)
            {
                case Id.Pages: return "← →"; // matches the book's own chip, dedups into it
                case Id.Absorb: return "F";
                default: return "?";
            }
        }

        /// Labels state the benefit, not the mechanics.
        static string Label(Id id)
        {
            switch (id)
            {
                case Id.Pages: return Loc.T("chip.pages");
                case Id.Absorb: return Loc.T("chip.absorb");
                default: return "";
            }
        }
    }
}
