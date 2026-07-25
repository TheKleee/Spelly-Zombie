using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpellyZombie
{
    /// Central tuning constants for the drawing / seal system.
    /// Everything gameplay-feel lives here so balancing is one file.
    ///
    /// MODDING / BALANCE HOOK (Jul 25): every value below is a DEFAULT that can
    /// be overridden at runtime by a JSON file at
    ///   {Application.persistentDataPath}/sz_tuning.json
    /// with no recompile — ship a balance file, tweak the demo live, or let a
    /// Steam Workshop tuning mod reach these. Format (only list what you change):
    ///   {"entries":[{"key":"InkMax","value":120},{"key":"HeatPerSecond","value":90}]}
    /// Keys are the field names below. Fields are `static readonly` (not `const`)
    /// so the overlay can reach them AND Harmony can patch them.
    public static class DrawingConfig
    {
        // ---- the tuning overlay: loaded ONCE, before any field below (this
        // field is declared first, so its initializer runs first) ----
        [Serializable] class TuneEntry { public string key; public float value; }
        [Serializable] class TuneFile { public TuneEntry[] entries; }
        public const string OverlayFileName = "sz_tuning.json"; // a filename, never tuned → stays const
        static readonly Dictionary<string, float> _overlay = LoadOverlay();

        static Dictionary<string, float> LoadOverlay()
        {
            var d = new Dictionary<string, float>();
            try
            {
                string path = Path.Combine(Application.persistentDataPath, OverlayFileName);
                if (File.Exists(path))
                {
                    var f = JsonUtility.FromJson<TuneFile>(File.ReadAllText(path));
                    if (f != null && f.entries != null)
                        foreach (var e in f.entries)
                            if (!string.IsNullOrEmpty(e.key)) d[e.key] = e.value;
                    Debug.Log($"[SpellyZombie] tuning overlay: {d.Count} override(s) from {path}");
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[SpellyZombie] tuning overlay skipped: {ex.Message}"); }
            return d;
        }
        static float O(string key, float def) => _overlay.TryGetValue(key, out var v) ? v : def;
        static int Oi(string key, int def) => _overlay.TryGetValue(key, out var v) ? Mathf.RoundToInt(v) : def;

        // ---- Pen / stroke capture ----
        public static readonly float DrawRange = O(nameof(DrawRange), 8f);           // max raycast distance of the pen
        public static readonly float NodeSpacing = O(nameof(NodeSpacing), 0.015f);   // min world distance between nodes — dense enough that even finger-sized runes keep their corners
        public static readonly float SurfaceOffset = O(nameof(SurfaceOffset), 0.008f); // lift ink off the surface to avoid z-fighting
        public static readonly float MaxStrokeJump = O(nameof(MaxStrokeJump), 0.12f); // hit point jumping further than this in one frame ends the stroke — separate marks STAY separate
        public static readonly float MaxStrokeJumpPerMeter = O(nameof(MaxStrokeJumpPerMeter), 0.02f); // tiny distance allowance (fast flicks split; forgiving seals reconnect what should connect)
        public static readonly int MinStrokeNodes = Oi(nameof(MinStrokeNodes), 3);   // strokes shorter than this are discarded as accidental clicks (3 = ~6cm; small seal-closers survive)
        public static readonly float InkWidth = O(nameof(InkWidth), 0.007f);         // line renderer width — THIN pen (Marko: more runes in smaller places)
        public static readonly float DrawSmoothingTime = O(nameof(DrawSmoothingTime), 0.025f); // hand-jitter smoothing time constant, seconds (0 = raw input)
        public static readonly float DrawLookSensitivityScale = O(nameof(DrawLookSensitivityScale), 0.35f); // camera sensitivity multiplier while the pen is down
        public static readonly float EraseRadius = O(nameof(EraseRadius), 0.02f);    // eraser WIDER than the thin pen (Marko: pen-width erasing was impossible to aim) — still small enough for precise corrections; swept along the cursor path so thin ≠ skippy

        // ---- Seal closure / integrity ----
        // Closure requires the ink to basically touch. Ends only link at
        // near-contact; a visible gap never seals. The reliable, exact way to
        // close is to let the lines actually cross (CrossingFinder).
        // (CloseThreshold stays in HAND units, not ink widths — the pen got
        // thinner but fingers didn't get steadier.)
        public static readonly float CloseThreshold = O(nameof(CloseThreshold), 0.035f); // cross-stroke endpoint link distance — EXACTNESS: ink must basically touch
        public static readonly float SelfCloseFraction = O(nameof(SelfCloseFraction), 0.05f);// self close: threshold = fraction of the loop's length...
        public static readonly float SelfCloseMin = O(nameof(SelfCloseMin), 0.02f);  // ...clamped to [SelfCloseMin, SelfCloseMax]
        public static readonly float SelfCloseMax = O(nameof(SelfCloseMax), 0.06f);  // exactness: the ends must MEET — small runes/seals stay drawable side by side
        public static readonly float BreakDistance = O(nameof(BreakDistance), 0.12f); // an ACTIVE seal opens when a gap grows this far past its drawn length
        public static readonly int MinLoopNodes = Oi(nameof(MinLoopNodes), 8);
        public static readonly float MinLoopPerimeter = O(nameof(MinLoopPerimeter), 0.18f); // palm-sized seals are legal (~6cm triangle)
        public static readonly float MinLoopBulge = O(nameof(MinLoopBulge), 0.06f);  // a loop must enclose something — rejects paper-thin slivers
        public static readonly float GlyphCellMax = O(nameof(GlyphCellMax), 0.13f);  // a self-crossing CELL smaller than this is a rune's inner point (star), not a seal — bigger loops always close
        public static readonly float MaxLoopGapFraction = O(nameof(MaxLoopGapFraction), 0.15f); // total link-gaps in a chained loop ≤ this share of its perimeter — kills "sealed without touching"
        public static readonly int MaxLoopStrokes = Oi(nameof(MaxLoopStrokes), 6);   // DFS depth cap when chaining strokes into one seal

        // ---- Seal shape -> duration (Marko Jul 22: "1 second per side with a
        // cap of 10 seconds — circle = 10 lines forming a shape") ----
        public static readonly float DurationPerEdge = O(nameof(DurationPerEdge), 1f); // triangle = 3s
        public static readonly int CircleEdges = Oi(nameof(CircleEdges), 10);        // a circle reads as 10 sides = the cap
        public static readonly float SealMaxSeconds = O(nameof(SealMaxSeconds), 10f); // no shape outlasts this
        public static readonly float CircleMaxVariance = O(nameof(CircleMaxVariance), 0.16f);// radius variation below this = circle — HAND circles are 10-18% wobbly, 7% only accepted machines
        public static readonly int CircleMinCorners = Oi(nameof(CircleMinCorners), 8); // and it must not be an obvious low-corner polygon
        public static readonly float RdpEpsilonFactor = O(nameof(RdpEpsilonFactor), 0.015f);// RDP epsilon as fraction of the loop's bounding diagonal
        public static readonly float MinCornerAngle = O(nameof(MinCornerAngle), 20f); // degrees of direction change required to count as an edge corner

        // ---- Detection / recognition ----
        public static readonly float DetectInterval = O(nameof(DetectInterval), 0.12f); // how often the seal detector rescans stroke endpoints
        // Recognition law (Marko): the RIGHT rune fires or NONE fires. The
        // stretch-fill oriented-chamfer matcher (InkChamfer) is the only
        // authority; these two knobs are its whole tuning surface. Tuned on
        // his recordings under GAME-REALISTIC distortion (jitter + oblique-
        // view foreshortening + shear, 200 draws/rune + 500 scribbles):
        // at .42/.03 → 99.4% right-rune, 0.0% wrong rune, 1.4% gibberish.
        // Raise the floor if wrong runes ever slip through; lower it if
        // honest drawings fizzle too often.
        public static readonly float RuneChamferFloor = O(nameof(RuneChamferFloor), 0.42f); // similarity the best rune must reach to fire at all (chamfer path, currently benched)
        public static readonly float RuneChamferMargin = O(nameof(RuneChamferMargin), 0.03f); // best must beat runner-up by this — a close second = coin flip → fizzle
        public static readonly float MinRuneScore = O(nameof(MinRuneScore), 0.42f);  // $P ensemble floor — below this the shape is unreadable → fizzle
        public static readonly float RuneAmbiguityMargin = O(nameof(RuneAmbiguityMargin), 0.05f); // two DIFFERENT runes within this of each other = coin flip → fizzle, never misfire
        public static readonly float RuneTrustScore = O(nameof(RuneTrustScore), 0.55f); // a top match AT/ABOVE this is TRUSTED outright — the ambiguity guard only referees weak scribbles (big wall pools raised every runner-up; honest CHILL/COMPRESS draws kept fizzling on 0.03 gaps — Marko's Jul 22 bug)
        public static readonly float RuneTouchDistance = O(nameof(RuneTouchDistance), 0.05f); // strokes this close read as ONE drawing — matches the endpoint stitcher, so "looks connected" = "is connected" (0.03 left visually-touching arrow barbs orphaned)
        public static readonly float GoodRuneScore = O(nameof(GoodRuneScore), 0.75f); // at/above this the match counts as full strength
        public static readonly float MinSizePower = O(nameof(MinSizePower), 0.30f);  // a tiny rune in a big seal still does this fraction of its effect

        // Legacy glyph-join knobs still referenced by Seal.Cast's Segment call;
        // actual grouping is RuneTouchDistance union-find everywhere now.
        public static readonly float GlyphJoinBase = O(nameof(GlyphJoinBase), 0.10f);
        public static readonly float GlyphJoinSizeFactor = O(nameof(GlyphJoinSizeFactor), 0.55f);

        // ---- Spell effects (rune zones) ----
        public static readonly float ZoneRadiusScale = O(nameof(ZoneRadiusScale), 1.6f); // rune zone radius = drawn rune size × this
        public static readonly float HeatPerSecond = O(nameof(HeatPerSecond), 150f);   // °C/s of heat a full-strength Heat rune delivers
        public static readonly float BurnThreshold = O(nameof(BurnThreshold), 70f);    // above this °C an object takes burn damage
        // freeze point MIRRORS the burn point around ambient 18° (Marko: "it's
        // just a game") — freezing something costs the same number of frost
        // hits as igniting it costs sparks; particles stay symmetric ±25
        public static readonly float FreezeThreshold = O(nameof(FreezeThreshold), -34f);
        public static readonly float BurnDamagePerSec = O(nameof(BurnDamagePerSec), 18f);
        public static readonly float FreezeDamagePerSec = O(nameof(FreezeDamagePerSec), 10f);
        public static readonly float AmbientDriftPerSec = O(nameof(AmbientDriftPerSec), 6f); // °C/s an object relaxes back toward ambient
        public static readonly float ForceAccel = O(nameof(ForceAccel), 25f);         // m/s² a full-strength Density rune applies — beats gravity with room to spare
        public static readonly float DirectionForce = O(nameof(DirectionForce), 40f); // m/s² a Direction rune applies along its arrow — the main mover, and the flight engine
        // (matter spawning is one block per State zone per activation — size and
        // behaviour live in SurfaceMaterialDB / Matter, not here)
        public static readonly float MaxThermalObjectSize = O(nameof(MaxThermalObjectSize), 3f); // don't cook colliders bigger than this unless they're dynamic (walls/ground)

        // ---- Physics damage (Marko: non-damage runes kill through velocity —
        // push a zombie off a roof, drop with a dead FLOAT, slam with a gust) ----
        public static readonly float SafeFallSpeed = O(nameof(SafeFallSpeed), 10f);   // ≈ a 3.5m drop at gravity 14 — free
        public static readonly float FallDamagePerSpeed = O(nameof(FallDamagePerSpeed), 5f); // hp per m/s past safe (10m fall ≈ 33 dmg)
        public static readonly float ImpactDamageSpeed = O(nameof(ImpactDamageSpeed), 8f); // collisions slower than this are harmless (walking, brushing)
        public static readonly float ImpactDamagePerSpeed = O(nameof(ImpactDamagePerSpeed), 4f); // hp per m/s past that

        // ---- air tumble (Marko: airborne too long = ragdoll comedy). No jump
        // lasts 2s — only spells, shoves and cliffs keep you up that long, so
        // this NEVER interrupts normal jumping; it fires exactly when you got
        // SENT. Landing gets a short flop beat, then you're a wizard again.
        public static readonly float AirTumbleSeconds = O(nameof(AirTumbleSeconds), 2f);
        public static readonly float AirTumbleRecover = O(nameof(AirTumbleRecover), 0.45f); // faster than a combat sprawl — it's a joke, not a punishment

        // ---- Round game (ink economy / survival loop) ----
        public static readonly float InkMax = O(nameof(InkMax), 100f);
        public static readonly float InkCostPerMeter = O(nameof(InkCostPerMeter), 6f); // ~16m of line per full tank (12/m drained a tank per seal — "unplayable", ship test)
        public static readonly float InkPerKill = O(nameof(InkPerKill), 10f);        // shared to ALL players per zombie down — the fast lane
        public static readonly float InkRegenPerSec = O(nameof(InkRegenPerSec), 3.5f); // slow passive refill during waves — never truly stuck
        public static readonly float CauldronInkPerSec = O(nameof(CauldronInkPerSec), 22f); // standing at any cauldron refills fast — the ship's "keep coming back" anchor
        public static readonly float HolyLightPerSec = O(nameof(HolyLightPerSec), 11f); // light zones sear undead flesh — light is a weapon again (Marko's veto)
        public static readonly float MidDrawCloseStartRegion = O(nameof(MidDrawCloseStartRegion), 0.12f); // mid-draw closure only onto the stroke's first 12cm (the circle gesture) — deeper self-crossings are glyphs, not lassos
        public static readonly float SolidDropHeight = O(nameof(SolidDropHeight), 3f); // solid conjures materialize OVERHEAD and drop — gravity is the damage
        public static readonly float InkTricklePerSec = O(nameof(InkTricklePerSec), 4f); // extra intermission refill
        public static readonly float IntermissionSeconds = O(nameof(IntermissionSeconds), 20f);
        public static readonly int MaxRounds = Oi(nameof(MaxRounds), 10);            // demo cap — survive this and you "win"
        public static readonly float BleedOutSeconds = O(nameof(BleedOutSeconds), 20f);
        public static readonly float ReviveSeconds = O(nameof(ReviveSeconds), 3f);
        public static readonly float ReviveRange = O(nameof(ReviveRange), 2.5f);

        // ---- Sticky (real strength) ----
        public static readonly float StickyGripDamping = O(nameof(StickyGripDamping), 16f); // linear damping a Sticky-up zone slams on
        public static readonly float StickyPressForce = O(nameof(StickyPressForce), 12f);   // force pressing objects onto the surface (clinging)
        public static readonly float SlickGravityBoost = O(nameof(SlickGravityBoost), 0.4f); // Sticky-down adds this × gravity — things SLIP faster

        // ---- Luminance ----
        // plain light no longer damages — light's damage is EARNED through the
        // plasma ladder (light + density → lightning → laser)
        public static readonly float BlindSeconds = O(nameof(BlindSeconds), 1.5f);   // darkness re-applies this while a creature stays inside

        // ---- GRAMMAR v4 (SPELL_PARTICLES.md — leveling, paradox, lineage) ----
        public static readonly float Lvl2AuraRadius = O(nameof(Lvl2AuraRadius), 1.15f); // a lvl2 particle radiates this far (Marko: "shouldn't be that large" — and the ground ring now shows it)
        public static readonly float Lvl2AuraPeriod = O(nameof(Lvl2AuraPeriod), 0.8f); // seconds between aura beats
        public static readonly float UltimateRadius = O(nameof(UltimateRadius), 3.5f); // lvl3 area effects (flame burst, snow field…)
        public static readonly float UltimateSeconds = O(nameof(UltimateSeconds), 5f); // lifetime of lvl3 fields (plasma, inertia…)
        public static readonly float BarrierSeconds = O(nameof(BarrierSeconds), 5f);   // two-way isolation duration (Marko's ruling)
        public static readonly float DemonCooldown = O(nameof(DemonCooldown), 90f);    // all-12 chains can't machine-gun apocalypses

        // ---- living particles (Marko: "just want particles to feel alive") ----
        public static readonly float ParticleKinRange = O(nameof(ParticleKinRange), 1.3f); // affinity range toward OTHER particles (first love)
        public static readonly float ParticleChaseRange = O(nameof(ParticleChaseRange), 3.5f); // and toward nearby prey when no kin is around
        public static readonly float ParticleChaseAccel = O(nameof(ParticleChaseAccel), 1.7f); // gentler than kin-pull — a stalk, not a homing missile

        // ---- Spell particles (SPELL_PARTICLES.md — the matter-level law) ----
        public static readonly float SparkHeatDelta = O(nameof(SparkHeatDelta), 25f); // °C one spark/frost carries; 3 hits on one target = 3× (Marko's rule)
        public static readonly float ZoneEmitPeriod = O(nameof(ZoneEmitPeriod), 3.5f); // seconds between a zone's emissions (ONE particle per rune — law 10)
                                                                                       // (State conjures ONCE per activation instead)
        public static readonly int ParticleCap = Oi(nameof(ParticleCap), 120);        // world particle budget — oldest dies first
        public static readonly float ParticleLife = O(nameof(ParticleLife), 4.5f);    // seconds a particle lives (shadows get double)

        // ---- Pressure & explosion (density confined by rigid walls) ----
        public static readonly float PressureBuildRate = O(nameof(PressureBuildRate), 0.55f); // pressure/sec per unit gas intensity when fully contained
        public static readonly float ExplodeThreshold = O(nameof(ExplodeThreshold), 1f);      // contained pressure that triggers the burst
        public static readonly float ContainRange = O(nameof(ContainRange), 1.7f);            // a rigid surface within this counts as containing a side
        public static readonly float HeatPressureFactor = O(nameof(HeatPressureFactor), 0.6f); // Heat adds to the gas that pressurizes with Density
        public static readonly float ExplodeImpulse = O(nameof(ExplodeImpulse), 11f);         // burst impulse on nearby bodies
        public static readonly float ExplodeRadius = O(nameof(ExplodeRadius), 3.8f);
        public static readonly int ExplodeParticles = Oi(nameof(ExplodeParticles), 48);

        // ---- Persistent ink (characters & weapons) ----
        public static readonly float ReArmDistance = O(nameof(ReArmDistance), 0.10f);  // a spent loop must open this far before it can fire again
        public static readonly float ReCloseDistance = O(nameof(ReCloseDistance), 0.05f); // ...and a re-armed BODY/weapon seal re-fires when its junctions come back within THIS — a forgiving close hand-posing can hit (Schmitt trigger: open >10cm, re-close <5cm). The tight 3.5cm CloseThreshold stays for FRESH world ink only; this is why posing your body in third person casts the seal you drew (it can't reproduce a saved pose to the millimetre)
        public static readonly float SealLimbReach = O(nameof(SealLimbReach), 0.12f);  // a SPENT body seal re-casts when another limb crosses INTO its enclosed loop (Marko: "get my hands near it") — this is how far off the loop's surface a limb may be and still count as inside
        public static readonly int MaxEnvironmentStrokes = Oi(nameof(MaxEnvironmentStrokes), 300);// oldest unsealed world ink fades beyond this (perf cap)

        /// Self-closure distance scales with the loop's own size: a 20cm rune
        /// keeps its gap open, while a 2m loop closes when the ink visually
        /// touches (3.5cm at most — no more chunky floating gaps).
        public static float SelfCloseThreshold(float loopLength)
        {
            return Mathf.Clamp(loopLength * SelfCloseFraction, SelfCloseMin, SelfCloseMax);
        }
    }
}
