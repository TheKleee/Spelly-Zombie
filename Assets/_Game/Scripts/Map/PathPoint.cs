using UnityEngine;

namespace SpellyZombie
{
    /// Marks a spot on a prefab the path network must reach - a door, an
    /// entrance. After placement the generator grows a spur from the nearest
    /// path to it and keeps that spur clear of spawns.
    public class PathPoint : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.75f, 0.35f, 0.95f);
            Gizmos.DrawSphere(transform.position, 0.35f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.9f);
        }
    }
}
