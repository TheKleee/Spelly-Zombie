using System.Collections.Generic;
using UnityEngine;

namespace SpellyZombie
{
    /// THE BODY IS MATTER : sliders live on
    /// every body, spells only PUSH them - damage, slow, inverted controls,
    /// floating, weight gates and vision are all READINGS derived each tick.
    /// The environment drifts every slider home: walking away from the fire
    /// IS the cure.
    ///
    ///   Temp   - THE damage band. Out of band = DPS by offset; deep cold =
    ///            frozen solid (the body stops entirely).
    ///   Lum    - VISION ONLY, never damage: darkness creeps in low, the
    ///            world blooms high.
    /// Grip   - too grippy = slowed  planted; too slick = skating  the
    ///            floor wins (ragdoll).
    ///   Weight - light = higher jumps, slower falls, then FLOAT; heavy =
    ///            movement gates (no sprint, then crouch-crawl only).
    ///   Move   - dead-center normal; + = arrow speed buff; − = Y-owned:
    ///            inputs INVERTED at the Y's amplitude.
    ///
    /// On PLAYERS the whole board runs. On CREATURES only Grip/Weight/Move
    /// run - their temperature already lives on Thermal (Frozen/Burning),
    /// and two thermometers would fight.
    public class BodyState : MonoBehaviour
    {
        // ---- naturals, bands, thresholds - the survival game tunes HERE ----
        // AXIOM : `const` INLINES at every call site, so a
        // tuning file could never reach these — the header says "the survival
        // game tunes HERE" and it was lying. `static readonly` + the
        // sz_tuning.json overlay makes every number changeable with no rebuild.
        public static readonly float NaturalTemp = Tune("BodyNaturalTemp", 37f);
        public static readonly float TempBandLow = Tune("BodyTempBandLow", 15f);
        public static readonly float TempBandHigh = Tune("BodyTempBandHigh", 45f);
        public static readonly float FrozenSolidAt = Tune("BodyFrozenSolidAt", -25f);
        public static readonly float TempDriftPerSec = Tune("BodyTempDriftPerSec", 2.4f);
        public static readonly float TempDamagePerDegree = Tune("BodyTempDamagePerDegree", 0.10f);

        public static readonly float NaturalLum = Tune("BodyNaturalLum", 0.55f);   // daylight — torches sit INSIDE the band
        public static readonly float LumDriftPerSec = Tune("BodyLumDriftPerSec", 0.4f);

        public static readonly float GripDriftPerSec = Tune("BodyGripDriftPerSec", 0.45f);
        public static readonly float GripSlowAt = Tune("BodyGripSlowAt", 0.45f);
        public static readonly float GripStuckAt = Tune("BodyGripStuckAt", 1.15f);
        public static readonly float SlickSlideAt = Tune("BodySlickSlideAt", -0.45f);
        public static readonly float SlickDeepAt = Tune("BodySlickDeepAt", -1.4f); // full soap-hell depth

        public static readonly float WeightDriftPerSec = Tune("BodyWeightDriftPerSec", 0.30f);
        public static readonly float FloatBelow = Tune("BodyFloatBelow", 0.35f);   // REALLY light: you float
        public static readonly float FloatMaxSeconds = Tune("BodyFloatMaxSeconds", 5f); // ...for THIS long, however many spells stack
        public static readonly float RunLimit = Tune("BodyRunLimit", 1.55f);       // sprint refuses above
        public static readonly float WalkLimit = Tune("BodyWalkLimit", 2.4f);      // crouch-crawl only above

        public static readonly float MoveDriftPerSec = Tune("BodyMoveDriftPerSec", 0.45f);

        public static readonly float DriftExpo = Tune("BodyDriftExpo", 3f);

        static float Tune(string key, float def) => DrawingConfig.Overlay(key, def);

        /// Recovery speeds up with the size of the effect: a slider pushed to
        /// its extreme sheds up to DriftExpo times faster than a light touch,
        /// so stacked spells cannot leave you stranded for long.
        static float Rush(float dev, float span) =>
            Mathf.Max(1f, Mathf.Abs(dev) / span * DriftExpo);

