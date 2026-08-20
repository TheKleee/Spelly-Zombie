using UnityEngine;

namespace SpellyZombie
{
    /// A zombie summoned from a seal: it belongs to its caster and expires.
    /// Separate component so ordinary world zombies are unaffected.
    public class SummonedZombie : MonoBehaviour
    {
        /// Grimoire owner id of the acolyte who drew the seal.
        public int SummonedBy = -1;

        /// Melee zombies got the Solid rune, ranged ones got Liquid.
        public bool Ranged;

        /// The tight aura it breathes while alive - body-sized, not a fog bank.
        public PoisonField Gas => _gas;
        PoisonField _gas;

        /// Radius of the death cloud, and the base a detonation multiplies.
        public float GasRadius { get; private set; } = 1f;

        float _left, _paintRetry;
        bool _painted;
        static MaterialPropertyBlock _block;

        public void Begin(int owner, bool ranged, float seconds, float gasRadius)
        {
            SummonedBy = owner;
            Ranged = ranged;
            _left = seconds;
            // gasRadius drives the DEATH cloud only; alive it keeps a body-tight aura
            GasRadius = gasRadius;
            float bodyHeight = transform.localScale.y * 2f;
            _gas = PoisonField.Open(transform.position + Vector3.up * bodyHeight * 0.35f,
                Mathf.Min(bodyHeight * DrawingConfig.PoisonAuraBodyMul, 1.1f),
                seconds + 1f, transform);
            Paint();
        }

        /// Tints the body: melee and ranged get different greens, readable at range.
        void Paint()
        {
            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return;      // body not built yet; Update retries

            Color c = Ranged ? DrawingConfig.SummonRangedColor : DrawingConfig.SummonMeleeColor;
            // the ground it was raised on pulls the colour, bounded so the
            // melee/ranged read survives
            var stamp = GetComponent<BiomeStamp>();
            if (stamp != null) c = stamp.Shift(c);

            // baked body: property block (shader and maps survive); graybox: MatterFX material
            var dress = GetComponent<ZombieDress>();
            bool custom = dress != null && dress.IsCustomBody;

            foreach (var r in rends)
            {
                if (r == null) continue;
                bool head = r.transform.name == "Head";
                Color mine = head ? c * 1.15f : c;

                if (custom)
                {
                    if (_block == null) _block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(_block);
                    _block.SetColor("_BaseColor", mine);
                    _block.SetColor("_Color", mine);
                    r.SetPropertyBlock(_block);
                }
                else r.sharedMaterial = MatterFX.Get(mine, MoteShade.Opaque);
            }
            _painted = true;
        }

        void Update()
        {
            // the rig may finish building a frame or two after we spawned
            if (!_painted && (_paintRetry -= Time.deltaTime) <= 0f)
            {
                _paintRetry = 0.2f;
                Paint();
            }

            _left -= Time.deltaTime;
            if (_left > 0f) return;

            // expires with no corpse and no drops
            if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.Poof, transform.position);
            Destroy(gameObject);
        }
    }
}
