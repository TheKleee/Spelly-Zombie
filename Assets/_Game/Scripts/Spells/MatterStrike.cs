using UnityEngine;

namespace SpellyZombie
{
    /// A PARTICLE IN BEHAVIOR, NOT IN SHAPE ("It's a particle
    /// when it comes to the behavior not when it comes to the shape... they
    /// still behave as before but fly cause they are magical spells. Spells
    /// float and jump towards a target. Normal rock doesn't cause it's not a
    /// spell"). Spell.SpawnMatter attaches this to the conjured matter — the
    /// shape, the material, the sides-pick-the-shape rule, all untouched.
    /// This only adds the flight: float in the air, lock on prey, jump and
    /// slam. Damage stays pure physics - a flying mass hitting things is the
    /// existing impact calculation, boulder big, splash small.
    public class MatterStrike : MonoBehaviour
    {
        Rigidbody _rb;
        Transform _prey;
        Vector3 _point;
        bool _slam;
        bool _held, _thrown;
        float _scanCd;
        float _grace = 0.25f; // a beat of birth before the pounce reads
        float _spellLife = -1f; // the hover turret is not eternal either

        public int OwnerId;
        SurfaceMaterialType _mat;
        MatterPhase _phase;
        float _size;

        /// Still magic (not thrown yet): the caster lifts it FREE, no ink law,
        /// no weight .
        public bool SpellForm => !_thrown;

        public void Init(int owner, SurfaceMaterialType mat, MatterPhase phase, float size)
        {
            OwnerId = owner;
            _mat = mat;
            _phase = phase;
            _size = size;
        }

        /// #3 and #5: IMPACT = EXPLOSION. The body never survives to deform -
        /// it shatters into debris of ITS OWN material, and those chunks ARE
        /// the lingering effect (water chunks puddle and slick, rock chunks
        /// litter and crush), stabilizing on their own exactly like all matter.
        void Detonate()
        {
            if (_rb == null) { enabled = false; return; }
            int n = Mathf.Max(2, DrawingConfig.StrikeBurstPieces);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = (Random.onUnitSphere + Vector3.up * 0.5f).normalized;
                float chunkSize = Mathf.Max(0.12f, _size * 0.35f);
                var chunk = Matter.Spawn(_mat, _phase, chunkSize, _rb.position + d * 0.4f, 0);
                if (chunk == null) continue;
                // rubble crumbles away in time and POPS instead of deforming
                chunk.gameObject.AddComponent<SpellDebris>().Init(_mat, _phase, chunkSize);
                if (chunk.TryGetComponent<Rigidbody>(out var crb))
                    crb.linearVelocity = d * 7f;
            }
            Juice.Thud(_rb.position);
            Destroy(gameObject);
        }

        Vector3 _hoverPt;

        void OnEnable() => _hoverPt = transform.position + Vector3.up * 1.1f;

        void FixedUpdate()
        {
            // Matter wires its Rigidbody on its own schedule - claim it lazily,
            // and RE-ASSERT the float every tick (its physics setup was putting
            // gravity back, which is why the spawns sat on the ground)
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_rb == null) return;
            // grabbed/claimed matter belongs to the hand systems, not to us
            if (InkRuneStone.Carried != null && InkRuneStone.Carried.transform == transform) return;
            if (HandGrab.LocalHeldBody == _rb) { _held = true; return; } // in the hand

            // THROWN = NO LONGER A PARTICLE ("they stop being
            // particles when I throw them and fall to the ground afterwards
            // unless they see an opponent") — after the hand lets go it flies
            // ballistic, gravity honest; only SEEING prey mid-flight wakes the
            // strike one last time.
            if (_held && HandGrab.LocalHeldBody != _rb)
            {
                _held = false;
                _thrown = true;
                _rb.useGravity = true;
                // #6: SPELLS THROW FAST REGARDLESS OF WEIGHT - the arm is
                // magic, not muscle. Pour more into the spell and it is only
                // MORE dangerous at the same full speed.
                if (_rb.linearVelocity.sqrMagnitude > 0.5f)
                    _rb.linearVelocity = _rb.linearVelocity.normalized * HandGrab.ThrowSpeed;
            }

            float dt = Time.fixedDeltaTime;

            // THE SPELL EXPIRES ("the overall spell effect should
            // not last forever") — an unspent hover turret eventually detonates
            // where it waits, scattering debris that itself crumbles away
            if (_spellLife < 0f) _spellLife = DrawingConfig.ParticleLife * 2f;
            if (SpellForm && !_held)
            {
                _spellLife -= dt;
                if (_spellLife <= 0f) { Detonate(); return; }
            }

            if (!_thrown && (!_slam || _grace > 0f))
            {
                _rb.isKinematic = false;
                _rb.useGravity = false; // it FLOATS - it is a spell, not a rock
            }
            if (_grace > 0f) { _grace -= dt; return; }

            if (_thrown && !_slam)
            {
                // ballistic - but if prey crosses its path, one last pounce
                _scanCd -= dt;
                if (_scanCd <= 0f)
                {
                    _scanCd = 0.2f;
                    float b = DrawingConfig.StrikeLockRange * DrawingConfig.StrikeLockRange;
                    var seen = Targets.Nearest(_rb.position, ref b, includePlayers: false);
                    if (seen != null)
                    {
                        _prey = seen;
                        _point = seen.position + Vector3.up * 0.5f;
                        _slam = true;
                        _rb.useGravity = false;
                    }
                }
                return;
            }

            if (_slam)
            {
                if (_prey != null) _point = _prey.position + Vector3.up * 0.5f;
                Vector3 to = _point - _rb.position;
                if (to.sqrMagnitude < 1.2f)
                {
                    Detonate(); // arrived = exploded; the debris does the rest
                    return;
                }
                _rb.linearVelocity = to.normalized * DrawingConfig.StrikeSpeed * 0.85f;
                return;
            }

            // the hover: RISE to shoulder height and hang there, visibly a
            // spell waiting - not a stone lying in the road
            Vector3 lift = (_hoverPt - _rb.position) * 2.2f;
            _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity,
                Vector3.ClampMagnitude(lift, 3f), 5f * dt);

            _scanCd -= dt;
            if (_scanCd > 0f) return;
            _scanCd = 0.3f;
            float best = DrawingConfig.StrikeLockRange * DrawingConfig.StrikeLockRange;
            var prey = Targets.Nearest(_rb.position, ref best, includePlayers: false);
            if (prey == null) return;
            _prey = prey;
            _point = prey.position + Vector3.up * 0.5f;
            _slam = true;
        }

        void OnCollisionEnter(Collision c)
        {
            // slamming OR thrown: the landing IS the detonation
            // it never lies there deforming, it explodes into its own debris.
            // A hovering unthrown spell just bumps things and keeps waiting.
            if (_slam || _thrown) Detonate();
        }
    }
}
