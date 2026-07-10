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
        SurfaceMaterialTag _tag;
        float _age, _life = 20f, _integrity = 1f, _baseSize = 0.3f, _slump;
        bool _ice, _burning;
        int _lastLook = int.MinValue;

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

            // conjured matter is a real surface: you can draw your next seal on it
            _tag = gameObject.GetComponent<SurfaceMaterialTag>();
            if (_tag == null) _tag = gameObject.AddComponent<SurfaceMaterialTag>();
            _tag.Material = mat;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rend = GetComponent<Renderer>();
            All.Add(this);
            if (All.Count > MaxAlive && All[0] != null) Destroy(All[0].gameObject); // oldest yields
        }

        void OnDestroy() => All.Remove(this);

        public void AddHeat(float d) => Temperature += d / Mathf.Max(0.2f, _info.HeatCapacity);
        public void AddDensity(float d) => Density = Mathf.Clamp(Density + d, 0.05f, 6f);
        public void AddStickiness(float d) => Stickiness = Mathf.Clamp01(Stickiness + d);

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
                    else { Slump(dt); Cohere(); }                       // liquids pool AND re-pool
                    break;

                case MatterPhase.Solid:
                    if (_ice && Temperature > 2f) { Phase = MatterPhase.Liquid; _ice = false; }
                    else if (_info.Meltable && Temperature >= _info.MeltPoint) Phase = MatterPhase.Liquid;
                    else if (_info.Flammable && Temperature >= _info.IgnitePoint) _burning = true;
                    if (Density <= FragmentAt) { Fragment(); return; }  // low density: burst into smaller blocks
                    break;

                case MatterPhase.Gas:
                    if (_rb) _rb.AddForce(Vector3.up * 1.4f, ForceMode.Acceleration); // rises
                    break;
            }

            // sustained compression jumps the material a tier: wood→coal→diamond
            if (Density >= TransmuteAt && _info.DenserForm != Material) Transmute();

            if (_burning)
            {
                _integrity -= dt * 0.5f / Mathf.Max(0.3f, _info.Strength);
                Temperature = Mathf.Max(Temperature, _info.IgnitePoint + 80f); // feeds itself
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

        /// Marko's liquid rule (SPELL_PARTICLES.md): same-material liquids hold
        /// weak RE-FORMABLE BONDS — nearby blobs pull together (bond strength IS
        /// Stickiness, so glue makes goo and repel makes mist), and touching
        /// blobs MERGE into one bigger one. Splash a puddle apart and it
        /// re-pools; freeze it and the bonds lock into one solid. Different
        /// materials never bond — chemistry (React) decides for them.
        float _cohereTimer;
        void Cohere()
        {
            _cohereTimer -= Time.deltaTime;
            if (_cohereTimer > 0f) return;
            _cohereTimer = 0.3f;

            for (int i = 0; i < All.Count; i++)
            {
                var o = All[i];
                if (o == null || o == this || o.Phase != MatterPhase.Liquid
                    || o.Material != Material) continue;
                Vector3 to = o.transform.position - transform.position;
                float d = to.magnitude;
                if (d > 1.2f) continue;

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

                if (_rb != null && d > 0.01f)
                    _rb.AddForce(to.normalized * Stickiness * 5f, ForceMode.Acceleration);
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

            // puddle coatings: oil = lose footing (they topple!), slime/sap = glued
            var creature = col.collider.GetComponentInParent<Creature>();
            if (creature != null && Phase == MatterPhase.Liquid && Temperature < 150f)
            {
                if (Material == SurfaceMaterialType.Coal) creature.ApplySlip(2f);          // oil
                else if (Material == SurfaceMaterialType.Slime) creature.ApplyStuck(2.5f); // gel trap
                else if (Material == SurfaceMaterialType.Wood) creature.ApplyStuck(1.5f);  // sap
            }

            // solid blocks crush by momentum (creatures AND crates); frozen shatter
            if (impact && Phase == MatterPhase.Solid && _rb != null)
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
            _rend.sharedMaterial = MatterFX.Get(c, shade);
        }
    }
}
