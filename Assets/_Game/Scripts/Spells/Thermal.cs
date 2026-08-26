using UnityEngine;

namespace SpellyZombie
{
    /// Tracks an object's temperature. Heat zones push it up or down; every frame
    /// it drifts back toward ambient, tints the object (glowing hot / frosted
    /// cold), and bleeds burn or freeze damage past a threshold. Added on demand.
    public class Thermal : MonoBehaviour
    {
        /// The element's, not its own. Thermal is the LOOK of being hot - the
        /// glow and the flames - and nothing else: the burning itself moved to
        /// Element, where everything in the world can feel it.
        public float Temperature
        {
            get => _dmg != null ? _dmg.Data.Temp : _looseTemp;
            set
            {
                if (_dmg == null) { _looseTemp = value; return; }
                var d = _dmg.Data; d.Temp = value; _dmg.Data = d;
            }
        }
        float _looseTemp = Element.RoomTemp;
        public float Ambient = 18f;
        public float HeatCapacity = 1f;

        static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
        static readonly Color HotColor = new Color(1f, 0.25f, 0.05f);
        static readonly Color ColdColor = new Color(0.55f, 0.75f, 1f);

        Renderer _rend;
        MaterialPropertyBlock _mpb;
        Color _baseColor;
        bool _canTint, _dmgSearched, _creatureLimb;
        Element _dmg;

        void Awake()
        {
            GetComponent<Creature>()?.BindThermal(this); // kills Creature's per-frame GetComponent poll
            _rend = GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
            if (_rend != null && _rend.sharedMaterial != null && _rend.sharedMaterial.HasProperty(BaseColorID))
            {
                _baseColor = _rend.sharedMaterial.GetColor(BaseColorID);
                _canTint = true;
            }
            _dmg = GetComponent<Element>();
            // cached once; creatures wear their own flames
            _creatureLimb = GetComponentInParent<Creature>() != null;
        }

        /// delta is raw heat energy; high-capacity materials change slower. Clamped.
        public void AddHeat(float delta) => Temperature = Mathf.Clamp(
            Temperature + delta / Mathf.Max(0.25f, HeatCapacity), -320f, 900f);

        void Update()
        {
            float dt = Time.deltaTime;
            // COOLING IS THE ELEMENT'S JOB - it drifts toward what the ground
            // says, on the world beat. A second cooling loop here fought it.

            if (_canTint)
            {
                Color c = _baseColor;
                if (Temperature > 40f) c = Color.Lerp(_baseColor, HotColor, Mathf.InverseLerp(40f, 220f, Temperature));
                else if (Temperature < 8f) c = Color.Lerp(_baseColor, ColdColor, Mathf.InverseLerp(8f, -20f, Temperature));
                _rend.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorID, c);
                _rend.SetPropertyBlock(_mpb);
            }

            // one late look: GiveHeat adds the Element after this Thermal, so Awake missed it
            if (_dmg == null && !_dmgSearched)
            {
                _dmgSearched = true;
                _dmg = GetComponent<Element>();
            }
            // BURNING IS THE ELEMENT'S JOB TOO, measured from what the thing
            // naturally is. Doing it here as well meant double damage, and it
            // could only ever reach things somebody remembered to add a
            // Thermal to.

            // burning wood visibly burns; creatures have their own flame system
            bool ablaze = Temperature > DrawingConfig.BurnThreshold;
            if (ablaze && _flames == null && !_creatureLimb)
            {
                var lib = FxLibrary.I;
                if (lib != null && lib.Fire != null)
                {
                    _flames = Object.Instantiate(lib.Fire, transform);
                    _flames.transform.localPosition = Vector3.zero;
                    // undo the parent's scale so the flame keeps its authored size
                    var ls = transform.lossyScale;
                    float inv = 1f / Mathf.Max(0.01f, Mathf.Max(ls.x, Mathf.Max(ls.y, ls.z)));
                    _flames.transform.localScale = Vector3.one * inv;
                }
            }
            else if (!ablaze && _flames != null)
            {
                Destroy(_flames);
                _flames = null;
            }
        }

        GameObject _flames;
    }
}
