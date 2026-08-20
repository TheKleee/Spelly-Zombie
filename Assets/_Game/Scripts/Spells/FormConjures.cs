using UnityEngine;

namespace SpellyZombie
{
    /// Form recipes (SPELL_PARTICLES.md cross matrix). Form combos resolve at
    /// conjure time from the seal's rune list; every conjure carries the seal's
    /// lineage, so matter chains toward the Demon like particles do.
    public static class FormConjures
    {
        /// The one steam spawner: the heat+chill paradox and hot water both call this.
        public static Matter SpawnSteam(Vector3 at, float size, ulong lineage)
        {
            var steam = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Gas, size, at);
            steam.Temperature = 130f; // scalding - hot gas bites waders
            steam.Density = 0.3f;
            steam.Lineage = lineage;
            return steam;
        }

        // ---------------------------------------------------- HeatUp + Solid --
        /// METEORITE: born glowing hot; seal-cast erupts and leaps, sky release falls from overhead.
        public static void Meteorite(Vector3 at, Vector3 normal, SurfaceMaterialType mat,
            float size, float reach, int count, ulong lineage, bool fromSky = false)
        {
            // dormant release: the rock appears overhead at full size and slams down
            if (fromSky)
            {
                // appears huge high up and keeps growing on the way down
                DrawingWorld.Instance?.LogEvent("the sky delivers");
                for (int i = 0; i < count; i++)
                {
                    Vector3 high = at + Vector3.up * 70f + Random.insideUnitSphere * 1.5f;
                    var sky = Matter.Spawn(mat, MatterPhase.Solid, size * 8f, high);
                    sky.Temperature = 420f;
                    sky.Lineage = lineage;
                    sky.gameObject.AddComponent<MeteorTrail>();
                    var drop = sky.gameObject.AddComponent<MeteorRise>();
                    drop.Grow = 2.2f;    // big at birth, bigger by landing
                    drop.Reach = reach;
                    drop.SkyDrop = true; // dive from frame one
                }
                return;
            }
            // seal cast: the stone is born at the seal and thrown upward while swelling
            DrawingWorld.Instance?.LogEvent("the stone catches fire and LEAPS");
            for (int i = 0; i < count; i++)
            {
                Vector3 spawn = at + normal * (size * 0.7f) + Random.insideUnitSphere * 0.2f;
                var m = Matter.Spawn(mat, MatterPhase.Solid, size, spawn);
                m.Temperature = 420f; // glowing - ignites what it grazes
                m.Lineage = lineage;
                m.gameObject.AddComponent<MeteorTrail>();

                // grows tenfold on the way up; reach is the impact-area dial
                var rise = m.gameObject.AddComponent<MeteorRise>();
                rise.Grow = 10f;
                rise.Reach = reach;

                if (m.TryGetComponent<Rigidbody>(out var rb))
                {
                    // launched along the surface normal (a wall seal throws off the wall)
                    Vector3 up = Vector3.Dot(normal, Vector3.up) >= 0.5f ? Vector3.up : normal;
                    rb.linearVelocity = (up + Random.insideUnitSphere * 0.12f).normalized
                        * DrawingConfig.MeteorRiseSpeed;
                }
            }
        }

        // -------------------------------------------------- HeatDown + Solid --
        /// ICE SPIKES: frozen shards of the surface's material erupt from the
        /// ground in a ring - standing terrain that chills whatever touches it.
        public static void IceSpikes(Vector3 at, Vector3 normal, SurfaceMaterialType mat,
            float size, float reach, ulong lineage, int owner = -1)
        {
            DrawingWorld.Instance?.LogEvent("the ground grows ICE SPIKES");
            Juice.Crackle(at);
            int spikes = 6;
            for (int i = 0; i < spikes; i++)
            {
                float a = i / (float)spikes * Mathf.PI * 2f;
                // the ring radius comes from reach, not size
                Vector3 ring = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.35f + reach * 0.5f);
                var m = Matter.Spawn(mat, MatterPhase.Solid, size, at + ring + normal * size * 0.8f);
                m.Temperature = -60f;
                m.Lineage = lineage;
                // a spike, not a cube: stretched to a point
                m.transform.localScale = new Vector3(size * 0.45f, size * 2.4f, size * 0.45f);
                m.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal)
                    * Quaternion.Euler(Random.Range(-12f, 12f), Random.value * 360f, Random.Range(-12f, 12f));

