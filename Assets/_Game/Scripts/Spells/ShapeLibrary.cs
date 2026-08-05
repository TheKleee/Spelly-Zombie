using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE SHAPE SHELF (Marko's design): what a conjured SOLID looks like is
    /// decided by how many lines the seal was drawn with — and YOU decide what
    /// each number means, by dropping a prefab into a slot. Nothing about
    /// shapes lives in code.
    ///
    ///   Assets/_Game/Resources/ShapeLibrary.asset
    ///   (Create ▸ Spelly Zombie ▸ Shape Library — it MUST sit in a Resources
    ///    folder so the game can find it.)
    ///
    /// Add a row per material you care about. Each row has:
    ///   Default   — this material, any seal that has no slot filled
    ///   By Lines  — one slot per line count. Element 3 = a 3-line seal,
    ///               Element 10 = a circle. Leave any slot empty and it falls
    ///               through to Default; leave Default empty too and the
    ///               soft-body blob takes over, exactly as before.
    ///
    /// Fill as few or as many as you like, whenever you like. A material with
    /// no row at all simply behaves the way it does today.
    ///
    /// Your prefab is used AS AUTHORED: your mesh, your collider (mesh for a
    /// wheel, box for a plank — your call), your rigidbody settings. Code only
    /// adds what is missing, and scales your proportions by how big the rune
    /// was drawn.
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

        /// MOD SHELVES, ON TOP (Steam Workshop): a custom map ships its own
        /// ShapeLibrary and pushes it while it's loaded — its filled slots win,
        /// anything it leaves empty falls through to the base game's shelf,
        /// so a modder overrides only what they made. Pushing is additive and
        /// ordered: last pushed is checked first.
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

        /// Drop every mod shelf — call on returning to the menu.
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

        /// True when anything at all can answer — base shelf or a mod's.
        public static bool Any => Base != null || _overrides.Count > 0;

        /// Editor convenience: pick the asset up again after it's created or
        /// moved, without restarting play mode.
        public static void Forget() { _searched = false; _base = null; }

        /// THE lookup: mod shelves first (newest first), then the base game.
        /// Null = nothing authored for this combination, so the soft-body blob
        /// handles it, exactly as before.
        public static GameObject Find(SurfaceMaterialType mat, int lines)
        {
            for (int i = _overrides.Count - 1; i >= 0; i--)
            {
                var hit = _overrides[i] != null ? _overrides[i].Resolve(mat, lines) : null;
                if (hit != null) return hit;
            }
            return Base != null ? Base.Resolve(mat, lines) : null;
        }

        /// The prefab for this material drawn with this many lines on THIS
        /// shelf alone, or null.
        public GameObject Resolve(SurfaceMaterialType mat, int lines)
        {
            foreach (var row in Materials)
            {
                if (row == null || row.Material != mat) continue;
                if (row.ByLines != null && lines >= 0 && lines < row.ByLines.Length
                    && row.ByLines[lines] != null)
                    return row.ByLines[lines];
                return row.Default;   // slot empty → this material's default
            }
            return null;              // no row → let the next shelf answer
        }
    }
}
