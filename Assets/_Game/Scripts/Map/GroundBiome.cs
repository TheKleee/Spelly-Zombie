using UnityEngine;

namespace SpellyZombie
{
    /// Walkable land: forest, town, beach, stone tiers - whatever the name
    /// says. The generator raises ground into this box's band and places
    /// the fill on its surface.
    public class GroundBiome : Biome
    {
        [Tooltip("Scene-view color for this region. Pick something that reads as the biome.")]
        public Color GizmoColor = new Color(0.25f, 0.75f, 0.35f);

        public override Color GizmoTint => GizmoColor;
    }
}
