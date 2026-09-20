using UnityEngine;

namespace SpellyZombie
{
    /// The charge, shared by golems and zombies. Three beats:
    ///   TELL   - it hops on the spot and looks angry. You get this long to read it.
    ///   COMMIT - the direction is locked to where the target WAS at that moment.
    ///   RUN    - it drives straight, hits whatever it meets, then recovers.
    /// It never steers mid-charge. A homing charge is undodgeable, so the
    /// tell plus the committed line IS the fight: move and it misses.
    [RequireComponent(typeof(Rigidbody))]
    public class ChargeAttack : MonoBehaviour
    {
        public float Range = 9f;          // starts looking to charge inside this
        public float Cooldown = 4f;
        public float Damage = 12f;

        /// Range and run speed times this: a boss golem's grows with its size.
        public float Reach { get; set; } = 1f;

        /// Authored animation for the tell, from the worn spell. Empty = the
        /// built-in hop is the whole tell. The hop and eyes stay either way -
        /// the dodge window is law, the clip is its face.
        public AnimationClip TellClip;

        enum Beat { Idle, Tell, Run, Recover }

        Beat _beat = Beat.Idle;
        float _until, _readyAt;
        Vector3 _dir;
        Rigidbody _rb;
        Element _me2;
        float _lentStrength;   // what the charge is holding up, given back after
        Creature _me;
        GooglyEyes _eyes;

        /// True while it is telling, running or shaking it off - the brain
        /// must not steer, and its own impact must not wound it.
        public bool Busy => _beat != Beat.Idle;

        /// Dazed after a hit: it stands, then walks, before lining up another.
        public bool Recovering => _beat == Beat.Recover;

        /// Driving its locked line right now, and the line.
        public bool Running => _beat == Beat.Run;
        public Vector3 Heading => _dir;

        /// Ends the run on the spot; the daze and the cooldown follow as after any charge.
        public void Halt()
        {
            if (_beat != Beat.Run) return;
            Stop();
            if (_rb != null && !_rb.isKinematic)
                _rb.linearVelocity = new Vector3(0f, _rb.linearVelocity.y, 0f);
        }

        /// The beat as the golem snapshot carries it: 0 idle, 1 tell, 2 run, 3 recover.
        public byte BeatWire => (byte)_beat;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _me = GetComponent<Creature>();
            _eyes = GetComponentInChildren<GooglyEyes>();
        }

        /// Ask for a charge at a target. Refused while cooling down, already
        /// charging, or when the creature cannot act.
        public bool TryStart(Vector3 targetPos)
        {
            if (_beat != Beat.Idle || Time.time < _readyAt) return false;
            if (_me != null && _me.SpeedMultiplier <= 0.01f) return false;

            Vector3 flat = targetPos - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.04f || flat.magnitude > Range * Reach) return false;

            _dir = flat.normalized;   // LOCKED HERE - where you were, not where you go
            _beat = Beat.Tell;
            LendStrength();
            _until = Time.time + DrawingConfig.ChargeTellSeconds;

