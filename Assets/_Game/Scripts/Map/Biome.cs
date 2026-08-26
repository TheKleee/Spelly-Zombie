using UnityEngine;

namespace SpellyZombie
{
    /// One biome = one box; the box's own bottom and top are its elevation
    /// band. Concrete flavours: GroundBiome (walkable land) and LiquidBiome
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
        [Range(0f, 8f)] public float FloorNoise = 1.5f;
        [Tooltip("Metres per noise bump. Bigger = wider, calmer hills.")]
        [Range(1f, 60f)] public float NoiseScale = 14f;
        [Tooltip("Your brush: the ground texture painted across this biome (the bed, for liquids).")]
        public TerrainLayer FloorLayer;

        [Header("PATHS")]
        [Tooltip("Can the verified path network run THROUGH here? A path may still LEAD to this biome's edge when off.")]
        public bool CanPath = true;
        [Tooltip("0 = dead straight (town streets), 1 = wandering trail. Routes bend by this much as they cross this biome.")]
        [Range(0f, 1f)]
        public float PathCurve = 0.35f;
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

        [Header("IMPOSED: this place WINS, past whatever you naturally are")]
        [Space(2)]
        // ★ THE FIRST OF TWO GROUPS, AND THEY DO NOT BEHAVE THE SAME.
        // Everything under this header is pushed onto you regardless of what
        // you are: stand in a 60 degree place with a natural of 30 and you go
        // to 60, which is how burning and freezing are possible at all.
        // The biome's natural state, on the same five axes an object carries.
        // What a source teaches is its own payload PLUS this, so one pebble
        // reads Dark in a forest and Light on a beach without authoring either.
        [Tooltip("IMPOSED. Ambient heat offset from lobby-natural. A frozen peak goes negative, magma goes high.")]
        [Range(-200, 300)] public int HeatOffset;
        [Tooltip("IMPOSED. Ambient luminance offset from lobby-natural. Deep shade and space go negative.")]
        [Range(-100, 100)] public int LightOffset;
        [Tooltip("IMPOSED. Ambient density offset. Thin air on a peak goes negative, deep or heavy ground goes positive.")]
        [Range(-100, 100)] public int DensityOffset;
        [Tooltip("IMPOSED. Ambient balance offset. How planted things are here - a swamp holds you, ice and polished stone let you slide.")]
        [Range(-100, 100)] public int StickOffset;
        [Tooltip("IMPOSED. Ambient affinity - gravity. Positive makes the things standing here attract other things to THEM; negative makes them repel. Never a pull toward the biome's own center.")]
        [Range(-100, 100)] public int AffinityOffset;

        [Header("ALLOWED: you get the LESSER of yours and this")]
        [Space(2)]
        // ★ THE SECOND GROUP. These are CAPACITIES, not impositions: a place
        // can hold you back but never lift you past what you are. A 120
        // strength biome leaves a 90-cap acolyte at 90; a dreadful place
        // unnerves the brave, and a safe one never emboldens a coward.
        [Tooltip("CAPACITY. How clear-headed things are here. LOW IS A CAPACITY, NOT A CURSE: a mindless place drags a sharp mind down, but a clever one never makes a stupid thing clever. 25 = ordinary, 100 = a genius place.")]
        [Range(0, 100)] public int IntCap = 25;

        [Tooltip("CAPACITY. How brave things are here. Same rule: a dreadful place unnerves the brave, a safe one does not embolden a coward. 25 = ordinary, 100 = nothing here fears anything.")]
        [Range(0, 100)] public int CourageCap = 25;

        [Tooltip("CAPACITY. How many copies of itself a thing naturally has here. 0 = ordinary. Anything above needs a body that can HAVE clones, or it gets none.")]
        [Range(0, 3)] public int ClonesCap;
        [Tooltip("IMPOSED. How SOLID this place is, on the same axis everything else carries. " +
                 "Below -50 is gas, ordinary air you walk through. -50 to +50 is liquid, you swim; " +
                 "the top of that band is mud. Past +50 is solid - a place nothing moves " +
                 "through, which is terrain, so it reads as raised ground with very dense " +
                 "liquid at its edges. A slider and not a dropdown because mud is a real " +
                 "place and sits between the named ones.")]
        [Range(-150, 150)] public int StateOffset = -150;

