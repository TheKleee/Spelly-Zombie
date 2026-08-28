using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ AN INTERIOR/EXTERIOR FIELD (his design, Aug 27): a region that
    /// belongs to a PREFAB, not to a biome - a house's inside, the ground
    /// around a tree. When the prefab lands in the world, the field fills
    /// itself with random details exactly the way a biome fills its box:
    /// grid fields, jittered points, a ray down, a claim. Every match the
    /// clutter is different, which is what lets an acolyte hide in it.
    ///
    /// It does NOT write biome data - it only places things.
    ///
    /// UPPER FLOOR: author-set ("some houses simply have an upper floor").
    /// The structure goes in FIRST - the ceiling slab, a HOLE at a random
    /// spot in it, stairs leading up to the hole - and only then the random
    /// details fill both floors.
    ///
    /// Details are ordinary prefabs, so they can be anything he sets up -
    /// including an AbsorbSource that teaches.
    public class InteriorField : MonoBehaviour
    {
        [System.Serializable]
        public class Detail
        {
            public GameObject Prefab;
            [Range(0f, 1f)]
            [Tooltip("Per grid field: the chance this detail tries to spawn there.")]
            public float Chance = 0.25f;
            [Tooltip("Never more than this many of it in one field.")]
            public int Max = 3;
        }

        [Tooltip("The region, local to this prefab. Inside a house, or the ground around a tree - the field doesn't care which.")]
        public Vector3 Size = new Vector3(6f, 3f, 6f);
        public Vector3 Center = new Vector3(0f, 1.5f, 0f);

        [Tooltip("Grid field size in metres - the same searching law a biome box uses.")]
        public float FieldSize = 1.2f;

        [Tooltip("What may appear here. Prefabs can be anything - flowers, pebbles, furniture, an AbsorbSource.")]
        public Detail[] Details;

        [Header("UPPER FLOOR (leave Ceiling empty for a single floor)")]
        [Tooltip("The slab dividing the field into two floors. Author-set: some houses simply have one.")]
        public GameObject Ceiling;
        [Tooltip("Local height of the slab above this transform.")]
        public float CeilingHeight = 2.6f;
        [Tooltip("The opening in the slab, placed at a RANDOM spot each match. Its prefab is the visible rim/trapdoor.")]
        public GameObject Hole;
        [Tooltip("Instantiated beneath the hole, leading up to it. Author the prefab with its base at the pivot, climbing +Z.")]
        public GameObject Stairs;

        bool _filled;

        /// Hand-placed in a scene (studio, lobby): fill itself with a seed
        /// carved from where it stands, so every client rolls the same room.
        void Start()
        {
            if (_filled) return;
            int seed = name.GetHashCode()
                ^ Mathf.RoundToInt(transform.position.x * 73856093f)
                ^ Mathf.RoundToInt(transform.position.z * 19349663f);
            Fill(new System.Random(seed));
        }

        /// Map generation calls this with ITS rng, so the fill rides the map
        /// seed and stays identical on every machine.
        public void Fill(System.Random rng)
        {
            if (_filled) return;
            _filled = true;
            Physics.SyncTransforms(); // the house may be seconds old - its floors must catch rays

            var claims = new List<Bounds>();
            Bounds area = new Bounds(transform.TransformPoint(Center),
                Vector3.Scale(Size, transform.lossyScale));

            // ---- 1. STRUCTURE FIRST (his order): ceiling, hole, stairs ----
            float holeLX = 0f, holeLZ = 0f;
            bool twoFloors = Ceiling != null;
            if (twoFloors)
            {
                var slab = Instantiate(Ceiling, transform);
                slab.transform.localPosition = new Vector3(Center.x, CeilingHeight, Center.z);
                slab.transform.localRotation = Quaternion.identity;

                if (Hole != null)
                {
                    // a random spot in the slab's inner 60%, new every match
                    holeLX = Center.x + ((float)rng.NextDouble() - 0.5f) * Size.x * 0.6f;
                    holeLZ = Center.z + ((float)rng.NextDouble() - 0.5f) * Size.z * 0.6f;
                    var hole = Instantiate(Hole, transform);
                    hole.transform.localPosition = new Vector3(holeLX, CeilingHeight, holeLZ);

                    if (Stairs != null)
                    {
                        // base on the ground floor, facing the hole from the
                        // side with the most room, climbing toward it
                        Vector3 toCenter = new Vector3(Center.x - holeLX, 0f, Center.z - holeLZ);
                        if (toCenter.sqrMagnitude < 0.04f) toCenter = Vector3.forward;
                        toCenter.Normalize();
                        var stairs = Instantiate(Stairs, transform);
                        stairs.transform.localPosition = new Vector3(
                            holeLX + toCenter.x * CeilingHeight * 0.9f,
                            Center.y - Size.y * 0.5f,
                            holeLZ + toCenter.z * CeilingHeight * 0.9f);
                        stairs.transform.localRotation = Quaternion.LookRotation(-toCenter);
                        // the stairwell stays clear of clutter
                        claims.Add(new Bounds(
                            stairs.transform.position + Vector3.up * CeilingHeight * 0.5f,
                            new Vector3(1.6f, CeilingHeight, CeilingHeight * 1.2f)));
                    }
                    claims.Add(new Bounds(
                        transform.TransformPoint(new Vector3(holeLX, CeilingHeight, holeLZ)),
                        new Vector3(1.6f, 1f, 1.6f)));
                }
            }

            // ---- 2. THEN THE RANDOM DETAILS, both floors ----
            if (Details == null || Details.Length == 0) return;
            var placed = new int[Details.Length];

            int nx = Mathf.Max(1, Mathf.FloorToInt(Size.x / FieldSize));
            int nz = Mathf.Max(1, Mathf.FloorToInt(Size.z / FieldSize));
            int floors = twoFloors ? 2 : 1;

            for (int floor = 0; floor < floors; floor++)
                for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        int di = rng.Next(Details.Length);
                        var d = Details[di];
                        if (d == null || d.Prefab == null) continue;
                        if (placed[di] >= Mathf.Max(1, d.Max)) continue;
                        if (rng.NextDouble() > d.Chance) continue;

                        float lx = Center.x - Size.x * 0.5f + (ix + 0.5f) * FieldSize
                            + ((float)rng.NextDouble() - 0.5f) * FieldSize * 0.6f;
                        float lz = Center.z - Size.z * 0.5f + (iz + 0.5f) * FieldSize
                            + ((float)rng.NextDouble() - 0.5f) * FieldSize * 0.6f;

                        // ray from just under this floor's own roof, exactly
                        // the biome law but inside the prefab's world
                        float topLY = floor == 0
                            ? (twoFloors ? CeilingHeight - 0.15f : Center.y + Size.y * 0.5f)
                            : Center.y + Size.y * 0.5f;
                        Vector3 from = transform.TransformPoint(new Vector3(lx, topLY, lz));
                        float maxDist = twoFloors && floor == 0
                            ? CeilingHeight + 0.5f : Size.y + 0.5f;
                        if (!Physics.Raycast(from, Vector3.down, out var hit, maxDist,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            continue;
                        if (!area.Contains(hit.point)) continue;
                        if (hit.normal.y < 0.7f) continue;        // walls are not floors
                        if (twoFloors && floor == 1
                            && hit.point.y < transform.TransformPoint(
                                new Vector3(0f, CeilingHeight - 0.3f, 0f)).y)
                            continue; // the upper pass only dresses the slab

                        var box = d.Prefab.GetComponent<ObjectBox>();
                        Vector3 claimSize = box != null ? box.Size
                            : Vector3.one * (FieldSize * 0.8f);
                        var claim = new Bounds(hit.point + (box != null ? box.Center
                            : Vector3.up * claimSize.y * 0.5f), claimSize);
                        bool blocked = false;
                        for (int i = 0; i < claims.Count && !blocked; i++)
                            if (claims[i].Intersects(claim)) blocked = true;
                        if (blocked) continue;
                        claims.Add(claim);

                        var go = Instantiate(d.Prefab, hit.point,
                            Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up),
                            transform);
                        if (go.GetComponentInChildren<Element>(true) == null
                            && go.GetComponentInChildren<Collider>(true) != null)
                            go.AddComponent<Element>();
                        placed[di]++;
                    }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.9f, 0.6f, 0.5f);
            Gizmos.DrawWireCube(Center, Size);
            if (Ceiling != null)
            {
                Gizmos.color = new Color(0.9f, 0.7f, 0.3f, 0.6f);
                Gizmos.DrawWireCube(new Vector3(Center.x, CeilingHeight, Center.z),
                    new Vector3(Size.x, 0.05f, Size.z));
            }
        }
    }
}
