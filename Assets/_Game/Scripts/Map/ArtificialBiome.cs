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
            // ★ THE SPHERE IS FOR BIOMES ONLY (his ruling): a linger is not a
            // biome and must not look like one - the ground ring says the
            // range, the element puffs say the effect, and that is all
            if (seconds <= 0f)
            {
                b._dome = GrammarFX.FieldBall(at, b.Radius, c, MoteShade.Transparent);
                b._dome.SetParent(go.transform, true);
                DrawingWorld.Instance?.LogEvent("the nature here changes");
            }
            GrammarFX.GroundRing(go.transform, c).localScale = Vector3.one * b.Radius;

            // ★ EVERY AXIS SHOWS ITSELF, BY INTENSITY (his design): each of
            // the six sliders that sits meaningfully off neutral spawns ITS
            // authored fx, scaled by how far toward the edge it is - near
            // zero nothing appears, at the edge it dominates. A mixed spell
            // shows its whole recipe stacked in one place.
            for (int ax = 0; ax < 6; ax++)
            {
                float u = Mathf.Abs(offsets.Unit(ax));
                if (u < 0.15f) continue;
                var fx = CollectionManager.AreaFxFor(ax, offsets[ax] > 0f);
                if (fx == null) continue;
                var v = Object.Instantiate(fx, at, Quaternion.identity, go.transform);
                v.transform.localScale *= Mathf.Max(0.4f, b.Radius * 0.5f * u);
                b._customFx = true;
            }
            return b;
        }
        bool _customFx;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Seconds) { Destroy(gameObject); return; }
            // affinity drifts onto the things inside like every axis - THEY
            // pull and push, the place never drags toward its own centre

            // ★ EVERY LINGER BREATHES ITS ELEMENT (his rule: no boring
            // circles): the dominant axis decides what dances inside the
            // dome - embers rise in heat, frost hangs in chill, glue drips
            // low, sparks crown light, wisps swallow dark
            if (_customFx) return; // his authored look carries this linger
            if (Time.time < _fxAt) return;
            _fxAt = Time.time + 0.45f;
            int ax = Offsets.Dominant;
            if (ax < 0 || ax > 5) return;
            bool up = Offsets[ax] > 0f;
            for (int i = 0; i < 2; i++)
            {
                Vector2 flat = Random.insideUnitCircle * Radius * 0.7f;
                Vector3 p = transform.position + new Vector3(flat.x, 0f, flat.y);
                switch (ax)
                {
                    case 0:
                        if (up) GrammarFX.PuffBurst(p + Vector3.up * Random.Range(0.2f, 1.4f),
                            new Color(1f, 0.5f, 0.1f), 2);           // rising embers
                        else GrammarFX.PuffBurst(p + Vector3.up * Random.Range(0.6f, 1.8f),
                            new Color(0.9f, 0.97f, 1f), 2);          // hanging frost
                        break;
                    case 1:
                        GrammarFX.PuffBurst(p + Vector3.up * Random.Range(1f, 2.2f),
                            up ? new Color(1f, 0.97f, 0.75f)
                               : new Color(0.1f, 0.07f, 0.16f), 2);  // sparks / wisps
                        break;
                    case 2:
                        GrammarFX.PuffBurst(p + Vector3.up * (up ? 0.1f : Random.Range(0.8f, 1.8f)),
                            up ? new Color(0.4f, 0.38f, 0.35f)
                               : new Color(1f, 0.98f, 0.85f), 2);    // low dust / floaters
                        break;
                    case 3:
                        GrammarFX.PuffBurst(p + Vector3.up * 0.08f,
                            up ? new Color(0.85f, 0.65f, 0.2f)
                               : new Color(0.65f, 0.88f, 1f), 2);    // glue / sheen
                        break;
                    case 4:
                        GrammarFX.PuffBurst(p + Vector3.up * 0.3f,
                            up ? new Color(0.55f, 0.52f, 0.5f)
                               : new Color(0.45f, 0.7f, 0.95f), 2);  // grit / droplets
                        break;
                    default:
                        GrammarFX.PuffBurst(p + Vector3.up * 0.6f,
                            new Color(0.9f, 0.9f, 0.95f), 1);        // the vector dots
                        break;
                }
            }
        }
        float _fxAt;

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
