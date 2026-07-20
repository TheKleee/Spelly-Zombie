using UnityEngine;

namespace SpellyZombie
{
    public enum MatterPhase { Solid, Liquid, Gas }

    /// A block of conjured matter. State runes are the only thing that makes it:
    /// Solid conjures ONE block of the surface's material, Liquid conjures the
    /// same block already in its liquid form (Stone→Lava, Flesh→Blood, Coal→Oil).
    /// Everything after that is consequences, driven by the material table:
    ///   heat past MeltPoint    → melts into its liquid       (stone → lava)
    ///   liquid cooled          → re-solidifies / freezes      (lava → stone, water → ice)
    ///   heat past IgnitePoint  → burns; WOOD BURNS INTO COAL
    ///   liquid past BoilPoint  → gas (steam / fumes)
    ///   density-down on solid  → FRAGMENTS into smaller blocks of the same stuff
    ///   density-down on liquid → vapor
    ///   density-up sustained   → TRANSMUTES into DenserForm   (coal → diamond!)
    ///   sticky up/down         → clumps / slides
    /// Conjured solids carry a SurfaceMaterialTag, so you can draw ON your own
    /// conjured matter and chain spells.
    public class Matter : MonoBehaviour
    {
        public SurfaceMaterialType Material = SurfaceMaterialType.Unknown;
        public MatterPhase Phase = MatterPhase.Solid;
        public float Temperature = 18f;
        public float Density = 1f;
        public float Stickiness = 0.35f;

        // GRAMMAR v4 (P2): matter chains like particles do
        public ulong Lineage;       // the seal's rune ancestry — matter is a Demon path too
        public int FormLevel = 1;   // 2 = lvl2 form: a solid GROWS on its own, a liquid SPREADS
        public bool DarkAura;       // solid/liquid darkness: blinds whatever touches it

        const float MinFragmentSize = 0.07f;   // fragments below this stop splitting
        const float TransmuteAt = 3.2f;        // density needed to jump a tier
        const float FragmentAt = 0.35f;        // density where a solid falls apart
        const int MaxAlive = 90;               // world matter cap (multiplayer grief-proofing)

        static readonly System.Collections.Generic.List<Matter> All = new System.Collections.Generic.List<Matter>();

        /// Treasure worth when collected (0 = not treasure).
        public int TreasureValue =>
            Phase != MatterPhase.Solid ? 0 :
            Material == SurfaceMaterialType.Diamond ? 20 :
            Material == SurfaceMaterialType.Gold ? 5 : 0;

        MaterialInfo _info;
        Rigidbody _rb;
        Renderer _rend;
        Collider _core;
        LiquidVolume _shell;      // trigger shell while Liquid/Gas — wading effects
        MatterPhase _shellPhase = (MatterPhase)(-1); // last phase the colliders were synced for
        SurfaceMaterialTag _tag;
        MaterialPropertyBlock _mpb;
        float _age, _life = 20f, _integrity = 1f, _baseSize = 0.3f, _slump;
        bool _ice, _burning;
        int _lastLook = int.MinValue;

        public float BlobTemperature => Temperature;
        public Collider Core => _core;
        public Rigidbody Body => _rb;

        static readonly int SquashID = Shader.PropertyToID("_Squash");

        public void Init(SurfaceMaterialType mat, MatterPhase phase, float baseSize)
        {
            Material = mat;
            _info = SurfaceMaterialDB.Info(mat);
            Phase = phase;
            _baseSize = baseSize;
            Stickiness = _info.BaseStickiness;
            Density = phase == MatterPhase.Solid ? 2f : 1f;
            _life = phase == MatterPhase.Solid ? 25f : 12f;
            if (mat == SurfaceMaterialType.Diamond || mat == SurfaceMaterialType.Gold)
                _life = 60f; // treasure sticks around long enough to be carried home

            // a meltable material's liquid form IS hot — drawing Liquid on stone
            // makes lava, and lava is born glowing (it cools back to stone later)
            Temperature = phase == MatterPhase.Liquid && _info.Meltable ? _info.MeltPoint + 150f : 18f;

            // conjured solid WATER is ice, and ice MELTS — without this flag the
            // glacier shards and snowballs were permanent fireproof blocks (verified)
            _ice = mat == SurfaceMaterialType.Water && phase == MatterPhase.Solid;

            // conjured matter is a real surface: you can draw your next seal on it
            _tag = gameObject.GetComponent<SurfaceMaterialTag>();
            if (_tag == null) _tag = gameObject.AddComponent<SurfaceMaterialTag>();
            _tag.Material = mat;

            SyncPhaseCollision(); // liquids are walk-through from birth
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rend = GetComponent<Renderer>();
            _core = GetComponent<Collider>();
            All.Add(this);
            if (All.Count > MaxAlive && All[0] != null)
            {
                var oldest = All[0];
                All.RemoveAt(0); // NOW — deferred OnDestroy let burst spawns overshoot the cap
                Destroy(oldest.gameObject);
            }
        }

