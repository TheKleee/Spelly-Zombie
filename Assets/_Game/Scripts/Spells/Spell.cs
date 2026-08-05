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
            public bool Conjured;     // State runes conjure ONCE per activation
            public float GlyphSize;   // UNCLAMPED drawn half-extent — matter sizing
                                      // uses this, not Radius (whose 0.9 floor is
                                      // for effect areas and made boulder spam)
            public Object Tracked;    // SUSTAIN LAW: what this rune's particle currently
                                      // IS (walked through combinations) — no re-emit
                                      // until it is fully gone (Marko's Jul 20 ruling)
        }

        readonly List<Zone> _zones = new List<Zone>();
        SurfaceMaterialType _surface;
        int _ownerId; // whose cast this is — their powerup buffs apply
        int _edges = 10; // the seal's side count — THE SHAPE the solid takes
        float _remaining;
        bool _ended;
        bool _bodyThrow;      // remote body seal: no live glyph, but it's still a body cast (netcode §2)
        Transform _netCaster; // the remote caster's avatar — throws spare it briefly

        // pressure (density confined by rigid walls builds until it bursts)
        float _gasIntensity;
        float _pressure;
        Vector3 _pressureCenter;
        bool _exploded;

        // dark deepens & spreads when combined with low density / low stickiness
        float _darkSpread;

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
            spell._edges = seal.Edges; // triangle = 3 … circle = 10 (his shape dial)

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

            WorldEvents.Report(WorldEventKind.Spell, seal.PlaneOrigin, 1.5f); // eyes turn, zombies notice

            return spell;
        }

        /// A client's BODY seal fired: its ink never replicated, so the HOST builds
        /// the spell from the shipped payload — no seal, no glyphs, no re-reading
        /// (netcode §2). Surface is Flesh by definition (body ink).
        public static Spell CreateRemote(int ownerId, Vector3 origin, Vector3 normal, int edges,
            float duration, int[] runes, float[] strengths, Vector3[] centers, Vector3[] pushDirs,
            float[] sizes, Transform caster)
        {
            var host = new GameObject("Spell_net");
            host.transform.position = origin;
            var spell = host.AddComponent<Spell>();
            spell._surface = SurfaceMaterialType.Flesh;
            spell._ownerId = ownerId;
            spell._remaining = duration;
            spell._edges = edges;
            spell._bodyThrow = true;
            spell._netCaster = caster;

            int n = Mathf.Min(runes.Length, 12); // rate/size cap, like ZombieHit
            for (int i = 0; i < n; i++)
            {
                var rune = (RuneType)runes[i];
                float strength = Mathf.Clamp01(strengths[i]);
                if (rune == RuneType.None || strength <= 0.02f) continue;
                if (rune == RuneType.DensityDown || rune == RuneType.StickyDown)
                    spell._darkSpread += strength * 0.6f;
                float glyphHalf = Mathf.Clamp(sizes[i], 0.01f, 3f);
                var z = new Zone
                {
                    Rune = rune,
                    Center = centers[i] + normal * 0.06f,
                    Normal = normal,
                    Radius = Mathf.Clamp(glyphHalf * DrawingConfig.ZoneRadiusScale, 0.9f, 3.5f),
                    GlyphSize = glyphHalf,
                    Intensity = strength,
                    Phase = Random.value * 6.28f,
                    PushDir = pushDirs[i].sqrMagnitude > 0.01f ? pushDirs[i].normalized : normal
                };
                spell.BuildVisual(z);
                spell._zones.Add(z);
            }
            spell._darkSpread = Mathf.Clamp(spell._darkSpread, 0f, 1.5f);
            if (spell._zones.Count == 0) { Destroy(host); return null; }

            spell._pressureCenter = origin + normal * 0.1f;
            foreach (var z in spell._zones)
            {
                if (z.Rune == RuneType.DensityUp) spell._gasIntensity += z.Intensity;
                else if (z.Rune == RuneType.HeatUp) spell._gasIntensity += z.Intensity * DrawingConfig.HeatPressureFactor;
            }
            WorldEvents.Report(WorldEventKind.Spell, origin, 1.5f);
            return spell;
        }

        /// The arrow/Y pointing rule, readable from outside — clients ship the
        /// direction with their body-seal payload (netcode §2).
        public static Vector3 ArrowDirFor(RuneGlyph g, Vector3 normal, RuneType rune)
            => ArrowDirection(g, normal, rune);

        // ONE METRONOME PER SEAL (Marko: "things inside the same seal should
        // fire at the same time") — zones don't own clocks anymore; the SPELL
        // pulses and every zone that's allowed to emit does so on the pulse.
        // Sustain-law zones whose product died mid-cycle wait for the next
        // shared beat instead of rebursting on a private timer.
        float _pulseTimer;   // starts 0 → the first pulse is the first tick
        bool _pulseFire;

        void Update()
        {
            if (_ended) return;
            float dt = Time.deltaTime;
            _remaining -= dt;

            _pulseFire = false;
            _pulseTimer -= dt;
            if (_pulseTimer <= 0f)
            {
                _pulseFire = true;
                // the fastest Rapid stack in the seal drives everyone's beat —
                // simultaneity outranks per-rune tempo (Marko's law)
                float mul = 1f;
                foreach (var z in _zones)
                    mul = Mathf.Min(mul, Mathf.Pow(0.75f, Powerups.For(_ownerId, z.Rune).Rapid));
                _pulseTimer = DrawingConfig.ZoneEmitPeriod * mul;
            }

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
            var hits = GrammarFX.ScanBuffer; // shared scratch (consumed before return)
            int n = Physics.OverlapSphereNonAlloc(_pressureCenter, DrawingConfig.ExplodeRadius, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var pilot = hits[i] ? hits[i].GetComponent<SimpleFPSController>() : null;
                if (pilot != null)
                {
                    Vector3 away2 = (pilot.transform.position - _pressureCenter).normalized;
                    pilot.TakeHit((dir * 2f + away2).normalized * kick, 8f);
                    pilot.KnockDown(1.2f); // blast off your feet
                    continue;
                }
                var blasted = hits[i] ? hits[i].GetComponentInParent<Creature>() : null;
                if (blasted != null) blasted.KnockDown(2f); // horde bowling
                var rb = hits[i] ? hits[i].attachedRigidbody : null;
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
                // shared fire mote (the old per-mote `new Material` leaked one
                // Material per sphere — MatterFX.Get caches)
                var go = GrammarFX.FireMote(origin, Random.Range(0.05f, 0.13f), Random.Range(0.8f, 1.6f));
                var body = go.AddComponent<Rigidbody>();
                body.mass = 0.08f;
                body.linearVelocity = (dir + Random.insideUnitSphere * 0.55f).normalized
                    * speed * Random.Range(0.6f, 1.2f);
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

            // THE EMITTER RULE (Marko's particle system): every rune PRODUCES —
            // ONE particle per pulse (law 10: the drawing is the recipe, "if
            // you want 3, draw 3 runes") for as long as the seal lives; a
            // circle seal is 36 seconds of periodic mayhem.
            // emissions happen ONLY on the seal's shared pulse — every rune of
            // one drawing fires the same frame, always (Marko's law)
            if (_pulseFire)
            {
                if (ProducesMatter(z.Rune))
                {
                    // State conjures ONCE per activation (re-firing the seal —
                    // pose re-close — conjures the next batch)
                    if (!z.Conjured)
                    {
                        z.Conjured = true;
                        EmitParticles(z);
                    }
                }
                else if (TrackerAlive(ref z.Tracked))
                {
                    // SUSTAIN LAW (Marko): this rune's magic is still OUT THERE
                    // — as itself, or inside whatever it combined into. One
                    // light rune makes ONE light, forever; it re-emits only
                    // when the final product of its chain has disappeared —
                    // and then only on the next shared pulse.
                }
                else
                {
                    EmitParticles(z);
                }
            }

            // what remains of the FIELD: the player flight channel, a weak
            // direction push, and darkness blinding — everything else moved
            // into the particles
            int n = Physics.OverlapSphereNonAlloc(z.Center, z.Radius, GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) Apply(z, GrammarFX.ScanBuffer[i], dt);
        }

        void EmitParticles(Zone z)
        {
            if (ProducesMatter(z.Rune)) { SpawnMatter(z); return; }

            // rune → particle now reads the ONE registry (RuneDef.Emits) instead
            // of a duplicate switch (moddability audit Jul 25: the switch was a
            // second source of truth that a new/modded rune had to be added to).
            // State runes never reach here — ProducesMatter caught them above.
            var def = RuneGrammar.Def(z.Rune);
            if (def == null) return;
            ParticleKind kind = def.Emits;

            // the caster's powerups shape the burst (per rune family);
            // the Spell perk (local player only, like Powerups) tops it up
            var buff = Powerups.For(_ownerId, z.Rune);
            bool localCaster = _ownerId == Grimoire.LocalPlayerId;
            // ONE particle per rune (Marko's ruling: "if you want 3, draw 3
            // runes") — the DRAWING is the recipe; powerups still add extras
            int count = Mathf.Min(12, 1 + buff.More + (localCaster ? Perks.ExtraParticles : 0));
            float speedMul = 1f + 0.5f * buff.Fast;
            float potent = (1f + 0.35f * buff.Potent) * (localCaster ? Perks.PotencyMul : 1f);
            float bigMul = 1f + 0.3f * buff.Big;

            Vector3 dir = kind == ParticleKind.Push ? z.PushDir : z.Normal;

            // A BODY (or weapon) SEAL THROWS ITS CAST (Marko: particles born
            // on the skin "always activate and you can't make combinations
            // like fire bolts" — his fix: "the body can push the particles
            // as if they were thrown"): persistent-surface casts launch
            // along the seal's outward normal, sparing the caster briefly.
            // Siblings of one drawing leave together, seek each other in
            // flight, and combine mid-air — the bolt.
            bool bodyCast = _bodyThrow; // remote body seals throw too (netcode §2)
            Transform caster = _netCaster;
            var m0 = z.Glyph != null && z.Glyph.Members.Count > 0 ? z.Glyph.Members[0] : null;
            if (m0 != null && m0.Persistent && m0.First != null)
            {
                bodyCast = true;
                caster = m0.First.transform.root;
            }

            for (int i = 0; i < count; i++)
            {
                var p = SpellParticle.Emit(kind,
                    z.Center + z.Normal * 0.12f + Random.insideUnitSphere * z.Radius * 0.18f,
                    dir, z.Intensity);
                p.SrcSize = z.Radius * bigMul; // drawn size rides the chain (demon sizing)
                p.Lineage = RuneGrammar.Bit(z.Rune); // GRAMMAR v4: ancestry starts here —
                                                     // all 12 in one chain = THE DEMON
                p.SealId = GetHashCode();            // siblings of one DRAWING pair up first
                if (i == 0) z.Tracked = p;           // sustain law: the rune WATCHES this one
                                                     // (powerup extras are untracked bonuses)
                p.Vel *= speedMul;
                if (bodyCast)
                    p.ThrowFrom(caster, z.Normal * DrawingConfig.BodyCastThrowSpeed + p.Vel);
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

        /// SUSTAIN LAW bookkeeping: walk the became-chain to whatever the
        /// rune's particle currently is. Alive = a living particle, a running
        /// field, an existing matter blob, or a demon still digesting it.
        /// The walked end is written back so chains stay one hop long.
        static bool TrackerAlive(ref Object tracked)
        {
            int hops = 0;
            while (tracked is SpellParticle p && p.Dead)
            {
                tracked = p.BecameObj; // follow what it turned into
                if (++hops > 16) break;
            }
            if (tracked == null) return false; // Unity-null covers destroyed things
            // claimed = harvested: it left the magic world, so the rune
            // re-emits — an active seal is a factory (Marko's sustain law)
            if (tracked is SpellParticle alive) return !alive.Dead && !alive.Claimed;
            return true; // fields, matter, demons — Component null-check above rules
        }

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
                        // FEET-SEAL FLIGHT ONLY (body ink). A ground arrow seal
                        // no longer pushes whoever stands in it — the PARTICLES
                        // are the movers (Marko: "make particles do the effects
                        // themselves")
                        if (_surface == SurfaceMaterialType.Flesh)
                            pilot.AddSpellForce(z.PushDir * DrawingConfig.DirectionForce * z.Intensity, dt);
                        break;
                    case RuneType.DensityDown:
                        pilot.AddSpellForce(Vector3.up * DrawingConfig.ForceAccel * z.Intensity, dt);
                        break;
                    case RuneType.DensityUp:
                        pilot.AddSpellForce(Vector3.down * DrawingConfig.ForceAccel * z.Intensity, dt);
                        break;
                    // draw LIGHT around a blinded friend and the darkness
                    // washes off them faster (Marko's cure channel) — dark
                    // seals do the opposite, symmetrically
                    case RuneType.LuminanceUp:
                        BodyState.Of(pilot)?.PushLum(1.1f * z.Intensity * dt);
                        break;
                    case RuneType.LuminanceDown:
                        BodyState.Of(pilot)?.PushLum(-0.9f * z.Intensity * dt);
                        break;
                }
                return;
            }

            switch (z.Rune)
            {
                // (Direction case REMOVED — Marko Jul 22: "there is still a
                // passive pull/push effect from the old build. Make particles
                // do the effects themselves." Even the breeze is gone.)

                case RuneType.LuminanceDown: // darkness BLINDS — they can't find you inside it
                    var dark = c.GetComponentInParent<Creature>();
                    if (dark != null) dark.ApplyBlind(DrawingConfig.BlindSeconds);
                    break;

                case RuneType.LuminanceUp: // HOLY LIGHT sears the undead in its
                    // glow (Marko's veto of the v17 nerf: "light on its own is
                    // completely useless" — modest solo damage restored; the
                    // lightning/laser ladder stays the big-damage path)
                    var seared = c.GetComponentInParent<Creature>();
                    if (seared != null)
                        seared.GetComponent<Damageable>()?.TakeDamage(
                            DrawingConfig.HolyLightPerSec * z.Intensity * dt, "holy light");
                    break;
            }
        }

        /// Only State runes reach here, ONCE per activation. Solid conjures the
        /// surface's material (Stone when unmarked); Liquid conjures its liquid
        /// form (Stone→Lava, Flesh→Blood, Coal→Oil, default Water).
        /// DENSITY IS THE COUNT DIAL (the user's rule): plain State = a FEW
        /// pieces; + Density-up = ONE big one; + Density-down = MANY small ones.
        /// GRAMMAR v4 P2 — the FORM RECIPE RESOLVER. The seal is the recipe
        /// card: every sibling rune enclosed with a State rune changes what it
        /// conjures (SPELL_PARTICLES.md cross matrix). Identity rides along as
        /// LINEAGE, so matter chains toward the Demon like particles do.
        void SpawnMatter(Zone z)
        {
            bool solid = z.Rune == RuneType.StateSolid;
            var mat = _surface;
            if (mat == SurfaceMaterialType.Unknown)
                mat = solid ? SurfaceMaterialType.Stone : SurfaceMaterialType.Water;

            // ONE recipe per drawing (verified bug: each State zone resolved the
            // whole seal independently — three Solid runes opened THREE
            // avalanches). The first zone of each State rune does the work.
            // (plain loop — Find's lambda allocated a closure per activation)
            foreach (var o in _zones)
                if (o.Rune == z.Rune) { if (o != z) return; break; }

            // ---- read the recipe: every rune enclosed in this seal ----
            bool denser = false, thinner = false, heatUp = false, heatDown = false,
                lightUp = false, lightDown = false, glue = false, slick = false;
            int sameForm = 0;
            ulong lineage = 0;
            foreach (var other in _zones)
            {
                lineage |= RuneGrammar.Bit(other.Rune);
                switch (other.Rune)
                {
                    case RuneType.DensityUp: denser = true; break;
                    case RuneType.DensityDown: thinner = true; break;
                    case RuneType.HeatUp: heatUp = true; break;
                    case RuneType.HeatDown: heatDown = true; break;
                    case RuneType.LuminanceUp: lightUp = true; break;
                    case RuneType.LuminanceDown: lightDown = true; break;
                    case RuneType.StickyUp: glue = true; break;
                    case RuneType.StickyDown: slick = true; break;
                }
                if (other.Rune == z.Rune) sameForm++;
            }
            RuneGrammar.TryDemon(lineage, z.Center, z.Radius); // a full drawing IS a chain

            var buff = Powerups.For(_ownerId, z.Rune);
            float size = Mathf.Clamp(z.GlyphSize * 0.5f, 0.08f, 0.45f)
                * Mathf.Lerp(0.75f, 1.15f, z.Intensity)
                * (denser ? 1.7f : thinner ? 0.5f : 0.9f) * (1f + 0.25f * buff.Big);

            // ---- FORM LEVELING: draw the State rune twice = lvl2 (grows /
            // spreads), three times = the AREA ultimate ----
            if (sameForm >= 3)
            {
                if (solid) SolidAvalancheField.Open(z.Center, mat, z.Intensity, lineage);
                else LiquidAreaField.Open(z.Center, mat, z.Intensity, lineage);
                return;
            }
            int formLevel = Mathf.Min(2, sameForm);
            if (!solid && (thinner || lightDown)) formLevel = 2; // Liquid+Spread spreads — and DARK liquid spreads like darkness

            // ---- HEAT × FORM — the showpieces ----
            if (solid && heatUp) { FormConjures.Meteorite(z.Center, z.Normal, mat, size, 1 + buff.More, lineage); return; }
            if (solid && heatDown) { FormConjures.IceSpikes(z.Center, z.Normal, mat, size, lineage); return; }
            if (!solid && heatDown) { FormConjures.Glacier(z.Center, mat, z.Intensity, lineage); return; }
            if (!solid && heatUp) { FormConjures.HotLiquid(z.Center, z.Normal, mat, size, z.Intensity, lineage); return; }

            // ---- Liquid + Dense = PRESSURE JET along the seal's normal ----
            if (!solid && denser) { FormConjures.PressureJet(z.Center, z.Normal, mat, size, z.Intensity, lineage); return; }

            // (NO cross-form seal recipe — Marko: "why are you making
            // exceptions?" Solid and Liquid each emit their OWN blob and the
            // blobs COMBINE on contact like any other particles → MUD.)

            // ONE conjure per cast (Marko Jul 22: "spells only create 1
            // particle as per usual, not 3 like our old solid/liquid") —
            // density buffs change the SIZE, never the count
            {
                // SOLID materializes overhead and DROPS — the anvil rune.
                // Liquids stay surface-born (they slump into puddles in place).
                float lift = solid ? DrawingConfig.SolidDropHeight : size * 0.5f;
                // THE SEAL'S SIDES PICK THE SOLID'S SHAPE (his ruling: "3 sides
                // => 1 shape, 4 => another… 10 would be a wheel for wood but
                // default for rock") — passed through, resolved in Matter.Spawn
                // against his prefabs. Liquid/gas ignore it: they share one blob.
                var conjured = Matter.Spawn(mat, solid ? MatterPhase.Solid : MatterPhase.Liquid, size,
                    z.Center + z.Normal * lift, solid ? _edges : 0);
                // SPELL-BORN, FOREVER (Marko's ruling): conjured matter can
                // never teach a rune, even after the touch law makes it an
                // object — otherwise conjure → touch → absorb prints runes.
                // Covers an authored shape/skin prefab that carries Analyzable.
                foreach (var an in conjured.GetComponentsInChildren<Analyzable>(true))
                    an.SpellBorn = true;
                conjured.Lineage = lineage;
                conjured.FormLevel = formLevel;
                if (buff.Bond > 0) conjured.AddStickiness(0.2f * buff.Bond); // gooier conjures

                // ---- STICKY / SLICK / LIGHT / DARK forms (identity preserved) ----
                if (glue) conjured.AddStickiness(0.65f);   // sticky solid: carry things stuck to it
                if (slick) conjured.AddStickiness(-1f);    // slick solid: the frictionless plow
                if (lightUp) // solid/liquid LIGHT — carriable lantern
                {
                    var l = new GameObject("FormGlow").AddComponent<Light>();
                    l.transform.SetParent(conjured.transform, false);
                    l.type = LightType.Point; l.range = 6f; l.intensity = 3.2f;
                    l.color = new Color(1f, 0.95f, 0.75f);
                }
                if (lightDown) conjured.DarkAura = true;   // solid/liquid DARKNESS — blinds on touch
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

            // NO STATIC GROUND ARROW (Marko, repeatedly — "these arrows that
            // appear on the floor should disappear"; it was also a FIXED size
            // no matter how large you drew the rune). The FLYING arrow/Y
            // particle is the whole visual now — it moves, so it reads.
            if (z.Rune == RuneType.DirectionAway || z.Rune == RuneType.DirectionToward)
                root.transform.rotation = Quaternion.LookRotation(z.PushDir);

            // Luminance-down "drinks light" — mild on its own; it deepens & spreads
            // when combined with low density / low stickiness (see _darkSpread).
            if (z.Rune == RuneType.LuminanceDown)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(root.transform, false);
                sphere.transform.localScale = Vector3.one * z.Radius * (1.1f + _darkSpread) * z.Intensity;
                var sc = sphere.GetComponent<Collider>();
                if (sc) Destroy(sc);
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
