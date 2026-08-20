using UnityEngine;

namespace SpellyZombie
{
    /// Marker: ink drawn on this object (or any child) is never consumed by spell
    /// resolution. Put it on character roots and weapons. Persistent seals go
    /// "spent" when their duration ends and re-arm once the loop opens.
    public class PersistentInkSurface : MonoBehaviour
    {
    }
}
