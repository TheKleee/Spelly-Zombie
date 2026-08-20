using UnityEngine;

namespace SpellyZombie
{
    /// The object's claim: put this on a prefab root and size the box around
    /// the whole thing. The spawner keeps every other spawn out of it; paths
    /// never cut through it and reach it only at PathPoint children (doors).
    public class ObjectBox : MonoBehaviour
    {
        [Tooltip("The claimed space in local metres, centered on Center. Box bottom = the ground line.")]
        public Vector3 Size = new Vector3(2f, 2f, 2f);
        [Tooltip("Local offset of the claim, for prefabs whose pivot is not at the middle of their footprint.")]
        public Vector3 Center = new Vector3(0f, 1f, 0f);

        public Bounds LocalArea => new Bounds(Center, Size);

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(Center, Size);
        }
    }
}
