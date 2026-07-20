using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellyZombie
{
    /// GRAMMAR v4 P3 — the EXOTICS (Marko's cross matrix): authored products
    /// for specific essence×behavior pairs, dispatched from the exotics table.
    /// Every entity here is deliberately small; the grammar carries the rest.
    public static class Exotics
    {
        /// One doorway from the particle law: two particles combined into an
        /// exotic — spawn its product and return the thing the runes should
        /// WAIT on (sustain law). Null = instantaneous, runes re-arm at once.
        public static Object Cast(RuneGrammar.ExoticKind kind, SpellParticle a, SpellParticle b,
            Vector3 at, float power)
        {
            switch (kind)
            {
                case RuneGrammar.ExoticKind.Healing: return HealingField.Open(at, power);
                case RuneGrammar.ExoticKind.SunStrike: return SunStrike.Open(at, power);
                case RuneGrammar.ExoticKind.DarkFlames: return DarkFlame.Open(at, power);
                case RuneGrammar.ExoticKind.StickyLava: return StickyLavaField.Open(at, power);
                case RuneGrammar.ExoticKind.FireBolts: return ElementBolt.Volley(at, power, hot: true);
                case RuneGrammar.ExoticKind.IceBolts: return ElementBolt.Volley(at, power, hot: false);
                case RuneGrammar.ExoticKind.ObsidianBlade: return ObsidianBlade.Conjure(at);
                case RuneGrammar.ExoticKind.AbsoluteZero: return AbsoluteZeroField.Open(at, power);
                case RuneGrammar.ExoticKind.DarkMatter: return DarkMatterMote.Open(at, power);
                case RuneGrammar.ExoticKind.StickyLight: return StickyLightMote.Open(at, power);
                case RuneGrammar.ExoticKind.SlickLight: return SlickLightMote.Open(at, power);
                case RuneGrammar.ExoticKind.Multiplication: return MultiplicationMote.Open(at, power);
                case RuneGrammar.ExoticKind.TeleportPair: return TeleportMote.OpenPair(at, power);
            }
            return null;
        }

        public static GameObject Glow(Vector3 at, float scale, Color c, MoteShade shade)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = at;
            go.transform.localScale = Vector3.one * scale;
            go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(c, shade);
            return go;
        }
    }

    /// HeatUp + Light — SUN STRIKE: a telegraphed ring, a heartbeat to dodge,
    /// then the sky's judgment on that exact spot (Marko: "can be dodged by
    /// walking and is channeling").
    public class SunStrike : MonoBehaviour
    {
        public float Power = 1f;
        const float ChannelSeconds = 1.7f;
        const float BlastRadius = 2.4f;
        float _age;
        Light _glow;
        Transform _ring;

        public static SunStrike Open(Vector3 at, float power)
        {
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 30f))
                at = hit.point;
            var go = new GameObject("SunStrike");
            go.transform.position = at;
            var s = go.AddComponent<SunStrike>();
            s.Power = power;
            DrawingWorld.Instance?.LogEvent("the sun takes AIM — move!");
            return s;
        }

        void Start()
        {
            _ring = GrammarFX.GroundRing(transform, new Color(1f, 0.85f, 0.4f));
            _ring.localScale = Vector3.one * BlastRadius;
            var lgo = new GameObject("Gather");
            lgo.transform.SetParent(transform, false);
            lgo.transform.localPosition = Vector3.up * 6f;
            _glow = lgo.AddComponent<Light>();
            _glow.type = LightType.Point; _glow.range = 14f; _glow.color = new Color(1f, 0.9f, 0.6f);
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_glow != null) _glow.intensity = Mathf.Lerp(0.5f, 9f, _age / ChannelSeconds);
            if (_age < ChannelSeconds) return;

            // THE STRIKE — a column of sun on whoever stayed
            Juice.Boom(transform.position, 1f);
            ZombieBrain.ScareVisible(transform.position, 18f, 6f);
            int n = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.8f, BlastRadius,
                GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null) continue;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null) { pl.TakeHit(Vector3.up * 3f, 35f * Power, "sun strike"); continue; }
                var d = c.GetComponentInParent<Damageable>();
                if (d != null) d.TakeDamage(85f * Power, "sun-struck");
                SpellParticle.GiveHeatTo(c, 160f * Power);
            }
            var flash = Exotics.Glow(transform.position + Vector3.up * 1f, BlastRadius * 2f,
                new Color(1f, 0.93f, 0.6f, 0.9f), MoteShade.Additive);
            Destroy(flash, 0.35f);
            Destroy(gameObject);
        }
    }

    /// HeatUp + Darkness — DARK FLAMES: near-black fire that hunts the
    /// nearest MOVING thing (anything that moves — even an object).
    public class DarkFlame : MonoBehaviour
    {
        public float Power = 1f;
        float _age, _retarget;
        Transform _prey;

        public static DarkFlame Open(Vector3 at, float power)
        {
            DarkFlame first = null;
            for (int i = 0; i < 2; i++)
            {
                var go = Exotics.Glow(at + Random.insideUnitSphere * 0.3f, 0.34f,
                    new Color(0.12f, 0.02f, 0.1f, 0.92f), MoteShade.Transparent);
                go.name = "DarkFlame";
                var f = go.AddComponent<DarkFlame>();
                f.Power = power;
                if (first == null) first = f;
            }
            DrawingWorld.Instance?.LogEvent("the fire goes HUNGRY and dark");
            return first;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > 9f) { Destroy(gameObject); return; }

            _retarget -= dt;
            if (_retarget <= 0f)
            {
                _retarget = 0.6f;
                _prey = null;
                float best = 14f * 14f;
                foreach (var z in Zombie.All) // moving zombies
                {
                    if (z == null) continue;
                    var rb = z.GetComponent<Rigidbody>();
                    if (rb != null && rb.linearVelocity.sqrMagnitude < 0.4f) continue;
                    float d = (z.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _prey = z.transform; }
                }
                foreach (var p in SimpleFPSController.All) // moving wizards
                {
                    if (p == null || p.Velocity.sqrMagnitude < 0.4f) continue;
                    float d = (p.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _prey = p.transform; }
                }
                // "anything that moves — EVEN AN OBJECT" (Marko): rolling
                // crates, thrown ores, tumbling matter — all legitimate prey
                int n = Physics.OverlapSphereNonAlloc(transform.position, 14f,
                    GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = GrammarFX.ScanBuffer[i];
                    var rb = c != null ? c.attachedRigidbody : null;
                    if (rb == null || rb.isKinematic || rb.linearVelocity.sqrMagnitude < 0.4f) continue;
                    float d = (rb.worldCenterOfMass - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _prey = rb.transform; }
                }
            }
            if (_prey == null) return;
            Vector3 to = _prey.position + Vector3.up * 0.7f - transform.position;
            transform.position += to.normalized * 3.6f * dt;
            if (to.sqrMagnitude < 0.45f)
            {
                SpellParticle.GiveHeatTo(_prey.GetComponentInChildren<Collider>(), 95f * Power);
                var pl = _prey.GetComponent<SimpleFPSController>();
                if (pl != null) pl.TakeHit(Vector3.zero, 9f * Power, "dark flames");
                Destroy(gameObject);
            }
        }
    }

    /// HeatUp + Sticky — STICKY LAVA: a patch that slows and burns whoever
    /// wades through it.
    public class StickyLavaField : GrammarField
    {
        public static StickyLavaField Open(Vector3 at, float power)
        {
            var f = Spawn<StickyLavaField>(at, power, 2.1f, 9f,
                new Color(0.85f, 0.3f, 0.05f, 0.5f), MoteShade.Additive);
            DrawingWorld.Instance?.LogEvent("the lava CLINGS");
            return f;
        }

        protected override float TickPeriod => 0.3f;

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                Vector3 v = pl.Velocity; v.y = 0f;
                pl.AddSpellForce(-v * 2.5f, dt);       // wading through tar
                pl.TakeHit(Vector3.zero, 1.2f * Power, "sticky lava"); // that is on fire
                return;
            }
            var cr = c.GetComponentInParent<Creature>();
            if (cr != null) { cr.ApplyStuck(0.6f); SpellParticle.GiveHeatTo(c, 30f * Power); }
        }
    }

    /// Fire bolts / ice bolts — a volley with NO targeting: they fly where
    /// they fly (Marko: "randomly flying off, not looking for a target").
    public class ElementBolt : MonoBehaviour
    {
        public bool Hot = true;
        public float Power = 1f;
        Vector3 _vel;
        float _age;

        public static ElementBolt Volley(Vector3 at, float power, bool hot)
        {
            ElementBolt first = null;
            for (int i = 0; i < 5; i++)
            {
                var go = Exotics.Glow(at, 0.16f,
                    hot ? new Color(1f, 0.5f, 0.1f) : new Color(0.6f, 0.85f, 1f), MoteShade.Additive);
                go.name = hot ? "FireBolt" : "IceBolt";
                var bolt = go.AddComponent<ElementBolt>();
                bolt.Hot = hot;
                bolt.Power = power;
                bolt._vel = (Random.onUnitSphere + Vector3.up * 0.4f).normalized * Random.Range(8f, 12f);
                if (first == null) first = bolt;
            }
            return first;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > 3f) { Destroy(gameObject); return; }
            _vel += Physics.gravity * 0.25f * dt; // a lazy arc
            transform.position += _vel * dt;

            if (Physics.SphereCast(transform.position - _vel.normalized * 0.3f, 0.14f,
                _vel.normalized, out var hit, _vel.magnitude * dt + 0.3f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                var pl = hit.collider.GetComponent<SimpleFPSController>();
                if (pl != null) pl.TakeHit(_vel.normalized * 2f, (Hot ? 6f : 3f) * Power,
                    Hot ? "fire bolt" : "ice bolt");
                else
                {
                    SpellParticle.GiveHeatTo(hit.collider, (Hot ? 60f : -60f) * Power);
                    var cr = hit.collider.GetComponentInParent<Creature>();
                    if (cr != null && !Hot) cr.ApplyStuck(0.7f); // cold slows
                }
                Destroy(gameObject);
            }
        }
    }

    /// HeatDown + Sticky — THE OBSIDIAN BLADE (Marko's matrix): frozen grip
    /// hardens into a black glass edge you can PICK UP and swing (E takes it,
    /// LMB swings). It crumbles when the magic runs out.
    public class ObsidianBlade : HeldWeapon
    {
        static readonly Quaternion RestRot = Quaternion.Euler(-42f, 0f, 0f); // held UP and ready
        static readonly Quaternion ChopRot = Quaternion.Euler(82f, 0f, 0f);  // rotated FORWARD

        Transform _pivot;
        float _swing;   // 0 = raised · 1 = fully chopped
        bool _chopping;

        /// The edge reads this: a chop cuts DEEP, a mere touch still cuts.
        public bool Swinging => _chopping || _swing > 0.25f;

        public static ObsidianBlade Conjure(Vector3 at)
        {
            var root = new GameObject("ObsidianBlade");
            root.transform.position = at + Vector3.up * 0.75f;

            // everything hangs off a PIVOT so the chop is one localRotation
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localRotation = RestRot;

            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = "Edge";
            edge.transform.SetParent(pivot, false);
            edge.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            edge.transform.localScale = new Vector3(0.045f, 0.62f, 0.11f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { color = new Color(0.06f, 0.05f, 0.08f) };
            m.SetFloat("_Smoothness", 0.85f); // black GLASS
            edge.GetComponent<Renderer>().sharedMaterial = m;

            // SHARP BY NATURE (Marko's rule): the edge itself cuts whatever
            // touches it — trigger box + kinematic body so contact events fire
            // while the blade sweeps
            var edgeCol = edge.GetComponent<BoxCollider>();
            edgeCol.isTrigger = true;
            edgeCol.size = new Vector3(1.5f, 1.05f, 1.5f); // a little proud of the glass
            var edgeRb = edge.AddComponent<Rigidbody>();
            edgeRb.isKinematic = true;
            edge.AddComponent<BladeEdge>();

            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "Grip";
            grip.transform.SetParent(pivot, false);
            grip.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            grip.transform.localScale = new Vector3(0.05f, 0.09f, 0.05f);
            grip.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.3f, 0.22f, 0.14f), MoteShade.Opaque);
            Object.Destroy(grip.GetComponent<Collider>()); // only the EDGE is sharp

            var bubble = root.AddComponent<SphereCollider>();
            bubble.isTrigger = true;
            bubble.radius = 0.9f;

            Juice.Crackle(at);
            DrawingWorld.Instance?.LogEvent("cold grips the fire's glass — OBSIDIAN BLADE (E to take)");
            root.AddComponent<BladeLifetime>(); // sidecar: HeldWeapon owns Update, so
                                                // the crumble clock lives beside it
            var blade = root.AddComponent<ObsidianBlade>();
            blade._pivot = pivot;
            return blade;
        }

        void Awake()
        {
            if (_pivot == null) _pivot = transform.Find("Pivot"); // prefab copies recover it
        }

        /// The edge reads these: who is holding it (never cut them) and
        /// whether it's held at all (a LOOSE blade never cuts players — you
        /// can't see an invisible hitbox bobbing in the dark).
        public SimpleFPSController Holder => Wielder;
        public bool IsHeld => Wielder != null;

        /// Base EquipTo puts every trigger to sleep (the pickup bubble) — but
        /// the EDGE must stay live: a held blade that can't cut isn't a blade.
        public override void EquipTo(SimpleFPSController player)
        {
            base.EquipTo(player);
            var edge = transform.Find("Pivot/Edge");
            if (edge != null)
            {
                var ec = edge.GetComponent<Collider>();
                if (ec != null) ec.enabled = true;
            }
        }

        protected override string SkinName => "ObsidianBlade"; // Marko's skin slot

        protected override void OnSkinApplied(Transform skin)
        {
            var pivot = FindPart("Pivot"); // his model's pivot takes over the chop
            if (pivot != null) _pivot = pivot;
        }

        protected override void UpdateArmed(Keyboard kb, Mouse mouse)
        {
            float dt = Time.deltaTime;

            // PRESS: the blade rotates FORWARD (the chop); it springs back UP
            // on its own — ready, strike, recover (Marko's animation ruling).
            // In engrave mode LMB is the PEN — no swinging while drawing.
            if (!DrawMode && mouse != null && mouse.leftButton.wasPressedThisFrame && !_chopping && _swing < 0.15f)
            {
                _chopping = true;
                Juice.Whoosh(transform.position);
            }
            _swing = Mathf.MoveTowards(_swing, _chopping ? 1f : 0f, dt * (_chopping ? 7f : 3.2f));
            if (_chopping && _swing >= 1f) _chopping = false;

            if (_pivot != null)
                _pivot.localRotation = Quaternion.Slerp(RestRot, ChopRot,
                    Mathf.SmoothStep(0f, 1f, _swing));
        }
    }

    /// The blade's cutting surface: SHARPNESS IS AUTOMATIC — anything touching
    /// the edge is cut (per-victim cooldown so contact isn't a blender). A
    /// deliberate chop cuts far deeper than a graze. It never cuts whoever is
    /// holding it — grip discipline is assumed.
    public class BladeEdge : MonoBehaviour
    {
        readonly System.Collections.Generic.Dictionary<Component, float> _lastCut =
            new System.Collections.Generic.Dictionary<Component, float>();
        ObsidianBlade _blade;

        void Awake() => _blade = GetComponentInParent<ObsidianBlade>();

        void OnTriggerStay(Collider other)
        {
            if (other.isTrigger || _blade == null) return;

            var touchedPilot = other.GetComponentInParent<SimpleFPSController>();

            // NEVER the wielder — the authoritative holder, not a parent guess
            // (this is what was cutting Marko while he swung his own blade)
            if (touchedPilot != null && touchedPilot == _blade.Holder) return;
            // a LOOSE blade never cuts players: nobody can see a bobbing
            // invisible hitbox in a dark cave. Zombies still step on it. Ha.
            if (touchedPilot != null && !_blade.IsHeld) return;

            Component victim = (Component)other.GetComponentInParent<Creature>()
                ?? (Component)touchedPilot
                ?? other.GetComponentInParent<Damageable>();
            if (victim == null) return;

            _lastCut.TryGetValue(victim, out float last);
            if (Time.time - last < 0.4f) return;
            _lastCut[victim] = Time.time;

            bool deep = _blade.Swinging;
            float dmg = deep ? 32f : 11f;

            if (victim is SimpleFPSController pl)
            {
                // yes, you can cut a FRIEND. friendly fire is a personality.
                pl.TakeHit((other.transform.position - transform.position).normalized * 2f,
                    dmg * 0.6f, "a friend's obsidian blade");
                return;
            }
            var d = victim.GetComponent<Damageable>();
            if (d == null) d = other.GetComponentInParent<Damageable>();
            if (d != null) d.TakeDamage(dmg, deep ? "obsidian chop" : "obsidian edge");
            if (deep && victim is Creature cr && Random.value < 0.35f) cr.KnockDown(0.6f);
        }
    }

    /// The obsidian blade's crumble clock — separate from HeldWeapon because
    /// the base class owns Update() (shadowing it would kill E-pickup).
    public class BladeLifetime : MonoBehaviour
    {
        float _dieAt; // absolute deadline — holstering must not pause the crumble

        void Awake() => _dieAt = Time.time + 30f; // "spell active + 5s" — greybox seal-length

        void OnEnable() { if (_dieAt > 0f && Time.time >= _dieAt) Crumble(); }

        void Update()
        {
            if (Time.time >= _dieAt) Crumble();
        }

        void Crumble()
        {
            GrammarFX.PuffBurst(transform.position, new Color(0.1f, 0.08f, 0.12f, 0.7f), 5);
            DrawingWorld.Instance?.LogEvent("the obsidian blade crumbles");
            Destroy(gameObject);
        }
    }

    /// HeatDown + Darkness — ABSOLUTE ZERO: everything inside freezes. Now.
    public class AbsoluteZeroField : GrammarField
    {
        public static AbsoluteZeroField Open(Vector3 at, float power)
        {
            var f = Spawn<AbsoluteZeroField>(at, power, 2.6f, 4.5f,
                new Color(0.5f, 0.7f, 1f, 0.3f), MoteShade.Transparent);
            DrawingWorld.Instance?.LogEvent("ABSOLUTE ZERO — nothing moves here");
            return f;
        }

        protected override float TickPeriod => 0.2f;

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null) { pl.TakeHit(Vector3.zero, 2.5f * Power, "absolute zero"); return; }
            SpellParticle.GiveHeatTo(c, -260f * Power); // instant freeze territory
        }
    }

    /// Darkness + Dense — DARK MATTER: slow, heavy, deletes magic on contact
    /// and hits like a falling star when it finally arrives.
    public class DarkMatterMote : MonoBehaviour
    {
        public float Power = 1f;
        float _age, _tick;
        Transform _prey;

        public static DarkMatterMote Open(Vector3 at, float power)
        {
            var go = Exotics.Glow(at, 0.5f, new Color(0.04f, 0.02f, 0.08f, 0.97f), MoteShade.Transparent);
            go.name = "DarkMatter";
            var m = go.AddComponent<DarkMatterMote>();
            m.Power = power;
            DrawingWorld.Instance?.LogEvent("something HEAVY condenses in the dark");
            return m;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > 14f) { Destroy(gameObject); return; }

            _tick -= dt;
            if (_tick <= 0f)
            {
                _tick = 0.4f;
                _prey = null;
                float best = 20f * 20f;
                foreach (var z in Zombie.All)
                {
                    if (z == null) continue;
                    float d = (z.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _prey = z.transform; }
                }
                foreach (var p in SimpleFPSController.All)
                {
                    if (p == null) continue;
                    float d = (p.transform.position - transform.position).sqrMagnitude;
                    if (d < best) { best = d; _prey = p.transform; }
                }

                // it EATS magic: any particle it overlaps is simply unmade
                int n = Physics.OverlapSphereNonAlloc(transform.position, 1.1f,
                    GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < n; i++)
                {
                    var sp = GrammarFX.ScanBuffer[i] != null
                        ? GrammarFX.ScanBuffer[i].GetComponent<SpellParticle>() : null;
                    if (sp != null) Destroy(sp.gameObject);
                }
            }

            if (_prey != null)
                transform.position += (_prey.position + Vector3.up * 0.7f - transform.position).normalized
                    * 0.85f * dt; // slow. inevitable.

            if (_prey != null && (_prey.position + Vector3.up * 0.7f - transform.position).sqrMagnitude < 0.5f)
            {
                var pl = _prey.GetComponent<SimpleFPSController>();
                if (pl != null) pl.TakeHit(Vector3.up * 2f, 25f * Power);
                else _prey.GetComponentInParent<Damageable>()?.TakeDamage(70f * Power, "dark matter");
                Juice.Boom(transform.position, 0.6f);
                Destroy(gameObject);
            }
        }
    }

    /// Light + Sticky — STICKY LIGHT: stands perfectly still, holds what
    /// touches it, and zombies CANNOT resist coming to look (the lure).
    public class StickyLightMote : MonoBehaviour
    {
        float _age, _lureTick;

        public static StickyLightMote Open(Vector3 at, float power)
        {
            var go = Exotics.Glow(at + Vector3.up * 0.9f, 0.4f,
                new Color(1f, 0.96f, 0.75f), MoteShade.Additive);
            go.name = "StickyLight";
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 9f; l.intensity = 2.4f;
            l.color = new Color(1f, 0.95f, 0.7f);
            var trig = go.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = 0.7f;
            DrawingWorld.Instance?.LogEvent("a light that HOLDS — moths incoming");
            return go.AddComponent<StickyLightMote>();
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_age > 14f) { Destroy(gameObject); return; }
            _lureTick -= Time.deltaTime;
            if (_lureTick <= 0f)
            {
                _lureTick = 1.1f;
                WorldEvents.Report(WorldEventKind.Spell, transform.position, 1.4f); // come look…
            }
        }

        void OnTriggerStay(Collider other)
        {
            var cr = other.GetComponentInParent<Creature>();
            if (cr != null) cr.ApplyStuck(1.2f); // …and STAY
        }
    }

    /// Light + Slick — SLICK LIGHT: an uncatchable glare ricocheting through
    /// the dark, blinding and bowling over whatever it clips.
    public class SlickLightMote : MonoBehaviour
    {
        public float Power = 1f;
        Vector3 _vel;
        float _age;

        public static SlickLightMote Open(Vector3 at, float power)
        {
            var go = Exotics.Glow(at + Vector3.up * 0.8f, 0.28f,
                new Color(1f, 0.97f, 0.8f), MoteShade.Additive);
            go.name = "SlickLight";
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 6f; l.intensity = 1.8f;
            var m = go.AddComponent<SlickLightMote>();
            m.Power = power;
            m._vel = (Random.onUnitSphere + Vector3.up * 0.1f).normalized * 6.5f;
            return m;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > 8f) { Destroy(gameObject); return; }

            if (Physics.SphereCast(transform.position, 0.25f, _vel.normalized, out var hit,
                _vel.magnitude * dt + 0.25f, ~0, QueryTriggerInteraction.Ignore))
            {
                var pl = hit.collider.GetComponent<SimpleFPSController>();
                if (pl != null) pl.KnockDown(0.8f);
                var cr = hit.collider.GetComponentInParent<Creature>();
                if (cr != null) { cr.ApplyBlind(2.2f); cr.KnockDown(0.7f); }
                _vel = Vector3.Reflect(_vel, hit.normal); // cannot be caught
                transform.position = hit.point + hit.normal * 0.3f;
            }
            else transform.position += _vel * dt;
        }
    }

    /// Light + Dense — MULTIPLICATION: whatever magic it touches, there is
    /// suddenly MORE of. It ignores its own kind (no infinite mirrors), and
    /// yes — it can clone a zombie. Marko asked for mayhem.
    public class MultiplicationMote : MonoBehaviour
    {
        float _age, _cooldown;
        int _clones;

        public static MultiplicationMote Open(Vector3 at, float power)
        {
            var go = Exotics.Glow(at + Vector3.up * 0.8f, 0.3f,
                new Color(0.85f, 1f, 0.95f), MoteShade.Additive);
            go.name = "MultiplicationMote";
            var trig = go.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = 0.5f;
            DrawingWorld.Instance?.LogEvent("the light learns to COUNT");
            return go.AddComponent<MultiplicationMote>();
        }

        void Update()
        {
            _age += Time.deltaTime;
            _cooldown -= Time.deltaTime;
            transform.position += Vector3.up * Mathf.Sin(_age * 2f) * 0.003f; // hover
            if (_age > 9f || _clones >= 4) Destroy(gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            if (_cooldown > 0f) return;

            var sp = other.GetComponent<SpellParticle>();
            if (sp != null && sp.Kind != ParticleKind.BarrierMote)
            {
                _cooldown = 0.4f;
                _clones++;
                var twin = SpellParticle.Emit(sp.Kind, sp.transform.position + Random.insideUnitSphere * 0.3f,
                    Random.onUnitSphere, sp.Power);
                twin.Temp = sp.Temp; twin.Lum = sp.Lum; twin.Density = sp.Density; twin.Stick = sp.Stick;
                twin.Lineage = sp.Lineage;
                twin.SealId = sp.SealId;
                return;
            }

            var z = other.GetComponentInParent<Zombie>();
            if (z != null && z.GetComponent<Demon>() == null)
            {
                _cooldown = 1.2f;
                _clones += 2; // cloning the dead is expensive arithmetic
                Zombie.Spawn(z.transform.position + Random.insideUnitSphere * 1f + Vector3.up * 0.3f,
                    ZombieKind.Walker, 1f);
                DrawingWorld.Instance?.LogEvent("…now there are TWO of it. you did this.");
            }
        }
    }

    /// Slick + Dense — the TELEPORT PAIR: two linked motes fly apart; touch
    /// one, arrive at the other. Doors are a suggestion now.
    public class TeleportMote : MonoBehaviour
    {
        public TeleportMote Partner;
        Vector3 _vel;
        float _age, _cooldown;

        public static TeleportMote OpenPair(Vector3 at, float power)
        {
            Vector3 dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.2f;
            var a = One(at, dir.normalized * 5f);
            var b = One(at, -dir.normalized * 5f);
            a.Partner = b;
            b.Partner = a;
            DrawingWorld.Instance?.LogEvent("space folds — a DOOR PAIR flies apart");
            return a;
        }

        static TeleportMote One(Vector3 at, Vector3 vel)
        {
            var go = Exotics.Glow(at + Vector3.up * 0.8f, 0.34f,
                new Color(0.7f, 0.4f, 1f, 0.9f), MoteShade.Additive);
            go.name = "TeleportMote";
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 5f; l.intensity = 1.6f;
            l.color = new Color(0.7f, 0.45f, 1f);
            var trig = go.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = 0.55f;
            var m = go.AddComponent<TeleportMote>();
            m._vel = vel;
            return m;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            _cooldown -= dt;
            if (_age > 12f) { Destroy(gameObject); return; }
            if (_age < 1.3f) transform.position += _vel * dt; // fly apart, then hold
        }

        void OnTriggerEnter(Collider other)
        {
            if (Partner == null || _cooldown > 0f || other.isTrigger) return;
            if (other.GetComponent<SpellParticle>() != null) return; // magic keeps its feet

            Vector3 dest = Partner.transform.position;
            var pl = other.GetComponentInParent<SimpleFPSController>();
            if (pl != null)
            {
                var cc = pl.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                pl.transform.position = dest + Vector3.up * 0.4f;
                if (cc != null) cc.enabled = true;
                pl.CancelMomentum();
            }
            else
            {
                var rb = other.attachedRigidbody;
                if (rb == null || rb.isKinematic) return;
                rb.position = dest + Vector3.up * 0.4f;
            }
            _cooldown = 0.6f;
            Partner._cooldown = 0.6f; // no ping-pong loops
            Juice.Chime(dest);
        }

        void OnDestroy()
        {
            if (Partner != null) Partner.Partner = null;
        }
    }
}
