using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Teaches runes in the authored Teaches order: each absorb grants the
    /// next rune the wizard is missing; a spent source refuses. Infinite
    /// sources survive absorbs, others are consumed. Wizards only; F absorbs.
    public class AbsorbSource : MonoBehaviour
    {
        [Tooltip("The runes this object teaches, IN THIS ORDER. Each absorb grants the next one the player is missing.")]
        public RuneType[] Teaches;

        [Tooltip("ON: a training element — absorb it forever, it never vanishes. OFF: one absorb consumes the object (the wizard trail).")]
        public bool Infinite = true;

        [Tooltip("How close the wizard must stand for the absorb to work, meters.")]
        public float Range = 3.5f;

        /// The next rune this would teach the owner - None when it is spent.
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
            if (!Infinite) Destroy(gameObject);
        }
    }
}
