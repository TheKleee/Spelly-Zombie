using UnityEngine;

namespace SpellyZombie
{
    /// THE WAY BACK FROM THE SKIN TO THE ZOMBIE.
    ///
    /// ZombieDress deliberately lives OUTSIDE the zombie's hierarchy - the
    /// capsule's per-kind scale is non-uniform and skinned bones rotating under
    /// a non-uniform parent shear, so the outfit is a world-space follower
    /// instead of a child. That is the right call for the rig and a trap for
    /// everything else: anything that finds the visible body then asks
    /// GetComponentInParent&lt;Zombie&gt;() gets NOTHING, because there is no Zombie
    /// above the dress.
    ///
    /// Which bites exactly where it matters - a seal drawn on a zombie's actual
    /// skin could not detonate the zombie it was drawn on, since the detonation
    /// resolves its target by walking up from the collider it hit.
    ///
    /// One component, one field, so the lookup has an answer.
    public class ZombieOwner : MonoBehaviour
    {
        public Zombie Of;

        /// Resolve a collider to its zombie whether it hit the physics capsule
        /// (normal parenting) or the dressed skin (the follower). Everything
        /// that needs "which zombie is this?" should come through here rather
        /// than re-deriving it and getting the follower case wrong.
        public static Zombie From(Component c)
        {
            if (c == null) return null;
            var direct = c.GetComponentInParent<Zombie>();
            if (direct != null) return direct;
            var owner = c.GetComponentInParent<ZombieOwner>();
            return owner != null ? owner.Of : null;
        }
    }
}
