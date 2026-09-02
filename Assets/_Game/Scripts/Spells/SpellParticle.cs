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
        BarrierMote,                  // GRAMMAR v4: Dense+Spread paradox - isolates what it touches
        Solid, Liquid                 // the state runes' own body kinds - particles like every rune
    }

    /// A particle is its numbers. It drifts toward whatever the place around
    /// it is natural for, it takes in whatever it meets, and what it IS at any
    /// moment is read off those numbers - never looked up and dispatched.
    /// Particles are the only things that COMBINE; everything else absorbs.
    public class SpellParticle : MonoBehaviour, ISpellData
    {
        /// Read off the numbers. Nothing assigns this any more.
        public ParticleKind Kind => KindOf(PayloadNow);

        /// Which pool this object came out of, fixed at Emit. The LOOK moves
        /// with the numbers; the recycled GameObject must not, or a particle
        /// that drifted would be handed back to a stack whose art it never had.
        ParticleKind _poolKind;

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

        public float SrcSize = 1f; // the rune's own drawn size; rides the fusion chain

        /// ★ REACH = the rune's size RELATIVE TO ITS SEAL (his rule, same as
        /// the zombie gas): a rune filling its seal reaches far, a small rune
        /// in a big seal barely past itself. 0 = no seal lineage (fall back
        /// to drawn size).
        public float Reach;


        /// The effect-radius factor every aura and area shares: ratio-driven
        /// when the seal said so, drawn-size otherwise.
        float ReachK => Reach > 0.01f
            ? Mathf.Clamp(Reach / DrawingConfig.RuneSizeMin, 0.6f, 3f)
            : Mathf.Clamp(SizeMul(SrcSize), 0.6f, 3f);

        /// ★ THE ONE BODY-SIZE CURVE (his rule: reuse, one method): the
        /// zombie summon's Spell.RuneSizeMul, normalized so the smallest
        /// legal rune is exactly 1. Clamped into the existing fused cap so
        /// summed merges grow visibly without breaking blast reach.
        public static float DrawnSizeK(float srcSize)
        {
            float d = 2f / DrawingConfig.ZoneRadiusScale; // SrcSize -> drawn diameter
            return Mathf.Clamp(
                Spell.RuneSizeMul(srcSize * d)
                / Spell.RuneSizeMul(DrawingConfig.ParticleSizeNeutral * d),
                0.3f, DrawingConfig.FusedSizeMulMax);
        }

        /// Body-size changes go through here: visuals scale by the ratio,
        /// the trigger compensates so the impact judge never inflates.
        public void ApplySizeRatio(float ratio)
        {
            if (ratio <= 0f || Mathf.Approximately(ratio, 1f)) return;
            transform.localScale *= ratio;
            var sc = GetComponent<SphereCollider>();
            if (sc != null) sc.radius /= ratio;
        }

        /// A merge grew SrcSize (already summed by the caller) - grow the
        /// body by the curve's own ratio. Relative, so it respects dormant
        /// shrink, decay, and whatever base a transform gave the body.
        void GrowToSize(float oldSrcSize) =>
            ApplySizeRatio(DrawnSizeK(SrcSize) / DrawnSizeK(oldSrcSize));

        /// ★ THE STATE DETONATION IS REAL MATTER (his rule: detonations per
        /// state and axis). A solid spending itself on impact bursts as a
        /// stone that fragments and throws debris; a liquid as water that
        /// splashes. The matter keeps the caster's team and the spell's
        /// momentum, so throws still FEEL like throws.
        void ManifestState(Vector3 at, float sizeK = 1f, bool hot = false)
        {
            float st = SpellPayload.ToHuman(4, PayloadNow.State);
            if (Mathf.Abs(st) < 12f) return;
            bool solid = st > 0f;
            var m = Matter.Spawn(
                solid ? SurfaceMaterialType.Stone : SurfaceMaterialType.Water,
                solid ? MatterPhase.Solid : MatterPhase.Liquid,
                Mathf.Clamp(SrcSize * 0.5f * sizeK, 0.12f, 0.9f), at + Vector3.up * 0.15f);
            if (m == null) return;
            m.StampOwner(OwnerId);
            m.Lineage = Lineage;
            // a meteor-born stone lands BURNING - its own heat ignites what
            // the debris touches, through the ordinary burn law
            if (hot) m.Temperature = Mathf.Max(150f, PayloadNow.Temp * 6f);
            // ★ SPELL SOLIDS ARE SPELLS (his rule): they expire in seconds
            // and EXPLODE on a hard impact, throwing chunks - the same rubble
            // law strike stones always used. Never a forever-prop.
            var sd = m.gameObject.AddComponent<SpellDebris>();
            sd.Init(solid ? SurfaceMaterialType.Stone : SurfaceMaterialType.Water,
                solid ? MatterPhase.Solid : MatterPhase.Liquid,
                Mathf.Clamp(SrcSize * 0.5f * sizeK, 0.12f, 0.9f));
            sd.OwnerId = OwnerId;
            if (m.TryGetComponent<Rigidbody>(out var mrb)) mrb.linearVelocity = Vel * 0.6f;

            // ★ EVERYTHING NEEDS JUICE (his words): a state being born is an
            // EVENT - a bang and a couple of chunks thrown, never a quiet prop
            Juice.Boom(at, 0.5f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 d = (Random.onUnitSphere + Vector3.up).normalized;
                var chip = Matter.Spawn(
                    solid ? SurfaceMaterialType.Stone : SurfaceMaterialType.Water,
                    solid ? MatterPhase.Solid : MatterPhase.Liquid,
                    Mathf.Clamp(SrcSize * 0.22f * sizeK, 0.08f, 0.3f), at + d * 0.3f);
                if (chip == null) continue;
                chip.StampOwner(OwnerId);
                var cd = chip.gameObject.AddComponent<SpellDebris>();
                cd.Init(solid ? SurfaceMaterialType.Stone : SurfaceMaterialType.Water,
                    solid ? MatterPhase.Solid : MatterPhase.Liquid, 0.15f);
                cd.OwnerId = OwnerId;
                if (chip.TryGetComponent<Rigidbody>(out var crb))
                    crb.linearVelocity = d * 4.5f + Vel * 0.4f;
            }
        }

        // ★ ALL SPELLS CREATE DEBRIS, and debris IS the spell (his rule):
        // smaller versions of the same thing - real motes carrying a scaled
        // copy of the payload, wearing the same identity, one generation
        // down so debris never throws debris.
        void ThrowDebris(Vector3 at, int count)
        {
            if (_generation > 0) return;
            for (int i = 0; i < count; i++)
            {
                float ang = (i + Random.value * 0.5f) * (360f / count) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(ang), 0.9f, Mathf.Sin(ang)).normalized;
                var bit = Emit(Kind, at + d * 0.3f, d, Power * 0.5f, _generation + 1);
                if (bit == null) continue;
                var dd = new SpellPayload();
                for (int ax = 0; ax < SpellPayload.AxisCount; ax++)
                    dd[ax] = PayloadNow[ax] * 0.35f;
                bit.Data = dd;
                bit.OwnerId = OwnerId;
                bit.Lineage = Lineage;
                bit.SrcSize = Mathf.Clamp(SrcSize * 0.4f, 0.1f, 0.5f);
                bit.Reach = Reach * 0.45f; // small debris, small effect areas (his rule)
                bit.ApplySizeRatio(DrawnSizeK(bit.SrcSize));
                bit.Vel = d * 9f + Vel * 0.5f;
                bit._ballistic = true; // heavy: gravity owns it, so it ALWAYS
                                       // lands on something - nothing hangs
                                       // or evaporates in air (his rule)
                bit.RefreshIdentity_Public(); // dressed as the small spell it is
                bit.PrimeToBlow(); // debris HITS: first contact delivers and detonates
            }
        }

        // ★ HIS LAW (Aug 27): runes BLOW UP on F, and a thrown rune blows up
        // on impact - no more passive releases that do nothing interesting.
        bool _primed;
        bool _ballistic; // debris flies on gravity and lands - never hovers
        Transform _thrownBy;   // brief self-immunity: in third person the rune
        float _thrownAt;       // exits THROUGH the thrower's own body
        readonly System.Collections.Generic.HashSet<Object> _kickedBodies =
            new System.Collections.Generic.HashSet<Object>();
        public void PrimeToBlow(Transform thrower = null)
        {
            _primed = true;
            _thrownBy = thrower;
            _thrownAt = Time.time;
        }

        public void DetonateNow()
        {
            if (_dead) return;
            if (Dormant) Wake();       // areas flush on wake - a meteor still falls
            if (_dead) return;         // the wake verb may already have spent it
            if (_areasDeferred) FlushAreas(); // a thrown spell's areas raise HERE, at the impact
            ImpactFx();

            // ★ THE BLAST IS THE PAYLOAD LANDING: every body in reach takes
            // the direct hit - the lvl2 delivery, reused. Friendly fire is on
            // by his rule, and that includes the hand that lit it.
            float r = AuraRadius;
            int n = Physics.OverlapSphereNonAlloc(transform.position, r,
                GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            _hitOnce.Clear();
            for (int i = 0; i < n; i++)
            {
                var h = GrammarFX.ScanBuffer[i];
                if (h == null || h.GetComponent<SpellParticle>() != null) continue;
                Object body = h.GetComponentInParent<Element>();
                if (body == null) body = h.attachedRigidbody;
                if (body == null) body = h;
                if (!_hitOnce.Add(body)) continue;
                Detonate(h);
            }

            ManifestState(transform.position);
            ThrowDebris(transform.position, 4); // every spell dies throwing chunks
            // ★ NOTHING FLOATS (his rule): the lingering payload looks for
            // the nearest surface and CLINGS there - the floor below, or the
            // closest solid thing in reach. No sky domes, ever; if there is
            // truly nothing near, there is no linger.
            Vector3 lingerAt = transform.position;
            bool cling = false;
            if (Physics.Raycast(lingerAt + Vector3.up * 0.1f, Vector3.down, out var lh, 7f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            { lingerAt = lh.point; cling = true; }
            else
            {
                float best = 8f * 8f;
                foreach (var h in Physics.OverlapSphere(lingerAt, 8f, ~0,
                    QueryTriggerInteraction.Ignore))
                {
                    if (h.GetComponent<SpellParticle>() != null) continue;
                    Vector3 p = h.ClosestPoint(lingerAt);
                    float d = (p - lingerAt).sqrMagnitude;
                    if (d < best) { best = d; lingerAt = p; cling = true; }
                }
            }
            if (cling)
                ArtificialBiome.Open(lingerAt, GiveNow(Data), r,
                    1f, DrawingConfig.LingerSeconds); // full strength - the payload IS the knob
            Juice.Boom(transform.position, 0.7f);
            // an acolyte's spell spending itself returns a bit of wand
            PlayerInk.CreditWand(OwnerId, DrawingConfig.InkMax * 0.05f);
            Die();
        }

        /// A transformation SETS its own body size - the trigger returns to
        /// the primitive base so the impact judge stays honest whatever the
        /// drawn-size compensation was before the change.
        void SetBodyScale(float scale)
        {
            transform.localScale = Vector3.one * scale;
            var baseSc = GetComponent<SphereCollider>();
            if (baseSc != null) baseSc.radius = 0.5f;
        }
        public int Echo;           // ECHO powerup stacks: landing may re-emit

        // GRAMMAR v4 (SPELL_PARTICLES.md): same+same levels up, opposites
        // synthesize; all 12 runes in one lineage summons the Demon.
        /// Kept as the runtime's own notion for the paths that still set it;
        /// Level is the authored truth and wins wherever both are asked.
        public int GrammarLevel = 1;
        public ulong Lineage;         // union of every rune that fed this chain
        public int SealId;            // which SEAL emitted this; same-seal siblings combine first

        // ★ A SEAL IS ONE UTTERANCE: how many of one drawing's motes still
        // live. While mates remain, no verdict is final - no stone verb, no
        // biome, no areas - the ingredients must pool first. The meteor died
        // here for days: its Solid mote turned to stone on wake before the
        // heats and light could join it.
        static readonly System.Collections.Generic.Dictionary<int, int> _sealAlive =
            new System.Collections.Generic.Dictionary<int, int>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearSealRegistry() => _sealAlive.Clear();
        public void JoinSeal(int id)
        {
            LeaveSeal();
            if (id == 0) return;
            SealId = id;
            _sealAlive.TryGetValue(id, out int n);
            _sealAlive[id] = n + 1;
        }
        void LeaveSeal()
        {
            if (SealId == 0) return;
            if (_sealAlive.TryGetValue(SealId, out int n))
            {
                if (n <= 1) _sealAlive.Remove(SealId);
                else _sealAlive[SealId] = n - 1;
            }
            SealId = 0;
        }
        static int MatesLeft(int sealId) =>
            sealId != 0 && _sealAlive.TryGetValue(sealId, out int n) ? n : 0;
        bool PoolingDone => MatesLeft(SealId) <= 1;

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
        SpellPayload _pendingData; // the partner's POOLED axes - losing them
                                   // meant a figured-out recipe came up short
        bool _hasPending;

        Vector3 _anchorPos, _anchorNrm;
        bool _hasAnchor;
        bool _areasDeferred;   // fused asleep: the areas wait for Wake

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
            a._pendingData = b.Data;
            a._hasPending = true;
            float aWasSrc = a.SrcSize;
            a.SrcSize = FuseSize(a.SrcSize, b.SrcSize);
            a.GrowToSize(aWasSrc); // same curve as every other merge
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
            // scale is already state-correct: merges grow by the curve
            // ratio (GrowToSize) - re-applying the preview shrink here
            // halved the ghost on every merge
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

        readonly System.Collections.Generic.List<SpellDef> _areasOwed =
            new System.Collections.Generic.List<SpellDef>();
        // ★ THE COUNT CAN LIE (zones re-emit, a held spare never joins), so
        // the pool also ends by SILENCE: no new merge for this long = the
        // utterance is over and everything owed raises.
        float _mergeQuietAt;
        bool _biomeOwed;
        bool PoolSettled => PoolingDone || Time.time >= _mergeQuietAt;

        void FlushAreas()
        {
            _areasDeferred = false;
            for (int i = 0; i < _areasOwed.Count; i++)
            {
                var a = SpellBook.Live.Aoe(_areasOwed[i].Aoe);
                if (a != null) StartCoroutine(RaiseArea(a, _areasOwed[i]));
                else Debug.LogWarning($"[SpellyZombie] {_areasOwed[i].Name} owes area "
                    + $"'{_areasOwed[i].Aoe}' but the book has no such area.");
            }
            if (_areasOwed.Count == 0)
                for (int i = 0; i < Fusions.Count; i++)
                    if (Fusions[i].HasAoe)
                        Debug.LogWarning($"[SpellyZombie] {Fusions[i].Name} wears area "
                            + $"'{Fusions[i].Aoe}' but none was banked before the flush.");
            _areasOwed.Clear();
        }

        public void Wake()
        {
            if (!Dormant || _dead) return;
            // ★ HOLDING IS STASIS (his rule): a spell in someone's hand never
            // wakes - not by contact, not by anything. The hand releasing it
            // is the only door out, so it blows up as MADE, never as "what it
            // drifted into while you carried it".
            if (Holder != null) return;
            Dormant = false;
            _wakeAt = -1f;
            transform.localScale /= DrawingConfig.DormantPreviewScale;
            _age = 0f; // the clock was FROZEN - a stockpiled spell wakes fresh
            // the ghost turns fully real at once
            if (_shapeBody != null) _shapeBody.GetComponent<StateView>()?.ClearFade();
            RefreshLook();
            ImpactFx(); // the pop of becoming real
            // a stockpiled spell owes its areas NOW - waking IS the cast.
            // EXCEPT a thrown (primed) one: its areas belong to the IMPACT,
            // so the meteor falls on the victim, not on the thrower's hand.
            if (_areasDeferred && !_primed) FlushAreas();

            // ★ THE STONE STANDS UP ON ACTIVATION (his rule): a PURE bare
            // Solid wakes as the real rock, a pure Liquid as water - kept
            // AFTER the dormant phase so runes still combine. Only when the
            // state is the spell's WHOLE identity: a Meteor also matches the
            // Solid region, and the stone must never steal the meteor.
            // The METEOR itself needs no verb - its authored AREA falls from
            // the sky (the offset), trails, slams, and burns.
            if (Fusions.Count == 1 && PoolingDone
                && (Fusions[0].Name == "Solid" || Fusions[0].Name == "Liquid"))
            {
                ManifestState(transform.position, 2f);
                Die();
                return;
            }

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
                    mate.Data = _pendingData;   // the carried half returns WHOLE
                    mate.JoinSeal(SealId);      // still family for the pool count
                }
            }
        }

        /// The reach the spell will have live - the preview's visible area,
        /// the trap's tripwire and the helper's range, all one number.
        float AreaReach() => Mathf.Max(1.2f, 1.6f * ReachK);

        /// ★ HOW FAR THIS PARTICLE REACHES. Was a flat constant, so a barely
        /// warm flame and a raging one covered exactly the same ground and
        /// nobody could tell them apart until they were standing in it.
        ///
        /// Now it grows with how far past its thresholds the numbers sit, and
        /// with the size of the rune that was drawn. The ring you can see is
        /// this same number, so the tell never lies about the reach.
        public float AuraRadius
        {
            get
            {
                var p = PayloadNow;
                float influence = 1f;
                for (int i = 0; i < Fusions.Count; i++)
                    influence = Mathf.Max(influence, Fusions[i].Influence(p));
                return DrawingConfig.Lvl2AuraRadius
                     * Mathf.Clamp(influence, 1f, DrawingConfig.AuraInfluenceMax)
                     * ReachK;
            }
        }

        Transform _dormantSeek;   // a BODY the preview drifts toward (enemy/ally)
        float _seekLift;          // aim at chest height for bodies
        Vector3 _meetPoint;       // seal-mates converge on a fixed spot
        bool _meetAt;

        /// Dormant seeking: 1. fly at an enemy its effect can touch, 2. fly
        /// to an ally it can serve, 3. pool with sleeping kin, 4. else float
        /// until expiry. Flying only closes distance; waking obeys area rules.
        void DormantTick(float dt)
        {
            if (_wakeAt > 0f && Time.time >= _wakeAt) { Wake(); return; }

            // the ghost IS the authored spell (his rule: dormant differs by
            // size and transparency ONLY) - the pose morph must run asleep
            // too, or the preview stays a raw blob
            TickShape(dt);

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
                    // converging on kin gets the light damping too - fusing
                    // asleep should feel eager, never a slow drift
                    Vel *= Mathf.Max(0f, 1f - (_dormantSeek != null || _meetAt ? 1.2f : 3f) * dt);
                    if (Vel.sqrMagnitude > DrawingConfig.DormantSeekSpeed * DrawingConfig.DormantSeekSpeed)
                        Vel = Vel.normalized * DrawingConfig.DormantSeekSpeed;
                }
                transform.position += Vel * dt;
                // a thrown ghost faces where it is going, same as a live one
                if (inFlight && Vel.sqrMagnitude > 1.2f)
                    transform.rotation = Quaternion.LookRotation(Vel);

                // leash: an anchored preview never strays beyond a short
                // radius of its seal; a ghost chasing a body is exempt, and
                // so is one converging on a seal-mate - the leash was holding
                // the two halves of a drawing apart forever, and the meteor
                // could never finish pooling
                if (!inFlight && _hasAnchor && _dormantSeek == null && !_meetAt)
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

            // an unused preparation pops out - never a shrink (his rule);
            // even expiring unused, an acolyte's spell feeds the wand back
            _dormantLeft -= dt;
            if (_dormantLeft <= 0f)
            {
                ImpactFx();
                PlayerInk.CreditWand(OwnerId, DrawingConfig.InkMax * 0.05f);
                Die();
                return;
            }

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
                if (q.Kind != Kind || Kind == ParticleKind.Flame) continue;
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

            // ★ DETONATION IS THE NUMBERS (his rule): every loud axis
            // detonates as what it IS - heat as flame, chill as ice, light
            // as a flash, dark as a poof, liquid as a splash in its own
            // colour. Several loud axes burst together.
            var p = PayloadNow;
            bool any = false;
            bool Loud(int axis) =>
                Mathf.Abs(SpellPayload.ToHuman(axis, p[axis])) >= SpellPayload.LineFor(axis);

            if (Loud(0))
            {
                FxLibrary.Spawn(p.Temp > 0f ? lib.HitSpark : lib.IceHit, at);
                if (p.Temp > 0f) { var b = FxLibrary.Spawn(lib.FireBurst, at);
                    if (b != null) b.transform.localScale *= 0.55f; }
                any = true;
            }
            if (Loud(1))
            {
                if (p.Lum > 0f) FxLibrary.Spawn(lib.HitLight, at);
                else FxLibrary.SpawnTinted(lib.Poof, at, p.Tint());
                any = true;
            }
            if (Loud(2)) { FxLibrary.Spawn(lib.HitThud, at); any = true; }
            if (Loud(5)) { FxLibrary.Spawn(lib.HitVector, at); any = true; }
            switch (SpellPayload.PhaseOf(p.State))
            {
                case MatterPhase.Liquid:
                    FxLibrary.SpawnTinted(lib.Splash, at, p.Tint()); any = true; break;
                case MatterPhase.Gas:
                    var g = FxLibrary.Spawn(lib.GasCloud, at, null, 1.2f);
                    if (g != null) g.transform.localScale *= 0.4f;
                    any = true; break;
                default:
                    if (Loud(4)) { FxLibrary.Spawn(lib.GroundHit, at); any = true; }
                    break;
            }
            if (any) return;

            // nothing loud: the old kind-family fallback still pops
            var fam = Family(Kind);
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

        /// ★ ATTRACT AND REPEL ARE PARTICLES NORMALLY (his rule): the sign of
        /// their own Affinity says which way they act - the old lineage-read
        /// IsY vector identity is gone with the rest of the vector specials.

        /// THE CONTAINER - all ten, not four. A field, not a property, so
        /// Data.Temp += x writes in place.
        public SpellPayload Data;

        /// What it was born as: the biome it was cast in. Every carrier is
        /// stamped once at birth and measures its capacities from there.
        public SpellPayload Natural { get; private set; }

        SpellPayload ISpellData.Data { get => Data; set => Data = value; }

        /// The caster. Already carried for the dormant wake rules; now it is
        /// the same field every transfer passes along.
        int ISpellData.Owner { get => OwnerId; set => OwnerId = value; }

        // the four names the rest of the code still speaks - views on the
        // container, not storage. Delete once the call sites move over.
        public float Temp { get => Data.Temp; set => Data.Temp = value; }
        public float Lum { get => Data.Lum; set => Data.Lum = value; }
        public float Density { get => Data.Pressure; set => Data.Pressure = value; }
        public float Stick { get => Data.Balance; set => Data.Balance = value; }

        const float AirDensity = 0.55f;      // below this effective density the particle rises
        const float PlasmaDensity = 1.0f;    // at/above this density elementals transform

        static int _nextKey;   // hands each particle its own clock phase
        static readonly List<SpellParticle> All = new List<SpellParticle>();

        /// Every live particle - the sticky hand scans this for grab targets.
        public static IReadOnlyList<SpellParticle> Living => All;

        Renderer _rend;
        float _age, _fearTick, _strikeTick;
        float _chaosLeft;      // ChaosGrip paradox: random impulses, uncontrollable
        Transform _reachRing;  // the visible edge of AuraRadius; tracks it every beat
        /// Riding something. Set when it lands carrying a row that attaches.
        public bool Attached { get; private set; }

        /// Does anything it currently IS want to ride rather than burst?
        /// ★ NOT A FLAG ANY MORE. A thing with no strength cannot be
        /// destroyed by contact, so touching something does not end it - it
        /// rides along instead. Sticky keeps it there. Both are numbers the
        /// author already set, so nothing needed a checkbox.
        bool WantsToAttach => !Physical && PayloadNow.Balance > 0.05f;

        long _auraBeat = -1;   // the world beat this particle last radiated on
        long _driftBeat = -1;  // and the one it last drifted on

        /// This particle's phase on the world clock. Stable for its whole
        /// life, so its beats stay evenly spread rather than jumping about.
        int ClockKey => _clockKey;
        int _clockKey;
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
            // pool rebirth: the primitive's own trigger size (emission and
            // the settle path both rescale it per life)
            var resetSc = GetComponent<SphereCollider>();
            if (resetSc != null) resetSc.radius = 0.5f;
            _settled = false;
            _chaosLeft = 0f;
            _donateTick = _patchTick = 0f;
            _fearTick = _strikeTick = _lureRetarget = 0f;
            _impactFxAt = 0f;
            _lure = null;
            _slamActive = false;
            _slamPrey = null;
            _scanCd = 0f;
            _strikeGen = 0;
            GrammarLevel = 1;
            Lineage = 0;
            LeaveSeal();
            _areasOwed.Clear();
            _areasDeferred = false;
            _biomeOwed = false;
            _mergeQuietAt = 0f;
            _primed = false;
            _ballistic = false;
            _thrownBy = null;
            _kickedBodies.Clear();
            _liveAreas.Clear();
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
            Data = new SpellPayload();
            _lookKind = ParticleKind.Push;
            if (_reachRing != null) { Destroy(_reachRing.gameObject); }
            _reachRing = null;
            Fusions.Clear();      // or a reused husk acts as its previous life's spell for a beat
            _isAreaChild = false;
            _wornArea = null;
            _areaHome = null;
            Natural = new SpellPayload();
            _biomes.Remove(this);
            Attached = false;
            if (_tail != null) { Destroy(_tail); _tail = null; }
            if (_rowFx != null) { Destroy(_rowFx); _rowFx = null; }
            if (_areaLook != null) { Destroy(_areaLook); _areaLook = null; }
            _newest = null;
            _wearing = null;
            _morph = 1f;
            _poseP = null; _poseR = null; _poseS = null;
            _bones.Clear(); _boneList.Clear();
            if (_shapeBody != null) { Destroy(_shapeBody); _shapeBody = null; }
            if (_rend != null) _rend.enabled = true;
            _auraBeat = _driftBeat = -1;
            _areasDeferred = false;
            Reach = 0f;
            _rushSpeed = 0f;
            _clockKey = _nextKey++;
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
        /// ★ THE ROW SAYS SO. It used to be a list of particle kinds that
        /// happened not to hunt, which no author could extend.
        /// ★ HUNTING IS MIND. "0 mindless, high follows its task perfectly" -
        /// so a spell with a mind picks a target and goes for it, and a mindless
        /// one drifts. That was a checkbox and it did not need to be.
        bool StrikeKind => PayloadNow.Int >= DrawingConfig.FusionAt;

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
            Scatter();
            Juice.Thud(transform.position);
            Die();
        }

        /// ★ CAST A ROW DIRECTLY. For the Spell Window, so an author can see
        /// the thing they just described without owning the runes for it,
        /// finding a target, or drawing anything.
        ///
        /// It builds the payload from the row's own thresholds, pushed past
        /// them, which is exactly what a player's runes would have added up to.
        public static SpellParticle Cast(SpellTable.Row row, Vector3 at, Vector3 dir,
            float strength = 2.2f, int level = 1)
        {
            if (row == null) return null;
            var load = new SpellPayload();
            for (int i = 0; i < SpellPayload.AxisCount; i++)
                load[i] = row[i] * SpellPayload.UnitOf(i) * DrawingConfig.FusionAt * strength;

            var p = Emit(ParticleKind.Push, at, dir, 1.4f);
            if (p == null) return null;
            p.Data = load.Clamped();
            p.OwnerId = Grimoire.LocalPlayerId;
            p.SrcSize = DrawingConfig.RuneSizeMin * 2f;
            p.GrammarLevel = Mathf.Clamp(level, 1, 3);
            p.Vel = dir.normalized * 6f;
            p.Wake();
            if (level >= 2) p.RefreshIdentity_Public();
            return p;
        }

        /// The window needs the identity pass to run right after it sets the
        /// numbers, or the particle spends a beat not knowing what it is.
        public void RefreshIdentity_Public() => RefreshIdentity();

        /// ★ WHAT IT DIES HOLDING, SCATTERED. Not a debris feature - debris is
        /// simply numbers that were still there when something ended.
        ///
        /// WHAT KILLED IT DECIDES WHAT IT LEAVES. A flame that went out in a
        /// frost biome died BECAUSE its heat reached zero, so there is no heat
        /// in the leftovers and the fragments meet nothing and are nothing. A
        /// meteor died because its STRENGTH ran out while its heat and solidity
        /// were untouched - so the fragments are still hot rock, still fall,
        /// still hurt, and theirs will be too until one generation is not hot
        /// or solid enough and they simply stop being anything.
        ///
        /// Which is why there is no generation guard here. The condition
        /// already stops it.
        void Scatter()
        {
            var left = PayloadNow;
            if (left.Strongest < DrawingConfig.FusionAt * 0.5f) return;   // nothing worth leaving

            int n = Mathf.Max(2, DrawingConfig.ScatterPieces);
            var each = left.Scaled(1f / n);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = (Random.onUnitSphere + Vector3.up * 0.6f).normalized;
                var piece = Emit(ParticleKind.Push, transform.position + d * 0.35f, d,
                                 0.5f, _generation + 1);
                if (piece == null) continue;
                piece.Data = each.Clamped();
                piece.OwnerId = OwnerId;
                piece.SrcSize = SrcSize * 0.6f;
                piece.Vel = d * DrawingConfig.ScatterSpeed;
                piece.Wake();
            }
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
                p._poolKind = kind;
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

            // stamped once from the ground it was cast on. Every carrier
            // inherits its spawn biome the same way, and capacities are
            // measured from here ever after.
            var born = SpellyMap.BiomeAt(pos);
            // same degree language as Element: natural temp is room-based
            var nat = born != null ? born.Natural : new SpellPayload();
            nat.Temp += Element.RoomTemp;
            p.Natural = nat;

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

            // every particle blooms slowly and stays near the seal until a
            // force moves it - attract and repel included (his rule)
            p.Vel = dir.normalized * 0.9f + Random.insideUnitSphere * 0.22f;

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
            var stack = PoolFor(_poolKind);
            if (stack.Count < PoolKeep)
            {
                transform.SetParent(null); // a hand may still be holding us
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

            // dormant: everything below (auras, strikes, lures, fear,
            // chemistry, decay) is frozen, held or free alike - and that
            // INCLUDES drift. A preparation is inactive: it hovers up to
            // DormantLifeSeconds waiting to be thrown, so a mote that relaxed
            // toward ambient while it waited would be empty by the time it
            // was cast.
            if (Dormant) { DormantTick(dt); return; }

            // held-back verdicts land once the last mate is in, or once the
            // merging has gone quiet: areas first, then the biome question
            if (_areasDeferred && PoolSettled) FlushAreas();
            if (_biomeOwed && PoolSettled)
            {
                _biomeOwed = false;
                if (GrammarLevel < 3 && OutPowers(SpellLaw.Here(this)))
                {
                    GrammarLevel = 3;
                    BecomeBiome();
                    return;
                }
            }

            // WHERE IT IS STANDING CHANGES WHAT IT IS. A chill mote in a fire
            // biome heats until it is no longer a chill mote; a liquid one
            // heated far enough crosses into gas. Nothing casts that - it is
            // just the numbers moving.
            TickShape(dt);
            TickDrift(dt);

            // claimed: no lure; held, the hand drives the position (HandGrab),
            // free, plain physics. Auras keep burning everyone else, and the
            // lifetime clock keeps running.
            if (Claimed)
            {
                if (GrammarLevel >= 2 || Kind == ParticleKind.Flame) TickAura(dt);
                // ★ AN AREA CHASES ITS LIVING SPELL and parks where it died -
                // the one-shot homing aimed at the BIRTH spot, which left
                // every poison puddle at the zombie's mouth.
                if (_isAreaChild && !Attached && (_areaHome != null || _rushSpeed > 5f))
                {
                    // ★ THE DIVE TARGET IS FIXED (his rule, explicit): whatever
                    // spawns at an offset flies to WHERE THE SPELL DETONATED -
                    // never chasing the mote, never parking if it dies. Only
                    // after landing does the area ride the living spell.
                    if (_areaHome != null && !_areaHome.Dead)
                    {
                        if (_rushSpeed <= 5f)
                            _areaHomePos = _areaHome.transform.position;
                    }
                    else if (_areaHome != null) _areaHome = null;
                    if (_areaHome == null && _rushSpeed <= 5f) Vel = Vector3.zero;
                    else
                    {
                        Vector3 to = _areaHomePos - transform.position;
                        if (to.sqrMagnitude <= 0.3f && _rushSpeed > 5f)
                        {
                            // ★ THE ARRIVAL IS THE IMPACT (his meteor): a
                            // child that rushed in from its sky offset lands
                            // as a detonation - burning stone, debris, boom -
                            // and then stays on as the area.
                            _rushSpeed = 0f;
                            ImpactFx();
                            ManifestState(transform.position, 1.5f, true);
                            ThrowDebris(transform.position, 5); // the slam scatters burning chunks
                            Juice.Boom(transform.position, 0.9f);
                            // the dive is over: a lookless rock goes back to
                            // being an invisible effect region
                            if (_areaLook == null && _shapeBody == null && _rend != null)
                                _rend.enabled = false;
                        }
                        // the launch speed holds until first arrival; the
                        // gentle follow takes over from there
                        float speed = Mathf.Max(DrawingConfig.AreaHomingSpeed, _rushSpeed);
                        Vel = to.sqrMagnitude > 0.04f ? to.normalized * speed : Vector3.zero;
                    }
                }
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
                // a rune burning out in your hand POPS (nothing ever shrinks
                // out - his rule; a spell dies as an event)
                if (_age > claimedLife) DetonateNow();
                return;
            }

            // the exotic physics (crackle-strike, the pull) only drives motes
            // the BOOK does not name - a named spell does what its author said
            if (Fusions.Count == 0)
            {
                if (Kind == ParticleKind.Lightning) TickLightning(dt);
                else if (Kind == ParticleKind.BlackHole) TickBlackHole(dt);
            }

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
                                // opposite pulls stay undefined - no bending
                                if (Mathf.Sign(o.Data.Affinity) != Mathf.Sign(Data.Affinity)) continue;
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
                    // ballistic debris arcs under gravity, undamped - it flies
                    // heavy and it LANDS; everything else glides mote-style
                    if (_ballistic) Vel += Physics.gravity * 0.85f * dt;
                    else Vel *= Mathf.Max(0f, 1f - (Kind == ParticleKind.Push ? 0.25f : 1.4f) * dt);
                    // ★ RUNES HIT THE FLOOR (his fix): a ray down the flight
                    // direction - if something stands inside this frame's
                    // step, the mote stops THERE and touches it, instead of
                    // tunnelling through the world between frames
                    float step = Vel.magnitude * dt;
                    bool swept = false;
                    if (step > 0.03f)
                    {
                        // solid world first; then trigger SURFACES (the studio
                        // floor is a drawable trigger) - other motes excluded,
                        // so merging still works
                        if (!Physics.Raycast(transform.position, Vel.normalized,
                                out var sweep, step + 0.06f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        {
                            float bestD = float.MaxValue;
                            foreach (var th in Physics.RaycastAll(transform.position,
                                Vel.normalized, step + 0.06f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                            {
                                if (th.collider.GetComponent<SpellParticle>() != null) continue;
                                if (th.distance < bestD) { bestD = th.distance; sweep = th; }
                            }
                        }
                        if (sweep.collider != null)
                        {
                            transform.position = sweep.point + sweep.normal * 0.08f;
                            Touch(sweep.collider);
                            if (_dead) return;
                            swept = true;
                        }
                    }
                    if (!swept) transform.position += Vel * dt;
                    // ★ A FLYING SPELL FACES ITS TRAVEL (his rule): the same
                    // glyph rotated IS a different rune, so the orientation is
                    // a tell of what is coming. Standing motes keep their pose.
                    // A DIVING AREA CHILD stays upright instead - a plummeting
                    // flame nose-down read as "rotated wrongly", and the
                    // authored body is built standing.
                    if (Vel.sqrMagnitude > 1.2f && !(_isAreaChild && _rushSpeed > 5f))
                        transform.rotation = Quaternion.LookRotation(Vel);
                    // the vector-at-rest death is GONE (his rule: attract and
                    // repel are mute particles like every other spell - a
                    // released mote stands where you left it)
                }
            }
            else if (Kind != ParticleKind.BarrierMote)
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
            // a spell's time running out IS a detonation - never a shrink
            if (_age > life) DetonateNow();
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
        /// A BIOME NEVER DETONATES. You walk into it - that is what being a
        /// place means - so it delivers to whatever stands in it and stays put
        /// rather than spending itself on the first thing it touches.
        bool Persistent => GrammarLevel >= 3
            || Kind == ParticleKind.Flame
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


        /// GRAMMAR v4 collision resolution (SPELL_PARTICLES.md). Order:
        ///   1. barrier isolation (nothing combines through a barrier)
        ///   2. same family: level up (lvl2 radiant self, lvl3 ultimate area)
        ///   3. opposites: paradox synthesis
        ///   4. the v2 substrate: lower level dissolves into higher, carriers
        ///      pool, otherwise plain physics.
        static void ResolveLaw(SpellParticle a, SpellParticle b)
        {
            if (a._dead || b._dead) return;
            // AN ATTACHED PARTICLE DOES NOT COMBINE. It is riding something and
            // doing its own job; a passing mote must not absorb it or be
            // absorbed by it.
            if (a.Attached || b.Attached) return;

            // a live particle wakes a sleeper on contact - EXCEPT its own
            // seal-mates: the utterance stays asleep (his rule), so the live
            // spare joining the sleeping drawing goes to sleep itself
            if (a.Dormant != b.Dormant)
            {
                if (a.SealId != 0 && a.SealId == b.SealId)
                    (a.Dormant ? b : a).Sleep();
                else
                {
                    (a.Dormant ? a : b).Wake();
                    // a woken conjure-ghost DIED into its conjure just now - the
                    // law must not keep resolving a corpse against a live mote
                    if (a._dead || b._dead) return;
                }
            }
            if (a.Dormant && b.Dormant)
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

                // ONE LAW, ASLEEP TOO: same axis levels up, different axes
                // fuse by threshold - lvl3-bound pairs wait as a pending pair
                // so the biome opens where the ghost WAKES, not where it slept
                if (SpellTable.IsSteam(a.PayloadNow, b.PayloadNow)
                    || Mathf.Max(EffLevel(a), EffLevel(b)) >= 2)
                { StorePendingPair(a, b); return; }
                LevelMerge(a, b);
                SettleDormantSurvivor(a, b);
                return;
            }

            // THE ONE LAW: heat meeting chill is steam - the only opposition
            // with a product; every other combining pair simply ADDS payloads
            // and the threshold table says what the sum now is. No paradox
            // table, no exotics, no per-pair code. Order matters by itself.
            if (SpellTable.IsSteam(a.PayloadNow, b.PayloadNow))
            {
                a.ImpactFx(); b.ImpactFx();
                Vector3 sat = (a.transform.position + b.transform.position) * 0.5f;
                RuneGrammar.TryDemon(a.Lineage | b.Lineage, sat, FuseSize(a.SrcSize, b.SrcSize));
                SpellEffects.Steam(sat, (a.Power + b.Power) * 0.5f,
                    a.OwnerId >= 0 ? a.OwnerId : b.OwnerId);
                a.Die(); b.Die();
                return;
            }
            LevelMerge(a, b);
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

        /// What a caster actually put into this: how big they drew the rune and
        /// how well they drew it.
        static float Invested(SpellParticle p) => p.Power * p.SrcSize;

        static void LevelMerge(SpellParticle a, SpellParticle b)
        {
            // both ingredients flash at the meeting
            a.ImpactFx(); b.ImpactFx();
            int la = EffLevel(a), lb = EffLevel(b);
            // ON A TIE, THE OLDER ONE SURVIVES. It used to be whichever collider
            // the physics engine reported first, which is arbitrary and not even
            // reproducible between two runs of the same match - and it decided
            // who OWNED the result. Two wizards feeding one fire had the kill go
            // to a coin flip. Spawn order is stable, and it reads right: you lit
            // it, they fed it, it is still your fire.
            var hi = la != lb ? (la > lb ? a : b)
                   : (a._clockKey >= b._clockKey ? a : b);   // the NEWER one
            var other = hi == a ? b : a;

            // THE CREDIT GOES TO WHOEVER INVESTED MORE, which is a different
            // question from which object survives. The further-along particle
            // carries on as the body; the kill belongs to whoever drew bigger
            // and cast better.
            //
            // On an exact tie the NEWER body survives - his call, and it buys
            // something real: lifetime runs off the survivor age, so keeping the
            // younger one means a merge refreshes the clock instead of handing
            // the result an almost-expired one. Spawn order is stable either
            // way, so a match still replays the same twice.
            if (Invested(other) > Invested(hi) && other.OwnerId >= 0)
                hi.OwnerId = other.OwnerId;
            var lo = hi == a ? b : a;

            if (Mathf.Max(la, lb) >= 3) { hi.Absorb(lo); return; } // capped: eats its kin

            // pool payload + ancestry into the survivor
            hi.Lineage |= lo.Lineage;
            hi._primed |= lo._primed; // a thrown ingredient keeps the fuse lit
            hi.Data = (hi.Data + lo.Data).Clamped();   // tops out, then drift pulls it back
            // mismatched levels: the weaker half rules the product (law 6);
            // equals pool their power instead
            hi.Power = la != lb
                ? Mathf.Min(3f, Mathf.Min(hi.Power, lo.Power) * 1.25f)
                : Mathf.Min(3f, hi.Power + lo.Power * 0.5f);
            float hiWasSrc = hi.SrcSize;
            hi.SrcSize = FuseSize(hi.SrcSize, lo.SrcSize);
            hi.Reach = Mathf.Max(hi.Reach, lo.Reach);
            hi.GrowToSize(hiWasSrc);

            // MOMENTUM ADDS. His rule, and it is literally vector addition -
            // two motes flying at each other at the same speed sum to nothing
            // and the result hangs where they met, while anything else carries
            // on along the sum. There is no cancel rule; the stopping IS the
            // addition. Nothing was doing this at all before.
            hi.Vel = a.Vel + b.Vel;

            // and it survives where the two of them MET, not wherever the
            // winner happened to be standing - on a tie the winner is whichever
            // collider reported first, which is no place to put the result
            hi.transform.position = (a.transform.position + b.transform.position) * 0.5f;

            Vector3 at = hi.transform.position;
            lo.BecameObj = hi; // sustain law: lo's rune waits on the survivor
            lo.Die();
            RuneGrammar.TryDemon(hi.Lineage, at, hi.SrcSize);

            // the sum may have crossed into a named region - the table decides
            hi.RefreshIdentity();

            // ★ ONE PARTICLE, ONE LOOK (his rule): combining pools the data
            // and ONE survivor keeps ONE look, changing only when the summed
            // numbers cross a region threshold - which RefreshIdentity just
            // read. Level is the matched region's own, never a count of how
            // many motes met (the two-lvl1s-make-a-lvl2 ladder was pre-V2).
            int lvl = 1;
            for (int i = 0; i < hi.Fusions.Count; i++)
                lvl = Mathf.Max(lvl, hi.Fusions[i].Level);

            // ★ A BIOME MUST OUT-POWER THE GROUND IT STANDS IN (his rule) -
            // falling short parks it at area strength until it eats more.
            // And never mid-pool: two heats of a four-rune seal crossing the
            // biome line must not end the utterance before the rest join.
            if (lvl >= 3 && hi.PoolingDone && hi.OutPowers(SpellLaw.Here(hi)))
            {
                hi.GrammarLevel = 3;
                hi.BecomeBiome();
            }
            else
            {
                hi.GrammarLevel = Mathf.Min(lvl, 2);
                hi._biomeOwed = lvl >= 3; // judged again when the pool settles
            }

            hi._mergeQuietAt = Time.time + 0.6f; // the utterance is still speaking
            if (hi._areasDeferred && !hi.Dormant && hi.PoolingDone) hi.FlushAreas();
        }

        /// ★ IS THIS AXIS AT BIOME STRENGTH? Read off the numbers every time,
        /// never stored - the same way phase and kind are. An axis is a biome
        /// when it carries more than the ground does, so chilling a fire
        /// particle takes its heat below the ground and the marking is simply
        /// gone. Nothing has to un-mark anything.
        ///
        /// PER AXIS, because a particle can be a heat biome and an ordinary
        /// dark mote at the same time - which is what happens when a flame
        /// picks up lightning on the way.
        public bool BiomeOn(int axis) => (_imposeMask & (1 << axis)) != 0;

        /// The mask, recomputed once per drift beat while the ground reading is
        /// already in hand.
        ///
        /// It CANNOT be worked out on demand: asking the ground what it is
        /// means asking every imposing particle what it imposes, which would
        /// come straight back here and never return. Once per beat, from a
        /// ground reading that excludes this particle, is both correct and the
        /// only shape that terminates.
        int _imposeMask;
        float _biomeLeft;   // seconds of ground it has borrowed

        void RemarkBiome(SpellPayload ground)
        {
            if (GrammarLevel < 3) { _imposeMask = 0; return; }
            var mine = PayloadNow;
            int mask = 0;
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                float m = Mathf.Abs(mine.Unit(i));
                if (m < DrawingConfig.FusionAt) continue;
                // what the ground says WITHOUT me - otherwise it out-powers
                // itself and every axis stays marked forever
                float theirs = Mathf.Abs(ground.Unit(i) - (BiomeOn(i) ? mine.Unit(i) : 0f));
                if (m >= theirs) mask |= 1 << i;
            }
            _imposeMask = mask;

            // ★ PUSHED OUT OF BEING A BIOME. Chill a heat biome far enough and
            // its heat falls under the ground's, the axis unmarks, and with no
            // axis left it is an ordinary lvl2 area again. Nothing demotes it -
            // it just stops qualifying, the same way it stopped being a spark.
            if (mask == 0)
            {
                GrammarLevel = 2;
                _biomes.Remove(this);
                DrawingWorld.Instance?.LogEvent("the biome collapses");
            }
        }

        /// Every particle currently imposing on the world. Only lvl3 ones ever
        /// join, so this stays short.
        static readonly List<SpellParticle> _biomes = new List<SpellParticle>();

        /// What the particle-biomes impose at a point. Read by SpellLaw.Here,
        /// exactly like a map biome - a lvl3 particle IS a biome, it does not
        /// spawn one and leave.
        public static SpellPayload SampleAt(Vector3 at)
        {
            var sum = new SpellPayload();
            for (int i = 0; i < _biomes.Count; i++)
            {
                var b = _biomes[i];
                if (b == null || b._dead) continue;
                float r = b.AuraRadius;
                if ((b.transform.position - at).sqrMagnitude > r * r) continue;
                var p = b.PayloadNow;
                for (int k = 0; k < SpellPayload.AxisCount; k++)
                    if (b.BiomeOn(k)) sum[k] += p[k];
            }
            return sum;
        }

        /// Does this particle carry more than the place does, on the axes it
        /// actually carries? Measured in UNITS so degrees and units can be
        /// compared at all, and only where the particle has something to say -
        /// a pure heat mote is not held back by a dark biome it is not arguing
        /// with.
        public bool OutPowers(SpellPayload ground)
        {
            var mine = PayloadNow;
            bool said = false;
            for (int i = 0; i < SpellPayload.AxisCount; i++)
            {
                float m = Mathf.Abs(mine.Unit(i));
                if (m < DrawingConfig.FusionAt) continue;   // not an axis it speaks on
                said = true;
                if (m < Mathf.Abs(ground.Unit(i))) return false;
            }
            return said;
        }

        /// It grows to its summed size and starts imposing. Nothing else
        /// changes - the aura it had at lvl2 keeps running, which is why a
        /// biome both HOLDS you at its numbers and keeps pushing more at you.
        void BecomeBiome()
        {
            if (!_biomes.Contains(this)) _biomes.Add(this);
            RemarkBiome(SpellLaw.Here(this));
            // A SPELL BIOME IS TEMPORARY. Wizards rewriting the island for good
            // would be the end of the map - it borrows the ground and hands it
            // back. Bigger ones last longer, because they cost more to make.
            _biomeLeft = DrawingConfig.BiomeSeconds
                       * Mathf.Clamp(SrcSize / DrawingConfig.RuneSizeMin, 1f, 3f);
            _settled = true;                 // a biome sits where it was made
            Vel = Vector3.zero;
            SetBodyScale(DrawingConfig.BiomeMoteScale);
            if (_reachRing == null)
                _reachRing = GrammarFX.GroundRing(transform, new Color(1f, 1f, 1f, 0.5f));
            DrawingWorld.Instance?.LogEvent("the ink becomes a BIOME");
            RefreshLook();
        }




        void Absorb(SpellParticle food)
        {
            if (_dead || food._dead) return;
            Data = (Data + food.Data).Clamped();
            Vel += food.Vel;   // same law when one simply eats another: vectors add
            Power = Mathf.Min(3f, Power + food.Power * 0.35f);
            float wasSrc = SrcSize;
            SrcSize = FuseSize(SrcSize, food.SrcSize);
            Lineage |= food.Lineage; // ancestry rides EVERY combination
            food.BecameObj = this;   // the food's rune now waits on ME (sustain law)
            _settled = false; // fresh attributes knock it loose
            GrowToSize(wasSrc);
            // spread on either side multiplies
            bool split = food.Density < -0.5f || Density < -0.5f;
            int spreadLevel = Family(food.Kind) == ParticleKind.Spread ? EffLevel(food)
                : Family(Kind) == ParticleKind.Spread ? GrammarLevel : 1;
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
            twin.JoinSeal(SealId); // a split twin is STILL family (law 11)
            if (spreadLevel >= 3) twin.transform.localScale = transform.localScale * 1.2f;
            twin.RefreshLook();
            _generation++;
        }

        // ------------------------------------- transformations (GRAMMAR v4) --
        /// The Dense payload turns an essence particle into its persistent
        /// physical object.
        /// The look this particle was last wearing. The moment the numbers put
        /// it in a different one, the transition runs - once.
        ParticleKind _lookKind = ParticleKind.Push;

        /// Ask the NUMBERS what this has become. Never the derived label: it
        /// already reads Flame the instant the numbers say Flame, so the old
        /// "Kind == Spark && dense" test could no longer ever be true.
        ///
        /// Because drift calls this too, a mote that merely FLOATS somewhere
        /// hot enough will change form on its own with nothing cast at it.
        void CheckTransform()
        {
            // ★ NO HARDCODED TRANSFORMS (his order, Aug 27): the spell
            // creator is the ONLY author of what a payload becomes.
            // Light+Light is Blinding because he wrote Lum 50 as Blinding,
            // never Lightning because an old rule said light condenses.
            // The numbers still pick an unnamed mote's LOOK, and behaviors
            // still read the numbers, but nothing rewrites a payload or
            // renames a spell from code any more.
            var now = Kind;
            if (now == _lookKind) return;
            _lookKind = now;
            RefreshLook();
        }

        /// Dark-heavy motes still PULL - a numbers behavior, kept. But no
        /// verb renames anything: the book decides what a payload is called.
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
        /// WHERE IT STANDS CHANGES WHAT IT IS. The ground pulls its numbers
        /// toward what is natural here, and the threshold set is re-read after
        /// - so a particle becomes a different particle with nothing casting
        /// anything. This is also the only route to gas: heat a liquid mote
        /// far enough and State crosses on its own.
        const float DriftPeriod = 0.25f;

        void TickDrift(float dt)
        {
            if (!WorldClock.IsBeat(DriftPeriod, ClockKey, ref _driftBeat)) return;

            // ★ A SPELL STILL ASSEMBLING DOES NOT DECAY (his reliability
            // rule): while ANY seal-mate remains - hovering, held in a hand,
            // anywhere - every piece of the drawing keeps its full values.
            // The quiet-timer fallback must NOT reopen this door (it did:
            // holding one piece stalled the pool past the quiet window and
            // the Solid bled its State again). Strictly mates-based.
            // A HELD spell is in stasis the same way: it stays what you made.
            if (MatesLeft(SealId) > 1 || Holder != null || Claimed) return;

            // AN AXIS AT BIOME STRENGTH DOES NOT FALL. That is the whole
            // meaning of the marking: it stops being affected by the place and
            // starts being the place. Every OTHER axis still drifts normally,
            // so a heat biome standing in the dark still goes dark.
            if (GrammarLevel >= 3)
            {
                _biomeLeft -= DriftPeriod;
                if (_biomeLeft <= 0f)
                {
                    // the ground goes back to whatever it was; the mote itself
                    // is spent along with it
                    _biomes.Remove(this);
                    DrawingWorld.Instance?.LogEvent("the biome fades");
                    Die();
                    return;
                }
            }

            var was = Data;
            RemarkBiome(SpellLaw.Here(this));
            SpellLaw.Drift(this, DriftPeriod);
            if (GrammarLevel >= 3)
            {
                var held = Data;
                for (int i = 0; i < SpellPayload.AxisCount; i++)
                    if (BiomeOn(i)) held[i] = was[i];
                Data = held;
            }

            // ★ AFFINITY WORKS FROM WHERE IT STANDS (his rule: attract and
            // repel are normal particles) - a released mote pulls or pushes
            // its surroundings on this same beat, no level required. The
            // sign of its own Affinity picks the direction.
            if (!Dormant && Mathf.Abs(Data.Affinity) > 0.05f)
            {
                int an = Physics.OverlapSphereNonAlloc(transform.position,
                    DrawingConfig.AffinityReach, GrammarFX.ScanBuffer, ~0,
                    QueryTriggerInteraction.Ignore);
                for (int i = 0; i < an; i++)
                {
                    var ac = GrammarFX.ScanBuffer[i];
                    if (ac == null || ac.GetComponentInParent<SpellParticle>() == this) continue;
                    Pull(ac, DriftPeriod);
                }
            }
            RefreshIdentity();
            CheckTransform();   // the ground alone can change what it is
        }

        void TickAura(float dt)
        {
            // ONE WORLD BEAT, not a private countdown. Phase comes off this
            // particle's own key, so a hundred flames do not all sweep on the
            // same frame and every machine picks the same frames anyway.
            if (!WorldClock.IsBeat(DrawingConfig.Lvl2AuraPeriod, ClockKey, ref _auraBeat)) return;

            // a hook has one victim and it is already holding it
            if (Attached && transform.parent != null)
            {
                var host = transform.parent.GetComponentInChildren<Collider>();
                if (host != null) Pull(host, 1f);
            }
            float reach = AuraRadius;
            // the ring is drawn in the particle's own scale, so divide it out
            if (_reachRing != null)
                _reachRing.localScale = Vector3.one *
                    (reach / Mathf.Max(0.01f, transform.localScale.x));

            int n = Physics.OverlapSphereNonAlloc(transform.position, reach,
                GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null || c.GetComponent<SpellParticle>() != null) continue;
                // limb capsules ride the same body - the ROOT pays, once
                if (c.attachedRigidbody != null
                    && c.GetComponentInParent<SimpleFPSController>() != null) continue;

                // STRONGEST AT CENTRE, which is his own words for what a lvl2
                // area does. Nothing else in here asks what KIND of particle it
                // is or what kind of thing it found.
                // non-convex mesh colliders refuse ClosestPoint - bounds are
                // close enough for a falloff weight
                var meshC = c as MeshCollider;
                Vector3 near = meshC != null && !meshC.convex
                    ? c.ClosestPointOnBounds(transform.position)
                    : c.ClosestPoint(transform.position);
                float d = (near - transform.position).magnitude;
                float w = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, reach));
                HandOver(c, w * DrawingConfig.AuraShare * Power);
                Pull(c, w);
            }
        }

        /// ★ THE ONLY THING A PARTICLE DOES TO ANYTHING: give away some of its
        /// numbers. Burning, freezing, sticking, slipping, floating, dying are
        /// all consequences the receiver works out for itself from what it now
        /// holds - none of them are written here, and nothing asks whether it
        /// found a player, a zombie, a crate or a wall.
        ///
        /// This replaced a branch that knew about exactly two kinds of victim,
        /// which is why a zombie in a fire used to feel nothing at all.
        /// ★ EVERY SPELL DETONATES ITS AXES ON IMPACT. It spends what it was
        /// carrying into whatever it hit - that IS the hit.
        ///
        /// A biome spends only the axes that are NOT its biome. Throw a rock at
        /// a heat biome and the luminance and compression it happened to be
        /// carrying discharge into the rock; the heat stays, because that axis
        /// is not cargo any more, it is the place. So a flame-and-lightning
        /// biome does not keep both forever - anything that touches it strips
        /// whatever it has not become.
        /// ★ RIDE IT. No detonation, no combining, no expiry on contact - it
        /// hangs on and keeps handing over its numbers on the aura beat, which
        /// is what makes a trail keep marking you and a poison cling.
        ///
        /// It comes off on its own: the moment its numbers stop putting it in
        /// an attaching region it is an ordinary particle again and behaves
        /// like one. Nothing has to remember to remove it.
        void AttachTo(Collider c)
        {
            var host = c.attachedRigidbody != null ? c.attachedRigidbody.transform
                     : c.GetComponentInParent<Element>()?.transform;
            if (host == null) host = c.transform;

            Attached = true;
            Holder = null;
            _settled = true;
            Vel = Vector3.zero;
            transform.SetParent(host, true);
            GrammarLevel = Mathf.Max(GrammarLevel, 2);   // it works by radiating
            DrawingWorld.Instance?.LogEvent("the ink clings on");
        }

        /// Does anything it currently IS get spent on the first thing it
        /// touches? A carried teleport does; a flame riding the same parent
        /// does not, and keeps burning.
        /// ★ WHAT A LEVEL 1 IS. It carries no area, so there is nothing to
        /// leave behind and the hit is the whole of it. Anything carrying an
        /// area survives its own impact, because the area does.
        bool SpentOnContact => Level <= 1;

        void Detonate(Collider c)
        {
            var carried = PayloadNow;

            // ★ THINGS REACT TO BEING HIT (his rule) - but ONCE per body per
            // spell, on impact only. A body has many colliders (every limb),
            // and kicking on each trigger event launched him into orbit.
            if (Mathf.Abs(SpellPayload.ToHuman(5, carried.Affinity)) < 10f)
            {
                var pl = c.GetComponentInParent<SimpleFPSController>();
                Object body = pl != null ? (Object)pl
                    : c.GetComponentInParent<Element>() != null
                        ? c.GetComponentInParent<Element>() : c.attachedRigidbody;
                if (body != null && _kickedBodies.Add(body))
                {
                    Vector3 dir = c.bounds.center - transform.position;
                    dir = (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.up)
                        + Vector3.up * 0.35f;
                    float kick = 2.5f + Power * 1.5f;
                    if (pl != null) pl.TakeHit(dir * kick, 0f);
                    else
                    {
                        // props FLY tumbling (mass decides how far), rooted
                        // things SHAKE - the environment always answers a hit
                        var jel = c.GetComponentInParent<Element>();
                        if (jel != null) jel.ImpactJolt(transform.position, kick);
                        else
                        {
                            var prb = c.attachedRigidbody;
                            if (prb != null && !prb.isKinematic)
                                prb.AddForce(dir * kick, ForceMode.VelocityChange);
                        }
                    }
                }
            }

            if (GrammarLevel < 3)
            {
                HandOver(c, carried, DrawingConfig.TouchShare * Power);
                return;
            }

            var spend = new SpellPayload();
            var keep = new SpellPayload();
            for (int i = 0; i < SpellPayload.AxisCount; i++)
                if (BiomeOn(i)) keep[i] = carried[i];
                else spend[i] = carried[i];

            HandOver(c, spend, DrawingConfig.TouchShare * Power);
            Data = keep;              // discharged; what it IS remains
            RefreshIdentity();        // it may have stopped being a flame
        }

        /// ★ AFFINITY IS A FORCE, and until now it was a number nothing read.
        /// Positive gathers things toward this particle, negative drives them
        /// off - "its own gravity, on everything near", which is the axis
        /// description and now also what it does.
        ///
        /// An ATTACHED one pulls its host toward the seal it was drawn at
        /// instead of toward itself, which is the whole of a hook: catch
        /// something, and it comes to you.
        void Pull(Collider c, float w)
        {
            float aff = PayloadNow.Affinity;
            if (Mathf.Abs(aff) < 0.05f) return;
            // a hook reels its host toward the seal and takes no recoil;
            // a free mote is one light end of the pair - attracting something
            // heavy mostly flings ITSELF there, payload delivered on arrival
            bool hooked = Attached && _hasAnchor;
            Vel += AffinityPair(c, hooked ? _anchorPos : transform.position, aff, w,
                hooked ? float.PositiveInfinity : MoteMass);
        }

        /// A body is ~70kg (the constant creatures already use), so the tuned
        /// force numbers keep meaning "enough to move a person".
        public const float ReferenceMass = 70f;
        const float MoteMass = 1f;

        /// What a collider weighs for the gravity law - density fully
        /// determines weight (his rule). Immovables weigh infinity.
        public static float MassOf(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb != null) return rb.isKinematic ? float.PositiveInfinity : rb.mass;
            var pl = c.GetComponentInParent<SimpleFPSController>();
            if (pl != null)
            {
                var bd = BodyState.Of(pl);
                return ReferenceMass * (bd != null ? bd.TotalWeight : 1f);
            }
            return float.PositiveInfinity;
        }

        static void Nudge(Collider c, Vector3 dv)
        {
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic) rb.AddForce(dv, ForceMode.VelocityChange);
            else
                // AddSpellForce integrates accel x dt - feed this frame's
                // worth so dv lands as written
                c.GetComponentInParent<SimpleFPSController>()
                    ?.AddSpellForce(dv / Mathf.Max(0.001f, Time.deltaTime), Time.deltaTime);
        }

        /// ★ THE GRAVITY IS A PAIR (his rule): one force lands on both ends
        /// and density decides who yields - repel something heavier than you
        /// and you repel yourself. `w` is the caller's beat span, so applied
        /// and returned values are VELOCITY CHANGE over that span (the old
        /// acceleration mode delivered one physics-step's worth per beat -
        /// the pull existed and did nothing you could feel).
        public static Vector3 AffinityPair(Collider c, Vector3 center, float aff, float w, float selfMass)
        {
            Vector3 toward = center - c.transform.position;
            if (toward.sqrMagnitude < 0.01f) return Vector3.zero;
            float mc = MassOf(c);
            // terrain and other immovables have no density - they are outside
            // the data game entirely, so no force pair forms with them (a
            // mote must not drag itself into the floor it hovers over)
            if (float.IsPositiveInfinity(mc)) return Vector3.zero;
            // dv for a reference body; density decides who yields, capped so
            // a feather is flung hard, never teleported
            Vector3 dv = toward.normalized * (aff * w * DrawingConfig.AffinityForce);
            Nudge(c, dv * Mathf.Min(8f, ReferenceMass / mc));
            return float.IsPositiveInfinity(selfMass) ? Vector3.zero
                : -dv * Mathf.Min(8f, ReferenceMass / selfMass);
        }

        /// A body or object CARRYING the axis radiates it - hit by attract or
        /// repel, the target is its own gravity until the drift sheds it.
        /// The carrier is resolved once here, so every caller recoils the
        /// same way: rigidbodies by mass, players by body weight, walls never.
        public static void AffinityField(Transform self, float aff, float w)
        {
            if (Mathf.Abs(aff) < 0.05f) return;
            var selfRb = self.GetComponentInParent<Rigidbody>();
            var selfPl = self.GetComponentInParent<SimpleFPSController>();
            float selfMass;
            if (selfRb != null && !selfRb.isKinematic) selfMass = selfRb.mass;
            else if (selfPl != null)
            {
                var bd = BodyState.Of(selfPl);
                selfMass = ReferenceMass * (bd != null ? bd.TotalWeight : 1f);
            }
            else selfMass = float.PositiveInfinity;

            Vector3 back = Vector3.zero;
            int n = Physics.OverlapSphereNonAlloc(self.position, DrawingConfig.AffinityReach,
                GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = GrammarFX.ScanBuffer[i];
                if (c == null || c.transform.IsChildOf(self)) continue;
                back += AffinityPair(c, self.position, aff, w, selfMass);
            }
            if (back == Vector3.zero) return;
            if (selfRb != null && !selfRb.isKinematic) selfRb.AddForce(back, ForceMode.VelocityChange);
            else selfPl?.AddSpellForce(back / Mathf.Max(0.001f, Time.deltaTime), Time.deltaTime);
        }

        void HandOver(Collider c, float share) => HandOver(c, PayloadNow, share);

        /// Worn defs author the give. Authored axes hand over the def's
        /// numbers instead of the raw ink. Identity and drift keep raw data.
        SpellPayload GiveNow(SpellPayload raw)
        {
            if (Fusions.Count == 0) return raw;
            var g = raw;
            for (int ax = 0; ax < 6; ax++)
            {
                bool authored = false;
                float sum = 0f;
                for (int i = 0; i < Fusions.Count; i++)
                    if (Fusions[i].Axis[ax] != 0)
                    {
                        authored = true;
                        sum += SpellPayload.FromHuman(ax, Fusions[i].Axis[ax]);
                    }
                if (authored) g[ax] = sum;
            }
            return g;
        }

        void HandOver(Collider c, SpellPayload what, float share)
        {
            if (share <= 0.001f) return;
            what = GiveNow(what);
            var el = c.GetComponentInParent<Element>();
            if (el == null) return;

            // A ROW MAY CHOOSE ITS VICTIMS. Nothing else can - a payload
            // reaches whatever it lands on - so this is the one gate, and it
            // is a checkbox a Workshop author ticks.
            // A SPELL MAY CHOOSE ITS VICTIMS - the one selective rule, and
            // the only one, because a payload reaches whatever it lands on.
            // Shoving is not here any more: that is Affinity, which pulls and
            // pushes on its own.
            bool living = el.Data.Alive;
            for (int i = 0; i < Fusions.Count; i++)
                if (Fusions[i].OnlyLiving && !living) return;

            // ★ POISON NEVER EATS ITS OWN (his standing law): an OnlyLiving
            // spell spares living things on its caster's side. Without this
            // the goo's own area rooted the very zombie that spat it - sticky
            // payload, Stuck, canMove=False, the spinning-in-place zombie.
            if (living && OwnerId >= 0
                && Sides.SideOfThing(el.gameObject) == Sides.Of(OwnerId))
                for (int i = 0; i < Fusions.Count; i++)
                    if (Fusions[i].OnlyLiving) return;

            // SENT BACK TO WHERE THE SPELL WAS DRAWN is not a flag any more:
            // an attached particle already pulls its host toward its own seal
            // with Affinity, so a recall is a sticky strengthless spell that
            // attracts. Pull() does it.

            var give = what.Scaled(share);
            el.Data = (el.Data + give).Clamped();

            // mending is SEEN: a green breath on whoever the strength lands on
            if (give.Strength > 1f)
                GrammarFX.PuffBurst(c.bounds.center + Vector3.up * 0.3f,
                    new Color(0.45f, 1f, 0.55f), 4);

            if (OwnerId >= 0) el.Owner = OwnerId;   // blame rides along

            // Luminance still lives on the body board for players - the last
            // number that has not moved onto the element yet.
            if (Mathf.Abs(give.Lum) > 0.001f)
            {
                var pl = c.GetComponentInParent<SimpleFPSController>();
                if (pl != null) BodyState.Of(pl)?.PushLum(give.Lum);
            }

            // BALANCE IS GRIP, and a rigidbody cannot read a number - so the
            // one place a payload turns into physics. Positive is sticky and
            // holds things still; negative is slick and takes their feet away.
            float grip = give.Balance;
            if (Mathf.Abs(grip) < 0.02f) return;
            var cr = c.GetComponentInParent<Creature>();
            if (grip > 0f)
            {
                if (cr != null) cr.ApplyStuck(grip * DrawingConfig.GripSeconds);
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity,
                        Vector3.zero, grip * DrawingConfig.GripBrake);
            }
            else if (cr != null) cr.ApplySlip(-grip * DrawingConfig.GripSeconds);
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
                    || c.GetComponentInParent<Element>() != null
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
            var d = best.GetComponentInParent<Element>();
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

        /// The 12 runes' particle identities - look and feel only; what
        /// combinations become is the table's business, never this map's.
        /// ★ THE KIND IS READ OFF THE NUMBERS, never stored - the same way a
        /// biome's phase is. It is a LOOK and a pool tag, not a state: which
        /// art this particle wears right now. The dominant axis says which
        /// family, and how far out it sits says plain form or condensed one.
        ///
        /// Which is why the old BecomeFlame / BecomeLightning / BecomeBlackHole
        /// were redundant: each one set a kind and then shoved the numbers to
        /// where that kind's region already was. The numbers were always the
        /// real answer.
        ///
        /// Thresholds are the ones those methods used, so nothing about when a
        /// particle changes its look has moved.
        public const float FlameDensity = PlasmaDensity;  // hot AND heavy = a flame that stays
        public const float LightningAt = 2f;              // two lights merged
        public const float BlackHoleAt = -1.5f;           // two darks merged

        public static ParticleKind KindOf(SpellPayload d)
        {
            int ax = d.Dominant;
            if (ax < 0) return ParticleKind.Push;   // nothing in particular: a bare vector
            float u = d.Unit(ax);
            switch (ax)
            {
                case 0: return u > 0f
                    ? (d.Pressure >= FlameDensity ? ParticleKind.Flame : ParticleKind.Spark)
                    : ParticleKind.Frost;
                case 1: return d.Lum >= LightningAt ? ParticleKind.Lightning
                     : d.Lum <= BlackHoleAt ? ParticleKind.BlackHole
                     : u > 0f ? ParticleKind.Light : ParticleKind.Dark;
                case 2: return u > 0f ? ParticleKind.Dense : ParticleKind.Spread;
                case 3: return u > 0f ? ParticleKind.Glue : ParticleKind.Repel;
                default: return ParticleKind.Push;  // affinity and the capacities wear the vector
            }
        }

        public static ParticleKind KindOf(RuneType r)
        {
            switch (r)
            {
                case RuneType.HeatUp: return ParticleKind.Spark;
                case RuneType.HeatDown: return ParticleKind.Frost;
                case RuneType.LuminanceUp: return ParticleKind.Light;
                case RuneType.LuminanceDown: return ParticleKind.Dark;
                case RuneType.StickyUp: return ParticleKind.Glue;
                case RuneType.StickyDown: return ParticleKind.Repel;
                case RuneType.DensityUp: return ParticleKind.Dense;
                case RuneType.DensityDown: return ParticleKind.Spread;
                case RuneType.StateSolid: return ParticleKind.Solid;
                case RuneType.StateLiquid: return ParticleKind.Liquid;
                default: return ParticleKind.Push;
            }
        }

        /// The fusion that makes this a biome, if any axis is locked.
        SpellDef BiomeFusion()
        {
            for (int i = 0; i < Fusions.Count; i++)
                if (Fusions[i].AnyBiome) return Fusions[i];
            return null;
        }

        /// A condensed state maps to its base family (Lightning -> Light).
        public static ParticleKind Family(ParticleKind k)
        {
            switch (k)
            {
                case ParticleKind.Lightning: return ParticleKind.Light;
                case ParticleKind.BlackHole: return ParticleKind.Dark;
                case ParticleKind.Flame: return ParticleKind.Spark;
                default: return k;
            }
        }

        /// What this particle IS on the shared axes - the threshold engine's
        /// whole view of it. Vectors carry their push as Affinity.
        public SpellPayload PayloadNow
        {
            get
            {
                // ★ PURE NUMBERS. Every emission seeds the rune's real
                // payload now, so the old Push-vector Affinity injection -
                // the last place a particle's behaviour lived outside its
                // Data - is gone.
                return Data;
            }
        }

        /// EVERY region this particle's numbers sit in, all at once. A mote
        /// that satisfies both flame and lightning wears both areas rather
        /// than picking one - nothing here beats anything else.
        /// ★ EVERY SPELL THESE NUMBERS ARE, all at once. Authored in the
        /// Spell Creator, not written in code - which is the whole point.
        public readonly List<SpellDef> Fusions = new List<SpellDef>();
        static readonly List<SpellDef> _reread = new List<SpellDef>();

        /// The last region this particle crossed INTO. Null until it is
        /// anything more than its own strongest axis.
        SpellDef _newest;

        /// ★ THE AREA IT CARRIES, if any. One per spell, and it has no numbers
        /// of its own - it works from this particle's, which is why a hot spell
        /// has a hot area and the two can never disagree.
        AoeDef Area
        {
            get
            {
                for (int i = 0; i < Fusions.Count; i++)
                {
                    var a = SpellBook.Live.Aoe(Fusions[i].Aoe);
                    if (a != null) return a;
                }
                return null;
            }
        }

        /// What art it should be wearing right now: the newest spell it became,
        /// or failing that the axis it mostly IS. Both are just names, and a
        /// name is all CollectionManager needs - drop a posed blob called
        /// "Tornado" into Particle Shapes and tornadoes are tornado-shaped,
        /// with no code touched and no enum extended.
        public string ShapeName
        {
            get
            {
                string baseName = _newest != null ? _newest.Name
                                : AxisName(PayloadNow.Dominant, PayloadNow);
                if (baseName == null) return null;

                // A LEVEL CAN HAVE ITS OWN SHAPE. Attract is an arrow of force;
                // Attract at lvl2 is a TORNADO - same axis, completely
                // different thing to look at. So "Attract 2" and "Attract 3"
                // are asked for first and the bare name is the fallback, which
                // means only the levels that deserve their own silhouette need
                // one authored.
                if (GrammarLevel >= 2)
                {
                    string levelled = baseName + " " + GrammarLevel;
                    if (SpellBook.Live.Shape(levelled) != null
                        || CollectionManager.ParticleShapeFor(levelled) != null) return levelled;
                }
                return baseName;
            }
        }

        static string AxisName(int axis, SpellPayload p) => axis switch
        {
            0 => p.Temp > 0f ? "Heat" : "Chill",
            1 => p.Lum > 0f ? "Light" : "Dark",
            2 => p.Pressure > 0f ? "Compress" : "Spread",
            3 => p.Balance > 0f ? "Sticky" : "Slick",
            4 => p.State > 0f ? "Solid" : "Liquid",
            5 => p.Affinity > 0f ? "Attract" : "Repel",
            _ => null,      // nothing in particular: the plain blob
        };

        /// Re-read the table after the numbers moved - by a merge, or by the
        /// ground drifting them. Regions get entered and left; the particle
        /// says so and wears the blended colour.
        void RefreshIdentity()
        {
            // THE CASTER'S BOOK decides which regions exist for this mote. A
            // particle with no owner - the demon's, a biome's leftovers - reads
            // from every book, because nobody is holding one.
            if (OwnerId >= 0) SpellBook.All(PayloadNow, Grimoires.HeldBy(OwnerId), _reread);
            else SpellBook.All(PayloadNow, _reread);
            bool changed = _reread.Count != Fusions.Count;
            if (!changed)
                for (int i = 0; i < _reread.Count; i++)
                    if (_reread[i] != Fusions[i]) { changed = true; break; }
            if (!changed) return;

            // ★ A SPELL CARRIES ITS EFFECTS WHILE IT IS THAT SPELL (his rule,
            // and the creator's own words: byproducts RIDE the numbers).
            // Becoming Heal grants its Strength to the payload; drifting back
            // out of Heal takes it away again - collect the heal rune, get
            // the heal benefit, exactly as long as it IS the heal.
            var fx = Data;
            bool fxMoved = false;
            for (int i = 0; i < _reread.Count; i++)
                if (!Fusions.Contains(_reread[i]))
                    for (int ax = 6; ax < SpellPayload.AxisCount; ax++)
                        if (_reread[i].Axis[ax] != 0)
                        {
                            fx[ax] += SpellPayload.FromHuman(ax, _reread[i].Axis[ax]);
                            fxMoved = true;
                        }
            for (int i = 0; i < Fusions.Count; i++)
                if (!_reread.Contains(Fusions[i]))
                    for (int ax = 6; ax < SpellPayload.AxisCount; ax++)
                        if (Fusions[i].Axis[ax] != 0)
                        {
                            fx[ax] -= SpellPayload.FromHuman(ax, Fusions[i].Axis[ax]);
                            fxMoved = true;
                        }
            if (fxMoved) Data = fx.Clamped();

            // ★ THE NEWEST THING IT BECAME decides its shape. A particle can
            // wear several spells at once, so no coat can be "the" one - but
            // the LATEST crossing is always single, and it is also the most
            // useful thing to show: what just happened to this mote.
            for (int i = 0; i < _reread.Count; i++)
                if (!Fusions.Contains(_reread[i])) _newest = _reread[i];
            if (Fusions.Count > 0 && _reread.Count == 0) _newest = null;

            // ★ BECOMING SOMETHING WITH AN AREA IS WHAT SUMMONS IT. Not a
            // separate "summons" setting - the area appears because the spell
            // it belongs to now exists. A DORMANT ghost summons nothing (a
            // mute preview) - its areas raise the moment it wakes.
            for (int i = 0; i < _reread.Count; i++)
                if (!Fusions.Contains(_reread[i]))
                {
                    var a = SpellBook.Live.Aoe(_reread[i].Aoe);
                    if (a == null) continue;
                    // mid-pool crossings summon LATER, not never: the crossing
                    // is banked, because drift can bleed the payload back under
                    // the line before the pool completes - the meteor was worn
                    // for one second and owed its sky-fall anyway
                    if (Dormant || !PoolSettled)
                    {
                        _areasDeferred = true;
                        _mergeQuietAt = Time.time + 0.6f;
                        if (!_areasOwed.Contains(_reread[i])) _areasOwed.Add(_reread[i]);
                        continue;
                    }
                    StartCoroutine(RaiseArea(a, _reread[i]));
                }

            Fusions.Clear();
            Fusions.AddRange(_reread);
            RefreshTrail();

            // stopped being the thing that clings: let go and be a mote again
            // (a clinging AREA is exempt - its ride is the Spreading law's)
            if (Attached && !WantsToAttach && !_isAreaChild)
            {
                Attached = false;
                _settled = false;
                transform.SetParent(null, true);
            }
            if (Fusions.Count > 0)
            {
                string names = string.Join(" + ", Fusions.ConvertAll(r => r.Name.ToUpperInvariant()));
                DrawingWorld.Instance?.LogEvent($"the ink becomes {names}");
            }
            ReshapeBody();
            // AFTER the body exists - skinning before ReshapeBody painted a
            // body that was not there yet, and the first frame flew white
            RefreshSkin();
            // ★ SOLID READS SOLID (his rule): the additive glow is for energy;
            // a mote in the solid band wears the opaque material, liquid wears
            // glass - the same ladder MatterFX was built for.
            var ph = SpellPayload.PhaseOf(Data.State);
            if (_rend != null) _rend.sharedMaterial = MatterFX.Get(PayloadNow.Tint(),
                ph == MatterPhase.Solid ? MoteShade.Opaque
                : ph == MatterPhase.Liquid ? MoteShade.Transparent : MoteShade.Additive);
        }

        // ------------------------------------------------- touching the world --
        void Touch(Collider c)
        {
            // a thrown rune's first contact IS its detonation (his law) -
            // merging with other motes still happens upstream in ResolveLaw.
            // The thrower's own body is immune for the first half second: in
            // third person the rune exits THROUGH you.
            if (_primed)
            {
                if (_thrownBy != null && Time.time < _thrownAt + 0.5f
                    && c.transform.IsChildOf(_thrownBy)) return;
                DetonateNow();
                return;
            }

            // A DIRECT HIT IS THE WHOLE PAYLOAD, not a share of it - that is
            // the only difference between lvl1 touching a target and lvl2
            // radiating at it.
            if (!Dormant)
            {
                // a clinging spell touching a PLAYER delivers instead of
                // riding (his fix: it parked on his foot and glued him solid)
                if (!Attached && WantsToAttach
                    && c.GetComponentInParent<SimpleFPSController>() == null)
                { AttachTo(c); return; }

                // ★ A LVL2 HIT LANDS ON EVERYTHING IN ITS AREA - each BODY
                // once, not once per collider it happens to own.
                if (GrammarLevel >= 2)
                {
                    float r = AuraRadius;
                    int n = Physics.OverlapSphereNonAlloc(transform.position, r,
                        GrammarFX.ScanBuffer, ~0, QueryTriggerInteraction.Ignore);
                    _hitOnce.Clear();
                    for (int i = 0; i < n; i++)
                    {
                        var h = GrammarFX.ScanBuffer[i];
                        if (h == null || h.GetComponent<SpellParticle>() != null) continue;
                        Object body = h.GetComponentInParent<Element>();
                        if (body == null) body = h.attachedRigidbody;
                        if (body == null) body = h;
                        if (!_hitOnce.Add(body)) continue;
                        Detonate(h);
                    }
                }
                else Detonate(c);

                // ★ ALWAYS SPENT ON IMPACT (his Aug 25 ruling) - the THROWN
                // spell particle only. Areas, biomes and bare unfused motes
                // fall THROUGH to the classic contact handling below (players,
                // matter, the demon, settling); the first version of this
                // gate returned on every path and orphaned all of it.
                // ★ ONLY THE COLLIDER JUDGES IMPACT (his correction): contact
                // hands the data to a catcher or bursts on a non-catcher - no
                // speed threshold. A released spell persists because it HOVERS
                // and touches nothing, not because of a velocity rule.
                if (Fusions.Count > 0 && !_isAreaChild)
                {
                    var bio = BiomeFusion();
                    bool runtimeBiome = GrammarLevel >= 3;
                    if (bio == null && !runtimeBiome)
                    {
                        ImpactFx();
                        ManifestState(transform.position); // solid bursts as rock, liquid as water
                        ThrowDebris(transform.position, 3); // every impact throws chunks
                        // ANYTHING THAT CANNOT CATCH THE DATA (his rule) -
                        // terrain, props with no axes defined - takes the
                        // burst and the payload hangs in the air where it
                        // happened, until the biome clears the spot
                        bool catchable = c.GetComponentInParent<Element>() != null
                            || c.GetComponentInParent<SimpleFPSController>() != null
                            || c.GetComponentInParent<Creature>() != null;
                        if (!catchable)
                            ArtificialBiome.Open(transform.position, Data,
                                AreaReach(), 1f, DrawingConfig.LingerSeconds);
                        // spent on impact: the acolyte's wand gets its cut
                        PlayerInk.CreditWand(OwnerId, DrawingConfig.InkMax * 0.05f);
                        Die();
                        return;
                    }
                    if (bio != null)
                        for (int i = 0; i < SpellPayload.AxisCount; i++)
                            if (!(bio.BiomeAxis[i] && bio.Axis[i] != 0)) Data[i] = 0f;
                    ImpactFx();   // the unmarked axes just detonated; it lives on
                }

                // ★ SPREADING POISON CLINGS (his call): an area whose AoeDef
                // spreads rides the living thing that walked into it, the
                // way flame carries - contagion, not a parked puddle.
                if (_isAreaChild && !Attached && _wornArea != null && _wornArea.Spreading)
                {
                    var host = c.GetComponentInParent<Element>();
                    if (host != null && host.Data.Alive && host.transform != transform.parent
                        // never rides its own side - poison spares its own
                        && !(OwnerId >= 0 && Sides.SideOfThing(host.gameObject) == Sides.Of(OwnerId)))
                    {
                        transform.SetParent(host.transform, true);
                        Attached = true;
                        _areaHome = null;   // it found a better ride
                    }
                }
            }

            // ENGINE HOOKS ARE GONE. Teleport is Affinity, invisibility is
            // State, a trail is the area's. Nothing is left that a number
            // could not say, so nothing is dispatched by name any more.

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
            // dark spot is a trap, an attract mote is a waiting magnet.
            if (m == null && creature == null && rb == null)
            {
                // lvl2 slip CANNOT be stopped by anything - it bounces
                if (GrammarLevel >= 2 && Kind == ParticleKind.Repel)
                {
                    Vel = Vector3.Reflect(Vel, Vector3.up) + Random.insideUnitSphere * 1.5f;
                    return;
                }
                // ★ A RUSHING AREA LANDS LIKE A METEOR (his design): arriving
                // fast from its sky offset it detonates - the carried state
                // bursts as burning matter whose debris ignites what it hits -
                // and then it STAYS as the area (spreading flames, if authored).
                if (_isAreaChild && Vel.sqrMagnitude > 25f)
                {
                    ImpactFx();
                    ManifestState(transform.position, 1.5f, true);
                    Juice.Boom(transform.position, 0.9f);
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
                e.JoinSeal(SealId);   // and its family
            }
            Die();
        }

        void TouchPlayer(SimpleFPSController pilot)
        {
            // a preview touches nobody; a live particle bites everyone,
            // holder included
            if (Dormant) return;
            // ★ A GHOST IS NOT A BODY. The ghost hovers at the driven
            // zombie's head, so its own cast spawned INTO it - the hit
            // re-downed the pilot and threw them out of the body every time.
            if (pilot.IsDowned) return;
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
            if (Kind == ParticleKind.Push)
                pilot.TakeHit(VectorImpulse(pilot.Velocity, pilot.transform.position), 0f); // the felt kick

            // ★ ABSORB ITS VALUES (his rule): a spell that spends itself on a
            // body hands over EVERYTHING it carries, whatever kind it wears -
            // friendly fire included, your own release included. The per-kind
            // fixed handouts were eating every other axis a fusion carried.
            var give = Data;
            if (Mathf.Abs(give.Temp) > 0.5f) board?.PushTemp(give.Temp * 0.3f);
            if (Mathf.Abs(give.Lum) > 0.05f) board?.PushLum(give.Lum * 0.5f);
            if (Mathf.Abs(give.Pressure) > 0.05f) board?.PushWeight(give.Pressure * 0.45f);
            if (Mathf.Abs(give.Balance) > 0.05f) board?.PushGrip(give.Balance * 0.9f);
            if (Mathf.Abs(give.Affinity) > 0.05f) board?.PushAffinity(give.Affinity);
            ImpactFx();
            ManifestState(transform.position); // the rock still lands ON people
            Die();
        }

        /// Attract gathers the target toward the mote, Repel drives it off
        /// and turns an incoming charge around - the sign of Affinity does
        /// the work, same law as the ambient pull.
        Vector3 VectorImpulse(Vector3 targetVel, Vector3 targetPos)
        {
            Vector3 off = targetPos - transform.position;
            Vector3 away = off.sqrMagnitude > 0.01f ? off.normalized
                : Vel.sqrMagnitude > 0.01f ? Vel.normalized : transform.forward;

            if (Data.Affinity >= 0f)
                return -away * (DrawingConfig.VectorPull * Power);

            return -targetVel * DrawingConfig.VectorReverse
                   + away * (DrawingConfig.VectorPull * Power);
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

            // AFFINITY LANDS LIKE ANY OTHER AXIS, on any object (his rule) -
            // the target carries it and radiates until the drift sheds it
            if (Mathf.Abs(Data.Affinity) > 0.05f)
            {
                var bd = BodyState.Of(c);
                if (bd != null) bd.PushAffinity(Data.Affinity);
                else
                {
                    var ea = c.GetComponentInParent<Element>();
                    if (ea != null)
                    { var da = ea.Data; da.Affinity += Data.Affinity; ea.Data = da.Clamped(); }
                }
            }

            // attract and repel move THINGS by the same law they move players
            if (Kind == ParticleKind.Push)
            {
                if (rb != null && !rb.isKinematic)
                    rb.AddForce(VectorImpulse(rb.linearVelocity, rb.position)
                        / Mathf.Max(0.2f, rb.mass * 0.1f), ForceMode.VelocityChange);
                else if (creature != null)
                {
                    var crb = creature.GetComponent<Rigidbody>();
                    if (crb != null && !crb.isKinematic)
                        crb.AddForce(VectorImpulse(crb.linearVelocity, crb.position)
                            / Mathf.Max(0.2f, crb.mass * 0.1f), ForceMode.VelocityChange);
                }
                ImpactFx();
                Die();
                return;
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

        /// ★ ONE DESTINATION. Heat goes into the thing's own numbers, and
        /// what happens next - burning, melting, boiling, spreading - falls out
        /// of those numbers on the element beat.
        ///
        /// This used to be a three-way branch: Matter got its own store, a
        /// player got a different one, and everything else had a Thermal added
        /// to it on the spot. A zombie was on none of those paths, which is
        /// exactly why a zombie could stand in a fire and not care.
        static void GiveHeat(Collider c, float delta)
        {
            var el = c.GetComponentInParent<Element>();
            if (el == null) return;
            var d = el.Data;
            d.Temp += delta;
            el.Data = d.Clamped();
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
            var fam = Family(Kind);
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
                case ParticleKind.Solid: c = new Color(0.50f, 0.62f, 0.50f); break;   // his palette: grayish green
                case ParticleKind.Liquid: c = new Color(0.55f, 0.75f, 0.95f); shade = MoteShade.Transparent; break; // light blue
                default: c = Color.white; break;
            }
        }

        string _wearing;
        GameObject _shapeBody;

        // ONE BLOB, POSED. Every shape is the same model with its bones moved,
        // which is how they are authored - so changing shape is not swapping an
        // object, it is moving the bones to where the other pose has them. That
        // is also why it can be gradual: there is nothing to pop between.
        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        readonly List<Transform> _boneList = new List<Transform>();
        Vector3[] _poseP; Quaternion[] _poseR; Vector3[] _poseS;
        float _morph;   // 0 = still in the old pose, 1 = arrived

        /// Put on the pose its current name asks for. Only acts when the name
        /// actually changed, so a flame that stays a flame is not re-posed
        /// every beat.
        /// ★ A SPELL CASTING A SPELL. The children are ordinary particles
        /// carrying a share of this one's numbers - so a meteor is hot, bright
        /// and solid on the way down, and burns what it lands on for exactly
        /// the reason anything else does.
        ///
        /// A child never summons again: only a first-generation particle can,
        /// or a meteor would rain meteors forever.
        /// ★ RAISE THE AREA. It appears at its offset and rushes back toward
        /// the spell - twenty metres up is a meteor, no offset at all is an
        /// aura sitting on it.
        ///
        /// It carries THIS particle's numbers, because an area has none of its
        /// own. And it is NOT parented: it outlives the spell that raised it,
        /// staying as long as those numbers hold, which is why a fire keeps
        /// burning after the mote that lit it is gone.
        /// The area's authored look, on the particle that is the area.
        GameObject _areaLook;

        /// Set the moment a particle is dressed as an area (WearArea) and
        /// cleared on pool reuse. Deriving this from the LOOK meant areas
        /// whose prefab only resolves in the editor died on first contact in
        /// builds - and a look-less authored area was never an area at all.
        bool _isAreaChild;

        /// The authored area this child wears - its Spreading flag decides
        /// whether it clings to what walks in.
        AoeDef _wornArea;

        /// The spell this area rides toward while it lives; where the spell
        /// DIES is where the area parks. Homing at a snapshot of the birth
        /// position left every poison puddle at the zombie's mouth instead of
        /// the landing site.
        SpellParticle _areaHome;
        Vector3 _areaHomePos; // last known home spot - the dive lands here even if the home dies
        float _rushSpeed; // the offset launch speed; cleared on first arrival

        /// One body per lvl2 sweep - without this a zombie with N colliders
        /// took N full payload shares from a single impact.
        static readonly HashSet<Object> _hitOnce = new HashSet<Object>();
        public void WearArea(AoeDef area)
        {
            if (area == null) return;
            _isAreaChild = true;
            _wornArea = area;
            var prefab = area.Prefab;
            if (prefab != null && _areaLook == null)
            {
                _areaLook = Instantiate(prefab, transform);
                _areaLook.transform.localPosition = Vector3.zero;
                _areaLook.transform.localRotation = Quaternion.identity;
                foreach (var col in _areaLook.GetComponentsInChildren<Collider>(true)) Destroy(col);
            }
            // an effect region is not a second body: with no authored look it
            // is INVISIBLE, never the kind-colored fallback blob riding
            // inside the spell it serves
            if (_rend != null) _rend.enabled = false;
            if (area.TrailWidth > 0f)
            {
                if (_tail == null) _tail = gameObject.AddComponent<TrailRenderer>();
                _tail.time = Mathf.Max(0.05f, area.TrailSeconds);
                _tail.widthMultiplier = area.TrailWidth;
                _tail.minVertexDistance = 0.08f;
                _tail.sharedMaterial = MatterFX.Get(PayloadNow.Tint(), MoteShade.Additive);
            }
        }

        readonly System.Collections.Generic.List<(SpellDef def, SpellParticle child)>
            _liveAreas = new System.Collections.Generic.List<(SpellDef, SpellParticle)>();

        System.Collections.IEnumerator RaiseArea(AoeDef area, SpellDef source)
        {
            if (_generation > 0) yield break;   // an area does not raise areas
            // ★ ONE LIVING CHILD PER DEF: drift dropping and re-crossing a
            // region was stacking a fresh fire area every tick - a def only
            // raises again after its previous child is gone
            for (int i = _liveAreas.Count - 1; i >= 0; i--)
            {
                if (_liveAreas[i].child == null || _liveAreas[i].child._dead)
                    _liveAreas.RemoveAt(i);
                else if (_liveAreas[i].def == source) yield break;
            }
            Vector3 home = transform.position;
            int owner = OwnerId;
            // ★ AN AREA CARRIES ONLY THE AXES IT WAS MADE FROM (his rule):
            // the region that raised it masks the payload, so a spell wearing
            // several areas gives each one its own slice, never everything.
            var load = new SpellPayload();
            var full = PayloadNow;
            for (int ax = 0; ax < SpellPayload.AxisCount; ax++)
                if (source == null || source.Axis[ax] != 0) load[ax] = full[ax];
            float size = SrcSize;

            {
                Vector3 at = home + area.Offset;
                // a circle with no launch offset belongs ON the ground
                if (area.Offset.sqrMagnitude < 0.01f
                    && Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var ground,
                        6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    at = home = ground.point + Vector3.up * 0.05f;
                Vector3 back = (home - at);
                Vector3 aim = back.sqrMagnitude < 0.01f ? Vector3.zero : back.normalized;

                var child = Emit(ParticleKind.Push, at, aim == Vector3.zero ? Vector3.up : aim,
                                 1f, _generation + 1);
                if (child != null)
                {
                    child.Data = load.Clamped();
                    // a loaded spell replaces the parent slice and scales
                    // with the drawing's ink
                    var loaded = SpellBook.Live.Spell(area.Spell);
                    if (loaded != null)
                    {
                        float ink = DrawnSizeK(size);
                        var dd = new SpellPayload();
                        for (int ax = 0; ax < SpellPayload.AxisCount; ax++)
                            if (loaded.Axis[ax] != 0)
                                dd[ax] = SpellPayload.FromHuman(ax, loaded.Axis[ax]) * ink;
                        child.Data = dd.Clamped();
                    }
                    child.OwnerId = owner;
                    child.SrcSize = size;
                    child.Reach = Reach; // the area's span keeps the seal's ratio
                    // smaller drawings make smaller areas (his rule)
                    child.ApplySizeRatio(DrawnSizeK(size));
                    // ★ THE OFFSET IS A LAUNCH (his rule): the child spawns at
                    // the offset and RUSHES to the spell - a big offset means
                    // a fast arrival, which is how a meteor falls from Y+22.
                    child._rushSpeed = Mathf.Max(DrawingConfig.AreaHomingSpeed,
                        area.Offset.magnitude * 2.2f); // a meteor STREAKS - 22m up lands in half a second
                    child.Vel = aim * child._rushSpeed;
                    child.Wake();
                    child.WearArea(area);
                    child.RefreshIdentity_Public(); // a loaded spell dresses NOW, not next beat
                    // a LOOKLESS child must still be SEEN falling - the root
                    // shows for the dive and hides again on arrival
                    if (child._areaLook == null && child._shapeBody == null
                        && child._rend != null) child._rend.enabled = true;
                    child._areaHome = this;   // ride the LIVING spell...
                    child._areaHomePos = home; // ...but the dive lands HERE even if it dies
                    _liveAreas.Add((source, child));
                    Debug.Log($"[SpellyZombie] {area.Name} area child born at {at} " +
                        $"(offset {area.Offset}) rush {child._rushSpeed:0.0} m/s");
                }
            }
            yield break;
        }


        /// ★ PUSH THE ROW'S SLIDERS INTO THE MATERIAL. Through a property
        /// block, so the authored material is never replaced and two particles
        /// wearing the same blob can still move completely differently.
        ///
        /// A field left at -1 is not written at all, so a row only overrides
        /// what it cares about.
        void RefreshSkin()
        {
            if (_shapeBody == null) return;

            SpellTable.Look look = null;
            for (int i = 0; i < Fusions.Count; i++)
                if (Fusions[i].Skin != null) look = Fusions[i].Skin;   // newest wins
            // a shape saved with its own material brings it along - the book
            // first, then legacy prefabs
            if (look == null)
            {
                var data = SpellBook.Live.Shape(ShapeName);
                if (data != null && data.Look != null) look = data.Look;
            }
            if (look == null)
            {
                var shapeAsset = CollectionManager.ParticleShapeFor(ShapeName);
                var ss = shapeAsset != null ? shapeAsset.GetComponent<ShapeSkin>() : null;
                if (ss != null) look = ss.Look;
            }
            // ★ ONE WRITER. The same StateView that paints the preview, the
            // zombies and the golems paints the flying particle: tint, state
            // and the sliders together, from birth. The hand-written property
            // block it replaces left the blob white until something else
            // happened to touch it - the exact two-writer bug the preview
            // already taught us, re-made here.
            var view = _shapeBody.GetComponent<StateView>();
            if (view == null) view = _shapeBody.AddComponent<StateView>();
            view.Tint = PayloadNow.Tint();
            view.DriveTint = true;
            view.StateT = SpellPayload.StateT01(PayloadNow.State);
            if (look != null) view.Look = look;
            // ★ A GHOST IS SEE-THROUGH (his complaint: dormant looked exactly
            // like awake). The ghost dress only ever touched the hidden code
            // sphere; the blob needs its own fade, cleared on Wake.
            if (Dormant) view.Fade(0.35f, 999999f);
            view.PushNow();

            // and the authored effects, if this row asked for any
            if (look != null && !string.IsNullOrEmpty(look.Fx) && _rowFx == null)
                _rowFx = FxLibrary.SpawnNamed(look.Fx, transform.position, transform);
        }

        GameObject _rowFx;

        /// What a client needs to look like this one: which posed blob, and the
        /// colour its numbers came out as. Both are read off the payload, so
        /// neither can drift from what the host sees.
        public byte ShapeId => CollectionManager.ParticleShapeIndex(ShapeName);
        public Color32 WireTint => PayloadNow.Tint();
        public byte WireLevel => (byte)Mathf.Clamp(Level, 1, 3);

        /// ★ NOBODY TYPES A LEVEL. No area is a hit, an area makes it an area,
        /// a locked axis makes it a place - read back from what the author
        /// actually made, every time it is asked.
        public int Level
        {
            get
            {
                int lv = 1;
                for (int i = 0; i < Fusions.Count; i++)
                    lv = Mathf.Max(lv, Fusions[i].Level);
                return lv;
            }
        }

        /// ★ NO STRENGTH MEANS FORCE CANNOT TOUCH IT. Not "very fragile" -
        /// not in the physical damage system at all, the same way Mind 0 means
        /// not living rather than very stupid. So it rides things instead of
        /// dying on them, and it can only ever go out when its own numbers run
        /// down - which is also why it leaves nothing behind.
        public bool Physical => Mathf.Abs(PayloadNow.Strength) > 0.001f;

        /// The net id of whatever this is riding, or 0. An attached particle
        /// is only in the right place if the other machines know what it
        /// caught - otherwise a hook hangs in the air while its victim leaves.
        public int RidingId
        {
            get
            {
                if (!Attached || transform.parent == null) return 0;
                var host = transform.parent.GetComponentInParent<Element>();
                return host != null ? host.NetId : 0;
            }
        }

        TrailRenderer _tail;

        /// The widest trail anything it currently IS asks for. A meteor gets a
        /// fat short one, a tracking mark a thin long one, and a particle that
        /// stops being either drops it.
        void RefreshTrail()
        {
            // THE TRAIL IS THE AREA'S, because a trail is part of how an
            // area looks and a bare spell has no look of its own beyond its
            // body.
            var area = Area;
            float w = area != null ? area.TrailWidth : 0f;
            float t = area != null ? area.TrailSeconds : 0f;
            if (w <= 0f)
            {
                if (_tail != null) { Destroy(_tail); _tail = null; }
                return;
            }
            if (_tail == null) _tail = gameObject.AddComponent<TrailRenderer>();
            _tail.time = Mathf.Max(0.05f, t);
            _tail.widthMultiplier = w;
            _tail.minVertexDistance = 0.08f;
            _tail.sharedMaterial = MatterFX.Get(PayloadNow.Tint(), MoteShade.Additive);
        }

        void ReshapeBody()
        {
            string want = ShapeName;
            if (want == _wearing) return;
            // ★ A SHAPE ONCE WORN IS KEPT (his rule): fading numbers never
            // strip a spell back to the anonymous blob - the unique look IS
            // the information. Death is the only floor; a NEW earned shape
            // still replaces the old one.
            if (string.IsNullOrEmpty(want) && !string.IsNullOrEmpty(_wearing)) return;
            _wearing = want;

            EnsureBlob();
            if (_shapeBody == null) return;      // no blob authored yet: code look stands

            // ★ THE BOOK FIRST: a shape is data, so a Workshop spell poses the
            // blob with no prefab anywhere. Legacy prefabs still count after.
            var data = SpellBook.Live.Shape(want);
            if (data != null && data.Bones.Count > 0)
            {
                CapturePose(data);
                _morph = 0f;
                return;
            }

            var pose = CollectionManager.ParticleShapeFor(want);
            if (pose == null) pose = CollectionManager.ParticleBlob;   // back to rest
            if (pose == null) return;

            CapturePose(pose);
            _morph = 0f;
        }

        /// The body itself, made once and kept for this particle's whole life.
        void EnsureBlob()
        {
            if (_shapeBody != null) return;
            var blob = CollectionManager.ParticleBlob;
            if (blob == null) return;

            _shapeBody = Instantiate(blob, transform);
            _shapeBody.name = "Body";
            _shapeBody.transform.localPosition = Vector3.zero;
            _shapeBody.transform.localRotation = Quaternion.identity;
            foreach (var col in _shapeBody.GetComponentsInChildren<Collider>(true))
                Destroy(col);                    // the mote already has its own trigger
            if (_rend != null) _rend.enabled = false;   // authored art replaces the sphere

            _bones.Clear(); _boneList.Clear();
            foreach (var t in _shapeBody.GetComponentsInChildren<Transform>(true))
            {
                if (t == _shapeBody.transform) continue;
                if (!_bones.ContainsKey(t.name)) { _bones[t.name] = t; _boneList.Add(t); }
            }
        }

        /// Read where the target pose keeps each bone. Matched BY NAME, so a
        /// pose prefab only has to be the same blob - it can be missing bones
        /// and the ones it does not mention simply stay where they are.
        void CapturePose(GameObject pose)
        {
            int n = _boneList.Count;
            if (_poseP == null || _poseP.Length != n)
            { _poseP = new Vector3[n]; _poseR = new Quaternion[n]; _poseS = new Vector3[n]; }

            var want = new Dictionary<string, Transform>();
            foreach (var t in pose.GetComponentsInChildren<Transform>(true))
                if (!want.ContainsKey(t.name)) want[t.name] = t;

            for (int i = 0; i < n; i++)
            {
                var mine = _boneList[i];
                if (want.TryGetValue(mine.name, out var theirs))
                {
                    _poseP[i] = theirs.localPosition;
                    _poseR[i] = theirs.localRotation;
                    _poseS[i] = theirs.localScale;
                }
                else
                {
                    _poseP[i] = mine.localPosition;
                    _poseR[i] = mine.localRotation;
                    _poseS[i] = mine.localScale;
                }
            }
        }

        /// The same, from book data instead of a prefab.
        void CapturePose(ShapeDef pose)
        {
            int n = _boneList.Count;
            if (_poseP == null || _poseP.Length != n)
            { _poseP = new Vector3[n]; _poseR = new Quaternion[n]; _poseS = new Vector3[n]; }

            var want = new Dictionary<string, BonePose>();
            foreach (var b in pose.Bones)
                if (!string.IsNullOrEmpty(b.Bone) && !want.ContainsKey(b.Bone)) want[b.Bone] = b;

            for (int i = 0; i < n; i++)
            {
                var mine = _boneList[i];
                if (want.TryGetValue(mine.name, out var theirs))
                {
                    _poseP[i] = theirs.P;
                    _poseR[i] = theirs.R;
                    _poseS[i] = theirs.S;
                }
                else
                {
                    _poseP[i] = mine.localPosition;
                    _poseR[i] = mine.localRotation;
                    _poseS[i] = mine.localScale;
                }
            }
        }

        /// Ease the bones toward the pose. A particle that becomes a tornado
        /// GROWS into one over a moment, which also reads as the transformation
        /// it is rather than a cut.
        void TickShape(float dt)
        {
            if (_poseP == null || _morph >= 1f || _boneList.Count == 0) return;
            _morph = Mathf.Min(1f, _morph + dt / Mathf.Max(0.01f, DrawingConfig.ShapeMorphSeconds));
            float k = _morph * _morph * (3f - 2f * _morph);   // ease, so it settles
            for (int i = 0; i < _boneList.Count; i++)
            {
                var t = _boneList[i];
                if (t == null) continue;
                t.localPosition = Vector3.Lerp(t.localPosition, _poseP[i], k);
                t.localRotation = Quaternion.Slerp(t.localRotation, _poseR[i], k);
                t.localScale = Vector3.Lerp(t.localScale, _poseS[i], k);
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
