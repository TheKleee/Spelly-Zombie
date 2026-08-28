using UnityEngine;

namespace SpellyZombie
{
    public enum MatterPhase { Solid, Liquid, Gas }

    /// A block of conjured matter: Solid or Liquid of the surface's material.
    /// The material table drives melt/freeze/boil/burn (wood to coal), fragment
    /// on density-down, transmute on density-up. Wears SurfaceMaterialTag so seals chain on it.
    public class Matter : MonoBehaviour
    {
        public SurfaceMaterialType Material = SurfaceMaterialType.Unknown;
        public MatterPhase Phase = MatterPhase.Solid;

        /// Seal side-count = shape identity; on merge the higher count wins.
        public int Edges;
        /// Once held it is a world object: no spell lifetime; only solids can be grabbed again.
        public bool Touched;
        /// ★ NOT A FIELD ANY MORE. Temperature lives on the Element, once, so
        /// a crate and a zombie and a wall are all hot in the same place and by
        /// the same number. Matter used to keep its own, which is why heating
        /// a crate and heating a zombie were two unrelated pieces of code.
        public float Temperature
        {
            get => El != null ? El.Data.Temp : _looseTemp;
            set
            {
                if (El == null) { _looseTemp = value; return; }
                var d = El.Data; d.Temp = value; El.Data = d;
            }
        }
        float _looseTemp = Element.RoomTemp;   // only before an Element exists

        Element _el;
        Element El
        {
            get
            {
                if (_el == null) _el = GetComponent<Element>();
                // matter is spawned at runtime, so it may arrive before any
                // editor pass has seen it; it is a solid thing in the world and
                // therefore an element, always
                if (_el == null && this != null) _el = gameObject.AddComponent<Element>();
                return _el;
            }
        }
        public float Density = 1f;
        public float Stickiness = 0.35f;

        // matter chains like particles do
        public ulong Lineage;       // the seal's rune ancestry - matter is a Demon path too
        public int FormLevel = 1;   // 2 = lvl2 form: solid grows on its own, liquid spreads
        public bool DarkAura;       // solid/liquid darkness: blinds whatever touches it

        /// THE TEAM CHAIN (his rule: wizard team, acolyte team, neutral).
        /// -1 = Neutral/Environment; a player id means this matter came from
        /// that player and every fragment, split copy and golem born of it
        /// stays on their team. Serialized on purpose: Instantiate-based
        /// splits copy it for free.
        public int TeamOwner = -1;

        /// Stamp the team, and the blame channel with it.
        public void StampOwner(int owner)
        {
            if (owner < 0) return;
            TeamOwner = owner;
            var stampEl = GetComponent<Element>();
            if (stampEl != null) stampEl.Owner = owner;
        }

        const float MinFragmentSize = 0.07f;   // fragments below this stop splitting
        const float TransmuteAt = 3.2f;        // density needed to jump a tier
        const float FragmentAt = 0.35f;        // density where a solid falls apart
        const int MaxAlive = 90;               // world matter cap (multiplayer grief-proofing)

        static readonly System.Collections.Generic.List<Matter> All = new System.Collections.Generic.List<Matter>();
        /// List, not IReadOnlyList: foreach over the interface boxes the enumerator.
        public static System.Collections.Generic.List<Matter> Living => All;

        MaterialInfo _info;
        Rigidbody _rb;
        Renderer _rend;
        Collider _core;
        LiquidVolume _shell;      // trigger shell while Liquid/Gas - wading effects
        MatterPhase _shellPhase = (MatterPhase)(-1); // last phase the colliders were synced for
        SurfaceMaterialTag _tag;
        MaterialPropertyBlock _mpb;
        float _age, _life = 20f, _integrity = 1f, _baseSize = 0.3f, _slump;
        bool _ice, _burning;
        int _lastLook = int.MinValue;

        public Collider Core => _core;
        public Rigidbody Body => _rb;

        /// Wire look byte for client proxies: 1 burning · 2 molten-glow · 4 ice · 8 dark (netcode §3).
        public byte NetLook => (byte)((_burning ? 1 : 0) | (Temperature > 300f ? 2 : 0)
            | (_ice ? 4 : 0) | (DarkAura ? 8 : 0));

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

