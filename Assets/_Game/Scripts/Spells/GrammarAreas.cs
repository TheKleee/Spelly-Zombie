using UnityEngine;

namespace SpellyZombie
{
    /// GRAMMAR v4 area effects — the lvl3 ULTIMATES and the paradox products
    /// (SPELL_PARTICLES.md). Each is a small self-contained field; numbers live
    /// in DrawingConfig so Marko tunes them in one file.
    public static class GrammarFX
    {
        static readonly Collider[] _hits = new Collider[48];

        public static Collider[] ScanBuffer => _hits;

        /// Shared soft-sphere visual for every field. The dome is kept FAINT —
        /// from inside it must read as atmosphere, not a blindfold (Marko:
        /// "while inside you can't tell you're inside").
        public static Transform FieldBall(Vector3 at, float radius, Color c, MoteShade shade)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "GrammarField";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            go.transform.localScale = Vector3.one * radius * 2f;
            c.a *= 0.45f;
            go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(c, shade);
            return go.transform;
        }

        /// The BOUNDARY RING: a bright circle on the ground under a field, so
        /// the area reads from inside and outside alike — no reading required.
        public static Transform GroundRing(Transform parent, Color c)
        {
            var go = new GameObject("AreaRing");
            go.transform.SetParent(parent, false);

            // sit the ring on the actual ground beneath the field
            if (Physics.Raycast(parent.position + Vector3.up * 0.3f, Vector3.down,
                    out var hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                go.transform.position = hit.point + Vector3.up * 0.04f;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = 0.09f;
            lr.positionCount = 36;
            for (int i = 0; i < 36; i++)
            {
                float a = i / 36f * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            lr.sharedMaterial = MatterFX.Get(new Color(c.r, c.g, c.b, 0.9f), MoteShade.Additive);
            return go.transform;
        }

        public static void PuffBurst(Vector3 at, Color c, int n = 4)
        {
            for (int i = 0; i < n; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = "Puff";
                Object.Destroy(puff.GetComponent<Collider>());
                puff.transform.position = at + Random.insideUnitSphere * 0.15f;
                puff.transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f);
                puff.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(c, MoteShade.Transparent);
                var rb = puff.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearVelocity = Vector3.up * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.35f;
                Object.Destroy(puff, 0.7f);
            }
        }

        /// ONE fire mote (dedupe: Spell.SpawnBurst, FlameBurst and MeteorTrail
        /// each hand-rolled this sphere — SpawnBurst even leaked a Material per
        /// mote via `new Material`; MatterFX.Get caches).
        public static GameObject FireMote(Vector3 at, float scale, float life)
        {
            var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            f.name = "Fire";
            Object.Destroy(f.GetComponent<Collider>());
            f.transform.position = at;
            f.transform.localScale = Vector3.one * scale;
            f.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(
                Color.Lerp(new Color(1f, 0.75f, 0.15f), new Color(1f, 0.25f, 0.05f), Random.value),
                MoteShade.Additive);
            Object.Destroy(f, life);
            return f;
        }

        /// A gravity-free fling of fire motes — the shared bloom look.
        public static void FireBloom(Vector3 at, int count, float speed, float upKick)
        {
            for (int i = 0; i < count; i++)
            {
                var f = FireMote(at + Random.insideUnitSphere * 0.4f,
                    Random.Range(0.15f, 0.3f), Random.Range(0.4f, 0.9f));
                var rb = f.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearVelocity = Random.onUnitSphere * speed + Vector3.up * upKick;
            }
        }

        /// Spark lvl3 — FLAME BURST: flames burst across the area, once.
        public static void FlameBurst(Vector3 at, float power)
        {
            float r = DrawingConfig.UltimateRadius;
            Juice.Boom(at, 0.7f);
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.FireBurst, at); // the JMO bloom
            WorldEvents.Report(WorldEventKind.Explosion, at, 2f);
            DrawingWorld.Instance?.LogEvent("FLAME BURST");
            int n = Physics.OverlapSphereNonAlloc(at, r, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i];
                if (c == null) continue;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null) { pl.TakeHit((pl.transform.position - at).normalized * 9f, 28f * power); continue; }
                SpellParticle.GiveHeatTo(c, 200f * power); // combinations hit MANY times harder (Marko)
                var rb = c.attachedRigidbody;
                if (rb != null) rb.AddForce((rb.worldCenterOfMass - at).normalized * 9f, ForceMode.VelocityChange);
            }
            FireBloom(at, 10, 4f, 2f); // the visible bloom
        }
    }

    /// Base class for timed fields: a soft sphere that ticks an effect on
    /// everything inside, then fades. Subclasses implement one Affect().
    public abstract class GrammarField : MonoBehaviour
    {
        public float Power = 1f;
        public float Radius = 3.5f;
        public float Seconds = 5f;
        public Color Tint;      // full-strength field colour — feeds the inside-HUD

        protected Transform Ball;
        /// HIS FX_&lt;FieldClass&gt; skin, when he authored one (AXIOM: code must
        /// not build its own art inside it, and must not stomp its scale).
        [System.NonSerialized] public bool HasSkin;
        [System.NonSerialized] public Transform Skin;
        [System.NonSerialized] public Vector3 SkinBase = Vector3.one;
        /// How his skin should scale with the field. Ball uses DIAMETER, so
        /// the default matches the dome it replaces; fields may override.
        protected virtual Vector3 SkinShape => Vector3.one * Radius * 2f;
        Transform _ring;        // the ground boundary circle
        float _age, _tick;
        static readonly System.Collections.Generic.HashSet<Component> _seenRoots =
            new System.Collections.Generic.HashSet<Component>(); // per-tick body dedupe

        protected virtual float TickPeriod => 0.35f;
        protected abstract void Affect(Collider c, float dt);
        protected virtual void Grow(float dt) { }
        protected virtual void ShapeBall() { if (Ball != null) Ball.localScale = Vector3.one * Radius * 2f; }
        protected virtual void OnExpire() { }

        protected static T Spawn<T>(Vector3 at, float power, float radius, float seconds, Color c, MoteShade shade)
            where T : GrammarField
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.position = at;
            var f = go.AddComponent<T>();
            f.Power = power;
            f.Radius = radius;
            f.Seconds = seconds;
            f.Tint = new Color(c.r, c.g, c.b, 1f);
            f.Ball = GrammarFX.FieldBall(at, radius, c, shade);
            f.Ball.SetParent(go.transform, true);
            f._ring = GrammarFX.GroundRing(go.transform, c);

            // MARKO'S FX OVERRIDE: a prefab named FX_<FieldClass> (FX_SnowField,
            // FX_PlasmaField, FX_TornadoField, FX_HealingField…) in
            // Resources/Custom replaces the code dome — ring + inside-HUD stay.
            var skin = PrefabVault.Get("FX_" + typeof(T).Name);
            if (skin != null)
            {
                f.Skin = Object.Instantiate(skin, go.transform, false).transform;
                f.SkinBase = f.Skin.localScale;   // his authored size is HIS
                f.HasSkin = true;                 // fields must not build code art over it
                var dome = f.Ball.GetComponent<Renderer>();
                if (dome != null) dome.enabled = false;
            }
            else if (FxLibrary.I != null)
            {
                // the JMO layer (Marko's mapping, Jul 22) rides the field for
                // its whole life — his FX_<FieldClass> override still wins
                var jmo = FxLibrary.I.FieldFor(typeof(T).Name);
                if (jmo != null) FxLibrary.Spawn(jmo, at, go.transform, seconds + 0.5f);
            }
            return f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            Grow(dt);
            ShapeBall();
            if (_ring != null) _ring.localScale = Vector3.one * Radius; // grows with the field
            // his skin grows WITH the field, scaled from what he authored
            // (a growing black hole used to leave a fixed-size prefab behind)
            if (Skin != null) Skin.localScale = Vector3.Scale(SkinBase, SkinShape);

            _tick -= dt;
            if (_tick <= 0f)
            {
                _tick = TickPeriod;
                // Collide, not Ignore: spell particles are TRIGGER spheres and
                // the fields must reach them (a black hole eats magic too)
                int n = Physics.OverlapSphereNonAlloc(transform.position, Radius,
                    GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Collide);
                _seenRoots.Clear();
                for (int i = 0; i < n; i++)
                {
                    var c = GrammarFX.ScanBuffer[i];
                    if (c == null) continue;
                    // ONE BODY = ONE TICK: the player's rig is a dozen limb
                    // capsules and each was billed separately (verified: snow
                    // chilled per-limb). Resolve to the owning body and dedupe.
                    Component root = (Component)c.GetComponentInParent<SimpleFPSController>()
                        ?? (Component)c.GetComponentInParent<Creature>()
                        ?? (Component)c.GetComponent<Matter>()
                        ?? (Component)c.attachedRigidbody;
                    if (root != null && !_seenRoots.Add(root)) continue;
                    // YOU ARE INSIDE and you can TELL (Marko's rule): standing
                    // in any field pulses the screen edges in its colour
                    if (root is SimpleFPSController pilotRoot)
                    {
                        GrammarFieldHUD.Inside(Tint);
                        // the elected collider may be a LIMB BONE — fields test
                        // GetComponent on it, so hand them the pilot's own
                        // capsule or healing (and every player branch) misses
                        var pilotCC = pilotRoot.GetComponent<CharacterController>();
                        if (pilotCC != null) c = pilotCC;
                    }
                    Affect(c, TickPeriod);
                }
            }

            if (_age >= Seconds) { OnExpire(); Destroy(gameObject); }
        }
    }

    /// Frost lvl3 — SNOW FIELD: freezing snow covers the area; ice damage,
    /// slow, and staying too long freezes you solid for a moment.
    public class SnowField : GrammarField
    {
        float _burstTick;

        public static SnowField Open(Vector3 at, float power) =>
            Spawn<SnowField>(at, power, DrawingConfig.UltimateRadius, DrawingConfig.UltimateSeconds * 1.3f,
                new Color(0.85f, 0.93f, 1f, 0.35f), MoteShade.Transparent);

        protected override void Grow(float dt)
        {
            // ice bursts flurry across the area (Marko's JMO mapping)
            _burstTick -= dt;
            if (_burstTick > 0f || FxLibrary.I == null) return;
            _burstTick = 0.9f;
            Vector2 r = Random.insideUnitCircle * Radius * 0.8f;
            FxLibrary.Spawn(FxLibrary.I.IceHit, transform.position + new Vector3(r.x, 0.4f, r.y));
        }

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            // snow only COOLS (Marko's derived law) — being cold does the rest
            if (pl != null) { BodyState.Of(pl)?.PushTemp(-8f * Power); return; }
            var cr = c.GetComponentInParent<Creature>();
            if (cr != null)
            {
                cr.ApplyStuck(0.5f);
                SpellParticle.GiveHeatTo(c, -60f * Power);
                if (Random.value < 0.05f && FxLibrary.I != null) // comic beat
                    FxLibrary.Spawn(FxLibrary.I.TextFrozen, cr.transform.position + Vector3.up * 1.6f);
                return;
            }
            SpellParticle.GiveHeatTo(c, -12f * Power);
        }
    }

    /// Light lvl3 — PLASMA: a small sun. Blinds, radiates heat, and touching
    /// it is a catastrophe. (Light+Light = lightning; feed the lightning more
    /// light and you have made a star.)
    public class PlasmaField : GrammarField
    {
        Light _glow;

        public static PlasmaField Open(Vector3 at, float power)
        {
            var f = Spawn<PlasmaField>(at + Vector3.up * 1.2f, power, 1.1f, DrawingConfig.UltimateSeconds,
                new Color(1f, 0.9f, 0.5f, 0.95f), MoteShade.Additive);
            var l = f.gameObject.AddComponent<Light>();
            l.type = LightType.Point; l.range = 18f; l.intensity = 10f;
            l.color = new Color(1f, 0.93f, 0.7f);
            f._glow = l;
            DrawingWorld.Instance?.LogEvent("a SMALL SUN ignites");
            ZombieBrain.ScareVisible(at, 25f, 6f);
            return f;
        }

        protected override float TickPeriod => 0.4f;

        protected override void Affect(Collider c, float dt)
        {
            float d = Vector3.Distance(c.bounds.center, transform.position);
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                var board = BodyState.Of(pl);
                board?.PushLum(1f); // it IS a sun — the world whites out near it
                if (d < Radius * 1.6f)
                {
                    board?.PushTemp(50f * Power); // touching it is still a catastrophe
                    pl.TakeHit((pl.transform.position - transform.position).normalized * 6f, 0f);
                }
                return;
            }
            var cr = c.GetComponentInParent<Creature>();
            if (cr != null) cr.ApplyBlind(2f);
            if (d < Radius * 1.6f)
            {
                var dmg = c.GetComponentInParent<Damageable>();
                if (dmg != null) dmg.TakeDamage(120f * Power, "touched the sun");
            }
            SpellParticle.GiveHeatTo(c, 30f * Power); // it radiates
        }

        void LateUpdate()
        {
            if (_glow != null) _glow.intensity = 8f + Mathf.PerlinNoise(Time.time * 7f, 0.3f) * 4f;
        }
    }

    /// Dark lvl2/lvl3 — BLACK HOLE: pulls things in. The lvl3 version GROWS,
    /// rapidly spreading until it has sucked as much as it can (Marko's rule).
    public class BlackHoleField : GrammarField
    {
        public bool Growing;
        int _swallowed;

        public static BlackHoleField Open(Vector3 at, float power, bool growing)
        {
            var f = Spawn<BlackHoleField>(at + Vector3.up * 0.8f, power,
                growing ? 2.2f : 1.8f,
                DrawingConfig.UltimateSeconds * (growing ? 1.4f : 0.9f),
                new Color(0.02f, 0.01f, 0.05f, 0.93f), MoteShade.Transparent);
            f.Growing = growing;
            // a black hole you can SEE (Marko: "currently invisible") — an
            // OPAQUE void core riding the Ball, so it grows with the hunger;
            // the transparent shell becomes its haze
            if (!f.HasSkin) // AXIOM: never build code art inside HIS skin
            {
                var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                core.name = "VoidCore";
                Object.Destroy(core.GetComponent<Collider>());
                core.transform.SetParent(f.Ball, false);
                core.transform.localScale = Vector3.one * 0.42f;
                core.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(Color.black, MoteShade.Opaque);
            }
            DrawingWorld.Instance?.LogEvent(growing ? "the BLACK HOLE grows hungry" : "the dark collapses — BLACK HOLE");
            return f;
        }

        protected override float TickPeriod => 0.12f; // pull must feel continuous

        protected override void Grow(float dt)
        {
            if (Growing) Radius = Mathf.Min(Radius + dt * 1.6f, 8f);
        }

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null) // players get DRAGGED, never swallowed
            {
                pl.AddSpellForce((transform.position - c.transform.position).normalized * 20f * Power, dt);
                return;
            }
            var p = c.GetComponent<SpellParticle>();
            if (p != null) { p.Pull(transform.position, dt * 2f); return; }
            var rb = c.attachedRigidbody;
            if (rb == null) return;
            Vector3 to = transform.position - rb.worldCenterOfMass;
            rb.AddForce(to.normalized * Mathf.Lerp(40f, 10f, to.magnitude / Radius), ForceMode.Acceleration);
            if (to.magnitude < 0.9f && _swallowed < (Growing ? 14 : 5))
            {
                bool edible = rb.GetComponentInParent<Zombie>() != null || rb.GetComponent<Matter>() != null;
                if (edible) { _swallowed++; Destroy(rb.gameObject); }
            }
        }
    }

    /// Light+Dark paradox — WHITE HOLE: black hole strength, opposite sign.
    public class WhiteHoleField : GrammarField
    {
        public static WhiteHoleField Open(Vector3 at, float power)
        {
            var f = Spawn<WhiteHoleField>(at + Vector3.up * 0.8f, power, 4f, DrawingConfig.UltimateSeconds * 0.9f,
                new Color(1f, 1f, 0.97f, 0.75f), MoteShade.Additive);
            if (FxLibrary.I != null) // ignition flash; the stars ride the field
                FxLibrary.Spawn(FxLibrary.I.Flash, at + Vector3.up * 0.8f);
            // THE OPENING YEET (Marko: "it should yeet everything in all
            // directions around it") — one detonation the moment it ignites
            int n = Physics.OverlapSphereNonAlloc(at, f.Radius * 1.6f, GrammarFX.ScanBuffer,
                ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null) continue;
                Vector3 away = (c.bounds.center - at); away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere.normalized;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null) { pl.TakeHit(away * 13f + Vector3.up * 3.5f, 0f); continue; }
                var cr = c.GetComponentInParent<Creature>();
                if (cr != null) cr.KnockDown(1.2f);
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddForce(away * 15f + Vector3.up * 4f, ForceMode.VelocityChange);
            }
            DrawingWorld.Instance?.LogEvent("light and dark REFUSE each other — WHITE HOLE");
            return f;
        }

        protected override float TickPeriod => 0.12f;

        protected override void Affect(Collider c, float dt)
        {
            Vector3 away = (c.bounds.center - transform.position).normalized;
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null) { pl.AddSpellForce(away * 24f * Power, dt); return; }
            var p = c.GetComponent<SpellParticle>();
            if (p != null) { p.Pull(transform.position + away * 30f, dt * 2f); return; }
            var rb = c.attachedRigidbody;
            if (rb != null) rb.AddForce(away * 34f, ForceMode.Acceleration);
        }
    }

    /// Glue lvl3 — TIME FREEZE: the area spreads and stops anything inside.
    public class TimeFreezeField : GrammarField
    {
        public static TimeFreezeField Open(Vector3 at, float power)
        {
            var f = Spawn<TimeFreezeField>(at, power, DrawingConfig.UltimateRadius, DrawingConfig.UltimateSeconds * 0.8f,
                new Color(0.75f, 0.95f, 0.7f, 0.28f), MoteShade.Transparent);
            DrawingWorld.Instance?.LogEvent("absolute grip — TIME stops here");
            return f;
        }

        protected override float TickPeriod => 0.15f;

        protected override void Affect(Collider c, float dt)
        {
            // it stops ANYTHING nearby (Marko) — wizards obey time too
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null) { pl.StickFeet(0.7f); return; }
            var cr = c.GetComponentInParent<Creature>();
            if (cr != null) cr.ApplyStuck(1.2f);
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, Vector3.zero, 100f * dt);
                rb.angularVelocity = Vector3.MoveTowards(rb.angularVelocity, Vector3.zero, 100f * dt);
            }
            var p = c.GetComponent<SpellParticle>();
            if (p != null) p.Vel = Vector3.zero; // even magic stands still
        }
    }

    /// Repel lvl3 — INERTIA: nothing inside can stand in place.
    public class InertiaField : GrammarField
    {
        public static InertiaField Open(Vector3 at, float power)
        {
            var f = Spawn<InertiaField>(at, power, DrawingConfig.UltimateRadius, DrawingConfig.UltimateSeconds,
                new Color(0.9f, 0.9f, 1f, 0.22f), MoteShade.Transparent);
            DrawingWorld.Instance?.LogEvent("absolute slip — NOTHING stands still");
            return f;
        }

        protected override float TickPeriod => 0.3f;

        protected override void Affect(Collider c, float dt)
        {
            Vector3 shove = Random.insideUnitSphere;
            shove.y = Mathf.Abs(shove.y) * 0.3f;
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null) { pl.AddSpellForce(shove.normalized * 14f * Power, dt * 3f); return; }
            var cr = c.GetComponentInParent<Creature>();
            if (cr != null) cr.ApplySlip(1.5f);
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
            {
                rb.linearDamping = 0f;
                rb.AddForce(shove.normalized * 8f * Power, ForceMode.VelocityChange);
            }
        }
    }

    /// Arrow+Arrow — TORNADO (swirls, lifts, tosses out) · Y+Y — WHIRLPOOL
    /// (swirls, drags DOWN, tosses out). Marko's vector laws, all of them:
    /// the storm CARRIES particles and stamps its lineage into them (spinning
    /// everything together is a Demon path), and another Arrow/Y thrown into
    /// it STEERS it — the storm travels.
    public class TornadoField : GrammarField
    {
        public ulong FieldLineage;
        Vector3 _drift;
        bool _down;

        public static TornadoField Open(Vector3 at, float power, bool down, ulong lineage)
        {
            var f = Spawn<TornadoField>(at + Vector3.up * (down ? 0.2f : 1.4f), power,
                2.4f, DrawingConfig.UltimateSeconds * 1.2f,
                down ? new Color(0.35f, 0.55f, 0.75f, 0.35f) : new Color(0.78f, 0.8f, 0.88f, 0.3f),
                MoteShade.Transparent);
            f._down = down;
            f.FieldLineage = lineage;
            // the storm WEARS its vectors (Marko: "visual arrows going around
            // it") — parented to the Ball, they ride its spin for free
            if (!f.HasSkin) // AXIOM: his FX_TornadoField owns the look outright
            {
                var vecMat = MatterFX.Get(down ? new Color(0.7f, 0.35f, 0.95f) : new Color(0.92f, 0.9f, 0.6f),
                    MoteShade.Additive); // pull wears PURPLE (Marko), push stays gold
                for (int i = 0; i < 3; i++)
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    g.name = "StormVector";
                    Object.Destroy(g.GetComponent<Collider>());
                    g.transform.SetParent(f.Ball, false);
                    float ang = i * 120f * Mathf.Deg2Rad;
                    g.transform.localPosition = new Vector3(Mathf.Cos(ang) * 0.42f, (i - 1) * 0.16f, Mathf.Sin(ang) * 0.42f);
                    g.transform.localRotation = Quaternion.LookRotation(
                        new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * (down ? -1f : 1f));
                    g.transform.localScale = new Vector3(0.03f, 0.03f, 0.22f);
                    g.GetComponent<Renderer>().sharedMaterial = vecMat;
                }
            }
            Juice.Whoosh(at);
            var lib = FxLibrary.I; // wind wears trails, water wears rings (Marko's mapping)
            if (lib != null)
                FxLibrary.Spawn(down ? lib.Ripples : lib.WindTrails, f.transform.position, f.transform,
                    DrawingConfig.UltimateSeconds * 1.2f + 0.5f);
            DrawingWorld.Instance?.LogEvent(down ? "the pulls entwine — WHIRLPOOL" : "the winds entwine — TORNADO");
            return f;
        }

        protected override float TickPeriod => 0.1f; // wind must feel continuous

        protected override void ShapeBall()
        {
            if (Ball != null)
                Ball.localScale = new Vector3(Radius * 1.5f, Radius * (_down ? 0.8f : 3f), Radius * 1.5f);
        }

        protected override void Grow(float dt)
        {
            transform.position += _drift * dt; // steered by thrown Arrows/Ys (Marko's ruling)
            if (Ball != null) Ball.Rotate(0f, (_down ? -540f : 540f) * dt, 0f); // counter-spins the tornado
        }

        protected override void Affect(Collider c, float dt)
        {
            Vector3 rel = c.bounds.center - transform.position;
            rel.y = 0f;
            if (rel.sqrMagnitude < 0.01f) rel = Random.insideUnitSphere;
            // the whirlpool turns the OPPOSITE way to the tornado (Marko: the
            // pull has its own specific direction — not the same spin re-used)
            Vector3 tangent = Vector3.Cross(_down ? Vector3.down : Vector3.up, rel).normalized;
            Vector3 vertical = _down ? Vector3.down : Vector3.up;
            Vector3 swirl = tangent * 11f + vertical * 6.5f - rel.normalized * 3f; // REAL winds (Marko: mayhem)

            var p = c.GetComponent<SpellParticle>();
            if (p != null)
            {
                // an Arrow/Y thrown INTO the storm gives it travel direction
                if (p.Kind == ParticleKind.Push)
                {
                    Vector3 d = p.Vel; d.y = 0f;
                    if (d.sqrMagnitude > 0.5f) _drift = d.normalized * 2.2f;
                    Object.Destroy(p.gameObject);
                    return;
                }
                // carried — and the spin STAMPS the vector into its ancestry
                p.Lineage |= FieldLineage;
                p.Pull(c.bounds.center + swirl, dt * 1.6f);
                return;
            }

            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                pl.AddSpellForce(swirl * 3.2f * Power, dt);
                var board = BodyState.Of(pl);
                board?.PushMove((_down ? -0.5f : 0.5f) * dt); // ride it out buffed — or Y-owned
                if (_down) // the Y-storm EATS your direction vector (Marko)
                {
                    Vector3 pv = pl.Velocity; pv.y = 0f;
                    pl.AddSpellForce(-pv * 1.8f, dt);
                }
                return;
            }

            var cr = c.GetComponentInParent<Creature>();
            if (cr != null && Random.value < 0.12f) cr.KnockDown(1f); // tumble-dried
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
                rb.AddForce(swirl * 9f * Power, ForceMode.Acceleration);
        }

        /// The storm dies the way Marko wrote it: it TOSSES everything out.
        protected override void OnExpire()
        {
            Juice.Whoosh(transform.position);
            int n = Physics.OverlapSphereNonAlloc(transform.position, Radius * 1.7f,
                GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null) continue;
                Vector3 fling = (c.bounds.center - transform.position);
                fling.y = 0f;
                fling = fling.normalized * 7f + Vector3.up * 4f;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null) { pl.AddSpellForce(fling * 2.6f, 0.5f); continue; }
                var p = c.GetComponent<SpellParticle>();
                if (p != null) { p.Vel = fling; continue; }
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddForce(fling, ForceMode.VelocityChange);
            }
        }
    }

    /// HeatDown + Light exotic — HEALING AREA (Marko's matrix): cold light is
    /// MERCY, the only healing in the game. Walk in, mend over time.
    public class HealingField : GrammarField
    {
        public static HealingField Open(Vector3 at, float power)
        {
            var f = Spawn<HealingField>(at, power, DrawingConfig.UltimateRadius * 0.8f,
                DrawingConfig.UltimateSeconds * 1.4f,
                new Color(0.85f, 1f, 0.8f, 0.4f), MoteShade.Additive);
            DrawingWorld.Instance?.LogEvent("cold light is MERCY — a healing ground");
            Juice.Chime(at);
            if (FxLibrary.I != null) // the mercy ring — a runic circle on the ground
                FxLibrary.Spawn(FxLibrary.I.RunicAura, at + Vector3.up * 0.1f, f.transform,
                    DrawingConfig.UltimateSeconds * 1.4f + 0.5f);
            return f;
        }

        protected override float TickPeriod => 0.3f;

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl == null || pl.IsDowned) return; // the downed need a friend, not a fountain
            pl.Health = Mathf.Min(Perks.MaxHealth, pl.Health + 25f * Power * dt); // mercy scaled with the mayhem
        }
    }

    /// Dense+Spread paradox — BARRIER (Marko's ruling): two-way ISOLATION.
    /// What's inside is protected from the environment AND cannot act on it.
    /// No particle donates through it, no combination happens inside it.
    /// The system's insulator — and the only way to stop an accidental Demon.
    public class Barrier : MonoBehaviour
    {
        float _left;
        Transform _shell;

        public static bool Protects(Component c) =>
            c != null && c.GetComponentInParent<Barrier>() != null;

        public static void Wrap(Transform root)
        {
            var b = root.GetComponent<Barrier>();
            if (b == null)
            {
                b = root.gameObject.AddComponent<Barrier>();
                float r = 0.8f;
                var rend = root.GetComponentInChildren<Renderer>();
                if (rend != null) r = Mathf.Clamp(rend.bounds.extents.magnitude * 1.1f, 0.5f, 2.2f);
                b._shell = GrammarFX.FieldBall(root.position, r,
                    new Color(0.6f, 0.9f, 1f, 0.3f), MoteShade.Transparent);
                b._shell.SetParent(root, true);
                if (FxLibrary.I != null) // the JMO shield loop rides the shell
                    FxLibrary.Spawn(FxLibrary.I.Shield, root.position, b._shell, 0f);
                DrawingWorld.Instance?.LogEvent("BARRIER — isolated (protected, and harmless)");
            }
            b._left = DrawingConfig.BarrierSeconds;
        }

        void Update()
        {
            // being barriered is also a state you must FEEL from inside
            if (GetComponent<SimpleFPSController>() != null)
                GrammarFieldHUD.Inside(new Color(0.6f, 0.9f, 1f, 1f));

            _left -= Time.deltaTime;
            if (_left <= 0f)
            {
                if (_shell != null) Destroy(_shell.gameObject);
                Destroy(this);
            }
        }
    }

    /// "You are INSIDE something" — the screen answer (Marko: "make sure users
    /// understand that they're inside, not just when they leave"). Any field
    /// standing on the local player pings this each tick; while pinged, the
    /// screen edges glow and pulse in the field's colour. No words — the
    /// colour IS the message (localization rule: teach via images).
    public class GrammarFieldHUD : MonoBehaviour
    {
        static GrammarFieldHUD _i;
        Color _color;
        float _until;

        public static void Inside(Color c)
        {
            if (_i == null)
            {
                var go = new GameObject("GrammarFieldHUD");
                DontDestroyOnLoad(go);
                _i = go.AddComponent<GrammarFieldHUD>();
            }
            _i._color = c;
            _i._until = Time.time + 0.55f; // outlives one tick — no flicker
        }

        void OnGUI()
        {
            if (Time.time > _until) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4.2f);
            float w = Screen.width, h = Screen.height;
            var c = _color;

            // soft full-screen wash — presence, not blindness
            GUI.color = new Color(c.r, c.g, c.b, 0.05f + 0.03f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);

            // glowing edges, two strips each for a cheap gradient
            float e1 = h * 0.045f, e2 = h * 0.10f;
            GUI.color = new Color(c.r, c.g, c.b, 0.28f + 0.12f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, w, e1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, h - e1, w, e1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, 0f, e1, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(w - e1, 0f, e1, h), Texture2D.whiteTexture);
            GUI.color = new Color(c.r, c.g, c.b, 0.12f + 0.06f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, w, e2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, h - e2, w, e2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, 0f, e2, h), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(w - e2, 0f, e2, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