        /// The phase is READ OFF the number, never stored. Solid, liquid and
        /// gas are regions on one axis - which is why a place can be mud, and
        /// why heating a liquid thing far enough turns it to gas with nothing
        /// casting anything.
        public MatterPhase NaturalPhase => SpellPayload.PhaseOf(SpellPayload.FromHuman(4, StateOffset));

        [Header("SPAWNING")]
        [Tooltip("Mark ONE biome as the wizards' home - they all start here, scattered inside it. " +
                 "Every UNMARKED biome is acolyte ground: they spawn randomly across those, so they " +
                 "usually land apart but may share one. Mark none and a biome is chosen for you; " +
                 "mark them all and one is released back to the acolytes.")]
        public bool WizardSpawn;

        [Header("STRENGTH (strength IS health)")]
        [Tooltip("CAPACITY. How strong things naturally get here. Everything is pulled toward it, capped by " +
                 "its OWN ceiling - so a 90-cap acolyte sits at 90 in a 100 biome (strong for them) " +
                 "while a 140-cap wizard is dragged DOWN to 100. 0 = no ceiling, the natural world.")]
        [Range(0f, 500f)] public float StrengthCap;

        [Tooltip("Multiplies how fast strength mends here. 1 = natural. Below 1 for hostile ground " +
                 "where wounds linger; above 1 for a restful place.")]
        [Range(0f, 3f)] public float RegenScale = 1f;

        /// WHAT IS NATURAL HERE, as the one parameter set. Every carrier speaks
        /// this - the biome, the body, the spell - which is the whole point.
        /// Authored in HUMAN units - the same degrees, percent, HP and count
        /// a spell is authored in - and converted here, once, so a biome and
        /// a spell never disagree about what "50" means.
        public SpellPayload Natural => new SpellPayload
        {
            Temp = SpellPayload.FromHuman(0, HeatOffset),
            Lum = SpellPayload.FromHuman(1, LightOffset),
            Pressure = SpellPayload.FromHuman(2, DensityOffset),
            Balance = SpellPayload.FromHuman(3, StickOffset),
            State = SpellPayload.FromHuman(4, StateOffset),
            Affinity = SpellPayload.FromHuman(5, AffinityOffset),
            Strength = StrengthCap,
            Int = SpellPayload.FromHuman(7, IntCap),
            Courage = SpellPayload.FromHuman(8, CourageCap),
            Clones = ClonesCap,
        };

        public Bounds Area => new Bounds(transform.position, Size);

        /// Every biome in the scene. BiomeAt picks ONE winner by layer, which
        /// is right for terrain and fill - a forest cuts through a beach - but
        /// wrong for what the air is like. Standing where two overlap you are
        /// in both.
        static readonly System.Collections.Generic.List<Biome> _all =
            new System.Collections.Generic.List<Biome>();
        public static System.Collections.Generic.IReadOnlyList<Biome> All => _all;
        protected virtual void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        protected virtual void OnDisable() { _all.Remove(this); }

        /// THE COMPOSITE BIOME. Overlapping biomes make a new one where they
        /// meet simply by adding up - no intersection is authored, nothing is
        /// coded per pair, and a place that is both hot and heavy is hot and
        /// heavy because both boxes said so.
        public static SpellPayload CompositeAt(Vector3 world) => CompositeAt(world, out _);

        /// One pass that answers both questions - what the place is, and
        /// whether it is a place at all. Asking them separately walked every
        /// biome in the scene twice, for every element, five times a second.
        public static SpellPayload CompositeAt(Vector3 world, out bool any)
        {
            var sum = new SpellPayload();
            any = false;
            for (int i = 0; i < _all.Count; i++)
            {
                var b = _all[i];
                if (b == null || !b.Area.Contains(world)) continue;
                sum += b.Natural;
                any = true;
            }
            return sum;
        }

        /// Is this point inside any biome at all? The lobby has none, and
        /// there "nothing is imposed" is the right answer, not "everything is
        /// zero".
        public static bool AnyAt(Vector3 world)
        {
            for (int i = 0; i < _all.Count; i++)
                if (_all[i] != null && _all[i].Area.Contains(world)) return true;
            return false;
        }

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
