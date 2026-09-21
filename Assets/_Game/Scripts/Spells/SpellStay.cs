using UnityEngine;

namespace SpellyZombie
{
    /// The one place a spell particle hears OnTriggerStay. Worn only by an awake particle that
    /// delivers to whatever stays inside it (SpellParticle.ListenForStays): Unity sends the
    /// message for every collider in a trigger on every physics step, so a mote that would only
    /// throw it away must not have the method at all.
    class SpellStay : MonoBehaviour
    {
        public SpellParticle Mote;

        /// How many times a particle was told something is still inside it (the Stress Test reads and clears it).
        public static int Calls;

        void OnTriggerStay(Collider other)
        {
            Calls++;
            if (Mote == null) return;
            using (PerfMarkers.SpellStays.Auto()) Mote.Stay(other);
        }
    }
}
