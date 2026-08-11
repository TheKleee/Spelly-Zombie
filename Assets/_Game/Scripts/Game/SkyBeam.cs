using UnityEngine;

namespace SpellyZombie
{
    /// THE INK COMET (Marko Aug 11 v2: "that effect must be simple and
    /// beautiful... just use a long trail on flying object") — the ink LEAVES
    /// as a bright drop climbing to the sky with a long trail, and ARRIVES as
    /// the same drop falling out of the sky onto the pot. Direction is the
    /// meaning, the trail is the beauty, one flying object each way.
    ///
    /// Dark ink gets BRIGHTENED for the additive material: v1 was a black
    /// additive beam, which adds nothing to any pixel and was therefore
    /// literally invisible — his "where was the falling from the sky effect?"
    public class SkyBeam : MonoBehaviour
    {
        const float Height = 70f;
        const float UpTime = 2.2f;
        /// How long the falling drop takes — PUBLIC because the ink must
        /// appear the MOMENT it lands (Marko), so callers fire the comet
        /// this many seconds before they fill the pot.
        public const float DownSeconds = 1.5f;

        Vector3 _from, _to;
        float _age, _time;
        Transform _target;        // a pot mid-toss: the drop CHASES it down
        System.Action _onArrive;  // the ink spawns HERE — arrival, not a timer
        bool _arrived;

        /// The ink LEFT here — the drop climbs away and is gone.
        public static void Up(Vector3 at, Color c) => Spawn(at, null, c, true, null);

        /// The ink LANDS here — the drop falls onto a fixed spot.
        public static void Down(Vector3 at, Color c) => Spawn(at, null, c, false, null);

        /// The drop falls onto a LIVING target — it homes while it falls
        /// (Marko: "you never know what cauldron might be doing... people
        /// might even be tossing it. You can't time it perfectly") and fires
        /// onArrive at true contact. The caller spawns the ink THERE.
        public static void Down(Transform target, Color c, System.Action onArrive)
            => Spawn(target.position, target, c, false, onArrive);

        static void Spawn(Vector3 at, Transform target, Color c, bool rising, System.Action onArrive)
        {
            // THE TRAIL IS THE INK (Marko: "trail needs to be the ink's
            // current color... the ink is saved along with the team it
            // belongs to") — no substitute colors. Dark ink cannot glow
            // additively (black adds nothing to any pixel — why v1 was
            // invisible), so it rides an opaque streak instead: black ink
            // writes a BLACK line across the sky; corrupt green glows.
            bool dark = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.3f;
            var mat = dark
                ? MatterFX.Get(new Color(c.r, c.g, c.b, 0.95f), MoteShade.Transparent)
                : MatterFX.Get(new Color(c.r, c.g, c.b, 0.9f), MoteShade.Additive);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = rising ? "InkComet_Up" : "InkComet_Down";
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.55f;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 1.1f;
            trail.startWidth = 0.5f;
            trail.endWidth = 0.02f;
            trail.sharedMaterial = mat;
            trail.minVertexDistance = 0.3f;

            var b = go.AddComponent<SkyBeam>();
            b._target = target;
            b._onArrive = onArrive;
            b._from = rising ? at + Vector3.up * 0.4f : at + Vector3.up * Height;
            b._to = rising ? at + Vector3.up * Height : at + Vector3.up * 0.4f;
            b._time = rising ? UpTime : DownSeconds;
            go.transform.position = b._from;
        }

        void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _time);
            t *= t; // a drop gathering speed, both directions
            if (_target != null)
            {
                // A FLARE, ALWAYS VERTICAL (Marko: "spawning directly above
                // the cauldron and then it flies down"): both ends ride the
                // pot's own column, so a tossed pot slides the whole line
                // with it and the drop still falls straight down onto it.
                Vector3 basePos = _target.position;
                _from = basePos + Vector3.up * Height;
                _to = basePos + Vector3.up * 0.4f;
            }
            transform.position = Vector3.Lerp(_from, _to, t);
            if (!_arrived && _age >= _time)
            {
                _arrived = true;
                _onArrive?.Invoke(); // contact — NOW the ink may exist
            }
            if (_age >= _time + 1.3f) Destroy(gameObject); // the trail finishes fading
        }
    }
}