        // ---- the sliders ----
        public float Temp = NaturalTemp;
        public float Lum = NaturalLum;
        public float Grip;          // 0 natural · + sticky · − slick
        public float Weight = 1f;   // mass multiplier
        public float Move;          // 0 center · + arrow buff · − Y inversion

        /// What your arms hold right now ("their weight is added on
        /// your weight so you might move slower") — the gates are the
        /// strength limit, no artificial carry cap.
        public float CarriedWeight;
        public float TotalWeight => Weight + CarriedWeight;

        SimpleFPSController _pilot;
        Creature _creature;
        float _hurtCarry;   // sub-point band damage lands in readable chunks
        float _floatFor;    // seconds of continuous floating - the balloon clock
        bool _floatSpent;   // budget used: no hover, full gravity, until real recovery
        float _slipTick;    // ragdoll-roulette beat while deep slick

        /// Resolve the body a collider belongs to, adding the board on first
        /// contact with the slider law (players and creatures self-heal one).
        public static BodyState Of(Component c)
        {
            if (c == null) return null;
            var found = c.GetComponentInParent<BodyState>();
            if (found != null) return found;
            Component host = c.GetComponentInParent<SimpleFPSController>();
            if (host == null) host = c.GetComponentInParent<Creature>();
            return host == null ? null : host.gameObject.AddComponent<BodyState>();
        }

        void Awake()
        {
            _pilot = GetComponent<SimpleFPSController>();
            _creature = GetComponent<Creature>();
        }

        // ---- pushes (the ONLY thing spells are allowed to do) ----
        public void PushTemp(float d)
        {
            if (_creature != null) // creatures: Thermal owns temp - route there
            {
                var col = GetComponentInChildren<Collider>();
                if (col != null) SpellParticle.GiveHeatTo(col, d);
                return;
            }
            Temp = Mathf.Clamp(Temp + d, -60f, 160f);
        }
        public void PushLum(float d) => Lum = Mathf.Clamp(Lum + d, -1.5f, 3f);
        public void PushGrip(float d) => Grip = Mathf.Clamp(Grip + d, -1.4f, 1.6f);
        public void PushWeight(float d) => Weight = Mathf.Clamp(Weight + d, 0.12f, 4f);
        public void PushMove(float d) => Move = Mathf.Clamp(Move + d, -2.2f, 2.2f);

        /// Resets every slider to natural. Used by the sky catch.
        public void ClearSpellEffects()
        {
            Temp = NaturalTemp;
            Lum = NaturalLum;
            Grip = 0f;
            Weight = 1f;
            Move = 0f;
            _floatFor = 0f;
            _floatSpent = false;
        }

        // ---- readings ----
        public bool FrozenSolid => _pilot != null && Temp <= FrozenSolidAt;
        // the screen answers EARLY
        // creep starts near natural temp; the DAMAGE band is unchanged
        public float BurnSeverity => Mathf.Clamp01((Temp - 41f) / 42f);
        public float FreezeSeverity => Mathf.Clamp01((33f - Temp) / 42f);
        /// 0 = normal sight · 1 = pitch black (this IS the vision reduction)
        public float DarknessSeverity => Mathf.Clamp01((NaturalLum - Lum) / (NaturalLum + 0.55f));
        public float BloomSeverity => Mathf.Clamp01((Lum - 1.4f) / 1.4f);

        public bool Floating => TotalWeight <= FloatBelow && !_floatSpent;
        public bool CanSprint => TotalWeight < RunLimit;
        public bool CrawlOnly => TotalWeight >= WalkLimit;
        /// Lighter bodies spring higher and fall softer; heavy is gated, not nerfed.
        public float JumpMul => TotalWeight < 1f ? Mathf.Lerp(1.5f, 1f, TotalWeight) : 1f;
        /// A spent float budget also cancels light-body gravity scaling.
        public float GravityMul => _floatSpent ? 1f
            : TotalWeight < 1f ? Mathf.Max(0.18f, TotalWeight) : 1f;

        /// −1 while a Y owns you: your inputs walk you the other way.
        public float InputSign => Move < -0.05f ? -1f : 1f;

