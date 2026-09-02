using UnityEngine;

namespace SpellyZombie
{
    /// Marks the body's own skinned mesh INSIDE the body prefab, where the
    /// reference is legal. The rig reads this off any body it builds or
    /// adopts, so the Player prefab needs no cross prefab slot.
    public class BodySkin : MonoBehaviour
    {
        [Tooltip("The body's Skinned Mesh Renderer (SZ_Body). Assign it here in the body prefab itself.")]
        public SkinnedMeshRenderer Renderer;
    }
}
