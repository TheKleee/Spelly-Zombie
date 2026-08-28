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
        [Tooltip("The runes this object teaches, IN THIS ORDER. Each absorb grants the next one the player is missing. Leave empty to teach from the AXES below instead.")]
        public RuneType[] Teaches;

        [Header("AXES (used when Teaches is empty)")]
        [Tooltip("What was added upon this object, in human units. Each absorb gives away the strongest remaining axis as its rune - heat 50 + light 35 teaches Heat first, Light on the next visit. Sign picks the rune: -40 light is Dark. Per wizard: a spent source still teaches everyone else.")]
        public int Temperature;
        public int Light;
        public int Density;
        public int Balance;
        public int State;
        public int Affinity;

        static RuneType RuneFor(int axis, int value) => axis switch
        {
            0 => value > 0 ? RuneType.HeatUp : RuneType.HeatDown,
            1 => value > 0 ? RuneType.LuminanceUp : RuneType.LuminanceDown,
            2 => value > 0 ? RuneType.DensityUp : RuneType.DensityDown,
            3 => value > 0 ? RuneType.StickyUp : RuneType.StickyDown,
            4 => value > 0 ? RuneType.StateSolid : RuneType.StateLiquid,
            _ => value > 0 ? RuneType.DirectionAway : RuneType.DirectionToward,
        };

        /// The teach order: authored list as-is, or the axes ranked by how
        /// much of each was added - the strongest gives itself away first.
        System.Collections.Generic.IEnumerable<RuneType> TeachOrder()
        {
            if (Teaches != null && Teaches.Length > 0)
            {
                foreach (var r in Teaches) yield return r;
                yield break;
            }
            int[] vals = { Temperature, Light, Density, Balance, State, Affinity };
            var order = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4, 5 };
            order.Sort((a, b) => Mathf.Abs(vals[b]).CompareTo(Mathf.Abs(vals[a])));
            foreach (int ax in order)
                if (vals[ax] != 0) yield return RuneFor(ax, vals[ax]);
        }

        [Tooltip("ON: a training element — absorb it forever, it never vanishes. OFF: one absorb consumes the object (the wizard trail).")]
        public bool Infinite = true;

        [Tooltip("How close the wizard must stand for the absorb to work, meters.")]
        public float Range = 3.5f;

        /// The next rune this would teach the owner - None when it is spent.
        /// Already-known runes are skipped, so a source is invisible to anyone
        /// who has learned everything on its list.
        public RuneType NextFor(int owner)
        {
            foreach (var r in TeachOrder())
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
