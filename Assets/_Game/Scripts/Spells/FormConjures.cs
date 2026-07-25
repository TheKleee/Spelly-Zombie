using UnityEngine;

namespace SpellyZombie
{
    /// GRAMMAR v4 P2 — the FORM recipes (SPELL_PARTICLES.md cross matrix).
    /// State runes conjure ONCE per activation, so form combos resolve at
    /// CONJURE TIME from the seal's rune list — the drawing is the recipe.
    /// Identity is preserved throughout (a light-liquid counts as Light):
    /// every conjure carries the seal's LINEAGE, so matter chains toward the
    /// Demon exactly like particles do.
    public static class FormConjures
    {
        // ---------------------------------------------------- HeatUp + Solid --
        /// METEORITE: the block materializes HIGH overhead, born glowing hot,
        /// and falls — crush is momentum (existing law), the burning trail
        /// marks the sky, and what it lands on catches the heat.
        public static void Meteorite(Vector3 at, Vector3 normal, SurfaceMaterialType mat,
            float size, int count, ulong lineage)
        {
            DrawingWorld.Instance?.LogEvent("the sky answers — METEORITE");
            // a WALL or CEILING seal still calls the sky: when the surface
            // normal isn't up-ish, spawn overhead and aim the fall at the seal
            bool skyward = Vector3.Dot(normal, Vector3.up) >= 0.5f;
            for (int i = 0; i < count; i++)
            {
                Vector3 spawn = (skyward ? at + normal * 12f : at + Vector3.up * 12f)
                    + Random.insideUnitSphere * 1.2f;
                Vector3 fall = skyward ? -normal : (at - spawn).normalized;
                var m = Matter.Spawn(mat, MatterPhase.Solid, size * 1.4f, spawn);
                m.Temperature = 420f; // glowing — ignites what it grazes
                m.Lineage = lineage;
                if (m.TryGetComponent<Rigidbody>(out var rb))
                    rb.linearVelocity = fall * 11f + Random.insideUnitSphere * 1.5f;
                m.gameObject.AddComponent<MeteorTrail>();
            }
        }