                // spikes hunt via MatterStrike: float, lock a target, leap, hit with momentum
                if (m.TryGetComponent<Rigidbody>(out var rb)) rb.isKinematic = false;
                var strike = m.GetComponent<MatterStrike>();
                if (strike == null) strike = m.gameObject.AddComponent<MatterStrike>();
                strike.Init(owner, mat, MatterPhase.Solid, size);
            }
        }

        // ------------------------------------------------- HeatDown + Liquid --
        /// GLACIER: the liquid freezes into a growing mass, CHANNELING - then
        /// bursts in a frost explosion: knockover, deep cold, damage.
        public static void Glacier(Vector3 at, SurfaceMaterialType mat, float power, ulong lineage)
        {
            var go = new GameObject("Glacier");
            go.transform.position = at;
            var g = go.AddComponent<GlacierChannel>();
            g.Power = power;
            g.Lineage = lineage;
            DrawingWorld.Instance?.LogEvent("the liquid LOCKS into a glacier…");
        }

        // --------------------------------------------------- HeatUp + Liquid --
        /// Water flashes to hot steam; meltable stone erupts lava; flammable
        /// sap pours out aflame and spreading.
        public static void HotLiquid(Vector3 at, Vector3 normal, SurfaceMaterialType mat,
            float size, float reach, float power, ulong lineage)
        {
            var info = SurfaceMaterialDB.Info(mat);
            if (mat == SurfaceMaterialType.Water)
            {
                // hot water = the same steam as the heat+chill paradox; sized by summed reach
                SpawnSteam(at + normal * 0.5f, Mathf.Max(0.9f, reach), lineage);
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
            flam.FormLevel = 2; // it spreads while it burns
        }

        // -------------------------------------------------- Liquid + Density --
        /// PRESSURE JET: the conjured liquid fires out along the seal's normal
        /// as a stream of heavy blobs - a water cannon; the damage is momentum.
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
            DrawingWorld.Instance?.LogEvent("the seal becomes a PRESSURE cannon");
        }
    }

    /// A falling meteor sheds burning motes.
    public class MeteorTrail : MonoBehaviour
    {
        // visuals only; the explosion lives in MeteorRise.OnCollisionEnter
        void Start()
        {
            // one bright streak riding the rock, width scaled to the rock
            var ribbon = gameObject.AddComponent<TrailRenderer>();
            float s = Mathf.Max(1f, transform.lossyScale.x * 0.4f);
            ribbon.time = 1.1f;
            ribbon.startWidth = 0.55f * s;
            ribbon.endWidth = 0.03f;
            ribbon.minVertexDistance = 0.3f;
            ribbon.sharedMaterial = MatterFX.Get(new Color(1f, 0.55f, 0.15f, 0.9f), MoteShade.Additive);
        }

        float _tick;
        void Update()
        {
            _tick -= Time.deltaTime;
            if (_tick > 0f) return;
            _tick = 0.06f;
            // mote size scales with the rock
            float s = Mathf.Max(1f, transform.lossyScale.x * 0.45f);
            GrammarFX.FireMote(transform.position + Random.insideUnitSphere * 0.25f * s,
                Random.Range(0.08f, 0.18f) * s, 0.5f);
        }
    }

    /// The glacier: grows for a beat (channeling - the telegraph), then the
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
                SpellParticle.GiveHeatTo(c, -70f * Power); // deep cold - freezes the close ones
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddForce((rb.worldCenterOfMass - transform.position).normalized * 6f,
                        ForceMode.VelocityChange);
            }
            for (int i = 0; i < 6; i++) // shards remain - ice to pick up or slip on
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

    /// Fires heavy liquid blobs along the normal for a few seconds; damage is momentum.
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
            m.AddDensity(1.2f); // heavy - the punch is the mass; 2.5+ would snap-freeze it solid
            if (m.TryGetComponent<Rigidbody>(out var rb))
                rb.linearVelocity = Normal * (13f * Mathf.Max(0.6f, Power))
                    + Random.insideUnitSphere * 0.8f;
        }
    }

    /// Solid × 3 - solid avalanche: the area births growing lvl2 solids that roll outward.
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
            DrawingWorld.Instance?.LogEvent("the ground becomes an AVALANCHE");
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

    /// Liquid × 3 - liquid area: a floating bubble; wade mid-air, drown if you
    /// stay. Carries the liquid's own coating (oil slips, slime sticks).
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
            DrawingWorld.Instance?.LogEvent("the liquid rises into a floating SEA");
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
                cr.ApplyStuck(0.3f); // swimming slows
                if (Mat == SurfaceMaterialType.Coal) cr.ApplySlip(1.5f);
                else if (Mat == SurfaceMaterialType.Slime) cr.ApplyStuck(1.5f);
                else if (Mat == SurfaceMaterialType.Wood) cr.ApplyStuck(1.5f); // sap
                // a molten sea burns its swimmers
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