        /// One multiplier from the whole board - players and zombies both.
        /// NOTHING here ever returns zero ("the only thing that's so
        /// strong that it keeps you from moving at all is the time freeze") —
        /// grip and cold slow you PROPORTIONALLY, down to a pitiful shuffle.
        public float SpeedMul
        {
            get
            {
                float m = 1f;
                if (Grip > GripSlowAt)
                    m *= Mathf.Lerp(1f, 0.09f, Mathf.InverseLerp(GripSlowAt, 1.5f, Grip));
                m *= Mathf.Lerp(1f, 0.3f, FreezeSeverity);  // being frozen IS the slow
                if (FrozenSolid) m *= 0.15f;                // frozen stiff: a shuffle, never a statue
                if (Move > 0.05f) m *= 1f + Move * 0.5f;    // arrow buff
                else if (Move < -0.05f)
                    m *= Mathf.Clamp(-Move, 0.15f, 1.3f);   // small Y = slow backwards walk
                return m;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // ---- the drift home: the environment IS the cure ----
            if (_pilot != null)
            {
                Temp = Mathf.MoveTowards(Temp, NaturalTemp, TempDriftPerSec * Rush(Temp - NaturalTemp, 40f) * dt);
                Lum = Mathf.MoveTowards(Lum, NaturalLum, LumDriftPerSec * Rush(Lum - NaturalLum, 1.5f) * dt);
            }
            Grip = Mathf.MoveTowards(Grip, 0f, GripDriftPerSec * Rush(Grip, 1.5f) * dt);
            Weight = Mathf.MoveTowards(Weight, 1f, WeightDriftPerSec * Rush(Weight - 1f, 0.9f) * dt);
            Move = Mathf.MoveTowards(Move, 0f, MoveDriftPerSec * Rush(Move, 2.2f) * dt);

            // Floating is a spendable budget, not a per-hit state: stacked
            // Spread motes re-pin the weight every frame, so a timer that
            // resets on each hit would never expire.
            if (TotalWeight <= FloatBelow)
            {
                if (!_floatSpent)
                {
                    _floatFor += dt;
                    if (_floatFor > FloatMaxSeconds)
                    {
                        _floatSpent = true;
                        if (_pilot != null && _pilot.IsLocalViewer)
                            DrawingWorld.Instance?.LogEvent("your body remembers its weight");
                    }
                }
            }
            else { _floatSpent = false; _floatFor = 0f; } // recovered: budget refills

            // ---- temp band damage (players only - Thermal burns creatures) ----
            if (_pilot != null && !_pilot.IsDead)
            {
                float off = Temp < TempBandLow ? TempBandLow - Temp
                    : Temp > TempBandHigh ? Temp - TempBandHigh : 0f;
                if (off > 0f)
                {
                    _hurtCarry += off * TempDamagePerDegree * dt;
                    if (_hurtCarry >= 0.75f)
                    {
                        _pilot.TakeHit(Vector3.zero, _hurtCarry,
                            Temp < NaturalTemp ? "freezing" : "burning");
                        _hurtCarry = 0f;
                    }
                }
            }

            // ---- slick: the floor wins PROPORTIONALLY ("if slippery
            // is weak you might barely ragdoll a bit, extremely strong you'll
            // ragdoll way more") — depth drives the odds, the pace, and how
            // long you eat cobblestone
            if (Grip < SlickSlideAt)
            {
                float depth = Mathf.InverseLerp(SlickSlideAt, SlickDeepAt, Grip); // 0 faint … 1 soap hell
                _slipTick -= dt;
                if (_slipTick <= 0f)
                {
                    _slipTick = Mathf.Lerp(1.7f, 0.75f, depth);
                    if (Random.value < Mathf.Lerp(0.08f, 0.75f, depth))
                    {
                        float sprawl = Mathf.Lerp(0.45f, 1.6f, depth);
                        if (_pilot != null) _pilot.KnockDown(sprawl);
                        else _creature?.KnockDown(sprawl);
                    }
                }
            }

            // ---- creatures read the shared sliders through their own laws ----
            if (_creature != null)
            {
                if (Grip >= GripStuckAt) _creature.ApplyStuck(0.3f);
                else if (Grip <= SlickSlideAt) _creature.ApplySlip(0.3f);
            }

            if (_pilot != null) UpdateBodyFx(); // the body wears its damage
        }

        // ---- the BODY shows it ("small flames over your body
        // depending of how much you're burning") — allies read your state at
        // a glance. And the cure is already in the sliders: a chill mote on a
        // burning friend pushes their temp back toward the band.
        readonly GameObject[] _bodyFlames = new GameObject[3];
        readonly GameObject[] _eyeWisps = new GameObject[2];
        readonly GameObject[] _eyeGlares = new GameObject[2];
        float _iceFxTick, _bleedTick;

        /// The body's REAL eye height - the fallback anchor when a model has
        /// no FX sockets at all (the graybox bean).
        float EyeY => _pilot != null && _pilot.CameraPivot != null
            ? Mathf.Max(0.4f, _pilot.CameraPivot.localPosition.y) : 1.5f;

            // ---- FX SOCKETS ON THE BONES : put empties named
        // Socket_Burn / Socket_Freeze / Socket_Bleed / Socket_Eyes anywhere
        // on the model - several of each is fine (Socket_Burn on the chest,
        // another on a shoulder). Effects spawn AS CHILDREN, so they ride the
        // bones through animation and ragdoll instead of floating at root
        // offsets. A model with NO sockets gets code fallbacks on its bones
        // (humanoid) or at eye-height fractions (the bean) - the empties
        // always win over the guesses.
        readonly List<Transform> _burnS = new List<Transform>();
        readonly List<Transform> _freezeS = new List<Transform>();
        readonly List<Transform> _bleedS = new List<Transform>();
        readonly List<Transform> _eyeS = new List<Transform>();
        float _socketScan;

        void ResolveSockets()
        {
            // bodies build/rebuild at runtime (CharacterRig, bakes) - rescan
            // when stale, at most every 2s
            bool stale = _burnS.Count == 0 || _burnS[0] == null;
            if (!stale || Time.time < _socketScan) return;
            _socketScan = Time.time + 2f;
            _burnS.Clear(); _freezeS.Clear(); _bleedS.Clear(); _eyeS.Clear();

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Socket_Burn")) _burnS.Add(t);
                else if (t.name.StartsWith("Socket_Freeze")) _freezeS.Add(t);
                else if (t.name.StartsWith("Socket_Bleed")) _bleedS.Add(t);
                else if (t.name.StartsWith("Socket_Eyes")) _eyeS.Add(t);
            }
            // authored sockets win; fallbacks fill only the EMPTY categories -
            // ON THE BONES THEMSELVES ("what does matter is that they
            // are not offset from the body so it looks like it's affecting
            // you"). the approved parents: spine, chest, head, shoulders,
            // hips, legs - NEVER the arms (their bones point the wrong way).
            // Each socket sits at its bone with a small forward nudge so the
            // effect burns on the skin, not inside the ribs.
            var anim = GetComponentInChildren<Animator>();
            Transform B(HumanBodyBones b) =>
                anim != null && anim.isHuman ? anim.GetBoneTransform(b) : null;
            Vector3 fwd = transform.forward;
            Vector3 root = transform.position;
            float e = EyeY;

            Transform Mk(string n, Transform bone, float beanY)
            {
                var go = new GameObject(n);
                go.transform.SetParent(bone != null ? bone : transform, false);
                // world position: at the bone (or a body-height fraction on
                // the bean), pushed slightly out the character's front
                go.transform.position = bone != null
                    ? bone.position + fwd * 0.07f
                    : root + Vector3.up * (beanY * e) + fwd * (0.09f * e);
                return go.transform;
            }

            var chest = B(HumanBodyBones.Chest);
            var spine = B(HumanBodyBones.Spine);
            var head = B(HumanBodyBones.Head);
            var hips = B(HumanBodyBones.Hips);
            var shoulder = B(HumanBodyBones.RightShoulder);

            if (_burnS.Count == 0)
            {
                _burnS.Add(Mk("Socket_Burn_Auto", chest, 0.62f));
                _burnS.Add(Mk("Socket_Burn_Auto2", spine, 0.45f));
                // third flame high - shoulder first, head only as its stand-in
                _burnS.Add(Mk("Socket_Burn_Auto3", shoulder != null ? shoulder : head, 0.8f));
            }
            if (_freezeS.Count == 0)
            {
                _freezeS.Add(Mk("Socket_Freeze_Auto", chest, 0.55f));
                _freezeS.Add(Mk("Socket_Freeze_Auto2", hips, 0.35f));
            }
            if (_bleedS.Count == 0)
            {
                _bleedS.Add(Mk("Socket_Bleed_Auto", spine, 0.5f));
                _bleedS.Add(Mk("Socket_Bleed_Auto2", chest, 0.62f));
            }
            if (_eyeS.Count == 0)    // the one legitimate face socket
            {
                var go = new GameObject("Socket_Eyes_Auto");
                go.transform.SetParent(head != null ? head : transform, false);
                go.transform.localPosition = head != null
                    ? new Vector3(0f, 0.04f, 0.09f) : new Vector3(0f, e, 0.1f * e);
                _eyeS.Add(go.transform);
            }
        }

