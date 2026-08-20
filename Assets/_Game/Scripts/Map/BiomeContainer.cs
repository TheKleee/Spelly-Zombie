using UnityEngine;

namespace SpellyZombie
{
    /// Listed biomes may only exist - and shuffle - inside this box; anything
    /// not listed lives free. Keep it comfortably smaller than the loop
    /// distance so the wrap seam always sits in open water.
    public class BiomeContainer : MonoBehaviour
    {
        [Tooltip("Container size in metres. Center = this transform. Listed biomes never spawn or shuffle past these walls.")]
        public Vector3 Size = new Vector3(80f, 60f, 80f);

        [Tooltip("The biomes locked to this container. Not listed (water) = free to span the whole map.")]
        public Biome[] Contained;

        public Bounds Area => new Bounds(transform.position, Size);

        void OnValidate()
        {
            var p = transform.position;
            transform.position = new Vector3(Snap(p.x), Snap(p.y), Snap(p.z));
            Size = new Vector3(Mathf.Max(1f, Snap(Size.x)),
                               Mathf.Max(1f, Snap(Size.y)),
                               Mathf.Max(1f, Snap(Size.z)));
        }

        static float Snap(float v) => Mathf.Round(v * 2f) / 2f;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
            Gizmos.DrawWireCube(transform.position, Size);
        }
    }
}
