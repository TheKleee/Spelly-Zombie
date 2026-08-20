using UnityEngine;

namespace SpellyZombie
{
    /// Per-prop scan data for acolytes: ink reward, spent look, consume rules.
    /// A scan refills the wand and teaches the shape; a consumed prop is the
    /// trail wizards read. Lobby props set Consume off.
    public class Scannable : MonoBehaviour
    {
        [Header("WHAT SCANNING THIS GIVES BACK")]
        [Tooltip("Wand ink an acolyte gets for scanning this. Scanning is their only refill, " +
                 "so this is the map's ink economy. Bigger or rarer props can be worth more.")]
        public float InkReward = 25f;

        [Header("WHAT'S LEFT BEHIND - your call")]
        [Tooltip("Your effect at the moment it is scanned. Empty = the default poof.")]
        public GameObject ScanFx;
        [Tooltip("What a used-up prop looks like. Empty = it stays put and dims, see below. " +
                 "This is the TRAIL a wizard reads, so it should be visible at a glance.")]
        public GameObject Remains;
        [Tooltip("OFF only for a prop you want to be scannable forever (the lobby sets this " +
                 "for every prop). ON everywhere else: one prop, one scan, and the map runs down.")]
        public bool Consume = true;
        [Tooltip("With Consume on and no Remains prefab: destroy it, or leave it standing and " +
                 "just switch these renderers off.")]
        public bool DestroyOnScan;
        [Tooltip("Switched off instead of destroyed. Empty = this object's own renderers.")]
        public GameObject[] Dim;

        /// Already used. A spent prop is still scenery and still worth hiding
        /// behind, it just cannot be worn or milked again.
        public bool Spent { get; private set; }

        public bool CanScan => !Spent;

        /// Called by ShapeShift. Returns false when there was nothing to take.
        public bool Take()
        {
            if (Spent) return false;
            if (!Consume) return true;      // lobby props: endlessly reusable

            Spent = true;

            if (ScanFx != null) Instantiate(ScanFx, transform.position, Quaternion.identity);
            else if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, transform.position);

            if (Remains != null)
            {
                Instantiate(Remains, transform.position, transform.rotation, transform.parent);
                Destroy(gameObject);
                return true;
            }
            if (DestroyOnScan) { Destroy(gameObject); return true; }

            // it stays standing but visibly used: the trail
            if (Dim != null && Dim.Length > 0)
            {
                foreach (var go in Dim) if (go != null) go.SetActive(false);
            }
            else
            {
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.materials)
                        if (m.HasProperty("_BaseColor"))
                            m.SetColor("_BaseColor", m.GetColor("_BaseColor") * 0.55f);
            }
            return true;
        }
    }
}
