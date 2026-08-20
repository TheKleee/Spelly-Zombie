using UnityEngine;

namespace SpellyZombie
{
    /// Nature's answer to wrecking the place. A golem belongs to nobody: it
    /// wanders, and anything it sees is an enemy. It does not guard a summoner
    /// and it does not take orders.
    /// Its one attack is the shared ChargeAttack - hop, lock the direction,
    /// drive straight - so it is dodgeable by moving, and its debuff is
    /// whatever its BiomeStamp made it.
    /// Built from the CollectionManager's Golem prefab; nothing is code-built.
    [RequireComponent(typeof(Rigidbody))]
    public class Golem : MonoBehaviour
    {
        public float SightRange = 13f;
        public float WalkSpeed = 1.9f;

        Rigidbody _rb;
        Creature _me;
        ChargeAttack _charge;
        Damageable _dmg;
        GooglyEyes _eyes;
        bool _sawPrey;
        float _safeUntil;   // birth shield: it cannot be killed while rising

        void OnEnable() { if (_dmg == null) _dmg = GetComponent<Damageable>(); }

        void LateUpdate()
        {
            // while the shield holds, damage cannot finish it - it is topped
            // back up rather than made immune, so hits still register and read
            if (_dmg == null || Time.time >= _safeUntil) return;
            if (_dmg.Health < _dmg.MaxStrength * 0.35f)
                _dmg.Health = _dmg.MaxStrength * 0.35f;
        }

        Vector3 _wander;
        float _pickAt, _skipAt;

        /// Raise one from the authored prefab. Null (and a loud log) when the
        /// CollectionManager slot is empty - nothing is substituted.
        public static Golem Spawn(Vector3 at, float sizeMul = 1f)
        {
            var prefab = CollectionManager.Golem;
            if (prefab == null) return null;

            // SIZE IS THE INGREDIENTS' BUSINESS, TOUGHNESS IS NOT. A golem
            // raised from two small blobs stays small - that reads right - but
            // it is never made of paper: strength and mass have floors, so a
            // little one is still worth fighting. Bigger still means stronger.
            float scale = Mathf.Clamp(sizeMul,
                DrawingConfig.GolemMinScale, DrawingConfig.GolemMaxScale);

            var go = Instantiate(prefab, at, Quaternion.Euler(0f, Random.value * 360f, 0f));
            go.transform.localScale *= scale;

            var g = go.GetComponent<Golem>();
            if (g == null) g = go.AddComponent<Golem>();

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = Mathf.Max(DrawingConfig.GolemMinMass,
                    DrawingConfig.GolemBaseMass * scale * scale * scale);
                // A SMALL GOLEM MOVES FURTHER PER STEP THAN IT IS WIDE. At
                // charge speed a 0.08m body travels ~0.2m per physics tick, so
                // Discrete collision walks it straight through the terrain and
                // it is gone. Sweeping is the only thing that catches it.
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            var dmg = go.GetComponent<Damageable>();
            if (dmg != null)
            {
                dmg.MaxStrength = Mathf.Max(DrawingConfig.GolemMinStrength,
                    Damageable.StrengthFromBody(scale, rb != null ? rb.mass : 0f));
                dmg.Health = dmg.MaxStrength;
            }

            // it rises WHOLE: nothing can kill it mid-birth, so a golem always
            // gets to take at least one step
            if (g != null) g._safeUntil = Time.time + DrawingConfig.GolemBirthShield;

            // spread it thin enough and it cannot hold together: two golems
            if (go.GetComponent<DensitySplit>() == null) go.AddComponent<DensitySplit>();

            // weight you can see before it crushes
            if (go.GetComponent<WeightSag>() == null) go.AddComponent<WeightSag>();

            // the ground that raised it decides what it resists and how it looks
            BiomeStamp.Apply(go, at);
            return g;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _me = GetComponent<Creature>();
            _dmg = GetComponent<Damageable>();
            _charge = GetComponent<ChargeAttack>();
            if (_charge == null) _charge = gameObject.AddComponent<ChargeAttack>();

            // eyes ride a blob BONE, whose axes point wherever the rig says -
            // usually not where the golem walks. Aim their idle gaze at the
            // body so it reads as a creature looking around, not an ornament.
            _eyes = GetComponentInChildren<GooglyEyes>();
            if (_eyes != null) _eyes.Facing = transform;

            // it never just disappears: whatever kills it, it comes apart in
            // its own colour so you can see it happen
            if (_dmg == null) _dmg = GetComponent<Damageable>();
            if (_dmg != null)
            {
                _dmg.OnDeath += _ => Poof();
                // every wound reads: a chip of it flies off where it was hit
                _dmg.OnDamaged += (amount, _) =>
                {
                    if (amount < 2f) return;
                    var view = GetComponent<StateView>();
                    Color c = view != null && view.DriveTint ? view.Tint : Color.gray;
                    GrammarFX.PuffBurst(transform.position + Vector3.up * 0.3f, c,
                        amount > 15f ? 4 : 2);
                    if (_eyes != null) _eyes.SetMood(EyeMood.Scared, 0.35f);
                };
            }

            PickWander();
        }

