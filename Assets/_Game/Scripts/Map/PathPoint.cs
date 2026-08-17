using UnityEngine;

namespace SpellyZombie
{
    /// Mark a spot on a prefab the verified path network must reach - a
    /// house door, a forest monument's entrance. After placement the
    /// generator grows a spur from the nearest path to every one of these,
    /// and keeps that spur clear of spawns like any other path cell.
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