        /// CFXR prefabs ignore a small localScale unless their particle
        /// systems scale with the hierarchy - force it, then shrink for real.
        /// Sockets live on BONES, and bones carry rig scale (FBX armatures are
        /// often 0.01, baked parts can be anything) - cancel the parent's
        /// scale so the WORLD size is the size we asked for, on every rig.
        static GameObject Fit(GameObject fx, float scale)
        {
            if (fx == null) return null;
            var p = fx.transform.parent != null ? fx.transform.parent.lossyScale : Vector3.one;
            var s = fx.transform.localScale * scale;
            fx.transform.localScale = new Vector3(
                s.x / Mathf.Max(0.0001f, Mathf.Abs(p.x)),
                s.y / Mathf.Max(0.0001f, Mathf.Abs(p.y)),
                s.z / Mathf.Max(0.0001f, Mathf.Abs(p.z)));
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            return fx;
        }

        void UpdateBodyFx()
        {
            var lib = FxLibrary.I;
            if (lib == null) return;

            ResolveSockets();

            // ONE VISUAL LANGUAGE ("distinguishable but visually
            // coherent"): every status lives ON a bone socket, in the same
            // small size family, with the same severity rhythm - count first,
            // pace second, a slight growth third. Identity comes from the
            // effect itself: orange licks · blue crystals · red drips ·
            // black wisps · white shine.

            // burning: 1..3 small licks, one per Socket_Burn
            int want = BurnSeverity > 0.55f ? 3 : BurnSeverity > 0.28f ? 2 : BurnSeverity > 0.08f ? 1 : 0;
            for (int i = 0; i < _bodyFlames.Length; i++)
            {
                bool on = i < want && i < _burnS.Count && _burnS[i] != null;
                if (on && _bodyFlames[i] == null && lib.Fire != null)
                {
                    // chunky enough to be funny ("a bit larger for
                    // comedic effects"), still clearly ON the body
                    var fx = Fit(Instantiate(lib.Fire, _burnS[i]),
                        Mathf.Lerp(0.16f, 0.26f, BurnSeverity));
                    fx.name = "BodyFlame";
                    fx.transform.localPosition = Vector3.zero; // the socket IS the spot
                    // FLAMES BURN UP : bones carry
                    // arbitrary axes - the effect starts world-upright and
                    // only sways with the body from there
                    fx.transform.rotation = Quaternion.identity;
                    _bodyFlames[i] = fx;
                }
                else if (!on && _bodyFlames[i] != null)
                {
                    Destroy(_bodyFlames[i]);
                    _bodyFlames[i] = null;
                }
            }

            // darkness: wisps gather AROUND THE EYES (allies must see
            // you're going blind) - one at dusk, two when the dark owns you.
            // GLARE mirrors it in WHITE: light clouds at the eyes, and
            // darkness is the cure (the sliders already do it)
            int wisps = DarknessSeverity > 0.55f ? 2 : DarknessSeverity > 0.18f ? 1 : 0;
            int glares = BloomSeverity > 0.55f ? 2 : BloomSeverity > 0.18f ? 1 : 0;
            EyeFx(_eyeWisps, wisps, lib.Smoke, 0.08f, "EyeDark");
            EyeFx(_eyeGlares, glares, lib.HealShine, 0.09f, "EyeGlare");

            // BLEEDING = the HP readout ("the more you have over your
            // body the less hp you have") — wounds drip on a beat, more and
            // faster as HP falls; healing raises HP and the dripping stops
            float hurt = _pilot != null ? 1f - Mathf.Clamp01(_pilot.Health / Perks.MaxHealth) : 0f;
            if (hurt > 0.25f && _pilot != null && !_pilot.IsDead)
            {
                _bleedTick -= Time.deltaTime;
                if (_bleedTick <= 0f)
                {
                    _bleedTick = Mathf.Lerp(2.2f, 0.6f, hurt);
                    int drips = hurt > 0.75f ? 3 : hurt > 0.5f ? 2 : 1;
                    for (int i = 0; i < drips && _bleedS.Count > 0; i++)
                    {
                        var s = _bleedS[Random.Range(0, _bleedS.Count)];
                        if (s == null) continue;
                        Fit(FxLibrary.Spawn(lib.Blood, s.position, s, 2.5f),
                            Mathf.Lerp(0.12f, 0.2f, hurt)); // small smears - count+pace tell the story
                    }
                }
            }

            // freezing: ice crystals FORM on you on a beat - faster and
            // bigger the deeper you are
            if (FreezeSeverity > 0.08f)
            {
                _iceFxTick -= Time.deltaTime;
                if (_iceFxTick <= 0f)
                {
                    _iceFxTick = Mathf.Lerp(1.4f, 0.45f, FreezeSeverity);
                    if (_freezeS.Count > 0)
                    {
                        var s = _freezeS[Random.Range(0, _freezeS.Count)];
                        if (s != null)
                            Fit(FxLibrary.Spawn(lib.IceHit, s.position, s, 2f),
                                Mathf.Lerp(0.12f, 0.24f, FreezeSeverity)); // crystals ON the skin
                    }
                }
            }
        }