        /// The end: a burst of the stuff it was made of, then gone.
        public void Poof()
        {
            Color c = Color.gray;
            var view = GetComponent<StateView>();
            if (view != null && view.DriveTint) c = view.Tint;

            GrammarFX.PuffBurst(transform.position + Vector3.up * 0.2f, c, 7);
            if (FxLibrary.I != null)
                FxLibrary.SpawnTinted(FxLibrary.I.Poof, transform.position + Vector3.up * 0.2f, c);
            Juice.Thud(transform.position);
        }

        void Update()
        {
            // one that slipped through the world dies where you last saw it,
            // rather than falling forever out of sight
            if (transform.position.y < DrawingConfig.GolemFloorY && _dmg != null)
                _dmg.TakeDamage(_dmg.Health + 1f, "swallowed by the ground");
        }

        void PickWander()
        {
            float a = Random.value * Mathf.PI * 2f;
            _wander = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            _pickAt = Time.time + Random.Range(2.5f, 6f);
        }

        void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic) return;
            if (_dmg != null && _dmg.Health <= 0f) return;
            // the charge owns movement while it runs, and while it is dazed
            // afterwards the golem just stands there shaking it off
            if (_charge != null && _charge.Busy) return;

            float mul = _me != null ? _me.SpeedMultiplier : 1f;
            if (mul <= 0.01f) return;

            // anything alive in sight is an enemy - no teams, no owner
            var prey = NearestTarget();
            if (prey != null)
            {
                // the face does the talking: it stares at whatever it found
                if (_eyes != null)
                {
                    _eyes.LookTarget = prey.position + Vector3.up * 1.2f;
                    if (!_sawPrey) _eyes.SetMood(EyeMood.Mad, 1.2f);
                }
                _sawPrey = true;

                Vector3 to = prey.position - transform.position;
                to.y = 0f;
                if (_charge != null && _charge.TryStart(prey.position)) return;
                if (to.sqrMagnitude > 0.04f) _wander = to.normalized;
            }
            else
            {
                if (_sawPrey && _eyes != null) _eyes.SetMood(EyeMood.Neutral, 0.4f);
                _sawPrey = false;
                if (Time.time >= _pickAt) PickWander();
            }

            Step(_wander, mul);
        }

        /// Walks by SKIPPING: a little hop with every step rather than a glide.
        void Step(Vector3 dir, float mul)
        {
            Vector3 want = dir * (WalkSpeed * mul);
            var v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(want.x, v.y, want.z);
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir, Vector3.up), 6f * Time.fixedDeltaTime);

            if (Time.time < _skipAt) return;
            _skipAt = Time.time + DrawingConfig.GolemSkipEvery / Mathf.Max(0.2f, mul);
            if (Mathf.Abs(v.y) < 0.6f)  // only when it is actually on the ground
                _rb.AddForce(Vector3.up * DrawingConfig.GolemSkipHop, ForceMode.VelocityChange);
        }

        Transform NearestTarget()
        {
            Transform best = null;
            float bestSqr = SightRange * SightRange;

            foreach (var p in SimpleFPSController.All)
            {
                if (p == null || p.IsDead) continue;
                // A DISGUISE FOOLS NATURE TOO. A golem that walks past every
                // bench in the village but beelines for the one that is an
                // acolyte would make hiding pointless wherever golems roam.
                if (SimpleFPSController.ThirdPersonActive && p.IsLocalViewer
                    && ShapeShift.LocalIsShaped) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p.transform; }
            }
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                float d = (z.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = z.transform; }
            }
            return best;
        }
    }
}
