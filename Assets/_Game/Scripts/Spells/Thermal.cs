using UnityEngine;

namespace SpellyZombie
{
    /// Tracks an object's temperature. Heat zones push it up or down; every frame
    /// it drifts back toward ambient (second law of thermodynamics), tints the
    /// object (glowing hot / frosted cold), and bleeds burn or freeze damage when
    /// it crosses a threshold. Added on demand the first time a heat zone touches
    /// an object.
    public class Thermal : MonoBehaviour
    {
        public float Temperature = 18f;
        public float Ambient = 18f;
        public float HeatCapacity = 1f;

        static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        static readonly Color HotColor = new Color(1f, 0.25f, 0.05f);
        static readonly Color ColdColor = new Color(0.55f, 0.75f, 1f);

        Renderer _rend;
        MaterialPropertyBlock _mpb;
        Color _baseColor;
        bool _canTint;
        Damageable _dmg;

        void Awake()
        {
            _rend = GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
            if (_rend != null && _rend.sharedMaterial != null && _rend.sharedMaterial.HasProperty(BaseColorID))
            {
                _baseColor = _rend.sharedMaterial.GetColor(BaseColorID);
                _canTint = true;
            }
            _dmg = GetComponent<Damageable>();
        }

        /// delta is raw heat energy; heavy/high-capacity materials change slower.
        public void AddHeat(float delta) => Temperature += delta / Mathf.Max(0.25f, HeatCapacity);

        void Update()
        {
            float dt = Time.deltaTime;
            Temperature = Mathf.MoveTowards(Temperature, Ambient, DrawingConfig.AmbientDriftPerSec * dt);

            if (_canTint)
            {
                Color c = _baseColor;
                if (Temperature > 40f) c = Color.Lerp(_baseColor, HotColor, Mathf.InverseLerp(40f, 220f, Temperature));
                else if (Temperature < 8f) c = Color.Lerp(_baseColor, ColdColor, Mathf.InverseLerp(8f, -20f, Temperature));
                _rend.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorID, c);
                _rend.SetPropertyBlock(_mpb);
            }

            if (_dmg == null) _dmg = GetComponent<Damageable>();
            if (_dmg != null)
            {
                if (Temperature > DrawingConfig.BurnThreshold)
                    _dmg.TakeDamage(DrawingConfig.BurnDamagePerSec * dt, "burning");
                else if (Temperature < DrawingConfig.FreezeThreshold)
                    _dmg.TakeDamage(DrawingConfig.FreezeDamagePerSec * dt, "freezing");
            }
        }
    }
}
