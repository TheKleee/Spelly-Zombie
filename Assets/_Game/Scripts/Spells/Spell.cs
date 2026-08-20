using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// The active effect of a seal: each recognized rune becomes an emitter
    /// zone at its drawn spot, producing particles (SPELL_PARTICLES.md).
    /// State zones conjure Matter; Direction zones keep a weak push field
    /// plus PUSH particles. Lives for the seal's duration; cancelled when
    /// the seal breaks (DrawingWorld ends it via the seal).
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
            public RuneGlyph Glyph;   // live ink anchor - the zone RIDES its glyph
            public GameObject Visual; // zone root (light/arrow), follows the ink
            public bool Conjured;     // State runes conjure ONCE per activation
            public float GlyphSize;   // UNCLAMPED drawn half-extent - matter sizing
                                      // uses this, not Radius (0.9-floored for effect areas)
            public Object Tracked;    // SUSTAIN LAW: what this rune's particle currently
                                      // IS (walked through combinations) - no re-emit
                                      // until it is fully gone
        }

        readonly List<Zone> _zones = new List<Zone>();
        SurfaceMaterialType _surface;
        int _ownerId; // whose cast this is - their powerup buffs apply
        int _edges = 10; // the seal's side count - THE SHAPE the solid takes
        float _remaining;
        bool _ended;
        bool _bodyThrow;      // remote body seal: no live glyph, but it's still a body cast (netcode §2)
        Transform _netCaster; // the remote caster's avatar - throws spare it briefly

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
            // Acolyte ink reads the same glyphs differently: Solid raises a
            // melee zombie, Liquid a ranged one, every other rune does
            // nothing. Same recognizer, same templates. An acolyte seal
            // closed on a zombie detonates it; wizard seals are unaffected.
            if (Sides.IsAcolyte(seal.OwnerId))
            {
                var doomed = ZombieUnder(seal);
                if (doomed != null)
                {
                    doomed.Detonate(SealSizeMul(seal), SealPower(seal));
                    return null;
                }
                return AcolyteSummon(seal);
            }

            var host = new GameObject($"Spell_{seal.Id}");
            host.transform.position = seal.PlaneOrigin;
            var spell = host.AddComponent<Spell>();
            spell._surface = surface;
            spell._ownerId = seal.OwnerId;
            spell._remaining = seal.Duration;
            spell._edges = seal.Edges; // triangle = 3 … circle = 10

            // low density + low stickiness make a Dark rune spread and deepen
            foreach (var g in seal.Runes)
                if (g.Rune == RuneType.DensityDown || g.Rune == RuneType.StickyDown)
                    spell._darkSpread += g.Strength * 0.6f;
            spell._darkSpread = Mathf.Clamp(spell._darkSpread, 0f, 1.5f);

            // no predetermined combo outcomes - zones just run and physics
            // composes whatever it composes

            foreach (var g in seal.Runes)
            {
                if (g.Rune == RuneType.None || g.Strength <= 0.02f) continue;
                float glyphHalf = g.WorldBounds().size.magnitude * 0.5f;
                // SIZE = the rune's own drawn size; EFFECT RADIUS = the
                // rune's size relative to its seal; power = the seal's shape.
                float reach = glyphHalf / SealRadius(seal);
                var z = new Zone
                {
                    Rune = g.Rune,
                    Center = g.Centroid() + seal.PlaneNormal * 0.06f,
                    Normal = seal.PlaneNormal,
                    // the floor is the NEUTRAL POINT - SpellParticle.SizeMul
                    // returns exactly 1 there, so a smallest rune is unchanged.
                    // Shared constant so the floor and the reference cannot drift.
                    Radius = Mathf.Clamp(reach * DrawingConfig.RuneReachScale,
                        DrawingConfig.RuneSizeMin, 3.5f),
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

            // pressure potential: Density-up is the gas; Heat feeds it
            spell._pressureCenter = seal.PlaneOrigin + seal.PlaneNormal * 0.1f;
            foreach (var z in spell._zones)
            {
                if (z.Rune == RuneType.DensityUp) spell._gasIntensity += z.Intensity;
                else if (z.Rune == RuneType.HeatUp) spell._gasIntensity += z.Intensity * DrawingConfig.HeatPressureFactor;
            }

            WorldEvents.Report(WorldEventKind.Spell, seal.PlaneOrigin, 1.5f); // eyes turn, zombies notice

            return spell;
        }

        /// The acolyte summon returns null on purpose: no zones, no Spell
        /// object. One Solid glyph = one melee zombie, one Liquid = one
        /// ranged; three Solids in a seal raise three.
        static readonly System.Collections.Generic.List<ZombieBrain> _orderBuf =
            new System.Collections.Generic.List<ZombieBrain>();

        /// One summon glyph: which kind it raises, how big it stands, how far
        /// its gas reaches. Body size is the glyph's own drawn size; only
        /// strength stays a property of the seal's shape.
        struct SummonOrder { public bool Ranged; public float GasRadius; public float SizeMul; }

        /// The seal's equivalent radius, commensurate with a glyph's
        /// half-diagonal (sqrt(Area) alone is an edge length).
        static float SealRadius(Seal seal) =>
            Mathf.Sqrt(Mathf.Max(0.0004f, seal.Area) / Mathf.PI);

        /// Size from the rune's own drawn diameter; unclamped above, floored
        /// below.
        static float RuneSizeMul(float glyphDiameter)
        {
            float range = Mathf.Max(0.001f,
                DrawingConfig.SummonRuneMax - DrawingConfig.SummonRuneMin);
            return Mathf.Max(DrawingConfig.SummonSizeFloor,
                Mathf.LerpUnclamped(DrawingConfig.SummonSizeMin, DrawingConfig.SummonSizeMax,
                    (glyphDiameter - DrawingConfig.SummonRuneMin) / range));
        }

        /// The detonation's size dial: the seal loop itself is the drawing,
        /// so its size is the size. Summons read their glyph sizes instead.
        static float SealSizeMul(Seal seal)
        {
            float range = Mathf.Max(0.001f,
                DrawingConfig.SummonSealMax - DrawingConfig.SummonSealMin);
            return Mathf.Max(DrawingConfig.SummonSizeFloor,
                Mathf.LerpUnclamped(DrawingConfig.SummonSizeMin, DrawingConfig.SummonSizeMax,
                    (SealRadius(seal) * 2f - DrawingConfig.SummonSealMin) / range));
        }

        /// SealLineBonus per line missing from ten: a circle is exactly 1.0,
        /// a triangle 3.58x.
        static float SealPower(Seal seal) =>
            Mathf.Pow(DrawingConfig.SealLineBonus,
                Mathf.Max(0, DrawingConfig.CircleEdges - seal.Edges));

        static readonly Collider[] _sealHits = new Collider[16];

        /// The zombie this seal was drawn ON, if any. The loop is traced across
        /// a body, so its plane origin sits on that body - a short overlap at
        /// the origin finds it without needing the strokes to report a host.
        static Zombie ZombieUnder(Seal seal)
        {
            int n = Physics.OverlapSphereNonAlloc(seal.PlaneOrigin,
                DrawingConfig.DetonateSealReach, _sealHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            Zombie best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_sealHits[i] == null) continue;
                // through ZombieOwner, so a seal drawn on the dressed skin
                // finds its zombie (the dress is a world-space follower)
                var z = ZombieOwner.From(_sealHits[i]);
                if (z == null || z.IsDemon) continue;   // demons are not fireworks
                // ON the zombie, not merely NEAR it - measured to the collider's
                // surface, so a summon seal drawn on the ground beside one keeps
                // summoning instead of blowing up the zombie standing there
                float d = (_sealHits[i].ClosestPoint(seal.PlaneOrigin) - seal.PlaneOrigin).sqrMagnitude;
                if (d > DrawingConfig.DetonateSurfaceSlack * DrawingConfig.DetonateSurfaceSlack)
                    continue;
                if (d < bestSqr) { bestSqr = d; best = z; }
            }
            return best;
        }

        static readonly System.Collections.Generic.List<SummonOrder> _summonBuf =
            new System.Collections.Generic.List<SummonOrder>();

        static Spell AcolyteSummon(Seal seal)
        {
            // one entry per summon glyph: body size = the glyph's own drawn
            // size; gas reach = the glyph's size relative to its seal;
            // strength = the seal's shape.
            _summonBuf.Clear();
            bool hasArrow = false, scatter = false;
            Vector3 marchDir = Vector3.zero;

            float sealSpan = SealRadius(seal);

            foreach (var g in seal.Runes)
            {
                if (g.Strength <= 0.02f) continue;
                if (g.Rune == RuneType.StateSolid || g.Rune == RuneType.StateLiquid)
                {
                    float glyphSpan = g.WorldBounds().size.magnitude * 0.5f;
                    float ratio = glyphSpan / sealSpan;   // 0..1, how much of the seal the rune fills
                    float gas = Mathf.Lerp(DrawingConfig.SummonGasRadiusMin,
                        DrawingConfig.SummonGasRadiusMax, Mathf.Clamp01(ratio));
                    _summonBuf.Add(new SummonOrder
                    {
                        Ranged = g.Rune == RuneType.StateLiquid,
                        GasRadius = gas,
                        SizeMul = RuneSizeMul(glyphSpan * 2f)
                    });
                }
                else if (g.Rune == RuneType.DirectionAway || g.Rune == RuneType.DirectionToward)
                {
                    // the arrow glyph points the dead; flattened - zombies
                    // walk, they do not fly
                    Vector3 d = ArrowDirection(g, seal.PlaneNormal, g.Rune);
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.0001f)
                    {
                        marchDir = d.normalized;
                        hasArrow = true;
                        // arrow marches to one spot, Y scatters across an arc
                        scatter = g.Rune == RuneType.DirectionToward;
                    }
                }
            }

            // arrow: one spot; Y: `i of n` fans them across an arc
            Vector3 MarchPoint(int i, int n)
            {
                Vector3 dir = marchDir;
                if (scatter && n > 0)
                {
                    float t = n == 1 ? 0f : (i / (float)(n - 1)) * 2f - 1f;  // -1 .. +1
                    dir = Quaternion.AngleAxis(t * DrawingConfig.ZombieScatterArc * 0.5f,
                        Vector3.up) * marchDir;
                }
                return seal.PlaneOrigin + dir * DrawingConfig.ZombieMarchDistance;
            }

            // an arrow with no summon runes re-points the dead this acolyte
            // already has
            if (_summonBuf.Count == 0)
            {
                if (!hasArrow)
                {
                    DrawingWorld.Instance?.LogEvent("nothing answers. draw solid or liquid");
                    return null;
                }

                // gather mine first, so a scatter can fan them across the arc
                _orderBuf.Clear();
                foreach (var z in Zombie.All)
                {
                    if (z == null) continue;
                    var mine = z.GetComponent<SummonedZombie>();
                    if (mine == null || mine.SummonedBy != seal.OwnerId) continue;
                    var b = z.GetComponent<ZombieBrain>();
                    if (b != null) _orderBuf.Add(b);
                }

                int told = _orderBuf.Count;
                for (int i = 0; i < told; i++) _orderBuf[i].Order(MarchPoint(i, told));
                _orderBuf.Clear();

                DrawingWorld.Instance?.LogEvent(told == 0
                    ? "nobody is listening"
                    : told == 1 ? "it turns and goes" : $"{told} of them turn and go");
                return null;
            }

            // the summon deed: raising one earns the arrow, raising two the Y
            AcolyteDeeds.Summoned(seal.OwnerId, _summonBuf.Count);

            int total = _summonBuf.Count;
            float life = DrawingConfig.SummonedZombieLife;

            // strength belongs to the seal; each glyph carries its own body size
            float power = SealPower(seal);
            for (int i = 0; i < total; i++)
            {
                bool isRanged = _summonBuf[i].Ranged;
                float sizeMul = _summonBuf[i].SizeMul;

                // stand them in a ring around the seal so they do not spawn
                // inside each other and shove themselves apart
                float a = total <= 1 ? 0f : (i / (float)total) * Mathf.PI * 2f;
                Vector3 spot = seal.PlaneOrigin
                    + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.6f + total * 0.12f)
                    + Vector3.up * 0.2f;

                // THEY RISE FROM THE GROUND, NOT OUT OF THE AIR. The ring can
                // step off the edge of whatever the seal was drawn on. Zombie
                // does the standing itself; this only settles the spot the
                // BIOME is read from, so it must land on floor, not on a wall
                // or on the ink of the seal that raised them.
                int floorMask = Physics.DefaultRaycastLayers
                    & ~(1 << InkCanvasLayer.Layer) & ~(1 << VesselShell.Layer);
                if (Physics.Raycast(spot + Vector3.up * 2.5f, Vector3.down,
                        out var stand, 12f, floorMask, QueryTriggerInteraction.Ignore)
                    && stand.normal.y > 0.55f)
                    spot = stand.point + Vector3.up * 0.15f;

                // Charger is the brute; ranged uses Walker for now (Scribbler
                // can cast, and zombies never cast)
                var z = Zombie.Spawn(spot, isRanged ? ZombieKind.Walker : ZombieKind.Charger);
                if (z == null) continue;

                // multiplies the kind's own shape, so a big Solid still
                // raises a stocky brute and a big Liquid a lanky one
                z.transform.localScale *= sizeMul;

                // mass is cubic (volume); combat stats are linear in size,
                // then take the line bonus
                var srb = z.GetComponent<Rigidbody>();
                if (srb != null)
                    srb.mass = Mathf.Max(DrawingConfig.SummonMinMass,
                        srb.mass * sizeMul * sizeMul * sizeMul);

                // strength IS health, and a body's strength comes from its own
                // size and weight - a giant is strong because it is big. The
                // seal's potency still counts on top.
                var sdmg = z.GetComponent<Damageable>();
                if (sdmg != null)
                {
                    float kg = srb != null ? srb.mass : 0f;
                    sdmg.SetStrengthFromBody(sizeMul, kg);
                    sdmg.MaxStrength = Mathf.Max(DrawingConfig.SummonMinStrength,
                        sdmg.MaxStrength * power);
                    sdmg.Health = sdmg.MaxStrength;
                }
                z.AttackDamage *= sizeMul * power;

                z.gameObject.AddComponent<SummonedZombie>()
                    .Begin(seal.OwnerId, isRanged, life, _summonBuf[i].GasRadius);

                // THE GROUND MAKES THE CREATURE: raised on the peak = a frost
                // thing for life, tinted and capped by that place. After Begin
                // so the melee/ranged base colour is known.
                BiomeStamp.Apply(z.gameObject, spot);

                // in this mode ignoring a zombie has to cost you something
                var brain = z.GetComponent<ZombieBrain>();
                if (brain != null)
                {
                    brain.StrikesTurnedBacks = true;
                    if (hasArrow) brain.Order(MarchPoint(i, total)); // how many, and which way
                }
            }

            DrawingWorld.Instance?.LogEvent(total == 1
                ? "one of them gets up"
                : $"{total} of them get up");
            WorldEvents.Report(WorldEventKind.Spell, seal.PlaneOrigin, 1.5f);
            return null;
        }

        /// A client's BODY seal fired: its ink never replicated, so the HOST builds
        /// the spell from the shipped payload - no seal, no glyphs, no re-reading
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
                    // PARITY GAP (flagged, not silent): the payload ships no
                    // seal radius, so a REMOTE body seal cannot compute the
                    // rune-to-seal ratio that now drives reach locally. Old
                    // absolute law stands here until the seal message grows a
                    // sealRadius field - close it together with sides sync.
                    Radius = Mathf.Clamp(glyphHalf * DrawingConfig.ZoneRadiusScale,
                        DrawingConfig.RuneSizeMin, 3.5f),
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

        /// The arrow/Y pointing rule, readable from outside - clients ship the
        /// direction with their body-seal payload (netcode §2).
        public static Vector3 ArrowDirFor(RuneGlyph g, Vector3 normal, RuneType rune)
            => ArrowDirection(g, normal, rune);

        // One metronome per seal: the Spell pulses and every zone emits on
        // the pulse; sustain-law zones wait for the next shared beat.
        float _pulseTimer;   // starts 0 so the first pulse is the first tick
        bool _pulseFire;

        Vector3 _drift;     // accumulated arrow travel shared by every zone
        Vector3 _arrowDir;  // this frame's arrow heading, zero without one

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
                // the fastest Rapid stack in the seal drives everyone's beat -
                // simultaneity outranks per-rune tempo
                float mul = 1f;
                foreach (var z in _zones)
                    mul = Mathf.Min(mul, Mathf.Pow(0.75f, Powerups.For(_ownerId, z.Rune).Rapid));
                _pulseTimer = DrawingConfig.ZoneEmitPeriod * mul;
            }

            // an arrow or Y CARRIES the whole drawing: every zone drifts
            // along it and emissions inherit the heading. Body casts are
            // exempt - their arrow is the flight engine, and the zones must
            // stay on the skin they were drawn on.
            _arrowDir = Vector3.zero;
            if (!_bodyThrow)
                foreach (var z in _zones)
                    if ((z.Rune == RuneType.DirectionAway || z.Rune == RuneType.DirectionToward)
                        && z.PushDir.sqrMagnitude > 0.01f)
                    {
                        _arrowDir = z.PushDir;
                        _drift += z.PushDir * (DrawingConfig.ArrowZoneDrift * z.Intensity * dt);
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
        /// direction - walls on the sides mean it erupts upward.
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
                    // no caster immunity: a confined combo can pop at the
                    // caster's feet until zones join the dormant model (phase 2)
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

            // the eruption itself - a fat cone of fire particles up the vent
            SpawnBurst(_pressureCenter, dir, DrawingConfig.ExplodeParticles, 6f + power * 3f);
            _pressure = 0f;
        }

        void SpawnBurst(Vector3 origin, Vector3 dir, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                // shared material via MatterFX.Get (per-mote new Material leaks)
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
        /// pulls the shape's center of ink toward it - stable however sloppily
        /// or in whatever order it was drawn. A featureless straight line falls
        /// back to pen-travel (points the way you drew it). Away fires
        /// basehead (+ off the surface); Toward pulls back (+ into it).
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
                // symmetric (a plain line) - the pen's travel decides
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
                    z.Center = live + z.Normal * 0.06f + _drift;
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

            // every rune produces ONE particle per pulse (law 10) for as long
            // as the seal lives; emissions happen only on the shared pulse
            if (_pulseFire)
            {
                if (ProducesMatter(z.Rune))
                {
                    // State conjures ONCE per activation (re-firing the seal -
                    // pose re-close - conjures the next batch)
                    if (!z.Conjured)
                    {
                        z.Conjured = true;
                        EmitParticles(z);
                    }
                }
                else if (TrackerAlive(ref z.Tracked))
                {
                    // sustain law: this rune's product is still out there;
                    // re-emit only once the chain's final product is gone
                }
                else
                {
                    EmitParticles(z);
                }
            }

            // what remains of the FIELD: the player flight channel, a weak
            // direction push, and darkness blinding - everything else moved
            // into the particles
            int n = Physics.OverlapSphereNonAlloc(z.Center, z.Radius, GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) Apply(z, GrammarFX.ScanBuffer[i], dt);
        }

        void EmitParticles(Zone z)
        {
            if (ProducesMatter(z.Rune)) { SpawnMatter(z); return; }

            // State runes never reach here - ProducesMatter caught them above.
            ParticleKind kind = SpellParticle.KindOf(z.Rune);

            // the caster's powerups shape the burst (per rune family);
            // the Spell perk (local player only, like Powerups) tops it up
            var buff = Powerups.For(_ownerId, z.Rune);
            bool localCaster = _ownerId == Grimoire.LocalPlayerId;
            // one particle per rune - the drawing is the recipe; powerups
            // still add extras
            int count = Mathf.Min(12, 1 + buff.More);
            float speedMul = 1f + 0.5f * buff.Fast;
            float potent = 1f + 0.35f * buff.Potent;
            float bigMul = 1f + 0.3f * buff.Big;

            Vector3 dir = kind == ParticleKind.Push ? z.PushDir
                : _arrowDir.sqrMagnitude > 0.01f ? _arrowDir : z.Normal;

            // a body (or weapon) seal throws its cast along the outward
            // normal, sparing the caster briefly; siblings leave together
            // and combine mid-air
            bool bodyCast = _bodyThrow; // remote body seals throw too (netcode §2)
            var m0 = z.Glyph != null && z.Glyph.Members.Count > 0 ? z.Glyph.Members[0] : null;
            if (m0 != null && m0.Persistent && m0.First != null)
                bodyCast = true; // the activation delay is universal, not personal

            for (int i = 0; i < count; i++)
            {
                var p = SpellParticle.Emit(kind,
                    z.Center + z.Normal * 0.12f + Random.insideUnitSphere * z.Radius * 0.18f,
                    dir, z.Intensity);
                // size is the rune's own drawn size, NOT z.Radius (which
                // means reach, the rune-to-seal ratio)
                p.SrcSize = z.GlyphSize * DrawingConfig.ZoneRadiusScale * bigMul;
                p.Lineage = RuneGrammar.Bit(z.Rune); // GRAMMAR v4: ancestry starts here -
                                                     // all 12 in one chain = THE DEMON
                p.SealId = GetHashCode();            // siblings of one DRAWING pair up first
                // the vectors remember WHERE THE GLYPH POINTED - the arrow
                // drags along this, the Y reverses against it. Kept apart
                // from the mote's own drift, which changes as it flies.
                if (kind == ParticleKind.Push) p._aimDir = z.PushDir;
                p.OwnerId = _ownerId;                // dormant wake rules ask whose spell this is
                if (i == 0) z.Tracked = p;           // sustain law: the rune WATCHES this one
                                                     // (powerup extras are untracked bonuses)
                p.Vel *= speedMul;
                // ground seals cast previews; body/weapon casts are throws,
                // and throwing activates
                if (bodyCast)
                    p.ThrowFrom(z.Normal * DrawingConfig.BodyCastThrowSpeed + p.Vel);
                else if (kind != ParticleKind.Push)
                    // anchored to its rune's spot on the seal plane; vectors
                    // skip this - they are never dormant
                    p.Sleep(z.Center, z.Normal);
                p.Temp *= potent;
                p.Lum *= potent;
                p.Density *= potent;
                p.Stick = p.Stick * potent + 0.35f * buff.Bond;
                p.Echo = buff.Echo;
                if (bigMul > 1f) p.transform.localScale *= bigMul;
            }
        }

        /// GROUND combos become a WAITING GHOST instead of the real thing -
        /// one dormant preview mote carries the conjure and casts it where it
        /// wakes: walk it over, throw it, or leave it armed as a trap. Body
        /// and weapon casts return false and fire the real thing instantly:
        /// a skin cast is a throw, and throwing IS activation.
        bool PreviewConjure(Zone z, ParticleKind ghostKind, float srcSize,
            System.Action<Vector3> conjure, float realSize = 0f)
        {
            bool bodyCast = _bodyThrow;
            var m0 = z.Glyph != null && z.Glyph.Members.Count > 0 ? z.Glyph.Members[0] : null;
            if (m0 != null && m0.Persistent && m0.First != null) bodyCast = true;
            if (bodyCast) return false;

            var p = SpellParticle.Emit(ghostKind, z.Center + z.Normal * 0.15f, z.Normal, z.Intensity);
            if (p == null) return false;
            p.SrcSize = Mathf.Max(0.5f, srcSize);
            p.OwnerId = _ownerId;
            p.Lineage = RuneGrammar.Bit(z.Rune);
            p.SealId = GetHashCode();
            p.PendingConjure = conjure;
            p.Sleep(z.Center, z.Normal);
            // when the caller names the real thing's size, the ghost is that
            // at preview scale; otherwise a plain 2x
            if (realSize > 0f)
                p.transform.localScale = Vector3.one
                    * Mathf.Max(0.45f, realSize * DrawingConfig.DormantPreviewScale);
            else
                p.transform.localScale *= 2f;
            // sustain law: the ghost IS the zone's live product; without this
            // the rune re-manufactures each beat
            z.Tracked = p;
            return true;
        }

        // ONLY State runes spawn matter (once, at activation) - everything else modifies it.
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
            // re-emits - an active seal is a factory
            if (tracked is SpellParticle alive) return !alive.Dead && !alive.Claimed;
            return true; // fields, matter, demons - Component null-check above rules
        }

        /// The slim field remainder: the player flight channel (feet seals
        /// fly), a weak direction push, and darkness blinding.
        void Apply(Zone z, Collider c, float dt)
        {
            if (c == null) return;

            // the player is a CharacterController - physics forces bounce off
            // it, so force runes feed its spell-velocity channel instead
            var pilot = c.GetComponent<SimpleFPSController>();
            if (pilot != null)
            {
                switch (z.Rune)
                {
                    case RuneType.DirectionAway:
                    case RuneType.DirectionToward:
                        // feet-seal flight only (body ink); ground arrow
                        // seals don't push players - particles are the movers
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
                    // washes off them faster - dark
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
                case RuneType.LuminanceDown: // darkness BLINDS - they can't find you inside it
                    var dark = c.GetComponentInParent<Creature>();
                    if (dark != null) dark.ApplyBlind(DrawingConfig.BlindSeconds);
                    break;

                case RuneType.LuminanceUp: // holy light sears the undead
                    var seared = c.GetComponentInParent<Creature>();
                    if (seared != null)
                        seared.GetComponent<Damageable>()?.TakeDamage(
                            DrawingConfig.HolyLightPerSec * z.Intensity * dt, "holy light");
                    break;
            }
        }

        /// Only State runes reach here, once per activation. Solid conjures
        /// the surface's material (Stone when unmarked); Liquid its liquid
        /// form (StoneLava, FleshBlood, CoalOil, default Water). Every sibling
        /// rune in the seal changes what it conjures (SPELL_PARTICLES.md cross
        /// matrix); identity rides along as lineage.
        void SpawnMatter(Zone z)
        {
            bool solid = z.Rune == RuneType.StateSolid;
            var mat = _surface;
            if (mat == SurfaceMaterialType.Unknown)
                mat = solid ? SurfaceMaterialType.Stone : SurfaceMaterialType.Water;


            // one recipe per drawing: the first zone of each State rune does
            // the work (plain loop - Find's lambda allocates per activation)
            foreach (var o in _zones)
                if (o.Rune == z.Rune) { if (o != z) return; break; }

            // ---- THE THRESHOLD DOCTRINE: no seal recipes. The State rune
            // conjures its matter and NOTHING else - sibling runes emit their
            // own particles, and whatever they become together is decided by
            // payload addition against the table, in the world, in order.
            ulong lineage = 0;
            foreach (var other in _zones) lineage |= RuneGrammar.Bit(other.Rune);
            RuneGrammar.TryDemon(lineage, z.Center, z.Radius); // a full drawing IS a chain

            var buff = Powerups.For(_ownerId, z.Rune);
            float size = Mathf.Clamp(z.GlyphSize * 0.5f, 0.08f, 0.45f)
                * Mathf.Lerp(0.75f, 1.15f, z.Intensity) * (1f + 0.25f * buff.Big);

            // one conjure per cast; density buffs change the size, never the
            // count
            {
                // birth is at the seal with a small lift; the strike driver
                // below does the jumping. Liquids slump into puddles in place.
                float lift = size * 0.55f;
                // the seal's side count picks the solid's shape (resolved in
                // Matter.Spawn); liquid/gas ignore it
                var conjured = Matter.Spawn(mat, solid ? MatterPhase.Solid : MatterPhase.Liquid,
                size * 2f, // the drawn size, doubled
                    z.Center + z.Normal * lift, solid ? _edges : 0);
                // a particle in behavior, not in shape: conjured matter keeps
                // its shape and material, plus flight (float, lock, jump)
                var msStrike = conjured.GetComponent<MatterStrike>();
                if (msStrike == null) msStrike = conjured.gameObject.AddComponent<MatterStrike>();
                msStrike.Init(_ownerId, mat, solid ? MatterPhase.Solid : MatterPhase.Liquid, size * 2f);
                // spell-born matter can never teach a rune - otherwise
                // conjure/touch/absorb prints runes. Covers authored prefabs
                // carrying Analyzable.
                foreach (var an in conjured.GetComponentsInChildren<Analyzable>(true))
                    an.SpellBorn = true;
                conjured.Lineage = lineage;
                if (buff.Bond > 0) conjured.AddStickiness(0.2f * buff.Bond); // gooier conjures
                // NO seal-side dressing: sibling particles land on the blob in
                // the world and change it there - that is the one law
            }
        }

        void BuildVisual(Zone z)
        {
            var root = new GameObject($"Zone_{z.Rune}");
            root.transform.SetParent(transform, false);
            root.transform.position = z.Center;
            z.Visual = root;

            // no zone rings; the emitted particles are the visibility

            // no static ground arrow; the flying arrow/Y particle is the
            // whole visual
            if (z.Rune == RuneType.DirectionAway || z.Rune == RuneType.DirectionToward)
                root.transform.rotation = Quaternion.LookRotation(z.PushDir);

            // Luminance-down: mild on its own; deepens & spreads with low
            // density / low stickiness (see _darkSpread).
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

            // State runes make matter, not light - no zone light for them
            if (z.Rune == RuneType.StateSolid || z.Rune == RuneType.StateLiquid) return;

            // only the Light rune produces light; Heat keeps a barely-there
            // ember flicker so a fire zone reads at night
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
                ember.intensity = 0.35f * z.Intensity; // faint - an ember, not a floodlight
                z.Light = ember;
            }
        }
    }
}
