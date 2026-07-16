using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The active effect of a seal. When a seal fires it spawns one Spell, which
    /// turns each recognized rune into an EMITTER ZONE sitting where the rune
    /// was drawn. Zones don't apply invisible field effects anymore — they EMIT
    /// PARTICLES (SPELL_PARTICLES.md, Marko's matter-level law): every rune
    /// visibly produces something, and composition happens where particles
    /// collide. State zones conjure Matter; Direction zones keep a weak push
    /// field (the flight engine) plus PUSH particles that do the real shoving.
    /// The spell lives for the seal's duration and is cancelled the instant the
    /// seal breaks (DrawingWorld ends it via the seal).
    public class Spell : MonoBehaviour
    {
        class Zone
        {
            public RuneType Rune;
            public Vector3 Center;
            public Vector3 Normal;
            public Vector3 PushDir;   // Direction runes: the way the arrow points (world space)
            public float Radius;
            public float Intensity;
            public Light Light;
            public float Phase;
            public RuneGlyph Glyph;   // live ink anchor — the zone RIDES its glyph
            public GameObject Visual; // zone root (light/arrow), follows the ink
            public float EmitTimer;   // countdown to the next particle burst (0 = fire now)
            public float GlyphSize;   // UNCLAMPED drawn half-extent — matter sizing
                                      // uses this, not Radius (whose 0.9 floor is
                                      // for effect areas and made boulder spam)
        }

        readonly List<Zone> _zones = new List<Zone>();
        SurfaceMaterialType _surface;
        int _ownerId; // whose cast this is — their powerup buffs apply
        float _remaining;
        bool _ended;

        // pressure (density confined by rigid walls builds until it bursts)
        float _gasIntensity;
        float _pressure;
        Vector3 _pressureCenter;
        bool _exploded;

        // dark deepens & spreads when combined with low density / low stickiness
        float _darkSpread;

        static readonly Collider[] _hits = new Collider[48];

        static readonly Vector3[] ConfineDirs =
        {
            Vector3.up, Vector3.down,
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
            new Vector3( 1, 0,  1).normalized, new Vector3(-1, 0,  1).normalized,
            new Vector3( 1, 0, -1).normalized, new Vector3(-1, 0, -1).normalized
        };

        public static Spell Create(Seal seal, SurfaceMaterialType surface)
        {
            var host = new GameObject($"Spell_{seal.Id}");
            host.transform.position = seal.PlaneOrigin;
            var spell = host.AddComponent<Spell>();
            spell._surface = surface;
            spell._ownerId = seal.OwnerId;
            spell._remaining = seal.Duration;

            // low density + low stickiness make a Dark rune spread and deepen
            foreach (var g in seal.Runes)
                if (g.Rune == RuneType.DensityDown || g.Rune == RuneType.StickyDown)
                    spell._darkSpread += g.Strength * 0.6f;
            spell._darkSpread = Mathf.Clamp(spell._darkSpread, 0f, 1.5f);

            // NO predetermined outcomes (user verdict: "I want mayhem") — no
            // combo names, no banners, no resonance boosts. Zones just run and
            // physics composes whatever it composes.

            foreach (var g in seal.Runes)
            {
                if (g.Rune == RuneType.None || g.Strength <= 0.02f) continue;
                float glyphHalf = g.WorldBounds().size.magnitude * 0.5f;
                var z = new Zone
                {
                    Rune = g.Rune,
                    Center = g.Centroid() + seal.PlaneNormal * 0.06f,
                    Normal = seal.PlaneNormal,
                    Radius = Mathf.Clamp(glyphHalf * DrawingConfig.ZoneRadiusScale, 0.9f, 3.5f),
                    GlyphSize = glyphHalf,
                    Intensity = g.Strength,
                    Phase = Random.value * 6.28f,
                    Glyph = g
                };
                z.PushDir = (g.Rune == RuneType.DirectionAway || g.Rune == RuneType.DirectionToward)
                    ? ArrowDirection(g, seal.PlaneNormal, g.Rune)
                    : seal.PlaneNormal;
                spell.BuildVisual(z);
                spell._zones.Add(z);
            }

            if (spell._zones.Count == 0) { Destroy(host); return null; }

            // pressure potential: Density-up is the gas; Heat feeds it (a hot,
            // dense, confined pocket is what bursts — exactly the sketch)
            spell._pressureCenter = seal.PlaneOrigin + seal.PlaneNormal * 0.1f;
            foreach (var z in spell._zones)
            {
                if (z.Rune == RuneType.DensityUp) spell._gasIntensity += z.Intensity;
                else if (z.Rune == RuneType.HeatUp) spell._gasIntensity += z.Intensity * DrawingConfig.HeatPressureFactor;
            }

            // every zone's EmitTimer starts at 0, so the first particle burst
            // (and the first State conjuring) happens on the very first tick

            WorldEvents.Report(WorldEventKind.Spell, seal.PlaneOrigin, 1.5f); // eyes turn, zombies notice

            return spell;
        }

        void Update()
        {
            if (_ended) return;
            float dt = Time.deltaTime;
            _remaining -= dt;
            for (int i = 0; i < _zones.Count; i++) TickZone(_zones[i], dt);
            if (_gasIntensity > 0.01f && !_exploded) TickPressure(dt);
            if (_remaining <= 0f) End();
        }

        public void End()
        {
            if (_ended) return;
            _ended = true;
            if (this != null) Destroy(gameObject);
        }

        /// Density (fed by Heat) trapped by rigid walls builds pressure; when it
        /// exceeds the container's strength it bursts out the least-blocked
        /// direction — walls on the sides mean it erupts upward.
        void TickPressure(float dt)
        {
            int blocked = 0;
            float bestClear = -1f;
            Vector3 ventDir = Vector3.up;
            foreach (var d in ConfineDirs)
            {
                float clear = DrawingConfig.ContainRange;
                if (Physics.Raycast(_pressureCenter, d, out var hit, DrawingConfig.ContainRange,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    blocked++;
                    clear = hit.distance;
                }
                // prefer venting upward when clearances tie
                if (clear > bestClear || (Mathf.Approximately(clear, bestClear) && d == Vector3.up))
                {
                    bestClear = clear;
                    ventDir = d;
                }
            }

            float containment = blocked / (float)ConfineDirs.Length;
            if (containment < 0.45f)
            {
                _pressure = Mathf.Max(0f, _pressure - dt); // open space vents harmlessly
                return;
            }

            _pressure += _gasIntensity * DrawingConfig.PressureBuildRate * containment * dt;
            if (_pressure >= DrawingConfig.ExplodeThreshold)
                Explode(ventDir);
        }

        void Explode(Vector3 dir)
        {
            _exploded = true;
            float power = _pressure;
            DrawingWorld.Instance?.LogEvent($"PRESSURE BURST → {power:0.0} out {DirName(dir)}");
            WorldEvents.Report(WorldEventKind.Explosion, _pressureCenter, 3f + power); // panic close, awe far
            Juice.Boom(_pressureCenter, power);
            Juice.Shake(Mathf.Min(1f, 0.5f + power * 0.3f));
            Juice.HitStop(0.12f, 0.35f);

            // fling nearby bodies out the vent + radially (VelocityChange so a
            // 70kg zombie flies as spectacularly as a 1kg rock)
            float kick = Mathf.Min(power, 1.5f) * 7f;
            int n = Physics.OverlapSphereNonAlloc(_pressureCenter, DrawingConfig.ExplodeRadius, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var pilot = _hits[i] ? _hits[i].GetComponent<SimpleFPSController>() : null;
                if (pilot != null)
                {
                    Vector3 away2 = (pilot.transform.position - _pressureCenter).normalized;
                    pilot.TakeHit((dir * 2f + away2).normalized * kick, 8f);
                    pilot.KnockDown(1.2f); // blast off your feet
                    continue;
                }
                var blasted = _hits[i] ? _hits[i].GetComponentInParent<Creature>() : null;
                if (blasted != null) blasted.KnockDown(2f); // horde bowling
                var rb = _hits[i] ? _hits[i].attachedRigidbody : null;
                if (rb == null) continue;
                Vector3 away = (rb.worldCenterOfMass - _pressureCenter);
                Vector3 push = (dir * 2f + away.normalized).normalized;
                rb.AddForce(push * kick, ForceMode.VelocityChange);
            }

            // the eruption itself — a fat cone of fire particles up the vent
            SpawnBurst(_pressureCenter, dir, DrawingConfig.ExplodeParticles, 6f + power * 3f);
            _pressure = 0f;
        }

        void SpawnBurst(Vector3 origin, Vector3 dir, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Burst";
                go.transform.position = origin;
                go.transform.localScale = Vector3.one * Random.Range(0.05f, 0.13f);
                var col = go.GetComponent<Collider>();
                if (col) col.isTrigger = true;
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null)
                    go.GetComponent<Renderer>().sharedMaterial = new Material(shader)
                        { color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.15f, 0.05f), Random.value) };
                var body = go.AddComponent<Rigidbody>();
                body.mass = 0.08f;
                Vector3 v = (dir + Random.insideUnitSphere * 0.55f).normalized * speed * Random.Range(0.6f, 1.2f);
                body.linearVelocity = v;
                Destroy(go, Random.Range(0.8f, 1.6f));
            }
        }

        static string DirName(Vector3 d) =>
            d == Vector3.up ? "up" : d == Vector3.down ? "down" : "sideways";

        /// The way a Direction rune points, from its geometry. The shaft is the
        /// farthest-apart pair of points; the HEAD is found by the CENTROID
        /// test: barbs (arrow) or fork (Y) put extra ink at the head end, which
        /// pulls the shape's center of ink toward it — stable however sloppily
        /// or in whatever order it was drawn. A featureless straight line falls
        /// back to pen-travel (points the way you drew it). Away fires
        /// base→head (+ off the surface); Toward pulls back (+ into it).
        static readonly List<Vector3> _dirPts = new List<Vector3>(64);
        static Vector3 ArrowDirection(RuneGlyph g, Vector3 normal, RuneType rune)
        {
            _dirPts.Clear();
            Vector3 lastDrawn = Vector3.zero;
            foreach (var m in g.Members)
            {
                if (m == null) continue;
                foreach (var n in m.Nodes)
                {
                    if (n == null) continue;
                    _dirPts.Add(n.transform.position);
                    lastDrawn = n.transform.position;
                }
            }
            if (_dirPts.Count < 2) return normal;

            // the shaft = the farthest-apart pair of points
            int ai = 0, bi = 1;
            float best = -1f;
            for (int i = 0; i < _dirPts.Count; i++)
                for (int j = i + 1; j < _dirPts.Count; j++)
                {
                    float d = (_dirPts[i] - _dirPts[j]).sqrMagnitude;
                    if (d > best) { best = d; ai = i; bi = j; }
                }
            Vector3 pa = _dirPts[ai], pb = _dirPts[bi];

            // centroid test: the barbed/forked end holds more ink, so the ink
            // centroid sits closer to it
            Vector3 centroid = Vector3.zero;
            foreach (var p in _dirPts) centroid += p;
            centroid /= _dirPts.Count;
            float da = Vector3.Distance(centroid, pa);
            float db = Vector3.Distance(centroid, pb);

            Vector3 head, tail;
            float shaft = Mathf.Sqrt(best);
            if (Mathf.Abs(da - db) < shaft * 0.06f)
            {
                // symmetric (a plain line) — the pen's travel decides
                bool aIsLater = Vector3.Distance(pa, lastDrawn) < Vector3.Distance(pb, lastDrawn);
                head = aIsLater ? pa : pb;
                tail = aIsLater ? pb : pa;
            }
            else
            {
                head = da < db ? pa : pb;
                tail = da < db ? pb : pa;
            }

            Vector3 inPlane = Vector3.ProjectOnPlane(head - tail, normal);
            if (inPlane.sqrMagnitude < 1e-6f) return normal;
            inPlane.Normalize();

            return rune == RuneType.DirectionToward
                ? (-inPlane - normal * 0.3f).normalized  // pull back and into the surface
                : (inPlane + normal * 0.3f).normalized;   // fire along the spear and off the surface
        }

        void TickZone(Zone z, float dt)
        {
            // the zone RIDES its ink: a seal on a crate keeps working while the
            // crate flies, and a seal drawn on your own feet flies WITH you
            if (z.Glyph != null)
            {
                Vector3 live = z.Glyph.Centroid();
                if (live != Vector3.zero)
                {
                    z.Center = live + z.Normal * 0.06f;
                    if (z.Rune == RuneType.DirectionAway || z.Rune == RuneType.DirectionToward)
                        z.PushDir = ArrowDirection(z.Glyph, z.Normal, z.Rune);
                }
            }
            if (z.Visual != null)
            {
                z.Visual.transform.position = z.Center;
                if (z.PushDir.sqrMagnitude > 0.01f)
                    z.Visual.transform.rotation = Quaternion.LookRotation(z.PushDir);
            }

            if (z.Light != null && z.Rune == RuneType.HeatUp) // faint ember flicker
                z.Light.intensity = (0.2f + Mathf.PerlinNoise(Time.time * 9f, z.Phase) * 0.3f) * z.Intensity;

            // THE EMITTER RULE (Marko's particle system): every rune PRODUCES.
            // Bursts of 3 on a cadence for as long as the seal lives — a circle
            // seal is 36 seconds of periodic mayhem.
            z.EmitTimer -= dt;
            if (z.EmitTimer <= 0f)
            {
                // State conjures ONCE per activation (re-firing the seal — pose
                // re-close — conjures the next batch); everything else pulses,
                // faster if the caster stacked RAPID on this rune family
                z.EmitTimer = ProducesMatter(z.Rune)
                    ? float.PositiveInfinity
                    : DrawingConfig.ZoneEmitPeriod
                        * Mathf.Pow(0.75f, Powerups.For(_ownerId, z.Rune).Rapid);
                EmitParticles(z);
            }

            // what remains of the FIELD: the player flight channel, a weak
            // direction push, and darkness blinding — everything else moved
            // into the particles
            int n = Physics.OverlapSphereNonAlloc(z.Center, z.Radius, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) Apply(z, _hits[i], dt);
        }

        void EmitParticles(Zone z)
        {
            if (ProducesMatter(z.Rune)) { SpawnMatter(z); return; }

            ParticleKind kind;
            switch (z.Rune)
            {
                case RuneType.HeatUp: kind = ParticleKind.Spark; break;
                case RuneType.HeatDown: kind = ParticleKind.Frost; break;
                case RuneType.LuminanceUp: kind = ParticleKind.Light; break;
                case RuneType.LuminanceDown: kind = ParticleKind.Dark; break;
                case RuneType.StickyUp: kind = ParticleKind.Glue; break;
                case RuneType.StickyDown: kind = ParticleKind.Repel; break;
                case RuneType.DensityUp: kind = ParticleKind.Dense; break;
                case RuneType.DensityDown: kind = ParticleKind.Spread; break;
                case RuneType.DirectionAway:
                case RuneType.DirectionToward: kind = ParticleKind.Push; break;
                default: return;
            }

            // the caster's powerups shape the burst (per rune family);
            // the Spell perk (local player only, like Powerups) tops it up
            var buff = Powerups.For(_ownerId, z.Rune);
            bool localCaster = _ownerId == Grimoire.LocalPlayerId;
            int count = Mathf.Min(12, 3 + buff.More * 2 + (localCaster ? Perks.ExtraParticles : 0));
            float speedMul = 1f + 0.5f * buff.Fast;
            float potent = (1f + 0.35f * buff.Potent) * (localCaster ? Perks.PotencyMul : 1f);
            float bigMul = 1f + 0.3f * buff.Big;

            Vector3 dir = kind == ParticleKind.Push ? z.PushDir : z.Normal;
            for (int i = 0; i < count; i++)
            {
                var p = SpellParticle.Emit(kind,
                    z.Center + z.Normal * 0.12f + Random.insideUnitSphere * z.Radius * 0.18f,
                    dir, z.Intensity);
                p.SrcSize = z.Radius * bigMul; // drawn size rides the chain (demon sizing)
                p.Vel *= speedMul;
                p.Temp *= potent;
                p.Lum *= potent;
                p.Density *= potent;
                p.Stick = p.Stick * potent + 0.35f * buff.Bond;
                p.Echo = buff.Echo;
                if (bigMul > 1f) p.transform.localScale *= bigMul;
            }
        }

        // ONLY State runes spawn matter (once, at activation) — everything else modifies it.
        static bool ProducesMatter(RuneType r) => r == RuneType.StateSolid || r == RuneType.StateLiquid;

        /// The slim field remainder. All the real work moved into the emitted
        /// particles — the zone keeps only what MUST be a field: the player
        /// flight channel (feet seals fly), a much-weakened direction push on
        /// objects (the PUSH particles are the movers now, per Marko), and
        /// darkness blinding whoever stands inside it.
        void Apply(Zone z, Collider c, float dt)
        {
            if (c == null) return;

            // the player is a CharacterController — physics forces bounce off it,
            // so force runes feed its spell-velocity channel instead. THIS is
            // what makes "draw an arrow seal on your feet and fly" actually fly.
            var pilot = c.GetComponent<SimpleFPSController>();
            if (pilot != null)
            {
                switch (z.Rune)
                {
                    case RuneType.DirectionAway:
                    case RuneType.DirectionToward:
                        pilot.AddSpellForce(z.PushDir * DrawingConfig.DirectionForce * z.Intensity, dt);
                        break;
                    case RuneType.DensityDown:
                        pilot.AddSpellForce(Vector3.up * DrawingConfig.ForceAccel * z.Intensity, dt);
                        break;
                    case RuneType.DensityUp:
                        pilot.AddSpellForce(Vector3.down * DrawingConfig.ForceAccel * z.Intensity, dt);
                        break;
                }
                return;
            }

            switch (z.Rune)
            {
                case RuneType.DirectionAway:
                case RuneType.DirectionToward:
                    var rb = c.attachedRigidbody;
                    if (rb) rb.AddForce(z.PushDir * DrawingConfig.DirectionForce * 0.25f * z.Intensity,
                        ForceMode.Acceleration);
                    break;

                case RuneType.LuminanceDown: // darkness BLINDS — they can't find you inside it
                    var dark = c.GetComponentInParent<Creature>();
                    if (dark != null) dark.ApplyBlind(DrawingConfig.BlindSeconds);
                    break;
            }
        }

        /// Only State runes reach here, ONCE per activation. Solid conjures the
        /// surface's material (Stone when unmarked); Liquid conjures its liquid
        /// form (Stone→Lava, Flesh→Blood, Coal→Oil, default Water).
        /// DENSITY IS THE COUNT DIAL (the user's rule): plain State = a FEW
        /// pieces; + Density-up = ONE big one; + Density-down = MANY small ones.
        void SpawnMatter(Zone z)
        {
            bool solid = z.Rune == RuneType.StateSolid;
            var mat = _surface;
            if (mat == SurfaceMaterialType.Unknown)
                mat = solid ? SurfaceMaterialType.Stone : SurfaceMaterialType.Water;

            bool denser = false, thinner = false;
            foreach (var other in _zones)
            {
                if (other.Rune == RuneType.DensityUp) denser = true;
                else if (other.Rune == RuneType.DensityDown) thinner = true;
            }
            var buff = Powerups.For(_ownerId, z.Rune);
            int count = (denser ? 1 : thinner ? 6 : 3) + buff.More;
            float sizeMul = (denser ? 1.7f : thinner ? 0.5f : 0.9f) * (1f + 0.25f * buff.Big);

            // sized by the DRAWN glyph (user: conjured pieces were way too big —
            // the old zone-radius base had a 0.9m floor → boulders from doodles)
            float size = Mathf.Clamp(z.GlyphSize * 0.5f, 0.08f, 0.45f)
                * Mathf.Lerp(0.75f, 1.15f, z.Intensity) * sizeMul;
            for (int i = 0; i < count; i++)
            {
                Vector3 scatter = count == 1 ? Vector3.zero
                    : Random.insideUnitSphere * z.Radius * 0.4f;
                var conjured = Matter.Spawn(mat, solid ? MatterPhase.Solid : MatterPhase.Liquid, size,
                    z.Center + z.Normal * size * 0.5f + scatter); // ON the surface, not inside
                if (buff.Bond > 0) conjured.AddStickiness(0.2f * buff.Bond); // gooier conjures
            }
        }

        void BuildVisual(Zone z)
        {
            var root = new GameObject($"Zone_{z.Rune}");
            root.transform.SetParent(transform, false);
            root.transform.position = z.Center;
            z.Visual = root;

            // NO zone rings (user: "circles break the immersion") — the emitted
            // particles ARE the visibility now; a running spell shows itself by
            // producing, not by drawing UI onto the world.

            // Direction zones show their push as a glowing arrow — on a CHILD
            // object (a GameObject holds only ONE LineRenderer; keeping the
            // child layout from the ring era)
            if (z.Rune == RuneType.DirectionAway || z.Rune == RuneType.DirectionToward)
            {
                root.transform.rotation = Quaternion.LookRotation(z.PushDir);
                var arrowGo = new GameObject("Arrow");
                arrowGo.transform.SetParent(root.transform, false);
                var lr = arrowGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.widthMultiplier = 0.05f;
                lr.positionCount = 5;
                lr.sharedMaterial = MatterFX.Get(z.Rune == RuneType.DirectionAway
                    ? new Color(1f, 0.95f, 0.4f, 0.9f) : new Color(1f, 0.4f, 0.9f, 0.9f), MoteShade.Additive);
                lr.SetPosition(0, new Vector3(0f, 0f, -0.25f));
                lr.SetPosition(1, new Vector3(0f, 0f, 0.55f));   // shaft
                lr.SetPosition(2, new Vector3(-0.14f, 0f, 0.34f)); // barb L
                lr.SetPosition(3, new Vector3(0f, 0f, 0.55f));
                lr.SetPosition(4, new Vector3(0.14f, 0f, 0.34f));  // barb R
            }

            // Luminance-down "drinks light" — mild on its own; it deepens & spreads
            // when combined with low density / low stickiness (see _darkSpread).
            if (z.Rune == RuneType.LuminanceDown)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(root.transform, false);
                sphere.transform.localScale = Vector3.one * z.Radius * (1.1f + _darkSpread) * z.Intensity;
                var sc = sphere.GetComponent<Collider>();
                if (sc) Destroy(sc);
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh != null)
                    sphere.GetComponent<Renderer>().sharedMaterial =
                        MatterFX.Get(new Color(0.02f, 0.02f, 0.05f, Mathf.Clamp01(0.35f + _darkSpread * 0.4f)), MoteShade.Transparent);
                return;
            }

            // State runes make matter, not light — no zone light for them
            if (z.Rune == RuneType.StateSolid || z.Rune == RuneType.StateLiquid) return;

            // ONLY the Light rune produces light (user rule — zone glows washed
            // out every effect). Heat keeps a barely-there ember flicker so a
            // fire zone still reads at night; everything else is dark.
            if (z.Rune == RuneType.LuminanceUp)
            {
                var light = root.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.98f, 0.9f);
                light.range = z.Radius * 7f;
                light.intensity = 9f * z.Intensity;
                z.Light = light;
            }
            else if (z.Rune == RuneType.HeatUp)
            {
                var ember = root.AddComponent<Light>();
                ember.type = LightType.Point;
                ember.color = new Color(1f, 0.5f, 0.15f);
                ember.range = z.Radius * 1.5f;
                ember.intensity = 0.35f * z.Intensity; // faint — an ember, not a floodlight
                z.Light = ember;
            }
        }
    }
}
