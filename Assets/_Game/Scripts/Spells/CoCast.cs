using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ COMBINED CASTING (his call): runes of several players of one side in
    /// one seal fire together, and everyone whose ink made it shares its kills,
    /// so acolytes can take revenge on all of them. The cast itself belongs to
    /// whoever closed the ring; for a while after, that player's kills count
    /// for the others too: CoCastShareSeconds for a spell, the whole life of
    /// the dead it raised for a summon. Host-written, told to every machine.
    public static class CoCast
    {
        struct Entry { public int Caster; public int[] With; public float Until; }
        static readonly List<Entry> _live = new List<Entry>();

        /// HOST: a combined seal fired. `raised` = it raised the dead (or
        /// woke darts) instead of making a spell.
        public static void Fired(int caster, List<int> with, bool raised)
        {
            if (with == null || with.Count == 0 || !NetGame.IsAuthority) return;
            float seconds = raised ? DrawingConfig.SummonedZombieLife : DrawingConfig.CoCastShareSeconds;
            var arr = with.ToArray();
            Add(caster, arr, seconds);
            NetSync.PushCoCast(caster, arr, seconds);
        }

        /// Any machine: these players share the caster's kills for this long.
        public static void Add(int caster, int[] with, float seconds)
        {
            Prune();
            _live.Add(new Entry { Caster = caster, With = with ?? new int[0], Until = Time.time + seconds });
        }

        /// Everyone who shares this killer's kills right now.
        public static void PartnersOf(int killer, List<int> into)
        {
            into.Clear();
            if (killer < 0) return;
            Prune();
            foreach (var e in _live)
                if (e.Caster == killer)
                    foreach (int w in e.With)
                        if (w != killer && !into.Contains(w)) into.Add(w);
        }

        /// Does `who` share `killer`'s kills right now?
        public static bool Shares(int killer, int who)
        {
            if (killer < 0 || who == killer) return false;
            Prune();
            foreach (var e in _live)
                if (e.Caster == killer && System.Array.IndexOf(e.With, who) >= 0) return true;
            return false;
        }

        public static void Clear() => _live.Clear();

        static void Prune()
        {
            float now = Time.time;
            for (int i = _live.Count - 1; i >= 0; i--)
                if (_live[i].Until <= now) _live.RemoveAt(i);
        }
    }
}
