using UnityEngine;

namespace SpellyZombie
{
    /// Spell flight for conjured matter (attached by Spell.SpawnMatter): float
    /// in the air, lock on prey, jump and slam. Damage stays pure impact physics.
    public class MatterStrike : MonoBehaviour
    {
        Rigidbody _rb;
        Transform _prey;
        Vector3 _point;
        bool _slam;
        bool _held, _thrown;
        float _scanCd;
        float _grace = 0.25f; // a beat of birth before the pounce reads
        float _spellLife = -1f; // the hover expires eventually

        public int OwnerId;
        SurfaceMaterialType _mat;
        MatterPhase _phase;
        float _size;

        /// Still magic (not thrown yet): lifted free, no ink cost, no weight.
        public bool SpellForm => !_thrown;

        public void Init(int owner, SurfaceMaterialType mat, MatterPhase phase, float size)
        {
            OwnerId = owner;
            _mat = mat;
            _phase = phase;
            _size = size;
        }

        /// On impact the body shatters into debris of its own material; the
        /// chunks are the lingering effect.
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
                // rubble crumbles away in time and pops instead of deforming
                var sd = chunk.gameObject.AddComponent<SpellDebris>();
                sd.Init(_mat, _phase, chunkSize);
                sd.OwnerId = OwnerId;
                chunk.StampOwner(OwnerId);
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
            // claim the Rigidbody lazily; re-assert the float every tick since Matter re-enables gravity
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_rb == null) return;
            // grabbed/claimed matter belongs to the hand systems, not to us
            if (InkRuneStone.Carried != null && InkRuneStone.Carried.transform == transform) return;
            if (HandGrab.LocalHeldBody == _rb) { _held = true; return; } // in the hand

            // thrown = ballistic with gravity; only seeing prey mid-flight wakes the strike once more
            if (_held && HandGrab.LocalHeldBody != _rb)
            {
                _held = false;
                _thrown = true;
                _rb.useGravity = true;
                // spells throw at full speed regardless of weight
                if (_rb.linearVelocity.sqrMagnitude > 0.5f)
                    _rb.linearVelocity = _rb.linearVelocity.normalized * HandGrab.ThrowSpeed;
            }

            float dt = Time.fixedDeltaTime;

            // an unspent hover eventually detonates where it waits
            if (_spellLife < 0f) _spellLife = DrawingConfig.ParticleLife * 2f;
            if (SpellForm && !_held)
            {
                _spellLife -= dt;
                if (_spellLife <= 0f) { Detonate(); return; }
            }

            if (!_thrown && (!_slam || _grace > 0f))
            {
                _rb.isKinematic = false;
                _rb.useGravity = false; // spells float
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

            // hover: rise to shoulder height and hang there
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
            // slamming or thrown: the landing is the detonation; a hovering spell just bumps and waits
            if (_slam || _thrown) Detonate();
        }
    }
}
