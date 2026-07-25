using UnityEngine;

namespace SpellyZombie
{
    /// THE BODY IS MATTER (Marko's slider board, Jul 22): sliders live on
    /// every body, spells only PUSH them — damage, slow, inverted controls,
    /// floating, weight gates and vision are all READINGS derived each tick.
    /// The environment drifts every slider home: walking away from the fire
    /// IS the cure.
    ///
    ///   Temp   — THE damage band. Out of band = DPS by offset; deep cold =
    ///            frozen solid (the body stops entirely).
    ///   Lum    — VISION ONLY, never damage: darkness creeps in low, the
    ///            world blooms high.
    ///   Grip   — too grippy = slowed → planted; too slick = skating → the
    ///            floor wins (ragdoll).
    ///   Weight — light = higher jumps, slower falls, then FLOAT; heavy =
    ///            movement gates (no sprint, then crouch-crawl only).
    ///   Move   — dead-center normal; + = arrow speed buff; − = Y-owned:
    ///            inputs INVERTED at the Y's amplitude.
    ///
    /// On PLAYERS the whole board runs. On CREATURES only Grip/Weight/Move
    /// run — their temperature already lives on Thermal (Frozen/Burning),
    /// and two thermometers would fight.
    public class BodyState : MonoBehaviour
    {
        // ---- naturals, bands, thresholds — the survival game tunes HERE ----
        public const float NaturalTemp = 37f;
        public const float TempBandLow = 15f, TempBandHigh = 45f;
        public const float FrozenSolidAt = -25f;
        public const float TempDriftPerSec = 2.4f;
        public const float TempDamagePerDegree = 0.10f; // DPS per degree outside the band

        public const float NaturalLum = 0.55f;          // daylight — torches sit INSIDE the band
        public const float LumDriftPerSec = 0.4f;

        public const float GripDriftPerSec = 0.45f;
        public const float GripSlowAt = 0.45f;
        public const float GripStuckAt = 1.15f;
        public const float SlickSlideAt = -0.45f;
        public const float SlickRagdollAt = -0.95f;

        public const float WeightDriftPerSec = 0.30f;
        public const float FloatBelow = 0.35f;          // REALLY light: you float
        public const float RunLimit = 1.55f;            // sprint refuses above
        public const float WalkLimit = 2.4f;            // crouch-crawl only above

        public const float MoveDriftPerSec = 0.45f;

        // ---- the sliders ----
        public float Temp = NaturalTemp;
        public float Lum = NaturalLum;
        public float Grip;          // 0 natural · + sticky · − slick
        public float Weight = 1f;   // mass multiplier
        public float Move;          // 0 center · + arrow buff · − Y inversion

        /// What your arms hold right now (Marko: "their weight is added on
        /// your weight so you might move slower") — the gates are the
        /// strength limit, no artificial carry cap.
        public float CarriedWeight;
        public float TotalWeight => Weight + CarriedWeight;

        SimpleFPSController _pilot;
        Creature _creature;
        float _hurtCarry;   // sub-point band damage lands in readable chunks
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
            if (_creature != null) // creatures: Thermal owns temp — route there
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

        // ---- readings ----
        public bool FrozenSolid => _pilot != null && Temp <= FrozenSolidAt;
        // the screen answers EARLY (Marko: one chill touch must be seen) —
        // creep starts near natural temp; the DAMAGE band is unchanged
        public float BurnSeverity => Mathf.Clamp01((Temp - 41f) / 42f);
        public float FreezeSeverity => Mathf.Clamp01((33f - Temp) / 42f);
        /// 0 = normal sight · 1 = pitch black (this IS the vision reduction)
        public float DarknessSeverity => Mathf.Clamp01((NaturalLum - Lum) / (NaturalLum + 0.55f));
        public float BloomSeverity => Mathf.Clamp01((Lum - 1.4f) / 1.4f);

        public bool Floating => TotalWeight <= FloatBelow;
        public bool CanSprint => TotalWeight < RunLimit;
        public bool CrawlOnly => TotalWeight >= WalkLimit;
        /// Lighter bodies spring higher and fall softer; heavy is gated, not nerfed.
        public float JumpMul => TotalWeight < 1f ? Mathf.Lerp(1.5f, 1f, TotalWeight) : 1f;
        public float GravityMul => TotalWeight < 1f ? Mathf.Max(0.18f, TotalWeight) : 1f;

        /// −1 while a Y owns you: your inputs walk you the other way.
        public float InputSign => Move < -0.05f ? -1f : 1f;

        /// One multiplier from the whole board — players and zombies both.
        /// NOTHING here ever returns zero (Marko: "the only thing that's so
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
                Temp = Mathf.MoveTowards(Temp, NaturalTemp, TempDriftPerSec * dt);
                Lum = Mathf.MoveTowards(Lum, NaturalLum, LumDriftPerSec * dt);
            }
            Grip = Mathf.MoveTowards(Grip, 0f, GripDriftPerSec * dt);
            Weight = Mathf.MoveTowards(Weight, 1f, WeightDriftPerSec * dt);
            Move = Mathf.MoveTowards(Move, 0f, MoveDriftPerSec * dt);

            // ---- temp band damage (players only — Thermal burns creatures) ----
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

