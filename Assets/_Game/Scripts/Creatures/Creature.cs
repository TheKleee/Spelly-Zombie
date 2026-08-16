using UnityEngine;

namespace SpellyZombie
{
    /// Status-effect carrier for anything alive - every state is chemistry meeting biology (frozen/burning/stuck/slipping); movers read CanMove/SpeedMultiplier.
    public class Creature : MonoBehaviour
    {
        public bool Frozen { get; private set; }
        public bool Burning => _burnLeft > 0f;
        public bool Stuck => _stuckLeft > 0f;
        public bool Slipping => _slipLeft > 0f;

        public bool GettingUp => _getUpLeft > 0f;
        public bool CanMove => !Frozen && !Stuck;
        /// NEGATIVE while a Y owns this body
        /// - the walk plays forward, the zombie slides BACKWARD. Moonwalk.
        public float SpeedMultiplier =>
            (Frozen || Stuck || GettingUp ? 0f : Slipping ? 0.15f : Burning ? 1.8f : 1f)
            * (Board != null ? Board.SpeedMul * Board.InputSign : 1f);

        BodyState _board;
        BodyState Board => _board != null ? _board : _board = GetComponent<BodyState>();

        float _stuckLeft, _slipLeft, _burnLeft, _flameTimer, _getUpLeft;
        Rigidbody _rb;
        Thermal _thermal;
        Damageable _dmg;
        GameObject _iceShell;
        RigidbodyConstraints _normalConstraints;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _dmg = GetComponent<Damageable>();
            _thermal = GetComponent<Thermal>(); // late-added Thermals bind themselves (BindThermal)
            if (_rb != null) _normalConstraints = _rb.constraints;
        }

        /// Thermal is added on demand - it hands itself over in its Awake, so Update needn't poll GetComponent every frame.
        public void BindThermal(Thermal t) => _thermal = t;

        public void ApplyStuck(float seconds) { if (!Frozen) _stuckLeft = Mathf.Max(_stuckLeft, seconds); }

        /// Darkness: can't SEE anything (the brain skips visual perception).
        public bool Blinded => _blindLeft > 0f;
        float _blindLeft;
        public void ApplyBlind(float seconds)
        {
            _blindLeft = Mathf.Max(_blindLeft, seconds);
            GetComponentInChildren<GooglyEyes>()?.SetMood(EyeMood.Scared, seconds); // pinprick panic in the dark
        }

        /// Bowled over: full physics topple + dizzy orbiting pupils, then stands
        /// back up. This is the ragdoll-lite everything funny routes through.
        public void KnockDown(float seconds)
        {
            ApplySlip(seconds);
            if (_rb != null) _rb.AddForce(Vector3.up * 1.5f, ForceMode.Impulse); // pop off the feet
            GetComponentInChildren<GooglyEyes>()?.SetMood(EyeMood.Dizzy, seconds);
        }

        public void ApplySlip(float seconds)
        {
            _slipLeft = Mathf.Max(_slipLeft, seconds);
            _getUpLeft = 0f; // knocked back down mid-rise - classic
            // lose your footing for real: let physics tip the body over
            if (_rb != null && !Frozen)
            {
                _rb.constraints = RigidbodyConstraints.None;
                _rb.AddTorque(Random.onUnitSphere * 2.5f, ForceMode.Impulse);
            }
        }

        /// A frozen creature is brittle - a solid hit shatters the shell for big
        /// bonus damage. Called by impacting Matter.
        public bool TryShatter(float impactDamage)
        {
            if (!Frozen) return false;
            Unfreeze();
            if (_dmg != null) _dmg.TakeDamage(impactDamage * 3f, "shattered");
            return true;
        }


        void Update()
        {
            float dt = Time.deltaTime;
            if (_stuckLeft > 0f) _stuckLeft -= dt;
            if (_blindLeft > 0f) _blindLeft -= dt;
            if (_slipLeft > 0f)
            {
                _slipLeft -= dt;
                if (_slipLeft <= 0f && !Frozen)
                    _getUpLeft = 0.7f; // ragdoll over - now the struggle to stand
            }

            // getting up: visibly right itself over ~0.7s instead of snapping
            if (_getUpLeft > 0f)
            {
                _getUpLeft -= dt;
                if (_rb != null && !Frozen)
                {
                    Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, upright, dt * 6f);
                    if (_getUpLeft <= 0f)
                    {
                        transform.rotation = upright;
                        _rb.constraints = _normalConstraints; // feet under it again
                    }
                }
            }

            TickFire(dt);

            if (_thermal == null) return;