            // the tell: a hop in place and wide angry eyes; a golem says so too, lower the bigger it is
            if (GetComponentInParent<Golem>() != null)
                Juice.Sound(Sfx.GolemCroak, transform.position + Vector3.up * transform.localScale.y, 1f,
                    Mathf.Lerp(1.2f, 0.6f, Mathf.InverseLerp(0.5f, 3f, transform.localScale.y)));
            if (TellClip != null) OneShotClip.Play(gameObject, TellClip);
            if (_rb != null && !_rb.isKinematic)
                _rb.AddForce(Vector3.up * DrawingConfig.ChargeTellHop, ForceMode.VelocityChange);
            if (_eyes != null)
            {
                _eyes.Swell(DrawingConfig.ChargeTellSeconds, DrawingConfig.ChargeTellEyeSwell);
                _eyes.SetMood(EyeMood.Mad, DrawingConfig.ChargeTellSeconds
                    + DrawingConfig.ChargeRunSeconds);   // it looks angry, then goes
                _eyes.LookTarget = targetPos + Vector3.up * 1.2f;
            }
            return true;
        }

        void FixedUpdate()
        {
            if (_beat == Beat.Idle) return;
            if (_me != null && _me.SpeedMultiplier <= 0.01f) { Stop(); return; }

            if (Time.time >= _until)
            {
                if (_beat == Beat.Tell)
                {
                    _beat = Beat.Run;
                    _until = Time.time + DrawingConfig.ChargeRunSeconds;
                    // a golem's run is heard coming, and the sound runs with it; bigger is lower
                    var golem = GetComponentInParent<Golem>();
                    if (golem != null)
                        Juice.Sound(Sfx.GolemCharge, transform.position, 1f,
                            Mathf.Lerp(1.12f, 0.8f, Mathf.InverseLerp(0.5f, 3f, transform.localScale.y)), true, transform);
                }
                else if (_beat == Beat.Recover) { _beat = Beat.Idle; TakeStrengthBack(); return; }
                else { Stop(); return; }
            }

            if (_beat != Beat.Run || _rb == null || _rb.isKinematic) return;

            // strength IS health: a hurt charger hits softer and slower
            float mul = _me != null ? _me.StrengthMul : 1f;
            Vector3 want = _dir * (DrawingConfig.ChargeSpeed * Reach * mul);
            var v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(want.x, v.y, want.z);
            transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);
        }

        void Stop()
        {
            if (_beat == Beat.Run || _beat == Beat.Tell)
            {
                // it spent itself: a beat of standing there dazed, then it
                // walks again, and only later does it line another one up
                _readyAt = Time.time + Cooldown;
                _beat = Beat.Recover;
                _until = Time.time + DrawingConfig.ChargeRecoverSeconds;
                if (_rb != null && !_rb.isKinematic)
                {
                    var v = _rb.linearVelocity;
                    _rb.linearVelocity = new Vector3(v.x * 0.2f, v.y, v.z * 0.2f);
                }
                if (_eyes != null) _eyes.SetMood(EyeMood.Dizzy, DrawingConfig.ChargeRecoverSeconds);
                return;
            }
            _beat = Beat.Idle;
            TakeStrengthBack();
        }

        /// BRACED FOR ITS OWN HIT. A charge hurts the charger - that is the
        /// same law a falling rock obeys - so it is lent strength for the
        /// length of the charge and survives what it dishes out. Take the
        /// loan away and ramming a wall is suicide again, which is the point:
        /// the survival is a number, not an exception.
        void LendStrength()
        {
            if (_me2 == null) _me2 = GetComponent<Element>();
            if (_me2 == null || _lentStrength > 0f) return;
            _lentStrength = _me2.MaxStrength * (DrawingConfig.ChargeStrengthMul - 1f);
            if (_lentStrength <= 0f) { _lentStrength = 0f; return; }
            _me2.MaxStrength += _lentStrength;
            _me2.Health += _lentStrength;
        }

        void TakeStrengthBack()
        {
            if (_me2 != null && _lentStrength > 0f)
            {
                _me2.MaxStrength = Mathf.Max(1f, _me2.MaxStrength - _lentStrength);
                // it keeps whatever the charge cost it; only the loan goes back
                _me2.Health = Mathf.Min(_me2.Health, _me2.MaxStrength);
                _lentStrength = 0f;
            }
            if (_me2 != null && _recoilOwed > 0f)
            {
                // paid after the loan is back, so the loan cannot swallow it
                float owed = _recoilOwed;
                _recoilOwed = 0f;
                _me2.TakeDamage(owed, "hit a player");
            }
        }

        float _recoilOwed;

        /// A wild golem cracks by a share of its own strength for hitting a player.
        void OweRecoil()
        {
            if (_me2 == null) _me2 = GetComponent<Element>();
            if (_me2 == null) return;
            float own = Mathf.Max(0f, _me2.MaxStrength - _lentStrength);
            _recoilOwed += own * DrawingConfig.WildGolemChargeRecoilShare;
        }

        void OnCollisionEnter(Collision c)
        {
            if (_beat != Beat.Run) return;

            float mul = _me != null ? _me.StrengthMul : 1f;
            float hit = Damage * mul;
            Vector3 shove = _dir * DrawingConfig.ChargeShove + Vector3.up * 2f;

            // the moment of contact, wherever it happened
            Vector3 spot = c.contactCount > 0 ? c.GetContact(0).point : transform.position;

            // an acolyte's zombie wrecking things feeds the wand back
            int chOwner = -1;
            var zo = GetComponentInParent<Zombie>();
            if (zo != null) chOwner = zo.OwnerId;
            var go = GetComponentInParent<Golem>();
            if (go != null) chOwner = go.OwnerId;
            // a wild golem is a nuisance, not an executioner: a hit that grows
            // with its size from a trip to a real blow, and it pays for landing it
            bool wild = go != null && go.OwnerId < 0;
            bool pays = wild && go.GetComponent<BossMark>() == null; // a boss never cracks on you (his call)
            if (wild)
                hit = DrawingConfig.WildGolemChargeDamage
                    * Mathf.Pow(Mathf.Max(0.05f, go.SizeMul), DrawingConfig.WildGolemChargeSizePower) * mul;
            if (FxLibrary.I != null)
                FxLibrary.Spawn(FxLibrary.I.GroundHit, spot);
            GrammarFX.PuffBurst(spot, new Color(0.9f, 0.85f, 0.7f), 4);
            // a golem lands with its own hit; a zombie with the plain thump
            if (!(go != null && Juice.Sound(Sfx.GolemHit, spot, 1f, Random.Range(0.92f, 1.06f)))
                && !Juice.Sound(Sfx.ThrownObjectHitting, spot, 0.9f, Random.Range(0.9f, 1.05f))) Juice.Thud(spot);

            // ★ BALANCE IS A BARRIER (his design): a planted wizard takes
            // the damage but not the tumble - the charger BOUNCES off
            // unless it hits hard enough to break the barrier. A slick
            // wizard is the opposite: flattened and sent gliding.
            // Shared by the pilot and a puppet, so the split lands the same
            // on every machine.
            Vector3 ShoveFor(Element pel)
            {
                float bal = pel != null
                    ? SpellPayload.ToHuman(3, pel.Data.Balance - pel.Natural.Balance) : 0f;
                if (bal > 15f && hit < bal * 1.2f)
                {
                    var mrb = GetComponent<Rigidbody>();
                    if (mrb != null)
                        mrb.linearVelocity = -_dir * Mathf.Max(4f, mrb.linearVelocity.magnitude * 0.6f)
                            + Vector3.up * 2.5f;
                    GrammarFX.PuffBurst(spot, new Color(0.95f, 0.8f, 0.3f), 6); // the BOING
                    return shove * 0.15f; // hurt, not toppled
                }
                if (bal < -15f)
                {
                    // flattened: extra shove, mostly flat, and the slip law glides it
                    Vector3 flat = _dir; flat.y = 0f;
                    GrammarFX.PuffBurst(spot, new Color(0.6f, 0.85f, 1f), 5); // the WHOOPS
                    return flat.normalized * shove.magnitude * 1.7f + Vector3.up * 1f;
                }
                return shove;
            }

            var player = c.collider.GetComponentInParent<SimpleFPSController>();
            if (player != null)
            {
                player.TakeHit(ShoveFor(player.GetComponent<Element>()), hit, $"{name} charge", chOwner, true);
                if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.TextPow, spot + Vector3.up * 1.2f);
                if (pays) OweRecoil();
            }
            else
            {
                var dmg = c.collider.GetComponentInParent<Element>();
                if (dmg != null && dmg.gameObject != gameObject)
                {
                    dmg.TakeDamage(hit, $"{name} charge", chOwner, true);
                    // a remote player's puppet is a player hit too: their own
                    // body takes the shove, and the same POW everyone sees
                    var av = dmg.GetComponentInParent<NetAvatar>();
                    if (av != null)
                    {
                        NetSync.SendKick(NetSync.OwnerIdOf(av.Id), ShoveFor(dmg), false);
                        if (FxLibrary.I != null) FxLibrary.Spawn(FxLibrary.I.TextPow, spot + Vector3.up * 1.2f);
                        if (pays) OweRecoil();
                    }
                }
                var orb = c.collider.attachedRigidbody;
                if (orb != null && !orb.isKinematic) orb.AddForce(shove, ForceMode.VelocityChange);
            }

            Stop(); // it spends itself on the first thing it meets
        }
    }
}
