using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// Per-object ledger of ink laid on a thing - levitation authority comes
    /// from it (marking IS drawing). Shared control: stakes resolve into one
    /// proportional vector, never winner-takes-all.
    public class InkMark : MonoBehaviour
    {
        readonly Dictionary<int, float> _byOwner = new Dictionary<int, float>();

        /// Everyone's ink on this object combined.
        public float Total { get; private set; }

        /// BornOf: your own conjuration counts as a full wand of your ink.
        /// FreeForAll (ink ores) grants that full-wand authority to everyone.
        public int BornOf = -1;
        public bool FreeForAll;

        public float Authority(int ownerId)
        {
            _byOwner.TryGetValue(ownerId, out float ink);
            if (FreeForAll || (BornOf >= 0 && BornOf == ownerId)) ink += DrawingConfig.InkMax;
            return ink;
        }

        public void Add(int ownerId, float ink)
        {
            if (ink <= 0f) return;
            _byOwner.TryGetValue(ownerId, out float had);
            _byOwner[ownerId] = had + ink;
            Total += ink;
        }

        /// Scrubbing your ink off returns it to the wand - and with it goes
        /// your hold.
        public float Remove(int ownerId, float ink)
        {
            if (!_byOwner.TryGetValue(ownerId, out float had) || had <= 0f) return 0f;
            float taken = Mathf.Min(had, ink);
            _byOwner[ownerId] = had - taken;
            Total -= taken;
            return taken;
        }

        /// Everyone with a stake, so a shared hold can resolve one vector.
        public IEnumerable<KeyValuePair<int, float>> Stakes => _byOwner;

        /// ALL your ink anywhere under this object. Strokes land on whichever
        /// collider they hit, so the lift must look at the whole subtree.
        public static float AuthorityIn(Transform root, int ownerId)
        {
            if (root == null) return 0f;
            return AuthorityIn(root.GetComponentsInChildren<InkMark>(true), ownerId);
        }

        /// Same law over a pre-fetched ledger set (HandGrab caches at grab
        /// time). Counts only strokes currently alive on the object; the
        /// ledger itself only carries ownership flags.
        public static float AuthorityIn(InkMark[] marks, int ownerId)
        {
            if (marks == null) return 0f;
            float total = 0f;
            var world = DrawingWorld.Instance;
            foreach (var m in marks)
            {
                if (m == null) continue;
                if (m.FreeForAll || m.BornOf == ownerId) total += DrawingConfig.InkMax;
                if (world == null) continue;
                var host = m.transform;
                for (int i = 0; i < world.Strokes.Count; i++)
                {
                    var s = world.Strokes[i];
                    if (s == null || !s.Alive || s.OwnerId != ownerId || s.Surface == null) continue;
                    if (s.Surface != host && !s.Surface.IsChildOf(host)) continue;
                    total += s.PathLength() * DrawingConfig.InkCostPerMeter;
                }
            }
            return total;
        }

        /// Find (or start) the ledger on whatever owns this collider.
        public static InkMark For(Transform t, bool create)
        {
            if (t == null) return null;
            Transform host = Host(t);
            var mark = host.GetComponent<InkMark>();
            if (mark == null && create) mark = host.gameObject.AddComponent<InkMark>();
            return mark;
        }

        /// The thing the ink belongs to: a rigidbody if there is one, else the
        /// collider's own object - NOT t.root, which on static scenery walks
        /// up to the map root and would mark the entire level.
        public static Transform Host(Transform t)
        {
            var rb = t.GetComponentInParent<Rigidbody>();
            if (rb != null) return rb.transform;
            var col = t.GetComponentInParent<Collider>();
            return col != null ? col.transform : t;
        }

        /// What holds rooted scenery down. Tearing a thing free costs exactly
        /// what holding it up costs; once torn free it stays free.
        public static float AnchorHold(Transform host)
        {
            return EstimateMass(host) * DrawingConfig.LiftInkPerKg;
        }

        /// Base kg of a small object in the host's material - the first half
        /// of weight = material × size. An authored Liftable.Mass overrides.
        public static float MaterialBaseKg(Transform host)
        {
            var tag = host != null ? host.GetComponentInChildren<SurfaceMaterialTag>(true) : null;
            if (tag == null && host != null) tag = host.GetComponentInParent<SurfaceMaterialTag>();
            switch (tag != null ? tag.Material : SurfaceMaterialType.Unknown)
            {
                case SurfaceMaterialType.Slime:   return 4f;
                case SurfaceMaterialType.Water:   return 6f;
                case SurfaceMaterialType.Wood:    return 8f;
                case SurfaceMaterialType.Bone:    return 8f;
                case SurfaceMaterialType.Coal:    return 14f;
                case SurfaceMaterialType.Flesh:   return 15f;
                case SurfaceMaterialType.Earth:   return 22f;
                case SurfaceMaterialType.Stone:   return 30f;
                case SurfaceMaterialType.Metal:   return 40f;
                case SurfaceMaterialType.Gold:    return 60f;
                case SurfaceMaterialType.Diamond: return 45f;
                default:                          return DrawingConfig.PropMassKg;
            }
        }

        /// The second half: the size step multiplies it. Small ×1 … XXL ×5.
        /// Anything without a Liftable is treated as Medium.
        public static float EstimateMass(Transform host)
        {
            var lift = host != null ? host.GetComponentInParent<Liftable>() : null;
            int size = lift != null ? (int)lift.Size : (int)PropSize.Medium;
            return MaterialBaseKg(host) * Mathf.Max(1, size);
        }
    }
}
