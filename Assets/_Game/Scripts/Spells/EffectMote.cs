using UnityEngine;

namespace SpellyZombie
{
    /// A carried effect in flight. Direction zones don't invent effects — they
    /// TRANSPORT the other zones in the seal: Heat+arrow streams hot motes that
    /// way (a heat wave, not a fireball); Cold+arrow streams frost; Liquid+arrow
    /// sprays droplets. This is the compositional rule the whole spell system
    /// runs on — a fire BOLT needs Heat + arrow + something solid to throw.
    public class EffectMote : MonoBehaviour
    {
        RuneType _rune;
        float _intensity;
        Vector3 _vel;
        float _life = 1.2f;

        public static void Launch(RuneType rune, float intensity, Vector3 from, Vector3 dir)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Mote_" + rune;
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.12f;
            go.GetComponent<SphereCollider>().isTrigger = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; // moves by script, triggers on contact
            go.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(MoteColor(rune), MoteShade.Additive);

            var m = go.AddComponent<EffectMote>();
            m._rune = rune;
            m._intensity = intensity;
            m._vel = dir.normalized * 7f + Random.insideUnitSphere * 0.5f;
        }

        void Update()
        {
            transform.position += _vel * Time.deltaTime;
            _life -= Time.deltaTime;
            if (_life <= 0f) Destroy(gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.isTrigger || other.GetComponent<EffectMote>() != null) return;
            Apply(other);
            Destroy(gameObject);
        }

        /// One bite of the carried zone's effect, delivered downrange.
        void Apply(Collider c)
        {
            var matter = c.GetComponent<Matter>();
            var creature = c.GetComponentInParent<Creature>();

            switch (_rune)
            {
                case RuneType.HeatUp: Heat(c, matter, 55f * _intensity); break;
                case RuneType.HeatDown: Heat(c, matter, -55f * _intensity); break;

                case RuneType.StickyUp:
                    if (creature != null) creature.ApplyStuck(1.2f * _intensity);
                    if (matter != null) matter.AddStickiness(0.3f);
                    break;
                case RuneType.StickyDown:
                    if (creature != null) creature.ApplySlip(1.2f * _intensity);
                    if (matter != null) matter.AddStickiness(-0.3f);
                    break;

                case RuneType.DensityUp:
                    if (matter != null) matter.AddDensity(0.4f);
                    break;
                case RuneType.DensityDown:
                    if (matter != null) matter.AddDensity(-0.4f);
                    break;

                case RuneType.StateLiquid: // spray: droplets land downrange
                    Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Liquid, 0.14f,
                        transform.position);
                    break;
                case RuneType.StateSolid: // pellet: grit lands downrange
                    Matter.Spawn(SurfaceMaterialType.Stone, MatterPhase.Solid, 0.1f,
                        transform.position);
                    break;

                case RuneType.LuminanceUp: // a spark of light at the impact
                    var l = new GameObject("MoteFlash").AddComponent<Light>();
                    l.transform.position = transform.position;
                    l.type = LightType.Point;
                    l.color = new Color(1f, 0.97f, 0.85f);
                    l.range = 3f;
                    l.intensity = 3f * _intensity;
                    Destroy(l.gameObject, 0.3f);
                    break;
                // LuminanceDown carried = just the dark mote visual
            }
        }

        void Heat(Collider c, Matter matter, float delta)
        {
            if (matter != null) { matter.AddHeat(delta); return; }
            var t = c.GetComponentInParent<Thermal>();
            if (t == null)
            {
                var go = c.attachedRigidbody ? c.attachedRigidbody.gameObject : c.gameObject;

                // character limbs route heat to the BEING, never to a bone
                // (bone rigidbodies made limbs read as burnable props)
                var pilot = c.GetComponentInParent<SimpleFPSController>();
                if (pilot != null) go = pilot.gameObject;
                else
                {
                    var creature = c.GetComponentInParent<Creature>();
                    if (creature != null) go = creature.gameObject;
                }

                // don't cook giant static walls — same guard the zones use
                var rend = go.GetComponentInChildren<Renderer>();
                if (rend == null) return;
                if (c.attachedRigidbody == null && rend.bounds.size.magnitude > DrawingConfig.MaxThermalObjectSize) return;
                t = go.GetComponent<Thermal>();
                if (t == null) t = go.AddComponent<Thermal>();
            }
            t.AddHeat(delta);
        }

        static Color MoteColor(RuneType r)
        {
            switch (r)
            {
                case RuneType.HeatUp: return new Color(1f, 0.5f, 0.12f, 0.9f);
                case RuneType.HeatDown: return new Color(0.6f, 0.85f, 1f, 0.9f);
                case RuneType.StateLiquid: return new Color(0.35f, 0.6f, 0.95f, 0.9f);
                case RuneType.StateSolid: return new Color(0.6f, 0.55f, 0.5f, 0.9f);
                case RuneType.StickyUp: return new Color(0.4f, 0.8f, 0.35f, 0.9f);
                case RuneType.StickyDown: return new Color(0.85f, 0.85f, 0.9f, 0.9f);
                case RuneType.DensityUp: return new Color(0.7f, 0.5f, 0.3f, 0.9f);
                case RuneType.DensityDown: return new Color(0.7f, 1f, 0.8f, 0.9f);
                case RuneType.LuminanceUp: return new Color(1f, 0.97f, 0.8f, 0.9f);
                case RuneType.LuminanceDown: return new Color(0.25f, 0.15f, 0.35f, 0.9f);
                default: return Color.white;
            }
        }
    }
}
