using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// A lvl3 spell IS a temporary biome: a sphere of rewritten nature that
    /// dictates the local drift for ~10 seconds, born at the drawing's own
    /// summed size. They STACK - overlapping biomes simply ADD their offsets
    /// into the area, which is the frost-inside-fire counterplay: a small
    /// chill biome inside a big fire one cancels the burn where you stand.
    /// Affinity is the odd axis out: it is a FORCE - a trap pulls everything
    /// back to its centre, a closed biome expels everything - stronger the
    /// further out (trap) or the deeper in (closed).
    public class ArtificialBiome : MonoBehaviour
    {
        public SpellPayload Offsets;
        public float Radius = 4f;
        public float Seconds = 10f;

        static readonly List<ArtificialBiome> All = new List<ArtificialBiome>();
        float _age;
        Transform _dome;

        public static ArtificialBiome Open(Vector3 at, SpellPayload offsets, float radius, float power)
        {
            var go = new GameObject("ArtificialBiome");
            go.transform.position = at;
            var b = go.AddComponent<ArtificialBiome>();
            b.Offsets = offsets.Scaled(power);
            b.Radius = Mathf.Max(1f, radius);
            b.Seconds = DrawingConfig.SpellBiomeSeconds;

            var c = offsets.Tint(); c.a = 0.35f;
            b._dome = GrammarFX.FieldBall(at, b.Radius, c, MoteShade.Transparent);
            b._dome.SetParent(go.transform, true);
            GrammarFX.GroundRing(go.transform, c).localScale = Vector3.one * b.Radius;
            DrawingWorld.Instance?.LogEvent("the nature here changes");
            return b;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Seconds) { Destroy(gameObject); return; }

            // affinity is force, not drift: pull in or push out, by weight
            float aff = Offsets.Affinity;
            if (Mathf.Abs(aff) < 0.05f) return;
            foreach (var p in SimpleFPSController.All)
            {
                if (p == null) continue;
                Vector3 to = transform.position - p.transform.position;
                float d = to.magnitude;
                if (d > Radius || d < 0.2f) continue;
                // trap bites harder at the rim, closed bites harder at the core
                float edge = aff > 0f ? d / Radius : 1f - d / Radius;
                var body = BodyState.Of(p);
                float heft = body != null ? Mathf.Max(0.2f, body.TotalWeight) : 1f;
                p.AddSpellForce(to.normalized * Mathf.Sign(aff)
                    * DrawingConfig.AffinityForce * Mathf.Abs(aff) * edge / heft, Time.deltaTime);
            }
        }

        /// The summed offsets of every artificial biome covering a point -
        /// ADDITIVE by ruling, on top of whatever the map biome says.
        public static SpellPayload SampleAt(Vector3 at)
        {
            var sum = new SpellPayload();
            for (int i = 0; i < All.Count; i++)
            {
                var b = All[i];
                if (b == null) continue;
                if ((b.transform.position - at).sqrMagnitude > b.Radius * b.Radius) continue;
                sum += b.Offsets;
            }
            return sum;
        }
    }
}
