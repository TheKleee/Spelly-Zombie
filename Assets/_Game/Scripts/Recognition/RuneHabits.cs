using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// Which runes this player casts, and which pairs sit together: the tie
    /// breaker for completions. Counts what FIRES, however it was finished; a
    /// hand-drawn cast weighs double, an erased completion counts against.
    /// Its own small file, never the template file.
    public static class RuneHabits
    {
        const int N = 13; // RuneType 0..12

        [System.Serializable]
        class Saved
        {
            public int[] casts = new int[N];
            public int[] pairs = new int[N * N];
        }

        static Saved _s;
        static bool _dirty;
        static float _saveAt;
        static string FilePath => Path.Combine(Application.persistentDataPath, "sz_rune_habits.json");

        static void Load()
        {
            if (_s != null) return;
            try { if (File.Exists(FilePath)) _s = JsonUtility.FromJson<Saved>(File.ReadAllText(FilePath)); }
            catch { _s = null; }
            if (_s == null || _s.casts == null || _s.casts.Length != N || _s.pairs == null || _s.pairs.Length != N * N)
                _s = new Saved();
        }

        public static void NoteCast(RuneType r, bool byHand)
        {
            Load();
            int i = (int)r;
            if (i <= 0 || i >= N) return;
            _s.casts[i] += byHand ? 2 : 1;
            Touch();
        }

        /// A completion the player rubbed out right away: the guess was wrong.
        public static void NoteMiss(RuneType r)
        {
            Load();
            int i = (int)r;
            if (i <= 0 || i >= N) return;
            _s.casts[i] = Mathf.Max(0, _s.casts[i] - 1);
            Touch();
        }

        public static void NotePair(RuneType a, RuneType b)
        {
            Load();
            int i = (int)a, j = (int)b;
            if (i <= 0 || j <= 0 || i >= N || j >= N || i == j) return;
            _s.pairs[i * N + j]++;
            _s.pairs[j * N + i]++;
            Touch();
        }

        /// How likely r is as this player's completion: its casts, raised by
        /// how often it sits next to the neighbour rune. 1 with no history.
        public static float Weight(RuneType r, RuneType neighbour)
        {
            Load();
            int i = (int)r;
            if (i <= 0 || i >= N) return 0f;
            float w = 1f + _s.casts[i];
            int j = (int)neighbour;
            if (j > 0 && j < N) w *= 1f + _s.pairs[i * N + j];
            return w;
        }

        /// True while every rune weighs the same: no habits yet.
        public static bool Blank()
        {
            Load();
            for (int i = 1; i < N; i++) if (_s.casts[i] != 0) return false;
            return true;
        }

        static void Touch() { _dirty = true; _saveAt = Time.time + 2f; }

        /// Writes a couple of seconds after the last change.
        public static void Tick()
        {
            if (!_dirty || Time.time < _saveAt) return;
            _dirty = false;
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(_s)); } catch { }
        }
    }
}
