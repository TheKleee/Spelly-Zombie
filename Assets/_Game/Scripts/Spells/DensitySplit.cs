using UnityEngine;

namespace SpellyZombie
{
    /// Matter cannot be spread thinner than it can hold together. Push a
    /// body's weight below what its own bulk can sustain and it SPLITS into
    /// smaller ones that can.
    ///
    /// This is why Spread multiplies things: nothing duplicates anything, it
    /// just gets too thin to stay one object.
    ///
    /// ONLY ELEMENTS SPLIT. Living things do not divide - they simply get too
    /// light for the medium and drift (BodyState.GravityMul goes negative).
    /// Halving a player would mean handing someone an AI half that cannot
    /// draw. A GOLEM splits because it is not alive: it is made OF elements,
    /// so thinning it thins the stuff it is built from.
    ///
    /// Splitting is recursive by nature: each piece re-checks itself, so a
    /// hard enough Spread cascades until the pieces cannot divide again.
    [RequireComponent(typeof(Rigidbody))]
    public class DensitySplit : MonoBehaviour
    {
        [Tooltip("Below this mass-per-volume the body cannot hold together.")]
        public float MinDensity = -1f;   // <0 = take the DrawingConfig default

        [Tooltip("Never divide below this scale - the pieces would be dust.")]
        public float MinScale = 0.25f;

        [Tooltip("How many pieces a split makes.")]
        public int Pieces = 2;

        Rigidbody _rb;
        float _checkAt;
        bool _splitting;

        void Awake() => _rb = GetComponent<Rigidbody>();

        /// Volume rises with the cube of scale, so a body twice as wide needs
        /// eight times the mass to keep the same density.
        public float Density
        {
            get
            {
                if (_rb == null) return float.MaxValue;
                float s = Mathf.Abs(transform.localScale.x);
                float volume = Mathf.Max(1e-4f, s * s * s);
                return _rb.mass / volume;
            }
        }

        void FixedUpdate()
        {
            if (_splitting || _rb == null || _rb.isKinematic) return;
            // a living thing floats instead - only elements come apart
            if (GetComponent<Creature>() != null && GetComponent<Golem>() == null) return;
            if (GetComponent<SimpleFPSController>() != null) return;
            if (Time.time < _checkAt) return;
            _checkAt = Time.time + 0.25f;   // a settling frame, not every tick

            float floor = MinDensity > 0f ? MinDensity : DrawingConfig.SplitMinDensity;
            if (Density >= floor)
            {
                if (_adrift) { _adrift = false; _rb.useGravity = true; } // heavy again: it falls
                return;
            }

            // DIVIDE FIRST, FLOAT LAST. An element spread thin keeps breaking
            // into smaller pieces; only when it cannot get any smaller does it
            // give up and drift, like everything else too light for the air.
            if (Mathf.Abs(transform.localScale.x) > MinScale) { Split(); return; }
            Drift();
        }

        /// The smallest a piece can get and still be too light: it stops
        /// dividing and rises instead. Same end state a living thing reaches
        /// straight away, since a being never gets smaller - it is who it is.
        void Drift()
        {
            if (_adrift) return;
            _adrift = true;
            _rb.useGravity = false;
            _rb.linearDamping = Mathf.Max(_rb.linearDamping, DrawingConfig.FloatDrag);
        }

        bool _adrift;

        /// Two smaller bodies out of one, conserving mass. Each piece keeps a
        /// volume small enough that the new density is legal, so the pair is
        /// stable unless something spreads them further.
        public void Split()
        {
            if (_splitting) return;
            _splitting = true;

            int n = Mathf.Max(2, Pieces);
            // volume splits n ways, so each piece is the cube root smaller
            float shrink = Mathf.Pow(1f / n, 1f / 3f);
            float pieceMass = _rb.mass / n;
            Vector3 at = transform.position;
            float radius = Mathf.Abs(transform.localScale.x) * 0.6f;

            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                Vector3 spot = at + new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * radius;

                var copy = Instantiate(gameObject, spot, transform.rotation);
                copy.name = gameObject.name;
                copy.transform.localScale = transform.localScale * shrink;

                var crb = copy.GetComponent<Rigidbody>();
                if (crb != null)
                {
                    crb.mass = pieceMass;
                    crb.linearVelocity = _rb.linearVelocity
                        + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * DrawingConfig.SplitPush;
                }

                // strength follows the new body: smaller means weaker
                var dmg = copy.GetComponent<Element>();
                if (dmg != null) dmg.SetStrengthFromBody(copy.transform.localScale.x, pieceMass);

                var s = copy.GetComponent<DensitySplit>();
                if (s != null) s._checkAt = Time.time + 0.5f; // let it settle before re-judging
            }

            Destroy(gameObject);
        }
    }
}
