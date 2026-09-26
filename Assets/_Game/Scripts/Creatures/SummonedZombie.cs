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

        /// The creature it is, if any - its material sliders go onto the body,
        /// and its colour shades the zombie's own green.
        public CreatureDef Creature;

        /// Seconds of life left: the smallest is the oldest.
        public float Left => _left;

        /// Spreading: it comes apart where it fell into `count` smaller ones
        /// of the same kind, weaker, with what life it had left. One
        /// generation: the halves inherit no rune.
        public void SplitInto(int count, float scaleMul, float strengthMul)
        {
            var me = GetComponent<Zombie>();
            var myEl = GetComponent<Element>();
            var myRb = GetComponent<Rigidbody>();
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f;
                Vector3 spot = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f + Vector3.up * 0.2f;
                var z = Zombie.Spawn(spot);
                if (z == null) return;
                var half = z.gameObject.AddComponent<SummonedZombie>(); // before Wear (the law)
                if (Creature != null) z.Wear(Creature);
                else z.Abilities.Add(Ranged ? "Goo" : Zombie.Charge);
                z.transform.localScale = transform.localScale * scaleMul;
                var rb = z.GetComponent<Rigidbody>();
                if (rb != null && myRb != null)
                    rb.mass = Mathf.Max(DrawingConfig.SummonMinMass, myRb.mass * scaleMul * scaleMul * scaleMul);
                var el = z.GetComponent<Element>();
                if (el != null && myEl != null)
                {
                    el.MaxStrength = Mathf.Max(DrawingConfig.SummonMinStrength, myEl.MaxStrength * strengthMul);
                    el.Health = el.MaxStrength;
                }
                if (me != null) z.AttackDamage = me.AttackDamage * strengthMul;
                half.Begin(SummonedBy, Ranged, Mathf.Max(5f, _left), GasRadius * scaleMul);
                BiomeStamp.Apply(z.gameObject, spot);
                var brain = z.GetComponent<ZombieBrain>();
                if (brain != null)
                {
                    brain.StrikesTurnedBacks = true;
                    brain.CopyHomeFrom(GetComponent<ZombieBrain>());
                }
            }
        }

        /// The island holds DrawingConfig.ZombieBudget summons. This one is the
        /// newest, so when it is one too many the summoner's oldest crumbles,
        /// or the oldest anywhere when they had none.
        void EnforceBudget()
        {
            int others = 0;
            SummonedZombie mineOldest = null, anyOldest = null;
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                var s = z.GetComponent<SummonedZombie>();
                if (s == null || s == this || s._left <= 0f) continue;
                others++;
                if (s.SummonedBy == SummonedBy && (mineOldest == null || s._left < mineOldest._left)) mineOldest = s;
                if (anyOldest == null || s._left < anyOldest._left) anyOldest = s;
            }
            if (others < DrawingConfig.ZombieBudget) return;
            var gone = mineOldest ?? anyOldest;
            if (gone != null) gone._left = 0f; // its own Update runs the death path
        }

        /// It serves `owner` now (the Life Needle): its aura changes sides with it, here and on every client.
        public void Serve(int owner)
        {
            SummonedBy = owner;
            if (_gas == null) return;
            _gas.Team = owner < 0 ? (Side?)null : Sides.Of(owner);
            _gas.Owner = owner;
            _gas.OwnerViaMinion = owner >= 0;
            NetSync.PushField(2, _gas.transform.position, _gas.Radius, Mathf.Max(1f, _left + 1f),
                gameObject.GetInstanceID(), owner, owner >= 0);
        }

        public void Begin(int owner, bool ranged, float seconds, float gasRadius)
        {
            SummonedBy = owner;
            Ranged = ranged;
            _left = seconds;
            EnforceBudget();
            // gasRadius drives the DEATH cloud only; alive it keeps a body-tight aura
            GasRadius = gasRadius;
            float bodyHeight = transform.localScale.y * 2f;
            float auraRadius = Mathf.Min(bodyHeight * DrawingConfig.PoisonAuraBodyMul, 0.88f);
            _gas = PoisonField.Open(transform.position + Vector3.up * bodyHeight * 0.35f,
                auraRadius, seconds + 1f, transform);
            // the aura serves the summoner's side: a wizard's demon must not gas its wizard; no summoner, no side
            _gas.Team = owner < 0 ? (Side?)null : Sides.Of(owner);
            _gas.Owner = owner;
            _gas.OwnerViaMinion = owner >= 0;
            // clients ride the same aura on this zombie's stand-in
            NetSync.PushField(2, _gas.transform.position, auraRadius, seconds + 1f,
                gameObject.GetInstanceID(), owner, owner >= 0);
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
            if (Creature != null) c = Color.Lerp(c, Creature.Payload.Tint(), DrawingConfig.BiomeTintStrength);

            var view = GetComponent<StateView>() ?? gameObject.AddComponent<StateView>();
            view.Tint = c;
            view.DriveTint = true;
            if (Creature != null) view.Look = Creature.Skin;
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