            // ---- slick: the floor wins PROPORTIONALLY (Marko: "if slippery
            // is weak you might barely ragdoll a bit, extremely strong you'll
            // ragdoll way more") — depth drives the odds, the pace, and how
            // long you eat cobblestone
            if (Grip < SlickSlideAt)
            {
                float depth = Mathf.InverseLerp(SlickSlideAt, -1.4f, Grip); // 0 faint … 1 soap hell
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

        // ---- the BODY shows it (Marko: "small flames over your body
        // depending of how much you're burning") — allies read your state at
        // a glance. And the cure is already in the sliders: a chill mote on a
        // burning friend pushes their temp back toward the band.
        readonly GameObject[] _bodyFlames = new GameObject[3];
        readonly GameObject[] _eyeWisps = new GameObject[2];
        readonly GameObject[] _eyeGlares = new GameObject[2];
        float _iceFxTick, _bleedTick;

        void UpdateBodyFx()
        {
            var lib = FxLibrary.I;
            if (lib == null) return;

            // burning: 1..3 real flames climb your body with severity
            int want = BurnSeverity > 0.55f ? 3 : BurnSeverity > 0.28f ? 2 : BurnSeverity > 0.08f ? 1 : 0;
            for (int i = 0; i < _bodyFlames.Length; i++)
            {
                bool on = i < want;
                if (on && _bodyFlames[i] == null && lib.Fire != null)
                {
                    var fx = Instantiate(lib.Fire, transform);
                    fx.name = "BodyFlame";
                    fx.transform.localPosition = new Vector3(
                        i == 1 ? 0.22f : i == 2 ? -0.2f : 0.05f,
                        0.7f + i * 0.35f,
                        i == 2 ? -0.08f : 0.06f);
                    fx.transform.localScale = Vector3.one * 0.45f;
                    _bodyFlames[i] = fx;
                }
                else if (!on && _bodyFlames[i] != null)
                {
                    Destroy(_bodyFlames[i]);
                    _bodyFlames[i] = null;
                }
            }

            // darkness: wisps gather AROUND THE EYES (Marko: allies must see
            // you're going blind) — one at dusk, two when the dark owns you.
            // GLARE mirrors it in WHITE: light clouds at the eyes, and
            // darkness is the cure (the sliders already do it)
            int wisps = DarknessSeverity > 0.55f ? 2 : DarknessSeverity > 0.18f ? 1 : 0;
            int glares = BloomSeverity > 0.55f ? 2 : BloomSeverity > 0.18f ? 1 : 0;
            for (int i = 0; i < _eyeWisps.Length; i++)
            {
                bool on = i < wisps;
                if (on && _eyeWisps[i] == null && lib.Smoke != null)
                {
                    var fx = Instantiate(lib.Smoke, transform);
                    fx.name = "EyeDark";
                    fx.transform.localPosition = new Vector3(i == 0 ? 0.12f : -0.12f, 1.55f, 0.14f);
                    fx.transform.localScale = Vector3.one * 0.22f;
                    _eyeWisps[i] = fx;
                }
                else if (!on && _eyeWisps[i] != null)
                {
                    Destroy(_eyeWisps[i]);
                    _eyeWisps[i] = null;
                }
            }
            for (int i = 0; i < _eyeGlares.Length; i++)
            {
                bool on = i < glares;
                if (on && _eyeGlares[i] == null && lib.HealShine != null)
                {
                    var fx = Instantiate(lib.HealShine, transform);
                    fx.name = "EyeGlare";
                    fx.transform.localPosition = new Vector3(i == 0 ? 0.12f : -0.12f, 1.55f, 0.14f);
                    fx.transform.localScale = Vector3.one * 0.25f;
                    _eyeGlares[i] = fx;
                }
                else if (!on && _eyeGlares[i] != null)
                {
                    Destroy(_eyeGlares[i]);
                    _eyeGlares[i] = null;
                }
            }

            // BLEEDING = the HP readout (Marko: "the more you have over your
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
                    for (int i = 0; i < drips; i++)
                    {
                        var at = transform.position
                            + Vector3.up * (0.7f + i * 0.4f)
                            + new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.15f, 0.15f));
                        var fx = FxLibrary.Spawn(lib.Blood, at, transform, 2.5f);
                        if (fx != null) fx.transform.localScale = Vector3.one * 0.5f;
                    }
                }
            }

            // freezing: ice crystals FORM on you on a beat — faster and
            // bigger the deeper you are
            if (FreezeSeverity > 0.08f)
            {
                _iceFxTick -= Time.deltaTime;
                if (_iceFxTick <= 0f)
                {
                    _iceFxTick = Mathf.Lerp(1.4f, 0.45f, FreezeSeverity);
                    var at = transform.position + Vector3.up * Random.Range(0.5f, 1.7f)
                        + new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
                    var fx = FxLibrary.Spawn(lib.IceHit, at, transform, 2f);
                    if (fx != null)
                        fx.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 0.9f, FreezeSeverity);
                }
            }
        }

        // ---- the screen IS the readout (Marko: no bars; darkness genuinely
        // steals vision; frost/heat creep at the edges) — local player only ----
        static Texture2D _white;
        void OnGUI()
        {
            if (_pilot == null || _pilot.CameraPivot == null) return;
            var cam = _pilot.CameraPivot.GetComponentInChildren<Camera>();
            if (cam == null || !cam.isActiveAndEnabled) return; // not the local viewer
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            float dark = DarknessSeverity;
            // full darkness COVERS the camera outright (Marko) — at the
            // bottom of the slider you see nothing at all
            if (dark > 0.01f) Tint(full, new Color(0f, 0f, 0.02f, Mathf.Min(1f, dark * 1.06f)));
            // GLARE blinds too (Marko): at the top of the slider the screen
            // is solid white — darkness is the cure, symmetrically
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
