using UnityEngine;

namespace SpellyZombie
{
    /// An authored path node: the network must pass through here, and links
    /// are carved exactly. Radius > 0 makes it a roundabout. Parenting it
    /// under a Biome binds it: no auto nodes there, and it rides the shuffle.
    public class PathNode : MonoBehaviour
    {
        [Tooltip("0 = plain junction. >0 = roundabout: ring path at this radius, nothing ever spawns inside it.")]
        public float Radius = 0f;

        [Tooltip("Nodes this one connects to - drawing your sketch. Each link is carved exactly (bent only by the biome's PathCurve).")]
        public PathNode[] LinksTo;

        [Tooltip("Also let the auto web tie this node to its nearest neighbour. Off = only your drawn links (plus the minimal bridge that keeps the whole network connected, per the connectivity law).")]
        public bool JoinWeb = true;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.75f, 0.35f, 0.95f);
            Gizmos.DrawSphere(transform.position, 0.7f);

            if (Radius > 0.01f)
            {
                Vector3 prev = transform.position + new Vector3(Radius, 0f, 0f);
                for (int k = 1; k <= 24; k++)
                {
                    float ang = k / 24f * Mathf.PI * 2f;
                    Vector3 next = transform.position
                        + new Vector3(Mathf.Cos(ang) * Radius, 0f, Mathf.Sin(ang) * Radius);
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }
            }

            if (LinksTo == null) return;
            foreach (var other in LinksTo)
                if (other != null)
                    Gizmos.DrawLine(transform.position, other.transform.position);
        }
    }
}
