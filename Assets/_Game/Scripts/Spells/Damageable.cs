using UnityEngine;

namespace SpellyZombie
{
    /// Minimal health sink so spell effects have something to damage. Damage is
    /// always a physical consequence (heat, impact, crush) — never a "damage
    /// spell". Destructible dynamic props (crates) are removed on death; static
    /// or marked-indestructible things just log.
    ///
    /// HEALTH *IS* STRENGTH - one stat for players, creatures and scenery
    /// alike. A hurt thing is a weak thing: it lifts less, hits softer and
    /// holds itself up worse. Everything that carries a Damageable is part of
    /// that same law, which is why the environment has strength too.
    public class Damageable : MonoBehaviour
    {
        public float Health = 100f;
        public bool Destructible = true;

        [Tooltip("This thing's own strength ceiling. 0 = whatever it was born with.")]
        public float MaxStrength;

        /// 0..1 of its ceiling. Anything that scales with strength reads this.
        public float StrengthFraction =>
            MaxStrength <= 0f ? 1f : Mathf.Clamp01(Health / MaxStrength);

        /// A creature's ceiling comes from its BODY: bigger and heavier means
        /// stronger. Size counts more than mass (a big light thing is still
        /// strong), and the biome it was raised in caps the result.
        /// One definition, used by zombies and golems alike.
        /// Mass is taken as a ROOT, not straight: a body eight times heavier is
        /// a few times tougher, not eight. Linear mass ran away the moment
        /// something heavy existed - a scale-2 golem massing 360 came out with
        /// thousands of strength.
        public static float StrengthFromBody(float sizeMul, float massKg) =>
            DrawingConfig.BodyStrengthBase
            * Mathf.Pow(Mathf.Max(0.05f, sizeMul), DrawingConfig.BodyStrengthSizePower)
            * (1f + Mathf.Sqrt(Mathf.Max(0f, massKg)) * DrawingConfig.BodyStrengthPerKg);

        /// Set the ceiling from the body and fill it. Call once, after the
        /// thing has its final scale and mass.
        public void SetStrengthFromBody(float sizeMul, float massKg)
        {
            MaxStrength = Mathf.Max(1f, StrengthFromBody(sizeMul, massKg));
            Health = MaxStrength;
            NaturalMass = massKg;   // this IS its own weight from here on
        }

        /// The multiplier the world uses: never 0, so a nearly-dead thing is
        /// feeble rather than inert.
        public float StrengthMul =>
            Mathf.Lerp(DrawingConfig.StrengthFloorMul, 1f, StrengthFraction);

        /// Fired once, just before the object is removed (cause string passed).
        public System.Action<string> OnDeath;

        /// Fired on every hit (amount, cause) - lets AI flinch / interrupt casts.
        public System.Action<float, string> OnDamaged;

        float _logAccum;
        bool _dead;

        Rigidbody _body;

        // authored scene furniture (present at load) is what the lobby
        // rebuilds; runtime spawns - zombies, matter, debris - die for real
        bool _authored;

        void Awake()
        {
            // dynamic props can be destroyed; static geometry shouldn't vanish
            _body = GetComponent<Rigidbody>();
            Destructible = _body != null;
            _authored = Time.timeSinceLevelLoad < 1f;
            // born full: whatever health it was given IS its ceiling
            if (MaxStrength <= 0f) MaxStrength = Mathf.Max(1f, Health);
            if (_body != null) NaturalMass = _body.mass;
        }

        /// What this body weighed when it was born, or when its strength was
        /// last set from its body. Anything past this is weight it is CARRYING.
        public float NaturalMass { get; private set; }

        /// 0 = carrying nothing but itself, 1 = at the point where it starts
        /// buckling. Views read this to show the strain before it kills.
        public float Burden01
        {
            get
            {
                if (_body == null || _body.isKinematic) return 0f;
                float extra = Mathf.Max(0f, _body.mass - NaturalMass);
                if (extra <= 0f) return 0f;
                float strength = Mathf.Max(1f, Health > 0f ? Health : MaxStrength);
                return Mathf.Clamp01(extra * DrawingConfig.PropWeightPerKg
                    / (strength * DrawingConfig.PropCrushLoad));
            }
        }