            // IGNITION IS STICKY: crossing the threshold burns for a real duration - leaving the heat zone does not save you
            if (!Frozen && _thermal.Temperature > DrawingConfig.BurnThreshold && _burnLeft <= 0f)
            {
                _burnLeft = 5f;
                GetComponent<ZombieBrain>()?.Mumble("AAAAGH!!", 2f);
                Juice.Whoosh(transform.position);
            }

            if (!Frozen && _thermal.Temperature < DrawingConfig.FreezeThreshold) Freeze();
            else if (Frozen && _thermal.Temperature > 5f) { Unfreeze(); _burnLeft = 0f; }
        }

        void TickFire(float dt)
        {
            if (_burnLeft <= 0f) return;
            if (Frozen) { _burnLeft = 0f; return; } // ice beats fire

            _burnLeft -= dt;
            if (_thermal != null)
            {
                // the fire feeds itself - but as a PUSH, not a pin: sustained
                // cold (frost hits, a snow field) can WIN the tug-of-war and
                // put you out
                _thermal.Temperature += 30f * dt;
                if (_thermal.Temperature < DrawingConfig.BurnThreshold - 15f)
                {
                    _burnLeft = 0f; // quenched
                    GrammarFX.PuffBurst(transform.position + Vector3.up * 0.8f,
                        new Color(0.92f, 0.94f, 0.97f, 0.5f), 5); // steam hiss
                    GetComponent<ZombieBrain>()?.Mumble("...phew", 1.5f);
                    return;
                }
            }
            if (_dmg != null) _dmg.TakeDamage(15f * dt, "on fire");

            // FLAMES - the part that was missing. Orange blobs boil off the body.
            _flameTimer -= dt;
            if (_flameTimer <= 0f)
            {
                _flameTimer = 0.14f;
                var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flame.name = "Flame";
                Destroy(flame.GetComponent<Collider>());
                flame.transform.position = transform.position + Vector3.up * Random.Range(0.2f, 1.6f)
                    + Random.insideUnitSphere * 0.35f;
                flame.transform.localScale = Vector3.one * Random.Range(0.15f, 0.3f);
                flame.GetComponent<Renderer>().sharedMaterial = MatterFX.Get(
                    Color.Lerp(new Color(1f, 0.75f, 0.15f, 0.95f), new Color(1f, 0.3f, 0.05f, 0.95f), Random.value),
                    MoteShade.Additive);
                var rise = flame.AddComponent<Rigidbody>();
                rise.useGravity = false;
                rise.linearVelocity = Vector3.up * Random.Range(0.8f, 1.6f);
                Destroy(flame, Random.Range(0.35f, 0.6f));
            }
        }

        void Freeze()
        {
            Frozen = true;
            Juice.Crackle(transform.position);
            if (_rb != null) _rb.constraints = RigidbodyConstraints.FreezeAll; // statue
            _iceShell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _iceShell.name = "IceShell";
            var shellCol = _iceShell.GetComponent<Collider>();
            if (shellCol != null) Destroy(shellCol);
            _iceShell.transform.SetParent(transform, false);
            _iceShell.transform.localScale = Vector3.one * 1.25f;
            _iceShell.GetComponent<Renderer>().sharedMaterial =
                MatterFX.Get(new Color(0.72f, 0.88f, 1f, 0.55f), MoteShade.Transparent);
        }

        void Unfreeze()
        {
            Frozen = false;
            if (_rb != null) _rb.constraints = _normalConstraints;
            if (_iceShell != null) Destroy(_iceShell);
        }

        /// Burning creatures pass fire on contact, and PHYSICS HURTS - fast collisions deal impact damage, falls included.
        void OnCollisionEnter(Collision col)
        {
            if (Burning)
            {
                var t = col.collider.GetComponentInParent<Thermal>();
                if (t != null) t.AddHeat(60f);
                var m = col.collider.GetComponent<Matter>();
                if (m != null) m.AddHeat(60f);
            }

            float speed = col.relativeVelocity.magnitude;
            if (speed < DrawingConfig.ImpactDamageSpeed) return;
            float dmg = (speed - DrawingConfig.ImpactDamageSpeed) * DrawingConfig.ImpactDamagePerSpeed;

            // a flung zombie is a projectile: whoever it lands on feels it too
            var pilot = col.collider.GetComponent<SimpleFPSController>();
            if (pilot != null) pilot.TakeHit(col.relativeVelocity * 0.25f, dmg * 0.6f);

            if (TryShatter(dmg)) return; // frozen = brittle, triple payout
            if (_dmg != null) _dmg.TakeDamage(dmg, "impact");
            if (dmg > 8f) Juice.Thud(transform.position);
        }
    }
}
