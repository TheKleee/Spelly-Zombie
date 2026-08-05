using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    public enum ParticleKind
    {
        Push,                         // level 0 — pure force
        Light, Dark,                  // level 1 — energy; opposites annihilate
        Dense, Spread, Glue, Repel,   // level 2 — property carriers
        Spark, Frost,                 // level 3 — elemental matter
        Lightning, Laser, Shadow,     // condensed states (level 3 — they absorb further)
        Flame,                        // GRAMMAR v4: Spark manifested by Dense — persistent fire, merges bigger
        BlackHole,                    // GRAMMAR v4: Dark lvl2 — pulls things in (family: Dark)
        BarrierMote                   // GRAMMAR v4: Dense+Spread paradox — isolates what it touches
    }

    /// THE LAW (Marko's matter-level rule, SPELL_PARTICLES.md): particles carry
    /// attributes; on collision the lower level dissolves into the higher, and
    /// GRAMMAR v4 (leveling / paradox / exotics) resolves the rest in ResolveLaw.
    public class SpellParticle : MonoBehaviour
    {
        public ParticleKind Kind;
        public float Power = 1f;
        public Vector3 Vel;
        public float SrcSize = 1f; // zone radius of the emitting rune — rides the
                                   // chain so the Demon is sized by the DRAWING
        public int Echo;           // ECHO powerup stacks: landing may re-emit

        // ---- GRAMMAR v4 (SPELL_PARTICLES.md): same+same levels up, opposites
        // synthesize, and every product carries its ANCESTRY — all 12 runes in
        // one chain summons the Demon.
        public int GrammarLevel = 1;  // 1 = base · 2 = radiant self · 3 = ultimate
        public ulong Lineage;         // union of every rune that fed this chain
        public int SealId;            // which SEAL emitted this — siblings of one
                                      // drawing prefer each other (Marko's rule:
                                      // runes inside a seal combine TOGETHER first)

        /// SUSTAIN LAW (Marko's ruling, Jul 20): a rune re-emits only when its
        /// particle is fully GONE — and "gone" follows combinations. When this
        /// particle dies INTO something (an eater, a field, a matter blob, a
        /// demon), that something is recorded here; the emitting rune walks the
        /// chain and waits for the FINAL product to disappear. One light rune
        /// makes one light, forever — never an accidental lightning.
        public Object BecameObj;
        public bool Dead => _dead;

        // ---- THE GRAB (Marko's law): a purposeful grab CLAIMS the spell —
        // it leaves the magic world. No combining ever again, no expiry, no
        // effect on the holder; still dangerous to everyone else. The
        // emitting rune sees a claimed particle as GONE, so the seal
        // re-emits: claiming is harvesting.
        public bool Claimed;
        public Transform Holder;      // whose hand it's in (null once released)
        float _graceUntil;            // released: 0.2s where it spares the thrower
        Transform _graceFor;

        public void Claim(Transform holder)
        {
            Claimed = true;
            Holder = holder;
            _settled = false;
            _lure = null;
            Vel = Vector3.zero;
        }

        /// Let go (throw or drop): velocity in, and 0.2s of grace for the
        /// former holder so it has time to get clear — you can never land a
        /// spell on yourself, but allies CAN be hit: saves are skill shots.
        public void ReleaseHeld(Vector3 velocity)
        {
            _graceFor = Holder;
            _graceUntil = Time.time + 0.2f;
            Holder = null;
            Vel = velocity;
            _settled = false; // brushing walls while held must not freeze the throw
        }

        bool Spares(Transform t) =>
            t == Holder || (t == _graceFor && Time.time < _graceUntil);

        /// A BODY CAST IS A THROW (Marko: "the body can push the particles
        /// as if they were thrown") — the particle leaves with real speed
        /// and spares its caster on the way out, exactly like a hand-thrown
        /// one spares the thrower. Siblings launched together fly together
        /// and combine mid-air: the fire bolt.
        public void ThrowFrom(Transform caster, Vector3 velocity, float grace = 0.35f)
        {
            _graceFor = caster;
            _graceUntil = Time.time + grace;
            Vel = velocity;
            _settled = false;
        }

        float _impactFxAt; // juice throttle — repeats within the window are noise

        /// EVERY delivery lands with JUICE (Marko: "we're not using almost
        /// anything") — one impact effect per kind, at the point of contact.
        /// THROTTLED per particle (Marko: "chain effects overly pop up and
        /// lag") — a chain hitting the same mote 10× a second reads as ONE.
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
                // FLAME EXPLOSION (Marko: "chill has a cool visual but heat
                // doesn't") — heat lands like heat
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

        /// THE ARROW LOOKS LIKE AN ARROW (Marko: "a moving push particle in
        /// the shape of an arrow instead of small yellow ball") — and the Y
        /// forks. Built once lineage says which; an FX_Push prefab in
        /// Resources/Custom (his art) still owns the look outright.
        bool _vectorShaped;
        void EnsureVectorShape()
        {
            if (_vectorShaped || _dead || Kind != ParticleKind.Push) return;
            if (_age < 0.06f) return; // let the seal stamp lineage first
            _vectorShaped = true;
            if (PrefabVault.Get("FX_Push") != null) return;
            // both vectors are ARROWS (Marko: "make the pull purple arrow") —
            // colour tells them apart, pointing tells the truth
            var mat = IsY ? MatterFX.Get(new Color(0.7f, 0.35f, 0.95f), MoteShade.Additive)
                : _rend != null ? _rend.sharedMaterial : null;
            if (_rend != null) _rend.enabled = false; // the ball retires

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

        const float AirDensity = 0.55f;      // effective density below this → rises
        const float PlasmaDensity = 1.0f;    // + luminance/darkness/heat → transformation

        static readonly List<SpellParticle> All = new List<SpellParticle>();

        /// Every live particle — the sticky hand scans this for grab targets.
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

        // ---- THE FREEZER (Marko: "prepare the particles in advance and
        // freeze them and call them from the pool" — building a sphere, a
        // rigidbody and a CFXR/FX rig from scratch at every seal close was
        // the casting hitch). Pools are PER KIND, so a thawed body wakes with
        // its look, its light and its glyph shape already built.
        static readonly Dictionary<ParticleKind, Stack<SpellParticle>> _pool =
            new Dictionary<ParticleKind, Stack<SpellParticle>>();
        const int PoolKeep = 6; // per kind — beyond this, dying particles really die

        static Stack<SpellParticle> PoolFor(ParticleKind k)
        {
            if (!_pool.TryGetValue(k, out var s)) _pool[k] = s = new Stack<SpellParticle>();
            return s;
        }

        /// Load-time warmup: build a few of every kind (shaders compile, his
        /// FX_&lt;Kind&gt; prefabs and the CFXR idles instantiate ONCE) and freeze
        /// them — the first seal of the match costs the same as the fiftieth.
        public static void PrewarmPool(int perKind = 2)
        {
            foreach (ParticleKind k in System.Enum.GetValues(typeof(ParticleKind)))
                for (int i = 0; i < perKind; i++)
                    Emit(k, new Vector3(0f, -900f, 0f), Vector3.up, 1f).Die();
        }

        /// Everything a past life could have left behind, zeroed. Kind, the
        /// look, the light and the vector glyph survive on purpose — they are
        /// exactly the expensive parts the pool exists to keep.
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
            GrammarLevel = 1;
            Lineage = 0;
            SealId = 0;
            BecameObj = null;
            Claimed = false;
            Holder = null;
            _graceUntil = 0f;
            _graceFor = null;
            Temp = Lum = Density = Stick = 0f;
            Vel = Vector3.zero;
        }

        // ------------------------------------------------------------- birth --
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
                    All[0].Die(); // oldest yields — the same fence Awake holds
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
            go.transform.localScale = Vector3.one * (kind == ParticleKind.Push ? 0.09f : 0.14f);
            p.Power = Mathf.Clamp(intensity, 0.2f, 2f);
            p._generation = generation;
            p._appetite = Random.value; // personality: some motes stalk, some are lazy

            // the payload IS the rune: fixed spark heat (Marko: 3 hits on one
            // target = 3× the bump), symmetric both ways — the freeze POINT was
            // rescaled instead of making frost weaker
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

            // push particles DRIFT with intent (Marko: "their own movement
            // shouldn't be as fast so that they can be combined") — the
            // DIRECTION is the payload, the mote itself is slow enough that
            // two arrows in one seal attract and entwine into the tornado.
            // Everything else blooms off the ink and hangs around the seal
            // until a force moves it (user rule: no drifting off on their own).
            float speed = kind == ParticleKind.Push ? 4.2f : 0.9f; // vectors FLY (Marko: "make the particle go faster")
            p.Vel = dir.normalized * speed
                + Random.insideUnitSphere * (kind == ParticleKind.Push ? 0.2f : 0.22f);

            if (kind == ParticleKind.Light && go.GetComponent<Light>() == null)
            {
                var l = go.AddComponent<Light>(); // a thawed Light keeps its lamp
                l.type = LightType.Point; l.range = 4.5f; l.intensity = 2.2f;
                l.color = new Color(1f, 0.96f, 0.8f);
            }

            p.RefreshLook();
            return p;
        }

        void Awake()
        {
            _rend = GetComponent<Renderer>();
            All.Add(this);
            if (All.Count > DrawingConfig.ParticleCap && All[0] != null)
                All[0].Die(); // oldest yields — mayhem needs a fence
        }

        void OnDestroy() => All.Remove(this);

        void Die()
        {
            if (_dead) return;
            _dead = true;
            All.Remove(this);
            // into the freezer, not the grave (Marko's pool ruling) — the
            // body keeps its kind-specific look for the next cast
            var stack = PoolFor(Kind);
            if (stack.Count < PoolKeep)
            {
                transform.SetParent(null); // a hand may still be holding us
                if (_vectorShaped)
                {
                    // the glyph was tinted by THIS life's arrow/Y identity —
                    // the next life re-shapes it from its own lineage
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

            // CLAIMED = object now (Marko's law): no combining, no lure, no
            // expiry. Held, the hand drives the position (HandGrab); free,
            // plain physics. Auras keep burning EVERYONE ELSE, and a carried
            // flame still terrifies zombies that can see it.
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
                Vel += Random.insideUnitSphere * 14f * dt; // uncontrollable means UNCONTROLLABLE
                _settled = false;
            }

            if (!_settled)
            {
                // DENSITY IS WEIGHT (Marko's rule): heavy falls, thinned floats — gently
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.BlackHole)
                    Vel += Vector3.down * (EffDensity() - AirDensity) * 2.5f * dt;

                // MOTES SEEK EACH OTHER (Marko: "they almost always miss")
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.BlackHole
                    && Kind != ParticleKind.BarrierMote)
                {
                    // SEAL KIN FIRST (Marko's rule): particles born of the SAME
                    // DRAWING want each other above all — the runes you enclosed
                    // together are a recipe, not roommates. Foreign particles
                    // only attract when no seal-sibling is in reach.
                    // VECTORS LOVE VECTORS: a Push only seeks other Pushes (two
                    // arrows entwine into the tornado) and never distracts an
                    // essence particle from its own chemistry.
                    bool iAmPush = Kind == ParticleKind.Push;
                    SpellParticle near = null;
                    bool nearIsKin = false, nearIsVector = false;
                    float baseRange = DrawingConfig.ParticleKinRange * DrawingConfig.ParticleKinRange;
                    float kinRange = baseRange * 4f; // same-seal reach: 2× the distance
                    float vecRange = baseRange * 9f; // twin sight: 3× — vectors FLY, they need the head start
                    float bestSqr = float.MaxValue;
                    for (int i = 0; i < All.Count; i++)
                    {
                        var o = All[i];
                        if (o == this || o == null || o._dead) continue;
                        if (iAmPush)
                        {
                            // VECTORS IGNORE ESSENCES (their line stays true) but
                            // ARROWS SEEK ARROWS and Ys SEEK Ys (Marko: "so we
                            // can combine them more easily") — twins entwine.
                            if (o.Kind == ParticleKind.Push)
                            {
                                if (o.IsY != IsY) continue; // arrow×Y stays undefined — no bending
                                float vd = (o.transform.position - transform.position).sqrMagnitude;
                                if (vd <= vecRange && vd < bestSqr)
                                { near = o; bestSqr = vd; nearIsVector = true; }
                                continue;
                            }
                            // THE ARROW IS WIND: it still herds every essence —
                            // and SIZE IS NO ARMOR (Marko: "push should work
                            // on all objects"): reach measures to the SURFACE,
                            // so a huge compress+fire ball is a bigger sail,
                            // not an immovable one (center-distance ranging
                            // made anything large silently push-proof)
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
                            // vectors out-pull STRANGERS, never the mote's own
                            // recipe (Marko's lightning catch: Light+Light must
                            // still entwine with arrows flying past) — kin first
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

                    // WIND MOVES MATTER TOO (Marko: "push should work on all objects")
                    if (iAmPush)
                        for (int mi = 0; mi < Matter.Living.Count; mi++) // indexed — foreach boxed the enumerator every frame
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
                        // FIRST LOVE: other particles — affinity beats appetite
                        Vector3 to = near.transform.position - transform.position;
                        if (iAmPush && nearIsVector)
                        {
                            // TWINS HUNT HARD (Marko: "stronger attraction…
                            // they move so fast it's hard to make them
                            // combine"): raw pull plus a real STEER — the
                            // arrow bends its flight line toward its twin
                            // instead of adding a nudge it instantly outruns.
                            Vel += to.normalized * (22f * dt);
                            float sp = Vel.magnitude;
                            if (sp > 0.05f)
                                Vel = Vector3.Slerp(Vel / sp, to.normalized,
                                    Mathf.Clamp01(6f * dt)) * sp;
                        }
                        else
                            Vel += to.normalized * ((iAmPush ? 4.5f : nearIsVector ? 3.6f : 2.2f) * dt);
                        // two fast twins can STEP OVER a tiny meet window in a
                        // single frame — theirs is wider so the pass connects
                        float meetR = iAmPush && nearIsVector ? 0.3f : 0.12f;
                        if (bestSqr < meetR * meetR && GetInstanceID() < near.GetInstanceID())
                            ResolveLaw(this, near);
                    }
                    else if (!iAmPush)
                    {
                        // no particle nearby — a spell still hunts MATTER blobs
                        // (Marko: ALL spells look to combine, vectors excepted)
                        Matter mNear = null;
                        float mBest = baseRange;
                        for (int mi = 0; mi < Matter.Living.Count; mi++) // indexed — foreach boxed the enumerator every frame
                        {
                            var mm = Matter.Living[mi];
                            if (mm == null || mm.Touched) continue;
                            float md = (mm.transform.position - transform.position).sqrMagnitude;
                            if (md < mBest) { mBest = md; mNear = mm; }
                        }
                        if (mNear != null)
                            Vel += (mNear.transform.position + Vector3.up * 0.15f - transform.position)
                                .normalized * (2.4f * dt);
                        else TickLure(dt); // nothing magical around — stalk something ALIVE
                    }
                }
                // condensed movers integrate their own motion in Tick* — moving
                // them here too ran them at DOUBLE speed (verified)
                if (Kind != ParticleKind.Lightning && Kind != ParticleKind.BlackHole)
                {
                    Vel *= Mathf.Max(0f, 1f - (Kind == ParticleKind.Push ? 0.25f : 1.4f) * dt);
                    transform.position += Vel * dt;
                    // arrows point WHERE THEY'RE GOING; Ys point the other way
                    if (_vectorShaped && Vel.sqrMagnitude > 0.02f)
                        transform.rotation = Quaternion.LookRotation(IsY ? -Vel : Vel);
                    // a vector at REST is nothing (motion IS its identity) —
                    // no more arrows littering the floor (Marko); the seal's
                    // sustain law just makes a fresh one
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
            // panic — a flame carrying darkness is invisible, and they walk
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

        /// PARTICLES FEEL ALIVE (Marko's rule): a kinless mote stalks the
        /// nearest living thing; only the hungriest quarter stalks wizards.
        void TickLure(float dt)
        {
            if (_appetite < 0.35f) return; // the lazy third just sits there — variety reads as life

            _lureRetarget -= dt;
            if (_lureRetarget <= 0f)
            {
                _lureRetarget = 0.5f;
                float best = DrawingConfig.ParticleChaseRange * DrawingConfig.ParticleChaseRange;
                _lure = Targets.Nearest(transform.position, ref best, _appetite > 0.75f);
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
            || Kind == ParticleKind.Flame; // (the black hole is invisible — never feared, always fatal)

        /// How visible this particle is to a googly eye. Base glow by kind,
        /// dimmed by any darkness it carries — the invisible-flame trap.
        float EffectiveLum()
        {
            float glow;
            switch (Kind)
            {
                case ParticleKind.Spark: glow = 0.6f; break;
                case ParticleKind.Frost: glow = 0.35f; break;
                case ParticleKind.Lightning: glow = 2.5f; break;
                case ParticleKind.Flame: glow = 0.9f; break; // a flame is SEEN — unless darkness dims it
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
                // objects don't combine (Marko's law): a claimed particle is
                // out of the grammar for good, on either side of the meeting
                if (Claimed || op.Claimed) return;
                // both sides get the event — only one resolves the law
                if (GetInstanceID() < op.GetInstanceID()) ResolveLaw(this, op);
                return;
            }
            if (other.isTrigger) return;
            Touch(other);
        }

        /// Persistent = delivers on a beat and STAYS: flames, lvl2 grip/slip,
        /// and settled Glue/Repel ground patches (one predicate, two callers).
        bool Persistent => Kind == ParticleKind.Flame
            || (GrammarLevel >= 2 && (Kind == ParticleKind.Glue || Kind == ParticleKind.Repel))
            || (_settled && (Kind == ParticleKind.Glue || Kind == ParticleKind.Repel));

        /// Persistent particles deliver to whatever STAYS in them — Enter alone
        /// fired once (verified: standing in fire was free after the first lick).
        void OnTriggerStay(Collider other)
        {
            if (_dead || other.isTrigger) return;
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
        ///   2. same FAMILY → LEVEL UP (lvl2 radiant self, lvl3 ultimate area)
        ///   3. opposites → PARADOX SYNTHESIS (annihilation is repealed)
        ///   4. the v2 substrate: lower level dissolves into higher, carriers
        ///      pool, otherwise plain physics. "Old passive states are kept."
        static void ResolveLaw(SpellParticle a, SpellParticle b)
        {
            if (a._dead || b._dead) return;

            // an isolated particle refuses ALL chemistry — the barrier is the
            // system's insulator (and the only way to stop an accidental Demon)
            if (Time.time < a._isolatedUntil || Time.time < b._isolatedUntil)
            {
                BounceApart(a, b, 1.2f);
                return;
            }

            // a BarrierMote doesn't combine — it ISOLATES the other particle
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

            // SAME + SAME = LEVEL UP. (Flames are manifests, not levels — they
            // pool bigger. Push+Push becomes the tornado in the vectors phase.)
            if (fa == fb)
            {
                // a FLAME is a manifest, not a level — it EATS its kin and grows
                if (a.Kind == ParticleKind.Flame || b.Kind == ParticleKind.Flame)
                {
                    var flame = a.Kind == ParticleKind.Flame ? a : b;
                    flame.Absorb(flame == a ? b : a);
                    return;
                }
                if (fa == ParticleKind.Push)
                {
                    // VECTORS (Marko's law 7): Arrow+Arrow = TORNADO, Y+Y =
                    // WHIRLPOOL. The lineage bit knows which rune threw each
                    // push; a mixed arrow+Y pair is undefined on purpose —
                    // velocities just pool.
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
                            down: bothToward, a.Lineage | b.Lineage);
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

            // OPPOSITES SYNTHESIZE a paradox object — never "nothing happens"
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
                RuneGrammar.TryDemon(xlin, xat, Mathf.Max(a.SrcSize, b.SrcSize));
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
            // BOTH ingredients announce themselves at the meeting (Marko:
            // "tell exactly what's interacting with what — remove ambiguity")
            a.ImpactFx(); b.ImpactFx();
            int la = EffLevel(a), lb = EffLevel(b);
            var hi = la >= lb ? a : b;
            var lo = hi == a ? b : a;

            if (Mathf.Max(la, lb) >= 3) { hi.Absorb(lo); return; } // capped: eats its kin

            // pool payload + ancestry into the survivor
            hi.Lineage |= lo.Lineage;
            hi.Temp += lo.Temp; hi.Lum += lo.Lum; hi.Density += lo.Density; hi.Stick += lo.Stick;
            // mismatched levels: the WEAKER half rules the product (Marko's law
            // 6) — equals pool their power instead
            hi.Power = la != lb
                ? Mathf.Min(3f, Mathf.Min(hi.Power, lo.Power) * 1.25f)
                : Mathf.Min(3f, hi.Power + lo.Power * 0.5f);
            hi.SrcSize = Mathf.Max(hi.SrcSize, lo.SrcSize);

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
                case ParticleKind.Light: BecomeLightning(); return;      // Light+Light = LIGHTNING (Marko)
                case ParticleKind.Dark: BecomeBlackHole(); return;       // Dark+Dark = BLACK HOLE
                case ParticleKind.Glue:
                    _settled = true; // lvl2 grip doesn't move — and won't let go
                    break;
            }
            transform.localScale = Vector3.one * 0.26f;
            // the aura ANNOUNCES its reach (Marko: "things start burning
            // without it even being close visually") — a faint ground ring
            var reach = GrammarFX.GroundRing(transform, new Color(1f, 1f, 1f, 0.35f));
            reach.localScale = Vector3.one * (DrawingConfig.Lvl2AuraRadius / 0.26f);
            RefreshLook();
        }

        /// lvl3 — the ULTIMATES (each element taken to its absolute). Returns
        /// the created field so the sustain law can wait on it (a flame burst
        /// is instantaneous — returns null, the runes re-arm at once).
        static Object Ultimate(ParticleKind family, Vector3 at, float power, float srcSize, ulong lineage)
        {
            RuneGrammar.TryDemon(lineage, at, srcSize);
            switch (family)
            {
                case ParticleKind.Spark: GrammarFX.FlameBurst(at, power); return null;
                case ParticleKind.Frost: return SnowField.Open(at, power);
                case ParticleKind.Light: return PlasmaField.Open(at, power);
                case ParticleKind.Dark: return BlackHoleField.Open(at, power, growing: true);
                case ParticleKind.Glue: return TimeFreezeField.Open(at, power);
                case ParticleKind.Repel: return InertiaField.Open(at, power);
            }
            return null;
        }

        /// Opposites make PARADOX objects — the tension embodied, never a cancel.
        static void Synthesize(SpellParticle a, SpellParticle b, ParadoxKind paradox)
        {
            a.ImpactFx(); b.ImpactFx(); // the two OPPOSITES flash their colors as they refuse
            Vector3 at = (a.transform.position + b.transform.position) * 0.5f;
            ulong lineage = a.Lineage | b.Lineage;
            float power = (a.Power + b.Power) * 0.5f;
            float srcSize = Mathf.Max(a.SrcSize, b.SrcSize);
            bool big = EffLevel(a) >= 2 && EffLevel(b) >= 2; // lvl2 × lvl2 = the AREA version
            RuneGrammar.TryDemon(lineage, at, srcSize);

            switch (paradox)
            {
                case ParadoxKind.Steam:
                {
                    // heat + chill = the ONE gas substance (Marko: one substance,
                    // one behavior) — born big per his Aug 4 area ruling
                    var steam = FormConjures.SpawnSteam(at + Vector3.up * 0.3f,
                        big ? 1.6f : 0.9f, lineage);
                    DrawingWorld.Instance?.LogEvent("fire and frost REFUSE each other — SCALDING STEAM");
                    a.BecameObj = steam; b.BecameObj = steam; // sustain law: runes wait on the cloud
                    a.Die(); b.Die();
                    return;
                }
                case ParadoxKind.WhiteHole:
                {
                    var f = WhiteHoleField.Open(at, power * (big ? 1.6f : 1f));
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
            SrcSize = Mathf.Max(SrcSize, food.SrcSize);
            Lineage |= food.Lineage; // ancestry rides EVERY combination
            food.BecameObj = this;   // the food's rune now waits on ME (sustain law)
            _settled = false; // fresh attributes knock it loose
            transform.localScale = Vector3.one * Mathf.Min(0.45f, transform.localScale.x * 1.18f);
            // spread on EITHER side multiplies (the old food-only gate killed
            // Dark+Spread and Light+Spread and coin-flipped the level-2 pairs)
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
        /// Dense = MANIFEST (Marko's law): the Dense payload turns an essence
        /// particle into its persistent physical object. The old attribute
        /// ladders (light+dense→lightning etc.) are gone — leveling took their
        /// place (Light+Light=lightning); those pairings become exotics in P3.
        void CheckTransform()
        {
            if (Kind == ParticleKind.Spark && Density >= PlasmaDensity) { BecomeFlame(); return; }
            if (Kind == ParticleKind.Frost && Density >= PlasmaDensity) BecomeSnowball();
        }

        /// HeatUp+Dense — FLAME: a persistent fire that stays where it lands,
        /// burns what's near, and merges with other flames into bigger flames.
        void BecomeFlame()
        {
            Kind = ParticleKind.Flame;
            GrammarLevel = Mathf.Max(GrammarLevel, 1);
            Temp = Mathf.Max(Temp, 60f);
            transform.localScale = Vector3.one * 0.3f;
            Vel *= 0.2f;
            DrawingWorld.Instance?.LogEvent("the fire TAKES — a flame");
            RefreshLook();
        }

        /// HeatDown+Dense — SNOWBALL: real matter, cold, rolls, merges via
        /// Matter's own chemistry; passively chills its surroundings (Matter side).
        void BecomeSnowball()
        {
            var m = Matter.Spawn(SurfaceMaterialType.Water, MatterPhase.Solid,
                0.3f * Mathf.Max(1f, Power), transform.position);
            m.Temperature = -30f;
            if (m.TryGetComponent<Rigidbody>(out var rb))
                rb.linearVelocity = Vel + Vector3.down * 1f;
            DrawingWorld.Instance?.LogEvent("the cold TAKES — a snowball");
            m.Lineage = Lineage; // the demon chain survives the snowball
            BecameObj = m;       // the rune waits for the snowball to be gone
            Die();
        }

        /// Dark+Dark — BLACK HOLE: a particle that pulls things in. Feed it
        /// more darkness and it goes lvl3: the GROWING black hole area.
        void BecomeBlackHole()
        {
            Kind = ParticleKind.BlackHole;
            Lum = Mathf.Min(Lum, -1.5f);
            _settled = false;
            transform.localScale = Vector3.one * 0.4f;
            var l = GetComponent<Light>();
            if (l != null) Destroy(l);
            DrawingWorld.Instance?.LogEvent("the dark COLLAPSES — black hole");
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

        /// lvl2 particles RADIATE: they do their effect to everything around
        /// them on a beat (Marko: "increases in size and burns things around").
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
                    // limb capsules ride the same player — the ROOT pays, once
                    if (c.attachedRigidbody != null) continue;
                    if (Spares(pl.transform)) continue; // the holder's own aura spares them
                    // auras only push sliders — the board does the rest
                    var plBoard = BodyState.Of(pl);
                    if (Mathf.Abs(Temp) > 25f) plBoard?.PushTemp(Temp * 0.03f);
                    // a friend's LIGHT washes the darkness off you (Marko's cure)
                    var auraFam = RuneGrammar.Family(Kind);
                    if (auraFam == ParticleKind.Light) plBoard?.PushLum(0.12f);
                    else if (auraFam == ParticleKind.Dark) plBoard?.PushLum(-0.1f);
                    continue;
                }
                if (Kind == ParticleKind.Flame || RuneGrammar.Family(Kind) == ParticleKind.Spark)
                    GiveHeat(c, 50f * Power);  // lvl2 radiance BURNS (Marko: combinations many times stronger)
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
            DrawingWorld.Instance?.LogEvent("the light CONDENSES — lightning");
            RefreshLook();
        }

        void TickLightning(float dt)
        {
            Vel = Vector3.Lerp(Vel, Random.insideUnitSphere * 2.2f, 0.2f); // erratic crackle-drift
            transform.position += Vel * dt;
            _strikeTick -= dt;
            if (_strikeTick <= 0f) { _strikeTick = 0.75f; Strike(); }
        }

        /// Lightning strikes the HIGHEST thing nearby, randomly (Marko's rule).
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
                // "highest, RANDOMLY" — a big dice roll so it doesn't just
                // bully the tallest thing in sight (usually the player)
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
            // BARRIER (Marko's ruling): isolation is two-way — the particle
            // can't reach what's inside, so it just bounces off the shell
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

            // ANY player collider counts — in third person a FOOT capsule can
            // reach a ground patch the root capsule never overlaps (Marko's
            // bug: sticky/slick patches dead in 3rd person). TouchPlayer's
            // branches all self-throttle, so limb+root double-events are safe.
            var pilot = c.GetComponentInParent<SimpleFPSController>();
            if (pilot != null) { TouchPlayer(pilot); return; }

            // the DEMON absorbs anything that touches it — it BECOMES the last
            // element it ate, so even a fireball is food, not a hit
            var demon = c.GetComponentInParent<Demon>();
            if (demon != null) { demon.AbsorbParticle(this); BecameObj = demon; Die(); return; }

            var m = c.GetComponent<Matter>();
            var creature = c.GetComponentInParent<Creature>();
            var rb = c.attachedRigidbody;

            // bare world geometry (floor/walls): SETTLE instead of dying — a
            // spark on the ground is a waiting ember, a light is a torch, a
            // dark spot is a trap. A push particle can knock them loose again.
            if (m == null && creature == null && rb == null)
            {
                // a push BOUNCES off bare rock instead of dying — it still
                // owes the seal a tornado (Marko: arrows attract and combine;
                // dying on the first cobble bump prevented every entwine)
                if (Kind == ParticleKind.Push)
                {
                    Vel = Vector3.Reflect(Vel, Vector3.up) * 0.7f;
                    return;
                }
                // lvl2 slip CANNOT be stopped by anything — it bounces (Marko)
                if (GrammarLevel >= 2 && Kind == ParticleKind.Repel)
                {
                    Vel = Vector3.Reflect(Vel, Vector3.up) + Random.insideUnitSphere * 1.5f;
                    return;
                }
                if (Claimed && Vel.sqrMagnitude > 9f && FxLibrary.I != null) // a thrown thing LANDS
                    FxLibrary.Spawn(FxLibrary.I.GroundHit, transform.position);
                // EVERY landed mote is a PATCH (Marko: walking over a chill
                // mote must DO something) — the touch zone widens to real-boot
                // size, or feet walk clean over a 7cm mote and nothing happens
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
                // (no per-beat ImpactFx — a resident flame bursting every half
                // second was most of the chain-lag; the idle look carries it)
                return;
            }

            Donate(c, m, creature, rb);
            ImpactFx(); // the payload LANDS visibly (Marko's juice pass)

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
            // a held spell never bites its holder; a released one spares the
            // thrower for 0.2s while it gets clear (Marko's rule)
            if (Spares(pilot.transform)) return;
            if (Barrier.Protects(pilot)) // barriered player: nothing gets in
            {
                Vel = Vector3.Reflect(Vel, (transform.position - pilot.transform.position).normalized) * 0.6f;
                return;
            }
            var board = BodyState.Of(pilot); // the slider board takes it from here
            if (Kind == ParticleKind.Flame) // standing in fire is a CHOICE
            {
                _donateTick -= Time.deltaTime;
                if (_donateTick > 0f) return;
                _donateTick = 0.7f;
                board?.PushTemp(Mathf.Max(6f, Temp * 0.05f)); // fire only HEATS — the band does the hurting
                return;
            }
            // lvl2 grip is ULTIMATE GLUE (Marko): touch it and you're STUCK
            // where you stand — and the glue never dies to a touch (it won't
            // let go). The carrier is spared by the guard at the top.
            if (Kind == ParticleKind.Glue && GrammarLevel >= 2)
            {
                // ultimate glue SLOWS you into a deep shuffle — never a full
                // stop (Marko: only the TIME FREEZE may hold you at zero)
                bool was = board != null && board.Grip > BodyState.GripSlowAt;
                board?.PushGrip(0.9f);
                if (!was && FxLibrary.I != null) // comic beat — first stick only
                    FxLibrary.Spawn(FxLibrary.I.TextBoing, pilot.transform.position + Vector3.up * 2f);
                return;
            }
            // GROUND PATCHES (Marko's bug): a settled sticky mote is a glue
            // spot, a settled slick one is a soap spot — stepping on them ACTS
            // and the patch survives the step
            if (_settled && (Kind == ParticleKind.Glue || Kind == ParticleKind.Repel))
            {
                // own timer — objects donating on the patch must not starve
                // the player effect (shared _donateTick did exactly that)
                _patchTick -= Time.deltaTime;
                if (_patchTick > 0f) return;
                _patchTick = 0.55f;
                if (Kind == ParticleKind.Glue)
                {
                    board?.PushGrip(0.5f); // glue spot: grip climbs toward planted
                }
                else
                {
                    // soap spot: grip drains toward skating — the board's deep-
                    // slick roulette owns the ragdolls now
                    board?.PushGrip(-0.55f);
                    Vector3 v = pilot.Velocity; v.y = 0f;
                    if (v.sqrMagnitude > 0.2f) pilot.TakeHit(v.normalized * 3.2f, 0f); // momentum keeps you
                }
                return;
            }
            // THE VECTOR LAW (Marko's final formula): amplify-or-invert your
            // current velocity, plus the mote's own add along its TRAVEL.
            // An arrow powers you up; a Y reverts you — hit while falling,
            // you fly UP. (Sign of the Y's own add = his test flag.)
            if (Kind == ParticleKind.Push)
            {
                Vector3 travel = Vel.sqrMagnitude > 0.01f ? Vel.normalized : transform.forward;
                Vector3 vNow = pilot.Velocity;
                // CATAPULT, not grandma (Marko: "if I'm standing in place I
                // want to be shoved away for real") — the arrow's own kick is
                // a launch; the amplify term stacks on top when you're moving
                Vector3 impulse = (IsY ? -vNow * 1.6f : vNow * 0.6f) + travel * (15f * Power);
                pilot.TakeHit(impulse, 0f);
                board?.PushMove((IsY ? -0.8f : 0.8f) * Power); // and the slider remembers
                ImpactFx();
                Die();
                return;
            }
            // friendly fire stays ON — your own magic bills you (exactness!)
            if (Kind == ParticleKind.Dark)
            {
                board?.PushLum(-0.45f); // darkness steals sight
                board?.PushTemp(-4f); // and chills (Marko: dark moves the heat slider down)
                Die(); return;
            }
            if (Kind == ParticleKind.Light)
            {
                board?.PushLum(0.5f); // see better — or glare, if someone overdoes it
                board?.PushTemp(3f);  // light warms (his coupling)
                Die(); return;
            }
            if (Kind == ParticleKind.Dense) { board?.PushWeight(0.45f * Power); Die(); return; }
            if (Kind == ParticleKind.Spread) { board?.PushWeight(-0.45f * Power); Die(); return; }
            if (Mathf.Abs(Temp) > 12f) board?.PushTemp(Temp * 0.3f); // a mote is FELT (Marko: underwhelming at 0.12)
            if (Mathf.Abs(Stick) > 0.4f) board?.PushGrip(Stick * 0.9f); // flying glue grips, flying soap strips
            ImpactFx();
            Die();
        }

        /// Level 5 — the world absorbs everything: the payload becomes real
        /// temperature, light, blindness, weight, glue, and shove.
        void Donate(Collider c, Matter m, Creature creature, Rigidbody rb)
        {
            // GRAMMAR v4: matter is part of the chain — a particle donating
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
                TryWeld(c); // two recently-glued things → JOINED
                // ULTIMATE GLUE (Marko): whatever TOUCHES the lvl2 grip is
                // stuck — welded to the world where it stands (the glue
                // itself never moves; creatures use their own stuck system)
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
                if (m != null) m.AddDensity(0.6f * Density);
                else if (creature != null) Resize(creature, Density > 0f ? 1.12f : 0.88f);
                else if (rb != null) rb.mass = Mathf.Max(0.05f, rb.mass * (1f + 0.28f * Density));
            }

            if (Kind == ParticleKind.Push && rb != null)
            {
                // THE VECTOR LAW for everything else too: amplify-or-invert
                // what it's doing + the mote's own add along its travel —
                // a thrown crate met by a Y comes BACK
                Vector3 travel = Vel.sqrMagnitude > 0.01f ? Vel.normalized : transform.forward;
                Vector3 impulse = (IsY ? -rb.linearVelocity * 1.6f : rb.linearVelocity * 0.6f)
                    + travel * (18f * Power); // CATAPULT things (Marko) — crates properly fly
                rb.AddForce(impulse, ForceMode.VelocityChange);
                if (creature != null)
                    BodyState.Of(creature)?.PushMove((IsY ? -0.8f : 0.8f) * Power); // moonwalk fuel
            }
        }

        /// Dense-fed creatures grow heavy; spread-fed ones shrink — and a small
        /// enough zombie is lighter than air (Marko: eventually they FLY).
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

            // a PLAYER'S temperature lives on the BODY BOARD, full stop —
            // giving the player a Thermal made a SECOND thermometer that
            // freeze-damaged a phantom Damageable every beat (the 3.4e38-hp
            // log spam). Route heat to the slider and stop.
            var pilot = c.GetComponentInParent<SimpleFPSController>();
            if (pilot != null)
            {
                BodyState.Of(pilot)?.PushTemp(delta * 0.12f); // same scale as mote touches
                return;
            }
            var creature = c.GetComponentInParent<Creature>();
            if (creature != null) go = creature.gameObject;

            // don't cook giant static surfaces — same guard the old zones used
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
                // the ladder: lvl1 tears under strain, lvl2 holds hard —
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

        /// MARKO'S FX OVERRIDE: drop a prefab named FX_<Kind> (FX_Spark,
        /// FX_Frost, FX_Flame, FX_Lightning, FX_BlackHole…) into
        /// Resources/Custom and it BECOMES that particle's look — the code
        /// sphere hides, your prefab rides the particle. Zero code changes.
        void EnsureCustomLook()
        {
            if (_customLook != null && _customFor == Kind) return;
            if (_customLook != null) Destroy(_customLook);
            _customLook = null;
            var skin = PrefabVault.Get("FX_" + Kind);
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

        /// EVERY MOTE STANDS OUT (Marko's juice pass): each kind wears a small
        /// looping CFXR effect while it lives — fire actually flickers, light
        /// actually glows. Dies with the mote (child), swaps on kind change.
        void AttachIdleFx()
        {
            var old = transform.Find("IdleFx");
            if (old != null) Destroy(old.gameObject);
            // AXIOM: if HIS FX_<Kind> prefab is in play, it owns the look —
            // don't staple a CFXR effect onto his art. (Destroy runs first so
            // a stale IdleFx can't ride along after a kind change.)
            if (_customLook != null) return;
            var lib = FxLibrary.I;
            if (lib == null) return;
            GameObject pick = null;
            float scale = 3.2f; // motes are ~0.14 scale — children inherit it
            var fam = RuneGrammar.Family(Kind);
            // lvl1 heat carries NO idle flame (Marko: "too large — it just
            // needs juice when it hits, like chill") — real fire is for the
            // Flame fixture and lvl2+ radiance
            // TRIMMED (Marko: the star/shine storms were clutter + lag) — only
            // the two kinds whose IDENTITY is "actively burning energy" wear a
            // looping effect; everyone else speaks through delivery juice.
            if (Kind == ParticleKind.Flame || (fam == ParticleKind.Spark && GrammarLevel >= 2))
                pick = lib.Fire;
            else if (Kind == ParticleKind.Lightning) pick = lib.ElectricHit;
            if (pick == null) return;
            var fx = Instantiate(pick, transform.position, Quaternion.identity, transform);
            fx.name = "IdleFx";
            fx.transform.localScale *= scale; // *= keeps an authored prefab scale
        }

        void RefreshLook()
        {
            if (_rend == null) _rend = GetComponent<Renderer>();
            if (_rend == null) return;
            EnsureCustomLook();   // resolve HIS art FIRST — AttachIdleFx checks it
            AttachIdleFx();
            if (_customLook != null) return; // your art owns the look now
            Color c;
            MoteShade shade = MoteShade.Additive;
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
            // carried darkness dims ANY particle — the invisible flame is also
            // hard for PLAYERS to see, which is only fair
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

    /// ONE nearest-prey scan for every stalker (dedupe: TickLure, DarkFlame,
    /// DarkMatterMote pasted this loop verbatim). bestSqr rides by ref so a
    /// caller can keep competing against its own extra candidates.
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
