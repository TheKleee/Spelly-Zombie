using UnityEngine;

namespace SpellyZombie
{
    /// A volume you float and sink in: water, magma, space liquid. The
    /// generator shapes the floor beneath it and floats this biome's fill
    /// inside the volume instead of on the bed.
    public class LiquidBiome : Biome
    {
        [Header("LIQUID")]
        [Tooltip("The liquid's color: blue water, red magma, dark space. Tints the volume, the fog inside it, and the gizmo.")]
        public Color Tint = new Color(0.25f, 0.55f, 0.95f, 0.6f);
        [Tooltip("YOUR surface prefab (water shader plane, magma sheet), stretched across the box top so it reads from above. Empty = no visible surface.")]
        public GameObject Surface;
        [Tooltip("1 = you float on top, just under 1 = the slow sink (water), 0 = no lift at all.")]
        [Range(0f, 1f)] public float Buoyancy = 0.92f;

        public override Color GizmoTint => Tint;

        static readonly System.Collections.Generic.List<LiquidBiome> _liquids =
            new System.Collections.Generic.List<LiquidBiome>();
        protected override void OnEnable() { base.OnEnable(); _liquids.Add(this); }
        protected override void OnDisable() { base.OnDisable(); _liquids.Remove(this); }

        /// The liquid volume containing this world point, or null. Cheap:
        /// a couple of box tests, safe in any per-frame path.
        public static LiquidBiome At(Vector3 p)
        {
            for (int i = 0; i < _liquids.Count; i++)
                if (_liquids[i] != null && _liquids[i].Area.Contains(p)) return _liquids[i];
            return null;
        }

        protected override void Reset()
        {
            base.Reset(); // takes the next layer
            // liquids default to no through-paths and a calm bed
            CanPath = false;
            FloorNoise = 0.5f;
        }
    }
}
