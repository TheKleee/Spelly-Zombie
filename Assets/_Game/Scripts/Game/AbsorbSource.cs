using UnityEngine;

namespace SpellyZombie
{
    /// A THING THAT TEACHES. It holds the runes it can teach plus its visible
    /// mote (blob, light, trail). The wizard aims at it, the badge offers F,
    /// and the mote flies to the winner's grimoire - a trail everyone sees.
    ///
    /// Safe to hand to map authors: attaching one attaches a label. The
    /// Absorbable Creator window crafts the full glowing prefab form.
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
        /// much of each was added - the strongest ABSOLUTE value gives itself
        /// away first (-35 heat outranks +20 light and teaches Chill). Axes
        /// that share a number come out in random order (his rule).
        System.Collections.Generic.IEnumerable<RuneType> TeachOrder()
        {
            if (Teaches != null && Teaches.Length > 0)
            {
                foreach (var r in Teaches) yield return r;
                yield break;
            }
            int[] vals = { Temperature, Light, Density, Balance, State, Affinity };
            // a sub-integer jitter randomizes ties without ever crossing a
            // real 1-point gap
            var rng = new System.Random();
            float[] tie = new float[6];
            for (int i = 0; i < 6; i++) tie[i] = (float)rng.NextDouble() * 0.5f;
            var order = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4, 5 };
            order.Sort((a, b) =>
                (Mathf.Abs(vals[b]) + tie[b]).CompareTo(Mathf.Abs(vals[a]) + tie[a]));
            foreach (int ax in order)
                if (vals[ax] != 0) yield return RuneFor(ax, vals[ax]);
        }

        [Tooltip("ON: a training element — absorb it forever, it never vanishes. OFF: one absorb consumes the object (the wizard trail).")]
        public bool Infinite = true;

        [Tooltip("How close the wizard must stand for the absorb to work, meters.")]
        public float Range = 3.5f;

        [Header("MOTE")]
        [Tooltip("The visible glowing part (blob + light + trail) that flies to the winner's book. Empty = an invisible legacy source, data only.")]
        public Transform Mote;

        [Tooltip("Seconds after a mote is taken before this source grows a new one for the next wizard.")]
        public float RegrowSeconds = 8f;

        /// Whether there is a mote to take right now. Legacy sources with no
        /// mote are always ready.
        public bool Ready => Mote == null || Mote.gameObject.activeSelf;

        static readonly System.Collections.Generic.List<AbsorbSource> _all =
            new System.Collections.Generic.List<AbsorbSource>();
        void OnEnable() { _all.Add(this); }
        void OnDisable() { _all.Remove(this); }

        /// The source nearest a broadcast point - authored positions match on
        /// every machine, so a small radius is identity enough.
        public static AbsorbSource Near(Vector3 at)
        {
            AbsorbSource best = null;
            float bd = 2.25f;
            foreach (var s in _all)
            {
                if (s == null) continue;
                float d = (s.transform.position - at).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        /// The next rune this would teach the owner - None when it is spent.
        /// Already-known runes are skipped, so a source is invisible to anyone
        /// who has learned everything on its list.
        public RuneType NextFor(int owner)
        {
            foreach (var r in TeachOrder())
                if (r != RuneType.None && !Grimoire.HasRune(owner, r)) return r;
            return RuneType.None;
        }

        /// The give, ON EVERY MACHINE: the mote flies to the winner's book
        /// trailing for all to see, and the source regrows. Only the winner's
        /// own machine writes the rune - knowledge is per wizard.
        public void Grant(int owner)
        {
            Juice.Chime(transform.position);

            if (Mote != null && Mote.gameObject.activeSelf)
            {
                // the rune rides the mote and lands WITH it - the unlock
                // happens when it hits the grimoire, not before (his finesse)
                var fly = Instantiate(Mote.gameObject, Mote.position, Mote.rotation);
                var f = fly.AddComponent<AbsorbFlight>();
                f.Owner = owner;
                f.Rune = owner == Grimoire.LocalPlayerId ? NextFor(owner) : RuneType.None;
                Mote.gameObject.SetActive(false);
                if (Infinite) StartCoroutine(Regrow());
            }
            else
            {
                // legacy moteless form: nothing flies, so the rune lands now
                if (owner == Grimoire.LocalPlayerId)
                {
                    var rune = NextFor(owner);
                    if (rune != RuneType.None)
                    {
                        Grimoire.UnlockRune(owner, rune);
                        DrawingWorld.Instance?.LogEvent(
                            $"absorbed: it teaches {RuneLibrary.Icon(rune)}");
                    }
                }
                if (FxLibrary.I != null && FxLibrary.I.AbsorbBurst != null)
                    FxLibrary.Spawn(FxLibrary.I.AbsorbBurst,
                        transform.position + Vector3.up * 0.4f);
            }
            if (!Infinite) Destroy(gameObject, 0.1f);
        }

        /// A new mote grows IN (never shrinks out - his law) after the wait.
        System.Collections.IEnumerator Regrow()
        {
            yield return new WaitForSeconds(RegrowSeconds);
            if (Mote == null) yield break;
            Mote.gameObject.SetActive(true);
            Vector3 full = Mote.localScale;
            float t = 0f;
            while (t < 0.3f && Mote != null)
            {
                t += Time.deltaTime;
                Mote.localScale = full * Mathf.SmoothStep(0f, 1f, t / 0.3f);
                yield return null;
            }
            if (Mote != null) Mote.localScale = full;
        }
    }
}