        /// A planted (kinematic) spike that melts must FLOW, not hover.
        void MeltFree()
        {
            if (_rb != null) _rb.isKinematic = false;
        }

        /// MARKO'S LIQUID RULE: you WALK THROUGH liquid (it slows you and pulls
        /// you downstream) — but the blob itself still rests on the GROUND.
        /// The trick is a SPLIT: the real collider stays (so the puddle sits on
        /// floors/slopes and matter-vs-matter chemistry keeps colliding), but
        /// it pair-IGNORES every creature/player that comes near, and a bigger
        /// TRIGGER SHELL applies the wading effects (viscous slow, downstream
        /// drag, coatings, lava burn). Freeze the puddle and the ignores are
        /// restored — ice is a floor again.
        void SyncPhaseCollision()
        {
            if (Phase == _shellPhase) return;
            _shellPhase = Phase;
            bool passable = Phase != MatterPhase.Solid;
            // liquids live on the built-in Water layer: the player capsule
            // EXCLUDES that layer outright (SimpleFPSController), which makes
            // walk-through bulletproof for the CharacterController while the
            // ground (layer 0) still holds the puddle up
            gameObject.layer = passable ? 4 : 0;
            if (passable && _shell == null)
            {
                var go = new GameObject("LiquidShell");
                go.transform.SetParent(transform, false);
                var trig = go.AddComponent<SphereCollider>();
                trig.isTrigger = true;
                trig.radius = 0.85f; // in blob-local space — scales with the puddle
                _shell = go.AddComponent<LiquidVolume>();
                _shell.Owner = this;
            }
            else if (!passable && _shell != null)
            {
                _shell.RestoreCollisions(); // a frozen puddle is a FLOOR
                Destroy(_shell.gameObject);
                _shell = null;
            }
        }

        void OnDestroy() => All.Remove(this);

        public void AddHeat(float d) => Temperature += d / Mathf.Max(0.2f, _info.HeatCapacity);
        public void AddDensity(float d) => Density = Mathf.Clamp(Density + d, 0.05f, 6f);
        // full [-1, 1] range: NEGATIVE stickiness is the slick payload (the old
        // Clamp01 silently erased Liquid+Slick — the slip pool was inert)
        public void AddStickiness(float d) => Stickiness = Mathf.Clamp(Stickiness + d, -1f, 1f);

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            Temperature = Mathf.MoveTowards(Temperature, 18f, DrawingConfig.AmbientDriftPerSec * 0.4f * dt);

