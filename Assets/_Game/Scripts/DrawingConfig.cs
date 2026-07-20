using UnityEngine;

namespace SpellyZombie
{
    /// Central tuning constants for the drawing / seal system.
    /// Everything gameplay-feel lives here so balancing is one file.
    public static class DrawingConfig
    {
        // ---- Pen / stroke capture ----
        public const float DrawRange = 8f;           // max raycast distance of the pen
        public const float NodeSpacing = 0.015f;     // min world distance between nodes — dense enough that even finger-sized runes keep their corners
        public const float SurfaceOffset = 0.008f;   // lift ink off the surface to avoid z-fighting
        public const float MaxStrokeJump = 0.12f;    // hit point jumping further than this in one frame ends the stroke — separate marks STAY separate
        public const float MaxStrokeJumpPerMeter = 0.02f; // tiny distance allowance (fast flicks split; forgiving seals reconnect what should connect)
        public const int MinStrokeNodes = 3;         // strokes shorter than this are discarded as accidental clicks (3 = ~6cm; small seal-closers survive)
        public const float InkWidth = 0.007f;        // line renderer width — THIN pen (Marko: more runes in smaller places)
        public const float DrawSmoothingTime = 0.025f; // hand-jitter smoothing time constant, seconds (0 = raw input)
        public const float DrawLookSensitivityScale = 0.35f; // camera sensitivity multiplier while the pen is down
        public const float EraseRadius = 0.02f;      // eraser WIDER than the thin pen (Marko: pen-width erasing was impossible to aim) — still small enough for precise corrections; swept along the cursor path so thin ≠ skippy

        // ---- Seal closure / integrity ----
        // Closure requires the ink to basically touch. Ends only link at
        // near-contact; a visible gap never seals. The reliable, exact way to
        // close is to let the lines actually cross (CrossingFinder).
        // (CloseThreshold stays in HAND units, not ink widths — the pen got
        // thinner but fingers didn't get steadier.)
        public const float CloseThreshold = 0.035f;  // cross-stroke endpoint link distance — EXACTNESS: ink must basically touch
        public const float SelfCloseFraction = 0.05f;// self close: threshold = fraction of the loop's length...
        public const float SelfCloseMin = 0.02f;     // ...clamped to [SelfCloseMin, SelfCloseMax]
        public const float SelfCloseMax = 0.06f;     // exactness: the ends must MEET — small runes/seals stay drawable side by side
        public const float BreakDistance = 0.12f;    // an ACTIVE seal opens when a gap grows this far past its drawn length
        public const int MinLoopNodes = 8;
        public const float MinLoopPerimeter = 0.18f; // palm-sized seals are legal (~6cm triangle)
        public const float MinLoopBulge = 0.06f;     // a loop must enclose something — rejects paper-thin slivers
        public const float GlyphCellMax = 0.13f;      // a self-crossing CELL smaller than this is a rune's inner point (star), not a seal — bigger loops always close
        public const float MaxLoopGapFraction = 0.15f; // total link-gaps in a chained loop ≤ this share of its perimeter — kills "sealed without touching"
        public const int MaxLoopStrokes = 6;         // DFS depth cap when chaining strokes into one seal

        // ---- Seal shape -> duration ----
        public const float DurationPerEdge = 0.1f;   // triangle = 0.3s
        public const int CircleEdges = 360;          // perfect circle counts as 360 edges = 36s
        public const float CircleMaxVariance = 0.16f;// radius variation below this = circle — HAND circles are 10-18% wobbly, 7% only accepted machines
        public const int CircleMinCorners = 8;       // and it must not be an obvious low-corner polygon
        public const float RdpEpsilonFactor = 0.015f;// RDP epsilon as fraction of the loop's bounding diagonal
        public const float MinCornerAngle = 20f;     // degrees of direction change required to count as an edge corner