            // a meltable material's liquid form is born hot (stone -> lava)
            Temperature = phase == MatterPhase.Liquid && _info.Meltable ? _info.MeltPoint + 150f : 18f;

            // conjured solid water is ice, and ice must melt
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
                All.RemoveAt(0); // remove now; deferred OnDestroy overshoots the cap
                Destroy(oldest.gameObject);
            }
        }

        /// A planted (kinematic) spike that melts must FLOW, not hover.
        void MeltFree()
        {
            if (_rb != null) _rb.isKinematic = false;
        }

        /// Liquids are walk-through: the real collider stays (puddle rests on
        /// ground, chemistry still collides) but pair-ignores creatures/players;
        /// a trigger shell applies wading effects. Freezing restores the ignores.
        void SyncPhaseCollision()
        {
            if (Phase == _shellPhase) return;
            _shellPhase = Phase;
            bool passable = Phase != MatterPhase.Solid;
            // layer 4 (Water): the player capsule excludes it (SimpleFPSController); layer 0 ground still holds the puddle
            gameObject.layer = passable ? 4 : 0;
            if (passable && _shell == null)
            {
                var go = new GameObject("LiquidShell");
                go.transform.SetParent(transform, false);
                var trig = go.AddComponent<SphereCollider>();
                trig.isTrigger = true;
                trig.radius = 0.85f; // in blob-local space - scales with the puddle
                _shell = go.AddComponent<LiquidVolume>();
                _shell.Owner = this;
            }
            else if (!passable && _shell != null)
            {
                _shell.RestoreCollisions(); // a frozen puddle is a floor
                Destroy(_shell.gameObject);
                _shell = null;
            }
        }

        void OnDestroy() => All.Remove(this);

        public void AddHeat(float d) => Temperature += d / Mathf.Max(0.2f, _info.HeatCapacity);
        public void AddDensity(float d) => Density = Mathf.Clamp(Density + d, 0.05f, 6f);
        // full [-1, 1] range: negative stickiness is the slick payload; Clamp01 would erase it
        public void AddStickiness(float d) => Stickiness = Mathf.Clamp(Stickiness + d, -1f, 1f);

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            Temperature = Mathf.MoveTowards(Temperature, 18f, DrawingConfig.AmbientDriftPerSec * 0.4f * dt);

            // gas gets no gravity
            if (_rb != null) _rb.useGravity = Phase != MatterPhase.Gas;

            // spell-phase blobs seek each other; liquids/gasses keep merging as world objects
            if (Phase != MatterPhase.Solid || !Touched)
            {
                _kinTick -= dt;
                if (_kinTick <= 0f)
                {
                    _kinTick = 0.3f;
                    Matter near = null;
                    float best = 2.6f * 2.6f;
                    foreach (var m2 in All)
                    {
                        if (!CanFuse(this, m2)) continue;
                        float d2 = (m2.transform.position - transform.position).sqrMagnitude;
                        if (d2 < best) { best = d2; near = m2; }
                    }
                    if (near != null && _rb != null)
                    {
                        // merge by proximity: liquid cores are pair-ignored, collision events can't be trusted
                        float reach = (transform.localScale.x + near.transform.localScale.x) * 0.55f;
                        if (best < reach * reach && GetInstanceID() < near.GetInstanceID())
                            CombineWith(near);
                        else if (!Touched)
                            // only spell-phase blobs chase; a claimed puddle never crawls
                            _rb.AddForce((near.transform.position - transform.position).normalized * 1.6f,
                                ForceMode.VelocityChange);
                    }
                }
            }

            switch (Phase)
            {
                case MatterPhase.Liquid:
                    if (_info.Type == SurfaceMaterialType.Water && Temperature <= 0f) { Phase = MatterPhase.Solid; _ice = true; _slump = 0f; }
                    else if (_info.Meltable && Temperature < _info.MeltPoint - 120f) { Phase = MatterPhase.Solid; _slump = 0f; } // lava cools to stone
                    else if (Temperature >= _info.BoilPoint) Phase = MatterPhase.Gas;
                    else if (Density <= 0.4f) Phase = MatterPhase.Gas;  // thinned to vapor - floats away
                    // the only place a liquid's size moves: swell up after a merge; nothing shrinks a liquid
                    if (transform.localScale.x < _baseSize)
                        transform.localScale = Vector3.one * Mathf.MoveTowards(
                            transform.localScale.x, _baseSize, _baseSize * 0.9f * dt);
                    else if (Density >= 2.5f)                           // compressed liquid solidifies
                    {
                        Phase = MatterPhase.Solid;
                        _ice = _info.Type == SurfaceMaterialType.Water; // water  ice; heat melts it back
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
                // barely rises, mostly grows
                    if (_rb)
                    {
                        _rb.AddForce(Vector3.up * Mathf.Max(0.04f, (2.6f - Density) * 0.06f),
                            ForceMode.Acceleration);
                        var v = _rb.linearVelocity;
                        if (v.y > DrawingConfig.GasRiseSpeed) v.y = DrawingConfig.GasRiseSpeed;
                        // sideways drift bleeds off so the cloud hangs where it was made
                        v.x *= 1f - Mathf.Clamp01(0.9f * dt);
                        v.z *= 1f - Mathf.Clamp01(0.9f * dt);
                        _rb.linearVelocity = v;
                    }
                    // spread eases in over 0.4s to avoid a frame-one pop
                    float bloom = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_age / 0.4f));
                    // spread stops when the fade begins
                    if (_age < DrawingConfig.GasLifeSeconds - 1.5f)
                        transform.localScale = Vector3.one * Mathf.MoveTowards(
                            transform.localScale.x,
                            _baseSize * DrawingConfig.GasSpreadMax,
                            _baseSize * DrawingConfig.GasSpreadPerSec * bloom * dt);
                    break;
            }

            // flammable material boiled into gas wears the green warning cloud
            if (Phase != _lastPhase && Phase == MatterPhase.Gas && _info.Flammable && FxLibrary.I != null)
                FxLibrary.Spawn(FxLibrary.I.GasCloud, transform.position, transform, 3f);
            _lastPhase = Phase;

            SyncPhaseCollision(); // phase flips flip walk-through-ness with them

            // lvl2 FORMS (two State runes in one seal): a solid grows slowly on
            // its own; a liquid rapidly spreads - sheds smaller blobs around it
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
                        child.Temperature = Temperature; // burning sap spreads burning
                        child.Lineage = Lineage;
                        // sheds share a family id (see _shedFamily)
                        if (_shedFamily == 0) _shedFamily = _nextShedFamily++;
                        child._shedFamily = _shedFamily;
                    }
                }
            }

            Cohere(); // fake joints, every phase - strength graded by phase

            // sustained compression jumps the material a tier: woodcoaldiamond
            if (Density >= TransmuteAt && _info.DenserForm != Material) Transmute();

            if (_burning)
            {
                _integrity -= dt * 0.5f / Mathf.Max(0.3f, _info.Strength);
                Temperature += 40f * dt; // feeds itself; frost can still win
                if (Temperature < _info.IgnitePoint - 30f)
                {
                    _burning = false; // doused
                    GrammarFX.PuffBurst(transform.position, new Color(0.92f, 0.94f, 0.97f, 0.5f), 4);
                }
                if (_integrity <= 0f)
                {
                    // wood burns down to a lump of coal - the alchemy chain starts here
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

            // touched matter never expires; gas still disperses
            if (!Touched || Phase == MatterPhase.Gas)
            {
                // a cloud grows for most of its life, then fades
                float life = Phase == MatterPhase.Gas ? DrawingConfig.GasLifeSeconds : _life;
                if (Phase == MatterPhase.Gas)
                {
                    // end-of-life: gone in one beat - nothing shrinks out (his rule)
                    if (_age > life) Destroy(gameObject);
                }
                else if (_age > life)
                {
                    // no shrink-out: the blob just goes; a liquid leaves a splash
                    if (Phase == MatterPhase.Liquid && FxLibrary.I != null)
                        FxLibrary.SpawnTinted(FxLibrary.I.Splash, transform.position, BodyColor());
                    Destroy(gameObject);
                }
            }
        }

        /// Fake joints: same-material, same-phase blobs spring toward a rest
        /// distance. Liquid strength = Stickiness (strongest), solid weak, gas
        /// weakest; liquids also velocity-match and merge on contact.
        float _cohereTimer;
        float _spreadTick; // lvl2 liquid: cadence of shedding smaller blobs

        // Sheds from one source share a family id; family members don't fuse,
        // chase, or cohere-drink each other while within 1.5x merge reach -
        // prevents a shed/merge size oscillation. Separated family is social again.
        int _shedFamily;              // 0 = no family - fuses freely
        static int _nextShedFamily = 1;

        bool GracedAgainst(Matter o)
        {
            if (_shedFamily == 0 || o._shedFamily != _shedFamily) return false;
            float reach = (transform.localScale.x + o.transform.localScale.x) * 0.55f;
            return (o.transform.position - transform.position).sqrMagnitude
                <= reach * reach * 2.25f; // still huddled - spare each other
        }

        static bool FuseGraced(Matter a, Matter b) => a.GracedAgainst(b);

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
                    // shed grace gates the drink too; the spring rests a shed child near absorb distance
                    if (d < 0.22f && iAbsorb && _baseSize < 1.3f && !FuseGraced(this, o))
                    {
                        // the bigger blob drinks the smaller - volumes add
                        _baseSize = Mathf.Min(1.4f, Mathf.Pow(
                            _baseSize * _baseSize * _baseSize + o._baseSize * o._baseSize * o._baseSize, 1f / 3f));
                        Temperature = (Temperature + o.Temperature) * 0.5f;
                        _slump = 0f; // re-settle at the new size
                        if (_shedFamily == 0) _shedFamily = o._shedFamily; // the family survives the drink
                        Destroy(o.gameObject);
                        continue;
                    }
                }

                // spring toward rest distance; overlap pushes apart
                float rest = (_baseSize + o._baseSize) * 0.55f;
                _rb.AddForce(to.normalized * (d - rest) * k, ForceMode.Acceleration);

                // soft-body velocity matching: the blob wobbles, then settles
                if (liquid && o._rb != null)
                    _rb.AddForce((o._rb.linearVelocity - _rb.linearVelocity) * 0.3f,
                        ForceMode.Acceleration);
            }
        }

        /// Relaxes a liquid into a puddle where it stands.
        void Slump(float dt)
        {
            _slump = Mathf.MoveTowards(_slump, 1f, dt / 1.2f);

            // scale stays uniform; the shader's squash owns the pooling look
            // (per-renderer MPB - the material itself stays shared)
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
                    m.StampOwner(TeamOwner);
                    if (m.TryGetComponent<Rigidbody>(out var rb) && _rb != null)
                        rb.linearVelocity = _rb.linearVelocity + Random.insideUnitSphere * 1.2f;
                }
            }
            Destroy(gameObject);
        }

        /// Density-up compressed this into its stronger form (stone  diamond).
        void Transmute()
        {
            Rebecome(_info.DenserForm, _baseSize * 0.8f);
            WorldEvents.Report(WorldEventKind.Sparkle, transform.position, 1.2f);
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

        /// Contact chemistry.
        void OnCollisionEnter(Collision col) => React(col, true);
        void OnCollisionStay(Collision col)
        {
            _reactCooldown -= Time.fixedDeltaTime;
            if (_reactCooldown <= 0f) { _reactCooldown = 0.5f; React(col, false); }
        }
        float _reactCooldown;
        float _kinTick; // spell-phase blob attraction beat
        MatterPhase _lastPhase = MatterPhase.Solid; // gas-cloud FX fires on the flip

        static MaterialPropertyBlock _colorProbe;

        /// The blob's current visible colour - a tinted blob (the goo) hands
        /// it to every effect it makes, so a green thing splashes green.
        Color BodyColor()
        {
            var r = GetComponentInChildren<Renderer>();
            if (r == null) return Color.white;
            if (_colorProbe == null) _colorProbe = new MaterialPropertyBlock();
            r.GetPropertyBlock(_colorProbe);
            var c = _colorProbe.GetColor("_BaseColor");
            if (c.maxColorComponent > 0.01f) return c;
            return r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")
                ? r.sharedMaterial.GetColor("_BaseColor") : Color.white;
        }

        void React(Collision col, bool impact)
        {
            // a landing liquid blob throws a splash - in ITS colour, so green
            // goo splashes green (his rule: the effect inherits the parent)
            if (impact && Phase == MatterPhase.Liquid && col.contactCount > 0 && FxLibrary.I != null)
                FxLibrary.SpawnTinted(FxLibrary.I.Splash, col.GetContact(0).point, BodyColor());

            // a flying block damages by momentum, nothing below the threshold
            if (impact && _rb != null)
            {
                var hitPl = col.collider.GetComponentInParent<SimpleFPSController>();
                if (hitPl != null)
                {
                    float momentum = _rb.mass * col.relativeVelocity.magnitude;
                    float dmg = Mathf.Max(0f, momentum - 14f) * 0.6f;
                    if (dmg > 0.5f)
                    {
                        hitPl.TakeHit(-col.relativeVelocity * 0.4f, dmg, "hit by flying matter");
                        // momentum decides the knockdown, not the damage math
                        if (momentum > 22f)
                            hitPl.KnockDown(Mathf.Min(2f, 0.6f + momentum * 0.03f));
                            if (FxLibrary.I != null)
                            FxLibrary.Spawn(FxLibrary.I.TextPow,
                                hitPl.transform.position + Vector3.up * 1.9f);
                    }
                }
            }
            var other = col.collider.GetComponent<Matter>();

            // merge on contact: same+same grows a level; solid+liquid joins into liquid
            if (CanFuse(this, other) && GetInstanceID() < other.GetInstanceID())
            {
                CombineWith(other);
                return;
            }

            // lava + water: stone + steam
            if (other != null && Phase == MatterPhase.Liquid && Temperature > 300f
                && other.Material == SurfaceMaterialType.Water && other.Phase == MatterPhase.Liquid)
            {
                Temperature = 60f;                    // quenched - solidifies next frame
                other.Temperature = other._info.BoilPoint + 10f; // flashes to steam
                return;
            }

            // hot matter heats what it touches
            if (Temperature > 150f)
            {
                if (other != null) other.AddHeat(Temperature * 0.25f);
                var thermal = col.collider.GetComponentInParent<Thermal>();
                if (thermal != null) thermal.AddHeat(Temperature * 0.15f);
            }

            // cold matter chills what touches it
            if (Temperature < -20f)
                SpellParticle.GiveHeatTo(col.collider, Temperature * 0.15f);

            // solid/liquid darkness blinds on touch
            var creatureEarly = col.collider.GetComponentInParent<Creature>();
            if (DarkAura && creatureEarly != null) creatureEarly.ApplyBlind(2f);

            // puddle coatings: oil = slip, slime/sap = glued
            var creature = col.collider.GetComponentInParent<Creature>();
            if (creature != null && Phase == MatterPhase.Liquid && Temperature < 150f)
            {
                if (Material == SurfaceMaterialType.Coal) creature.ApplySlip(2f);          // oil
                else if (Material == SurfaceMaterialType.Slime) creature.ApplyStuck(2.5f); // gel trap
                else if (Material == SurfaceMaterialType.Wood) creature.ApplyStuck(1.5f);  // sap
            }

            // sticky solid is a carrier: what lands on it rides it
            if (impact && Phase == MatterPhase.Solid && Stickiness > 0.7f)
            {
                if (creature != null) creature.ApplyStuck(1.2f);
                var rider = col.rigidbody;
                if (rider != null && !rider.isKinematic && _rb != null
                    && rider.GetComponent<FixedJoint>() == null)
                {
                    var joint = rider.gameObject.AddComponent<FixedJoint>();
                    joint.connectedBody = _rb;
                    // break force scales with stickiness; full Sticky3 is unbreakable
                    joint.breakForce = StickyBonds.BreakForce(Stickiness);
                    joint.breakTorque = joint.breakForce;
                }
            }

            // solids crush by momentum; heavy liquid (density > 1.8) crushes too
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
                        var d = col.collider.GetComponentInParent<Element>();
                        if (d != null) d.TakeDamage(dmg, $"crushed by {Material}");
                    }
                    // thud at the impact point, comic WHAM on big damage
                    if (FxLibrary.I != null && col.contactCount > 0)
                    {
                        Vector3 hitAt = col.GetContact(0).point;
                        FxLibrary.Spawn(FxLibrary.I.HitThud, hitAt);
                        if (dmg > 25f)
                            FxLibrary.Spawn(FxLibrary.I.TextWham, hitAt + Vector3.up * 0.7f);
                    }
                }
            }
        }

        /// Merge rules: same material only; gas only with gas; liquids/gasses
        /// keep merging as world objects; two claimed solids stay separate;
        /// molten pairs go to the lava/water chemistry instead.
        static bool CanFuse(Matter a, Matter b)
        {
            if (a == null || b == null || a == b) return false;
            // shed-family grace (see _shedFamily); also kills the chase, which targets CanFuse partners
            if (FuseGraced(a, b)) return false;
            // different materials never combine
            if (a.Material != b.Material) return false;
            // gas mixes only with gas
            if ((a.Phase == MatterPhase.Gas) != (b.Phase == MatterPhase.Gas)) return false;

            // solid + liquid of the same material always join, even when the solid is a claimed world object
            if (a.Phase != b.Phase) return a.Temperature < 300f && b.Temperature < 300f;

            // two claimed solids stay separate so the world doesn't weld itself together
            if (a.Phase == MatterPhase.Solid && (a.Touched || b.Touched)) return false;
            return a.Temperature < 300f && b.Temperature < 300f;
        }

        void CombineWith(Matter o)
        {
            // solid + liquid becomes liquid
            bool melted = Phase != o.Phase;
            float merged = Mathf.Pow(
                Mathf.Pow(transform.localScale.x, 3f) + Mathf.Pow(o.transform.localScale.x, 3f), 1f / 3f);
            Lineage |= o.Lineage;
            // absorbing a family member joins the family, else a stranger bridges the shed grace
            if (_shedFamily == 0) _shedFamily = o._shedFamily;
            if (melted)
            {
                Phase = MatterPhase.Liquid;
                SyncPhaseCollision();   // it's walk-through from this moment
                DrawingWorld.Instance?.LogEvent($"the {Material} dissolves into the pool");
            }
            else
            {
                FormLevel = Mathf.Min(2, Mathf.Max(FormLevel, o.FormLevel) + 1);
                // LEVEL 2 IS A GOLEM, NOT A BIGGER LUMP. Same phase meeting
                // same phase stands up and walks - solid, liquid or gas alike.
                if (FormLevel >= 2 && RiseAsGolem(merged, o)) return;
            }
            // the survivor keeps the higher seal side-count
            Edges = Mathf.Max(Edges, o.Edges);
            _baseSize = merged;
            // only a solid snaps to the new size; fluids grow into it in Update
            if (Phase == MatterPhase.Solid)
                transform.localScale = Vector3.one * merged;
            _age = 0f; // a fresh thing
            var lib = FxLibrary.I; // both halves show FX at the meeting
            if (lib != null)
            {
                FxLibrary.SpawnTinted(lib.Poof, transform.position, BodyColor());
                FxLibrary.SpawnTinted(melted || Phase == MatterPhase.Liquid ? lib.Splash : lib.HitThud,
                    o.transform.position, BodyColor());
            }
            RuneGrammar.TryDemon(Lineage, transform.position, merged);
            Destroy(o.gameObject);
        }

        /// LEVEL 2 STANDS UP. Two of the same phase meeting no longer make a
        /// bigger lump - they make a GOLEM, whatever the phase: a solid one,
        /// a liquid one, a gas one. It inherits this matter's size, material
        /// and the ground it was born on. Returns false (and leaves the merge
        /// alone) when there is no golem prefab to raise.
        bool RiseAsGolem(float mergedSize, Matter eaten)
        {
            // TWO BECOMING ONE STILL ADDS UP: mergedSize is the volume-summed
            // size of both halves, so a golem raised from two blobs is bigger
            // than one raised from either - the same growth every other
            // particle gets, just standing on legs.
            var g = Golem.Spawn(transform.position,
                mergedSize * DrawingConfig.GolemSizePerMatter);
            if (g == null) return false;

            // TEAMS SURVIVE THE CHAIN (his rule): rock, water, fragments,
            // debris - whatever a spell made stays on its team, and so does
            // the golem rising from it. True world matter stays Neutral.
            int owner = TeamOwner >= 0 ? TeamOwner
                : eaten != null ? eaten.TeamOwner : -1;
            if (owner < 0)
            {
                var ms = GetComponent<MatterStrike>();
                if (ms == null && eaten != null) ms = eaten.GetComponent<MatterStrike>();
                if (ms != null) owner = ms.OwnerId;
            }
            g.OwnerId = owner;

            var view = g.GetComponent<StateView>();
            if (view == null) view = g.gameObject.AddComponent<StateView>();
            view.Set(Phase);                       // solid, liquid or gas golem
            view.Tint = _info.SolidColor;
            view.DriveTint = true;

            var tag = g.GetComponent<SurfaceMaterialTag>();
            if (tag == null) tag = g.gameObject.AddComponent<SurfaceMaterialTag>();
            tag.Material = Material;

            var lib2 = FxLibrary.I;
            if (lib2 != null) FxLibrary.Spawn(lib2.Poof, transform.position);
            DrawingWorld.Instance?.LogEvent($"the {Material} stands up");

            if (eaten != null) Destroy(eaten.gameObject);
            Destroy(gameObject);
            return true;
        }

        /// Seal line count picks the shape from the ShapeLibrary asset (a slot
        /// per material per line count); an empty slot falls back to a primitive.
        public static Matter Spawn(SurfaceMaterialType mat, MatterPhase phase, float size,
            Vector3 pos, int edges = 0)
        {
            GameObject go = null;
            bool authored = false;

            if (phase == MatterPhase.Solid && edges > 0 && ShapeLibrary.Any)
            {
                var skin = ShapeLibrary.Find(mat, edges); // mod shelves first, then the game's
                if (skin != null)
                {
                    go = Object.Instantiate(skin, pos, skin.transform.rotation);
                    go.transform.localScale = skin.transform.localScale * size; // the proportions × drawn size
                    authored = true;
                }
            }

            if (go == null)
            {
                go = GameObject.CreatePrimitive(phase == MatterPhase.Solid ? PrimitiveType.Cube : PrimitiveType.Sphere);
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * size;
            }
            go.name = (phase == MatterPhase.Solid ? "Solid_" : "Liquid_") + mat
                    + (authored ? "_" + edges : "");

            Adopt.Component<Rigidbody>(go);           // the settings survive
            var m = Adopt.Component<Matter>(go);
            m.Init(mat, phase, size);
            m.Edges = edges;
            // grabbable from birth (FreeForAll InkMark)
            Adopt.Component<InkMark>(go).FreeForAll = true;
            if (authored && go.GetComponent<Collider>() == null)
                go.AddComponent<BoxCollider>();      // only if authored none
            // non-authored matter wears the soft-body StateBlob
            if (!authored) go.AddComponent<StateBlob>();
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
            // solids stay rigid - their bonds are locked, and it shows
            if (Phase == MatterPhase.Liquid)
                _rend.sharedMaterial = MatterFX.Particle(c, shade, 0.07f, 0.5f);
            else if (Phase == MatterPhase.Gas)
                _rend.sharedMaterial = MatterFX.Particle(c, shade, 0.11f, 0.7f);
            else
                _rend.sharedMaterial = MatterFX.Get(c, shade);
        }
    }

    /// Trigger shell riding a liquid/gas blob: pair-ignores waders against the
    /// real collider and applies wading effects (slow, downstream drag,
    /// coatings, heat). Freezing restores every ignore.
    public class LiquidVolume : MonoBehaviour
    {
        public Matter Owner;

        readonly System.Collections.Generic.List<Collider> _ignored =
            new System.Collections.Generic.List<Collider>();
        // collider -> pilot/creature root, resolved once and cached
        readonly System.Collections.Generic.Dictionary<Collider, Component> _roots =
            new System.Collections.Generic.Dictionary<Collider, Component>();
        float _coatTick;

        Component RootOf(Collider c)
        {
            if (_roots.TryGetValue(c, out var root))
                return root == null ? null : root; // Unity-null guard for dead roots
            root = (Component)c.GetComponentInParent<SimpleFPSController>()
                ?? c.GetComponentInParent<Creature>();
            _roots[c] = root;
            return root;
        }

        void OnTriggerEnter(Collider other)
        {
            if (Owner == null || Owner.Core == null || other.isTrigger) return;
            if (RootOf(other) == null) return; // only waders get the pair-ignore
            Physics.IgnoreCollision(Owner.Core, other, true);
            _ignored.Add(other);
        }

        void OnTriggerStay(Collider other)
        {
            if (Owner == null || other.isTrigger) return;

            float dt = Time.fixedDeltaTime;
            var flow = Owner.Body != null ? Owner.Body.linearVelocity : Vector3.zero;
            bool flowing = flow.sqrMagnitude > 0.2f;

            var root = RootOf(other);
            var pilot = root as SimpleFPSController;
            if (pilot != null)
            {
                // gas never slows a wader; its temperature still applies
                if (Owner.Phase == MatterPhase.Gas)
                {
                    if (Mathf.Abs(Owner.Temperature - 18f) > 40f && Tick(0.5f))
                    {
                        BodyState.Of(pilot)?.PushTemp((Owner.Temperature - 18f) * 0.06f);
                        var lib = FxLibrary.I;
                        if (lib != null)
                        {
                            var at = pilot.transform.position + Vector3.up * 1.3f
                                + Random.insideUnitSphere * 0.25f;
                            var fx = FxLibrary.Spawn(
                                Owner.Temperature > 18f ? lib.HitSpark : lib.IceHit, at);
                            if (fx != null) fx.transform.localScale *= 0.55f;
                        }
                    }
                    return;
                }
                // viscosity: drag against your own motion; current: the stream
                // carries you with it
                Vector3 v = pilot.Velocity; v.y *= 0.3f;
                pilot.AddSpellForce(-v * 2.2f, dt);
                if (flowing) pilot.AddSpellForce(flow * 2.5f, dt);
                if (Owner.Stickiness < -0.3f && Random.value < 0.02f)
                    pilot.KnockDown(1f); // the slick pool takes your feet eventually
                // burn gate is 100°C: steam is born around 130° and must scald
                if (Owner.Temperature > 100f && Tick(0.5f))
                    pilot.TakeHit(Vector3.zero, Owner.Temperature > 150f ? 6f : 3f);
                else if (Owner.Temperature < -20f && Tick(0.5f))
                    SpellParticle.GiveHeatTo(other, Owner.Temperature * 0.15f); // icy water CHILLS waders
                return;
            }

            var creature = root as Creature;
            if (creature != null)
            {
                if (Tick(0.4f))
                {
                    // cold liquid chills waders (collision chemistry never fires for them)
                    if (Owner.Temperature < -20f)
                        SpellParticle.GiveHeatTo(other, Owner.Temperature * 0.15f);

                    // dark liquid blinds waders
                    if (Owner.DarkAura) creature.ApplyBlind(2f);

                    // negative stickiness = the slip pool
                    if (Owner.Stickiness < -0.3f) creature.ApplySlip(2f);

                    // heavy flowing liquid crushes waders (pair-ignore bypasses React)
                    if (Owner.Density > 1.8f && flow.magnitude > 2f)
                    {
                        var dmgc = creature.GetComponent<Element>();
                        if (dmgc != null) dmgc.TakeDamage(flow.magnitude * 3f, "crushed by heavy liquid");
                    }

                    // coating rules live here; collision never fires for waders
                    if (Owner.Temperature > 100f) // past boiling = it burns (steam included)
                        SpellParticle.GiveHeatTo(other, Owner.Temperature * 0.15f);
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