            switch (Phase)
            {
                case MatterPhase.Liquid:
                    if (_info.Type == SurfaceMaterialType.Water && Temperature <= 0f) { Phase = MatterPhase.Solid; _ice = true; _slump = 0f; }
                    else if (_info.Meltable && Temperature < _info.MeltPoint - 120f) { Phase = MatterPhase.Solid; _slump = 0f; } // lava cools to stone
                    else if (Temperature >= _info.BoilPoint) Phase = MatterPhase.Gas;
                    else if (Density <= 0.4f) Phase = MatterPhase.Gas;  // thinned to vapor — floats away
                    else if (Density >= 2.5f)                           // compressed liquid SOLIDIFIES
                    {
                        Phase = MatterPhase.Solid;
                        _ice = _info.Type == SurfaceMaterialType.Water; // water → ice; heat melts it back
                        _slump = 0f;
                    }
                    else Slump(dt);                                     // liquids pool where they are
                    break;

                case MatterPhase.Solid:
                    if (_ice && Temperature > 2f) { Phase = MatterPhase.Liquid; _ice = false; MeltFree(); }
                    else if (_info.Meltable && Temperature >= _info.MeltPoint) { Phase = MatterPhase.Liquid; MeltFree(); }
                    else if (_info.Flammable && Temperature >= _info.IgnitePoint) _burning = true;
                    if (Density <= FragmentAt) { Fragment(); return; }  // low density: burst into smaller blocks
                    break;

                case MatterPhase.Gas:
                    if (_rb) _rb.AddForce(Vector3.up * 1.4f, ForceMode.Acceleration); // rises
                    break;
            }

            SyncPhaseCollision(); // phase flips flip walk-through-ness with them

            // lvl2 FORMS (two State runes in one seal): a solid grows slowly on
            // its own; a liquid rapidly spreads — sheds smaller blobs around it
            if (FormLevel >= 2)
            {
                if (Phase == MatterPhase.Solid)
                {
                    _baseSize = Mathf.Min(_baseSize + 0.05f * dt, 1.3f);
                    transform.localScale = Vector3.one * _baseSize;
                }
                else if (Phase == MatterPhase.Liquid)
                {
                    _spreadTick -= dt;
                    if (_spreadTick <= 0f && All.Count < MaxAlive - 4 && _baseSize > 0.12f)
                    {
                        _spreadTick = 1.1f;
                        Vector3 dirOut = Random.insideUnitSphere; dirOut.y = 0f;
                        var child = Spawn(Material, MatterPhase.Liquid, _baseSize * 0.55f,
                            transform.position + dirOut.normalized * _baseSize * 0.9f + Vector3.up * 0.1f);
                        child.Temperature = Temperature; // burning sap spreads BURNING
                        child.Lineage = Lineage;
                    }
                }
            }

            Cohere(); // fake joints, every phase — strength graded by phase

            // sustained compression jumps the material a tier: wood→coal→diamond
            if (Density >= TransmuteAt && _info.DenserForm != Material) Transmute();

            if (_burning)
            {
                _integrity -= dt * 0.5f / Mathf.Max(0.3f, _info.Strength);
                Temperature += 40f * dt; // feeds itself — but frost can still WIN
                if (Temperature < _info.IgnitePoint - 30f)
                {
                    _burning = false; // doused (Marko's rule: cold frees the burning)
                    GrammarFX.PuffBurst(transform.position, new Color(0.92f, 0.94f, 0.97f, 0.5f), 4);
                }
                if (_integrity <= 0f)
                {
                    // wood burns down to a lump of coal — the alchemy chain starts here
                    if (Material == SurfaceMaterialType.Wood && _baseSize * 0.6f > MinFragmentSize)
                    {
                        Rebecome(SurfaceMaterialType.Coal, _baseSize * 0.6f);
                        return;
                    }
                    Destroy(gameObject);
                    return;
                }
            }

            ApplyPhysics();
            Refresh();

            float life = Phase == MatterPhase.Gas ? 2.5f : _life;
            if (_age > life - 1.5f) // matter fades out by shrinking at end of life
                transform.localScale = transform.localScale * Mathf.Max(0.01f, 1f - dt / 1.5f);
            if (_age > life || transform.localScale.x < 0.02f) Destroy(gameObject);
        }

