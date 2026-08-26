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

        /// The spell that raised it, if any - its material sliders go onto the
        /// body, and its colour shades the zombie's own green.
        public SpellDef Spell;

        public void Begin(int owner, bool ranged, float seconds, float gasRadius)
        {
            SummonedBy = owner;
            Ranged = ranged;
            _left = seconds;
            // gasRadius drives the DEATH cloud only; alive it keeps a body-tight aura
            GasRadius = gasRadius;
            float bodyHeight = transform.localScale.y * 2f;
            _gas = PoisonField.Open(transform.position + Vector3.up * bodyHeight * 0.35f,
                Mathf.Min(bodyHeight * DrawingConfig.PoisonAuraBodyMul, 0.88f),
                seconds + 1f, transform);
            Paint();
        }

        /// ★ ONE WRITER. Zombies are green; the spell only shades that, and its
        /// material sliders ride the body - all through StateView, which keeps
        /// the eyes out. This used to REPLACE the body's material with a flat
        /// one on authored bodies (its custom-body check asked the ZombieDress,
        /// and a prefab body has none) - tearing the state material off, which
        /// is why a summon never looked like its spell.
        void Paint()
        {
            if (GetComponentsInChildren<Renderer>(true).Length == 0)
                return;                     // body not built yet; Update retries

            Color c = Ranged ? DrawingConfig.SummonRangedColor : DrawingConfig.SummonMeleeColor;
            // the ground it was raised on pulls the colour, bounded so the
            // melee/ranged read survives
            var stamp = GetComponent<BiomeStamp>();
            if (stamp != null) c = stamp.Shift(c);
            if (Spell != null) c = Color.Lerp(c, Spell.Payload.Tint(), DrawingConfig.BiomeTintStrength);

            var view = GetComponent<StateView>() ?? gameObject.AddComponent<StateView>();
            view.Tint = c;
            view.DriveTint = true;
            if (Spell != null) view.Look = Spell.Skin;
            _painted = true;
        }

        /// ★ THE AURA OUTLIVES ITS OWNER (his rule): freed BEFORE the body
        /// goes, while the field is still a living object - the OnDestroy
        /// version freed a half-destroyed child that never ticked again.
        public void FreeGas()
        {
            if (_gas == null) return;
            _gas.transform.SetParent(null, true);
            _gas.Wearer = null;
            _gas = null;
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

            // expires with no corpse and no drops - but it still shows, or a
            // summon just blinks out of existence with nothing to read
            FreeGas();
            GetComponent<Zombie>()?.DeathPoof("its time ran out");
            Destroy(gameObject);
        }
    }
}