        /// One eye-status loop for wisps AND glares (they were the same loop
        /// twice - only prefab, scale and name differed).
        void EyeFx(GameObject[] cache, int want, GameObject prefab, float scale, string fxName)
        {
            for (int i = 0; i < cache.Length; i++)
            {
                bool on = i < want && _eyeS.Count > 0 && _eyeS[0] != null;
                if (on && cache[i] == null && prefab != null)
                {
                    // two authored eye sockets = one per eye; one = split around it
                    var anchor = _eyeS[Mathf.Min(i, _eyeS.Count - 1)];
                    var fx = Fit(Instantiate(prefab, anchor), scale);
                    fx.name = fxName;
                    fx.transform.position = _eyeS.Count > 1 ? anchor.position
                        : anchor.position + transform.right * (i == 0 ? 0.05f : -0.05f);
                    fx.transform.rotation = Quaternion.identity; // head-bone axes lie
                    cache[i] = fx;
                }
                else if (!on && cache[i] != null)
                {
                    Destroy(cache[i]);
                    cache[i] = null;
                }
            }
        }

        // ---- the screen IS the readout (no bars; darkness genuinely
        // steals vision; frost/heat creep at the edges) - local player only ----
        static Texture2D _white;
        void OnGUI()
        {
            if (_pilot == null || !_pilot.IsLocalViewer) return; // cached - no per-event camera scan
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            float dark = DarknessSeverity;
            // full darkness COVERS the camera outright - at the
            // bottom of the slider you see nothing at all
            if (dark > 0.01f) Tint(full, new Color(0f, 0f, 0.02f, Mathf.Min(1f, dark * 1.06f)));
            // GLARE blinds too : at the top of the slider the screen
            // is solid white - darkness is the cure, symmetrically
            float bloom = BloomSeverity;
            if (bloom > 0.01f) Tint(full, new Color(1f, 1f, 0.94f, Mathf.Min(1f, bloom * 1.06f)));
            float frost = FreezeSeverity;
            if (frost > 0.01f) Tint(full, new Color(0.62f, 0.8f, 1f, frost * 0.34f));
            float burn = BurnSeverity;
            if (burn > 0.01f) Tint(full, new Color(1f, 0.32f, 0.06f, burn * 0.3f));
        }

        static void Tint(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }
    }
}