        // ---- Detection / recognition ----
        public const float DetectInterval = 0.12f;   // how often the seal detector rescans stroke endpoints
        // Recognition law (Marko): the RIGHT rune fires or NONE fires. The
        // stretch-fill oriented-chamfer matcher (InkChamfer) is the only
        // authority; these two knobs are its whole tuning surface. Tuned on
        // his recordings under GAME-REALISTIC distortion (jitter + oblique-
        // view foreshortening + shear, 200 draws/rune + 500 scribbles):
        // at .42/.03 → 99.4% right-rune, 0.0% wrong rune, 1.4% gibberish.
        // Raise the floor if wrong runes ever slip through; lower it if
        // honest drawings fizzle too often.
        public const float RuneChamferFloor = 0.42f;  // similarity the best rune must reach to fire at all (chamfer path, currently benched)
        public const float RuneChamferMargin = 0.03f; // best must beat runner-up by this — a close second = coin flip → fizzle
        public const float MinRuneScore = 0.42f;      // $P ensemble floor — below this the shape is unreadable → fizzle
        public const float RuneAmbiguityMargin = 0.05f; // two DIFFERENT runes within this of each other = coin flip → fizzle, never misfire
        public const float RuneTouchDistance = 0.05f; // strokes this close read as ONE drawing — matches the endpoint stitcher, so "looks connected" = "is connected" (0.03 left visually-touching arrow barbs orphaned)
        public const float GoodRuneScore = 0.75f;     // at/above this the match counts as full strength
        public const float MinSizePower = 0.30f;      // a tiny rune in a big seal still does this fraction of its effect

        // Legacy glyph-join knobs still referenced by Seal.Cast's Segment call;
        // actual grouping is RuneTouchDistance union-find everywhere now.
        public const float GlyphJoinBase = 0.10f;
        public const float GlyphJoinSizeFactor = 0.55f;

        // ---- Spell effects (rune zones) ----
        public const float ZoneRadiusScale = 1.6f;    // rune zone radius = drawn rune size × this
        public const float HeatPerSecond = 150f;       // °C/s of heat a full-strength Heat rune delivers
        public const float BurnThreshold = 70f;        // above this °C an object takes burn damage
        // freeze point MIRRORS the burn point around ambient 18° (Marko: "it's
        // just a game") — freezing something costs the same number of frost
        // hits as igniting it costs sparks; particles stay symmetric ±25
        public const float FreezeThreshold = -34f;
        public const float BurnDamagePerSec = 18f;
        public const float FreezeDamagePerSec = 10f;
        public const float AmbientDriftPerSec = 6f;    // °C/s an object relaxes back toward ambient
        public const float ForceAccel = 25f;           // m/s² a full-strength Density rune applies — beats gravity with room to spare
        public const float DirectionForce = 40f;        // m/s² a Direction rune applies along its arrow — the main mover, and the flight engine
        // (matter spawning is one block per State zone per activation — size and
        // behaviour live in SurfaceMaterialDB / Matter, not here)
        public const float MaxThermalObjectSize = 3f;  // don't cook colliders bigger than this unless they're dynamic (walls/ground)

        // ---- Physics damage (Marko: non-damage runes kill through velocity —
        // push a zombie off a roof, drop with a dead FLOAT, slam with a gust) ----
        public const float SafeFallSpeed = 10f;       // ≈ a 3.5m drop at gravity 14 — free
        public const float FallDamagePerSpeed = 5f;   // hp per m/s past safe (10m fall ≈ 33 dmg)
        public const float ImpactDamageSpeed = 8f;    // collisions slower than this are harmless (walking, brushing)
        public const float ImpactDamagePerSpeed = 4f; // hp per m/s past that

        // ---- air tumble (Marko: airborne too long = ragdoll comedy). No jump
        // lasts 2s — only spells, shoves and cliffs keep you up that long, so
        // this NEVER interrupts normal jumping; it fires exactly when you got
        // SENT. Landing gets a short flop beat, then you're a wizard again.
        public const float AirTumbleSeconds = 2f;
        public const float AirTumbleRecover = 0.45f;  // faster than a combat sprawl — it's a joke, not a punishment

        // ---- Round game (ink economy / survival loop) ----
        public const float InkMax = 100f;
        public const float InkCostPerMeter = 6f;    // ~16m of line per full tank (12/m drained a tank per seal — "unplayable", ship test)
        public const float InkPerKill = 10f;        // shared to ALL players per zombie down — the fast lane
        public const float InkRegenPerSec = 3.5f;   // slow passive refill during waves — never truly stuck
        public const float CauldronInkPerSec = 22f; // standing at any cauldron refills fast — the ship's "keep coming back" anchor
        public const float HolyLightPerSec = 11f;   // light zones sear undead flesh — light is a weapon again (Marko's veto)
        public const float MidDrawCloseStartRegion = 0.12f; // mid-draw closure only onto the stroke's first 12cm (the circle gesture) — deeper self-crossings are glyphs, not lassos
        public const float SolidDropHeight = 3f;    // solid conjures materialize OVERHEAD and drop — gravity is the damage
        public const float InkTricklePerSec = 4f;   // extra intermission refill
        public const float IntermissionSeconds = 20f;
        public const int MaxRounds = 10;            // demo cap — survive this and you "win"
        public const float BleedOutSeconds = 20f;
        public const float ReviveSeconds = 3f;
        public const float ReviveRange = 2.5f;

