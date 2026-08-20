using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum ParticleKind
    {
        Push,                         // level 0 - pure force
        Light, Dark,                  // level 1 - energy; opposites annihilate
        Dense, Spread, Glue, Repel,   // level 2 - property carriers
        Spark, Frost,                 // level 3 - elemental matter
        Lightning, Laser, Shadow,     // condensed states (level 3 - they absorb further)
        Flame,                        // GRAMMAR v4: Spark manifested by Dense - persistent fire, merges bigger
        BlackHole,                    // GRAMMAR v4: Dark lvl2 - pulls things in (family: Dark)
        BarrierMote                   // GRAMMAR v4: Dense+Spread paradox - isolates what it touches
    }

    /// Particles carry attributes; on collision the lower level dissolves into
    /// the higher. ResolveLaw applies GRAMMAR v4 (leveling / paradox / exotics).
    public class SpellParticle : MonoBehaviour
    {
        public ParticleKind Kind;
        public float Power = 1f;
        public Vector3 Vel;
        /// Fused size is the SUM of both parents, capped. Every fusion path
        /// must use this - never Max.
        public static float FuseSize(float a, float b) =>
            Mathf.Min(a + b, DrawingConfig.FusedSizeCap);

        /// Maps a source size to an effect-size multiplier, shared by every
        /// effect. RuneSizeMin (0.9) returns exactly 1; the 1f floor protects
        /// paths that pass no size.
        public static float SizeMul(float srcSize) =>
            Mathf.Clamp(0.55f + srcSize * 0.5f, 1f, DrawingConfig.FusedSizeMulMax);

        public float SrcSize = 1f; // zone radius of the emitting rune; rides the fusion chain
        public int Echo;           // ECHO powerup stacks: landing may re-emit

        // GRAMMAR v4 (SPELL_PARTICLES.md): same+same levels up, opposites
        // synthesize; all 12 runes in one lineage summons the Demon.
        public int GrammarLevel = 1;  // 1 = base · 2 = radiant self · 3 = ultimate
        public ulong Lineage;         // union of every rune that fed this chain
        public int SealId;            // which SEAL emitted this; same-seal siblings combine first

        /// What this particle died INTO (eater, field, matter blob, demon);
        /// the emitting rune waits for the final product to disappear before
        /// re-emitting.
        public Object BecameObj;
        public bool Dead => _dead;

        // Claimed: grabbed out of the magic world; the emitting rune sees it
        // as gone and re-emits.
        public bool Claimed;
        public Transform Holder;      // whose hand it's in (null once released)
        /// Whose cast this is - dormant wake rules ask which side the spell
        /// serves (0 = unowned/unknown: treats everyone as a friend).
        public int OwnerId;

        public void Claim(Transform holder)
        {
            Claimed = true;
            Holder = holder;
            _settled = false;
            _lure = null;
            Vel = Vector3.zero;
        }

        /// Let go (throw or drop): velocity in; the particle rejoins the magic
        /// world - it combines, seeks kin, and expires on its own clock.
        public void ReleaseHeld(Vector3 velocity)
        {
            Holder = null;
            Claimed = false;
            Vel = velocity;
            _settled = false; // brushing walls while held must not freeze the throw
            // throw/release activates after WakeDelaySeconds; a conjure ghost
            // instead fires where it lands
            if (!Dormant) return;
            if (PendingConjure != null) _wakeOnLand = true;
            else WakeIn(DrawingConfig.WakeDelaySeconds);
        }

        /// Steering from a possessing ghost. Velocity eases toward the
        /// direction rather than snapping, so heavy spells still feel heavy.
        public void Steer(Vector3 dir, float speed)
        {
            if (_dead) return;
            _settled = false;
            Vel = Vector3.MoveTowards(Vel, dir * speed, 26f * Time.deltaTime);
        }

        public void ThrowFrom(Vector3 velocity)
        {
            Vel = velocity;
            _settled = false;
            // vectors are never dormant; push/pull fly live from birth
            if (Kind == ParticleKind.Push) return;
            if (!Dormant) Sleep();
            WakeIn(DrawingConfig.WakeDelaySeconds);
        }

        // ---- DORMANT / ACTIVE ----------------------------------------------
        /// Inactive: smaller, faint, clock frozen, no effects. Wakes by
        /// throw/release, live-particle contact, an enemy entering its area,
        /// or a friendly inside the area needing what it offers.
        /// MP gap (flagged): the dormant flag is not synced yet (sides-sync).
        public bool Dormant { get; private set; }
        float _wakeAt = -1f;
        float _dormantLeft;
        float _dormantScan;
        bool _wakeOnLand;   // thrown conjure ghost: fly the full arc, fire at impact

        /// The real conjure waits inside this preview and fires where the
        /// ghost wakes - carry it, throw it, or leave it as a trap.
        public System.Action<Vector3> PendingConjure;

        // a fused-but-unmade PAIR: one ghost holds both halves; waking
        // re-births the partner live on the spot and nature runs the recipe
        ParticleKind _pendingKind;
        float _pendingPower, _pendingSrc;
        ulong _pendingLin;
        bool _hasPending;

        Vector3 _anchorPos, _anchorNrm;
        bool _hasAnchor;

        /// A ground/wall preview remembers its seal's spot and surface normal;
        /// hover point = anchor + normal × hover range. The anchor outlives
        /// the seal's ink.
        public void Sleep(Vector3 sealAt, Vector3 sealNormal)
        {
            _anchorPos = sealAt;
            _anchorNrm = sealNormal.sqrMagnitude > 0.001f ? sealNormal.normalized : Vector3.up;
            _hasAnchor = true;
            Sleep();
            // zero the emit velocity so the preview stays at its hover point;
            // ThrowFrom uses the base Sleep and sets Vel after
            Vel = Vector3.zero;
        }

        /// Hover is along the seal's normal: floor up, wall out, ceiling down.
        Vector3 HoverPoint() => _anchorPos + _anchorNrm * DrawingConfig.DormantHoverRange;

        /// Object-product combinations (steam, white hole, tornado, lvl3
        /// areas, exotics) sleep as one ghost holding both halves; waking
        /// re-births the partner and the recipe runs.
        static void StorePendingPair(SpellParticle a, SpellParticle b)
        {
            a._pendingKind = b.Kind;
            a._pendingPower = b.Power;
            a._pendingSrc = b.SrcSize;
            a._pendingLin = b.Lineage;
            a._hasPending = true;
            a.SrcSize = FuseSize(a.SrcSize, b.SrcSize);
            a.transform.localScale *= 1.25f;
            a.InheritAnchor(b);
            a.Vel = Vector3.zero;
            a.GhostLook();
            b.Die();
        }

        /// After a REAL merge ran asleep: the survivor re-anchors, settles,
        /// and goes back to ghost dress - Absorb/LevelMerge dressed it in its
        /// LIVE size and look, so the preview scale is re-applied on top.
        static void SettleDormantSurvivor(SpellParticle a, SpellParticle b)
        {
            var live = a._dead ? b : a;
            if (live._dead) return;
            live.InheritAnchor(live == a ? b : a);
            live.Vel = Vector3.zero;
            if (!live.Dormant) return;
            live.transform.localScale *= DrawingConfig.DormantPreviewScale;
            live.GhostLook();
        }

        /// A fused preview hovers over the closest of its parent seals.
        void InheritAnchor(SpellParticle other)
        {
            if (!other._hasAnchor) return;
            if (!_hasAnchor
                || (other._anchorPos - transform.position).sqrMagnitude
                   < (_anchorPos - transform.position).sqrMagnitude)
            {
                _anchorPos = other._anchorPos;
                _anchorNrm = other._anchorNrm;
                _hasAnchor = true;
            }
        }

        public void Sleep()
        {
            if (Dormant || _dead) return;
            // vectors are never dormant
            if (Kind == ParticleKind.Push) return;
            Dormant = true;
            _dormantLeft = DrawingConfig.DormantLifeSeconds;
            _wakeAt = -1f;
            transform.localScale *= DrawingConfig.DormantPreviewScale;
            GhostLook();
        }

        /// Schedule activation.
        public void WakeIn(float seconds)
        {
            if (!Dormant) return;
            if (_wakeAt < 0f || Time.time + seconds < _wakeAt)
                _wakeAt = Time.time + seconds;
        }

        public void Wake()
        {
            if (!Dormant || _dead) return;
            Dormant = false;
            _wakeAt = -1f;
            transform.localScale /= DrawingConfig.DormantPreviewScale;
            _age = 0f; // the clock was FROZEN - a stockpiled spell wakes fresh
            RefreshLook();
            ImpactFx(); // the pop of becoming real

            // a carried conjure fires where it woke; a moving ghost casts
            // along its heading, flight line continued to the ground
            if (PendingConjure != null)
            {
                var c = PendingConjure;
                PendingConjure = null;
                Vector3 at = transform.position;
                if (Vel.sqrMagnitude > 4f)
                {
                    Vector3 dir = Vel.normalized;
                    if (Physics.Raycast(at, dir, out var h1, 24f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        at = h1.point;
                    else if (Physics.Raycast(at + dir * 12f, Vector3.down, out var h2, 80f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        at = h2.point;
                }
                Die();
                c(at);
                return;
            }
            // a carried partner is re-born live beside it; contact runs the
            // recipe (tornado, exotic, paradox)
            if (_hasPending)
            {
                _hasPending = false;
                var mate = Emit(_pendingKind,
                    transform.position + Random.insideUnitSphere * 0.15f,
                    Vector3.up, Mathf.Max(0.2f, _pendingPower));
                if (mate != null)
                {
                    mate.SrcSize = _pendingSrc;
                    mate.Lineage = _pendingLin;
                    mate.OwnerId = OwnerId;
                }
            }
        }

        /// The reach the spell will have live - the preview's visible area,
        /// the trap's tripwire and the helper's range, all one number.
        float AreaReach() => Mathf.Max(1.2f, 1.6f * Mathf.Clamp(SizeMul(SrcSize), 0.6f, 3f));

        Transform _dormantSeek;   // a BODY the preview drifts toward (enemy/ally)
        float _seekLift;          // aim at chest height for bodies
        Vector3 _meetPoint;       // seal-mates converge on a fixed spot
        bool _meetAt;

        /// Dormant seeking: 1. fly at an enemy its effect can touch, 2. fly
        /// to an ally it can serve, 3. pool with sleeping kin, 4. else float
        /// until expiry. Flying only closes distance; waking obeys area rules.
        void DormantTick(float dt)
        {
            // however it got here, a vector wakes at once
            if (Kind == ParticleKind.Push) { Wake(); return; }
            if (_wakeAt > 0f && Time.time >= _wakeAt) { Wake(); return; }

            if (Holder == null && !_settled)
            {
                // a thrown mote mid-activation-beat is pure ballistics - no
                // drag or speed cap, so momentum survives into the live particle
                bool inFlight = _wakeAt > 0f || _wakeOnLand;
                if (_wakeOnLand)
                    Vel += Physics.gravity * 0.6f * dt; // the thrown ghost ARCS to its landing
                if (!inFlight)
                {
                    // no buoyancy while dormant; with nothing calling, home
                    // is the hover point over its own seal
                    if (_dormantSeek != null)
                        Vel += ((_dormantSeek.position + Vector3.up * _seekLift) - transform.position)
                            .normalized * DrawingConfig.DormantSeekSpeed * 2f * dt;
                    else if (_meetAt)
                        // a fixed rendezvous on the seal, never mid-air pursuit
                        Vel += (_meetPoint - transform.position) * 6f * dt;
                    else if (_hasAnchor)
                        Vel += (HoverPoint() - transform.position) * 2.2f * dt;
                    Vel *= Mathf.Max(0f, 1f - (_dormantSeek != null ? 1.2f : 3f) * dt);
                    if (Vel.sqrMagnitude > DrawingConfig.DormantSeekSpeed * DrawingConfig.DormantSeekSpeed)
                        Vel = Vel.normalized * DrawingConfig.DormantSeekSpeed;
                }
                transform.position += Vel * dt;

                // leash: an anchored preview never strays beyond a short
                // radius of its seal; a ghost chasing a body is exempt
                if (!inFlight && _hasAnchor && _dormantSeek == null)
                {
                    Vector3 off = transform.position - _anchorPos;
                    float leash = DrawingConfig.DormantHoverRange * 1.8f;
                    if (off.sqrMagnitude > leash * leash)
                    {
                        transform.position = _anchorPos + off.normalized * leash;
                        Vel *= 0.2f; // arriving at the fence kills the escape speed
                    }
                }
            }

            // an unused preparation politely leaves
            _dormantLeft -= dt;
            if (_dormantLeft < 1.2f)
                transform.localScale *= Mathf.Max(0.01f, 1f - dt / 1.2f);
            if (_dormantLeft <= 0f || transform.localScale.x < 0.004f) { Die(); return; }

            _dormantScan -= dt;
            if (_dormantScan > 0f) return;
            _dormantScan = 0.25f; // the tune: combining should feel eager
            _dormantSeek = null;
            _seekLift = 0f;
            _meetAt = false;

            float reach = AreaReach();
            float r2 = reach * reach;
            float seek2 = DrawingConfig.DormantSeekRange * DrawingConfig.DormantSeekRange;

            // ---- 0. same-seal siblings combine first, any kinds; the only
            // pair skipped is two loaded ghosts
            {
                bool meLoaded = _hasPending || PendingConjure != null;
                SpellParticle sib = null;
                float bestSib = seek2;
                foreach (var q in All)
                {
                    if (q == null || q == this || q._dead || !q.Dormant) continue;
                    if (SealId == 0 || q.SealId != SealId) continue;
                    if (meLoaded && (q._hasPending || q.PendingConjure != null)) continue;
                    float d2q = (q.transform.position - transform.position).sqrMagnitude;
                    if (d2q < bestSib) { bestSib = d2q; sib = q; }
                }
                if (sib != null)
                {
                    // meet at a fixed spot between the two hover homes
                    Vector3 mine = _hasAnchor ? HoverPoint() : transform.position;
                    Vector3 theirs = sib._hasAnchor ? sib.HoverPoint() : sib.transform.position;
                    _meetPoint = (mine + theirs) * 0.5f;
                    _meetAt = true;
                    return;
                }
            }

            bool ownerAcolyte = Sides.IsAcolyte(OwnerId);
            var lp = SimpleFPSController.All.Count > 0 ? SimpleFPSController.All[0] : null;
            bool lpEnemy = lp != null && Sides.IsAcolytePlayer(lp) != ownerAcolyte;
            float lpD2 = lp != null
                ? (lp.transform.position - transform.position).sqrMagnitude : float.MaxValue;

            // ---- 1. ENEMIES, if this kind can touch them (Dangerous = the
            // same law zombies fear). Inside the area = wake; inside seek
            // range = fly at it.
            if (Dangerous())
            {
                Transform foe = null;
                float best = seek2;
                if (!ownerAcolyte)
                    foreach (var z in Zombie.All) // zombies side with the acolytes
                    {
                        if (z == null) continue;
                        float d2z = (z.transform.position - transform.position).sqrMagnitude;
                        if (d2z <= r2) { Wake(); return; }
                        if (d2z < best) { best = d2z; foe = z.transform; }
                    }
                if (lpEnemy)
                {
                    if (lpD2 <= r2) { Wake(); return; }
                    if (lpD2 < best) foe = lp.transform;
                }
                if (foe != null) { _dormantSeek = foe; _seekLift = 0.5f; return; }
            }

            // ---- 2. allies missing what it carries: need inside the area
            // wakes it, need inside seek range draws it over; no need = stay.
            // heal-wake waits for an identifiable heal lineage (phase 2).
            if (lp != null && !lpEnemy)
            {
                var board = BodyState.Of(lp);
                if (board != null)
                {
                    bool needsMe =
                        (board.Temp < BodyState.TempBandLow + 3f && Temp > 30f)   // freezing + I am warm
                        || (board.Temp > BodyState.TempBandHigh - 3f && Temp < 8f); // burning + I am chill
                    if (needsMe && lpD2 <= r2) { Wake(); return; }
                    if (needsMe && lpD2 <= seek2) { _dormantSeek = lp.transform; _seekLift = 0.5f; return; }
                }
            }

            // ---- 3. SLEEPING KIN it can pool with (same kind - the fusions
            // that would TRANSFORM wait for their phase-2 preview forms).
            // Contact does the merge through the ordinary collision law.
            foreach (var q in All)
            {
                if (q == null || q == this || q._dead || !q.Dormant) continue;
                if (q.Kind != Kind || Kind == ParticleKind.Flame || Kind == ParticleKind.Push) continue;
                float d2q = (q.transform.position - transform.position).sqrMagnitude;
                if (d2q <= seek2) { _dormantSeek = q.transform; return; }
            }

            // ---- 4. nothing calls: hold position, wait, fade in due time
        }

        /// Dormant look: the kind's own colour, faint; authored FX skins are
        /// left untouched.
        void GhostLook()
        {
            if (_rend == null) _rend = GetComponent<Renderer>();
            if (_rend == null || _customLook != null) return;
            KindLook(out var c, out _);
            // a pending pair wears both parents' colours mixed (authored
            // miniatures are the phase-2 art pass)
            if (_hasPending)
            {
                KindColor(_pendingKind, out var pc, out _);
                c = Color.Lerp(c, pc, 0.5f);
            }
            // nearly solid; the slight see-through marks it as a preview
            _rend.sharedMaterial = MatterFX.Particle(
                new Color(c.r, c.g, c.b, 0.85f), MoteShade.Transparent, 0.02f, 0.25f);
        }

        float _impactFxAt; // juice throttle - repeats within the window are noise

        /// One impact effect per kind at the point of contact, throttled per
        /// particle so chains read as one hit.
        public void ImpactFx()
        {
            if (Time.time - _impactFxAt < 0.5f) return;
            _impactFxAt = Time.time;
            var lib = FxLibrary.I;
            if (lib == null) return;
            Vector3 at = transform.position;
            var fam = RuneGrammar.Family(Kind);
            if (Kind == ParticleKind.Flame || fam == ParticleKind.Spark)
            {
                FxLibrary.Spawn(lib.HitSpark, at);
                var boom = FxLibrary.Spawn(lib.FireBurst, at);
                if (boom != null) boom.transform.localScale *= 0.55f;
            }
            else if (fam == ParticleKind.Frost)
                FxLibrary.Spawn(lib.IceHit, at);
            else if (Kind == ParticleKind.Push)
                FxLibrary.Spawn(lib.HitVector, at);
            else if (fam == ParticleKind.Light || Kind == ParticleKind.Lightning)
                FxLibrary.Spawn(lib.HitLight, at);
            else if (fam == ParticleKind.Dark)
                FxLibrary.Spawn(lib.Poof, at);
            else if (Kind == ParticleKind.Dense || Kind == ParticleKind.Spread)
                FxLibrary.Spawn(lib.HitThud, at);
            else
                FxLibrary.Spawn(lib.Poof, at);
        }

        /// A Y (Toward-rune) vector: flies its way, POINTS the other way.
        public bool IsY =>
            (Lineage & RuneGrammar.Bit(RuneType.DirectionToward)) != 0
            && (Lineage & RuneGrammar.Bit(RuneType.DirectionAway)) == 0;

        /// Push particles render as an arrow glyph (Y forks), built once
        /// lineage says which; an FX_Push prefab overrides the look outright.
        bool _vectorShaped;
        void EnsureVectorShape()
        {
            if (_vectorShaped || _dead || Kind != ParticleKind.Push) return;
            if (_age < 0.06f) return; // let the seal stamp lineage first
            _vectorShaped = true;
            if (PrefabVault.Get("FX_Push") != null) return;
            // both vectors are arrows; colour tells them apart
            var mat = IsY ? MatterFX.Get(new Color(0.7f, 0.35f, 0.95f), MoteShade.Additive)
                : _rend != null ? _rend.sharedMaterial : null;
            if (_rend != null) _rend.enabled = false;

            void Bar(Vector3 pos, float yaw, float len)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = "Glyph";
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(transform, false);
                g.transform.localPosition = pos;
                g.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
                g.transform.localScale = new Vector3(0.32f, 0.32f, len);
                if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
            }

            Bar(new Vector3(0f, 0f, -0.1f), 0f, 2.4f);        // shaft
            Bar(new Vector3(0.44f, 0f, 0.78f), -38f, 1.1f);   // barb
            Bar(new Vector3(-0.44f, 0f, 0.78f), 38f, 1.1f);   // barb
        }

        // the attribute payload every particle carries
        public float Temp, Lum, Density, Stick;

        const float AirDensity = 0.55f;      // below this effective density the particle rises
        const float PlasmaDensity = 1.0f;    // at/above this density elementals transform

        static readonly List<SpellParticle> All = new List<SpellParticle>();

        /// Every live particle - the sticky hand scans this for grab targets.
        public static IReadOnlyList<SpellParticle> Living => All;

        Renderer _rend;
        float _age, _fearTick, _strikeTick;
        float _chaosLeft;      // ChaosGrip paradox: random impulses, uncontrollable
        float _isolatedUntil;  // barrier-moted: refuses ALL chemistry until this
        float _auraTick;       // lvl2 particles radiate their effect around them
        float _donateTick;     // persistent particles (Flame, lvl2 Glue) re-donate on a beat
        float _patchTick;      // ground patches (settled Glue/Repel) act on their own beat
        float _appetite;       // 0..1 personality: how much this mote stalks LIVING things
        float _lureRetarget;
        Transform _lure;       // the thing this mote is currently stalking
        int _generation;
        bool _dead, _settled;

        // Pools are per kind, so a thawed body wakes with its look, light and
        // glyph shape already built.
        static readonly Dictionary<ParticleKind, Stack<SpellParticle>> _pool =
            new Dictionary<ParticleKind, Stack<SpellParticle>>();
        const int PoolKeep = 6; // per kind - beyond this, dying particles really die

        static Stack<SpellParticle> PoolFor(ParticleKind k)
        {
            if (!_pool.TryGetValue(k, out var s)) _pool[k] = s = new Stack<SpellParticle>();
            return s;
        }

        /// Load-time warmup: build and freeze a few of every kind so shaders
        /// and FX prefabs instantiate once.
        public static void PrewarmPool(int perKind = 2)
        {
            foreach (ParticleKind k in System.Enum.GetValues(typeof(ParticleKind)))
                for (int i = 0; i < perKind; i++)
                    Emit(k, new Vector3(0f, -900f, 0f), Vector3.up, 1f).Die();
        }

        /// Zero all per-life state. Kind, look, light and the vector glyph
        /// survive on purpose - the expensive parts the pool exists to keep.
        void ResetForReuse()
        {
            _dead = false;
            _age = 0f;
            _settled = false;
            _chaosLeft = 0f;
            _isolatedUntil = 0f;
            _auraTick = _donateTick = _patchTick = 0f;
            _fearTick = _strikeTick = _lureRetarget = 0f;
            _impactFxAt = 0f;
            _lure = null;
            _slamActive = false;
            _slamPrey = null;
            _scanCd = 0f;
            _strikeGen = 0;
            GrammarLevel = 1;
            Lineage = 0;
            SealId = 0;
            BecameObj = null;
            Claimed = false;
            Holder = null;
            Dormant = false;      // pool reborn = live defaults; Sleep() re-arms
            _wakeAt = -1f;
            _dormantLeft = 0f;
            _hasAnchor = false;
            _hasPending = false;
            _wakeOnLand = false;
            PendingConjure = null;
            OwnerId = 0;
            Temp = Lum = Density = Stick = 0f;
            Vel = Vector3.zero;
            SrcSize = DrawingConfig.RuneSizeMin;
            Echo = 0;
        }

        // ------------------------------------------------------------- birth --
        // ---- strike delivery: erupt, pounce, slam, shatter; survivors hover
        // as turrets. Delivery only - chemistry untouched.
        Vector3 _slamPoint;
        Transform _slamPrey; // live aim while it exists
        bool _slamActive;
        float _scanCd;
        int _strikeGen; // burst children don't burst again

        /// Vectors keep their own flight law; the sky-scale kinds never hover.
        bool StrikeKind => Kind != ParticleKind.Push && Kind != ParticleKind.Lightning
            && Kind != ParticleKind.BlackHole && Kind != ParticleKind.BarrierMote;

        /// Pick a slam target at birth; no target = fly off and hover as a
        /// turret.
        void StrikeLaunch(int generation)
        {
            _strikeGen = generation;
            _slamActive = false;
            _scanCd = 0f;
            if (!StrikeKind) return;
            float best = DrawingConfig.StrikeLockRange * DrawingConfig.StrikeLockRange;
            var prey = Targets.Nearest(transform.position, ref best, includePlayers: false);
            if (prey == null) return;
            _slamPrey = prey;
            _slamPoint = prey.position + Vector3.up * 0.6f;
            _slamActive = true;
            _settled = false;
            // the pounce: up and out, fast
            Vel = ((_slamPoint - transform.position).normalized + Vector3.up * 0.25f).normalized
                * DrawingConfig.StrikeSpeed;
        }

        /// Impact: the payload lands, then the body shatters into pieces that
        /// are real particles.
        void Burst()
        {
            if (!_slamActive) return;
            _slamActive = false;
            _slamPrey = null;

            // the burst delivers the payload to everything in its blast
            // radius; rune size scales the radius
            float aoe = 1.6f * Mathf.Max(1f, transform.localScale.x * 2f);
            var hits = Physics.OverlapSphere(transform.position, aoe);
            foreach (var h in hits)
                if (h != null && !h.isTrigger) Touch(h);
            if (_strikeGen > 0) { Die(); return; } // debris doesn't re-shatter
            int n = Mathf.Max(2, DrawingConfig.StrikeBurstPieces);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = (Random.onUnitSphere + Vector3.up * 0.6f).normalized;
                var piece = Emit(Kind, transform.position + d * 0.3f, d, 0.5f, 1);
                if (piece != null) piece.SrcSize = SrcSize;   // debris is the same SIZE of spell
                if (piece != null) piece.Vel = d * (DrawingConfig.StrikeSpeed * 0.45f);
            }
            Juice.Thud(transform.position);
            Die();
        }

        public static SpellParticle Emit(ParticleKind kind, Vector3 pos, Vector3 dir,
            float intensity, int generation = 0)
        {
            SpellParticle p = null;
            var stack = PoolFor(kind);
            while (stack.Count > 0 && (p = stack.Pop()) == null) { } // scene changes leave husks
            GameObject go;
            if (p != null)
            {
                go = p.gameObject;
                go.SetActive(true);
                p.ResetForReuse();
                All.Add(p);
                if (All.Count > DrawingConfig.ParticleCap && All[0] != null)
                    All[0].Die(); // oldest yields - the same fence Awake holds
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.GetComponent<SphereCollider>().isTrigger = true;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true; // moves by script; triggers do the touching
                p = go.AddComponent<SpellParticle>();
                p.Kind = kind;
            }
            go.name = "P_" + kind;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            // state particles start imposing; the bigger body is also a
            // bigger trigger, so strikes connect
            go.transform.localScale = Vector3.one * (kind == ParticleKind.Push ? 0.18f : 0.3f);
            p.Power = Mathf.Clamp(intensity, 0.2f, 2f);
            p.SrcSize = DrawingConfig.RuneSizeMin;  // callers that know better overwrite it
            p._generation = generation;
            p._appetite = Random.value; // personality: some motes stalk, some are lazy

            // the payload is the rune: fixed per kind, symmetric both ways
            float k = Mathf.Lerp(0.75f, 1.5f, Mathf.Clamp01(intensity));
            switch (kind)
            {
                case ParticleKind.Spark: p.Temp = DrawingConfig.SparkHeatDelta * k; break;
                case ParticleKind.Frost: p.Temp = -DrawingConfig.SparkHeatDelta * k; break;
                case ParticleKind.Light: p.Lum = k; break;
                case ParticleKind.Dark: p.Lum = -k; break;
                case ParticleKind.Dense: p.Density = 1.2f * k; break;
                case ParticleKind.Spread: p.Density = -1.2f * k; break;
                case ParticleKind.Glue: p.Stick = k; break;
                case ParticleKind.Repel: p.Stick = -k; break;
            }

            // vectors fly; everything else blooms slowly and stays near the
            // seal until a force moves it
            float speed = kind == ParticleKind.Push ? 4.2f : 0.9f; // vectors FLY
            p.Vel = dir.normalized * speed
                + Random.insideUnitSphere * (kind == ParticleKind.Push ? 0.2f : 0.22f);

            if (kind == ParticleKind.Light && go.GetComponent<Light>() == null)
            {
                var l = go.AddComponent<Light>(); // a thawed Light keeps its lamp
                l.type = LightType.Point; l.range = 4.5f; l.intensity = 2.2f;
                l.color = new Color(1f, 0.96f, 0.8f);
            }

            p.RefreshLook();
            // after the payload: slam when prey is in range, hovering turret
            // when not; overrides the bloom velocity only when it locks
            p.StrikeLaunch(generation);
            return p;
        }

        void Awake()
        {
            _rend = GetComponent<Renderer>();
            All.Add(this);
            if (All.Count > DrawingConfig.ParticleCap && All[0] != null)
                All[0].Die(); // oldest yields - mayhem needs a fence
        }

        void OnDestroy() => All.Remove(this);

        void Die()
        {
            if (_dead) return;
            _dead = true;
            All.Remove(this);
            // pooled, keeping its kind-specific look for the next cast
            var stack = PoolFor(Kind);
            if (stack.Count < PoolKeep)
            {
                transform.SetParent(null); // a hand may still be holding us
                if (_vectorShaped)
                {
                    // glyph tint is per-life; the next life re-shapes it
                    _vectorShaped = false;
                    for (int i = transform.childCount - 1; i >= 0; i--)
                    {
                        var c = transform.GetChild(i);
                        if (c.name == "Glyph") Destroy(c.gameObject);
                    }
                }
                gameObject.SetActive(false);
                stack.Push(this);
            }
            else Destroy(gameObject);
        }

        // ------------------------------------------------------------ living --
        void Update()
        {
            if (_dead) return;
            float dt = Time.deltaTime;
            _age += dt;
            EnsureVectorShape();

            // dormant: everything below (auras, strikes, lures, fear,
            // chemistry, decay) is frozen, held or free alike
            if (Dormant) { DormantTick(dt); return; }

            // claimed: no lure; held, the hand drives the position (HandGrab),
            // free, plain physics. Auras keep burning everyone else, and the
            // lifetime clock keeps running.
            if (Claimed)
            {
                if (GrammarLevel >= 2 || Kind == ParticleKind.Flame) TickAura(dt);
                if (Holder == null && !_settled)
                {
                    Vel += Vector3.down * (EffDensity() - AirDensity) * 2.5f * dt;
                    Vel *= Mathf.Max(0f, 1f - 0.6f * dt);
                    transform.position += Vel * dt;
                }
                _fearTick -= dt;
                if (_fearTick <= 0f)
                {
                    _fearTick = 0.4f;
                    if (Dangerous()) ZombieBrain.ScareVisible(transform.position, 11f, EffectiveLum());
                }
                float claimedLife = DrawingConfig.ParticleLife
                    * (Kind == ParticleKind.Flame ? 2.5f
                     : Kind == ParticleKind.BlackHole ? 1.5f
                     : GrammarLevel >= 2 ? 1.5f : 1f); // same clock as free particles
                if (_age > claimedLife - 0.8f)
                    transform.localScale *= Mathf.Max(0.01f, 1f - dt / 0.8f);
                if (_age > claimedLife || transform.localScale.x < 0.015f) Die();
                return;
            }

            if (Kind == ParticleKind.Lightning) TickLightning(dt);
            else if (Kind == ParticleKind.BlackHole) TickBlackHole(dt);

            // GRAMMAR v4: lvl2 particles radiate; flames burn where they sit;
            // chaos-grip products jitter uncontrollably
            if (GrammarLevel >= 2 || Kind == ParticleKind.Flame) TickAura(dt);
            if (_chaosLeft > 0f)
            {
                _chaosLeft -= dt;
                Vel += Random.insideUnitSphere * 14f * dt;
                _settled = false;
            }

            // ---- THE STRIKE TICK ----
            if (_slamActive)
            {
                // the slam follows living prey; the snapshot point is the
                // fallback for a dead one
                if (_slamPrey != null) _slamPoint = _slamPrey.position + Vector3.up * 0.6f;
                Vel = (_slamPoint - transform.position).normalized * DrawingConfig.StrikeSpeed;
                transform.position += Vel * dt;
                if ((transform.position - _slamPoint).sqrMagnitude < 1.4f) Burst();
                return; // the slam owns the frame - seeking/gravity stand down
            }
            if (StrikeKind)
            {
                // the turret half: hold in the air and watch; prey in range
                // triggers the same quick slam
                _scanCd -= dt;
                if (_scanCd <= 0f)
                {
                    _scanCd = 0.25f;
                    float best = DrawingConfig.StrikeLockRange * DrawingConfig.StrikeLockRange;
                    var prey = Targets.Nearest(transform.position, ref best, includePlayers: false);
                    if (prey != null)
                    {
                        _slamPrey = prey;
                        _slamPoint = prey.position + Vector3.up * 0.6f;
                        _slamActive = true;
                        _settled = false;
                    }
                }
            }

            if (!_settled)
            {
                // density is weight: heavy falls, thinned floats; a strike
                // elemental hovers where it stopped instead
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.BlackHole
                    && !StrikeKind)
                    Vel += Vector3.down * (EffDensity() - AirDensity) * 2.5f * dt;
                else if (StrikeKind)
                    Vel *= Mathf.Max(0f, 1f - 1.6f * dt); // ease to a hanging stop

                    // MOTES SEEK EACH OTHER
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.BlackHole
                    && Kind != ParticleKind.BarrierMote)
                {
                    // seal kin first: same-drawing particles attract above
                    // all; foreign ones only when no sibling is in reach.
                    // a Push only seeks other Pushes.
                    bool iAmPush = Kind == ParticleKind.Push;
                    SpellParticle near = null;
                    bool nearIsKin = false, nearIsVector = false;
                    float baseRange = DrawingConfig.ParticleKinRange * DrawingConfig.ParticleKinRange;
                    float kinRange = baseRange * 4f; // same-seal reach: 2× the distance
                    float vecRange = baseRange * 9f; // twin sight: 3× - vectors FLY, they need the head start
                    float bestSqr = float.MaxValue;
                    for (int i = 0; i < All.Count; i++)
                    {
                        var o = All[i];
                        if (o == this || o == null || o._dead) continue;
                        if (iAmPush)
                        {
                            // arrows seek arrows, Ys seek Ys
                            if (o.Kind == ParticleKind.Push)
                            {
                                if (o.IsY != IsY) continue; // arrow×Y stays undefined - no bending
                                float vd = (o.transform.position - transform.position).sqrMagnitude;
                                if (vd <= vecRange && vd < bestSqr)
                                { near = o; bestSqr = vd; nearIsVector = true; }
                                continue;
                            }
                            // wind herds every essence; reach measures to the
                            // surface, so big things are bigger sails
                            if (o.Kind != ParticleKind.BarrierMote)
                            {
                                float reach = DrawingConfig.ParticleKinRange
                                    + (transform.localScale.x + o.transform.localScale.x) * 0.5f;
                                float wd = (o.transform.position - transform.position).sqrMagnitude;
                                if (wd < reach * reach)
                                {
                                    o.Vel += Vel.normalized * (3f * Power * dt);
                                    o._settled = false; // wind wakes resting embers
                                }
                            }
                            continue;
                        }
                        if (o.Kind == ParticleKind.Push)
                        {
                            // vectors out-pull strangers, never seal kin
                            if (nearIsKin) continue;
                            float vd = (o.transform.position - transform.position).sqrMagnitude;
                            if (vd > kinRange) continue;
                            if (nearIsVector && vd >= bestSqr) continue;
                            near = o; bestSqr = vd; nearIsVector = true;
                            continue;
                        }
                        bool kin = SealId != 0 && o.SealId == SealId;
                        if (nearIsVector && !kin) continue; // a vector outranks strangers only
                        float d = (o.transform.position - transform.position).sqrMagnitude;
                        if (d > (kin ? kinRange : baseRange)) continue;
                        if (nearIsKin && !kin) continue;              // a stranger never beats family
                        if (kin && (nearIsVector || !nearIsKin))
                        { near = o; bestSqr = d; nearIsKin = true; nearIsVector = false; continue; }
                        if (d < bestSqr) { bestSqr = d; near = o; nearIsKin = kin; }
                    }

                    // WIND MOVES MATTER TOO
                    if (iAmPush)
                        for (int mi = 0; mi < Matter.Living.Count; mi++) // indexed - foreach boxed the enumerator every frame
                        {
                            var mm = Matter.Living[mi];
                            if (mm == null) continue;
                            var mrb = mm.Body;
                            if (mrb == null || mrb.isKinematic) continue;
                            float reach = DrawingConfig.ParticleKinRange
                                + (transform.localScale.x + mm.transform.localScale.x) * 0.5f;
                            if ((mm.transform.position - transform.position).sqrMagnitude < reach * reach)
                                mrb.AddForce(Vel.normalized * (3f * Power * dt), ForceMode.VelocityChange);
                        }

                    if (near != null)
                    {
                        // affinity beats appetite
                        Vector3 to = near.transform.position - transform.position;
                        if (iAmPush && nearIsVector)
                        {
                            // raw pull plus a real steer: the arrow bends its
                            // flight line toward its twin
                            Vel += to.normalized * (22f * dt);
                            float sp = Vel.magnitude;
                            if (sp > 0.05f)
                                Vel = Vector3.Slerp(Vel / sp, to.normalized,
                                    Mathf.Clamp01(6f * dt)) * sp;
                        }
                        else
                            Vel += to.normalized * ((iAmPush ? 4.5f : nearIsVector ? 3.6f : 2.2f) * dt);
                        // two fast twins can STEP OVER a tiny meet window in a
                        // single frame - theirs is wider so the pass connects
                        float meetR = iAmPush && nearIsVector ? 0.3f : 0.12f;
                        if (bestSqr < meetR * meetR && GetInstanceID() < near.GetInstanceID())
                            ResolveLaw(this, near);
                    }
                    else if (!iAmPush)
                    {
                        // no particle nearby - a spell still hunts MATTER blobs
                        Matter mNear = null;
                        float mBest = baseRange;
                        for (int mi = 0; mi < Matter.Living.Count; mi++) // indexed - foreach boxed the enumerator every frame
                        {
                            var mm = Matter.Living[mi];
                            if (mm == null || mm.Touched) continue;
                            float md = (mm.transform.position - transform.position).sqrMagnitude;
                            if (md < mBest) { mBest = md; mNear = mm; }
                        }
                        if (mNear != null)
                            Vel += (mNear.transform.position + Vector3.up * 0.15f - transform.position)
                                .normalized * (2.4f * dt);
                        else TickLure(dt); // nothing magical around - stalk something ALIVE
                    }
                }
                // condensed movers integrate their own motion in Tick* - moving
                // them here too ran them at DOUBLE speed (verified)
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.BlackHole)
                {
                    Vel *= Mathf.Max(0f, 1f - (Kind == ParticleKind.Push ? 0.25f : 1.4f) * dt);
                    transform.position += Vel * dt;
                    // arrows point WHERE THEY'RE GOING; Ys point the other way
                    if (_vectorShaped && Vel.sqrMagnitude > 0.02f)
                        transform.rotation = Quaternion.LookRotation(IsY ? -Vel : Vel);
                    // a vector at rest dies; the sustain law re-emits a fresh one
                    if (Kind == ParticleKind.Push && _age > 0.6f && Vel.sqrMagnitude < 0.12f)
                    {
                        Die();
                        return;
                    }
                }
            }
            else if (Kind != ParticleKind.Push && Kind != ParticleKind.BarrierMote)
            {
                // a SETTLED ember still watches: prey wandering close wakes it
                // (TickLure clears _settled, and next frame it moves again)
                TickLure(dt);
            }

            // FEAR IS VISUAL: nearby zombies that can SEE a dangerous particle
            // panic - a flame carrying darkness is invisible, and they walk
            // right into it
            _fearTick -= dt;
            if (_fearTick <= 0f)
            {
                _fearTick = 0.4f;
                if (Dangerous()) ZombieBrain.ScareVisible(transform.position, 11f, EffectiveLum());
            }

            float life = DrawingConfig.ParticleLife
                * (Kind == ParticleKind.Flame ? 2.5f     // a flame is a fixture, not a spark
                 : Kind == ParticleKind.BlackHole ? 1.5f
                 : GrammarLevel >= 2 ? 1.5f : 1f);       // leveled particles earn their time
            if (_age > life - 0.8f)
                transform.localScale *= Mathf.Max(0.01f, 1f - dt / 0.8f);
            if (_age > life || transform.localScale.x < 0.015f) Die();
        }

        /// Zombies attract spells - and only zombies; players are never lured
        /// targets.
        void TickLure(float dt)
        {
            if (_appetite < 0.35f) return; // the lazy third never stalks

            _lureRetarget -= dt;
            if (_lureRetarget <= 0f)
            {
                _lureRetarget = 0.5f;
                float best = DrawingConfig.ParticleChaseRange * DrawingConfig.ParticleChaseRange;
                _lure = Targets.Nearest(transform.position, ref best, includePlayers: false);
            }
            if (_lure == null) return;

            Vector3 to = _lure.position + Vector3.up * 0.8f - transform.position;
            float range = DrawingConfig.ParticleChaseRange;
            if (to.sqrMagnitude > range * range * 1.7f) { _lure = null; return; } // it got away
            _settled = false; // the waiting ember wakes
            Vel += to.normalized * (DrawingConfig.ParticleChaseAccel * _appetite * dt);
        }

        float EffDensity()
        {
            float baseD;
            switch (Kind)
            {
                case ParticleKind.Push: baseD = AirDensity; break;          // flies straight
                case ParticleKind.Light:
                case ParticleKind.Dark: baseD = 0.35f; break;               // energy drifts
                case ParticleKind.Spark:
                case ParticleKind.Frost: baseD = 0.9f; break;               // embers sink gently
                default: baseD = 0.8f; break;
            }
            return baseD + Density * 0.45f;
        }

        bool Dangerous() =>
            Mathf.Abs(Temp) >= 20f
            || Kind == ParticleKind.Lightning
            || Kind == ParticleKind.Flame; // (the black hole is invisible - never feared, always fatal)

        /// How visible this particle is to a googly eye. Base glow by kind,
        /// dimmed by any darkness it carries - the invisible-flame trap.
        float EffectiveLum()
        {
            float glow;
            switch (Kind)
            {
                case ParticleKind.Spark: glow = 0.6f; break;
                case ParticleKind.Frost: glow = 0.35f; break;
                case ParticleKind.Lightning: glow = 2.5f; break;
                case ParticleKind.Flame: glow = 0.9f; break; // a flame is SEEN - unless darkness dims it
                case ParticleKind.BlackHole: glow = -1f; break; // light doesn't leave it
                default: glow = 0.3f; break;
            }
            return glow + Lum;
        }

        // --------------------------------------------------------- collision --
        void OnTriggerEnter(Collider other)
        {
            if (_dead) return;
            var op = other.GetComponent<SpellParticle>();
            if (op != null)
            {
                // both sides get the event - only one resolves the law
                if (GetInstanceID() < op.GetInstanceID()) ResolveLaw(this, op);
                return;
            }
            if (other.isTrigger) return;
            if (Dormant)
            {
                // a thrown conjure ghost fires where it lands; anything else
                // a preview touches, it ignores
                if (_wakeOnLand) { _wakeOnLand = false; Wake(); }
                return;
            }
            if (_slamActive)
            {
                // the slam lands: payload first, then the shatter
                Touch(other);
                Burst();
                return;
            }
            Touch(other);
        }

        /// Persistent = delivers on a beat and STAYS: flames, lvl2 grip/slip,
        /// and settled Glue/Repel ground patches (one predicate, two callers).
        bool Persistent => Kind == ParticleKind.Flame
            || (GrammarLevel >= 2 && (Kind == ParticleKind.Glue || Kind == ParticleKind.Repel))
            || (_settled && (Kind == ParticleKind.Glue || Kind == ParticleKind.Repel));

        /// Persistent particles deliver to whatever stays in them; Enter
        /// alone fires only once.
        void OnTriggerStay(Collider other)
        {
            if (_dead || other.isTrigger) return;
            if (Dormant) return; // a preview touches NOTHING - physics
                                 // callbacks bypass Update's gate, not this one
            if (other.GetComponent<SpellParticle>() != null) return;
            if (Persistent) Touch(other);
        }

        static int Level(ParticleKind k)
        {
            switch (k)
            {
                case ParticleKind.Push: return 0;
                case ParticleKind.Light:
                case ParticleKind.Dark: return 1;
                case ParticleKind.Dense:
                case ParticleKind.Spread:
                case ParticleKind.Glue:
                case ParticleKind.Repel: return 2;
                default: return 3; // spark, frost, and the condensed states
            }
        }

        /// GRAMMAR v4 collision resolution (SPELL_PARTICLES.md). Order:
        ///   1. barrier isolation (nothing combines through a barrier)
        ///   2. same family: level up (lvl2 radiant self, lvl3 ultimate area)
        ///   3. opposites: paradox synthesis
        ///   4. the v2 substrate: lower level dissolves into higher, carriers
        ///      pool, otherwise plain physics.
        static void ResolveLaw(SpellParticle a, SpellParticle b)
        {
            if (a._dead || b._dead) return;

            // a live particle wakes a sleeper on contact (in a holder's hand
            // too), then the full law runs on the woken pair
            if (a.Dormant != b.Dormant)
            {
                (a.Dormant ? a : b).Wake();
                // a woken conjure-ghost DIED into its conjure just now - the
                // law must not keep resolving a corpse against a live mote
                if (a._dead || b._dead) return;
            }
            else if (a.Dormant)
            {
                // same law asleep: particle products merge for real (still
                // dormant); object products (steam, tornado, lvl3 areas,
                // exotics) sleep as one loaded ghost and form at wake
                bool aLoaded = a._hasPending || a.PendingConjure != null;
                bool bLoaded = b._hasPending || b.PendingConjure != null;
                if (aLoaded || bLoaded)
                {
                    // a loaded ghost absorbs its seal-mates, so a drawing
                    // ends as one waiting ghost
                    if (aLoaded != bLoaded && a.SealId != 0 && a.SealId == b.SealId)
                    {
                        var host = aLoaded ? a : b;
                        host.Absorb(host == a ? b : a);
                        SettleDormantSurvivor(a, b);
                        return;
                    }
                    BounceApart(a, b, 0.25f); // two full ghosts drift apart, gently
                    return;
                }

                var dfa = RuneGrammar.Family(a.Kind);
                var dfb = RuneGrammar.Family(b.Kind);
                if (dfa == dfb)
                {
                    if (a.Kind == ParticleKind.Flame || b.Kind == ParticleKind.Flame)
                    {
                        var fl = a.Kind == ParticleKind.Flame ? a : b;
                        fl.Absorb(fl == a ? b : a); // a flame eats its kin
                    }
                    else if (dfa == ParticleKind.Push
                        || Mathf.Max(EffLevel(a), EffLevel(b)) >= 2)
                    { StorePendingPair(a, b); return; } // tornado / lvl3 AREA = objects, wait
                    else LevelMerge(a, b); // heat+heat = a REAL lvl2, asleep
                    SettleDormantSurvivor(a, b);
                    return;
                }
                if (RuneGrammar.ParadoxOf(a.Kind, b.Kind) != ParadoxKind.None
                    || RuneGrammar.ExoticOf(a.Kind, b.Kind) != RuneGrammar.ExoticKind.None)
                { StorePendingPair(a, b); return; } // steam & friends form at wake, identically

                int dla = Level(a.Kind), dlb = Level(b.Kind);
                if (dla != dlb || dla == 2)
                {
                    var hi2 = dla >= dlb ? a : b;
                    hi2.Absorb(hi2 == a ? b : a); // the substrate rule, asleep
                    SettleDormantSurvivor(a, b);
                    return;
                }
                BounceApart(a, b, 0.25f); // rock bounces off ice, asleep too
                return;
            }

            // an isolated particle refuses ALL chemistry - the barrier is the
            // system's insulator (and the only way to stop an accidental Demon)
            if (Time.time < a._isolatedUntil || Time.time < b._isolatedUntil)
            {
                BounceApart(a, b, 1.2f);
                return;
            }

            // a BarrierMote doesn't combine - it ISOLATES the other particle
            if (a.Kind == ParticleKind.BarrierMote || b.Kind == ParticleKind.BarrierMote)
            {
                var mote = a.Kind == ParticleKind.BarrierMote ? a : b;
                var tgt = mote == a ? b : a;
                if (tgt.Kind != ParticleKind.BarrierMote)
                {
                    tgt._isolatedUntil = Time.time + DrawingConfig.BarrierSeconds;
                    GrammarFX.PuffBurst(mote.transform.position, new Color(0.6f, 0.9f, 1f, 0.5f));
                    mote.Die();
                }
                else BounceApart(a, b, 1f); // two motes ignore each other
                return;
            }

            var fa = RuneGrammar.Family(a.Kind);
            var fb = RuneGrammar.Family(b.Kind);

            // SAME + SAME = LEVEL UP. (Flames are manifests, not levels - they
            // pool bigger. Push+Push becomes the tornado in the vectors phase.)
            if (fa == fb)
            {
                // a FLAME is a manifest, not a level - it EATS its kin and grows
                if (a.Kind == ParticleKind.Flame || b.Kind == ParticleKind.Flame)
                {
                    var flame = a.Kind == ParticleKind.Flame ? a : b;
                    flame.Absorb(flame == a ? b : a);
                    return;
                }
                if (fa == ParticleKind.Push)
                {
                    // Arrow+Arrow = tornado, Y+Y = whirlpool; a mixed pair
                    // just pools velocities.
                    ulong awayBit = RuneGrammar.Bit(RuneType.DirectionAway);
                    ulong towardBit = RuneGrammar.Bit(RuneType.DirectionToward);
                    bool bothAway = (a.Lineage & awayBit) != 0 && (b.Lineage & awayBit) != 0
                        && (a.Lineage & towardBit) == 0 && (b.Lineage & towardBit) == 0;
                    bool bothToward = (a.Lineage & towardBit) != 0 && (b.Lineage & towardBit) != 0
                        && (a.Lineage & awayBit) == 0 && (b.Lineage & awayBit) == 0;
                    if (bothAway || bothToward)
                    {
                        Vector3 vat = (a.transform.position + b.transform.position) * 0.5f;
                        var storm = TornadoField.Open(vat, (a.Power + b.Power) * 0.5f,
                            down: bothToward, a.Lineage | b.Lineage,
                            FuseSize(a.SrcSize, b.SrcSize));
                        a.BecameObj = storm; b.BecameObj = storm; // arrows wait out their storm
                        a.Die(); b.Die();
                        return;
                    }
                    a.Absorb(b);
                    return;
                }
                LevelMerge(a, b);
                return;
            }

            // opposites synthesize a paradox object
            var paradox = RuneGrammar.ParadoxOf(a.Kind, b.Kind);
            if (paradox != ParadoxKind.None) { Synthesize(a, b, paradox); return; }

            // EXOTICS: authored cross-pairs override the substrate (Healing…)
            var exotic = RuneGrammar.ExoticOf(a.Kind, b.Kind);
            if (exotic != RuneGrammar.ExoticKind.None)
            {
                a.ImpactFx(); b.ImpactFx(); // the recipe's two halves shown at the birth
                Vector3 xat = (a.transform.position + b.transform.position) * 0.5f;
                ulong xlin = a.Lineage | b.Lineage;
                float xpow = (a.Power + b.Power) * 0.5f;
                RuneGrammar.TryDemon(xlin, xat, FuseSize(a.SrcSize, b.SrcSize));
                Object made = Exotics.Cast(exotic, a, b, xat, xpow);
                a.BecameObj = made; b.BecameObj = made; // sustain: wait on the product
                a.Die(); b.Die();
                return;
            }

            // ---- v2 substrate ----
            int la = Level(a.Kind), lb = Level(b.Kind);
            if (la != lb)
            {
                var hi = la > lb ? a : b;
                var lo = la > lb ? b : a;
                hi.Absorb(lo);
                return;
            }
            if (la == 2) { a.Absorb(b); return; } // property carriers pool payloads (heavy glue…)
            BounceApart(a, b, 1.2f); // rock bounces off ice
        }

        static void BounceApart(SpellParticle a, SpellParticle b, float force)
        {
            Vector3 apart = (a.transform.position - b.transform.position).normalized;
            a.Vel += apart * force; b.Vel -= apart * force;
        }

        /// The leveling engine: lvl1+lvl1 = lvl2 · anything+lvl2 = lvl3 ULTIMATE.
        /// Condensed states count as their family's lvl2 (Lightning IS Light 2).
        static int EffLevel(SpellParticle p) =>
            p.Kind == ParticleKind.Lightning || p.Kind == ParticleKind.BlackHole ? 2 : p.GrammarLevel;

        static void LevelMerge(SpellParticle a, SpellParticle b)
        {
            // both ingredients flash at the meeting
            a.ImpactFx(); b.ImpactFx();
            int la = EffLevel(a), lb = EffLevel(b);
            var hi = la >= lb ? a : b;
            var lo = hi == a ? b : a;

            if (Mathf.Max(la, lb) >= 3) { hi.Absorb(lo); return; } // capped: eats its kin

            // pool payload + ancestry into the survivor
            hi.Lineage |= lo.Lineage;
            hi.Temp += lo.Temp; hi.Lum += lo.Lum; hi.Density += lo.Density; hi.Stick += lo.Stick;
            // mismatched levels: the weaker half rules the product (law 6);
            // equals pool their power instead
            hi.Power = la != lb
                ? Mathf.Min(3f, Mathf.Min(hi.Power, lo.Power) * 1.25f)
                : Mathf.Min(3f, hi.Power + lo.Power * 0.5f);
            hi.SrcSize = FuseSize(hi.SrcSize, lo.SrcSize);

            Vector3 at = (a.transform.position + b.transform.position) * 0.5f;
            int target = Mathf.Min(3, Mathf.Max(la, lb) + 1);
            var family = RuneGrammar.Family(hi.Kind);
            lo.BecameObj = hi; // sustain law: lo's rune waits on the survivor
            lo.Die();
            RuneGrammar.TryDemon(hi.Lineage, at, hi.SrcSize);

            if (target == 2) { hi.BecomeLevel2(); return; }

            // lvl3 for the carriers is a BIGGER carrier (Dense: larger+heavier;
            // Spread: copies come out LARGER); for everyone else it's an AREA
            if (family == ParticleKind.Dense || family == ParticleKind.Spread)
            {
                hi.GrammarLevel = 3;
                hi.transform.localScale = Vector3.one * 0.34f;
                hi.RefreshLook();
                return;
            }
            float power = hi.Power; float srcSize = hi.SrcSize; ulong lineage = hi.Lineage;
            hi.Die();
            hi.BecameObj = Ultimate(family, at, power, srcSize, lineage); // runes wait on the FIELD
        }

        void BecomeLevel2()
        {
            GrammarLevel = 2;
            _settled = false;
            switch (Kind)
            {
                case ParticleKind.Light: BecomeLightning(); return; // Light+Light = LIGHTNING
                case ParticleKind.Dark: BecomeBlackHole(); return;       // Dark+Dark = BLACK HOLE
                case ParticleKind.Glue:
                    _settled = true; // lvl2 grip doesn't move - and won't let go
                    break;
            }
            transform.localScale = Vector3.one * 0.26f;
            // a faint ground ring announces the aura's reach
            var reach = GrammarFX.GroundRing(transform, new Color(1f, 1f, 1f, 0.35f));
            reach.localScale = Vector3.one * (DrawingConfig.Lvl2AuraRadius / 0.26f);
            RefreshLook();
        }

        /// lvl3 ultimates. Returns the created field so the sustain law can
        /// wait on it.
        static Object Ultimate(ParticleKind family, Vector3 at, float power, float srcSize, ulong lineage)
        {
            RuneGrammar.TryDemon(lineage, at, srcSize);
            switch (family)
            {
                case ParticleKind.Spark: return FlameVortexField.Open(at, power, srcSize);
                case ParticleKind.Frost: return SnowField.Open(at, power, srcSize);
                case ParticleKind.Light: return PlasmaField.Open(at, power, srcSize);
                case ParticleKind.Dark: return BlackHoleField.Open(at, power, growing: true, size: srcSize);
                case ParticleKind.Glue: return TimeFreezeField.Open(at, power, srcSize);
                case ParticleKind.Repel: return InertiaField.Open(at, power, srcSize);
            }
            return null;
        }

        /// Opposites make PARADOX objects - the tension embodied, never a cancel.
        static void Synthesize(SpellParticle a, SpellParticle b, ParadoxKind paradox)
        {
            a.ImpactFx(); b.ImpactFx(); // the two OPPOSITES flash their colors as they refuse
            Vector3 at = (a.transform.position + b.transform.position) * 0.5f;
            ulong lineage = a.Lineage | b.Lineage;
            float power = (a.Power + b.Power) * 0.5f;
            float srcSize = FuseSize(a.SrcSize, b.SrcSize);
            bool big = EffLevel(a) >= 2 && EffLevel(b) >= 2; // lvl2 × lvl2 = the AREA version
            RuneGrammar.TryDemon(lineage, at, srcSize);

            switch (paradox)
            {
                case ParadoxKind.Steam:
                {
                    // heat + chill = the one gas substance
                    var steam = FormConjures.SpawnSteam(at + Vector3.up * 0.3f,
                        big ? 1.6f : 0.9f, lineage);
                    DrawingWorld.Instance?.LogEvent("fire and frost make SCALDING STEAM");
                    a.BecameObj = steam; b.BecameObj = steam; // sustain law: runes wait on the cloud
                    a.Die(); b.Die();
                    return;
                }
                case ParadoxKind.WhiteHole:
                {
                    var f = WhiteHoleField.Open(at, power * (big ? 1.6f : 1f), srcSize);
                    a.BecameObj = f; b.BecameObj = f;
                    a.Die(); b.Die();
                    return;
                }
                case ParadoxKind.ChaosGrip:
                {
                    // grip+slip: sticks to everything but can't be controlled
                    var keep = RuneGrammar.Family(a.Kind) == ParticleKind.Glue ? a : b;
                    var eat = keep == a ? b : a;
                    keep.Lineage = lineage;
            keep.SrcSize = srcSize;
                    keep.Stick = Mathf.Abs(keep.Stick) + Mathf.Abs(eat.Stick);
                    keep._chaosLeft = 999f;
                    keep._settled = false;
                    eat.BecameObj = keep;
                    eat.Die();
                    keep.RefreshLook();
                    return;
                }
                case ParadoxKind.Barrier:
                {
                    var mote = Emit(ParticleKind.BarrierMote, at, Vector3.up, power);
                    mote.Lineage = lineage;
                    mote.SrcSize = srcSize;
                    mote.SealId = a.SealId == b.SealId ? a.SealId : 0;
                    a.BecameObj = mote; b.BecameObj = mote;
                    a.Die(); b.Die();
                    return;
                }
            }
        }

        void Absorb(SpellParticle food)
        {
            if (_dead || food._dead) return;
            Temp += food.Temp; Lum += food.Lum; Density += food.Density; Stick += food.Stick;
            if (food.Kind == ParticleKind.Push || food.Kind == Kind) Vel += food.Vel * 0.55f;
            Power = Mathf.Min(3f, Power + food.Power * 0.35f);
            SrcSize = FuseSize(SrcSize, food.SrcSize);
            Lineage |= food.Lineage; // ancestry rides EVERY combination
            food.BecameObj = this;   // the food's rune now waits on ME (sustain law)
            _settled = false; // fresh attributes knock it loose
            transform.localScale = Vector3.one * Mathf.Min(0.45f, transform.localScale.x * 1.18f);
            // spread on either side multiplies
            bool split = food.Density < -0.5f || Density < -0.5f;
            int spreadLevel = RuneGrammar.Family(food.Kind) == ParticleKind.Spread ? EffLevel(food)
                : RuneGrammar.Family(Kind) == ParticleKind.Spread ? GrammarLevel : 1;
            food.Die();
            if (split) TrySplit(spreadLevel);
            RuneGrammar.TryDemon(Lineage, transform.position, SrcSize);
            CheckTransform();
            RefreshLook();
        }

        /// Spread's ladder rides the split: lvl1 halves the copies, lvl2 copies
        /// come out FULL SIZE, lvl3 copies come out LARGER than the original.
        void TrySplit(int spreadLevel)
        {
            if (_dead || _generation >= 2 || All.Count > DrawingConfig.ParticleCap - 6) return;
            float keep = spreadLevel >= 2 ? 1f : 0.5f;
            float twinMul = spreadLevel >= 3 ? 1.25f : keep;
            Temp *= keep; Lum *= keep; Density *= keep; Stick *= keep;
            var twin = Emit(Kind, transform.position + Random.insideUnitSphere * 0.18f,
                Random.onUnitSphere, Power * twinMul, _generation + 1);
            twin.Temp = Temp * twinMul; twin.Lum = Lum * twinMul;
            twin.Density = Density * twinMul; twin.Stick = Stick * twinMul;
            twin.SrcSize = SrcSize;
            twin.Lineage = Lineage;
            twin.SealId = SealId; // a split twin is STILL family (law 11)
            if (spreadLevel >= 3) twin.transform.localScale = transform.localScale * 1.2f;
            twin.RefreshLook();
            _generation++;
        }

        // ------------------------------------- transformations (GRAMMAR v4) --
        /// The Dense payload turns an essence particle into its persistent
        /// physical object.
        void CheckTransform()
        {
            if (Kind == ParticleKind.Spark && Density >= PlasmaDensity) { BecomeFlame(); return; }
            if (Kind == ParticleKind.Frost && Density >= PlasmaDensity) BecomeSnowball();
        }

        /// HeatUp+Dense - FLAME: a persistent fire that stays where it lands,
        /// burns what's near, and merges with other flames into bigger flames.
        void BecomeFlame()
        {
            Kind = ParticleKind.Flame;
            GrammarLevel = Mathf.Max(GrammarLevel, 1);
            Temp = Mathf.Max(Temp, 60f);
            transform.localScale = Vector3.one * 0.3f;
            Vel *= 0.2f;
            DrawingWorld.Instance?.LogEvent("the fire becomes a FLAME");
            RefreshLook();
        }

        /// HeatDown+Dense - SNOWBALL: real matter, cold, rolls, merges via
        /// Matter's own chemistry; passively chills its surroundings (Matter side).
        void BecomeSnowball()
        {
            var m = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Solid,
                0.3f * Mathf.Max(1f, Power), transform.position);
            m.Temperature = -30f;
            if (m.TryGetComponent<Rigidbody>(out var rb))
                rb.linearVelocity = Vel + Vector3.down * 1f;
            DrawingWorld.Instance?.LogEvent("the cold becomes a SNOWBALL");
            m.Lineage = Lineage; // the demon chain survives the snowball
            BecameObj = m;       // the rune waits for the snowball to be gone
            Die();
        }

        /// Dark+Dark - BLACK HOLE: a particle that pulls things in. Feed it
        /// more darkness and it goes lvl3: the GROWING black hole area.
        void BecomeBlackHole()
        {
            Kind = ParticleKind.BlackHole;
            Lum = Mathf.Min(Lum, -1.5f);
            _settled = false;
            transform.localScale = Vector3.one * 0.4f;
            var l = GetComponent<Light>();
            if (l != null) Destroy(l);
            DrawingWorld.Instance?.LogEvent("the dark becomes a BLACK HOLE");
            RefreshLook();
        }

        void TickBlackHole(float dt)
        {
            transform.position += Vel * dt;
            Vel *= 1f - 1.2f * dt;
            int n = Physics.OverlapSphereNonAlloc(transform.position, 3.5f, GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null) continue;
                var pl = c.GetComponent<SimpleFPSController>();
                if (pl != null)
                {
                    pl.AddSpellForce((transform.position - c.transform.position).normalized * 5f * Power, dt);
                    continue;
                }
                var p = c.GetComponent<SpellParticle>();
                if (p != null && p != this) { p.Pull(transform.position, dt); continue; }
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddForce((transform.position - rb.worldCenterOfMass).normalized * 8f, ForceMode.Acceleration);
            }
        }

        /// lvl2 particles radiate their effect on a beat.
        void TickAura(float dt)
        {
            _auraTick -= dt;
            if (_auraTick > 0f) return;
            _auraTick = DrawingConfig.Lvl2AuraPeriod;
            int n = Physics.OverlapSphereNonAlloc(transform.position, DrawingConfig.Lvl2AuraRadius,
                GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null || c.GetComponent<SpellParticle>() != null) continue;
                var pl = c.GetComponentInParent<SimpleFPSController>();
                if (pl != null)
                {
                    // limb capsules ride the same player - the ROOT pays, once
                    if (c.attachedRigidbody != null) continue;
                    // no holder immunity; auras only push sliders - the
                    // board does the rest
                    var plBoard = BodyState.Of(pl);
                    if (Mathf.Abs(Temp) > 25f) plBoard?.PushTemp(Temp * 0.03f);
                    // a friend's LIGHT washes the darkness off you
                    var auraFam = RuneGrammar.Family(Kind);
                    if (auraFam == ParticleKind.Light) plBoard?.PushLum(0.12f);
                    else if (auraFam == ParticleKind.Dark) plBoard?.PushLum(-0.1f);
                    continue;
                }
                if (Kind == ParticleKind.Flame || RuneGrammar.Family(Kind) == ParticleKind.Spark)
                GiveHeat(c, 50f * Power); // lvl2 radiance BURNS
                else if (RuneGrammar.Family(Kind) == ParticleKind.Frost)
                    GiveHeat(c, -45f * Power);
                else if (Kind == ParticleKind.Glue) // lvl2 grip: nothing near it moves
                {
                    var cr = c.GetComponentInParent<Creature>();
                    if (cr != null) cr.ApplyStuck(2f);
                    var rb = c.attachedRigidbody;
                    if (rb != null && !rb.isKinematic)
                        rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, Vector3.zero, 7f);
                }
                else if (Kind == ParticleKind.Repel) // lvl2 slip: nothing near it stands
                {
                    var cr = c.GetComponentInParent<Creature>();
                    if (cr != null) cr.ApplySlip(1.8f);
                }
            }
        }

        void BecomeLightning()
        {
            Kind = ParticleKind.Lightning;
            Lum = Mathf.Max(Lum, 2.5f);
            _settled = false;
            _strikeTick = 0.3f;
            transform.localScale = Vector3.one * 0.22f;
            if (GetComponent<Light>() == null)
            {
                var l = gameObject.AddComponent<Light>();
                l.type = LightType.Point; l.range = 7f; l.intensity = 5f;
                l.color = new Color(0.75f, 0.9f, 1f);
            }
            Juice.Crackle(transform.position);
            DrawingWorld.Instance?.LogEvent("the light becomes LIGHTNING");
            RefreshLook();
        }

        void TickLightning(float dt)
        {
            Vel = Vector3.Lerp(Vel, Random.insideUnitSphere * 2.2f, 0.2f); // erratic crackle-drift
            transform.position += Vel * dt;
            _strikeTick -= dt;
            if (_strikeTick <= 0f) { _strikeTick = 0.75f; Strike(); }
        }

        /// Lightning strikes the highest thing nearby, randomly.
        void Strike()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, 8f, GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            Collider best = null;
            float bestY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null || c.GetComponent<SpellParticle>() != null) continue;
                bool interesting = c.attachedRigidbody != null
                    || c.GetComponentInParent<Damageable>() != null
                    || c.GetComponent<SimpleFPSController>() != null;
                if (!interesting) continue; // strikes THINGS, not the map itself
                // dice roll so it doesn't always pick the tallest thing
                float y = c.bounds.max.y + Random.value * 2.5f;
                if (y > bestY) { bestY = y; best = c; }
            }
            if (best == null) return;

            Vector3 hit = best.bounds.center + Vector3.up * best.bounds.extents.y;
            Bolt(transform.position, hit);
            Juice.Crackle(hit);
            var lib = FxLibrary.I;
            if (lib != null) FxLibrary.Spawn(lib.ElectricHit, hit, null, 3f);

            var pl = best.GetComponent<SimpleFPSController>();
            if (pl != null) { pl.TakeHit(Vector3.down * 4f, 24f); return; }
            var d = best.GetComponentInParent<Damageable>();
            if (d != null) d.TakeDamage(50f * Power, "struck by lightning");
            GiveHeat(best, 150f); // a strike IGNITES what it hits
            var rb = best.attachedRigidbody;
            if (rb != null) rb.AddForce(Vector3.down * 7f, ForceMode.VelocityChange);
        }

        static void Bolt(Vector3 a, Vector3 b)
        {
            var go = new GameObject("Bolt");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.045f;
            lr.positionCount = 4;
            lr.SetPosition(0, a);
            lr.SetPosition(1, Vector3.Lerp(a, b, 0.35f) + Random.insideUnitSphere * 0.5f);
            lr.SetPosition(2, Vector3.Lerp(a, b, 0.7f) + Random.insideUnitSphere * 0.5f);
            lr.SetPosition(3, b);
            lr.sharedMaterial = MatterFX.Get(new Color(0.8f, 0.92f, 1f, 0.95f), MoteShade.Additive);
            Destroy(go, 0.12f);
        }

        // ------------------------------------------------- touching the world --
        void Touch(Collider c)
        {
            // barrier isolation is two-way - bounce off the shell
            if (Barrier.Protects(c))
            {
                Vel = Vector3.Reflect(Vel, (transform.position - c.bounds.center).normalized) * 0.6f;
                _settled = false;
                return;
            }

            // a BarrierMote wraps the first THING it touches, then is spent
            if (Kind == ParticleKind.BarrierMote)
            {
                Transform root = null;
                var bp = c.GetComponentInParent<SimpleFPSController>();
                if (bp != null) root = bp.transform;
                else
                {
                    var bc = c.GetComponentInParent<Creature>();
                    if (bc != null) root = bc.transform;
                    else if (c.attachedRigidbody != null) root = c.attachedRigidbody.transform;
                }
                if (root != null)
                {
                    Barrier.Wrap(root);
                    BecameObj = root.GetComponent<Barrier>(); // runes wait out the shield
                    Die();
                }
                else { _settled = true; Vel = Vector3.zero; } // bare wall: wait for a target
                return;
            }

            // any player collider counts (a foot capsule can reach patches
            // the root never overlaps); TouchPlayer branches self-throttle,
            // so limb+root double-events are safe
            var pilot = c.GetComponentInParent<SimpleFPSController>();
            if (pilot != null) { TouchPlayer(pilot); return; }

            // the DEMON absorbs anything that touches it - it BECOMES the last
            // element it ate, so even a fireball is food, not a hit
            var demon = c.GetComponentInParent<Demon>();
            if (demon != null) { demon.AbsorbParticle(this); BecameObj = demon; Die(); return; }

            var m = c.GetComponent<Matter>();
            var creature = c.GetComponentInParent<Creature>();
            var rb = c.attachedRigidbody;

            // bare world geometry (floor/walls): SETTLE instead of dying - a
            // spark on the ground is a waiting ember, a light is a torch, a
            // dark spot is a trap. A push particle can knock them loose again.
            if (m == null && creature == null && rb == null)
            {
                // a push bounces off bare rock instead of dying
                if (Kind == ParticleKind.Push)
                {
                    Vel = Vector3.Reflect(Vel, Vector3.up) * 0.7f;
                    return;
                }
                // lvl2 slip CANNOT be stopped by anything - it bounces
                if (GrammarLevel >= 2 && Kind == ParticleKind.Repel)
                {
                    Vel = Vector3.Reflect(Vel, Vector3.up) + Random.insideUnitSphere * 1.5f;
                    return;
                }
                if (Claimed && Vel.sqrMagnitude > 9f && FxLibrary.I != null) // a thrown thing LANDS
                    FxLibrary.Spawn(FxLibrary.I.GroundHit, transform.position);
                // a landed mote widens its trigger to boot size so steps register
                {
                    var sc = GetComponent<SphereCollider>();
                    if (sc != null)
                        sc.radius = Mathf.Max(sc.radius, 0.3f / Mathf.Max(0.01f, transform.lossyScale.x));
                }
                _settled = true;
                Vel = Vector3.zero;
                return;
            }

            // PERSISTENT particles expire by lifetime, not by first contact
            if (Persistent)
            {
                _donateTick -= Time.deltaTime;
                if (_donateTick > 0f) return;
                _donateTick = 0.5f;
                Donate(c, m, creature, rb);
                // no per-beat ImpactFx - it was most of the chain-lag
                return;
            }

            Donate(c, m, creature, rb);
            ImpactFx(); // the payload LANDS visibly

            // ECHO powerup: the payload delivered, the particle sometimes
            // ricochets back to life at half power (mayhem compounding)
            if (Echo > 0 && _generation < 2 && Random.value < 0.22f * Echo)
            {
                var e = Emit(Kind, transform.position + Vector3.up * 0.15f,
                    (Random.onUnitSphere + Vector3.up).normalized, Power * 0.6f, _generation + 1);
                e.SrcSize = SrcSize;
                e.Lineage = Lineage; // the echo remembers its ancestry
                e.SealId = SealId;   // and its family
            }
            Die();
        }

        void TouchPlayer(SimpleFPSController pilot)
        {
            // a preview touches nobody; a live particle bites everyone,
            // holder included
            if (Dormant) return;
            if (Barrier.Protects(pilot)) // barriered player: nothing gets in
            {
                Vel = Vector3.Reflect(Vel, (transform.position - pilot.transform.position).normalized) * 0.6f;
                return;
            }
            var board = BodyState.Of(pilot); // the slider board takes it from here
            if (Kind == ParticleKind.Flame)
            {
                _donateTick -= Time.deltaTime;
                if (_donateTick > 0f) return;
                _donateTick = 0.7f;
                board?.PushTemp(Mathf.Max(6f, Temp * 0.05f)); // fire only HEATS - the band does the hurting
                return;
            }
            // lvl2 grip: you're stuck where you stand; the glue never dies
            // to a touch
            if (Kind == ParticleKind.Glue && GrammarLevel >= 2)
            {
                // slows into a deep shuffle - never a full stop
                bool was = board != null && board.Grip > BodyState.GripSlowAt;
                board?.PushGrip(0.9f);
                if (!was && FxLibrary.I != null) // comic beat - first stick only
                    FxLibrary.Spawn(FxLibrary.I.TextBoing, pilot.transform.position + Vector3.up * 2f);
                return;
            }
            // ground patches: a settled sticky mote is a glue spot, a slick
            // one a soap spot; steps act and the patch survives
            if (_settled && (Kind == ParticleKind.Glue || Kind == ParticleKind.Repel))
            {
                // own timer so object donations don't starve the player effect
                _patchTick -= Time.deltaTime;
                if (_patchTick > 0f) return;
                _patchTick = 0.55f;
                if (Kind == ParticleKind.Glue)
                {
                    board?.PushGrip(0.5f); // glue spot: grip climbs toward planted
                }
                else
                {
                    // soap spot: grip drains toward skating
                    board?.PushGrip(-0.55f);
                    Vector3 v = pilot.Velocity; v.y = 0f;
                    if (v.sqrMagnitude > 0.2f) pilot.TakeHit(v.normalized * 3.2f, 0f); // momentum keeps you
                }
                return;
            }
            // vector law: amplify-or-invert current velocity plus the mote's
            // own add along its travel; arrow amplifies, Y inverts
            if (Kind == ParticleKind.Push)
            {
                Vector3 travel = Vel.sqrMagnitude > 0.01f ? Vel.normalized : transform.forward;
                Vector3 vNow = pilot.Velocity;
                // the arrow's own kick is a launch; the amplify term stacks
                // on top when moving
                Vector3 impulse = (IsY ? -vNow * 1.6f : vNow * 0.6f) + travel * (15f * Power);
                pilot.TakeHit(impulse, 0f);
                board?.PushMove((IsY ? -0.8f : 0.8f) * Power); // and the slider remembers
                ImpactFx();
                Die();
                return;
            }
            // friendly fire stays on
            if (Kind == ParticleKind.Dark)
            {
                board?.PushLum(-0.45f); // darkness steals sight
                board?.PushTemp(-4f); // and chills
                Die(); return;
            }
            if (Kind == ParticleKind.Light)
            {
                board?.PushLum(0.5f); // see better - or glare, if someone overdoes it
                board?.PushTemp(3f);  // light warms
                Die(); return;
            }
            if (Kind == ParticleKind.Dense) { board?.PushWeight(0.45f * Power); Die(); return; }
            if (Kind == ParticleKind.Spread) { board?.PushWeight(-0.45f * Power); Die(); return; }
            if (Mathf.Abs(Temp) > 12f) board?.PushTemp(Temp * 0.3f); // a mote is FELT
            if (Mathf.Abs(Stick) > 0.4f) board?.PushGrip(Stick * 0.9f); // flying glue grips, flying soap strips
            ImpactFx();
            Die();
        }

        /// Level 5 - the world absorbs everything: the payload becomes real
        /// temperature, light, blindness, weight, glue, and shove.
        void Donate(Collider c, Matter m, Creature creature, Rigidbody rb)
        {
            // GRAMMAR v4: matter is part of the chain - a particle donating
            // into a block hands over its ANCESTRY too (matter can complete
            // the all-12 Demon lineage just like particles can)
            if (m != null)
            {
                m.Lineage |= Lineage;
                RuneGrammar.TryDemon(m.Lineage, m.transform.position, SrcSize);
            }

            if (Mathf.Abs(Temp) > 0.5f)
            {
                if (m != null) m.AddHeat(Temp * 2f);
                else GiveHeat(c, Temp * (creature != null ? 1.5f : 1f)); // flesh catches fast
                if (Temp < -20f && FxLibrary.I != null) // frost bites visibly
                    FxLibrary.Spawn(FxLibrary.I.IceHit, transform.position);
            }

            if (Lum > 0.4f) AttachLantern(c);
            else if (Lum < -0.4f && creature != null)
                creature.ApplyBlind(2.5f + -Lum);

            if (Stick > 0.4f)
            {
                if (creature != null) creature.ApplyStuck(1.4f * Stick);
                if (m != null) m.AddStickiness(0.35f * Stick);
                if (rb != null) rb.linearDamping = Mathf.Max(rb.linearDamping, 6f * Stick);
                TryWeld(c); // two recently-glued things get joined
                // lvl2 grip welds touchers to the world where they stand;
                // creatures use their own stuck system
                if (GrammarLevel >= 2 && Kind == ParticleKind.Glue && creature == null
                    && rb != null && !rb.isKinematic && rb.GetComponent<FixedJoint>() == null)
                {
                    var world = rb.gameObject.AddComponent<FixedJoint>(); // no connectedBody = the world itself
                    world.breakForce = StickyBonds.BreakForce(StickyBonds.Sticky2);
                    world.breakTorque = world.breakForce;
                }
            }
            else if (Stick < -0.4f)
            {
                if (creature != null) creature.ApplySlip(1.2f * -Stick);
                if (m != null) m.AddStickiness(0.35f * Stick);
                if (rb != null)
                {
                    rb.linearDamping = 0f;
                    rb.AddForce((c.transform.position - transform.position).normalized * 5f * -Stick,
                        ForceMode.VelocityChange);
                }
            }

            if (Mathf.Abs(Density) > 0.4f)
            {
                // Compress makes things HEAVIER, Spread makes them LIGHTER -
                // creatures included. Size is left alone on purpose: thinning
                // a body without shrinking it is what drops its density until
                // it cannot hold together and splits (DensitySplit). That is
                // why Spread multiplies things - no duplication rule needed.
                if (m != null) m.AddDensity(0.6f * Density);
                else if (rb != null) rb.mass = Mathf.Max(0.05f, rb.mass * (1f + 0.28f * Density));
            }

            if (Kind == ParticleKind.Push && rb != null)
            {
                // vector law for objects: amplify-or-invert velocity plus
                // the mote's own add along its travel
                Vector3 travel = Vel.sqrMagnitude > 0.01f ? Vel.normalized : transform.forward;
                Vector3 impulse = (IsY ? -rb.linearVelocity * 1.6f : rb.linearVelocity * 0.6f)
                + travel * (18f * Power);
                rb.AddForce(impulse, ForceMode.VelocityChange);
                if (creature != null)
                    BodyState.Of(creature)?.PushMove((IsY ? -0.8f : 0.8f) * Power);
            }
        }

        /// Changes a creature's SIZE. Density no longer routes here -
        /// Compress and Spread change WEIGHT, and thinning a body is what
        /// splits it.
        static void Resize(Creature creature, float factor)
        {
            float s = Mathf.Clamp(creature.transform.localScale.x * factor, 0.5f, 1.7f);
            creature.transform.localScale = Vector3.one * s;
            var rb = creature.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.mass = 70f * s * s;
                if (s <= 0.6f) rb.AddForce(Vector3.up * 4f, ForceMode.VelocityChange); // balloon zombie
            }
        }

        /// Public doorway for the grammar's area fields (GrammarAreas.cs).
        public static void GiveHeatTo(Collider c, float delta) => GiveHeat(c, delta);

        static void GiveHeat(Collider c, float delta)
        {
            var m = c.GetComponent<Matter>();
            if (m != null) { m.AddHeat(delta); return; }
            var go = c.attachedRigidbody ? c.attachedRigidbody.gameObject : c.gameObject;

            // a player's temperature lives on the body board only - never
            // add a Thermal to the player
            var pilot = c.GetComponentInParent<SimpleFPSController>();
            if (pilot != null)
            {
                BodyState.Of(pilot)?.PushTemp(delta * 0.12f); // same scale as mote touches
                return;
            }
            var creature = c.GetComponentInParent<Creature>();
            if (creature != null) go = creature.gameObject;

            // don't cook giant static surfaces
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null && c.attachedRigidbody == null
                && rend.bounds.size.magnitude > DrawingConfig.MaxThermalObjectSize) return;
            var t = go.GetComponent<Thermal>();
            if (t == null)
            {
                t = go.AddComponent<Thermal>();
                t.HeatCapacity = SurfaceMaterialDB.Info(SurfaceMaterialDB.Resolve(go)).HeatCapacity;
                if (go.GetComponent<Rigidbody>() != null && go.GetComponent<Damageable>() == null)
                    go.AddComponent<Damageable>();
            }
            t.AddHeat(delta);
        }

        void AttachLantern(Collider c)
        {
            var go = new GameObject("Lantern");
            go.transform.SetParent(c.transform, true);
            go.transform.position = transform.position;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 5f;
            l.intensity = 2.6f * Mathf.Min(2f, Lum);
            l.color = new Color(1f, 0.95f, 0.75f);
            Destroy(go, 7f);
        }

        // glue memory: the last two glued bodies get JOINED if they meet soon
        static Rigidbody _lastGlued;
        static float _gluedAt;
        void TryWeld(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null) return;
            if (_lastGlued != null && _lastGlued != rb && Time.time - _gluedAt < 4f
                && (rb.worldCenterOfMass - _lastGlued.worldCenterOfMass).sqrMagnitude < 9f)
            {
                var joint = rb.gameObject.AddComponent<FixedJoint>();
                joint.connectedBody = _lastGlued;
                // the ladder: lvl1 tears under strain, lvl2 holds hard -
                // there is no lvl3 glue PARTICLE (lvl3 is the time zone)
                joint.breakForce = StickyBonds.BreakForce(
                    GrammarLevel >= 2 ? StickyBonds.Sticky2 : StickyBonds.Sticky1);
                joint.breakTorque = joint.breakForce;
                DrawingWorld.Instance?.LogEvent("GLUED TOGETHER.");
            }
            _lastGlued = rb;
            _gluedAt = Time.time;
        }

        /// Fields (black hole, tornado…) drag settled particles back into the air.
        public void Pull(Vector3 to, float dt)
        {
            if (_dead) return;
            _settled = false;
            Vel += (to - transform.position).normalized * 10f * dt;
        }

        // -------------------------------------------------------------- look --
        GameObject _customLook;
        ParticleKind _customFor;

        /// A PrefabVault prefab named FX_<Kind> becomes that particle's look;
        /// the code sphere hides and the prefab rides the particle.
        void EnsureCustomLook()
        {
            if (_customLook != null && _customFor == Kind) return;
            if (_customLook != null) Destroy(_customLook);
            _customLook = null;
            // authored shape first (CollectionManager, by name - fusions too),
            // then the legacy FX_ hook, then nothing: the code sphere shows.
            var skin = CollectionManager.ParticleShapeFor(Kind.ToString())
                ?? PrefabVault.Get("FX_" + Kind);
            if (skin == null)
            {
                if (_rend != null) _rend.enabled = true;
                return;
            }
            _customFor = Kind;
            _customLook = Instantiate(skin, transform);
            _customLook.transform.localPosition = Vector3.zero;
            if (_rend != null) _rend.enabled = false;
        }

        /// Looping idle FX for burning kinds; dies with the mote (child),
        /// swaps on kind change.
        void AttachIdleFx()
        {
            var old = transform.Find("IdleFx");
            if (old != null) Destroy(old.gameObject);
            // an FX_<Kind> prefab owns the look; no CFXR effect on top.
            // Destroy runs first so a stale IdleFx can't survive a kind change.
            if (_customLook != null) return;
            var lib = FxLibrary.I;
            if (lib == null) return;
            GameObject pick = null;
            float scale = 3.2f; // motes are ~0.14 scale - children inherit it
            var fam = RuneGrammar.Family(Kind);
            // only actively burning kinds wear a looping effect
            if (Kind == ParticleKind.Flame || (fam == ParticleKind.Spark && GrammarLevel >= 2))
                pick = lib.Fire;
            else if (Kind == ParticleKind.Lightning) pick = lib.ElectricHit;
            if (pick == null) return;
            var fx = Instantiate(pick, transform.position, Quaternion.identity, transform);
            fx.name = "IdleFx";
            fx.transform.localScale *= scale; // *= keeps an authored prefab scale
        }

        /// The kind's identity colour - ONE switch, shared by the live look
        /// and the dormant hologram (GhostLook), so they can never drift.
        void KindLook(out Color c, out MoteShade shade) => KindColor(Kind, out c, out shade);

        static void KindColor(ParticleKind Kind, out Color c, out MoteShade shade)
        {
            shade = MoteShade.Additive;
            switch (Kind)
            {
                case ParticleKind.Spark: c = new Color(1f, 0.55f, 0.12f); break;
                case ParticleKind.Frost: c = new Color(0.6f, 0.85f, 1f); break;
                case ParticleKind.Light: c = new Color(1f, 0.97f, 0.8f); break;
                case ParticleKind.Dark: c = new Color(0.2f, 0.1f, 0.3f); shade = MoteShade.Transparent; break;
                case ParticleKind.Glue: c = new Color(0.4f, 0.8f, 0.35f); break;
                case ParticleKind.Repel: c = new Color(0.85f, 0.85f, 0.9f); break;
                case ParticleKind.Dense: c = new Color(0.75f, 0.55f, 0.3f); break;
                case ParticleKind.Spread: c = new Color(0.7f, 1f, 0.8f); break;
                case ParticleKind.Push: c = new Color(1f, 0.95f, 0.4f); break;
                case ParticleKind.Lightning: c = new Color(0.75f, 0.9f, 1f); break;
                case ParticleKind.Flame: c = new Color(1f, 0.45f, 0.08f); break;
                case ParticleKind.BlackHole: c = new Color(0.03f, 0.01f, 0.06f); shade = MoteShade.Transparent; break;
                case ParticleKind.BarrierMote: c = new Color(0.6f, 0.9f, 1f, 0.7f); shade = MoteShade.Transparent; break;
                default: c = Color.white; break;
            }
        }

        void RefreshLook()
        {
            if (_rend == null) _rend = GetComponent<Renderer>();
            if (_rend == null) return;
            EnsureCustomLook();   // resolve the art FIRST - AttachIdleFx checks it
            AttachIdleFx();
            if (_customLook != null) return; // your art owns the look now
            KindLook(out Color c, out MoteShade shade);
            // carried darkness dims any particle
            if (Lum < -0.2f && Kind != ParticleKind.Dark)
            {
                c.a = Mathf.Clamp01(0.9f + Lum * 0.55f);
                shade = MoteShade.Transparent;
            }

            // alive, not flat: jelly wobble + rim glow (SZParticle shader)
            float wobble = Kind == ParticleKind.Glue ? 0.06f : 0.04f;
            float rim = shade == MoteShade.Additive ? 0.9f : 0.35f;
            _rend.sharedMaterial = MatterFX.Particle(c, shade, wobble, rim);
        }
    }

    /// Shared nearest-prey scan. bestSqr rides by ref so a caller can keep
    /// competing against its own extra candidates.
    public static class Targets
    {
        public static Transform Nearest(Vector3 pos, ref float bestSqr,
            bool includePlayers, bool movingOnly = false)
        {
            Transform prey = null;
            foreach (var z in Zombie.All)
            {
                if (z == null) continue;
                if (movingOnly)
                {
                    var rb = z.GetComponent<Rigidbody>();
                    if (rb != null && rb.linearVelocity.sqrMagnitude < 0.4f) continue;
                }
                float d = (z.transform.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; prey = z.transform; }
            }
            if (includePlayers)
                foreach (var p in SimpleFPSController.All)
                {
                    if (p == null) continue;
                    if (movingOnly && p.Velocity.sqrMagnitude < 0.4f) continue;
                    float d = (p.transform.position - pos).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; prey = p.transform; }
                }
            return prey;
        }
    }

    /// The demon's spawn hates everything equally: it keeps a fresh grudge
    /// against the nearest zombie while hunting players like any other.
    public class ShadowFeral : MonoBehaviour
    {
        float _timer;

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 2.5f;
            var brain = GetComponent<ZombieBrain>();
            if (brain == null) return;
            Zombie nearest = null;
            float best = 81f;
            foreach (var z in Zombie.All)
            {
                if (z == null || z.gameObject == gameObject) continue;
                float d = (z.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = z; }
            }
            if (nearest != null)
                brain.Remember(MemKind.MadAt, MemEvent.Grudge, nearest.transform.position, nearest.transform);
        }
    }
}
