using UnityEngine;

namespace SpellyZombie
{
    /// THE INK'S FLARE (Marko Aug 11): "a flare or a long line in the color of
    /// ink stretching to the sky... and disappearing" where the ink LEFT, and
    /// "the line from the sky falling onto the cauldron" where it LANDS —
    /// "showing where it is across the map." One asset, two directions: the
    /// FlowMotes direction-is-meaning language at map scale.
    ///
    /// Animated by WIDTH, not by tinting the material — MatterFX materials are
    /// shared and cached, and fading a shared material would fade every mote
    /// wearing it.
    public class SkyBeam : MonoBehaviour
    {
        const float Height = 70f;
        const float Life = 2.6f;
        const float Width = 0.45f;

        LineRenderer _lr;
        float _age;
        bool _rising;

        /// The ink LEFT here — a beam that stands, then thins away.
        public static void Up(Vector3 at, Color c) => Spawn(at, c, rising: true);

        /// The ink LANDS here — the same line falling onto the pot.
        public static void Down(Vector3 at, Color c) => Spawn(at, c, rising: false);

        static void Spawn(Vector3 at, Color c, bool rising)
        {
            var go = new GameObject(rising ? "InkBeam_Up" : "InkBeam_Down");
            go.transform.position = at;
            var b = go.AddComponent<SkyBeam>();
            b._rising = rising;
            b._lr = go.AddComponent<LineRenderer>();
            b._lr.useWorldSpace = true;
            b._lr.positionCount = 2;
            b._lr.SetPosition(0, at);
            b._lr.SetPosition(1, at + Vector3.up * Height);
            b._lr.widthMultiplier = rising ? Width : 0f;
            b._lr.sharedMaterial = MatterFX.Get(
                new Color(c.r, c.g, c.b, 0.85f), MoteShade.Additive);
            Destroy(go, Life + 0.1f);
        }

        void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / Life);
            // rising: stands bright then thins to nothing (the ink is GONE).
            // falling: swells in from a thread, then collapses at the landing.
            _lr.widthMultiplier = _rising
                ? Width * (1f - t * t)
                : Width * Mathf.Sin(t * Mathf.PI);
        }
    }
}