        // ---- Sticky (real strength) ----
        public const float StickyGripDamping = 16f;    // linear damping a Sticky-up zone slams on
        public const float StickyPressForce = 12f;     // force pressing objects onto the surface (clinging)
        public const float SlickGravityBoost = 0.4f;   // Sticky-down adds this × gravity — things SLIP faster

        // ---- Luminance ----
        // plain light no longer damages — light's damage is EARNED through the
        // plasma ladder (light + density → lightning → laser)
        public const float BlindSeconds = 1.5f;        // darkness re-applies this while a creature stays inside

        // ---- GRAMMAR v4 (SPELL_PARTICLES.md — leveling, paradox, lineage) ----
        public const float Lvl2AuraRadius = 1.4f;  // a lvl2 particle radiates its effect this far
        public const float Lvl2AuraPeriod = 0.8f;  // seconds between aura beats
        public const float UltimateRadius = 3.5f;  // lvl3 area effects (flame burst, snow field…)
        public const float UltimateSeconds = 5f;   // lifetime of lvl3 fields (plasma, inertia…)
        public const float BarrierSeconds = 5f;    // two-way isolation duration (Marko's ruling)
        public const float DemonCooldown = 90f;    // all-12 chains can't machine-gun apocalypses

        // ---- living particles (Marko: "just want particles to feel alive") ----
        public const float ParticleKinRange = 1.3f;   // affinity range toward OTHER particles (first love)
        public const float ParticleChaseRange = 3.5f; // and toward nearby prey when no kin is around
        public const float ParticleChaseAccel = 1.7f; // gentler than kin-pull — a stalk, not a homing missile

        // ---- Spell particles (SPELL_PARTICLES.md — the matter-level law) ----
        public const float SparkHeatDelta = 25f;   // °C one spark/frost carries; 3 hits on one target = 3× (Marko's rule)
        public const float ZoneEmitPeriod = 3.5f;  // seconds between a zone's emissions (ONE particle per rune — law 10)
                                                   // (State conjures ONCE per activation instead)
        public const int ParticleCap = 120;        // world particle budget — oldest dies first
        public const float ParticleLife = 4.5f;    // seconds a particle lives (shadows get double)

        // ---- Pressure & explosion (density confined by rigid walls) ----
        public const float PressureBuildRate = 0.55f;  // pressure/sec per unit gas intensity when fully contained
        public const float ExplodeThreshold = 1f;      // contained pressure that triggers the burst
        public const float ContainRange = 1.7f;        // a rigid surface within this counts as containing a side
        public const float HeatPressureFactor = 0.6f;  // Heat adds to the gas that pressurizes with Density
        public const float ExplodeImpulse = 11f;       // burst impulse on nearby bodies
        public const float ExplodeRadius = 3.8f;
        public const int ExplodeParticles = 48;

        // ---- Persistent ink (characters & weapons) ----
        public const float ReArmDistance = 0.10f;    // a spent loop must open this far before it can fire again
        public const float ReCloseDistance = 0.05f;  // ...and a re-armed BODY/weapon seal re-fires when its junctions come back within THIS — a forgiving close hand-posing can hit (Schmitt trigger: open >10cm, re-close <5cm). The tight 3.5cm CloseThreshold stays for FRESH world ink only; this is why posing your body in third person casts the seal you drew (it can't reproduce a saved pose to the millimetre)
        public const float SealLimbReach = 0.12f;    // a SPENT body seal re-casts when another limb crosses INTO its enclosed loop (Marko: "get my hands near it") — this is how far off the loop's surface a limb may be and still count as inside
        public const int MaxEnvironmentStrokes = 300;// oldest unsealed world ink fades beyond this (perf cap)

        /// Self-closure distance scales with the loop's own size: a 20cm rune
        /// keeps its gap open, while a 2m loop closes when the ink visually
        /// touches (3.5cm at most — no more chunky floating gaps).
        public static float SelfCloseThreshold(float loopLength)
        {
            return Mathf.Clamp(loopLength * SelfCloseFraction, SelfCloseMin, SelfCloseMax);
        }
    }
}
