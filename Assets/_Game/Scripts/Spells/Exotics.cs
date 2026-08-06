using UnityEngine;

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
                case RuneGrammar.ExoticKind.LightStrike: return LightStrike.Open(at, power);
                case RuneGrammar.ExoticKind.DarkFlames: return DarkFlame.Open(at, power);
                case RuneGrammar.ExoticKind.StickyLava: return StickyLavaField.Open(at, power);
                case RuneGrammar.ExoticKind.FireBolts: return ElementBolt.Volley(at, power, hot: true);
                case RuneGrammar.ExoticKind.IceBolts: return ElementBolt.Volley(at, power, hot: false);
                case RuneGrammar.ExoticKind.FrostGlue: return FrostGlueField.Open(at, power);
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

    /// HeatUp + Light — LIGHT STRIKE (renamed Jul 22, Marko: Valve owns
    /// "Sun Strike" territory and we ship on Steam): a telegraphed ring, a
    /// heartbeat to dodge, then the sky's judgment on that exact spot.
    public class LightStrike : MonoBehaviour
    {
        public float Power = 1f;
        const float ChannelSeconds = 1.7f;
        const float BlastRadius = 2.4f;
        float _age;
        Light _glow;
        Transform _ring;

        public static LightStrike Open(Vector3 at, float power)
        {
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 30f))
                at = hit.point;
            var go = new GameObject("LightStrike");
            go.transform.position = at;
            var s = go.AddComponent<LightStrike>();
            s.Power = power;
            DrawingWorld.Instance?.LogEvent("the light AIMS here, move");
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
                if (pl != null) { pl.TakeHit(Vector3.up * 6f, 90f * Power, "light strike"); continue; }
                var d = c.GetComponentInParent<Damageable>();
                if (d != null) d.TakeDamage(220f * Power, "light-struck"); // JUDGMENT judges now
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
                float best = 14f * 14f;
                _prey = Targets.Nearest(transform.position, ref best,
                    includePlayers: true, movingOnly: true); // moving zombies + wizards
                // "anything that moves - EVEN AN OBJECT" (Marko): rolling
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
                SpellParticle.GiveHeatTo(_prey.GetComponentInChildren<Collider>(), 220f * Power);
                var pl = _prey.GetComponent<SimpleFPSController>();
                if (pl != null) pl.TakeHit(Vector3.zero, 24f * Power, "dark flames");
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
                var board = BodyState.Of(pl);
                board?.PushGrip(0.85f * Power); // the tar GRIPS — grip does the slowing
                board?.PushTemp(9f * Power);    // and it burns, via the band
                return;
            }
            var cr = c.GetComponentInParent<Creature>();
            if (cr != null) { cr.ApplyStuck(1.2f); SpellParticle.GiveHeatTo(c, 75f * Power); }
        }
    }

    /// HeatDown + Sticky — FROST GLUE (Marko Jul 22, the blade's replacement:
    /// "a glue that freezes... sticky lava is just a glue that burns - chill
    /// and heat can have the same logic"): a patch that grips and CHILLS.
    /// Whatever stays stuck is freezing toward the ice-block.
    public class FrostGlueField : GrammarField
    {
        public static FrostGlueField Open(Vector3 at, float power)
        {
            var f = Spawn<FrostGlueField>(at, power, 2.1f, 9f,
                new Color(0.55f, 0.78f, 1f, 0.5f), MoteShade.Additive);
            DrawingWorld.Instance?.LogEvent("the frost CLINGS");
            return f;
        }

        protected override float TickPeriod => 0.3f;

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                var board = BodyState.Of(pl);
                board?.PushGrip(0.85f * Power); // the frost GRIPS — grip does the slowing
                board?.PushTemp(-9f * Power);   // and it bites cold, via the band
                return;
            }
            var cr = c.GetComponentInParent<Creature>();
            if (cr != null) { cr.ApplyStuck(1.2f); SpellParticle.GiveHeatTo(c, -75f * Power); }
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
                // a bolt in the SHIN still counts (limb capsules lead in 3rd person)
                var pl = hit.collider.GetComponentInParent<SimpleFPSController>();
                if (pl != null) pl.TakeHit(_vel.normalized * 4f, (Hot ? 16f : 9f) * Power,
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


    /// HeatDown + Darkness — ABSOLUTE ZERO: everything inside freezes. Now.
    public class AbsoluteZeroField : GrammarField
    {
        public static AbsoluteZeroField Open(Vector3 at, float power)
        {
            var f = Spawn<AbsoluteZeroField>(at, power, 2.6f, 4.5f,
                new Color(0.5f, 0.7f, 1f, 0.3f), MoteShade.Transparent);
            DrawingWorld.Instance?.LogEvent("nothing moves in ABSOLUTE ZERO");
            return f;
        }

        protected override float TickPeriod => 0.2f;

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                var board = BodyState.Of(pl);
                board?.PushTemp(-20f * Power); // races toward frozen-solid — the band and the ice do the rest
                board?.PushLum(-0.6f);         // the dark of deep cold
                return;
            }
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
                float best = 20f * 20f;
                _prey = Targets.Nearest(transform.position, ref best, includePlayers: true);

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
                if (pl != null) pl.TakeHit(Vector3.up * 4f, 60f * Power);
                else _prey.GetComponentInParent<Damageable>()?.TakeDamage(170f * Power, "dark matter");
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
            DrawingWorld.Instance?.LogEvent("a light that HOLDS, zombies come");
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
                var pl = hit.collider.GetComponentInParent<SimpleFPSController>();
                if (pl != null) pl.KnockDown(1.3f); // clipping a LIMB still bowls you
                var cr = hit.collider.GetComponentInParent<Creature>();
                if (cr != null) { cr.ApplyBlind(4f); cr.KnockDown(1.2f); }
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
            DrawingWorld.Instance?.LogEvent("a DOOR PAIR flies apart");
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
