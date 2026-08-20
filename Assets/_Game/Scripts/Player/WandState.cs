using UnityEngine;

namespace SpellyZombie
{
    /// The wand is the ink vessel: drawing drains it and the cauldron is the
    /// only well. Dry past the grace = wandless (no drawing, zombies stop
    /// fearing you); a refill past ReformAt brings it back.
    public class WandState : MonoBehaviour
    {
        /// Drain per second while drawing - tunable from sz_tuning.json.
        public static float DrainPerSec => DrawingConfig.WandDrainPerSec;
        public const float DissolveGrace = 6f;  // seconds empty before wandless
        public const float ReformAt = 4f;       // ink level that reforms the wand

        PlayerInk _ink;
        SimpleFPSController _pilot;
        float _dissolveLeft = -1f;

        /// You hold a working wand (has ink, or still inside the dissolve grace).
        public bool HasWand { get; private set; } = true;

        /// The LOCAL player can draw only with ink in hand - SurfaceDrawer gates
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
        // drawing - the static must never strand the lobby/studio pen
        void OnDisable() => LocalCanDraw = true;

        void Update()
        {
            if (_ink == null) return;
            float dt = Time.deltaTime;
            bool local = _pilot != null && _pilot.IsLocalViewer; // cached - no per-frame camera scan

            // drawing wears the wand - local pen only
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

            // inside the pot's close radius the pen is refused entirely; only
            // a pouring pot blocks (green, closed or dry pours nothing)
            bool hosed = false;
            if (local && CauldronEconomy.Active != null
                && !Sides.IsAcolytePlayer(_pilot)
                && !CauldronEconomy.IsCorrupt
                && CauldronEconomy.PrepRemaining <= 0f
                && CauldronEconomy.Fill01 > 0f)
            {
                hosed = Vector3.Distance(transform.position,
                    CauldronEconomy.Active.transform.position) <= DrawingConfig.PotCloseRadius;
                if (hosed && HasWand)
                    UIPrompt.Show("!", "the pour is too strong. step back to draw",
                        new Color(1f, 0.85f, 0.4f));
            }

            if (local) LocalCanDraw = HasWand && _ink.Ink > 0.5f && !hosed;
        }

        // no UI bar - the wand itself is the gauge (WandInk)
    }
}
