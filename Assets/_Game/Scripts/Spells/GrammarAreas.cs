using UnityEngine;

namespace SpellyZombie
{
    /// Area-effect fields for rune combinations (SPELL_PARTICLES.md). Numbers live in DrawingConfig.
    public static class GrammarFX
    {
        static readonly Collider[] _hits = new Collider[48];

        public static Collider[] ScanBuffer => _hits;

        /// Shared soft-sphere visual for every field; alpha kept low so it reads from inside.
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

        /// Bright ground circle marking a field's boundary.
        public static Transform GroundRing(Transform parent, Color c)
        {
            var go = new GameObject("AreaRing");
            go.transform.SetParent(parent, false);

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

        /// Spawns one fire mote sphere.
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

        /// A gravity-free fling of fire motes - the shared bloom look.
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

        /// Spark lvl3 - FLAME BURST: flames burst across the area, once.
        public static void FlameBurst(Vector3 at, float power)
        {
            float r = DrawingConfig.UltimateRadius;
            Juice.Boom(at, 0.7f);
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.FireBurst, at);
            WorldEvents.Report(WorldEventKind.Explosion, at, 2f);
            DrawingWorld.Instance?.LogEvent("FLAME BURST");
            int n = Physics.OverlapSphereNonAlloc(at, r, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i];
                if (c == null) continue;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null) { pl.TakeHit((pl.transform.position - at).normalized * 9f, 28f * power); continue; }
                SpellParticle.GiveHeatTo(c, 200f * power); // combinations heat harder than single runes
                var rb = c.attachedRigidbody;
                if (rb != null) rb.AddForce((rb.worldCenterOfMass - at).normalized * 9f, ForceMode.VelocityChange);
            }
            FireBloom(at, 10, 4f, 2f);
        }
    }

    /// Base class for timed fields: a soft sphere that ticks an effect on
    /// everything inside, then fades. Subclasses implement one Affect().
    public abstract class GrammarField : MonoBehaviour
    {
        public float Power = 1f;
        public float Radius = 3.5f;
        public float Seconds = 5f;
        public Color Tint;      // full-strength field colour - feeds the inside-HUD

        protected Transform Ball;
        /// Authored FX skin; code must not add art inside it or change its scale.
        [System.NonSerialized] public bool HasSkin;
        [System.NonSerialized] public Transform Skin;
        [System.NonSerialized] public Vector3 SkinBase = Vector3.one;
        /// Skin scale with the field; default is the dome diameter.
        protected virtual Vector3 SkinShape => Vector3.one * Radius * 2f;
        Transform _ring;        // the ground boundary circle
        float _age, _tick;
        static readonly System.Collections.Generic.HashSet<Component> _seenRoots =
            new System.Collections.Generic.HashSet<Component>(); // per-tick body dedupe

        protected virtual float TickPeriod => 0.35f;
        protected abstract void Affect(Collider c, float dt);
        protected virtual void Grow(float dt) { }
        protected virtual void ShapeBall() { if (Ball != null) Ball.localScale = Vector3.one * Radius * 2f; }

        /// False hides the dome sphere; zone, ring and inside-HUD still work.
        protected virtual bool ShowDome => true;

        /// Whether the field affects this player; asked before the inside-HUD pulse.
        protected virtual bool AffectsPlayer(SimpleFPSController p) => true;

        public void ShowGroundRing(bool on)
        {
            if (_ring != null) _ring.gameObject.SetActive(on);
        }
        protected virtual void OnExpire() { }

        protected void Extend(float seconds)
        {
            Seconds = Mathf.Max(Seconds, seconds);
            _age = 0f;
        }

        /// Radius scales by SizeMul(size); size 0 leaves the radius unchanged.
        protected static T Spawn<T>(Vector3 at, float power, float radius, float seconds, Color c, MoteShade shade,
            float size = 0f)
            where T : GrammarField
        {
            radius *= SpellParticle.SizeMul(size);
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

            // a prefab named FX_<FieldClass> in Resources/Custom replaces the code dome; ring + HUD stay
            var skin = PrefabVault.Get("FX_" + typeof(T).Name);
            if (skin != null)
            {
                f.Skin = Object.Instantiate(skin, go.transform, false).transform;
                f.SkinBase = f.Skin.localScale;
                f.HasSkin = true;                 // fields must not build code art over it
                var dome = f.Ball.GetComponent<Renderer>();
                if (dome != null) dome.enabled = false;
            }
            else if (FxLibrary.I != null)
            {
                // rides the field for its whole life; an FX_<FieldClass> override wins instead
                var jmo = FxLibrary.I.FieldFor(typeof(T).Name);
                if (jmo != null) FxLibrary.Spawn(jmo, at, go.transform, seconds + 0.5f);
            }

            if (!f.ShowDome)
            {
                var bare = f.Ball.GetComponent<Renderer>();
                if (bare != null) bare.enabled = false;
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
            if (Skin != null) Skin.localScale = Vector3.Scale(SkinBase, SkinShape);

            _tick -= dt;
            if (_tick <= 0f)
            {
                _tick = TickPeriod;
                // Collide, not Ignore: spell particles are trigger spheres
                int n = Physics.OverlapSphereNonAlloc(transform.position, Radius,
                    GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Collide);
                _seenRoots.Clear();
                for (int i = 0; i < n; i++)
                {
                    var c = GrammarFX.ScanBuffer[i];
                    if (c == null) continue;
                    // one body = one tick: rigs are many limb colliders; dedupe by root
                    Component root = (Component)c.GetComponentInParent<SimpleFPSController>()
                        ?? (Component)c.GetComponentInParent<Creature>()
                        ?? (Component)c.GetComponent<Matter>()
                        ?? (Component)c.attachedRigidbody;
                    if (root != null && !_seenRoots.Add(root)) continue;
                    // standing in a field pulses the screen edges in its colour
                    if (root is SimpleFPSController pilotRoot)
                    {
                        // pulse only when the field actually affects this player
                        if (!AffectsPlayer(pilotRoot)) continue;
                        GrammarFieldHUD.Inside(Tint);
                        // hand fields the pilot's capsule; a limb bone would miss GetComponent checks
                        var pilotCC = pilotRoot.GetComponent<CharacterController>();
                        if (pilotCC != null) c = pilotCC;
                    }
                    Affect(c, TickPeriod);
                }
            }

            if (_age >= Seconds) { OnExpire(); Destroy(gameObject); }
        }
    }

    /// Poison gas zone. Three uses: the cloud a zombie breathes, the bigger
    /// one a detonation leaves, and the one clinging to a body.
    public class PoisonField : GrammarField
    {
        /// Body wearing this cloud; it never poisons its own host.
        [System.NonSerialized] public Transform Wearer;

        static readonly Color Sick = new Color(0.55f, 0.85f, 0.25f);

        /// Gas is drawn by the CFXR cloud; the dome stays hidden.
        protected override bool ShowDome => false;

        const float PuffLife = 2.8f;
        float _puffIn;
        bool _firstPuff = true;

        /// Live poison zone count; caps total particle cost.
        static int _liveFields;
        void OnEnable() => _liveFields++;
        void OnDisable() => _liveFields--;

        protected override void Grow(float dt)
        {
            if ((_puffIn -= dt) > 0f) return;
            if (FxLibrary.I == null) return;

            // a possessed zombie emits no puffs so the driver can see
            if (Wearer != null)
            {
                var host = Wearer.GetComponent<Zombie>();
                if (host != null && host.Possessed) return;
            }

            // one puff per tick, never a burst - FxLibrary drops spawns past its
            // per-frame budget. Cadence stretches with _liveFields so total smoke stays roughly fixed.
            float crowd = Mathf.Max(1f, _liveFields / DrawingConfig.PoisonFxCrowd);
            _puffIn = DrawingConfig.PoisonPuffEvery * crowd;

            // skip puffs farther than PoisonFxDistance from the camera
            var eye = Camera.main;
            if (eye != null)
            {
                float far = DrawingConfig.PoisonFxDistance;
                if ((eye.transform.position - transform.position).sqrMagnitude > far * far)
                    return;
            }

            // scatter through the sphere; the first puff lands centre so a new cloud shows at once
            Vector3 spot = transform.position;
            if (!_firstPuff) spot += Random.insideUnitSphere * Radius * 0.6f;
            _firstPuff = false;

            var fx = FxLibrary.Spawn(FxLibrary.I.GasCloud, spot, null, PuffLife);
            // the prefab emits 2-3 UNIT particles, so metres need converting
            if (fx != null)
                fx.transform.localScale = Vector3.one *
                    Mathf.Max(0.05f, Radius * DrawingConfig.PoisonFxScale);
        }

        /// ring: ground circle, on for detonations, off for body-carried gas.
        public static PoisonField Open(Vector3 at, float radius, float seconds,
            Transform rideOn = null, bool ring = false)
        {
            var f = Spawn<PoisonField>(at, 1f, radius, seconds, Sick, MoteShade.Transparent);
            f.ShowGroundRing(ring);
            // stagger so simultaneous clouds don't starve each other
            f._puffIn = Random.value * DrawingConfig.PoisonPuffEvery;
            if (rideOn != null)
            {
                f.transform.SetParent(rideOn, true);
                f.Wearer = rideOn;
            }
            return f;
        }

        /// Acolytes are immune; asked before the pulse so they get no edges either.
        protected override bool AffectsPlayer(SimpleFPSController p) =>
            !Sides.IsAcolytePlayer(p);

        protected override void Affect(Collider c, float dt)
        {
            var p = c.GetComponentInParent<SimpleFPSController>();
            if (p == null || p.IsDowned) return;
            if (Wearer != null && p.transform == Wearer) return;  // your own cloud
            if (!AffectsPlayer(p)) return;   // one predicate, asked here and by the HUD

            p.TakeHit(Vector3.zero, DrawingConfig.PoisonDamage * dt, "the corruption");
            Cling(p, dt);
        }

        /// Attaches a small PoisonField to the victim's head; it grows with
        /// exposure and poisons others in turn.
        static void Cling(SimpleFPSController victim, float dt)
        {
            var worn = victim.GetComponentInChildren<PoisonField>();
            if (worn == null)
            {
                // on the head, small, visible to other players
                PoisonField.Open(victim.transform.position + Vector3.up * 1.6f,
                    DrawingConfig.PoisonClingRadius,
                    DrawingConfig.PoisonClingSeconds, victim.transform);
                return;
            }
            worn.Radius = Mathf.Min(worn.Radius + DrawingConfig.PoisonClingGrow * dt,
                DrawingConfig.PoisonClingMax);
            worn.Extend(DrawingConfig.PoisonClingSeconds);
        }
    }

    public class SnowField : GrammarField
    {
        float _burstTick;

        public static SnowField Open(Vector3 at, float power, float size = 0f) =>
            Spawn<SnowField>(at, power, DrawingConfig.UltimateRadius, DrawingConfig.UltimateSeconds * 1.3f,
                new Color(0.85f, 0.93f, 1f, 0.35f), MoteShade.Transparent, size);

        protected override void Grow(float dt)
        {
            // ice bursts flurry across the area
            _burstTick -= dt;
            if (_burstTick > 0f || FxLibrary.I == null) return;
            _burstTick = 0.9f;
            Vector2 r = Random.insideUnitCircle * Radius * 0.8f;
            FxLibrary.Spawn(FxLibrary.I.IceHit, transform.position + new Vector3(r.x, 0.4f, r.y));
        }

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            // snow only cools; being cold does the rest
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

    /// Heat lvl3 - flame vortex: everything inside is dragged around the axis,
    /// heated and lifted; fire climbs the column while it lives.
    public class FlameVortexField : GrammarField
    {
        float _fxTick;

        protected override bool ShowDome => false; // the fire visuals replace the dome

        public static FlameVortexField Open(Vector3 at, float power, float size = 0f)
        {
            var f = Spawn<FlameVortexField>(at, power, DrawingConfig.UltimateRadius * 0.8f,
                DrawingConfig.UltimateSeconds * 1.2f,
                new Color(1f, 0.5f, 0.12f, 0.5f), MoteShade.Additive, size);
            DrawingWorld.Instance?.LogEvent("the fire starts to SPIN");
            ZombieBrain.ScareVisible(at, 20f, 5f);
            return f;
        }

        float _spin;

        /// One flame: revolves around the vortex axis and climbs while it lives.
        class Orbiter : MonoBehaviour
        {
            public Transform Axis;
            void Update()
            {
                if (Axis == null) return;
                transform.RotateAround(Axis.position, Vector3.up, 240f * Time.deltaTime);
                transform.position += Vector3.up * 1.1f * Time.deltaTime;
            }
        }

        protected override void Grow(float dt)
        {
            // three arms of flame placed by one advancing angle
            _spin += dt * 4.2f;
            _fxTick -= dt;
            if (_fxTick > 0f) return;
            _fxTick = 0.09f;
            for (int arm = 0; arm < 3; arm++)
            {
                float a = _spin + arm * (Mathf.PI * 2f / 3f);
                float r = Radius * Random.Range(0.55f, 0.85f);
                Vector3 at = transform.position + new Vector3(
                    Mathf.Cos(a) * r, Random.Range(0.15f, 2.2f), Mathf.Sin(a) * r);
                var flame = GrammarFX.FireMote(at, Random.Range(0.22f, 0.45f), 0.85f);
                if (flame != null) flame.AddComponent<Orbiter>().Axis = transform;
            }
        }

        protected override void Affect(Collider c, float dt)
        {
            // the whirl: around the axis, inward a touch, upward a little
            Vector3 to = transform.position - c.transform.position;
            to.y = 0f;
            Vector3 spin = Vector3.Cross(Vector3.up, to).normalized;

            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                // fire only heats; the temperature band does the hurting
                BodyState.Of(pl)?.PushTemp(9f * Power);
                pl.TakeHit((spin * 5f + to.normalized * 1.5f + Vector3.up * 2f) * Power, 0f);
                return;
            }
            SpellParticle.GiveHeatTo(c, 55f * Power);
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
                rb.AddForce((spin * 7f + to.normalized * 2f + Vector3.up * 3.2f) * Power,
                    ForceMode.Acceleration);
        }
    }

    /// Light lvl3 - plasma: a small sun. Blinds, radiates heat, heavy contact damage.
    public class PlasmaField : GrammarField
    {
        Light _glow;

        public static PlasmaField Open(Vector3 at, float power, float size = 0f)
        {
            var f = Spawn<PlasmaField>(at + Vector3.up * 1.2f, power, 1.1f, DrawingConfig.UltimateSeconds,
                new Color(1f, 0.9f, 0.5f, 0.95f), MoteShade.Additive, size);
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
                board?.PushLum(1f); // the world whites out near it
                if (d < Radius * 1.6f)
                {
                    board?.PushTemp(50f * Power);
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
            SpellParticle.GiveHeatTo(c, 30f * Power);
        }

        void LateUpdate()
        {
            if (_glow != null) _glow.intensity = 8f + Mathf.PerlinNoise(Time.time * 7f, 0.3f) * 4f;
        }
    }

    /// Dark lvl2/lvl3 - black hole: pulls things in; the lvl3 version grows.
    public class BlackHoleField : GrammarField
    {
        public bool Growing;
        int _swallowed;

        public static BlackHoleField Open(Vector3 at, float power, bool growing, float size = 0f)
        {
            var f = Spawn<BlackHoleField>(at + Vector3.up * 0.8f, power,
                growing ? 2.2f : 1.8f,
                DrawingConfig.UltimateSeconds * (growing ? 1.4f : 0.9f),
                new Color(0.02f, 0.01f, 0.05f, 0.93f), MoteShade.Transparent, size);
            f.Growing = growing;
            // opaque void core rides the Ball so it grows with the field
            if (!f.HasSkin) // no code art inside an authored skin
            {
                var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                core.name = "VoidCore";
                Object.Destroy(core.GetComponent<Collider>());
                core.transform.SetParent(f.Ball, false);
                core.transform.localScale = Vector3.one * 0.42f;
                core.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(Color.black, MoteShade.Opaque);
            }
            DrawingWorld.Instance?.LogEvent(growing ? "the BLACK HOLE grows hungry" : "a BLACK HOLE opens in the dark");
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
            if (pl != null) // players get dragged, never swallowed
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

    /// Light+Dark paradox - WHITE HOLE: black hole strength, opposite sign.
    public class WhiteHoleField : GrammarField
    {
        public static WhiteHoleField Open(Vector3 at, float power, float size = 0f)
        {
            var f = Spawn<WhiteHoleField>(at + Vector3.up * 0.8f, power, 4f, DrawingConfig.UltimateSeconds * 0.9f,
                new Color(1f, 1f, 0.97f, 0.75f), MoteShade.Additive, size);
            if (FxLibrary.I != null) // ignition flash
                FxLibrary.Spawn(FxLibrary.I.Flash, at + Vector3.up * 0.8f);
            // one detonation the moment it ignites
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
            DrawingWorld.Instance?.LogEvent("light and dark make a WHITE HOLE");
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

    /// Glue lvl3 - TIME FREEZE: the area spreads and stops anything inside.
    public class TimeFreezeField : GrammarField
    {
        public static TimeFreezeField Open(Vector3 at, float power, float size = 0f)
        {
            var f = Spawn<TimeFreezeField>(at, power, DrawingConfig.UltimateRadius, DrawingConfig.UltimateSeconds * 0.8f,
                new Color(0.75f, 0.95f, 0.7f, 0.28f), MoteShade.Transparent, size);
            DrawingWorld.Instance?.LogEvent("absolute grip stops TIME here");
            return f;
        }

        protected override float TickPeriod => 0.15f;

        protected override void Affect(Collider c, float dt)
        {
            // players are stopped too
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
            if (p != null) p.Vel = Vector3.zero;
        }
    }

    /// Repel lvl3 - INERTIA: nothing inside can stand in place.
    public class InertiaField : GrammarField
    {
        public static InertiaField Open(Vector3 at, float power, float size = 0f)
        {
            var f = Spawn<InertiaField>(at, power, DrawingConfig.UltimateRadius, DrawingConfig.UltimateSeconds,
                new Color(0.9f, 0.9f, 1f, 0.22f), MoteShade.Transparent, size);
            DrawingWorld.Instance?.LogEvent("absolute slip, NOTHING stands still");
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

    /// Arrow+Arrow - tornado (lifts) · Y+Y - whirlpool (drags down). Carries
    /// particles and stamps its lineage into them; a thrown Arrow/Y steers it.
    public class TornadoField : GrammarField
    {
        public ulong FieldLineage;
        Vector3 _drift;
        bool _down;

        public static TornadoField Open(Vector3 at, float power, bool down, ulong lineage, float size = 0f)
        {
            var f = Spawn<TornadoField>(at + Vector3.up * (down ? 0.2f : 1.4f), power,
                2.4f, DrawingConfig.UltimateSeconds * 1.2f,
                down ? new Color(0.35f, 0.55f, 0.75f, 0.35f) : new Color(0.78f, 0.8f, 0.88f, 0.3f),
                MoteShade.Transparent, size);
            f._down = down;
            f.FieldLineage = lineage;
            // vector cubes parented to the Ball ride its spin
            if (!f.HasSkin) // skip when an FX_TornadoField skin owns the look
            {
                var vecMat = MatterFX.Get(down ? new Color(0.7f, 0.35f, 0.95f) : new Color(0.92f, 0.9f, 0.6f),
                MoteShade.Additive); // pull purple, push gold
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
            var lib = FxLibrary.I; // wind wears trails, water wears rings
            if (lib != null)
                FxLibrary.Spawn(down ? lib.Ripples : lib.WindTrails, f.transform.position, f.transform,
                    DrawingConfig.UltimateSeconds * 1.2f + 0.5f);
            DrawingWorld.Instance?.LogEvent(down ? "the pulls become a WHIRLPOOL" : "the winds become a TORNADO");
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
            transform.position += _drift * dt; // steered by thrown Arrows/Ys
            if (Ball != null) Ball.Rotate(0f, (_down ? -540f : 540f) * dt, 0f); // counter-spins the tornado
        }

        protected override void Affect(Collider c, float dt)
        {
            Vector3 rel = c.bounds.center - transform.position;
            rel.y = 0f;
            if (rel.sqrMagnitude < 0.01f) rel = Random.insideUnitSphere;
            // the whirlpool spins the opposite way to the tornado
            Vector3 tangent = Vector3.Cross(_down ? Vector3.down : Vector3.up, rel).normalized;
            Vector3 vertical = _down ? Vector3.down : Vector3.up;
            Vector3 swirl = tangent * 11f + vertical * 6.5f - rel.normalized * 3f;

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
                // carried; the spin stamps the vector into its lineage
                p.Lineage |= FieldLineage;
                p.Pull(c.bounds.center + swirl, dt * 1.6f);
                return;
            }

            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                pl.AddSpellForce(swirl * 3.2f * Power, dt);
                var board = BodyState.Of(pl);
                board?.PushMove((_down ? -0.5f : 0.5f) * dt); // tornado buffs move, whirlpool cuts it
                if (_down) // the whirlpool cancels your horizontal velocity
                {
                    Vector3 pv = pl.Velocity; pv.y = 0f;
                    pl.AddSpellForce(-pv * 1.8f, dt);
                }
                return;
            }

            var cr = c.GetComponentInParent<Creature>();
            if (cr != null && Random.value < 0.12f) cr.KnockDown(1f);
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
                rb.AddForce(swirl * 9f * Power, ForceMode.Acceleration);
        }

        /// On expiry the storm tosses everything out.
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

    /// HeatDown + Light - healing area: the only healing in the game.
    public class HealingField : GrammarField
    {
        public static HealingField Open(Vector3 at, float power, float size = 0f)
        {
            var f = Spawn<HealingField>(at, power, DrawingConfig.UltimateRadius * 0.8f,
                DrawingConfig.UltimateSeconds * 1.4f,
                new Color(0.85f, 1f, 0.8f, 0.4f), MoteShade.Additive, size);
            DrawingWorld.Instance?.LogEvent("cold light is MERCY, a healing ground");
            Juice.Chime(at);
            if (FxLibrary.I != null) // runic circle on the ground
                FxLibrary.Spawn(FxLibrary.I.RunicAura, at + Vector3.up * 0.1f, f.transform,
                    DrawingConfig.UltimateSeconds * 1.4f + 0.5f);
            return f;
        }

        protected override float TickPeriod => 0.3f;

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl == null || pl.IsDowned) return; // downed players are not healed
            // the heal spell stops at the side's own ceiling, same as mending
            pl.Health = Mathf.Min(Sides.MaxHealthFor(Grimoire.LocalPlayerId),
                pl.Health + 25f * Power * dt);
        }
    }

    /// Dense+Spread paradox - barrier: two-way isolation. Inside is protected
    /// and cannot act out; no particle donates through it, no combination inside it.
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
                if (FxLibrary.I != null)
                    FxLibrary.Spawn(FxLibrary.I.Shield, root.position, b._shell, 0f);
                DrawingWorld.Instance?.LogEvent("BARRIER: isolated (protected, and harmless)");
            }
            b._left = DrawingConfig.BarrierSeconds;
        }

        void Update()
        {
            // barrier pulses the inside-HUD too
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

    /// Fields ping this each tick while affecting the local player; the screen
    /// edges glow and pulse in the field's colour.
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
            _i._until = Time.time + 0.55f; // outlives one tick - no flicker
        }

        void OnGUI()
        {
            if (Time.time > _until) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4.2f);
            float w = Screen.width, h = Screen.height;
            var c = _color;

            // soft full-screen wash
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
