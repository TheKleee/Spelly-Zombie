using UnityEngine;

namespace SpellyZombie
{
    /// One biome = one box, and the box IS the elevation band (its own
    /// bottom and top). No kinds, no enum: a biome is whatever its NAME and
    /// its definition say - "Forest", "Magma", "Space". Boxes snap, never
    /// overlap, and same-definition neighbours read as one region.
    /// Concrete flavours: GroundBiome (walkable land) and LiquidBiome
    /// (a volume you float and sink in).
    public abstract class Biome : MonoBehaviour
    {
        [Tooltip("Box size in metres. Center = this transform's position. The box's OWN bottom/top Y are the biome's elevation band.")]
        public Vector3 Size = new Vector3(30f, 8f, 30f);

        [Tooltip("Overlap rule: a HIGHER layer CUTS THROUGH lower ones where they overlap - a forest placed over a desert leaves desert only at the edges. Newly added biomes take the next layer automatically.")]
        public int Layer = 1;

        [Range(0f, 1f)]
        [Tooltip("The untouchable core: this fraction of the footprint around the center can NEVER be cut by a higher layer or covered by a shuffle - intruders are pushed off it. 0 = fully erasable (a beach a same-sized forest may delete outright); 1 = the whole box is sacred (the Center biome: wizard spawn + where the ink grounds once all pots are broken).")]
        public float ProtectedCore = 0f;

        [Header("TERRAIN")]
        [Tooltip("Roughness: how far the ground wanders inside the band. 0 = flat. For a liquid this shapes the FLOOR beneath it.")]
        public float FloorNoise = 1.5f;
        [Tooltip("Metres per noise bump. Bigger = wider, calmer hills.")]
        public float NoiseScale = 14f;
        [Tooltip("Your brush: the ground texture painted across this biome (the bed, for liquids).")]
        public TerrainLayer FloorLayer;

        [Header("PATHS")]
        [Tooltip("Can the verified path network run THROUGH here? A path may still LEAD to this biome's edge when off.")]
        public bool CanPath = true;
        [Tooltip("Your brush for paths inside this biome (cobble in town, dirt in forest). Empty = FloorLayer carries the path.")]
        public TerrainLayer PathLayer;
        [Tooltip("Child empties marking where routes may enter (gaps in fences, ramps through cliffs). Empty = anywhere along the border.")]
        public Transform[] Entries;

        [Header("LAYOUT (randomized maps)")]
        [Tooltip("May the randomizer move this box on X,Z? Off = pinned at the authored spot; the seed still varies the terrain inside it. Y never moves, sizes never change, and a box only shuffles WITHIN the lower-layer biome it sits in - nesting is the relationship, so no adjacency lists exist.")]
        public bool RandomPosition = true;

        [Header("FILL")]
        [Tooltip("Grid field size in metres for this box (peak wants small fields, town wants building-sized).")]
        public float FieldSize = 2f;
        [Tooltip("THE ABUNDANCE: what fills this biome up. Ground biomes place on the surface; liquids float their spawns INSIDE the volume.")]
        public GameObject[] Props;

        [Tooltip("THE SCARCITY: absorbables that exist to be found - spawned rare, guaranteed between Min and Max. A torch carrier, a lone ore rock, a floating blob holder.")]
        public GameObject[] Sources;
        [Tooltip("At least this many Sources always appear here.")]
        public int MinSources = 1;
        [Tooltip("Never more than this many - wizards must stay hungry.")]
        public int MaxSources = 3;

        [Header("CAULDRON")]
        [Tooltip("The pot prefab this biome MAY host, at a random spot inside it. Empty = never a cauldron here. The map's CauldronLimit picks which candidate biomes actually get theirs each match.")]
        public GameObject Cauldron;

        [Header("LANDMARK")]
        [Tooltip("YOUR centerpiece prefab, spawned standing at this biome's center on generation. The Center biome wants one - it marks where wizards spawn and where the ink grounds.")]
        public GameObject Landmark;

        [Header("ENVIRONMENT (applies when the biome-average pass lands)")]
        [Tooltip("Ambient heat offset from lobby-natural. A frozen peak goes negative, magma goes high.")]
        public float HeatOffset;
        [Tooltip("Ambient luminance offset from lobby-natural. Deep shade and space go negative.")]
        public float LightOffset;

        public Bounds Area => new Bounds(transform.position, Size);

        /// Scene-view identity color; each flavour decides where it comes from.
        public abstract Color GizmoTint { get; }

        protected virtual void Reset()
        {
            // each new biome takes the next layer: placement order = cut order
            int top = 0;
            foreach (var b in FindObjectsByType<Biome>(FindObjectsSortMode.None))
                if (b != this && b.Layer > top) top = b.Layer;
            Layer = top + 1;
        }

        protected virtual void OnValidate()
        {
            // boxes snap: half-metre grid on position and size
            var p = transform.position;
            transform.position = new Vector3(Snap(p.x), Snap(p.y), Snap(p.z));
            Size = new Vector3(Mathf.Max(1f, Snap(Size.x)),
                               Mathf.Max(1f, Snap(Size.y)),
                               Mathf.Max(1f, Snap(Size.z)));
        }

        static float Snap(float v) => Mathf.Round(v * 2f) / 2f;

        void OnDrawGizmos()
        {
            Color c = GizmoTint;
            c.a = 1f;
            Gizmos.color = c;
            Gizmos.DrawWireCube(transform.position, Size);
            c.a = 0.06f;
            Gizmos.color = c;
            Gizmos.DrawCube(transform.position, Size);

            if (ProtectedCore > 0.01f)
            {
                c.a = 0.85f;
                Gizmos.color = c;
                Gizmos.DrawWireCube(transform.position,
                    new Vector3(Size.x * ProtectedCore, Size.y, Size.z * ProtectedCore));
            }

            if (Entries == null) return;
            Gizmos.color = new Color(0.75f, 0.35f, 0.95f); // path purple
            foreach (var e in Entries)
                if (e != null) Gizmos.DrawSphere(e.position, 0.6f);
        }
    }
}