        // impact damage: a prop with a Rigidbody takes damage scaled by how hard it hit
        static readonly float ImpactFloor = DrawingConfig.Overlay("ImpactDamageFloor", 4f);
        // distinct key on purpose: "ImpactDamagePerSpeed" is the creature knob (Creature.cs)
        static readonly float ImpactScale = DrawingConfig.Overlay("PropImpactDamagePerSpeed", 2.2f);

        void OnCollisionEnter(Collision col)
        {
            if (_dead) return;
            // rooted props gain their body after Awake when torn loose - re-check
            if (_body == null)
            {
                _body = GetComponent<Rigidbody>();
                if (_body == null) return;
                Destructible = true;   // it's a real object now
            }
            float speed = col.relativeVelocity.magnitude;
            if (speed < ImpactFloor) return;

            // heavier things carry more into the hit, and both sides feel it
            float mass = Mathf.Max(0.2f, _body.mass);
            float dmg = (speed - ImpactFloor) * ImpactScale * Mathf.Sqrt(mass);
            if (dmg < 1f) return;

            string what = col.collider != null ? col.collider.name : "the ground";
            TakeDamage(dmg, $"slammed into {what}");

            // what it hit takes damage too
            var other = col.collider != null
                ? col.collider.GetComponentInParent<Damageable>() : null;
            if (other != null && other != this) other.TakeDamage(dmg * 0.7f, $"hit by {name}");
        }

        /// Clears the dead flag on lobby respawn; restoring Health alone is not enough.
        public void Revive(float health)
        {
            _dead = false;
            Health = health;
        }

        /// A thing too weak for its own mass buckles - the scenery obeys the
        /// same weight-against-strength law bodies do. Only free-standing
        /// objects: static geometry is held up by the world, not by itself.
        void FixedUpdate()
        {
            if (_dead || _body == null || _body.isKinematic) return;
            if (MaxStrength <= 0f) return;

            // WEIGHT AGAINST STRENGTH, not against a health fraction. Dividing
            // by StrengthMul (0.35..1) measured mass against nothing, so a
            // healthy 110kg charger buckled just for being a charger.
            // A hurt thing still holds itself up worse: strength IS health.
            float carried = _body.mass * DrawingConfig.PropWeightPerKg;
            float strength = Mathf.Max(1f, Health > 0f ? Health : MaxStrength);
            float load = carried / strength;
            if (load < DrawingConfig.PropCrushLoad) return;

            _crushCarry += (load - DrawingConfig.PropCrushLoad)
                * DrawingConfig.PropCrushPerSec * Time.fixedDeltaTime;
            if (_crushCarry < 1f) return;
            float bite = _crushCarry;
            _crushCarry = 0f;
            TakeDamage(bite, "buckling under its own weight");
        }

        float _crushCarry;

        public void TakeDamage(float amount, string cause)
        {
            if (amount <= 0f || _dead) return;

            // damage on a limb bone forwards to the owning being; never Destroy() a skeleton bone
            var pilot = GetComponentInParent<SimpleFPSController>();
            Component owner = pilot != null ? (Component)pilot : GetComponentInParent<Creature>();
            if (owner != null && owner.gameObject != gameObject)
            {
                var rootDmg = owner.GetComponent<Damageable>();
                if (rootDmg != null && rootDmg != this) rootDmg.TakeDamage(amount, cause);
                return;
            }

            Health -= amount;
            OnDamaged?.Invoke(amount, cause);
            _logAccum += amount;
            if (_logAccum >= 30f)
            {
                Debug.Log($"[SpellyZombie] {name}: {cause}, {Mathf.Max(0, Health):0} hp left");
                _logAccum = 0f;
            }
            if (Health <= 0f)
            {
                _dead = true;
                Debug.Log($"[SpellyZombie] {name} destroyed by {cause}");
                OnDeath?.Invoke(cause);
                if (!Destructible) return;
                // in the lobby, authored props respawn; creatures and runtime spawns die for real
                if (RoundDirector.InLobby && _authored
                    && GetComponent<Creature>() == null
                    && GetComponent<SimpleFPSController>() == null)
                    LobbyRespawn.Take(gameObject, DrawingConfig.LobbyRespawnSeconds);
                else
                    Destroy(gameObject);
            }
        }
    }
}
