using UnityEngine;

namespace SpellyZombie
{
    /// Marker: ink drawn on this object (or any of its children) is never consumed
    /// by spell resolution. Put it on character roots and weapons.
    ///
    /// Persistent seals go "spent" instead of burning when their duration ends,
    /// and re-arm once the loop physically opens — so a seal drawn across a joint
    /// fires every time an emote/pose closes it, and a weapon's pre-drawn seal
    /// fires every trigger pull. Environment ink stays consumable to keep the
    /// world ink count (and frame rate) bounded.
    public class PersistentInkSurface : MonoBehaviour
    {
    }
}