        /// FAKE JOINTS (Marko's rule — no Unity joints, a spring is enough):
        /// same-material, same-phase blobs are linked by a spring toward a rest
        /// distance. Strength is graded by phase — LIQUID strongest (bond IS
        /// Stickiness: glue makes goo, repel makes mist), SOLID weak (rubble
        /// loosely piles), GAS weakest (clouds barely hold). Liquids also match
        /// velocities (soft-body: the blob cluster moves and jiggles as one)
        /// and MERGE on contact. Splash a puddle apart and it re-pools; freeze
        /// it and the bonds lock rigid. Different materials never bond —
        /// chemistry (React) decides for them. Cost: a distance sweep 3×/sec
        /// under the 90-matter cap — no physics solver, nothing on the wire.
        float _cohereTimer;
        float _spreadTick; // lvl2 liquid: cadence of shedding smaller blobs
        void Cohere()
        {
            _cohereTimer -= Time.deltaTime;
            if (_cohereTimer > 0f) return;
            _cohereTimer = 0.3f;
            if (_rb == null) return;

            float k = Phase == MatterPhase.Liquid ? Stickiness * 6f
                : Phase == MatterPhase.Solid ? 0.7f : 0.25f;
            float reach = Phase == MatterPhase.Liquid ? 1.2f : 0.9f;
            bool liquid = Phase == MatterPhase.Liquid;

            for (int i = 0; i < All.Count; i++)
            {
                var o = All[i];
                if (o == null || o == this || o.Phase != Phase
                    || o.Material != Material) continue;
                Vector3 to = o.transform.position - transform.position;
                float d = to.magnitude;
                if (d > reach || d < 0.01f) continue;

                if (liquid)
                {
                    bool iAbsorb = _baseSize > o._baseSize
                        || (Mathf.Approximately(_baseSize, o._baseSize) && GetInstanceID() < o.GetInstanceID());
                    if (d < 0.22f && iAbsorb && _baseSize < 1.3f)
                    {
                        // the bigger blob drinks the smaller — volumes add
                        _baseSize = Mathf.Min(1.4f, Mathf.Pow(
                            _baseSize * _baseSize * _baseSize + o._baseSize * o._baseSize * o._baseSize, 1f / 3f));
                        Temperature = (Temperature + o.Temperature) * 0.5f;
                        _slump = 0f; // re-settle at the new size
                        Destroy(o.gameObject);
                        continue;
                    }
                }

                // the spring: pull toward rest distance, push apart when
                // overlapping — the cluster holds SHAPE instead of collapsing
                float rest = (_baseSize + o._baseSize) * 0.55f;
                _rb.AddForce(to.normalized * (d - rest) * k, ForceMode.Acceleration);

                // soft-body velocity matching: the blob wobbles, then settles
                if (liquid && o._rb != null)
                    _rb.AddForce((o._rb.linearVelocity - _rb.linearVelocity) * 0.3f,
                        ForceMode.Acceleration);
            }
        }

