using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// AN OBJECT THAT TEACHES RUNES, IN ORDER (Marko Aug 10): "a flame can be
    /// absorbed to learn heat, but next time it's absorbed it'll teach you
    /// spread and luminance respectively. Solid object can teach you solid but
    /// it can also teach you density and darkness... in that order as darkness
    /// is more powerful so it needs to be taught later."
    ///
    /// HE ADDS THIS to his absorbable objects and FILLS THE LIST HIMSELF —
    /// the order is authored, never inferred. Each absorb grants the next
    /// rune the wizard is missing; a source with nothing left to teach
    /// refuses. INFINITE sources (training elements in the lobby) survive
    /// every absorb; a normal one is CONSUMED by it — the wizard's trail.
    ///
    /// Wizards only — acolytes SCAN, they never absorb. F absorbs while the
    /// aim badge is on this object.
    public class AbsorbSource : MonoBehaviour
    {
        [Tooltip("The runes this object teaches, IN THIS ORDER. Each absorb grants the next one the player is missing.")]
        public RuneType[] Teaches;

        [Tooltip("ON: a training element — absorb it forever, it never vanishes. OFF: one absorb consumes the object (the wizard trail).")]
        public bool Infinite = true;

        [Tooltip("How close the wizard must stand for the absorb to work, meters.")]
        public float Range = 3.5f;

        /// The next rune this would teach the owner — None when it is spent.
        public RuneType NextFor(int owner)
        {
            if (Teaches == null) return RuneType.None;
            foreach (var r in Teaches)
                if (r != RuneType.None && !Grimoire.HasRune(owner, r)) return r;
            return RuneType.None;
        }

        void Update()
        {
            if (AimBadge.Aimed != (Component)this) return;
            var kb = Keyboard.current;
            if (kb == null || !kb.fKey.wasPressedThisFrame) return;
            if (UIKit.Typing || GameMenu.IsOpen || PoseStudio.IsOpen) return;

            int me = Grimoire.LocalPlayerId;
            if (Sides.Of(me) == Side.Acolyte) return; // acolytes scan, never absorb

            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p == null) return;
            if ((p.transform.position - transform.position).sqrMagnitude > Range * Range) return;

            var rune = NextFor(me);
            if (rune == RuneType.None)
            {
                DrawingWorld.Instance?.LogEvent("this taught you everything it knows");
                return;
            }
            Grimoire.UnlockRune(me, rune);
            Juice.Chime(transform.position);
            DrawingWorld.Instance?.LogEvent($"absorbed: it teaches {RuneLibrary.Icon(rune)}");
            // consumption is the trail (Marko Aug 8: absorbing destroys the
            // object) — unless this is one of his infinite training elements
            if (!Infinite) Destroy(gameObject);
        }
    }
}
