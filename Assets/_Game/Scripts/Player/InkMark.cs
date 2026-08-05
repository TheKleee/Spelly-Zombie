using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// YOUR CLAIM ON A THING (Marko's lifting rules): you don't grab, you
    /// LEVITATE what you MARKED — and "the more ink you spend on something the
    /// higher your levitational power".
    ///
    /// This is that ledger. Marking IS drawing: every stroke you lay on an
    /// object records its ink cost here, per player. No new verb, no new key,
    /// and the wand shrinks exactly as it always did.
    ///
    /// Control is SHARED BY SHARE: two wizards on one crate each steer it in
    /// proportion to the ink they poured in, resolved into ONE vector — never
    /// winner-takes-all, so nobody can freeze an objective by pulling back.
    public class InkMark : MonoBehaviour
    {
        readonly Dictionary<int, float> _byOwner = new Dictionary<int, float>();

        /// Everyone's ink on this object combined.
        public float Total { get; private set; }

        /// FULL-WAND OWNERSHIP: a thing YOU conjured answers to you as if you
        /// had poured a WHOLE wand into it — a full one, not your current
        /// stub, because you need the advantage on your own spells. Ink ores
        /// set FreeForAll so anyone can lift them with no wand at all, and
        /// extra ink on top still buffs you for a tug-of-war.
        public int BornOf = -1;
        public bool FreeForAll;

        public float Authority(int ownerId)
        {
            _byOwner.TryGetValue(ownerId, out float ink);
            if (FreeForAll || (BornOf >= 0 && BornOf == ownerId)) ink += Perks.InkMax;
            return ink;
        }

        public void Add(int ownerId, float ink)
        {
            if (ink <= 0f) return;
            _byOwner.TryGetValue(ownerId, out float had);
            _byOwner[ownerId] = had + ink;
            Total += ink;
        }

        /// Scrubbing your ink off returns it to the wand — and with it goes
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
        /// collider they hit, which may be a child, a sibling or the root —
        /// so the lift must look at the whole thing, not one exact transform.
        /// Getting this wrong made ink you'd clearly drawn count as zero.
        public static float AuthorityIn(Transform root, int ownerId)
        {
            if (root == null) return 0f;
            return AuthorityIn(root.GetComponentsInChildren<InkMark>(true), ownerId);
        }

        /// Same law over a pre-fetched ledger set — HandGrab caches the held
        /// subtree once at grab time (no per-frame GetComponentsInChildren).
        /// LIVE INK ONLY (Marko, Aug 5: "you can only lift based on how much
        /// of your ink it has THE MOMENT you try to lift it"). The ledger
        /// remembered every stroke ever drawn, so ERASED ink kept granting
        /// lift. Authority is now counted from the strokes actually alive on
        /// the object right now; the ledger only carries ownership flags.
        public static float AuthorityIn(InkMark[] marks, int ownerId)
        {
            if (marks == null) return 0f;
            float total = 0f;
            var world = DrawingWorld.Instance;
            foreach (var m in marks)
            {
                if (m == null) continue;
                if (m.FreeForAll || m.BornOf == ownerId) total += Perks.InkMax;
                if (world == null) continue;
                var host = m.transform;
                for (int i = 0; i < world.Strokes.Count; i++)
                {
                    var s = world.Strokes[i];
                    if (s == null || !s.Alive || s.OwnerId != ownerId || s.Surface == null) continue;
                    if (s.Surface != host && !s.Surface.IsChildOf(host)) continue;
                    total += s.PathLength() * DrawingConfig.InkCostPerMeter * Perks.InkCostMul;
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
        /// collider's own object — NOT t.root, which on static scenery walks
        /// up to the map root and would mark the entire level.
        public static Transform Host(Transform t)
        {
            var rb = t.GetComponentInParent<Rigidbody>();
            if (rb != null) return rb.transform;
            var col = t.GetComponentInParent<Collider>();
            return col != null ? col.transform : t;
        }

        /// WHAT HOLDS IT DOWN: static scenery is rooted, and you must overpower
        /// that before it will ever float. Estimated from its size so no
        /// per-prop authoring is needed — a bench yields, a house doesn't.
        /// Once torn free it stays free.
        /// ROOT IS NEVER STRONGER THAN THE LIFT (Marko: "root should never be
        /// that strong — the moment you can easily lift it you should be able
        /// to unroot it"). So there is no second gate: tearing a thing out of
        /// the ground costs exactly what holding it up costs. If you can carry
        /// it, you can free it.
        public static float AnchorHold(Transform host)
        {
            return EstimateMass(host) * DrawingConfig.LiftInkPerKg;
        }

        /// The weight of a prop nobody has authored a mass on.
        ///
        /// GUESSING FROM SIZE WAS NONSENSE (Marko: "this is making the chair
        /// somehow heavier than the cauldron and books as well"). Bounding
        /// boxes are world-space and axis-aligned, so a chair's splayed legs
        /// and tall back inflate its box far past the real object while a
        /// compact cauldron measures small — the number tracked a shape's
        /// bounding box, not its heft, and no amount of tuning fixes that.
        ///
        /// So: everything unauthored weighs the SAME, and it's light. If a
        /// specific thing should be heavy, put a Liftable on it and type a
        /// Mass — one number, in your hands, doing exactly what it says.
        /// WEIGHT = WHAT IT'S MADE OF × HOW BIG IT IS (Marko's rule). This is
        /// the first half: the base weight of a small object in that material.
        /// Bounding boxes never enter into it — a chair outweighing a cauldron
        /// was the whole problem with measuring shapes.
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