        /// Liquids don't shoot around — the conjured block relaxes into a puddle
        /// where it stands (flattens, widens, slides by its own stickiness).
        void Slump(float dt)
        {
            _slump = Mathf.MoveTowards(_slump, 1f, dt / 1.2f);
            float flat = Mathf.Lerp(1f, 0.3f, _slump);
            float wide = Mathf.Lerp(1f, 1.7f, _slump);
            transform.localScale = new Vector3(_baseSize * wide, _baseSize * flat, _baseSize * wide);

            // feed the shader's soft-body squash so the surface CONFORMS as it
            // settles (per-renderer MPB — the material itself stays shared)
            if (_rend != null)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                _rend.GetPropertyBlock(_mpb);
                _mpb.SetFloat(SquashID, _slump * 0.35f);
                _rend.SetPropertyBlock(_mpb);
            }
        }

        void ApplyPhysics()
        {
            if (_rb == null) return;
            _rb.mass = Mathf.Max(0.02f, 0.6f * _baseSize * Density);
            _rb.linearDamping = Mathf.Lerp(0.02f, 9f, Stickiness) + (Phase == MatterPhase.Gas ? 0.6f : 0f);
            if (Phase == MatterPhase.Liquid) _rb.constraints = RigidbodyConstraints.FreezeRotation; // puddles don't tumble
        }

        /// Density-down tore this solid apart: replace it with smaller blocks of
        /// the SAME material sharing the volume. Keeps fragmenting under
        /// continued exposure until pieces reach MinFragmentSize.
        void Fragment()
        {
            int pieces = Random.Range(3, 5);
            float childSize = _baseSize * 0.55f;
            if (childSize >= MinFragmentSize)
            {
                for (int i = 0; i < pieces; i++)
                {
                    var m = Spawn(Material, MatterPhase.Solid, childSize,
                        transform.position + Random.insideUnitSphere * _baseSize * 0.5f);
                    m.Temperature = Temperature;
                    if (m.TryGetComponent<Rigidbody>(out var rb) && _rb != null)
                        rb.linearVelocity = _rb.linearVelocity + Random.insideUnitSphere * 1.2f;
                }
            }
            Destroy(gameObject);
        }

        /// Density-up compressed this into its stronger form (stone → diamond).
        void Transmute()
        {
            Rebecome(_info.DenserForm, _baseSize * 0.8f);
            WorldEvents.Report(WorldEventKind.Sparkle, transform.position, 1.2f); // ooooh, shiny
            // a short glint so the upgrade reads on camera
            var glow = new GameObject("TransmuteGlint");
            glow.transform.position = transform.position;
            var l = glow.AddComponent<Light>();
            l.type = LightType.Point; l.color = _info.SolidColor; l.intensity = 6f; l.range = 2.5f;
            Destroy(glow, 0.4f);
        }

        void Rebecome(SurfaceMaterialType mat, float size)
        {
            Material = mat;
            _info = SurfaceMaterialDB.Info(mat);
            _baseSize = size;
            transform.localScale = Vector3.one * size;
            Density = 1f;
            Stickiness = _info.BaseStickiness;
            _burning = false;
            _integrity = 1f;
            _age = 0f;               // fresh life in the new form
            _lastLook = int.MinValue;
            if (_tag != null) _tag.Material = mat;
        }

        /// Contact chemistry — where the clips come from.
        void OnCollisionEnter(Collision col) => React(col, true);
        void OnCollisionStay(Collision col)
        {
            _reactCooldown -= Time.fixedDeltaTime;
            if (_reactCooldown <= 0f) { _reactCooldown = 0.5f; React(col, false); }
        }
        float _reactCooldown;

        void React(Collision col, bool impact)
        {
            var other = col.collider.GetComponent<Matter>();

            // LAVA + WATER → stone + steam, the classic
            if (other != null && Phase == MatterPhase.Liquid && Temperature > 300f
                && other.Material == SurfaceMaterialType.Water && other.Phase == MatterPhase.Liquid)
            {
                Temperature = 60f;                    // quenched — solidifies next frame
                other.Temperature = other._info.BoilPoint + 10f; // flashes to steam
                return;
            }

            // hot matter ignites/heats what it touches — lava is a weapon
            if (Temperature > 150f)
            {
                if (other != null) other.AddHeat(Temperature * 0.25f);
                var thermal = col.collider.GetComponentInParent<Thermal>();
                if (thermal != null) thermal.AddHeat(Temperature * 0.15f);
            }

            // COLD matter chills what touches it — ice spikes and snowballs
            // are weapons the same way lava is (mirror of the hot rule)
            if (Temperature < -20f)
                SpellParticle.GiveHeatTo(col.collider, Temperature * 0.15f);

            // solid/liquid DARKNESS: touching it blinds (identity preserved —
            // it still counts as darkness, whatever shape it wears)
            var creatureEarly = col.collider.GetComponentInParent<Creature>();
            if (DarkAura && creatureEarly != null) creatureEarly.ApplyBlind(2f);

            // puddle coatings: oil = lose footing (they topple!), slime/sap = glued
            var creature = col.collider.GetComponentInParent<Creature>();
            if (creature != null && Phase == MatterPhase.Liquid && Temperature < 150f)
            {
                if (Material == SurfaceMaterialType.Coal) creature.ApplySlip(2f);          // oil
                else if (Material == SurfaceMaterialType.Slime) creature.ApplyStuck(2.5f); // gel trap
                else if (Material == SurfaceMaterialType.Wood) creature.ApplyStuck(1.5f);  // sap
            }

            // sticky SOLID is a carrier: what lands on it RIDES it (Marko:
            // "stick a zombie or a player and carry them like this")
            if (impact && Phase == MatterPhase.Solid && Stickiness > 0.7f)
            {
                if (creature != null) creature.ApplyStuck(1.2f);
                var rider = col.rigidbody;
                if (rider != null && !rider.isKinematic && _rb != null
                    && rider.GetComponent<FixedJoint>() == null)
                {
                    var joint = rider.gameObject.AddComponent<FixedJoint>();
                    joint.connectedBody = _rb;
                    joint.breakForce = 420f;
                }
            }

            // solid blocks crush by momentum (creatures AND crates); frozen
            // shatter — and a HEAVY liquid (liquid rock) crushes too
            if (impact && _rb != null
                && (Phase == MatterPhase.Solid
                    || (Phase == MatterPhase.Liquid && Density > 1.8f)))
            {
                float force = col.relativeVelocity.magnitude * _rb.mass;
                if (force > 3f)
                {
                    float dmg = force * 3f * _info.Strength;
                    if (creature == null || !creature.TryShatter(dmg))
                    {
                        var d = col.collider.GetComponentInParent<Damageable>();
                        if (d != null) d.TakeDamage(dmg, $"crushed by {Material}");
                    }
                }
            }
        }

        public static Matter Spawn(SurfaceMaterialType mat, MatterPhase phase, float size, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(phase == MatterPhase.Solid ? PrimitiveType.Cube : PrimitiveType.Sphere);
            go.name = (phase == MatterPhase.Solid ? "Solid_" : "Liquid_") + mat;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<Rigidbody>();
            var m = go.AddComponent<Matter>();
            m.Init(mat, phase, size);
            return m;
        }

        void OnDisable()
        {
            if (_shell != null) _shell.RestoreCollisions();
        }

        void Refresh()
        {
            if (_rend == null) return;

            Color c;
            MoteShade shade;
            int look;
            if (_burning) { c = new Color(1f, 0.4f, 0.1f, 0.95f); shade = MoteShade.Additive; look = 1; }
            else if (Phase == MatterPhase.Gas) { c = new Color(0.9f, 0.92f, 0.95f, 0.4f); shade = MoteShade.Transparent; look = 2; }
            else if (Phase == MatterPhase.Liquid)
            {
                c = _info.LiquidColor;
                shade = Temperature > 300f ? MoteShade.Additive : MoteShade.Transparent; // molten glows
                look = 3 + (Temperature > 300f ? 1 : 0);
            }
            else if (_ice) { c = new Color(0.72f, 0.88f, 1f); shade = MoteShade.Opaque; look = 5; }
            else if (Temperature > 120f) { c = Color.Lerp(_info.SolidColor, new Color(1f, 0.3f, 0.05f), Mathf.InverseLerp(120f, _info.Meltable ? _info.MeltPoint : 500f, Temperature)); shade = MoteShade.Opaque; look = 6 + (int)(Temperature / 40f); }
            else { c = _info.SolidColor; shade = MoteShade.Opaque; look = 0; }

            if (look == _lastLook) return; // avoid re-setting the material every frame
            _lastLook = look;

            // liquids and gas wear the soft-body shader (jelly wobble + rim);
            // solids stay rigid — their bonds are locked, and it shows
            if (Phase == MatterPhase.Liquid)
                _rend.sharedMaterial = MatterFX.Particle(c, shade, 0.07f, 0.5f);
            else if (Phase == MatterPhase.Gas)
                _rend.sharedMaterial = MatterFX.Particle(c, shade, 0.11f, 0.7f);
            else
                _rend.sharedMaterial = MatterFX.Get(c, shade);
        }
    }

    /// The walk-through half of Marko's liquid rule: a trigger shell riding a
    /// liquid/gas blob. Any creature or player it touches gets pair-IGNORED
    /// against the blob's real collider (so you wade instead of bump) and,
    /// while inside, WADING effects apply: viscous slow, downstream drag when
    /// the blob is flowing, the material's coating (oil slip, slime stick),
    /// and heat from molten blobs. Frozen puddles restore every ignore.
    public class LiquidVolume : MonoBehaviour
    {
        public Matter Owner;

        readonly System.Collections.Generic.List<Collider> _ignored =
            new System.Collections.Generic.List<Collider>();
        float _coatTick;

        static bool IsWader(Collider c) =>
            c.GetComponentInParent<SimpleFPSController>() != null
            || c.GetComponentInParent<Creature>() != null;

        void OnTriggerEnter(Collider other)
        {
            if (Owner == null || Owner.Core == null || other.isTrigger) return;
            if (!IsWader(other)) return;
            Physics.IgnoreCollision(Owner.Core, other, true);
            _ignored.Add(other);
        }

        void OnTriggerStay(Collider other)
        {
            if (Owner == null || other.isTrigger) return;

            float dt = Time.fixedDeltaTime;
            var flow = Owner.Body != null ? Owner.Body.linearVelocity : Vector3.zero;
            bool flowing = flow.sqrMagnitude > 0.2f;

            var pilot = other.GetComponentInParent<SimpleFPSController>();
            if (pilot != null)
            {
                // viscosity: drag against your own motion; current: the stream
                // carries you with it (Marko: "pull you downstream")
                Vector3 v = pilot.Velocity; v.y *= 0.3f;
                pilot.AddSpellForce(-v * 2.2f, dt);
                if (flowing) pilot.AddSpellForce(flow * 2.5f, dt);
                if (Owner.Stickiness < -0.3f && Random.value < 0.02f)
                    pilot.KnockDown(1f); // the slick pool takes your feet eventually
                if (Owner.BlobTemperature > 150f && Tick(0.5f))
                    pilot.TakeHit(Vector3.zero, 6f); // wading in lava is a CHOICE
                else if (Owner.BlobTemperature < -20f && Tick(0.5f))
                    SpellParticle.GiveHeatTo(other, Owner.BlobTemperature * 0.15f); // icy water CHILLS waders
                return;
            }

            var creature = other.GetComponentInParent<Creature>();
            if (creature != null)
            {
                if (Tick(0.4f))
                {
                    // cold liquid chills waders (mirror of lava — pair-ignores
                    // mean collision chemistry never fires for them)
                    if (Owner.BlobTemperature < -20f)
                        SpellParticle.GiveHeatTo(other, Owner.BlobTemperature * 0.15f);

                    // DARK liquid blinds whoever wades it (identity preserved —
                    // it's still darkness, just wet)
                    if (Owner.DarkAura) creature.ApplyBlind(2f);

                    // SLICK liquid: negative stickiness = the slip-ragdoll pool
                    if (Owner.Stickiness < -0.3f) creature.ApplySlip(2f);

                    // HEAVY liquid (liquid rock): a flowing blob crushes waders —
                    // the weight IS the damage (the pair-ignore bypasses React)
                    if (Owner.Density > 1.8f && flow.magnitude > 2f)
                    {
                        var dmgc = creature.GetComponent<Damageable>();
                        if (dmgc != null) dmgc.TakeDamage(flow.magnitude * 3f, "crushed by heavy liquid");
                    }

                    // the coating rules that used to live on collision — the
                    // pair-ignore means collision never fires for waders now
                    if (Owner.BlobTemperature > 150f)
                        SpellParticle.GiveHeatTo(other, Owner.BlobTemperature * 0.15f);
                    else if (Owner.Material == SurfaceMaterialType.Coal) creature.ApplySlip(2f);   // oil
                    else if (Owner.Material == SurfaceMaterialType.Slime) creature.ApplyStuck(2.5f);
                    else if (Owner.Material == SurfaceMaterialType.Wood) creature.ApplyStuck(1.5f); // sap
                    else creature.ApplyStuck(0.3f); // plain wading is just slow
                }
                var rb = other.attachedRigidbody;
                if (rb != null && !rb.isKinematic && flowing)
                    rb.AddForce(flow * 1.5f, ForceMode.Acceleration); // downstream
            }
        }

        bool Tick(float period)
        {
            _coatTick -= Time.fixedDeltaTime;
            if (_coatTick > 0f) return false;
            _coatTick = period;
            return true;
        }

        /// Ice is a floor again: undo every pair-ignore this shell created.
        public void RestoreCollisions()
        {
            if (Owner != null && Owner.Core != null)
                foreach (var c in _ignored)
                    if (c != null) Physics.IgnoreCollision(Owner.Core, c, false);
            _ignored.Clear();
        }

        void OnDestroy() => RestoreCollisions();
    }
}
