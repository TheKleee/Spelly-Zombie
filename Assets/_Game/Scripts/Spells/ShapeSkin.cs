using UnityEngine;

namespace SpellyZombie
{
    /// ★ A SHAPE IS A POSE AND A MATERIAL TOGETHER. The bones say what it is
    /// shaped like; this says how it moves and breaks up. Saved onto the
    /// shape prefab by the Spell Creator, read back by anything that loads
    /// the shape - so a Repel carries its rim glow with its silhouette, and a
    /// tornado is a funnel that spins rather than a funnel and, separately,
    /// some sliders somebody has to remember.
    public class ShapeSkin : MonoBehaviour
    {
        public SpellTable.Look Look = new SpellTable.Look();
    }
}
