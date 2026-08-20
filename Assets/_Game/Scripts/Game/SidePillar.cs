using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// Side pillar: walk up and press E to switch wizard <-> acolyte. Lobby
    /// only. The pillar is placed by hand (Spelly Zombie -> Build LOBBY
    /// PILLARS); nothing is auto-found.
    public class SidePillar : MonoBehaviour
    {
        [Tooltip("How close the player must stand for the offer, meters.")]
        public float Range = 2.8f;

        [Tooltip("Optional: the art to tint by side. Empty = a beam of light builds itself.")]
        public Renderer Emblem;

        Renderer _beam, _glow;

        void Start()
        {
            if (Emblem == null) _beam = PillarBeam.Build(transform, out _glow);
            // a dedicated aim body: trigger only, so bodies pass through and
            // the pen ignores it (pen raycasts skip triggers). Tall as the beam.
            if (transform.Find("AimTarget") == null)
            {
                var aim = new GameObject("AimTarget");
                aim.transform.SetParent(transform, false);
                var col = aim.AddComponent<CapsuleCollider>();
                col.isTrigger = true;
                col.center = Vector3.up * 1.6f;
                col.height = 3.6f;
                col.radius = 0.5f;
            }
        }

        void Update()
        {
            if (ActiveScene.Name != "Lobby") return;
            var p = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            if (p == null) return;

            // the beam wears the local side's ink color
            Color c = Sides.LocalIsAcolyte ? DrawingConfig.CorruptInkColor : Stroke.InkColor;
            PillarBeam.Tint(Emblem != null ? Emblem : _beam, c);
            PillarBeam.Tint(_glow, c);

            // aim-gated: the crosshair must be on the pillar; the badge range-gates
            if (!ReferenceEquals(AimBadge.Aimed, this)) return;
            if (UIKit.Typing) return;

            var kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;

            Sides.Toggle(Sides.LocalPlayerId);
            p.Health = Sides.MaxHealthFor(Sides.LocalPlayerId);
            DrawingWorld.Instance?.LogEvent(Sides.LocalIsAcolyte
                ? "you are an acolyte now" : "you are a wizard now");
            Juice.Chime(p.transform.position);
        }
    }
}