        // -------------------------------------------------- HeatDown + Solid --
        /// ICE SPIKES: frozen shards of the surface's material erupt from the
        /// ground in a ring — standing terrain that chills whatever touches it.
        public static void IceSpikes(Vector3 at, Vector3 normal, SurfaceMaterialType mat,
            float size, ulong lineage)
        {
            DrawingWorld.Instance?.LogEvent("the ground bares its TEETH — ice spikes");
            Juice.Crackle(at);
            int spikes = 6;
            for (int i = 0; i < spikes; i++)
            {
                float a = i / (float)spikes * Mathf.PI * 2f;
                Vector3 ring = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.35f + size);
                var m = Matter.Spawn(mat, MatterPhase.Solid, size, at + ring + normal * size * 0.8f);
                m.Temperature = -60f;
                m.Lineage = lineage;
                // a spike, not a cube: stretched along the surface normal and
                // planted (kinematic) — it's terrain for its lifetime
                m.transform.localScale = new Vector3(size * 0.45f, size * 2.4f, size * 0.45f);
                m.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal)
                    * Quaternion.Euler(Random.Range(-12f, 12f), Random.value * 360f, Random.Range(-12f, 12f));
                if (m.TryGetComponent<Rigidbody>(out var rb)) rb.isKinematic = true;
            }
        }

        // ------------------------------------------------- HeatDown + Liquid --
        /// GLACIER: the liquid freezes into a growing mass, CHANNELING — then
        /// bursts in a frost explosion: knockover, deep cold, damage.
        public static void Glacier(Vector3 at, SurfaceMaterialType mat, float power, ulong lineage)
        {
            var go = new GameObject("Glacier");
            go.transform.position = at;
            var g = go.AddComponent<GlacierChannel>();
            g.Power = power;
            g.Lineage = lineage;
            DrawingWorld.Instance?.LogEvent("the liquid LOCKS — a glacier grows…");
        }

        // --------------------------------------------------- HeatUp + Liquid --
        /// The liquid decides (Marko's rule): water flashes to a hot steam
        /// area · meltable stone ERUPTS lava · burnable sap pours out already
        /// aflame and spreading.
        public static void HotLiquid(Vector3 at, Vector3 normal, SurfaceMaterialType mat,
            float size, float power, ulong lineage)
        {
            var info = SurfaceMaterialDB.Info(mat);
            if (mat == SurfaceMaterialType.Water)
            {
                // hot water = the same GAS SUBSTANCE as the heat+chill paradox
                // (Marko's rule: one substance, one behavior — no ambiguity)
                var steam = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Gas, 0.9f,
                    at + normal * 0.5f);
                steam.Temperature = 130f;
                steam.Density = 0.3f;
                steam.Lineage = lineage;
                return;
            }
            if (info.Meltable)
            {
                DrawingWorld.Instance?.LogEvent("the ground ERUPTS");
                Juice.Boom(at, 0.7f);
                for (int i = 0; i < 5; i++)
                {
                    var m = Matter.Spawn(mat, MatterPhase.Liquid, size * 0.8f,
                        at + normal * 0.3f + Random.insideUnitSphere * 0.3f);
                    m.Temperature = info.MeltPoint + 200f;
                    m.Lineage = lineage;
                    if (m.TryGetComponent<Rigidbody>(out var rb))
                        rb.linearVelocity = normal * Random.Range(5f, 9f) + Random.insideUnitSphere * 2f;
                }
                return;
            }
            // sap and everything else that burns: a spreading, flaming pour
            var flam = Matter.Spawn(mat, MatterPhase.Liquid, size, at + normal * size * 0.5f);
            flam.Temperature = info.Flammable ? info.IgnitePoint + 120f : 300f;
            flam.Lineage = lineage;
            flam.FormLevel = 2; // it SPREADS while it burns
        }

        // -------------------------------------------------- Liquid + Density --
        /// PRESSURE JET: the conjured liquid fires out along the seal's normal
        /// as a stream of heavy blobs — a water cannon; the damage is momentum.
        public static void PressureJet(Vector3 at, Vector3 normal, SurfaceMaterialType mat,
            float size, float power, ulong lineage)
        {
            var go = new GameObject("PressureJet");
            go.transform.position = at + normal * 0.2f;
            var j = go.AddComponent<PressureJetSource>();
            j.Normal = normal;
            j.Mat = mat;
            j.Size = Mathf.Max(0.12f, size * 0.7f);
            j.Power = power;
            j.Lineage = lineage;
            DrawingWorld.Instance?.LogEvent("PRESSURE — the seal becomes a cannon");
        }
    }

    /// A falling meteor sheds burning motes — the trail IS the warning.
    public class MeteorTrail : MonoBehaviour
    {
        float _tick;
        void Update()
        {
            _tick -= Time.deltaTime;
            if (_tick > 0f) return;
            _tick = 0.06f;
            var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            f.name = "MeteorEmber";
            Destroy(f.GetComponent<Collider>());
            f.transform.position = transform.position + Random.insideUnitSphere * 0.2f;
            f.transform.localScale = Vector3.one * Random.Range(0.08f, 0.18f);
            f.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(
                Color.Lerp(new Color(1f, 0.7f, 0.2f), new Color(1f, 0.3f, 0.05f), Random.value),
                MoteShade.Additive);
            Destroy(f, 0.5f);

            // landed (or nearly stopped): one hot stamp on arrival, then done
            if (TryGetComponent<Rigidbody>(out var rb) && rb.linearVelocity.sqrMagnitude < 1f)
            {
                GrammarFX.FlameBurst(transform.position, 0.7f);
                Destroy(this);
            }
        }
    }

    /// The glacier: grows for a beat (channeling — the telegraph), then the
    /// frost explosion: knockover + deep cold + damage, and shards remain.
    public class GlacierChannel : MonoBehaviour
    {
        public float Power = 1f;
        public ulong Lineage;

        const float GrowSeconds = 2.2f;
        const float BurstRadius = 4f;

        Transform _mass;
        float _age;

        void Start()
        {
            _mass = GrammarFX.FieldBall(transform.position + Vector3.up * 0.3f, 0.3f,
                new Color(0.72f, 0.88f, 1f, 0.8f), MoteShade.Transparent);
            _mass.SetParent(transform, true);
            Juice.Crackle(transform.position);
        }

        void Update()
        {
            _age += Time.deltaTime;
            if (_mass != null)
                _mass.localScale = Vector3.one * Mathf.Lerp(0.6f, 2.6f, _age / GrowSeconds);
            if (_age < GrowSeconds) return;

            // BURST
            Juice.Boom(transform.position, 0.9f);
            DrawingWorld.Instance?.LogEvent("the glacier BURSTS");
            int n = Physics.OverlapSphereNonAlloc(transform.position, BurstRadius,
                GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null) continue;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null)
                {
                    pl.TakeHit((pl.transform.position - transform.position).normalized * 6f,
                        12f * Power, "glacier burst");
                    continue;
                }
                var cr = c.GetComponentInParent<Creature>();
                if (cr != null) cr.KnockDown(1.6f);
                SpellParticle.GiveHeatTo(c, -70f * Power); // deep cold — freezes the close ones
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddForce((rb.worldCenterOfMass - transform.position).normalized * 6f,
                        ForceMode.VelocityChange);
            }
            for (int i = 0; i < 6; i++) // shards remain — ice to pick up or slip on
            {
                var m = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Solid, 0.18f,
                    transform.position + Vector3.up * 0.4f + Random.insideUnitSphere * 0.4f);
                m.Temperature = -40f;
                m.Lineage = Lineage;
                if (m.TryGetComponent<Rigidbody>(out var rb2))
                    rb2.linearVelocity = Random.onUnitSphere * 5f + Vector3.up * 3f;
            }
            Destroy(gameObject);
        }
    }

    /// The pressure jet: fires heavy liquid blobs along the normal for a few
    /// seconds — pure momentum weaponry, no magic numbers on top.
    public class PressureJetSource : MonoBehaviour
    {
        public Vector3 Normal = Vector3.up;
        public SurfaceMaterialType Mat;
        public float Size = 0.15f, Power = 1f;
        public ulong Lineage;

        float _left = 2.6f, _tick;

        void Update()
        {
            _left -= Time.deltaTime;
            if (_left <= 0f) { Destroy(gameObject); return; }
            _tick -= Time.deltaTime;
            if (_tick > 0f) return;
            _tick = 0.22f;
            var m = Matter.Spawn(Mat, MatterPhase.Liquid, Size, transform.position + Normal * 0.3f);
            m.Lineage = Lineage;
            m.AddDensity(1.2f); // heavy — the punch is the mass (2.5 would snap-freeze it, verified)
            if (m.TryGetComponent<Rigidbody>(out var rb))
                rb.linearVelocity = Normal * (13f * Mathf.Max(0.6f, Power))
                    + Random.insideUnitSphere * 0.8f;
        }
    }

    /// Solid × 3 — SOLID AVALANCHE (Marko's lvl3): the area births growing
    /// lvl2 solids that roll outward, spreading.
    public class SolidAvalancheField : GrammarField
    {
        public SurfaceMaterialType Mat;
        public ulong FieldLineage;
        float _spawnTick;
        int _spawned;

        public static void Open(Vector3 at, SurfaceMaterialType mat, float power, ulong lineage)
        {
            var f = Spawn<SolidAvalancheField>(at, power, DrawingConfig.UltimateRadius,
                DrawingConfig.UltimateSeconds * 1.2f,
                new Color(0.6f, 0.5f, 0.4f, 0.2f), MoteShade.Transparent);
            f.Mat = mat;
            f.FieldLineage = lineage;
            DrawingWorld.Instance?.LogEvent("the ground MULTIPLIES — avalanche");
        }

        protected override void Grow(float dt)
        {
            _spawnTick -= dt;
            if (_spawnTick > 0f || _spawned >= 7) return;
            _spawnTick = 0.7f;
            _spawned++;
            Vector3 dirOut = Random.insideUnitSphere; dirOut.y = 0f; dirOut.Normalize();
            var m = Matter.Spawn(Mat, MatterPhase.Solid, 0.22f,
                transform.position + Vector3.up * 0.6f + dirOut * 0.4f);
            m.FormLevel = 2; // they grow on their own
            m.Lineage = FieldLineage;
            if (m.TryGetComponent<Rigidbody>(out var rb))
                rb.linearVelocity = dirOut * Random.Range(2.5f, 4.5f) + Vector3.up * 1f;
        }

        protected override void Affect(Collider c, float dt) { } // the boulders do the affecting
    }

    /// Liquid × 3 — LIQUID AREA (Marko's lvl3): a floating bubble of the
    /// liquid. Walk in and you WADE mid-air; stay too long and you DROWN.
    /// Carries the liquid's own coating (oil slips, slime sticks…).
    public class LiquidAreaField : GrammarField
    {
        public SurfaceMaterialType Mat;
        public ulong FieldLineage; // carriers keep the chain alive (like the tornado)
        readonly System.Collections.Generic.Dictionary<Component, float> _submerged =
            new System.Collections.Generic.Dictionary<Component, float>();

        public static void Open(Vector3 at, SurfaceMaterialType mat, float power, ulong lineage)
        {
            var info = SurfaceMaterialDB.Info(mat);
            var c = info.LiquidColor; c.a = 0.5f;
            var f = Spawn<LiquidAreaField>(at + Vector3.up * 1.6f, power, 2.3f,
                DrawingConfig.UltimateSeconds * 2f, c, MoteShade.Transparent);
            f.Mat = mat;
            f.FieldLineage = lineage;
            DrawingWorld.Instance?.LogEvent("the liquid REFUSES the ground — a floating sea");
        }

        protected override float TickPeriod => 0.25f;

        protected override void Affect(Collider c, float dt)
        {
            var pl = c.GetComponent<SimpleFPSController>();
            if (pl != null)
            {
                // buoyant wading in mid-air; the drown clock runs while inside
                Vector3 v = pl.Velocity; v.y *= 0.4f;
                pl.AddSpellForce(-v * 2f + Vector3.up * 6f, dt);
                float t = Tally(pl, dt);
                if (t > 3f) pl.TakeHit(Vector3.zero, 5f * dt, "drowning");
                return;
            }
            var cr = c.GetComponentInParent<Creature>();
            if (cr != null)
            {
                cr.ApplyStuck(0.3f); // swimming zombies are slow zombies
                if (Mat == SurfaceMaterialType.Coal) cr.ApplySlip(1.5f);
                else if (Mat == SurfaceMaterialType.Slime) cr.ApplyStuck(1.5f);
                else if (Mat == SurfaceMaterialType.Wood) cr.ApplyStuck(1.5f); // sap
                // the bubble CARRIES its liquid's temperament: a molten sea
                // burns its swimmers (a plain one just drowns them)
                if (SurfaceMaterialDB.Info(Mat).Meltable)
                    SpellParticle.GiveHeatTo(c, 35f * Power);
                float t = Tally(cr, dt);
                var dmg = cr.GetComponent<Damageable>();
                if (t > 2f && dmg != null) dmg.TakeDamage(8f * dt, "drowned");
            }
        }

        float Tally(Component who, float dt)
        {
            _submerged.TryGetValue(who, out float t);
            t += dt;
            _submerged[who] = t;
            return t;
        }
    }
}
