using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Maps seal line count to an authored solid prefab, per material. Asset:
    /// Assets/_Game/Resources/ShapeLibrary.asset (must sit in Resources). Empty
    /// slot falls to the row's Default, then to the soft-body blob; prefabs are used as authored.
    [CreateAssetMenu(fileName = "ShapeLibrary", menuName = "Spelly Zombie/Shape Library")]
    public class ShapeLibrary : ScriptableObject
    {
        /// One material's shelf: a default, plus a slot per line count.
        [System.Serializable]
        public class Row
        {
            public SurfaceMaterialType Material = SurfaceMaterialType.Stone;

            [Tooltip("Used for this material whenever the drawn line count has no slot filled.")]
            public GameObject Default;

            [Tooltip("Element 0 is ignored. Element 3 = a seal drawn with 3 lines, " +
                     "Element 4 = 4 lines … Element 10 = 10 lines (a circle counts as 10).")]
            public GameObject[] ByLines = new GameObject[11];
        }

        [Tooltip("Add a row per material you want shapes for. Materials with no row " +
                 "keep the default blob look.")]
        public List<Row> Materials = new List<Row>();

        // ---------------------------------------------------------------- load
        static ShapeLibrary _base;      // the game's own shelf
        static bool _searched;

        /// Workshop overrides: a custom map pushes its own ShapeLibrary; filled
        /// slots win, empty ones fall through. Last pushed is checked first.
        static readonly List<ShapeLibrary> _overrides = new List<ShapeLibrary>();

        /// A workshop map calls this when it loads (and Pop when it unloads).
        /// Safe to call twice with the same asset.
        public static void Push(ShapeLibrary lib)
        {
            if (lib == null || _overrides.Contains(lib)) return;
            _overrides.Add(lib);
        }

        public static void Pop(ShapeLibrary lib)
        {
            if (lib != null) _overrides.Remove(lib);
        }

        /// Drop every mod shelf - call on returning to the menu.
        public static void ClearMods() => _overrides.Clear();

        public static ShapeLibrary Base
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _base = Resources.Load<ShapeLibrary>("ShapeLibrary");
                }
                return _base;
            }
        }

        /// True when anything at all can answer - base shelf or a mod's.
        public static bool Any => Base != null || _overrides.Count > 0;

        /// Editor convenience: pick the asset up again after it's created or
        /// moved, without restarting play mode.
        public static void Forget() { _searched = false; _base = null; }

        /// Lookup: mod shelves first (newest first), then the base game; null = soft-body blob.
        public static GameObject Find(SurfaceMaterialType mat, int lines)
        {
            for (int i = _overrides.Count - 1; i >= 0; i--)
            {
                var hit = _overrides[i] != null ? _overrides[i].Resolve(mat, lines) : null;
                if (hit != null) return hit;
            }
            return Base != null ? Base.Resolve(mat, lines) : null;
        }

        /// The prefab for this material and line count on this shelf alone, or null.
        public GameObject Resolve(SurfaceMaterialType mat, int lines)
        {
            foreach (var row in Materials)
            {
                if (row == null || row.Material != mat) continue;
                if (row.ByLines != null && lines >= 0 && lines < row.ByLines.Length
                    && row.ByLines[lines] != null)
                    return row.ByLines[lines];
                return row.Default;   // slot empty -> this material's default
            }
            return null;              // no row -> let the next shelf answer
        }
    }
}
