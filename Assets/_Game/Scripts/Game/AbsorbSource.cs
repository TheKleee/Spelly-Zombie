using UnityEngine;

namespace SpellyZombie
{
    /// A THING THAT TEACHES. Pure data: it holds the runes it can teach and
    /// nothing else - no input, no Update, no behaviour. The wizard aims at it,
    /// the badge offers F, and GrimoireAbsorb does the absorbing, exactly the
    /// way the acolyte's scan works.
    ///
    /// That is also what makes it safe to hand to map authors: attaching one
    /// attaches a label, never code.
    public class AbsorbSource : MonoBehaviour
    {
        [Tooltip("The runes this object teaches, IN THIS ORDER. Each absorb grants the next one the player is missing.")]
        public RuneType[] Teaches;

        [Tooltip("ON: a training element — absorb it forever, it never vanishes. OFF: one absorb consumes the object (the wizard trail).")]
        public bool Infinite = true;

        [Tooltip("How close the wizard must stand for the absorb to work, meters.")]
        public float Range = 3.5f;

        /// The next rune this would teach the owner - None when it is spent.
        /// Already-known runes are skipped, so a source is invisible to anyone
        /// who has learned everything on its list.
        public RuneType NextFor(int owner)
        {
            if (Teaches == null) return RuneType.None;
            foreach (var r in Teaches)
                if (r != RuneType.None && !Grimoire.HasRune(owner, r)) return r;
            return RuneType.None;
        }

        /// Teach the next missing rune. False when it has nothing left to give.
        /// The caller has already decided the aim and the key.
        public bool Teach(int owner)
        {
            var rune = NextFor(owner);
            if (rune == RuneType.None)
            {
                DrawingWorld.Instance?.LogEvent("this taught you everything it knows");
                return false;
            }
            Grimoire.UnlockRune(owner, rune);
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent($"absorbed: it teaches {RuneLibrary.Icon(rune)}");
            if (!Infinite) Destroy(gameObject);
            return true;
        }
    }
}
