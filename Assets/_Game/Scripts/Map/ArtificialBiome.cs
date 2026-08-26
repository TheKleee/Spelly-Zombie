using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// A lvl3 spell IS a temporary biome: a sphere of rewritten nature that
    /// dictates the local drift for ~10 seconds, born at the drawing's own
    /// summed size. They STACK - overlapping biomes simply ADD their offsets
    /// into the area, which is the frost-inside-fire counterplay: a small
    /// chill biome inside a big fire one cancels the burn where you stand.
    /// Affinity imposes like every axis (his rule) - things standing here
    /// become the magnets or repellers themselves, never a centre pull.
    /// A spell bursting on terrain opens a small short one: the data hangs
    /// in the air where it exploded until the drift clears it.
    public class ArtificialBiome : MonoBehaviour
    {
        public SpellPayload Offsets;
        public float Radius = 4f;
        public float Seconds = 10f;

        static readonly List<ArtificialBiome> All = new List<ArtificialBiome>();
        float _age;
        Transform _dome;

        /// Opened by whatever cast it - host-side, since seals only resolve
        /// there. It tells the clients, who open their own copy.
        public static ArtificialBiome Open(Vector3 at, SpellPayload offsets, float radius, float power,
            float seconds = 0f)
        {
            var b = OpenLocal(at, offsets.Scaled(power), radius, seconds);
            NetSync.PushBiome(at, b.Offsets, b.Radius, b.Seconds);
            return b;
        }

        /// The biome itself, with the offsets ALREADY scaled. Clients build
        /// theirs through here, so applying the host's copy cannot echo back
        /// out onto the wire. seconds 0 = the lvl3 default; an explicit
        /// lifetime marks a terrain-burst linger, which opens quietly.
        public static ArtificialBiome OpenLocal(Vector3 at, SpellPayload offsets, float radius,
            float seconds = 0f)
        {
            var go = new GameObject("ArtificialBiome");
            go.transform.position = at;
            var b = go.AddComponent<ArtificialBiome>();
            b.Offsets = offsets;
            b.Radius = Mathf.Max(1f, radius);
            b.Seconds = seconds > 0f ? seconds : DrawingConfig.SpellBiomeSeconds;

            var c = offsets.Tint(); c.a = 0.35f;
            b._dome = GrammarFX.FieldBall(at, b.Radius, c, MoteShade.Transparent);
            b._dome.SetParent(go.transform, true);
            GrammarFX.GroundRing(go.transform, c).localScale = Vector3.one * b.Radius;
            if (seconds <= 0f) DrawingWorld.Instance?.LogEvent("the nature here changes");
            return b;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Seconds) { Destroy(gameObject); return; }
            // affinity drifts onto the things inside like every axis - THEY
            // pull and push, the place never drags toward its own centre
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
