using UnityEngine;

namespace SpellyZombie
{
    /// THE WAND TETHER (Phase 1 — Marko's spine): your wand IS your ink vessel.
    /// Drawing wears it down, and it does NOT passively refill — the CAULDRON
    /// is the only well. Run it dry and the wand DISSOLVES after a short grace,
    /// leaving you WANDLESS: you can't draw (and, Phase 4, zombies stop fearing
    /// you). Refill at a cauldron to reform it. This is what pulls everyone
    /// back to the pot and forces the camp-to-camp journey.
    ///
    /// TEST-ISOLATED: it owns its own drain, independent of the run economy, so
    /// it works in a bare sandbox with no RoundDirector. When the real map
    /// lands, this replaces the run-gated ink spend and kills passive regen.
    public class WandState : MonoBehaviour
    {
        public const float DrainPerSec = 6f;    // drawing wears the wand down
        public const float DissolveGrace = 6f;  // empty this long → wandless
        public const float ReformAt = 4f;       // refilled past this → wand returns

        PlayerInk _ink;
        SimpleFPSController _pilot;
        float _dissolveLeft = -1f;

        /// You hold a working wand (has ink, or still inside the dissolve grace).
        /// Phase 4's fear/attack system will read this ("armed = has wand").
        public bool HasWand { get; private set; } = true;

        /// The LOCAL player can draw only with ink in hand — SurfaceDrawer gates
        /// on this. Defaults true so scenes with no WandState (lobby, studio)
        /// keep free practice-drawing.
        public static bool LocalCanDraw = true;

        void Awake()
        {
            _ink = GetComponent<PlayerInk>();
            if (_ink == null) _ink = gameObject.AddComponent<PlayerInk>();
            _pilot = GetComponent<SimpleFPSController>();
        }

        // leaving the tether behind (a scene with no WandState) restores free
        // drawing — the static must never strand the lobby/studio pen
        void OnDisable() => LocalCanDraw = true;

        void Update()
        {
            if (_ink == null) return;
            float dt = Time.deltaTime;
            bool local = _pilot != null && _pilot.IsLocalViewer; // cached — no per-frame camera scan

            // drawing wears the wand (use it → lose it) — local pen only
            if (local && HasWand && SurfaceDrawer.IsPenActive)
                _ink.Ink = Mathf.Max(0f, _ink.Ink - DrainPerSec * dt);

            if (HasWand)
            {
                if (_ink.Ink <= 0.5f)              // dry: the grace clock to a cauldron
                {
                    if (_dissolveLeft < 0f) _dissolveLeft = DissolveGrace;
                    _dissolveLeft -= dt;
                    if (_dissolveLeft <= 0f)
                    {
                        HasWand = false;
                        DrawingWorld.Instance?.LogEvent("your wand is dissolving. find a cauldron");
                    }
                }
                else _dissolveLeft = -1f;          // topped up before it went
            }
            else if (_ink.Ink >= ReformAt)         // only a real refill brings it back
            {
                HasWand = true;
                _dissolveLeft = -1f;
                DrawingWorld.Instance?.LogEvent("the ink reforms your wand");
            }

            if (local) LocalCanDraw = HasWand && _ink.Ink > 0.5f;
        }

        // NO UI BAR (Marko: "we don't need any UI - the ink is always visibly
        // decaying, cauldron or wand"). The wand ITSELF is the gauge now:
        // WandInk shrinks it with the ink and disintegrates it when wandless.
    }
}
