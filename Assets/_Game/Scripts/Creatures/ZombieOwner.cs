using UnityEngine;

namespace SpellyZombie
{
    /// Maps the dressed skin back to its Zombie. ZombieDress lives outside the
    /// zombie hierarchy, so GetComponentInParent&lt;Zombie&gt;() from the visible
    /// body finds nothing; this component carries the link.
    public class ZombieOwner : MonoBehaviour
    {
        public Zombie Of;

        /// Resolve a collider to its zombie whether it hit the physics capsule
        /// or the dressed skin. All "which zombie is this?" lookups come through here.
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
